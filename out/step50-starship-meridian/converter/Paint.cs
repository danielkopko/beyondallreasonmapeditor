// The ship's texture, one texel a game unit, and its detail maps and sky. Space is transparent (alpha 0: with
// voidGround the engine doesn't draw it and the starfield sky shows); the hull's own sheer side is opaque. Decks are
// plated in 64-unit panels with seams, rivets, wear, oil and scorch marks; the hangars have landing circles and guide
// lines in the side's colour (port red, starboard green, a ship's navigation colours), the passages edge stripes, the
// doors hazard chevrons, the bridge a dark blue deck, the CIC a lit grid, the reactor's well glowing rings round the
// core. Bulkhead tops and the rim are hull plating; their faces darker ribs.
using System.Buffers.Binary;
using MapCore.Imaging;
using MapCore.Textures;

readonly record struct C(double R, double G, double B)
{
    public static C operator *(C a, double k) => new(a.R * k, a.G * k, a.B * k);
    public C Mix(C o, double t) { t = Math.Clamp(t, 0, 1); return new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t); }
}

/// <summary>Slope (degrees) and baked light per heightmap vertex.</summary>
sealed class Surface
{
    public readonly int NX, NZ;
    public readonly float[] H, Slope, Shade;

    public Surface(float[] heights, int nx, int nz)
    {
        NX = nx; NZ = nz; H = heights;
        Slope = new float[nx * nz];
        Shade = new float[nx * nz];
        double lx = -0.5, ly = 0.75, lz = -0.42, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ll; ly /= ll; lz /= ll;
        Parallel.For(0, nz, z =>
        {
            for (int x = 0; x < nx; x++)
            {
                double dx = (V(x + 1, z) - V(x - 1, z)) / 16.0, dz = (V(x, z + 1) - V(x, z - 1)) / 16.0;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                Slope[z * nx + x] = (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI);
                Shade[z * nx + x] = (float)(0.82 + 0.3 * Math.Clamp((-dx * lx + ly - dz * lz) / len, 0, 1));
            }
        });
    }

    float V(int x, int z) => H[Math.Clamp(z, 0, NZ - 1) * NX + Math.Clamp(x, 0, NX - 1)];
    public double At(float[] f, double x, double z) => Terrain.Sample(f, NX, NZ, new P(x, z));
}

sealed class Painter(Surface s, Terrain.Result t)
{
    static readonly Noise N1 = new(2001), N2 = new(2002), N3 = new(2003), Stain = new(2004), Scorch = new(2005);

    static readonly C Deck = new(112, 116, 122), DeckDark = new(84, 88, 94), Seam = new(46, 48, 52), Rivet = new(150, 154, 160);
    static readonly C HangarDeck = new(128, 132, 136), BridgeDeck = new(58, 70, 92), CicDeck = new(70, 82, 96);
    static readonly C Hull = new(92, 96, 104), HullDark = new(60, 63, 70), HullTop = new(104, 108, 116), Rib = new(52, 54, 60);
    static readonly C Hazard = new(222, 168, 34), HazardDark = new(30, 30, 32), Crossing = new(232, 120, 36);
    static readonly C Port = new(196, 52, 44), Starboard = new(44, 170, 76), Cyan = new(80, 230, 245), Oil = new(34, 34, 38);
    static readonly C WellFloor = new(44, 52, 62);

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    public void Row(int z, Span<Rgba> row)
    {
        for (int x = 0; x < row.Length; x++)
        {
            var (c, a) = Colour(x + 0.5, z + 0.5);
            row[x] = new Rgba(B(c.R), B(c.G), B(c.B), a);
        }
    }

    static double Hash(int x, int z, int seed)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(seed * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 65535.0;
    }

    /// <summary>The side a point is on, for its trim colour: port red, starboard green.</summary>
    static C SideColour(double x) => x < Layout.CX ? Port : Starboard;

    /// <summary>The mirror image's coordinates for the noise, so wear and stains are mirrored too.</summary>
    static double Mx(double x) => x <= Layout.CX ? x : Layout.W - x;

