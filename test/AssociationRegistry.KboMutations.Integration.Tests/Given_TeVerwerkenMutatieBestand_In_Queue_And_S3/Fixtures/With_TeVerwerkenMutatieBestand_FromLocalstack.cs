using System.Diagnostics;
using Amazon.Lambda.TestUtilities;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Notifications;
using AssociationRegistry.KboMutations.MutationFileLambda;
using AssociationRegistry.KboMutations.MutationLambdaContainer;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Abstractions;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Configuration;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Ftps;
using AssociationRegistry.KboMutations.Tests.Fixtures;
using AssociationRegistry.Vereniging;
using Marten;
using Marten.Events;
using Npgsql;
using Weasel.Core;

namespace AssociationRegistry.KboMutations.Integration.Tests.Given_TeVerwerkenMutatieBestand_In_Queue_And_S3.Fixtures;

public class With_TeVerwerkenMutatieBestand_FromLocalstack : WithLocalstackFixture
{
    public MessageProcessor MessageProcessor { get; private set; }
    public MutatieFtpProcessor FtpProcessor { get; private set; }
    public static KboNummer KboNummerBekendeVereniging = KboNummer.Create("0442528054");
    public static KboNummer KboNummerOnbekendeVereniging = KboNummer.Create("0000000097");
    
    public static Dictionary<KboNummer, VCode> KboNummersToSeed =
    new()
    {
        { KboNummerBekendeVereniging, VCode.Create("V0001001")},
        { KboNummerOnbekendeVereniging, VCode.Create("V0001002")},
    };

    public With_TeVerwerkenMutatieBestand_FromLocalstack() : base(
        WellKnownBucketNames.MutationFileBucketName,
        WellKnownQueueNames.MutationFileQueueUrl,
        WellKnownQueueNames.SyncQueueUrl)
    {
        
    }
    
    public IFtpsClient SecureFtpClient { get; private set; }

    protected override async Task SetupAsync()
    {
        var logger = new TestLambdaLogger();
        var sftpPath = "../../../../../sftp";
        var seedFolder = "seed";
        var inFolder = "files/in";

        var certPath = $"{sftpPath}/cert/custom_vsftpd.crt";
        var keyPath = $"{sftpPath}/cert/custom_vsftpd.der";

        // Copy all directories and files from seed to in folder
        foreach (var entry in Directory.EnumerateFileSystemEntries(Path.Join(sftpPath, seedFolder)))
        {
            var entryInfo = new FileInfo(entry);
            var destPath = Path.Join(sftpPath, inFolder, entryInfo.Name);
            
            if (Directory.Exists(entry))
            {
                // Copy directory recursively
                CopyDirectory(entry, destPath);
            }
            else if (File.Exists(entry))
            {
                // Copy file
                File.Copy(entry, destPath, true);
            }
        }
        
        var kboMutationsConfiguration = new KboMutationsConfiguration
        {
            Host = "localhost",
            Port = 21000,
            Username = "files",
            Password = "FSBhuNOR",
            SourcePath = "in/ondernemingen",
            SourcePathFuncties = "in/functies",
            CachePath = "archive",
            CertPath = certPath,
            CaCertPath = string.Empty,
            KeyPath = keyPath,
            KeyType = "DER",
            LockEnabled = false,
            CurlLocation = "curl",
            AdditionalParams = "-k"
        };
        await ClearQueue(KboSyncConfiguration.MutationFileQueueUrl);
        await ClearQueue(KboSyncConfiguration.SyncQueueUrl);

        SecureFtpClient = new CurlFtpsClient(logger, kboMutationsConfiguration);

        FtpProcessor = new MutatieFtpProcessor(logger, SecureFtpClient, AmazonS3Client,
            AmazonSqsClient, kboMutationsConfiguration,
            KboSyncConfiguration, 
            new NullNotifier(new TestLambdaLogger()));

        MessageProcessor = new MessageProcessor(AmazonS3Client, AmazonSqsClient, new NullNotifier(new TestLambdaLogger()), KboSyncConfiguration);
    }

    public static DocumentStore CreateDocumentStore()
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
        return documentStore;
    }

    public async Task<List<Message>> FetchMessages(string syncQueueUrl)
    {
        var stopWatch = Stopwatch.StartNew();
        var allReceivedMessages = new List<Message>();
        const int maxWaitTimeSeconds = 3; 
        const int totalOperationTimeSeconds = 3;

        while (stopWatch.Elapsed < TimeSpan.FromSeconds(totalOperationTimeSeconds))
        {
            var response = await AmazonSqsClient.ReceiveMessageAsync(
                new ReceiveMessageRequest(syncQueueUrl)
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
    
    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        // Create destination directory if it doesn't exist
        Directory.CreateDirectory(destinationDir);
        
        // Copy all files in the directory
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        
        // Copy all subdirectories recursively
        foreach (var subdir in Directory.GetDirectories(sourceDir))
        {
            var destSubdir = Path.Combine(destinationDir, Path.GetFileName(subdir));
            CopyDirectory(subdir, destSubdir);
        }
    }
}