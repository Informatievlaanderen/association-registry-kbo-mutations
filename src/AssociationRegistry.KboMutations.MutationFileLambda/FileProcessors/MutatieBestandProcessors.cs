using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Amazon.SQS;
using AssocationRegistry.KboMutations.Configuration;
using AssociationRegistry.KboMutations.MutationFileLambda.Csv;
using AssociationRegistry.KboMutations.Telemetry;

namespace AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;

public class MutatieBestandProcessors: ReadOnlyCollection<IMutatieBestandProcessor>
{
    public MutatieBestandProcessors(IList<IMutatieBestandProcessor> list) : base(list)
    {
    }

    public static MutatieBestandProcessors CreateDefault(KboSyncConfiguration kboSyncConfiguration,
        IAmazonSQS sqsClient,
        ILogger contextLogger,
        KboMutationsMetrics? metrics = null)
    {
        var csvParser = new CsvMutatieBestandParser(metrics);
        var xmlParser = new PersoonXmlMutatieBestandParser(metrics);

        return new([
            new OndernemingMutatieBestandProcessor(kboSyncConfiguration, sqsClient, csvParser, contextLogger),
            new FunctieMutatieBestandProcessor(kboSyncConfiguration, sqsClient, csvParser, contextLogger),
            new PersoonMutatieBestandProcessor(kboSyncConfiguration, sqsClient, xmlParser, contextLogger),
        ]);
    }

    public IMutatieBestandProcessor? FindProcessorOrNull(string fileName)
        => this.SingleOrDefault(x => x.CanHandle(fileName));
}