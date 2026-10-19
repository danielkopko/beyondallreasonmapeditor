namespace MapCore;

/// <summary>How the two halves of a symmetric map match.</summary>
public enum MirrorKind
{
    /// <summary>Left and right mirror each other across the line down the middle: (x, z) → (width − x, z).</summary>
    LeftRight,

    /// <summary>Top and bottom mirror each other across the line across the middle: (x, z) → (x, height − z).</summary>
    TopBottom,

    /// <summary>Across the diagonal from the top-left corner to the bottom-right (square maps only): (x, z) → (z, x).</summary>
    Diagonal,

    /// <summary>Across the diagonal from the top-right corner to the bottom-left (square maps only): (x, z) → (size − z, size − x).</summary>
    AntiDiagonal,

    /// <summary>
    /// Mirrored and flipped: across both middle lines, which is a half turn about the centre, so the copy is at the
    /// opposite side and corner: (x, z) → (width − x, height − z).
    /// </summary>
    HalfTurn,
}

/// <summary>
/// One of a map's symmetries, in game units (x east, z south): where it takes a point, a direction and a heading.
/// Each is its own inverse: doing it twice gives back what it started from.
/// </summary>
public readonly record struct MapMirror
{
    /// <exception cref="InvalidOperationException">A diagonal on a map that isn't square.</exception>
    public MapMirror(MirrorKind kind, double width, double height)
    {
        if (kind is MirrorKind.Diagonal or MirrorKind.AntiDiagonal && width != height)
            throw new InvalidOperationException($"A diagonal mirror needs a square map; this one is {width:0} by {height:0} game units.");
        (Kind, Width, Height) = (kind, width, height);
    }

    public MirrorKind Kind { get; }
    public double Width { get; }
    public double Height { get; }

    /// <summary>Where a point goes.</summary>
    public (double X, double Z) Map(double x, double z) => Kind switch
    {
        MirrorKind.LeftRight => (Width - x, z),
        MirrorKind.TopBottom => (x, Height - z),
        MirrorKind.Diagonal => (z, x),
        MirrorKind.AntiDiagonal => (Width - z, Height - x),
        _ => (Width - x, Height - z),
    };

    /// <summary>Where a direction (or an offset between two points) goes.</summary>
    public (double X, double Z) MapVector(double dx, double dz) => Kind switch
    {
        MirrorKind.LeftRight => (-dx, dz),
        MirrorKind.TopBottom => (dx, -dz),
        MirrorKind.Diagonal => (dz, dx),
        MirrorKind.AntiDiagonal => (-dz, -dx),
        _ => (-dx, -dz),
    };

    /// <summary>Whether it is a mirror image (left and right swap), rather than a half turn: turning one way, the copy turns the other.</summary>
    public bool Reflects => Kind != MirrorKind.HalfTurn;

    /// <summary>Where a heading goes, in degrees from 0 to 360 (0 faces south, 90 east, as items' headings do).</summary>
    public double Heading(double degrees)
    {
        double turned = Kind switch
        {
            MirrorKind.LeftRight => -degrees,
            MirrorKind.TopBottom => 180 - degrees,
            MirrorKind.Diagonal => 90 - degrees,
            MirrorKind.AntiDiagonal => 270 - degrees,
            _ => degrees + 180,
        };
        return (turned % 360 + 360) % 360;
    }

    /// <summary>Where a quarter-turn facing goes (0 south, 1 east, 2 north, 3 west: buildings', doors' and tunnels').</summary>
    public int Facing(int facing) => (int)Math.Round(Heading(((facing % 4) + 4) % 4 * 90) / 90) % 4;

    /// <summary>How operations write it: lr, tb, diag, anti or turn.</summary>
    public string Code => CodeOf(Kind);

    public static string CodeOf(MirrorKind kind) => kind switch
    {
        MirrorKind.LeftRight => "lr",
        MirrorKind.TopBottom => "tb",
        MirrorKind.Diagonal => "diag",
        MirrorKind.AntiDiagonal => "anti",
        _ => "turn",
    };

    public static MirrorKind ParseKind(string code) => code.Trim() switch
    {
        "lr" => MirrorKind.LeftRight,
        "tb" => MirrorKind.TopBottom,
        "diag" => MirrorKind.Diagonal,
        "anti" => MirrorKind.AntiDiagonal,
        "turn" => MirrorKind.HalfTurn,
        var other => throw new InvalidOperationException($"'{other}' isn't a mirror: lr (left-right), tb (top-bottom), diag, anti or turn (mirrored and flipped)."),
    };

    /// <summary>Whether a map of this size has the symmetry (the diagonals need a square one).</summary>
    public static bool Fits(MirrorKind kind, double width, double height) => kind is not (MirrorKind.Diagonal or MirrorKind.AntiDiagonal) || width == height;
}
