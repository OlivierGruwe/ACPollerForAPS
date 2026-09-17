using System.Collections.Generic;
using System.IO;
using ACPollerForAPS.Core;
using Newtonsoft.Json;

namespace PipelineConfigWpf
{
    /// <summary>
    /// Chargement / enregistrement de la configuration (côté UI), au format
    /// multi-pipelines : { "Pipelines": [ {...} ] }. Chaque pipeline a une
    /// sortie unique (Output). Migration des anciens formats mono-pipeline.
    /// Écriture atomique + sauvegarde .bak.
    /// </summary>
    public static class ConfigStore
    {
        private class LegacyRoot { public PipelineSettings Pipeline { get; set; } }

        private static readonly JsonSerializerSettings JsonOpts = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static AppConfig Load(string path)
        {
            var json = File.ReadAllText(path);

            // 1) nouveau format multi-pipelines
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json);
            if (cfg?.Pipelines != null && cfg.Pipelines.Count > 0)
                return cfg;

            // 2) ancien format { "Pipeline": {...} } -> migration
            var legacy = JsonConvert.DeserializeObject<LegacyRoot>(json);
            if (legacy?.Pipeline != null)
            {
                if (string.IsNullOrWhiteSpace(legacy.Pipeline.Name))
                    legacy.Pipeline.Name = "Pipeline 1";
                return new AppConfig { Pipelines = new List<PipelineSettings> { legacy.Pipeline } };
            }

            // 3) tolérance : un PipelineSettings nu
            var flat = JsonConvert.DeserializeObject<PipelineSettings>(json);
            if (flat != null && (flat.Output != null || !string.IsNullOrEmpty(flat.InputFolder)))
            {
                if (string.IsNullOrWhiteSpace(flat.Name)) flat.Name = "Pipeline 1";
                return new AppConfig { Pipelines = new List<PipelineSettings> { flat } };
            }

            throw new InvalidDataException("Invalid configuration file.");
        }

        public static void Save(string path, AppConfig config)
        {
            var json = JsonConvert.SerializeObject(config, JsonOpts);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
            {
                var bak = path + ".bak";
                try { if (File.Exists(bak)) File.Delete(bak); } catch { }
                File.Replace(tmp, path, bak);
            }
            else File.Move(tmp, path);
        }
    }
}
