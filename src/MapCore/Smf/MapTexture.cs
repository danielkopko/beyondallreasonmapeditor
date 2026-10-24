using MapCore.Imaging;
using MapCore.Textures;

namespace MapCore.Smf;

/// <summary>What <see cref="MapTexture.Update"/> did: tiles re-encoded in total, and how many of them are new tiles.</summary>
public readonly record struct TextureUpdate(int TilesRewritten, int TilesAdded);

/// <summary>
/// The map's diffuse texture: the SMF tile index pointing into tiles held by one or more .smt files.
/// Tile numbers run through the files in order. Several positions may share one tile.
/// </summary>
public sealed class MapTexture
{
    readonly SmfFile smf;
    readonly IReadOnlyList<SmtFile> files;
    readonly List<string> warnings = [];
    bool extraTilesTrimmed;

    public SmfFile Smf => smf;
    public IReadOnlyList<SmtFile> TileFiles => files;

    /// <summary>Package paths of <see cref="TileFiles"/> ('/' separators), when loaded from a package.</summary>
    public IReadOnlyList<string>? Entries { get; }

    public int Width => smf.TileColumns * SmtFile.TileSize;
    public int Height => smf.TileRows * SmtFile.TileSize;
    public PixelRect Bounds => new(0, 0, Width, Height);
    public IReadOnlyList<string> Warnings => warnings;

    /// <param name="files">One per entry in <c>smf.TileFiles</c>, in the same order.</param>
    public MapTexture(SmfFile smf, IReadOnlyList<SmtFile> files, IReadOnlyList<string>? entries = null)
    {
        if (files.Count != smf.TileFiles.Count)
            throw new ArgumentException($"The SMF names {smf.TileFiles.Count} tile files but {files.Count} were given.");
        for (int i = 0; i < files.Count; i++)
        {
            int used = smf.TileFiles[i].TileCount;
            if (files[i].TileCount < used)
                throw new InvalidDataException($"Tile file {i} has {files[i].TileCount} tiles but the SMF uses {used}.");
            if (files[i].TileCount > used)
                warnings.Add($"tile file {i} has {files[i].TileCount - used} tiles beyond the {used} the engine reads");
        }
        this.smf = smf;
        this.files = files;
        Entries = entries;
    }

    /// <summary>Writes every tile file to <paramref name="root"/>/<see cref="Entries"/>.</summary>
    public void SaveTileFiles(string root)
    {
        if (Entries == null)
            throw new InvalidOperationException("This texture was not loaded from a package, so it has no file paths.");
        for (int i = 0; i < files.Count; i++)
            files[i].Save(Path.Combine(root, Entries[i]));
    }

    /// <summary>The whole texture at full resolution.</summary>
    public RgbaImage Decode()
    {
        var image = new RgbaImage(Width, Height);
        Parallel.For(0, smf.TileRows, row =>
        {
            for (int column = 0; column < smf.TileColumns; column++)
                DecodeTileInto(column, row, 0, image, column * SmtFile.TileSize, row * SmtFile.TileSize);
        });
        return image;
    }

    /// <summary>One tile position at a mip level (32, 16, 8 or 4 pixels square).</summary>
    public RgbaImage DecodeTile(int column, int row, int mip = 0)
    {
        var image = new RgbaImage(SmtFile.TileSize >> mip, SmtFile.TileSize >> mip);
        DecodeTileInto(column, row, mip, image, 0, 0);
        return image;
    }

    void DecodeTileInto(int column, int row, int mip, RgbaImage target, int x, int y)
    {
        var (offset, length) = SmtFile.MipLayout[mip];
        var data = TileData(smf.TileIndex[row * smf.TileColumns + column]).Span.Slice(offset, length);
        BlockCompression.DecodeInto(BlockFormat.Dxt1, data, SmtFile.TileSize >> mip, SmtFile.TileSize >> mip, target, x, y);
    }

    /// <summary>
    /// Re-encodes the tiles <paramref name="region"/> touches from <paramref name="image"/> (the full texture).
    /// Within each tile only the 4x4 blocks the region touches are rewritten, at every mip level. A tile shared
    /// with other positions is copied first, so those positions keep their look. Pixels inside the region are
    /// stored opaque (DXT1 would otherwise turn alpha below 128 into transparent black), unless
    /// <paramref name="keepAlpha"/>: then alpha below 128 is kept as DXT1's transparent texels, which a map with
    /// voidGround uses to cut the ground it doesn't draw.
    /// </summary>
    public TextureUpdate Update(RgbaImage image, PixelRect region, bool keepAlpha = false)
    {
        CheckSize(image);
        region = region.Intersect(Bounds);
        if (region.IsEmpty)
            return new(0, 0);

        const int size = SmtFile.TileSize;
        int tx0 = region.X / size, ty0 = region.Y / size, tx1 = (region.Right + size - 1) / size, ty1 = (region.Bottom + size - 1) / size;
        var parts = new List<(int Tx, int Ty, IReadOnlyList<PixelRect> Local)>();
        for (int ty = ty0; ty < ty1; ty++)
            for (int tx = tx0; tx < tx1; tx++)
            {
                var tileRect = new PixelRect(tx * size, ty * size, size, size);
                var local = region.Intersect(tileRect);
                parts.Add((tx, ty, [local with { X = local.X - tileRect.X, Y = local.Y - tileRect.Y }]));
            }
        return UpdateTiles(image, parts, keepAlpha);
    }

