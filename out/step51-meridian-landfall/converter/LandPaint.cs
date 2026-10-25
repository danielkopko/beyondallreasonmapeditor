// Meridian Landfall's texture, one texel a game unit. Inside the hull (and on its sheer side) every texel is Meridian's
// own painter's, unchanged; the bay doors get hazard chevrons. Outside is moonrock:
//   pale regolith, mottled and speckled, pocked with micro-craters (paint only); the mares round the outside starts dark
//   and smooth, their edges ragged; each crater's floor darker, a bright ejecta blanket round it and, round the bowls and
//   pits, bright rays; on the pits' and the edge's scarps and broken rims, bare basalt in layers with dust on the ledges
//   and cracks at two scales (only broad tones on the steepest faces, where the cast-down texture would smear), stones
//   lying about with lit sides and shadows, cracks running out across the plain; soot from the landing burn along the hull, the engines' blast fanned out behind the stern, and tracks
//   from the bay doors. The void (where VoidV, with a little texel-scale noise, is below zero) is transparent.
// Every noise is read at the port half's mirror image, so the texture is symmetric too.
using MapCore.Imaging;
using MapCore.Textures;

sealed class LandPainter
{
    readonly Surface world;
    readonly Asteroid.Land land;
    readonly Painter ship;
    readonly List<(World.Crater C, bool Starboard, int Type)>[] bins;      // Type: 0 small, 1 bowl, 2 pit
    const int Bin = 512;
    static readonly Noise N1 = new(5201), N2 = new(5202), N3 = new(5203), Streak = new(5205), Edge = new(5206), Grain = new(5207);
    static readonly C Hazard = new(222, 168, 34), HazardDark = new(30, 30, 32);

    public LandPainter(Surface world, Asteroid.Land land)
    {
        this.world = world;
        this.land = land;
        ship = new Painter(new Surface(land.Ship.H, land.Ship.NX, land.Ship.NZ), land.Ship);
        int bx = (int)(World.W / Bin) + 1, bz = (int)(World.H / Bin) + 1;
        bins = new List<(World.Crater, bool, int)>[bx * bz];
        for (int i = 0; i < bins.Length; i++) bins[i] = [];
        void Put(World.Crater c, int type, double reach)
        {
            foreach (bool sb in new[] { false, true })
            {
                var at = sb ? World.Mirror(c.At) : c.At;
                for (int j = Math.Max(0, (int)((at.Z - reach) / Bin)); j <= Math.Min(bz - 1, (int)((at.Z + reach) / Bin)); j++)
                    for (int i = Math.Max(0, (int)((at.X - reach) / Bin)); i <= Math.Min(bx - 1, (int)((at.X + reach) / Bin)); i++)
                        bins[j * bx + i].Add((c, sb, type));
            }
        }
        foreach (var c in World.Small) Put(c, 0, c.R * 1.5);
        foreach (var c in World.Bowls) Put(c, 1, c.R * 4.2);
        foreach (var c in World.Pits) Put(c, 2, c.R * 4.2);
        marks = new List<(P, bool)>[bx * bz];
        for (int i = 0; i < marks.Length; i++) marks[i] = [];
        foreach (var (at, vent) in World.Spots.Select(s => (s.At, false)).Concat(World.Vents.Select(v => (v, true))))
        {
            if (land.ShipCell(at) != Terrain.Space) continue;                  // the ship's are its own painter's
            for (int j = Math.Max(0, (int)((at.Z - 80) / Bin)); j <= Math.Min(bz - 1, (int)((at.Z + 80) / Bin)); j++)
                for (int i = Math.Max(0, (int)((at.X - 80) / Bin)); i <= Math.Min(bx - 1, (int)((at.X + 80) / Bin)); i++)
                    marks[j * bx + i].Add((at, vent));
        }
    }

