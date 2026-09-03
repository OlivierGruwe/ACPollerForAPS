using System;
using System.Windows;
using ACPollerForAPS.Core;
using FluentFTP;
using MahApps.Metro.Controls;

namespace PipelineConfigWpf
{
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            HookVm();
            // libellé du bouton selon le thème courant (mémorisé)
            ThemeButton.Content = ThemeManager2.IsDark() ? "☀ Light" : "🌙 Dark";
        }

        // Quand l'utilisateur choisit une suggestion dans le petit déroulant,
        // on remplit le Path du champ (la ligne courante), sans jamais l'écraser
        // au chargement (le binding ne touche pas Path).
        private void PathSuggestion_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var combo = sender as System.Windows.Controls.ComboBox;
            if (combo?.SelectedItem == null) return;
            var field = combo.DataContext as FieldVm;
            if (field == null) return;
            field.Path = combo.SelectedItem.ToString();
            combo.SelectedItem = null; // le déroulant ne garde pas de sélection
        }

        private MainViewModel Vm => (MainViewModel)DataContext;

        // bascule clair/sombre et met à jour le libellé du bouton
        private void ToggleTheme(object sender, RoutedEventArgs e)
        {
            bool isDark = ThemeManager2.Toggle();
            ThemeButton.Content = isDark ? "☀ Light" : "🌙 Dark";
        }

        private void HookVm()
        {
            if (Vm == null) return;
            Vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedChannel))
                {
                    FtpPassword.Clear();
                    S3Secret.Clear();
                }
            };
            // avant chaque save, appliquer le mot de passe éventuellement saisi
            Vm.BeforeSave = ApplyPasswordIfTyped;
        }

        // --- sélecteur de dossier (WPF n'en a pas de natif) ---
        private string PickFolder()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
        }
        private void BrowseInput(object sender, RoutedEventArgs e) { var p = PickFolder(); if (p != null) Vm.InputFolder = p; }
        private void BrowseArchive(object sender, RoutedEventArgs e) { var p = PickFolder(); if (p != null) Vm.ArchiveFolder = p; }
        private void BrowseError(object sender, RoutedEventArgs e) { var p = PickFolder(); if (p != null) Vm.ErrorFolder = p; }

        // Charge un fichier XML dans le SampleXml du canal courant.
        private void LoadSampleXml(object sender, RoutedEventArgs e)
        {
            var ch = Vm?.SelectedChannel;
            if (ch == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "XML (*.xml)|*.xml|All files (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                ch.SampleXml = System.IO.File.ReadAllText(dlg.FileName);
                Vm.Dirty = true;
                Vm.LoadReferencePaths(ch.SampleXml); // rafraîchit l'auto-complétion
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot read XML:\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Re-scanne le SampleXml du canal pour mettre à jour la liste des Path.
        private void RefreshPaths(object sender, RoutedEventArgs e)
        {
            var ch = Vm?.SelectedChannel;
            if (ch == null) return;
            Vm.LoadReferencePaths(ch.SampleXml);
        }

        /// <summary>
        /// Applique le mot de passe saisi (s'il y en a un) au canal courant :
        /// on le CHIFFRE immédiatement (DPAPI) et on ne conserve jamais le clair.
        /// Champ vide => on garde le mot de passe chiffré existant.
        /// </summary>
        private void ApplyPasswordIfTyped()
        {
            var ch = Vm?.SelectedChannel;
            if (ch == null) return;
            var typedFtp = FtpPassword.Password;
            if (!string.IsNullOrEmpty(typedFtp))
            {
                ch.PasswordEncrypted = CredentialProtector.Encrypt(typedFtp);
                Vm.Dirty = true;
            }
            var typedS3 = S3Secret.Password;
            if (!string.IsNullOrEmpty(typedS3))
            {
                ch.S3SecretKeyEncrypted = CredentialProtector.Encrypt(typedS3);
                Vm.Dirty = true;
            }
        }

        private void TestFtps(object sender, RoutedEventArgs e)
        {
            var ch = Vm?.SelectedChannel;
            if (ch == null) return;
            ApplyPasswordIfTyped();

            string password = !string.IsNullOrEmpty(FtpPassword.Password)
                ? FtpPassword.Password
                : CredentialProtector.Decrypt(ch.PasswordEncrypted);

            try
            {
                bool impl = string.Equals(ch.FtpsMode, "Implicit", StringComparison.OrdinalIgnoreCase);
                using (var client = new FtpClient(ch.Host, ch.Username, password, ch.Port))
                {
                    client.Config.EncryptionMode = impl ? FtpEncryptionMode.Implicit : FtpEncryptionMode.Explicit;
                    client.Config.DataConnectionEncryption = true;
                    client.Config.ValidateAnyCertificate = !ch.ValidateCertificate;
                    client.Connect();
                    bool ok = client.IsConnected;
                    client.Disconnect();
                    MessageBox.Show(ok ? "Connection successful." : "Could not connect.",
                        "FTPS test", MessageBoxButton.OK,
                        ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Connection failed:\n" + ex.Message, "FTPS test",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestS3(object sender, RoutedEventArgs e)
        {
            var ch = Vm?.SelectedChannel;
            if (ch == null) return;
            ApplyPasswordIfTyped();

            string secret = !string.IsNullOrEmpty(S3Secret.Password)
                ? S3Secret.Password
                : CredentialProtector.Decrypt(ch.S3SecretKeyEncrypted);

            try
            {
                var creds = new Amazon.Runtime.BasicAWSCredentials(ch.S3AccessKey, secret);
                var config = new Amazon.S3.AmazonS3Config();
                if (!string.IsNullOrWhiteSpace(ch.S3ServiceUrl))
                {
                    config.ServiceURL = ch.S3ServiceUrl;
                    config.ForcePathStyle = ch.S3ForcePathStyle;
                }
                else
                {
                    config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(ch.S3Region);
                }

                using (var client = new Amazon.S3.AmazonS3Client(creds, config))
                {
                    // teste l'accès au bucket
                    var resp = client.ListObjectsV2Async(
                        new Amazon.S3.Model.ListObjectsV2Request { BucketName = ch.S3Bucket, MaxKeys = 1 })
                        .GetAwaiter().GetResult();
                    MessageBox.Show("Bucket reachable (HTTP " + (int)resp.HttpStatusCode + ").",
                        "S3 test", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("S3 test failed:\n" + ex.Message, "S3 test",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
