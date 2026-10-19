using System.Text;

namespace MapCore.Movement;

/// <summary>Whether one group of movement classes can climb a ramp, and how fast going up.</summary>
/// <param name="LimitDegrees">The group's steepest slope (the same for every class in a group).</param>
/// <param name="NeededLength">For a group the ramp's surface is too steep for: the shortest ramp of the same rise it could climb.</param>
/// <param name="BlockedAt">For a group the surface suits but where the ramp meets the ground is too steep: "start" or "end".</param>
public sealed record GroupVerdict(string Group, double LimitDegrees, bool Climbs, double SlowestUphill, double FastestUphill, double? NeededLength,
    IReadOnlyList<MoveClass> Classes, string? BlockedAt = null, double BlockedDegrees = 0);

/// <summary>
/// Which BAR units can drive or walk up a straight ramp, by the engine's own rule (RecoilEngine GroundMoveMath.cpp and
/// HoverMoveMath.cpp): a slope-map cell blocks a class when its slope measure is above the class's limit, or when it is
/// deeper under water than the class goes; hovers ignore slopes on water. Going straight up, speed is multiplied by
/// 1 / (1 + slope × slopeMod). A ramp's surface is a plane, so every cell on it has the slope measure 1 − cos(angle).
/// Where it meets the ground at each end ("lips", measured by <see cref="Editing.MapEditSession.RampLipDegrees"/>) it
/// can be steeper, when an end isn't on ground at its height: a group climbs the ramp only if those pass too.
/// </summary>
public static class RampPassability
{
    /// <summary>Within this many degrees of a limit, "only just": quantised heights or an uneven end can tip it.</summary>
    public const double CloseDegrees = 1;

    /// <param name="startLip">The steepest slope (degrees) along the middle where the ramp meets the ground at its start; 0 if unknown.</param>
    /// <param name="endLip">The same at its end.</param>
    public static IReadOnlyList<GroupVerdict> Check(double length, double h1, double h2, double startLip = 0, double endLip = 0)
    {
        double rise = Math.Abs(h2 - h1), degrees = Math.Atan2(rise, length) * 180 / Math.PI;
        double slope = TerrainSlope.OfAngle(degrees);
        bool onWater = Math.Max(h1, h2) < 0; // all of it under water: hovers ride over it
        var verdicts = new List<GroupVerdict>();
        foreach (var group in BarMoveClasses.Groups)
        {
            var classes = BarMoveClasses.All.Where(c => c.Group == group).ToList();
            double limit = classes.Min(c => c.MaxSlopeDegrees);
            bool Passes(MoveClass c, double s) => c.Kind == MoverKind.Hover && onWater || s <= c.MaxSlope;
            bool surface = classes.All(c => Passes(c, slope));
            string? blockedAt = null;
            double blockedDegrees = 0;
            if (surface)
                foreach (var (where, lip) in new[] { ("start", startLip), ("end", endLip) })
                    if (blockedAt == null && !classes.All(c => Passes(c, TerrainSlope.OfAngle(lip))))
                        (blockedAt, blockedDegrees) = (where, lip);
            var speeds = classes.Select(c => c.Kind == MoverKind.Hover && onWater ? 1 : c.UphillSpeed(slope)).ToList();
            double? needed = surface || limit >= 90 ? null : rise / Math.Tan(limit * Math.PI / 180);
            verdicts.Add(new GroupVerdict(group, limit, surface && blockedAt == null, speeds.Min(), speeds.Max(), needed, classes, blockedAt, blockedDegrees));
        }
        return verdicts;
    }

    /// <summary>The verdicts as the editor shows them, with notes on water depth and width.</summary>
    public static string Describe(double length, double width, double h1, double h2, double startLip = 0, double endLip = 0)
    {
        double degrees = Math.Atan2(Math.Abs(h2 - h1), length) * 180 / Math.PI;
        var verdicts = Check(length, h1, h2, startLip, endLip);
        var text = new StringBuilder("Who can go up it (speed going up):");
        foreach (var v in verdicts)
        {
            string limit = v.LimitDegrees >= 90 ? "any slope" : $"{v.LimitDegrees:0}°";
            text.AppendLine();
            if (v.Climbs)
            {
                string speed = Math.Round(v.SlowestUphill * 100) == Math.Round(v.FastestUphill * 100)
                    ? $"{v.SlowestUphill * 100:0}%"
                    : $"{v.SlowestUphill * 100:0}–{v.FastestUphill * 100:0}%";
                bool close = v.LimitDegrees < 90 && v.LimitDegrees - degrees < CloseDegrees;
                text.Append($"✓ {v.Group} ({limit}): {speed}{(close ? ", only just" : "")}");
            }
            else if (v.BlockedAt is string where)
                text.Append($"✗ {v.Group} ({limit}): where it meets the ground at the {where} ({v.BlockedDegrees:0}°)");
            else
                text.Append($"✗ {v.Group} ({limit}): too steep; needs {Math.Ceiling(v.NeededLength!.Value / 10) * 10:0} long at this rise");
        }
        if (verdicts.Any(v => v.BlockedAt != null))
            text.AppendLine().Append("Where it meets the ground is steeper than the ramp: put that end on ground at its height, or set its height to the ground's.");

        double lowest = Math.Min(h1, h2);
        var stopped = verdicts.Where(v => v.Climbs).SelectMany(v => v.Classes)
            .Where(c => c.Kind != MoverKind.Hover && c.MaxWaterDepth < -lowest).ToList();
        if (stopped.Count > 0)
            text.AppendLine().Append($"Its low end is {-lowest:0} under water: land units stop at {stopped.Max(c => c.MaxWaterDepth):0} deep " +
                                     "(amphibious ones and hovers go on).");

        var wide = verdicts.Where(v => v.Climbs).SelectMany(v => v.Classes).Where(c => c.Footprint * 8 > width).ToList();
        if (wide.Count > 0)
            text.AppendLine().Append($"Narrower than the widest units ({string.Join(", ", wide.OrderByDescending(c => c.Footprint).Take(3).Select(c => c.Examples.Split(',')[0]))}, " +
                                     $"up to {wide.Max(c => c.Footprint) * 8} units across): they may not fit, as the engine checks a unit's whole footprint.");
        return text.ToString();
    }
}
