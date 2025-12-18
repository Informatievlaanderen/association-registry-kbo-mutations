using AssocationRegistry.KboMutations.Models;

namespace AssociationRegistry.KboMutations.MutationFileLambda.Csv;

public interface IPersoonXmlMutatieBestandParser
{
    IEnumerable<PersoonMutatieLijn> ParseMutatieLijnen(string xmlContent);
}
