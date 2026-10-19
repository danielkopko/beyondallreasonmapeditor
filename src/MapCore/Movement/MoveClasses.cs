using MapCore.Heights;

namespace MapCore.Movement;

/// <summary>How the engine moves a class over terrain (its speedModClass).</summary>
public enum MoverKind { Tank, Bot, Hover }

/// <summary>
/// A BAR movement class, as far as terrain decides where it goes: the steepest slope it climbs (degrees, as written
/// in BAR's movedefs.lua), how much slopes slow it, how deep into water it goes, and its footprint (squares).
/// </summary>
public sealed record MoveClass(string Name, string Group, MoverKind Kind, double MaxSlopeDegrees, double SlopeMod, double MaxWaterDepth, int Footprint, string Examples)
{
    /// <summary>
    /// The class's limit on the engine's slope measure (1 − the ground normal's y). BAR divides its degrees by 1.5 when
    /// loading (movedefs.lua, setMaxSlope) and the engine multiplies them by 1.5 again, clamped to 60 × 1.5
    /// (RecoilEngine MoveDefHandler.cpp, DegreesToMaxSlope): so it is 1 − cos(degrees), up to 90°.
    /// </summary>
    public double MaxSlope => 1 - Math.Cos(Math.Clamp(MaxSlopeDegrees, 0, 90) * Math.PI / 180);

    /// <summary>The speed factor going straight up a slope (the engine's slope measure): 1 / (1 + slope × slopeMod).</summary>
    public double UphillSpeed(double slope) => 1 / (1 + Math.Max(0, slope) * SlopeMod);
}

/// <summary>
/// BAR's land and hover movement classes, from gamedata/movedefs.lua (beyond-all-reason/Beyond-All-Reason, master,
/// read 2026-10-15). Ships, submarines and the raptor and scavenger classes are left out: a ramp is no use to them.
/// Bot classes all get slopeMod 4 (movedefs.lua sets SLOPE_MOD.MINIMUM for every name with BOT in it); TBOT3 sets no
/// maxslope, so the engine's default applies (60 × 1.5 = 90°, slopeMod 4 / (1 + 0.001)).
/// </summary>
public static class BarMoveClasses
{
    public const string Vehicles = "Vehicles", HeavyTank = "Heavy tank (armthor)", Amphibious = "Amphibious and T4 vehicles, armamph",
        Bots = "Bots and commanders", AllTerrain = "Spiders and all-terrain bots", Hovers = "Hovercraft";

    public static readonly IReadOnlyList<MoveClass> All =
    [
        new("TANK2", Vehicles, MoverKind.Tank, 27, 18, 20, 2, "armflash, armfav, corgator"),
        new("TANK3", Vehicles, MoverKind.Tank, 27, 18, 20, 3, "armstump, corraid, armcv"),
        new("MTANK3", Vehicles, MoverKind.Tank, 27, 25, 20, 3, "correap, armbull"),
        new("HTANK4", Vehicles, MoverKind.Tank, 27, 36, 20, 4, "corgol, armmanni"),
        new("HTANK7", HeavyTank, MoverKind.Tank, 33, 42, 20, 7, "armthor"),
        new("ATANK3", Amphibious, MoverKind.Tank, 54, 18, 5000, 3, "armcroc, corseal, armbeaver"),
        new("EPICVEH", Amphibious, MoverKind.Tank, 54, 18, 9999, 5, "corgolt4, armrattet4"),
        new("AHOVER2", Amphibious, MoverKind.Hover, 54, 18, double.PositiveInfinity, 2, "armamph"),
        new("BOT2", Bots, MoverKind.Bot, 54, 4, 20, 2, "armpw, corak"),
        new("BOT3", Bots, MoverKind.Bot, 54, 4, 20, 3, "armzeus, armmav"),
        new("HBOT4", Bots, MoverKind.Bot, 54, 4, 20, 4, "armraz, corsumo"),
        new("HBOT7", Bots, MoverKind.Bot, 54, 4, 20, 7, "corjugg"),
        new("SBOT2", Bots, MoverKind.Bot, 54, 4, 5, 2, "armflea"),
        new("ABOT3", Bots, MoverKind.Bot, 54, 4, 5000, 3, "cormando, armjeth"),
        new("ABOTBOMB2", Bots, MoverKind.Bot, 54, 4, 5000, 2, "corroach, armvader"),
        new("HABOT5", Bots, MoverKind.Bot, 54, 4, 5000, 5, "corshiva, armbanth"),
        new("VBOT6", Bots, MoverKind.Bot, 54, 4, 5000, 6, "corkorg"),
        new("COMMANDERBOT", Bots, MoverKind.Bot, 54, 4, 5000, 3, "commanders"),
        new("EPICBOT", Bots, MoverKind.Bot, 54, 4, 9999, 4, "armpwt4, corakt4"),
        new("HTBOT6", AllTerrain, MoverKind.Bot, 90, 4, 20, 6, "armvang, corkarg"),
        new("TBOT3", AllTerrain, MoverKind.Bot, 90, 4 / 1.001, 20, 3, "armspid, armsptk"),
        new("HOVER2", Hovers, MoverKind.Hover, 33, 25, double.PositiveInfinity, 2, "armsh, corsh"),
        new("HOVER3", Hovers, MoverKind.Hover, 33, 25, double.PositiveInfinity, 3, "armanac, corhal"),
        new("HHOVER4", Hovers, MoverKind.Hover, 33, 18, double.PositiveInfinity, 4, "armlun, corsok"),
    ];

