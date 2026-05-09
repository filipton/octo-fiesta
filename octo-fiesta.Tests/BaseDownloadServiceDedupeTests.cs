using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services.Common;
using octo_fiesta.Services.Local;
using octo_fiesta.Services;

namespace octo_fiesta.Tests;

public class BaseDownloadServiceDedupeTests : IDisposable
{
    private readonly string _testDownloadPath;

    public BaseDownloadServiceDedupeTests()
    {
        _testDownloadPath = Path.Combine(Path.GetTempPath(), "octo-fiesta-dedupe-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDownloadPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDownloadPath))
        {
            Directory.Delete(_testDownloadPath, true);
        }
    }

    private FakeDedupeDownloadService BuildService(
        Mock<ILocalLibraryService> localLibMock,
        Mock<IMusicMetadataService> metaMock,
        string? folderTemplate = null)
    {
        var settings = new SubsonicSettings
        {
            FolderTemplate = folderTemplate ?? "{artist}/{album}/{track}. {title}",
            StorageMode = StorageMode.Permanent
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Library:DownloadPath"] = _testDownloadPath
            })
            .Build();
        return new FakeDedupeDownloadService(
            new Mock<IHttpClientFactory>().Object,
            config,
            localLibMock.Object,
            metaMock.Object,
            settings,
            new Mock<IServiceProvider>().Object,
            NullLogger.Instance);
    }

    [Fact]
    public async Task DownloadSongAsync_WhenFileExistsOnDiskWithoutMapping_RegistersAndSkipsDownload()
    {
        var album = Path.Combine(_testDownloadPath, "Artist", "Album");
        Directory.CreateDirectory(album);
        var expectedFile = Path.Combine(album, "01. Track.flac");
        await File.WriteAllTextAsync(expectedFile, "existing-file");

        var localLibMock = new Mock<ILocalLibraryService>();
        localLibMock
            .Setup(x => x.GetMappingForExternalSongAsync("fake", "1"))
            .ReturnsAsync((LocalSongMapping?)null);
        localLibMock
            .Setup(x => x.GetLocalPathForExternalSongAsync("fake", "1"))
            .ReturnsAsync((string?)null);
        localLibMock
            .Setup(x => x.FindLocalSongByMetadataAsync(It.IsAny<Song>()))
            .ReturnsAsync((LocalSongMatch?)null);

        var metaMock = new Mock<IMusicMetadataService>();
        metaMock
            .Setup(x => x.GetSongAsync("fake", "1"))
            .ReturnsAsync(new Song
            {
                ExternalId = "1",
                ExternalProvider = "fake",
                Title = "Track",
                Artist = "Artist",
                Album = "Album",
                Track = 1
            });

        var service = BuildService(localLibMock, metaMock);

        var result = await service.DownloadSongAsync("fake", "1");

        Assert.Equal(expectedFile, result);
        Assert.False(service.DownloadTrackCalled);
        localLibMock.Verify(
            x => x.RegisterDownloadedSongAsync(It.IsAny<Song>(), expectedFile, null, null),
            Times.Once);
    }

    [Fact]
    public async Task DownloadSongAsync_WhenNavidromeReturnsMetadataMatch_RegistersAndSkipsDownload()
    {
        var localLibMock = new Mock<ILocalLibraryService>();
        localLibMock
            .Setup(x => x.GetMappingForExternalSongAsync("fake", "2"))
            .ReturnsAsync((LocalSongMapping?)null);
        localLibMock
            .Setup(x => x.GetLocalPathForExternalSongAsync("fake", "2"))
            .ReturnsAsync((string?)null);

        var naviPath = "/library/Artist/Album/01. Track.flac";
        localLibMock
            .Setup(x => x.FindLocalSongByMetadataAsync(It.IsAny<Song>()))
            .ReturnsAsync(new LocalSongMatch("local-id-999", naviPath));

        var metaMock = new Mock<IMusicMetadataService>();
        metaMock
            .Setup(x => x.GetSongAsync("fake", "2"))
            .ReturnsAsync(new Song
            {
                ExternalId = "2",
                ExternalProvider = "fake",
                Title = "Track",
                Artist = "Artist",
                Album = "Album",
                Track = 1
            });

        var service = BuildService(localLibMock, metaMock);

        var result = await service.DownloadSongAsync("fake", "2");

        Assert.Equal(naviPath, result);
        Assert.False(service.DownloadTrackCalled);
        localLibMock.Verify(
            x => x.RegisterDownloadedSongAsync(It.IsAny<Song>(), naviPath, null, null),
            Times.Once);
    }

    [Fact]
    public async Task SaveDownloadStream_WhenTargetCreatedBetweenProbeAndWrite_Throws()
    {
        var localLibMock = new Mock<ILocalLibraryService>();
        localLibMock
            .Setup(x => x.GetMappingForExternalSongAsync("fake", "3"))
            .ReturnsAsync((LocalSongMapping?)null);
        localLibMock
            .Setup(x => x.GetLocalPathForExternalSongAsync("fake", "3"))
            .ReturnsAsync((string?)null);
        localLibMock
            .Setup(x => x.FindLocalSongByMetadataAsync(It.IsAny<Song>()))
            .ReturnsAsync((LocalSongMatch?)null);

        var metaMock = new Mock<IMusicMetadataService>();
        var song = new Song
        {
            ExternalId = "3",
            ExternalProvider = "fake",
            Title = "RaceTrack",
            Artist = "Artist",
            Album = "Album",
            Track = 1
        };
        metaMock.Setup(x => x.GetSongAsync("fake", "3")).ReturnsAsync(song);

        var service = BuildService(localLibMock, metaMock);
        service.CreateFileBeforeWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadSongAsync("fake", "3"));
    }

    private sealed class FakeDedupeDownloadService : BaseDownloadService
    {
        public bool DownloadTrackCalled { get; private set; }
        public bool CreateFileBeforeWrite { get; set; }

        protected override string ProviderName => "fake";

        public FakeDedupeDownloadService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILocalLibraryService localLibraryService,
            IMusicMetadataService metadataService,
            SubsonicSettings subsonicSettings,
            IServiceProvider serviceProvider,
            Microsoft.Extensions.Logging.ILogger logger)
            : base(httpClientFactory, configuration, localLibraryService, metadataService, subsonicSettings, serviceProvider, logger)
        {
        }

        public override Task<bool> IsAvailableAsync() => Task.FromResult(true);

        protected override string? ExtractExternalIdFromAlbumId(string albumId) => albumId;

        protected override string? GetTargetQuality() => null;

        protected override Task<DownloadResult> DownloadTrackAsync(string trackId, Song song, CancellationToken cancellationToken)
        {
            DownloadTrackCalled = true;

            if (CreateFileBeforeWrite)
            {
                var racePath = PathHelper.BuildTrackPath(DownloadPath, song, ".flac", SubsonicSettings.FolderTemplate, null);
                Directory.CreateDirectory(Path.GetDirectoryName(racePath)!);
                File.WriteAllText(racePath, "race-written");
            }

            return Task.FromResult(new DownloadResult(
                new MemoryStream(new byte[] { 0x66, 0x4C, 0x61, 0x43 }),
                ".flac",
                "FLAC"));
        }
    }
}
