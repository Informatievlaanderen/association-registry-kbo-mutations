using Microsoft.Extensions.Logging;
using AssociationRegistry.Notifications;
using Slack.Webhooks;

namespace AssocationRegistry.KboMutations.Notifications;

public class SlackNotifier : INotifier
{
    private readonly ILogger _logger;
    private SlackClient _slackClient;

    public SlackNotifier(ILogger logger, string webhookUrl)
    {
        if (webhookUrl == null) throw new ArgumentNullException(nameof(webhookUrl));
        _logger = logger;

        _slackClient = new SlackClient(webhookUrl);
    }

    public async Task Notify(IMessage message)
    {
        var postAsync = await _slackClient.PostAsync(new SlackMessage
        {
            Channel = string.Empty,
            Markdown = true,
            Text = message.Value,
            IconEmoji = message.Type switch
            {
                NotifyType.None => Emoji.Bulb,
                NotifyType.Success => Emoji.Up,
                NotifyType.Failure => Emoji.X
            },
            Username = "Kbo Sync"
        });
        
        if(!postAsync)
        {
            _logger.LogWarning($"Slack bericht kon niet verstuurd worden: '{message.Value}' ({message.Type})");
        }
        else
        {
            _logger.LogInformation($"Slack bericht verstuurd: '{message.Value}' ({message.Type})");
        }
    }
}