    readonly List<(P At, bool Vent)>[] marks;

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>
    /// The four pits a side are each blasted into a different rock, with its own ore: its tint, its ore's colour, how many
    /// of its layers carry a seam, how much the seams glint, how much lighter the rock is.
    /// </summary>
    static readonly (C Tint, C Ore, double Seams, double Glitter, double Light)[] PitRocks =
    [
        (new(1.06, 0.98, 0.91), new(198, 96, 50), 0.42, 0.02, 0),         // iron: warm basalt, rust-red seams
        (new(0.95, 1.03, 1.00), new(62, 158, 122), 0.36, 0.03, 4),         // copper: grey-green rock, malachite seams
        (new(0.93, 0.96, 1.07), new(178, 190, 214), 0.40, 0.14, -8),       // ilmenite: dark blue-grey, silvery seams that glint
        (new(1.02, 1.00, 0.95), new(216, 176, 70), 0.30, 0.06, 24),        // anorthosite: pale rock, gold-yellow seams
    ];

    /// <summary>
    /// A pit's wall: its rock in layers 16 deep, each its own tone, thin dark partings between them, warped along the
    /// wall; darker the deeper it lies; dust on the ledges; cracks where it isn't too steep. Some layers carry a seam of
    /// the pit's ore, 2.5 to 6 thick, pinching out and back in along the wall. Returns its grey and its ore (0 to 1).
    /// </summary>
    (double G, double Ore) PitFace(P port, double h, double slope, World.Crater c, double a, int style)
    {
        var rock = PitRocks[style];
        double mx = World.CX - port.X, z = port.Z, along = a * c.R;
        double fine = 1 - 0.85 * Geo.S((slope - 42) / 18), steep = Geo.S((slope - 28) / 24);
        double below = World.Plain - h, y = below + 9 * N1.Fbm(along / 220, 3.3 + c.Key, 2) + 3 * N2.Fbm(along / 60, 7.1, 2);
        const double depth = 16;
        int layer = (int)Math.Floor(y / depth);
        double f = y / depth - layer, edge = Math.Min(f, 1 - f) * depth;
        double g = (92 + rock.Light + 30 * (Hash(layer, c.Key, 71) - 0.5)) * (1 - 0.3 * (0.4 + 0.6 * fine) * (1 - Geo.S(edge / 3)));
        g += 10 * fine * Grain.Fbm(mx / 14, z / 14, 2) + 8 * N3.Fbm(mx / 120, z / 120, 2);
        g *= 1 - 0.28 * Geo.S(below / 160 - 0.3);
        g += (138 + 8 * N2.Fbm(mx / 90, z / 90, 2) - g) * (1 - steep) * 0.7;
        g *= 1 - fine * (0.3 * Fissure(mx, z, 34, 2.2) + 0.15 * Fissure(mx, z, 11, 1.2));
        double ore = 0;
        if (Hash(layer, c.Key, 73) < rock.Seams)
        {
            double thick = 2.5 + 3.5 * Hash(layer, c.Key, 75), centre = depth * (0.3 + 0.4 * Hash(layer, c.Key, 77));
            double pinch = Geo.S((N3.Fbm(along / 70 + layer, 5.5 + c.Key, 2) + 0.15) / 0.3);
            ore = (1 - Geo.S(Math.Abs(f * depth - centre) / thick)) * pinch;
        }
        return (g, ore * (1 - 0.6 * (1 - steep)));
    }

    /// <summary>
    /// The asteroid's edge, worn: layered rock bared where the slope steepens (bands 22 deep, laminated, each its own
    /// tone, lighter and browner than the pits' fresh basalt), dust on the rounded shoulder, the gullies' floors darker
    /// and streaked down the slope, the spurs between them paler, the rills faint.
    /// </summary>
    double EdgeFace(double g, P port, double h, double slope, Asteroid.EdgeHere eh)
    {
        double mx = World.CX - port.X, z = port.Z, d = Math.Max(0, eh.Down);
        double y = World.Plain - h + 10 * N1.Fbm(eh.U / 300, 1.3, 2);
        int layer = (int)Math.Floor(y / 22);
        double band = 116 + 26 * (Hash(layer, 900, 91) - 0.5) + 7 * Math.Sin(y / 5.5);
        double rock = (band + 8 * Grain.Fbm(mx / 20, z / 20, 2) * (1 - 0.7 * Geo.S((slope - 42) / 18))) * (1 - 0.22 * Geo.S((World.Plain - h) / 200));
        double exposed = Geo.S((slope - 16) / 22);
        g += (rock - g) * exposed;
        double streak = Streak.Fbm(eh.U / 5, d / 70, 2);
        g *= 1 - eh.Channel * Geo.S(d / 80) * (0.24 + 0.1 * streak);
        g *= 1 - 0.1 * eh.Rill * Geo.S(d / 30);
        return g + 9 * (1 - eh.Channel) * exposed;
    }

