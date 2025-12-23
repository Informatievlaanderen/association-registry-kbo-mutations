using System.Net;
using System.Text;
using System.Text.Unicode;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using AssocationRegistry.KboMutations.Configuration;
using AssocationRegistry.KboMutations.Messages;
using AssociationRegistry.KboMutations.MutationFileLambda.Csv;
using AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;
using AssociationRegistry.Notifications;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace AssociationRegistry.KboMutations.MutationFileLambda.Tests;

public class MutationFileLambdaTests
{
    [Fact]
    public async Task SendsAnItemToQueueForEachLineInFile()
    {
        var s3client = SetUpS3ClientMock();
        var sqsClient = SetUpSqsClientMock();

        var messageProcessor = new TeVerwerkenMessageProcessor(
            s3Client: s3client.Object,
            sqsClient.Object,
            new Mock<INotifier>().Object,
            new KboSyncConfiguration(),
            MutatieBestandProcessors.CreateDefault(new KboSyncConfiguration(),
                sqsClient.Object,
                new TestLambdaLogger()),
            new AssociationRegistry.KboMutations.Telemetry.KboMutationsMetrics(new System.Diagnostics.Metrics.Meter("test")));

        await messageProcessor.ProcessMessage(new SQSEvent()
        {
            Records = new List<SQSEvent.SQSMessage>()
            {
                new SQSEvent.SQSMessage()
                {
                    Body = JsonConvert.SerializeObject(new TeVerwerkenMutatieBestandMessage("pub_mut_klanten-functies.csv"))
                }
            }
        }, Mock.Of<ILambdaLogger>(), CancellationToken.None);

        sqsClient.Verify(x => x.SendMessageAsync(It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()));

    }

    private static Mock<IAmazonSQS> SetUpSqsClientMock()
    {
        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient
            .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse {HttpStatusCode = HttpStatusCode.OK});
        return sqsClient;
    }

    private static Mock<IAmazonS3> SetUpS3ClientMock()
    {
        var content = """
                      "2024-03-28";"0204245277";"03";"KBO";"";"";"";"2022-03-04";"1";"Onderneming";"";"";"";"AC";"";"Actief";"";"";"2";"Rechtspersoon";"";"";"";"";"";"ENODIA";"fr";"2018-11-20";"TECTEO";"fr";"2007-06-22";"";"";"";"1988-06-30";"2002";"Rue Louvrex";"95";"";"62063";"4000";"Liège";"150";"BEL";"Belgique";"";"";"fr";"";"";"";"";"";"";"";"";"";"";"officiel.ic-enodia@enodia.net";"";"1923-03-30";"2003-01-18";"9999-12-31";"1923-03-30";"000";"";"Normale toestand";"";"";"2021-12-22";"706";"";"Coöperatieve vennootschap";"";"";"";"";"";"";"";"";"";"";"";"";"";"";"";""
                      "2024-03-29";"0206767574";"03";"KBO";"";"";"";"2003-12-12";"1";"Onderneming";"";"";"";"AC";"";"Actief";"";"";"2";"Rechtspersoon";"";"";"";"";"";"IGEAN dienstverlening";"nl";"2016-06-17";"I.G.E.A.N.-dienstverlening";"nl";"2003-12-12";"";"";"";"1996-07-16";"1070";"Doornaardstraat";"60";"";"11052";"2160";"Wommelgem";"150";"BEL";"België";"";"";"nl";"";"";"";"";"";"";"";"";"";"";"";"";"1969-06-19";"2003-01-18";"9999-12-31";"1969-06-19";"000";"";"Normale toestand";"";"";"2003-11-08";"416";"";"Dienstverlenende vereniging (Vlaams Gewest)";"";"";"";"";"";"";"";"";"";"";"";"";"";"";"";""
                      """;

        var s3client = new Mock<IAmazonS3>();
        var getObjectResponse = new GetObjectResponse()
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
        };
        s3client
            .Setup(s3 => 
                s3.GetObjectAsync(
                    It.IsAny<string>(), 
                    It.IsAny<string>(), 
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(getObjectResponse);
        return s3client;
    }
}