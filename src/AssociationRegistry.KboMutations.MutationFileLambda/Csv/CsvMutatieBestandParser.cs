using System.Diagnostics;
using System.Globalization;
using AssociationRegistry.KboMutations.Telemetry;
using CsvHelper;
using CsvHelper.Configuration;

namespace AssociationRegistry.KboMutations.MutationFileLambda.Csv;

public class CsvMutatieBestandParser : ICsvMutatieBestandParser
{
    private readonly KboMutationsMetrics? _metrics;

    public CsvMutatieBestandParser(KboMutationsMetrics? metrics = null)
    {
        _metrics = metrics;
    }

    public IEnumerable<T> ParseMutatieLijnen<T>(string content)
    {
        var stopwatch = Stopwatch.StartNew();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            MissingFieldFound = null,
            Delimiter = ";",
        };

        using var stringReader = new StringReader(content);
        using var csv = new CsvReader(stringReader, config);

        var records = csv.GetRecords<T>().ToList();

        stopwatch.Stop();
        _metrics?.RecordRecordsParsed("csv", records.Count);

        return records;
    }
}