using System.Collections.Generic;
using System.Text;
using ACPollerForAPS.Core;

namespace PipelineConfigWpf
{
    /// <summary>
    /// ViewModel d'UN pipeline (modèle FUSIONNÉ) : une entrée (dossier surveillé
    /// + record path + planification) + UNE sortie unique (format, transport,
    /// mapping). Plus de liste de canaux, plus de routage.
    /// La sortie est un ChannelVm (Output) réutilisant toute sa logique.
    /// </summary>
    public class PipelineVm : ObservableObject
    {
        private string _name = "Pipeline";
        private string _inputFolder = "", _archiveFolder = "", _errorFolder = "";
        private bool _archiveEnabled = true;
        private string _fileFilter = "*.xml";
        private int _stableCheckMs = 1000;
        private string _recordPath = "/InvoiceData";
        private int _intervalValue = 24;
        private string _intervalUnit = "Hours";
        private string _previewOutput = "";

        public System.Action MarkDirty { get; set; }
        private void Touch() { MarkDirty?.Invoke(); }

        public PipelineVm()
        {
            Output = new ChannelVm();
        }

        public string Name
        {
            get => _name;
            set { Set(ref _name, value); Touch(); OnPropertyChanged(nameof(Display)); }
        }
        public string Display => string.IsNullOrWhiteSpace(_name) ? "(unnamed)" : _name;

        // ---- entrée ----
        public string InputFolder { get => _inputFolder; set { Set(ref _inputFolder, value); Touch(); } }
        public string ArchiveFolder { get => _archiveFolder; set { Set(ref _archiveFolder, value); Touch(); } }
        public string ErrorFolder { get => _errorFolder; set { Set(ref _errorFolder, value); Touch(); } }
        public bool ArchiveEnabled { get => _archiveEnabled; set { Set(ref _archiveEnabled, value); Touch(); } }
        public string FileFilter { get => _fileFilter; set { Set(ref _fileFilter, value); Touch(); } }
        public int StableCheckMs { get => _stableCheckMs; set { Set(ref _stableCheckMs, value); Touch(); } }
        public string RecordPath { get => _recordPath; set { Set(ref _recordPath, value); Touch(); } }

        public int IntervalValue { get => _intervalValue; set { Set(ref _intervalValue, value); Touch(); OnPropertyChanged(nameof(IntervalPreview)); } }
        public string IntervalUnit { get => _intervalUnit; set { Set(ref _intervalUnit, value); Touch(); OnPropertyChanged(nameof(IntervalPreview)); } }
        public string[] IntervalUnits => new[] { "Minutes", "Hours" };
        public string IntervalPreview
        {
            get
            {
                long sec = (IntervalUnit == "Minutes" ? IntervalValue * 60L : IntervalValue * 3600L);
                return $"→ one run every {sec} seconds.";
            }
        }

        // ---- sortie unique ----
        public ChannelVm Output { get; private set; }

        public string PreviewOutput { get => _previewOutput; set => Set(ref _previewOutput, value); }

        private readonly List<string> _invoicePaths = new List<string>();
        private readonly List<string> _linePaths = new List<string>();

        public void LoadReferencePaths(string xmlContent)
        {
            _invoicePaths.Clear();
            _linePaths.Clear();
            if (Output == null || string.IsNullOrWhiteSpace(xmlContent)) { PushPathsToFields(); return; }
            try
            {
                var res = XmlPathExtractor.Extract(xmlContent, Output.RecordPath, Output.LinesPath);
                foreach (var p in res.InvoicePaths) _invoicePaths.Add(p);
                foreach (var p in res.LinePaths) _linePaths.Add(p);
                PushPathsToFields();
            }
            catch { }
        }

        private void PushPathsToFields()
        {
            if (Output == null) return;
            foreach (var f in Output.Fields)
                f.SetPathSources(_invoicePaths, _linePaths);
        }

        public FieldVm SelectedField { get; set; }

        public void AddField()
        {
            if (Output == null) return;
            var f = new FieldVm { Name = "Field" };
            f.SetPathSources(_invoicePaths, _linePaths);
            Output.Fields.Add(f); Touch();
        }
        public void RemoveField()
        {
            if (Output == null || SelectedField == null) return;
            Output.Fields.Remove(SelectedField); Touch();
        }

        public void Preview()
        {
            if (Output == null) return;
            var sample = Output.SampleXml;
            if (string.IsNullOrWhiteSpace(sample)) { PreviewOutput = "(add a sample XML)"; return; }
            try
            {
                var ch = Output.ToModel();
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

        public static PipelineVm FromModel(PipelineSettings s)
        {
            var vm = new PipelineVm
            {
                _name = s.Name,
                _inputFolder = s.InputFolder, _archiveFolder = s.ArchiveFolder, _errorFolder = s.ErrorFolder,
                _archiveEnabled = s.ArchiveEnabled, _fileFilter = s.FileFilter, _stableCheckMs = s.StableCheckMs,
                _recordPath = s.RecordPath,
                _intervalValue = s.Schedule?.IntervalValue ?? 24,
                _intervalUnit = s.Schedule?.IntervalUnit ?? "Hours"
            };
            vm.Output = ChannelVm.FromModel(s.Output ?? new OutputChannel());
            return vm;
        }

        public PipelineSettings ToModel() => new PipelineSettings
        {
            Name = Name,
            InputFolder = InputFolder, ArchiveFolder = ArchiveFolder, ErrorFolder = ErrorFolder,
            ArchiveEnabled = ArchiveEnabled, FileFilter = FileFilter, StableCheckMs = StableCheckMs,
            RecordPath = RecordPath,
            Schedule = new PipelineSchedule { IntervalValue = IntervalValue, IntervalUnit = IntervalUnit },
            Output = Output.ToModel()
        };
    }
}
