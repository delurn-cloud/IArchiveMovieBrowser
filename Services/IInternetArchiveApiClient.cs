using System.Threading;
using System.Threading.Tasks;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Read-only client for Internet Archive's public catalog APIs.
///
/// Every method invocation performs a fresh HTTP request. It keeps no in-memory cache,
/// no static result store, and performs no automatic refresh or catalog mirroring, so new
/// or changed Internet Archive items can always surface through a fresh call.
/// </summary>
public interface IInternetArchiveApiClient
{
    /// <summary>Performs a title-oriented search against the Internet Archive catalog.</summary>
    Task<InternetArchiveSearchPage> SearchAsync(
        InternetArchiveSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the current metadata and file inventory for a single item.</summary>
    Task<InternetArchiveItemMetadata> GetItemMetadataAsync(
        string identifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the raw bytes of the Internet Archive image for an item, or null when the request
    /// fails, is cancelled, or is not an image. Never throws; cancellation/closure is surfaced as
    /// null so a stale/closed selection is suppressed rather than crashing.
    /// </summary>
    Task<byte[]?> GetImageBytesAsync(
        string identifier,
        CancellationToken cancellationToken = default);
}