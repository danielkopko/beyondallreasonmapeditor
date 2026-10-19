using MapCore.Assets;
using MapCore.Editing;
using MapCore.Items;
using MapCore.Lua;
using MapCore.Structures;

namespace MapCore.Tests;

/// <summary>
/// Guardian towers: an indestructible tower that destroys units of the types its kind lists while they are near the
/// path it guards, run against the mock engine (each call as Recoil's source has it).
/// </summary>
static class GuardianTests
{
    const string Kind = "sentinel";
    const double TowerX = 1008, TowerZ = 1008;

    /// <summary>A path far from the tower: east-west at z = 3000, from x = 2000 to 3000; units within 200 of it are in the zone.</summary>
    static readonly GuardianZone Zone = new([(2000, 3000), (3000, 3000)], 200, false);

    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static string Text(MapEditSession s, string entry) => File.ReadAllText(Path.Combine(s.Package.Root, entry.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>A guardian kind destroying Stormbringer bombers (and their Scavengers variant), placed and given <see cref="Zone"/>.</summary>
    static MapEditSession WithGuardian(string name, StructureKindSettings? settings = null, GuardianZone? zone = null, params string[] targets)
    {
        var s = Fresh(name);
        s.AddGuardianKind(Kind, settings ?? StructureKindSettings.Guardian(), targets.Length > 0 ? targets : ["armthund"]);
        s.PlaceStructure(Kind, TowerX, TowerZ, 0);
        s.SetGuardianZone(TowerX, TowerZ, zone ?? Zone);
        return s;
    }

    [Test]
    public static void AGuardianKindWritesItsTowerRocketAndEffects()
    {
        var s = Fresh("guardian-files");
        string result = s.AddGuardianKind(Kind, StructureKindSettings.Guardian(), ["armthund", "armkam", "armthund"]);
        Report.Info(result);
        var kind = s.Structures.Kinds[Kind];
        Assert.True(kind is { Type: StructureType.Guardian, Gap: 3, Depth: 3, Look: StructureLook.Ancient, KillSeconds: 1, RocketSeconds: 2 } &&
                    kind.TargetList.SequenceEqual(["armkam", "armthund"]),
            $"the kind: 3 by 3, ancient, a second to destroy, a rocket every 2 s, its targets once each in order ({string.Join(",", kind.TargetList)})");
        var tower = FeatureDefs.Load(s.Package.Root).SelectMany(f => f.Features).Single(f => f.Name == Kind + "_centre").Table!;
        Assert.True(tower.Get("indestructible") is true && tower.Get("reclaimable") is false && tower.Get("blocking") is true &&
                    LuaValuesRuntime.ToNumber(tower.Get("footprintx")) == 3 && LuaValuesRuntime.ToNumber(tower.Get("footprintz")) == 3,
            "the tower can't be destroyed or reclaimed, and blocks its 3 by 3 squares");
        var model = S3oModel.Load(File.ReadAllBytes(Path.Combine(s.Package.Root, "objects3d", Kind + "_centre.s3o")));
        Assert.True(model?.Texture1 == StructureModels.GuardianTexture1, "the tower is textured from the guardian atlas");
        foreach (var entry in new[] { "unittextures/" + StructureModels.GuardianTexture1, "unittextures/" + StructureModels.GuardianTexture2,
                     StructureFiles.RocketModelEntry, StructureFiles.RocketWeaponEntry, StructureFiles.GuardianEffectsEntry })
            Assert.True(File.Exists(Path.Combine(s.Package.Root, entry.Replace('/', Path.DirectorySeparatorChar))), $"{entry} written");

        // The rocket does no harm at all, and draws the map's rocket model.
        var weapons = new LuaInterpreter().Run(Text(s, StructureFiles.RocketWeaponEntry), "weapon") is [LuaMap w, ..] ? w : null;
        var rocket = weapons?.Get(StructureKind.GuardianRocket) as LuaMap;
        double N(string key) => LuaValuesRuntime.ToNumber(rocket?.Get(key)) ?? double.NaN;
        Assert.True(rocket != null && (rocket.Get("damage") as LuaMap)?.Get("default") is 0.0 && N("areaofeffect") == 0 && N("craterareaofeffect") == 0 &&
                    N("impulsefactor") == 0 && N("cratermult") == 0 && rocket.Get("model") as string == "barmapeditor_guardian_rocket.s3o",
            "the rocket weapon: no damage, area, craters or push, and the map's model");
        var effects = new LuaInterpreter().Run(Text(s, StructureFiles.GuardianEffectsEntry), "effects") is [LuaMap e, ..] ? e : null;
        Assert.True(effects != null && effects.Get("barmapeditor_guardian_burst") is LuaMap && effects.Get("barmapeditor_guardian_hit") is LuaMap,
            "the effects file defines the burst and the hit");

        // Another kind shares them; a door can't look ancient; a guardian can't be from assets.
        s.AddGuardianKind("warden", StructureKindSettings.Guardian() with { Look = StructureLook.Metal }, []);
        Assert.True(s.Structures.Kinds["warden"] is { Look: StructureLook.Metal, TargetList.Count: 0 }, "a metal guardian destroying nothing yet");
        Assert.Throws<InvalidOperationException>(() => s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door) with { Look = StructureLook.Ancient }),
            "a door can't look ancient");
        Assert.Throws<InvalidOperationException>(() => s.AddGuardianKind("bad", StructureKindSettings.Guardian() with { KillSeconds = 0.05 }, []), "a kill time too short is refused");
        Assert.Throws<InvalidOperationException>(() => s.SetGuardianTargets(Kind, ["Arm Thund"]), "a target must be a unit name");
    }

