namespace AssociationRegistry.KboMutations.MutationLambdaContainer.Configuration;

public class KboMutationsConfiguration
{
    public static string Section = "KboMutations";

    public DateTime Created => DateTime.Now;

    public string Host { get; set; } = null!;
    public int Port { get; set; }
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string SourcePath { get; set; } = "ondernemingen";
    public string SourcePathFuncties { get; set; } = "functies";
    public string SourcePathPersonen { get; set; } = "personen";
    public string CachePath { get; set; } = null!;
    public string CertPath { get; set; } = null!;
    public string CaCertPath { get; set; } = null!;
    public string KeyPath { get; set; } = null!;
    public string KeyType { get; set; } = null!;
    public string LockRegionEndPoint { get; set; } = null!;
    public string LockAccessKeyId { get; set; } = null!;
    public string LockAccessKeySecret { get; set; } = null!;
    public string LockTableName { get; set; } = null!;
    public int LockLeasePeriodInMinutes { get; set; }
    public bool LockEnabled { get; set; }
    public string CurlLocation { get; set; } = null!;
    public string AdditionalParams { get; set; } = null!;
    public string DownloadPath { get; set; } = null!;
    public string CertBucketName { get; set; } = null!;
}