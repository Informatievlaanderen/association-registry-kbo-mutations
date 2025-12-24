using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS.Model;
using AssociationRegistry.Kbo;
using AssociationRegistry.KboMutations.CloudEvents;
using AssociationRegistry.KboMutations.Integration.Tests.Given_TeVerwerkenMutatieBestand_In_Queue_And_S3.Fixtures;
using AssociationRegistry.KboSyncLambda.SyncKbo;
using AssociationRegistry.Vereniging;
using CloudNative.CloudEvents;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Xunit;
using Xunit.Abstractions;

namespace AssociationRegistry.KboMutations.Integration.Tests.Given_TeVerwerkenMutatieBestand_In_Queue_And_S3;

public class When_Processing_Incoming_Ftps_Through_Both_Lambdas : IClassFixture<With_TeVerwerkenMutatieBestand_FromLocalstack>
{
    private readonly ITestOutputHelper _helper;
    private readonly With_TeVerwerkenMutatieBestand_FromLocalstack _fixture;

    public When_Processing_Incoming_Ftps_Through_Both_Lambdas(
        ITestOutputHelper helper,
        With_TeVerwerkenMutatieBestand_FromLocalstack fixture)
    {
        _helper = helper;
        _fixture = fixture;
        
        _fixture.FtpProcessor.ProcessAsync().GetAwaiter().GetResult();
        
        var messages = fixture.FetchMessages(fixture.KboSyncConfiguration.MutationFileQueueUrl).GetAwaiter().GetResult();
        
        messages.Should().NotBeEmpty();
        messages.Should().HaveCount(5);

        foreach (var message in messages)
        {
            try
            {
                _fixture.TeVerwerkenMessageProcessor
                    .ProcessMessage(new SQSEvent
                    {
                        Records =
                        [
                            new() { Body = message.Body }
                        ]
                    }, NullLogger.Instance, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch(Exception ex)
            {
                helper.WriteLine($"File could not be processed: {ex.Message}");
            }
        }
    }

    [Fact]
    public async Task SyncQueue_Has_Messages()
    {
        var retryPolicy = Policy.Handle<AssertionFailedException>()
            .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(i), (exception, ts, i, context) =>
            {
                _helper.WriteLine($"No matching events found in run {i}: {exception.Message}");
            });

        await retryPolicy.ExecuteAsync(async () => await VerifyKboEventsWereAdded(_helper, _fixture));
    }

    private static async Task VerifyKboEventsWereAdded(ITestOutputHelper helper, With_TeVerwerkenMutatieBestand_FromLocalstack fixture)
    {
        var messages = await fixture.FetchMessages(fixture.KboSyncConfiguration.SyncQueueUrl);
        messages.Should().HaveCount(6);

        var messageParser = new CloudEventMessageCollectionParser(messages);
        var kboMessages = messageParser.ExtractKboMessages().ToArray();
        var inszMessages = messageParser.ExtractInszMessages().ToArray();

        kboMessages.Should().BeEquivalentTo([
            new TeSynchroniserenKboNummerMessage(KboNummer.Create("0000000196")),
            new TeSynchroniserenKboNummerMessage(KboNummer.Create("0000000196")),
            new TeSynchroniserenKboNummerMessage(KboNummer.Create("1999999745")),
        ]);

        inszMessages.Should().BeEquivalentTo([
            new AssociationRegistry.KboMutations.Messages.TeSynchroniserenInszMessage("43030003000", true),
            new AssociationRegistry.KboMutations.Messages.TeSynchroniserenInszMessage("90000837000", true),
            new AssociationRegistry.KboMutations.Messages.TeSynchroniserenInszMessage("81010006400", false),
        ]);
    }
}

internal class CloudEventMessageCollectionParser
{
    private readonly IEnumerable<CloudEvent> _cloudEvents;

    public CloudEventMessageCollectionParser(IEnumerable<Message> sqsMessages)
    {
        _cloudEvents = sqsMessages
            .Select(m => CloudEventExtensions.FromJson(m.Body))
            .Where(ce => ce != null)
            .Cast<CloudEvent>();
    }

    public IEnumerable<TeSynchroniserenKboNummerMessage> ExtractKboMessages()
    {
        return _cloudEvents
            .Select(ce => new CloudEventDataExtractor(ce))
            .Select(extractor => extractor.TryExtractKboMessage())
            .Where(message => message != null)
            .Cast<TeSynchroniserenKboNummerMessage>();
    }

    public IEnumerable<AssociationRegistry.KboMutations.Messages.TeSynchroniserenInszMessage> ExtractInszMessages()
    {
        return _cloudEvents
            .Select(ce => new CloudEventDataExtractor(ce))
            .Select(extractor => extractor.TryExtractInszMessage())
            .Where(message => message != null)
            .Cast<AssociationRegistry.KboMutations.Messages.TeSynchroniserenInszMessage>();
    }
}

internal class CloudEventDataExtractor
{
    private readonly JsonElement _dataElement;

    public CloudEventDataExtractor(CloudEvent cloudEvent)
    {
        var dataJson = JsonSerializer.Serialize(cloudEvent.Data);
        _dataElement = JsonDocument.Parse(dataJson).RootElement;
    }

    public TeSynchroniserenKboNummerMessage? TryExtractKboMessage()
    {
        if (!_dataElement.TryGetProperty("KboNummer", out var kboElement))
            return null;

        var kboNummer = kboElement.GetString();
        return kboNummer != null
            ? new TeSynchroniserenKboNummerMessage(KboNummer.Create(kboNummer))
            : null;
    }

    public AssociationRegistry.KboMutations.Messages.TeSynchroniserenInszMessage? TryExtractInszMessage()
    {
        if (!_dataElement.TryGetProperty("Insz", out var inszElement))
            return null;

        var insz = inszElement.GetString();
        if (insz == null)
            return null;

        var overleden = _dataElement.TryGetProperty("Overleden", out var overledenElement)
            ? overledenElement.GetBoolean()
            : false;

        return new AssociationRegistry.KboMutations.Messages.TeSynchroniserenInszMessage(insz, overleden);
    }
}