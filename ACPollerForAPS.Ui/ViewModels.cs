using System.Collections.ObjectModel;
using System.Linq;
using ACPollerForAPS.Core;

namespace PipelineConfigWpf
{
    // =====================================================================
    // Les ViewModels enveloppent le modèle de la DLL Core (PipelineSettings,
    // OutputChannel, PipelineField) en exposant des propriétés "bindables".
    //
    // On ne remplace PAS le modèle : on le décore. ToModel()/FromModel()
    // font l'aller-retour avec les classes Core que le service lit. Ainsi
    // l'UI reste jolie et réactive, mais ce qui est sauvegardé reste
    // exactement le modèle partagé.
    // =====================================================================

    /// <summary>ViewModel d'un champ de mapping (une ligne de la grille).</summary>
    public class FieldVm : ObservableObject
    {
        private string _name, _type = "text", _source = "line", _value, _path, _inFormat;
        private string _onlyWhenPath, _onlyWhenEquals;
        private int _decDigits = 2;
        private bool _absolute;

        public string Name { get => _name; set => Set(ref _name, value); }
        public string Type { get => _type; set => Set(ref _type, value); }
        public string Source
        {
            get => _source;
            set
            {
                if (Set(ref _source, value))
                {
                    bool isFixed = string.Equals(value, "fixed", System.StringComparison.OrdinalIgnoreCase);
                    if (isFixed) Path = "";
                    else Value = "";
                    RebuildPathOptions(); // la liste dépend de Source
                }
            }
        }

        private System.Collections.Generic.IEnumerable<string> _invoicePaths;
        private System.Collections.Generic.IEnumerable<string> _linePaths;

        // Vraie collection observable : le ComboBox se met à jour de façon
        // fiable quand on la repeuple (contrairement à une propriété calculée).
        public System.Collections.ObjectModel.ObservableCollection<string> PathOptions { get; }
            = new System.Collections.ObjectModel.ObservableCollection<string>();

        public void SetPathSources(System.Collections.Generic.IEnumerable<string> invoicePaths,
                                   System.Collections.Generic.IEnumerable<string> linePaths)
        {
            _invoicePaths = invoicePaths;
            _linePaths = linePaths;
            RebuildPathOptions();
        }

        /// <summary>Repeuple PathOptions selon la Source courante.</summary>
        private void RebuildPathOptions()
        {
            PathOptions.Clear();
            var s = (_source ?? "").ToLowerInvariant();
            System.Collections.Generic.IEnumerable<string> src = null;
            if (s == "line") src = _linePaths;
            else if (s == "header" || s == "xpath") src = _invoicePaths;
            // fixed => rien
            if (src != null)
                foreach (var p in src) PathOptions.Add(p);
        }
        public string Value { get => _value; set => Set(ref _value, value); }
        public string Path { get => _path; set => Set(ref _path, StripPrefix(value)); }

        // retire un préfixe d'auto-complétion "[inv] " / "[line] " si présent,
        // pour ne stocker que le vrai chemin.
        private static string StripPrefix(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            if (v.StartsWith("[inv] ")) return v.Substring(6);
            if (v.StartsWith("[line] ")) return v.Substring(7);
            return v;
        }
        public int DecDigits { get => _decDigits; set => Set(ref _decDigits, value); }
        public bool AbsoluteValue { get => _absolute; set => Set(ref _absolute, value); }
        public string InFormat { get => _inFormat; set => Set(ref _inFormat, value); }

        // écriture conditionnelle (lignes Vendor/Ledger) : le champ n'est écrit
        // que si le nœud à OnlyWhenPath (relatif à la ligne) vaut OnlyWhenEquals.
        public string OnlyWhenPath { get => _onlyWhenPath; set => Set(ref _onlyWhenPath, value); }
        public string OnlyWhenEquals { get => _onlyWhenEquals; set => Set(ref _onlyWhenEquals, value); }

        // listes pour les ComboBox de la grille
        public static string[] Types => new[] { "text", "amount", "date" };
        public static string[] Sources => new[] { "fixed", "header", "xpath", "line" };

        public static FieldVm FromModel(PipelineField f) => new FieldVm
        {
            Name = f.Name, Type = f.Type, Source = f.Source, Value = f.Value,
            Path = f.Path, DecDigits = f.DecDigits, AbsoluteValue = f.AbsoluteValue,
            InFormat = f.InFormat,
            OnlyWhenPath = f.OnlyWhenPath, OnlyWhenEquals = f.OnlyWhenEquals
        };

