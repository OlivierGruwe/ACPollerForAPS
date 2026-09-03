using System.IO;
using ACPollerForAPS.Core;
using Newtonsoft.Json;

namespace PipelineConfigWpf
{
    /// <summary>
    /// Chargement / enregistrement fiable de la configuration (côté UI).
    ///
    /// L'UI lit et écrit le MÊME fichier settings.json que le service, avec la
    /// même structure : la config du pipeline est sous la clé "Pipeline".
    /// Un seul fichier, un seul nom (settings.json), partagé UI/service.
    /// Écriture atomique + sauvegarde .bak.
    /// </summary>
    public static class ConfigStore
    {
        // wrapper local reflétant la structure du settings.json du service :
        // { "Pipeline": { ... } }
        private class Root
        {
            public PipelineSettings Pipeline { get; set; }
        }

        private static readonly JsonSerializerSettings JsonOpts = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static PipelineSettings Load(string path)
        {
            var json = File.ReadAllText(path);

            // structure attendue : { "Pipeline": {...} }
            var root = JsonConvert.DeserializeObject<Root>(json);
            if (root != null && root.Pipeline != null)
                return root.Pipeline;

            // tolérance : accepte aussi un fichier sans wrapper (juste {...})
            var flat = JsonConvert.DeserializeObject<PipelineSettings>(json);
            if (flat != null)
                return flat;

            throw new InvalidDataException("Invalid configuration file.");
        }

        public static void Save(string path, PipelineSettings settings)
        {
            // on écrit TOUJOURS avec le wrapper Pipeline, comme le service l'attend
            var root = new Root { Pipeline = settings };
            var json = JsonConvert.SerializeObject(root, JsonOpts);

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
            else
            {
                File.Move(tmp, path);
            }
        }
    }
}