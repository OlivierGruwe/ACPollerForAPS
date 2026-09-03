using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using ACPollerForAPS.Core;
using Microsoft.Win32;

namespace PipelineConfigWpf
{
    /// <summary>
    /// ViewModel principal : détient l'état global de la config et les
    /// commandes (New/Open/Save/Validate). Les sections (General, Schedule,
    /// Channels) sont exposées comme propriétés bindables.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private string _currentPath;
        private string _status = "Ready.";
        private bool _dirty;

        // --- General ---
        private string _inputFolder = "", _archiveFolder = "", _errorFolder = "";
        private bool _archiveEnabled = true;
        private string _fileFilter = "*.xml";
        private int _stableCheckMs = 1000;
        private string _recordPath = "/InvoiceData";
        private string _buyerPath = "Header/Buyer/BuyerId";

        // --- Schedule ---
        private int _intervalValue = 24;
        private string _intervalUnit = "Hours";

        // --- Channels ---
        private ChannelVm _selectedChannel;

        // --- Preview ---
        private string _previewXml = "", _previewOutput = "";

        public MainViewModel()
        {
            NewCommand = new RelayCommand(New);
            OpenCommand = new RelayCommand(Open);
            SaveCommand = new RelayCommand(() => Save(false));
            SaveAsCommand = new RelayCommand(() => Save(true));
            ValidateCommand = new RelayCommand(Validate);
            AddChannelCommand = new RelayCommand(AddChannel);
            RemoveChannelCommand = new RelayCommand(RemoveChannel, () => SelectedChannel != null);
            DuplicateChannelCommand = new RelayCommand(DuplicateChannel, () => SelectedChannel != null);
            AddFieldCommand = new RelayCommand(AddField, () => SelectedChannel != null);
            RemoveFieldCommand = new RelayCommand(RemoveField, () => SelectedField != null);
            PreviewCommand = new RelayCommand(Preview, () => SelectedChannel != null);

            TryAutoLoad();
        }

        /// <summary>
        /// Au démarrage : charge automatiquement le settings.json situé à côté
        /// de l'exe s'il existe. Échoue en silence (démarre vide) si absent ou
        /// illisible — l'UI s'ouvre toujours.
        /// </summary>
        private void TryAutoLoad()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                var path = System.IO.Path.Combine(dir, "settings.json");
                if (!System.IO.File.Exists(path)) return;

