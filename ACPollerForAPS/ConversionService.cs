using ACPollerForAPS.Core;
using NLog;
using System;
using System.IO;
using System.Reflection;
using System.ServiceProcess;

namespace ConversionService
{
    public class ConversionWindowsService : ServiceBase
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private PipelineWorker _pipeline;

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
                              " : configurez le pipeline via l'interface puis redémarrez le service.";
                    Log.Error(msg);
                    EventLogWriter.Error(msg, EventLogWriter.EvtServiceStarted);
                    throw new FileNotFoundException(msg, path);
                }
                var settings = AppSettings.Load(path);

                if (settings?.Pipeline == null)
                {
                    Log.Error("Aucune section 'Pipeline' dans settings.json : le service ne démarre pas de worker.");
                    return;
                }

                _pipeline = new PipelineWorker(settings.Pipeline,
                    ProviderLoader.LoadAll(
                        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)));
                _pipeline.Start();
                Log.Info("Service started");
                EventLogWriter.Info("ACPollerForAPS service started.", EventLogWriter.EvtServiceStarted);
            }
            catch (Exception ex)
            {
                try { Log.Fatal(ex, "Échec du démarrage du service."); } catch { }
                try
                {
                    EventLogWriter.Error("Service start failed: " + ex.Message,
                        EventLogWriter.EvtServiceStarted);
                }
                catch { }
                throw;
            }
         
        }

        protected override void OnPause() => _pipeline?.Pause();
        protected override void OnContinue() => _pipeline?.Resume();

        protected override void OnStop()
        {
            _pipeline?.Stop();
            Log.Info("Service stopped");
            EventLogWriter.Info("ACPollerForAPS service stopped.", EventLogWriter.EvtServiceStopped);
            LogManager.Shutdown();
        }
    }
}
