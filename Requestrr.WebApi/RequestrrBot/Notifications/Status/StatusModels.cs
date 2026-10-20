using System;
using System.Collections.Generic;
using System.Linq;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public enum RequestStatusState
    {
        NoRelease,
        Downloading,
        Stuck
    }

    public class ArrQueueItem
    {
        private static readonly string[] StuckStates = { "importBlocked", "importPending", "failedPending", "failed" };
        private static readonly string[] StuckStatuses = { "warning", "failed" };

        public int? MovieId { get; set; }
        public int? EpisodeId { get; set; }
        public int? AlbumId { get; set; }
        public string Status { get; set; }
        public string TrackedDownloadStatus { get; set; }
        public string TrackedDownloadState { get; set; }
        public double Size { get; set; }
        public double SizeLeft { get; set; }
        public string TimeLeft { get; set; }
        public DateTime? AddedAtUtc { get; set; }

        public bool IsStuck =>
            Is(TrackedDownloadStatus, "warning", "error")
            || Is(TrackedDownloadState, StuckStates)
            || Is(Status, StuckStatuses);

        private static bool Is(string value, params string[] candidates)
        {
            return value != null && candidates.Any(x => x.Equals(value, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class StatusUnit
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public bool HasFile { get; set; }
        public DateTime? ReleasedAtUtc { get; set; }
        public ArrQueueItem Queue { get; set; }
    }

    public class ArrItemStatus
    {
        public string Title { get; set; }
        public IReadOnlyList<StatusUnit> Units { get; set; }
    }

    public class UnitNotification
    {
        public DateTime LastNotifiedAtUtc { get; set; }
        public int NotifiedCount { get; set; }
    }

    public class StatusSubscription
    {
        public DateTime RequestedAtUtc { get; set; }
        public Dictionary<string, UnitNotification> Units { get; set; } = new Dictionary<string, UnitNotification>();
    }

    public class StatusAlert
    {
        public StatusUnit Unit { get; set; }
        public RequestStatusState State { get; set; }
    }

    public class StatusEvaluation
    {
        public IReadOnlyList<StatusAlert> Due { get; set; }
        public IReadOnlyList<StatusAlert> Problems { get; set; }
    }
}
