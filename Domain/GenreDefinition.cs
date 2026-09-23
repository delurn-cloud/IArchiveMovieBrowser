using System.Collections.Generic;

namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// One curated user-facing genre choice shown in the Narrow results filter. The <see cref="Key"/>
/// is the stable identifier carried through search state, <see cref="Label"/> is the exact text a
/// user sees, and <see cref="SubjectPhrase"/> is the Internet Archive <c>subject</c> metadata phrase
/// this choice maps to. Genres are suggestions, not a claim that Internet Archive uses a complete or
/// standardized genre taxonomy: they are curated search aids mapped onto equal lowercase IA subject
/// phrases.
/// </summary>
public sealed class GenreDefinition
{
    /// <summary>Stable identifier, e.g. <c>science-fiction</c>; never shown to users.</summary>
    public string Key { get; }

    /// <summary>Exact user-facing label, e.g. <c>Science fiction</c>.</summary>
    public string Label { get; }

    /// <summary>Exact IA <c>subject</c> metadata search phrase, e.g. <c>science fiction</c>.</summary>
    public string SubjectPhrase { get; }

    private GenreDefinition(string key, string label, string subjectPhrase)
    {
        Key = key;
        Label = label;
        SubjectPhrase = subjectPhrase;
    }

    internal static GenreDefinition Of(string key, string label, string subjectPhrase)
        => new GenreDefinition(key, label, subjectPhrase);
}
/// <summary>
/// The fixed set of curated genre choices presented in the UI, in a declared order that is
/// authoritative for both presentation and query building. All query terms originate here (never from
/// arbitrary checkbox text), so the catalog is the single source of truth and can be unit-tested
/// without any UI dependency.
/// </summary>
public static class GenreCatalog
{
    /// <summary>The 16 curated genre choices, in the exact declared display order.</summary>
    public static IReadOnlyList<GenreDefinition> All()
    {
        return new List<GenreDefinition>
        {
            GenreDefinition.Of("action", "Action", "action"),
            GenreDefinition.Of("adventure", "Adventure", "adventure"),
            GenreDefinition.Of("animation", "Animation", "animation"),
            GenreDefinition.Of("comedy", "Comedy", "comedy"),
            GenreDefinition.Of("crime", "Crime", "crime"),
            GenreDefinition.Of("documentary", "Documentary", "documentary"),
            GenreDefinition.Of("drama", "Drama", "drama"),
            GenreDefinition.Of("family", "Family", "family"),
            GenreDefinition.Of("fantasy", "Fantasy", "fantasy"),
            GenreDefinition.Of("horror", "Horror", "horror"),
            GenreDefinition.Of("mystery", "Mystery", "mystery"),
            GenreDefinition.Of("romance", "Romance", "romance"),
            GenreDefinition.Of("science-fiction", "Science fiction", "science fiction"),
            GenreDefinition.Of("thriller", "Thriller", "thriller"),
            GenreDefinition.Of("war", "War", "war"),
            GenreDefinition.Of("western", "Western", "western")
        };
    }
/// <summary>True when the key is a known <see cref="All"/> genre key.</summary>
    public static bool IsKnownKey(string key)
    {
        return SubjectPhraseFor(key) is not null;
    }

    /// <summary>
    /// Returns the IA <c>subject</c> phrase for a known genre key, or null for an unknown key.
    /// Phrase text exists only in the centralized catalog.
    /// </summary>
    public static string? SubjectPhraseFor(string key)
    {
        if (key is null)
        {
            return null;
        }
        foreach (GenreDefinition genre in All())
        {
            if (string.Equals(genre.Key, key))
            {
                return genre.SubjectPhrase;
            }
        }
        return null;
    }

    /// <summary>
    /// Normalizes an unordered (possibly duplicated) set of selected genre keys into the declared
    /// catalog order with duplicates removed. This keeps requests/tests deterministic regardless of
    /// checkbox-click order and prevents duplicate subject clauses.
    /// </summary>
    public static IReadOnlyList<string> NormalizeSelection(IReadOnlyList<string> selectedKeys)
    {
        var result = new List<string>();
        if (selectedKeys is null || selectedKeys.Count == 0)
        {
            return result;
        }

        foreach (GenreDefinition genre in All())
        {
            if (ContainsKey(selectedKeys, genre.Key))
            {
                result.Add(genre.Key);
            }
        }
        return result;
    }

    private static bool ContainsKey(IReadOnlyList<string> keys, string key)
    {
        foreach (string candidate in keys)
        {
            if (string.Equals(candidate, key))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Minimal, WPF-independent selection model for the Genre filter. It holds which curated genre keys
/// are selected and always reports them in the declared catalog order (deduplicated). It performs no
/// networking and knows nothing about search requests: choosing or clearing genres here never triggers
/// a request, matching the app convention that filters apply only when Search or Refresh runs.
/// </summary>
public sealed class GenreSelectionState
{
    private List<string> _selected = new List<string>();

    /// <summary>Marks a known genre key as selected (unknown keys are ignored).</summary>
    public void Select(string key)
    {
        if (!GenreCatalog.IsKnownKey(key))
        {
            return;
        }
        if (!_selected.Contains(key))
        {
            _selected.Add(key);
        }
    }

    /// <summary>Clears a single genre selection; harmless when not selected.</summary>
    public void Deselect(string key)
    {
        _selected.Remove(key);
    }

    /// <summary>Clears every genre selection.</summary>
    public void Clear()
    {
        _selected.Clear();
    }

    /// <summary>True when the given genre key is currently selected.</summary>
    public bool IsSelected(string key)
    {
        return _selected.Contains(key);
    }

    /// <summary>Number of currently selected genres.</summary>
    public int SelectedCount()
    {
        return _selected.Count;
    }

    /// <summary>
    /// The currently selected genre keys in declared catalog order (deduplicated), independent of the
    /// order in which they were selected.
    /// </summary>
    public IReadOnlyList<string> SelectedKeys()
    {
        return GenreCatalog.NormalizeSelection(_selected);
    }
}