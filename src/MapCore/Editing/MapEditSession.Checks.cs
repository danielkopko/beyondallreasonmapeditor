using MapCore.Items;
using MapCore.Structures;


namespace MapCore.Editing;

public enum CheckLevel { Note, Warning, Error }

/// <summary>Something to know before saving: errors stop the save, warnings ask first, notes are for the log.</summary>
public sealed record SaveCheck(CheckLevel Level, string Message);

/// <summary>Checks before saving: what would break in game, or surprise.</summary>
public sealed partial class MapEditSession
{
    const double EdgeMargin = 128;

    /// <summary>
    /// BAR's commanders by name: every unit BAR marks a commander starts armcom, corcom or legcom (armcomcon, legcomlvl2,
    /// legcomt2off …) and nothing else does (its units at 9d7bdf3, 2026-10-15). Decoys (armdecom) aren't commanders.
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex CommanderName = new("^(arm|cor|leg)com[a-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Warnings the map already had when opened: they are notes, not warnings, at save (the user didn't cause them).</summary>
    HashSet<string>? baselineWarnings;

    /// <summary>Records what the map is like before the first change of this session (called by <see cref="Apply"/>).</summary>
    void RecordBaseline() => baselineWarnings ??= Collect(requireNewName: false, checkName: false)
        .Where(c => c.Level == CheckLevel.Warning).Select(c => c.Message).ToHashSet(StringComparer.Ordinal);

    /// <param name="requireNewName">Keeping the original name and version is an error (the editor's Save As), not a warning.</param>
    /// <param name="checkName">Check the name at all (the editor's Save As dialog already insists on a new one).</param>
    public IReadOnlyList<SaveCheck> CheckBeforeSave(bool requireNewName = true, bool checkName = true)
    {
        RecordBaseline(); // nothing changed yet: everything found now was already there
        return Collect(requireNewName, checkName)
            .Select(c => c.Level == CheckLevel.Warning && baselineWarnings!.Contains(c.Message)
                ? c with { Level = CheckLevel.Note, Message = c.Message + " (already so when the map was opened)" }
                : c)
            .ToList();
    }

    List<SaveCheck> Collect(bool requireNewName, bool checkName)
    {
        var checks = new List<SaveCheck>();
        void Add(CheckLevel level, string message) => checks.Add(new SaveCheck(level, message));

        if (checkName && string.Equals(Info.Name, LoadedName, StringComparison.OrdinalIgnoreCase) && string.Equals(Info.Version, LoadedVersion, StringComparison.OrdinalIgnoreCase))
            Add(requireNewName ? CheckLevel.Error : CheckLevel.Warning,
                $"The map is still called \"{Info.Name} {Info.Version}\", like the original: BAR would treat the two as the same map. Change the name or the version.");

        bool OnMap(double x, double z) => x >= 0 && z >= 0 && x <= MapWidth && z <= MapHeight;
        foreach (var start in Info.StartPositions)
        {
            if (!OnMap(start.X, start.Z))
                Add(CheckLevel.Error, $"Start position {start.Team} is off the map at ({start.X:0}, {start.Z:0}).");
            else if (Heights.HeightAt(start.X, start.Z) < 0)
                Add(CheckLevel.Warning, $"Start position {start.Team} is under water ({-Heights.HeightAt(start.X, start.Z):0} deep).");
            else if (start.X < EdgeMargin || start.Z < EdgeMargin || start.X > MapWidth - EdgeMargin || start.Z > MapHeight - EdgeMargin)
                Add(CheckLevel.Note, $"Start position {start.Team} is within {EdgeMargin:0} units of the map edge.");
        }

        var items = Items.All;
        var off = items.Where(i => !OnMap(i.X, i.Z)).ToList();
        if (off.Count > 0)
            Add(CheckLevel.Error, $"{Describe(off)} off the map: move or remove {(off.Count == 1 ? "it" : "them")}.");
        var wet = items.Where(i => OnMap(i.X, i.Z) && Heights.HeightAt(i.X, i.Z) < 0).ToList();
        if (wet.Count > 0)
            Add(CheckLevel.Note, $"{Describe(wet)} under water.");
        var (definitions, _) = FeatureCatalog.Load(Package.Root, Smf);
        var defined = definitions.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = items.Select(i => i.Name).Where(n => !defined.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
            Add(CheckLevel.Warning, $"Not defined by this map: {string.Join(", ", unknown.Take(8))}{(unknown.Count > 8 ? ", …" : "")}. " +
                                    "They must exist in BAR, or those items will simply be missing in game.");

        if (heights?.HasChanges == true)
        {
            var (_, min, max) = heights.Quantize();
            if (min != heights.MinHeight || max != heights.MaxHeight)
            {
                Add(CheckLevel.Note, $"The height range widens to {min} .. {max} to fit the edits (mapinfo's smf.minheight / maxheight follow where set).");
                var overrides = Info.FindOverrides(Package.Root);
                var setters = MapInfo.Affecting(overrides, "smf.minheight").Concat(MapInfo.Affecting(overrides, "smf.maxheight"))
                    .Select(o => o.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (setters.Count > 0)
                    Add(CheckLevel.Warning, $"{string.Join(", ", setters)} may set smf.minheight / maxheight in game (map options such as a water level). " +
                                            "With such an option on, the game spreads the stored heights over its own range, so widening the range changes " +
                                            "how the whole map looks under that option. Lowering the edits to fit the current range avoids this.");
            }
            Add(CheckLevel.Note, "Saving rotates the normal map and redraws the minimap over the reshaped ground.");
        }

        foreach (var warning in Metal.Warnings)
            Add(CheckLevel.Warning, warning);
        var awkward = Metal.FindSpots().Select(s => (Spot: s, Notes: SpotNotes(s))).Where(p => p.Notes.Count > 0).ToList();
        if (awkward.Count > 0)
            Add(CheckLevel.Warning, $"{awkward.Count} metal spot{(awkward.Count == 1 ? "" : "s")} may not take an extractor: " +
                                    string.Join("; ", awkward.Take(3).Select(p => $"({p.Spot.X:0}, {p.Spot.Z:0}) {p.Notes[0]}")) + (awkward.Count > 3 ? "; …" : ""));

        // Guardian towers: what they would surprise a player with.
        var structures = Structures;
        foreach (var tower in structures.Instances)
        {
            if (!structures.Kinds.TryGetValue(tower.Kind, out var kind) || kind.Type != StructureType.Guardian)
                continue;
            string name = $"The guardian tower {kind.Name} at ({tower.X:0}, {tower.Z:0})";
            if (tower.Zone is not GuardianZone zone)
            {
                Add(CheckLevel.Note, $"{name} guards nothing: draw its path in Items › Select.");
                continue;
            }
            var rules = RulesOf(tower, kind);
            foreach (var region in new[] { GuardianRegion.Band, GuardianRegion.Inside, GuardianRegion.Outside })
            {
                var rule = rules[region];
                if (!rule.Acts || region == GuardianRegion.Inside && !zone.Closed)
                    continue;
                string where = region switch { GuardianRegion.Inside => "inside its path", GuardianRegion.Outside => "outside its path", _ => "crossing its path" };
                var commanders = rule.Targets.Where(n => CommanderName.IsMatch(n)).ToList();
                if (commanders.Count > 0)
                    Add(CheckLevel.Warning, $"{name} destroys commanders {where} ({string.Join(", ", commanders.Take(4))}{(commanders.Count > 4 ? ", …" : "")}): " +
                                            "a player whose commander is caught there loses it, which in most games ends theirs.");
                if (region == GuardianRegion.Outside && rule.AllTeams && rule.DelaySeconds == 0)
                    Add(CheckLevel.Warning, $"{name} destroys {rule.Targets.Count} unit type{(rule.Targets.Count == 1 ? "" : "s")} of every team at once outside its path: " +
                                            "that is the whole map beyond it, other towers' zones and every base included.");
                if (region == GuardianRegion.Band && rule.DelaySeconds == 0)
                    foreach (var start in Info.StartPositions.Where(p => zone.Contains(p.X, p.Z)))
                        Add(CheckLevel.Warning, $"Start position {start.Team} is within the band of {name}: " +
                                                "units it destroys there are doomed as soon as they are finished.");
            }
        }

        return checks;

        static string Describe(List<MapItem> list) =>
            list.Count == 1 ? $"{list[0].Name} at ({list[0].X:0}, {list[0].Z:0}) is" : $"{list.Count} items (e.g. {list[0].Name} at ({list[0].X:0}, {list[0].Z:0})) are";
    }
}
