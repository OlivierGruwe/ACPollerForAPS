using ACPollerForAPS.Core;
using NLog;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.ServiceProcess;

namespace ConversionService
{
    public class ConversionWindowsService : ServiceBase
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        // un worker par pipeline (flux bidirectionnels : APS->Optima, Optima->APS, ...)
        private readonly List<PipelineWorker> _pipelines = new List<PipelineWorker>();

        public ConversionWindowsService()
        {
            ServiceName = "ACPollerForAPS";
            CanPauseAndContinue = true;
            CanStop = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                try { Directory.CreateDirectory(Path.Combine(baseDir, "logs")); } catch { }

                var path = Path.Combine(baseDir, "settings.json");
                if (!File.Exists(path))
                {
                    var msg = "settings.json introuvable dans " + baseDir +
                              " : configurez les pipelines via l'interface puis redémarrez le service.";
                    Log.Error(msg);
                    EventLogWriter.Error(msg, EventLogWriter.EvtServiceStarted);
                    throw new FileNotFoundException(msg, path);
                }

                // nouveau format : { "Pipelines": [ ... ] }
                var config = JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(path));
                var pipelines = config?.Pipelines ?? new List<PipelineSettings>();

                if (pipelines.Count == 0)
                {
                    Log.Error("Aucun pipeline dans settings.json (clé 'Pipelines' vide) : rien à démarrer.");
                    EventLogWriter.Error("No pipeline in settings.json.", EventLogWriter.EvtServiceStarted);
                    return;
                }

                // providers chargés une fois, partagés par tous les pipelines
                var providers = ProviderLoader.LoadAll(baseDir);

                foreach (var p in pipelines)
                {
                    var worker = new PipelineWorker(p, providers);
                    worker.Start();
                    _pipelines.Add(worker);
                    Log.Info("Pipeline démarré : {0}", string.IsNullOrWhiteSpace(p.Name) ? "(sans nom)" : p.Name);
                }

                Log.Info("Service started — {0} pipeline(s).", _pipelines.Count);
                EventLogWriter.Info(
                    string.Format("ACPollerForAPS service started ({0} pipeline(s)).", _pipelines.Count),
                    EventLogWriter.EvtServiceStarted);
            }
            catch (Exception ex)
            {
                try { Log.Fatal(ex, "Échec du démarrage du service."); } catch { }
                try { EventLogWriter.Error("Service start failed: " + ex.Message, EventLogWriter.EvtServiceStarted); } catch { }
                throw;
            }
        }

        protected override void OnPause() { foreach (var p in _pipelines) p.Pause(); }
        protected override void OnContinue() { foreach (var p in _pipelines) p.Resume(); }

        protected override void OnStop()
        {
            foreach (var p in _pipelines) { try { p.Stop(); } catch (Exception ex) { Log.Error(ex, "stop pipeline"); } }
            _pipelines.Clear();
            Log.Info("Service stopped");
            EventLogWriter.Info("ACPollerForAPS service stopped.", EventLogWriter.EvtServiceStopped);
            LogManager.Shutdown();
        }
    }
}
