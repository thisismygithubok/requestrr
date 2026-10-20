using System.Collections.Generic;
using System.Linq;

namespace Requestrr.WebApi.RequestrrBot.TvShows
{
    public static class TvSeasonSubscriptions
    {
        // "All seasons" is a placeholder season; requesters are subscribed to each season that is not yet available.
        public static IReadOnlyList<TvSeason> For(TvShow tvShow, TvSeason selectedSeason)
        {
            if (selectedSeason is AllTvSeasons)
                return tvShow.Seasons.OfType<NormalTvSeason>().Where(x => !x.IsAvailable).ToArray();

            return new[] { selectedSeason };
        }
    }
}
