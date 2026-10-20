using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Requestrr.WebApi.RequestrrBot.Locale;
using Requestrr.WebApi.RequestrrBot.Notifications.Status;
using Xunit;

namespace Requestrr.Tests.Status
{
    public class RequestRecheckServiceTests
    {
        private static readonly DateTime AttemptedAt = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        private const string Key = "movie:u1:603";

        private readonly RequestStatusRepository _status = new RequestStatusRepository(new Dictionary<string, StatusSubscription>(), _ => { });
        private readonly FakeSender _sender = new FakeSender();

        public RequestRecheckServiceTests()
        {
            Language.Current = TestLanguage.English;
        }

        private RequestRecheckService CreateService(bool withSender = true)
        {
            return new RequestRecheckService(_status, NullLogger.Instance, TimeSpan.Zero) { Sender = withSender ? _sender : null };
        }

        private RecheckRequest Request(Func<Task<bool>> exists, Func<Task> subscribe = null)
        {
            return new RecheckRequest
            {
                UserId = "u1",
                Title = "Dune",
                StatusKey = Key,
                AttemptedAtUtc = AttemptedAt,
                ExistsAsync = exists,
                SubscribeAsync = subscribe ?? (() => Task.CompletedTask),
            };
        }

        [Fact]
        public async Task Exists_SubscribesBackdatesAndSendsSuccess()
        {
            var subscribed = false;

            await CreateService().Schedule(Request(() => Task.FromResult(true), () =>
            {
                subscribed = true;
                _status.Start(Key, AttemptedAt.AddSeconds(30));
                return Task.CompletedTask;
            }));

            Assert.True(subscribed);
            Assert.Equal(AttemptedAt, _status.Get(Key).RequestedAtUtc);
            Assert.Equal("Your request for **Dune** did go through despite the earlier error.", Assert.Single(_sender.Sent).Message);
        }

        [Fact]
        public async Task NotExists_SendsFailure_NoSubscribe()
        {
            var subscribed = false;

            await CreateService().Schedule(Request(() => Task.FromResult(false), () => { subscribed = true; return Task.CompletedTask; }));

            Assert.False(subscribed);
            Assert.Contains("really failed", Assert.Single(_sender.Sent).Message);
        }

        [Fact]
        public async Task ExistsThrows_SendsFailure()
        {
            await CreateService().Schedule(Request(() => throw new Exception("Radarr unreachable")));

            Assert.Contains("really failed", Assert.Single(_sender.Sent).Message);
        }

        [Fact]
        public async Task ChannelSender_MentionsUser()
        {
            _sender.UsesChannels = true;

            await CreateService().Schedule(Request(() => Task.FromResult(false)));

            Assert.StartsWith("<@u1> your request", Assert.Single(_sender.Sent).Message);
        }

        [Fact]
        public async Task NoSender_NoThrow()
        {
            await CreateService(withSender: false).Schedule(Request(() => Task.FromResult(true)));
        }

        [Fact]
        public void DefaultDelay_Is30Seconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(30), RequestRecheckService.DefaultDelay);
        }

        private class FakeSender : IStatusMessageSender
        {
            public List<(string UserId, string Message)> Sent { get; } = new List<(string UserId, string Message)>();
            public bool UsesChannels { get; set; }

            public Task<bool> SendAsync(string userId, string message, CancellationToken token)
            {
                Sent.Add((userId, message));
                return Task.FromResult(true);
            }
        }
    }
}
