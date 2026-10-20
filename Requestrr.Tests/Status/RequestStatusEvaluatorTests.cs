using System;
using System.Collections.Generic;
using Requestrr.WebApi.RequestrrBot.Notifications.Status;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class RequestStatusEvaluatorTests
    {
        private static readonly DateTime Now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        private static StatusUnit Unit(bool hasFile = false, ArrQueueItem queue = null, DateTime? releasedAt = null)
        {
            return new StatusUnit { Key = "movie", Label = "Movie", HasFile = hasFile, Queue = queue, ReleasedAtUtc = releasedAt };
        }

        private static StatusSubscription RequestedHoursAgo(double hours)
        {
            return new StatusSubscription { RequestedAtUtc = Now.AddHours(-hours) };
        }

        private static StatusEvaluation Evaluate(StatusUnit unit, StatusSubscription subscription, StatusNotificationSettings settings = null)
        {
            return RequestStatusEvaluator.Evaluate(new[] { unit }, subscription, settings ?? new StatusNotificationSettings(), Now);
        }

        [Fact]
        public void Classify_HasFile_ReturnsNull()
        {
            Assert.Null(RequestStatusEvaluator.Classify(Unit(hasFile: true)));
        }

        [Fact]
        public void Classify_NoQueue_NoRelease()
        {
            Assert.Equal(RequestStatusState.NoRelease, RequestStatusEvaluator.Classify(Unit()));
        }

        [Fact]
        public void Classify_QueueWarning_Stuck()
        {
            Assert.Equal(RequestStatusState.Stuck, RequestStatusEvaluator.Classify(Unit(queue: new ArrQueueItem { TrackedDownloadStatus = "warning" })));
        }

        [Fact]
        public void Classify_QueueImportBlocked_Stuck()
        {
            Assert.Equal(RequestStatusState.Stuck, RequestStatusEvaluator.Classify(Unit(queue: new ArrQueueItem { TrackedDownloadStatus = "ok", TrackedDownloadState = "importBlocked" })));
        }

        [Fact]
        public void Classify_QueueOk_Downloading()
        {
            Assert.Equal(RequestStatusState.Downloading, RequestStatusEvaluator.Classify(Unit(queue: new ArrQueueItem { Status = "downloading", TrackedDownloadStatus = "ok", TrackedDownloadState = "downloading" })));
        }

        [Fact]
        public void Evaluate_NoReleaseBeforeThreshold_NothingDue()
        {
            var result = Evaluate(Unit(), RequestedHoursAgo(23));

            Assert.Empty(result.Due);
            Assert.Empty(result.Problems);
        }

        [Fact]
        public void Evaluate_NoReleaseAfterThreshold_Due()
        {
            var result = Evaluate(Unit(), RequestedHoursAgo(25));

            var alert = Assert.Single(result.Due);
            Assert.Equal(RequestStatusState.NoRelease, alert.State);
        }

        [Fact]
        public void Evaluate_StuckAfterOneHour_Due()
        {
            var result = Evaluate(Unit(queue: new ArrQueueItem { TrackedDownloadStatus = "error" }), RequestedHoursAgo(2));

            var alert = Assert.Single(result.Due);
            Assert.Equal(RequestStatusState.Stuck, alert.State);
        }

        [Fact]
        public void Evaluate_ClockStartsAtReleaseDate()
        {
            var result = Evaluate(Unit(releasedAt: Now.AddHours(-2)), RequestedHoursAgo(48));

            Assert.Empty(result.Due);
            Assert.Empty(result.Problems);
        }

        [Fact]
        public void Evaluate_AlreadyNotified_RenotifyDisabled_NotDue_ButStillProblem()
        {
            var subscription = RequestedHoursAgo(50);
            subscription.Units[RequestStatusEvaluator.TrackingKey("movie", RequestStatusState.NoRelease)] = new UnitNotification { LastNotifiedAtUtc = Now.AddHours(-25), NotifiedCount = 1 };

            var result = Evaluate(Unit(), subscription);

            Assert.Empty(result.Due);
            Assert.Single(result.Problems);
        }

        [Fact]
        public void Evaluate_RenotifyIntervalElapsed_Due()
        {
            var subscription = RequestedHoursAgo(50);
            subscription.Units[RequestStatusEvaluator.TrackingKey("movie", RequestStatusState.NoRelease)] = new UnitNotification { LastNotifiedAtUtc = Now.AddHours(-25), NotifiedCount = 1 };

            var result = Evaluate(Unit(), subscription, new StatusNotificationSettings { RenotifyEnabled = true });

            Assert.Single(result.Due);
        }

        [Fact]
        public void Evaluate_RenotifyMaxReached_NotDue()
        {
            var subscription = RequestedHoursAgo(200);
            subscription.Units[RequestStatusEvaluator.TrackingKey("movie", RequestStatusState.NoRelease)] = new UnitNotification { LastNotifiedAtUtc = Now.AddHours(-25), NotifiedCount = 4 };

            var result = Evaluate(Unit(), subscription, new StatusNotificationSettings { RenotifyEnabled = true });

            Assert.Empty(result.Due);
            Assert.Single(result.Problems);
        }

        [Fact]
        public void Evaluate_StateChange_IsNewDue()
        {
            var subscription = RequestedHoursAgo(13);
            subscription.Units[RequestStatusEvaluator.TrackingKey("movie", RequestStatusState.NoRelease)] = new UnitNotification { LastNotifiedAtUtc = Now.AddHours(-1), NotifiedCount = 1 };

            var result = Evaluate(Unit(queue: new ArrQueueItem { TrackedDownloadStatus = "ok", TrackedDownloadState = "downloading" }), subscription);

            var alert = Assert.Single(result.Due);
            Assert.Equal(RequestStatusState.Downloading, alert.State);
        }

        [Fact]
        public void Validate_ZeroHours_ReturnsError()
        {
            Assert.NotNull(new StatusNotificationSettings { StuckHours = 0 }.Validate());
        }

        [Fact]
        public void Validate_Defaults_ReturnsNull()
        {
            Assert.Null(new StatusNotificationSettings().Validate());
        }
    }
}