    [Test]
    public static void AGuardiansZoneIsOneUndoStepAndChecked()
    {
        var s = Fresh("guardian-zone");
        s.AddGuardianKind(Kind, StructureKindSettings.Guardian(), ["armthund"]);
        s.PlaceStructure(Kind, TowerX, TowerZ, 0);
        string before = Text(s, StructureConfig.Entry);
        string result = s.SetGuardianZone(TowerX, TowerZ, Zone);
        Report.Info(result);
        var placed = s.Structures.Instances.Single();
        Assert.True(placed.Zone is { Distance: 200, Closed: false } z && z.Points.SequenceEqual(Zone.Points) &&
                    s.UndoDescription == "structure-zone:1008,1008:200:0:2000,3000;3000,3000",
            $"the zone is kept, as one step named by its operation ({s.UndoDescription})");
        s.Undo();
        Assert.Equal(before, Text(s, StructureConfig.Entry), "undo takes it away");
        s.Redo();
        Assert.True(s.Structures.Instances.Single().Zone != null, "redo puts it back");

        string[] bad =
        [
            "structure-zone:1008,1008:200:0:2000,3000",                // one point
            "structure-zone:1008,1008:8:0:2000,3000;3000,3000",         // too narrow
            "structure-zone:1008,1008:200:0:2000,3000;9000,3000",       // off the map
            "structure-zone:1008,1008:200:2:2000,3000;3000,3000",       // closed isn't 0 or 1
            "structure-zone:1008,1008:200:0:2000,3000;2000,3000",       // the same point twice
            "structure-zone:4000,4000:200:0:2000,3000;3000,3000",       // no guardian near
        ];
        foreach (var op in bad)
            Assert.Throws<InvalidOperationException>(() => s.Apply(op), op);
        s.SetGuardianZone(TowerX, TowerZ, null);
        Assert.True(s.Structures.Instances.Single().Zone == null, "'none' clears the zone");
        s.SetGuardianTargets(Kind, ["armthund", "armfig"]);
        Assert.True(s.Structures.Kinds[Kind].TargetList.SequenceEqual(["armfig", "armthund"]) && s.UndoDescription == "structure-targets:sentinel:armfig,armthund",
            "the targets change as one step");
        s.ChangeStructureKind(Kind, StructureKindSettings.Guardian() with { KillSeconds = 3 });
        Assert.True(s.Structures.Kinds[Kind] is { KillSeconds: 3 } k && k.TargetList.Count == 2, "changing the kind's settings keeps its targets");

        // Moving or turning the tower keeps its path where it is (the path isn't tied to the tower).
        s.SetGuardianZone(TowerX, TowerZ, Zone);
        s.MoveStructure(TowerX, TowerZ, TowerX + 64, TowerZ);
        Assert.True(s.Structures.Instances.Single() is { X: TowerX + 64, Zone: GuardianZone moved } && moved.Points.SequenceEqual(Zone.Points), "moved, it keeps its path");
        s.TurnStructure(TowerX + 64, TowerZ, 1);
        Assert.True(s.Structures.Instances.Single() is { Facing: 1, Zone: GuardianZone turned } && turned.Points.SequenceEqual(Zone.Points), "turned, it keeps its path");
        s.Undo();
        s.Undo();
        Assert.True(s.Structures.Instances.Single() is { X: TowerX, Facing: 0, Zone: GuardianZone back } && back.Points.SequenceEqual(Zone.Points), "and two undos put it back with it");
    }

