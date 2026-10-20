using System.Linq;
using Requestrr.WebApi.RequestrrBot.TvShows;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class TvSeasonSubscriptionsTests
    {
        private static readonly TvShow Show = new TvShow
        {
            TheTvDbId = 81189,
            Seasons = new TvSeason[]
            {
                new AllTvSeasons { SeasonNumber = 0 },
                new NormalTvSeason { SeasonNumber = 1, IsAvailable = true },
                new NormalTvSeason { SeasonNumber = 2 },
                new NormalTvSeason { SeasonNumber = 3 },
            }
        };

        [Fact]
        public void AllSeasons_SubscribesEveryUnavailableNormalSeason()
        {
            var seasons = TvSeasonSubscriptions.For(Show, Show.Seasons[0]);

            Assert.Equal(new[] { 2, 3 }, seasons.Select(x => x.SeasonNumber));
        }

        [Fact]
        public void NormalSeason_SubscribesOnlyThatSeason()
        {
            Assert.Same(Show.Seasons[2], Assert.Single(TvSeasonSubscriptions.For(Show, Show.Seasons[2])));
        }
    }
}
