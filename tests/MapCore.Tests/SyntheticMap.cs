using System.Globalization;
using MapCore.Archives;
using MapCore.Imaging;
using MapCore.Smf;

namespace MapCore.Tests;

/// <summary>
/// A second map, built from scratch, unlike Acidic Quarry wherever maps differ: not square (2048 x 1024 game units),
/// an .sdz, the texture split over two tile files (named through mapinfo's smf.smtFileName0/1, from the package root),
/// SMF features only (trees and geovents, no featureplacer), no resources at all (no splat or normal map), and
/// ground under water. Everything is computed, so tests can compare against the exact intended values.
/// </summary>
static class SyntheticMap
{
    public const int SquaresX = 256, SquaresZ = 128;
    public const int Width = SquaresX * 8, Height = SquaresZ * 8;
    public const float MinHeight = -40, MaxHeight = 260;
    public const string SmfEntry = "maps/synthetic.smf";

    /// <summary>Metal spots (centres), each 4 x 4 metal cells of 255.</summary>
    public static readonly (int X, int Z)[] Spots = [(416, 304), (1632, 720), (1024, 512)];

    /// <summary>Ground height at a world point: rolling hills, the low ones under water.</summary>
    public static float GroundAt(double x, double z) => (float)(60 + 90 * Math.Sin(x / 260) * Math.Cos(z / 170));

    /// <summary>The texture's colour at a texel: smooth ramps, a dark line every 256 texels, a dark patch at each spot.</summary>
    public static Rgba Colour(int x, int y)
    {
        if (Spots.Any(s => (x - s.X) * (x - s.X) + (y - s.Z) * (y - s.Z) < 40 * 40))
            return new Rgba(50, 40, 35, 255);
        if (x % 256 < 3 || y % 256 < 3)
            return new Rgba(30, 30, 30, 255);
        return new Rgba((byte)(60 + x * 150 / Width), (byte)(70 + y * 150 / Height), 120, 255);
    }

    public static string MapInfo(string extraSmf = "") => $$$"""
        local mapinfo = {
        	name        = "Synthetic Test",
        	shortname   = "SynthTest",
        	description = "A made-up map for the editor's tests",
        	author      = "MapCore tests",
        	version     = "1",
        	mapfile     = "{{{SmfEntry}}}",
        	modtype     = 3,
        	depend      = {"Map Helper v1"},
        	maxMetal        = 2.0,
        	extractorRadius = 90.0,
        	smf = {
        		minheight = {{{MinHeight.ToString(CultureInfo.InvariantCulture)}}},
        		maxheight = {{{MaxHeight.ToString(CultureInfo.InvariantCulture)}}},
        		smtFileName0 = "maps/synth_a.smt",
        		smtFileName1 = "maps/synth_b.smt",{{{extraSmf}}}
        	},
        	teams = {
        		[0] = {startPos = {x = 260, z = 520}},
        		[1] = {startPos = {x = 1790, z = 500}},
        	},
        }
        return mapinfo
        """;

    /// <summary>Builds the map into <paramref name="dir"/> and returns the archive's path.</summary>
    /// <param name="extraSmf">Lua fields added to mapinfo's smf table (each starting with a newline).</param>
    /// <param name="files">Extra files for the package (path from the root, contents).</param>
    public static string Build(string dir, string fileName = "synthetic_1.sdz", string extraSmf = "", IEnumerable<(string Path, byte[] Bytes)>? files = null)
    {
        var root = Path.Combine(dir, Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(Path.Combine(root, "maps"));
        var smf = new SmfFile(SquaresX, SquaresZ) { MinHeight = MinHeight, MaxHeight = MaxHeight, MapId = 20261014 };

        for (int z = 0; z <= SquaresZ; z++)
            for (int x = 0; x <= SquaresX; x++)
                smf.Heightmap[z * smf.HeightmapWidth + x] =
                    (ushort)Math.Round((GroundAt(x * 8, z * 8) - MinHeight) / (MaxHeight - MinHeight) * 65535);

        foreach (var (sx, sz) in Spots)
            for (int j = sz / 16 - 2; j < sz / 16 + 2; j++)
                for (int i = sx / 16 - 2; i < sx / 16 + 2; i++)
                    smf.MetalMap[j * smf.MetalMapWidth + i] = 255;

        // Features clear of the spots and start positions, in a fixed pattern.
        for (int n = 0; n < 40; n++)
        {
            double x = 80 + n * 47 % 1890, z = 60 + n * 113 % 900;
            if (Spots.Any(s => Math.Abs(s.X - x) < 120 && Math.Abs(s.Z - z) < 120) || x < 400 && z > 380 || x > 1650 && z > 380)
                continue;
            smf.AddFeature($"treetype{n % 4}", (float)x, (float)z, n * 1500 % 65536);
        }
        smf.AddFeature("geovent", 700, 200, 0);
        smf.AddFeature("geovent", 1400, 850, 0);

        // Every tile position has its own tile; the west half's tiles are in one file, the east half's in the other.
        int columns = smf.TileColumns, rows = smf.TileRows, half = columns / 2;
        var west = new SmtFile();
        var east = new SmtFile();
        west.Resize(half * rows);
        east.Resize((columns - half) * rows);
        smf.TileFiles.Add(new("synth_a.smt", west.TileCount));
        smf.TileFiles.Add(new("synth_b.smt", east.TileCount));
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
                smf.TileIndex[row * columns + col] = col < half ? row * half + col : west.TileCount + row * (columns - half) + col - half;
        var texture = new MapTexture(smf, [west, east]);
        var image = new RgbaImage(texture.Width, texture.Height);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                image.SetPixel(x, y, Colour(x, y));
        texture.Update(image, texture.Bounds);
        Minimap.Rebuild(smf, image);

        smf.Save(Path.Combine(root, "maps", "synthetic.smf"));
        west.Save(Path.Combine(root, "maps", "synth_a.smt"));
        east.Save(Path.Combine(root, "maps", "synth_b.smt"));
        File.WriteAllText(Path.Combine(root, "mapinfo.lua"), MapInfo(extraSmf));
        foreach (var (path, bytes) in files ?? [])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
            File.WriteAllBytes(Path.Combine(root, path), bytes);
        }
        var archive = Path.Combine(dir, fileName);
        ArchiveIO.Pack(root, archive);
        return archive;
    }
}
