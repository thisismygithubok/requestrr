using Requestrr.WebApi.RequestrrBot.Notifications.Status;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class RequestStatusMessageBuilderTests
    {
        private static StatusAlert Alert(string label, RequestStatusState state, ArrQueueItem queue = null)
        {
            return new StatusAlert { Unit = new StatusUnit { Key = label, Label = label, Queue = queue }, State = state };
        }

        [Fact]
        public void FormatEta_Hours()
        {
            Assert.Equal("2h 10m", RequestStatusMessageBuilder.FormatEta("02:10:00"));
        }

        [Fact]
        public void FormatEta_Days()
        {
            Assert.Equal("1d 2h 10m", RequestStatusMessageBuilder.FormatEta("1.02:10:00"));
        }

        [Fact]
        public void FormatEta_MinutesOnly()
        {
            Assert.Equal("5m", RequestStatusMessageBuilder.FormatEta("00:05:00"));
        }

        [Fact]
        public void FormatEta_Null_Unknown()
        {
            Assert.Equal("unknown", RequestStatusMessageBuilder.FormatEta(null));
            Assert.Equal("unknown", RequestStatusMessageBuilder.FormatEta("soon"));
        }

        [Fact]
        public void BuildStatus_GroupsByStateInOrder()
        {
            var message = RequestStatusMessageBuilder.BuildStatus(TestLanguage.English, "Severance", new[]
            {
                Alert("S02E04", RequestStatusState.NoRelease),
                Alert("S02E09", RequestStatusState.Downloading, new ArrQueueItem { Size = 200, SizeLeft = 50, TimeLeft = "02:10:00" }),
                Alert("S02E07", RequestStatusState.NoRelease),
                Alert("S02E01", RequestStatusState.Stuck, new ArrQueueItem { TrackedDownloadStatus = "warning" }),
            }, null);

            Assert.Equal(
                "Status update for **Severance** that you requested:\n"
                + "- Stuck in the download queue, an admin needs to take a look: S02E01\n"
                + "- Still downloading: S02E09 (75%, ETA 2h 10m)\n"
                + "- No suitable release found yet: S02E04, S02E07",
                message);
        }

        [Fact]
        public void BuildStatus_ChannelIncludesMention()
        {
            var message = RequestStatusMessageBuilder.BuildStatus(TestLanguage.English, "Dune", new[] { Alert("Dune", RequestStatusState.NoRelease) }, "<@42>");

            Assert.StartsWith("<@42> status update for **Dune** that you requested:", message);
        }

        [Fact]
        public void BuildRecheck_FailureDM()
        {
            Assert.Equal(
                "Your request for **Dune** really failed. Please try again or contact an admin.",
                RequestStatusMessageBuilder.BuildRecheck(TestLanguage.English, "Dune", false, null));
        }

        [Fact]
        public void BuildRecheck_SuccessChannel()
        {
            Assert.Equal(
                "<@42> your request for **Dune** did go through despite the earlier error.",
                RequestStatusMessageBuilder.BuildRecheck(TestLanguage.English, "Dune", true, "<@42>"));
        }
    }
}
