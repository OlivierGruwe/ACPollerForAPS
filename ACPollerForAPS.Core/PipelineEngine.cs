using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Moteur du pipeline : routage par Buyer + génération de la sortie
    /// (CSV ou XML) selon le canal. Sans état. Identique en logique à celui
    /// de l'UI PipelineConfig, pour qu'aperçu et production coïncident.
    /// </summary>
    public static class PipelineEngine
    {
        /// <summary>Lit la valeur de Buyer d'un XML (pour le routage).</summary>
        public static string ReadBuyer(string xmlContent, PipelineSettings s, out string error)
        {
            error = null;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xmlContent);
                var nav = doc.CreateNavigator();
                var record = nav.SelectSingleNode(s.RecordPath);
                if (record == null) { error = "RecordPath introuvable : " + s.RecordPath; return null; }
                var b = record.SelectSingleNode(s.BuyerPath);
                return b?.Value?.Trim() ?? "";
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static OutputChannel SelectChannel(PipelineSettings s, string buyer)
        {
            if (s.Channels == null) return null;
            foreach (var ch in s.Channels)
            {
                if (!ch.Enabled || ch.Buyers == null) continue;
                foreach (var b in ch.Buyers)
                    if (string.Equals(b, buyer, StringComparison.OrdinalIgnoreCase))
                        return ch;
            }
            return null;
        }

        // ---- CSV : concatène les lignes de plusieurs fichiers dans un buffer ----

        /// <summary>Ajoute au StringBuilder les lignes CSV d'un document.</summary>
        public static void AppendCsvRows(StringBuilder sb, string xmlContent,
            OutputChannel ch, List<string> warnings)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);
            var nav = doc.CreateNavigator();
            var f = ch.CsvFormat ?? new PipelineCsvFormat();

            var records = nav.Select(ch.RecordPath);
            while (records.MoveNext())
            {
                var record = records.Current;
                var lines = record.Select(ch.LinesPath);
                bool any = false;
                while (lines.MoveNext())
                {
                    any = true;
                    sb.Append(BuildCsvRow(record, lines.Current, ch, f, warnings)).Append("\r\n");
                }
                if (!any && ch.WriteRecordEvenIfNoLines)
                    sb.Append(BuildCsvRow(record, null, ch, f, warnings)).Append("\r\n");
            }
        }

        public static string CsvHeader(OutputChannel ch)
        {
            var f = ch.CsvFormat ?? new PipelineCsvFormat();
            var heads = new List<string>();
            foreach (var col in ch.Fields) heads.Add(EscapeCsv(col.Name ?? "", f));
            return string.Join(f.Delimiter, heads);
        }

        private static string BuildCsvRow(XPathNavigator record, XPathNavigator line,
            OutputChannel ch, PipelineCsvFormat f, List<string> warnings)
        {
            var cells = new List<string>();
            foreach (var col in ch.Fields)
            {
                string raw = Resolve(record, line, col, warnings, ch.Name);
                string val = FormatValue(raw, col, f.DecimalSeparator, f.DateFormat);
                cells.Add(EscapeCsv(val, f));
            }
            return string.Join(f.Delimiter, cells);
        }

        // ---- XML : accumule les <Invoice> dans un document de sortie ----

        public static string BuildXmlDocument(List<string> xmlContents,
            OutputChannel ch, List<string> warnings)
        {
            var xf = ch.XmlFormat ?? new PipelineXmlFormat();
            var outDoc = new XmlDocument();
            var root = outDoc.CreateElement(xf.RootElement);
            outDoc.AppendChild(root);

            foreach (var xml in xmlContents)
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var nav = doc.CreateNavigator();
                var records = nav.Select(ch.RecordPath);
                while (records.MoveNext())
                {
                    var record = records.Current;
                    var recEl = outDoc.CreateElement(xf.RecordElement);
                    root.AppendChild(recEl);
                    var lines = record.Select(ch.LinesPath);
                    while (lines.MoveNext())
                    {
                        var lineEl = outDoc.CreateElement(xf.LineElement);
                        recEl.AppendChild(lineEl);
                        foreach (var col in ch.Fields)
                        {
                            string raw = Resolve(record, lines.Current, col, warnings, ch.Name);
                            string val = FormatValue(raw, col, xf.DecimalSeparator, xf.DateFormat);
                            var el = outDoc.CreateElement(SafeElementName(col.Name));
                            el.InnerText = val;
                            lineEl.AppendChild(el);
                        }
                    }
                }
            }

            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            var swb = new StringBuilder();
            using (var w = XmlWriter.Create(swb, settings))
                outDoc.Save(w);
            return swb.ToString();
        }

        private static string SafeElementName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Field";
            var sb = new StringBuilder();
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            var s = sb.ToString();
            if (!char.IsLetter(s[0]) && s[0] != '_') s = "_" + s;
            return s;
        }

        // ---- résolution + formatage ----

        private static string Resolve(XPathNavigator record, XPathNavigator line,
            PipelineField col, List<string> warnings, string channelName)
        {
            // écriture conditionnelle : si OnlyWhen est défini et non satisfait,
            // le champ reste vide (colonne présente mais cellule vide).
            if (!string.IsNullOrWhiteSpace(col.OnlyWhenPath))
            {
                string actual = "";
                if (line != null)
                {
                    var cond = line.SelectSingleNode(col.OnlyWhenPath);
                    actual = cond?.Value ?? "";
                }
                if (!string.Equals(actual.Trim(), (col.OnlyWhenEquals ?? "").Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return "";
            }

            string value = "";
            try
            {
                switch ((col.Source ?? "").ToLowerInvariant())
                {
                    case "fixed": value = col.Value ?? ""; break;
                    case "header":
                    case "xpath":
                        var n = record.SelectSingleNode(col.Path);
                        value = n?.Value ?? "";
                        break;
                    case "line":
                        if (line != null)
                        {
                            var l = line.SelectSingleNode(col.Path);
                            value = l?.Value ?? "";
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                warnings.Add(string.Format("[{0}] champ '{1}': {2}", channelName, col.Name, ex.Message));
            }
            return ApplyValues(value, col);
        }

        private static string ApplyValues(string raw, PipelineField col)
        {
            if (col.Values == null || col.Values.Count == 0 || raw == null) return raw;
            foreach (var kv in col.Values)
                if (string.Equals(kv.Key, raw.Trim(), StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            return raw;
        }

        private static string FormatValue(string raw, PipelineField col, string decSep, string dateFmt)
        {
            string type = (col.Type ?? "text").ToLowerInvariant();

            if (type == "amount")
            {
                double val = 0;
                if (!string.IsNullOrWhiteSpace(raw))
                    double.TryParse(raw.Replace(",", "."), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out val);
                if (col.AbsoluteValue) val = Math.Abs(val);
                string s = val.ToString("F" + col.DecDigits, CultureInfo.InvariantCulture);
                return s.Replace(".", decSep ?? ".");
            }

            if (type == "date")
            {
                if (string.IsNullOrWhiteSpace(raw)) return "";
                DateTime dt; var ci = CultureInfo.InvariantCulture; var st = DateTimeStyles.None;
                if (!string.IsNullOrWhiteSpace(col.InFormat) &&
                    DateTime.TryParseExact(raw.Trim(), col.InFormat, ci, st, out dt))
                    return dt.ToString(dateFmt, ci);
                if (DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd", ci, st, out dt))
                    return dt.ToString(dateFmt, ci);
                if (DateTime.TryParseExact(raw.Trim(), "yyyyMMdd", ci, st, out dt))
                    return dt.ToString(dateFmt, ci);
                if (DateTime.TryParse(raw.Trim(), ci, st, out dt))
                    return dt.ToString(dateFmt, ci);
                return raw.Trim();
            }

            return raw ?? "";
        }

        private static string EscapeCsv(string value, PipelineCsvFormat f)
        {
            value = value ?? "";
            bool q = f.QuoteAllFields
                || (!string.IsNullOrEmpty(f.Delimiter) && value.Contains(f.Delimiter))
                || (!string.IsNullOrEmpty(f.Quote) && value.Contains(f.Quote))
                || value.Contains("\n") || value.Contains("\r");
            if (!q || string.IsNullOrEmpty(f.Quote)) return value;
            return f.Quote + value.Replace(f.Quote, f.Quote + f.Quote) + f.Quote;
        }
    }
}
