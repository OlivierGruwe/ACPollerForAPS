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
        private readonly PendingStore _pending;

        public PipelineWorker(PipelineSettings settings)
            : this(settings, new ProviderRegistry()) { }

        public PipelineWorker(PipelineSettings settings, ProviderRegistry providers)
        {
            _s = settings;
            _providers = providers ?? new ProviderRegistry();
            // dossier d'attente de transfert : sous ArchiveFolder/pending, ou à
            // défaut à côté du dossier d'entrée.
            var baseDir = !string.IsNullOrWhiteSpace(_s.ArchiveFolder)
                ? _s.ArchiveFolder
                : (_s.InputFolder ?? ".");
            _pending = new PendingStore(System.IO.Path.Combine(baseDir, "pending"));

            // historique des passages : dossier stats/ PARTAGÉ, à côté de l'exe du
            // service, pour que TOUS les pipelines y écrivent et que l'UI le lise.
            var exeDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            _stats = new StatsStore(System.IO.Path.Combine(exeDir, "stats"));
        }

        private readonly StatsStore _stats;

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
            if (_s.Output != null && !string.IsNullOrWhiteSpace(_s.Output.OutputFolder))
                Directory.CreateDirectory(_s.Output.OutputFolder);
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
            // 1) d'abord : retenter les fichiers en attente de transfert (livraisons
            //    précédemment échouées), avant tout nouveau traitement.
            RetryPending(token);

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
            int notReady = 0, readErrors = 0;

            // sortie unique du pipeline (plus de routage par Buyer)
            var ch = _s.Output;
            if (ch == null) { Log.Error("{0}: pipeline sans sortie (Output null).", Name); return; }

            // on ne garde que les fichiers PRÊTS et LISIBLES (XML bien formé).
            // Un fichier illisible/malformé est isolé vers le dossier Error tout
            // de suite (il ne rebouclera pas à l'infini), et les fichiers sains
            // du même passage sont traités normalement.
            var ready = new List<string>();
            foreach (var file in files)
            {
                if (token.IsCancellationRequested) return;
                _pauseGate.Wait(token);
                if (!IsFileReady(file)) { notReady++; continue; } // encore en écriture

                // validation individuelle : le fichier est-il lisible et bien formé ?
                try
                {
                    var content = File.ReadAllText(file);
                    var probe = new System.Xml.XmlDocument();
                    probe.LoadXml(content); // lève si XML malformé
                    ready.Add(file);
                }
                catch (Exception ex)
                {
                    // fichier corrompu : on l'isole vers Error et on l'exclut du lot
                    readErrors++;
                    Log.Error(ex, "{0}: [run {1}] fichier illisible/malformé, déplacé vers Error : {2}",
                        Name, runId, file);
                    MoveToError(file);
                }
            }

            // découpe en lots (BatchSize du canal ; 0 = un seul lot)
            int delivered = 0, outputsWritten = 0, pendedOutputs = 0;
            bool hadFailure = false;

            var batches = SplitIntoBatches(ready, ch.BatchSize);
            int totalBatches = batches.Count;
            int batchNo = 0;

            foreach (var batch in batches)
            {
                if (token.IsCancellationRequested) return;
                batchNo++;
                try
                {
                    bool deliveredNow = WriteChannelOutput(ch, batch, runId, batchNo, totalBatches, token);
                    // le contenu est sécurisé (déposé OU mis en attente) : on archive les sources
                    foreach (var f in batch) Archive(f);
                    if (deliveredNow) { delivered += batch.Count; outputsWritten++; }
                    else { pendedOutputs++; hadFailure = true; }
                }
                catch (Exception ex)
                {
                    // Les fichiers ont été pré-validés (XML bien formé), donc une
                    // erreur ici vient de la GÉNÉRATION/mapping ou de la mise en
                    // attente, pas d'un fichier corrompu. On conserve les sources
                    // (pas d'archivage) : à corriger côté config, retenté au prochain passage.
                    hadFailure = true;
                    Log.Error(ex, "{0}: [run {1}] lot {2}/{3} : génération/mise en attente impossible, {4} source(s) conservée(s) (vérifier le mapping).",
                        Name, runId, batchNo, totalBatches, batch.Count);
                }
            }

            sw.Stop();
            var summary = string.Format(
                "[run {0}] terminé en {1} ms — {2} fichier(s) source traité(s) en {3} sortie(s) déposée(s), {4} sortie(s) en attente | ignorés(non prêts)={5} | erreurs lecture={6}",
                runId, sw.ElapsedMilliseconds, delivered, outputsWritten, pendedOutputs, notReady, readErrors);
            Log.Info("{0}: {1}", Name, summary);

            int problems = (hadFailure ? 1 : 0) + readErrors + pendedOutputs;

            // historique pour le dashboard (best-effort)
            _stats.Append(new RunStats
            {
                Timestamp = DateTime.Now,
                Pipeline = Name,
                FilesProcessed = delivered,
                OutputsDelivered = outputsWritten,
                OutputsPending = pendedOutputs,
                NotReady = notReady,
                ReadErrors = readErrors,
                HadFailure = hadFailure,
                DurationMs = sw.ElapsedMilliseconds
            });

            if (problems > 0)
                EventLogWriter.Warn("Passage terminé avec des anomalies. " + summary, EventLogWriter.EvtRunErrors);
            else
                EventLogWriter.Info("Passage OK. " + summary, EventLogWriter.EvtRunSummary);
        }

        /// <summary>
        /// Retente le dépôt des fichiers en attente de transfert. Ceux qui
        /// passent quittent la file ; les autres y restent pour le prochain essai.
        /// </summary>
        private void RetryPending(CancellationToken token)
        {
            List<PendingItem> items;
            try { items = _pending.List().ToList(); }
            catch (Exception ex) { Log.Warn(ex, "{0}: lecture de la file d'attente impossible.", Name); return; }

            if (items.Count == 0) return;
            Log.Info("{0}: {1} fichier(s) en attente de transfert à retenter.", Name, items.Count);

            int redelivered = 0, stillPending = 0;
            foreach (var item in items)
            {
                if (token.IsCancellationRequested) return;
                _pauseGate.Wait(token);
                try
                {
                    var content = File.ReadAllBytes(item.Path);
                    TransportRunner.DeliverWithRetry(item.Meta.Channel, item.Meta.FileName, content, token);
                    _pending.Remove(item);
                    redelivered++;
                    Log.Info("{0}: attente -> déposé '{1}' (canal '{2}').",
                        Name, item.Meta.FileName, item.Meta.ChannelName);
                }
                catch (Exception ex)
                {
                    stillPending++;
                    Log.Warn(ex, "{0}: '{1}' toujours en attente de transfert (canal '{2}').",
                        Name, item.Meta.FileName, item.Meta.ChannelName);
                }
            }
            Log.Info("{0}: file d'attente — {1} redéposé(s), {2} encore en attente.",
                Name, redelivered, stillPending);
            if (stillPending > 0)
                EventLogWriter.Warn(
                    string.Format("File d'attente de transfert : {0} fichier(s) toujours non livré(s).", stillPending),
                    EventLogWriter.EvtDeliveryFailed);
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

        /// <summary>
        /// Génère la sortie du lot, puis tente le dépôt. Si le dépôt échoue après
        /// ses tentatives, le fichier généré est mis en ATTENTE DE TRANSFERT (il
        /// sera redéposé au prochain passage). Dans les deux cas la génération a
        /// réussi et le contenu est sécurisé, donc les sources sont archivables.
        /// Retourne true si déposé, false si mis en attente.
        /// </summary>
        private bool WriteChannelOutput(OutputChannel ch, List<string> files,
            string runId, int batchNo, int totalBatches, CancellationToken token)
        {
            // résout le provider du canal ("mapping" par défaut, ou une DLL plugin)
            var exporter = _providers.Resolve(ch.Provider);

            var inputXmls = files.Select(File.ReadAllText).ToList();
            var result = exporter.Export(inputXmls, ch);

            foreach (var w in result.Warnings) Log.Warn("{0}: {1}", Name, w);

            // nom de fichier de sortie, avec le numéro de lot pour éviter l'écrasement
            var fileName = BuildFileName(ch, batchNo, totalBatches);

            try
            {
                // dépôt via le transport du canal (FS / FTPS / S3), avec retry.
                TransportRunner.DeliverWithRetry(ch, fileName, result.Content, token);
                Log.Info("{0}: [run {1}] déposé '{2}' via {3} (provider '{4}')", Name, runId, fileName,
                    (ch.Transport?.Type ?? "Fs"), ch.Provider ?? "mapping");
                return true;
            }
            catch (Exception ex)
            {
                // dépôt impossible : on met le fichier GÉNÉRÉ en attente de transfert.
                // (peut lever si même l'écriture en attente échoue -> propagée)
                Log.Warn(ex, "{0}: [run {1}] dépôt de '{2}' échoué, mise en attente de transfert.",
                    Name, runId, fileName);
                _pending.Save(ch, fileName, result.Content);
                return false;
            }
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