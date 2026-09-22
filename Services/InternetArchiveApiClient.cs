using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// HTTP client for the Internet Archive public read APIs.
/// Used endpoints: search https://archive.org/advancedsearch.php and
/// metadata https://archive.org/metadata/{identifier}.
/// Every call performs a fresh HTTP request (no caching or static state) and passes the
/// supplied <see cref="CancellationToken"/> through to every HTTP operation. Expected
/// cancellation (<see cref="OperationCanceledException"/>) is allowed to propagate.
/// </summary>
public sealed class InternetArchiveApiClient : IInternetArchiveApiClient
{
    private const string SearchEndpoint = "https://archive.org/advancedsearch.php";
    private const string MetadataEndpointBase = "https://archive.org/metadata/";

    private const string SearchFieldList =
        "identifier,title,creator,date,year,mediatype,collection,downloads";

    private readonly HttpClient _httpClient;

    public InternetArchiveApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<InternetArchiveSearchPage> SearchAsync(
        InternetArchiveSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        Uri uri = BuildSearchUri(request);
        using HttpResponseMessage response =
            await _httpClient.GetAsync(uri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException("search", uri, response);
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSearchPage(json, uri);
    }

    /// <inheritdoc />
    public async Task<InternetArchiveItemMetadata> GetItemMetadataAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        string safeIdentifier = ValidateIdentifier(identifier);
        Uri uri = new Uri(
            MetadataEndpointBase + Uri.EscapeDataString(safeIdentifier),
            UriKind.Absolute);

        using HttpResponseMessage response =
            await _httpClient.GetAsync(uri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException("metadata", uri, response);
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseItemMetadata(json, uri, safeIdentifier);
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetImageBytesAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        string safeIdentifier = ValidateIdentifier(identifier);
        string? url = InternetArchiveImagePreview.BuildImageUrl(safeIdentifier);
        if (url is null)
        {
            return null;
        }

        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null; // cancelled/closed: suppressed by the caller's generation guard
        }
        catch (Exception)
        {
            return null; // any fetch failure → unavailable; no unobserved exception
        }
    }

    /// <inheritdoc />
    public async Task<DirectLinkProbe> ProbeVideoLinkAsync(
        Uri directUri,
        CancellationToken cancellationToken = default)
    {
        if (directUri is null)
        {
            return DirectLinkProbe.NetworkError(null);
        }

        try
        {
            // Explicitly header-first: we request only the response headers and never read the
            // response body (no ReadAs*/CopyTo*). Both request and response are disposed after the
            // metadata is captured.
            HttpRequestMessage request = new HttpRequestMessage();
            request.RequestUri = directUri;
            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                Uri? finalUri = response.RequestMessage?.RequestUri;
                string? contentType = response.Content.Headers.ContentType is null
                    ? null
                    : response.Content.Headers.ContentType.ToString();
                long? contentLength = response.Content.Headers.ContentLength;
                string? disposition = response.Content.Headers.ContentDisposition is null
                    ? null
                    : response.Content.Headers.ContentDisposition.ToString();

                return DirectLinkProbe.FromResponse(
                    directUri,
                    finalUri,
                    response.StatusCode,
                    response.ReasonPhrase,
                    contentType,
                    contentLength,
                    disposition);
            }
            finally
            {
                if (response is not null)
                {
                    response.Dispose();
                }
                request.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DirectLinkProbe.Cancelled(directUri);
        }
        catch (Exception)
        {
            return DirectLinkProbe.NetworkError(directUri);
        }
    }

