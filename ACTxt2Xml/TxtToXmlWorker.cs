using System.IO;

namespace ConversionService
{
    public class TxtToXmlWorker : WorkerBase
    {
        public TxtToXmlWorker(WorkerSettings s) : base("TxtToXml", s) { }

        protected override void ProcessFile(string sourcePath)
        {
            var content = File.ReadAllText(sourcePath);

            // TODO: mécanique de mappage TXT -> XML
            string xml = MapToXml(content);

            var target = Path.Combine(Settings.TargetFolder,
                Path.GetFileNameWithoutExtension(sourcePath) + ".xml");
            File.WriteAllText(target, xml);
            File.Delete(sourcePath);

            Log.Info("Converted {0} -> {1}", sourcePath, target);
        }

        private string MapToXml(string txt)
        {
            // placeholder
            return "<root></root>";
        }
    }
}
