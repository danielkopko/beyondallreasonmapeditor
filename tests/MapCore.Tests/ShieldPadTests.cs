using MapCore.Assets;
using MapCore.Editing;
using MapCore.Items;
using MapCore.Lua;
using MapCore.Structures;

namespace MapCore.Tests;

/// <summary>
/// The structures gadget's drawing side, run without BAR: a game side's messages in, DrawWorld out, gl recorded. Every
/// engine call is as Recoil's source has it (checked 2026-10-16); any other is a nil and fails.
/// </summary>
sealed class MockDrawSide
{
    readonly LuaInterpreter lua = new(new LuaLimits(MaxSteps: 100_000_000));
    readonly LuaMap gadget = new();
    public readonly List<string> Calls = [];
    public readonly HashSet<int> InLos = [];
    public readonly Dictionary<int, (double X, double Y, double Z)> Positions = [];
    public bool FullView;
    public int MyAllyTeam;
    public int Vertices;

    public MockDrawSide(string gadgetFile)
    {
        var g = lua.Globals;
        static double Num(object?[] a, int i) => LuaValuesRuntime.ToNumber(i < a.Length ? a[i] : null) ?? throw new LuaRuntimeException($"argument {i + 1}: a number expected");
        var spring = new LuaMap();
        void F(LuaMap t, string name, Func<object?[], object?[]> body) => t.Set(name, LuaInterpreter.Function(name, body));
        F(spring, "GetSpectatingState", _ => [FullView, FullView, false]);
        F(spring, "GetMyAllyTeamID", _ => [(double)MyAllyTeam]);
        F(spring, "IsUnitInLos", a => [InLos.Contains((int)Num(a, 0)) && (int)Num(a, 1) == MyAllyTeam]);
        F(spring, "IsUnitInView", a => [Positions.ContainsKey((int)Num(a, 0))]);
        F(spring, "GetUnitViewPosition", a => Positions.TryGetValue((int)Num(a, 0), out var p) ? [p.X, p.Y, p.Z] : []);
        g.Set("Spring", spring);
        var gl = new LuaMap();
        int lists = 0;
        F(gl, "CreateList", a =>
        {
            lua.Invoke((LuaFunction)a[0]!);
            return [(double)++lists];
        });
        F(gl, "BeginEnd", a =>
        {
            lua.Invoke((LuaFunction)a[1]!);
            return [];
        });
        F(gl, "Vertex", a =>
        {
            double x = Num(a, 0), y = Num(a, 1), z = Num(a, 2);
            if (Math.Abs(x * x + y * y + z * z - 1) > 1e-9)
                throw new LuaRuntimeException("a vertex off the unit sphere");
            Vertices++;
            return [];
        });
        foreach (var name in new[] { "DepthTest", "DepthMask", "Blending", "Culling", "Color", "PushMatrix", "PopMatrix", "CallList", "DeleteList" })
            F(gl, name, a =>
            {
                Calls.Add(name);
                return [];
            });
        F(gl, "Translate", a =>
        {
            Calls.Add(FormattableString.Invariant($"Translate {Num(a, 0)} {Num(a, 1)} {Num(a, 2)}"));
            return [];
        });
        F(gl, "Scale", a =>
        {
            Calls.Add(FormattableString.Invariant($"Scale {Num(a, 0):0.###}"));
            return [];
        });
        g.Set("gl", gl);
        var constants = new LuaMap();
        foreach (var (name, value) in new[] { ("TRIANGLES", 4.0), ("LINES", 1.0), ("SRC_ALPHA", 770.0), ("ONE", 1.0), ("ONE_MINUS_SRC_ALPHA", 771.0), ("BACK", 1029.0) })
            constants.Set(name, value);
        g.Set("GL", constants);
        var handler = new LuaMap();
        handler.Set("IsSyncedCode", LuaInterpreter.Function("IsSyncedCode", _ => [false]));
        g.Set("gadgetHandler", handler);
        g.Set("gadget", gadget);
        lua.Run(File.ReadAllText(gadgetFile), "map_structures.lua");
    }

