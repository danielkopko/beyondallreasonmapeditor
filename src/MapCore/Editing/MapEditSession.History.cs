using System.Globalization;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;

namespace MapCore.Editing;

/// <summary>Which parts of the map an edit touched.</summary>
[Flags]
public enum MapLayers
{
    None = 0,
    Heights = 1,
    Texture = 2,
    Minimap = 4,
    Metal = 8,
    Grass = 16,
    Items = 32,
    MapInfo = 64,
    Resources = 128,
    /// <summary>The splat distribution map (painted with splat-paint; also a resource).</summary>
    Splat = 256,
    /// <summary>The terrain type map (which of mapinfo's terrain types each 16-unit square is).</summary>
    Types = 512,
    All = 1023,
}

/// <summary>A rectangle in game units (x east, z south).</summary>
public readonly record struct WorldRect(double X, double Z, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Z + Height;

    public WorldRect Union(WorldRect other) => new(
        Math.Min(X, other.X), Math.Min(Z, other.Z),
        Math.Max(Right, other.Right) - Math.Min(X, other.X), Math.Max(Bottom, other.Bottom) - Math.Min(Z, other.Z));
}

/// <summary>What changed: the layers, and the area (null: anywhere / everywhere).</summary>
public sealed record MapChange(MapLayers Layers, WorldRect? Area);

/// <summary>Undo/redo. Every operation records a snapshot of exactly what it can change, taken just before it runs.</summary>
public sealed partial class MapEditSession
{
    public const int MaxUndo = 100;

    /// <summary>Undo keeps at most this much memory: the oldest steps go first (the latest always stays).</summary>
    public const long MaxUndoBytes = 512L << 20;

    /// <summary>Memory held for undo and redo (approximate).</summary>
    public long UndoBytes => undo.Concat(redo).Sum(e => e.Snapshots.Sum(s => s.Bytes)) + (group?.Sum(s => s.Bytes) ?? 0);

    readonly List<UndoEntry> undo = [];
    readonly List<UndoEntry> redo = [];
    List<Snapshot>? group;
    string? groupDescription;
    int changeCount, savedChangeCount;

    /// <summary>
    /// Where the session records what it does, for recovery after a crash (null: not recorded). Set it right after
    /// opening: setting it first <see cref="Prime">primes</see> the session, as a replay does before its first entry.
    /// </summary>
    public SessionJournal? Journal
    {
        get => journal;
        set
        {
            if (value != null)
                Prime();
            journal = value;
        }
    }
    SessionJournal? journal;
    bool primed;

    /// <summary>
    /// Works out now what the session otherwise works out on first use from the map as it is then: the heights (which
    /// read mapinfo's height range), the decoded texture, the metal spots' look, the minimap style and the save-check
    /// baseline. A session that is recorded and its replay both do this before any edit, so these come out the same
    /// whatever the editor happened to look at first.
    /// </summary>
    public void Prime()
    {
        if (primed)
            return;
        primed = true;
        _ = Heights;
        _ = TextureImage;
        _ = Look;
        _ = Style;
        RecordBaseline();
    }

    /// <summary>True when something was applied, undone or redone since the last <see cref="Save"/>.</summary>
    public bool HasUnsavedChanges => changeCount != savedChangeCount;

    /// <summary>Raised after every applied, undone or redone change.</summary>
    public event EventHandler<MapChange>? Changed;

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string? UndoDescription => undo.Count > 0 ? undo[^1].Description : null;
    public string? RedoDescription => redo.Count > 0 ? redo[^1].Description : null;

