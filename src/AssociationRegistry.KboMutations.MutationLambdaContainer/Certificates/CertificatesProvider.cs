using System.Text;
using Amazon.Lambda.Core;
using Amazon.S3;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Configuration;

namespace AssociationRegistry.KboMutations.MutationLambdaContainer;

public class CertificatesProvider
{
    private readonly KboMutationsConfiguration _kboMutationsConfiguration;

    public CertificatesProvider(KboMutationsConfiguration kboMutationsConfiguration)
    {
        if (kboMutationsConfiguration == null)
            throw new ArgumentNullException(nameof(kboMutationsConfiguration));

        _kboMutationsConfiguration = kboMutationsConfiguration;
    }
    
    public async Task WriteCertificatesToFileSystem(ILambdaLogger logger, IAmazonS3 s3Client)
    {
        logger.LogInformation($"Downloading certs from {_kboMutationsConfiguration.CertBucketName}");
        
        await s3Client.DownloadToFilePathAsync(_kboMutationsConfiguration.CertBucketName, "cacert",
            _kboMutationsConfiguration.CaCertPath, new Dictionary<string, object>());
        
        await s3Client.DownloadToFilePathAsync(_kboMutationsConfiguration.CertBucketName, "cert.crt",
            _kboMutationsConfiguration.CertPath, new Dictionary<string, object>());
        
        await s3Client.DownloadToFilePathAsync(_kboMutationsConfiguration.CertBucketName, "key",
            _kboMutationsConfiguration.KeyPath, new Dictionary<string, object>());

    }
}