using System.ComponentModel;
using System.Configuration.Install;
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
                DisplayName = "ACPollerForAPS Conversion Service (TXT<->XML)",
                Description = "Convertit les fichiers TXT en XML et inversement (multithread).",
                StartType = ServiceStartMode.Automatic
            };

            Installers.Add(process);
            Installers.Add(service);
        }
    }
}
