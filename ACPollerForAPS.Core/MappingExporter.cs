using System.Collections.Generic;
using System.Text;

namespace ACPollerForAPS.Core
{
    /// <summary>
    /// Provider par défaut, piloté par la configuration (le mapping du canal).
    /// Couvre tous les ERP "standards" convertibles en CSV ou XML sans code
    /// spécifique. C'est ce provider qui est utilisé quand OutputChannel.Provider
    /// vaut "mapping" (ou est vide).
    ///
    /// Il réutilise PipelineEngine, donc l'aperçu de l'UI et la production
    /// restent strictement identiques.
    /// </summary>
    public class MappingExporter : IErpExporter
    {
        public const string Name = "mapping";

        public IEnumerable<string> ProviderNames { get { return new[] { Name }; } }

        public ExportResult Export(IEnumerable<string> inputXmls, OutputChannel channel)
        {
            var result = new ExportResult();
            var fmt = (channel.OutputFormat ?? "Csv").ToLowerInvariant();

            string text;
            if (fmt == "xml")
            {
                var list = new List<string>(inputXmls);
                text = PipelineEngine.BuildXmlDocument(list, channel, result.Warnings);
            }
            else
            {
                var sb = new StringBuilder();
                if (channel.CsvFormat != null && channel.CsvFormat.WriteHeader)
                    sb.Append(PipelineEngine.CsvHeader(channel)).Append("\r\n");
                foreach (var xml in inputXmls)
                    PipelineEngine.AppendCsvRows(sb, xml, channel, result.Warnings);
                text = sb.ToString();
            }

            result.Content = new UTF8Encoding(false).GetBytes(text);
            return result;
        }
    }
}
