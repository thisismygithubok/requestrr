using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Requestrr.WebApi.RequestrrBot.Locale;
using Requestrr.WebApi.RequestrrBot.Notifications.Status;
using Requestrr.WebApi.RequestrrBot.Notifications.TvShows;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class RequestStatusMonitorTests
    {
        private static readonly DateTime Now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        private readonly RequestStatusRepository _status = new RequestStatusRepository(new Dictionary<string, StatusSubscription>(), _ => { });
        private readonly Dictionary<int, HashSet<string>> _movieSubscriptions = new Dictionary<int, HashSet<string>>();
        private readonly Dictionary<int, TvShowNotification[]> _tvSubscriptions = new Dictionary<int, TvShowNotification[]>();
        private readonly FakeMovieProvider _movies = new FakeMovieProvider();
        private readonly FakeTvProvider _tv = new FakeTvProvider();
        private readonly FakeSender _sender = new FakeSender();

        public RequestStatusMonitorTests()
        {
            Language.Current = TestLanguage.English;
        }

        private RequestStatusMonitor CreateMonitor()
        {
            return new RequestStatusMonitor(
                new StatusNotificationSettings { Enabled = true },
                _status,
                () => _movieSubscriptions, _movies,
                () => _tvSubscriptions, _tv,
                () => new Dictionary<string, HashSet<string>>(), null,
                _sender,
                NullLogger.Instance,
                () => Now);
        }

        private void SubscribeMovie(string userId, int movieId, double requestedHoursAgo)
        {
            if (!_movieSubscriptions.TryGetValue(movieId, out var users))
                _movieSubscriptions[movieId] = users = new HashSet<string>();

            users.Add(userId);
            _status.Start(RequestStatusKeys.Movie(userId, movieId), Now.AddHours(-requestedHoursAgo));
        }

        private static ArrItemStatus MissingMovie()
        {
            return new ArrItemStatus { Title = "Dune", Units = new[] { new StatusUnit { Key = "movie", Label = "Dune" } } };
        }

        [Fact]
        public async Task RunOnce_DueAlert_SendsAndMarks()
        {
            SubscribeMovie("u1", 603, 25);
            _movies.Result = MissingMovie();

            await CreateMonitor().RunOnceAsync(CancellationToken.None);

            var sent = Assert.Single(_sender.Sent);
            Assert.Equal("u1", sent.UserId);
            Assert.Contains("No suitable release found yet: Dune", sent.Message);
            Assert.Equal(1, _status.Get(RequestStatusKeys.Movie("u1", 603)).Units["movie|NoRelease"].NotifiedCount);
        }

        [Fact]
        public async Task RunOnce_SecondRunSameState_NoResend()
        {
            SubscribeMovie("u1", 603, 25);
            _movies.Result = MissingMovie();
            var monitor = CreateMonitor();

            await monitor.RunOnceAsync(CancellationToken.None);
            await monitor.RunOnceAsync(CancellationToken.None);

            Assert.Single(_sender.Sent);
        }

        [Fact]
        public async Task RunOnce_ProviderThrows_NoSendNoMark()
        {
            SubscribeMovie("u1", 603, 25);
            _movies.Throw = true;

            await CreateMonitor().RunOnceAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
            Assert.Empty(_status.Get(RequestStatusKeys.Movie("u1", 603)).Units);
        }

        [Fact]
        public async Task RunOnce_ProviderReturnsNull_NoSend()
        {
            SubscribeMovie("u1", 603, 25);
            _movies.Result = null;

            await CreateMonitor().RunOnceAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task RunOnce_TwoUsersSameMovie_OneProviderCall_IndependentClocks()
        {
            SubscribeMovie("u1", 603, 25);
            SubscribeMovie("u2", 603, 1);
            _movies.Result = MissingMovie();

            await CreateMonitor().RunOnceAsync(CancellationToken.None);

            Assert.Equal(1, _movies.Calls);
            var sent = Assert.Single(_sender.Sent);
            Assert.Equal("u1", sent.UserId);
        }

        [Fact]
        public async Task RunOnce_LegacySubscription_Skipped()
        {
            _movieSubscriptions[603] = new HashSet<string> { "u1" };
            _movies.Result = MissingMovie();

            await CreateMonitor().RunOnceAsync(CancellationToken.None);

            Assert.Empty(_sender.Sent);
        }

        [Fact]
        public async Task RunOnce_SendFails_NotMarked()
        {
            SubscribeMovie("u1", 603, 25);
            _movies.Result = MissingMovie();
            _sender.Succeed = false;

            await CreateMonitor().RunOnceAsync(CancellationToken.None);

            Assert.Single(_sender.Sent);
            Assert.Empty(_status.Get(RequestStatusKeys.Movie("u1", 603)).Units);
        }

        [Fact]
        public async Task RunOnce_TvSeason_EvaluatedPerSeasonNotification()
        {
            _tvSubscriptions[81189] = new TvShowNotification[] { new SeasonNotification("u1", 81189, 2) };
            _status.Start(RequestStatusKeys.TvSeason("u1", 81189, 2), Now.AddHours(-30));
            _tv.Result = new ArrItemStatus { Title = "Severance", Units = new[] { new StatusUnit { Key = "e1", Label = "S02E04" } } };

            await CreateMonitor().RunOnceAsync(CancellationToken.None);

            Assert.Equal(2, _tv.LastSeason);
            Assert.Contains("S02E04", Assert.Single(_sender.Sent).Message);
        }

        [Fact]
        public async Task RunOnce_SeasonAndFutureSeasonsSameSeason_SendsOnce()
        {
            _tvSubscriptions[81189] = new TvShowNotification[] { new SeasonNotification("u1", 81189, 2), new FutureSeasonsNotification("u1", 81189, 2) };
            _status.Start(RequestStatusKeys.TvSeason("u1", 81189, 2), Now.AddHours(-30));
            _tv.Result = new ArrItemStatus { Title = "Severance", Units = new[] { new StatusUnit { Key = "e1", Label = "S02E04" } } };

            await CreateMonitor().RunOnceAsync(CancellationToken.None);

            Assert.Single(_sender.Sent);
            Assert.Equal(1, _status.Get(RequestStatusKeys.TvSeason("u1", 81189, 2)).Units["e1|NoRelease"].NotifiedCount);
        }

        private class FakeMovieProvider : IMovieStatusProvider
        {
            public ArrItemStatus Result { get; set; }
            public bool Throw { get; set; }
            public int Calls { get; private set; }

            public Task<ArrItemStatus> GetMovieStatusAsync(int theMovieDbId, CancellationToken token)
            {
                Calls++;
                if (Throw)
                    throw new Exception("Radarr unreachable");

                return Task.FromResult(Result);
            }
        }

        private class FakeTvProvider : ITvShowStatusProvider
        {
            public ArrItemStatus Result { get; set; }
            public int LastSeason { get; private set; }

            public Task<ArrItemStatus> GetSeasonStatusAsync(int theTvDbId, int seasonNumber, CancellationToken token)
            {
                LastSeason = seasonNumber;
                return Task.FromResult(Result);
            }
        }

        private class FakeSender : IStatusMessageSender
        {
            public List<(string UserId, string Message)> Sent { get; } = new List<(string UserId, string Message)>();
            public bool Succeed { get; set; } = true;
            public bool UsesChannels => false;

            public Task<bool> SendAsync(string userId, string message, CancellationToken token)
            {
                Sent.Add((userId, message));
                return Task.FromResult(Succeed);
            }
        }
    }
}