    /// <inheritdoc />
    public async Task<PlayerResolutionResult> ResolvePlayerUrlAsync(
        Uri canonicalUri,
        CancellationToken cancellationToken = default)
    {
        if (canonicalUri is null)
        {
            return PlayerResolutionResult.UnsafeFinal(null);
        }

        // The source must already be a trusted canonical IA download URL before we send anything.
        Uri source = canonicalUri;
        if (source.AbsolutePath is null || !source.AbsolutePath.StartsWith("/download/"))
        {
            return PlayerResolutionResult.UnsafeFinal(source);
        }

        try
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.RequestUri = source;
            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                // Never read/copy the response body — only headers and the final URI.
                Uri? finalUri = response.RequestMessage?.RequestUri;
                System.Net.HttpStatusCode status = response.StatusCode;

                if (status == System.Net.HttpStatusCode.Unauthorized
                    || status == System.Net.HttpStatusCode.Forbidden)
                {
                    return PlayerResolutionResult.Restricted();
                }
                if (status == System.Net.HttpStatusCode.NotFound)
                {
                    return PlayerResolutionResult.Missing();
                }

                int code = (int)status;
                if (code < 200 || code >= 300)
                {
                    return PlayerResolutionResult.NonSuccess(status);
                }

                if (finalUri is null || !PlayerUrlResolver.IsTrustedFinalUri(finalUri))
                {
                    return PlayerResolutionResult.UnsafeFinal(finalUri);
                }

                return PlayerResolutionResult.Resolved(finalUri);
            }
            finally
            {
                if (response is not null)
                {
                    response.Dispose();
                }
                request.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlayerResolutionResult.Cancelled();
        }
        catch (Exception)
        {
            return PlayerResolutionResult.NetworkFailure();
        }
    }
