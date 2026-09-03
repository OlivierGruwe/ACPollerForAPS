using System;
using System.ServiceProcess;

namespace ConversionService
{
    static class Program
    {
        static void Main(string[] args)
        {
            // Mode console UNIQUEMENT si on passe explicitement --console.
            // On ne se fie PAS à Environment.UserInteractive, qui vaut true
            // quand "interagir avec le bureau" est coché et fausse le routage.
            bool consoleMode = args.Length > 0 &&
                args[0].Equals("--console", StringComparison.OrdinalIgnoreCase);

            if (consoleMode)
            {
                var host = new ConsoleHost();
                host.RunInteractive();
            }
            else
            {
                // Mode service : AUCUN appel Console ici ni en aval.
                ServiceBase.Run(new ServiceBase[] { new ConversionWindowsService() });
            }
        }
    }
}