    (C, byte) Colour(double x, double z)
    {
        var p = new P(x, z);
        double mx = Mx(x);
        int cell = Terrain.Nearest(t.Cell, t.NX, t.NZ, p);
        double outside = s.At(t.Outside, x, z);
        double h = s.At(s.H, x, z), sl = s.At(s.Slope, x, z), shade = s.At(s.Shade, x, z);
        double n1 = N1.Fbm(mx / 600, z / 600, 3), n2 = N2.Fbm(mx / 120, z / 120, 2), n3 = N3.Fbm(mx / 30, z / 30, 2);

        // Space: transparent, but for the hull's own sheer side.
        if (cell == Terrain.Space && (outside > 64 || h < Layout.Space + 45))
            return (new C(0, 0, 0), 0);

        // The 64-unit panel grid, its seams and rivets.
        double px = mx / 64, pz = z / 64;
        double fx = px - Math.Floor(px), fz = pz - Math.Floor(pz);
        double edge = Math.Min(Math.Min(fx, 1 - fx), Math.Min(fz, 1 - fz)) * 64;
        double tone = Hash((int)Math.Floor(px), (int)Math.Floor(pz), 3) - 0.5;
        bool rivet = edge > 3 && edge < 6 && (Math.Abs(fx * 64 - 4.5) < 1.5 || Math.Abs(fx * 64 - 59.5) < 1.5) && (Math.Abs(fz * 64 - 4.5) < 1.5 || Math.Abs(fz * 64 - 59.5) < 1.5);

        bool floor = Terrain.Nearest(t.Floor, t.NX, t.NZ, p);
        C c;
        if (!floor || sl > 40)
        {
            // Hull plating on the tops, darker ribs down the faces.
            var plate = HullPlate(h > Layout.Bulkhead + 20 ? HullTop : Hull, mx, z) * (0.96 + 0.05 * n2);
            double face = Geo.S((sl - 40) / 25);
            var faceC = Rib.Mix(Hull, 0.35 + 0.3 * Math.Abs(Math.Sin(mx / 9 + z / 9))) * (0.9 + 0.1 * n2);
            c = plate.Mix(faceC, face);
            // The rim's navigation lights, red to port and green to starboard, every 512 along the hull.
            if (cell != Terrain.Space && s.At(t.Inside, x, z) < 40 && Math.Abs(z % 512 - 256) < 10)
                c = c.Mix(SideColour(x) * 1.2, 0.85);
        }
        else
        {
            var info = cell >= 0 ? Layout.Cells[cell] : null;
            string name = info?.Name ?? "";
            var deck = name switch
            {
                "Forward Hangar" or "Main Hangar" => HangarDeck,
                "Bridge" or "Bridge Landing" => BridgeDeck,
                "CIC" => CicDeck,
                _ => Deck,
            };
            c = deck * (1 + 0.07 * tone) * (0.95 + 0.06 * n2);
            if (edge < 1.2) c = c.Mix(Seam, 0.75);
            if (rivet) c = c.Mix(Rivet, 0.45);
            // Wear down the middle of traffic, oil stains, the odd scorch.
            c = c.Mix(DeckDark, Geo.S((n1 + 0.1) / 0.3) * 0.25);
            c = c.Mix(Oil, Geo.S((Stain.Fbm(mx / 110, z / 110, 2) - 0.56) / 0.06) * 0.35);
            c = c.Mix(new C(24, 22, 22), Geo.S((Scorch.Fbm(mx / 160, z / 160, 3) - 0.58) / 0.06) * 0.45 * (0.6 + 0.4 * n3));
            if (info != null) c = Room(c, info, p, mx);
            int door = Terrain.Nearest(t.Door, t.NX, t.NZ, p);
            if (door >= 0) c = Door(c, door, p);
        }

        // Metal spots: a round extraction hatch; vents: a grated exhaust.
        foreach (var (at, _, _) in Layout.Spots)
        {
            double d = (p - at).Length;
            if (d > 70) continue;
            c = c.Mix(new C(70, 74, 80), 1 - Geo.S((d - 52) / 6)).Mix(new C(150, 156, 164), (1 - Geo.S(Math.Abs(d - 50) / 3)) * 0.8)
                 .Mix(new C(26, 28, 30), 1 - Geo.S((d - 30) / 4));
            if (d < 30 && Math.Abs(Math.Sin((p.X - at.X) / 5)) < 0.25) c = c.Mix(new C(90, 96, 104), 0.6);
        }
        foreach (var (at, _) in Layout.Vents)
        {
            double d = (p - at).Length;
            if (d > 90) continue;
            c = c.Mix(new C(232, 170, 70), (1 - Geo.S(Math.Abs(d - 70) / 6)) * 0.8).Mix(new C(20, 20, 22), 1 - Geo.S((d - 56) / 6));
            if (d < 56 && (Math.Abs(Math.Sin((p.X - at.X) / 6)) < 0.3 || Math.Abs(Math.Sin((p.Z - at.Z) / 6)) < 0.3)) c = c.Mix(new C(80, 70, 60), 0.7);
        }
        return (c * shade, 255);
    }

