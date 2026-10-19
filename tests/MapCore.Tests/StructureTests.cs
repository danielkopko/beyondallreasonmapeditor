using MapCore.Assets;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Items;
using MapCore.Lua;
using MapCore.Structures;

namespace MapCore.Tests;

/// <summary>
/// Stands in for the engine so the structures gadget can be run without BAR: it implements only the engine calls
/// the gadget uses, each as Recoil's source has it (checked 2026-10-15); any other call is a nil and fails. Features
/// and units it destroys reach FeatureDestroyed and UnitDestroyed after the callin that destroyed them. MoveCtrl:
/// Enable takes over any unit; SetPosition moves it only once enabled (LuaSyncedMoveCtrl.cpp), and it then stays put.
/// </summary>
sealed class MockEngine
{
    public sealed class Feature
    {
        public required string Name;
        public double X, Y, Z;
        public int Heading;
        public double Health, MaxHealth;
        public bool Blocking = true, ProjectileCollidable = true;
        public int Team = -1;     // CreateFeature's team (-1: none, a map feature's)
        public bool MoveCtrl;     // SetFeatureMoveCtrl: held where it is
        public (double X, double Y, double Z)? Front; // SetFeatureDirection's front
    }

    public sealed class Unit
    {
        public int Def;
        public double X, Y, Z;
        public bool MoveCtrl;
        public double Health = 1000, MaxHealth = 1000;
        public bool BeingBuilt;    // a nanoframe (GetUnitIsBeingBuilt)
        public int? Transporter;   // the transport carrying it (GetUnitTransporter)
        public int AllyTeam;       // GetUnitAllyTeam: BAR's lobby team - 1
        public int Team;           // GetUnitTeam (its allies: MockEngine.AllyOf)
    }

    /// <summary>A projectile SpawnProjectile made: where it started, where it was sent, its speed a frame, when.</summary>
    public sealed class Projectile
    {
        public int WeaponDef, Team, SpawnFrame, Ttl;
        public (double X, double Y, double Z) Pos, End, Speed;
        public (double X, double Y, double Z) At(int frame) =>
            (Pos.X + Speed.X * (frame - SpawnFrame), Pos.Y + Speed.Y * (frame - SpawnFrame), Pos.Z + Speed.Z * (frame - SpawnFrame));
    }

    readonly LuaInterpreter lua = new(new LuaLimits(MaxSteps: 100_000_000));
    readonly string root;
    readonly Dictionary<string, (int Id, int XSize, int ZSize, double MaxHealth, bool Blocking)> defs = [];
    readonly Queue<int> destroyed = [];
    readonly Queue<(int Id, int Def)> unitsDestroyed = [];
    readonly Func<(int X, int Z), int> mapType;
    LuaMap gadget = new();
    int nextFeature = 1, nextUnit = 1000;

    public int Frame;
    public readonly Dictionary<int, Feature> Features = [];
    public readonly Dictionary<int, Unit> Units = [];
    public readonly Dictionary<(int X, int Z), int> TypeMap = [];
    public readonly Dictionary<int, double[]> TerrainTypes = [];
    public readonly Dictionary<int, (double Metal, double Energy)> Teams = [];
    public readonly List<(int Team, string Text)> Messages = [];
    public const int GaiaTeam = 7;
    public readonly int MapWidth, MapHeight;

    public const int Turret = 1, Factory = 2, Tank = 3, Plane = 4, Bomber = 5, Fighter = 6, BomberScav = 7, Transport = 8, Pawn = 9, Titan = 10;

    /// <summary>Teams' alliances for AreTeamsAllied (a team not listed is its own).</summary>
    public readonly Dictionary<int, int> AllyOf = [];

    /// <summary>What the game side sent the drawing side (SendToUnsynced), in order.</summary>
    public readonly List<object?[]> ToUnsynced = [];

    /// <summary>The unit types' heights, as UnitDefs gives them (a unit's middle is half its height up).</summary>
    readonly Dictionary<int, double> heights = [];

    /// <summary>The unit types' footprints (xsize, squares of 8) and whether they are buildings or factories.</summary>
    readonly Dictionary<int, (int Size, bool Immobile)> footprints = [];

    /// <summary>
    /// Whether BAR's "Footprint clearance" gadget runs (luarules/gadgets/unit_footprint_clearance.lua, read 2026-10-19):
    /// as a building or factory is created it destroys every feature whose position lies within xsize * 5 by zsize * 5
    /// of it, geothermal vents apart. It is LuaRules (order 100), so it sees the unit before the map's LuaGaia gadget
    /// (order 300). On by default, as in BAR.
    /// </summary>
    public bool BarClearance = true;

    /// <summary>Features BAR's clearance destroyed, by name.</summary>
    public readonly List<string> Cleared = [];

    /// <summary>The weapon def ID the map's guardian rocket gets when the map ships its weapon file (as BAR loads weapons/*.lua).</summary>
    public const int RocketWeapon = 41;

    public readonly Dictionary<int, Projectile> Projectiles = [];
    public readonly List<int> DeletedProjectiles = [];
    public readonly List<(string Name, double X, double Y, double Z, int Frame)> Effects = [];
    public readonly List<string> Echoes = [];
    int nextProjectile = 5000;

    public MockEngine(MapEditSession session)
    {
        root = session.Package.Root;
        (MapWidth, MapHeight) = ((int)session.MapWidth, (int)session.MapHeight);
        int w = session.Smf.MapX / 2;
        mapType = sq => session.Smf.TypeMap[sq.Z * w + sq.X];
        int id = 1;
        foreach (var def in FeatureDefs.Load(root).SelectMany(f => f.Features))
        {
            double Number(string key, double fallback) => LuaValuesRuntime.ToNumber(def.Table?.GetIgnoreCase(key)) ?? fallback;
            // As FeatureDefHandler.cpp reads them; a feature blocks as its definition says (blocking defaults to true).
            defs.TryAdd(def.Name, (id++, (int)Number("footprintx", 1) * 2, (int)Number("footprintz", 1) * 2, Math.Max(0.1, Number("damage", 0.1)),
                def.Table?.GetIgnoreCase("blocking") is not false));
        }
    }

    static LuaMap List(IEnumerable<object> items)
    {
        var t = new LuaMap();
        int i = 1;
        foreach (var item in items)
            t.Set((double)i++, item);
        return t;
    }

    static double Num(object?[] a, int i) => LuaValuesRuntime.ToNumber(i < a.Length ? a[i] : null) ?? throw new LuaRuntimeException($"argument {i + 1}: a number expected");

    /// <summary>A feature the map put there before the gadget loaded (as the featureplacer does).</summary>
    public int AddFeature(string name, double x, double z, int heading = 0)
    {
        var def = defs[name];
        int id = nextFeature++;
        Features[id] = new Feature { Name = name, X = x, Y = 0, Z = z, Heading = heading, Health = def.MaxHealth, MaxHealth = def.MaxHealth, Blocking = def.Blocking };
        return id;
    }

    /// <summary>A unit, with a new ID or (as the engine reuses a destroyed unit's ID) a given one.</summary>
    public int AddUnit(int def, double x, double z, double y = 0, int? reuse = null)
    {
        int id = reuse ?? nextUnit++;
        Units[id] = new Unit { Def = def, X = x, Y = y, Z = z };
        return id;
    }

    /// <summary>Something else destroys a unit: it is gone, and the gadget told (UnitDestroyed).</summary>
    public void KillUnit(int unitID)
    {
        if (Units.Remove(unitID, out var u))
            Call("UnitDestroyed", (double)unitID, (double)u.Def, 0.0);
    }

    /// <summary>
    /// A hit, in the order of Unit.cpp's DoDamage: ApplyDamage takes the health (paralysis takes none of it, and
    /// nothing is clamped), then UnitDamaged, then the unit dies if it has none left. weaponDefID: 0 and up a weapon,
    /// -1 debris, -2 and -3 collisions, -4 fire, -5 acid or lava (SolidObject.h's DamageType, negated).
    /// </summary>
    public void Damage(int unitID, double damage, int? attackerID = null, int weaponDefID = 0, bool paralyzer = false)
    {
        if (!Units.TryGetValue(unitID, out var u))
            return;
        if (!paralyzer)
            u.Health -= damage;
        Unit? attacker = attackerID is int a && Units.TryGetValue(a, out var au) ? au : null;
        Call("UnitDamaged", (double)unitID, (double)u.Def, (double)u.Team, damage, paralyzer, (double)weaponDefID, -1.0,
            attacker != null ? (double)attackerID!.Value : null, attacker != null ? (double)attacker.Def : null, attacker != null ? (double)attacker.Team : null);
        if (Units.TryGetValue(unitID, out u) && u.Health <= 0)
        {
            Units.Remove(unitID);
            Call("UnitDestroyed", (double)unitID, (double)u.Def, (double)u.Team);
        }
    }

