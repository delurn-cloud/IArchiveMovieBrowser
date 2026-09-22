using System;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Result of validating a user-entered Year field.
/// </summary>
public enum YearValidationState
{
    /// <summary>Blank/whitespace-only: no Year filter.</summary>
    Blank,

    /// <summary>Not exactly four ASCII digits in the accepted range.</summary>
    Invalid,

    /// <summary>A valid exact year within 1800..max.</summary>
    Valid
}

/// <summary>Outcome of validating an optional Year field.</summary>
public sealed class YearValidation
{
    public YearValidationState State { get; }
    public int? Value { get; }

    private YearValidation(YearValidationState state, int? value)
    {
        State = state;
        Value = value;
    }

    internal static YearValidation Blank() => new YearValidation(YearValidationState.Blank, null);
    internal static YearValidation Invalid() => new YearValidation(YearValidationState.Invalid, null);
    internal static YearValidation Valid(int value) => new YearValidation(YearValidationState.Valid, value);
}

/// <summary>Result of building validated <see cref="SearchCriteria"/> from user fields.</summary>
public enum CriteriaBuildState
{
    /// <summary>No title, creator, or year supplied: no search should start.</summary>
    NoCriteria,

    /// <summary>A Year endpoint failed validation; <see cref="CriteriaBuildResult.Message"/> explains it.</summary>
    InvalidYear,

    /// <summary>Both Year endpoints are valid but ordered From &gt; To; no search should start.</summary>
    InvalidYearRange,

    /// <summary>A valid <see cref="SearchCriteria"/> is available.</summary>
    Valid
}

/// <summary>Outcome of <see cref="FilterCriteriaLogic.BuildCriteria"/>.</summary>
public sealed class CriteriaBuildResult
{
    public CriteriaBuildState State { get; }
    public SearchCriteria? Criteria { get; }
    public string? Message { get; }

    public bool IsValid => State == CriteriaBuildState.Valid;

    private CriteriaBuildResult(CriteriaBuildState state, SearchCriteria? criteria, string? message)
    {
        State = state;
        Criteria = criteria;
        Message = message;
    }

    internal static CriteriaBuildResult NoCriteria()
        => new CriteriaBuildResult(CriteriaBuildState.NoCriteria, null, null);

    internal static CriteriaBuildResult InvalidYear(string message)
        => new CriteriaBuildResult(CriteriaBuildState.InvalidYear, null, message);

    internal static CriteriaBuildResult InvalidYearRange(string message)
        => new CriteriaBuildResult(CriteriaBuildState.InvalidYearRange, null, message);

    internal static CriteriaBuildResult Valid(SearchCriteria criteria)
        => new CriteriaBuildResult(CriteriaBuildState.Valid, criteria, null);
}

/// <summary>
/// Pure, WPF-independent validation and normalized criteria construction for optional
/// Made-by/credited-to (creator) and Year range filters. Year validation takes an explicit current
/// year so tests do not depend on the real calendar date.
/// </summary>
public static class FilterCriteriaLogic
{
    /// <summary>Oldest accepted year (inclusive).</summary>
    public const int MinYear = 1800;

    /// <summary>
    /// The production current-year provider (used as the upper Year bound). Tests pass an
    /// explicit year value instead so they never depend on the real date.
    /// </summary>
    public static int CurrentYear() => DateTime.UtcNow.Year;

    /// <summary>
    /// Maximum accepted year for a given current year: the current year plus one (inclusive).
    /// </summary>
    public static int MaxYear(int currentYear) => currentYear + 1;

    /// <summary>User-facing message when the Year-from endpoint is later than the Year-to endpoint.</summary>
    public const string YearRangeOrderMessage = "Year from must be earlier than or equal to Year to.";

    /// <summary>
    /// Validates an optional Year input. Accepts exactly four ASCII digits in the inclusive range
    /// 1800..MaxYear(currentYear). Leading/trailing whitespace is trimmed first.
    /// </summary>
    public static YearValidation ValidateYear(string? input, int currentYear)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return YearValidation.Blank();
        }

        string trimmed = input.Trim();
        if (trimmed.Length != 4)
        {
            return YearValidation.Invalid();
        }

        for (int i = 0; i < 4; i++)
        {
            if (trimmed[i] < '0' || trimmed[i] > '9')
            {
                return YearValidation.Invalid();
            }
        }

        int value = (trimmed[0] - '0') * 1000
            + (trimmed[1] - '0') * 100
            + (trimmed[2] - '0') * 10
            + (trimmed[3] - '0');

        if (value < MinYear || value > MaxYear(currentYear))
        {
            return YearValidation.Invalid();
        }

        return YearValidation.Valid(value);
    }

    /// <summary>
    /// User-facing message for an invalid Year: plainly states the required four-digit range.
    /// </summary>
    public static string InvalidYearMessage(int currentYear)
    {
        return "Year must be a four-digit year between "
            + MinYear.ToString()
            + " and "
            + MaxYear(currentYear).ToString()
            + ".";
    }

    /// <summary>
    /// Builds validated <see cref="SearchCriteria"/> from the launcher fields. Returns
    /// <see cref="CriteriaBuildState.NoCriteria"/> when everything is blank, or an invalid state
    /// (with a clear message) when a Year endpoint is malformed or the range is reversed. Never
    /// throws on user input and always preserves the entered values for UI correction.
    /// </summary>
    public static CriteriaBuildResult BuildCriteria(
        string? title,
        string? creator,
        string? yearFromText,
        string? yearToText,
        SearchScope scope,
        int currentYear)
    {
        YearValidation from = ValidateYear(yearFromText, currentYear);
        if (from.State == YearValidationState.Invalid)
        {
            return CriteriaBuildResult.InvalidYear(InvalidYearMessage(currentYear));
        }

        YearValidation to = ValidateYear(yearToText, currentYear);
        if (to.State == YearValidationState.Invalid)
        {
            return CriteriaBuildResult.InvalidYear(InvalidYearMessage(currentYear));
        }

        if (from.State == YearValidationState.Valid
            && to.State == YearValidationState.Valid
            && from.Value > to.Value)
        {
            return CriteriaBuildResult.InvalidYearRange(YearRangeOrderMessage);
        }

        SearchCriteria criteria = SearchCriteria.Validated(
            title,
            creator,
            from.Value,
            to.Value,
            currentYear,
            scope);
        if (!criteria.HasAnyCriterion)
        {
            return CriteriaBuildResult.NoCriteria();
        }

        return CriteriaBuildResult.Valid(criteria);
    }
}