using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACPollerForAPS.Core;
using Newtonsoft.Json;
using NLog;

namespace ConversionService
{
    /// <summary>
    /// File d'attente de transfert : quand le dépôt d'un fichier de sortie
    /// échoue (FTPS/S3 injoignable), le fichier GÉNÉRÉ est conservé ici, puis
    /// redéposé tel quel au passage suivant — sans régénération.
    ///
    /// Organisation : &lt;PendingRoot&gt;/&lt;canal&gt;/&lt;fichier&gt; + un compagnon
    /// &lt;fichier&gt;.meta.json décrivant le canal cible (pour retrouver son
    /// transport). Le contenu généré étant sauvegardé ici, les XML sources
    /// peuvent être archivés normalement : plus aucun risque de perte.
    /// </summary>
    public class PendingStore
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly string _root;

        public PendingStore(string root)
        {
            _root = root;
        }

        private string ChannelDir(string channelName)
        {
            var safe = MakeSafe(channelName);
            var dir = Path.Combine(_root, safe);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Enregistre un fichier de sortie non déposé, avec le canal cible.</summary>
        public void Save(OutputChannel channel, string fileName, byte[] content)
        {
            try
            {
                var dir = ChannelDir(channel.Name);
                // évite l'écrasement si un fichier de même nom attend déjà
                var target = UniquePath(dir, fileName);
                File.WriteAllBytes(target, content);

                var meta = new PendingMeta
                {
                    ChannelName = channel.Name,
                    FileName = Path.GetFileName(target),
                    SavedUtc = DateTime.UtcNow,
                    Channel = channel
                };
                File.WriteAllText(target + ".meta.json",
                    JsonConvert.SerializeObject(meta, Formatting.Indented));

                Log.Warn("Dépôt différé : '{0}' mis en attente de transfert (canal '{1}').",
                    Path.GetFileName(target), channel.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Impossible de mettre le fichier en attente de transfert (canal '{0}').",
                    channel.Name);
                throw; // si même la mise en attente échoue, on ne doit PAS archiver les sources
            }
        }

        /// <summary>Liste les fichiers en attente (hors compagnons .meta.json).</summary>
        public IEnumerable<PendingItem> List()
        {
            if (!Directory.Exists(_root)) yield break;
            foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) continue;
                var metaPath = file + ".meta.json";
                if (!File.Exists(metaPath)) continue; // orphelin sans méta : ignoré
                PendingMeta meta = null;
                try { meta = JsonConvert.DeserializeObject<PendingMeta>(File.ReadAllText(metaPath)); }
                catch (Exception ex) { Log.Warn(ex, "Méta illisible pour {0}, ignoré.", file); }
                if (meta?.Channel == null) continue;
                yield return new PendingItem { Path = file, MetaPath = metaPath, Meta = meta };
            }
        }

        /// <summary>Supprime un fichier en attente et son compagnon (après dépôt réussi).</summary>
        public void Remove(PendingItem item)
        {
            try { if (File.Exists(item.Path)) File.Delete(item.Path); } catch { }
            try { if (File.Exists(item.MetaPath)) File.Delete(item.MetaPath); } catch { }
        }

        private static string UniquePath(string dir, string fileName)
        {
            var dest = Path.Combine(dir, fileName);
            if (!File.Exists(dest)) return dest;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (int i = 1; ; i++)
            {
                var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }
        }

        private static string MakeSafe(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "channel").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }

    public class PendingMeta
    {
        public string ChannelName { get; set; }
        public string FileName { get; set; }
        public DateTime SavedUtc { get; set; }
        // le canal complet, pour retrouver le transport au moment du re-dépôt
        public OutputChannel Channel { get; set; }
    }

    public class PendingItem
    {
        public string Path { get; set; }
        public string MetaPath { get; set; }
        public PendingMeta Meta { get; set; }
    }
}
