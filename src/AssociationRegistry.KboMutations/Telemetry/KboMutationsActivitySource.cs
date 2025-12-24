namespace AssociationRegistry.KboMutations.Telemetry;

using System.Diagnostics;

// OpenTelemetry Semantic Convention constants
// Based on https://opentelemetry.io/docs/specs/semconv/
public static class SemanticConventions
{
    // FAAS (Function as a Service) - https://opentelemetry.io/docs/specs/semconv/faas/
    public const string FaasName = "faas.name";
    public const string FaasTrigger = "faas.trigger";
    public const string FaasColdstart = "faas.coldstart";

    // Cloud - https://opentelemetry.io/docs/specs/semconv/resource/cloud/
    public const string CloudProvider = "cloud.provider";
    public const string CloudPlatform = "cloud.platform";

    // Trigger type values - https://opentelemetry.io/docs/specs/semconv/faas/faas-spans/#faas-trigger
    public static class TriggerTypes
    {
        public const string Timer = "timer";
        public const string Pubsub = "pubsub";
        public const string Http = "http";
        public const string Datasource = "datasource";
        public const string Other = "other";
    }

    // Cloud provider values
    public static class CloudProviders
    {
        public const string Aws = "aws";
    }

    // Cloud platform values
    public static class CloudPlatforms
    {
        public const string AwsLambda = "aws_lambda";
    }
}

// Lambda names
public static class LambdaNames
{
    public const string KboMutations = "kbo_mutations";
    public const string KboMutationFile = "kbo_mutation_file";
}

public static class KboMutationsActivitySource
{
    public static readonly ActivitySource Source = new("KboMutations", "1.0.0");

    public static Activity? StartLambdaExecution(string lambdaName, string trigger, bool coldStart)
    {
        var activity = Source.StartActivity("LambdaExecution", ActivityKind.Server);
        activity?.SetTag(SemanticConventions.FaasName, lambdaName);
        activity?.SetTag(SemanticConventions.FaasTrigger, trigger);
        activity?.SetTag(SemanticConventions.FaasColdstart, coldStart);
        activity?.SetTag(SemanticConventions.CloudProvider, SemanticConventions.CloudProviders.Aws);
        activity?.SetTag(SemanticConventions.CloudPlatform, SemanticConventions.CloudPlatforms.AwsLambda);
        return activity;
    }

    public static Activity? StartFileProcessing(string fileName, string fileType)
    {
        var activity = Source.StartActivity("ProcessFile");
        activity?.SetTag("file.name", fileName);
        activity?.SetTag("file.type", fileType);
        return activity;
    }

    public static Activity? StartParsing(string fileType, long fileSize)
    {
        var activity = Source.StartActivity("ParseFile");
        activity?.SetTag("file.type", fileType);
        activity?.SetTag("file.size.bytes", fileSize);
        return activity;
    }

    public static Activity? StartFtpDownload(string host, string path)
    {
        var activity = Source.StartActivity("FtpDownload");
        activity?.SetTag("ftp.host", host);
        activity?.SetTag("ftp.path", path);
        return activity;
    }

    public static Activity? StartSqsPublish(string queueUrl, int messageCount)
    {
        var activity = Source.StartActivity("PublishToSqs");
        activity?.SetTag("sqs.queue.url", queueUrl);
        activity?.SetTag("sqs.message.count", messageCount);
        return activity;
    }

    public static Activity? StartS3Download(string bucket, string key)
    {
        var activity = Source.StartActivity("S3Download");
        activity?.SetTag("s3.bucket", bucket);
        activity?.SetTag("s3.key", key);
        return activity;
    }

    public static void RecordException(this Activity? activity, Exception ex)
    {
        if (activity == null) return;

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.SetTag("exception.type", ex.GetType().FullName);
        activity.SetTag("exception.message", ex.Message);
        activity.SetTag("exception.stacktrace", ex.StackTrace);
    }
}