    /// <summary>
    /// Applies one text operation (see the class summary) as it is written, never mirrored, records it for undo and
    /// describes what changed. A failed operation is rolled back, so it leaves nothing half-done. (<see cref="Apply"/>
    /// adds the mirror image, with <see cref="Mirror"/> set; a journal's replay applies what was recorded, mirror
    /// images included, through this.)
    /// </summary>
    /// <exception cref="InvalidOperationException">The operation is malformed or cannot be applied.</exception>
    internal string ApplyOne(string operation)
    {
        RecordBaseline();
        var snapshots = CaptureFor(operation);
        string result;
        try
        {
            result = ApplyCore(operation);
        }
        catch
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
                snapshots[i].Restore();
            throw;
        }
        if (group != null)
            group.AddRange(snapshots);
        else
            Push(undo, new UndoEntry(operation, snapshots));
        redo.Clear();
        changeCount++;
        Journal?.Write(new JournalEntry("apply", KeepImportedFile(operation)));
        RaiseChanged(snapshots);
        return result;
    }

    /// <summary>With a journal, an import reads a copy kept beside it, so a replay finds the file even if the original moves.</summary>
    string KeepImportedFile(string operation)
    {
        // A World Machine output: options, '|', the file (World Machine rewrites it on every build: the copy is what was read).
        if (Journal != null && operation.StartsWith("wm-import:", StringComparison.Ordinal) && operation.IndexOf('|') is int bar and > 10)
            return File.Exists(operation[(bar + 1)..]) ? operation[..(bar + 1)] + Journal.KeepCopy(operation[(bar + 1)..]) : operation;
        int eq = operation.IndexOf('=');
        if (Journal == null || !operation.StartsWith("import:", StringComparison.Ordinal) || eq <= 7)
            return operation;
        var file = operation[(eq + 1)..];
        return File.Exists(file) ? operation[..(eq + 1)] + Journal.KeepCopy(file) : operation;
    }

    /// <summary>Reads a layer from PNG (see <see cref="ImportLayerCore"/>), as an undoable operation.</summary>
    public string ImportLayer(string layer, string pngPath) => Apply($"import:{layer}={pngPath}");

    /// <summary>Paints a colour onto the map texture (see <see cref="PaintTextureCore"/>), as an undoable operation.</summary>
    public string PaintTexture(Brush brush, Rgba colour, double strength = 1) =>
        Apply(string.Create(CultureInfo.InvariantCulture,
            $"texture-paint:{brush.X},{brush.Z}:{brush.Radius}:{colour.R},{colour.G},{colour.B}:{brush.Hardness}:{strength}"));

    /// <summary>Starts collecting operations into one undo step (e.g. all dabs of a brush stroke).</summary>
    public void BeginGroup(string description)
    {
        if (group == null)
            Journal?.Write(new JournalEntry("begin", description));
        group ??= [];
        groupDescription ??= description;
    }

    public void EndGroup()
    {
        if (group != null)
            Journal?.Write(new JournalEntry("end"));
        if (group is { Count: > 0 })
            Push(undo, new UndoEntry(groupDescription ?? "edit", group));
        group = null;
        groupDescription = null;
    }

    public void Undo()
    {
        EndGroup();
        if (undo.Count == 0)
            return;
        Journal?.Write(new JournalEntry("undo"));
        var entry = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        var after = new List<Snapshot>(entry.Snapshots.Count);
        for (int i = entry.Snapshots.Count - 1; i >= 0; i--)
        {
            after.Add(entry.Snapshots[i].CaptureAgain());
            entry.Snapshots[i].Restore();
        }
        after.Reverse();
        Push(redo, entry with { Snapshots = after });
        changeCount++;
        RaiseChanged(entry.Snapshots);
    }

    public void Redo()
    {
        EndGroup();
        if (redo.Count == 0)
            return;
        Journal?.Write(new JournalEntry("redo"));
        var entry = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        var before = new List<Snapshot>(entry.Snapshots.Count);
        foreach (var snapshot in entry.Snapshots)
        {
            before.Add(snapshot.CaptureAgain());
            snapshot.Restore();
        }
        Push(undo, entry with { Snapshots = before });
        changeCount++;
        RaiseChanged(entry.Snapshots);
    }

    static void Push(List<UndoEntry> stack, UndoEntry entry)
    {
        stack.Add(entry);
        if (stack.Count > MaxUndo)
            stack.RemoveAt(0);
        while (stack.Count > 1 && stack.Sum(e => e.Snapshots.Sum(s => s.Bytes)) > MaxUndoBytes)
            stack.RemoveAt(0);
    }

    void RaiseChanged(IReadOnlyList<Snapshot> snapshots)
    {
        if (Changed == null || snapshots.Count == 0)
            return;
        var layers = MapLayers.None;
        WorldRect? area = null;
        bool everywhere = false;
        foreach (var s in snapshots)
        {
            layers |= s.Layers;
            if (s.Area is WorldRect a)
                area = area?.Union(a) ?? a;
            else
                everywhere = true;
        }
        Changed(this, new MapChange(layers, everywhere ? null : area));
    }

    /// <summary>Snapshots of what <paramref name="operation"/> can change, taken now.</summary>
    List<Snapshot> CaptureFor(string operation)
    {
        int colon = operation.IndexOf(':');
        string name = colon > 0 ? operation[..colon] : operation, rest = colon > 0 ? operation[(colon + 1)..] : "";
        var args = rest.Split(':');
        switch (name)
        {
            case "set" or "unset" or "start-move":
                return [new LuaSnapshot(Info.Document, MapLayers.MapInfo)];
            case "metal-move" when args.Length == 3 && args[2] == "look" && TryLookPlan(args[0], args[1], out var plan):
                return LookSnapshots(plan);
            case var _ when name.StartsWith("metal-", StringComparison.Ordinal):
                return [new BytesSnapshot(Smf.MetalMap, MapLayers.Metal)];
            case "grass-paint":
                return [new GrassSnapshot(Smf)];
            case var _ when name.StartsWith("item-", StringComparison.Ordinal) || name.StartsWith("items-", StringComparison.Ordinal):
                var items = new List<Snapshot> { new FeatureSnapshot(Smf) };
                if (Items.Placer != null)
                    items.Add(new LuaSnapshot(Items.Placer.Document, MapLayers.Items));
                return items;
            case "height-raise" or "height-flatten" or "height-smooth" or "height-noise" when args.Length >= 2 && TryBrush(args[0], args[1], out var brush):
                return [new HeightSnapshot(Heights, Heights.VertexBounds(brush))];
            case "height-offset" or "height-scale":
                return [new HeightSnapshot(Heights, Heights.AllVertices)];
            case "ramp" when TryParseRamp(args, out var ramp):
                return RampSnapshots(ramp);
            case "asset-add" when args.Length == 1:
                return AssetSnapshots(args[0]);
            case "feature-custom":
                return CustomFeatureSnapshots(args);
            case "wm-import":
                return WorldMachineSnapshots(rest);
            case "beautify-heights":
                return [new HeightSnapshot(Heights, Heights.AllVertices)];
            case "beautify-texture":
                return [new TextureSnapshot(this, Texture.Bounds), new BytesSnapshot(Smf.Minimap, MapLayers.Minimap)];
            case "structure-kind" or "structure-change" or "structure-add" or "structure-move" or "structure-turn" or "structure-broken" or "structure-remove"
                or "structure-marker-add" or "structure-marker-move" or "structure-marker-remove"
                or "structure-zone" or "structure-targets" or "structure-rule":
                return StructureSnapshots(name, args);
            case "texture-paint" or "texture-tint" or "texture-adjust" or "texture-clone" when args.Length >= 2 && TryBrush(args[0], args[1], out var paint):
                return [new TextureSnapshot(this, TextureRegion(paint)), new MinimapSnapshot(this, [TextureRegion(paint)])];
            case "splat-paint" when args.Length >= 2 && TryBrush(args[0], args[1], out var splat) && TrySplat(out var splatEntry):
                return [new DdsRegionSnapshot(this, splatEntry, [BrushArea(splat)], MapLayers.Splat)];
            case "import":
            {
                int eq = rest.IndexOf('=');
                string layer = eq > 0 ? rest[..eq] : rest;
                return layer switch
                {
                    "heights" => [new HeightSnapshot(Heights, Heights.AllVertices)],
                    "texture" => [new TextureSnapshot(this, Texture.Bounds), new BytesSnapshot(Smf.Minimap, MapLayers.Minimap)],
                    "metal" => [new BytesSnapshot(Smf.MetalMap, MapLayers.Metal)],
                    "grass" => [new GrassSnapshot(Smf)],
                    "types" => [new BytesSnapshot(Smf.TypeMap, MapLayers.Types)],
                    _ when layer.StartsWith("resource:", StringComparison.Ordinal) && TryResourceEntry(layer, out var entry) => [new ResourceSnapshot(this, entry)],
                    _ => [],
                };
            }
            default:
                return [];
        }
    }

    static bool TryBrush(string point, string radius, out Brush brush)
    {
        try
        {
            brush = MakeBrush(point, radius, null);
            return true;
        }
        catch (InvalidOperationException)
        {
            brush = default;
            return false;
        }
    }

    bool TrySplat(out string entry)
    {
        try
        {
            Resource(SplatKey, out entry);
            return true;
        }
        catch (InvalidOperationException)
        {
            entry = "";
            return false;
        }
    }

    bool TryResourceEntry(string layer, out string entry)
    {
        try
        {
            Resource(ResourceKey(layer), out entry);
            return true;
        }
        catch (InvalidOperationException)
        {
            entry = "";
            return false;
        }
    }

    sealed record UndoEntry(string Description, IReadOnlyList<Snapshot> Snapshots);

    /// <summary>A store's state before (or after) an operation, which can be put back.</summary>
    abstract class Snapshot
    {
        public abstract MapLayers Layers { get; }

        /// <summary>Roughly how much memory the snapshot holds (for the undo limit).</summary>
        public abstract long Bytes { get; }

        /// <summary>The affected area, or null for the whole map.</summary>
        public virtual WorldRect? Area => null;

        /// <summary>A snapshot of the same scope with the store's current state.</summary>
        public abstract Snapshot CaptureAgain();

        public abstract void Restore();
    }

    sealed class LuaSnapshot(Lua.LuaDocument document, MapLayers layers) : Snapshot
    {
        readonly string text = document.Text;
        public override MapLayers Layers => layers;
        public override long Bytes => text.Length * 2L;
        public override Snapshot CaptureAgain() => new LuaSnapshot(document, layers);
        public override void Restore() => document.ReplaceText(text);
    }

    sealed class BytesSnapshot(byte[] target, MapLayers layers) : Snapshot
    {
        readonly byte[] copy = (byte[])target.Clone();
        public override MapLayers Layers => layers;
        public override long Bytes => copy.Length;
        public override Snapshot CaptureAgain() => new BytesSnapshot(target, layers);
        public override void Restore() => copy.CopyTo(target, 0);
    }

    sealed class GrassSnapshot(SmfFile smf) : Snapshot
    {
        readonly byte[]? copy = (byte[]?)smf.GrassMap?.Clone();
        public override MapLayers Layers => MapLayers.Grass;
        public override long Bytes => copy?.Length ?? 0;
        public override Snapshot CaptureAgain() => new GrassSnapshot(smf);
        public override void Restore() => smf.GrassMap = (byte[]?)copy?.Clone();
    }

    sealed class FeatureSnapshot(SmfFile smf) : Snapshot
    {
        readonly List<SmfFeature> features = [.. smf.Features];
        readonly List<string> types = [.. smf.FeatureTypes];
        public override MapLayers Layers => MapLayers.Items;
        public override long Bytes => features.Count * 24L + types.Sum(t => t.Length * 2L);
        public override Snapshot CaptureAgain() => new FeatureSnapshot(smf);

        public override void Restore()
        {
            smf.Features.Clear();
            smf.Features.AddRange(features);
            smf.FeatureTypes.Clear();
            smf.FeatureTypes.AddRange(types);
        }
    }

    sealed class HeightSnapshot(HeightMap heights, PixelRect region) : Snapshot
    {
        readonly HeightMap.State state = heights.Capture(region);
        public override MapLayers Layers => MapLayers.Heights;
        public override long Bytes => region.Width * (long)region.Height * 4;

        public override WorldRect? Area => new WorldRect(region.X * HeightMap.Spacing, region.Y * HeightMap.Spacing,
            region.Width * HeightMap.Spacing, region.Height * HeightMap.Spacing);

        public override Snapshot CaptureAgain() => new HeightSnapshot(heights, region);
        public override void Restore() => heights.Restore(state);
    }

    /// <summary>The texture over one or more regions (texture pixels) that one operation edits, captured as one state.</summary>
    sealed class TextureSnapshot : Snapshot
    {
        readonly MapEditSession session;
        readonly IReadOnlyList<PixelRect> regions;
        readonly MapTexture.State state;

        public TextureSnapshot(MapEditSession session, PixelRect region) : this(session, [region]) { }

        public TextureSnapshot(MapEditSession session, IReadOnlyList<PixelRect> regions)
        {
            this.session = session;
            this.regions = regions;
            state = session.Texture.Capture(regions);
        }

        public override MapLayers Layers => MapLayers.Texture;
        public override long Bytes => MapTexture.Bytes(state);

        /// <summary>The regions grown to whole 4x4 blocks (re-encoding changes every pixel of a touched block), together.</summary>
        public override WorldRect? Area
        {
            get
            {
                double unitsPerPixel = session.Smf.MapX * (double)SmfFile.SquareSize / session.Texture.Width;
                WorldRect? area = null;
                foreach (var region in regions)
                {
                    var blocks = region.ToBlockBounds(session.Texture.Width, session.Texture.Height);
                    var rect = new WorldRect(blocks.X * unitsPerPixel, blocks.Y * unitsPerPixel, blocks.Width * unitsPerPixel, blocks.Height * unitsPerPixel);
                    area = area?.Union(rect) ?? rect;
                }
                return area;
            }
        }

        public override Snapshot CaptureAgain() => new TextureSnapshot(session, regions);

        public override void Restore()
        {
            session.Texture.Restore(state);
            session.textureChanged = true;
            foreach (var region in regions)
                session.RefreshTexture(region.Intersect(session.Texture.Bounds));
        }
    }

    /// <summary>The minimap blocks a redraw of some texture regions changes (not the whole minimap: dabs come by the hundred).</summary>
    sealed class MinimapSnapshot : Snapshot
    {
        readonly MapEditSession session;
        readonly IReadOnlyList<PixelRect> textureRegions;
        readonly Minimap.RegionState state;

        public MinimapSnapshot(MapEditSession session, IReadOnlyList<PixelRect> textureRegions)
        {
            this.session = session;
            this.textureRegions = textureRegions;
            var texture = session.Texture;
            state = Minimap.CaptureRegions(session.Smf, textureRegions.Select(r => Minimap.ToMinimapRegion(r, texture.Width, texture.Height)));
        }

        public override MapLayers Layers => MapLayers.Minimap;
        public override long Bytes => state.Bytes;
        public override Snapshot CaptureAgain() => new MinimapSnapshot(session, textureRegions);
        public override void Restore() => Minimap.RestoreRegions(session.Smf, state);
    }

    sealed class ResourceSnapshot(MapEditSession session, string entry) : Snapshot
    {
        readonly byte[][][] levels = session.ResourceByEntry(entry).CaptureLevels();
        public override long Bytes => levels.Sum(f => f.Sum(l => (long)l.Length));

        public override MapLayers Layers =>
            MapLayers.Resources | (session.TrySplat(out var splat) && splat.Equals(entry, StringComparison.OrdinalIgnoreCase) ? MapLayers.Splat : MapLayers.None);

        public override Snapshot CaptureAgain() => new ResourceSnapshot(session, entry);

        public override void Restore()
        {
            session.ResourceByEntry(entry).RestoreLevels(levels);
            session.changedResources.Add(entry);
            session.ForgetResourceEdit(entry);
        }
    }
}
