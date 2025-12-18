namespace AssocationRegistry.KboMutations.Configuration;

public record KboSyncConfiguration
{
    public static string Section = "KboSync";

    public string? MutationFileBucketName { get; set; }
    public string MutationFileQueueUrl { get; set; }
    public string MutationFileDeadLetterQueueUrl { get; set; }
    public string SyncQueueUrl { get; set; }
    public string SyncDeadLetterQueueUrl { get; set; }
    
    public string PersonenFileNamePrefix { get; set; } = "persoon.publiceermutatiepersoon-0202";
    public string FunctiesFileNamePrefix { get; set; } = "pub_mut_klanten-functies";
    
    public string OndernemingFileNamePrefix { get; set; } = "pub_mut-ondernemingVKBO";
}