    /// <summary>The hovercraft class the passability overlay uses (HOVER3; HOVER2 and HHOVER4 go to the same places).</summary>
    public static MoveClass Hover => All.Single(c => c.Name == "HOVER3");

    /// <summary>The vehicle class the overlay uses (TANK3: every class in the Vehicles group goes to the same places).</summary>
    public static MoveClass Vehicle => All.Single(c => c.Name == "TANK3");

    /// <summary>The land-bot class the overlay uses (BOT3; amphibious bots and commanders share its slope limit but go deeper).</summary>
    public static MoveClass Bot => All.Single(c => c.Name == "BOT3");

    /// <summary>The passability overlays, by the name used in the 3D view and on the command line.</summary>
    public static readonly IReadOnlyList<(string Name, MoveClass Class)> Overlays =
        [("Hovercraft", Hover), ("Vehicles", Vehicle), ("Bots", Bot)];

    /// <summary>The groups in the order they are shown.</summary>
    public static readonly IReadOnlyList<string> Groups = [Bots, AllTerrain, Vehicles, HeavyTank, Amphibious, Hovers];
}

/// <summary>
/// The engine's slope measure, from the corner heights as RecoilEngine computes it (rts/Map/ReadMap.cpp): each 8-unit
/// square has two triangles (top-left and bottom-right); a slope-map cell covers 2 x 2 squares (16 units) and takes
/// the steepest of its 8 triangle normals blended towards their average, so small holes don't block big units.
/// </summary>
public static class TerrainSlope
{
    /// <summary>1 − (the blended normal's y) for the slope-map cell (cx, cz), each 16 units across.</summary>
    public static double Cell(HeightMap heights, int cx, int cz) => Cell((x, z) => heights[x, z], cx, cz);

    /// <summary>The same, from any corner heights (vertex x, z: 8 units apart), e.g. heights an edit would leave.</summary>
    public static double Cell(Func<int, int, double> heights, int cx, int cz)
    {
        double sum = 0, steepest = double.MaxValue;
        for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
            {
                int x = 2 * cx + dx, z = 2 * cz + dz;
                double tl = heights(x, z), tr = heights(x + 1, z), bl = heights(x, z + 1), br = heights(x + 1, z + 1);
                double s = HeightMap.Spacing;
                double topLeft = s / Math.Sqrt(s * s + (tr - tl) * (tr - tl) + (bl - tl) * (bl - tl));
                double bottomRight = s / Math.Sqrt(s * s + (bl - br) * (bl - br) + (tr - br) * (tr - br));
                sum += topLeft + bottomRight;
                steepest = Math.Min(steepest, Math.Min(topLeft, bottomRight));
            }
        double average = sum / 8;
        double y = steepest + (average - steepest) * (steepest / average); // mix(maxslope, avgslope, maxslope / avgslope)
        return 1 - y;
    }

    /// <summary>The slope measure of ground tilted by <paramref name="degrees"/> (a plane: every normal alike).</summary>
    public static double OfAngle(double degrees) => 1 - Math.Cos(degrees * Math.PI / 180);

    /// <summary>The angle, in degrees, whose slope measure is <paramref name="slope"/>.</summary>
    public static double ToDegrees(double slope) => Math.Acos(Math.Clamp(1 - slope, -1, 1)) * 180 / Math.PI;
}
