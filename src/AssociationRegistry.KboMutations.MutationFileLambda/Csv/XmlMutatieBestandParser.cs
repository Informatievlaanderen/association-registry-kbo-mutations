using System.Xml.Linq;
using System.Xml.XPath;
using AssocationRegistry.KboMutations.Models;

namespace AssociationRegistry.KboMutations.MutationFileLambda.Csv;

public class PersoonXmlMutatieBestandParser : IPersoonXmlMutatieBestandParser
{
    public IEnumerable<PersoonMutatieLijn> ParseMutatieLijnen(string xmlContent)
    {
        var document = XDocument.Parse(xmlContent);
        var navigator = document.CreateNavigator();

        var persoonNodes = navigator.Select("//Persoon");

        while (persoonNodes?.MoveNext() == true)
        {
            var persoonNode = persoonNodes.Current;
            if (persoonNode == null) continue;

            var inszNode = persoonNode.SelectSingleNode("INSZ");
            if (inszNode == null || string.IsNullOrWhiteSpace(inszNode.Value))
                continue;

            var insz = inszNode.Value.Trim();
            var overlijdenNode = persoonNode.SelectSingleNode("Overlijden");
            var overleden = overlijdenNode != null;

            yield return new PersoonMutatieLijn
            {
                Insz = insz,
                Overleden = overleden
            };
        }
    }
}
