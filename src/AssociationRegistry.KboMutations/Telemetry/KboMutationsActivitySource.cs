namespace AssociationRegistry.KboMutations.Telemetry;

using System.Diagnostics;

public static class KboMutationsActivitySource
{
    public static readonly ActivitySource Source = new("KboMutations", "1.0.0");

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
