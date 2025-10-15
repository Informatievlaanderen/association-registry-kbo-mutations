using AssocationRegistry.KboMutations.Models;
using AssociationRegistry.Vereniging;
using AutoFixture;

namespace AssociationRegistry.KboMutations.Tests.Customizations;

public static class CustomFixture
{
    public static IFixture Default => new Fixture().CustomizeAll();

     public static IFixture CustomizeAll(this IFixture fixture)
     {
        return fixture
            .CustomizeMutatielijn()
            .CustomizeKboNummer();
    }
    
    private static IFixture CustomizeMutatielijn(this IFixture fixture)
    {
        fixture.Customize<OndernemingMutatieLijn>(
            composerTransformation: composer => composer.FromFactory(
                    factory: () => new OndernemingMutatieLijn
                    {
                        DatumModificatie = fixture.Create<DateTime>(),
                        StatusCode = fixture.Create<string>(),
                        Ondernemingsnummer = fixture.Create<KboNummer>(),
                        MaatschappelijkeNaam = fixture.Create<string>(),
                        StopzettingsDatum = fixture.Create<DateTime>(),
                        StopzettingsCode = fixture.Create<string>(),
                        StopzettingsReden = fixture.Create<string>()
                    })
                .OmitAutoProperties()
        );
        return fixture;
    }
    private static IFixture CustomizeKboNummer(this IFixture fixture)
    {
        fixture.Customize<KboNummer>(
            composerTransformation: composer => composer.FromFactory(
                    factory: () =>
                    {
                        var kboBase = new Random().Next(0, 99999999);
                        var kboModulo = 97 - kboBase % 97;

                        return KboNummer.Create($"{kboBase:D8}{kboModulo:D2}");
                    })
                .OmitAutoProperties()
        );
         return fixture;
     }
 }