    /// <summary>
    /// Like <see cref="Update"/>, but re-encodes only the given 4x4 blocks (texture pixels, block-aligned): an edit
    /// that changed part of a large area leaves the blocks it didn't change byte for byte as they were.
    /// </summary>
    public TextureUpdate UpdateBlocks(RgbaImage image, IEnumerable<PixelRect> blocks)
    {
        CheckSize(image);
        const int size = SmtFile.TileSize;
        var parts = blocks.Select(b => b.Intersect(Bounds)).Where(b => !b.IsEmpty)
            .GroupBy(b => (Tx: b.X / size, Ty: b.Y / size))
            .Select(g => (g.Key.Tx, g.Key.Ty, (IReadOnlyList<PixelRect>)g.Select(b => b with { X = b.X - g.Key.Tx * size, Y = b.Y - g.Key.Ty * size }).ToList()))
            .ToList();
        return parts.Count == 0 ? new(0, 0) : UpdateTiles(image, parts);
    }

    /// <summary>
    /// Brings the texture in line with <paramref name="image"/> (the full texture, e.g. an imported PNG), re-encoding
    /// only the 4x4 blocks whose pixels differ — so importing an unedited export changes nothing. Returns the update
    /// and the bounding box of the changed pixels (empty when nothing differs).
    /// </summary>
    public (TextureUpdate Update, PixelRect Changed) UpdateFrom(RgbaImage image)
    {
        CheckSize(image);
        var current = Decode();
        const int size = SmtFile.TileSize;
        var parts = new List<(int Tx, int Ty, IReadOnlyList<PixelRect> Local)>();
        var changed = new PixelRect(0, 0, 0, 0);
        for (int ty = 0; ty < smf.TileRows; ty++)
            for (int tx = 0; tx < smf.TileColumns; tx++)
            {
                var blocks = new List<PixelRect>();
                for (int by = 0; by < size; by += 4)
                    for (int bx = 0; bx < size; bx += 4)
                        if (!BlockEqual(current, image, tx * size + bx, ty * size + by))
                            blocks.Add(new PixelRect(bx, by, 4, 4));
                if (blocks.Count == 0)
                    continue;
                parts.Add((tx, ty, blocks));
                foreach (var b in blocks)
                    changed = changed.Union(b with { X = b.X + tx * size, Y = b.Y + ty * size });
            }
        return (UpdateTiles(image, parts), changed);
    }

    static bool BlockEqual(RgbaImage a, RgbaImage b, int x, int y)
    {
        for (int row = 0; row < 4; row++)
        {
            int offset = ((y + row) * a.Width + x) * 4;
            if (!a.Pixels.AsSpan(offset, 16).SequenceEqual(b.Pixels.AsSpan(offset, 16)))
                return false;
        }
        return true;
    }

    void CheckSize(RgbaImage image)
    {
        if (image.Width != Width || image.Height != Height)
            throw new ArgumentException($"Image is {image.Width}x{image.Height}; the map texture is {Width}x{Height}.");
    }

    /// <summary>Re-encodes the given areas (tile-local rectangles) of the given tile positions, copying shared tiles first.</summary>
    TextureUpdate UpdateTiles(RgbaImage image, IReadOnlyList<(int Tx, int Ty, IReadOnlyList<PixelRect> Local)> parts, bool keepAlpha = false)
    {
        if (parts.Count == 0)
            return new(0, 0);
        var references = new int[TotalTiles];
        foreach (int tile in smf.TileIndex)
            if ((uint)tile < (uint)references.Length)
                references[tile]++;

        // Decide where each touched position's new tile goes (in place, or a fresh copy), then encode in parallel.
        var jobs = new List<(int Tile, int X, int Y, IReadOnlyList<PixelRect> Local)>();
        int added = 0;
        foreach (var (tx, ty, local) in parts)
        {
            int position = ty * smf.TileColumns + tx;
            int tile = smf.TileIndex[position];
            bool valid = (uint)tile < (uint)references.Length;
            if (!valid || references[tile] > 1)
            {
                var copy = valid ? TileData(tile).ToArray() : new byte[SmtFile.TileBytes];
                if (valid)
                    references[tile]--;
                tile = AppendTile(copy);
                smf.TileIndex[position] = tile;
                added++;
            }
            jobs.Add((tile, tx * SmtFile.TileSize, ty * SmtFile.TileSize, local));
        }

        Parallel.ForEach(jobs, job => EncodeTile(image, job.Tile, job.X, job.Y, job.Local, keepAlpha));
        return new(jobs.Count, added);
    }