    /// <summary>
    /// Units of a listed type within the zone lose a fifth of their health every 6 frames and die on the 5th tick (a
    /// second); anything else in the zone, and listed units outside it, being built, or carried, are left alone.
    /// </summary>
    [Test]
    public static void AGuardianDestroysListedUnitsInItsZoneOnly()
    {
        var s = WithGuardian("guardian-kill");
        var engine = new MockEngine(s);
        engine.Load();
        Assert.True(engine.Named(Kind + "_centre").Single().F is { X: TowerX, Z: TowerZ }, "the tower stands where it was placed");
        int inside = engine.AddUnit(MockEngine.Bomber, 2500, 3150, 300);
        int scav = engine.AddUnit(MockEngine.BomberScav, 2100, 2900, 300);
        int outside = engine.AddUnit(MockEngine.Bomber, 2500, 3250, 300);         // 250 from the path
        int beyondEnd = engine.AddUnit(MockEngine.Bomber, 3150, 3000, 300);       // 150 past its end: inside
        int fighter = engine.AddUnit(MockEngine.Fighter, 2500, 3000, 300);
        int transport = engine.AddUnit(MockEngine.Transport, 2400, 3000, 200);
        int carried = engine.AddUnit(MockEngine.Bomber, 2400, 3000, 190);
        engine.Units[carried].Transporter = transport;
        int nanoframe = engine.AddUnit(MockEngine.Bomber, 2600, 3000, 0);
        engine.Units[nanoframe].BeingBuilt = true;

        engine.Frames(24);
        Assert.True(engine.Units[inside].Health == 200 && engine.Units[scav].Health == 200 && engine.Units[beyondEnd].Health == 200,
            $"after 4 ticks the listed units in the zone are down to a fifth ({engine.Units[inside].Health})");
        engine.Frames(6);
        Assert.True(!engine.Units.ContainsKey(inside) && !engine.Units.ContainsKey(scav) && !engine.Units.ContainsKey(beyondEnd),
            "on the 5th tick (a second) they are destroyed, the Scavengers variant too");
        Assert.True(engine.Units[outside].Health == 1000 && engine.Units[fighter].Health == 1000 && engine.Units[transport].Health == 1000 &&
                    engine.Units[carried].Health == 1000 && engine.Units[nanoframe].Health == 1000,
            "a bomber outside the zone, a fighter, a transport, a bomber it carries and a bomber being built are untouched");
        Assert.True(engine.Effects.Count(e => e.Name == "barmapeditor_guardian_hit") == 15, "each strike shows a hit (3 units, 5 ticks)");
        Assert.True(!engine.Echoes.Any(e => e.Contains("doesn't have", StringComparison.Ordinal)), "every listed name is known");
    }

    /// <summary>
    /// Entering is death: a unit that comes into the zone dies on time wherever it goes next, healed, repaired in full
    /// or picked up by a transport; and its doom isn't handed on with its ID when something else destroys it first.
    /// </summary>
    [Test]
    public static void EnteringTheZoneIsDeath()
    {
        var s = WithGuardian("guardian-doom", StructureKindSettings.Guardian() with { KillSeconds = 0.5 });
        var engine = new MockEngine(s);
        engine.Load();
        int healer = engine.AddUnit(MockEngine.Bomber, 2500, 3000, 300);
        int leaver = engine.AddUnit(MockEngine.Bomber, 2200, 3000, 300);
        int lifted = engine.AddUnit(MockEngine.Bomber, 2800, 3000, 300);
        int killed = engine.AddUnit(MockEngine.Bomber, 2600, 3000, 300);
        foreach (int id in new[] { healer, leaver, lifted, killed })
            engine.Units[id].MaxHealth = engine.Units[id].Health = 1530;
        int transport = engine.AddUnit(MockEngine.Transport, 600, 600, 300);
        for (int frame = 1; frame <= 18; frame++)
        {
            engine.Frames(1);
            if (engine.Units.TryGetValue(healer, out var u))
                u.Health = Math.Min(u.MaxHealth, u.Health + 50);      // a fast repair, every frame
            if (frame >= 7 && engine.Units.TryGetValue(leaver, out var l))
                (l.X, l.Z, l.Health) = (500, 500, l.MaxHealth);       // gone far away after the 1st tick, and kept repaired in full
            if (frame == 7)
            {
                (engine.Units[lifted].X, engine.Units[lifted].Z) = (600, 600);
                engine.Units[lifted].Transporter = transport;          // carried off by a transport
                engine.KillUnit(killed);                               // destroyed by something else, its ID given to a fighter
                engine.AddUnit(MockEngine.Fighter, 600, 900, 300, reuse: killed);
            }
            if (frame == 12)
                Assert.True(engine.Units[leaver].Health == 1530, "(the leaver is at full health just before the 2nd tick)");
        }
        // Ticks at frames 6, 12, 18: 0.4 of 1530 a tick, so each is destroyed on the 3rd (less than a quarter share left).
        // Counting its repairs the healer would still have 294 then, and the leaver 918.
        Assert.True(!engine.Units.ContainsKey(healer), "repaired every frame, it still dies on time (0.5 s: the 3rd tick)");
        Assert.True(!engine.Units.ContainsKey(leaver), "it left the zone and was repaired in full, and still died on time");
        Assert.True(!engine.Units.ContainsKey(lifted) && engine.Units[transport].Health == 1000, "carried off, it still died; the transport is untouched");
        Assert.True(engine.Units[killed].Health == 1000, "the fighter given a doomed unit's ID is untouched");
    }

    /// <summary>
    /// The zone is checked along the way each unit came since the last check, so one crossing a narrow zone between
    /// checks is caught; one passing alongside it isn't.
    /// </summary>
    [Test]
    public static void AFastPlaneCantSlipAcrossBetweenChecks()
    {
        var s = WithGuardian("guardian-fast", zone: new GuardianZone([(2000, 3000), (3000, 3000)], 16, false));
        var engine = new MockEngine(s);
        engine.Load();
        int crosser = engine.AddUnit(MockEngine.Bomber, 2500, 3100, 300);
        int alongside = engine.AddUnit(MockEngine.Bomber, 2200, 3040, 300);
        int pastTheEnd = engine.AddUnit(MockEngine.Bomber, 3100, 3100, 300);
        engine.Frames(6);
        // 200 in 6 frames (33 a frame) across a zone 32 wide; alongside, 24 off the path; past the end, 100 beyond it.
        (engine.Units[crosser].Z, engine.Units[alongside].X, engine.Units[pastTheEnd].Z) = (2900, 2400, 2900);
        engine.Frames(6);
        Assert.True(engine.Units[crosser].Health == 800, "the crosser was caught: down a fifth on the next check, though it is outside both times");
        Assert.True(engine.Units[alongside].Health == 1000 && engine.Units[pastTheEnd].Health == 1000, "those that never came within 16 are untouched");
        engine.Frames(24);
        Assert.True(!engine.Units.ContainsKey(crosser), "and it dies a second after crossing");
    }

