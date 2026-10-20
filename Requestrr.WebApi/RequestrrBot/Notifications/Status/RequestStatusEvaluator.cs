using System;
using System.Collections.Generic;
using System.Linq;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public static class RequestStatusEvaluator
    {
        public static RequestStatusState? Classify(StatusUnit unit)
        {
            if (unit.HasFile)
                return null;

            if (unit.Queue == null)
                return RequestStatusState.NoRelease;

            return unit.Queue.IsStuck ? RequestStatusState.Stuck : RequestStatusState.Downloading;
        }

        public static string TrackingKey(string unitKey, RequestStatusState state)
        {
            return $"{unitKey}|{state}";
        }

        public static StatusEvaluation Evaluate(IEnumerable<StatusUnit> units, StatusSubscription subscription, StatusNotificationSettings settings, DateTime nowUtc)
        {
            var problems = new List<StatusAlert>();
            var due = new List<StatusAlert>();

            foreach (var unit in units)
            {
                var state = Classify(unit);
                if (state == null)
                    continue;

                var clockStart = unit.ReleasedAtUtc.HasValue && unit.ReleasedAtUtc.Value > subscription.RequestedAtUtc
                    ? unit.ReleasedAtUtc.Value
                    : subscription.RequestedAtUtc;

                // Download and queue problems are timed from when the item entered the queue, not from the request.
                if (state != RequestStatusState.NoRelease && unit.Queue.AddedAtUtc.HasValue && unit.Queue.AddedAtUtc.Value > clockStart)
                    clockStart = unit.Queue.AddedAtUtc.Value;

                if (nowUtc - clockStart < TimeSpan.FromHours(ThresholdHours(state.Value, settings)))
                    continue;

                var alert = new StatusAlert { Unit = unit, State = state.Value };
                problems.Add(alert);

                if (!subscription.Units.TryGetValue(TrackingKey(unit.Key, state.Value), out var notified)
                    || (settings.RenotifyEnabled
                        && nowUtc - notified.LastNotifiedAtUtc >= TimeSpan.FromHours(settings.RenotifyIntervalHours)
                        && notified.NotifiedCount <= settings.RenotifyMaxCount))
                {
                    due.Add(alert);
                }
            }

            return new StatusEvaluation { Due = due, Problems = problems };
        }

        private static int ThresholdHours(RequestStatusState state, StatusNotificationSettings settings)
        {
            switch (state)
            {
                case RequestStatusState.Stuck:
                    return settings.StuckHours;
                case RequestStatusState.Downloading:
                    return settings.DownloadingHours;
                default:
                    return settings.NoReleaseHours;
            }
        }
    }
}
