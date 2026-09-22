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

        private CancellationTokenSource? _linkProbeCts;
        private long _linkProbeGeneration;
        private bool _linkProbeActive;

        private CancellationTokenSource? _playerResolveCts;
        private long _playerResolveGeneration;
        private bool _playerResolveActive;

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
                InvalidateLinkCheck();
                InvalidatePlayerResolve();
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
            InvalidateLinkCheck();
            InvalidatePlayerResolve();
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
            ResetLinkCheckDisplay();

            Object? selected = PlayableList.SelectedItem;
            if (selected is null)
            {
                DirectUrlTextBox.Text = PlayableVideoPreview.SelectionPlaceholder();
                UpdateExternalPlayerButton();
                UpdateLinkCheckButton();
                return;
            }

            FileRow row = (FileRow)selected;
            DirectUrlTextBox.Text = PlayableVideoPreview.Build(_identifier, row.NameText);
            UpdateExternalPlayerButton();
            UpdateLinkCheckButton();
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
            if (readiness.IsReady && !_playerResolveActive)
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
        /// Explicit user action: resolve the selected file's canonical IA URL through redirects,
        /// validate the trusted final URI, and then start the configured external player with that
        /// final URI as exactly one argument. Resolution is header-only (body never read).
        /// </summary>
        private void OnOpenInPlayerClick(object sender, RoutedEventArgs e)
        {
            if (_playerResolveActive)
            {
                return; // duplicate prevention while resolving/launching
            }

            Object? selected = PlayableList.SelectedItem;
            if (selected is not FileRow row)
            {
                ShowStatus(ExternalPlayerLaunchDisplayText.NoSelectionExplanation);
                return;
            }

            Uri? canonical = SelectedDirectUri();
            if (canonical is null)
            {
                ShowStatus(ExternalPlayerLaunchDisplayText.NotReadyExplanation(
                    ExternalPlayerLaunchNotReadyReason.NoDirectUrl));
                return;
            }

            ExternalPlayerLaunchReadiness readiness =
                ExternalPlayerLaunchLogic.BuildReadiness(_identifier, row.NameText, _playerStorage.Load());
            if (!readiness.IsReady || readiness.Request is null)
            {
                ShowStatus(ExternalPlayerLaunchDisplayText.NotReadyExplanation(readiness.NotReadyReason));
                ApplyExternalPlayerReadiness(readiness);
                return;
            }

            _playerResolveActive = true;
            _playerResolveGeneration++;
            long generation = _playerResolveGeneration;
            ApplyExternalPlayerReadiness(readiness); // disables the button while resolving
            ClearResolvedPlayerUrl();
            ShowPlayerResolveStatus(PlayerResolveDisplayText.ResolvingText);

            CancelAndDisposePlayerResolveCts();
            _playerResolveCts = new CancellationTokenSource();
            CancellationToken token = _playerResolveCts.Token;

            _ = RunResolveAndLaunchAsync(canonical, readiness.Request.ExecutablePath, generation, token);
        }

        private async Task RunResolveAndLaunchAsync(
            Uri canonical,
            string executablePath,
            long generation,
            CancellationToken token)
        {
            PlayerResolutionResult result = await _client.ResolvePlayerUrlAsync(canonical, token);

            if (_closing || generation != _playerResolveGeneration)
            {
                return; // cancelled/stale: never launch and never touch the UI late
            }

            if (result.Outcome != PlayerResolutionOutcome.Resolved)
            {
                ShowPlayerResolveStatus(PlayerResolveDisplayText.OutcomeText(result));
                CompletePlayerResolve();
                return;
            }

            ExternalPlayerLaunchRequest? launch =
                PlayerResolveThenLaunchLogic.BuildLaunchRequest(result, executablePath);
            if (launch is null)
            {
                ShowPlayerResolveStatus(PlayerResolveDisplayText.UnsafeFinalText);
                CompletePlayerResolve();
                return;
            }

            SetResolvedPlayerUrl(result.FinalUri);
            ExternalPlayerLaunchResult outcome = _launcher.Launch(launch);

            string status = outcome.Succeeded
                ? ExternalPlayerLaunchDisplayText.OpeningStatus
                : ExternalPlayerLaunchDisplayText.FailureStatus(outcome.FailureReason);
            ShowPlayerResolveStatus(status);
            ShowStatus(status);

            CompletePlayerResolve();
        }

        private void CompletePlayerResolve()
        {
            _playerResolveActive = false;
            CancelAndDisposePlayerResolveCts();
            UpdateExternalPlayerButton();
        }

        private void InvalidatePlayerResolve()
        {
            _playerResolveGeneration++;
            _playerResolveActive = false;
            CancelAndDisposePlayerResolveCts();
            ClearResolvedPlayerUrl();
        }

        private void CancelAndDisposePlayerResolveCts()
        {
            if (_playerResolveCts is not null)
            {
                _playerResolveCts.Cancel();
                _playerResolveCts.Dispose();
                _playerResolveCts = null;
            }
        }

        private void ShowPlayerResolveStatus(string text)
        {
            OpenPlayerStatusText.Text = text;
        }

        private void SetResolvedPlayerUrl(Uri? finalUri)
        {
            if (ResolvedPlayerUrlTextBox is not null && finalUri is not null)
            {
                ResolvedPlayerUrlTextBox.Text = finalUri.AbsoluteUri;
                ResolvedPlayerUrlTextBox.Visibility = Visibility.Visible;
            }
        }

        private void ClearResolvedPlayerUrl()
        {
            if (ResolvedPlayerUrlTextBox is not null)
            {
                ResolvedPlayerUrlTextBox.Text = "";
                ResolvedPlayerUrlTextBox.Visibility = Visibility.Collapsed;
            }
        }

