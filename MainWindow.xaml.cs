using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;

namespace IArchiveMovieBrowser
{
    /// <summary>
    /// Compact search launcher. Owns the single shared <see cref="HttpClient"/> and
    /// <see cref="IInternetArchiveApiClient"/>, produces <see cref="CompletedSearch"/>
    /// snapshots, and opens/updates the separate search-results window. It never parses
    /// JSON or builds Internet Archive URLs itself.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int PageSize = 25;

        private readonly HttpClient _http;
        private readonly IInternetArchiveApiClient _client;
        private SearchResultsWindow? _resultsWindow;

        private CancellationTokenSource? _active;
        private long _generation;
        private bool _closing;

        private CompletedSearch? _latest;

        private readonly IPlayerExecutableStorage _playerStorage;
        private readonly GenreSelectionState _genreSelection = new GenreSelectionState();

        public MainWindow()
        {
            InitializeComponent();

            _http = new HttpClient();
            _client = new InternetArchiveApiClient(_http);
            _playerStorage = new ApplicationPlayerSettings();

            SearchProgress.Visibility = Visibility.Collapsed;
            ResultStage.Visibility = Visibility.Collapsed;
            StatusText.Text = SearchDisplayText.EmptyQueryText();
            SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
            ShowFilterMessage(null);
            UpdateDiscoveryStateText();

            ApplyPlayerState(ExternalPlayerLogic.AnalyzeStoredPath(_playerStorage.Load()));

            RegisterScrollHintTriggers();

            Closed += (sender, e) =>
            {
                _closing = true;
                CancelActiveRequest();
                if (_resultsWindow is not null)
                {
                    _resultsWindow.Close();
                    _resultsWindow = null;
                }
                _http.Dispose();
            };
        }

private void OnChoosePlayerClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose external media player",
                Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return; // cancelled: no state change
            }

            // Validate the chosen path; only a valid absolute .exe is saved.
            if (ExternalPlayerLogic.TryNormalizeForSave(dialog.FileName, out string? normalized))
            {
                _playerStorage.Save(normalized);
                ApplyPlayerState(ExternalPlayerLogic.AnalyzeStoredPath(normalized));
            }
        }

        private void OnClearPlayerClick(object sender, RoutedEventArgs e)
        {
            _playerStorage.Clear();
            ApplyPlayerState(ExternalPlayerLogic.AnalyzeStoredPath(null));
        }

        private void ApplyPlayerState(PlayerConfiguration config)
        {
            switch (config.State)
            {
                case PlayerConfigurationState.Ready:
                    PlayerPathText.Text = config.ValidatedPath;
                    PlayerStatusText.Text = PlayerDisplayText.ReadyStatus(config.ValidatedPath!);
                    ShowChooseButton(false);
                    break;

                case PlayerConfigurationState.Missing:
                    PlayerPathText.Text = config.ValidatedPath;
                    PlayerStatusText.Text = PlayerDisplayText.MissingStatus;
                    ShowChooseButton(true);
                    break;

                case PlayerConfigurationState.Invalid:
                    PlayerPathText.Text = config.ValidatedPath ?? "";
                    PlayerStatusText.Text = PlayerDisplayText.InvalidStatus;
                    ShowChooseButton(true);
                    break;

                default: // NotConfigured
                    PlayerPathText.Text = "";
                    PlayerStatusText.Text = PlayerDisplayText.NotConfiguredStatus;
                    ShowChooseButton(true);
                    break;
            }
        }

        private void ShowChooseButton(bool notConfigured)
        {
            ChoosePlayerButton.Visibility = notConfigured
                ? Visibility.Visible
                : Visibility.Collapsed;
            ChangePlayerButton.Visibility = notConfigured
                ? Visibility.Collapsed
                : Visibility.Visible;
            ClearPlayerButton.Visibility = notConfigured
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// Wires the events that can change whether the main content is taller than its visible
        /// viewport: the initial load, any window/viewport resize, and either expander toggling. Each
        /// of these fires after the relevant layout change, so the measured extent/viewport are the
        /// settled values used to decide the scroll hint's visibility.
        /// </summary>
        private void RegisterScrollHintTriggers()
        {
            Loaded += OnWindowLoaded;
            MainScrollViewer.SizeChanged += OnMainScrollSizeChanged;
            NarrowResults.Expanded += OnNarrowResultsExpanded;
            NarrowResults.Collapsed += OnNarrowResultsExpanded;
            GenreExpander.Expanded += OnGenreExpanded;
            GenreExpander.Collapsed += OnGenreExpanded;
        }

        private void OnWindowLoaded(object sender, EventArgs e)
        {
            UpdateScrollHint();
        }

        private void OnMainScrollSizeChanged(object sender, EventArgs e)
        {
            UpdateScrollHint();
        }

        private void OnNarrowResultsExpanded(object sender, EventArgs e)
        {
            UpdateScrollHint();
        }

        private void OnGenreExpanded(object sender, EventArgs e)
        {
            UpdateScrollHint();
        }

        /// <summary>
        /// Shows the one-line, non-focusable "scroll down" hint only while the main content is
        /// genuinely taller than the scroll viewport (i.e. exactly when there is below-the-fold
        /// content the user can reach by scrolling), and hides it whenever everything fits. Comparing
        /// the ScrollViewer's extent to its viewport uses the actual laid-out geometry, so the hint
        /// never tracks a mere scrollbar-present artifact and never affects search or navigation.
        /// </summary>
        private void UpdateScrollHint()
        {
            bool hasBelowTheFold = MainScrollViewer.ExtentHeight > MainScrollViewer.ViewportHeight + 0.5;
            ScrollHintText.Visibility = hasBelowTheFold ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            ExecuteSearchAsync();
        }

        /// <summary>The currently selected keyword-field target from the launcher ComboBox.</summary>
        private SearchKeywordFields SelectedKeywordFields()
        {
            int index = KeywordFieldsCombo.SelectedIndex;
            IReadOnlyList<SearchKeywordFields> options = SearchDisplayText.KeywordFieldOptions();
            if (index >= 0 && index < options.Count)
            {
                return options[index];
            }
            return SearchKeywordFields.Subjects;
        }

        /// <summary>The currently selected keyword match mode from the launcher ComboBox.</summary>
        private SearchKeywordMatchMode SelectedMatchMode()
        {
            int index = MatchModeCombo.SelectedIndex;
            IReadOnlyList<SearchKeywordMatchMode> options = SearchDisplayText.MatchModeOptions();
            if (index >= 0 && index < options.Count)
            {
                return options[index];
            }
            return SearchKeywordMatchMode.AllTerms;
        }

        /// <summary>The currently selected television-collection narrowing from the launcher ComboBox.</summary>
        private VideoCollectionScope SelectedVideoCollections()
        {
            int index = VideoCollectionsCombo.SelectedIndex;
            IReadOnlyList<VideoCollectionScope> options = SearchDisplayText.VideoCollectionOptions();
            if (index >= 0 && index < options.Count)
            {
                return options[index];
            }
            return VideoCollectionScope.AllWatchableVideo;
        }

        /// <summary>The currently selected result ordering from the launcher ComboBox.</summary>
        private SearchOrdering SelectedOrdering()
        {
            int index = OrderingCombo.SelectedIndex;
            IReadOnlyList<SearchOrdering> options = SearchDisplayText.OrderingOptions();
            if (index >= 0 && index < options.Count)
            {
                return options[index];
            }
            return SearchOrdering.InternetArchiveDefault;
        }

        /// <summary>
        /// Reports which discovery filters are currently set, as plain text next to Clear all filters.
        /// Never color alone, so the active state is available to assistive technology and in
        /// high-contrast rendering.
        /// </summary>
        private void UpdateDiscoveryStateText()
        {
            var active = new List<string>();

            if (!string.IsNullOrWhiteSpace(KeywordsTextBox.Text))
            {
                active.Add(SearchDisplayText.KeywordsLabel);
            }
            if (!string.IsNullOrWhiteSpace(ExcludedTermsTextBox.Text))
            {
                active.Add(SearchDisplayText.ExcludeWordsLabel);
            }

            VideoCollectionScope collections = SelectedVideoCollections();
            if (collections != VideoCollectionScope.AllWatchableVideo)
            {
                active.Add(SearchDisplayText.VideoCollectionsLabel(collections));
            }

            SearchOrdering ordering = SelectedOrdering();
            if (ordering != SearchOrdering.InternetArchiveDefault)
            {
                active.Add(SearchDisplayText.OrderingLabel(ordering));
            }

            DiscoveryStateText.Text = active.Count == 0
                ? ""
                : SearchDisplayText.DiscoverySectionHeader(active.Count) + ": " + string.Join(", ", active);
        }

        /// <summary>
        /// Shows or clears the text-based filter validation message. The message is always plain text
        /// (never color alone) and is announced through an assertive live region.
        /// </summary>
        private void ShowFilterMessage(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                FilterMessageText.Text = "";
                FilterMessageText.Visibility = Visibility.Collapsed;
                return;
            }

            FilterMessageText.Text = message!;
            FilterMessageText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Clears the validation message as soon as the user edits a discovery entry, so a stale
        /// correction is never left on screen next to corrected text.
        /// </summary>
        private void OnDiscoveryEntryChanged(object sender, TextChangedEventArgs e)
        {
            if (FilterMessageText.Visibility == Visibility.Visible)
            {
                ShowFilterMessage(null);
            }

            UpdateDiscoveryStateText();
        }

        /// <summary>
        /// Resets every control inside Narrow results to its documented default. The primary-row
        /// title and the primary search scope are deliberately preserved, because the scope is a
        /// deliberate "how wide is the catalogue" choice rather than a narrowing filter.
        ///
        /// This issues no request, does not auto-submit, and is idempotent.
        /// </summary>
        private void OnClearAllFiltersClick(object sender, RoutedEventArgs e)
        {
            ActorTextBox.Text = "";
            YearFromTextBox.Text = "";
            YearToTextBox.Text = "";

            ApplyGenreCheckboxes(false);
            SyncGenreSelectionFromCheckboxes();
            UpdateGenreSectionUI();

            ExcludeTrailersCheckBox.IsChecked = false;

            KeywordsTextBox.Text = "";
            KeywordFieldsCombo.SelectedIndex = 0;
            MatchModeCombo.SelectedIndex = 0;
            ExcludedTermsTextBox.Text = "";
            VideoCollectionsCombo.SelectedIndex = 0;
            OrderingCombo.SelectedIndex = 0;

            ShowFilterMessage(null);
            UpdateDiscoveryStateText();
            UpdateScrollHint();
        }

        /// <summary>The currently selected <see cref="SearchScope"/> from the launcher ComboBox.</summary>
        private SearchScope SelectedScope()
        {
            int index = SearchScopeCombo.SelectedIndex;
            var options = SearchDisplayText.ScopeOptions();
            if (index >= 0 && index < options.Count)
            {
                return options[index];
            }
            return SearchScope.WatchableVideo;
        }

        private void OnOpenResultsClick(object sender, RoutedEventArgs e)
        {
            OpenOrUpdateResults();
        }

        /// <summary>
        /// Handles any genre checkbox toggle. Only the visible header/count and the Clear action state
        /// change; no network request is issued here. Genres take effect when Search or Refresh runs.
        /// </summary>
        private void OnGenreSelectionChanged(object sender, RoutedEventArgs e)
        {
            SyncGenreSelectionFromCheckboxes();
            UpdateGenreSectionUI();
        }

        /// <summary>
        /// Clears every selected genre without issuing a request and leaves all other filters
        /// (title, scope, credited-to, year) untouched. Disabled/harmless when nothing is selected.
        /// </summary>
        private void OnClearGenresClick(object sender, RoutedEventArgs e)
        {
            ApplyGenreCheckboxes(false);
            SyncGenreSelectionFromCheckboxes();
            UpdateGenreSectionUI();
        }

        /// <summary>Refreshes the Genre header label and Clear action state from current selections.</summary>
        private void UpdateGenreSectionUI()
        {
            GenreHeaderText.Text = SearchDisplayText.GenreSectionHeader(_genreSelection.SelectedCount());
            ClearGenresButton.IsEnabled = _genreSelection.SelectedCount() > 0;
        }

        /// <summary>Sets every genre checkbox to the given checked state (no request).</summary>
        private void ApplyGenreCheckboxes(bool isChecked)
        {
            foreach (GenreDefinition genre in GenreCatalog.All())
            {
                GenreCheckboxFor(genre).IsChecked = isChecked;
            }
        }

        /// <summary>Reads all checkbox states into <see cref="_genreSelection"/>.</summary>
        private void SyncGenreSelectionFromCheckboxes()
        {
            _genreSelection.Clear();
            foreach (GenreDefinition genre in GenreCatalog.All())
            {
                if (GenreCheckboxFor(genre).IsChecked == true)
                {
                    _genreSelection.Select(genre.Key);
                }
            }
        }

        /// <summary>Selected genre keys in the declared catalog order (deduplicated), for the request.</summary>
        private IReadOnlyList<string> SelectedGenreKeys()
        {
            SyncGenreSelectionFromCheckboxes();
            return _genreSelection.SelectedKeys();
        }

        /// <summary>Returns the checkbox control for a catalog genre, by stable label-derived name.</summary>
        private CheckBox GenreCheckboxFor(GenreDefinition genre)
        {
            return genre.Key switch
            {
                "action" => GenreCheckboxAction,
                "adventure" => GenreCheckboxAdventure,
                "animation" => GenreCheckboxAnimation,
                "comedy" => GenreCheckboxComedy,
                "crime" => GenreCheckboxCrime,
                "documentary" => GenreCheckboxDocumentary,
                "drama" => GenreCheckboxDrama,
                "family" => GenreCheckboxFamily,
                "fantasy" => GenreCheckboxFantasy,
                "horror" => GenreCheckboxHorror,
                "mystery" => GenreCheckboxMystery,
                "romance" => GenreCheckboxRomance,
                "science-fiction" => GenreCheckboxScienceFiction,
                "thriller" => GenreCheckboxThriller,
                "war" => GenreCheckboxWar,
                _ => GenreCheckboxWestern
            };
        }

        /// <summary>
        /// Runs a fresh launcher search at page 1. Blank criteria never issue a request. A
        /// missing criterion set shows the existing prompt; an invalid Year shows clear
        /// non-modal validation and leaves the entered value visible. Each new search cancels
        /// any launcher request still in flight.
        /// </summary>
        private async void ExecuteSearchAsync()
        {
            CancelActiveRequest();
            _generation++;

            SearchScope scope = SelectedScope();
            int currentYear = FilterCriteriaLogic.CurrentYear();
            CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
                QueryTextBox.Text,
                ActorTextBox.Text,
                YearFromTextBox.Text,
                YearToTextBox.Text,
                scope,
                currentYear,
                KeywordsTextBox.Text,
                ExcludedTermsTextBox.Text,
                SelectedKeywordFields(),
                SelectedMatchMode(),
                SelectedVideoCollections(),
                SelectedOrdering());

            if (build.State == CriteriaBuildState.NoCriteria)
            {
                SearchProgress.Visibility = Visibility.Collapsed;
                ResultStage.Visibility = Visibility.Collapsed;
                StatusText.Text = SearchDisplayText.EmptyQueryText();
                SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
                UpdateScrollHint();
                return;
            }

            if (!build.IsValid)
            {
                // A rejected entry blocks submission: no request is issued and a prior valid search
                // is not re-run. The message is text-based and names what to correct.
                SearchProgress.Visibility = Visibility.Collapsed;
                ResultStage.Visibility = Visibility.Collapsed;
                StatusText.Text = build.Message ?? "";
                if (build.State == CriteriaBuildState.InvalidKeywordEntry)
                {
                    ShowFilterMessage(build.Message);
                }
                SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
                UpdateScrollHint();
                return;
            }

            // The entry parsed cleanly, so any previous correction no longer applies.
            ShowFilterMessage(null);

            long currentGeneration = _generation;
            var tokenSource = new CancellationTokenSource();
            _active = tokenSource;

            SearchProgress.Visibility = Visibility.Visible;
            ResultStage.Visibility = Visibility.Collapsed;
            StatusText.Text = SearchDisplayText.SearchingText();
            SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
            UpdateScrollHint();

            try
            {
                // Genres and trailer exclusion are both narrowing options layered onto the validated
                // criteria. They are read only when Search runs (never on toggle), and they ride
                // along inside CompletedSearch so paging and Refresh keep them applied. The discovery
                // dimensions are already inside build.Criteria for the same reason: the whole
                // specification, including ordering, is carried on the immutable criteria.
                SearchCriteria genreCriteria = build.Criteria!
                    .WithGenres(SelectedGenreKeys())
                    .WithExcludeTrailers(ExcludeTrailersCheckBox.IsChecked == true);
                var request = new InternetArchiveSearchRequest(genreCriteria, 1, PageSize);
                InternetArchiveSearchPage results =
                    await _client.SearchAsync(request, tokenSource.Token);

                if (currentGeneration != _generation || _closing)
                {
                    return; // superseded or window closed
                }

                SearchProgress.Visibility = Visibility.Collapsed;

                var completed = new CompletedSearch(genreCriteria, results.NumFound, 1, results.Results);
                _latest = completed;

                if (results.Results.Count == 0)
                {
                    ResultStageText.Text = SearchDisplayText.NoMatchingItemsText();
                    ResultStage.Visibility = Visibility.Visible;
                    SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
                    UpdateScrollHint();
                    return;
                }

                ResultStageText.Text = SearchDisplayText.FoundText(results.NumFound);
                ResultStage.Visibility = Visibility.Visible;
                SetOpenResultsEnabled(results.NumFound);
                UpdateScrollHint();
                UpdateOpenResultsWindow();
            }
            catch (OperationCanceledException)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    SearchProgress.Visibility = Visibility.Collapsed;
                    ShowError("Operation cancelled.");
                }
            }
            catch (InternetArchiveApiException ex)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    SearchProgress.Visibility = Visibility.Collapsed;
                    ShowError(ex.Operation + ": " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    SearchProgress.Visibility = Visibility.Collapsed;
                    ShowError("Unexpected error: " + ex.Message);
                }
            }
        }

        private void OpenOrUpdateResults()
        {
            if (_resultsWindow is null)
            {
                // Deliberately NOT setting Owner: an owned Results window inherits WPF
                // activation/minimize behavior that drops the launcher to the taskbar when
                // Results closes. It is an independent top-level window, tracked here so the
                // launcher still closes it on exit (no orphans).
                _resultsWindow = new SearchResultsWindow(_client, _playerStorage, new ExternalPlayerLauncher());
                _resultsWindow.Closed += (sender, e) => _resultsWindow = null;
            }

            if (_latest is not null)
            {
                _resultsWindow.ApplyCompletedSearch(_latest);
            }
            _resultsWindow.Show();
        }

        private void UpdateOpenResultsWindow()
        {
            if (_resultsWindow is not null && _latest is not null)
            {
                _resultsWindow.ApplyCompletedSearch(_latest);
            }
        }

        private void CancelActiveRequest()
        {
            var current = _active;
            if (current is not null)
            {
                current.Cancel();
                current.Dispose();
                _active = null;
            }
        }

        private void SetOpenResultsEnabled(long numFound)
        {
            OpenResultsButton.IsEnabled = true;
            OpenResultsButton.Content = SearchDisplayText.OpenResultsText(numFound);
        }

        private void SetOpenResultsDisabled(string label)
        {
            OpenResultsButton.IsEnabled = false;
            OpenResultsButton.Content = label;
        }

        private void ShowError(string message)
        {
            SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
            ResultStageText.Text = SearchDisplayText.StatusError(message);
            ResultStage.Visibility = Visibility.Visible;
            UpdateScrollHint();
        }
    }
}