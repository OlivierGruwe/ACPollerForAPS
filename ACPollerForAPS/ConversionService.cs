using NLog;
using ACPollerForAPS.Core;
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
            var path = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "settings.json");
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
        }

        protected override void OnPause() => _pipeline?.Pause();
        protected override void OnContinue() => _pipeline?.Resume();

        protected override void OnStop()
        {
            _pipeline?.Stop();
            Log.Info("Service stopped");
            LogManager.Shutdown();
        }
    }
}
