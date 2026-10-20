using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public static class ArrQueueParser
    {
        // Accepts both queue/details (array) and paged queue ({ records: [...] }) responses.
        public static IReadOnlyList<ArrQueueItem> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<ArrQueueItem>();

            var token = JToken.Parse(json);
            var records = token is JArray array ? array : token["records"] as JArray;

            return (records ?? new JArray()).Select(x => new ArrQueueItem
            {
                MovieId = (int?)x["movieId"],
                EpisodeId = (int?)x["episodeId"],
                AlbumId = (int?)x["albumId"],
                Status = (string)x["status"],
                TrackedDownloadStatus = (string)x["trackedDownloadStatus"],
                TrackedDownloadState = (string)x["trackedDownloadState"],
                Size = (double?)x["size"] ?? 0,
                SizeLeft = (double?)x["sizeleft"] ?? 0,
                TimeLeft = (string)x["timeleft"],
            }).ToArray();
        }
    }
}