    /// <summary>
    /// Hull plating: 256 × 128 plates in staggered courses, each a shade apart, seamed, with a lit lip and a row of
    /// rivets inside each seam; here and there a plate carries an access hatch or a vent grille.
    /// </summary>
    static C HullPlate(C baseC, double mx, double z)
    {
        double rz = z / 128, row = Math.Floor(rz);
        double rx = (mx + ((long)row % 2 == 0 ? 0 : 128)) / 256, col = Math.Floor(rx);
        double ex = (rx - col) * 256, ez = (rz - row) * 128;
        double toX = Math.Min(ex, 256 - ex), toZ = Math.Min(ez, 128 - ez), edge = Math.Min(toX, toZ);
        var c = baseC * (1 + 0.07 * (Hash((int)col, (int)row, 7) - 0.5));
        if (Math.Abs(ex % 64) < 0.8) c = c.Mix(Seam, 0.15);                              // the plate's own faint panel lines
        if (edge < 1.5) return c.Mix(Seam, 0.75);
        if (edge < 3) c *= 1.06;
        double along = toX < toZ ? ez : ex;
        if (Math.Abs(edge - 6) < 1.2 && Math.Abs(along % 16 - 8) < 1.2) c = c.Mix(Rivet, 0.5);
        double k = Hash((int)col, (int)row, 11);
        if (k < 0.07 && toX > 64 && toZ > 26)
        {
            // An access hatch: a dark inset with a light frame, a handle.
            c = Math.Min(toX - 64, toZ - 26) < 2.5 ? c.Mix(Rivet, 0.45) : c.Mix(HullDark, 0.55);
            if (Math.Abs(ez - 64) < 3 && Math.Abs(ex - 128) < 14) c = c.Mix(Hazard, 0.7);
        }
        else if (k < 0.12 && toX > 80 && toZ > 40)
            c = Math.Min(toX - 80, toZ - 40) < 2 ? c.Mix(Rivet, 0.35) : c.Mix(((int)Math.Floor(ex / 6) & 1) == 0 ? Seam : HullDark, 0.75);
        return c;
    }

