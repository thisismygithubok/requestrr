using System;
using System.Collections.Generic;
using Requestrr.WebApi.RequestrrBot.Notifications.Status;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class RequestStatusRepositoryTests
    {
        private static readonly DateTime Now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        private int _writes;

        private RequestStatusRepository Create()
        {
            return new RequestStatusRepository(new Dictionary<string, StatusSubscription>(), _ => _writes++);
        }

        [Fact]
        public void Start_ThenGet_ReturnsRequestedAt()
        {
            var repository = Create();

            repository.Start("k", Now);

            Assert.Equal(Now, repository.Get("k").RequestedAtUtc);
        }

        [Fact]
        public void Start_Existing_DoesNotOverwrite()
        {
            var repository = Create();
            repository.Start("k", Now);

            repository.Start("k", Now.AddHours(1));

            Assert.Equal(Now, repository.Get("k").RequestedAtUtc);
        }

        [Fact]
        public void Backdate_KeepsEarlier()
        {
            var repository = Create();
            repository.Start("k", Now);

            repository.Backdate("k", Now.AddMinutes(-1));
            repository.Backdate("k", Now.AddHours(1));
            repository.Backdate("missing", Now);

            Assert.Equal(Now.AddMinutes(-1), repository.Get("k").RequestedAtUtc);
            Assert.Null(repository.Get("missing"));
        }

        [Fact]
        public void MarkNotified_IncrementsCountAndPersists()
        {
            var repository = Create();
            repository.Start("k", Now);
            var writesBefore = _writes;

            repository.MarkNotified("k", new[] { "movie|NoRelease" }, Now);
            Assert.Equal(1, repository.Get("k").Units["movie|NoRelease"].NotifiedCount);

            repository.MarkNotified("k", new[] { "movie|NoRelease" }, Now.AddHours(1));
            var unit = repository.Get("k").Units["movie|NoRelease"];

            Assert.Equal(2, unit.NotifiedCount);
            Assert.Equal(Now.AddHours(1), unit.LastNotifiedAtUtc);
            Assert.Equal(writesBefore + 2, _writes);
        }

        [Fact]
        public void Get_ReturnsCopy()
        {
            var repository = Create();
            repository.Start("k", Now);

            repository.Get("k").Units["x"] = new UnitNotification();

            Assert.Empty(repository.Get("k").Units);
        }

        [Fact]
        public void Get_Missing_ReturnsNull()
        {
            Assert.Null(Create().Get("nope"));
        }

        [Fact]
        public void Remove_DeletesEntry()
        {
            var repository = Create();
            repository.Start("k", Now);

            repository.Remove("k");

            Assert.Null(repository.Get("k"));
        }

        [Fact]
        public void Keys_Format()
        {
            Assert.Equal("movie:1:603", RequestStatusKeys.Movie("1", 603));
            Assert.Equal("tv:1:81189:2", RequestStatusKeys.TvSeason("1", 81189, 2));
            Assert.Equal("music:1:abc", RequestStatusKeys.Music("1", "abc"));
        }
    }
}
