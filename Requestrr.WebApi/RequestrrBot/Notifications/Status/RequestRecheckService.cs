using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Locale;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    public class RecheckRequest
    {
        public string UserId { get; set; }
        public string Title { get; set; }
        public IReadOnlyList<string> StatusKeys { get; set; }
        public DateTime AttemptedAtUtc { get; set; }
        public Func<Task<bool>> ExistsAsync { get; set; }
        public Func<Task> SubscribeAsync { get; set; }
    }

    // Requests that error out are sometimes still added by the download client, so check again shortly after.
    public class RequestRecheckService
    {
        public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(30);

        private readonly RequestStatusRepository _statusRepository;
        private readonly ILogger _logger;
        private readonly TimeSpan _delay;

        public RequestRecheckService(RequestStatusRepository statusRepository, ILogger logger, TimeSpan? delay = null)
        {
            _statusRepository = statusRepository;
            _logger = logger;
            _delay = delay ?? DefaultDelay;
        }

        // Set by the bot once Discord is connected; null while notifications are disabled.
        public IStatusMessageSender Sender { get; set; }

        public Task Schedule(RecheckRequest request)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_delay);

                    var exists = false;

                    try
                    {
                        exists = await request.ExistsAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"An error occurred while re-checking the request for \"{request.Title}\": " + ex.Message);
                    }

                    if (exists)
                    {
                        await request.SubscribeAsync();
                        foreach (var statusKey in request.StatusKeys)
                        {
                            _statusRepository.Backdate(statusKey, request.AttemptedAtUtc);
                        }
                    }

                    var sender = Sender;
                    if (sender != null)
                    {
                        var message = RequestStatusMessageBuilder.BuildRecheck(Language.Current, request.Title, exists, sender.UsesChannels ? $"<@{request.UserId}>" : null);
                        await sender.SendAsync(request.UserId, message, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"An error occurred while handling the re-check for \"{request.Title}\": " + ex.Message);
                }
            });
        }
    }
}
