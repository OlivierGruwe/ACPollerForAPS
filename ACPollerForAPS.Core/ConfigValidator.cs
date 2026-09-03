using System;
using System.Collections.Generic;
using System.IO;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Validation de la configuration pipeline. Utilisée par l'UI avant
    /// enregistrement et réutilisable par le service au démarrage.
    /// Sépare erreurs (bloquantes) et avertissements.
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

            if (string.IsNullOrWhiteSpace(s.BuyerPath))
                r.Errors.Add("Buyer path (routing) is required.");
            if (string.IsNullOrWhiteSpace(s.RecordPath))
                r.Errors.Add("Global record path (for routing) is required.");

            if (s.Channels == null || s.Channels.Count == 0)
            {
                r.Errors.Add("At least one output channel is required.");
                return r;
            }

            var seenBuyers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ch in s.Channels)
            {
                string label = string.IsNullOrWhiteSpace(ch.Name) ? "(unnamed channel)" : ch.Name;

                if (string.IsNullOrWhiteSpace(ch.Name))
                    r.Errors.Add("A channel has no name.");

                if (!ch.Enabled)
                    r.Warnings.Add(string.Format("Channel '{0}' is disabled and will be ignored.", label));

                if (ch.Buyers == null || ch.Buyers.Count == 0)
                    r.Warnings.Add(string.Format("Channel '{0}' has no buyers: it will receive no files.", label));
                else
                {
                    foreach (var b in ch.Buyers)
                    {
                        if (string.IsNullOrWhiteSpace(b)) continue;
                        string other;
                        if (seenBuyers.TryGetValue(b, out other))
                            r.Errors.Add(string.Format(
                                "Buyer '{0}' is routed to two channels ('{1}' and '{2}'): ambiguous routing.", b, other, label));
                        else
                            seenBuyers[b] = label;
                    }
                }

                if (string.IsNullOrWhiteSpace(ch.OutputFolder))
                    r.Errors.Add(string.Format("Channel '{0}': output folder missing.", label));
                if (string.IsNullOrWhiteSpace(ch.OutputFileName))
                    r.Errors.Add(string.Format("Channel '{0}': output file name missing.", label));
                if (string.IsNullOrWhiteSpace(ch.RecordPath))
                    r.Errors.Add(string.Format("Channel '{0}': record path missing.", label));

                if (ch.BatchSize < 0)
                    r.Errors.Add(string.Format("Channel '{0}': invoices per file cannot be negative.", label));

                var fmt = (ch.OutputFormat ?? "").ToLowerInvariant();
                if (fmt != "csv" && fmt != "xml")
                    r.Errors.Add(string.Format("Channel '{0}': unknown output format '{1}' (Csv or Xml).", label, ch.OutputFormat));

                // --- provider ERP ---
                if (string.IsNullOrWhiteSpace(ch.Provider))
                    r.Warnings.Add(string.Format("Channel '{0}': no provider set, defaulting to 'mapping'.", label));
                else if (!string.Equals(ch.Provider, "mapping", StringComparison.OrdinalIgnoreCase))
                    r.Warnings.Add(string.Format(
                        "Channel '{0}': uses custom provider '{1}' — make sure the matching plugin DLL is deployed in the providers/ folder.",
                        label, ch.Provider));

                // --- transport de sortie ---
                var tr = ch.Transport;
                if (tr != null)
                {
                    var ttype = (tr.Type ?? "Fs").ToLowerInvariant();
                    if (ttype != "fs" && ttype != "ftps" && ttype != "s3")
                        r.Errors.Add(string.Format("Channel '{0}': unknown transport '{1}' (Fs, Ftps or S3).", label, tr.Type));

                    if (ttype == "fs" && string.IsNullOrWhiteSpace(ch.OutputFolder))
                        r.Errors.Add(string.Format("Channel '{0}': FS transport requires an output folder.", label));

                    if (ttype == "s3")
                    {
                        if (string.IsNullOrWhiteSpace(tr.S3Bucket))
                            r.Errors.Add(string.Format("Channel '{0}': S3 bucket is required.", label));
                        if (string.IsNullOrWhiteSpace(tr.S3AccessKey))
                            r.Errors.Add(string.Format("Channel '{0}': S3 access key is required.", label));
                        if (string.IsNullOrWhiteSpace(tr.S3Region) && string.IsNullOrWhiteSpace(tr.S3ServiceUrl))
                            r.Warnings.Add(string.Format("Channel '{0}': no S3 region nor endpoint set.", label));
                    }

                    if (ttype == "ftps")
                    {
                        if (string.IsNullOrWhiteSpace(tr.Host))
                            r.Errors.Add(string.Format("Channel '{0}': FTPS host is required.", label));
                        if (string.IsNullOrWhiteSpace(tr.Username))
                            r.Errors.Add(string.Format("Channel '{0}': FTPS username is required.", label));
                        if (tr.Port <= 0 || tr.Port > 65535)
                            r.Errors.Add(string.Format("Channel '{0}': invalid FTPS port.", label));
                        var mode = (tr.FtpsMode ?? "").ToLowerInvariant();
                        if (mode != "explicit" && mode != "implicit")
                            r.Errors.Add(string.Format("Channel '{0}': FTPS mode must be Explicit or Implicit.", label));
                        if (!tr.ValidateCertificate)
                            r.Warnings.Add(string.Format("Channel '{0}': FTPS certificate validation is OFF (test only).", label));
                    }

                    if (tr.RetryCount < 1)
                        r.Warnings.Add(string.Format("Channel '{0}': retry count < 1, no retry on failure.", label));
                }

                if (ch.Fields == null || ch.Fields.Count == 0)
                {
                    r.Warnings.Add(string.Format("Channel '{0}' has no mapped fields.", label));
                }
                else
                {
                    bool hasLine = false;
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in ch.Fields)
                    {
                        if (string.IsNullOrWhiteSpace(f.Name))
                        {
                            r.Errors.Add(string.Format("Channel '{0}': a field has no name.", label));
                            continue;
                        }
                        if (!names.Add(f.Name))
                            r.Warnings.Add(string.Format("Channel '{0}': duplicate field name '{1}'.", label, f.Name));

                        var src = (f.Source ?? "").ToLowerInvariant();
                        if (src == "line") hasLine = true;

                        if (src == "fixed" && f.Value == null)
                            r.Errors.Add(string.Format("Channel '{0}', field '{1}': fixed source without value.", label, f.Name));
                        if ((src == "header" || src == "xpath" || src == "line") && string.IsNullOrWhiteSpace(f.Path))
                            r.Errors.Add(string.Format("Channel '{0}', field '{1}': source {2} without path.", label, f.Name, src));
                        if (src != "fixed" && src != "header" && src != "xpath" && src != "line")
                            r.Errors.Add(string.Format("Channel '{0}', field '{1}': unknown source '{2}'.", label, f.Name, f.Source));
                        if (f.Type == "amount" && f.DecDigits < 0)
                            r.Errors.Add(string.Format("Channel '{0}', field '{1}': negative decimals.", label, f.Name));
                    }

                    if (hasLine && string.IsNullOrWhiteSpace(ch.LinesPath))
                        r.Errors.Add(string.Format("Channel '{0}': line fields exist but lines path is empty.", label));
                }
            }

            return r;
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
            if (s.Channels != null)
                foreach (var ch in s.Channels)
                    check(ch.OutputFolder, string.Format("Output folder for channel '{0}'", ch.Name));
        }
    }
}
