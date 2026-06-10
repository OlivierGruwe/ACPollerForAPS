using System;
using System.ServiceProcess;

namespace ConversionService
{
    static class Program
    {
        static void Main(string[] args)
        {
            // Mode console pour debug : lancer avec l'argument --console
            if (Environment.UserInteractive ||
                (args.Length > 0 && args[0].Equals("--console", StringComparison.OrdinalIgnoreCase)))
            {
                var svc = new ConsoleHost();
                svc.RunInteractive();
            }
            else
            {
                ServiceBase.Run(new ServiceBase[] { new ConversionWindowsService() });
            }
        }
    }
}
