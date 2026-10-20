using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public static class RequestStatusKeys
    {
        public static string Movie(string userId, int theMovieDbId) => $"movie:{userId}:{theMovieDbId}";
        public static string TvSeason(string userId, int theTvDbId, int seasonNumber) => $"tv:{userId}:{theTvDbId}:{seasonNumber}";
        public static string Music(string userId, string artistId) => $"music:{userId}:{artistId}";
    }

    public class RequestStatusRepository
    {
        private readonly Dictionary<string, StatusSubscription> _subscriptions;
        private readonly Action<Dictionary<string, StatusSubscription>> _write;
        private object _lock = new object();

        public RequestStatusRepository()
            : this(((JToken)NotificationsFile.Read().Status)?.ToObject<Dictionary<string, StatusSubscription>>() ?? new Dictionary<string, StatusSubscription>(),
                   NotificationsFile.WriteStatus)
        {
        }

        public RequestStatusRepository(Dictionary<string, StatusSubscription> initial, Action<Dictionary<string, StatusSubscription>> write)
        {
            _subscriptions = initial;
            _write = write;
        }

        public void Start(string key, DateTime requestedAtUtc)
        {
            lock (_lock)
            {
                if (_subscriptions.ContainsKey(key))
                    return;

                _subscriptions[key] = new StatusSubscription { RequestedAtUtc = requestedAtUtc };
                _write(_subscriptions);
            }
        }

        public void Backdate(string key, DateTime requestedAtUtc)
        {
            lock (_lock)
            {
                if (_subscriptions.TryGetValue(key, out var subscription) && requestedAtUtc < subscription.RequestedAtUtc)
                {
                    subscription.RequestedAtUtc = requestedAtUtc;
                    _write(_subscriptions);
                }
            }
        }

        public StatusSubscription Get(string key)
        {
            lock (_lock)
            {
                if (!_subscriptions.TryGetValue(key, out var subscription))
                    return null;

                return new StatusSubscription
                {
                    RequestedAtUtc = subscription.RequestedAtUtc,
                    Units = subscription.Units.ToDictionary(x => x.Key, x => new UnitNotification { LastNotifiedAtUtc = x.Value.LastNotifiedAtUtc, NotifiedCount = x.Value.NotifiedCount }),
                };
            }
        }

        public void MarkNotified(string key, IEnumerable<string> trackingKeys, DateTime nowUtc)
        {
            lock (_lock)
            {
                if (!_subscriptions.TryGetValue(key, out var subscription))
                    return;

                foreach (var trackingKey in trackingKeys)
                {
                    if (!subscription.Units.TryGetValue(trackingKey, out var unit))
                    {
                        unit = new UnitNotification();
                        subscription.Units[trackingKey] = unit;
                    }

                    unit.LastNotifiedAtUtc = nowUtc;
                    unit.NotifiedCount++;
                }

                _write(_subscriptions);
            }
        }

        public void Remove(string key)
        {
            lock (_lock)
            {
                if (_subscriptions.Remove(key))
                {
                    _write(_subscriptions);
                }
            }
        }
    }
}
