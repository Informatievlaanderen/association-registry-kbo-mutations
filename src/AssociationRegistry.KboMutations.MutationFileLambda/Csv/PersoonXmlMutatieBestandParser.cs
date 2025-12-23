using System.Diagnostics;
using System.Xml.Linq;
using System.Xml.XPath;
using AssocationRegistry.KboMutations.Models;
using AssociationRegistry.KboMutations.Telemetry;

namespace AssociationRegistry.KboMutations.MutationFileLambda.Csv;

public class PersoonXmlMutatieBestandParser : IPersoonXmlMutatieBestandParser
{
    private readonly KboMutationsMetrics? _metrics;

    public PersoonXmlMutatieBestandParser(KboMutationsMetrics? metrics = null)
    {
        _metrics = metrics;
    }

    public IEnumerable<PersoonMutatieLijn> ParseMutatieLijnen(string xmlContent)
    {
        var stopwatch = Stopwatch.StartNew();
        var parsedCount = 0;
        var skippedCount = 0;

        var document = XDocument.Parse(xmlContent);
        var navigator = document.CreateNavigator();

        var persoonNodes = navigator.Select("//Persoon");

        while (persoonNodes?.MoveNext() == true)
        {
            var persoonNode = persoonNodes.Current;
            if (persoonNode == null)
            {
                skippedCount++;
                continue;
            }

            var inszNode = persoonNode.SelectSingleNode("INSZ");
            if (inszNode == null || string.IsNullOrWhiteSpace(inszNode.Value))
            {
                skippedCount++;
                _metrics?.RecordRecordsSkipped("persoon", 1, "missing_insz");
                continue;
            }

            var insz = inszNode.Value.Trim();
            var overlijdenNode = persoonNode.SelectSingleNode("Overlijden");
            var overleden = overlijdenNode != null;

            parsedCount++;
            yield return new PersoonMutatieLijn
            {
                Insz = insz,
                Overleden = overleden
            };
        }

        stopwatch.Stop();
        _metrics?.RecordRecordsParsed("persoon", parsedCount);
    }
}
