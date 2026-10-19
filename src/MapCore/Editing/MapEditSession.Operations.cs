using System.Globalization;
using MapCore.Heights;

namespace MapCore.Editing;

/// <summary>
/// Typed forms of the text operations, for the editor. Numbers are always written with invariant formatting,
/// so a comma decimal separator in the user's locale cannot break an operation. Each is one undoable step
/// (or part of the open group).
/// </summary>
public sealed partial class MapEditSession
{
    public string RaiseHeights(Brush brush, double amount) =>
        Apply(Op($"height-raise:{brush.X},{brush.Z}:{brush.Radius}:{amount}:{brush.Hardness}"));

    /// <summary>Pulls heights towards <paramref name="height"/>; <paramref name="strength"/> (0-1) scales the pull.</summary>
    public string FlattenHeights(Brush brush, double height, double strength = 1) =>
        Apply(Op($"height-flatten:{brush.X},{brush.Z}:{brush.Radius}:{height}:{brush.Hardness}:{strength}"));

    public string SmoothHeights(Brush brush, double strength) =>
        Apply(Op($"height-smooth:{brush.X},{brush.Z}:{brush.Radius}:{strength}:{brush.Hardness}"));

    public string AddHeightNoise(Brush brush, double amplitude, double featureSize) =>
        Apply(Op($"height-noise:{brush.X},{brush.Z}:{brush.Radius}:{amplitude}:{brush.Hardness}:{featureSize}"));

    public string TintTexture(Brush brush, Imaging.Rgba tint, double strength = 1) =>
        Apply(Op($"texture-tint:{brush.X},{brush.Z}:{brush.Radius}:{tint.R},{tint.G},{tint.B}:{brush.Hardness}:{strength}"));

    public string AdjustTexture(Brush brush, double brightness, double saturation, double hueDegrees, double strength = 1) =>
        Apply(Op($"texture-adjust:{brush.X},{brush.Z}:{brush.Radius}:{brightness},{saturation},{hueDegrees}:{brush.Hardness}:{strength}"));

    public string CloneTexture(Brush brush, double dx, double dz, double strength = 1) =>
        Apply(Op($"texture-clone:{brush.X},{brush.Z}:{brush.Radius}:{dx},{dz}:{brush.Hardness}:{strength}"));

    /// <summary>Blends the splat weights towards <paramref name="weights"/> (R, G, B, A = detail textures 1-4).</summary>
    public string PaintSplat(Brush brush, Imaging.Rgba weights, double strength = 1) =>
        Apply(Op($"splat-paint:{brush.X},{brush.Z}:{brush.Radius}:{weights.R},{weights.G},{weights.B},{weights.A}:{brush.Hardness}:{strength}"));

    /// <summary>Sets a mapinfo colour (or any three numbers), creating the key if the file lacks it.</summary>
    public string SetColour(string path, double r, double g, double b) => Apply(Op($"set:{path}={r},{g},{b}"));

    /// <summary>Moves the spot at (x, z) towards (toX, toZ); with <paramref name="look"/>, its look (texture, splat, normals) moves too.</summary>
    public string MoveSpot(double x, double z, double toX, double toZ, bool look) =>
        Apply(Op($"metal-move:{x},{z}:{toX},{toZ}") + (look ? ":look" : ""));

    /// <summary>Adds a spot shaped like the map's typical spot.</summary>
    public string AddSpot(double x, double z) => Apply(Op($"metal-add:{x},{z}"));

    public string RemoveSpot(double x, double z) => Apply(Op($"metal-remove:{x},{z}"));

    /// <summary>Scales the spot so a T1 extractor makes <paramref name="income"/> metal/s.</summary>
    public string SetSpotIncome(double x, double z, double income) => Apply(Op($"metal-income:{x},{z}:{income}"));

    public string ResizeSpot(double x, double z, int cellsAcross) => Apply(Op($"metal-resize:{x},{z}:{cellsAcross}"));

    public string OffsetHeights(double amount) => Apply(Op($"height-offset:{amount}"));

    public string ScaleHeights(double factor) => Apply(Op($"height-scale:{factor}"));

    static string Op(FormattableString operation) => operation.ToString(CultureInfo.InvariantCulture);
}
