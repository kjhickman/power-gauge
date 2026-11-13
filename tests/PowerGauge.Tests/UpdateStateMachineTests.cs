using System;
using System.Threading;
using System.Threading.Tasks;
using PowerGauge.Updates;
using Xunit;

namespace PowerGauge.Tests;

public sealed class UpdateStateMachineTests
{
    [Fact]
    public async Task CheckForUpdates_TransitionsFromIdleToUpdateAvailable_AndFormatsStatus()
    {
        var feedClient = new FakeReleaseFeedClient
        {
            ReleaseToReturn = new GitHubReleaseDto
            {
                TagName = "v0.3.0",
                HtmlUrl = "https://github.com/kjhickman/power-gauge/releases/tag/v0.3.0",
                Assets =
                [
                    new GitHubReleaseAssetDto
                    {
                        Name = "PowerGauge-windows-x64-setup-0.3.0.exe",
                        BrowserDownloadUrl = "https://example.invalid/setup.exe",
                    }
                ],
            },
        };

        var env = new InstallEnvironmentInfo(InstallKind.WindowsInstaller, @"C:\App", @"C:\App\PowerGauge.exe");
        var controller = new UpdateController(feedClient, env, () => "0.2.1", _ => { });

        Assert.True(controller.State is IdleUpdateState);
        Assert.Equal("Check for updates", controller.CurrentStatus.ActionHeader);

        await controller.CheckForUpdatesAsync(isManualCheck: true);

        Assert.True(controller.State is UpdateAvailableState);
        Assert.Equal("Version: 0.2.1 (v0.3.0 available)", controller.CurrentStatus.VersionHeader);
        Assert.Equal("Install update (v0.3.0)", controller.CurrentStatus.ActionHeader);
        Assert.True(controller.CurrentStatus.IsActionEnabled);
    }

    [Fact]
    public async Task HandleMenuAction_InDevelopmentInstall_FallsBackToBrowserOpenFailedStateOnError()
    {
        var feedClient = new FakeReleaseFeedClient
        {
            ReleaseToReturn = new GitHubReleaseDto
            {
                TagName = "v0.3.0",
                HtmlUrl = "https://github.com/kjhickman/power-gauge/releases/tag/v0.3.0",
            },
        };

        var env = new InstallEnvironmentInfo(InstallKind.Development, @"C:\Dev", null);
        var controller = new UpdateController(
            feedClient,
            env,
            () => "0.2.1",
            _ => throw new InvalidOperationException("Browser launch failed."));

        await controller.CheckForUpdatesAsync(isManualCheck: false);
        await controller.HandleMenuActionAsync(() => { });

        Assert.True(controller.State is BrowserOpenFailedState);
        Assert.Equal("Version: 0.2.1 (failed to open browser)", controller.CurrentStatus.VersionHeader);
        Assert.Equal("Download update (v0.3.0)", controller.CurrentStatus.ActionHeader);
    }

    [Fact]
    public async Task HandleMenuAction_WhenDownloadThrows_TransitionsToUpdateFailedStateWithRetry()
    {
        var feedClient = new FakeReleaseFeedClient
        {
            ReleaseToReturn = new GitHubReleaseDto
            {
                TagName = "v0.3.0",
                HtmlUrl = "https://github.com/kjhickman/power-gauge/releases/tag/v0.3.0",
                Assets =
                [
                    new GitHubReleaseAssetDto
                    {
                        Name = "PowerGauge-windows-x64-setup-0.3.0.exe",
                        BrowserDownloadUrl = "https://example.invalid/setup.exe",
                    }
                ],
            },
            DownloadException = new InvalidOperationException("Network error."),
        };

        var env = new InstallEnvironmentInfo(InstallKind.WindowsInstaller, @"C:\App", @"C:\App\PowerGauge.exe");
        var controller = new UpdateController(feedClient, env, () => "0.2.1", _ => { });

        await controller.CheckForUpdatesAsync(isManualCheck: false);
        await controller.HandleMenuActionAsync(() => { });

        Assert.True(controller.State is UpdateFailedState);
        Assert.False(controller.State.IsBusy);
        Assert.Equal("Version: 0.2.1 (update failed)", controller.CurrentStatus.VersionHeader);
        Assert.Equal("Retry update (v0.3.0)", controller.CurrentStatus.ActionHeader);
        Assert.True(controller.CurrentStatus.IsActionEnabled);
    }

