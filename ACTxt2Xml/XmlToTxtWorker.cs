using System.IO;

namespace ConversionService
{
    public class XmlToTxtWorker : WorkerBase
    {
        public XmlToTxtWorker(WorkerSettings s) : base("XmlToTxt", s) { }

        protected override void ProcessFile(string sourcePath)
        {
            var content = File.ReadAllText(sourcePath);

            // TODO: mécanique de mappage XML -> TXT
            string txt = MapToTxt(content);

            var target = Path.Combine(Settings.TargetFolder,
                Path.GetFileNameWithoutExtension(sourcePath) + ".txt");
            File.WriteAllText(target, txt);
            File.Delete(sourcePath);

            Log.Info("Converted {0} -> {1}", sourcePath, target);
        }

        private string MapToTxt(string xml)
        {
            // placeholder
            return string.Empty;
        }
    }
}
