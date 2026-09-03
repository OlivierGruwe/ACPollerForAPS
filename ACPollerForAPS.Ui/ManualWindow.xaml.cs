using System.IO;
using System.Reflection;
using System.Text;
using MahApps.Metro.Controls;

namespace PipelineConfigWpf
{
    public partial class ManualWindow : MetroWindow
    {
        public ManualWindow()
        {
            InitializeComponent();
            LoadManual();
        }

        /// <summary>
        /// Charge le manuel HTML embarqué comme ressource et l'affiche dans le
        /// WebBrowser (moteur IE) — 100% hors ligne, aucun fichier externe.
        /// </summary>
        private void LoadManual()
        {
            var html = ReadEmbedded("PipelineConfigWpf.Manual.html");
            if (html != null)
                Browser.NavigateToString(html);
            else
                Browser.NavigateToString("<html><body style='font-family:Segoe UI'>"
                    + "Manual resource not found.</body></html>");
        }

        private static string ReadEmbedded(string resourceName)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }
    }
}
