using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations.Configuration;
using Xunit;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Abstractions;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Configuration;
using AssociationRegistry.KboMutations.MutationLambdaContainer.Ftps;
using AssociationRegistry.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssociationRegistry.KboMutations.MutationLambdaContainer.Tests;

public class MutatieFtpProcessorTests
{
    [Fact]
    public async Task PutsItemsOnQueueAndS3()
    {
        var ftpsClient = new Mock<IFtpsClient>();

        var result = $"-rw-rw-r--   1 kbbj.vlaanderen.be-dv-verenigingsregister-ftp kbbj.vlaanderen.be-dv-verenigingsregister-ftp        0 Mar 22 05:41 pub_mut_klanten-functies0200_20240322043924000.csv{Environment.NewLine}-rw-rw-r--   1 kbbj.vlaanderen.be-dv-verenigingsregister-ftp kbbj.vlaanderen.be-dv-verenigingsregister-ftp     2260 Apr 17 05:18 pub_mut-ondernemingVKBO0200_20240417031451000.xml";
        ftpsClient.Setup(x => x.GetListing(It.IsAny<string>()))
            .Returns(
                result);

        ftpsClient.Setup(x => x.Download(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);
        
        var amazonS3 = new Mock<IAmazonS3>();
        var amazonSqs = new Mock<IAmazonSQS>();

        var sut = new MutatieFtpProcessor(NullLogger.Instance, ftpsClient.Object, amazonS3.Object,
            amazonSqs.Object, new KboMutationsConfiguration(){SourcePath = "test", Host = "host", Port = 123, CachePath = "cache"}, new KboSyncConfiguration(), Mock.Of<INotifier>(),
            new AssociationRegistry.KboMutations.Telemetry.KboMutationsMetrics(new System.Diagnostics.Metrics.Meter("test")));
        await sut.ProcessAsync();
        
        amazonS3.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
        amazonSqs.Verify(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(6));

        var ftpsListItems = FtpsListParser.Parse(new FtpUriBuilder("host", 21), result);

        ftpsListItems.Should().HaveCount(2);
    }
}