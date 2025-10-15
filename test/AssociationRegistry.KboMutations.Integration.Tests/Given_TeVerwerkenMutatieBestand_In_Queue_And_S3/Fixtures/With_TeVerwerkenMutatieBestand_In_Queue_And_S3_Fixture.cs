using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Amazon.S3.Model;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations;
using AssocationRegistry.KboMutations.Messages;
using AssocationRegistry.KboMutations.Models;
using AssociationRegistry.EventStore;
using AssociationRegistry.Framework;
using AssociationRegistry.Kbo;
using AssociationRegistry.KboMutations.Tests.Customizations;
using AssociationRegistry.KboMutations.Tests.Fixtures;
using AssociationRegistry.Vereniging;
using AutoFixture;
using CsvHelper;
using FluentAssertions;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using Npgsql;
using Weasel.Core;

namespace AssociationRegistry.KboMutations.Integration.Tests.Given_TeVerwerkenMutatieBestand_In_Queue_And_S3.Fixtures;

public class With_TeVerwerkenMutatieBestand_In_Queue_And_S3_Fixture : WithLocalstackFixture
{
    public TeVerwerkenMutatieBestandMessage TeVerwerkenMutatieBestandMessage { get; }
    public OndernemingMutatieLijn[] MutatieLijnen { get; set; }

    public With_TeVerwerkenMutatieBestand_In_Queue_And_S3_Fixture() : base(
        WellKnownBucketNames.MutationFileBucketName,
        WellKnownQueueNames.MutationFileQueueUrl,
        WellKnownQueueNames.SyncQueueUrl
 )
    {
        TeVerwerkenMutatieBestandMessage = CustomFixture.Default.Create<TeVerwerkenMutatieBestandMessage>();
        MutatieLijnen = CustomFixture.Default
            .CreateMany<OndernemingMutatieLijn>()
            .ToArray();
    }

    protected override async Task SetupAsync()
    {
        var documentStore = DocumentStore.For(opts =>
        {
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder();
            connectionStringBuilder.Host = "localhost";
            connectionStringBuilder.Database = "verenigingsregister";
            connectionStringBuilder.Username = "root";
            connectionStringBuilder.Password = "root";

            opts.Connection(connectionStringBuilder.ToString());
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            // opts.Serializer(CreateCustomMartenSerializer());
            opts.Events.MetadataConfig.EnableAll();
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });
        
        await documentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        
        await documentStore.Storage.Database.DeleteAllEventDataAsync();

        var eventConflictResolver =
            new EventConflictResolver(Array.Empty<IEventPreConflictResolutionStrategy>(), Array.Empty<IEventPostConflictResolutionStrategy>());
        
        var repo = new VerenigingsRepository(new EventStore.EventStore(documentStore, eventConflictResolver, new NullLogger<EventStore.EventStore>()));

        foreach (var (lijn, i) in MutatieLijnen.Select(((lijn, i) => (lijn, i))))
        {
            var result = await repo.Save(VerenigingMetRechtspersoonlijkheid.Registreer(
                VCode.Create(1001 + i),
                new VerenigingVolgensKbo
                {
                    KboNummer = KboNummer.Create(lijn.Ondernemingsnummer),
                    Adres = new AdresVolgensKbo(),
                    Contactgegevens = new ContactgegevensVolgensKbo(),
                    Naam = "Test",
                    Startdatum = DateOnly.MinValue,
                    Type = Verenigingstype.VZW,
                    Vertegenwoordigers = Array.Empty<VertegenwoordigerVolgensKbo>(),
                    KorteNaam = "T"
                }), new CommandMetadata("OVO000001", new Instant(), Guid.NewGuid()));

            result.Sequence.Should().BeGreaterThan(0);
        }

        await ClearQueue(KboSyncConfiguration.MutationFileQueueUrl);
        await ClearQueue(KboSyncConfiguration.SyncQueueUrl);
        
        await WriteMutationFileToBucket(
            TeVerwerkenMutatieBestandMessage, 
            KboSyncConfiguration.MutationFileBucketName!);

        await SendMutationFileToQueue(
            TeVerwerkenMutatieBestandMessage, 
            KboSyncConfiguration.MutationFileQueueUrl);
        
        // ReceivedMessages = await FetchMessages();
    }

    public async Task<List<Message>> FetchMessages()
    {
        var stopWatch = Stopwatch.StartNew();
        var allReceivedMessages = new List<Message>();
        const int maxWaitTimeSeconds = 3; 
        const int totalOperationTimeSeconds = 3;

        while (stopWatch.Elapsed < TimeSpan.FromSeconds(totalOperationTimeSeconds))
        {
            var response = await AmazonSqsClient.ReceiveMessageAsync(
                new ReceiveMessageRequest(KboSyncConfiguration.SyncQueueUrl)
            {
                WaitTimeSeconds = maxWaitTimeSeconds,
                MaxNumberOfMessages = 10,
            });

            if (response.Messages.Any()) allReceivedMessages.AddRange(response.Messages);
        }

        return allReceivedMessages;
    }

    private async Task ClearQueue(string queueUrl)
    {
        await AmazonSqsClient.PurgeQueueAsync(queueUrl);
    }

    public List<Message> ReceivedMessages { get; set; }

    private async Task WriteMutationFileToBucket(TeVerwerkenMutatieBestandMessage mutationFile, string mutationFileBucket)
    {
        await using var writer = new StringWriter();
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        await csv.WriteRecordsAsync(MutatieLijnen);

        var putObjectResponse = await AmazonS3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = mutationFileBucket,
            Key = mutationFile.Key,
            ContentBody = writer.ToString()
        });
    }

    private async Task SendMutationFileToQueue(TeVerwerkenMutatieBestandMessage mutationFile, string mutationFileQueueUrl)
    {
        var sendMessageResponse = await AmazonSqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = mutationFileQueueUrl,
            MessageBody = JsonSerializer.Serialize(mutationFile)
        });

        sendMessageResponse.HttpStatusCode.Should().Be(HttpStatusCode.OK);
    }
}