using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Search;
using octo_fiesta.Services;
using octo_fiesta.Services.Composite;
using Xunit;

namespace octo_fiesta.Tests;

/// <summary>An offline provider must never fail or stall a request that other providers can answer.</summary>
public class CompositeResilienceTests
{
    private static Song Track(string provider, string id) => new() { Title = id, ExternalProvider = provider, ExternalId = id };

    private static Mock<IMusicMetadataService> Working(string provider)
    {
        var m = new Mock<IMusicMetadataService>();
        m.Setup(x => x.SearchSongsAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync([Track(provider, "1")]);
        m.Setup(x => x.SearchAllAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new SearchResult { Songs = [Track(provider, "1")] });
        return m;
    }

    private static CompositeMetadataService Composite(TimeSpan timeout, params (string, IMusicMetadataService)[] providers)
        => new(providers, NullLogger<CompositeMetadataService>.Instance, timeout);

    [Fact]
    public async Task Search_WhenOneProviderThrows_ReturnsTheOthersResults()
    {
        var broken = new Mock<IMusicMetadataService>();
        broken.Setup(x => x.SearchSongsAsync(It.IsAny<string>(), It.IsAny<int>())).ThrowsAsync(new HttpRequestException("offline"));

        var result = await Composite(TimeSpan.FromSeconds(5), ("apple", broken.Object), ("gdstudio", Working("gdstudio").Object))
            .SearchSongsAsync("q");

        Assert.Equal("gdstudio", Assert.Single(result).ExternalProvider);
    }

    [Fact]
    public async Task Search_WhenOneProviderHangs_ReturnsTheOthersAfterTheTimeout()
    {
        var hung = new Mock<IMusicMetadataService>();
        hung.Setup(x => x.SearchSongsAsync(It.IsAny<string>(), It.IsAny<int>())).Returns(new TaskCompletionSource<List<Song>>().Task);

        var started = DateTime.UtcNow;
        var result = await Composite(TimeSpan.FromMilliseconds(300), ("apple", hung.Object), ("gdstudio", Working("gdstudio").Object))
            .SearchSongsAsync("q");

        Assert.Equal("gdstudio", Assert.Single(result).ExternalProvider);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task SearchAll_WhenOneProviderThrows_ReturnsTheOthersResults()
    {
        var broken = new Mock<IMusicMetadataService>();
        broken.Setup(x => x.SearchAllAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await Composite(TimeSpan.FromSeconds(5), ("apple", broken.Object), ("gdstudio", Working("gdstudio").Object))
            .SearchAllAsync("q");

        Assert.Equal("gdstudio", Assert.Single(result.Songs).ExternalProvider);
    }

    [Fact]
    public async Task GetAlbum_WhenItsProviderIsOffline_ReturnsNullInsteadOfThrowing()
    {
        var broken = new Mock<IMusicMetadataService>();
        broken.Setup(x => x.GetAlbumAsync("apple", "1")).ThrowsAsync(new HttpRequestException("offline"));

        var album = await Composite(TimeSpan.FromSeconds(5), ("apple", broken.Object)).GetAlbumAsync("apple", "1");

        Assert.Null(album);
    }

    [Fact]
    public async Task IsAvailable_WhenTheFirstProviderThrows_StillChecksTheOthers()
    {
        var broken = new Mock<octo_fiesta.Services.IDownloadService>();
        broken.Setup(x => x.IsAvailableAsync()).ThrowsAsync(new HttpRequestException("offline"));
        var ok = new Mock<octo_fiesta.Services.IDownloadService>();
        ok.Setup(x => x.IsAvailableAsync()).ReturnsAsync(true);

        var composite = new CompositeDownloadService([("apple", broken.Object), ("gdstudio", ok.Object)]);

        Assert.True(await composite.IsAvailableAsync());
    }
}