    void EncodeTile(RgbaImage image, int tile, int x, int y, IReadOnlyList<PixelRect> areas, bool keepAlpha)
    {
        var crop = image.Crop(new PixelRect(x, y, SmtFile.TileSize, SmtFile.TileSize));
        var data = TileData(tile);
        Memory<byte> Level(int mip) => data.Slice(SmtFile.MipLayout[mip].Offset, SmtFile.MipLayout[mip].Length);

        var local = areas[0];
        foreach (var area in areas)
        {
            if (!keepAlpha) crop.MakeOpaque(area);
            BlockCompression.Encode(BlockFormat.Dxt1, crop, Level(0), area, parallel: false);
            local = local.Union(area);
        }
        MipChain.Rebuild(crop, SmtFile.MipCount, local,
            mip =>
            {
                var level = new RgbaImage(SmtFile.TileSize >> mip, SmtFile.TileSize >> mip);
                BlockCompression.DecodeInto(BlockFormat.Dxt1, Level(mip).Span, level.Width, level.Height, level, 0, 0);
                return level;
            },
            (mip, level, dirty) => BlockCompression.Encode(BlockFormat.Dxt1, level, Level(mip), dirty, parallel: false));
    }

    /// <summary>What an edit of a texture region can change: the tiles shown there, the tile index and the tile counts.</summary>
    public sealed class State
    {
        /// <summary>The tile-grid cells the regions cover, and their tile numbers (an edit changes no other cell).</summary>
        internal int[] IndexCells { get; init; } = [];
        internal int[] IndexValues { get; init; } = [];
        internal SmtReference[] TileFileEntries { get; init; } = [];
        internal int[] FileTileCounts { get; init; } = [];
        internal Dictionary<int, byte[]> Tiles { get; init; } = new();
        internal bool Trimmed { get; init; }
    }

    /// <summary>Captures everything an edit of <paramref name="region"/> (texture pixels) could change, for undo.</summary>
    public State Capture(PixelRect region) => Capture([region]);

    /// <summary>
    /// Captures everything one edit of several regions could change, as one state. (Separate states for the regions
    /// of one edit would each record the tile index and tile counts from before it, and undoing one would cut off
    /// tile copies the other still points at.)
    /// </summary>
    public State Capture(IEnumerable<PixelRect> regions)
    {
        const int size = SmtFile.TileSize;
        var tiles = new Dictionary<int, byte[]>();
        var cells = new SortedSet<int>();
        foreach (var r in regions)
        {
            var region = r.Intersect(Bounds);
            if (region.IsEmpty)
                continue;
            for (int ty = region.Y / size; ty < (region.Bottom + size - 1) / size; ty++)
                for (int tx = region.X / size; tx < (region.Right + size - 1) / size; tx++)
                {
                    int cell = ty * smf.TileColumns + tx, tile = smf.TileIndex[cell];
                    cells.Add(cell);
                    if ((uint)tile < (uint)TotalTiles && !tiles.ContainsKey(tile))
                        tiles[tile] = TileData(tile).ToArray();
                }
        }
        return new State
        {
            IndexCells = [.. cells],
            IndexValues = cells.Select(c => smf.TileIndex[c]).ToArray(),
            TileFileEntries = [.. smf.TileFiles],
            FileTileCounts = files.Select(f => f.TileCount).ToArray(),
            Tiles = tiles,
            Trimmed = extraTilesTrimmed,
        };
    }

    public void Restore(State state)
    {
        for (int f = 0; f < files.Count; f++)
            files[f].Resize(state.FileTileCounts[f]);
        smf.TileFiles.Clear();
        smf.TileFiles.AddRange(state.TileFileEntries);
        for (int i = 0; i < state.IndexCells.Length; i++)
            smf.TileIndex[state.IndexCells[i]] = state.IndexValues[i];
        foreach (var (tile, bytes) in state.Tiles)
            bytes.CopyTo(TileData(tile));
        extraTilesTrimmed = state.Trimmed;
    }

    /// <summary>Roughly how much memory a captured state holds, for undo limits.</summary>
    public static long Bytes(State state) => state.Tiles.Values.Sum(t => (long)t.Length) + state.IndexCells.Length * 8L + 64;

    int TotalTiles => smf.TileCount;

    /// <summary>The bytes of a tile by its global number (writable).</summary>
    Memory<byte> TileData(int tile)
    {
        for (int f = 0; f < files.Count; f++)
        {
            int count = smf.TileFiles[f].TileCount;
            if (tile < count)
                return files[f].Tile(tile);
            tile -= count;
        }
        throw new ArgumentOutOfRangeException(nameof(tile), "Tile number is beyond the last tile file.");
    }

    /// <summary>Adds a tile at the end of the last tile file and returns its global number.</summary>
    int AppendTile(byte[] tile)
    {
        var last = files[^1];
        int used = smf.TileFiles[^1].TileCount;
        if (!extraTilesTrimmed && last.TileCount > used)
        {
            // Unread spare tiles would shift the new tile's number; the engine never reads them anyway.
            int spare = last.TileCount - used;
            last.Truncate(used);
            warnings.Add($"dropped {spare} unused tiles from the last tile file to make room");
        }
        extraTilesTrimmed = true;
        last.AddTile(tile);
        smf.TileFiles[^1] = smf.TileFiles[^1] with { TileCount = used + 1 };
        return TotalTiles - 1;
    }
}
