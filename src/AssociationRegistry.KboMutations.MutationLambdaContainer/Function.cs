using System.Net;
using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Notifications;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Certificates;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Configuration;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Ftps;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Logging;
using AssociationRegistry.Notifications;
using Microsoft.Extensions.Configuration;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace AssociationRegistry.KboMutations.MutationLambdaContainer;

public static class Function
{
    private static async Task Main()
    {
        var handler = FunctionHandler;
        await LambdaBootstrapBuilder.Create(handler,
                new SourceGeneratorLambdaJsonSerializer<LambdaFunctionJsonSerializerContext>())
            .Build()
            .RunAsync();
    }

    private static async Task FunctionHandler(string input, ILambdaContext context)
    {
        await SharedFunctionHandler(context);
    }

    public static async Task SharedFunctionHandler(ILambdaContext context)
    {
        var configurationRoot = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true, true)
            .AddEnvironmentVariables()
            .Build();

        var paramNamesConfiguration = GetParamNamesConfiguration(configurationRoot);
        var kboMutationsConfiguration = GetKboMutationsConfiguration(configurationRoot);
        var kboSyncConfiguration = GetKboSyncConfiguration(configurationRoot);
        var ssmClientWrapper = new SsmClientWrapper(new AmazonSimpleSystemsManagementClient());
        var notifier = await new NotifierFactory(ssmClientWrapper, paramNamesConfiguration, context.Logger).TryCreate();
        var amazonSqsClient = new AmazonSQSClient();

        await NotifyMetrics(notifier, amazonSqsClient, kboSyncConfiguration);

        try
        {
            await notifier.Notify(new KboMutationLambdaGestart());
            var mutatieBestandProcessor = await SetUpFunction(
                context,
                amazonSqsClient,
                kboMutationsConfiguration,
                kboSyncConfiguration,
                notifier);

            await mutatieBestandProcessor.ProcessAsync();
            await notifier.Notify(new KboMutationLambdaVoltooid());
        }
        catch (Exception ex)
        {
            await notifier.Notify(new KboMutationLambdaGefaald(ex));
            throw;
        }
    }

    private static async Task<MutatieBestandProcessor> SetUpFunction(
        ILambdaContext context,
        IAmazonSQS amazonSqsClient,
        KboMutationsConfiguration kboMutationsConfiguration,
        KboSyncConfiguration kboSyncConfigurtion,
        INotifier notifier)
    {
        var certProvider = new CertificatesProvider(kboMutationsConfiguration);

        var amazonS3Client = new AmazonS3Client();
        await certProvider.WriteCertificatesToFileSystem(context.Logger, amazonS3Client);

        var mutatieBestandProcessor = new MutatieBestandProcessor(
            context.Logger,
            new CurlFtpsClient(context.Logger, kboMutationsConfiguration),
            amazonS3Client,
            amazonSqsClient,
            kboMutationsConfiguration,
            kboSyncConfigurtion,
            notifier);

        return mutatieBestandProcessor;
    }

    private static KboMutationsConfiguration GetKboMutationsConfiguration(IConfigurationRoot configurationRoot)
    {
        var kboMutationsConfiguration = configurationRoot
            .GetSection(KboMutationsConfiguration.Section)
            .Get<KboMutationsConfiguration>();

        if (kboMutationsConfiguration is null)
            throw new ApplicationException($"Could not load {nameof(KboMutationsConfiguration)}");
        return kboMutationsConfiguration;
    }

    private static KboSyncConfiguration GetKboSyncConfiguration(IConfigurationRoot configurationRoot)
    {
        var kboSyncConfiguration = configurationRoot
            .GetSection(KboSyncConfiguration.Section)
            .Get<KboSyncConfiguration>();

        if (kboSyncConfiguration is null)
            throw new ApplicationException($"Could not load {nameof(KboSyncConfiguration)}");
        return kboSyncConfiguration;
    }

    private static ParamNamesConfiguration GetParamNamesConfiguration(IConfigurationRoot configurationRoot)
    {
        var paramNamesConfiguration = configurationRoot
            .GetSection(ParamNamesConfiguration.Section)
            .Get<ParamNamesConfiguration>();

        if (paramNamesConfiguration is null)
            throw new ApplicationException("Could not load ParamNamesConfiguration");
        return paramNamesConfiguration;
    }

    private static async Task NotifyMetrics(INotifier notifier, IAmazonSQS amazonSqsClient,
        KboSyncConfiguration kboSyncConfiguration)
    {
        await foreach (var attributeResponse in GetQueueAttributes(amazonSqsClient, kboSyncConfiguration))
        {
            await notifier.Notify(new KboMutationLambdaQueueStatus(
                attributeResponse.QueueARN,
                0 +
                attributeResponse.ApproximateNumberOfMessages +
                attributeResponse.ApproximateNumberOfMessagesDelayed +
                attributeResponse.ApproximateNumberOfMessagesNotVisible));
        }
    }

    private static async IAsyncEnumerable<GetQueueAttributesResponse> GetQueueAttributes(IAmazonSQS amazonSqsClient, KboSyncConfiguration kboSyncConfiguration)
    {
        var attributeNames = new List<string>
        {
            "QueueARN",
            "ApproximateNumberOfMessages",
            "ApproximateNumberOfMessagesDelayed",
            "ApproximateNumberOfMessagesNotVisible"
        };

        var queueUrls = new[]
        {
            kboSyncConfiguration.MutationFileQueueUrl,
            kboSyncConfiguration.MutationFileDeadLetterQueueUrl,
            kboSyncConfiguration.SyncQueueUrl,
            kboSyncConfiguration.SyncDeadLetterQueueUrl
        };

        var tasks = new List<Task<GetQueueAttributesResponse>>();

        foreach (var queueUrl in queueUrls)
        {
            tasks.Add(amazonSqsClient.GetQueueAttributesAsync(queueUrl, attributeNames));
        }

        foreach (var task in tasks)
        {
            GetQueueAttributesResponse result = null;
            try
            {
                result = await task;
            }
            catch (Exception ex)
            {
                // Handle or log the exception as needed
                // Optionally, continue to the next task instead of halting the process
                continue;
            }
        
            if (result != null)
            {
                yield return result;
            }
        }
    }


}

[JsonSerializable(typeof(string))]
public partial class LambdaFunctionJsonSerializerContext : JsonSerializerContext
{
}