    [Test]
    public static void OneOrFiveHundredDieOnTheSameTick()
    {
        foreach (int count in new[] { 1, 500 })
        {
            var s = WithGuardian("guardian-many-" + count);
            var engine = new MockEngine(s);
            engine.Load();
            var ids = Enumerable.Range(0, count).Select(i => engine.AddUnit(MockEngine.Bomber, 2000 + i * 2 % 1000, 3000 + (i % 7) * 20 - 60, 300)).ToList();
            engine.Frames(24);
            Assert.True(ids.All(engine.Units.ContainsKey), $"{count}: all alive after 4 ticks");
            engine.Frames(6);
            Assert.True(!ids.Any(engine.Units.ContainsKey), $"{count}: all destroyed on the 5th tick");
            int mostHits = engine.Effects.Where(e => e.Name == "barmapeditor_guardian_hit").GroupBy(e => e.Frame).Max(g => g.Count());
            Assert.True(mostHits == Math.Min(count, 16), $"{count}: at most 16 hit flashes a tick ({mostHits})");
        }
    }

    [Test]
    public static void TheZonesEdgeIsWhereItSays()
    {
        // An open path: along z = 3000; a closed one: a square, its closing run back down x = 2000.
        var closed = new GuardianZone([(2000, 2000), (3000, 2000), (3000, 3000), (2000, 3000)], 200, true);
        (string Name, GuardianZone Zone, (double X, double Z) In, (double X, double Z) Out)[] cases =
        [
            ("open", Zone, (2500.0, 3199.5), (2500.0, 3200.5)),
            ("closed", closed, (1800.5, 2500.0), (1799.5, 2500.0)),
            ("open's end", Zone, (3199.5, 3000.0), (3200.5, 3000.0)),
        ];
        foreach (var (name, zone, inPoint, outPoint) in cases)
        {
            var s = WithGuardian("guardian-edge-" + name.Replace("'", "").Replace(' ', '-'), zone: zone);
            var engine = new MockEngine(s);
            engine.Load();
            int a = engine.AddUnit(MockEngine.Bomber, inPoint.X, inPoint.Z, 300), b = engine.AddUnit(MockEngine.Bomber, outPoint.X, outPoint.Z, 300);
            engine.Frames(30);
            Assert.True(!engine.Units.ContainsKey(a) && engine.Units[b].Health == 1000, $"{name}: half a unit inside dies, half a unit outside lives");
            Assert.True(zone.Contains(inPoint.X, inPoint.Z) && !zone.Contains(outPoint.X, outPoint.Z), $"{name}: the editor's zone agrees");
        }
    }

    /// <summary>
    /// While anything is being worn down the tower fires one rocket every 2 seconds, from a pod, up to burst high above
    /// itself 45 frames later; none while nothing is.
    /// </summary>
    [Test]
    public static void RocketsGoUpAndBurstOverTheTower()
    {
        var s = WithGuardian("guardian-rockets", StructureKindSettings.Guardian() with { KillSeconds = 20 });
        var engine = new MockEngine(s);
        engine.Load();
        engine.Frames(90);
        Assert.True(engine.Projectiles.Count == 0 && engine.DeletedProjectiles.Count == 0, "no rockets while nothing is in the zone");
        for (int i = 0; i < 50; i++)
            engine.AddUnit(MockEngine.Bomber, 2000 + i * 20, 3050, 300);
        engine.Frames(180);
        var bursts = engine.Effects.Where(e => e.Name == "barmapeditor_guardian_burst").ToList();
        Assert.True(engine.DeletedProjectiles.Count == 3 && bursts.Count == 3,
            $"50 targets for 6 seconds: one rocket every 2 seconds, 3 in all, each burst ({engine.DeletedProjectiles.Count} rockets, {bursts.Count} bursts)");
        Assert.True(bursts.All(b => Math.Abs(b.X - TowerX) < 60 && Math.Abs(b.Z - TowerZ) < 60 && b.Y > 500),
            $"each bursts high over the tower ({string.Join("; ", bursts.Select(b => $"{b.X:0},{b.Y:0},{b.Z:0}"))})");
        int frames = bursts[1].Frame - bursts[0].Frame;
        Assert.Equal(60, frames, "frames between rockets");
    }

