using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Historique des passages, en fichiers journaliers JSON-lines (une RunStats
    /// par ligne) dans un dossier stats/. Zéro dépendance lourde, lisible, et
    /// s'archive/se purge facilement. Alimente le dashboard.
    ///
    /// Fichier : stats/2026-09-17.jsonl
    /// </summary>
    public class StatsStore
    {
        private readonly string _dir;
        private readonly object _lock = new object();

        public StatsStore(string statsDir)
        {
            _dir = statsDir;
        }

        private string FileForDay(DateTime day)
            => Path.Combine(_dir, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

        /// <summary>Ajoute le bilan d'un passage (best-effort : n'interrompt jamais le pipeline).</summary>
        public void Append(RunStats s)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                var line = JsonConvert.SerializeObject(s, Formatting.None);
                lock (_lock)
                    File.AppendAllText(FileForDay(s.Timestamp), line + Environment.NewLine);
            }
            catch { /* les stats ne doivent jamais faire échouer un passage */ }
        }

        /// <summary>Relit toutes les lignes des N derniers jours.</summary>
        public List<RunStats> ReadSince(int days)
        {
            var result = new List<RunStats>();
            if (!Directory.Exists(_dir)) return result;
            var from = DateTime.Now.Date.AddDays(-(days - 1));
            for (var d = from; d <= DateTime.Now.Date; d = d.AddDays(1))
            {
                var f = FileForDay(d);
                if (!File.Exists(f)) continue;
                foreach (var line in File.ReadAllLines(f))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try { result.Add(JsonConvert.DeserializeObject<RunStats>(line)); }
                    catch { /* ligne corrompue ignorée */ }
                }
            }
            return result;
        }

        /// <summary>Agrège par jour (pour la courbe de volumétrie / taux d'erreur).</summary>
        public static List<StatsBucket> ByDay(IEnumerable<RunStats> rows)
            => rows.GroupBy(r => r.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                   .OrderBy(g => g.Key)
                   .Select(g => Fold(g.Key, g))
                   .ToList();

        /// <summary>Agrège par pipeline (pour comparer les flux).</summary>
        public static List<StatsBucket> ByPipeline(IEnumerable<RunStats> rows)
            => rows.GroupBy(r => string.IsNullOrWhiteSpace(r.Pipeline) ? "(unnamed)" : r.Pipeline)
                   .OrderBy(g => g.Key)
                   .Select(g => Fold(g.Key, g))
                   .ToList();

        private static StatsBucket Fold(string label, IEnumerable<RunStats> g)
        {
            var b = new StatsBucket { Label = label };
            foreach (var r in g)
            {
                b.Runs++;
                b.FilesProcessed += r.FilesProcessed;
                b.OutputsDelivered += r.OutputsDelivered;
                b.OutputsPending += r.OutputsPending;
                b.ReadErrors += r.ReadErrors;
                if (r.HadFailure) b.Failures++;
                b.DurationMsTotal += r.DurationMs;
                b.Problems += r.Problems;
            }
            return b;
        }
    }
}
