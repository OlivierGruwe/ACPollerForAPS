using System;
using System.Collections.Generic;
using System.Linq;

namespace PipelineConfigWpf
{
    /// <summary>
    /// Assistant d'aide hors-ligne (sans IA) : associe la question de
    /// l'utilisateur à la meilleure entrée de FAQ par mots-clés. Entièrement
    /// local — aucun appel réseau. Le contenu reflète le produit actuel
    /// (pipelines, bidirectionnel, chemins imbriqués, sommes, dashboard).
    /// </summary>
    public class HelpBot
    {
        public class Entry
        {
            public string[] Keywords;
            public string Answer;
        }

        private static readonly List<Entry> _entries = new List<Entry>
        {
            new Entry {
                Keywords = new[]{"pipeline","what","concept","overview","start"},
                Answer = "A pipeline is one input folder → one transformation → one output. "
                       + "Each pipeline is autonomous (its own folder, schedule and output). "
                       + "Run several side by side, e.g. one per direction (APS→Optima and Optima→APS). "
                       + "Add or select pipelines with the selector at the top of the window."
            },
            new Entry {
                Keywords = new[]{"add","create","new","pipeline"},
                Answer = "Use the pipeline selector bar at the top: Add creates a new pipeline, "
                       + "Copy duplicates the current one, Remove deletes it. Then fill in the "
                       + "General, Schedule and Output tabs for the selected pipeline."
            },
            new Entry {
                Keywords = new[]{"output","path","nested","element","imbriqu","tree","xml element"},
                Answer = "In the mapping grid, the 'Output path' is the element path in the result. "
                       + "For nested XML use '/', e.g. NAGLOWEK/SPRZEDAWCA/NIP — parent nodes are "
                       + "created and shared automatically. Two fields sharing a prefix reuse the same parent."
            },
            new Entry {
                Keywords = new[]{"namespace","xmlns","comarch","optima"},
                Answer = "Set the output namespace in the XML format section (Namespace / xmlns). "
                       + "For Comarch Optima use http://www.cdn.com.pl/optima/dokument. When reading "
                       + "a namespaced input, namespaces are neutralized automatically so your paths stay simple."
            },
            new Entry {
                Keywords = new[]{"sum","total","aggregate","razem","addition","somme"},
                Answer = "To total line values into a header total (e.g. RAZEM_NETTO), set the field "
                       + "Source to 'sum' and its Path to a line node (e.g. WARTOSC_NETTO). It adds that "
                       + "value across all lines of the record. It respects 'Only when', so you can total "
                       + "a subset of lines (e.g. only debit lines)."
            },
            new Entry {
                Keywords = new[]{"line","number","index","lp","position"},
                Answer = "For a line number (1,2,3…), add a 'line' field whose Path is the special "
                       + "value #index. It outputs the current line's position."
            },
            new Entry {
                Keywords = new[]{"wrapper","lines","pozycje","container","group lines"},
                Answer = "The 'Line wrapper' (XML format section) is an optional container around all "
                       + "lines, e.g. POZYCJE wraps the POZYCJA elements. Leave it empty to add lines "
                       + "directly under the record element."
            },
            new Entry {
                Keywords = new[]{"only when","condition","vendor","ledger","credit","debit"},
                Answer = "Conditional write: set 'Only when (path)' and 'equals' on a field so it is "
                       + "written only when a line node matches (e.g. PostingType = Debit). Otherwise the "
                       + "field stays empty. Useful for Vendor/Ledger style lines."
            },
            new Entry {
                Keywords = new[]{"source","fixed","header","line","xpath","mapping"},
                Answer = "Field Source: 'fixed' = a constant (Value); 'header'/'xpath' = a value from the "
                       + "record; 'line' = a value from the current line; 'sum' = a total over the lines. "
                       + "The Path is the input XPath; suggestions are filtered by Source."
            },
            new Entry {
                Keywords = new[]{"amount","decimal","date","format","absolute"},
                Answer = "Type 'amount' formats numbers (decimals, and Abs drops the sign). Type 'date' "
                       + "reparses using 'Date in' (the input format) and outputs in the XML/CSV date format. "
                       + "Decimal separator and date format are set in the XML/CSV format section."
            },
            new Entry {
                Keywords = new[]{"encoding","utf","charset"},
                Answer = "Set the output Encoding in the XML format section (UTF-8 by default). This "
                       + "controls the <?xml ... encoding=\"...\"?> declaration of the generated file."
            },
            new Entry {
                Keywords = new[]{"transport","ftps","s3","deliver","destination","folder"},
                Answer = "Each pipeline delivers through a transport: FS (a folder), FTPS, or S3 "
                       + "(incl. MinIO/S3-compatible). Configure it in the Output transport section, with "
                       + "retry count and delay. Credentials are encrypted (DPAPI) on the target machine."
            },
            new Entry {
                Keywords = new[]{"pending","retry","failed","delivery","queue","perdu","lost"},
                Answer = "If a delivery fails after all retries, the generated file is queued under "
                       + "pending/<pipeline>/ and the sources are archived normally — nothing is lost. "
                       + "The service retries the queue at the start of every run, so delivery completes "
                       + "as soon as the target is reachable again."
            },
            new Entry {
                Keywords = new[]{"preview","test","result","output"},
                Answer = "Load a Sample XML in the Output tab, then click Preview to see the generated "
                       + "result. Refresh paths re-scans the sample for path auto-completion. Validate "
                       + "(toolbar) checks every pipeline before saving."
            },
            new Entry {
                Keywords = new[]{"dashboard","stats","monitor","volume","error rate","history"},
                Answer = "The Dashboard tab shows an operations overview from the run history in stats/: "
                       + "summary cards (runs, files, delivered, pending, error rate), a files-per-day chart, "
                       + "and a per-pipeline table. Choose 7/30/90 days and Refresh. It fills in as the "
                       + "service processes files."
            },
            new Entry {
                Keywords = new[]{"provider","plugin","dll","custom","erp"},
                Answer = "Most targets use the default 'mapping' provider (no code). For special logic, "
                       + "drop a DLL implementing IErpExporter into the providers/ folder next to the "
                       + "service and set the pipeline's Provider to its name. See PROVIDERS.md."
            },
            new Entry {
                Keywords = new[]{"service","start","stop","restart","install","1064"},
                Answer = "Manage the service with 'sc start ACPollerForAPS' / 'sc stop ACPollerForAPS'. "
                       + "Restart it after changing settings.json (it reads config at startup). "
                       + "If it won't start (error 1064), check settings.json exists next to the exe and the logs."
            },
            new Entry {
                Keywords = new[]{"save","settings","config","json","file"},
                Answer = "The UI and the service share one settings.json ({ \"Pipelines\": [...] }). "
                       + "Save writes it; the service must be restarted to apply changes. Validate checks it first."
            },
            new Entry {
                Keywords = new[]{"log","logs","nlog","diagnostic","debug"},
                Answer = "Logs are in logs/ next to the service exe (rotated and purged). For diagnostics, "
                       + "set the level to Debug in NLog.config, reproduce, then set it back to Info."
            },
            new Entry {
                Keywords = new[]{"event","monitoring","nagios","zabbix","supervision"},
                Answer = "The service writes to a dedicated Windows event log 'ACPollerForAPS' "
                       + "(source ACPollerForAPS.Service): start/stop, run summaries, warnings and errors. "
                       + "Enterprise monitors (Nagios, Zabbix, Centreon) read it natively."
            }
        };

        public static List<string> SuggestedQuestions => new List<string>
        {
            "What is a pipeline?",
            "How do I output nested XML elements?",
            "How do I set the Optima namespace?",
            "How do I total line amounts (sums)?",
            "What happens if a delivery fails?",
            "What does the Dashboard show?",
            "How do I use a custom ERP provider?"
        };

        private const string Fallback =
            "I'm a simple offline assistant. Try keywords like: pipeline, output path, namespace, "
          + "sum, transport, pending, preview, dashboard, provider, service, logs. For full details, "
          + "open the Manual (toolbar).";

        /// <summary>Retourne la meilleure réponse pour la question posée.</summary>
        public static string Answer(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return Fallback;
            var q = question.ToLowerInvariant();
            Entry best = null; int bestScore = 0;
            foreach (var e in _entries)
            {
                int score = e.Keywords.Count(k => q.Contains(k.ToLowerInvariant()));
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best != null && bestScore > 0 ? best.Answer : Fallback;
        }
    }
}