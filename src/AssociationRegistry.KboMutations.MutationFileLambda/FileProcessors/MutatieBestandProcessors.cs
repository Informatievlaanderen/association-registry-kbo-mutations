using System.Collections.ObjectModel;
using Amazon.Lambda.Core;
using Amazon.SQS;
using AssocationRegistry.KboMutations.Configuration;
using AssociationRegistry.KboMutations.MutationFileLambda.Csv;

namespace AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;

public class MutatieBestandProcessors: ReadOnlyCollection<IMutatieBestandProcessor>
{
    public MutatieBestandProcessors(IList<IMutatieBestandProcessor> list) : base(list)
    {
    }

    public static MutatieBestandProcessors CreateDefault(KboSyncConfiguration kboSyncConfiguration,
        IAmazonSQS sqsClient,
        ILambdaLogger contextLogger)
    {
        var csvParser = new CsvMutatieBestandParser();
        var xmlParser = new PersoonXmlMutatieBestandParser();

        return new([
            new OndernemingMutatieBestandProcessor(kboSyncConfiguration, sqsClient, csvParser, contextLogger),
            new FunctieMutatieBestandProcessor(kboSyncConfiguration, sqsClient, csvParser, contextLogger),
            new PersoonMutatieBestandProcessor(kboSyncConfiguration, sqsClient, xmlParser, contextLogger),
        ]);
    }

    public IMutatieBestandProcessor? FindProcessorOrNull(string fileName)
        => this.SingleOrDefault(x => x.CanHandle(fileName));
}