    public void Load()
    {
        var g = lua.Globals;
        var spring = new LuaMap();
        void F(string name, Func<object?[], object?[]> body) => spring.Set(name, LuaInterpreter.Function(name, body));
        F("GetGameFrame", _ => [(double)Frame]);
        F("SetTerrainTypeData", a =>
        {
            TerrainTypes[(int)Num(a, 0)] = [.. a.Skip(1).Select(v => LuaValuesRuntime.ToNumber(v) ?? double.NaN)];
            return [true];
        });
        F("GetAllFeatures", _ => [List(Features.Keys.Order().Select(k => (object)(double)k))]);
        F("GetFeatureDefID", a => Features.TryGetValue((int)Num(a, 0), out var f) ? [(double)defs[f.Name].Id] : [null]);
        F("CreateFeature", a =>
        {
            var name = a[0] as string ?? throw new LuaRuntimeException("CreateFeature: a name expected");
            if (!defs.TryGetValue(name, out var def))
                return []; // the engine returns nothing for an unknown def
            int id = nextFeature++;
            Features[id] = new Feature
            {
                Name = name, X = Num(a, 1), Y = Num(a, 2), Z = Num(a, 3), Heading = a.Length > 4 && a[4] is double h ? (int)h : 0,
                Health = def.MaxHealth, MaxHealth = def.MaxHealth, Blocking = def.Blocking, Team = a.Length > 5 && a[5] is double team ? (int)team : -1,
            };
            return [(double)id];
        });
        F("DestroyFeature", a =>
        {
            int id = (int)Num(a, 0);
            if (Features.Remove(id))
                destroyed.Enqueue(id);
            return [];
        });
        F("ValidFeatureID", a => [Features.ContainsKey((int)Num(a, 0))]);
        F("GetFeaturePosition", a => Features.TryGetValue((int)Num(a, 0), out var f) ? [f.X, f.Y, f.Z] : []);
        F("SetFeaturePosition", a =>
        {
            if (Features.TryGetValue((int)Num(a, 0), out var f))
                (f.X, f.Y, f.Z) = (Num(a, 1), Num(a, 2), Num(a, 3));
            return [];
        });
        // As LuaSyncedRead.cpp: the features whose positions lie in the rectangle.
        F("GetFeaturesInRectangle", a =>
        {
            double x1 = Num(a, 0), z1 = Num(a, 1), x2 = Num(a, 2), z2 = Num(a, 3);
            return [List(Features.Where(f => f.Value.X >= x1 && f.Value.X <= x2 && f.Value.Z >= z1 && f.Value.Z <= z2).Select(f => (object)(double)f.Key))];
        });
        F("GetFeatureHeading", a => Features.TryGetValue((int)Num(a, 0), out var f) ? [(double)f.Heading] : []);
        F("GetFeatureHealth", a => Features.TryGetValue((int)Num(a, 0), out var f) ? [f.Health, f.MaxHealth, 0.0] : []);
        F("SetFeatureHealth", a =>
        {
            if (Features.TryGetValue((int)Num(a, 0), out var f))
                f.Health = Math.Min(f.MaxHealth, Num(a, 1));
            return [];
        });
        F("SetFeatureBlocking", a =>
        {
            if (Features.TryGetValue((int)Num(a, 0), out var f))
            {
                if (a.Length > 1 && a[1] is bool blocking)
                    f.Blocking = blocking;
                if (a.Length > 3 && a[3] is bool projectiles)
                    f.ProjectileCollidable = projectiles;
            }
            return [f?.Blocking ?? false];
        });
        F("SetMapSquareTerrainType", a =>
        {
            int hx = (int)(Num(a, 0) / 8), hz = (int)(Num(a, 1) / 8);
            if (hx < 0 || hz < 0 || hx >= MapWidth / 8 || hz >= MapHeight / 8)
                throw new LuaRuntimeException($"Out of range: x = {hx} z = {hz}!");
            var square = (hx >> 1, hz >> 1);
            int old = TypeOf(square);
            TypeMap[square] = Math.Clamp((int)Num(a, 2), 0, 255);
            return [(double)old];
        });
        F("GetGroundHeight", _ => [0.0]);
        F("UseTeamResource", a =>
        {
            int team = (int)Num(a, 0);
            if (a.Length < 2 || a[1] is not LuaMap cost)
                throw new LuaRuntimeException("the mock implements only the table form of UseTeamResource");
            double metal = LuaValuesRuntime.ToNumber(cost.Get("metal")) ?? 0, energy = LuaValuesRuntime.ToNumber(cost.Get("energy")) ?? 0;
            var (m, e) = Teams.GetValueOrDefault(team);
            if (m >= metal && e >= energy)
            {
                Teams[team] = (m - metal, e - energy);
                return [true];
            }
            return [false];
        });
        F("GetUnitsInRectangle", a =>
        {
            double x1 = Num(a, 0), z1 = Num(a, 1), x2 = Num(a, 2), z2 = Num(a, 3);
            return [List(Units.Where(u => u.Value.X >= x1 && u.Value.X <= x2 && u.Value.Z >= z1 && u.Value.Z <= z2).Select(u => (object)(double)u.Key))];
        });
        F("GetUnitDefID", a => Units.TryGetValue((int)Num(a, 0), out var u) ? [(double)u.Def] : []);
        // As LuaSyncedRead.cpp: the base, and with midPos its middle too (half its height up).
        F("GetUnitPosition", a => !Units.TryGetValue((int)Num(a, 0), out var u) ? []
            : a.Length > 1 && a[1] is true ? [u.X, u.Y, u.Z, u.X, u.Y + heights.GetValueOrDefault(u.Def) / 2, u.Z] : [u.X, u.Y, u.Z]);
        F("GetUnitTeam", a => Units.TryGetValue((int)Num(a, 0), out var u) ? [(double)u.Team] : []);
        F("AreTeamsAllied", a => [AllyOf.GetValueOrDefault((int)Num(a, 0), (int)Num(a, 0)) == AllyOf.GetValueOrDefault((int)Num(a, 1), (int)Num(a, 1))]);
        F("GetUnitsInSphere", a =>
        {
            double x = Num(a, 0), y = Num(a, 1), z = Num(a, 2), r = Num(a, 3);
            return [List(Units.Where(u =>
            {
                double my = u.Value.Y + heights.GetValueOrDefault(u.Value.Def) / 2;
                return (u.Value.X - x) * (u.Value.X - x) + (my - y) * (my - y) + (u.Value.Z - z) * (u.Value.Z - z) <= r * r;
            }).Select(u => (object)(double)u.Key))];
        });
        F("DestroyUnit", a =>
        {
            int id = (int)Num(a, 0);
            if (Units.Remove(id, out var u))
                unitsDestroyed.Enqueue((id, u.Def));
            return [];
        });
        F("Echo", a =>
        {
            Echoes.Add(string.Concat(a.Select(v => v?.ToString())));
            return [];
        });
        F("GetGaiaTeamID", _ => [(double)GaiaTeam]);
        F("GetUnitHealth", a => Units.TryGetValue((int)Num(a, 0), out var u) ? [u.Health, u.MaxHealth, 0.0, 0.0, u.BeingBuilt ? 0.5 : 1.0] : []);
        F("SetUnitHealth", a =>
        {
            // The number form only (the gadget never passes the table form); health is kept to its maximum.
            if (Units.TryGetValue((int)Num(a, 0), out var u))
                u.Health = Math.Min(u.MaxHealth, Num(a, 1));
            return [];
        });
        F("GetUnitIsBeingBuilt", a => Units.TryGetValue((int)Num(a, 0), out var u) ? [u.BeingBuilt, u.BeingBuilt ? 0.5 : 1.0] : []);
        F("GetUnitTransporter", a => Units.TryGetValue((int)Num(a, 0), out var u) && u.Transporter is int t ? [(double)t] : []);
        F("GetUnitAllyTeam", a => Units.TryGetValue((int)Num(a, 0), out var u) ? [(double)u.AllyTeam] : []);
        F("GetAllUnits", _ => [List(Units.Keys.Order().Select(id => (object)(double)id))]);
        F("SpawnCEG", a =>
        {
            Effects.Add((a[0] as string ?? "", Num(a, 1), Num(a, 2), Num(a, 3), Frame));
            return [true];
        });
        F("SpawnProjectile", a =>
        {
            // As LuaSyncedCtrl.cpp's ParseProjectileParams: tables pos, end, speed; numbers ttl, team (Gaia's by default).
            int def = (int)Num(a, 0);
            if (def != RocketWeapon || a.Length < 2 || a[1] is not LuaMap t)
                return [];
            (double, double, double) V(string key) => t.Get(key) is LuaMap v
                ? (LuaValuesRuntime.ToNumber(v.Get(1.0)) ?? 0, LuaValuesRuntime.ToNumber(v.Get(2.0)) ?? 0, LuaValuesRuntime.ToNumber(v.Get(3.0)) ?? 0)
                : (0, 0, 0);
            int id = nextProjectile++;
            Projectiles[id] = new Projectile
            {
                WeaponDef = def, Pos = V("pos"), End = V("end"), Speed = V("speed"), SpawnFrame = Frame,
                Ttl = (int)(LuaValuesRuntime.ToNumber(t.Get("ttl")) ?? 0), Team = (int)(LuaValuesRuntime.ToNumber(t.Get("team")) ?? GaiaTeam),
            };
            return [(double)id];
        });
        F("GetProjectilePosition", a =>
        {
            if (!Projectiles.TryGetValue((int)Num(a, 0), out var pr))
                return [];
            var (x, y, z) = pr.At(Frame);
            return [x, y, z];
        });
        F("DeleteProjectile", a =>
        {
            int id = (int)Num(a, 0);
            if (Projectiles.Remove(id))
                DeletedProjectiles.Add(id);
            return [];
        });
        F("SetFeatureMoveCtrl", a =>
        {
            if (Features.TryGetValue((int)Num(a, 0), out var f))
                f.MoveCtrl = a.Length > 1 && a[1] is true;
            return [];
        });
        F("SetFeatureDirection", a =>
        {
            if (Features.TryGetValue((int)Num(a, 0), out var f))
                f.Front = (Num(a, 1), Num(a, 2), Num(a, 3));
            return [];
        });
        F("GetFeatureTeam", a => Features.TryGetValue((int)Num(a, 0), out var f) ? [(double)(f.Team >= 0 ? f.Team : GaiaTeam)] : []);
        F("AddTeamResource", a =>
        {
            // As LuaSyncedCtrl.cpp: the type's first letter, the amount never below 0.
            int team = (int)Num(a, 0);
            var type = a[1] as string ?? throw new LuaRuntimeException("AddTeamResource: a resource name expected");
            double amount = Math.Max(0, Num(a, 2));
            var (m, e) = Teams.GetValueOrDefault(team);
            Teams[team] = type[0] == 'm' ? (m + amount, e) : type[0] == 'e' ? (m, e + amount) : throw new LuaRuntimeException($"bad resource {type}");
            return [];
        });
        F("SendMessageToTeam", a =>
        {
            Messages.Add(((int)Num(a, 0), a[1] as string ?? ""));
            return [];
        });
        var moveCtrl = new LuaMap();
        moveCtrl.Set("Enable", LuaInterpreter.Function("Enable", a =>
        {
            if (Units.TryGetValue((int)Num(a, 0), out var u))
                u.MoveCtrl = true;
            return [];
        }));
        moveCtrl.Set("SetPosition", LuaInterpreter.Function("SetPosition", a =>
        {
            if (Units.TryGetValue((int)Num(a, 0), out var u) && u.MoveCtrl)
                (u.X, u.Y, u.Z) = (Num(a, 1), Num(a, 2), Num(a, 3));
            return [];
        }));
        spring.Set("MoveCtrl", moveCtrl);
        g.Set("Spring", spring);
        var game = new LuaMap();
        game.Set("mapSizeX", (double)MapWidth);
        game.Set("mapSizeZ", (double)MapHeight);
        game.Set("squareSize", 8.0);
        g.Set("Game", game);

        var vfs = new LuaMap();
        vfs.Set("FileExists", LuaInterpreter.Function("FileExists", a => [File.Exists(Path.Combine(root, ((string)a[0]!).Replace('/', Path.DirectorySeparatorChar)))]));
        vfs.Set("Include", LuaInterpreter.Function("Include", a =>
            new LuaInterpreter().Run(File.ReadAllText(Path.Combine(root, ((string)a[0]!).Replace('/', Path.DirectorySeparatorChar))), (string)a[0]!)));
        g.Set("VFS", vfs);

        var featureDefs = new LuaMap();
        foreach (var (name, def) in defs)
        {
            var t = new LuaMap();
            t.Set("name", name);
            t.Set("xsize", (double)def.XSize);
            t.Set("zsize", (double)def.ZSize);
            featureDefs.Set((double)def.Id, t);
        }
        g.Set("FeatureDefs", featureDefs);
        var unitDefs = new LuaMap();
        void U(int id, string name, bool immobile, bool factory, bool fly, int size, double radius = 16, double height = 20, double metal = 100)
        {
            var t = new LuaMap();
            t.Set("name", name);
            t.Set("isImmobile", immobile);
            t.Set("isFactory", factory);
            t.Set("canFly", fly);
            t.Set("xsize", (double)size);
            t.Set("zsize", (double)size);
            t.Set("radius", radius);
            t.Set("height", height);
            t.Set("metalCost", metal);
            heights[id] = height;
            footprints[id] = (size, immobile);
            unitDefs.Set((double)id, t);
        }
        U(Turret, "turret", true, false, false, 4);
        U(Factory, "factory", true, true, false, 12);
        U(Tank, "tank", false, false, false, 4, 16, 16, 200);
        U(Plane, "plane", false, false, true, 4);
        U(Bomber, "armthund", false, false, true, 4);
        U(Fighter, "armfig", false, false, true, 4);
        U(BomberScav, "armthund_scav", false, false, true, 4);
        U(Transport, "armatlas", false, false, true, 6);
        U(Pawn, "armpw", false, false, false, 2, 10, 20, 54);        // a light bot
        U(Titan, "armbanth", false, false, false, 6, 50, 120, 13000); // a titan: tall and wide
        g.Set("UnitDefs", unitDefs);
        // UnitDefNames: the same tables by name, each with its id.
        var unitDefNames = new LuaMap();
        foreach (var (key, value) in unitDefs.Entries)
            if (key is double id && value is LuaMap t && t.Get("name") is string name)
            {
                t.Set("id", id);
                unitDefNames.Set(name, t);
            }
        g.Set("UnitDefNames", unitDefNames);
        // WeaponDefNames: the guardian's rocket, if the map ships its weapon file.
        var weaponDefNames = new LuaMap();
        if (File.Exists(Path.Combine(root, StructureFiles.RocketWeaponEntry.Replace('/', Path.DirectorySeparatorChar))))
        {
            var rocket = new LuaMap();
            rocket.Set("id", (double)RocketWeapon);
            rocket.Set("name", StructureKind.GuardianRocket);
            weaponDefNames.Set(StructureKind.GuardianRocket, rocket);
        }
        g.Set("WeaponDefNames", weaponDefNames);

        var handler = new LuaMap();
        handler.Set("IsSyncedCode", LuaInterpreter.Function("IsSyncedCode", _ => [true]));
        g.Set("gadgetHandler", handler);
        g.Set("SendToUnsynced", LuaInterpreter.Function("SendToUnsynced", a =>
        {
            ToUnsynced.Add(a);
            return [];
        }));
        gadget = new LuaMap();
        g.Set("gadget", gadget);
        lua.Run(File.ReadAllText(Path.Combine(root, StructureFiles.GadgetEntry.Replace('/', Path.DirectorySeparatorChar))), "map_structures.lua");
        Call("Initialize");
    }

