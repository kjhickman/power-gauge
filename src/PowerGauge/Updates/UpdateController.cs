using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PowerGauge.Updates;

public sealed record TrayUpdateStatus(
    string VersionHeader,
    string ActionHeader,
    bool IsActionEnabled,
    string? TooltipNotice = null);

internal sealed record AvailableRelease(
    AppVersion Version,
    string HtmlUrl,
    GitHubReleaseAssetDto? Asset);

internal sealed record IdleUpdateState(string? Note = null);

internal sealed record CheckingForUpdatesState(AvailableRelease? PreviousRelease);

internal sealed record UpdateAvailableState(AvailableRelease Release);

internal sealed record BrowserOpenFailedState(AvailableRelease Release);

internal sealed record DownloadingUpdateState(AvailableRelease Release);

internal sealed record InstallingUpdateState(AvailableRelease Release);

internal sealed record UpdateFailedState(AvailableRelease Release);

internal union UpdateState(
    IdleUpdateState,
    CheckingForUpdatesState,
    UpdateAvailableState,
    BrowserOpenFailedState,
    DownloadingUpdateState,
    InstallingUpdateState,
    UpdateFailedState)
{
    public bool IsBusy => this is CheckingForUpdatesState or DownloadingUpdateState or InstallingUpdateState;

    public AvailableRelease? AvailableRelease => this switch
    {
        CheckingForUpdatesState(var previous) => previous,
        UpdateAvailableState(var release) => release,
        BrowserOpenFailedState(var release) => release,
        DownloadingUpdateState(var release) => release,
        InstallingUpdateState(var release) => release,
        UpdateFailedState(var release) => release,
        IdleUpdateState => null,
        null => null,
    };
}

internal interface IReleaseFeedClient
{
    Task<GitHubReleaseDto?> FetchLatestReleaseAsync(CancellationToken cancellationToken = default);

    Task DownloadAssetAsync(string downloadUrl, string destinationPath, string? expectedDigest, CancellationToken cancellationToken = default);
}

public sealed class UpdateController
{
    private readonly IReleaseFeedClient _releaseClient;
    private readonly InstallEnvironmentInfo _installEnvironment;
    private readonly Func<string> _getCurrentVersionString;
    private readonly Action<string> _openBrowserUrl;
    private UpdateState _state = new IdleUpdateState();

    public UpdateController()
        : this(
            new GitHubReleaseClient(),
            InstallEnvironment.Detect(),
            () => AppIdentity.DisplayVersion,
            SelfUpdateApplier.OpenBrowserUrl)
    {
    }

    internal UpdateController(
        IReleaseFeedClient releaseClient,
        InstallEnvironmentInfo installEnvironment,
        Func<string> getCurrentVersionString,
        Action<string> openBrowserUrl)
    {
        _releaseClient = releaseClient;
        _installEnvironment = installEnvironment;
        _getCurrentVersionString = getCurrentVersionString;
        _openBrowserUrl = openBrowserUrl;
        CurrentStatus = FormatStatus(_state);
    }

    public event Action<TrayUpdateStatus>? StatusChanged;

    public TrayUpdateStatus CurrentStatus { get; private set; }

    internal UpdateState State => _state;

    public async Task CheckForUpdatesAsync(bool isManualCheck)
    {
        if (_state.IsBusy)
        {
            return;
        }

        var previousRelease = _state.AvailableRelease;
        TransitionTo(new CheckingForUpdatesState(previousRelease), publishStatus: isManualCheck || previousRelease is null);

        try
        {
            var release = await _releaseClient.FetchLatestReleaseAsync();
            if (release is null
                || release.Draft
                || release.Prerelease
                || !AppVersion.TryParse(release.TagName, out var latestVersion)
                || latestVersion is null)
            {
                TransitionTo(new IdleUpdateState(isManualCheck ? "up to date" : null));
                return;
            }

            if (AppVersion.TryParse(_getCurrentVersionString(), out var currentVersion)
                && currentVersion is not null
                && latestVersion.CompareTo(currentVersion) <= 0)
            {
                TransitionTo(new IdleUpdateState(isManualCheck ? "up to date" : null));
                return;
            }

            var matchedAsset = InstallEnvironment.FindMatchingAsset(release, _installEnvironment.Kind);
            var htmlUrl = !string.IsNullOrWhiteSpace(release.HtmlUrl)
                ? release.HtmlUrl
                : $"{AppIdentity.GitHubRepositoryUrl}/releases/latest";

            TransitionTo(new UpdateAvailableState(new AvailableRelease(latestVersion, htmlUrl, matchedAsset)));
        }
        catch
        {
            if (previousRelease is not null)
            {
                TransitionTo(new UpdateAvailableState(previousRelease));
            }
            else
            {
                TransitionTo(new IdleUpdateState(isManualCheck ? "update check failed" : null));
            }
        }
    }

