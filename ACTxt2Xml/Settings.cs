using Newtonsoft.Json;
using System.IO;

namespace ConversionService
{
    public class WorkerSettings
    {
        public string SourceFolder { get; set; }
        public string TargetFolder { get; set; }
        public int PollingIntervalSeconds { get; set; } = 30;
        public string FileFilter { get; set; } = "*.*";
        public int StableCheckMs { get; set; } = 1000; // délai entre 2 mesures de taille
    }

    public class AppSettings
    {
        public WorkerSettings TxtToXml { get; set; }
        public WorkerSettings XmlToTxt { get; set; }

        public static AppSettings Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<AppSettings>(json);
        }
    }
}
