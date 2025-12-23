namespace AssociationRegistry.KboMutations.Telemetry;

using System.Diagnostics;
using System.Diagnostics.Metrics;

public class KboMutationsMetrics
{
    public const string MeterName = "kbomutations.metrics";

    private readonly Counter<long> _filesProcessed;
    private readonly Counter<long> _mutationsPublished;
    private readonly Counter<long> _recordsParsed;
    private readonly Counter<long> _recordsSkipped;
    private readonly Histogram<double> _fileProcessingDuration;
    private readonly Histogram<long> _fileSizeBytes;
    private readonly string _environment;

    public KboMutationsMetrics(Meter meter)
    {
        _environment = Environment.GetEnvironmentVariable("ENVIRONMENT")?.ToLowerInvariant() ?? "unknown";

        _filesProcessed = meter.CreateCounter<long>(
            "kbo_mutations_files_processed_total",
            description: "Number of mutation files processed");

        _mutationsPublished = meter.CreateCounter<long>(
            "kbo_mutations_published_total",
            description: "Number of mutations published to SQS");

        _recordsParsed = meter.CreateCounter<long>(
            "kbo_mutations_records_parsed_total",
            description: "Number of records parsed from files");

        _recordsSkipped = meter.CreateCounter<long>(
            "kbo_mutations_records_skipped_total",
            description: "Number of records skipped during parsing");

        _fileProcessingDuration = meter.CreateHistogram<double>(
            "kbo_mutations_file_processing_duration_ms",
            unit: "ms",
            description: "Duration of file processing");

        _fileSizeBytes = meter.CreateHistogram<long>(
            "kbo_mutations_file_size_bytes",
            unit: "bytes",
            description: "Size of processed files");
    }

    public void RecordFileProcessed(string fileType, bool success)
    {
        var tags = new TagList
        {
            { "file.type", fileType },
            { "success", success },
            { "environment", _environment }
        };
        _filesProcessed.Add(1, tags);
    }

    public void RecordMutationPublished(string mutationType)
    {
        var tags = new TagList
        {
            { "mutation.type", mutationType },
            { "environment", _environment }
        };
        _mutationsPublished.Add(1, tags);
    }

    public void RecordRecordsParsed(string fileType, int count)
    {
        var tags = new TagList
        {
            { "file.type", fileType },
            { "environment", _environment }
        };
        _recordsParsed.Add(count, tags);
    }

    public void RecordRecordsSkipped(string fileType, int count, string reason)
    {
        var tags = new TagList
        {
            { "file.type", fileType },
            { "reason", reason },
            { "environment", _environment }
        };
        _recordsSkipped.Add(count, tags);
    }

    public void RecordFileSize(string fileType, long sizeBytes)
    {
        var tags = new TagList
        {
            { "file.type", fileType },
            { "environment", _environment }
        };
        _fileSizeBytes.Record(sizeBytes, tags);
    }
}
