using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Models;
using AssociationRegistry.Kbo;
using AssociationRegistry.KboMutations.CloudEvents;
using AssociationRegistry.KboMutations.MutationFileLambda.Csv;
using AssociationRegistry.KboMutations.Telemetry;
using CsvHelper;
using CsvHelper.Configuration;

namespace AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;

public class OndernemingMutatieBestandProcessor: IMutatieBestandProcessor
{
    private readonly KboSyncConfiguration _kboSyncConfiguration;
    private readonly IAmazonSQS _sqsClient;
    private readonly ICsvMutatieBestandParser _csvParser;
    private readonly ILambdaLogger _contextLogger;

    public OndernemingMutatieBestandProcessor(KboSyncConfiguration kboSyncConfiguration,
        IAmazonSQS sqsClient,
        ICsvMutatieBestandParser csvParser,
        ILambdaLogger contextLogger)
    {
        _kboSyncConfiguration = kboSyncConfiguration;
        _sqsClient = sqsClient;
        _csvParser = csvParser;
        _contextLogger = contextLogger;
    }

    public bool CanHandle(string fileName) =>
        fileName.StartsWith(_kboSyncConfiguration.OndernemingFileNamePrefix);

    public async Task<List<SendMessageResponse>> Handle(string filename, string content, CancellationToken cancellationToken)
    {
        using var activity = KboMutationsActivitySource.StartParsing("onderneming", content.Length);

        var mutatielijnen = _csvParser.ParseMutatieLijnen<OndernemingMutatieLijn>(content).ToArray();

        activity?.SetTag("mutation.records_parsed", mutatielijnen.Length);
        _contextLogger.LogInformation($"Found {mutatielijnen.Length} mutatielijnen");

        var responses = new List<SendMessageResponse>();
        foreach (var mutatielijn in mutatielijnen)
        {
            _contextLogger.LogInformation($"Sending {mutatielijn.Ondernemingsnummer} to synchronize queue");

            // Create CloudEvent with trace context
            var cloudEventJson = CloudEventBuilder.KboSyncOrganisationQueued()
                .WithData(new TeSynchroniserenKboNummerMessage(mutatielijn.Ondernemingsnummer))
                .FromFile(Activity.Current?.GetTagItem("source.file.name")?.ToString())
                .BuildAsJson();

            responses.Add(await _sqsClient.SendMessageAsync(_kboSyncConfiguration.SyncQueueUrl, cloudEventJson,
                cancellationToken));
        }

        return responses;
    }
}