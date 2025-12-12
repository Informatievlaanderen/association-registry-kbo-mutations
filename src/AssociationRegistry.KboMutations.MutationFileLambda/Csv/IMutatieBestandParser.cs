namespace AssociationRegistry.KboMutations.MutationFileLambda.Csv;

public interface IMutatieBestandParser
{
    IEnumerable<T> ParseMutatieLijnen<T>(string content);
}