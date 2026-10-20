using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.Notifications.Status;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class ArrStatusMappingTests
    {
        private static readonly DateTime Now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        private static readonly ArrQueueItem[] NoQueue = Array.Empty<ArrQueueItem>();

        [Fact]
        public void Parse_PagedRecords()
        {
            var items = ArrQueueParser.Parse("{\"page\":1,\"records\":[{\"movieId\":5,\"status\":\"downloading\",\"trackedDownloadStatus\":\"ok\",\"trackedDownloadState\":\"downloading\",\"size\":100,\"sizeleft\":25,\"timeleft\":\"01:30:00\"}]}");

            var item = Assert.Single(items);
            Assert.Equal(5, item.MovieId);
            Assert.Equal("ok", item.TrackedDownloadStatus);
            Assert.Equal("downloading", item.TrackedDownloadState);
            Assert.Equal(100, item.Size);
            Assert.Equal(25, item.SizeLeft);
            Assert.Equal("01:30:00", item.TimeLeft);
        }

        [Fact]
        public void Parse_Array()
        {
            var items = ArrQueueParser.Parse("[{\"episodeId\":7},{\"albumId\":9}]");

            Assert.Equal(7, items[0].EpisodeId);
            Assert.Equal(9, items[1].AlbumId);
        }

        [Fact]
        public void Parse_EmptyString_ReturnsEmpty()
        {
            Assert.Empty(ArrQueueParser.Parse(""));
        }

        [Fact]
        public void MapMovie_Unmonitored_NoUnits()
        {
            var status = ArrStatusMapping.MapMovie(JObject.Parse("{\"id\":5,\"title\":\"Dune\",\"monitored\":false,\"isAvailable\":true,\"hasFile\":false}"), NoQueue);

            Assert.Equal("Dune", status.Title);
            Assert.Empty(status.Units);
        }

        [Fact]
        public void MapMovie_NotAvailable_NoUnits()
        {
            var status = ArrStatusMapping.MapMovie(JObject.Parse("{\"id\":5,\"title\":\"Dune\",\"monitored\":true,\"isAvailable\":false,\"hasFile\":false}"), NoQueue);

            Assert.Empty(status.Units);
        }

        [Fact]
        public void MapMovie_InQueue_AttachesQueue()
        {
            var queue = new[] { new ArrQueueItem { MovieId = 4 }, new ArrQueueItem { MovieId = 5, TimeLeft = "00:10:00" } };

            var status = ArrStatusMapping.MapMovie(JObject.Parse("{\"id\":5,\"title\":\"Dune\",\"monitored\":true,\"isAvailable\":true,\"hasFile\":false}"), queue);

            var unit = Assert.Single(status.Units);
            Assert.Equal("movie", unit.Key);
            Assert.Equal("Dune", unit.Label);
            Assert.False(unit.HasFile);
            Assert.Same(queue[1], unit.Queue);
        }

        [Fact]
        public void MapSeason_SkipsUnairedAndUnmonitoredAndOtherSeasons()
        {
            var episodes = JArray.Parse(@"[
                {""id"":1,""seasonNumber"":2,""episodeNumber"":4,""hasFile"":false,""monitored"":true,""airDateUtc"":""2026-01-01T00:00:00Z""},
                {""id"":2,""seasonNumber"":2,""episodeNumber"":5,""hasFile"":false,""monitored"":true,""airDateUtc"":""2026-02-01T00:00:00Z""},
                {""id"":3,""seasonNumber"":2,""episodeNumber"":6,""hasFile"":false,""monitored"":false,""airDateUtc"":""2026-01-01T00:00:00Z""},
                {""id"":4,""seasonNumber"":1,""episodeNumber"":1,""hasFile"":false,""monitored"":true,""airDateUtc"":""2025-01-01T00:00:00Z""},
                {""id"":5,""seasonNumber"":2,""episodeNumber"":7,""hasFile"":false,""monitored"":true}
            ]");

            var status = ArrStatusMapping.MapSeason("Severance", episodes, 2, new[] { new ArrQueueItem { EpisodeId = 1 } }, Now);

            Assert.Equal("Severance", status.Title);
            var unit = Assert.Single(status.Units);
            Assert.Equal("e1", unit.Key);
            Assert.Equal("S02E04", unit.Label);
            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), unit.ReleasedAtUtc);
            Assert.NotNull(unit.Queue);
        }

        [Fact]
        public void MapArtist_SkipsUnreleasedAndUnmonitored()
        {
            var albums = JArray.Parse(@"[
                {""id"":1,""title"":""Old"",""monitored"":true,""releaseDate"":""2020-01-01T00:00:00Z"",""statistics"":{""trackFileCount"":0}},
                {""id"":2,""title"":""Future"",""monitored"":true,""releaseDate"":""2027-01-01T00:00:00Z"",""statistics"":{""trackFileCount"":0}},
                {""id"":3,""title"":""Ignored"",""monitored"":false,""releaseDate"":""2020-01-01T00:00:00Z"",""statistics"":{""trackFileCount"":0}}
            ]");

            var status = ArrStatusMapping.MapArtist("Band", albums, new[] { new ArrQueueItem { AlbumId = 1 } }, Now);

            var unit = Assert.Single(status.Units);
            Assert.Equal("a1", unit.Key);
            Assert.Equal("Old", unit.Label);
            Assert.False(unit.HasFile);
            Assert.NotNull(unit.Queue);
        }

        [Fact]
        public void MapArtist_TrackFiles_HasFileTrue()
        {
            var albums = JArray.Parse(@"[{""id"":1,""title"":""Old"",""monitored"":true,""releaseDate"":""2020-01-01T00:00:00Z"",""statistics"":{""trackFileCount"":3}}]");

            var status = ArrStatusMapping.MapArtist("Band", albums, NoQueue, Now);

            Assert.True(status.Units.Single().HasFile);
        }
    }
}
