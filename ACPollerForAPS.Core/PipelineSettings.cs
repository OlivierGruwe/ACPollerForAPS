using System.Collections.Generic;

namespace ACPollerForAPS.Core
{
    // =====================================================================
    // Modèle de configuration du pipeline multi-canaux — CLASSES PARTAGÉES.
    //
    // Cette DLL (ACPollerForAPS.Core) est référencée à la fois par le service
    // (ACPollerForAPS) et par l'UI de configuration (PipelineConfig). Il n'y a
    // donc qu'UNE seule définition de ces classes : aucune divergence possible
    // entre ce que l'UI écrit et ce que le service lit.
    //
    // Fusionné dans settings.json sous la clé "Pipeline".
    // =====================================================================

    /// <summary>
    /// Racine de configuration multi-pipelines : { "Pipelines": [ {...} ] }.
    /// Chaque pipeline est autonome (dossier d'entrée + une sortie) et tourne
    /// indépendamment — permet les flux bidirectionnels (APS->Optima, Optima->APS).
    /// </summary>
    public class AppConfig
    {
        public List<PipelineSettings> Pipelines { get; set; } = new List<PipelineSettings>();
    }

    public class PipelineSettings
    {
        // nom du pipeline (affiché dans l'UI et les logs)
        public string Name { get; set; } = "Pipeline";

        // répertoires communs
        public string InputFolder { get; set; } = "";
        public string ArchiveFolder { get; set; } = "";
        public string ErrorFolder { get; set; } = "";
        public bool ArchiveEnabled { get; set; } = true;

        // détection / lecture
        public string FileFilter { get; set; } = "*.xml";
        public int StableCheckMs { get; set; } = 1000;

        // chemin du record (une facture) dans le XML d'entrée
        public string RecordPath { get; set; } = "/InvoiceData";

        // planification : intervalle entre deux passages de merge
        public PipelineSchedule Schedule { get; set; } = new PipelineSchedule();

        // sortie unique du pipeline (un pipeline = une conversion = une destination)
        public OutputChannel Output { get; set; } = new OutputChannel();
    }

    public class PipelineSchedule
    {
        public int IntervalValue { get; set; } = 24;
        public string IntervalUnit { get; set; } = "Hours"; // Minutes | Hours

        public int ToSeconds()
        {
            switch ((IntervalUnit ?? "Hours").ToLowerInvariant())
            {
                case "minutes": return IntervalValue * 60;
                case "hours": return IntervalValue * 3600;
                default: return IntervalValue * 3600;
            }
        }
    }

    public class OutputChannel
    {
        public string Name { get; set; } = "New channel";
        public bool Enabled { get; set; } = true;
        public List<string> Buyers { get; set; } = new List<string>();
        public string OutputFormat { get; set; } = "Csv";   // Csv | Xml

        // provider ERP : "mapping" (défaut, config-driven) ou le nom déclaré
        // par une DLL plugin déposée dans providers/ pour un ERP spécifique.
        public string Provider { get; set; } = "mapping";
        public string OutputFolder { get; set; } = "";
        public string OutputFileName { get; set; } = "{date}.csv";
        public string RecordPath { get; set; } = "/InvoiceData";
        public string LinesPath { get; set; } = "AccountingLines/AccountingLine";
        public bool WriteRecordEvenIfNoLines { get; set; } = false;

        // taille de lot pour CE canal : nb max de factures par fichier de sortie.
        // 0 = toutes les factures du canal dans un seul fichier (merge complet).
        // Ex. 5 avec 30 factures => 6 fichiers de sortie de 5.
        public int BatchSize { get; set; } = 0;
        public PipelineCsvFormat CsvFormat { get; set; } = new PipelineCsvFormat();
        public PipelineXmlFormat XmlFormat { get; set; } = new PipelineXmlFormat();
        public List<PipelineField> Fields { get; set; } = new List<PipelineField>();

        // XML d'entrée type pour ce canal : sert à la prévisualisation ET à
        // l'auto-complétion des chemins (Path) du mapping. Stocké dans la config
        // pour que le canal soit autonome/portable.
        public string SampleXml { get; set; } = "";

        // destination de sortie : FS (OutputFolder) ou FTPS. Si null => FS
        // vers OutputFolder (rétro-compatible).
        public OutputTransport Transport { get; set; } = new OutputTransport();
    }

    public class PipelineCsvFormat
    {
        public string Delimiter { get; set; } = ",";
        public string Quote { get; set; } = "\"";
        public bool QuoteAllFields { get; set; } = false;
        public string DecimalSeparator { get; set; } = ",";
        public string DateFormat { get; set; } = "dd.MM.yyyy";
        public bool WriteHeader { get; set; } = false;
        public string Encoding { get; set; } = "UTF-8";
    }

    public class PipelineXmlFormat
    {
        public string RootElement { get; set; } = "Invoices";
        public string RecordElement { get; set; } = "Invoice";
        public string LineElement { get; set; } = "Line";
        public string DecimalSeparator { get; set; } = ".";
        public string DateFormat { get; set; } = "yyyy-MM-dd";
        public string Encoding { get; set; } = "UTF-8";

        // namespace par défaut (xmlns) posé sur l'élément racine. Vide = aucun.
        // Ex. Comarch Optima : http://www.cdn.com.pl/optima/dokument
        public string Namespace { get; set; } = "";

        // conteneur optionnel autour des lignes (ex. Optima : "POZYCJE" enveloppe
        // les POZYCJA). Vide = les LineElement sont ajoutés directement au record.
        public string LineWrapper { get; set; } = "";
    }

    public class PipelineField
    {
        public string Name { get; set; }
        public string Type { get; set; } = "text";     // text | amount | date
        public string Source { get; set; } = "line";   // fixed | header | xpath | line | sum
        public string Value { get; set; }
        public string Path { get; set; }
        public int DecDigits { get; set; } = 2;
        public bool AbsoluteValue { get; set; } = false;
        public string InFormat { get; set; }
        public Dictionary<string, string> Values { get; set; }

        // --- écriture conditionnelle (niveau 2 : lignes Vendor/Ledger différentes) ---
        // Le champ n'est écrit QUE si le nœud à OnlyWhenPath (relatif à la ligne
        // comptable) vaut OnlyWhenEquals. Sinon la cellule est vide.
        // Ex. colonne "Debit" : OnlyWhenPath="PostingType", OnlyWhenEquals="Debit".
        // Vide/null => champ toujours écrit (comportement normal).
        public string OnlyWhenPath { get; set; }
        public string OnlyWhenEquals { get; set; }

        public PipelineField Clone()
        {
            return new PipelineField
            {
                Name = Name, Type = Type, Source = Source, Value = Value,
                Path = Path, DecDigits = DecDigits, AbsoluteValue = AbsoluteValue,
                InFormat = InFormat,
                Values = Values == null ? null : new Dictionary<string, string>(Values)
            };
        }
    }
}
