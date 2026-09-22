using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;

namespace IArchiveMovieBrowser
{
    /// <summary>
    /// Search Results window: owns browsing, refresh, and paging for one completed
    /// Internet Archive search. It receives the shared read-only
    /// <see cref="IInternetArchiveApiClient"/> from the launcher and never disposes it
    /// (the launcher owns the HttpClient).
    /// </summary>
    public partial class SearchResultsWindow : Window
    {
        private const int PageSize = 25;

        private readonly IInternetArchiveApiClient _client;
        private readonly IPlayerExecutableStorage _playerStorage;
        private readonly IExternalPlayerLauncher _launcher;

        private CancellationTokenSource? _active;
        private long _generation;
        private SearchCriteria _criteria = SearchCriteria.TitleOnly(null, SearchScope.Everything);
        private int _page = 1;
        private long _numFound;
        private bool _closing;

        private readonly List<string> _pageIdentifiers = new List<string>();
        private DetailsWindow? _detailsWindow;
        private DateTime _lastMouseUpUtc = DateTime.MinValue;
        private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(500);

        public SearchResultsWindow(
            IInternetArchiveApiClient client,
            IPlayerExecutableStorage playerStorage,
            IExternalPlayerLauncher launcher)
        {
            InitializeComponent();
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _playerStorage = playerStorage ?? throw new ArgumentNullException(nameof(playerStorage));
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));

            Closed += (sender, e) =>
            {
                _closing = true;
                CancelActiveRequest();
                if (_detailsWindow is not null)
                {
                    _detailsWindow.Close();
                    _detailsWindow = null;
                }
            };

            ResultsList.SelectionChanged += OnSelectionChanged;
            ResultsList.MouseUp += OnResultsMouseUp;
        }

        /// <summary>
        /// Applies a completed search snapshot: updates the query/summary text, paging
        /// buttons, the result list, and status. Any in-flight request in this window is
        /// superseded (cancelled) so it cannot render stale results.
        /// </summary>
        public void ApplyCompletedSearch(CompletedSearch completedSearch)
        {
            if (completedSearch is null)
            {
                throw new ArgumentNullException(nameof(completedSearch));
            }

            CancelActiveRequest();
            _generation++;
            _criteria = completedSearch.Criteria;
            _page = completedSearch.Page;
            _numFound = completedSearch.NumFound;

            ShowResults(completedSearch.Results, completedSearch.Page);
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            RunPageAsync(_page);
        }

        private void OnPreviousClick(object sender, RoutedEventArgs e)
        {
            RunPageAsync(_page - 1);
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            RunPageAsync(_page + 1);
        }

        private async void RunPageAsync(int requestedPage)
        {
            int page = requestedPage < 1 ? 1 : requestedPage;

            CancelActiveRequest();
            _generation++;
            long currentGeneration = _generation;

            var tokenSource = new CancellationTokenSource();
            _active = tokenSource;

            ShowLoading(page);

            try
            {
                var request = new InternetArchiveSearchRequest(_criteria, page, PageSize);
                InternetArchiveSearchPage results =
                    await _client.SearchAsync(request, tokenSource.Token);

                if (currentGeneration != _generation || _closing)
                {
                    return; // superseded or window closed
                }

                _page = page;
                _numFound = results.NumFound;
                ShowResults(results.Results, page);
            }
            catch (OperationCanceledException)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    ShowStatus(SearchDisplayText.StatusCancelled());
                }
            }
            catch (InternetArchiveApiException ex)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    ShowStatus(SearchDisplayText.StatusError(ex.Operation + ": " + ex.Message));
                }
            }
            catch (Exception ex)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    ShowStatus(SearchDisplayText.StatusError("Unexpected error: " + ex.Message));
                }
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

        private void ShowLoading(int page)
        {
            SummaryText.Text = SearchDisplayText.QuerySummaryText(_criteria, _numFound, page);
            PreviousButton.IsEnabled = SearchDisplayText.PreviousEnabled(page);
            NextButton.IsEnabled = SearchDisplayText.NextEnabled(_numFound, page, PageSize);
            StatusText.Text = SearchDisplayText.StatusLoading();
        }

        private void ShowResults(IReadOnlyList<InternetArchiveSearchResult> results, int page)
        {
            var lines = new List<string>();
            _pageIdentifiers.Clear();
            foreach (InternetArchiveSearchResult item in results)
            {
                lines.Add(SearchDisplayText.RowText(item));
                _pageIdentifiers.Add(item.Identifier);
            }

            ResultsList.ItemsSource = lines;
            SummaryText.Text = SearchDisplayText.QuerySummaryText(_criteria, _numFound, page);
            PreviousButton.IsEnabled = SearchDisplayText.PreviousEnabled(page);
            NextButton.IsEnabled = SearchDisplayText.NextEnabled(_numFound, page, PageSize);
            RefreshButton.IsEnabled = true;
            UpdateOpenDetailsEnabled();

            StatusText.Text = lines.Count == 0
                ? SearchDisplayText.StatusNoResults()
                : SearchDisplayText.StatusReady(page);
        }

        private void OnOpenDetailsClick(object sender, RoutedEventArgs e)
        {
            string? identifier = SelectedIdentifier();
            if (identifier is not null)
            {
                ShowOrOpenDetails(identifier);
            }
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            UpdateOpenDetailsEnabled();
        }

        private void OnResultsDoubleClicked(object sender, EventArgs e)
        {
            string? identifier = SelectedIdentifier();
            if (identifier is not null)
            {
                ShowOrOpenDetails(identifier);
            }
        }

        /// <summary>
        /// Detects a double-click via the ListView MouseUp event (no dedicated double-click
        /// event exists in this control dialect): two mouse-ups within the tolerance window
        /// are treated as a double-click and routed through the shared open method.
        /// </summary>
        private void OnResultsMouseUp(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            bool isDoubleClick = (now - _lastMouseUpUtc) <= DoubleClickWindow;
            _lastMouseUpUtc = now;

            if (isDoubleClick)
            {
                OnResultsDoubleClicked(sender, e);
            }
        }

        /// <summary>Shared open path used by both the Open Details button and double-click.</summary>
        private void ShowOrOpenDetails(string identifier)
        {
            if (_detailsWindow is null)
            {
                _detailsWindow = new DetailsWindow(_client, _playerStorage, _launcher)
                {
                    Owner = this
                };
                _detailsWindow.Closed += (sender, e) => _detailsWindow = null;
            }

            _detailsWindow.LoadIdentifier(identifier);
            _detailsWindow.Show();
        }

        private string? SelectedIdentifier()
        {
            Object? selected = ResultsList.SelectedItem;
            if (selected is null)
            {
                return null;
            }

            // selected is the two-line display string for the current row.
        if (selected is null)
        {
            return null;
        }
        string? selectedLine = selected.ToString();
        if (selectedLine is null)
        {
            return null;
        }
        int index = 0;
        foreach (string line in (IEnumerable<Object>)ResultsList.ItemsSource)
            {
                if (line == selectedLine && index < _pageIdentifiers.Count)
                {
                    string id = _pageIdentifiers[index];
                    return string.IsNullOrWhiteSpace(id) ? null : id;
                }
                index++;
            }
            return null;
        }

        private void UpdateOpenDetailsEnabled()
        {
            OpenDetailsButton.IsEnabled = SelectedIdentifier() is not null;
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
        }
    }
}