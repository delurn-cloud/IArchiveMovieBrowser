using System;
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

            ApplyPlayerState(ExternalPlayerLogic.AnalyzeStoredPath(_playerStorage.Load()));

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

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            ExecuteSearchAsync();
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
                YearTextBox.Text,
                scope,
                currentYear);

            if (build.State == CriteriaBuildState.NoCriteria)
            {
                SearchProgress.Visibility = Visibility.Collapsed;
                ResultStage.Visibility = Visibility.Collapsed;
                StatusText.Text = SearchDisplayText.EmptyQueryText();
                SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
                return;
            }

            if (build.State == CriteriaBuildState.InvalidYear)
            {
                SearchProgress.Visibility = Visibility.Collapsed;
                ResultStage.Visibility = Visibility.Collapsed;
                StatusText.Text = build.Message;
                SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
                return;
            }

            long currentGeneration = _generation;
            var tokenSource = new CancellationTokenSource();
            _active = tokenSource;

            SearchProgress.Visibility = Visibility.Visible;
            ResultStage.Visibility = Visibility.Collapsed;
            StatusText.Text = SearchDisplayText.SearchingText();
            SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);

            try
            {
                var request = new InternetArchiveSearchRequest(build.Criteria!, 1, PageSize);
                InternetArchiveSearchPage results =
                    await _client.SearchAsync(request, tokenSource.Token);

                if (currentGeneration != _generation || _closing)
                {
                    return; // superseded or window closed
                }

                SearchProgress.Visibility = Visibility.Collapsed;

                var completed = new CompletedSearch(build.Criteria!, results.NumFound, 1, results.Results);
                _latest = completed;

                if (results.Results.Count == 0)
                {
                    ResultStageText.Text = SearchDisplayText.NoMatchingItemsText();
                    ResultStage.Visibility = Visibility.Visible;
                    SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
                    return;
                }

                ResultStageText.Text = SearchDisplayText.FoundText(results.NumFound);
                ResultStage.Visibility = Visibility.Visible;
                SetOpenResultsEnabled(results.NumFound);
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
        }
    }
}