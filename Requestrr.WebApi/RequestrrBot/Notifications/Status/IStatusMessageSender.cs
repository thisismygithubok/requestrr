using System.Threading;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public interface IStatusMessageSender
    {
        bool UsesChannels { get; }

        // Returns true when the message was delivered or can never be delivered (user gone, DMs blocked).
        Task<bool> SendAsync(string userId, string message, CancellationToken token);
    }
}
