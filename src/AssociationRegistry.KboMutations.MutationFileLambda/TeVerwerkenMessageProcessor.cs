using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Messages;
using AssocationRegistry.KboMutations.Models;
using AssocationRegistry.KboMutations.Notifications;
using AssociationRegistry.Kbo;
using AssociationRegistry.KboMutations.CloudEvents;
using AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;
using AssociationRegistry.KboMutations.MutationFileLambda.Logging;
using AssociationRegistry.KboMutations.Telemetry;
using AssociationRegistry.Notifications;
using CsvHelper;
using CsvHelper.Configuration;

namespace AssociationRegistry.KboMutations.MutationFileLambda;

public class TeVerwerkenMessageProcessor
{
    private readonly KboSyncConfiguration _kboSyncConfiguration;
    private readonly MutatieBestandProcessors _mutatieBestandProcessors;
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonSQS _sqsClient;
    private readonly INotifier _notifier;
    private readonly KboMutationsMetrics _metrics;


    public TeVerwerkenMessageProcessor(IAmazonS3 s3Client,
        IAmazonSQS sqsClient,
        INotifier notifier,
        KboSyncConfiguration kboSyncConfiguration,
        MutatieBestandProcessors mutatieBestandProcessors,
        KboMutationsMetrics metrics)
    {
        _s3Client = s3Client;
        _sqsClient = sqsClient;
        _notifier = notifier;
        _kboSyncConfiguration = kboSyncConfiguration;
        _mutatieBestandProcessors = mutatieBestandProcessors;
        _metrics = metrics;
    }

    public async Task ProcessMessage(SQSEvent sqsEvent,
        ILogger contextLogger,
        CancellationToken cancellationToken)
    {
        contextLogger.LogInformation($"{nameof(_kboSyncConfiguration.MutationFileBucketName)}:{_kboSyncConfiguration.MutationFileBucketName}");
        contextLogger.LogInformation($"{nameof(_kboSyncConfiguration.MutationFileQueueUrl)}:{_kboSyncConfiguration.MutationFileQueueUrl}");
        contextLogger.LogInformation($"{nameof(_kboSyncConfiguration.SyncQueueUrl)}:{_kboSyncConfiguration.SyncQueueUrl}");

        var encounteredExceptions = new List<Exception>();

        foreach (var record in sqsEvent.Records)
        {
            contextLogger.LogInformation("Processing record body: " + record.Body);

            // Deserialize CloudEvent and extract trace context
            var cloudEvent = CloudEventExtensions.FromJson(record.Body);
            var message = JsonSerializer.Deserialize<TeVerwerkenMutatieBestandMessage>(
                JsonSerializer.Serialize(cloudEvent?.Data));

            // Extract trace context and create activity with parent context
            ActivityContext? parentContext = cloudEvent?.ExtractTraceContext();
            var sourceFileName = cloudEvent?.GetSourceFileName();

            try
            {
                var responses = await Handle(contextLogger, message, parentContext, sourceFileName, cancellationToken);
                await _notifier.Notify(new KboMutationFileLambdaSqsBerichtBatchVerstuurd(responses.Count(x => x.HttpStatusCode == HttpStatusCode.OK)));

                var failedResponses = responses.Where(x => x.HttpStatusCode != HttpStatusCode.OK).ToArray();
                if (failedResponses.Any())
                    await _notifier.Notify(new KboMutationFileLambdaSqsBerichtBatchNietVerstuurd(failedResponses.Length));
                
                foreach (var batchResultErrorEntry in failedResponses)
                    contextLogger.LogWarning($"KBO mutatie file lambda kon message '{batchResultErrorEntry.MessageId}' niet verzenden.'");
            }
            catch (Exception ex)
            {
                await _notifier.Notify(new KboMutationFileLambdaMessageProcessorGefaald(ex));
                encounteredExceptions.Add(ex);
            }
        }
        
        if (encounteredExceptions.Any())
            throw new AggregateException(encounteredExceptions);
    }

    private async Task<List<SendMessageResponse>> Handle(ILogger contextLogger,
        TeVerwerkenMutatieBestandMessage? message,
        ActivityContext? parentContext,
        string? sourceFileName,
        CancellationToken cancellationToken)
    {
        var fileType = DetermineFileType(message.Key);

        // Start activity with parent context from CloudEvent
        using var activity = parentContext.HasValue
            ? KboMutationsActivitySource.Source.StartActivity(
                "ProcessMutationFile",
                ActivityKind.Consumer,
                parentContext.Value)
            : KboMutationsActivitySource.StartFileProcessing(message.Key, fileType);

        // Add source file name as tag for traceability
        if (activity != null && !string.IsNullOrEmpty(sourceFileName))
        {
            activity.SetTag("source.file.name", sourceFileName);
            activity.SetTag("file.type", fileType);
        }

        try
        {
            contextLogger.LogInformation($"Starting processing of mutation file: {message.Key} (type: {fileType})");

            using var s3Activity = KboMutationsActivitySource.StartS3Download(_kboSyncConfiguration.MutationFileBucketName, message.Key);
            var fetchMutatieBestandResponse = await _s3Client.GetObjectAsync(
                _kboSyncConfiguration.MutationFileBucketName,
                message.Key,
                cancellationToken);

            var content = await FetchMutationFileContent(fetchMutatieBestandResponse.ResponseStream, cancellationToken);
            _metrics.RecordFileSize(fileType, content.Length);

            contextLogger.LogInformation($"MutatieBestand found, size: {content.Length} characters");

            var processor = _mutatieBestandProcessors.FindProcessorOrNull(message.Key);

            if (processor == null)
            {
                contextLogger.LogCritical("Could not find mutatie bestand processor for message " + message.Key);
                _metrics.RecordFileProcessed(fileType, success: false);
                return [];
            }

            using var sqsActivity = KboMutationsActivitySource.StartSqsPublish(_kboSyncConfiguration.SyncQueueUrl, 0);
            var responses = await processor.Handle(message.Key, content, cancellationToken);
            sqsActivity?.SetTag("sqs.message.count", responses.Count);

            var successCount = responses.Count(r => r.HttpStatusCode == HttpStatusCode.OK);
            contextLogger.LogInformation($"Published {successCount} of {responses.Count} mutations to SQS");

            foreach (var response in responses.Where(r => r.HttpStatusCode == HttpStatusCode.OK))
            {
                _metrics.RecordMutationPublished(fileType);
            }

            await _s3Client.DeleteObjectAsync(_kboSyncConfiguration.MutationFileBucketName, message.Key, cancellationToken);

            _metrics.RecordFileProcessed(fileType, success: true);
            contextLogger.LogInformation($"Successfully processed file: {message.Key}");

            return responses;
        }
        catch (Exception ex)
        {
            _metrics.RecordFileProcessed(fileType, success: false);
            activity?.RecordException(ex);
            contextLogger.LogError($"Error processing mutation file: {message.Key} - {ex.Message}");
            throw;
        }
    }

    private static string DetermineFileType(string fileName)
    {
        if (fileName.Contains("onderneming", StringComparison.OrdinalIgnoreCase))
            return "onderneming";
        if (fileName.Contains("functie", StringComparison.OrdinalIgnoreCase))
            return "functie";
        if (fileName.Contains("persoon", StringComparison.OrdinalIgnoreCase))
            return "persoon";
        return "unknown";
    }

    private static async Task<string> FetchMutationFileContent(
        Stream mutatieBestandStream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(mutatieBestandStream);

        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static IEnumerable<T> ReadMutationLines<T>(string content)
    where T : IMutatieLijn
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