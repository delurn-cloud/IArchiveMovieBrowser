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

        private CancellationTokenSource? _active;
        private long _generation;
        private string _query = "";
        private int _page = 1;
        private long _numFound;
        private bool _closing;

        public SearchResultsWindow(IInternetArchiveApiClient client)
        {
            InitializeComponent();
            _client = client ?? throw new ArgumentNullException(nameof(client));

            Closed += (sender, e) =>
            {
                _closing = true;
                CancelActiveRequest();
            };
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
            _query = completedSearch.Query;
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
                var request = new InternetArchiveSearchRequest(_query, page, PageSize);
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
            SummaryText.Text = SearchDisplayText.QuerySummaryText(_query, _numFound, page);
            PreviousButton.IsEnabled = SearchDisplayText.PreviousEnabled(page);
            NextButton.IsEnabled = SearchDisplayText.NextEnabled(_numFound, page, PageSize);
            StatusText.Text = SearchDisplayText.StatusLoading();
        }

        private void ShowResults(IReadOnlyList<InternetArchiveSearchResult> results, int page)
        {
            var lines = new List<string>();
            foreach (InternetArchiveSearchResult item in results)
            {
                lines.Add(SearchDisplayText.RowText(item));
            }

            ResultsList.ItemsSource = lines;
            SummaryText.Text = SearchDisplayText.QuerySummaryText(_query, _numFound, page);
            PreviousButton.IsEnabled = SearchDisplayText.PreviousEnabled(page);
            NextButton.IsEnabled = SearchDisplayText.NextEnabled(_numFound, page, PageSize);
            RefreshButton.IsEnabled = true;

            StatusText.Text = lines.Count == 0
                ? SearchDisplayText.StatusNoResults()
                : SearchDisplayText.StatusReady(page);
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
        }
    }
}