using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Extrait les chemins de nœuds d'un XML de référence, pour alimenter
    /// l'auto-complétion du champ Path dans l'UI de mapping.
    ///
    /// Produit deux ensembles :
    ///   - InvoicePaths : chemins relatifs au record (source header/xpath)
    ///   - LinePaths    : chemins relatifs à une ligne comptable (source line)
    ///
    /// Les chemins sont relatifs (comme attendu par le moteur), dédupliqués
    /// et triés. Les feuilles (nœuds contenant du texte) sont prioritaires,
    /// mais les nœuds intermédiaires sont aussi proposés.
    /// </summary>
    public static class XmlPathExtractor
    {
        public class Result
        {
            public List<string> InvoicePaths { get; } = new List<string>();
            public List<string> LinePaths { get; } = new List<string>();
        }

        public static Result Extract(string xmlContent, string recordPath, string linesPath)
        {
            var res = new Result();
            if (string.IsNullOrWhiteSpace(xmlContent)) return res;

            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);
            var nav = doc.CreateNavigator();

            // --- chemins facture : depuis le premier record ---
            var record = SafeSelectSingle(nav, recordPath);
            if (record != null)
            {
                var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                Collect(record, "", set);
                res.InvoicePaths.AddRange(set);
            }

            // --- chemins ligne : depuis la première ligne comptable ---
            if (record != null && !string.IsNullOrWhiteSpace(linesPath))
            {
                var line = SafeSelectSingle(record, linesPath);
                if (line != null)
                {
                    var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    Collect(line, "", set);
                    res.LinePaths.AddRange(set);
                }
            }

            return res;
        }

        private static XPathNavigator SafeSelectSingle(XPathNavigator from, string xpath)
        {
            try { return from.SelectSingleNode(xpath); }
            catch { return null; }
        }

        /// <summary>Parcourt récursivement les éléments et accumule leurs chemins relatifs.</summary>
        private static void Collect(XPathNavigator node, string prefix, SortedSet<string> acc)
        {
            var children = node.SelectChildren(XPathNodeType.Element);
            while (children.MoveNext())
            {
                var child = children.Current;
                string name = child.Name;
                string path = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;

                acc.Add(path);

                // descendre (profondeur raisonnable pour éviter les XML géants)
                if (prefix.Length < 200)
                    Collect(child, path, acc);
            }
        }
    }
}
