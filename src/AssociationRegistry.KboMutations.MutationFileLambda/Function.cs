using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Amazon.SQS;
using AssocationRegistry.KboMutations;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Notifications;
using AssociationRegistry.KboMutations.MutationFileLambda.Configuration;
using AssociationRegistry.KboMutations.MutationFileLambda.Csv;
using AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;
using AssociationRegistry.KboMutations.MutationFileLambda.Telemetry;
using AssociationRegistry.KboMutations.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AssociationRegistry.KboMutations.MutationFileLambda;

public class Function
{
    private static TeVerwerkenMessageProcessor? _processor;
    private static readonly ColdStartDetector ColdStartDetector = new();

    private static async Task<int> Main()
    {
        var handler = FunctionHandler;
        await LambdaBootstrapBuilder.Create(handler, new SourceGeneratorLambdaJsonSerializer<LambdaFunctionJsonSerializerContext>())
            .Build()
            .RunAsync();

        return 0;
    }

    /// <summary>
    ///     This method is called for every Lambda invocation. This method takes in an SQS event object and can be used
    ///     to respond to SQS messages.
    /// </summary>
    /// <param name="event"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public static async Task FunctionHandler(SQSEvent @event, ILambdaContext context)
    {
        var coldStart = ColdStartDetector.IsColdStart();

        var configurationRoot = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true, true)
            .AddEnvironmentVariables()
            .Build();

        var telemetryManager = new TelemetryManager(context.Logger, configurationRoot);

        var meter = new Meter(KboMutationsMetrics.MeterName);
        var metrics = new KboMutationsMetrics(meter);

        metrics.RecordLambdaInvocation("kbo_mutation_file", coldStart);

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new LambdaLoggerProvider(context.Logger));
            telemetryManager.ConfigureLogging(builder);
        });

        var logger = loggerFactory.CreateLogger("KboMutations.MutationFileLambda");

        using var rootActivity = KboMutationsActivitySource.StartLambdaExecution(
            LambdaNames.KboMutationFile,
            SemanticConventions.TriggerTypes.Pubsub,
            coldStart);

        try
        {
            logger.LogInformation("KBO mutation file lambda started. Records to process: {RecordCount}", @event.Records.Count);

            var amazonKboSyncConfiguration = GetKboSyncConfiguration(configurationRoot);
            var ssmClientWrapper = new SsmClientWrapper(new AmazonSimpleSystemsManagementClient());
            var paramNamesConfiguration = GetParamNamesConfiguration(configurationRoot);
            var notifierLogger = loggerFactory.CreateLogger("NotifierFactory");
            var notifier = await new NotifierFactory(
                ssmClientWrapper,
                paramNamesConfiguration,
                notifierLogger).TryCreate();
            var s3Client = new AmazonS3Client();
            var sqsClient = new AmazonSQSClient();

            var processorLogger = loggerFactory.CreateLogger("TeVerwerkenMessageProcessor");
            var fileProcessorsLogger = loggerFactory.CreateLogger("MutatieBestandProcessors");

            _processor = new TeVerwerkenMessageProcessor(s3Client,
                sqsClient,
                notifier,
                amazonKboSyncConfiguration,
                MutatieBestandProcessors.CreateDefault(amazonKboSyncConfiguration,
                    sqsClient,
                    fileProcessorsLogger,
                    metrics),
                metrics);

            await _processor.ProcessMessage(@event, processorLogger, CancellationToken.None);
            logger.LogInformation("KBO mutation file lambda completed successfully");
        }
        catch (Exception e)
        {
            rootActivity.RecordException(e);
            logger.LogError(e, "KBO mutation file lambda failed with error: {ErrorMessage}", e.Message);
            await telemetryManager.FlushAsync(context);
            throw;
        }
        finally
        {
            logger.LogInformation("Kbo mutation file lambda finished");

            // Flush metrics and traces first (while logging still works)
            await telemetryManager.FlushAsync(context);

            // Then dispose LoggerFactory to flush logs
            context.Logger.LogInformation("Disposing LoggerFactory to flush logs");
            loggerFactory.Dispose();
            context.Logger.LogInformation("LoggerFactory disposed");
        }
    }

    private static ISlackConfiguration GetParamNamesConfiguration(IConfigurationRoot configurationRoot)
    {
        var paramNamesConfiguration = configurationRoot
            .GetSection(ParamNamesConfiguration.Section)
            .Get<ParamNamesConfiguration>();

        if (paramNamesConfiguration is null)
            throw new ApplicationException("Could not load ParamNamesConfiguration");
        return paramNamesConfiguration;
    }

    private static KboSyncConfiguration GetKboSyncConfiguration(IConfigurationRoot configurationRoot)
    {
        var awsConfigurationSection = configurationRoot.GetSection(KboSyncConfiguration.Section);

        var kboSyncConfiguration = new KboSyncConfiguration
        {
            MutationFileBucketName = awsConfigurationSection[nameof(WellKnownBucketNames.MutationFileBucketName)],
            MutationFileQueueUrl = awsConfigurationSection[nameof(WellKnownQueueNames.MutationFileQueueUrl)],
            SyncQueueUrl = awsConfigurationSection[nameof(WellKnownQueueNames.SyncQueueUrl)]!
        };

        if (string.IsNullOrWhiteSpace(kboSyncConfiguration.SyncQueueUrl))
            throw new ArgumentException($"{nameof(kboSyncConfiguration.SyncQueueUrl)} cannot be null or empty");
        
        if (string.IsNullOrWhiteSpace(kboSyncConfiguration.MutationFileQueueUrl))
            throw new ArgumentException($"{nameof(kboSyncConfiguration.MutationFileQueueUrl)} cannot be null or empty");
        
        if (string.IsNullOrWhiteSpace(kboSyncConfiguration.MutationFileBucketName))
            throw new ArgumentException($"{nameof(kboSyncConfiguration.MutationFileBucketName)} cannot be null or empty");
        
        return kboSyncConfiguration;
    }
}

/// <summary>
/// This class is used to register the input event and return type for the FunctionHandler method with the System.Text.Json source generator.
/// There must be a JsonSerializable attribute for each type used as the input and return type or a runtime error will occur 
/// from the JSON serializer unable to find the serialization information for unknown types.
/// </summary>
[JsonSerializable(typeof(SQSEvent))]
public partial class LambdaFunctionJsonSerializerContext : JsonSerializerContext
{
    // By using this partial class derived from JsonSerializerContext, we can generate reflection free JSON Serializer code at compile time
    // which can deserialize our class and properties. However, we must attribute this class to tell it what types to generate serialization code for.
    // See https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-source-generation
}