    [Fact]
    public async Task HandleMenuAction_WhenManualCheckFindsUpdate_PromptsAndInstallsWhenAccepted()
    {
        UpdatePromptInfo? observedPrompt = null;
        var installerLaunched = false;
        var shutdownRequested = false;

        var controller = CreateController(
            "0.3.0",
            prompt =>
            {
                observedPrompt = prompt;
                return Task.FromResult(true);
            },
            (_, _, _) => installerLaunched = true);

        await controller.HandleMenuActionAsync(() => shutdownRequested = true);

        Assert.Equal(new UpdatePromptInfo("v0.3.0", "v0.3.1", CanSelfUpdate: true), observedPrompt);
        Assert.True(installerLaunched);
        Assert.True(shutdownRequested);
        Assert.True(controller.State is InstallingUpdateState);
    }

    [Fact]
    public async Task HandleMenuAction_WhenManualCheckDeclined_StaysAvailableAndInstallsDirectlyNextClick()
    {
        var promptCount = 0;
        var installerLaunched = false;
        var shutdownRequested = false;

        var controller = CreateController(
            "0.3.0",
            _ =>
            {
                promptCount++;
                return Task.FromResult(false);
            },
            (_, _, _) => installerLaunched = true);

        await controller.HandleMenuActionAsync(() => shutdownRequested = true);

        Assert.Equal(1, promptCount);
        Assert.False(installerLaunched);
        Assert.False(shutdownRequested);
        Assert.True(controller.State is UpdateAvailableState);
        Assert.Equal("Install update (v0.3.1)", controller.CurrentStatus.ActionHeader);

        await controller.HandleMenuActionAsync(() => shutdownRequested = true);

        Assert.Equal(1, promptCount);
        Assert.True(installerLaunched);
        Assert.True(shutdownRequested);
    }

    [Theory]
    [InlineData("0.3.1", true, "Version: 0.3.1 (up to date)", "Check for updates")]
    [InlineData("0.3.0", false, "Version: 0.3.0 (v0.3.1 available)", "Install update (v0.3.1)")]
    public async Task UpdateChecks_DoNotPrompt_WhenUpToDateOrAutomatic(
        string currentVersion,
        bool isMenuAction,
        string expectedVersionHeader,
        string expectedActionHeader)
    {
        var prompted = false;
        var controller = CreateController(currentVersion, _ =>
        {
            prompted = true;
            return Task.FromResult(true);
        });

        await (isMenuAction
            ? controller.HandleMenuActionAsync(() => { })
            : controller.CheckForUpdatesAsync(isManualCheck: false));

        Assert.False(prompted);
        Assert.Equal(expectedVersionHeader, controller.CurrentStatus.VersionHeader);
        Assert.Equal(expectedActionHeader, controller.CurrentStatus.ActionHeader);
    }

    private static UpdateController CreateController(
        string currentVersion,
        Func<UpdatePromptInfo, Task<bool>> promptForUpdateAsync,
        Action<InstallEnvironmentInfo, string, string>? launchInstaller = null)
    {
        var feedClient = new FakeReleaseFeedClient { ReleaseToReturn = CreateWindowsRelease("v0.3.1") };
        var env = new InstallEnvironmentInfo(InstallKind.WindowsInstaller, @"C:\App", @"C:\App\PowerGauge.exe");
        return new UpdateController(feedClient, env, () => currentVersion, _ => { }, promptForUpdateAsync, launchInstaller);
    }

    private static GitHubReleaseDto CreateWindowsRelease(string tagName)
    {
        var version = tagName.TrimStart('v');
        return new GitHubReleaseDto
        {
            TagName = tagName,
            HtmlUrl = $"https://github.com/kjhickman/power-gauge/releases/tag/{tagName}",
            Assets =
            [
                new GitHubReleaseAssetDto
                {
                    Name = $"PowerGauge-windows-x64-setup-{version}.exe",
                    BrowserDownloadUrl = "https://example.invalid/setup.exe",
                }
            ],
        };
    }

    private sealed class FakeReleaseFeedClient : IReleaseFeedClient
    {
        public GitHubReleaseDto? ReleaseToReturn { get; set; }

        public Exception? DownloadException { get; set; }

        public Task<GitHubReleaseDto?> FetchLatestReleaseAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReleaseToReturn);
        }

        public Task DownloadAssetAsync(string downloadUrl, string destinationPath, string? expectedDigest, CancellationToken cancellationToken = default)
        {
            if (DownloadException is not null)
            {
                return Task.FromException(DownloadException);
            }

            return Task.CompletedTask;
        }
    }
}
