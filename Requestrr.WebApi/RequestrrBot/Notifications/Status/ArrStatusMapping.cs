using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public static class ArrStatusMapping
    {
        public static ArrItemStatus MapMovie(JObject movie, IReadOnlyList<ArrQueueItem> queue)
        {
            var id = (int)movie["id"];
            var units = new List<StatusUnit>();

            if (((bool?)movie["monitored"] ?? false) && ((bool?)movie["isAvailable"] ?? true))
            {
                units.Add(new StatusUnit
                {
                    Key = "movie",
                    Label = (string)movie["title"],
                    HasFile = (bool?)movie["hasFile"] ?? false,
                    Queue = queue.FirstOrDefault(x => x.MovieId == id),
                });
            }

            return new ArrItemStatus { Title = (string)movie["title"], Units = units };
        }

        public static ArrItemStatus MapSeason(string seriesTitle, JArray episodes, int seasonNumber, IReadOnlyList<ArrQueueItem> queue, DateTime nowUtc)
        {
            var units = episodes
                .Where(x => (int)x["seasonNumber"] == seasonNumber && ((bool?)x["monitored"] ?? false))
                .Select(x => new { Episode = x, AiredAt = ToUtc(x["airDateUtc"]) })
                .Where(x => x.AiredAt.HasValue && x.AiredAt.Value <= nowUtc)
                .Select(x => new StatusUnit
                {
                    Key = $"e{(int)x.Episode["id"]}",
                    Label = $"S{seasonNumber:00}E{(int)x.Episode["episodeNumber"]:00}",
                    HasFile = (bool?)x.Episode["hasFile"] ?? false,
                    ReleasedAtUtc = x.AiredAt,
                    Queue = queue.FirstOrDefault(q => q.EpisodeId == (int)x.Episode["id"]),
                })
                .ToArray();

            return new ArrItemStatus { Title = seriesTitle, Units = units };
        }

        public static ArrItemStatus MapArtist(string artistName, JArray albums, IReadOnlyList<ArrQueueItem> queue, DateTime nowUtc)
        {
            var units = albums
                .Where(x => (bool?)x["monitored"] ?? false)
                .Select(x => new { Album = x, ReleasedAt = ToUtc(x["releaseDate"]) })
                .Where(x => x.ReleasedAt.HasValue && x.ReleasedAt.Value <= nowUtc)
                .Select(x => new StatusUnit
                {
                    Key = $"a{(int)x.Album["id"]}",
                    Label = (string)x.Album["title"],
                    HasFile = ((int?)x.Album["statistics"]?["trackFileCount"] ?? 0) > 0,
                    ReleasedAtUtc = x.ReleasedAt,
                    Queue = queue.FirstOrDefault(q => q.AlbumId == (int)x.Album["id"]),
                })
                .ToArray();

            return new ArrItemStatus { Title = artistName, Units = units };
        }

        private static DateTime? ToUtc(JToken token)
        {
            var value = (DateTime?)token;
            return value?.ToUniversalTime();
        }
    }
}
