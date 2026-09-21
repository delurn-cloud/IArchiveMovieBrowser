using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;

namespace IArchiveMovieBrowser
{
    /// <summary>
    /// Main (and only) screen: a compact text-first Internet Archive search-results pane.
    /// The UI talks only to <see cref="IInternetArchiveApiClient"/>; it neither parses JSON
    /// nor builds Internet Archive API URLs.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int PageSize = 25;

        private readonly HttpClient _http;
        private readonly IInternetArchiveApiClient _client;

        private CancellationTokenSource? _active;
        private long _generation;
        private int _page = 1;
        private bool _closing;

        public MainWindow()
        {
            InitializeComponent();

            _http = new HttpClient();
            _client = new InternetArchiveApiClient(_http);
            StatusText.Text = SearchDisplayText.EmptyQueryText();

            Closed += (sender, e) =>
            {
                _closing = true;
                CancelActiveRequest();
                _http.Dispose();
            };
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            ExecuteSearchAsync(1);
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            ExecuteSearchAsync(_page);
        }

        private void OnPreviousClick(object sender, RoutedEventArgs e)
        {
            ExecuteSearchAsync(_page - 1);
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            ExecuteSearchAsync(_page + 1);
        }

        /// <summary>
        /// Runs the current query for the requested page. Blank/whitespace queries never
        /// issue a network request. A replacement request cancels any prior in-flight one.
        /// </summary>
        private async void ExecuteSearchAsync(int requestedPage)
        {
            string query = QueryTextBox.Text;

            if (string.IsNullOrWhiteSpace(query))
            {
                CancelActiveRequest();
                _generation++;
                ShowEmptyQueryHint();
                return;
            }

            int page = requestedPage < 1 ? 1 : requestedPage;

            CancelActiveRequest();
            _generation++;
            long currentGeneration = _generation;

            var tokenSource = new CancellationTokenSource();
            _active = tokenSource;
            _page = page;
            ShowLoading();

            try
            {
                var request = new InternetArchiveSearchRequest(query, page, PageSize);
                InternetArchiveSearchPage results =
                    await _client.SearchAsync(request, tokenSource.Token);

                if (currentGeneration != _generation || _closing)
                {
                    return; // superseded by a newer request, or the window closed
                }

                ShowResults(results, page);
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

        private void ClearResultsPane()
        {
            ResultsList.ItemsSource = null;
            SummaryText.Text = "";
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = false;
        }

        private void ShowLoading()
        {
            ClearResultsPane();
            ShowStatus(SearchDisplayText.StatusLoading());
        }

        private void ShowEmptyQueryHint()
        {
            ClearResultsPane();
            ShowStatus(SearchDisplayText.EmptyQueryText());
        }

        private void ShowResults(InternetArchiveSearchPage page, int currentPage)
        {
            var lines = new List<string>();
            foreach (InternetArchiveSearchResult item in page.Results)
            {
                lines.Add(SearchDisplayText.RowText(item));
            }

            ResultsList.ItemsSource = lines;
            SummaryText.Text = SearchDisplayText.ResultsSummary(page.NumFound, currentPage);
            PreviousButton.IsEnabled = SearchDisplayText.PreviousEnabled(currentPage);
            NextButton.IsEnabled = SearchDisplayText.NextEnabled(page.NumFound, currentPage, PageSize);

            ShowStatus(lines.Count == 0
                ? SearchDisplayText.StatusNoResults()
                : SearchDisplayText.StatusReady(currentPage));
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
        }
    }
}