        public PipelineField ToModel() => new PipelineField
        {
            Name = Name, Type = Type, Source = Source, Value = Value,
            Path = Path, DecDigits = DecDigits, AbsoluteValue = AbsoluteValue,
            InFormat = InFormat,
            OnlyWhenPath = OnlyWhenPath, OnlyWhenEquals = OnlyWhenEquals
        };
    }

    /// <summary>ViewModel d'un canal de sortie.</summary>
    public class ChannelVm : ObservableObject
    {
        private string _name = "New channel", _outputFormat = "Csv",
            _outputFolder = "", _outputFileName = "{date}.csv",
            _recordPath = "/InvoiceData", _linesPath = "AccountingLines/AccountingLine",
            _buyersCsv = "", _sampleXml = "", _provider = "mapping";
        private int _batchSize;
        private bool _enabled = true;

        public string Name { get => _name; set { Set(ref _name, value); OnPropertyChanged(nameof(Display)); } }
        public bool Enabled { get => _enabled; set { Set(ref _enabled, value); OnPropertyChanged(nameof(Display)); } }
        public string BuyersCsv { get => _buyersCsv; set => Set(ref _buyersCsv, value); } // "A;B;C"
        public string OutputFormat { get => _outputFormat; set => Set(ref _outputFormat, value); }
        public string Provider { get => _provider; set => Set(ref _provider, value); }
        public string OutputFolder { get => _outputFolder; set => Set(ref _outputFolder, value); }
        public string OutputFileName { get => _outputFileName; set => Set(ref _outputFileName, value); }
        public string RecordPath { get => _recordPath; set => Set(ref _recordPath, value); }
        public string LinesPath { get => _linesPath; set => Set(ref _linesPath, value); }
        public int BatchSize { get => _batchSize; set => Set(ref _batchSize, value); }
        public string SampleXml { get => _sampleXml; set { Set(ref _sampleXml, value); OnPropertyChanged(nameof(SampleXmlStatus)); } }
        // indicateur affiché à côté du bouton Load dans Channel settings
        public string SampleXmlStatus => string.IsNullOrWhiteSpace(_sampleXml)
            ? "none loaded" : ("loaded (" + _sampleXml.Length + " chars)");

        public ObservableCollection<FieldVm> Fields { get; } = new ObservableCollection<FieldVm>();

        public static string[] Formats => new[] { "Csv", "Xml" };
        // providers connus proposés dans la liste ; saisie libre possible pour
        // les plugins DLL (dont le nom n'est connu que du service au runtime).
        public static string[] KnownProviders => new[] { "mapping" };

        /// <summary>Ce qui s'affiche dans la liste de gauche.</summary>
        public string Display => (Enabled ? "" : "○ ") + Name;

        // on conserve les formats CSV/XML tels quels (pas édités visuellement pour l'instant)
        private PipelineCsvFormat _csv = new PipelineCsvFormat();
        private PipelineXmlFormat _xml = new PipelineXmlFormat();
        public PipelineCsvFormat CsvFormat { get => _csv; set => _csv = value; }
        public PipelineXmlFormat XmlFormat { get => _xml; set => _xml = value; }

        // --- transport de sortie ---
        private string _transportType = "Fs";
        private string _host = "", _remoteFolder = "/", _username = "", _ftpsMode = "Explicit";
        private int _port = 21, _retryCount = 3, _retryDelayMs = 5000;
        private bool _validateCert = true;
        // mot de passe chiffré (DPAPI) tel que stocké ; jamais affiché en clair.
        private string _passwordEncrypted = "";

        public string TransportType { get => _transportType; set { Set(ref _transportType, value); OnPropertyChanged(nameof(IsFtps)); OnPropertyChanged(nameof(IsFs)); OnPropertyChanged(nameof(IsS3)); } }
        public bool IsFtps => string.Equals(TransportType, "Ftps", System.StringComparison.OrdinalIgnoreCase);
        public bool IsFs => string.Equals(TransportType, "Fs", System.StringComparison.OrdinalIgnoreCase);
        public bool IsS3 => string.Equals(TransportType, "S3", System.StringComparison.OrdinalIgnoreCase);
        public string Host { get => _host; set => Set(ref _host, value); }
        public int Port { get => _port; set => Set(ref _port, value); }
        public string RemoteFolder { get => _remoteFolder; set => Set(ref _remoteFolder, value); }
        public string Username { get => _username; set => Set(ref _username, value); }
        public string FtpsMode { get => _ftpsMode; set => Set(ref _ftpsMode, value); }
        public bool ValidateCertificate { get => _validateCert; set => Set(ref _validateCert, value); }
        public int RetryCount { get => _retryCount; set => Set(ref _retryCount, value); }
        public int RetryDelayMs { get => _retryDelayMs; set => Set(ref _retryDelayMs, value); }
        public string PasswordEncrypted { get => _passwordEncrypted; set => _passwordEncrypted = value; }

