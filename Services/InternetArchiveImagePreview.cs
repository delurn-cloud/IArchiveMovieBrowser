using System;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Pure, WPF-independent helpers for the Internet Archive image preview shown in the Details
/// window for the item currently being displayed: deterministic image-URL construction and exact
/// display/accessibility text. No networking or image decoding happens here. The Details window
/// always represents one item, so there is no no-selection state (the item's identifier is fixed).
/// </summary>
public static class InternetArchiveImagePreview
{
    /// <summary>Base of Internet Archive's per-item image service.</summary>
    public const string ImageServiceBase = "https://archive.org/services/img/";

    /// <summary>Exact text shown while the Details item's image is loading.</summary>
    public const string LoadingMessage = "Loading Internet Archive image…";

    /// <summary>Exact visible fallback text on the bundled placeholder.</summary>
    public const string FallbackText = "No Internet Archive image available";

    /// <summary>Accessible description for the bundled fallback placeholder.</summary>
    public const string FallbackAccessibleText = "No Internet Archive image is available for the selected item.";

    /// <summary>
    /// Builds the deterministic Internet Archive image URL for an item identifier, or null when
    /// the identifier is blank/invalid so no HTTP request should start. The identifier is used as
    /// the URL path segment (never title text) and is percent-encoded.
    /// </summary>
    public static string? BuildImageUrl(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }
        return ImageServiceBase + Uri.EscapeDataString(identifier);
    }

    /// <summary>
    /// Accessible/alternate text for a successfully loaded image: identifies the selected item and
    /// states it is an Internet Archive image, with a stable generic fallback when no title is known.
    /// </summary>
    public static string LoadedImageAccessibleText(string? displayTitle)
    {
        if (string.IsNullOrWhiteSpace(displayTitle))
        {
            return "Internet Archive image for the selected item";
        }
        return "Internet Archive image for " + displayTitle;
    }
}