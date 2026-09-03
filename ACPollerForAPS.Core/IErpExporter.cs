using System.Collections.Generic;

namespace ACPollerForAPS.Core
{
    // =====================================================================
    // Contrat des "providers ERP" (mécanisme de plugin).
    //
    // Chaque ERP dont la transformation est SPÉCIFIQUE fournit une DLL
    // implémentant IErpExporter, déposée dans le dossier "providers/" à la
    // racine du module. Le service la découvre au démarrage.
    //
    // Les ERP "standards" (mappables en CSV/XML par simple configuration)
    // n'ont PAS besoin de DLL : ils passent par le provider par défaut
    // "mapping" (MappingExporter), piloté par le JSON.
    //
    // Un canal indique quel provider utiliser via OutputChannel.Provider
    // ("mapping" par défaut, ou le nom déclaré par une DLL plugin).
    // =====================================================================

    /// <summary>Résultat d'une transformation : le contenu du fichier à déposer.</summary>
    public class ExportResult
    {
        /// <summary>Contenu binaire prêt à être déposé (le transport s'occupe du dépôt).</summary>
        public byte[] Content { get; set; }
        /// <summary>Avertissements non bloquants (champ manquant, etc.).</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Un provider ERP transforme un lot de XML d'entrée (déjà routés vers ce
    /// canal) en un fichier de sortie unique. Implémenté par le provider par
    /// défaut (mapping) et par d'éventuelles DLL spécifiques.
    /// </summary>
    public interface IErpExporter
    {
        /// <summary>
        /// Nom(s) sous lesquels ce provider s'enregistre. Un canal référence
        /// l'un de ces noms via sa propriété Provider. Insensible à la casse.
        /// </summary>
        IEnumerable<string> ProviderNames { get; }

        /// <summary>
        /// Transforme les XML d'entrée en un fichier de sortie, selon la
        /// configuration du canal (format, mapping, etc.).
        /// </summary>
        ExportResult Export(IEnumerable<string> inputXmls, OutputChannel channel);
    }
}
