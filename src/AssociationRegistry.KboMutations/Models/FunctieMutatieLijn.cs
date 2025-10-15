using CsvHelper.Configuration.Attributes;

namespace AssocationRegistry.KboMutations.Models;

public class FunctieMutatieLijn : IMutatieLijn
{
    // schema available at https://vlaamseoverheid.atlassian.net/wiki/spaces/MG/pages/516129060/Interface+PubliceerOndernemingVKBO-02.00
    [Index(0)] public DateTime DatumModificatie { get; init; }
    
    [Index(6)] public string Ondernemingsnummer { get; init; } = null!;

    protected bool Equals(FunctieMutatieLijn other)
    {
        return DatumModificatie.Equals(other.DatumModificatie) &&
               Ondernemingsnummer == other.Ondernemingsnummer;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((FunctieMutatieLijn)obj);
    }

    public override int GetHashCode()
    {
        return GetHashCodeFromField(Ondernemingsnummer, DatumModificatie.GetHashCode());
    }

    private static int GetHashCodeFromField(object field, int hashCode)
    {
        return (hashCode * 397) ^ field.GetHashCode();
    }
}

public interface IMutatieLijn
{
    public string Ondernemingsnummer { get; }
}