    public int TypeOf((int X, int Z) square) => TypeMap.TryGetValue(square, out int t) ? t : mapType(square);

    /// <summary>Calls a callin (with the gadget as self), then delivers FeatureDestroyed for what it destroyed.</summary>
    public object?[] Call(string callin, params object?[] args)
    {
        var f = gadget.Get(callin) as LuaFunction ?? throw new InvalidOperationException($"the gadget has no {callin}");
        var result = lua.Invoke(f, [gadget, .. args]);
        while (destroyed.TryDequeue(out int id))
            lua.Invoke(gadget.Get("FeatureDestroyed"), gadget, (double)id, -1.0);
        while (unitsDestroyed.TryDequeue(out var unit))
            lua.Invoke(gadget.Get("UnitDestroyed"), gadget, (double)unit.Id, (double)unit.Def, 0.0);
        return result;
    }

    /// <summary>The engine destroys a feature (its health ran out).</summary>
    public void Kill(int featureID)
    {
        Features.Remove(featureID);
        Call("FeatureDestroyed", (double)featureID, -1.0);
    }

    public void Frames(int count)
    {
        for (int i = 0; i < count; i++)
            Call("GameFrame", (double)++Frame);
    }

    /// <summary>Builders reclaiming features: one AllowFeatureBuildStep per builder per frame, as Feature.cpp asks.</summary>
    public void Reclaim(int frames, params (int Builder, int Team, int Feature, double Part)[] builders)
    {
        for (int i = 0; i < frames; i++)
        {
            foreach (var (builder, team, feature, part) in builders)
                if (Features.TryGetValue(feature, out var f))
                    Call("AllowFeatureBuildStep", (double)builder, (double)team, (double)feature, (double)defs[f.Name].Id, -part);
            Frames(1);
        }
    }

    public bool AllowBuild(int unitDef, double x, double z, int facing = 0) =>
        Call("AllowUnitCreation", (double)unitDef, 1.0, 0.0, x, 0.0, z, (double)facing) is [true, ..];

    /// <summary>A builder starting a building, as Builder.cpp does: allowed or not, then its nanoframe made (UnitCreated). Null if refused.</summary>
    public int? Build(int unitDef, double x, double z)
    {
        if (!AllowBuild(unitDef, x, z))
            return null;
        int id = AddUnit(unitDef, x, z);
        if (BarClearance && footprints.TryGetValue(unitDef, out var fp) && fp.Immobile)
        {
            double r = fp.Size * 5;
            foreach (var (fid, f) in Features.Where(f => Math.Abs(f.Value.X - x) <= r && Math.Abs(f.Value.Z - z) <= r && f.Value.Name != "geovent").ToList())
            {
                Features.Remove(fid);
                Cleared.Add(f.Name);
                lua.Invoke(gadget.Get("FeatureDestroyed"), gadget, (double)fid, -1.0); // DestroyFeature tells every gadget at once
            }
        }
        Call("UnitCreated", (double)id, (double)unitDef, 0.0, 1.0);
        return id;
    }

    public IEnumerable<(int Id, Feature F)> Named(string name) => Features.Where(f => f.Value.Name == name).Select(f => (f.Key, f.Value));
}

