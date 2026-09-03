using System.Windows;

namespace PipelineConfigWpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // applique le thème mémorisé (clair par défaut) avant l'affichage
            ThemeManager2.ApplySaved();
        }
    }
}
