using System;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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

        public MainWindow()
        {
            InitializeComponent();

            _http = new HttpClient();
            _client = new InternetArchiveApiClient(_http);

            SearchProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = SearchDisplayText.EmptyQueryText();
            SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);

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

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            ExecuteSearchAsync();
        }

        private void OnOpenResultsClick(object sender, RoutedEventArgs e)
        {
            OpenOrUpdateResults();
        }

        /// <summary>
        /// Runs a fresh launcher search at page 1. Blank/whitespace queries never issue a
        /// request. Each new search cancels any launcher request still in flight.
        /// </summary>
        private async void ExecuteSearchAsync()
        {
            string query = QueryTextBox.Text;

            if (string.IsNullOrWhiteSpace(query))
            {
                CancelActiveRequest();
                _generation++;
                SearchProgress.Visibility = Visibility.Collapsed;
                StatusText.Text = SearchDisplayText.EmptyQueryText();
                SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
                return;
            }

            CancelActiveRequest();
            _generation++;
            long currentGeneration = _generation;

            var tokenSource = new CancellationTokenSource();
            _active = tokenSource;

            SearchProgress.Visibility = Visibility.Visible;
            StatusText.Text = SearchDisplayText.SearchingText();
            SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);

            try
            {
                var request = new InternetArchiveSearchRequest(query, 1, PageSize);
                InternetArchiveSearchPage results =
                    await _client.SearchAsync(request, tokenSource.Token);

                if (currentGeneration != _generation || _closing)
                {
                    return; // superseded or window closed
                }

                SearchProgress.Visibility = Visibility.Collapsed;

                var completed = new CompletedSearch(query, results.NumFound, 1, results.Results);
                _latest = completed;

                if (results.Results.Count == 0)
                {
                    StatusText.Text = SearchDisplayText.NoMatchingItemsText();
                    SetOpenResultsDisabled(SearchDisplayText.NoResultsLabelText);
                    return;
                }

                StatusText.Text = SearchDisplayText.FoundText(results.NumFound);
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
                _resultsWindow = new SearchResultsWindow(_client)
                {
                    Owner = this
                };
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
            StatusText.Text = SearchDisplayText.StatusError(message);
        }
    }
}