static class StructureTests
{
    const string Door = "door_a", Tunnel = "tunnel_a", Open = "open_a";

    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static string Listing(string root) => string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", FileSystemUtil.AllEntries)
        .Select(f => FileSystemUtil.EntryPath(root, f) + (File.Exists(f) ? ":" + new FileInfo(f).Length : "/")).Order(StringComparer.Ordinal));

    /// <summary>Acidic Quarry with a door, a tunnel and an open tunnel, their parts using the map's own rocks' models.</summary>
    static MapEditSession WithStructures(string name)
    {
        var s = Fresh(name);
        s.AddStructureKind(Door, StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", "agorm_rock3", Gap: 4, WallWidth: 2, Depth: 2,
            Health: 20_000, Work: 36_000, Metal: 1200, Energy: 3000));
        s.AddStructureKind(Tunnel, StructureType.Tunnel, new StructureKindSettings("agorm_rock1", "agorm_rock4", "agorm_rock3", Gap: 6, WallWidth: 2, Depth: 4));
        s.AddStructureKind(Open, StructureType.OpenTunnel, new StructureKindSettings("agorm_rock1", "agorm_rock5", null, Gap: 4, WallWidth: 1, Depth: 3));
        s.PlaceStructure(Door, 3000, 3000, 0);
        s.PlaceStructure(Tunnel, 1600, 4400, 1);
        s.PlaceStructure(Open, 4400, 1600, 2);
        return s;
    }

    [Test]
    public static void PartsTileWithNoGapAtEveryFacing()
    {
        var kind = new StructureKind("k", StructureType.Door, 5, 3, 2, 0, 0);
        foreach (int facing in new[] { 0, 1, 2, 3 })
        {
            var parts = StructureLayout.Parts(kind, new StructureInstance("k", 3008, 2992, facing));
            // Blocked squares as the engine works them out for an object whose position is on the 8-unit grid.
            static HashSet<(int, int)> Squares(StructurePart p)
            {
                var set = new HashSet<(int, int)>();
                for (int x = (int)(p.X1 / 8); x < (int)(p.X2 / 8); x++)
                    for (int z = (int)(p.Z1 / 8); z < (int)(p.Z2 / 8); z++)
                        set.Add((x, z));
                return set;
            }
            var blocking = parts.Where(p => p.Role != "repair").Select(Squares).ToList();
            var all = blocking.SelectMany(b => b).ToList();
            int minX = all.Min(q => q.Item1), maxX = all.Max(q => q.Item1), minZ = all.Min(q => q.Item2), maxZ = all.Max(q => q.Item2);
            Assert.True(all.Count == all.Distinct().Count() && all.Count == (maxX - minX + 1) * (maxZ - minZ + 1),
                $"facing {facing}: walls and centre cover one rectangle, no square twice and none missed");
            Assert.True(parts.All(p => p.X % 8 == 0 && p.Z % 8 == 0), $"facing {facing}: every part on the 8-unit grid");
            var centre = parts.Single(p => p.Role == "centre");
            bool acrossX = facing is 0 or 2;
            Assert.True(parts.Where(p => p.Role == "wall").All(w => acrossX ? w.Z == centre.Z : w.X == centre.X), $"facing {facing}: the walls beside the gap");
        }
    }

    [Test]
    public static void KindsAndStructuresAreOneStepEach()
    {
        var s = Fresh("structures-ops");
        string before = Listing(s.Package.Root);
        Report.Info(s.AddStructureKind(Door, StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", "agorm_rock3")));
        var config = s.Structures;
        Assert.True(config.Kinds[Door] is { Gap: 4, Metal: 1200 } && config.TerrainType is int t && !s.Smf.TypeMap.Contains((byte)t),
            $"the kind and a terrain type the map doesn't use ({config.TerrainType})");
        var defs = FeatureDefs.Load(s.Package.Root).SelectMany(f => f.Features).ToDictionary(f => f.Name);
        Assert.True(defs["door_a_wall"].Table!.Get("indestructible") is true && defs["door_a_centre"].Table!.Get("blocking") is true &&
                    defs["door_a_centre"].Table!.Get("featuredead") == null && defs["door_a_repair"].Table!.Get("reclaimtime") is 36000.0 &&
                    defs["door_a_repair"].Table!.Get("autoreclaimable") is false && defs["door_a_repair"].Table!.Get("blocking") is false,
            "the parts: indestructible walls, a blocking centre with no wreck, repair markers whose reclaim time is the work");
        Assert.True(File.Exists(Path.Combine(s.Package.Root, "LuaGaia", "Gadgets", "map_structures.lua")) &&
                    File.ReadAllText(Path.Combine(s.Package.Root, "LuaGaia", "main.lua")).Contains("LuaGadgets/gadgets.lua", StringComparison.Ordinal),
            "the gadget, beside the map's own LuaGaia loader (kept)");
        s.PlaceStructure(Door, 3001, 2999, 1);
        Assert.True(s.Structures.Instances.Single() is { X: 3008, Z: 2992, Facing: 1 }, "placed on the 16-unit grid");
        Assert.Throws<InvalidOperationException>(() => s.PlaceStructure(Door, 8, 8, 0), "a structure sticking out of the map");
        Assert.Throws<InvalidOperationException>(() => s.AddStructureKind(Door, StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", "agorm_rock3")), "a kind twice");
        Assert.Throws<InvalidOperationException>(() => s.AddStructureKind("door_b", StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", null)), "a door without a repair model");
        s.RemoveStructure(3010, 2990);
        Assert.True(s.Structures.Instances.Count == 0, "removed");
        s.Undo();
        s.Undo();
        s.Undo();
        Assert.Equal(before, Listing(s.Package.Root), "three undos leave the package as it was");
    }

    /// <summary>The gadget, run against the mock engine: what it puts on the map at game start.</summary>
    [Test]
    public static void TheGadgetPutsTheStructuresUp()
    {
        var s = WithStructures("structures-start");
        var config = s.Structures;
        var engine = new MockEngine(s);
        engine.Load();
        int tt = config.TerrainType!.Value;
        Assert.True(engine.TerrainTypes[tt].SequenceEqual([0.0, 0, 0, 0]), "its terrain type has no speed for any kind of unit");
        foreach (var instance in config.Instances)
        {
            var kind = config.Kinds[instance.Kind];
            foreach (var part in StructureLayout.Parts(kind, instance).Where(p => p.Role != "repair"))
                Assert.True(engine.Named(part.Feature).Count(f => Math.Abs(f.F.X - part.X) < 0.01 && Math.Abs(f.F.Z - part.Z) < 0.01 && f.F.Heading == part.Heading) == 1,
                    $"{part.Feature} at ({part.X}, {part.Z}) heading {part.Heading}, where the editor puts it");
        }
        Assert.True(!engine.Features.Values.Any(f => f.Name.EndsWith("_repair", StringComparison.Ordinal)), "no repair markers while everything stands");
        var top = engine.Named("tunnel_a_centre").Single().F;
        Assert.True(!top.Blocking && top.ProjectileCollidable, "the tunnel top is off the blocking map (buildable) but can still be shot");
        var tunnel = config.Instances.Single(i => i.Kind == Tunnel);
        var topPart = StructureLayout.Parts(config.Kinds[Tunnel], tunnel).Single(p => p.Role == "centre");
        var under = Squares(topPart.X1, topPart.Z1, topPart.X2, topPart.Z2);
        Assert.True(under.All(q => engine.TypeOf(q) == tt), $"the ground under the tunnel top ({under.Count} squares) can't be crossed");
        Assert.True(engine.Named("door_a_centre").Single().F.Blocking && !engine.Named("open_a_centre").Single().F.Blocking,
            "the door blocks; the open tunnel's middle doesn't, as its definition says");
    }

    static List<(int X, int Z)> Squares(double x1, double z1, double x2, double z2)
    {
        var list = new List<(int, int)>();
        for (int z = (int)Math.Floor(z1 / 16); z * 16 < z2; z++)
            for (int x = (int)Math.Floor(x1 / 16); x * 16 < x2; x++)
                list.Add((x, z));
        return list;
    }

    [Test]
    public static void ABrokenDoorIsRepairedByReclaimingItsMarkers()
    {
        var s = WithStructures("structures-door");
        var config = s.Structures;
        var door = config.Instances.Single(i => i.Kind == Door);
        var parts = StructureLayout.Parts(config.Kinds[Door], door);
        var engine = new MockEngine(s);
        engine.Teams[0] = (2000, 5000);
        engine.Teams[1] = (0, 0);
        engine.Load();
        engine.Kill(engine.Named("door_a_centre").Single().Id);
        var markers = engine.Named("door_a_repair").ToList();
        Assert.True(markers.Count == 2 && parts.Where(p => p.Role == "repair").All(p => markers.Any(m => Math.Abs(m.F.X - p.X) < 0.01 && Math.Abs(m.F.Z - p.Z) < 0.01)) &&
                    markers.All(m => Math.Abs(m.F.Health - m.F.MaxHealth * 0.01) < 1e-9),
            "the door gone, two repair markers stand beside the walls at 1%");

        // A team with nothing to pay with gets nowhere.
        engine.Reclaim(30, (500, 1, markers[0].Id, 0.01));
        Assert.True(markers.All(m => Math.Abs(m.F.Health - m.F.MaxHealth * 0.01) < 1e-9), "a team that can't pay doesn't progress");

        // Two builders, one on each marker, feed one pool: 25 frames at 0.01 each is half way.
        engine.Reclaim(25, (501, 0, markers[0].Id, 0.01), (502, 0, markers[1].Id, 0.01));
        Assert.True(markers.All(m => Math.Abs(m.F.Health / m.F.MaxHealth - 0.5) < 1e-6), $"half way, shown on both markers ({markers[0].F.Health})");
        Assert.True(Math.Abs(engine.Teams[0].Metal - (2000 - 600)) < 1e-6 && Math.Abs(engine.Teams[0].Energy - (5000 - 1500)) < 1e-6,
            $"half the cost paid as the work went ({engine.Teams[0]})");

        // Something in the gap: the repair finishes but the door waits until it is clear.
        int tank = engine.AddUnit(MockEngine.Tank, door.X + 4, door.Z - 4);
        engine.Reclaim(25, (501, 0, markers[0].Id, 0.01), (502, 0, markers[1].Id, 0.01));
        Assert.True(!engine.Named("door_a_centre").Any() && engine.Named("door_a_repair").Count() == 2, "repaired, but held while a tank is in the gap");
        Assert.True(Math.Abs(engine.Teams[0].Metal - 800) < 1e-6, "the whole repair cost 1,200 metal");
        engine.Units.Remove(tank);
        engine.Frames(15);
        var back = engine.Named("door_a_centre").ToList();
        Assert.True(back.Count == 1 && Math.Abs(back[0].F.X - door.X) < 0.01 && !engine.Named("door_a_repair").Any(),
            "once the gap is clear the door is back and the markers are gone");
        Assert.True(Math.Abs(engine.Teams[0].Metal - 800) < 1e-6, "nothing more is paid after the repair is full");
    }

    [Test]
    public static void ATunnelCollapsesOnWhatIsOnIt()
    {
        var s = WithStructures("structures-tunnel");
        var config = s.Structures;
        var tunnel = config.Instances.Single(i => i.Kind == Tunnel);
        var top = StructureLayout.Parts(config.Kinds[Tunnel], tunnel).Single(p => p.Role == "centre");
        var engine = new MockEngine(s);
        var original = Squares(top.X1, top.Z1, top.X2, top.Z2).ToDictionary(q => q, engine.TypeOf);
        engine.Load();

        Assert.True(engine.AllowBuild(MockEngine.Turret, tunnel.X, tunnel.Z), "a turret can be built on the tunnel top");
        Assert.True(!engine.AllowBuild(MockEngine.Factory, tunnel.X, tunnel.Z), "not a factory: what it made couldn't move off");
        Assert.True(!engine.AllowBuild(MockEngine.Tank, tunnel.X, tunnel.Z), "nor anything mobile");
        var door = config.Instances.Single(i => i.Kind == Door);
        var open = config.Instances.Single(i => i.Kind == Open);
        Assert.True(!engine.AllowBuild(MockEngine.Turret, door.X, door.Z) && !engine.AllowBuild(MockEngine.Turret, open.X, open.Z),
            "nothing is built in a door's gap or an open tunnel's middle");
        Assert.True(engine.AllowBuild(MockEngine.Turret, 500, 500) && engine.Call("AllowUnitCreation", 3.0, 1.0, 0.0) is [true, ..],
            "elsewhere building is untouched, and so is a factory making a unit");

        int turret = engine.AddUnit(MockEngine.Turret, tunnel.X, tunnel.Z);
        int tank = engine.AddUnit(MockEngine.Tank, top.X1 + 4, top.Z1 + 4);
        int landed = engine.AddUnit(MockEngine.Plane, top.X2 - 4, top.Z2 - 4, y: 0);
        int flying = engine.AddUnit(MockEngine.Plane, tunnel.X, tunnel.Z, y: 300);
        int beside = engine.AddUnit(MockEngine.Tank, top.X2 + 100, tunnel.Z);
        engine.Kill(engine.Named("tunnel_a_centre").Single().Id);
        Assert.True(!engine.Units.ContainsKey(turret) && !engine.Units.ContainsKey(tank) && !engine.Units.ContainsKey(landed),
            "the turret, the tank and the landed plane on the top went with it");
        Assert.True(engine.Units.ContainsKey(flying) && engine.Units.ContainsKey(beside), "the plane flying over and the tank beside it did not");
        Assert.True(original.All(q => engine.TypeOf(q.Key) == q.Value), "the ground under it is its own type again: the gap can be crossed");
        Assert.True(engine.Named("tunnel_a_repair").Count() == 2 && !engine.AllowBuild(MockEngine.Turret, tunnel.X, tunnel.Z),
            "repair markers are up, and nothing can be built in the open gap");

        // Repair it (one builder; the kind's defaults: 1,200 metal, 3,000 energy).
        engine.Teams[0] = (5000, 5000);
        var marker = engine.Named("tunnel_a_repair").First().Id;
        engine.Reclaim(100, (600, 0, marker, 0.01));
        var rebuilt = engine.Named("tunnel_a_centre").SingleOrDefault();
        Assert.True(rebuilt.F != null && !rebuilt.F.Blocking && original.Keys.All(q => engine.TypeOf(q) == config.TerrainType),
            "rebuilt: buildable again, and its ground can't be crossed again");
    }

    [Test]
    public static void AReloadTakesBackWhatIsThere()
    {
        var s = WithStructures("structures-reload");
        var engine = new MockEngine(s);
        engine.Load();
        int count = engine.Features.Count;

        var again = new MockEngine(s) { Frame = 500 };
        foreach (var (id, f) in engine.Features)
            again.AddFeature(f.Name, f.X, f.Z, f.Heading);
        foreach (var (square, type) in engine.TypeMap)
            again.TypeMap[square] = type;
        again.Load();
        Assert.Equal(count, again.Features.Count, "nothing new made: the parts already there are taken back");
        again.Kill(again.Named("door_a_centre").Single().Id);
        Assert.True(again.Named("door_a_repair").Count() == 2, "and they work: the door's markers appear when it is destroyed");
    }

    [Test]
    public static void FeaturesCanBeBuiltOn()
    {
        var s = Fresh("structures-buildon");
        s.AddStructureKind(Door, StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", "agorm_rock3"));
        var config = s.Structures;
        config.BuildOn["agorm_rock4"] = true;
        File.WriteAllText(Path.Combine(s.Package.Root, "mapconfig", "map_structures.lua"), config.ToLua());
        var engine = new MockEngine(s);
        int rock = engine.AddFeature("agorm_rock4", 2000, 2000);
        var before = engine.TypeOf((125, 125));
        engine.Load();
        Assert.True(!engine.Features[rock].Blocking && engine.TypeOf((125, 125)) == config.TerrainType && engine.AllowBuild(MockEngine.Turret, 2000, 2000) &&
                    !engine.AllowBuild(MockEngine.Tank, 2000, 2000),
            "a buildOn feature is off the blocking map, can't be crossed, takes buildings but not units");
        engine.Kill(rock);
        Assert.True(engine.TypeOf((125, 125)) == before, "destroyed, its ground is its own again");
    }

    /// <summary>A new feature made to be built on: written blocking, listed in the config, and the gadget makes it buildable.</summary>
    [Test]
    public static void ANewFeatureCanBeMadeToBuildOn()
    {
        var s = Fresh("structures-buildon-feature");
        string before = Listing(s.Package.Root);
        var rock = FeatureDefs.Load(s.Package.Root).SelectMany(f => f.Features).Single(f => f.Name == "agorm_rock2");
        Report.Info(s.AddCustomFeature("platform", "agorm_rock2", CustomFeatureSettings.FromDefinition(rock) with { BuildOn = 2, FootprintX = 4, FootprintZ = 4 }));
        var config = s.Structures;
        Assert.True(config.BuildOn.TryGetValue("platform", out bool impassable) && impassable && config.TerrainType != null &&
                    File.Exists(Path.Combine(s.Package.Root, "LuaGaia", "Gadgets", "map_structures.lua")),
            "listed as something to build on that units can't cross, with the gadget");
        var engine = new MockEngine(s);
        int platform = engine.AddFeature("platform", 2048, 2048);
        engine.Load();
        Assert.True(!engine.Features[platform].Blocking && engine.TypeOf((128, 128)) == config.TerrainType && engine.AllowBuild(MockEngine.Turret, 2048, 2048) &&
                    !engine.AllowBuild(MockEngine.Factory, 2048, 2048),
            "in game: off the blocking map, its ground can't be crossed, a turret can go on it, a factory can't");
        s.Undo();
        Assert.Equal(before, Listing(s.Package.Root), "one undo takes the feature, the config and the gadget away");
    }

    [Test]
    public static void StructuresReplayFromTheJournal()
    {
        var archive = TestData.PathOf(TestData.AcidicQuarryArchive);
        var live = MapEditSession.Open(MapPackage.Open(archive, TestData.NewWorkDir("structures-journal-live")));
        var dir = TestData.NewWorkDir("structures-journal-record");
        using (var journal = new SessionJournal(Path.Combine(dir, "j.journal"), Path.Combine(dir, "files")))
        {
            live.Journal = journal;
            live.AddStructureKind(Door, StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", "agorm_rock3"));
            live.PlaceStructure(Door, 3000, 3000, 0);
            live.PlaceStructure(Door, 2000, 2000, 3);
            live.Undo();
            live.Journal = null;
        }
        var rebuilt = MapEditSession.Open(MapPackage.Open(archive, TestData.NewWorkDir("structures-journal-rebuilt")));
        SessionJournal.Replay(rebuilt, SessionJournal.Read(Path.Combine(dir, "j.journal")));
        Assert.Equal(Listing(live.Package.Root), Listing(rebuilt.Package.Root), "the replayed package has the same files");
        Assert.Equal(File.ReadAllText(Path.Combine(live.Package.Root, "mapconfig", "map_structures.lua")),
            File.ReadAllText(Path.Combine(rebuilt.Package.Root, "mapconfig", "map_structures.lua")), "and the same config");
    }

    static Dictionary<string, FeatureDef> Defs(MapEditSession s) => FeatureDefs.Load(s.Package.Root).SelectMany(f => f.Features).ToDictionary(f => f.Name);

    static byte[] Bytes(MapEditSession s, string entry) => File.ReadAllBytes(Path.Combine(s.Package.Root, entry.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>A built kind: its parts' models and the shared textures written, definitions that use them, and its settings kept.</summary>
    [Test]
    public static void BuiltKindsWriteTheirModels()
    {
        var s = Fresh("structures-built");
        string before = Listing(s.Package.Root);
        var settings = StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door);
        Report.Info(s.AddStructureKind("gate", StructureType.Door, settings));
        foreach (var part in new[] { "gate_wall", "gate_centre", "gate_repair", "gate_marker", "gate_broken" })
            Assert.True(S3oModel.Load(Bytes(s, $"objects3d/{part}.s3o")) is { TriangleCount: > 0, Texture1: StructureModels.Texture1, Texture2: StructureModels.Texture2 },
                $"{part}'s model, textured from the atlas");
        Assert.True(TgaReader.Read(Bytes(s, "unittextures/" + StructureModels.Texture1)) is { Width: 512, Height: 512 } &&
                    TgaReader.Read(Bytes(s, "unittextures/" + StructureModels.Texture2)) is { Width: 512, Height: 512 }, "the two atlas textures");
        var defs = Defs(s);
        var wall = defs["gate_wall"].Table!;
        Assert.True(defs["gate_wall"].Object == "gate_wall.s3o" && wall.Get("upright") is true && wall.Get("collisionvolumetype") is "box" &&
                    wall.Get("collisionvolumescales") is "48 80 48" && wall.Get("footprintx") is 3.0 && wall.Get("footprintz") is 3.0 &&
                    (wall.Get("customparams") as LuaMap)?.Get("cuspbr") is true,
            "the wall: its model, upright on slopes, hit over the box it fills (48 × 80 × 48), drawn by BAR's PBR shader");
        var kind = s.Structures.Kinds["gate"];
        Assert.True(kind is { Look: StructureLook.Concrete, Height: 80, WallWidth: 3, Depth: 3 } && s.KindSettings(kind) == settings,
            "the config keeps its look, height and the settings as given");
        Assert.True(StructureKindSettings.Decode(settings.Encode(), StructureType.Door) == settings, "settings survive being written and read");
        s.Undo();
        Assert.Equal(before, Listing(s.Package.Root), "one undo takes models, textures, definitions, config and gadget away");
    }

    [Test]
    public static void OldSettingsStillMeanModelsFromAssets()
    {
        var old = StructureKindSettings.Decode("wall=agorm_rock1;centre=agorm_rock2;repair=agorm_rock3;gap=4;wallwidth=2;depth=2;health=20000;work=36000;metal=1200;energy=3000;open=1");
        Assert.True(old is { Look: StructureLook.Assets, Height: 0, Wall: "agorm_rock1", WallWidth: 2 }, "a kind written before built models: assets, as it was");
        var plain = StructureKindSettings.Decode("look=metal", StructureType.OpenTunnel);
        Assert.True(plain is { Look: StructureLook.Metal, Height: 80, WallWidth: 3, Depth: 3, Gap: 4, Roof: StructureRoof.Cliff, Wall: null },
            "a built kind starts taller and thicker than a commander; an open tunnel with a rock roof");
        Assert.True(StructureKindSettings.Decode("gap=5").Look == StructureLook.Concrete, "no models named: built in concrete");
        Assert.Throws<InvalidOperationException>(() => StructureKindSettings.Decode("look=wood"), "an unknown look");
    }

    /// <summary>A kind made before the editor kept its settings gets them back from what it wrote.</summary>
    [Test]
    public static void KindsMadeBeforeSettingsWereKeptShowTheirs()
    {
        var s = Fresh("structures-old-settings");
        s.AddStructureKind(Door, StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", "agorm_rock3", Health: 5000, Work: 900));
        var path = Path.Combine(s.Package.Root, "mapconfig", "map_structures.lua");
        var text = File.ReadAllText(path);
        File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(text, @", settings = ""[^""]*""", ""));
        var kind = StructureConfig.Load(s.Package.Root).Kinds[Door];
        Assert.True(kind.Settings == null && s.KindSettings(kind) is { Look: StructureLook.Assets, Wall: "agorm_rock1", Centre: "agorm_rock2", Repair: "agorm_rock3", Health: 5000, Work: 900 },
            "its models (from what its parts were made from), the centre's health and the repair's work");
    }

    [Test]
    public static void ChangingAKindChangesEveryOnePlaced()
    {
        var s = Fresh("structures-change");
        s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door));
        s.PlaceStructure("gate", 3000, 3000, 0);
        s.PlaceStructure("gate", 2000, 2400, 1);
        s.PlaceStructure("gate", 200, 3000, 0); // near the west edge
        var wallBefore = Bytes(s, "objects3d/gate_wall.s3o");
        var configBefore = Bytes(s, StructureConfig.Entry);
        var changed = s.KindSettings(s.Structures.Kinds["gate"]) with { Height = 120, WallWidth = 4, Look = StructureLook.Metal };
        var report = s.ChangeStructureKind("gate", changed);
        Assert.True(report.Contains("3 placed", StringComparison.Ordinal), $"the report says how many it changed ({report})");
        var wall = S3oModel.Load(Bytes(s, "objects3d/gate_wall.s3o"))!;
        float top = wall.Vertices.Max(v => v.Position.Y), width = wall.Vertices.Max(v => v.Position.X) - wall.Vertices.Min(v => v.Position.X);
        Assert.True(Math.Abs(top - 120) < 0.01 && Math.Abs(width - 64) < 0.01, $"the walls built again: 120 tall, 64 wide ({top}, {width})");
        Assert.True(Defs(s)["gate_wall"].Table!.Get("footprintx") is 4.0 && s.Structures.Kinds["gate"] is { WallWidth: 4, Height: 120, Look: StructureLook.Metal } &&
                    s.Structures.Instances.Count == 3, "its footprint and config follow; the structures stay");
        Assert.Throws<InvalidOperationException>(() => s.ChangeStructureKind("gate", changed with { Gap = 32 }), "a gap that would put the one by the edge off the map");
        Assert.Throws<InvalidOperationException>(() => s.ChangeStructureKind("nothing", changed), "a kind the map doesn't have");
        s.Undo();
        Assert.True(Bytes(s, "objects3d/gate_wall.s3o").SequenceEqual(wallBefore) && Bytes(s, StructureConfig.Entry).SequenceEqual(configBefore),
            "one undo puts the old models and config back");

        // From built models to models from assets: the built ones go, and come back with an undo.
        string built = Listing(s.Package.Root);
        s.ChangeStructureKind("gate", s.KindSettings(s.Structures.Kinds["gate"]) with { Look = StructureLook.Assets, Wall = "agorm_rock1", Centre = "agorm_rock2", Repair = "agorm_rock3" });
        Assert.True(!File.Exists(Path.Combine(s.Package.Root, "objects3d", "gate_wall.s3o")) && Defs(s)["gate_wall"].Object == Defs(s)["agorm_rock1"].Object,
            "the walls use the rock's model, and the model built for them is gone");
        s.Undo();
        Assert.Equal(built, Listing(s.Package.Root), "undone");
    }

    /// <summary>A building begun on a tunnel's flat roof is lifted onto it, and buildings there are kept apart by the gadget.</summary>
    [Test]
    public static void BuildingsAreLiftedOntoATunnelsRoof()
    {
        var s = Fresh("structures-roof");
        s.AddStructureKind("bunker", StructureType.Tunnel, StructureKindSettings.Built(StructureLook.Metal, StructureType.Tunnel) with { Gap = 6, Depth = 4 });
        s.PlaceStructure("bunker", 3008, 3008, 0);
        var tunnel = s.Structures.Instances.Single();
        var top = StructureLayout.Parts(s.Structures.Kinds["bunker"], tunnel).Single(p => p.Role == "centre"); // 96 × 64
        var engine = new MockEngine(s);
        engine.Load();
        int first = engine.Build(MockEngine.Turret, top.X1 + 24, top.Z1 + 24) ?? throw new TestFailure("a turret wholly on the roof is allowed");
        Assert.True(engine.Units[first] is { MoveCtrl: true, Y: 80 }, $"its nanoframe is lifted onto the roof, 80 up ({engine.Units[first].Y})");
        Assert.True(engine.Build(MockEngine.Turret, top.X1 + 32, top.Z1 + 30) == null, "another overlapping it is refused (lifted, the first doesn't block the ground)");
        Assert.True(engine.Build(MockEngine.Turret, top.X1 + 4, top.Z2 - 24) == null, "one hanging over the roof's edge is refused");
        int second = engine.Build(MockEngine.Turret, top.X2 - 24, top.Z2 - 24) ?? throw new TestFailure("a second turret clear of the first is allowed");
        int outside = engine.Build(MockEngine.Turret, 500, 500) ?? throw new TestFailure("building elsewhere is untouched");
        Assert.True(engine.Units[outside] is { MoveCtrl: false, Y: 0 }, "and isn't lifted");
        engine.Kill(engine.Named("bunker_centre").Single().Id);
        Assert.True(!engine.Units.ContainsKey(first) && !engine.Units.ContainsKey(second), "the roof's buildings go with it");
        engine.Teams[0] = (5000, 5000);
        engine.Reclaim(100, (600, 0, engine.Named("bunker_broken").Single().Id, 0.01)); // no markers placed: what is left of it is reclaimed
        Assert.True(engine.Build(MockEngine.Turret, top.X1 + 32, top.Z1 + 30) is int again && engine.Units[again].Y == 80,
            "rebuilt, the roof takes buildings again where the gone ones stood");
    }

    /// <summary>
    /// BAR's footprint clearance destroys features under a new building. Found in game: a build-on feature vanished when
    /// built on. The gadget's features step aside while the building is made, so a tunnel's roof (and what it holds) and a
    /// build-on feature stay; a plain rock still goes, as BAR means it to.
    /// </summary>
    [Test]
    public static void BarsFootprintClearanceLeavesStructuresBe()
    {
        var s = Fresh("structures-clearance");
        s.AddStructureKind("bunker", StructureType.Tunnel, StructureKindSettings.Built(StructureLook.Metal, StructureType.Tunnel) with { Gap = 6, Depth = 4 });
        s.PlaceStructure("bunker", 3008, 3008, 0);
        var rockDef = FeatureDefs.Load(s.Package.Root).SelectMany(f => f.Features).Single(f => f.Name == "agorm_rock2");
        s.AddCustomFeature("platform", "agorm_rock2", CustomFeatureSettings.FromDefinition(rockDef) with { BuildOn = 2, FootprintX = 4, FootprintZ = 4 });
        var top = StructureLayout.Parts(s.Structures.Kinds["bunker"], s.Structures.Instances.Single()).Single(p => p.Role == "centre");
        var engine = new MockEngine(s);
        int platform = engine.AddFeature("platform", 2048, 2048);
        int rock = engine.AddFeature("agorm_rock1", 1024, 1024);
        engine.Load();
        var before = engine.Features.ToDictionary(f => f.Key, f => (f.Value.X, f.Value.Y, f.Value.Z));

        // On the roof, over the top's own position: the turret is lifted onto it and the top stays, where it was.
        var (cx, cz) = ((top.X1 + top.X2) / 2, (top.Z1 + top.Z2) / 2);
        int centre = engine.Named("bunker_centre").Single().Id;
        int onRoof = engine.Build(MockEngine.Turret, cx, cz) ?? throw new TestFailure("a turret on the roof's middle is allowed");
        Assert.True(engine.Features.ContainsKey(centre) && engine.Cleared.Count == 0, $"the roof outlasts BAR's clearance (cleared: {string.Join(", ", engine.Cleared)})");
        Assert.True(engine.Units[onRoof] is { MoveCtrl: true, Y: 80 }, $"and the turret stands on it, 80 up ({engine.Units[onRoof].Y})");
        Assert.True(engine.Features.All(f => before.TryGetValue(f.Key, out var p) && p == (f.Value.X, f.Value.Y, f.Value.Z)),
            "every feature is back where it stood, in the same frame");

        // On the build-on feature: it stays too.
        Assert.True(engine.Build(MockEngine.Turret, 2048, 2048) is int && engine.Features.ContainsKey(platform) && engine.Features[platform] is { X: 2048, Z: 2048 },
            "a turret on a feature made to be built on leaves it standing, in place");

        // A plain rock under a new building goes, as BAR means it to: the mock's clearance is real.
        Assert.True(engine.Build(MockEngine.Turret, 1024, 1024) is int && !engine.Features.ContainsKey(rock) && engine.Cleared.SequenceEqual(["agorm_rock1"]),
            "a rock under a new building is cleared");

        // The tunnel still works afterwards: destroyed, its roof's buildings go with it.
        engine.Kill(centre);
        Assert.True(!engine.Units.ContainsKey(onRoof), "the roof's turret goes with the tunnel, as before");
    }

    [Test]
    public static void ARockRoofedTunnelIsBuiltOnByNobody()
    {
        var s = Fresh("structures-rock-roof");
        s.AddStructureKind("crag", StructureType.Tunnel, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Tunnel) with { Roof = StructureRoof.Cliff });
        s.PlaceStructure("crag", 3008, 3008, 0);
        var engine = new MockEngine(s);
        var square = ((int)(3008 / 16), (int)(3008 / 16));
        int ground = engine.TypeOf(square);
        engine.Load();
        Assert.True(engine.Named("crag_centre").Single().F.Blocking && engine.TypeOf(square) == ground && !engine.AllowBuild(MockEngine.Turret, 3008, 3008),
            "it blocks as a door does: no ground made impassable, nothing built on it");
        engine.Kill(engine.Named("crag_centre").Single().Id);
        Assert.True(engine.Named("crag_broken").Count() == 1, "destroyed, what is left of it lies in the gap, to be reclaimed to repair it");
    }

    [Test]
    public static void AStructureCanStartDestroyed()
    {
        var s = Fresh("structures-start-broken");
        s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door));
        s.AddStructureKind("arch", StructureType.OpenTunnel, StructureKindSettings.Built(StructureLook.Concrete, StructureType.OpenTunnel));
        s.PlaceStructure("gate", 3008, 3008, 0, broken: true);
        s.AddStructureMarker(3008, 3008, 2904, 3104);
        s.AddStructureMarker(3008, 3008, 3120, 3104);
        s.PlaceStructure("gate", 2000, 2000, 1);
        s.AddStructureMarker(2000, 2000, 2104, 2000);
        s.PlaceStructure("arch", 4000, 4000, 0);
        Assert.Throws<InvalidOperationException>(() => s.PlaceStructure("arch", 1200, 4000, 0, broken: true), "an open tunnel can't start destroyed");
        Assert.Throws<InvalidOperationException>(() => s.SetStructureBroken(4000, 4000, true), "nor be made to");
        var config = s.Structures;
        var kind = config.Kinds["gate"];
        var broken = config.Instances.Single(i => i.Broken);
        var atStart = StructureLayout.AtStart(kind, broken).ToList();
        Assert.True(atStart.Select(p => p.Role).Order().SequenceEqual(["debris", "marker", "marker", "wall", "wall"]) &&
                    atStart.Where(p => p.Role == "marker").All(p => p.Feature == "gate_repair"),
            "the views show its walls, what is left of its gate, and its markers red, not its gate");

        var engine = new MockEngine(s);
        engine.Teams[0] = (5000, 5000);
        engine.Load();
        var gates = engine.Named("gate_centre").ToList();
        Assert.True(gates.Count == 1 && Math.Abs(gates[0].F.X - 2000) < 0.01, "only the standing one has its gate");
        Assert.True(engine.Named("gate_marker").SingleOrDefault() is { F: { X: 2104, Z: 2000, Blocking: false } }, "the standing one's marker is green, where it was put");
        var red = engine.Named("gate_repair").ToList();
        Assert.True(red.Count == 2 && red.All(m => Math.Abs(m.F.Health - m.F.MaxHealth * 0.01) < 1e-9) && red.Any(m => m.F.X == 2904 && m.F.Z == 3104),
            "the other starts with its markers red, where they were put");
        Assert.True(engine.Named("gate_broken").SingleOrDefault() is { F: { X: 3008, Z: 3008, Blocking: false } }, "and what is left of its gate in the gap, not blocking");
        engine.Reclaim(100, (700, 0, red[0].Id, 0.01));
        Assert.True(engine.Named("gate_centre").Count() == 2 && !engine.Named("gate_repair").Any() && !engine.Named("gate_broken").Any() &&
                    engine.Named("gate_marker").Count() == 3,
            "a red marker repairs it, as one destroyed in play: its gate back, the wreck gone, all three markers green");

        // The standing one, destroyed in play: its marker turns red and what is left of it lies in the gap; a reload takes them back.
        engine.Kill(engine.Named("gate_centre").Single(g => Math.Abs(g.F.X - 2000) < 0.01).Id);
        Assert.True(engine.Named("gate_broken").SingleOrDefault() is { F: { X: 2000, Z: 2000 } } && engine.Named("gate_repair").SingleOrDefault() is { F.X: 2104 } &&
                    engine.Named("gate_marker").Count() == 2,
            "a gate destroyed in play: its wreck in the gap, its marker red, the other gate's two still green");
        var again = new MockEngine(s) { Frame = 900 };
        foreach (var (_, f) in engine.Features)
            again.AddFeature(f.Name, f.X, f.Z, f.Heading);
        int count = engine.Features.Count;
        again.Load();
        Assert.True(again.Features.Count == count && again.Named("gate_broken").Count() == 1 && again.Named("gate_repair").Count() == 1,
            "after a reload: the same features, nothing new");

        s.SetStructureBroken(3008, 3008, false);
        Assert.True(!s.Structures.Instances.Any(i => i.Broken), "it can be set to start standing again");
        s.Undo();
        Assert.True(s.Structures.Instances.Single(i => i.Broken) is { X: 3008, Z: 3008 }, "undone");
    }

    /// <summary>A built door or tunnel with no markers placed is repaired by reclaiming what is left of it in its gap.</summary>
    [Test]
    public static void WithoutMarkersWhatIsLeftIsReclaimed()
    {
        var s = Fresh("structures-repair-wreck");
        s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Metal, StructureType.Door) with { Metal = 100, Energy = 100 });
        s.AddStructureKind("crag", StructureType.Tunnel, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Tunnel) with { Roof = StructureRoof.Cliff });
        s.PlaceStructure("gate", 3008, 3008, 0);
        s.PlaceStructure("crag", 2000, 2000, 0);
        var engine = new MockEngine(s);
        engine.Teams[0] = (5000, 5000);
        engine.Load();
        foreach (var name in new[] { "gate", "crag" })
        {
            engine.Kill(engine.Named(name + "_centre").Single().Id);
            var wreck = engine.Named(name + "_broken").Single();
            Assert.True(!engine.Named(name + "_repair").Any() && !engine.Named(name + "_marker").Any() && Math.Abs(wreck.F.Health - wreck.F.MaxHealth * 0.01) < 1e-9,
                $"{name}: destroyed, no markers anywhere: what is left of it, at 1%");
            engine.Reclaim(50, (800, 0, wreck.Id, 0.01));
            Assert.True(Math.Abs(wreck.F.Health / wreck.F.MaxHealth - 0.5) < 1e-6 && engine.Features.ContainsKey(wreck.Id), $"{name}: reclaiming it repairs half way, and it stays");
            engine.Reclaim(50, (800, 0, wreck.Id, 0.01));
            Assert.True(engine.Named(name + "_centre").Count() == 1 && !engine.Named(name + "_broken").Any(), $"{name}: repaired, the wreck gone");
        }
    }

    /// <summary>
    /// A built kind made by an earlier editor has no green-marker or wreck definitions: the gadget leaves those out and
    /// it keeps its two markers beside its walls, so it can still be repaired.
    /// </summary>
    [Test]
    public static void AKindWithoutItsNewPartsKeepsMarkersBesideItsWalls()
    {
        var s = Fresh("structures-older-kind");
        s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door));
        s.PlaceStructure("gate", 3008, 3008, 0);
        foreach (var part in new[] { "gate_marker", "gate_broken" }) // as a map made before they existed
        {
            File.Delete(Path.Combine(s.Package.Root, "features", part + ".lua"));
            File.Delete(Path.Combine(s.Package.Root, "objects3d", part + ".s3o"));
        }
        var engine = new MockEngine(s);
        engine.Teams[0] = (5000, 5000);
        engine.Load();
        engine.Kill(engine.Named("gate_centre").Single().Id);
        var markers = engine.Named("gate_repair").Select(m => m.F.X).Order().ToList();
        double out_ = (4 + 2 * 3) * 8 + 16;
        Assert.True(markers.SequenceEqual([3008 - out_, 3008 + out_]) && !engine.Named("gate_broken").Any(),
            $"destroyed, two markers beside its walls, as before ({string.Join(", ", markers)})");
        engine.Reclaim(100, (900, 0, engine.Named("gate_repair").First().Id, 0.01));
        Assert.True(engine.Named("gate_centre").Count() == 1, "and it is repaired through them");
    }

    /// <summary>Repair markers are placed for a built door or tunnel, moved and removed, each as one step.</summary>
    [Test]
    public static void RepairMarkersArePlacedMovedAndRemoved()
    {
        var s = Fresh("structures-markers");
        s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door));
        s.AddStructureKind("arch", StructureType.OpenTunnel, StructureKindSettings.Built(StructureLook.Concrete, StructureType.OpenTunnel));
        s.AddStructureKind(Door, StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", "agorm_rock3"));
        s.PlaceStructure("gate", 3008, 3008, 0);
        s.PlaceStructure("arch", 4000, 4000, 0);
        s.PlaceStructure(Door, 2000, 2000, 0);
        var before = Bytes(s, StructureConfig.Entry);
        Report.Info(s.AddStructureMarker(3008, 3008, 3203, 3011));
        Assert.True(s.Structures.Instances.Single(i => i.Kind == "gate").MarkerList.SequenceEqual([(3200.0, 3008.0)]), "placed for it, on the 8-unit grid");
        Assert.True(StructureConfig.Load(s.Package.Root).Instances.Single(i => i.Kind == "gate").MarkerList.SequenceEqual([(3200.0, 3008.0)]), "kept in the config");
        Assert.True(s.StructureMarkerAt(3195, 3010, 10) is { X: 3200, Z: 3008, Instance.Kind: "gate" }, "found at a point near it");
        Assert.Throws<InvalidOperationException>(() => s.AddStructureMarker(3008, 3008, 3205, 3010), "a second on the same spot");
        Assert.Throws<InvalidOperationException>(() => s.AddStructureMarker(3008, 3008, 2, 3000), "one off the map");
        Assert.Throws<InvalidOperationException>(() => s.AddStructureMarker(4000, 4000, 4100, 4000), "one for an open tunnel (it isn't repaired)");
        Assert.Throws<InvalidOperationException>(() => s.AddStructureMarker(2000, 2000, 2100, 2000), "one for a kind made from assets (it puts its own beside its walls)");
        s.MoveStructureMarker(3200, 3008, 3300, 3100);
        Assert.True(s.Structures.Instances.Single(i => i.Kind == "gate").MarkerList.SequenceEqual([(3296.0, 3104.0)]), "moved");
        s.RemoveStructureMarker(3296, 3104);
        Assert.True(s.Structures.Instances.Single(i => i.Kind == "gate").MarkerList.Count == 0, "removed");
        Assert.Throws<InvalidOperationException>(() => s.RemoveStructureMarker(3296, 3104), "nothing there to remove");
        s.Undo();
        s.Undo();
        s.Undo();
        Assert.True(Bytes(s, StructureConfig.Entry).SequenceEqual(before), "three undos: as it was");
    }

    /// <summary>
    /// A type map that uses all 256 types (All That Glitters' is a picture): the rarest type that behaves as another is
    /// freed for tunnel tops, its squares given that other type at game start, which changes nothing in play.
    /// </summary>
    [Test]
    public static void AMapUsingEveryTerrainTypeHasOneFreed()
    {
        var s = Fresh("structures-every-type");
        var types = s.Smf.TypeMap;
        int w = s.Smf.MapX / 2;
        for (int i = 0; i < types.Length; i++)
            types[i] = (byte)(i % 256 == 200 ? 0 : i % 256); // every type, type 200 nowhere yet
        var rare = new[] { (188, 188), (10, 20), (30, 40) }; // type 200 on just three squares, one under the tunnel to come
        foreach (var (x, z) in rare)
            types[z * w + x] = 200;
        Report.Info(s.AddStructureKind("bunker", StructureType.Tunnel, StructureKindSettings.Built(StructureLook.Metal, StructureType.Tunnel)));
        var config = StructureConfig.Load(s.Package.Root);
        Assert.True(config.TerrainType == 200 && config.RemapInto is int into && into != 200 && config.RemapSquares.Order().SequenceEqual(rare.Order()),
            $"the rarest type that behaves as another is freed, its squares listed for the gadget (type {config.TerrainType}, into {config.RemapInto}, {config.RemapSquares.Count} squares)");
        s.PlaceStructure("bunker", 3008, 3008, 0);
        Assert.True(s.Structures.Instances.Single().Ground == config.RemapInto, "the ground under it is recorded as the game will have it, not as the freed type");

        var engine = new MockEngine(s);
        engine.Load();
        Assert.True(engine.TypeOf((10, 20)) == config.RemapInto && engine.TypeOf((30, 40)) == config.RemapInto && engine.TypeOf((188, 188)) == 200,
            "at game start the freed type's squares take the other type, and the tunnel top's squares the freed one");
        Assert.True(engine.TerrainTypes[200].SequenceEqual([0.0, 0, 0, 0]), "the freed type has no speed");
        engine.Kill(engine.Named("bunker_centre").Single().Id);
        Assert.True(engine.TypeOf((188, 188)) == config.RemapInto && engine.TypeOf((187, 188)) == types[188 * w + 187],
            "the tunnel collapsed, its ground is crossable again: the freed type's square takes the other type, the rest their own");
    }

    /// <summary>A placed door or tunnel moves (on the 16-unit grid) and turns by quarters, each as one step, kept on the map.</summary>
    [Test]
    public static void StructuresMoveAndTurnByQuarters()
    {
        var s = Fresh("structures-move-turn");
        s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door));
        s.PlaceStructure("gate", 3008, 3008, 0, broken: true);
        var configBefore = Bytes(s, StructureConfig.Entry);
        Report.Info(s.MoveStructure(3008, 3008, 3100, 2950));
        Assert.True(s.Structures.Instances.Single() is { X: 3104, Z: 2944, Facing: 0, Broken: true }, "moved to the nearest 16-unit grid point, still starting destroyed");
        Report.Info(s.TurnStructure(3104, 2944, 3));
        Assert.True(s.Structures.Instances.Single() is { X: 3104, Z: 2944, Facing: 3 }, "turned a quarter: its passage runs east-west");
        Assert.Throws<InvalidOperationException>(() => s.MoveStructure(3104, 2944, 20, 2944), "a move that would put it off the map");
        Assert.Throws<InvalidOperationException>(() => s.MoveStructure(3104, 2944, 3100, 2950), "a move that leaves it where it is");
        Assert.Throws<InvalidOperationException>(() => s.TurnStructure(3104, 2944, 3), "a turn to the way it already faces");
        Assert.Throws<InvalidOperationException>(() => s.TurnStructure(1000, 1000, 1), "nothing there to turn");
        s.Undo();
        s.Undo();
        Assert.True(Bytes(s, StructureConfig.Entry).SequenceEqual(configBefore), "two undos put it back where and as it was");
    }

    /// <summary>
    /// An oil pump: derelict at the start; reclaiming the derelict repairs it, and the team that finishes owns it (the
    /// pump and its beam made for that team), the beam nodding and the team paid every second; destroyed, it is
    /// derelict again, and whoever repairs it next owns it.
    /// </summary>
    [Test]
    public static void OilPumpsPayWhoeverRepairsThem()
    {
        var s = Fresh("structures-pump");
        string before = Listing(s.Package.Root);
        Report.Info(s.AddStructureKind("oil", StructureType.Pump, StructureKindSettings.Pump()));
        Assert.True(s.Structures.Kinds["oil"] is { Type: StructureType.Pump, Gap: 4, Depth: 6, MetalIncome: 2, EnergyIncome: 25 },
            "the kind: its model's footprint, 2 metal and 25 energy a second");
        foreach (var part in new[] { "oil_centre", "oil_beam", "oil_broken" })
            Assert.True(S3oModel.Load(Bytes(s, $"objects3d/{part}.s3o")) is { Texture1: StructureModels.PumpTexture1 }, $"{part}: built, from the pump's atlas");
        Assert.True(Defs(s)["oil_beam"].Table!.Get("noselect") is true && Defs(s)["oil_beam"].Table!.Get("blocking") is false, "the beam can't be picked and doesn't block");
        s.PlaceStructure("oil", 3008, 3008, 0);
        var engine = new MockEngine(s);
        engine.Teams[2] = (1000, 1000);
        engine.Teams[3] = (1000, 1000);
        engine.Load();
        var derelict = engine.Named("oil_broken").Single();
        Assert.True(derelict.F.Blocking && !engine.Named("oil_centre").Any() && !engine.Named("oil_beam").Any(), "at the start it is derelict");

        engine.Reclaim(100, (500, 2, derelict.Id, 0.01));
        var pump = engine.Named("oil_centre").SingleOrDefault();
        var beam = engine.Named("oil_beam").SingleOrDefault();
        Assert.True(pump.F is { Team: 2, X: 3008, Z: 3008 } && !engine.Named("oil_broken").Any(), "team 2 repaired it: the pump is made for team 2");
        Assert.True(beam.F is { Team: 2, MoveCtrl: true, X: 3008, Y: 50, Z: 3012 }, "its beam too, held at the post's bearing");
        Assert.True(Math.Abs(engine.Teams[2].Metal - 850) < 1e-6 && Math.Abs(engine.Teams[2].Energy - 400) < 1e-6, $"the repair cost 150 metal and 600 energy ({engine.Teams[2]})");
        Assert.True(engine.Messages.Any(m => m.Team == 2 && m.Text.StartsWith("Oil pump repaired", StringComparison.Ordinal)), "team 2 is told");

        // Owned: the beam nods along the pump (facing south, +z), and team 2 is paid every 30 frames.
        var paid = engine.Teams[2];
        var fronts = new List<(double X, double Y, double Z)>();
        for (int i = 0; i < 120; i++) // a whole nod
        {
            engine.Frames(1);
            fronts.Add(engine.Features[beam.Id].Front!.Value);
        }
        Assert.True(fronts.All(f => Math.Abs(f.X) < 1e-9 && f.Z > 0.9 && Math.Abs(f.X * f.X + f.Y * f.Y + f.Z * f.Z - 1) < 1e-9) &&
                    fronts.Max(f => f.Y) > 0.15 && fronts.Min(f => f.Y) < -0.15,
            "its beam nods: its front along the pump, tipped up and down");
        Assert.True(Math.Abs(engine.Teams[2].Metal - paid.Metal - 8) < 1e-6 && Math.Abs(engine.Teams[2].Energy - paid.Energy - 100) < 1e-6,
            $"four seconds: 8 metal and 100 energy ({engine.Teams[2]})");

        // Destroyed: derelict again, no more pay; team 3 repairs it and owns it.
        engine.Kill(pump.Id);
        Assert.True(!engine.Named("oil_beam").Any() && engine.Named("oil_broken").Count() == 1 && engine.Messages.Any(m => m.Team == 2 && m.Text.Contains("destroyed", StringComparison.Ordinal)),
            "destroyed: the beam gone, derelict again, team 2 told");
        paid = engine.Teams[2];
        engine.Frames(60);
        Assert.True(engine.Teams[2] == paid, "and team 2 is paid no more");
        engine.Reclaim(100, (600, 3, engine.Named("oil_broken").Single().Id, 0.01));
        Assert.True(engine.Named("oil_centre").SingleOrDefault().F is { Team: 3 } && engine.Named("oil_beam").SingleOrDefault().F is { Team: 3 }, "team 3 repaired it: now it is team 3's");

        // A reload takes it back as team 3's, nodding.
        var again = new MockEngine(s) { Frame = 2000 };
        foreach (var (_, f) in engine.Features)
        {
            int id = again.AddFeature(f.Name, f.X, f.Z, f.Heading);
            again.Features[id].Y = f.Y;
            again.Features[id].Team = f.Team;
        }
        again.Teams[3] = (0, 0);
        again.Load();
        again.Frames(30);
        Assert.True(again.Features.Count == engine.Features.Count && again.Teams[3].Metal >= 2 && again.Named("oil_beam").Single().F.Front != null,
            "after a reload: nothing new made, still team 3's, paying and nodding");
        s.Undo();
        s.Undo();
        Assert.Equal(before, Listing(s.Package.Root), "two undos: as it was");
    }

    [Test]
    public static void BuiltStructuresReplayFromTheJournal()
    {
        var archive = TestData.PathOf(TestData.AcidicQuarryArchive);
        var live = MapEditSession.Open(MapPackage.Open(archive, TestData.NewWorkDir("structures-built-journal-live")));
        var dir = TestData.NewWorkDir("structures-built-journal-record");
        using (var journal = new SessionJournal(Path.Combine(dir, "j.journal"), Path.Combine(dir, "files")))
        {
            live.Journal = journal;
            live.AddStructureKind("arch", StructureType.OpenTunnel, StructureKindSettings.Built(StructureLook.Concrete, StructureType.OpenTunnel));
            live.PlaceStructure("arch", 3000, 3000, 0);
            live.ChangeStructureKind("arch", live.KindSettings(live.Structures.Kinds["arch"]) with { Height = 96 });
            live.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Metal, StructureType.Door));
            live.PlaceStructure("gate", 2000, 2000, 2, broken: true);
            live.Journal = null;
        }
        var rebuilt = MapEditSession.Open(MapPackage.Open(archive, TestData.NewWorkDir("structures-built-journal-rebuilt")));
        SessionJournal.Replay(rebuilt, SessionJournal.Read(Path.Combine(dir, "j.journal")));
        Assert.Equal(Listing(live.Package.Root), Listing(rebuilt.Package.Root), "the replayed package has the same files");
        foreach (var entry in new[] { "objects3d/arch_centre.s3o", "objects3d/gate_centre.s3o", "unittextures/" + StructureModels.Texture1, StructureConfig.Entry })
            Assert.True(Bytes(live, entry).SequenceEqual(Bytes(rebuilt, entry)), $"{entry}: the same bytes");
    }
}
