using System.Threading;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Status
{
    // Each provider returns null when the item is not (or no longer) in the download client.
    public interface IMovieStatusProvider
    {
        Task<ArrItemStatus> GetMovieStatusAsync(int theMovieDbId, CancellationToken token);
    }

    public interface ITvShowStatusProvider
    {
        Task<ArrItemStatus> GetSeasonStatusAsync(int theTvDbId, int seasonNumber, CancellationToken token);
    }

    public interface IMusicStatusProvider
    {
        Task<ArrItemStatus> GetArtistStatusAsync(string artistId, CancellationToken token);
    }
}
