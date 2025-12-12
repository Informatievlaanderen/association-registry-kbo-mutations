using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Models;
using AssociationRegistry.Kbo;
using AssociationRegistry.KboMutations.MutationFileLambda.Csv;

namespace AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;

public class FunctieMutatieBestandProcessor: IMutatieBestandProcessor
{
    private readonly KboSyncConfiguration _kboSyncConfiguration;
    private readonly IAmazonSQS _sqsClient;
    private readonly IMutatieBestandParser _bestandParser;
    private readonly ILambdaLogger _contextLogger;

    public FunctieMutatieBestandProcessor(KboSyncConfiguration kboSyncConfiguration,
        IAmazonSQS sqsClient,
        IMutatieBestandParser bestandParser,
        ILambdaLogger contextLogger)
    {
        _kboSyncConfiguration = kboSyncConfiguration;
        _sqsClient = sqsClient;
        _bestandParser = bestandParser;
        _contextLogger = contextLogger;
    }
    
    public bool CanHandle(string fileName) => 
        fileName.StartsWith(_kboSyncConfiguration.FunctiesFileNamePrefix);

    public async Task<List<SendMessageResponse>> Handle(string filename, string content, CancellationToken cancellationToken)
    {
        var mutatielijnen = _bestandParser.ParseMutatieLijnen<FunctieMutatieLijn>(content).ToArray();

        _contextLogger.LogInformation($"Found {mutatielijnen.Length} mutatielijnen");

        var responses = new List<SendMessageResponse>();
        foreach (var mutatielijn in mutatielijnen)
        {
            _contextLogger.LogInformation($"Sending {mutatielijn.Ondernemingsnummer} to synchronize queue");

            var messageBody = JsonSerializer.Serialize(
                new TeSynchroniserenKboNummerMessage(mutatielijn.Ondernemingsnummer));

            responses.Add(await _sqsClient.SendMessageAsync(_kboSyncConfiguration.SyncQueueUrl,messageBody,
                cancellationToken));
        }
        
        return responses;
    }
}
