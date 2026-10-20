using System.IO;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class SettingsUpgradeTests
    {
        [Fact]
        public void Upgrade_From213_AddsStatusNotificationDefaults()
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "{\"Version\":\"2.1.3\",\"ChatClients\":{\"Discord\":{}}}");

            SettingsFileUpgrader.Upgrade(path);

            var json = JObject.Parse(File.ReadAllText(path));
            var status = json["ChatClients"]["Discord"]["StatusNotifications"];
            Assert.Equal("2.1.4", (string)json["Version"]);
            Assert.False((bool)status["Enabled"]);
            Assert.Equal(24, (int)status["NoReleaseHours"]);
            Assert.Equal(3, (int)status["RenotifyMaxCount"]);
        }
    }
}
