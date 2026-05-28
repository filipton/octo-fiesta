using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace octo_fiesta.Services.Subsonic;

public interface ICoverArtTransformer
{
    Task<CoverArtTransformResult> ApplyExternalTreatmentAsync(byte[] sourceBytes, string contentType, CancellationToken cancellationToken = default);
}

public sealed record CoverArtTransformResult(byte[] Bytes, string ContentType);

public sealed class CoverArtTransformer : ICoverArtTransformer
{
    private const float SaturationFactor = 0.4f;
    private const float TriangleDivisor = 4.5f;
    private const int TriangleFloorPx = 30;

    public async Task<CoverArtTransformResult> ApplyExternalTreatmentAsync(
        byte[] sourceBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var format = Image.DetectFormat(sourceBytes);
        using var image = Image.Load<Rgba32>(sourceBytes);

        image.Mutate(ctx => ctx.Saturate(SaturationFactor));

        DrawFrostedCornerTriangle(image);

        await using var output = new MemoryStream();
        var encoder = GetEncoder(contentType, format);
        await image.SaveAsync(output, encoder, cancellationToken);
        return new CoverArtTransformResult(output.ToArray(), GetOutputContentType(contentType, format));
    }

    private static void DrawFrostedCornerTriangle(Image<Rgba32> image)
    {
        var shortestSide = Math.Min(image.Width, image.Height);
        var size = Math.Max(TriangleFloorPx, (int)Math.Round(shortestSide / TriangleDivisor));
        size = Math.Min(size, shortestSide / 2);
        if (size <= 2)
        {
            return;
        }

        var blurRadius = Math.Max(2, size / 5);
        var edgeWidth = Math.Max(2, (int)Math.Round(size / 14.0));

        using var blurred = image.Clone(c => c.BoxBlur(blurRadius));

        var width = image.Width;
        var fillTint = new Rgba32(255, 255, 255, (byte)(255 * 0.22f));
        var edgeTint = new Rgba32(0, 0, 0, (byte)(255 * 0.78f));

        image.ProcessPixelRows(blurred, (target, source) =>
        {
            for (var y = 0; y < size; y++)
            {
                var targetRow = target.GetRowSpan(y);
                var sourceRow = source.GetRowSpan(y);
                var rowWidth = size - y;
                var startX = width - rowWidth;

                for (var x = startX; x < width; x++)
                {
                    var isEdge = (x - startX) < edgeWidth;
                    var tint = isEdge ? edgeTint : fillTint;
                    targetRow[x] = AlphaBlend(sourceRow[x], tint);
                }
            }
        });
    }

    private static Rgba32 AlphaBlend(Rgba32 background, Rgba32 foreground)
    {
        var alpha = foreground.A / 255f;
        var inv = 1f - alpha;
        return new Rgba32(
            (byte)(foreground.R * alpha + background.R * inv),
            (byte)(foreground.G * alpha + background.G * inv),
            (byte)(foreground.B * alpha + background.B * inv),
            background.A);
    }

    private static IImageEncoder GetEncoder(string contentType, IImageFormat sourceFormat)
    {
        if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ||
            sourceFormat.DefaultMimeType.Contains("png", StringComparison.OrdinalIgnoreCase))
        {
            return new PngEncoder();
        }

        return new JpegEncoder { Quality = 86 };
    }

    private static string GetOutputContentType(string contentType, IImageFormat sourceFormat)
    {
        if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ||
            sourceFormat.DefaultMimeType.Contains("png", StringComparison.OrdinalIgnoreCase))
        {
            return "image/png";
        }

        return "image/jpeg";
    }
}