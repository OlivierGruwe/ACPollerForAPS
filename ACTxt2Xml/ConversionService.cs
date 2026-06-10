using NLog;
using System.IO;
using System.Reflection;
using System.ServiceProcess;

namespace ConversionService
{
    public class ConversionWindowsService : ServiceBase
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private TxtToXmlWorker _txtToXml;
        private XmlToTxtWorker _xmlToTxt;

        public ConversionWindowsService()
        {
            ServiceName = "ACTxt2Xml";
            CanPauseAndContinue = true;
            CanStop = true;
        }

        protected override void OnStart(string[] args)
        {
            var path = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "settings.json");
            var settings = AppSettings.Load(path);

            _txtToXml = new TxtToXmlWorker(settings.TxtToXml);
            _xmlToTxt = new XmlToTxtWorker(settings.XmlToTxt);

            _txtToXml.Start();
            _xmlToTxt.Start();
            Log.Info("Service started");
        }

        protected override void OnPause()
        {
            _txtToXml.Pause();
            _xmlToTxt.Pause();
        }

        protected override void OnContinue()
        {
            _txtToXml.Resume();
            _xmlToTxt.Resume();
        }

        protected override void OnStop()
        {
            _txtToXml.Stop();
            _xmlToTxt.Stop();
            Log.Info("Service stopped");
            LogManager.Shutdown();
        }
    }
}
