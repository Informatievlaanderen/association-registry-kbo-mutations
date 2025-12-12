using Amazon.SQS.Model;

namespace AssociationRegistry.KboMutations.MutationFileLambda.FileProcessors;

public interface IMutatieBestandProcessor
{
    public bool CanHandle(string fileName);

    public Task<List<SendMessageResponse>> Handle(string filename, string content, CancellationToken cancellationToken);
}