/// <summary>
/// <inheritdoc />
    public async Task<VideoDownloadResult> DownloadFileAsync(
        VideoDownloadRequest request,
        DownloadProgressListener? reportProgress,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string finalPath = request.FinalPath;

        // Preflight: destination exists without explicit replace auth -> no request/no transfer.
        if (System.IO.File.Exists(finalPath) && !request.ReplaceConfirmed)
        {
            return VideoDownloadResult.DestinationExists();
        }

        string tempPath = Downloads.TempSiblingPath(finalPath);
        string? backupPath = null;

        try
        {
            HttpRequestMessage requestMsg = new HttpRequestMessage();
            requestMsg.RequestUri = request.SourceUri;
            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(
                    requestMsg,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                Uri? finalUri = response.RequestMessage?.RequestUri;
                System.Net.HttpStatusCode status = response.StatusCode;

                if (!PlayerUrlResolver.IsTrustedFinalUri(finalUri))
                {
                    return VideoDownloadResult.UnsafeFinal(finalUri);
                }
                if (status == System.Net.HttpStatusCode.Unauthorized
                    || status == System.Net.HttpStatusCode.Forbidden)
                {
                    return VideoDownloadResult.Restricted();
                }
                if (status == System.Net.HttpStatusCode.NotFound)
                {
                    return VideoDownloadResult.Missing();
                }
                int code = (int)status;
                if (code < 200 || code >= 300)
                {
                    return VideoDownloadResult.NonSuccess(status);
                }

                string? contentType = response.Content.Headers.ContentType is null
                    ? null
                    : response.Content.Headers.ContentType.ToString();
                if (IsClearlyNonMedia(contentType))
                {
                    return VideoDownloadResult.NonMedia();
                }

                long? total = response.Content.Headers.ContentLength;

                // Stream to a sibling temporary file before any finalization.
                System.IO.FileStream? output = null;
                System.IO.Stream? inbound = null;
                try
                {
                    output = System.IO.File.Open(tempPath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write);
                    inbound = await response.Content.ReadAsStreamAsync(cancellationToken);

                    var buffer = new byte[128 * 1024];
                    long written = 0;
                    while (true)
                    {
                        int count = await inbound.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (count <= 0)
                        {
                            break;
                        }
                        await output.WriteAsync(buffer, 0, count, cancellationToken);
                        written += count;
                        if (reportProgress is not null)
                        {
                            reportProgress.OnProgress(new DownloadProgress(written, total));
                        }
                    }
                }
                finally
                {
                    if (inbound is not null)
                    {
                        try { inbound.Close(); } catch (Exception) { }
                    }
                    if (output is not null)
                    {
                        try { output.Close(); } catch (Exception) { }
                    }
                }
// Finalize only after a complete, successfully closed temporary transfer.
                try
                {
                    if (System.IO.File.Exists(finalPath))
                    {
                        backupPath = Downloads.BackupSiblingPath(finalPath);
                        System.IO.File.Replace(tempPath, finalPath, backupPath);
                        DeleteIfExists(backupPath); // replacement succeeded; drop old backup file
                    }
                    else
                    {
                        System.IO.File.Move(tempPath, finalPath);
                    }
                    return VideoDownloadResult.Completed();
                }
                catch (Exception replaceError)
                {
                    // Final target is untouched (atomic Replace/Move); clean the new temp + any backup.
                    DeleteIfExists(tempPath);
                    if (backupPath is not null)
                    {
                        DeleteIfExists(backupPath);
                    }
                    return VideoDownloadResult.FileSystemFailure(replaceError);
                }
            }
            finally
            {
                if (response is not null)
                {
                    response.Dispose();
                }
                requestMsg.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteIfExists(tempPath);
            if (backupPath is not null)
            {
                DeleteIfExists(backupPath);
            }
            return VideoDownloadResult.Cancelled();
        }
        catch (Exception ex)
        {
            DeleteIfExists(tempPath);
            if (backupPath is not null)
            {
                DeleteIfExists(backupPath);
            }
            return VideoDownloadResult.NetworkFailure(ex);
        }
    }

    /// <summary>True when a 2xx Content-Type is clearly non-media/error-like; absent/generic is not rejected.</summary>
    private static bool IsClearlyNonMedia(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }
        string lower = contentType.ToLower();
        return lower.StartsWith("text/")
            || lower.Contains("html")
            || lower == "application/json"
            || lower == "application/xml"
            || lower == "text/xml";
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }
    /// Builds the advancedsearch.php request URI. Query shape (documented): a simple
    /// title search over the user-provided text, scoped by media-type:
    ///   q=&lt;SearchScopeQueryBuilder expression&gt;&amp;rows=&lt;page size&gt;&amp;page=&lt;page&gt;
    ///   &amp;output=json&amp;fl=&lt;explicit field list&gt;
    /// User text and the whole expression are percent-encoded, never concatenated raw.
    /// </summary>
    private static Uri BuildSearchUri(InternetArchiveSearchRequest request)
    {
        string expression = SearchScopeQueryBuilder.BuildQuery(request.Criteria);
        string queryString =
            "q=" + Uri.EscapeDataString(expression) +
            "&output=json" +
            "&page=" + request.Page.ToString() +
            "&rows=" + request.PageSize.ToString() +
            "&fl=" + Uri.EscapeDataString(SearchFieldList);

        return new Uri(SearchEndpoint + "?" + queryString, UriKind.Absolute);
    }

    private static InternetArchiveSearchPage ParseSearchPage(string json, Uri requestUri)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            long numFound = 0;
            JsonElement results = default;

            if (root.TryGetProperty("response", out JsonElement response))
            {
                if (response.TryGetProperty("numFound", out JsonElement numFoundElement))
                {
                    numFound = ReadLong(numFoundElement) ?? 0;
                }

                if (response.TryGetProperty("docs", out JsonElement docs))
                {
                    results = docs;
                }
            }

            var parsedResults = new System.Collections.Generic.List<InternetArchiveSearchResult>();
            if (results.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement doc in results.EnumerateArray())
                {
                    InternetArchiveSearchResult? parsed = TryParseSearchResult(doc);
                    if (parsed is not null)
                    {
                        parsedResults.Add(parsed);
                    }
                }
            }

            return new InternetArchiveSearchPage(numFound, parsedResults);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InternetArchiveApiException(
                "search", requestUri, null,
                "The Internet Archive search response could not be interpreted.", ex);
        }
    }

    private static InternetArchiveSearchResult? TryParseSearchResult(JsonElement doc)
    {
        string? identifier = GetString(doc, "identifier");
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        return new InternetArchiveSearchResult(
            identifier!,
            GetString(doc, "title"),
            GetStringList(doc, "creator"),
            GetString(doc, "date"),
            GetString(doc, "year"),
            GetString(doc, "mediatype"),
            GetStringList(doc, "collection"),
            GetLong(doc, "downloads"));
    }

    private static InternetArchiveItemMetadata ParseItemMetadata(
        string json,
        Uri requestUri,
        string identifier)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string? title = null;
            string? description = null;
            string? date = null;
            string? year = null;
            string? mediaType = null;
            string? licenseUrl = null;
            System.Collections.Generic.IReadOnlyList<string>? creators = null;
            System.Collections.Generic.IReadOnlyList<string>? collections = null;
            System.Collections.Generic.IReadOnlyList<string>? subjects = null;
            System.Collections.Generic.IReadOnlyList<InternetArchiveRemoteFile> files =
                new System.Collections.Generic.List<InternetArchiveRemoteFile>();

            if (root.TryGetProperty("metadata", out JsonElement metadata))
            {
                title = GetString(metadata, "title");
                description = GetString(metadata, "description");
                date = GetString(metadata, "date");
                year = GetString(metadata, "year");
                mediaType = GetString(metadata, "mediatype");
                licenseUrl = GetString(metadata, "licenseurl");
                creators = GetStringList(metadata, "creator");
                collections = GetStringList(metadata, "collection");
                subjects = GetStringList(metadata, "subject");
            }

            if (root.TryGetProperty("files", out JsonElement fileArray)
                && fileArray.ValueKind == JsonValueKind.Array)
            {
                var fileList = new System.Collections.Generic.List<InternetArchiveRemoteFile>();
                foreach (JsonElement fileEntry in fileArray.EnumerateArray())
                {
                    InternetArchiveRemoteFile? parsed = TryParseRemoteFile(fileEntry);
                    if (parsed is not null)
                    {
                        fileList.Add(parsed);
                    }
                }
                files = fileList;
            }

            return new InternetArchiveItemMetadata(
                identifier, title, description, creators, date, year,
                mediaType, collections, subjects, licenseUrl, files);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InternetArchiveApiException(
                "metadata", requestUri, null,
                "The Internet Archive metadata response could not be interpreted.", ex);
        }
    }

    private static InternetArchiveRemoteFile? TryParseRemoteFile(JsonElement fileEntry)
    {
        string? name = GetString(fileEntry, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new InternetArchiveRemoteFile(
            name!,
            GetString(fileEntry, "format"),
            GetString(fileEntry, "source"),
            GetLong(fileEntry, "size"),
            GetLong(fileEntry, "mtime"),
            GetString(fileEntry, "sha1"));
    }

    private static InternetArchiveApiException CreateHttpException(
        string operation,
        Uri requestUri,
        HttpResponseMessage response)
    {
        return new InternetArchiveApiException(
            operation,
            requestUri,
            response.StatusCode,
            "The Internet Archive API returned a non-success HTTP status.",
            null);
    }

    // --- Tolerant JSON readers (non-identifier fields may be string, number, boolean,
    // array, or absent) -------------------------------------------------------------

    private static string? GetString(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out JsonElement value))
        {
            return ReadScalarString(value);
        }
        return null;
    }

    private static System.Collections.Generic.IReadOnlyList<string>? GetStringList(
        JsonElement parent,
        string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out JsonElement value))
        {
            return ReadStringList(value);
        }
        return null;
    }

    private static long? GetLong(JsonElement parent, string propertyName)
    {
        if (parent.TryGetProperty(propertyName, out JsonElement value))
        {
            return ReadLong(value);
        }
        return null;
    }

    private static string? ReadScalarString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => value.GetRawText(),
            JsonValueKind.False => value.GetRawText(),
            _ => null
        };
    }

    private static System.Collections.Generic.IReadOnlyList<string>? ReadStringList(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (JsonElement item in value.EnumerateArray())
            {
                string? text = ReadScalarString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    list.Add(text!);
                }
            }
            return list.Count == 0 ? null : list;
        }

        string? single = ReadScalarString(value);
        if (single is null)
        {
            return null;
        }
        return new System.Collections.Generic.List<string> { single };
    }

    private static long? ReadLong(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long whole))
        {
            return whole;
        }

        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out long fromText))
        {
            return fromText;
        }

        return null;
    }

    // --- Identifier validation consistent with InternetArchiveDownloadUrlBuilder ----

    private static string ValidateIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException(
                "Identifier must not be null, empty, or whitespace-only.",
                nameof(identifier));
        }

        if (identifier.Contains('/') || identifier.Contains('\\'))
        {
            throw new ArgumentException(
                "Identifier must be a single URL segment and must not contain '/' or '\\'.",
                nameof(identifier));
        }

        if (identifier.Contains(".."))
        {
            throw new ArgumentException(
                "Identifier must not contain '..'.",
                nameof(identifier));
        }

        if (Uri.TryCreate(identifier, UriKind.Absolute, out Uri? absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Identifier must not be a full HTTP(S) URL.",
                nameof(identifier));
        }

        return identifier;
    }
}