        public static string[] TransportTypes => new[] { "Fs", "Ftps", "S3" };
        public static string[] FtpsModes => new[] { "Explicit", "Implicit" };
        public bool HasStoredPassword => !string.IsNullOrEmpty(_passwordEncrypted);

        // --- S3 ---
        private string _s3Bucket = "", _s3KeyPrefix = "", _s3Region = "eu-west-1",
            _s3ServiceUrl = "", _s3AccessKey = "", _s3SecretEncrypted = "";
        private bool _s3ForcePathStyle;
        public string S3Bucket { get => _s3Bucket; set => Set(ref _s3Bucket, value); }
        public string S3KeyPrefix { get => _s3KeyPrefix; set => Set(ref _s3KeyPrefix, value); }
        public string S3Region { get => _s3Region; set => Set(ref _s3Region, value); }
        public string S3ServiceUrl { get => _s3ServiceUrl; set => Set(ref _s3ServiceUrl, value); }
        public bool S3ForcePathStyle { get => _s3ForcePathStyle; set => Set(ref _s3ForcePathStyle, value); }
        public string S3AccessKey { get => _s3AccessKey; set => Set(ref _s3AccessKey, value); }
        public string S3SecretKeyEncrypted { get => _s3SecretEncrypted; set => _s3SecretEncrypted = value; }

        public static ChannelVm FromModel(OutputChannel c)
        {
            var vm = new ChannelVm
            {
                Name = c.Name, Enabled = c.Enabled,
                BuyersCsv = c.Buyers != null ? string.Join(";", c.Buyers) : "",
                OutputFormat = c.OutputFormat, Provider = c.Provider, OutputFolder = c.OutputFolder,
                OutputFileName = c.OutputFileName, RecordPath = c.RecordPath,
                LinesPath = c.LinesPath, BatchSize = c.BatchSize, SampleXml = c.SampleXml, CsvFormat = c.CsvFormat, XmlFormat = c.XmlFormat
            };
            var t = c.Transport ?? new OutputTransport();
            vm.TransportType = t.Type;
            vm.Host = t.Host; vm.Port = t.Port; vm.RemoteFolder = t.RemoteFolder;
            vm.Username = t.Username; vm.PasswordEncrypted = t.PasswordEncrypted;
            vm.FtpsMode = t.FtpsMode; vm.ValidateCertificate = t.ValidateCertificate;
            vm.RetryCount = t.RetryCount; vm.RetryDelayMs = t.RetryDelayMs;
            vm.S3Bucket = t.S3Bucket; vm.S3KeyPrefix = t.S3KeyPrefix; vm.S3Region = t.S3Region;
            vm.S3ServiceUrl = t.S3ServiceUrl; vm.S3ForcePathStyle = t.S3ForcePathStyle;
            vm.S3AccessKey = t.S3AccessKey; vm.S3SecretKeyEncrypted = t.S3SecretKeyEncrypted;
            foreach (var f in c.Fields ?? new System.Collections.Generic.List<PipelineField>())
                vm.Fields.Add(FieldVm.FromModel(f));
            return vm;
        }

        public OutputChannel ToModel() => new OutputChannel
        {
            Name = Name, Enabled = Enabled,
            Buyers = BuyersCsv.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)
                              .Select(b => b.Trim()).Where(b => b.Length > 0).ToList(),
            OutputFormat = OutputFormat, Provider = Provider, OutputFolder = OutputFolder,
            OutputFileName = OutputFileName, RecordPath = RecordPath,
            LinesPath = LinesPath, BatchSize = BatchSize, SampleXml = SampleXml, CsvFormat = CsvFormat, XmlFormat = XmlFormat,
            Fields = Fields.Select(f => f.ToModel()).ToList(),
            Transport = new OutputTransport
            {
                Type = TransportType, Host = Host, Port = Port, RemoteFolder = RemoteFolder,
                Username = Username, PasswordEncrypted = PasswordEncrypted,
                FtpsMode = FtpsMode, ValidateCertificate = ValidateCertificate,
                RetryCount = RetryCount, RetryDelayMs = RetryDelayMs,
                S3Bucket = S3Bucket, S3KeyPrefix = S3KeyPrefix, S3Region = S3Region,
                S3ServiceUrl = S3ServiceUrl, S3ForcePathStyle = S3ForcePathStyle,
                S3AccessKey = S3AccessKey, S3SecretKeyEncrypted = S3SecretKeyEncrypted
            }
        };
    }
}
