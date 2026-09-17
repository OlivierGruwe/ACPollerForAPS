using System;
using System.Collections.Generic;
using System.IO;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Validation d'un pipeline (modèle fusionné : une entrée + une sortie unique).
    /// Utilisée par l'UI avant enregistrement et par le service au démarrage.
    /// </summary>
    public static class ConfigValidator
    {
        public class Result
        {
            public List<string> Errors { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
            public bool IsValid { get { return Errors.Count == 0; } }
        }

        public static Result Validate(PipelineSettings s)
        {
            var r = new Result();
            if (s == null) { r.Errors.Add("Null configuration."); return r; }

            if (string.IsNullOrWhiteSpace(s.InputFolder))
                r.Errors.Add("Input folder is required.");
            if (s.ArchiveEnabled && string.IsNullOrWhiteSpace(s.ArchiveFolder))
                r.Warnings.Add("Archiving enabled but no archive folder set.");

            if (s.Schedule == null)
                r.Errors.Add("Schedule is missing.");
            else if (s.Schedule.IntervalValue <= 0)
                r.Errors.Add("Run interval must be greater than 0.");

            if (string.IsNullOrWhiteSpace(s.RecordPath))
                r.Errors.Add("Record path is required.");

            var ch = s.Output;
            if (ch == null) { r.Errors.Add("The pipeline has no output."); return r; }

            ValidateOutput(ch, r);
            return r;
        }

        private static void ValidateOutput(OutputChannel ch, Result r)
        {
            if (string.IsNullOrWhiteSpace(ch.OutputFileName))
                r.Errors.Add("Output file name missing.");
            if (string.IsNullOrWhiteSpace(ch.RecordPath))
                r.Errors.Add("Output record path missing.");
            if (ch.BatchSize < 0)
                r.Errors.Add("Invoices per file cannot be negative.");

            var fmt = (ch.OutputFormat ?? "").ToLowerInvariant();
            if (fmt != "csv" && fmt != "xml")
                r.Errors.Add(string.Format("Unknown output format '{0}' (Csv or Xml).", ch.OutputFormat));

            if (string.IsNullOrWhiteSpace(ch.Provider))
                r.Warnings.Add("No provider set, defaulting to 'mapping'.");
            else if (!string.Equals(ch.Provider, "mapping", StringComparison.OrdinalIgnoreCase))
                r.Warnings.Add(string.Format(
                    "Uses custom provider '{0}' — make sure the matching plugin DLL is deployed in the providers/ folder.", ch.Provider));

            var tr = ch.Transport;
            if (tr != null)
            {
                var ttype = (tr.Type ?? "Fs").ToLowerInvariant();
                if (ttype != "fs" && ttype != "ftps" && ttype != "s3")
                    r.Errors.Add(string.Format("Unknown transport '{0}' (Fs, Ftps or S3).", tr.Type));

                if (ttype == "fs" && string.IsNullOrWhiteSpace(ch.OutputFolder))
                    r.Errors.Add("FS transport requires an output folder.");

                if (ttype == "s3")
                {
                    if (string.IsNullOrWhiteSpace(tr.S3Bucket)) r.Errors.Add("S3 bucket is required.");
                    if (string.IsNullOrWhiteSpace(tr.S3AccessKey)) r.Errors.Add("S3 access key is required.");
                    if (string.IsNullOrWhiteSpace(tr.S3Region) && string.IsNullOrWhiteSpace(tr.S3ServiceUrl))
                        r.Warnings.Add("No S3 region nor endpoint set.");
                }

                if (ttype == "ftps")
                {
                    if (string.IsNullOrWhiteSpace(tr.Host)) r.Errors.Add("FTPS host is required.");
                    if (string.IsNullOrWhiteSpace(tr.Username)) r.Errors.Add("FTPS username is required.");
                    if (tr.Port <= 0 || tr.Port > 65535) r.Errors.Add("Invalid FTPS port.");
                    var mode = (tr.FtpsMode ?? "").ToLowerInvariant();
                    if (mode != "explicit" && mode != "implicit")
                        r.Errors.Add("FTPS mode must be Explicit or Implicit.");
                    if (!tr.ValidateCertificate)
                        r.Warnings.Add("FTPS certificate validation is OFF (test only).");
                }

                if (tr.RetryCount < 1)
                    r.Warnings.Add("Retry count < 1, no retry on failure.");
            }

            if (ch.Fields == null || ch.Fields.Count == 0)
            {
                r.Warnings.Add("No mapped fields.");
            }
            else
            {
                bool hasLine = false;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in ch.Fields)
                {
                    if (string.IsNullOrWhiteSpace(f.Name))
                    {
                        r.Errors.Add("A field has no name.");
                        continue;
                    }
                    if (!names.Add(f.Name))
                        r.Warnings.Add(string.Format("Duplicate field name '{0}'.", f.Name));

                    var src = (f.Source ?? "").ToLowerInvariant();
                    if (src == "line" || src == "sum") hasLine = true;

                    if (src == "fixed" && f.Value == null)
                        r.Errors.Add(string.Format("Field '{0}': fixed source without value.", f.Name));
                    if ((src == "header" || src == "xpath" || src == "line" || src == "sum") && string.IsNullOrWhiteSpace(f.Path))
                        r.Errors.Add(string.Format("Field '{0}': source {1} without path.", f.Name, src));
                    if (src != "fixed" && src != "header" && src != "xpath" && src != "line" && src != "sum")
                        r.Errors.Add(string.Format("Field '{0}': unknown source '{1}'.", f.Name, f.Source));
                    if (f.Type == "amount" && f.DecDigits < 0)
                        r.Errors.Add(string.Format("Field '{0}': negative decimals.", f.Name));
                }

                if (hasLine && string.IsNullOrWhiteSpace(ch.LinesPath))
                    r.Errors.Add("Line/sum fields exist but lines path is empty.");
            }
        }

        /// <summary>Contrôle d'existence des répertoires (avertissements).</summary>
        public static void CheckFolders(PipelineSettings s, Result r)
        {
            Action<string, string> check = (path, what) =>
            {
                if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
                    r.Warnings.Add(string.Format("{0} not found on this machine: {1}", what, path));
            };
            check(s.InputFolder, "Input folder");
            if (s.ArchiveEnabled) check(s.ArchiveFolder, "Archive folder");
            if (s.Output != null) check(s.Output.OutputFolder, "Output folder");
        }
    }
}