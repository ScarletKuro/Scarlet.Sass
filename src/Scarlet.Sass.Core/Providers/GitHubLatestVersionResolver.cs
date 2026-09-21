using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Sass.Core.Providers;

/// <summary>
/// Resolves the concrete version behind GitHub's "latest" release redirect by inspecting the Location
/// header without following it.
/// </summary>
/// <remarks>
/// Dart Sass's release asset names include the version, so "latest" must first be resolved through the
/// release page redirect (for example ".../releases/tag/1.104.1") before the asset URL can be built.
/// </remarks>
public sealed class GitHubLatestVersionResolver : ILatestVersionResolver
{
    private static readonly Regex VersionFromUriRegex = new(@"(?:/tag/|/download/)(?<ver>[0-9][^/]*)", RegexOptions.Compiled);
    private readonly HttpMessageHandler? _handler;

    public GitHubLatestVersionResolver()
    {
    }

    /// <param name="handler">
    /// Overrides the handler used for the redirect probe. Internal - production code always uses the
    /// default (a fresh, non-redirecting <see cref="HttpClientHandler"/>); tests use this to substitute a
    /// fake handler without touching the network.
    /// </param>
    internal GitHubLatestVersionResolver(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public async Task<string?> TryResolveVersionAsync(string latestDownloadUrl, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(_handler ?? new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: _handler is null);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "Scarlet.Sass");

        using var response = await client.GetAsync(latestDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if ((int)response.StatusCode is < 300 or > 399)
        {
            // Not a redirect - GitHub's response shape may have changed, or the URL isn't what we expect.
            return null;
        }

        return TryParseVersionFromUri(response.Headers.Location);
    }

    /// <summary>
    /// Extracts the concrete version (e.g. "1.104.1") from a redirect Location such as
    /// ".../releases/tag/1.104.1". Returns <see langword="null"/> if the URI does not contain a recognizable
    /// version segment (including a null URI).
    /// </summary>
    internal static string? TryParseVersionFromUri(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var match = VersionFromUriRegex.Match(uri.ToString());
        return match.Success ? match.Groups["ver"].Value : null;
    }
}
