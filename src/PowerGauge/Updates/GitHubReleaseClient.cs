using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PowerGauge.Updates;

internal sealed class GitHubReleaseClient : IReleaseFeedClient
{
    private const string Sha256Prefix = "sha256:";
    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{AppIdentity.GitHubRepository}/releases/latest");
    private readonly HttpClient _httpClient;

    public GitHubReleaseClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppIdentity.ProductName, AppIdentity.DisplayVersion));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<GitHubReleaseDto?> FetchLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

        using var response = await _httpClient.GetAsync(LatestReleaseUri, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        return await JsonSerializer.DeserializeAsync(stream, GitHubJsonContext.Default.GitHubReleaseDto, timeoutCts.Token);
    }

    public async Task DownloadAssetAsync(string downloadUrl, string destinationPath, string? expectedDigest, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await networkStream.CopyToAsync(fileStream, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(expectedDigest)
            && expectedDigest.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var expectedHex = expectedDigest[Sha256Prefix.Length..].Trim();
            await using var verifyStream = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var actualHash = await SHA256.HashDataAsync(verifyStream, cancellationToken);
            var actualHex = Convert.ToHexStringLower(actualHash);

            if (!string.Equals(actualHex, expectedHex, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(destinationPath);
                }
                catch
                {
                    // Ignore cleanup errors after checksum failure.
                }

                throw new InvalidDataException("Downloaded update package failed SHA-256 verification.");
            }
        }
    }
}

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto>? Assets { get; set; }
}

internal sealed class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

[JsonSerializable(typeof(GitHubReleaseDto))]
internal partial class GitHubJsonContext : JsonSerializerContext;
