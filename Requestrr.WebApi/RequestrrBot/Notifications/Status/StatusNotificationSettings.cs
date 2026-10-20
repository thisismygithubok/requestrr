namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public record StatusNotificationSettings
    {
        public bool Enabled { get; set; } = false;
        public int NoReleaseHours { get; set; } = 24;
        public int DownloadingHours { get; set; } = 12;
        public int StuckHours { get; set; } = 1;
        public bool RenotifyEnabled { get; set; } = false;
        public int RenotifyIntervalHours { get; set; } = 24;
        public int RenotifyMaxCount { get; set; } = 3;

        public string Validate()
        {
            if (NoReleaseHours < 1 || DownloadingHours < 1 || StuckHours < 1 || RenotifyIntervalHours < 1 || RenotifyMaxCount < 1)
            {
                return "Invalid status notification settings, hour values and maximum count must be at least 1.";
            }

            return null;
        }
    }
}
