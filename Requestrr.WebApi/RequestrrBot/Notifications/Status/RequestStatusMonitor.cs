using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Locale;
using Requestrr.WebApi.RequestrrBot.Notifications.TvShows;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public class RequestStatusMonitor
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

        private readonly StatusNotificationSettings _settings;
        private readonly RequestStatusRepository _statusRepository;
        private readonly Func<Dictionary<int, HashSet<string>>> _movieSubscriptions;
        private readonly IMovieStatusProvider _movieProvider;
        private readonly Func<Dictionary<int, TvShowNotification[]>> _tvShowSubscriptions;
        private readonly ITvShowStatusProvider _tvShowProvider;
        private readonly Func<Dictionary<string, HashSet<string>>> _musicSubscriptions;
        private readonly IMusicStatusProvider _musicProvider;
        private readonly IStatusMessageSender _sender;
        private readonly ILogger _logger;
        private readonly Func<DateTime> _utcNow;
        private Task _monitorTask = null;
        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        // Providers are null when the media type is not handled by Radarr/Sonarr/Lidarr.
        public RequestStatusMonitor(
            StatusNotificationSettings settings,
            RequestStatusRepository statusRepository,
            Func<Dictionary<int, HashSet<string>>> movieSubscriptions,
            IMovieStatusProvider movieProvider,
            Func<Dictionary<int, TvShowNotification[]>> tvShowSubscriptions,
            ITvShowStatusProvider tvShowProvider,
            Func<Dictionary<string, HashSet<string>>> musicSubscriptions,
            IMusicStatusProvider musicProvider,
            IStatusMessageSender sender,
            ILogger logger,
            Func<DateTime> utcNow)
        {
            _settings = settings;
            _statusRepository = statusRepository;
            _movieSubscriptions = movieSubscriptions;
            _movieProvider = movieProvider;
            _tvShowSubscriptions = tvShowSubscriptions;
            _tvShowProvider = tvShowProvider;
            _musicSubscriptions = musicSubscriptions;
            _musicProvider = musicProvider;
            _sender = sender;
            _logger = logger;
            _utcNow = utcNow;
        }

        public void Start()
        {
            _monitorTask = Task.Run(async () =>
            {
                while (!_tokenSource.IsCancellationRequested)
                {
                    await RunOnceAsync(_tokenSource.Token);
                    await Task.Delay(Interval, _tokenSource.Token);
                }
            }, _tokenSource.Token);
        }

        public async Task StopAsync()
        {
            try
            {
                _tokenSource.Cancel();
                await _monitorTask;
            }
            catch (System.Exception)
            {
                _tokenSource.Dispose();
                _tokenSource = new CancellationTokenSource();
            }
        }

        public async Task RunOnceAsync(CancellationToken token)
        {
            if (_movieProvider != null)
            {
                foreach (var movie in _movieSubscriptions())
                {
                    await ProcessItemAsync(() => _movieProvider.GetMovieStatusAsync(movie.Key, token),
                        movie.Value.Select(userId => (userId, RequestStatusKeys.Movie(userId, movie.Key))), $"movie {movie.Key}", token);
                }
            }

            if (_tvShowProvider != null)
            {
                foreach (var tvShow in _tvShowSubscriptions())
                {
                    foreach (var season in tvShow.Value.GroupBy(x => x.SeasonNumber))
                    {
                        await ProcessItemAsync(() => _tvShowProvider.GetSeasonStatusAsync(tvShow.Key, season.Key, token),
                            season.Select(x => (x.UserId, RequestStatusKeys.TvSeason(x.UserId, tvShow.Key, season.Key))), $"tv show {tvShow.Key} season {season.Key}", token);
                    }
                }
            }

            if (_musicProvider != null)
            {
                foreach (var artist in _musicSubscriptions())
                {
                    await ProcessItemAsync(() => _musicProvider.GetArtistStatusAsync(artist.Key, token),
                        artist.Value.Select(userId => (userId, RequestStatusKeys.Music(userId, artist.Key))), $"music artist {artist.Key}", token);
                }
            }
        }

        private async Task ProcessItemAsync(Func<Task<ArrItemStatus>> getStatus, IEnumerable<(string UserId, string Key)> subscribers, string description, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                var subscriptions = subscribers
                    .Select(x => (x.UserId, x.Key, Subscription: _statusRepository.Get(x.Key)))
                    .Where(x => x.Subscription != null)
                    .ToArray();

                if (!subscriptions.Any())
                    return;

                var status = await getStatus();
                if (status == null)
                    return;

                var now = _utcNow();

                foreach (var subscription in subscriptions)
                {
                    var evaluation = RequestStatusEvaluator.Evaluate(status.Units, subscription.Subscription, _settings, now);
                    if (!evaluation.Due.Any())
                        continue;

                    var message = RequestStatusMessageBuilder.BuildStatus(Language.Current, status.Title, evaluation.Problems, _sender.UsesChannels ? $"<@{subscription.UserId}>" : null);

                    if (await _sender.SendAsync(subscription.UserId, message, token))
                    {
                        _statusRepository.MarkNotified(subscription.Key, evaluation.Due.Select(x => RequestStatusEvaluator.TrackingKey(x.Unit.Key, x.State)), now);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred while checking the request status of {description}: " + ex.Message);
            }
        }
    }
}