    /// <summary>A room's own markings.</summary>
    static C Room(C c, Layout.Cell info, P p, double mx)
    {
        var side = SideColour(p.X);
        var (min, max) = Bounds(info.Outline);
        double cx = (min.X + max.X) / 2, cz = (min.Z + max.Z) / 2;
        switch (info.Name)
        {
            case "Forward Hangar" or "Main Hangar":
            {
                // Two landing circles, a guide line down the hangar between them, hazard edging round the deck.
                foreach (double oz in new[] { -0.25, 0.25 })
                {
                    var at = new P(cx, cz + (max.Z - min.Z) * oz);
                    double d = (p - at).Length;
                    if (Math.Abs(d - 330) < 9 || Math.Abs(d - 300) < 3) c = c.Mix(side, 0.8);
                    if (d < 300 && Math.Abs(Math.Abs(p.X - at.X) - Math.Abs(p.Z - at.Z)) < 6 && d > 120) c = c.Mix(new C(220, 220, 214), 0.6);
                    if (Math.Abs(d - 120) < 4) c = c.Mix(new C(220, 220, 214), 0.7);
                }
                if (Math.Abs(p.X - cx) < 6) c = c.Mix(new C(230, 190, 40), 0.7);
                double inset = Math.Min(Math.Min(p.X - min.X, max.X - p.X), Math.Min(p.Z - min.Z, max.Z - p.Z));
                if (inset > 60 && inset < 84) c = c.Mix(((int)Math.Floor((p.X + p.Z) / 40) & 1) == 0 ? Hazard : HazardDark, 0.75);
                break;
            }
            case "Passage":
            {
                // Edge stripes in the side's colour, chevrons pointing fore and aft every 400.
                double a = Math.Min(p.X - min.X, max.X - p.X);
                if (a > 46 && a < 60) c = c.Mix(side, 0.75);
                double f = ((p.Z % 400) + 400) % 400;
                if (f < 70 && Math.Abs(Math.Abs(p.X - cx) - (f - 35) * 2.2) < 8 && Math.Abs(p.X - cx) < 140) c = c.Mix(new C(210, 214, 220), 0.6);
                break;
            }
            case "CIC":
            {
                // A lit grid, and the ring round the holo-table.
                if ((((p.X % 128) + 128) % 128 < 3 || ((p.Z % 128) + 128) % 128 < 3)) c = c.Mix(Cyan * 0.8, 0.35);
                double d = (p - new P(Layout.CX, cz)).Length;
                if (Math.Abs(d - 420) < 10) c = c.Mix(Cyan, 0.8);
                if (Math.Abs(d - 470) < 4) c = c.Mix(Cyan * 0.8, 0.6);
                break;
            }
            case "Bridge" or "Bridge Landing":
            {
                if ((((p.Z % 96) + 96) % 96) < 3) c = c.Mix(Cyan * 0.6, 0.3);
                break;
            }
            case "Main Reactor":
            {
                double r = (p - Layout.WellAt).Length;
                if (r < Layout.WellR - 40)
                {
                    c = WellFloor.Mix(c, 0.3);
                    foreach (double ring in new[] { 260.0, 380.0, 500.0 })
                        if (Math.Abs(r - ring) < 9) c = c.Mix(Cyan, 0.9);
                }
                else if (r < Layout.WellR + 70)
                    c = c.Mix(((int)Math.Floor(Math.Atan2(p.Z - Layout.WellAt.Z, p.X - Layout.WellAt.X) * 40 / Math.PI) & 1) == 0 ? Hazard : HazardDark, 0.8);
                break;
            }
            case "Shield Generator":
            {
                double d = (p - new P(Layout.CX, cz)).Length;
                if (Math.Abs(d - 520) < 8 || Math.Abs(d - 560) < 4) c = c.Mix(new C(120, 160, 255), 0.7);
                break;
            }
            case "Engine Room":
            {
                // Grating over the drive, framed.
                double gx = Math.Abs(p.X - Layout.CX);
                if (gx < 260 && p.Z > 11700 && p.Z < 13000)
                {
                    bool bar = (((p.Z % 24) + 24) % 24) < 4 || gx % 24 < 4;
                    c = gx > 250 || p.Z < 11710 || p.Z > 12990 ? c.Mix(Hazard, 0.7) : c.Mix(bar ? new C(80, 80, 78) : new C(30, 30, 32), 0.75);
                }
                break;
            }
        }
        return c;
    }

    /// <summary>A door: hazard chevrons across its threshold (orange at the centreline crossings and the spine).</summary>
    static C Door(C c, int door, P p)
    {
        var d = Layout.Doors[door];
        if (d.Kind == Layout.DoorKind.Stairs)
            return c.Mix(new C(74, 78, 86), 0.6).Mix(new C(150, 156, 164), ((((p.Z - d.Z0) % 14) + 14) % 14 < 2) ? 0.7 : 0);
        bool across = d.X1 - d.X0 < d.Z1 - d.Z0;                    // a wall running north-south: the door opens east-west
        double along = across ? p.Z : p.X;
        var colour = d.Kind is Layout.DoorKind.Crossing or Layout.DoorKind.Spine ? Crossing : Hazard;
        return c.Mix((((int)Math.Floor((along + (across ? p.X : p.Z)) / 36)) & 1) == 0 ? colour : HazardDark, 0.8);
    }