                LoadModel(ConfigStore.Load(path));
                _currentPath = path;
                OnPropertyChanged(nameof(Title));
                Status = "Auto-loaded: " + path;
            }
            catch (System.Exception ex)
            {
                // settings.json présent mais illisible : on n'empêche pas le
                // démarrage, on signale juste dans la barre de statut.
                Status = "Could not auto-load settings.json: " + ex.Message;
            }
        }

        // hook appelé juste avant Save/Validate : permet au code-behind
        // d'appliquer le mot de passe saisi (PasswordBox non bindable).
        public System.Action BeforeSave { get; set; }

        // ----- properties bound to the UI -----
        public string Status { get => _status; set => Set(ref _status, value); }
        public bool Dirty { get => _dirty; set { Set(ref _dirty, value); OnPropertyChanged(nameof(Title)); } }
        public string Title => "Pipeline configuration" +
            (_currentPath != null ? " — " + _currentPath : " — (unsaved)") + (Dirty ? " *" : "");

        public string InputFolder { get => _inputFolder; set { Set(ref _inputFolder, value); Dirty = true; } }
        public string ArchiveFolder { get => _archiveFolder; set { Set(ref _archiveFolder, value); Dirty = true; } }
        public string ErrorFolder { get => _errorFolder; set { Set(ref _errorFolder, value); Dirty = true; } }
        public bool ArchiveEnabled { get => _archiveEnabled; set { Set(ref _archiveEnabled, value); Dirty = true; } }
        public string FileFilter { get => _fileFilter; set { Set(ref _fileFilter, value); Dirty = true; } }
        public int StableCheckMs { get => _stableCheckMs; set { Set(ref _stableCheckMs, value); Dirty = true; } }
        public string RecordPath { get => _recordPath; set { Set(ref _recordPath, value); Dirty = true; } }
        public string BuyerPath { get => _buyerPath; set { Set(ref _buyerPath, value); Dirty = true; } }

        public int IntervalValue { get => _intervalValue; set { Set(ref _intervalValue, value); Dirty = true; OnPropertyChanged(nameof(IntervalPreview)); } }
        public string IntervalUnit { get => _intervalUnit; set { Set(ref _intervalUnit, value); Dirty = true; OnPropertyChanged(nameof(IntervalPreview)); } }
        public string[] IntervalUnits => new[] { "Minutes", "Hours" };
        public string IntervalPreview
        {
            get
            {
                long sec = (IntervalUnit == "Minutes" ? IntervalValue * 60L : IntervalValue * 3600L);
                return $"→ one run every {sec} seconds.";
            }
        }

        public ObservableCollection<ChannelVm> Channels { get; } = new ObservableCollection<ChannelVm>();
        public ChannelVm SelectedChannel { get => _selectedChannel; set { Set(ref _selectedChannel, value); OnPropertyChanged(nameof(HasChannel)); if (value != null) LoadReferencePaths(value.SampleXml); else { _invoicePaths.Clear(); _linePaths.Clear(); } } }
        public bool HasChannel => SelectedChannel != null;
        public FieldVm SelectedField { get; set; }

        public string PreviewXml { get => _previewXml; set => Set(ref _previewXml, value); }
        public string PreviewOutput { get => _previewOutput; set => Set(ref _previewOutput, value); }

        /// <summary>Charge un XML de référence et distribue les chemins filtrés à chaque champ.</summary>
        public void LoadReferencePaths(string xmlContent)
        {
            _invoicePaths.Clear();
            _linePaths.Clear();
            if (SelectedChannel == null || string.IsNullOrWhiteSpace(xmlContent))
            {
                PushPathsToFields();
                return;
            }
            try
            {
                var res = XmlPathExtractor.Extract(xmlContent,
                    SelectedChannel.RecordPath, SelectedChannel.LinesPath);
                foreach (var p in res.InvoicePaths) _invoicePaths.Add(p);
                foreach (var p in res.LinePaths) _linePaths.Add(p);
                PushPathsToFields();
                Status = $"Reference loaded: {res.InvoicePaths.Count} invoice paths, {res.LinePaths.Count} line paths.";
            }
            catch (System.Exception ex)
            {
                Status = "Reference XML error: " + ex.Message;
            }
        }

        // listes partagées de chemins (facture / ligne) pour le canal courant
        private readonly List<string> _invoicePaths = new List<string>();
        private readonly List<string> _linePaths = new List<string>();

        /// <summary>Donne à chaque champ du canal les deux listes de chemins.</summary>
        private void PushPathsToFields()
        {
            if (SelectedChannel == null) return;
            foreach (var f in SelectedChannel.Fields)
                f.SetPathSources(_invoicePaths, _linePaths);
        }

        // ----- commands -----
        public RelayCommand NewCommand { get; }
        public RelayCommand OpenCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand SaveAsCommand { get; }
        public RelayCommand ValidateCommand { get; }
        public RelayCommand AddChannelCommand { get; }
        public RelayCommand RemoveChannelCommand { get; }
        public RelayCommand DuplicateChannelCommand { get; }
        public RelayCommand AddFieldCommand { get; }
        public RelayCommand RemoveFieldCommand { get; }
        public RelayCommand PreviewCommand { get; }

        // ----- model <-> vm -----
        private void LoadModel(PipelineSettings s)
        {
            InputFolder = s.InputFolder; ArchiveFolder = s.ArchiveFolder; ErrorFolder = s.ErrorFolder;
            ArchiveEnabled = s.ArchiveEnabled; FileFilter = s.FileFilter; StableCheckMs = s.StableCheckMs;
            RecordPath = s.RecordPath; BuyerPath = s.BuyerPath;
            IntervalValue = s.Schedule?.IntervalValue ?? 24;
            IntervalUnit = s.Schedule?.IntervalUnit ?? "Hours";
            Channels.Clear();
            foreach (var c in s.Channels ?? new List<OutputChannel>())
                Channels.Add(ChannelVm.FromModel(c));
            SelectedChannel = Channels.FirstOrDefault();
            Dirty = false;
        }

        private PipelineSettings BuildModel() => new PipelineSettings
        {
            InputFolder = InputFolder, ArchiveFolder = ArchiveFolder, ErrorFolder = ErrorFolder,
            ArchiveEnabled = ArchiveEnabled, FileFilter = FileFilter, StableCheckMs = StableCheckMs,
            RecordPath = RecordPath, BuyerPath = BuyerPath,
            Schedule = new PipelineSchedule { IntervalValue = IntervalValue, IntervalUnit = IntervalUnit },
            Channels = Channels.Select(c => c.ToModel()).ToList()
        };

        // ----- actions -----
        private void New()
        {
            if (!ConfirmDiscard()) return;
            LoadModel(new PipelineSettings());
            _currentPath = null; OnPropertyChanged(nameof(Title));
            Status = "New configuration.";
        }

        private void Open()
        {
            if (!ConfirmDiscard()) return;
            var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                LoadModel(ConfigStore.Load(dlg.FileName));
                _currentPath = dlg.FileName; OnPropertyChanged(nameof(Title));
                Status = "Loaded: " + dlg.FileName;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Cannot load configuration:\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Save(bool saveAs)
        {
            BeforeSave?.Invoke();
            var model = BuildModel();
            var result = ConfigValidator.Validate(model);
            if (!result.IsValid)
            {
                MessageBox.Show("The configuration has errors and will not be saved:\n\n"
                    + string.Join("\n", result.Errors), "Validation failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (result.Warnings.Count > 0)
            {
                var r = MessageBox.Show("Warnings:\n\n" + string.Join("\n", result.Warnings)
                    + "\n\nSave anyway?", "Warnings", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r != MessageBoxResult.Yes) return;
            }

            if (saveAs || string.IsNullOrEmpty(_currentPath))
            {
                var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = _currentPath ?? "pipeline.json" };
                if (dlg.ShowDialog() != true) return;
                _currentPath = dlg.FileName;
            }
            try
            {
                ConfigStore.Save(_currentPath, model);
                Dirty = false; OnPropertyChanged(nameof(Title));
                Status = "Saved: " + _currentPath;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Save failed:\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Validate()
        {
            BeforeSave?.Invoke();
            var model = BuildModel();
            var result = ConfigValidator.Validate(model);
            ConfigValidator.CheckFolders(model, result);
            if (result.IsValid && result.Warnings.Count == 0)
            {
                MessageBox.Show("Configuration valid, no issues.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Status = "Validation OK."; return;
            }
            var sb = new StringBuilder();
            if (result.Errors.Count > 0) sb.AppendLine("ERRORS:").AppendLine(string.Join("\n", result.Errors)).AppendLine();
            if (result.Warnings.Count > 0) sb.AppendLine("WARNINGS:").AppendLine(string.Join("\n", result.Warnings));
            MessageBox.Show(sb.ToString(), "Validation result", MessageBoxButton.OK,
                result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Status = result.IsValid ? "Valid, with warnings." : "Invalid.";
        }

        private void AddChannel()
        {
            var vm = new ChannelVm { Name = "Channel " + (Channels.Count + 1) };
            Channels.Add(vm); SelectedChannel = vm; Dirty = true;
        }
        private void RemoveChannel()
        {
            if (SelectedChannel == null) return;
            if (MessageBox.Show($"Remove channel '{SelectedChannel.Name}'?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Channels.Remove(SelectedChannel);
            SelectedChannel = Channels.FirstOrDefault(); Dirty = true;
        }
        private void DuplicateChannel()
        {
            if (SelectedChannel == null) return;
            var copy = ChannelVm.FromModel(SelectedChannel.ToModel());
            copy.Name = SelectedChannel.Name + " (copy)";
            Channels.Add(copy); SelectedChannel = copy; Dirty = true;
        }

        private void AddField()
        {
            if (SelectedChannel == null) return;
            var f = new FieldVm { Name = "Field" };
            f.SetPathSources(_invoicePaths, _linePaths); // pour l'auto-complétion
            SelectedChannel.Fields.Add(f); Dirty = true;
        }
        private void RemoveField()
        {
            if (SelectedChannel == null || SelectedField == null) return;
            SelectedChannel.Fields.Remove(SelectedField); Dirty = true;
        }

        private void Preview()
        {
            if (SelectedChannel == null) return;
            var sample = SelectedChannel.SampleXml;
            if (string.IsNullOrWhiteSpace(sample)) { PreviewOutput = "(add a sample XML for this channel)"; return; }
            try
            {
                var ch = SelectedChannel.ToModel();
                var warnings = new List<string>();
                string outp;
                if ((ch.OutputFormat ?? "Csv").ToLowerInvariant() == "xml")
                    outp = PipelineEngine.BuildXmlDocument(new List<string> { sample }, ch, warnings);
                else
                {
                    var sb = new StringBuilder();
                    if (ch.CsvFormat != null && ch.CsvFormat.WriteHeader)
                        sb.Append(PipelineEngine.CsvHeader(ch)).Append("\r\n");
                    PipelineEngine.AppendCsvRows(sb, sample, ch, warnings);
                    outp = sb.ToString();
                }
                if (warnings.Count > 0)
                    outp += "\r\n\r\n--- Warnings ---\r\n" + string.Join("\r\n", warnings);
                PreviewOutput = outp;
            }
            catch (System.Exception ex) { PreviewOutput = "Error: " + ex.Message; }
        }

        private bool ConfirmDiscard()
        {
            if (!Dirty) return true;
            return MessageBox.Show("Unsaved changes will be lost. Continue?", "Unsaved changes",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }
    }
}
