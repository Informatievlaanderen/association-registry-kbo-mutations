using System.Globalization;
using System.Net;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Messages;
using AssocationRegistry.KboMutations.Models;
using AssocationRegistry.KboMutations.Notifications;
using AssociationRegistry.Kbo;
using AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;
using AssociationRegistry.KboMutations.MutationFileLambda.Logging;
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

    
    public TeVerwerkenMessageProcessor(IAmazonS3 s3Client,
        IAmazonSQS sqsClient,
        INotifier notifier,
        KboSyncConfiguration kboSyncConfiguration,
        MutatieBestandProcessors mutatieBestandProcessors)
    {
        _s3Client = s3Client;
        _sqsClient = sqsClient;
        _notifier = notifier;
        _kboSyncConfiguration = kboSyncConfiguration;
        _mutatieBestandProcessors = mutatieBestandProcessors;
    }

    public async Task ProcessMessage(SQSEvent sqsEvent,
        ILambdaLogger contextLogger,
        CancellationToken cancellationToken)
    {
        contextLogger.LogInformation($"{nameof(_kboSyncConfiguration.MutationFileBucketName)}:{_kboSyncConfiguration.MutationFileBucketName}");
        contextLogger.LogInformation($"{nameof(_kboSyncConfiguration.MutationFileQueueUrl)}:{_kboSyncConfiguration.MutationFileQueueUrl}");
        contextLogger.LogInformation($"{nameof(_kboSyncConfiguration.SyncQueueUrl)}:{_kboSyncConfiguration.SyncQueueUrl}");

        var encounteredExceptions = new List<Exception>();

        foreach (var record in sqsEvent.Records)
        {
            contextLogger.LogInformation("Processing record body: " + record.Body);

            var message = JsonSerializer.Deserialize<TeVerwerkenMutatieBestandMessage>(record.Body);

            try
            {
                var responses = await Handle(contextLogger, message, cancellationToken);
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

    private async Task<List<SendMessageResponse>> Handle(ILambdaLogger contextLogger,
        TeVerwerkenMutatieBestandMessage? message,
        CancellationToken cancellationToken)
    {
        var fetchMutatieBestandResponse = await _s3Client.GetObjectAsync(
            _kboSyncConfiguration.MutationFileBucketName,
            message.Key,
            cancellationToken);

        var content = await FetchMutationFileContent(fetchMutatieBestandResponse.ResponseStream, cancellationToken);

        contextLogger.LogInformation($"MutatieBestand found");

        var processor = _mutatieBestandProcessors.FindProcessorOrThrow(message.Key);

        var responses = await processor.Handle(message.Key, content, cancellationToken);

        await _s3Client.DeleteObjectAsync(_kboSyncConfiguration.MutationFileBucketName, message.Key, cancellationToken);

        return responses;
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