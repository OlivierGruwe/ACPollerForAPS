using System;
using System.Diagnostics;
using NLog;

namespace ConversionService
{
    /// <summary>
    /// Écrit des événements MÉTIER dans le journal d'événements Windows dédié
    /// "ACPollerForAPS", pour la supervision.
    ///
    /// IMPORTANT : la SOURCE ("ACPollerForAPS.Service") est DIFFÉRENTE du nom du
    /// JOURNAL ("ACPollerForAPS"). Windows interdit qu'une source porte le même
    /// nom qu'un journal existant — les nommer différemment élimine le conflit
    /// "la source est identique au nom du journal".
    /// </summary>
    public static class EventLogWriter
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public const string Source  = "ACPollerForAPS.Service"; // != LogName
        public const string LogName = "ACPollerForAPS";

        public const int EvtServiceStarted = 1000;
        public const int EvtServiceStopped = 1001;
        public const int EvtRunSummary     = 1100;
        public const int EvtRunErrors      = 1200;
        public const int EvtDeliveryFailed = 1300;

        private static bool _available;
        private static bool _checked;

        private static bool Available()
        {
            if (_checked) return _available;
            _checked = true;
            try
            {
                _available = EventLog.SourceExists(Source);
                if (!_available)
                    Log.Warn("Event Log source '{0}' absente : supervision Windows désactivée "
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
            try { EventLog.WriteEntry(Source, message, type, eventId); }
            catch (Exception ex) { Log.Warn(ex, "Écriture Event Log échouée."); }
        }
    }
}
