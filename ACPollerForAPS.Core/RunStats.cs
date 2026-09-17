using System;
using System.Collections.Generic;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Une ligne d'historique = le bilan d'UN passage d'UN pipeline.
    /// Sérialisée en JSON, une par ligne, dans les fichiers journaliers de stats/.
    /// Sert à alimenter le dashboard (volumétrie, taux d'erreur, durées).
    /// </summary>
    public class RunStats
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;   // fin du passage
        public string Pipeline { get; set; } = "";                // nom du pipeline
        public int FilesProcessed { get; set; }                   // fichiers source traités
        public int OutputsDelivered { get; set; }                 // sorties déposées
        public int OutputsPending { get; set; }                   // sorties en attente de transfert
        public int NotReady { get; set; }                         // fichiers ignorés (pas prêts)
        public int ReadErrors { get; set; }                       // erreurs de lecture
        public bool HadFailure { get; set; }                      // le passage a-t-il eu un échec
        public long DurationMs { get; set; }                      // durée du passage

        // total d'anomalies (pour le taux d'erreur)
        public int Problems => (HadFailure ? 1 : 0) + ReadErrors + OutputsPending;
    }

    /// <summary>
    /// Agrégat d'un jour (ou d'un pipeline sur une période) pour le dashboard.
    /// </summary>
    public class StatsBucket
    {
        public string Label { get; set; } = "";      // ex. "2026-09-17" ou nom pipeline
        public int Runs { get; set; }
        public int FilesProcessed { get; set; }
        public int OutputsDelivered { get; set; }
        public int OutputsPending { get; set; }
        public int ReadErrors { get; set; }
        public int Failures { get; set; }
        public long DurationMsTotal { get; set; }

        public int Problems { get; set; }
        public double ErrorRate => Runs == 0 ? 0 : (double)Problems / Runs;
        public long AvgDurationMs => Runs == 0 ? 0 : DurationMsTotal / Runs;
    }
}
