using System.Reflection;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Jellyfin.Plugin.LetterboxdSync.Imaging;

/// <summary>
/// Builds a poster-strip "key art" cover: a row of unblurred poster crops across the
/// top (kept close to natural poster proportions so faces/titles aren't mangled by
/// over-cropping), and a solid dark panel below for the collection title.
/// </summary>
public static class PosterGenerator
{
    private const int CanvasWidth = 2000;
    private const int CanvasHeight = 3000;
    private const double PanelAspect = 2.2;

    private static readonly Color Gold = Color.ParseHex("#C6A569");
    private static readonly Color OffWhite = Color.ParseHex("#F0EEE8");
    private static readonly Color PanelBackground = Color.ParseHex("#0C0C0E");

    private static FontFamily? _fontFamily;

    public static Image<Rgba32> Create(IReadOnlyList<Image<Rgba32>> posters, string collectionName, string? subtitle = null)
    {
        var canvas = new Image<Rgba32>(CanvasWidth, CanvasHeight);
        canvas.Mutate(ctx => ctx.Fill(Color.Black));

        var numPanels = Math.Min(posters.Count, 3);
        var panelWidth = CanvasWidth / Math.Max(numPanels, 1);
        var rowHeight = Math.Min((int)(panelWidth * PanelAspect), (int)(CanvasHeight * 0.55));

        canvas.Mutate(ctx =>
        {
            for (var i = 0; i < numPanels; i++)
            {
                using var fitted = posters[i].Clone(c => c.Resize(new ResizeOptions
                {
                    Size = new Size(panelWidth, rowHeight),
                    Mode = ResizeMode.Crop
                }));
                ctx.DrawImage(fitted, new Point(i * panelWidth, 0), 1f);

                if (i > 0)
                {
                    var x = i * panelWidth;
                    ctx.DrawLine(Gold, 3f, new PointF(x, 0), new PointF(x, rowHeight));
                }
            }

            ctx.DrawLine(Gold, 4f, new PointF(0, rowHeight), new PointF(CanvasWidth, rowHeight));

            var textZoneTop = rowHeight + 4;
            var textZoneHeight = CanvasHeight - textZoneTop;
            ctx.Fill(PanelBackground, new RectangularPolygon(0, textZoneTop, CanvasWidth, textZoneHeight));

            var fontFamily = GetFontFamily();
            var titleFont = fontFamily.CreateFont(120, FontStyle.Bold);
            var maxTextWidth = CanvasWidth * 0.85f;

            var titleOptions = new RichTextOptions(titleFont)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                WrappingLength = maxTextWidth,
                Origin = new PointF(CanvasWidth / 2f, 0)
            };

            var titleBounds = TextMeasurer.MeasureBounds(collectionName.ToUpperInvariant(), titleOptions);
            var subtitleFont = fontFamily.CreateFont(38, FontStyle.Bold);
            var subtitleHeight = string.IsNullOrEmpty(subtitle) ? 0 : 90;
            var blockHeight = titleBounds.Height + subtitleHeight;
            var textY = textZoneTop + (textZoneHeight - blockHeight) / 2f;

            titleOptions.Origin = new PointF(CanvasWidth / 2f, textY);
            ctx.DrawText(titleOptions, collectionName.ToUpperInvariant(), OffWhite);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var trackedSubtitle = string.Join(" ", subtitle.ToUpperInvariant().ToCharArray());
                var subtitleOptions = new RichTextOptions(subtitleFont)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Origin = new PointF(CanvasWidth / 2f, textY + titleBounds.Height + 30)
                };
                ctx.DrawText(subtitleOptions, trackedSubtitle, Gold);
            }
        });

        return canvas;
    }

    private static FontFamily GetFontFamily()
    {
        if (_fontFamily is not null)
        {
            return _fontFamily.Value;
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.Assets.Cinzel.ttf";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' not found.");
        var collection = new FontCollection();
        _fontFamily = collection.Add(stream);
        return _fontFamily.Value;
    }
}
