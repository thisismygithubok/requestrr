using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.ChatClients.Discord;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public class DiscordStatusMessageSender : IStatusMessageSender
    {
        private readonly DiscordClient _discordClient;
        private readonly ulong[] _channelIds;
        private readonly ILogger _logger;

        public DiscordStatusMessageSender(DiscordClient discordClient, string notificationMode, ulong[] channelIds, ILogger logger)
        {
            _discordClient = discordClient;
            _channelIds = channelIds;
            _logger = logger;
            UsesChannels = notificationMode == NotificationMode.Channels;
        }

        public bool UsesChannels { get; }

        public async Task<bool> SendAsync(string userId, string message, CancellationToken token)
        {
            if (!_discordClient.Guilds.Any())
                return false;

            // Long episode lists can exceed Discord's message limit.
            if (message.Length > DiscordConstants.MaxMessageLength)
                message = message.Substring(0, DiscordConstants.MaxMessageLength - 3) + "...";

            try
            {
                return UsesChannels ? await SendToChannelsAsync(ulong.Parse(userId), message) : await SendPrivateMessageAsync(ulong.Parse(userId), message);
            }
            catch (DSharpPlus.Exceptions.UnauthorizedException)
            {
                _logger.LogWarning($"Skipping request status notification for user [{userId}] as we are missing permissions to do so [Unauthorized].");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while sending a request status notification: " + ex.Message);
                return false;
            }
        }

        private async Task<bool> SendPrivateMessageAsync(ulong userId, string message)
        {
            DiscordMember user = null;

            foreach (var guild in _discordClient.Guilds.Values)
            {
                try
                {
                    user = await guild.GetMemberAsync(userId);
                    break;
                }
                catch { }
            }

            if (user == null)
            {
                _logger.LogWarning($"Skipping request status notification for user with ID {userId} as it could not be found in any of the guilds.");
                return true;
            }

            var channel = await user.CreateDmChannelAsync();
            await channel.SendMessageAsync(message);
            return true;
        }

        private async Task<bool> SendToChannelsAsync(ulong userId, string message)
        {
            var channels = _discordClient.Guilds
                .SelectMany(x => x.Value.Channels.Values)
                .Where(x => _channelIds.Contains(x.Id) && x.Users.Any(u => u.Id == userId))
                .ToArray();

            foreach (var channel in channels)
            {
                await channel.SendMessageAsync(message);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            return true;
        }
    }
}
