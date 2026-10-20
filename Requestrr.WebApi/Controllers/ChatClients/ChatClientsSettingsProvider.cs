using Requestrr.WebApi.config;
using Requestrr.WebApi.RequestrrBot;
using Requestrr.WebApi.RequestrrBot.Notifications.Status;

namespace Requestrr.WebApi.Controllers.ChatClients
{
    public class ChatClientsSettingsProvider
    {
        public ChatClientsSettings Provide()
        {
            dynamic settings = SettingsFile.Read();

            var discordSettings = new DiscordSettings
            {
                BotToken = (string)settings.ChatClients.Discord.BotToken,
                ClientId = (string)settings.ChatClients.Discord.ClientId,
                StatusMessage = (string)settings.ChatClients.Discord.StatusMessage,
                MonitoredChannels = settings.ChatClients.Discord.MonitoredChannels.ToObject<string[]>(),
                TvShowRoles = settings.ChatClients.Discord.TvShowRoles.ToObject<string[]>(),
                MovieRoles = settings.ChatClients.Discord.MovieRoles.ToObject<string[]>(),
                MusicRoles = settings.ChatClients.Discord.MusicRoles.ToObject<string[]>(),
                EnableRequestsThroughDirectMessages = (bool)settings.ChatClients.Discord.EnableRequestsThroughDirectMessages,
                AutomaticallyNotifyRequesters = (bool)settings.ChatClients.Discord.AutomaticallyNotifyRequesters,
                NotificationMode = (string)settings.ChatClients.Discord.NotificationMode,
                NotificationChannels = settings.ChatClients.Discord.NotificationChannels.ToObject<string[]>(),
                AutomaticallyPurgeCommandMessages = (bool)settings.ChatClients.Discord.AutomaticallyPurgeCommandMessages,
                StatusNotifications = settings.ChatClients.Discord.StatusNotifications.ToObject<StatusNotificationSettings>(),
            };

            return new ChatClientsSettings
            {
                Discord = discordSettings,
                Language = settings.ChatClients.Language,
            };
        }
    }
}