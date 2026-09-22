using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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
        private readonly IPlayerExecutableStorage _playerStorage;
        private readonly IExternalPlayerLauncher _launcher;

        private CancellationTokenSource? _active;
        private long _generation;
        private string? _identifier;
        private bool _closing;

        public DetailsWindow(
            IInternetArchiveApiClient client,
            IPlayerExecutableStorage playerStorage,
            IExternalPlayerLauncher launcher)
        {
            InitializeComponent();
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _playerStorage = playerStorage ?? throw new ArgumentNullException(nameof(playerStorage));
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));

            StatusText.Text = DetailsDisplayText.StatusLoadingDetails();

            PlayableList.SelectionChanged += OnPlayableListSelectionChanged;
            DirectUrlTextBox.Text = PlayableVideoPreview.SelectionPlaceholder();
            OpenPlayerStatusText.Text = ExternalPlayerLaunchDisplayText.NoSelectionExplanation;

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
            _ = FetchImageAsync(identifier, tokenSource, currentGeneration);
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
            ShowDetailsPreviewLoading();
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

            DirectUrlTextBox.Text = PlayableVideoPreview.SelectionPlaceholder();
            UpdateExternalPlayerButton();

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

        private void OnPlayableListSelectionChanged(object sender, EventArgs e)
        {
            UpdateDirectUrlPreview();
        }

        /// <summary>
        /// Shows the direct IA stream URL for the currently selected playable candidate, or the
        /// selection placeholder when there is no valid candidate selected. Also refreshes the
        /// external-player open button readiness for the current selection.
        /// </summary>
        private void UpdateDirectUrlPreview()
        {
            Object? selected = PlayableList.SelectedItem;
            if (selected is null)
            {
                DirectUrlTextBox.Text = PlayableVideoPreview.SelectionPlaceholder();
                UpdateExternalPlayerButton();
                return;
            }

            FileRow row = (FileRow)selected;
            DirectUrlTextBox.Text = PlayableVideoPreview.Build(_identifier, row.NameText);
            UpdateExternalPlayerButton();
        }

        /// <summary>
        /// Recomputes whether the currently selected playable candidate can be opened in the
        /// configured external player (selection + valid player + resolvable direct URL) and
        /// reflects it on the open button and its nearby status text.
        /// </summary>
        private void UpdateExternalPlayerButton()
        {
            Object? selected = PlayableList.SelectedItem;
            string? filename = selected is FileRow row ? row.NameText : null;

            ExternalPlayerLaunchReadiness readiness =
                ExternalPlayerLaunchLogic.BuildReadiness(_identifier, filename, _playerStorage.Load());
            ApplyExternalPlayerReadiness(readiness);
        }

        private void ApplyExternalPlayerReadiness(ExternalPlayerLaunchReadiness readiness)
        {
            if (readiness.IsReady)
            {
                OpenInPlayerButton.IsEnabled = true;
                OpenPlayerStatusText.Text = ExternalPlayerLaunchDisplayText.ReadyHint;
            }
            else
            {
                OpenInPlayerButton.IsEnabled = false;
                OpenPlayerStatusText.Text =
                    ExternalPlayerLaunchDisplayText.NotReadyExplanation(readiness.NotReadyReason);
            }
        }

        /// <summary>
        /// Explicit user action: revalidates the selection + configured player, builds the
        /// canonical direct URL through the shared builder, and starts the configured executable
        /// exactly once with that URL as a single argument.
        /// </summary>
        private void OnOpenInPlayerClick(object sender, RoutedEventArgs e)
        {
            Object? selected = PlayableList.SelectedItem;
            if (selected is not FileRow row)
            {
                return;
            }

            ExternalPlayerLaunchReadiness readiness =
                ExternalPlayerLaunchLogic.BuildReadiness(_identifier, row.NameText, _playerStorage.Load());

            if (!readiness.IsReady || readiness.Request is null)
            {
                ShowStatus(
                    ExternalPlayerLaunchDisplayText.NotReadyExplanation(readiness.NotReadyReason));
                ApplyExternalPlayerReadiness(readiness);
                return;
            }

            ExternalPlayerLaunchResult result = _launcher.Launch(readiness.Request);

            if (result.Succeeded)
            {
                ShowStatus(ExternalPlayerLaunchDisplayText.OpeningStatus);
            }
            else
            {
                ShowStatus(ExternalPlayerLaunchDisplayText.FailureStatus(result.FailureReason));
            }
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
        }

// --- Item image preview (reuses the shared metadata token/generation lifetime) ------

        private async Task FetchImageAsync(
            string identifier,
            CancellationTokenSource tokenSource,
            long currentGeneration)
        {
            if (InternetArchiveImagePreview.BuildImageUrl(identifier) is null)
            {
                ShowDetailsPreviewUnavailable();
                return;
            }

            byte[]? bytes = null;
            try
            {
                bytes = await _client.GetImageBytesAsync(identifier, tokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                return; // closed or superseded; no late update
            }
            catch (Exception)
            {
                bytes = null;
            }

            if (currentGeneration != _generation || _closing)
            {
                return; // superseded or window closed
            }

            if (bytes is null || bytes.Length == 0)
            {
                ShowDetailsPreviewUnavailable();
                return;
            }

            BitmapImage? image = DecodeImage(bytes);
            if (currentGeneration != _generation || _closing)
            {
                return;
            }

            if (image is null)
            {
                ShowDetailsPreviewUnavailable();
                return;
            }

            ShowDetailsPreviewLoaded(image);
        }

        private BitmapImage? DecodeImage(byte[] bytes)
        {
            try
            {
                BitmapImage image = new BitmapImage();
                using (MemoryStream stream = new MemoryStream(bytes, writable: false))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad; // release stream after init
                    image.StreamSource = stream;
                    image.EndInit();
                }
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null; // not a usable image / decode failure
            }
        }

        private void ShowDetailsPreviewLoading()
        {
            DetailsPreviewStatusText.Text = InternetArchiveImagePreview.LoadingMessage;
            DetailsPreviewStatusText.Visibility = Visibility.Visible;
            DetailsPreviewImage.Source = null;
            DetailsPreviewImage.Visibility = Visibility.Collapsed;
            DetailsPreviewFallback.Visibility = Visibility.Collapsed;
        }

        private void ShowDetailsPreviewLoaded(BitmapImage image)
        {
            string accessible = InternetArchiveImagePreview.LoadedImageAccessibleText(TitleText.Text);
            DetailsPreviewImage.Source = image;
            DetailsPreviewImage.Visibility = Visibility.Visible;
            DetailsPreviewImage.ToolTip = accessible;
            System.Windows.Automation.AutomationProperties.SetName(DetailsPreviewImage, accessible);

            DetailsPreviewStatusText.Visibility = Visibility.Collapsed;
            DetailsPreviewFallback.Visibility = Visibility.Collapsed;
        }

        private void ShowDetailsPreviewUnavailable()
        {
            DetailsPreviewImage.Source = null;
            DetailsPreviewImage.Visibility = Visibility.Collapsed;
            DetailsPreviewStatusText.Visibility = Visibility.Collapsed;
            DetailsPreviewFallback.Visibility = Visibility.Visible;
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