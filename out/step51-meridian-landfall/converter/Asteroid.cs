// Meridian Landfall's heights, per heightmap vertex (8 units apart), built for the port half and copied mirrored:
//   1. the moonrock plain at the ship's deck (Plain), swelling gently away from the hull (under 3°), dead level on the
//      aprons outside the bay doors, on the mares round the outside starts, and round every spot and vent;
//   2. bowls and small craters: a flat floor, a wall at about 41° (39° for the small ones) up to a raised rim, and the
//      rim falling away outside at under 24°, so vehicles may climb a rim but never get in; their outlines waver, the
//      whole profile scaled with the outline (not offset by it, which would steepen the wall near the floor);
//   3. void pits: a broken rim (ridged noise, crossed by cracks, its width wavering) and low hummocks of ejecta beyond,
//      a scarp on the pit's fractal outline in three steps (steep faces, gentler ledges, its width wavering, its face
//      roughened), down to a floor deep below; the texture is cut (void) where the scarp is 110 below the plain, so the
//      broken faces drop into the stars;
//   4. the asteroid's edge: a slope worn down over ages, from a rounded shoulder 360 to 480 inside the edge's line
//      (convex above, averaging about 23° and steepest at about 35° where it meets the void, which is cut 170 below the
//      plain), gullied straight down it and rilled near the top (EdgeAt), the gullies deepening as they go;
//   5. the ship: Meridian's own heights (Terrain.cs) raised by HY inside the hull, its sheer side down to the plain,
//      and the hangars' bay doors cut level through the rim.
// VoidV is a vertex's height over the void's cut there (negative: void, not drawn, its rubble blocks every ground unit).
static class Asteroid
{
    public const byte Plain = 0, InCrater = 1, InPit = 2, AtEdge = 3, Hull = 4, BayDoor = 5, HullSide = 6;
    public const double CutBelow = 110, PitWall = 110, EdgeWall = 110, VoidFloor = 450;
    /// <summary>A ship floor above this (its own heights: the bulkheads' cap, 340, less 60) gets its walls raised 60 over it.</summary>
    public const double HighFloor = Layout.Bulkhead - 60;

    public sealed class Land
    {
        public float[] H = [], VoidV = [];
        public byte[] Kind = [];
        public Terrain.Result Ship = null!;

        /// <summary>The ship's vertex index for a world vertex, or -1 off the ship's grid.</summary>
        public int ShipIndex(int x, int z)
        {
            int sx = x - World.VX, sz = z - World.VZ;
            return sx >= 0 && sz >= 0 && sx < Ship.NX && sz < Ship.NZ ? sz * Ship.NX + sx : -1;
        }

        /// <summary>The ship's cell at a world point (Terrain.Space off the ship's grid or outside the hull).</summary>
        public int ShipCell(P p)
        {
            var s = World.ToShip(p);
            if (s.X < 0 || s.Z < 0 || s.X > Layout.W || s.Z > Layout.H) return Terrain.Space;
            return Terrain.Nearest(Ship.Cell, Ship.NX, Ship.NZ, s);
        }

        /// <summary>Whether a world point is on the ship's floor (a room's, a ramp's or a doorway's, the grand stair's included).</summary>
        public bool ShipFloor(P p)
        {
            var s = World.ToShip(p);
            return s.X >= 0 && s.Z >= 0 && s.X <= Layout.W && s.Z <= Layout.H && Terrain.Nearest(Ship.Floor, Ship.NX, Ship.NZ, s);
        }

        public double VoidAt(P p) => Terrain.Sample(VoidV, World.NX, World.NZ, p);
    }

    static readonly Noise Swell = new(5101), Ripple = new(5102), Rough = new(5103);

    static double Smooth(double t) => Geo.S(t);

