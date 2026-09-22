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

    /// <summary>
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