    /// <summary>Cracks: 1 on the borders of jittered cells about g across (the cells warped, so no crack runs straight), falling to 0 within w of them.</summary>
    static double Fissure(double x, double z, double g, double w)
    {
        (x, z) = (x + g * 0.25 * N2.Fbm(x / g, z / g, 2), z + g * 0.25 * N3.Fbm(x / g + 9, z / g, 2));
        int ci = (int)Math.Floor(x / g), cj = (int)Math.Floor(z / g), salt = (int)g;
        double f1 = 1e9, f2 = 1e9;
        for (int j = cj - 1; j <= cj + 1; j++)
            for (int i = ci - 1; i <= ci + 1; i++)
            {
                double px = (i + Hash(i, j, 31 + salt)) * g, pz = (j + Hash(i, j, 61 + salt)) * g;
                double d = Math.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z));
                if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
            }
        return 1 - Geo.S((f2 - f1) / w);
    }

    /// <summary>Broken rock lying about, thicker where density is higher: pebbles and stones, pale on the side toward the sun (−x, −z), dark below.</summary>
    static double Pebbles(double g, double mx, double z, double density, int sideX)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            double cell = pass == 0 ? 6 : 16;
            int ci = (int)Math.Floor(mx / cell), cj = (int)Math.Floor(z / cell);
            if (Hash(ci, cj, 41 + pass) > density * (pass == 0 ? 0.55 : 0.35)) continue;
            double cx = (ci + 0.2 + 0.6 * Hash(ci, cj, 43 + pass)) * cell, cz = (cj + 0.2 + 0.6 * Hash(ci, cj, 45 + pass)) * cell;
            double rad = cell * (0.16 + 0.2 * Hash(ci, cj, 47 + pass));
            double dx = (mx - cx) * -sideX, dz = z - cz, d = Math.Sqrt(dx * dx + dz * dz);
            if (d < rad) g = (dx * 0.8 + dz * 0.6) / rad < -0.25 ? 192 : 64 + 22 * (d / rad);
            else if (d < rad * 1.6 && dx * 0.8 + dz * 0.6 > 0) g *= 0.78;                 // its shadow, away from the sun
        }
        return g;
    }

    static double Hash(int x, int z, int seed)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(seed * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 65535.0;
    }

    public void Row(int z, Span<Rgba> row)
    {
        Rgba[]? shipRow = null;
        int zs = z - (int)World.OZ;
        if (zs >= 0 && zs < (int)Layout.H)
        {
            shipRow = new Rgba[(int)Layout.W];
            ship.Row(zs, shipRow);
        }
        for (int x = 0; x < row.Length; x++)
        {
            var p = new P(x + 0.5, z + 0.5);
            byte kind = Terrain.Nearest(land.Kind, World.NX, World.NZ, p);
            int xs = x - (int)World.OX;
            bool onShip = shipRow != null && xs >= 0 && xs < shipRow.Length;
            if (kind == Asteroid.Hull && onShip) { row[x] = shipRow![xs]; continue; }
            if (kind == Asteroid.HullSide && onShip && shipRow![xs].A == 255 && world.At(world.H, p.X, p.Z) > World.Plain + 10) { row[x] = shipRow[xs]; continue; }
            double shade = world.At(world.Shade, p.X, p.Z);
            if (kind == Asteroid.BayDoor)
            {
                var d = new C(96, 100, 106).Mix((((int)Math.Floor((p.X + p.Z) / 36)) & 1) == 0 ? Hazard : HazardDark, 0.8);
                row[x] = new Rgba(B(d.R * shade), B(d.G * shade), B(d.B * shade), 255);
                continue;
            }
            var (c, alpha) = Rock(p);
            row[x] = alpha == 0 ? new Rgba(0, 0, 0, 0) : new Rgba(B(c.R * shade), B(c.G * shade), B(c.B * shade), 255);
        }
    }

    (C, byte) Rock(P p)
    {
        double mx = Math.Abs(p.X - World.CX), z = p.Z;
        var port = new P(World.CX - mx, z);                    // the port half's mirror image of this texel
        if (land.VoidAt(p) + 14 * Edge.Fbm(mx / 9, z / 9, 2) + 6 * (Hash((int)mx, (int)z, 7) - 0.5) < 0)
            return (new C(0, 0, 0), 0);

        // Regolith: pale grey, mottled, speckled (its pebbles are added further on, with the broken rock's).
        double g = 146 + 16 * N1.Fbm(mx / 900, z / 900, 3) + 9 * N2.Fbm(mx / 150, z / 150, 2) + 10 * (Hash((int)mx, (int)z, 1) - 0.5);

        // The mares round the outside starts: darker, smoother, mottled, their edges ragged.
        foreach (var (_, s) in World.StartsOutsidePort)
        {
            double d = (port - s).Length;
            if (d > World.MareR + 600) continue;
            double edge = World.MareR + 200 * N3.Fbm(mx / 400, z / 400, 3) + 70 * N2.Fbm(mx / 90, z / 90, 2);
            double m = 1 - Geo.S((d - edge) / 220);
            double mare = 112 + 14 * N2.Fbm(mx / 180, z / 180, 3) + 6 * N1.Fbm(mx / 50, z / 50, 2);
            g += (mare - g) * m * 0.85;
        }

        // Micro-craters (paint only): a darker pan inside a brighter lip.
        {
            int ci = (int)Math.Floor(mx / 40), cj = (int)Math.Floor(z / 40);
            for (int j = cj - 1; j <= cj + 1; j++)
                for (int i = ci - 1; i <= ci + 1; i++)
                {
                    if (Hash(i, j, 3) > 0.32) continue;
                    double cx = (i + Hash(i, j, 4)) * 40, cz = (j + Hash(i, j, 5)) * 40, r = 3 + 12 * Math.Pow(Hash(i, j, 6), 2);
                    double d = Math.Sqrt((cx - mx) * (cx - mx) + (cz - z) * (cz - z));
                    if (d < r) g -= 16 * (1 - d / r);
                    else if (d < r * 1.5) g += 12 * (1 - (d - r) / (r * 0.5));
                }
        }

        // The craters: floors darker, an ejecta blanket and (bowls and pits) rays. The pits: bare rock on the scarp,
        // exposed along the rim's ridges and fading out under the regolith, cracks running out, broken rock lying about.
        double h = world.At(world.H, p.X, p.Z), slope = world.At(world.Slope, p.X, p.Z), rubble = 0;
        C tint = new(1, 1, 1), oreColour = default;
        double tintAmount = 0, ore = 0, glitter = 0;
        var bin = bins[(int)(p.Z / Bin) * ((int)(World.W / Bin) + 1) + (int)(p.X / Bin)];
        foreach (var (c, sb, type) in bin)
        {
            var at = sb ? World.Mirror(c.At) : c.At;
            double dx = p.X - at.X, dz = p.Z - at.Z, r = Math.Sqrt(dx * dx + dz * dz);
            double a = Math.Atan2(dz, sb ? -dx : dx);             // the angle at the port crater
            double rj = type == 2 ? World.Jag(c.R, c.Key, a) : World.Waver(c.R, c.Key, a);
            if (type == 2)
            {
                int style = (c.Key - 1000) % PitRocks.Length;
                var rock = PitRocks[style];
                (tint, oreColour, glitter) = (rock.Tint, rock.Ore, rock.Glitter);
                if (r < rj)
                {
                    (g, ore) = PitFace(port, h, slope, c, a, style);
                    tintAmount = 1;
                    continue;
                }
                double rimW = Asteroid.PitRim(c, a), u = (r - rj) / rimW;
                if (u < 1)
                {
                    double exposed = Geo.S(Math.Pow(1 - u, 1.2) * (0.45 + 0.55 * Asteroid.Ridge(port)) * 1.6);
                    var (fg, fo) = PitFace(port, h, slope, c, a, style);
                    g += (fg - g) * exposed;
                    tintAmount = Math.Max(tintAmount, exposed);
                    ore = Math.Max(ore, fo * exposed);
                }
                double near = 1 - (r - rj) / (rimW + c.R * 1.2);
                rubble = Math.Max(rubble, near);
                if (near > 0)                                                   // round flecks of ore thrown out
                {
                    int ci = (int)Math.Floor(mx / 7), cj = (int)Math.Floor(z / 7);
                    if (Hash(ci, cj, 81 + style) < 0.06 * near)
                    {
                        double fx = (ci + 0.3 + 0.4 * Hash(ci, cj, 85)) * 7 - mx, fz = (cj + 0.3 + 0.4 * Hash(ci, cj, 86)) * 7 - z, fr = 0.9 + 0.9 * Hash(ci, cj, 87);
                        if (fx * fx + fz * fz < fr * fr) ore = Math.Max(ore, 0.85);
                    }
                }
            }
            else if (r < rj) { g -= type == 0 ? 10 : 16; continue; }
            double t = (r - rj) / c.R;
            if (type == 0) { if (t < 0.4) g += 10 * (1 - t / 0.4); continue; }
            if (t < 0.7) g += 16 * (1 - t / 0.7);
            double ray = 0;
            for (int k = 0; k < 14; k++)
            {
                double ra = 6.283 * World.Hash01(c.Key, k, 201), width = 0.03 + 0.05 * World.Hash01(c.Key, k, 202), len = 1.5 + 2.2 * World.Hash01(c.Key, k, 203);
                double da = Math.Abs(Math.Atan2(Math.Sin(a - ra), Math.Cos(a - ra)));
                if (da < width && t < len) ray = Math.Max(ray, (1 - da / width) * (1 - t / len));
            }
            g += 30 * ray * (0.7 + 0.3 * Streak.Fbm(mx / 40, z / 40, 2));
            if (type == 2)
                for (int k = 0; k < 11; k++)
                {
                    double ca = 6.283 * World.Hash01(c.Key, k, 211), clen = 0.4 + 1.2 * World.Hash01(c.Key, k, 212);
                    double wob = ca + 0.08 * Math.Sin(t * 9 + k) + 0.03 * Math.Sin(t * 31 + 2 * k);
                    double off = Math.Abs(Math.Atan2(Math.Sin(a - wob), Math.Cos(a - wob))) * r;
                    if (t < clen && off < 2.5 + 2 * (1 - t / clen)) g -= 45 * (1 - t / clen);
                }
        }

        // The asteroid's edge: its worn, gullied slope; scree gathering toward the foot.
        var eh = Asteroid.EdgeAt(port);
        if (eh.Down > -80)
        {
            g = EdgeFace(g, port, h, slope, eh);
            rubble = Math.Max(rubble, 0.25 * Geo.S((eh.Down + 80) / 80) + 0.6 * Geo.S((eh.Down - 120) / 120));
        }
        if (slope < 38) g = Pebbles(g, mx, z, Math.Max(0.1, rubble) * (1 - Geo.S((slope - 24) / 14)), p.X < World.CX ? -1 : 1);

        // Soot from the landing burn along the hull; the engines' blast behind the stern.
        var s2 = World.ToShip(p);
        if (s2.X >= 0 && s2.Z >= 0 && s2.X <= Layout.W && s2.Z <= Layout.H)
        {
            double dh = Terrain.Sample(land.Ship.Outside, land.Ship.NX, land.Ship.NZ, s2);
            if (dh < 380) g *= 1 - 0.42 * Math.Pow(1 - dh / 380, 2) * (0.65 + 0.35 * Streak.Fbm(mx / 18, z / 120, 2));
        }
        double behind = z - (14000 + World.OZ);
        if (behind > 0 && behind < 1800)
            foreach (double bx in new[] { 2000.0, 2800, 5744 })
            {
                double lateral = Math.Abs(mx - Math.Abs(bx + World.OX - World.CX)), spread = 220 + behind * 0.45;
                if (lateral < spread) g *= 1 - 0.6 * (1 - behind / 1800) * (1 - lateral / spread) * (0.7 + 0.3 * Streak.Fbm(lateral / 30, behind / 200, 2));
            }

        // The asteroid's metal: ore exposed in a dark, glittering patch; its vents: a dark throat in a ring of frost.
        foreach (var (at, isVent) in marks[(int)(p.Z / Bin) * ((int)(World.W / Bin) + 1) + (int)(p.X / Bin)])
        {
            double d = (p - at).Length;
            if (d > 70) continue;
            double rim = (isVent ? 52 : 44) + 9 * N3.Fbm(Math.Abs(p.X - World.CX) / 9, z / 9, 2);
            if (isVent)
            {
                if (d < rim - 18) g = 34 + 20 * (d / rim);
                else if (d < rim + 10) g += (196 - g) * 0.8;
            }
            else if (d < rim)
                g = Hash((int)mx, (int)z, 9) > 0.86 ? 205 : 70 + 16 * N2.Fbm(mx / 12, z / 12, 2);
        }

        // Tracks out of the bay doors.
        foreach (var a in World.ApronsPort)
        {
            double zc = (a.Z0 + a.Z1) / 2, out_ = a.X1 - (World.CX - mx);
            if (out_ < 0 || out_ > 1500) continue;
            foreach (double lane in new[] { -150.0, 150 })
            {
                double zt = zc + lane + 50 * Math.Sin(out_ / 300 + lane) * Geo.S((out_ - 500) / 600);
                foreach (double wheel in new[] { -32.0, 32 })
                    if (Math.Abs(z - zt - wheel) < 7) g -= 18 * (1 - Geo.S((out_ - 900) / 600));
            }
        }
        // The pits' own rock, tinted, and their ore (glinting where it does), its colour following the rock's light and shade.
        var colour = new C(g, g * 0.995, g * 0.985 + 2);
        if (tintAmount > 0) colour = colour.Mix(new C(g * tint.R, g * tint.G, g * tint.B), tintAmount);
        if (ore > 0)
        {
            var seam = oreColour * (0.55 + 0.45 * g / 110);
            if (Hash((int)mx, (int)z, 83) < glitter) seam *= 1.5;
            colour = colour.Mix(seam, ore * 0.9);
        }
        return (colour, 255);
    }
}

