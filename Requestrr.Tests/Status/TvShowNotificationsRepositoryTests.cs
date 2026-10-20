using System;
using System.Collections.Generic;
using Requestrr.WebApi.RequestrrBot.Notifications.Status;
using Requestrr.WebApi.RequestrrBot.Notifications.TvShows;
using Requestrr.WebApi.RequestrrBot.TvShows;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class TvShowNotificationsRepositoryTests
    {
        [Fact]
        public void FutureSeasonsMovesOn_ReplacesStatusRecord()
        {
            var status = new RequestStatusRepository(new Dictionary<string, StatusSubscription>(), _ => { });
            var repository = new TvShowNotificationsRepository(status, _ => { });

            repository.AddSeasonNotification("u1", 81189, new FutureTvSeasons { SeasonNumber = 2 });
            repository.AddSeasonNotification("u1", 81189, new FutureTvSeasons { SeasonNumber = 3 });

            Assert.Null(status.Get(RequestStatusKeys.TvSeason("u1", 81189, 2)));
            Assert.NotNull(status.Get(RequestStatusKeys.TvSeason("u1", 81189, 3)));
        }
    }
}
