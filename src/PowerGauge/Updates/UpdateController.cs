using System;
using System.IO;
using System.Threading.Tasks;

namespace PowerGauge.Updates;

public sealed record TrayUpdateStatus(
    string VersionHeader,
    string ActionHeader,
    bool IsActionEnabled,
    string? TooltipNotice = null);

public sealed class UpdateController
{
    private readonly GitHubReleaseClient _releaseClient = new();
    private readonly InstallEnvironmentInfo _installEnvironment = InstallEnvironment.Detect();
    private AvailableRelease? _availableRelease;
    private bool _isBusy;

    public UpdateController()
    {
        CurrentStatus = CreateDefaultStatus();
    }

    public event Action<TrayUpdateStatus>? StatusChanged;

    public TrayUpdateStatus CurrentStatus { get; private set; }

    public async Task CheckForUpdatesAsync(bool isManualCheck)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        if (isManualCheck || _availableRelease is null)
        {
            PublishStatus(new TrayUpdateStatus(
                $"Version: {AppIdentity.DisplayVersion}",
                "Checking for updates...",
                false));
        }

        try
        {
            var release = await _releaseClient.FetchLatestReleaseAsync();
            if (release is null
                || release.Draft
                || release.Prerelease
                || !AppVersion.TryParse(release.TagName, out var latestVersion)
                || latestVersion is null)
            {
                _availableRelease = null;
                PublishStatus(CreateIdleStatus(isManualCheck ? "up to date" : null));
                return;
            }

            if (AppVersion.TryParse(AppIdentity.DisplayVersion, out var currentVersion)
                && currentVersion is not null
                && latestVersion.CompareTo(currentVersion) <= 0)
            {
                _availableRelease = null;
                PublishStatus(CreateIdleStatus(isManualCheck ? "up to date" : null));
                return;
            }

            var matchedAsset = InstallEnvironment.FindMatchingAsset(release, _installEnvironment.Kind);
            var htmlUrl = !string.IsNullOrWhiteSpace(release.HtmlUrl)
                ? release.HtmlUrl
                : $"{AppIdentity.GitHubRepositoryUrl}/releases/latest";

            _availableRelease = new AvailableRelease(latestVersion, htmlUrl, matchedAsset);
            PublishStatus(CreateUpdateAvailableStatus(_availableRelease));
        }
        catch
        {
            if (_availableRelease is not null)
            {
                PublishStatus(CreateUpdateAvailableStatus(_availableRelease));
            }
            else
            {
                PublishStatus(CreateIdleStatus(isManualCheck ? "update check failed" : null));
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    public async Task HandleMenuActionAsync(Action requestShutdown)
    {
        if (_isBusy)
        {
            return;
        }

        if (_availableRelease is null)
        {
            await CheckForUpdatesAsync(isManualCheck: true);
            return;
        }

        var targetRelease = _availableRelease;
        if (!_installEnvironment.SupportsSelfUpdate
            || targetRelease.Asset is null
            || string.IsNullOrWhiteSpace(targetRelease.Asset.BrowserDownloadUrl)
            || string.IsNullOrWhiteSpace(targetRelease.Asset.Name))
        {
            try
            {
                SelfUpdateApplier.OpenBrowserUrl(targetRelease.HtmlUrl);
            }
            catch
            {
                PublishStatus(new TrayUpdateStatus(
                    $"Version: {AppIdentity.DisplayVersion} (failed to open browser)",
                    $"Download update ({targetRelease.Version.DisplayTag})",
                    true,
                    $"Update available: {targetRelease.Version.DisplayTag}"));
            }

            return;
        }

        _isBusy = true;
        PublishStatus(new TrayUpdateStatus(
            $"Version: {AppIdentity.DisplayVersion} ({targetRelease.Version.DisplayTag} available)",
            $"Downloading update ({targetRelease.Version.DisplayTag})...",
            false,
            $"Downloading {targetRelease.Version.DisplayTag}..."));

        try
        {
            var stagingDir = SelfUpdateApplier.GetStagingDirectory(targetRelease.Version.Normalized);
            var assetPath = Path.Combine(stagingDir, targetRelease.Asset.Name);

            await _releaseClient.DownloadAssetAsync(
                targetRelease.Asset.BrowserDownloadUrl,
                assetPath,
                targetRelease.Asset.Digest);

            PublishStatus(new TrayUpdateStatus(
                $"Version: {AppIdentity.DisplayVersion} (installing {targetRelease.Version.DisplayTag}...)",
                $"Installing update ({targetRelease.Version.DisplayTag})...",
                false));

            SelfUpdateApplier.LaunchInstallerAndScheduleRestart(_installEnvironment, assetPath, stagingDir);
            requestShutdown();
        }
        catch
        {
            _isBusy = false;
            PublishStatus(new TrayUpdateStatus(
                $"Version: {AppIdentity.DisplayVersion} (update failed)",
                $"Retry update ({targetRelease.Version.DisplayTag})",
                true,
                $"Update available: {targetRelease.Version.DisplayTag}"));
        }
    }

    private TrayUpdateStatus CreateUpdateAvailableStatus(AvailableRelease release)
    {
        var canSelfInstall = _installEnvironment.SupportsSelfUpdate && release.Asset is not null;
        var actionLabel = canSelfInstall
            ? $"Install update ({release.Version.DisplayTag})"
            : $"Download update ({release.Version.DisplayTag})";

        return new TrayUpdateStatus(
            $"Version: {AppIdentity.DisplayVersion} ({release.Version.DisplayTag} available)",
            actionLabel,
            true,
            $"Update available: {release.Version.DisplayTag}");
    }

    private static TrayUpdateStatus CreateDefaultStatus() => CreateIdleStatus(note: null);

    private static TrayUpdateStatus CreateIdleStatus(string? note)
    {
        var versionHeader = string.IsNullOrWhiteSpace(note)
            ? $"Version: {AppIdentity.DisplayVersion}"
            : $"Version: {AppIdentity.DisplayVersion} ({note})";

        return new TrayUpdateStatus(versionHeader, "Check for updates", true);
    }

    private void PublishStatus(TrayUpdateStatus status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(status);
    }

    private sealed record AvailableRelease(
        AppVersion Version,
        string HtmlUrl,
        GitHubReleaseAssetDto? Asset);
}
