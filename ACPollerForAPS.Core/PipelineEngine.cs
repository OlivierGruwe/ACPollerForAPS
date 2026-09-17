using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using System.Text.RegularExpressions;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Moteur du pipeline : génération de la sortie (CSV ou XML) selon le canal.
    /// Sans état. Le routage par Buyer a été retiré (un pipeline = une sortie).
    /// </summary>
    public static class PipelineEngine
    {
        // Regex de neutralisation des namespaces (déclarations xmlns + préfixes).
        private static readonly Regex RxXmlns = new Regex("\\s+xmlns(:\\w+)?=\"[^\"]*\"", RegexOptions.Compiled);
        private static readonly Regex RxPrefix = new Regex("(</?)\\w+:", RegexOptions.Compiled);

        /// <summary>
        /// Charge un XML en NEUTRALISANT les namespaces. De nombreux formats ERP
        /// (ex. Comarch Optima : xmlns="http://www.cdn.com.pl/optima/dokument")
        /// déclarent un namespace par défaut qui empêcherait les XPath simples
        /// (NAGLOWEK/NUMER_PELNY...) de matcher. On retire donc les déclarations
        /// xmlns et les préfixes avant chargement, pour que les chemins de la
        /// config restent simples et lisibles.
        /// </summary>
        private static XmlDocument LoadXmlNoNamespace(string xml)
        {
            if (!string.IsNullOrEmpty(xml))
            {
                xml = RxXmlns.Replace(xml, string.Empty);
                xml = RxPrefix.Replace(xml, "$1");
            }
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc;
        }

        // ---- CSV : concatène les lignes de plusieurs fichiers dans un buffer ----

        /// <summary>Ajoute au StringBuilder les lignes CSV d'un document.</summary>
        public static void AppendCsvRows(StringBuilder sb, string xmlContent,
            OutputChannel ch, List<string> warnings)
        {
            var doc = LoadXmlNoNamespace(xmlContent);
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
            var ns = xf.Namespace ?? "";
            var outDoc = new XmlDocument();

            // élément racine, avec namespace par défaut si défini
            var root = CreateEl(outDoc, xf.RootElement, ns);
            outDoc.AppendChild(root);

            // séparation header (record) / line
            var headerFields = new List<PipelineField>();
            var lineFields = new List<PipelineField>();
            foreach (var col in ch.Fields ?? new List<PipelineField>())
            {
                if (string.Equals(col.Source, "line", StringComparison.OrdinalIgnoreCase))
                    lineFields.Add(col);
                else
                    headerFields.Add(col);
            }

            foreach (var xml in xmlContents)
            {
                var doc = LoadXmlNoNamespace(xml);
                var nav = doc.CreateNavigator();
                var records = nav.Select(ch.RecordPath);
                while (records.MoveNext())
                {
                    var record = records.Current;

                    // conteneur du record (ex. DOKUMENT)
                    var recEl = CreateEl(outDoc, xf.RecordElement, ns);
                    root.AppendChild(recEl);

                    // champs header : écrits à leur chemin imbriqué SOUS le record
                    foreach (var col in headerFields)
                    {
                        string raw;
                        // source "sum" : somme d'un chemin de ligne sur toutes les
                        // lignes du record (ex. RAZEM_NETTO = somme des WARTOSC_NETTO)
                        if (string.Equals(col.Source, "sum", StringComparison.OrdinalIgnoreCase))
                            raw = ResolveSum(record, ch, col);
                        else
                            raw = Resolve(record, null, col, warnings, ch.Name);
                        string val = FormatValue(raw, col, xf.DecimalSeparator, xf.DateFormat);
                        var leaf = EnsurePath(outDoc, recEl, col.Name, ns);
                        if (leaf != null) leaf.InnerText = val;
                    }

                    // conteneur optionnel des lignes (ex. POZYCJE)
                    XmlElement linesParent = recEl;
                    if (!string.IsNullOrWhiteSpace(xf.LineWrapper))
                    {
                        linesParent = CreateEl(outDoc, xf.LineWrapper, ns);
                        recEl.AppendChild(linesParent);
                    }

                    // une ligne de sortie par ligne comptable de l'entrée
                    var lines = record.Select(ch.LinesPath);
                    int lineIndex = 0;
                    while (lines.MoveNext())
                    {
                        lineIndex++;
                        var lineEl = CreateEl(outDoc, xf.LineElement, ns);
                        linesParent.AppendChild(lineEl);
                        foreach (var col in lineFields)
                        {
                            string raw;
                            // Path spécial "#index" : numéro de ligne (1,2,3...) —
                            // utile pour un champ type LP/numéro de position.
                            if (string.Equals(col.Path, "#index", StringComparison.OrdinalIgnoreCase))
                                raw = lineIndex.ToString(CultureInfo.InvariantCulture);
                            else
                                raw = Resolve(record, lines.Current, col, warnings, ch.Name);
                            string val = FormatValue(raw, col, xf.DecimalSeparator, xf.DateFormat);
                            var leaf = EnsurePath(outDoc, lineEl, col.Name, ns);
                            if (leaf != null) leaf.InnerText = val;
                        }
                    }
                }
            }

            // Sérialisation avec l'encodage CONFIGURÉ (xf.Encoding, défaut UTF-8).
            // Écrire vers un StringBuilder forcerait UTF-16 (un StringBuilder .NET
            // est de l'UTF-16 en mémoire, l'encodage des settings serait ignoré).
            // On passe donc par un MemoryStream avec le bon Encoding, pour obtenir
            // une déclaration <?xml ... encoding="utf-8"?> conforme.
            var enc = ResolveEncoding(xf.Encoding);
            var settings = new XmlWriterSettings { Indent = true, Encoding = enc };
            using (var ms = new System.IO.MemoryStream())
            {
                using (var w = XmlWriter.Create(ms, settings))
                    outDoc.Save(w);
                return enc.GetString(ms.ToArray());
            }
        }

        // Résout un nom d'encodage ("UTF-8", "UTF-16", "windows-1250"...) en
        // Encoding ; défaut = UTF-8 sans BOM. Un nom inconnu retombe sur UTF-8.
        private static Encoding ResolveEncoding(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new UTF8Encoding(false);
            try
            {
                var n = name.Trim().ToLowerInvariant();
                if (n == "utf-8" || n == "utf8") return new UTF8Encoding(false);
                return Encoding.GetEncoding(name);
            }
            catch { return new UTF8Encoding(false); }
        }

        // Crée un élément, dans le namespace ns s'il est non vide.
        private static XmlElement CreateEl(XmlDocument doc, string name, string ns)
        {
            var n = SafeElementName(name);
            return string.IsNullOrWhiteSpace(ns)
                ? doc.CreateElement(n)
                : doc.CreateElement(n, ns);
        }

        // Construit (ou réutilise) l'arborescence décrite par un chemin de sortie
        // "A/B/C" sous 'parent', et retourne l'élément feuille (C) où poser la
        // valeur. Les parents partagés entre plusieurs champs sont réutilisés.
        private static XmlElement EnsurePath(XmlDocument doc, XmlElement parent, string path, string ns)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var parts = path.Split('/');
            var current = parent;
            foreach (var part in parts)
            {
                var name = SafeElementName(part.Trim());
                if (string.IsNullOrEmpty(name)) continue;
                // réutilise un enfant existant de même nom, sinon le crée
                XmlElement child = null;
                foreach (XmlNode c in current.ChildNodes)
                {
                    if (c is XmlElement e && e.LocalName == name) { child = e; break; }
                }
                if (child == null)
                {
                    child = CreateEl(doc, name, ns);
                    current.AppendChild(child);
                }
                current = child;
            }
            return current;
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

        /// <summary>
        /// Somme un chemin de ligne (col.Path, relatif à la ligne) sur TOUTES les
        /// lignes du record courant. Respecte OnlyWhen (permet des totaux
        /// conditionnels, ex. somme des seules lignes Debit). Retourne la somme
        /// en invariant (le formatage décimal/absolu est fait ensuite par
        /// FormatValue, comme pour un montant normal).
        /// </summary>
        private static string ResolveSum(XPathNavigator record, OutputChannel ch, PipelineField col)
        {
            double total = 0;
            if (record == null || string.IsNullOrWhiteSpace(ch.LinesPath) || string.IsNullOrWhiteSpace(col.Path))
                return "0";
            var lines = record.Select(ch.LinesPath);
            while (lines.MoveNext())
            {
                var line = lines.Current;

                // OnlyWhen : n'additionne que les lignes qui satisfont la condition
                if (!string.IsNullOrWhiteSpace(col.OnlyWhenPath))
                {
                    var cond = line.SelectSingleNode(col.OnlyWhenPath);
                    var actual = cond?.Value ?? "";
                    if (!string.Equals(actual.Trim(), (col.OnlyWhenEquals ?? "").Trim(),
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var n = line.SelectSingleNode(col.Path);
                var raw = n?.Value;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                double v;
                if (double.TryParse(raw.Replace(",", "."), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out v))
                    total += v;
            }
            // valeur brute invariante ; FormatValue appliquera décimales/Abs/séparateur
            return total.ToString(CultureInfo.InvariantCulture);
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