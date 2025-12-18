using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace AssociationRegistry.KboMutations.MutationFileLambda.Csv;

public class CsvMutatieBestandParser : ICsvMutatieBestandParser
{
    public IEnumerable<T> ParseMutatieLijnen<T>(string content)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            MissingFieldFound = null,
            Delimiter = ";",
        };

        using var stringReader = new StringReader(content);
        using var csv = new CsvReader(stringReader, config);

        return csv.GetRecords<T>().ToList();
    }
}