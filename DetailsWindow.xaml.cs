using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;

namespace IArchiveMovieBrowser
{
    /// <summary>
    /// Informational IA details window: shows current metadata and the file inventory for a
    /// single Internet Archive item. It receives the shared read-only
    /// <see cref="IInternetArchiveApiClient"/> and never disposes it. Every identifier or
    /// Refresh requests fresh data via GetItemMetadataAsync.
    /// </summary>
    public partial class DetailsWindow : Window
    {
        private readonly IInternetArchiveApiClient _client;

        private CancellationTokenSource? _active;
        private long _generation;
        private string? _identifier;
        private bool _closing;

        public DetailsWindow(IInternetArchiveApiClient client)
        {
            InitializeComponent();
            _client = client ?? throw new ArgumentNullException(nameof(client));

            StatusText.Text = DetailsDisplayText.StatusLoadingDetails();

            Closed += (sender, e) =>
            {
                _closing = true;
                CancelActiveRequest();
            };
        }

        /// <summary>
        /// Loads (or reseeds to) fresh metadata for the given identifier, cancelling and
        /// superseding any prior in-flight request within this window only.
        /// </summary>
        public void LoadIdentifier(string identifier)
        {
            _identifier = identifier;

            CancelActiveRequest();
            _generation++;
            long currentGeneration = _generation;

            var tokenSource = new CancellationTokenSource();
            _active = tokenSource;

            ShowLoading();

            _ = FetchAsync(identifier, tokenSource, currentGeneration);
        }

        private void OnRefreshDetailsClick(object sender, RoutedEventArgs e)
        {
            if (_identifier is not null)
            {
                LoadIdentifier(_identifier);
            }
        }

        private async Task FetchAsync(string identifier, CancellationTokenSource tokenSource, long currentGeneration)
        {
            try
            {
                InternetArchiveItemMetadata metadata =
                    await _client.GetItemMetadataAsync(identifier, tokenSource.Token);

                if (currentGeneration != _generation || _closing)
                {
                    return; // superseded or window closed
                }

                ShowMetadata(metadata);
            }
            catch (OperationCanceledException)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    ShowStatus(DetailsDisplayText.StatusCancelled());
                }
            }
            catch (InternetArchiveApiException ex)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    ShowStatus(DetailsDisplayText.StatusError(ex.Operation + ": " + ex.Message));
                    RefreshDetailsButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                if (currentGeneration == _generation && !_closing)
                {
                    ShowStatus(DetailsDisplayText.StatusError("Unexpected error: " + ex.Message));
                    RefreshDetailsButton.IsEnabled = true;
                }
            }
        }

        private void ShowLoading()
        {
            RefreshDetailsButton.IsEnabled = false;
            DescriptionText.Text = "";
            SetPlayableSection(null);
            FilesList.ItemsSource = null;
            StatusText.Text = DetailsDisplayText.StatusLoadingDetails();
        }

        private void ShowMetadata(InternetArchiveItemMetadata metadata)
        {
            TitleText.Text = DetailsDisplayText.TitleText(metadata);
            IdentifierText.Text = "Identifier: " + metadata.Identifier;
            CreatorText.Text = "Creator: " + DetailsDisplayText.JoinedText(metadata.Creators);
            DateYearText.Text = "Date / Year: " + DetailsDisplayText.DateYearText(metadata);
            MediaTypeText.Text = "Media type: " + DetailsDisplayText.MediaTypeText(metadata);
            CollectionsText.Text = "Collections: " + DetailsDisplayText.JoinedText(metadata.Collections);
            SubjectsText.Text = "Subjects: " + DetailsDisplayText.JoinedText(metadata.Subjects);
            LicenseText.Text = "License: " + DetailsDisplayText.LicenseText(metadata);
            DescriptionText.Text = DetailsDisplayText.DescriptionText(metadata.Description);

            SetPlayableSection(metadata.Files);
            FilesList.ItemsSource = BuildFileRows(metadata.Files);

            RefreshDetailsButton.IsEnabled = true;
            if (DetailsDisplayText.AnyMediaCandidate(metadata.Files))
            {
                ShowStatus(DetailsDisplayText.StatusReadyDetails());
            }
            else
            {
                ShowStatus(DetailsDisplayText.StatusNoUsableMedia());
            }
        }

        private IReadOnlyList<FileRow> BuildFileRows(IReadOnlyList<InternetArchiveRemoteFile> files)
        {
            var rows = new List<FileRow>();
            foreach (InternetArchiveRemoteFile file in files)
            {
                rows.Add(new FileRow(
                    file.Name,
                    file.Format ?? DetailsDisplayText.MissingText,
                    DetailsDisplayText.SizeText(file.Size),
                    DetailsDisplayText.KindText(file),
                    DetailsDisplayText.MediaCandidateText(file)));
            }
            return rows;
        }

        /// <summary>Populates the "Playable video files" section above the full inventory.</summary>
        private void SetPlayableSection(IReadOnlyList<InternetArchiveRemoteFile>? files)
        {
            IReadOnlyList<InternetArchiveRemoteFile> candidates =
                PlayableVideoClassifier.SelectPlayableCandidates(files);

            if (candidates.Count == 0)
            {
                PlayableList.ItemsSource = null;
                PlayableList.Visibility = Visibility.Collapsed;
                PlayableEmptyText.Visibility = Visibility.Visible;
                PlayableEmptyText.Text = DetailsDisplayText.NoPlayableVideoMessage();
                return;
            }

            PlayableList.ItemsSource = BuildFileRows(candidates);
            PlayableList.Visibility = Visibility.Visible;
            PlayableEmptyText.Visibility = Visibility.Collapsed;
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
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
    }

    /// <summary>Plain per-row display data bound to the file inventory list.</summary>
    public sealed class FileRow
    {
        public string NameText { get; }
        public string FormatText { get; }
        public string SizeText { get; }
        public string KindText { get; }
        public string MediaText { get; }

        public FileRow(string nameText, string formatText, string sizeText, string kindText, string mediaText)
        {
            NameText = nameText;
            FormatText = formatText;
            SizeText = sizeText;
            KindText = kindText;
            MediaText = mediaText;
        }
    }
}