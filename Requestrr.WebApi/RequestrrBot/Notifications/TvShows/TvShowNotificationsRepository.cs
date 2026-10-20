using System;
using System.Collections.Generic;
using Requestrr.WebApi.RequestrrBot.Notifications.Status;
using System.Linq;
using Requestrr.WebApi.RequestrrBot.TvShows;

namespace Requestrr.WebApi.RequestrrBot.Notifications.TvShows
{
    public class TvShowNotificationsRepository
    {
        private HashSet<TvShowNotification> _notifications = new HashSet<TvShowNotification>();

        private object _lock = new object();
        private readonly RequestStatusRepository _statusRepository;

        public TvShowNotificationsRepository(RequestStatusRepository statusRepository)
        {
            _statusRepository = statusRepository;

            foreach (var userNotif in NotificationsFile.Read().TvShows)
            {
                foreach (var tvNotif in userNotif.Notifications)
                {
                    if ((int)tvNotif.IsPermanent != 0)
                    {
                        _notifications.Add(new FutureSeasonsNotification(userNotif.UserId.ToString(), (int)tvNotif.TvShowId, (int)tvNotif.SeasonNumber));
                    }
                    else
                    {
                        _notifications.Add(new SeasonNotification(userNotif.UserId.ToString(), (int)tvNotif.TvShowId, (int)tvNotif.SeasonNumber));
                    }
                }
            }
        }

        public Dictionary<int, TvShowNotification[]> GetAllTvShowNotifications()
        {
            lock (_lock)
            {
                return _notifications
                    .GroupBy(x => x.TvShowId)
                    .ToDictionary(x => x.Key, x => x.ToArray());
            }
        }

        public void AddSeasonNotification(string userId, int tvShowId, TvSeason tvSeason)
        {
            lock (_lock)
            {
                if (tvSeason is FutureTvSeasons)
                {
                    if (_notifications.TryGetValue(new FutureSeasonsNotification(userId, tvShowId), out var previous))
                    {
                        _notifications.Remove(previous);
                        _statusRepository.Remove(RequestStatusKeys.TvSeason(userId, tvShowId, previous.SeasonNumber));
                    }

                    if (_notifications.Add(new FutureSeasonsNotification(userId, tvShowId, tvSeason.SeasonNumber)))
                    {
                        _statusRepository.Start(RequestStatusKeys.TvSeason(userId, tvShowId, tvSeason.SeasonNumber), DateTime.UtcNow);
                        WriteTvShowsNotifications();
                    }
                }
                else if (!_notifications.TryGetValue(new FutureSeasonsNotification(userId, tvShowId), out var notification) || notification.SeasonNumber != tvSeason.SeasonNumber)
                {
                    if (_notifications.Add(new SeasonNotification(userId, tvShowId, tvSeason.SeasonNumber)))
                    {
                        _statusRepository.Start(RequestStatusKeys.TvSeason(userId, tvShowId, tvSeason.SeasonNumber), DateTime.UtcNow);
                        WriteTvShowsNotifications();
                    }
                }
            }
        }

        public void RemoveSeasonNotification(TvShowNotification notification)
        {
            lock (_lock)
            {
                if (_notifications.Remove(notification))
                {
                    _statusRepository.Remove(RequestStatusKeys.TvSeason(notification.UserId, notification.TvShowId, notification.SeasonNumber));
                    WriteTvShowsNotifications();
                }
            }
        }

        public bool HasSeasonNotification(string userId, int tvShowId, TvSeason tvSeason)
        {
            var hasNotification = false;

            lock (_lock)
            {
                var hasFutureNotificationForSeason = _notifications.TryGetValue(new FutureSeasonsNotification(userId, tvShowId), out var notification) && notification.SeasonNumber == tvSeason.SeasonNumber;
                var hasNotificationForSeason = _notifications.Contains(new SeasonNotification(userId, tvShowId, tvSeason.SeasonNumber));

                hasNotification = hasFutureNotificationForSeason || hasNotificationForSeason;
            }

            return hasNotification;
        }

        private void WriteTvShowsNotifications()
        {
            NotificationsFile.WriteTvShows(_notifications.GroupBy(x => x.UserId).ToDictionary(x => x.Key, x => x.Select(n => n).ToArray()));
        }
    }
}