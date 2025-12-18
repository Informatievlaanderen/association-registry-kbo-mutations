namespace AssociationRegistry.KboMutations.MutationFileLambda.Csv;

public interface ICsvMutatieBestandParser
{
    IEnumerable<T> ParseMutatieLijnen<T>(string content);
}
