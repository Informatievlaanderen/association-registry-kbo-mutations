using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Models;
using AssociationRegistry.Kbo;
using AssociationRegistry.KboMutations.CloudEvents;
using AssociationRegistry.KboMutations.Messages;
using AssociationRegistry.KboMutations.MutationFileLambda.Csv;
using AssociationRegistry.KboMutations.Telemetry;
using CsvHelper;
using CsvHelper.Configuration;

namespace AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;

public class PersoonMutatieBestandProcessor: IMutatieBestandProcessor
{
    private readonly KboSyncConfiguration _kboSyncConfiguration;
    private readonly IAmazonSQS _sqsClient;
    private readonly IPersoonXmlMutatieBestandParser _xmlParser;
    private readonly ILogger _contextLogger;

    public PersoonMutatieBestandProcessor(KboSyncConfiguration kboSyncConfiguration,
        IAmazonSQS sqsClient,
        IPersoonXmlMutatieBestandParser xmlParser,
        ILogger contextLogger)
    {
        _kboSyncConfiguration = kboSyncConfiguration;
        _sqsClient = sqsClient;
        _xmlParser = xmlParser;
        _contextLogger = contextLogger;
    }

    public bool CanHandle(string fileName) =>
        fileName.StartsWith(_kboSyncConfiguration.PersonenFileNamePrefix);

    public async Task<List<SendMessageResponse>> Handle(string filename, string content, CancellationToken cancellationToken)
    {
        using var activity = KboMutationsActivitySource.StartParsing("persoon", content.Length);

        var mutatielijnen = _xmlParser.ParseMutatieLijnen(content).ToArray();

        activity?.SetTag("mutation.records_parsed", mutatielijnen.Length);
        _contextLogger.LogInformation($"Found {mutatielijnen.Length} mutatielijnen");

        var responses = new List<SendMessageResponse>();
        foreach (var mutatielijn in mutatielijnen)
        {
            _contextLogger.LogInformation($"Sending persoon to synchronize queue");

            // Create CloudEvent with trace context
            var cloudEventJson = CloudEventBuilder.KboSyncPersonQueued()
                .WithData(new TeSynchroniserenInszMessage(mutatielijn.Insz, mutatielijn.Overleden))
                .FromFile(Activity.Current?.GetTagItem("source.file.name")?.ToString())
                .BuildAsJson();

            responses.Add(await _sqsClient.SendMessageAsync(_kboSyncConfiguration.SyncQueueUrl, cloudEventJson,
                cancellationToken));
        }

        return responses;
    }
}
