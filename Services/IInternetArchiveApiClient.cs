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
}