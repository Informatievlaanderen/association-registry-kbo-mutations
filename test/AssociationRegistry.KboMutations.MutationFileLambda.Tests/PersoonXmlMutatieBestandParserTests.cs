using AssocationRegistry.KboMutations.Models;
using AssociationRegistry.KboMutations.MutationFileLambda.Csv;
using FluentAssertions;
using Xunit;

namespace AssociationRegistry.KboMutations.MutationFileLambda.Tests;

public class PersoonXmlMutatieBestandParserTests
{
    private readonly IPersoonXmlMutatieBestandParser _parser;

    public PersoonXmlMutatieBestandParserTests()
    {
        _parser = new PersoonXmlMutatieBestandParser();
    }

    [Fact]
    public void ParseMutatieLijnen_WithValidXml_ReturnsCorrectNumberOfRecords()
    {
        // Arrange
        var xmlContent = """
            <?xml version='1.0' encoding='UTF-8'?>
            <vip:PubliceerMutatiePersoon
                xmlns:vip="http://magda.vlaanderen.be/persoon/file/publiceermutatiepersoon/v02_02">
                <Publicatie>
                    <Context>
                        <Naam>Persoon.PubliceerMutatiePersoon</Naam>
                    </Context>
                    <Onderwerpen>
                        <Onderwerp>
                            <Referte>ref-1</Referte>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-10">
                                        <INSZ>43030003000</INSZ>
                                        <Overlijden>
                                            <Datum>2025-12-07</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                        <Onderwerp>
                            <Referte>ref-2</Referte>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-11">
                                        <INSZ>90000837000</INSZ>
                                        <Overlijden>
                                            <Datum>2025-11-30</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                        <Onderwerp>
                            <Referte>ref-3</Referte>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-12">
                                        <INSZ>81010006400</INSZ>
                                        <Overlijden>
                                            <Datum>2025-12-01</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                    </Onderwerpen>
                </Publicatie>
            </vip:PubliceerMutatiePersoon>
            """;

        // Act
        var result = _parser.ParseMutatieLijnen(xmlContent).ToList();

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public void ParseMutatieLijnen_ExtractsCorrectInszValues()
    {
        // Arrange
        var xmlContent = """
            <?xml version='1.0' encoding='UTF-8'?>
            <vip:PubliceerMutatiePersoon
                xmlns:vip="http://magda.vlaanderen.be/persoon/file/publiceermutatiepersoon/v02_02">
                <Publicatie>
                    <Onderwerpen>
                        <Onderwerp>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-10">
                                        <INSZ>43030003000</INSZ>
                                        <Overlijden>
                                            <Datum>2025-12-07</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                        <Onderwerp>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-11">
                                        <INSZ>90000837000</INSZ>
                                        <Overlijden>
                                            <Datum>2025-11-30</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                    </Onderwerpen>
                </Publicatie>
            </vip:PubliceerMutatiePersoon>
            """;

        // Act
        var result = _parser.ParseMutatieLijnen(xmlContent).ToList();

        // Assert
        result.Should().Contain(m => m.Insz == "43030003000" && m.Overleden);
        result.Should().Contain(m => m.Insz == "90000837000" && m.Overleden);
    }

    [Fact]
    public void ParseMutatieLijnen_ExtractsCorrectOverlijdenFlag()
    {
        // Arrange
        var xmlContent = """
            <?xml version='1.0' encoding='UTF-8'?>
            <vip:PubliceerMutatiePersoon
                xmlns:vip="http://magda.vlaanderen.be/persoon/file/publiceermutatiepersoon/v02_02">
                <Publicatie>
                    <Onderwerpen>
                        <Onderwerp>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-10">
                                        <INSZ>43030003000</INSZ>
                                        <Overlijden>
                                            <Datum>2025-12-07</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                    </Onderwerpen>
                </Publicatie>
            </vip:PubliceerMutatiePersoon>
            """;

        // Act
        var result = _parser.ParseMutatieLijnen(xmlContent).ToList();

        // Assert
        result.Should().ContainSingle();
        result[0].Insz.Should().Be("43030003000");
        result[0].Overleden.Should().BeTrue();
    }

    [Fact]
    public void ParseMutatieLijnen_SkipsRecordsWithoutInsz()
    {
        // Arrange
        var xmlContent = """
            <?xml version='1.0' encoding='UTF-8'?>
            <vip:PubliceerMutatiePersoon
                xmlns:vip="http://magda.vlaanderen.be/persoon/file/publiceermutatiepersoon/v02_02">
                <Publicatie>
                    <Onderwerpen>
                        <Onderwerp>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-10">
                                        <INSZ>43030003000</INSZ>
                                        <Overlijden>
                                            <Datum>2025-12-07</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                        <Onderwerp>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-11">
                                        <!-- No INSZ element -->
                                        <Overlijden>
                                            <Datum>2025-12-07</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                    </Onderwerpen>
                </Publicatie>
            </vip:PubliceerMutatiePersoon>
            """;

        // Act
        var result = _parser.ParseMutatieLijnen(xmlContent).ToList();

        // Assert
        result.Should().ContainSingle();
        result[0].Insz.Should().Be("43030003000");
        result[0].Overleden.Should().BeTrue();
    }

    [Fact]
    public void ParseMutatieLijnen_IncludesRecordsWithoutOverlijden()
    {
        // Arrange
        var xmlContent = """
            <?xml version='1.0' encoding='UTF-8'?>
            <vip:PubliceerMutatiePersoon
                xmlns:vip="http://magda.vlaanderen.be/persoon/file/publiceermutatiepersoon/v02_02">
                <Publicatie>
                    <Onderwerpen>
                        <Onderwerp>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-10">
                                        <INSZ>43030003000</INSZ>
                                        <Overlijden>
                                            <Datum>2025-12-07</Datum>
                                        </Overlijden>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                        <Onderwerp>
                            <Inhoud>
                                <MutatiePersoon>
                                    <Persoon Bron="KSZ" DatumModificatie="2025-12-11">
                                        <!-- Person alive - no Overlijden element -->
                                        <INSZ>90000837000</INSZ>
                                    </Persoon>
                                </MutatiePersoon>
                            </Inhoud>
                        </Onderwerp>
                    </Onderwerpen>
                </Publicatie>
            </vip:PubliceerMutatiePersoon>
            """;

        // Act
        var result = _parser.ParseMutatieLijnen(xmlContent).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(m => m.Insz == "43030003000" && m.Overleden);
        result.Should().Contain(m => m.Insz == "90000837000" && !m.Overleden);
    }

    [Fact]
    public void ParseMutatieLijnen_HandlesEmptyXml()
    {
        // Arrange
        var xmlContent = """
            <?xml version='1.0' encoding='UTF-8'?>
            <vip:PubliceerMutatiePersoon
                xmlns:vip="http://magda.vlaanderen.be/persoon/file/publiceermutatiepersoon/v02_02">
                <Publicatie>
                    <Onderwerpen>
                    </Onderwerpen>
                </Publicatie>
            </vip:PubliceerMutatiePersoon>
            """;

        // Act
        var result = _parser.ParseMutatieLijnen(xmlContent).ToList();

        // Assert
        result.Should().BeEmpty();
    }
}
