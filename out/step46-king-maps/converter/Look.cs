// The planets' looks (planets.py): each one's relief, texture, sky and light. Patterns are read in Team A's part
// (Ctx.Own / Ctx.Next, blended by Ctx.W near the lines between parts), so every team's part looks the same.
readonly record struct C(double R, double G, double B)
{
    public static C operator +(C a, C b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    public static C operator *(C a, double k) => new(a.R * k, a.G * k, a.B * k);
    public C Mix(C o, double t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);
    public static C Of((int R, int G, int B) c) => new(c.R, c.G, c.B);
}

/// <summary>What the painter knows about one texel.</summary>
struct Ctx
{
    public double X, Z, H, Slope, Shade, Radius;
    public double Road, Glow, Stair, Deck, Ford, Plaza, KingPlaza, Wall, Top, Cliff, SpotD, VentD, EdgeD, Oasis;
    public double N1, N2, N3, Fine, Grain;
    public Sym.Frame F;
    public double Pattern(Func<P, double> f) => Sym.Value(F, f);
    /// <summary>A pattern with edges (slabs, flags, stripes): read in this part's own frame only, never blended.</summary>
    public double Own(Func<P, double> f) => f(F.Own);
}

abstract class Look
{
    public abstract double Relief(Design d, Sym sym, P p, double h);
    /// <summary>Broad hills and hollows under everything on dry land, roads and bases included (none by default).</summary>
    public virtual double Swell(Design d, Sym sym, P p) => 0;
    public abstract C Colour(Design d, ref Ctx c);
    public virtual double MountainTop => 560;
    public virtual bool Grass => false;
    public abstract (double R, double G, double B) SkyTop { get; }
    public abstract (double R, double G, double B) SkyHorizon { get; }
    /// <summary>mapinfo values beyond the planet's colours and weather: light, water.</summary>
    public abstract Dictionary<string, string> MapInfo { get; }
    /// <summary>Oases or other places the texture and the features both use (none by default).</summary>
    public virtual List<P> Groves(Design d, Terrain.Result t) => [];

    protected static readonly Noise A1 = new(201), A2 = new(202), A3 = new(203), A4 = new(204), A5 = new(205);

    protected static double Frac(double v) => v - Math.Floor(v);

    /// <summary>A hash of a cell in [0, 1].</summary>
    protected static double Hash(long x, long z)
    {
        ulong h = (ulong)(x * 73856093) ^ (ulong)(z * 19349663) ^ 0x9E3779B97F4A7C15;
        h ^= h >> 31; h *= 0xBF58476D1CE4E5B9; h ^= h >> 29;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }

    /// <summary>The common finish: spots and vents ringed, made things, then the light baked in.</summary>
    protected static C Finish(C c, in Ctx x, C spotCore, C spotRing, C ventCore, C ventRing)
    {
        if (x.SpotD < 62)
        {
            c = c.Mix(spotRing, 0.8 * (1 - Geo.S((x.SpotD - 30) / 30)) * Geo.S((x.SpotD - 18) / 12));
            c = c.Mix(spotCore * (1 + 0.12 * x.Grain), 1 - Geo.S((x.SpotD - 24) / 8));
        }
        if (x.VentD < 78)
        {
            c = c.Mix(ventRing, 0.75 * (1 - Geo.S((x.VentD - 34) / 40)) * Geo.S((x.VentD - 20) / 14));
            c = c.Mix(ventCore, 1 - Geo.S((x.VentD - 28) / 8));
        }
        return c * (x.Shade * (1 + 0.04 * x.Grain));
    }
}

/// <summary>Dune: a sand-sea world. Rippled golden dunes, wind-polished sandstone ridges, pale stone flags in the fort.</summary>
sealed class Dune : Look
{
    static readonly P Wind = new(0.6, 0.8);

    static double DuneShape(P q)
    {
        double u = q.X * Wind.X + q.Z * Wind.Z;
        double ph = (u + 180 * A1.Fbm(q.X / 900, q.Z / 900, 3)) / 640;
        double f = Frac(ph);
        double prof = Geo.S(f < 0.72 ? f / 0.72 : (1 - f) / 0.28);
        double amp = 34 * (0.75 + 0.35 * A2.Fbm(q.X / 1500, q.Z / 1500, 2));
        return amp * (prof - 0.5) + 4 * A3.Fbm(q.X / 130, q.Z / 130, 2);
    }

    public override double Relief(Design d, Sym sym, P p, double h) => sym.Value(p, DuneShape);
    public override double MountainTop => 520;

    public override (double R, double G, double B) SkyTop => (0.3, 0.5, 0.88);
    public override (double R, double G, double B) SkyHorizon => (0.93, 0.85, 0.68);
    public override Dictionary<string, string> MapInfo => new()
    {
        ["lighting.groundAmbientColor"] = "0.5,0.46,0.4", ["lighting.groundDiffuseColor"] = "1.12,1.05,0.94",
        ["lighting.groundSpecularColor"] = "0.35,0.3,0.24", ["lighting.unitAmbientColor"] = "0.55,0.52,0.47",
        ["lighting.unitDiffuseColor"] = "1.05,1,0.9", ["custom.fog.color"] = "0.95,0.86,0.68",
    };

    /// <summary>Two oases flanking each ramp's foot, outside the fort.</summary>
    public override List<P> Groves(Design d, Terrain.Result t)
    {
        var list = new List<P>();
        foreach (var r in t.Ramps)
        {
            var side = r.Dir.Perp;
            list.Add(r.Foot - r.Dir * 300 + side * 560);
            list.Add(r.Foot - r.Dir * 300 - side * 560);
        }
        return list;
    }

    static readonly C SandShadow = new(186, 146, 92), SandLight = new(232, 200, 144), Dirt = new(204, 164, 110), Rock = new(176, 134, 88);
    static readonly C Flag = new(216, 200, 166), Block = new(198, 168, 122), Road = new(180, 148, 102), Stair = new(212, 192, 154);
    static readonly C Damp = new(156, 136, 88), Green = new(122, 134, 72), Trodden = new(196, 160, 108);

    public override C Colour(Design d, ref Ctx x)
    {
        double flat = 1 - Geo.S((x.Slope - 12) / 10);
        var c = SandShadow.Mix(SandLight, 0.5 + 0.5 * x.N1);
        var xx = x;
        double ripple = x.Pattern(q => Math.Sin(2 * Math.PI * ((q.X * Wind.X + q.Z * Wind.Z) + 30 * xx.N2) / 19));
        c *= 1 + 0.05 * ripple * flat * (1 - x.Road) * (1 - x.Top);
        c *= 0.97 + 0.05 * x.Fine;
        c = c.Mix(Dirt * 0.92, Geo.S((x.Slope - 16) / 12) * 0.6);
        double wr = Geo.S((x.Slope - 32) / 10);
        var rock = Rock * (1 + 0.14 * Math.Sin(x.H / 8 + x.N1 * 2.5)) * (0.92 + 0.08 * x.Fine);
        c = c.Mix(rock, wr);
        if (x.Oasis > 0)
        {
            c = c.Mix(Damp, x.Oasis * 0.6 * (1 - wr));
            c = c.Mix(Green, x.Oasis * Geo.S((x.N3 + 0.1) / 0.3) * 0.45 * (1 - wr));
        }
        if (x.Top > 0)
        {
            double joint = x.Own(q => Frac(q.X / 48) < 0.05 || Frac(q.Z / 48) < 0.05 ? 0.84 : 1);
            var stone = Flag * joint * (0.95 + 0.05 * x.Grain);
            c = c.Mix(stone.Mix(SandLight, Geo.S(x.N2 * 2 + 0.2) * 0.35), x.Top * (1 - wr));
        }
        if (x.Wall > 0)
            c = c.Mix(Block * (Frac(x.H / 11) < 0.12 ? 0.84 : 1) * (0.94 + 0.06 * x.Fine), x.Wall);
        c = c.Mix(Trodden, x.Plaza * 0.3 * (1 - wr));
        c = c.Mix(Road * 0.94, x.Glow * (1 - x.Road) * 0.35 * (1 - wr));
        c = c.Mix(Road * (0.95 + 0.05 * x.Grain), x.Road * (1 - wr));
        if (x.Stair > 0) c = c.Mix(Stair * (x.Slope > 6 && x.H % 6 < 1.1 ? 0.8 : 1), x.Stair);
        return Finish(c, x, new C(64, 48, 38), new C(150, 100, 64), new C(40, 28, 22), new C(226, 120, 44));
    }
}

/// <summary>Heath: a highland moor. Rolling hills of heather and moor grass, peat and green flushes in the hollows,
/// bracken on the slopes, granite tors and keep cliffs, a black-peat loch crossed by stone causeways.</summary>
sealed class Heath : Look
{
    public override double Swell(Design d, Sym sym, P p) => sym.Value(p, q => 80 * A4.Fbm(q.X / 2200, q.Z / 2200, 3));
    public override double Relief(Design d, Sym sym, P p, double h) =>
        sym.Value(p, q => 9 * A1.Fbm(q.X / 520, q.Z / 520, 3) + 3 * A2.Fbm(q.X / 140, q.Z / 140, 2));
    public override bool Grass => true;
    public override (double R, double G, double B) SkyTop => (0.5, 0.56, 0.65);
    public override (double R, double G, double B) SkyHorizon => (0.75, 0.78, 0.8);
    public override Dictionary<string, string> MapInfo => new()
    {
        ["lighting.groundAmbientColor"] = "0.46,0.48,0.52", ["lighting.groundDiffuseColor"] = "0.96,0.98,1.02",
        ["lighting.groundSpecularColor"] = "0.3,0.32,0.34", ["lighting.unitAmbientColor"] = "0.52,0.54,0.58",
        ["lighting.unitDiffuseColor"] = "0.95,0.97,1", ["custom.fog.color"] = "0.72,0.76,0.8",
        ["water.baseColor"] = "0.1,0.16,0.2", ["water.minColor"] = "0.02,0.03,0.04", ["water.surfaceColor"] = "0.4,0.48,0.55",
        ["water.absorb"] = "0.02,0.015,0.01", ["water.specularColor"] = "0.6,0.65,0.7",
    };

    static readonly C GrassG = new(96, 114, 60), MoorGrass = new(134, 124, 82), HeatherC = new(88, 64, 74), Bloom = new(130, 88, 124);
    static readonly C Peat = new(68, 54, 42), Bracken = new(146, 98, 56);
    static readonly C Granite = new(116, 114, 108), Lichen = new(150, 150, 96), Turf = new(104, 122, 70), Flags = new(128, 126, 120);
    static readonly C ShorePeat = new(46, 40, 34), Under = new(34, 40, 40), Deep = new(22, 28, 30), Track = new(124, 104, 80), Stair = new(134, 132, 126);
    static readonly C Trodden = new(110, 106, 76), Sett = new(118, 116, 110);

    public override C Colour(Design d, ref Ctx x)
    {
        C c;
        if (x.H < 0)
            c = Under.Mix(Deep, Geo.S(-x.H / 40));
        else
        {
            // How high the moor stands here: heather on the dry rises, grass, peat and green flushes in the hollows.
            double rise = Math.Clamp((x.H - d.GroundH) / 40, -1.5, 1.5);
            double steep = Geo.S((x.Slope - 22) / 8);
            c = MoorGrass.Mix(GrassG, Geo.S((-rise * 0.6 + x.N3 * 0.8 + 0.1) / 0.5)) * (0.93 + 0.08 * x.N1);
            var canopy = HeatherC.Mix(Bloom, Geo.S((x.N3 - 0.05) / 0.25) * 0.7) * (0.86 + 0.16 * x.Fine + 0.05 * x.Grain);
            double heather = Geo.S((x.N2 * 0.9 + x.N1 * 0.4 + rise * 0.3 + 0.12 * x.Fine + 0.05) / 0.22) * (1 - steep);
            c = c.Mix(canopy, heather * 0.9);
            c = c.Mix(Peat * (0.86 + 0.16 * x.Fine), Geo.S((-x.N1 * 0.5 - x.N3 * 0.9 - rise * 0.2 - 0.36) / 0.08) * 0.85);
            c = c.Mix(Bracken * (0.9 + 0.12 * x.Fine), Geo.S((x.Slope - 6) / 4) * (1 - Geo.S((x.Slope - 22) / 6)) * Geo.S((x.N3 - 0.08) / 0.14) * 0.7);
            c *= 1 + 0.05 * x.Fine;
            double wr = Geo.S((x.Slope - 32) / 10);
            var rock = Granite * (0.9 + 0.12 * x.Fine) * (1 + 0.07 * Math.Sin(x.H / 14 + x.N1 * 3));
            if (x.Fine > 0.55) rock = rock.Mix(Lichen, 0.4);
            c = c.Mix(rock, Math.Max(wr, x.Cliff * Geo.S((x.Slope - 18) / 10)));
            // The keeps: short turf, heather at the rims, granite breaking through.
            if (x.Top > 0)
            {
                var turf = Turf * (0.93 + 0.08 * x.N2 + 0.04 * x.Fine);
                turf = turf.Mix(canopy, Geo.S((220 - x.EdgeD) / 120) * Geo.S((x.N2 + 0.15) / 0.2) * 0.85);
                turf = turf.Mix(Granite * (0.9 + 0.12 * x.Fine), Geo.S((x.N3 - 0.22) / 0.07) * 0.8);
                c = c.Mix(turf, x.Top * (1 - wr));
            }
            double joint = x.Own(q => Frac(q.X / 40) < 0.06 || Frac(q.Z / 40) < 0.06 ? 0.82 : 1);
            c = c.Mix(Flags * joint, x.KingPlaza * (1 - wr) * 0.8);
            if (x.H < 14) c = c.Mix(ShorePeat, (1 - Geo.S((x.H - 3) / 11)) * (1 - wr));
            c = c.Mix(Trodden, x.Plaza * 0.35 * (1 - wr));
            c = c.Mix(Track * 0.9, x.Glow * (1 - x.Road) * 0.3);
            c = c.Mix(Track * (0.94 + 0.06 * x.Grain), x.Road * (1 - wr));
            if (x.Stair > 0) c = c.Mix(Stair * (x.Slope > 6 && x.H % 6 < 1.1 ? 0.8 : 1), x.Stair);
        }
        // Causeways of granite setts.
        if (x.Deck > 0 && x.H >= Terrain.Deck - 1)
        {
            double sett = x.Own(q => Frac(q.X / 28) < 0.08 || Frac(q.Z / 20) < 0.1 ? 0.76
                : 0.9 + 0.18 * Hash((long)Math.Floor(q.X / 28), (long)Math.Floor(q.Z / 20)));
            c = c.Mix(Sett * sett * (0.96 + 0.05 * x.Grain), x.Deck);
        }
        return Finish(c, x, new C(52, 52, 56), new C(96, 96, 100), new C(40, 40, 44), new C(200, 200, 192));
    }
}

/// <summary>Forge: an industrial wasteland. Cracked concrete and rust, oil-dark ground, paved yards, acid lakes.</summary>
sealed class Forge : Look
{
    public override double Relief(Design d, Sym sym, P p, double h) =>
        sym.Value(p, q => 3 * A1.Fbm(q.X / 500, q.Z / 500, 2) + 90 * Math.Max(0, A2.Fbm(q.X / 700, q.Z / 700, 3) - 0.3));
    public override (double R, double G, double B) SkyTop => (0.42, 0.37, 0.3);
    public override (double R, double G, double B) SkyHorizon => (0.74, 0.63, 0.44);
    public override Dictionary<string, string> MapInfo => new()
    {
        ["lighting.groundAmbientColor"] = "0.46,0.43,0.38", ["lighting.groundDiffuseColor"] = "1,0.93,0.82",
        ["lighting.groundSpecularColor"] = "0.35,0.32,0.28", ["lighting.unitAmbientColor"] = "0.52,0.49,0.44",
        ["lighting.unitDiffuseColor"] = "1,0.94,0.85", ["custom.fog.color"] = "0.62,0.56,0.42",
        ["water.baseColor"] = "0.45,0.6,0.1", ["water.minColor"] = "0.06,0.1,0.01", ["water.surfaceColor"] = "0.7,0.8,0.35",
        ["water.absorb"] = "0.012,0.006,0.03", ["water.specularColor"] = "0.8,0.9,0.4",
    };

    static readonly C Joint = new(88, 86, 82), Gravel = new(120, 110, 96), Rust = new(148, 88, 52), Oil = new(44, 42, 40);
    static readonly C Yard = new(172, 168, 160), Yellow = new(220, 176, 40), Black = new(40, 40, 40), TileA = new(150, 92, 70), TileB = new(128, 80, 62);
    static readonly C Crust = new(176, 186, 72), Sludge = new(90, 120, 30), SludgeDeep = new(58, 82, 22), Panel = new(130, 128, 124);
    static readonly C Asphalt = new(62, 62, 64), Kerb = new(150, 150, 148), Steel = new(116, 118, 122);

    static double Slab(P q, double size, out bool joint)
    {
        double fx = q.X / size, fz = q.Z / size;
        joint = Frac(fx) < 3 / size || Frac(fz) < 3 / size;
        return Hash((long)Math.Floor(fx), (long)Math.Floor(fz));
    }

    public override C Colour(Design d, ref Ctx x)
    {
        C c;
        if (x.H < 0)
            c = Sludge.Mix(SludgeDeep, Geo.S(-x.H / 40));
        else
        {
            double big = x.Top > 0.5 ? 128 : 96;
            double tone = x.Own(q => Slab(q, big, out bool j) * (j ? -1 : 1));
            bool joint = tone < 0;
            c = new C(140, 136, 128).Mix(new C(168, 164, 156), Math.Abs(tone));
            if (joint) c = Joint;
            double crack = x.Pattern(q => Math.Abs(A4.Fbm(q.X / 70, q.Z / 70, 2)));
            if (crack < 0.025) c = c.Mix(Joint, 0.8);
            c = c.Mix(Gravel * (0.9 + 0.15 * x.Grain), Geo.S((-x.N1 - 0.15) / 0.2) * (1 - x.Top));
            c = c.Mix(Rust, Geo.S((x.N2 - 0.3) / 0.15) * 0.6);
            c = c.Mix(Oil, Geo.S((x.N3 - 0.5) / 0.1) * 0.7 * (1 - x.Top));
            if (x.Top > 0)
            {
                c = c.Mix(Yard * (joint ? 0.6 : 0.96 + 0.06 * Math.Abs(tone)), x.Top * 0.8);
                if (x.EdgeD < 56)
                {
                    double stripe = x.Own(q => Frac((q.X + q.Z) / 44) < 0.5 ? 1 : 0);
                    c = c.Mix(Yellow.Mix(Black, 1 - stripe), x.Top * (1 - Geo.S((x.EdgeD - 40) / 16)));
                }
            }
            if (x.Radius < 56 * d.Upp)
            {
                double check = x.Own(q => Math.Abs(Math.Floor(q.X / 32) + Math.Floor(q.Z / 32)) % 2 == 0 ? 1 : 0);
                c = c.Mix(TileA.Mix(TileB, check), 1 - Geo.S((x.Radius - 52 * d.Upp) / (4 * d.Upp)));
            }
            if (x.H < 10) c = c.Mix(Crust, (1 - Geo.S((x.H - 2) / 8)) * 0.85);
            double wr = Geo.S((x.Slope - 32) / 10);
            c = c.Mix(Gravel * 0.8, wr * 0.7);
            if (x.Wall > 0) c = c.Mix(Panel * (x.H % 24 < 1.5 ? 0.72 : 1) * (0.95 + 0.05 * x.Fine), x.Wall);
            c = c.Mix(Kerb, x.Glow * (1 - x.Road) * 0.45);
            c = c.Mix(Asphalt * (0.95 + 0.08 * x.Grain), x.Road);
            if (x.Stair > 0) c = c.Mix(Steel * (x.Slope > 6 && x.H % 6 < 1.1 ? 0.75 : 1), x.Stair);
        }
        return Finish(c, x, new C(40, 42, 46), new C(156, 92, 50), new C(30, 30, 30), new C(226, 206, 70));
    }
}
