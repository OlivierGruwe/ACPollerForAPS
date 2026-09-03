using System;
using System.Diagnostics;
using NLog;

namespace ConversionService
{
    /// <summary>
    /// Écrit des événements MÉTIER dans le journal d'événements Windows dédié
    /// "ACPollerForAPS" (source "ACPollerForAPS"), pour la surveillance/supervision.
    ///
    /// Ce journal est complémentaire des logs fichier (NLog) : il ne reçoit que
    /// l'essentiel (démarrage/arrêt, résumés de passage, incidents), pour qu'un
    /// coup d'œil à l'Observateur d'événements suffise à juger la santé du service.
    ///
    /// La SOURCE est créée à l'installation (droits admin requis, cf.
    /// ProjectInstaller). Si elle n'existe pas (installation partielle), on
    /// dégrade proprement : on log un warning fichier et on n'écrit pas dans
    /// l'Event Log, sans jamais faire planter le service.
    /// </summary>
    public static class EventLogWriter
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public const string Source = "ACPollerForAPS";
        public const string LogName = "ACPollerForAPS";

        // ids d'événement (utiles pour filtrer/superviser)
        public const int EvtServiceStarted = 1000;
        public const int EvtServiceStopped = 1001;
        public const int EvtRunSummary     = 1100;
        public const int EvtRunErrors      = 1200; // passage terminé avec des erreurs
        public const int EvtDeliveryFailed = 1300;

        private static bool _available;
        private static bool _checked;

        /// <summary>Vérifie une fois que la source existe (sans tenter de la créer).</summary>
        private static bool Available()
        {
            if (_checked) return _available;
            _checked = true;
            try
            {
                _available = EventLog.SourceExists(Source);
                if (!_available)
                    Log.Warn("Event Log source '{0}' absente : la supervision Windows est désactivée "
                           + "(réinstaller le service en administrateur pour créer la source).", Source);
            }
            catch (Exception ex)
            {
                _available = false;
                Log.Warn(ex, "Impossible de vérifier la source Event Log '{0}'.", Source);
            }
            return _available;
        }

        public static void Info(string message, int eventId)
            => Write(message, EventLogEntryType.Information, eventId);

        public static void Warn(string message, int eventId)
            => Write(message, EventLogEntryType.Warning, eventId);

        public static void Error(string message, int eventId)
            => Write(message, EventLogEntryType.Error, eventId);

        private static void Write(string message, EventLogEntryType type, int eventId)
        {
            if (!Available()) return;
            try
            {
                EventLog.WriteEntry(Source, message, type, eventId);
            }
            catch (Exception ex)
            {
                // ne jamais laisser le logging faire planter le service
                Log.Warn(ex, "Écriture Event Log échouée.");
            }
        }
    }
}