    public async Task HandleMenuActionAsync(Action requestShutdown)
    {
        if (_state.IsBusy)
        {
            return;
        }

        if (_state.AvailableRelease is not { } targetRelease)
        {
            await CheckForUpdatesAsync(isManualCheck: true);
            return;
        }

        if (!_installEnvironment.SupportsSelfUpdate
            || targetRelease.Asset is null
            || string.IsNullOrWhiteSpace(targetRelease.Asset.BrowserDownloadUrl)
            || string.IsNullOrWhiteSpace(targetRelease.Asset.Name))
        {
            try
            {
                _openBrowserUrl(targetRelease.HtmlUrl);
            }
            catch
            {
                TransitionTo(new BrowserOpenFailedState(targetRelease));
            }

            return;
        }

        TransitionTo(new DownloadingUpdateState(targetRelease));

        try
        {
            var stagingDir = SelfUpdateApplier.GetStagingDirectory(targetRelease.Version.Normalized);
            var assetPath = Path.Combine(stagingDir, targetRelease.Asset.Name);

            await _releaseClient.DownloadAssetAsync(
                targetRelease.Asset.BrowserDownloadUrl,
                assetPath,
                targetRelease.Asset.Digest);

            TransitionTo(new InstallingUpdateState(targetRelease));

            SelfUpdateApplier.LaunchInstallerAndScheduleRestart(_installEnvironment, assetPath, stagingDir);
            requestShutdown();
        }
        catch
        {
            TransitionTo(new UpdateFailedState(targetRelease));
        }
    }

    private void TransitionTo(UpdateState nextState, bool publishStatus = true)
    {
        _state = nextState;
        if (!publishStatus)
        {
            return;
        }

        CurrentStatus = FormatStatus(nextState);
        StatusChanged?.Invoke(CurrentStatus);
    }

    private TrayUpdateStatus FormatStatus(UpdateState state)
    {
        var displayVersion = _getCurrentVersionString();

        return state switch
        {
            IdleUpdateState(var note) => new TrayUpdateStatus(
                string.IsNullOrWhiteSpace(note)
                    ? $"Version: {displayVersion}"
                    : $"Version: {displayVersion} ({note})",
                "Check for updates",
                IsActionEnabled: true),

            CheckingForUpdatesState => new TrayUpdateStatus(
                $"Version: {displayVersion}",
                "Checking for updates...",
                IsActionEnabled: false),

            UpdateAvailableState(var release) => new TrayUpdateStatus(
                $"Version: {displayVersion} ({release.Version.DisplayTag} available)",
                _installEnvironment.SupportsSelfUpdate && release.Asset is not null
                    ? $"Install update ({release.Version.DisplayTag})"
                    : $"Download update ({release.Version.DisplayTag})",
                IsActionEnabled: true,
                $"Update available: {release.Version.DisplayTag}"),

            BrowserOpenFailedState(var release) => new TrayUpdateStatus(
                $"Version: {displayVersion} (failed to open browser)",
                $"Download update ({release.Version.DisplayTag})",
                IsActionEnabled: true,
                $"Update available: {release.Version.DisplayTag}"),

            DownloadingUpdateState(var release) => new TrayUpdateStatus(
                $"Version: {displayVersion} ({release.Version.DisplayTag} available)",
                $"Downloading update ({release.Version.DisplayTag})...",
                IsActionEnabled: false,
                $"Downloading {release.Version.DisplayTag}..."),

            InstallingUpdateState(var release) => new TrayUpdateStatus(
                $"Version: {displayVersion} (installing {release.Version.DisplayTag}...)",
                $"Installing update ({release.Version.DisplayTag})...",
                IsActionEnabled: false),

            UpdateFailedState(var release) => new TrayUpdateStatus(
                $"Version: {displayVersion} (update failed)",
                $"Retry update ({release.Version.DisplayTag})",
                IsActionEnabled: true,
                $"Update available: {release.Version.DisplayTag}"),

            null => new TrayUpdateStatus(
                $"Version: {displayVersion}",
                "Check for updates",
                IsActionEnabled: true),
        };
    }
}
