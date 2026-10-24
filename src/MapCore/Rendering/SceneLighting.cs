namespace MapCore.Rendering;

/// <summary>
/// The map's lighting, water and sky colours from mapinfo.lua (lighting, water and atmosphere tables), with the
/// engine's defaults where a value is missing. Colours are 0-1.
/// </summary>
public sealed record SceneLighting(
    (double X, double Y, double Z) SunDirection,
    (double R, double G, double B) GroundAmbient,
    (double R, double G, double B) GroundDiffuse,
    double ShadowDensity,
    (double R, double G, double B) WaterBase,
    (double R, double G, double B) WaterMin,
    (double R, double G, double B) WaterAbsorb,
    (double R, double G, double B) WaterSurface,
    (double R, double G, double B) Sky,
    bool VoidWater,
    bool VoidGround = false,
    double VoidAlphaMin = 0.9)
{
    public static SceneLighting From(MapInfo info)
    {
        (double X, double Y, double Z) sun = Vector(info, "lighting.sunDir", (0, 1, 2));
        double length = Math.Sqrt(sun.X * sun.X + sun.Y * sun.Y + sun.Z * sun.Z);
        if (length < 1e-9 || sun.Y <= 0)
            sun = (0, 1, 2);
        length = Math.Sqrt(sun.X * sun.X + sun.Y * sun.Y + sun.Z * sun.Z);
        return new SceneLighting(
            (sun.X / length, sun.Y / length, sun.Z / length),
            Vector(info, "lighting.groundAmbientColor", (0.5, 0.5, 0.5)),
            Vector(info, "lighting.groundDiffuseColor", (0.5, 0.5, 0.5)),
            Math.Clamp(info.GetNumber("lighting.groundShadowDensity") ?? 0.8, 0, 1),
            Vector(info, "water.baseColor", (0, 0, 0)),
            Vector(info, "water.minColor", (0, 0, 0)),
            Vector(info, "water.absorb", (0, 0, 0)),
            Vector(info, "water.surfaceColor", (0.75, 0.8, 0.85)),
            Vector(info, "atmosphere.skyColor", (0.1, 0.15, 0.7)),
            info.Get("voidWater") is Lua.LuaBoolean { Value: true },
            // voidGround: the engine cuts the ground wherever the map texture's alpha is below voidAlphaMin
            // (SMFFragProg.glsl, SMF_VOID_GROUND; MapInfo.cpp's default 0.9). It overrides voidWater.
            info.Get("voidGround") is Lua.LuaBoolean { Value: true },
            Math.Clamp(info.GetNumber("voidAlphaMin") ?? 0.9, 0, 1));
    }

    static (double, double, double) Vector(MapInfo info, string path, (double, double, double) fallback) =>
        info.GetNumbers(path) is { Length: >= 3 } v ? (v[0], v[1], v[2]) : fallback;
}
