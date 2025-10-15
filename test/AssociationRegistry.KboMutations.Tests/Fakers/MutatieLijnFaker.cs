using System.Text;
using AssocationRegistry.KboMutations.Models;
using AutoBogus;

namespace AssociationRegistry.KboMutations.Tests.Fakers;

public sealed class MutatieLijnFaker : AutoFaker<OndernemingMutatieLijn>
{
    public MutatieLijnFaker()
    {
        Configure(builder => builder.WithLocale("nl_BE"));

        RuleFor(p => p.Ondernemingsnummer, (f) =>
        {
            var sb = new StringBuilder();
            sb.Append(f.Random.Int(0, 1));
            for (var i = 1; i < 10; i++) sb.Append(f.Random.Int(0, 9));
            return sb.ToString();
        });
        RuleFor(p => p.DatumModificatie, f => DateTime.Today);
        RuleFor(p => p.MaatschappelijkeNaam, f => f.Company.CompanyName());
        // RuleFor(p => p.StatusCode, f => f.PickRandom<>());
        RuleFor(p => p.StopzettingsDatum, f => default);
        RuleFor(p => p.StopzettingsCode, f => default);
        RuleFor(p => p.StopzettingsReden, f => default);
    }
}