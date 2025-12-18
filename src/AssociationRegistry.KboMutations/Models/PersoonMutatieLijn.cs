using CsvHelper.Configuration.Attributes;

namespace AssocationRegistry.KboMutations.Models;

public class PersoonMutatieLijn
{

    public string Insz { get; init; } = null!;

    public bool Overleden { get; init; }

    protected bool Equals(PersoonMutatieLijn other)
    {
        return Insz == other.Insz;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((PersoonMutatieLijn)obj);
    }

    public override int GetHashCode()
    {
        return GetHashCodeFromField(Insz, Insz.GetHashCode());
    }

    private static int GetHashCodeFromField(object field, int hashCode)
    {
        return (hashCode * 397) ^ field.GetHashCode();
    }
}