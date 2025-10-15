using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Messages;
using AssocationRegistry.KboMutations.Models;
using AssocationRegistry.KboMutations.Notifications;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Abstractions;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Configuration;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Ftps;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Logging;
using AssociationRegistry.Notifications;

namespace AssociationRegistry.KboMutations.MutationLambdaContainer;

public class MutatieBestandProcessor
{
    private readonly FtpUriBuilder _baseUriBuilder;
    private readonly IFtpsClient _ftpsClient;
    private readonly KboMutationsConfiguration _kboMutationsConfiguration;
    private readonly KboSyncConfiguration _kboSyncConfiguration;
    private readonly ILambdaLogger _logger;
    private readonly INotifier _notifier;
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonSQS _sqsClient;

    public MutatieBestandProcessor(ILambdaLogger logger, IFtpsClient ftpsClient, IAmazonS3 s3Client,
        IAmazonSQS sqsClient, KboMutationsConfiguration kboMutationsConfiguration,
        KboSyncConfiguration kboSyncConfiguration, INotifier notifier)
    {
        _logger = logger;
        _ftpsClient = ftpsClient;
        _s3Client = s3Client;
        _sqsClient = sqsClient;
        _kboMutationsConfiguration = kboMutationsConfiguration;
        _kboSyncConfiguration = kboSyncConfiguration;
        _notifier = notifier;

        _baseUriBuilder = new FtpUriBuilder(_kboMutationsConfiguration.Host, _kboMutationsConfiguration.Port);
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var magdaMutatieBestanden = GetMagdaMutatieBestanden().ToList();
        await _notifier.Notify(new KboMutationLambdaBestandenOpgehaald(magdaMutatieBestanden.Count));

        foreach (var magdaMutatieBestand in magdaMutatieBestanden)
            await ProcessBestandAsync(magdaMutatieBestand, cancellationToken);
    }

    private async Task ProcessBestandAsync(MagdaMutatieBestand magdaMutatieBestand, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream();

            // Download file from MAGDA
            var fullNameUri = _baseUriBuilder.WithPath(magdaMutatieBestand.FtpPath).ToString();
            var fileName = Path.GetFileName(fullNameUri);
            var localDestinationFilePath = Path.Join(_kboMutationsConfiguration.DownloadPath, fileName);

            if (!_ftpsClient.Download(stream, fullNameUri, localDestinationFilePath)) throw new ApplicationException($"Bestand {magdaMutatieBestand.Name} kon niet opgehaald worden");

            stream.Seek(0, SeekOrigin.Begin);

            // Save file on S3
            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _kboSyncConfiguration.MutationFileBucketName,
                Key = magdaMutatieBestand.Name,
                InputStream = stream
            }, cancellationToken);
            _logger.LogInformation($"S3:PutObject for filename '{magdaMutatieBestand.Name}'");

            // Notify about file on SQS
            await _sqsClient.SendMessageAsync(new SendMessageRequest(
                _kboSyncConfiguration.MutationFileQueueUrl,
                JsonSerializer.Serialize(new TeVerwerkenMutatieBestandMessage(magdaMutatieBestand.Name))
            )
            {
                MessageGroupId = Guid.NewGuid().ToString()
            }, cancellationToken);

            // Archive file from MAGDA
            ArchiveerMagdaMutatieBestand(magdaMutatieBestand);
        }
        catch (Exception ex)
        {
            _logger.LogError($"KBO mutation lambda kon een bestand niet verwerken. Bestandsnaam: '{magdaMutatieBestand.Name}' ({ex.Message})");
            throw;
        }
    }

    private IEnumerable<MagdaMutatieBestand> GetMagdaMutatieBestanden()
    {
        _logger.LogInformation($"Fetching mutation files from folder {_kboMutationsConfiguration.SourcePath} started.");

        var mutationFiles = new List<MagdaMutatieBestand>();
        mutationFiles.AddRange(GetMagdaMutatieBestandenVoor(_kboMutationsConfiguration.SourcePath));
        mutationFiles.AddRange(GetMagdaMutatieBestandenVoor(_kboMutationsConfiguration.SourcePathFuncties));

        _logger.LogInformation($"Found {mutationFiles.Count} mutation files to process");
        _logger.LogInformation($"Fetching mutation files from folder {_kboMutationsConfiguration.SourcePath} finished.");
        return mutationFiles;
    }

    private List<MagdaMutatieBestand> GetMagdaMutatieBestandenVoor(string sourcePath)
    {
        var sourceDirectoryUri = _baseUriBuilder.AppendDir(sourcePath);
        var curlListResult = _ftpsClient.GetListing(sourceDirectoryUri.ToString());
        var mutationFiles =
            FtpsListParser.Parse(sourceDirectoryUri, curlListResult)
                .Select(ftpsListItem => new MagdaMutatieBestand(ftpsListItem.FullName, ftpsListItem.Name))
                .OrderBy(item => item.FtpPath)
                .ToList();
        return mutationFiles;
    }

    private void ArchiveerMagdaMutatieBestand(MagdaMutatieBestand magdaMutatieBestand)
    {
        _logger.LogInformation($"Archiving {magdaMutatieBestand.FtpPath} to {_kboMutationsConfiguration.CachePath} started.");

        var sourceFullNameUri = _baseUriBuilder.WithPath(magdaMutatieBestand.FtpPath);
        var destinationFullNameUri = _baseUriBuilder
            .AppendDir(_kboMutationsConfiguration.CachePath)
            .AppendFileName(sourceFullNameUri.FileName);

        _ftpsClient.MoveFile(_baseUriBuilder.ToString(),
            sourceFullNameUri.Path,
            destinationFullNameUri.Path);

        _logger.LogInformation($"Archiving {magdaMutatieBestand.FtpPath} to {_kboMutationsConfiguration.CachePath} finished.");
    }
}