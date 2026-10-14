namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Optional narrowing of an Internet Archive search to (or away from) the best-known television
/// collections.
///
/// This is a deliberately <b>distinct type</b> from the primary <see cref="SearchScope"/>: the
/// primary scope controls media type (<c>mediatype:movies</c> / <c>NOT mediatype:movies</c> / no
/// restriction) and lives on the launcher's primary row, whereas this type only adds
/// <c>collection:</c> clauses. The two are orthogonal and are composed independently.
///
/// <para><b>Honest labelling.</b> The Internet Archive has no <c>mediatype:television</c>: television
/// episodes and series are filed as <c>mediatype:movies</c> and are distinguishable only by
/// collection, and that separation is demonstrably leaky — items sitting in user-favourite
/// collections such as <c>fav-tv_series_1960</c> are television but are not in any canonical
/// television collection. This type therefore makes <b>no</b> dependable movies-only or
/// television-only promise: the variants below are labelled for the collections they actually
/// select, and nothing more.</para>
/// </summary>
public enum VideoCollectionScope
{
    /// <summary>
    /// Default. Emits no collection clause at all: every item allowed by the primary
    /// <see cref="SearchScope"/>, including television.
    /// </summary>
    AllWatchableVideo,

    /// <summary>
    /// Items in at least one of the best-known Internet Archive television collections. Emitted as
    /// an <c>OR</c> group so an item in any one of them is included.
    /// </summary>
    TelevisionCollections,

    /// <summary>
    /// Removes items in the best-known television collections by negating each member. Some
    /// television will still appear, because the Internet Archive does not require television to be
    /// filed in these collections.
    /// </summary>
    ExcludeKnownTelevisionCollections
}