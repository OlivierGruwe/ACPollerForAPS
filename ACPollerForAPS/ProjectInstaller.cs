using System;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;

namespace ConversionService
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            var process = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem
            };

            var service = new ServiceInstaller
            {
                ServiceName = "ACPollerForAPS",
                DisplayName = "ACPollerForAPS — Invoice pipeline",
                Description = "Routes, merges and converts APS invoice exports to ERP formats "
                            + "(CSV/XML) and delivers them (FS/FTPS/S3).",
                StartType = ServiceStartMode.Automatic
            };

            Installers.Add(process);
            Installers.Add(service);
        }

        /// <summary>
        /// Le ServiceInstaller de .NET ajoute AUTOMATIQUEMENT un EventLogInstaller
        /// qui crée une source portant le nom du service ("ACPollerForAPS") dans le
        /// journal "Application". C'est lui qui provoquait le conflit avec notre
        /// journal dédié "ACPollerForAPS". On le RETIRE ici, puis on crée nous-mêmes
        /// la bonne source ("ACPollerForAPS.Service") dans le journal "ACPollerForAPS".
        /// </summary>
        protected override void OnBeforeInstall(System.Collections.IDictionary savedState)
        {
            RemoveDefaultEventLogInstallers();
            base.OnBeforeInstall(savedState);
        }

        protected override void OnBeforeUninstall(System.Collections.IDictionary savedState)
        {
            RemoveDefaultEventLogInstallers();
            base.OnBeforeUninstall(savedState);
        }

        private void RemoveDefaultEventLogInstallers()
        {
            foreach (var inst in Installers.Cast<Installer>().ToList())
            {
                var si = inst as ServiceInstaller;
                if (si == null) continue;
                var toRemove = si.Installers.Cast<Installer>()
                                 .OfType<EventLogInstaller>().ToList();
                foreach (var eli in toRemove)
                    si.Installers.Remove(eli);
            }
        }

        public override void Install(System.Collections.IDictionary stateSaver)
        {
            base.Install(stateSaver);
            EnsureEventSource();
        }

        private static void EnsureEventSource()
        {
            try
            {
                if (EventLog.SourceExists(EventLogWriter.Source))
                {
                    var currentLog = EventLog.LogNameFromSourceName(EventLogWriter.Source, ".");
                    if (string.Equals(currentLog, EventLogWriter.LogName,
                            StringComparison.OrdinalIgnoreCase))
                        return; // déjà correcte
                    EventLog.DeleteEventSource(EventLogWriter.Source);
                }
                EventLog.CreateEventSource(
                    new EventSourceCreationData(EventLogWriter.Source, EventLogWriter.LogName));
            }
            catch
            {
                // ne pas faire échouer l'installation pour un souci de source Event Log
            }
        }

        public override void Uninstall(System.Collections.IDictionary savedState)
        {
            try
            {
                if (EventLog.SourceExists(EventLogWriter.Source))
                    EventLog.DeleteEventSource(EventLogWriter.Source);
            }
            catch { /* best effort */ }
            base.Uninstall(savedState);
        }
    }
}