using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class InternetArchiveApiClientTests
{
    private const string SearchJson =
        "{\"response\":{\"numFound\":3,\"start\":0,\"docs\":[" +
        "{\"identifier\":\"id1\",\"title\":\"First\",\"creator\":[\"A Creator\",\"B Creator\"]," +
        "\"date\":\"2020-01-01\",\"year\":2020,\"mediatype\":\"movies\",\"collection\":[\"col-a\"],\"downloads\":123}," +
        "{\"identifier\":\"id2\",\"title\":null,\"creator\":\"Solo\",\"year\":\"1999\"," +
        "\"mediatype\":\"audio\",\"downloads\":\"7\"}" +
        "]}}";

    private const string MetadataJson =
        "{\"metadata\":{\"identifier\":\"item-1\",\"title\":\"My Item\",\"description\":\"Desc\"," +
        "\"creator\":[\"Anne\"],\"date\":\"2021\",\"year\":2021,\"mediatype\":\"movies\"," +
        "\"collection\":[\"colOne\"],\"subject\":[\"s1\"],\"licenseurl\":\"https://lic\"}," +
        "\"files\":[" +
        "{\"name\":\"video.mp4\",\"format\":\"MPEG4\",\"source\":\"original\",\"size\":1024,\"mtime\":\"1600000000\",\"sha1\":\"abc\"}," +
        "{\"name\":\"poster.jpg\",\"format\":\"JPEG\",\"size\":256}," +
        "{\"name\":\"\",\"format\":\"OMIT\"}" +
        "]}";

    [Fact]
    public async Task Search_BuildsEncodedRequest()
    {
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, SearchJson));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var page = await client.SearchAsync(new InternetArchiveSearchRequest("buckaroo bonsai", 1, 25));

        Assert.NotNull(page);
        Assert.Single(handler.Requests);
        Uri requestUri = handler.Requests[0].RequestUri!;

        Assert.Equal("https", requestUri.Scheme);
        Assert.Equal("archive.org", requestUri.Host);
        Assert.Equal("/advancedsearch.php", requestUri.AbsolutePath);

        string query = requestUri.Query;
        Assert.Contains("output=json", query);
        Assert.Contains("&page=1", query);
        Assert.Contains("&rows=25", query);
        Assert.Contains("&fl=" + Uri.EscapeDataString("identifier,title,creator,date,year,mediatype,collection,downloads"), query);
        Assert.Contains("q=" + Uri.EscapeDataString("title:\"buckaroo bonsai\""), query);
    }

    [Fact]
    public async Task Search_ParsesNumFoundAndDocs()
    {
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, SearchJson));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var page = await client.SearchAsync(new InternetArchiveSearchRequest("buckaroo"));

        Assert.Equal(3, page.NumFound);
        Assert.Equal(2, page.Results.Count);
        Assert.Equal("id1", page.Results[0].Identifier);
        Assert.Equal("First", page.Results[0].Title);
        Assert.Equal(2, page.Results[0].Creators!.Count);
        Assert.Equal("Solo", page.Results[1].Creators![0]);
        Assert.Equal("2020", page.Results[0].Year);
    }

    [Fact]
    public async Task Search_ToleratesMissingAndVariantFields()
    {
        const string json =
            "{\"response\":{\"numFound\":2,\"docs\":[" +
            "{\"identifier\":\"a\",\"title\":\"Only\",\"year\":1999,\"creator\":[\"C1\",\"C2\"]}," +
            "{\"identifier\":\"b\",\"downloads\":\"42\"}" +
            "]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var page = await client.SearchAsync(new InternetArchiveSearchRequest("x"));

        Assert.Equal(2, page.Results.Count);
        Assert.Equal("Only", page.Results[0].Title);
        Assert.Equal("1999", page.Results[0].Year);
        Assert.Equal(2, page.Results[0].Creators!.Count);
        Assert.Null(page.Results[0].Date);
        Assert.Equal(42, page.Results[1].Downloads);
    }

    [Fact]
    public async Task Search_FiltersDocsWithoutIdentifier()
    {
        const string json =
            "{\"response\":{\"numFound\":3,\"docs\":[" +
            "{\"identifier\":\"keep\",\"title\":\"Has Id\"}," +
            "{\"title\":\"No Id\"}," +
            "{\"identifier\":\"\",\"title\":\"Blank Id\"}" +
            "]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var page = await client.SearchAsync(new InternetArchiveSearchRequest("x"));

        Assert.Single(page.Results);
        Assert.Equal("keep", page.Results[0].Identifier);
    }

    [Fact]
    public void Search_RejectsInvalidRequests()
    {
        Assert.ThrowsAny<ArgumentException>(() => new InternetArchiveSearchRequest("   "));
        Assert.ThrowsAny<ArgumentException>(() => new InternetArchiveSearchRequest("ok", 0));
        Assert.ThrowsAny<ArgumentException>(() => new InternetArchiveSearchRequest("ok", -2));
        Assert.ThrowsAny<ArgumentException>(() => new InternetArchiveSearchRequest("ok", 1, 0));
        Assert.ThrowsAny<ArgumentException>(() => new InternetArchiveSearchRequest("ok", 1, 101));
    }

    [Fact]
    public async Task Metadata_BuildsEncodedIdentifierUri()
    {
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, MetadataJson));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        await client.GetItemMetadataAsync("my item 1");

        Assert.Single(handler.Requests);
        Uri requestUri = handler.Requests[0].RequestUri!;

        Assert.Equal("https", requestUri.Scheme);
        Assert.Equal("archive.org", requestUri.Host);
        Assert.StartsWith("/metadata/", requestUri.AbsolutePath);
        Assert.Contains("/metadata/my%20item%201", requestUri.AbsolutePath);
    }

    [Fact]
    public async Task Metadata_ParsesItemAndFiles()
    {
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, MetadataJson));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var metadata = await client.GetItemMetadataAsync("item-1");

        Assert.Equal("item-1", metadata.Identifier);
        Assert.Equal("My Item", metadata.Title);
        Assert.Equal("Desc", metadata.Description);
        Assert.Equal("Anne", metadata.Creators![0]);
        Assert.Equal("movies", metadata.MediaType);
        Assert.Equal("colOne", metadata.Collections![0]);
        Assert.Equal("s1", metadata.Subjects![0]);
        Assert.Equal("https://lic", metadata.LicenseUrl);
        Assert.Equal(2, metadata.Files.Count);
        Assert.Equal("video.mp4", metadata.Files[0].Name);
        Assert.Equal("MPEG4", metadata.Files[0].Format);
        Assert.Equal("original", metadata.Files[0].Source);
        Assert.Equal("abc", metadata.Files[0].Sha1);
    }

    [Fact]
    public async Task Metadata_OmitsFilesWithoutName()
    {
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, MetadataJson));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var metadata = await client.GetItemMetadataAsync("item-1");

        Assert.Equal(2, metadata.Files.Count);
        Assert.Equal("video.mp4", metadata.Files[0].Name);
        Assert.Equal("poster.jpg", metadata.Files[1].Name);
    }

    [Fact]
    public async Task Metadata_HandlesNumericStringVariations()
    {
        const string json =
            "{\"metadata\":{\"identifier\":\"v1\",\"year\":1985}," +
            "\"files\":[{\"name\":\"a.mp4\",\"size\":\"999\",\"mtime\":1700000000}]}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var metadata = await client.GetItemMetadataAsync("v1");

        Assert.Equal("1985", metadata.Year);
        Assert.Equal(999, metadata.Files[0].Size);
        Assert.Equal(1700000000, metadata.Files[0].MTime);
    }

    [Fact]
    public async Task NonSuccess_ThrowsExceptionWithContext()
    {
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.InternalServerError, "{ }"));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InternetArchiveApiException>(
            () => client.SearchAsync(new InternetArchiveSearchRequest("buckaroo")));

        Assert.Equal("search", ex.Operation);
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("advancedsearch.php", ex.RequestUri.ToString());
    }

    [Fact]
    public async Task MalformedJson_ThrowsExceptionWithInner()
    {
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, "not-json-{{"));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InternetArchiveApiException>(
            () => client.GetItemMetadataAsync("item-1"));

        Assert.Equal("metadata", ex.Operation);
        Assert.NotNull(ex.InnerException);
        Assert.Contains("metadata", ex.RequestUri.ToString());
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var client = new InternetArchiveApiClient(new HttpClient(new CancellingHandler()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.SearchAsync(new InternetArchiveSearchRequest("buckaroo")));
    }

    [Fact]
    public async Task EachMethodCall_HitsTheHandler_NoCaching()
    {
        var handler = new FakeHandler(req => req.RequestUri!.ToString().Contains("advancedsearch.php")
            ? JsonResponse(HttpStatusCode.OK, SearchJson)
            : JsonResponse(HttpStatusCode.OK, MetadataJson));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        await client.SearchAsync(new InternetArchiveSearchRequest("one"));
        await client.SearchAsync(new InternetArchiveSearchRequest("two"));
        await client.GetItemMetadataAsync("item-1");

        Assert.Equal(3, handler.Requests.Count);
    }

    // --- In-memory fake handler for deterministic tests (no live network) -------------

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = new List<HttpRequestMessage>();
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }
            throw new OperationCanceledException();
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status);
        response.Content = new StringContent(json);
        return response;
    }
}


