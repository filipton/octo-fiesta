using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace octo_fiesta.Services.Subsonic;

public interface ICoverArtTransformer
{
    Task<CoverArtTransformResult> AddExternalPillAsync(byte[] sourceBytes, string contentType, CancellationToken cancellationToken = default);
}

public sealed record CoverArtTransformResult(byte[] Bytes, string ContentType);

public sealed class CoverArtTransformer : ICoverArtTransformer
{
    private static readonly Color BadgeBackground = Color.FromRgba(220, 38, 38, 178);
    private static readonly Color BadgeForeground = Color.FromRgba(255, 255, 255, 230);
    private static readonly Color BadgeRing = Color.FromRgba(255, 255, 255, 92);

    public async Task<CoverArtTransformResult> AddExternalPillAsync(
        byte[] sourceBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var format = Image.DetectFormat(sourceBytes);
        using var image = Image.Load<Rgba32>(sourceBytes);
        var shortestSide = Math.Min(image.Width, image.Height);
        var badgeRadius = Math.Clamp(shortestSide / 8.5f, 7f, 24f);
        var margin = Math.Clamp(shortestSide / 24f, 3f, 14f);
        var ringWidth = Math.Max(1f, shortestSide / 150f);
        var center = new PointF(image.Width - margin - badgeRadius, image.Height - margin - badgeRadius);
        var iconScale = badgeRadius / 24f;

        image.Mutate(ctx =>
        {
            ctx.Fill(BadgeBackground, new EllipsePolygon(center, badgeRadius));
            ctx.Draw(BadgeRing, ringWidth, new EllipsePolygon(center, badgeRadius));
            DrawNetworkIcon(ctx, center, iconScale);
        });

        await using var output = new MemoryStream();
        var encoder = GetEncoder(contentType, format);
        await image.SaveAsync(output, encoder, cancellationToken);
        return new CoverArtTransformResult(output.ToArray(), GetOutputContentType(contentType, format));
    }

    private static void DrawNetworkIcon(IImageProcessingContext ctx, PointF center, float scale)
    {
        var top = new PointF(center.X, center.Y - 7.5f * scale);
        var left = new PointF(center.X - 8f * scale, center.Y + 5.5f * scale);
        var right = new PointF(center.X + 8f * scale, center.Y + 5.5f * scale);
        var lineWidth = Math.Max(1.2f, 2.4f * scale);
        var nodeRadius = Math.Max(1.7f, 3.2f * scale);

        DrawLine(ctx, top, left, lineWidth);
        DrawLine(ctx, top, right, lineWidth);
        DrawLine(ctx, left, right, lineWidth);
        ctx.Fill(BadgeForeground, new EllipsePolygon(top, nodeRadius));
        ctx.Fill(BadgeForeground, new EllipsePolygon(left, nodeRadius));
        ctx.Fill(BadgeForeground, new EllipsePolygon(right, nodeRadius));
    }

    private static void DrawLine(IImageProcessingContext ctx, PointF from, PointF to, float width)
    {
        var path = new PathBuilder()
            .AddLine(from, to)
            .Build();
        ctx.Draw(BadgeForeground, width, path);
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
