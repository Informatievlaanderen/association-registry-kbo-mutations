using AssociationRegistry.Notifications;
using Microsoft.Extensions.Logging;

namespace AssocationRegistry.KboMutations.Notifications;

public class NullNotifier : INotifier
{
    private readonly ILogger _logger;

    public NullNotifier(ILogger logger)
    {
        _logger = logger;
    }

    public async Task Notify(IMessage message) => _logger.LogInformation($"Not notifying slack: {message.Value}");
}