    static (P Min, P Max) Bounds(P[] poly) => (new P(poly.Min(q => q.X), poly.Min(q => q.Z)), new P(poly.Max(q => q.X), poly.Max(q => q.Z)));
}

static class Derive
{
    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>No detail textures on a ship (the template's are a jungle's): the splat distribution all zero.</summary>
    public static void Splat(int size, string path)
    {
        var img = new RgbaImage(size, size);
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    /// <summary>Decks and plating catch the light (metal); faces less; space none.</summary>
    public static void Specular(Surface s, Terrain.Result t, int sizeX, int sizeZ, string path)
    {
        var img = new RgbaImage(sizeX, sizeZ);
        double ux = Layout.W / sizeX, uz = Layout.H / sizeZ;
        Parallel.For(0, sizeZ, py =>
        {
            for (int px = 0; px < sizeX; px++)
            {
                double x = (px + 0.5) * ux, z = (py + 0.5) * uz;
                bool space = Terrain.Nearest(t.Cell, t.NX, t.NZ, new P(x, z)) == Terrain.Space;
                double sl = s.At(s.Slope, x, z);
                double a = space ? 0 : sl > 40 ? 70 : 150;
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
        double ux = Layout.W / sizeX, uz = Layout.H / sizeZ;
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

    /// <summary>A starfield sky cube (A8R8G8B8, six faces): black space, stars of a few brightnesses, a faint nebula.</summary>
    public static void Sky(string path, int size = 1024)
    {
        var nebula = new Noise(3001);
        var face = new RgbaImage(size, size);
        var probe = DdsFile.Create(DdsFormat.Uncompressed, face, false).ToBytes();
        int headerBytes = probe.Length - size * size * 4;
        var bytes = new byte[headerBytes + 6 * size * size * 4];
        probe.AsSpan(0, headerBytes).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(108), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(108)) | 0x8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(112), 0x200 | 0xFC00);
        for (int fc = 0; fc < 6; fc++)
        {
            int f = fc;
            Parallel.For(0, size, ty =>
            {
                for (int tx = 0; tx < size; tx++)
                {
                    double sc = (tx + 0.5) / size * 2 - 1, tc = (ty + 0.5) / size * 2 - 1;
                    (double X, double Y, double Z) dir = f switch
                    {
                        0 => (1, -tc, -sc), 1 => (-1, -tc, sc), 2 => (sc, 1, tc), 3 => (sc, -1, -tc), 4 => (sc, -tc, 1), _ => (-sc, -tc, -1),
                    };
                    double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
                    double ux = dir.X / len, uy = dir.Y / len, uz = dir.Z / len;
                    // A faint violet-blue nebula band.
                    double band = Math.Exp(-Math.Pow((uy * 0.8 + ux * 0.6) / 0.35, 2));
                    double neb = Math.Max(0, nebula.Fbm(ux * 3 + uz * 2, uy * 3 - uz, 4) + 0.15) * band;
                    double r = 4 + 40 * neb, g = 5 + 22 * neb, b = 12 + 70 * neb;
                    // Stars: one in a few hundred texels, a few bright.
                    uint hsh = (uint)(f * 92837111) ^ (uint)(tx * 73856093) ^ (uint)(ty * 19349663);
                    hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                    double roll = (hsh & 0xFFFF) / 65535.0;
                    if (roll > 0.9965)
                    {
                        double bright = roll > 0.99965 ? 255 : roll > 0.999 ? 200 : 130;
                        double tint = ((hsh >> 16) & 0xFF) / 255.0;
                        r = Math.Max(r, bright * (0.85 + 0.15 * tint)); g = Math.Max(g, bright * 0.9); b = Math.Max(b, bright * (1 - 0.1 * tint));
                    }
                    int o = headerBytes + (f * size * size + ty * size + tx) * 4;
                    bytes[o] = B(b); bytes[o + 1] = B(g); bytes[o + 2] = B(r); bytes[o + 3] = 255;
                }
            });
        }
        File.WriteAllBytes(path, bytes);
    }
}
