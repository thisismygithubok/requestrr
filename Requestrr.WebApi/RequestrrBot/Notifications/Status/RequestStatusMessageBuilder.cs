using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Requestrr.WebApi.RequestrrBot.Locale;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public static class RequestStatusMessageBuilder
    {
        private static readonly RequestStatusState[] StateOrder = { RequestStatusState.Stuck, RequestStatusState.Downloading, RequestStatusState.NoRelease };

        // userMention == null means the message is sent as a private message.
        public static string BuildStatus(Language language, string title, IReadOnlyList<StatusAlert> problems, string userMention)
        {
            var header = userMention == null ? language.DiscordNotificationStatusHeaderDM : language.DiscordNotificationStatusHeaderChannel;
            var lines = new List<string> { ReplaceTokens(header, title, userMention) };

            foreach (var state in StateOrder)
            {
                var items = problems.Where(x => x.State == state).Select(x => state == RequestStatusState.Downloading ? DescribeDownload(x.Unit) : x.Unit.Label).ToArray();
                if (items.Any())
                {
                    lines.Add("- " + StateTemplate(language, state).Replace(LanguageTokens.StatusItems, string.Join(", ", items)));
                }
            }

            return string.Join("\n", lines);
        }

        public static string BuildRecheck(Language language, string title, bool succeeded, string userMention)
        {
            var template = succeeded
                ? (userMention == null ? language.DiscordNotificationRecheckSuccessDM : language.DiscordNotificationRecheckSuccessChannel)
                : (userMention == null ? language.DiscordNotificationRecheckFailureDM : language.DiscordNotificationRecheckFailureChannel);

            return ReplaceTokens(template, title, userMention);
        }

        public static string FormatEta(string timeLeft)
        {
            if (!TimeSpan.TryParse(timeLeft, CultureInfo.InvariantCulture, out var eta))
                return "unknown";

            var parts = new List<string>();
            if (eta.Days > 0) parts.Add($"{eta.Days}d");
            if (eta.Hours > 0) parts.Add($"{eta.Hours}h");
            if (eta.Minutes > 0 || !parts.Any()) parts.Add($"{eta.Minutes}m");

            return string.Join(" ", parts);
        }

        private static string DescribeDownload(StatusUnit unit)
        {
            var queue = unit.Queue;
            var percent = queue.Size > 0 ? (int)Math.Round((queue.Size - queue.SizeLeft) / queue.Size * 100) : 0;
            return $"{unit.Label} ({percent}%, ETA {FormatEta(queue.TimeLeft)})";
        }

        private static string StateTemplate(Language language, RequestStatusState state)
        {
            switch (state)
            {
                case RequestStatusState.Stuck:
                    return language.DiscordNotificationStatusStuck;
                case RequestStatusState.Downloading:
                    return language.DiscordNotificationStatusDownloading;
                default:
                    return language.DiscordNotificationStatusNoRelease;
            }
        }

        private static string ReplaceTokens(string template, string title, string userMention)
        {
            return template.Replace(LanguageTokens.StatusTitle, title).Replace(LanguageTokens.UserMention, userMention ?? string.Empty);
        }
    }
}
