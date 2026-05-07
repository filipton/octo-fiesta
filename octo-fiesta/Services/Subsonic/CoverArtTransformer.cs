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
    private const string PillText = "REMOTE";
    private static readonly Color PillBackground = Color.FromRgba(0, 0, 0, 204);
    private static readonly Color PillForeground = Color.FromRgba(255, 255, 255, 217);
    private static readonly Color PillRing = Color.FromRgba(255, 255, 255, 38);

    public async Task<CoverArtTransformResult> AddExternalPillAsync(
        byte[] sourceBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var format = Image.DetectFormat(sourceBytes);
        using var image = Image.Load<Rgba32>(sourceBytes);
        var fontFamily = GetFontFamily();
        var font = fontFamily.CreateFont(GetFontSize(image.Width, image.Height), FontStyle.Bold);
        var textOptions = new TextOptions(font);
        var textSize = TextMeasurer.MeasureSize(PillText, textOptions);

        var shortestSide = Math.Min(image.Width, image.Height);
        var paddingX = shortestSide / 26f;
        var paddingY = shortestSide / 72f;
        var margin = shortestSide / 24f;
        var pillWidth = textSize.Width + paddingX * 2;
        var pillHeight = textSize.Height + paddingY * 2;
        var radius = shortestSide / 42f;
        var ringWidth = Math.Max(1f, shortestSide / 180f);
        var x = margin;
        var y = margin;
        var richTextOptions = new RichTextOptions(font)
        {
            Origin = new PointF(x + pillWidth / 2, y + pillHeight / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        image.Mutate(ctx =>
        {
            ctx.Fill(PillBackground, new RectangularPolygon(x + radius, y, pillWidth - radius * 2, pillHeight));
            ctx.Fill(PillBackground, new RectangularPolygon(x, y + radius, pillWidth, pillHeight - radius * 2));
            ctx.Fill(PillBackground, new EllipsePolygon(x + radius, y + radius, radius));
            ctx.Fill(PillBackground, new EllipsePolygon(x + pillWidth - radius, y + radius, radius));
            ctx.Fill(PillBackground, new EllipsePolygon(x + radius, y + pillHeight - radius, radius));
            ctx.Fill(PillBackground, new EllipsePolygon(x + pillWidth - radius, y + pillHeight - radius, radius));
            ctx.Draw(PillRing, ringWidth, new RectangularPolygon(x + radius, y, pillWidth - radius * 2, pillHeight));
            ctx.Draw(PillRing, ringWidth, new RectangularPolygon(x, y + radius, pillWidth, pillHeight - radius * 2));
            ctx.Draw(PillRing, ringWidth, new EllipsePolygon(x + radius, y + radius, radius));
            ctx.Draw(PillRing, ringWidth, new EllipsePolygon(x + pillWidth - radius, y + radius, radius));
            ctx.Draw(PillRing, ringWidth, new EllipsePolygon(x + radius, y + pillHeight - radius, radius));
            ctx.Draw(PillRing, ringWidth, new EllipsePolygon(x + pillWidth - radius, y + pillHeight - radius, radius));
            ctx.DrawText(richTextOptions, PillText, PillForeground);
        });

        await using var output = new MemoryStream();
        var encoder = GetEncoder(contentType, format);
        await image.SaveAsync(output, encoder, cancellationToken);
        return new CoverArtTransformResult(output.ToArray(), GetOutputContentType(contentType, format));
    }

    private static float GetFontSize(int width, int height)
    {
        var shortestSide = Math.Min(width, height);
        return shortestSide / 12.5f;
    }

    private static FontFamily GetFontFamily()
    {
        if (SystemFonts.TryGet("DejaVu Sans Condensed", out var fontFamily))
        {
            return fontFamily;
        }

        if (SystemFonts.TryGet("DejaVu Sans", out fontFamily))
        {
            return fontFamily;
        }

        return SystemFonts.Families.First();
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