// --- Selected-video-link diagnostic -------------------------------------------------

        /// <summary>
        /// Resolves the canonical trusted HTTPS IA download URI for the currently selected playable
        /// file, or null when there is no selection or the direct URL is not resolvable/trusted.
        /// </summary>
        private Uri? SelectedDirectUri()
        {
            Object? selected = PlayableList.SelectedItem;
            if (selected is not FileRow row)
            {
                return null;
            }
            try
            {
                return InternetArchiveDownloadUrlBuilder.BuildDownloadUri(_identifier, row.NameText);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private void UpdateLinkCheckButton()
        {
            CheckLinkButton.IsEnabled = !_linkProbeActive && SelectedDirectUri() is not null;
        }

        private void OnCheckLinkClick(object sender, RoutedEventArgs e)
        {
            if (_linkProbeActive)
            {
                return;
            }

            Uri? uri = SelectedDirectUri();
            if (uri is null)
            {
                ShowCheckStatus(DirectLinkDiagnosticText.NoSelectionText);
                return;
            }

            _linkProbeActive = true;
            _linkProbeGeneration++;
            long generation = _linkProbeGeneration;
            UpdateLinkCheckButton();
            DirectLinkSummaryTextBox.Visibility = Visibility.Collapsed;
            ShowCheckStatus(DirectLinkDiagnosticText.CheckingText);

            CancelAndDisposeLinkProbeCts();
            _linkProbeCts = new CancellationTokenSource();
            CancellationToken token = _linkProbeCts.Token;

            _ = RunLinkCheckAsync(uri, generation, token);
        }

        private async Task RunLinkCheckAsync(Uri uri, long generation, CancellationToken token)
        {
            DirectLinkProbe result = await _client.ProbeVideoLinkAsync(uri, token);

            if (_closing || generation != _linkProbeGeneration)
            {
                return; // stale/closed: a newer check or window close owns the UI state
            }

            ApplyLinkCheckResult(result);
            CompleteLinkCheck();
        }

        private void ApplyLinkCheckResult(DirectLinkProbe result)
        {
            ShowCheckStatus(DirectLinkDiagnosticText.OutcomeText(result));
            string summary = DirectLinkDiagnosticText.SummaryText(result);
            if (summary.Length > 0)
            {
                DirectLinkSummaryTextBox.Text = summary;
                DirectLinkSummaryTextBox.Visibility = Visibility.Visible;
            }
        }

        private void ShowCheckStatus(string text)
        {
            CheckLinkStatusText.Text = text;
        }

        private void CompleteLinkCheck()
        {
            _linkProbeActive = false;
            CancelAndDisposeLinkProbeCts();
            UpdateLinkCheckButton();
        }

        private void InvalidateLinkCheck()
        {
            _linkProbeGeneration++;
            _linkProbeActive = false;
            CancelAndDisposeLinkProbeCts();
        }

        private void CancelAndDisposeLinkProbeCts()
        {
            if (_linkProbeCts is not null)
            {
                _linkProbeCts.Cancel();
                _linkProbeCts.Dispose();
                _linkProbeCts = null;
            }
        }

        private void ResetLinkCheckDisplay()
        {
            CheckLinkStatusText.Text = "";
            DirectLinkSummaryTextBox.Text = "";
            DirectLinkSummaryTextBox.Visibility = Visibility.Collapsed;
            ClearResolvedPlayerUrl();
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