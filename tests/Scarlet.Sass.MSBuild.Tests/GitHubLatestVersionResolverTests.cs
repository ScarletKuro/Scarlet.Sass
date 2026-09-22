using System.Net;

namespace Scarlet.Sass.MSBuild.Tests;

public class GitHubLatestVersionResolverTests
{
    private const string LatestUrl = "https://github.com/sass/dart-sass/releases/latest";

    [Fact]
    public async Task TryResolveVersionAsync_WithGitHubRedirectShape_ShouldParseVersion()
    {
        // The exact shape GitHub returns for "latest": a 302 whose Location carries the release tag
        // (".../releases/tag/1.104.1"). The resolver must not follow the redirect itself, since the asset
        // URL still needs to be built from the version it extracts here.
        var handler = new StaticRedirectHandler(
            HttpStatusCode.Found,
            "https://github.com/sass/dart-sass/releases/tag/1.104.1");
        var resolver = new GitHubLatestVersionResolver(handler);

        var result = await resolver.TryResolveVersionAsync(LatestUrl);

        Assert.Equal("1.104.1", result);
    }

    [Fact]
    public async Task TryResolveVersionAsync_WhenResponseIsNotARedirect_ShouldReturnNull()
    {
        var handler = new StaticStatusHandler(HttpStatusCode.OK);
        var resolver = new GitHubLatestVersionResolver(handler);

        var result = await resolver.TryResolveVersionAsync(LatestUrl);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveVersionAsync_WhenRedirectHasNoLocation_ShouldReturnNull()
    {
        var handler = new StaticStatusHandler(HttpStatusCode.Found);
        var resolver = new GitHubLatestVersionResolver(handler);

        var result = await resolver.TryResolveVersionAsync(LatestUrl);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("https://github.com/sass/dart-sass/releases/tag/1.104.1", "1.104.1")]
    [InlineData("https://github.com/sass/dart-sass/releases/download/1.4.2/dart-sass-1.4.2-windows-x64.zip", "1.4.2")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/357728969/46532abe?sp=r&sig=abc", null)]
    [InlineData(null, null)]
    public void TryParseVersionFromUri_WithVariousUris_ShouldParseOrReturnNull(string? uri, string? expected)
    {
        var parsed = uri is null ? null : new Uri(uri);
        Assert.Equal(expected, GitHubLatestVersionResolver.TryParseVersionFromUri(parsed));
    }

    /// <summary>Returns a fixed status code with no Location header.</summary>
    private sealed class StaticStatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StaticStatusHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    /// <summary>Returns a fixed redirect status with a Location header.</summary>
    private sealed class StaticRedirectHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _location;

        public StaticRedirectHandler(HttpStatusCode statusCode, string location)
        {
            _statusCode = statusCode;
            _location = location;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            response.Headers.Location = new Uri(_location);
            return Task.FromResult(response);
        }
    }
}