    /// <summary>The plain before craters: swells, faded out near the hull and flattened on aprons, mares, spots and vents.</summary>
    static double Ground(P p, double hullDistance, bool flattenSpots = true)
    {
        double mx = Math.Abs(p.X - World.CX);
        double h = 22 * Swell.Fbm(mx / 2600, p.Z / 2600, 3) + 8 * Ripple.Fbm(mx / 650, p.Z / 650, 2);
        h *= Smooth((hullDistance - 150) / 600);
        foreach (var a in World.Aprons)
            h *= Smooth(BoxDistance(a, p) / 250);
        foreach (var (_, s) in World.Starts.Skip(4).Take(4).Concat(World.Starts.Skip(12)))
            h *= Smooth(((p - s).Length - World.MareR) / 300);
        if (flattenSpots)
            foreach (var q in FlatSpots)
            {
                double d = (p - q.At).Length;
                if (d < 250) h = q.H - World.Plain + (h - (q.H - World.Plain)) * Smooth((d - 130) / 120);
            }
        return World.Plain + h;
    }

    static double BoxDistance(World.Box b, P p)
    {
        double dx = Math.Max(Math.Max(b.X0 - p.X, 0), p.X - b.X1), dz = Math.Max(Math.Max(b.Z0 - p.Z, 0), p.Z - b.Z1);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The spots and vents on the plain (not those in craters), each with its ground's height, kept level round it.</summary>
    static readonly (P At, double H)[] FlatSpots = World.Spots.Where(s => s.Side != "bowl").Select(s => s.At)
        .Concat(World.Vents).Where(p => World.ToShip(p) is var q && !Terrain.InPolygon(q, Layout.Hull) && Terrain.ToOutline(q, Layout.Hull) > 200)
        .Select(p => (p, Ground(p, Terrain.ToOutline(World.ToShip(p), Layout.Hull), false))).ToArray();

    static readonly Noise RidgeNoise = new(5104), Hum = new(5105), Face = new(5106);
    /// <summary>The edge's slope falls from its shoulder over (shoulder + EdgeRunout), and is cut to the void EdgeCut below the plain.</summary>
    public const double EdgeRunout = 520, EdgeCut = 170;

    /// <summary>Ridged noise, 0 to 1, sharp along its crests: the broken rims' ridges (continuous: no steps, no prisms).</summary>
    public static double Ridge(P p)
    {
        double n = 1 - Math.Abs(RidgeNoise.Fbm(p.X / 95, p.Z / 95, 3));
        return n * n;
    }

    /// <summary>Cracks: 1 on the borders of jittered cells about g across, falling to 0 within w of them (Voronoi F2 − F1).</summary>
    public static double Crack(P p, double g, double w, int key)
    {
        long ci = (long)Math.Floor(p.X / g), cj = (long)Math.Floor(p.Z / g);
        double f1 = double.MaxValue, f2 = double.MaxValue;
        for (long j = cj - 1; j <= cj + 1; j++)
            for (long i = ci - 1; i <= ci + 1; i++)
            {
                double px = (i + World.Hash01(key, i, j, 1)) * g, pz = (j + World.Hash01(key, i, j, 2)) * g;
                double d = Math.Sqrt((px - p.X) * (px - p.X) + (pz - p.Z) * (pz - p.Z));
                if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
            }
        return 1 - Smooth((f2 - f1) / w);
    }

    /// <summary>A pit's rim (its broken, uplifted band) at angle a: 0.25 to 0.45 of its radius wide.</summary>
    public static double PitRim(World.Crater c, double a) => c.R * (0.25 + 0.2 * World.Around(c.Key, 7, a));

    /// <summary>A pit's scarp at angle a: 130 to 300 wide, so it is steeper in places, and stepped by ledges.</summary>
    public static double PitWallWidth(World.Crater c, double a) => 130 + 170 * World.Around(c.Key, 11, a);

    static readonly Noise ShoulderNoise = new(5107), GullyNoise = new(5108), GullyWarp = new(5109), RillNoise = new(5110), GullyDepth = new(5111);

    /// <summary>
    /// The asteroid's edge at a point: how far into the rock (E, negative toward the map's border), where its worn slope
    /// starts (the shoulder, 360 to 480 inside the edge's line), how far down that slope the point is (Down), along the
    /// nearest border (U), and the gullies and rills cut into the slope there (Channel and Rill, 0 to 1 across each; Carve,
    /// how deep). Gullies run straight down the slope, meandering a little, deepening as they go; rills only near the top.
    /// Where two borders meet, their slopes are blended. Read at the port half's mirror image, so it is symmetric.
    /// </summary>
    public readonly record struct EdgeHere(double E, double Shoulder, double Down, double U, double Channel, double Rill, double Carve);

    public static EdgeHere EdgeAt(P p)
    {
        double mx = Math.Abs(p.X - World.CX), x = World.CX - mx;
        Span<double> es = [p.Z - World.EdgeDepth(0, mx), World.H - p.Z - World.EdgeDepth(2, mx), x - World.EdgeDepth(1, p.Z)];
        Span<double> us = [mx, mx, p.Z];
        double e = Math.Min(es[0], Math.Min(es[1], es[2]));
        double wsum = 0, shoulder = 0, channel = 0, rill = 0, carve = 0, u = 0, best = double.MaxValue;
        for (int b = 0; b < 3; b++)
        {
            double over = es[b] - e;
            if (over > 300) continue;
            double w = Math.Exp(-over / 60), ub = us[b], salt = 100 * b;
            double sh = 420 + 60 * ShoulderNoise.Fbm(ub / 600 + salt, 0.5, 2), d = sh - es[b];
            double warp = 70 * GullyWarp.Fbm(ub / 260 + salt, d / 260, 2);
            double ch = Math.Max(0, 1 - Math.Abs(GullyNoise.Fbm((ub + warp) / 150 + salt, d / 900, 3)) / 0.16);
            double rl = Math.Max(0, 1 - Math.Abs(RillNoise.Fbm((ub + 0.5 * warp) / 48 + salt, d / 300, 2)) / 0.2);
            double deep = 55 * Smooth(d / 120) * (0.6 + 0.4 * GullyDepth.Fbm(ub / 500 + salt, 3.7, 2));
            double shallow = 14 * Smooth(d / 40) * (1 - Smooth((d - 220) / 160));
            wsum += w; shoulder += w * sh; channel += w * ch; rill += w * rl; carve += w * (ch * deep + rl * shallow);
            if (es[b] < best) { best = es[b]; u = ub; }
        }
        shoulder /= wsum; channel /= wsum; rill /= wsum; carve /= wsum;
        return new EdgeHere(e, shoulder, shoulder - e, u, channel, rill, carve);
    }

    /// <summary>A scarp's fall (0 at its top, 1 at its foot), in three steps: steeper faces and gentler ledges between them.</summary>
    static double Terraced(double s, double alpha) => s - alpha * Math.Sin(2 * Math.PI * 3 * s) / (2 * Math.PI * 3);

    /// <summary>How free the ground is to be rough here: 0 round the spots and vents, on the mares and aprons; 1 well clear of them.</summary>
    static double Quiet(P p)
    {
        double q = 1;
        foreach (var s in FlatSpots) q = Math.Min(q, Smooth(((p - s.At).Length - 160) / 140));
        foreach (var (_, s) in World.Starts.Skip(4).Take(4).Concat(World.Starts.Skip(12)))
            q = Math.Min(q, Smooth(((p - s).Length - World.MareR - 40) / 200));
        foreach (var a in World.Aprons) q = Math.Min(q, Smooth((BoxDistance(a, p) - 60) / 200));
        return q;
    }

    /// <summary>A bowl's or small crater's wall, rise per unit out (in its own scale): about 41°, the small ones 39°.</summary>
    public static double BowlSlope(World.Crater c) => Math.Tan((c.Key < 3000 ? 41 : 39) * Math.PI / 180);

    /// <summary>A crater's ground at its centre, before it is dug: its floor and rim are measured from this.</summary>
    static double CentreGround(World.Crater c) => Ground(c.At, Terrain.ToOutline(World.ToShip(c.At), Layout.Hull));

    public static Land Build()
    {
        var ship = Terrain.Build();
        int nx = World.NX, nz = World.NZ, half = nx / 2;
        var land = new Land { H = new float[nx * nz], VoidV = new float[nx * nz], Kind = new byte[nx * nz], Ship = ship };
        var bowls = World.Bowls.Concat(World.Small).Select(c => (C: c, B0: CentreGround(c), Slope: BowlSlope(c))).ToArray();
        var pits = World.Pits.Select(c => (C: c, B0: CentreGround(c))).ToArray();

        Parallel.For(0, nz, z =>
        {
            for (int x = 0; x <= half; x++)
            {
                int i = z * nx + x;
                var p = new P(x * 8.0, z * 8.0);
                int si = land.ShipIndex(x, z);
                if (si >= 0 && ship.Cell[si] != Terrain.Space)
                {
                    land.H[i] = (float)(ship.H[si] + World.HY);
                    land.Kind[i] = Hull;
                    land.VoidV[i] = 1000;
                    continue;
                }
                double hull = si >= 0 ? ship.Outside[si] : Terrain.ToOutline(World.ToShip(p), Layout.Hull);
                double h = Ground(p, hull), voidV = 1000;
                byte kind = Plain;

                // Bowls and small craters.
                foreach (var (c, b0, slope) in bowls)
                {
                    double dx = p.X - c.At.X, dz = p.Z - c.At.Z, reach = c.R * 1.2 + c.Rim / Math.Tan(16 * Math.PI / 180);
                    if (Math.Abs(dx) > reach || Math.Abs(dz) > reach) continue;
                    // In the crater's own scale: rr is the distance as if the outline were the circle of radius R.
                    double rr = Math.Sqrt(dx * dx + dz * dz) * c.R / World.Waver(c.R, c.Key, Math.Atan2(dz, dx));
                    double wall = (c.Depth + c.Rim) / slope, floor = c.R - wall, outer = c.Rim / Math.Tan(16 * Math.PI / 180);
                    if (rr <= floor) { h = b0 - c.Depth; kind = InCrater; }
                    else if (rr <= c.R) { h = b0 - c.Depth + (rr - floor) * slope; kind = InCrater; }
                    else if (rr <= c.R + outer)
                    {
                        double t = Smooth((rr - c.R) / outer);
                        h = h + (b0 - h) * (1 - t) + c.Rim * (1 - t);
                    }
                }

                // Void pits: a broken, uplifted rim, a terraced scarp on the pit's jagged outline, a floor in the void.
                foreach (var (c, b0) in pits)
                {
                    double dx = p.X - c.At.X, dz = p.Z - c.At.Z, reach = c.R * 3;
                    if (Math.Abs(dx) > reach || Math.Abs(dz) > reach) continue;
                    double a = Math.Atan2(dz, dx), r = Math.Sqrt(dx * dx + dz * dz), rj = World.Jag(c.R, c.Key, a);
                    double rimW = PitRim(c, a), apron = c.R * 1.1;
                    if (r >= rj + rimW + apron) continue;
                    double ridge = Ridge(p);
                    if (r >= rj)
                    {
                        double t = (r - rj) / rimW;
                        if (t < 1)
                        {
                            h += c.Rim * Math.Pow(1 - t, 1.6) * (0.4 + 0.6 * ridge) - 10 * Crack(p, 55, 6, c.Key) * 4 * t * (1 - t);
                            kind = InPit;
                        }
                        h += 8 * Math.Max(0, Hum.Fbm(p.X / 70, p.Z / 70, 2)) * Math.Max(0, 1 - (r - rj) / (rimW + apron)) * Quiet(p);
                        continue;
                    }
                    double top = h + c.Rim * (0.4 + 0.6 * ridge), bottom = b0 - c.Depth + 14 * Rough.Fbm(p.X / 60, p.Z / 60, 2);
                    double s = Math.Min(1, (rj - r) / PitWallWidth(c, a));
                    h = top + (bottom - top) * Terraced(s, 0.55 + 0.35 * World.Around(c.Key, 13, a)) + 12 * Face.Fbm(p.X / 26, p.Z / 26, 2) * Math.Sin(Math.PI * s);
                    voidV = h - (b0 - CutBelow);
                    kind = InPit;
                }

                // The asteroid's edge: a slope worn down over ages, from a rounded shoulder into the void, gullied.
                var edge = EdgeAt(p);
                if (edge.Down > 0)
                {
                    double q = Math.Min(1, edge.Down / (edge.Shoulder + EdgeRunout)), fall = q * q * (3 - 2 * q);
                    double bottom = World.Plain - VoidFloor + 20 * Rough.Fbm(p.X / 80, p.Z / 80, 2);
                    h += (bottom - h) * fall - edge.Carve;
                    voidV = Math.Min(voidV, h - (World.Plain - EdgeCut));
                    kind = AtEdge;
                }

                // The ship's sheer side, down to the plain.
                if (si >= 0)
                {
                    double side = Layout.Rim + World.HY - ship.Outside[si] * 6;
                    if (side > h) { h = side; kind = HullSide; }
                }
                land.H[i] = (float)h;
                land.VoidV[i] = (float)voidV;
                land.Kind[i] = kind;
            }
        });

        // Meridian's bulkheads are capped at 340, under the top of its bridge ramps (380 down to 240): there a vehicle can
        // drive off the ramp onto the wall's top and on over the bow's hull. Here a wall within 48 of a floor above 280
        // (the bridge's, its landings', the ramps' upper parts) stands at least 60 over it; every other wall is Meridian's.
        Parallel.For(0, nz, z =>
        {
            for (int x = 0; x <= half; x++)
            {
                int si = land.ShipIndex(x, z);
                if (si < 0 || ship.Cell[si] != Terrain.Solid || ship.Floor[si]) continue;
                int sx = si % ship.NX, sz = si / ship.NX;
                double floor = double.MinValue;
                for (int dz = -6; dz <= 6; dz++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        int ox = sx + dx, oz = sz + dz;
                        if (ox < 0 || oz < 0 || ox >= ship.NX || oz >= ship.NZ || dx * dx + dz * dz > 36) continue;
                        int o = oz * ship.NX + ox;
                        if (ship.Floor[o]) floor = Math.Max(floor, ship.H[o]);
                    }
                int i = z * nx + x;
                if (floor > HighFloor && land.H[i] < floor + World.HY + 60) land.H[i] = (float)(floor + World.HY + 60);
            }
        });

        // The hangars' bay doors: level with the deck, through the rim and the hull's side.
        foreach (var d in World.BayDoorsPort)
            for (int z = (int)Math.Ceiling(d.Z0 / 8); z <= (int)Math.Floor(d.Z1 / 8); z++)
                for (int x = (int)Math.Ceiling(d.X0 / 8); x <= (int)Math.Floor(d.X1 / 8); x++)
                {
                    int i = z * nx + x;
                    land.H[i] = (float)World.Plain;
                    land.Kind[i] = BayDoor;
                }

        Mirror(land.H, nx, nz);
        Mirror(land.VoidV, nx, nz);
        Mirror(land.Kind, nx, nz);
        return land;
    }

    static void Mirror<T>(T[] a, int nx, int nz)
    {
        Parallel.For(0, nz, z =>
        {
            for (int x = nx / 2 + 1; x < nx; x++)
                a[z * nx + x] = a[z * nx + (nx - 1 - x)];
        });
    }
}