    /// <summary>What the game side sent (SendToUnsynced), as base gadgets.lua hands it to the gadget: consumed or not.</summary>
    public bool Receive(object?[] message) => lua.Invoke(gadget.Get("RecvFromSynced"), [gadget, .. message]) is [true, ..];

    public void Draw()
    {
        Calls.Clear();
        lua.Invoke(gadget.Get("DrawWorld"), gadget);
    }
}

/// <summary>
/// Shield platforms: a derelict pad whose repairer owns it; its owner's and allies' units get a bubble shield as they
/// drive off it, paid in energy for their metal cost; the bubble takes hits for its unit and allies within it.
/// </summary>
static class ShieldPadTests
{
    const string Kind = "aegis";
    const double PadX = 3008, PadZ = 3008; // a 6 by 6 square pad: 2960 to 3056 each way

    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static string PathOf(MapEditSession s, string entry) => Path.Combine(s.Package.Root, entry.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A map with a shield platform repaired by team 2 (allied with team 4; team 3 an enemy), every team rich.</summary>
    static (MapEditSession S, MockEngine Engine) Repaired(string name, StructureKindSettings? settings = null)
    {
        var s = Fresh(name);
        s.AddStructureKind(Kind, StructureType.ShieldPad, settings ?? StructureKindSettings.ShieldPad());
        s.PlaceStructure(Kind, PadX, PadZ, 0);
        var engine = new MockEngine(s);
        foreach (int team in new[] { 2, 3, 4 })
            engine.Teams[team] = (100_000, 1_000_000);
        engine.AllyOf[4] = 2;
        engine.Load();
        engine.Reclaim(100, (500, 2, engine.Named(Kind + "_broken").Single().Id, 0.01));
        return (s, engine);
    }

    static int Unit(MockEngine engine, int def, double x, double z, int team, double maxHealth = 1000)
    {
        int id = engine.AddUnit(def, x, z);
        engine.Units[id].Team = team;
        engine.Units[id].MaxHealth = engine.Units[id].Health = maxHealth;
        return id;
    }

    /// <summary>Drives units onto the pad, waits a check, and off it again (to x, back at the z they came from): shielded on the next check.</summary>
    static void DriveOver(MockEngine engine, double offX, params int[] units)
    {
        var from = units.ToDictionary(id => id, id => engine.Units[id].Z);
        foreach (int id in units)
            (engine.Units[id].X, engine.Units[id].Z) = (PadX, PadZ);
        engine.Frames(6);
        foreach (int id in units)
            (engine.Units[id].X, engine.Units[id].Z) = (offX, from[id]);
        engine.Frames(6);
    }

    /// <summary>The last thing the game side told the drawing side of a unit: its bubble's radius and how much is left (0: gone).</summary>
    static (double Radius, double Left)? Told(MockEngine engine, int unitID) =>
        engine.ToUnsynced.LastOrDefault(m => m is ["barmapeditor_shield", double id, ..] && (int)id == unitID) is [_, _, double r, double left] ? (r, left) : null;

    [Test]
    public static void AShieldPadKindWritesItsPadDerelictAndEffects()
    {
        var s = Fresh("shield-files");
        Report.Info(s.AddStructureKind(Kind, StructureType.ShieldPad, StructureKindSettings.ShieldPad()));
        Assert.True(s.Structures.Kinds[Kind] is { Type: StructureType.ShieldPad, Gap: 6, Depth: 6, ShieldPercent: 50, EnergyPerMetal: 10, BubbleScale: 1, Owned: true },
            "the kind: a 6 by 6 square pad, shields half a unit's health for 10 energy a metal, bubbles their units' size");
        var defs = FeatureDefs.Load(s.Package.Root).SelectMany(f => f.Features).ToDictionary(f => f.Name, f => f.Table!);
        Assert.True(defs[Kind + "_centre"].Get("blocking") is false && defs[Kind + "_centre"].Get("indestructible") is false &&
                    defs[Kind + "_centre"].Get("reclaimable") is false && LuaValuesRuntime.ToNumber(defs[Kind + "_centre"].Get("damage")) == 6000,
            "the platform: units drive over it, it can be destroyed (6,000), not reclaimed");
        Assert.True(defs[Kind + "_broken"].Get("blocking") is false && defs[Kind + "_broken"].Get("reclaimable") is true &&
                    LuaValuesRuntime.ToNumber(defs[Kind + "_broken"].Get("reclaimtime")) == 12_000,
            "the derelict: driven over too, reclaimed with the repair's 12,000 work");
        foreach (var part in new[] { "_centre", "_broken" })
            Assert.True(S3oModel.Load(File.ReadAllBytes(PathOf(s, $"objects3d/{Kind}{part}.s3o"))) is { Texture1: StructureModels.ShieldPadTexture1 }, $"{part}: built, from the platform's atlas");
        var effects = new LuaInterpreter().Run(File.ReadAllText(PathOf(s, StructureFiles.ShieldEffectsEntry)), "effects") is [LuaMap e, ..] ? e : null;
        Assert.True(effects != null && new[] { "barmapeditor_shield_up", "barmapeditor_shield_hit", "barmapeditor_shield_down" }.All(n => effects.Get(n) is LuaMap),
            "the effects file defines a bubble going up, a hit and going down");
        foreach (var bad in new[]
                 {
                     StructureKindSettings.ShieldPad() with { ShieldPercent = 0 }, StructureKindSettings.ShieldPad() with { EnergyPerMetal = -1 },
                     StructureKindSettings.ShieldPad() with { BubbleScale = 5 }, StructureKindSettings.ShieldPad() with { Gap = 3 },
                     StructureKindSettings.ShieldPad() with { Look = StructureLook.Ancient },
                 })
            Assert.Throws<InvalidOperationException>(() => s.AddStructureKind("bad", StructureType.ShieldPad, bad), bad.Encode());

        // A map with LuaGaia's game side but not its drawing side gets draw.lua (the bubbles are drawn there); its main.lua is kept.
        var t = Fresh("shield-draw-lua");
        File.Delete(PathOf(t, StructureFiles.LuaGaiaDraw));
        string main = File.ReadAllText(PathOf(t, StructureFiles.LuaGaiaMain));
        t.AddStructureKind(Kind, StructureType.ShieldPad, StructureKindSettings.ShieldPad());
        Assert.True(File.ReadAllText(PathOf(t, StructureFiles.LuaGaiaDraw)) == StructureFiles.DrawText && File.ReadAllText(PathOf(t, StructureFiles.LuaGaiaMain)) == main,
            "draw.lua added; the map's own main.lua untouched");
    }

    /// <summary>
    /// A derelict platform, repaired by team 2: made for team 2 (its colour), driven over, still hit by shots. Units of
    /// team 2 and its ally get a shield as they drive off it, each team paying 10 energy a metal; standing on it does
    /// nothing; an enemy's, an aircraft, a nanoframe and a carried unit get none.
    /// </summary>
    [Test]
    public static void ItsOwnersAndAlliesUnitsGetAShieldAsTheyDriveOff()
    {
        var (_, engine) = Repaired("shield-grant");
        var pad = engine.Named(Kind + "_centre").SingleOrDefault();
        Assert.True(pad.F is { Team: 2, Blocking: false, ProjectileCollidable: true } && !engine.Named(Kind + "_broken").Any() &&
                    engine.Messages.Any(m => m.Team == 2 && m.Text.StartsWith("Shield platform repaired", StringComparison.Ordinal)),
            "repaired by team 2: made for it, driven over, still hit by shots; team 2 told");
        int own = Unit(engine, MockEngine.Pawn, PadX, PadZ, 2), ally = Unit(engine, MockEngine.Pawn, PadX + 10, PadZ, 4),
            enemy = Unit(engine, MockEngine.Pawn, PadX - 10, PadZ, 3), plane = Unit(engine, MockEngine.Plane, PadX, PadZ + 10, 2),
            frame = Unit(engine, MockEngine.Pawn, PadX, PadZ - 10, 2), transport = Unit(engine, MockEngine.Transport, PadX + 20, PadZ, 2),
            carried = Unit(engine, MockEngine.Pawn, PadX + 20, PadZ, 2);
        engine.Units[frame].BeingBuilt = true;
        engine.Units[carried].Transporter = transport;
        var energy = (Two: engine.Teams[2].Energy, Four: engine.Teams[4].Energy);
        engine.Frames(30);
        Assert.True(engine.ToUnsynced.Count == 0 && engine.Teams[2].Energy == energy.Two, "standing on it does nothing, and costs nothing");
        foreach (int id in new[] { own, ally, enemy, plane, frame, transport, carried })
            engine.Units[id].X = 3300;
        engine.Frames(6);
        Assert.True(Told(engine, own) is { Left: 1 } mine && Math.Abs(mine.Radius - 12 * 1.15) < 1e-9 && Told(engine, ally) is { Left: 1 },
            $"off it, the team 2 and team 4 units have bubbles, the larger of their radius and 0.6 of their height, 15% more ({Told(engine, own)})");
        Assert.True(Math.Abs(energy.Two - engine.Teams[2].Energy - 540) < 1e-6 && Math.Abs(energy.Four - engine.Teams[4].Energy - 540) < 1e-6,
            "each team paid for its own: 54 metal × 10 energy");
        Assert.True(new[] { enemy, plane, frame, transport, carried }.All(id => Told(engine, id) == null),
            "the enemy's unit, the aircraft, the nanoframe, the transport and the unit it carries have none");
        Assert.True(engine.Effects.Count(e => e.Name == "barmapeditor_shield_up") == 2, "each bubble goes up with an effect");
    }

    /// <summary>
    /// A bubble takes hits whole while it can (a hit that would kill the unit kills nothing); the hit that empties it
    /// lets the rest through and it bursts; shown on the bubble each time, and told to the drawing side.
    /// </summary>
    [Test]
    public static void ABubbleTakesHitsUntilItIsGone()
    {
        var (_, engine) = Repaired("shield-hits");
        int unit = Unit(engine, MockEngine.Pawn, 3300, 3300, 2), far = Unit(engine, MockEngine.Pawn, 3800, 3300, 3);
        DriveOver(engine, 3300, unit);
        engine.Units[unit].Health = 50; // wounded: the next hit would kill it
        engine.Damage(unit, 300, far);
        engine.Frames(6); // the drawing side is told at most once a check
        Assert.True(engine.Units.TryGetValue(unit, out var u) && u.Health == 50 && Told(engine, unit) is { Left: 0.4 },
            $"a 300 hit on a unit with 50 health: taken whole by its 500 shield, the unit untouched ({engine.Units.GetValueOrDefault(unit)?.Health}, {Told(engine, unit)})");
        var flash = engine.Effects.Last(e => e.Name == "barmapeditor_shield_hit");
        Assert.True(flash.X > 3300 + 12 && flash.X < 3300 + 15, $"the flash on the bubble, on the side the shot came from ({flash.X:0.#})");
        engine.Units[unit].Health = 1000;
        engine.Damage(unit, 300, far);
        Assert.True(engine.Units[unit].Health == 900 && Told(engine, unit) is { Left: 0 } && engine.Effects.Any(e => e.Name == "barmapeditor_shield_down"),
            "the next 300: 200 left in the bubble, 100 through; the bubble bursts and is gone");
        engine.Damage(unit, 100, far);
        Assert.True(engine.Units[unit].Health == 800, "gone: hits land again");
    }

    /// <summary>
    /// A titan's bubble (120 tall, 50 across: 72 × 1.15 = 82.8 from its middle, 60 up) covers an allied grunt 50 off
    /// at its feet, not one 90 off, nor an enemy beside it; a shot from inside it isn't stopped.
    /// </summary>
    [Test]
    public static void ATitansBubbleCoversTheGruntsAtItsFeet()
    {
        var (_, engine) = Repaired("shield-titan");
        int titan = Unit(engine, MockEngine.Titan, 3500, 3500, 2, maxHealth: 10_000);
        DriveOver(engine, 3500, titan);
        Assert.True(Told(engine, titan) is { } t && Math.Abs(t.Radius - 82.8) < 1e-9, $"the titan's bubble: 82.8 ({Told(engine, titan)})");
        int near = Unit(engine, MockEngine.Pawn, 3550, 3500, 2), past = Unit(engine, MockEngine.Pawn, 3590, 3500, 4),
            enemy = Unit(engine, MockEngine.Pawn, 3460, 3500, 3), shooter = Unit(engine, MockEngine.Pawn, 3800, 3500, 3),
            inside = Unit(engine, MockEngine.Pawn, 3500, 3530, 3);
        engine.Damage(near, 200, shooter);
        engine.Frames(6);
        Assert.True(engine.Units[near].Health == 1000 && Told(engine, titan) is { Left: 0.96 }, "a grunt 50 off at its feet: the titan's bubble takes the hit");
        engine.Damage(past, 200, shooter);
        engine.Damage(enemy, 200, shooter);
        Assert.True(engine.Units[past].Health == 800 && engine.Units[enemy].Health == 800, "an ally 90 off isn't within it; an enemy inside it isn't covered");
        engine.Damage(near, 100, inside);
        Assert.True(engine.Units[near].Health == 900, "a shot fired from inside the bubble isn't stopped");
        engine.Damage(titan, 1000, shooter);
        Assert.True(engine.Units[titan].Health == 10_000, "and it shields the titan itself");
    }

    /// <summary>Weapons and debris are taken; fire, acid and lava, collisions and paralysis are not.</summary>
    [Test]
    public static void OnlyWeaponsAndDebrisAreTaken()
    {
        var (_, engine) = Repaired("shield-kinds");
        int unit = Unit(engine, MockEngine.Pawn, 3300, 3300, 2);
        DriveOver(engine, 3300, unit);
        engine.Damage(unit, 10, weaponDefID: -1);
        Assert.True(engine.Units[unit].Health == 1000, "debris: taken");
        foreach (var (what, weapon, paralyzer) in new[] { ("fire", -4, false), ("acid", -5, false), ("a collision", -3, false), ("the ground", -2, false), ("paralysis", 7, true) })
        {
            double before = engine.Units[unit].Health;
            engine.Damage(unit, 10, weaponDefID: weapon, paralyzer: paralyzer);
            Assert.True(engine.Units[unit].Health == (paralyzer ? before : before - 10), $"{what}: not taken");
        }
    }

    /// <summary>
    /// Driving off again tops a shield up, paying only for what it is missing; a team that can't pay is charged
    /// nothing, keeps what it had, and is told.
    /// </summary>
    [Test]
    public static void DrivingOverAgainTopsItUp()
    {
        var (_, engine) = Repaired("shield-top-up");
        int unit = Unit(engine, MockEngine.Pawn, 3300, 3300, 2), far = Unit(engine, MockEngine.Pawn, 3800, 3300, 3);
        DriveOver(engine, 3300, unit);
        engine.Damage(unit, 250, far); // half of its 500 gone
        double energy = engine.Teams[2].Energy;
        DriveOver(engine, 3300, unit);
        Assert.True(Told(engine, unit) is { Left: 1 } && Math.Abs(energy - engine.Teams[2].Energy - 270) < 1e-6,
            $"topped up for half the price: 270 energy ({energy - engine.Teams[2].Energy})");
        engine.Damage(unit, 250, far);
        engine.Teams[2] = (engine.Teams[2].Metal, 100);
        DriveOver(engine, 3300, unit);
        Assert.True(engine.Teams[2].Energy == 100 && Told(engine, unit) is { Left: 0.5 } &&
                    engine.Messages.Any(m => m.Team == 2 && m.Text.Contains("not enough energy", StringComparison.Ordinal)),
            "a team that can't pay: nothing charged, the half it had kept, told");
    }

    /// <summary>
    /// Destroyed, the platform is derelict again and gives no shields (bubbles already given stay); a unit given the
    /// ID of a shielded one, or of one that stood on the platform, gets neither.
    /// </summary>
    [Test]
    public static void ADerelictPlatformAndARecycledIDGiveNothing()
    {
        var (_, engine) = Repaired("shield-derelict");
        int shielded = Unit(engine, MockEngine.Pawn, 3300, 3300, 2), standing = Unit(engine, MockEngine.Pawn, PadX, PadZ, 2), far = Unit(engine, MockEngine.Pawn, 3800, 3300, 3);
        DriveOver(engine, 3300, shielded);
        (engine.Units[standing].X, engine.Units[standing].Z) = (PadX, PadZ);
        engine.Frames(6); // standing on it, noted
        engine.KillUnit(standing);
        engine.AddUnit(MockEngine.Pawn, 3300, 3400, reuse: standing);
        engine.Units[standing].Team = 2;
        engine.Frames(6);
        Assert.True(Told(engine, standing) == null, "a new unit given the ID of one that stood on the platform isn't shielded off it");
        engine.KillUnit(shielded);
        Assert.True(Told(engine, shielded) is { Left: 0 }, "a shielded unit destroyed: its bubble gone from the drawing side");
        engine.AddUnit(MockEngine.Pawn, 3300, 3300, reuse: shielded);
        engine.Units[shielded].Team = 2;
        engine.Damage(shielded, 100, far);
        Assert.True(engine.Units[shielded].Health == 900, "and the new unit with its ID has no bubble");

        int keeps = Unit(engine, MockEngine.Pawn, 3300, 3500, 2);
        DriveOver(engine, 3300, keeps);
        engine.Kill(engine.Named(Kind + "_centre").Single().Id);
        Assert.True(engine.Named(Kind + "_broken").Count() == 1 && engine.Messages.Any(m => m.Team == 2 && m.Text.StartsWith("Your shield platform was destroyed", StringComparison.Ordinal)),
            "destroyed: derelict again, team 2 told");
        int late = Unit(engine, MockEngine.Pawn, 3300, 3600, 2);
        DriveOver(engine, 3300, late);
        Assert.True(Told(engine, late) == null && Told(engine, keeps) is { Left: 1 }, "the derelict gives nothing; a bubble given before stays");
    }

    /// <summary>A guardian's doom sets health, not damage: a shielded unit it dooms dies on time.</summary>
    [Test]
    public static void AGuardiansDoomGoesThroughAShield()
    {
        var s = Fresh("shield-guardian");
        s.AddStructureKind(Kind, StructureType.ShieldPad, StructureKindSettings.ShieldPad());
        s.PlaceStructure(Kind, PadX, PadZ, 0);
        s.AddGuardianKind("warden", StructureKindSettings.Guardian(), ["armpw"]);
        s.PlaceStructure("warden", 1008, 1008, 0);
        s.SetGuardianZone(1008, 1008, new GuardianZone([(4000, 3000), (4000, 4000)], 100, false));
        var engine = new MockEngine(s);
        engine.Teams[2] = (100_000, 1_000_000);
        engine.Load();
        engine.Reclaim(100, (500, 2, engine.Named(Kind + "_broken").Single().Id, 0.01));
        int unit = Unit(engine, MockEngine.Pawn, 3300, 3300, 2);
        DriveOver(engine, 3300, unit);
        Assert.True(Told(engine, unit) is { Left: 1 }, "shielded");
        engine.Units[unit].X = 4000;
        engine.Frames(30);
        Assert.True(!engine.Units.ContainsKey(unit), "in the guardian's band it dies on time, shield or not");
    }

    /// <summary>After a reload a standing platform is taken back as its owner's, still driven over and shielding; a derelict one stays derelict.</summary>
    [Test]
    public static void AReloadTakesThePlatformBack()
    {
        var (s, engine) = Repaired("shield-reload");
        var again = new MockEngine(s) { Frame = 2000 };
        foreach (var (_, f) in engine.Features)
        {
            int id = again.AddFeature(f.Name, f.X, f.Z, f.Heading);
            again.Features[id].Team = f.Team;
        }
        again.Teams[2] = (0, 1_000_000);
        again.Load();
        var pad = again.Named(Kind + "_centre").Single();
        Assert.True(again.Features.Count == engine.Features.Count && pad.F is { Team: 2, Blocking: false, ProjectileCollidable: true },
            "nothing new made; the platform team 2's, driven over");
        int unit = Unit(again, MockEngine.Pawn, 3300, 3300, 2);
        DriveOver(again, 3300, unit);
        Assert.True(Told(again, unit) is { Left: 1 }, "and it still shields team 2's units");
    }

    /// <summary>
    /// The drawing side: a bubble the game side tells of is drawn round its unit at its middle, scaled to its radius,
    /// for an allied unit or one in sight; an enemy's out of sight isn't (unless spectating with full view); a bubble
    /// gone is no longer drawn; other gadgets' messages are left alone.
    /// </summary>
    [Test]
    public static void TheDrawingSideDrawsWhatThisPlayerCanSee()
    {
        var s = Fresh("shield-draw");
        s.AddStructureKind(Kind, StructureType.ShieldPad, StructureKindSettings.ShieldPad());
        var draw = new MockDrawSide(PathOf(s, StructureFiles.GadgetEntry)) { MyAllyTeam = 0 };
        Assert.True(!draw.Receive(["someone_else", 1.0]), "another gadget's message isn't taken");
        draw.Receive(["barmapeditor_shield", 11.0, 82.8, 1.0]);
        draw.Receive(["barmapeditor_shield", 12.0, 13.8, 0.5]);
        draw.Positions[11] = (100, 60, 200);
        draw.Positions[12] = (300, 10, 400);
        draw.InLos.Add(11); // 12 is an enemy's, out of sight
        draw.Draw();
        Assert.True(draw.Calls.Contains("Translate 100 60 200") && draw.Calls.Contains("Scale 82.8") && !draw.Calls.Contains("Translate 300 10 400"),
            $"the bubble in sight drawn at its unit's middle, its radius; the one out of sight not ({string.Join(", ", draw.Calls.Where(c => c.StartsWith("T", StringComparison.Ordinal) || c.StartsWith("S", StringComparison.Ordinal)))})");
        Assert.True(draw.Vertices == 12 * 24 * 6 + 11 * 24 * 2 + 12 * 12 * 2, $"its sphere and lines built once, every point on the unit sphere ({draw.Vertices})");
        draw.FullView = true;
        draw.Draw();
        Assert.True(draw.Calls.Contains("Translate 300 10 400") && draw.Calls.Contains("Scale 13.8"), "a spectator with full view sees them all");
        draw.Receive(["barmapeditor_shield", 11.0, 82.8, 0.0]);
        draw.Draw();
        Assert.True(!draw.Calls.Contains("Translate 100 60 200"), "a bubble gone isn't drawn");
    }
}
