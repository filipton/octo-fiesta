using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using octo_fiesta.Controllers;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services;
using octo_fiesta.Services.Local;
using octo_fiesta.Services.Subsonic;

namespace octo_fiesta.Tests;

public class SubsonicControllerCoverArtTests
{
    private static SubsonicController CreateController(
        Mock<IMusicMetadataService> metadataServiceMock,
        Mock<ILocalLibraryService> localLibraryServiceMock,
        Mock<ICoverArtTransformer> coverArtTransformerMock,
        IExternalAlbumAvailabilityService externalAlbumAvailabilityService,
        byte[] sourceCoverBytes)
    {
        var settings = Options.Create(new SubsonicSettings
        {
            Url = "http://localhost:4533"
        });

        var mockHttpHandler = new Mock<HttpMessageHandler>();
        mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sourceCoverBytes)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") }
                }
            });

        var httpClient = new HttpClient(mockHttpHandler.Object);
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var responseBuilder = new SubsonicResponseBuilder();
        var proxyService = new SubsonicProxyService(mockHttpClientFactory.Object, settings, httpContextAccessor);
        var appLifetimeMock = new Mock<IHostApplicationLifetime>();
        appLifetimeMock.SetupGet(x => x.ApplicationStopping).Returns(CancellationToken.None);

        var controller = new SubsonicController(
            settings,
            metadataServiceMock.Object,
            localLibraryServiceMock.Object,
            new Mock<IDownloadService>().Object,
            new SubsonicRequestParser(),
            responseBuilder,
            new SubsonicModelMapper(responseBuilder, new Mock<ILogger<SubsonicModelMapper>>().Object),
            proxyService,
            appLifetimeMock.Object,
            mockHttpClientFactory.Object,
            coverArtTransformerMock.Object,
            new CoverArtCache(new MemoryCache(new MemoryCacheOptions { SizeLimit = 512 })),
            externalAlbumAvailabilityService,
            new Mock<ILogger<SubsonicController>>().Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?id=ext-qobuz-album-abc&size=150");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    [Fact]
    public async Task GetCoverArt_WithRemoteExternalAlbum_AddsPillAndCachesResult()
    {
        var metadataServiceMock = new Mock<IMusicMetadataService>();
        metadataServiceMock
            .Setup(x => x.GetAlbumCoverUrlAsync("qobuz", "abc"))
            .ReturnsAsync("https://static.qobuz.com/images/covers/aa/bb/abc_600.jpg");

        var localLibraryServiceMock = new Mock<ILocalLibraryService>();
        localLibraryServiceMock
            .Setup(x => x.ParseExternalId("ext-qobuz-album-abc"))
            .Returns((true, "qobuz", "album", "abc"));

        var transformerMock = new Mock<ICoverArtTransformer>();
        transformerMock
            .Setup(x => x.AddExternalPillAsync(It.IsAny<byte[]>(), "image/jpeg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoverArtTransformResult([9, 9, 9], "image/jpeg"));

        var controller = CreateController(
            metadataServiceMock,
            localLibraryServiceMock,
            transformerMock,
            new ExternalAlbumAvailabilityService(),
            [1, 2, 3]);

        var first = Assert.IsType<FileContentResult>(await controller.GetCoverArt());
        var second = Assert.IsType<FileContentResult>(await controller.GetCoverArt());

        Assert.Equal([9, 9, 9], first.FileContents);
        Assert.Equal([9, 9, 9], second.FileContents);
        transformerMock.Verify(
            x => x.AddExternalPillAsync(It.IsAny<byte[]>(), "image/jpeg", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCoverArt_WhenAlbumDownloadStarted_ReturnsOriginalCover()
    {
        var availabilityService = new ExternalAlbumAvailabilityService();
        availabilityService.MarkDownloadStarted("qobuz", "abc");

        var metadataServiceMock = new Mock<IMusicMetadataService>();
        metadataServiceMock
            .Setup(x => x.GetAlbumCoverUrlAsync("qobuz", "abc"))
            .ReturnsAsync("https://static.qobuz.com/images/covers/aa/bb/abc_600.jpg");

        var localLibraryServiceMock = new Mock<ILocalLibraryService>();
        localLibraryServiceMock
            .Setup(x => x.ParseExternalId("ext-qobuz-album-abc"))
            .Returns((true, "qobuz", "album", "abc"));

        var transformerMock = new Mock<ICoverArtTransformer>();
        var controller = CreateController(
            metadataServiceMock,
            localLibraryServiceMock,
            transformerMock,
            availabilityService,
            [1, 2, 3]);

        var result = Assert.IsType<FileContentResult>(await controller.GetCoverArt());

        Assert.Equal([1, 2, 3], result.FileContents);
        transformerMock.Verify(
            x => x.AddExternalPillAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
