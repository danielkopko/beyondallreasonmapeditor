using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MapCore.Assets;
using MapCore.Imaging;
using MapCore.Items;
using MapCore.Rendering;
using MapCore.Structures;

namespace MapEditor;

/// <summary>
/// Thumbnails for the asset browser: each feature's model drawn small, in the background, and kept in the library
/// (thumbs/) so it is drawn only once. A feature maps only name (BAR's, the engine's trees) is drawn from BAR's own
/// model shipped with the editor. BAR's units and buildings show their build pictures (BarUnits), as BAR's own menus
/// do, or their models when there is none. Features without a model to draw get a plain tile saying why. Experimental
/// entries (structure types, the open map's new features) are drawn in memory only.
/// </summary>
sealed class AssetThumbnails : IDisposable
{
    public const int Size = 80;
    CancellationTokenSource? running;

    /// <summary>
    /// Makes the thumbnails of <paramref name="rows"/> (rows with a model only: in the library, or BAR's in
    /// <paramref name="bar"/>), each handed to <paramref name="ready"/> on the caller's thread as it is done: a bitmap,
    /// or null when the model can't be drawn. Starting again stops the previous run. The task ends once every result
    /// has been handed over.
    /// </summary>
    public Task Make(AssetLibrary library, BarFeatureCatalog? bar, UnitCatalog? units, IReadOnlyList<AssetRow> rows, Action<AssetRow, Bitmap?> ready)
    {
        running?.Cancel();
        var cancel = running = new CancellationTokenSource();
        var token = cancel.Token;
        var context = SynchronizationContext.Current;
        var done = new TaskCompletionSource();
        void Post(Action action)
        {
            if (context == null)
                action();
            else
                context.Post(_ => action(), null);
        }
        Task.Run(() =>
        {
            try
            {
                var textures = new Dictionary<string, RgbaImage>();
                string? textureAsset = null;
                Func<string, string?>? resolve = null;
                foreach (var row in rows)
                {
                    if (token.IsCancellationRequested)
                        break;
                    // An Experimental entry, drawn in memory: a type's built model, or a new feature's model in the open map
                    // (not kept in the library: it is the map's, and undo can take it away).
                    if (row.Tool != null)
                    {
                        var made = row.Tool switch
                        {
                            { Type: StructureType structureType } => Tile(() => StructurePreviews.For(structureType)),
                            { Made.Model: string model, MapRoot: string root } => Tile(() => ModelPreview.Load(root, model, maxTextureSize: 256)),
                            _ => null,
                        };
                        Post(() =>
                        {
                            if (token.IsCancellationRequested)
                                made?.Dispose();
                            else
                                ready(row, made);
                        });
                        continue;
                    }
                    // BAR's own models (for a name a map only places) share BAR's textures; a library asset has its own.
                    bool fromBar = !row.Asset.HasFiles;
                    var source = fromBar ? "bar" : row.Asset.Id;
                    if (source != textureAsset)
                    {
                        textures.Clear(); // a set of features shares its textures; the next asset has its own
                        textureAsset = source;
                        resolve = !fromBar ? ModelPaths.Resolver(Path.Combine(library.Root, "files", row.Asset.Id))
                            : bar != null ? bar.Resolve : _ => null; // (a unit with a build picture needs no models)
                    }
                    bool unit = row.Asset.Kind is AssetKind.Unit or AssetKind.Building;
                    var bitmap = unit && UnitIcon(units, row.Name) is string icon ? Icon(icon)
                        : unit ? Thumbnail(bar!.Folder, bar.UnitModelOf(row.Name), library.ThumbnailPath(bar, row.Name, unit: true), resolve!, textures)
                        : fromBar ? Thumbnail(bar!.Folder, bar.ModelOf(row.Name), library.ThumbnailPath(bar, row.Name), resolve!, textures)
                        : Thumbnail(Path.Combine(library.Root, "files", row.Asset.Id), row.Feature?.Model,
                            row.Feature == null ? "" : library.ThumbnailPath(row.Asset, row.Feature.Name), resolve!, textures);
                    Post(() =>
                    {
                        if (token.IsCancellationRequested)
                            bitmap?.Dispose();
                        else
                            ready(row, bitmap);
                    });
                }
            }
            finally
            {
                // Drawing hundreds of models (BAR's 1,400-odd features, the first time) leaves the collector holding far
                // more memory than is in use (about 1 GB for 275 MB live): give it back once such a run ends.
                if (rows.Count >= 200)
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                Post(() => done.TrySetResult()); // after every result above: posts run in order
            }
        }, CancellationToken.None);
        return done.Task;
    }

    /// <summary>A model as a tile, not kept on disk (a structure type's built model, a map's new feature: few, and cheap to draw).</summary>
    static Bitmap? Tile(Func<ModelPreview> model)
    {
        try
        {
            var render = ModelRenderer.Render(model(), Size, Size, yaw: 35, pitch: 22);
            var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            ViewPainting.CopyToBitmap(render.Image, new byte[Size * Size * 4], bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A BAR unit's or building's build picture (shipped in BarUnits), or null when it has none.</summary>
    public static string? UnitIcon(UnitCatalog? units, string name) =>
        units?.Find(name.ToLowerInvariant()) is CatalogUnit unit ? units.IconPath(unit) : null;

    /// <summary>A build picture as a tile: scaled to the tile, inside a small margin.</summary>
    static Bitmap? Icon(string file)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(file));
            using var picture = Image.FromStream(stream);
            var tile = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(tile);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(picture, new Rectangle(3, 3, Size - 6, Size - 6));
            return tile;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The thumbnail of a model in <paramref name="root"/>: read from <paramref name="file"/> if drawn before, else drawn and kept there.</summary>
    static Bitmap? Thumbnail(string root, string? model, string file, Func<string, string?> resolve, Dictionary<string, RgbaImage> textures)
    {
        if (model == null || file.Length == 0)
            return null;
        try
        {
            if (File.Exists(file))
            {
                using var stream = new MemoryStream(File.ReadAllBytes(file));
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // A damaged thumbnail is drawn again.
        }
        try
        {
            var preview = ModelPreview.Load(root, model, resolve, textures, maxTextureSize: 256); // plenty for 80 pixels
            var render = ModelRenderer.Render(preview, Size, Size, yaw: 35, pitch: 22);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            PngWriter.WriteRgba(file, render.Image);
            var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            ViewPainting.CopyToBitmap(render.Image, new byte[Size * Size * 4], bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A plain tile for a feature with no model to draw: built into the engine or BAR, or missing.</summary>
    public static Bitmap Placeholder(string label, Color tint)
    {
        var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var path = new GraphicsPath();
        var r = new Rectangle(4, 4, Size - 9, Size - 9);
        int d = 14;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using (var fill = new LinearGradientBrush(r, ControlPaint.Light(tint, 0.3f), tint, LinearGradientMode.Vertical))
            g.FillPath(fill, path);
        using (var edge = new Pen(ControlPaint.Dark(tint, 0.1f)))
            g.DrawPath(edge, path);
        using var font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; // ClearType needs an opaque background
        g.DrawString(label, font, Brushes.White, r, format);
        return bitmap;
    }

    public void Dispose()
    {
        running?.Cancel();
        running?.Dispose();
    }
}
