using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
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
        /// À l'installation (droits admin) : crée la source d'événement Windows
        /// dans un journal dédié "ACPollerForAPS", pour la supervision. Fait une
        /// seule fois ; le service écrira ensuite sans droits particuliers.
        /// </summary>
        public override void Install(System.Collections.IDictionary stateSaver)
        {
            base.Install(stateSaver);
            try
            {
                if (!EventLog.SourceExists(EventLogWriter.Source))
                {
                    EventLog.CreateEventSource(
                        new EventSourceCreationData(EventLogWriter.Source, EventLogWriter.LogName));
                }
            }
            catch
            {
                // ne pas faire échouer l'installation si la création de source échoue :
                // le service dégradera proprement (supervision Windows désactivée).
            }
        }

        /// <summary>À la désinstallation : retire la source d'événement.</summary>
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
