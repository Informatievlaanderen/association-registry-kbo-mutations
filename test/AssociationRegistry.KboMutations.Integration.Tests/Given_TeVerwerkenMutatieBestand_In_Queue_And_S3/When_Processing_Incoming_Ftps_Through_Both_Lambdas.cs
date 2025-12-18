using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using AssociationRegistry.Kbo;
using AssociationRegistry.KboMutations.Integration.Tests.Given_TeVerwerkenMutatieBestand_In_Queue_And_S3.Fixtures;
using AssociationRegistry.KboSyncLambda.SyncKbo;
using AssociationRegistry.Vereniging;
using FluentAssertions;
using FluentAssertions.Execution;
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
                    }, new TestLambdaLogger(), CancellationToken.None)
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

        var kboMessages = messages.Where(x => x.Body.Contains("KboNummer"))
            .Select(x => JsonSerializer.Deserialize<TeSynchroniserenKboNummerMessage>(x.Body)).ToArray();
        var inszMessages = messages.Where(x => x.Body.Contains("Insz"))
            .Select(x => JsonSerializer.Deserialize<AssociationRegistry.KboMutations.Messages.TeSynchroniserenInszMessage>(x.Body)).ToArray();

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