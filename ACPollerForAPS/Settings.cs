using Newtonsoft.Json;
using ACPollerForAPS.Core;
using System.IO;

namespace ConversionService
{
    // NOTE : WorkerSettings et FieldMapping (format largeur-fixe) ont été
    // retirés en même temps que les workers TxtToXml / XmlToTxt. Le service
    // ne contient plus que le PipelineWorker, configuré par la section
    // "Pipeline" du settings.json.

    public class AppSettings
    {
        public PipelineSettings Pipeline { get; set; }

        public static AppSettings Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<AppSettings>(json);
        }
    }
}
