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
    /// ViewModel principal (modèle fusionné) : gère la LISTE de pipelines.
    /// Chaque pipeline = une entrée + une sortie unique (Output).
    /// Les propriétés de niveau pipeline (entrée) et de niveau sortie sont des
    /// FAÇADES vers le pipeline sélectionné et son Output — le XAML se lie à
    /// ces façades, ce qui évite de re-brancher tous les bindings.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private string _currentPath;
        private string _status = "Ready.";
        private bool _dirty;
        private PipelineVm _selectedPipeline;

        public System.Action BeforeSave { get; set; }

        public MainViewModel()
        {
            NewCommand = new RelayCommand(New);
            OpenCommand = new RelayCommand(Open);
            SaveCommand = new RelayCommand(() => Save(false));
            SaveAsCommand = new RelayCommand(() => Save(true));
            ValidateCommand = new RelayCommand(Validate);

            AddPipelineCommand = new RelayCommand(AddPipeline);
            RemovePipelineCommand = new RelayCommand(RemovePipeline, () => SelectedPipeline != null);
            DuplicatePipelineCommand = new RelayCommand(DuplicatePipeline, () => SelectedPipeline != null);

            AddFieldCommand = new RelayCommand(() => SelectedPipeline?.AddField(), () => SelectedPipeline != null);
            RemoveFieldCommand = new RelayCommand(() => SelectedPipeline?.RemoveField(), () => SelectedField != null);
            PreviewCommand = new RelayCommand(DoPreview, () => SelectedPipeline != null);

            TryAutoLoad();
            Dashboard = new DashboardVm();
        }

        // dashboard d'exploitation (lit stats/ à côté de l'exe)
        public DashboardVm Dashboard { get; private set; }

        private void TryAutoLoad()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                var path = System.IO.Path.Combine(dir, "settings.json");
                if (!System.IO.File.Exists(path)) { EnsureAtLeastOnePipeline(); return; }
                LoadConfig(ConfigStore.Load(path));
                _currentPath = path;
                OnPropertyChanged(nameof(Title));
                Status = "Auto-loaded: " + path;
            }
            catch (System.Exception ex)
            {
                Status = "Could not auto-load settings.json: " + ex.Message;
                EnsureAtLeastOnePipeline();
            }
        }

        // ---- état global ----
        public string Status { get => _status; set => Set(ref _status, value); }
        public bool Dirty { get => _dirty; set { Set(ref _dirty, value); OnPropertyChanged(nameof(Title)); } }
        public string Title => "Pipeline configuration" +
            (_currentPath != null ? " — " + _currentPath : " — (unsaved)") + (Dirty ? " *" : "");

        // ---- liste de pipelines ----
        public ObservableCollection<PipelineVm> Pipelines { get; } = new ObservableCollection<PipelineVm>();

        public PipelineVm SelectedPipeline
        {
            get => _selectedPipeline;
            set
            {
                Set(ref _selectedPipeline, value);
                WirePipeline(value);
                if (value != null) value.LoadReferencePaths(value.Output?.SampleXml);
                RaiseFacades();
            }
        }

        private void WirePipeline(PipelineVm p)
        {
            if (p == null) return;
            p.MarkDirty = () => Dirty = true;
        }

        private void RaiseFacades()
        {
            foreach (var n in new[] {
                nameof(InputFolder), nameof(ArchiveFolder), nameof(ErrorFolder), nameof(ArchiveEnabled),
                nameof(FileFilter), nameof(StableCheckMs), nameof(RecordPath),
                nameof(IntervalValue), nameof(IntervalUnit), nameof(IntervalPreview),
                nameof(Output), nameof(HasPipeline), nameof(PreviewOutput) })
                OnPropertyChanged(n);
        }

        // ---- façades ENTRÉE (vers SelectedPipeline) ----
        private PipelineVm P => _selectedPipeline;
        public bool HasPipeline => P != null;

        public string InputFolder { get => P?.InputFolder; set { if (P != null) P.InputFolder = value; } }
        public string ArchiveFolder { get => P?.ArchiveFolder; set { if (P != null) P.ArchiveFolder = value; } }
        public string ErrorFolder { get => P?.ErrorFolder; set { if (P != null) P.ErrorFolder = value; } }
        public bool ArchiveEnabled { get => P?.ArchiveEnabled ?? true; set { if (P != null) P.ArchiveEnabled = value; } }
        public string FileFilter { get => P?.FileFilter; set { if (P != null) P.FileFilter = value; } }
        public int StableCheckMs { get => P?.StableCheckMs ?? 1000; set { if (P != null) P.StableCheckMs = value; } }
        public string RecordPath { get => P?.RecordPath; set { if (P != null) P.RecordPath = value; } }
        public int IntervalValue { get => P?.IntervalValue ?? 24; set { if (P != null) P.IntervalValue = value; } }
        public string IntervalUnit { get => P?.IntervalUnit ?? "Hours"; set { if (P != null) P.IntervalUnit = value; } }
        public string[] IntervalUnits => new[] { "Minutes", "Hours" };
        public string IntervalPreview => P?.IntervalPreview ?? "";

        // ---- façade SORTIE (le XAML se lie à Output.*) ----
        public ChannelVm Output => P?.Output;

        public string PreviewOutput
        {
            get => P?.PreviewOutput;
            set { if (P != null) P.PreviewOutput = value; OnPropertyChanged(nameof(PreviewOutput)); }
        }
        public FieldVm SelectedField
        {
            get => P?.SelectedField;
            set { if (P != null) P.SelectedField = value; }
        }

        public void LoadReferencePaths(string xmlContent) => P?.LoadReferencePaths(xmlContent);

        // exécute la preview sur le pipeline courant PUIS notifie la façade
        // PreviewOutput (le XAML lit MainViewModel.PreviewOutput, pas celui du VM pipeline)
        private void DoPreview()
        {
            SelectedPipeline?.Preview();
            OnPropertyChanged(nameof(PreviewOutput));
        }

        // ---- commands ----
        public RelayCommand NewCommand { get; }
        public RelayCommand OpenCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand SaveAsCommand { get; }
        public RelayCommand ValidateCommand { get; }
        public RelayCommand AddPipelineCommand { get; }
        public RelayCommand RemovePipelineCommand { get; }
        public RelayCommand DuplicatePipelineCommand { get; }
        public RelayCommand AddFieldCommand { get; }
        public RelayCommand RemoveFieldCommand { get; }
        public RelayCommand PreviewCommand { get; }

        // ---- config <-> vm ----
        private void LoadConfig(AppConfig config)
        {
            Pipelines.Clear();
            foreach (var p in config?.Pipelines ?? new List<PipelineSettings>())
                Pipelines.Add(PipelineVm.FromModel(p));
            EnsureAtLeastOnePipeline();
            SelectedPipeline = Pipelines.FirstOrDefault();
            Dirty = false;
        }

        private AppConfig BuildConfig() => new AppConfig
        {
            Pipelines = Pipelines.Select(p => p.ToModel()).ToList()
        };

        private void EnsureAtLeastOnePipeline()
        {
            if (Pipelines.Count == 0)
            {
                var p = PipelineVm.FromModel(new PipelineSettings { Name = "Pipeline 1" });
                Pipelines.Add(p);
                SelectedPipeline = p;
            }
        }

        private void AddPipeline()
        {
            var p = PipelineVm.FromModel(new PipelineSettings { Name = "Pipeline " + (Pipelines.Count + 1) });
            Pipelines.Add(p); SelectedPipeline = p; Dirty = true;
        }
        private void RemovePipeline()
        {
            if (SelectedPipeline == null) return;
            if (MessageBox.Show($"Remove pipeline '{SelectedPipeline.Name}'?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Pipelines.Remove(SelectedPipeline);
            EnsureAtLeastOnePipeline();
            SelectedPipeline = Pipelines.FirstOrDefault();
            Dirty = true;
        }
        private void DuplicatePipeline()
        {
            if (SelectedPipeline == null) return;
            var copy = PipelineVm.FromModel(SelectedPipeline.ToModel());
            copy.Name = SelectedPipeline.Name + " (copy)";
            Pipelines.Add(copy); SelectedPipeline = copy; Dirty = true;
        }

        // ---- file actions ----
        private void New()
        {
            if (!ConfirmDiscard()) return;
            Pipelines.Clear();
            EnsureAtLeastOnePipeline();
            SelectedPipeline = Pipelines.FirstOrDefault();
            _currentPath = null; OnPropertyChanged(nameof(Title));
            Dirty = false; Status = "New configuration.";
        }

        private void Open()
        {
            if (!ConfirmDiscard()) return;
            var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                LoadConfig(ConfigStore.Load(dlg.FileName));
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
            var config = BuildConfig();

            var errors = new List<string>();
            var warnings = new List<string>();
            foreach (var p in config.Pipelines)
            {
                var r = ConfigValidator.Validate(p);
                errors.AddRange(r.Errors.Select(e => $"[{p.Name}] {e}"));
                warnings.AddRange(r.Warnings.Select(w => $"[{p.Name}] {w}"));
            }
            if (errors.Count > 0)
            {
                MessageBox.Show("The configuration has errors and will not be saved:\n\n"
                    + string.Join("\n", errors), "Validation failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (warnings.Count > 0)
            {
                var r = MessageBox.Show("Warnings:\n\n" + string.Join("\n", warnings)
                    + "\n\nSave anyway?", "Warnings", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r != MessageBoxResult.Yes) return;
            }

            if (saveAs || string.IsNullOrEmpty(_currentPath))
            {
                var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = _currentPath ?? "settings.json" };
                if (dlg.ShowDialog() != true) return;
                _currentPath = dlg.FileName;
            }
            try
            {
                ConfigStore.Save(_currentPath, config);
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
            var config = BuildConfig();
            var errors = new List<string>();
            var warnings = new List<string>();
            foreach (var p in config.Pipelines)
            {
                var r = ConfigValidator.Validate(p);
                ConfigValidator.CheckFolders(p, r);
                errors.AddRange(r.Errors.Select(e => $"[{p.Name}] {e}"));
                warnings.AddRange(r.Warnings.Select(w => $"[{p.Name}] {w}"));
            }
            if (errors.Count == 0 && warnings.Count == 0)
            {
                MessageBox.Show("Configuration valid, no issues.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Status = "Validation OK."; return;
            }
            var sb = new StringBuilder();
            if (errors.Count > 0) sb.AppendLine("ERRORS:").AppendLine(string.Join("\n", errors)).AppendLine();
            if (warnings.Count > 0) sb.AppendLine("WARNINGS:").AppendLine(string.Join("\n", warnings));
            MessageBox.Show(sb.ToString(), "Validation result", MessageBoxButton.OK,
                errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Status = errors.Count == 0 ? "Valid, with warnings." : "Invalid.";
        }

        private bool ConfirmDiscard()
        {
            if (!Dirty) return true;
            return MessageBox.Show("Unsaved changes will be lost. Continue?", "Unsaved changes",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }
    }
}
