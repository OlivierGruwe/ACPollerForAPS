using System;
using System.Reflection;

namespace ConversionService
{
    /// <summary>
    /// Hôte de debug en mode console (ACPollerForAPS.exe --console).
    /// Ne DOIT jamais être atteint en mode service : tout accès Console
    /// en session 0 (sans console attachée) est protégé et ne fera pas
    /// planter le processus.
    /// </summary>
    internal class ConsoleHost
    {
        public void RunInteractive()
        {
            // Garde-fou : si aucune console n'est réellement attachée
            // (cas d'un lancement en session 0), on n'essaie pas de piloter
            // le clavier — on démarre le service et on attend indéfiniment.
            bool hasConsole = HasRealConsole();

            var svc = new ConversionWindowsService();
            Invoke(svc, "OnStart", new object[] { new string[0] });

            if (!hasConsole)
            {
                // Pas de console : on ne lit pas de touches, on ne fait pas
                // planter. On bloque le thread jusqu'à l'arrêt du processus.
                System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
                return;
            }

            SafeWriteLine("=== ACPollerForAPS - mode console ===");
            SafeWriteLine("Commandes : [P]ause  [R]esume  [Q]uit");

            bool running = true;
            while (running)
            {
                ConsoleKey key;
                try
                {
                    key = Console.ReadKey(true).Key;
                }
                catch
                {
                    // Console devenue indisponible : on sort proprement.
                    break;
                }

                switch (key)
                {
                    case ConsoleKey.P:
                        Invoke(svc, "OnPause", null);
                        SafeWriteLine("[paused]");
                        break;
                    case ConsoleKey.R:
                        Invoke(svc, "OnContinue", null);
                        SafeWriteLine("[resumed]");
                        break;
                    case ConsoleKey.Q:
                        running = false;
                        break;
                }
            }

            Invoke(svc, "OnStop", null);
            SafeWriteLine("Arrêté.");
        }

        /// <summary>Vrai seulement si une console utilisable est attachée.</summary>
        private static bool HasRealConsole()
        {
            try
            {
                // Accéder à ces propriétés lève une exception si pas de console.
                return Environment.UserInteractive
                    && Console.WindowHeight > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void SafeWriteLine(string text)
        {
            try { Console.WriteLine(text); } catch { /* pas de console : on ignore */ }
        }

        private static void Invoke(object target, string method, object[] args)
        {
            var mi = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic);
            mi.Invoke(target, args);
        }
    }
}