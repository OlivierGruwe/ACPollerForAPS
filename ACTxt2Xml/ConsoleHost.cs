using System;
using System.Reflection;

namespace ConversionService
{
    /// <summary>
    /// Permet de lancer le service en mode console (F5 dans Visual Studio,
    /// ou ConversionWindowsService.exe --console) pour debugger facilement.
    /// </summary>
    internal class ConsoleHost
    {
        public void RunInteractive()
        {
            Console.WriteLine("=== ACTxt2Xml - mode console ===");
            Console.WriteLine("Commandes : [P]ause  [R]esume  [Q]uit");

            // On réutilise la logique du service via réflexion sur les méthodes protégées.
            var svc = new ConversionWindowsService();
            Invoke(svc, "OnStart", new object[] { new string[0] });

            bool running = true;
            while (running)
            {
                var key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.P:
                        Invoke(svc, "OnPause", null);
                        Console.WriteLine("[paused]");
                        break;
                    case ConsoleKey.R:
                        Invoke(svc, "OnContinue", null);
                        Console.WriteLine("[resumed]");
                        break;
                    case ConsoleKey.Q:
                        running = false;
                        break;
                }
            }

            Invoke(svc, "OnStop", null);
            Console.WriteLine("Arrêté.");
        }

        private static void Invoke(object target, string method, object[] args)
        {
            var mi = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic);
            mi.Invoke(target, args);
        }
    }
}