    /// <summary>
    /// Saving warns of a guardian that destroys commanders (naming the region), whose band covers a start position, or
    /// that destroys anything of every team at once outside its path (the whole map); a tower guarding nothing is a note.
    /// </summary>
    [Test]
    public static void SavingWarnsOfWhatAGuardianWouldSurprise()
    {
        var s = Fresh("guardian-checks");
        s.AddGuardianKind(Kind, StructureKindSettings.Guardian(), ["armthund", "armcom", "legcomlvl3"]);
        s.PlaceStructure(Kind, TowerX, TowerZ, 0);
        var checks = s.CheckBeforeSave(checkName: false);
        Assert.True(checks.Any(c => c is { Level: CheckLevel.Note } && c.Message.Contains("guards nothing", StringComparison.Ordinal)), "a tower without a path is a note");
        var start = s.Info.StartPositions[0];
        s.SetGuardianZone(TowerX, TowerZ, new GuardianZone([(start.X - 300, start.Z), (start.X + 300, start.Z)], 100, false));
        checks = s.CheckBeforeSave(checkName: false);
        string all = string.Join(" | ", checks.Select(c => c.Message));
        Assert.True(checks.Any(c => c is { Level: CheckLevel.Warning } && c.Message.Contains("destroys commanders crossing its path (armcom, legcomlvl3)", StringComparison.Ordinal)),
            $"listing commanders is a warning, naming the region ({all})");
        Assert.True(checks.Any(c => c is { Level: CheckLevel.Warning } && c.Message.StartsWith($"Start position {start.Team} is within the band", StringComparison.Ordinal)),
            $"a start position in a band is a warning ({all})");
        Assert.True(!checks.Any(c => c.Message.Contains("guards nothing", StringComparison.Ordinal)), "and with a path, no note");
        s.SetGuardianRule(TowerX, TowerZ, GuardianRegion.Outside, new GuardianRule(["armfig"]));
        Assert.True(s.CheckBeforeSave(checkName: false).Any(c => c is { Level: CheckLevel.Warning } && c.Message.Contains("of every team at once outside its path", StringComparison.Ordinal)),
            "destroying anything of every team at once outside its path is a warning");
        s.SetGuardianRule(TowerX, TowerZ, GuardianRegion.Outside, new GuardianRule(["armfig"], [2, 3], 30));
        Assert.True(!s.CheckBeforeSave(checkName: false).Any(c => c.Message.Contains("outside its path", StringComparison.Ordinal)), "but not for some teams, after a delay");
    }

