using System;
using System.IO;
using ControlzEx.Theming;

namespace PipelineConfigWpf
{
    /// <summary>
    /// Gère le thème clair/sombre (via MahApps/ControlzEx) et mémorise le
    /// choix dans un petit fichier à côté de l'exe (theme.pref).
    /// </summary>
    public static class ThemeManager2
    {
        private const string Light = "Light.Blue";
        private const string Dark = "Dark.Blue";

        private static string PrefPath =>
            Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                "theme.pref");

        /// <summary>Applique le thème mémorisé (ou clair par défaut) au démarrage.</summary>
        public static void ApplySaved()
        {
            var dark = LoadIsDark();
            Apply(dark);
        }

        /// <summary>Bascule clair/sombre, applique et mémorise. Retourne true si sombre.</summary>
        public static bool Toggle()
        {
            var current = IsDark();
            var next = !current;
            Apply(next);
            Save(next);
            return next;
        }

        public static bool IsDark()
        {
            var t = ThemeManager.Current.DetectTheme();
            return t != null && t.BaseColorScheme == "Dark";
        }

        private static void Apply(bool dark)
        {
            ThemeManager.Current.ChangeTheme(System.Windows.Application.Current, dark ? Dark : Light);
        }

        private static bool LoadIsDark()
        {
            try
            {
                if (File.Exists(PrefPath))
                    return File.ReadAllText(PrefPath).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);
            }
            catch { /* préférence illisible : on reste en clair */ }
            return false;
        }

        private static void Save(bool dark)
        {
            try { File.WriteAllText(PrefPath, dark ? "dark" : "light"); }
            catch { /* non bloquant */ }
        }
    }
}
