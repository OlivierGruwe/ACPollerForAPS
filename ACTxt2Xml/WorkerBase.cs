using NLog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace ConversionService
{
    public abstract class WorkerBase
    {
        protected readonly Logger Log = LogManager.GetCurrentClassLogger();
        protected readonly WorkerSettings Settings;
        private readonly string _name;

        private Thread _thread;
        private FileSystemWatcher _watcher;
        private readonly ManualResetEventSlim _pauseGate = new ManualResetEventSlim(true);
        private CancellationTokenSource _cts;

        // signal de réveil : déclenché par le watcher ou le polling de secours
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);

        // fichiers détectés en attente de traitement (dédupliqués)
        private readonly ConcurrentDictionary<string, byte> _pending =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        protected WorkerBase(string name, WorkerSettings settings)
        {
            _name = name;
            Settings = settings;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _pauseGate.Set();

            Directory.CreateDirectory(Settings.SourceFolder);
            Directory.CreateDirectory(Settings.TargetFolder);

            SetupWatcher();

            _thread = new Thread(() => Run(_cts.Token))
            {
                IsBackground = true,
                Name = _name
            };
            _thread.Start();
            Log.Info("{0} started", _name);
        }

        public void Pause()
        {
            _pauseGate.Reset();
            Log.Info("{0} paused", _name);
        }

        public void Resume()
        {
            _pauseGate.Set();
            _signal.Set(); // relance un cycle au redémarrage
            Log.Info("{0} resumed", _name);
        }

        public void Stop()
        {
            if (_cts == null) return;

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            _cts.Cancel();
            _pauseGate.Set();
            _signal.Set(); // débloque l'attente
            _thread?.Join(TimeSpan.FromSeconds(30));
            Log.Info("{0} stopped", _name);
        }

        private void SetupWatcher()
        {
            _watcher = new FileSystemWatcher(Settings.SourceFolder, Settings.FileFilter)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += (s, e) => Enqueue(e.FullPath);
            _watcher.Error += (s, e) =>
                Log.Error(e.GetException(), "{0}: watcher error (buffer overflow?)", _name);
            _watcher.EnableRaisingEvents = true;
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e) => Enqueue(e.FullPath);

        private void Enqueue(string path)
        {
            _pending.TryAdd(path, 0);
            _signal.Set();
        }

        private void Run(CancellationToken token)
        {
            // balayage initial : récupère les fichiers déjà présents avant le démarrage
            foreach (var file in Directory.GetFiles(Settings.SourceFolder, Settings.FileFilter))
                _pending.TryAdd(file, 0);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    _pauseGate.Wait(token);

                    // attend un signal (watcher/resume) ou expire pour le polling de secours
                    _signal.WaitOne(TimeSpan.FromSeconds(Settings.PollingIntervalSeconds));

                    if (token.IsCancellationRequested) break;
                    _pauseGate.Wait(token);

                    // filet de sécurité : ré-aligne avec le disque (events manqués / overflow)
                    foreach (var file in Directory.GetFiles(Settings.SourceFolder, Settings.FileFilter))
                        _pending.TryAdd(file, 0);

                    foreach (var file in _pending.Keys)
                    {
                        if (token.IsCancellationRequested) break;
                        _pauseGate.Wait(token);

                        if (!File.Exists(file))
                        {
                            _pending.TryRemove(file, out _);
                            continue;
                        }

                        // ne traite que les fichiers dont l'écriture est terminée
                        if (!IsFileReady(file))
                            continue; // re-tenté au prochain cycle

                        try
                        {
                            ProcessFile(file);
                            _pending.TryRemove(file, out _);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "{0}: error processing {1}", _name, file);
                            _pending.TryRemove(file, out _); // évite la boucle infinie sur fichier corrompu
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "{0}: loop error", _name);
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(Settings.PollingIntervalSeconds));
                }
            }
        }

        /// <summary>
        /// Vérifie qu'un fichier n'est plus en cours d'écriture :
        /// taille stable entre deux mesures + ouverture exclusive réussie.
        /// </summary>
        private bool IsFileReady(string path)
        {
            try
            {
                long len1 = new FileInfo(path).Length;
                Thread.Sleep(Settings.StableCheckMs);
                long len2 = new FileInfo(path).Length;

                if (len1 != len2)
                    return false; // taille encore en mouvement

                // tente un lock exclusif : échoue si un autre process écrit encore
                using (var fs = new FileStream(path, FileMode.Open,
                           FileAccess.Read, FileShare.None))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                return false; // verrouillé / en écriture
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "{0}: readiness check failed for {1}", _name, path);
                return false;
            }
        }

        protected abstract void ProcessFile(string sourcePath);
    }
}