    /// <summary>
    /// A tower's rules: a new tower takes those of its kind's last placed tower (or its kind's targets in the band);
    /// structure-rule sets one region's (checked, one undo step, kept in the config); moving and turning keep them.
    /// </summary>
    [Test]
    public static void EachTowerHasItsOwnRulesForEachRegion()
    {
        var s = WithGuardian("guardian-rules");
        var first = s.Structures.Instances.Single();
        Assert.True(first.Rules is { } r0 && r0.Band.Targets.SequenceEqual(["armthund"]) && r0.Band.AllTeams && r0.Band.DelaySeconds == 0 && !r0.Inside.Acts && !r0.Outside.Acts,
            "the first tower: its kind's targets crossing its path, of every team, at once; nothing inside or outside");
        var inside = new GuardianRule(["armfig", "corvamp"], [2, 3, 4], 30);
        string result = s.SetGuardianRule(TowerX, TowerZ, GuardianRegion.Inside, inside);
        Report.Info(result);
        Assert.True(s.Structures.Instances.Single().Rules!.Inside.Same(inside) && s.UndoDescription == "structure-rule:1008,1008:inside:30:2,3,4:armfig,corvamp" &&
                    result.Contains("of teams 2, 3, 4 inside its path after 30 s there", StringComparison.Ordinal),
            $"one region's rule is one step, named by its operation ({s.UndoDescription})");
        var reread = StructureConfig.Parse(File.ReadAllText(Path.Combine(s.Package.Root, StructureConfig.Entry.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(reread.Instances.Single().Rules is { } back && back.Inside.Same(inside) && back.Band.Same(first.Rules!.Band) && back.Outside.Same(GuardianRule.None),
            "the config keeps it as written");
        s.Undo();
        Assert.True(s.Structures.Instances.Single().Rules!.Inside.Same(GuardianRule.None), "undo takes it away");
        s.Redo();

        // A second tower takes the first's rules; a third, the second's after it is changed.
        s.PlaceStructure(Kind, 3008, 1008, 0);
        Assert.True(s.Structures.Instances[1].Rules!.Inside.Same(inside), "a new tower takes the rules of its kind's last placed tower");
        s.SetGuardianRule(3008, 1008, GuardianRegion.Outside, new GuardianRule(["armpeep"], null, 5));
        s.PlaceStructure(Kind, 3008, 3008, 0);
        Assert.True(s.Structures.Instances[2].Rules!.Outside.Same(new GuardianRule(["armpeep"], null, 5)) && s.Structures.Instances[0].Rules!.Outside.Same(GuardianRule.None),
            "and the next, the last one's as changed; the first keeps its own");
        s.MoveStructure(3008, 3008, 3072, 3008);
        s.TurnStructure(3072, 3008, 2);
        Assert.True(s.Structures.Instances[2] is { X: 3072, Facing: 2 } moved && moved.Rules!.Outside.DelaySeconds == 5, "moved and turned, it keeps them");

        string[] bad =
        [
            "structure-rule:1008,1008:middle:0:all:armfig",          // no such region
            "structure-rule:1008,1008:band:-1:all:armfig",           // delay below 0
            "structure-rule:1008,1008:band:3601:all:armfig",         // delay over an hour
            "structure-rule:1008,1008:band:0:0:armfig",              // team 0
            "structure-rule:1008,1008:band:0:9:armfig",              // team 9
            "structure-rule:1008,1008:band:0:2,x:armfig",            // not a team
            "structure-rule:1008,1008:band:0:all:Arm Fig",           // not a unit name
            "structure-rule:1008,1008:band:0:all",                   // too short
            "structure-rule:1008,1008:inside:30:2,3,4:armfig,corvamp", // already so
        ];
        foreach (var op in bad)
            Assert.Throws<InvalidOperationException>(() => s.Apply(op), op);
        Assert.True(s.Apply("structure-rule:1008,1008:band:0:none:armthund").Contains("destroys nothing crossing its path", StringComparison.Ordinal),
            "no teams: it destroys nothing there");
    }

    /// <summary>A guardian on a closed square, 100 either side, its band destroying nothing: the inside and outside rules alone act.</summary>
    static readonly GuardianZone Square = new([(2000, 2000), (3000, 2000), (3000, 3000), (2000, 3000)], 100, true);

    static MockEngine Regions(string name, GuardianZone zone, GuardianRule? inside = null, GuardianRule? outside = null, GuardianRule? band = null,
        StructureKindSettings? settings = null)
    {
        var s = WithGuardian(name, settings, zone);
        s.SetGuardianRule(TowerX, TowerZ, GuardianRegion.Band, band ?? GuardianRule.None);
        if (inside != null)
            s.SetGuardianRule(TowerX, TowerZ, GuardianRegion.Inside, inside);
        if (outside != null)
            s.SetGuardianRule(TowerX, TowerZ, GuardianRegion.Outside, outside);
        var engine = new MockEngine(s);
        engine.Load();
        return engine;
    }

    static int Fighter(MockEngine engine, double x, double z, int lobbyTeam)
    {
        int id = engine.AddUnit(MockEngine.Fighter, x, z, 300);
        engine.Units[id].AllyTeam = lobbyTeam - 1;
        return id;
    }

    /// <summary>
    /// Inside, for lobby teams 2 to 4 after 30 s: a team 2 fighter is doomed exactly 900 frames after it is first seen
    /// there, and dies a second later; a team 1 fighter beside it is untouched; one that leaves at 29 s starts again from
    /// nothing when it comes back.
    /// </summary>
    [Test]
    public static void InsideAfterADelayForSomeTeams()
    {
        var engine = Regions("guardian-inside", Square, inside: new GuardianRule(["armfig"], [2, 3, 4], 30));
        int enemy = Fighter(engine, 2500, 2500, 2), ally = Fighter(engine, 2520, 2500, 1), leaver = Fighter(engine, 2540, 2500, 3);
        engine.Frames(871); // first seen at frame 6; frame 870 is 864 frames (28.8 s) later
        engine.Units[leaver].X = 4000; // out for the tick at 876
        engine.Frames(9);
        engine.Units[leaver].X = 2540; // back: seen again at 882
        engine.Frames(25);
        Assert.True(engine.Units[enemy].Health == 1000, "at frame 905, not yet");
        engine.Frames(1);
        Assert.True(engine.Units[enemy].Health == 800, $"at frame 906, 900 frames (30 s) after it was first seen there, doomed and struck ({engine.Units[enemy].Health})");
        engine.Frames(24);
        Assert.True(!engine.Units.ContainsKey(enemy) && engine.Units[ally].Health == 1000, "a second later it is gone; the team 1 fighter beside it is untouched");
        engine.Frames(1781 - 930);
        Assert.True(engine.Units[leaver].Health == 1000, "the one that left at 29 s isn't doomed 30 s after it first came");
        engine.Frames(1);
        Assert.True(engine.Units[leaver].Health == 800, "but 30 s after it came back (frame 1782)");
    }

    /// <summary>
    /// Outside, for every team (Scavengers too) at once: a fighter far across the map, or in another tower's zone, is
    /// doomed; one inside the path or in its band isn't. A rule for lobby teams 1 to 8 leaves a Scavenger alone.
    /// </summary>
    [Test]
    public static void OutsideIsTheRestOfTheMap()
    {
        var engine = Regions("guardian-outside", Square, outside: new GuardianRule(["armfig"]));
        int far = Fighter(engine, 300, 5800, 1), scav = Fighter(engine, 5800, 300, 1), inBand = Fighter(engine, 2050, 2500, 1), inside = Fighter(engine, 2500, 2500, 1);
        engine.Units[scav].AllyTeam = 12;
        engine.Frames(6);
        Assert.True(engine.Units[far].Health == 800 && engine.Units[scav].Health == 800, "a fighter across the map and a Scavenger one are doomed at once");
        Assert.True(engine.Units[inBand].Health == 1000 && engine.Units[inside].Health == 1000, "in the band and inside the path, untouched");

        var numbered = Regions("guardian-outside-teams", Square, outside: new GuardianRule(["armfig"], [1, 2, 3, 4, 5, 6, 7, 8]));
        int scavenger = Fighter(numbered, 300, 5800, 1);
        numbered.Units[scavenger].AllyTeam = 12;
        numbered.Frames(30);
        Assert.True(numbered.Units[scavenger].Health == 1000, "teams 1 to 8 don't take in a Scavenger (ally team 12)");

        // Another tower's zone is outside this one's path.
        var s = WithGuardian("guardian-outside-two", zone: Square);
        s.SetGuardianRule(TowerX, TowerZ, GuardianRegion.Outside, new GuardianRule(["armfig"]));
        s.PlaceStructure(Kind, 4000, 4000, 0);
        s.SetGuardianZone(4000, 4000, new GuardianZone([(3600, 4600), (4600, 4600)], 200, false));
        s.SetGuardianRule(4000, 4000, GuardianRegion.Outside, GuardianRule.None);
        var two = new MockEngine(s);
        two.Load();
        int inOther = Fighter(two, 4100, 4600, 1);
        two.Frames(6);
        Assert.True(two.Units[inOther].Health == 800, "a fighter in another tower's band is outside this one's path: doomed");
    }

    /// <summary>
    /// The band wins: a fighter inside for 18 frames (of a 30-frame delay) that then sits in the band for a tick starts
    /// again from nothing when it is back inside, and the band's own rule decides there.
    /// </summary>
    [Test]
    public static void TheBandWinsAndLeavingItResetsTheCount()
    {
        var engine = Regions("guardian-band-wins", Square, inside: new GuardianRule(["armfig"], null, 1));
        int f = Fighter(engine, 2500, 2500, 1);
        engine.Frames(25); // seen at 6, 12, 18, 24: 18 frames inside
        engine.Units[f].X = 2050; // in the band (50 from the west side) for the tick at 30
        engine.Frames(6);
        Assert.True(engine.Units[f].Health == 1000, "in the band, whose rule destroys nothing, untouched");
        engine.Units[f].X = 2500; // back inside: seen at 36
        engine.Frames(30);
        Assert.True(engine.Units[f].Health == 1000, "30 frames after it first came in, but only 24 since it came back: not yet");
        engine.Frames(6);
        Assert.True(engine.Units[f].Health == 800, "30 frames after it came back (frame 66), doomed");
        Assert.True(Square.RegionOf(2050, 2500) == GuardianRegion.Band && Square.RegionOf(2500, 2500) == GuardianRegion.Inside && Square.RegionOf(1850, 2500) == GuardianRegion.Outside,
            "the editor's regions agree");
    }

    /// <summary>
    /// A tower written before towers had rules (no rules table, as the editor's noon build of 2026-10-16 writes them)
    /// destroys its kind's targets crossing its path, of every team, at once, and nothing inside or outside, in the
    /// editor and the gadget alike; setting a rule on it writes all three.
    /// </summary>
    [Test]
    public static void ATowerFromBeforeRulesDestroysItsKindsTargetsCrossingItsPath()
    {
        var s = WithGuardian("guardian-before-rules", zone: Square);
        string path = Path.Combine(s.Package.Root, StructureConfig.Entry.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(path);
        string old = System.Text.RegularExpressions.Regex.Replace(text, @",\n\t\t\trules = \{.*?\n\t\t\t\}\n\t\t", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(old != text && !old.Contains("rules =", StringComparison.Ordinal), "the rules table taken out, as an older build wrote it");
        File.WriteAllText(path, old);
        var tower = s.Structures.Instances.Single();
        Assert.True(tower.Rules == null && MapEditSession.RulesOf(tower, s.Structures.Kinds[Kind]) is { } rules && rules.Band.Targets.SequenceEqual(["armthund"]) &&
                    rules.Band is { AllTeams: true, DelaySeconds: 0 } && !rules.Inside.Acts && !rules.Outside.Acts,
            "read back without rules, it acts as its kind's targets crossing its path, of every team, at once");
        var engine = new MockEngine(s);
        engine.Load();
        int bomber = engine.AddUnit(MockEngine.Bomber, 2050, 2500, 300), fighter = Fighter(engine, 2500, 2500, 2);
        engine.Frames(30);
        Assert.True(!engine.Units.ContainsKey(bomber) && engine.Units[fighter].Health == 1000, "in game too: the bomber in the band dies on the 5th tick, the fighter inside is untouched");
        s.SetGuardianRule(TowerX, TowerZ, GuardianRegion.Inside, new GuardianRule(["armfig"]));
        Assert.True(s.Structures.Instances.Single().Rules is { } written && written.Band.Targets.SequenceEqual(["armthund"]) && written.Inside.Targets.SequenceEqual(["armfig"]),
            "a rule set on it writes all three, the band as it acted");
    }

    /// <summary>A band with a delay lets a bomber cross it quickly, and dooms one that stays the delay.</summary>
    [Test]
    public static void ABandWithADelayLetsAQuickCrossingPass()
    {
        var engine = Regions("guardian-band-delay", Square, band: new GuardianRule(["armthund"], null, 1));
        int stays = engine.AddUnit(MockEngine.Bomber, 2050, 2500, 300), crosses = engine.AddUnit(MockEngine.Bomber, 2050, 2600, 300);
        engine.Frames(7); // both seen in the band at 6
        engine.Units[crosses].X = 2500; // through it, inside, by the tick at 12
        engine.Frames(29);
        Assert.True(engine.Units[stays].Health == 800 && engine.Units[crosses].Health == 1000,
            "the one in the band 30 frames is doomed (frame 36); the one that crossed in a tick is untouched");
    }

    /// <summary>An open path has no inside: an inside rule does nothing there.</summary>
    [Test]
    public static void AnOpenPathHasNoInside()
    {
        var open = Square with { Closed = false };
        var engine = Regions("guardian-open-inside", open, inside: new GuardianRule(["armfig"]));
        int f = Fighter(engine, 2500, 2500, 1);
        engine.Frames(60);
        Assert.True(engine.Units[f].Health == 1000 && open.RegionOf(2500, 2500) == GuardianRegion.Outside, "nothing happens where the inside would be; the editor calls it outside");
    }

    /// <summary>
    /// A path that crosses itself (a bow tie) has each loop inside and the rest outside, by the even-odd rule, in the
    /// gadget as in the editor.
    /// </summary>
    [Test]
    public static void ABowTiePathHasTwoInsides()
    {
        var bowTie = new GuardianZone([(2000, 2000), (3000, 3000), (3000, 2000), (2000, 3000)], 100, true);
        var engine = Regions("guardian-bow-tie", bowTie, inside: new GuardianRule(["armfig"]), outside: new GuardianRule(["armatlas"]));
        (double X, double Z)[] lobes = [(2850, 2500), (2150, 2500)], between = [(2500, 2150), (2500, 2850)];
        var fighters = lobes.Concat(between).Select(p => Fighter(engine, p.X, p.Z, 1)).ToList();
        var transports = lobes.Concat(between).Select(p => engine.AddUnit(MockEngine.Transport, p.X, p.Z, 300)).ToList();
        engine.Frames(6);
        for (int i = 0; i < 4; i++)
        {
            bool lobe = i < 2;
            var (x, z) = i < 2 ? lobes[i] : between[i - 2];
            Assert.True(engine.Units[fighters[i]].Health == (lobe ? 800 : 1000) && engine.Units[transports[i]].Health == (lobe ? 1000 : 800),
                $"({x}, {z}): {(lobe ? "a loop, inside: the fighter doomed" : "between the loops, outside: the transport doomed")}");
            Assert.True(bowTie.RegionOf(x, z) == (lobe ? GuardianRegion.Inside : GuardianRegion.Outside), $"({x}, {z}): the editor agrees");
        }
    }

    /// <summary>
    /// A unit doomed inside still dies after it leaves; a unit given the ID of one that was counting inside starts from
    /// nothing; a tower doing the dooming fires at what it is wearing down.
    /// </summary>
    [Test]
    public static void DoomCountsAndRocketsFollowTheUnitNotTheID()
    {
        var engine = Regions("guardian-inside-doom", Square, inside: new GuardianRule(["armfig"], null, 1));
        int leaver = Fighter(engine, 2850, 2150, 1), counting = Fighter(engine, 2800, 2200, 1); // both further east of the tower than south
        engine.Frames(36); // seen at 6; doomed at 36
        Assert.True(engine.Units[leaver].Health == 800, "doomed 30 frames after it came in");
        engine.Units[leaver].X = 5800; // far east, outside
        engine.Frames(24);
        Assert.True(!engine.Units.ContainsKey(leaver), "it left, and still died a second after it was doomed");
        Assert.True(engine.Projectiles.Values.Any(p => p.SpawnFrame == 36 && p.Pos.X > TowerX), "the tower fired at it as it was doomed, from its east pod (it was east)");

        var again = Regions("guardian-recycled", Square, inside: new GuardianRule(["armfig"], null, 1));
        int first = Fighter(again, 2500, 2500, 1);
        again.Frames(25); // seen at 6, 12, 18, 24: 18 frames inside
        again.KillUnit(first);
        int reused = again.AddUnit(MockEngine.Fighter, 2500, 2500, 300, reuse: first);
        again.Frames(34);
        Assert.True(again.Units[reused].Health == 1000, "the new unit with its ID isn't doomed at frame 59");
        again.Frames(1);
        Assert.True(again.Units[reused].Health == 800, "but 30 frames after it was first seen (frame 60)");
    }

    [Test]
    public static void UnknownTargetsAreReportedAndATowerIsTakenBackAfterAReload()
    {
        var s = WithGuardian("guardian-reload", targets: ["armthund", "notaunit"]);
        var engine = new MockEngine(s);
        engine.Load();
        Assert.True(engine.Echoes.Count(e => e.Contains("notaunit", StringComparison.Ordinal)) == 1, "an unknown name is reported once, not fatal");
        int tower = engine.Named(Kind + "_centre").Single().Id;
        engine.Frames(10);
        // A reload (/luarules reload): the gadget loads again with the game running.
        engine.Load();
        Assert.True(engine.Named(Kind + "_centre").Single().Id == tower, "after a reload the tower is taken back, not made again");
        int bomber = engine.AddUnit(MockEngine.Bomber, 2500, 3000, 300);
        engine.Frames(30);
        Assert.True(!engine.Units.ContainsKey(bomber), "and it still guards its zone");
    }
}
