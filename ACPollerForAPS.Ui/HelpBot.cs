using System;
using System.Collections.Generic;
using System.Linq;

namespace PipelineConfigWpf
{
    /// <summary>
    /// Offline help assistant (no AI, no network). A hand-written Q/A knowledge
    /// base plus a keyword matcher. Extend freely by adding entries to Entries.
    /// </summary>
    public static class HelpBot
    {
        public class Entry
        {
            public string Title;              // sample question (shown as suggestion)
            public string[] Keywords;         // keywords used for matching
            public string Answer;             // the answer
        }

        // Shown when nothing matches
        public const string Fallback =
            "I don't have a precise answer to that. Try rephrasing, or pick one of "
            + "the frequently asked questions below. You can also check the product "
            + "README for details.";

        public static readonly List<Entry> Entries = new List<Entry>
        {
            new Entry {
                Title = "What is Buyer routing for?",
                Keywords = new[] { "routing", "buyer", "route", "channel", "dispatch" },
                Answer =
                    "Each input XML carries a Buyer value (its path is set by 'Buyer path' "
                    + "in the General tab). The service reads it and sends the invoice to the "
                    + "channel whose 'Buyers' list contains that value. This is how a single "
                    + "input flow is dispatched to different ERPs (Dynamics, Optima…)."
            },
            new Entry {
                Title = "What does 'Invoices per file' (BatchSize) do?",
                Keywords = new[] { "batch", "invoices per file", "split", "number", "invoices", "file", "per file" },
                Answer =
                    "It's the max number of invoices per output file, PER CHANNEL. "
                    + "0 = all invoices of the channel in a single file. "
                    + "Example: 5 with 30 invoices => 6 output files of 5 invoices each, "
                    + "generated in the same run."
            },
            new Entry {
                Title = "How do I configure FTPS delivery?",
                Keywords = new[] { "ftps", "ftp", "transport", "deliver", "delivery", "server", "upload" },
                Answer =
                    "In the channel, under 'Output transport', set Destination = Ftps. "
                    + "Fill in host, port (21 explicit / 990 implicit), remote folder, username "
                    + "and password. The password is encrypted (DPAPI) and never stored in clear "
                    + "in the file. Use 'Test connection' to verify. Important: encrypt the "
                    + "password ON THE MACHINE where the service runs."
            },
            new Entry {
                Title = "How does S3 delivery work?",
                Keywords = new[] { "s3", "aws", "minio", "bucket", "cloud", "amazon" },
                Answer =
                    "Under 'Output transport', Destination = S3. Fill in the bucket, region "
                    + "(or a custom endpoint for MinIO / S3-compatible), the access key and the "
                    + "secret key (DPAPI-encrypted). 'Path-style access' is required for MinIO. "
                    + "'Test connection' checks access to the bucket."
            },
            new Entry {
                Title = "What does the mapping 'Source' column mean?",
                Keywords = new[] { "source", "source column", "fixed", "header", "line", "xpath", "where value", "comes from" },
                Answer =
                    "Source tells where a field's value comes from:\n"
                    + "• fixed: a constant value (in Value)\n"
                    + "• header / xpath: a node of the invoice (path in Path)\n"
                    + "• line: a node of the current accounting line (path in Path)\n"
                    + "The Path suggestion list is filtered by Source."
            },
            new Entry {
                Title = "How do I handle Vendor and Ledger lines (Dynamics)?",
                Keywords = new[] { "vendor", "ledger", "conditional", "only when", "debit", "credit", "dynamics", "postingtype" },
                Answer =
                    "Use the 'Only when (path)' and 'equals' columns in the mapping. A field is "
                    + "written only when a node of the line (e.g. PostingType) equals a value "
                    + "(e.g. Credit). Example: the Credit column -> Only when PostingType = Credit; "
                    + "the Debit column -> Only when PostingType = Debit. This produces one Vendor "
                    + "line and N Ledger lines with different columns."
            },
            new Entry {
                Title = "What is the 'Provider' field and how do I build a DLL?",
                Keywords = new[] { "provider", "plugin", "dll", "library", "extension", "ierpexporter", "specific", "custom" },
                Answer =
                    "'Provider' decides HOW the output file is generated for a channel:\n\n"
                    + "• 'mapping' (default): uses the configurable field mapping in the UI. "
                    + "Enough for most ERPs (CSV/XML describable by the field table).\n\n"
                    + "• a custom provider: for an ERP with special logic (aggregations, unusual "
                    + "structure), you supply a dedicated DLL. You then put its name in this field.\n\n"
                    + "Building a provider DLL:\n"
                    + "1. New 'class library' project (.NET 4.8) referencing ACPollerForAPS.Core.\n"
                    + "2. Create a class implementing the IErpExporter interface:\n"
                    + "   - IEnumerable<string> ProviderNames => new[] { \"myerp\" };  (name(s) to use in the Provider field)\n"
                    + "   - ExportResult Export(IEnumerable<string> inputXmls, OutputChannel channel);  (produces the file)\n"
                    + "3. In Export, fill result.Content (byte[] of the file) and optionally result.Warnings.\n"
                    + "4. Compile the DLL and drop it in the 'providers/' folder next to the service exe.\n"
                    + "5. The service discovers it automatically at startup (a broken DLL is logged and ignored).\n\n"
                    + "The provider only handles content generation: Buyer routing, merge, transport "
                    + "(FS/FTPS/S3) and archiving stay common. See PROVIDERS.md."
            },
            new Entry {
                Title = "How do I test my configuration?",
                Keywords = new[] { "test", "preview", "check", "verify", "validate" },
                Answer =
                    "Load a 'Sample XML' into the channel (button in Channel settings), then click "
                    + "'Preview': you see the generated output file. The 'Validate' button (toolbar) "
                    + "checks the whole configuration and reports errors and warnings before saving."
            },
            new Entry {
                Title = "Does the service pick up my changes automatically?",
                Keywords = new[] { "service", "restart", "reload", "change", "apply", "pick up" },
                Answer =
                    "No: the service reads settings.json once, at startup. After a change made "
                    + "through this UI, RESTART the service (sc stop / sc start, or the Services "
                    + "manager) so it picks up the new configuration."
            },
            new Entry {
                Title = "Where are the logs and how do I read them?",
                Keywords = new[] { "log", "logs", "journal", "error", "diagnostic", "trace" },
                Answer =
                    "Logs are in the 'logs/' folder next to the service exe. Each run writes a start "
                    + "line, per-file detail, and a summary (delivered, errors…). For detailed "
                    + "diagnostics, set the log level to 'Debug' in NLog.config (no recompilation), "
                    + "then switch back to 'Info'."
            },
            new Entry {
                Title = "How often does the service process files?",
                Keywords = new[] { "schedule", "interval", "how often", "when", "frequency", "rate", "24h" },
                Answer =
                    "Schedule tab: the service runs at a regular interval (minutes or hours). "
                    + "Each run processes everything present. Note: a 24h interval means one run "
                    + "per day — for testing, set a few minutes."
            },
        };

        /// <summary>Finds the best answer to a question (keyword matching).</summary>
        public static string Answer(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return Fallback;
            var q = Normalize(question);

            Entry best = null;
            int bestScore = 0;
            foreach (var e in Entries)
            {
                int score = 0;
                foreach (var kw in e.Keywords)
                    if (q.Contains(Normalize(kw))) score++;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best != null && bestScore > 0 ? best.Answer : Fallback;
        }

        private static string Normalize(string s)
        {
            s = (s ?? "").ToLowerInvariant();
            // strip common accents for tolerant matching (FR users may type accents)
            s = s.Replace("é", "e").Replace("è", "e").Replace("ê", "e")
                 .Replace("à", "a").Replace("â", "a").Replace("ô", "o")
                 .Replace("û", "u").Replace("î", "i").Replace("ç", "c");
            return s;
        }

        /// <summary>Titles, to offer clickable suggested questions.</summary>
        public static IEnumerable<string> SuggestedQuestions => Entries.Select(e => e.Title);
    }
}