static class LandDerive
{
    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>The ship's decks and plating catch the light (metal); its faces less; moonrock a little; the void none.</summary>
    public static void Specular(Surface s, Asteroid.Land land, int sizeX, int sizeZ, string path)
    {
        var img = new RgbaImage(sizeX, sizeZ);
        double ux = World.W / sizeX, uz = World.H / sizeZ;
        Parallel.For(0, sizeZ, py =>
        {
            for (int px = 0; px < sizeX; px++)
            {
                var p = new P((px + 0.5) * ux, (py + 0.5) * uz);
                byte kind = Terrain.Nearest(land.Kind, World.NX, World.NZ, p);
                double a = land.VoidAt(p) < 0 ? 0 : kind is Asteroid.Hull or Asteroid.BayDoor ? (s.At(s.Slope, p.X, p.Z) > 40 ? 70 : 150) : 26;
                img.SetPixel(px, py, new Rgba(B(a * 0.35), B(a * 0.36), B(a * 0.4), B(a)));
            }
        });
        img.FlipVertical(); // map .dds files are stored upside down (PLAN.md rule 7)
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    /// <summary>The ground's slopes baked into a normal map (x red, z green, up blue).</summary>
    public static void Normals(Surface s, int sizeX, int sizeZ, string path)
    {
        var img = new RgbaImage(sizeX, sizeZ);
        double ux = World.W / sizeX, uz = World.H / sizeZ;
        Parallel.For(0, sizeZ, py =>
        {
            for (int px = 0; px < sizeX; px++)
            {
                double x = (px + 0.5) * ux, z = (py + 0.5) * uz;
                double dx = (s.At(s.H, x + 8, z) - s.At(s.H, x - 8, z)) / 16, dz = (s.At(s.H, x, z + 8) - s.At(s.H, x, z - 8)) / 16;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                img.SetPixel(px, py, new Rgba(B(127.5 * (1 - dx / len)), B(127.5 * (1 - dz / len)), B(127.5 * (1 + 1 / len)), 255));
            }
        });
        img.FlipVertical();
        DdsFile.Create(DdsFormat.Dxt1, img, true).Save(path);
    }
}
