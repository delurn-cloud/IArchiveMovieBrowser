using System;
using System.Net;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Raised when a request to an Internet Archive public API cannot be completed or its
/// response cannot be interpreted.
///
/// Carries the operation name, the request URI, and — when an HTTP response was actually
/// received — the HTTP status code. There are no credentials in this application, so this
/// type intentionally carries no secret material.
/// </summary>
public sealed class InternetArchiveApiException : Exception
{
    /// <summary>Short operation name, for example "search" or "metadata".</summary>
    public string Operation { get; }

    /// <summary>The full URI that was requested from Internet Archive.</summary>
    public Uri RequestUri { get; }

    /// <summary>HTTP status code of the response, or null when no HTTP response was obtained.</summary>
    public HttpStatusCode? StatusCode { get; }

    public InternetArchiveApiException(
        string operation,
        Uri requestUri,
        HttpStatusCode? statusCode,
        string? message,
        Exception? innerException)
        : base(message ?? "Internet Archive API request failed.", innerException)
    {
        Operation = operation;
        RequestUri = requestUri;
        StatusCode = statusCode;
    }
}