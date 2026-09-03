using NLog;
using ACPollerForAPS.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ConversionService
{
    /// <summary>
    /// Worker de pipeline par lots (choix B) : à intervalle planifié, scanne
    /// le dossier d'entrée, route chaque XML vers un canal selon le Buyer,
    /// agrège les fichiers d'un même canal en une sortie (CSV ou XML), écrit
    /// la sortie, puis archive les XML sources.
    ///
    /// Autonome (n'hérite pas de WorkerBase, qui est fichier-par-fichier) mais
    /// reprend ses garanties : pause/stop propres, sous-dossiers datés,
    /// contrôle de complétude des fichiers.
    /// </summary>
    public class PipelineWorker
    {
        private readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly PipelineSettings _s;
        private const string Name = "Pipeline";

        private Thread _thread;
        private readonly ManualResetEventSlim _pauseGate = new ManualResetEventSlim(true);
        private CancellationTokenSource _cts;
        private readonly ProviderRegistry _providers;

        public PipelineWorker(PipelineSettings settings)
            : this(settings, new ProviderRegistry()) { }

        public PipelineWorker(PipelineSettings settings, ProviderRegistry providers)
        {
            _s = settings;
            _providers = providers ?? new ProviderRegistry();
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _pauseGate.Set();

            EnsureFolders();

            _thread = new Thread(() => Run(_cts.Token)) { IsBackground = true, Name = Name };
            _thread.Start();
            Log.Info("{0} started (interval {1}s)",
                Name, _s.Schedule?.ToSeconds() ?? 0);
        }

        public void Pause() { _pauseGate.Reset(); Log.Info("{0} paused", Name); }
        public void Resume() { _pauseGate.Set(); Log.Info("{0} resumed", Name); }

        public void Stop()
        {
            if (_cts == null) return;
            _cts.Cancel();
            _pauseGate.Set();
            _thread?.Join(TimeSpan.FromSeconds(60)); // laisse un merge en cours finir
            Log.Info("{0} stopped", Name);
        }

        private void EnsureFolders()
        {
            if (!string.IsNullOrWhiteSpace(_s.InputFolder)) Directory.CreateDirectory(_s.InputFolder);
            if (_s.ArchiveEnabled && !string.IsNullOrWhiteSpace(_s.ArchiveFolder))
                Directory.CreateDirectory(_s.ArchiveFolder);
            if (!string.IsNullOrWhiteSpace(_s.ErrorFolder)) Directory.CreateDirectory(_s.ErrorFolder);
            foreach (var ch in _s.Channels ?? new List<OutputChannel>())
                if (!string.IsNullOrWhiteSpace(ch.OutputFolder)) Directory.CreateDirectory(ch.OutputFolder);
        }

        private void Run(CancellationToken token)
        {
            Log.Info("{0}: InputFolder = '{1}', existe ? {2}",
                Name, _s.InputFolder, Directory.Exists(_s.InputFolder));

            int intervalSec = Math.Max(1, _s.Schedule?.ToSeconds() ?? 3600);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    _pauseGate.Wait(token);

                    RunOnce(token);

                    // attend l'intervalle avant le prochain passage (réveil si stop)
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(intervalSec));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Error(ex, "{0}: cycle error", Name);
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(intervalSec));
                }
            }
        }

        /// <summary>Un passage de merge : scan, routage, agrégation, écriture, archivage.</summary>
        public void RunOnce(CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(_s.InputFolder) || !Directory.Exists(_s.InputFolder))
            {
                Log.Warn("{0}: dossier d'entrée absent, passage ignoré.", Name);
                return;
            }

            var files = Directory.GetFiles(_s.InputFolder, _s.FileFilter ?? "*.xml")
                                 .OrderBy(f => f).ToList();
            if (files.Count == 0) { Log.Debug("{0}: aucun fichier à traiter.", Name); return; }

            // identifiant de passage + chrono pour le résumé
            var runId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log.Info("{0}: [run {1}] début — {2} fichier(s) présent(s) dans {3}",
                Name, runId, files.Count, _s.InputFolder);

            // compteurs du passage
            int notReady = 0, readErrors = 0, routeErrors = 0, noChannel = 0;

            // NB : BatchSize n'est PLUS appliqué ici globalement. Il découpe
            // les fichiers de CHAQUE canal en lots (voir la génération par canal
            // plus bas) : 30 fichiers d'un canal avec BatchSize=5 => 6 sorties.

            // regroupe les fichiers par canal (routage par Buyer)
            var byChannel = new Dictionary<OutputChannel, List<string>>();
            var routedFiles = new List<string>();

            foreach (var file in files)
            {
                if (token.IsCancellationRequested) return;
                _pauseGate.Wait(token);

                if (!IsFileReady(file)) { notReady++; continue; } // encore en écriture

                string content;
                try { content = File.ReadAllText(file); }
                catch (Exception ex) { readErrors++; Log.Error(ex, "{0}: [run {1}] lecture échouée {2}", Name, runId, file); MoveToError(file); continue; }

                string err;
                var buyer = PipelineEngine.ReadBuyer(content, _s, out err);
                if (err != null) { routeErrors++; Log.Error("{0}: [run {1}] {2} ({3})", Name, runId, err, file); MoveToError(file); continue; }

                var ch = PipelineEngine.SelectChannel(_s, buyer);
                if (ch == null)
                {
                    noChannel++;
                    Log.Warn("{0}: [run {1}] aucun canal pour Buyer '{2}' ({3})", Name, runId, buyer, file);
                    MoveToError(file);
                    continue;
                }

                Log.Debug("{0}: [run {1}] {2} -> Buyer '{3}' -> canal '{4}'",
                    Name, runId, Path.GetFileName(file), buyer, ch.Name);

                if (!byChannel.ContainsKey(ch)) byChannel[ch] = new List<string>();
                byChannel[ch].Add(file);
                routedFiles.Add(file);
            }

            // génère les sorties par canal, découpées en lots de BatchSize
            int channelsOk = 0, channelsFailed = 0, delivered = 0, outputsWritten = 0;
            foreach (var kv in byChannel)
            {
                if (token.IsCancellationRequested) return;
                var ch = kv.Key;
                var chFiles = kv.Value;

                // découpe les fichiers de ce canal en lots (BatchSize DU CANAL ; 0 = un seul lot)
                var batches = SplitIntoBatches(chFiles, ch.BatchSize);
                int totalBatches = batches.Count;
                int batchNo = 0;
                bool channelHadFailure = false;

                foreach (var batch in batches)
                {
                    if (token.IsCancellationRequested) return;
                    batchNo++;
                    try
                    {
                        WriteChannelOutput(ch, batch, runId, batchNo, totalBatches, token);
                        // archive les sources de CE lot seulement si son dépôt a réussi
                        foreach (var f in batch) Archive(f);
                        delivered += batch.Count;
                        outputsWritten++;
                        Log.Info("{0}: [run {1}] canal '{2}' lot {3}/{4} -> {5} fichier(s) déposé(s) et archivé(s).",
                            Name, runId, ch.Name, batchNo, totalBatches, batch.Count);
                    }
                    catch (Exception ex)
                    {
                        channelHadFailure = true;
                        Log.Error(ex, "{0}: [run {1}] canal '{2}' lot {3}/{4} : échec dépôt, {5} source(s) conservée(s) (retry).",
                            Name, runId, ch.Name, batchNo, totalBatches, batch.Count);
                    }
                }

                if (channelHadFailure) channelsFailed++; else channelsOk++;
            }

            sw.Stop();
            // résumé de fin de passage — la ligne à lire d'un coup d'œil
            var summary = string.Format(
                "[run {0}] terminé en {1} ms — {2} fichier(s) source déposé(s) en {3} sortie(s), sur {4} canal(aux) OK / {5} en échec | ignorés(non prêts)={6} | erreurs lecture={7} routage={8} sans-canal={9}",
                runId, sw.ElapsedMilliseconds, delivered, outputsWritten,
                channelsOk, channelsFailed, notReady, readErrors, routeErrors, noChannel);
            Log.Info("{0}: {1}", Name, summary);

            // événement de supervision Windows : gravité selon les incidents du passage
            int problems = channelsFailed + readErrors + routeErrors + noChannel;
            if (problems > 0)
                EventLogWriter.Warn(
                    "Passage terminé avec des anomalies. " + summary,
                    EventLogWriter.EvtRunErrors);
            else
                EventLogWriter.Info(
                    "Passage OK. " + summary,
                    EventLogWriter.EvtRunSummary);
        }

        /// <summary>Découpe une liste en lots de taille max batchSize (0 = un seul lot).</summary>
        private static List<List<string>> SplitIntoBatches(List<string> files, int batchSize)
        {
            var result = new List<List<string>>();
            if (batchSize <= 0)
            {
                result.Add(new List<string>(files));
                return result;
            }
            for (int i = 0; i < files.Count; i += batchSize)
                result.Add(files.GetRange(i, Math.Min(batchSize, files.Count - i)));
            return result;
        }

        private void WriteChannelOutput(OutputChannel ch, List<string> files,
            string runId, int batchNo, int totalBatches, CancellationToken token)
        {
            // résout le provider du canal ("mapping" par défaut, ou une DLL plugin)
            var exporter = _providers.Resolve(ch.Provider);

            var inputXmls = files.Select(File.ReadAllText).ToList();
            var result = exporter.Export(inputXmls, ch);

            foreach (var w in result.Warnings) Log.Warn("{0}: {1}", Name, w);

            // nom de fichier de sortie, avec le numéro de lot pour éviter l'écrasement
            var fileName = BuildFileName(ch, batchNo, totalBatches);

            // dépôt via le transport du canal (FS / FTPS / S3), avec retry.
            TransportRunner.DeliverWithRetry(ch, fileName, result.Content, token);
            Log.Info("{0}: [run {1}] déposé '{2}' via {3} (provider '{4}')", Name, runId, fileName,
                (ch.Transport?.Type ?? "Fs"), ch.Provider ?? "mapping");
        }

        // ---- nom du fichier de sortie (jetons) ----
        // Jetons : {date} {time} {guid} {batch}. Si plusieurs lots et que le
        // motif ne contient pas {batch}, on ajoute un suffixe _N automatiquement
        // pour éviter que les lots s'écrasent.
        private string BuildFileName(OutputChannel ch, int batchNo, int totalBatches)
        {
            var pattern = string.IsNullOrWhiteSpace(ch.OutputFileName)
                ? "{date}." + ((ch.OutputFormat ?? "Csv").ToLowerInvariant() == "xml" ? "xml" : "csv")
                : ch.OutputFileName;

            bool hasBatchToken = pattern.IndexOf("{batch}", StringComparison.OrdinalIgnoreCase) >= 0;

            var name = pattern
                .Replace("{date}", DateTime.Now.ToString("yyyyMMdd"))
                .Replace("{time}", DateTime.Now.ToString("HHmmss"))
                .Replace("{guid}", Guid.NewGuid().ToString("N"))
                .Replace("{batch}", batchNo.ToString());

            // filet anti-écrasement : plusieurs lots sans jeton {batch}
            if (!hasBatchToken && totalBatches > 1)
            {
                var ext = Path.GetExtension(name);
                var stem = Path.GetFileNameWithoutExtension(name);
                name = stem + "_" + batchNo + ext;
            }
            return name;
        }

        // ---- archivage / erreurs (sous-dossiers datés) ----
        private void Archive(string file)
        {
            if (!_s.ArchiveEnabled || string.IsNullOrWhiteSpace(_s.ArchiveFolder))
            {
                SafeDelete(file);
                return;
            }
            try
            {
                var folder = DatedFolder(_s.ArchiveFolder);
                var dest = UniqueName(folder, Path.GetFileName(file));
                File.Move(file, dest);
            }
            catch (Exception ex) { Log.Error(ex, "{0}: archivage échoué {1}", Name, file); }
        }

        private void MoveToError(string file)
        {
            if (string.IsNullOrWhiteSpace(_s.ErrorFolder))
            {
                Log.Warn("{0}: pas d'ErrorFolder, {1} laissé en place", Name, file);
                return;
            }
            try
            {
                var folder = DatedFolder(_s.ErrorFolder);
                var dest = UniqueName(folder, Path.GetFileName(file));
                File.Move(file, dest);
                Log.Info("{0}: fichier en erreur -> {1}", Name, dest);
            }
            catch (Exception ex) { Log.Error(ex, "{0}: déplacement erreur échoué {1}", Name, file); }
        }

        private static string DatedFolder(string root)
        {
            var dated = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dated);
            return dated;
        }

        private void SafeDelete(string file)
        {
            try { File.Delete(file); }
            catch (Exception ex) { Log.Warn(ex, "{0}: suppression échouée {1}", Name, file); }
        }

        private static string UniqueName(string folder, string fileName)
        {
            var dest = Path.Combine(folder, fileName);
            if (!File.Exists(dest)) return dest;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            return Path.Combine(folder, stem + "_" + stamp + ext);
        }

        private bool IsFileReady(string path)
        {
            try
            {
                long len1 = new FileInfo(path).Length;
                Thread.Sleep(_s.StableCheckMs);
                long len2 = new FileInfo(path).Length;
                if (len1 != len2) return false;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    return true;
            }
            catch (IOException) { return false; }
            catch (Exception ex) { Log.Warn(ex, "{0}: readiness check {1}", Name, path); return false; }
        }
    }
}
