namespace MapCore.Rendering;

/// <summary>
/// A camera circling a point on the map (game units; x east, y up, z south). <see cref="Yaw"/> 0 puts the camera
/// south of the target looking north, so north is up as in the 2D view; positive yaw swings the camera towards the
/// east. <see cref="Pitch"/> is the angle above the horizon, in degrees.
/// </summary>
public readonly record struct OrbitCamera(double TargetX, double TargetZ, double Yaw, double Pitch, double Distance, double FieldOfView = 45)
{
    public const double MinPitch = 5, MaxPitch = 89.9;

    /// <summary>The whole map from the south, at 55° and far enough to see all of it.</summary>
    public static OrbitCamera Overview(double mapWidth, double mapHeight, double aspect = 1.5)
    {
        // Half the height the view must show: the map's depth, or its width squeezed into the view's shape.
        double half = Math.Max(mapHeight / 2, mapWidth / 2 / Math.Clamp(aspect, 0.5, 3));
        return new OrbitCamera(mapWidth / 2, mapHeight / 2, 0, 55, half / Math.Tan(45 * Math.PI / 360) * 1.05);
    }

    /// <summary>Horizontal unit vectors: towards screen-right, and straight ahead (both in the ground plane).</summary>
    public (double X, double Z) Right => (Math.Cos(Rad(Yaw)), -Math.Sin(Rad(Yaw)));
    public (double X, double Z) Ahead => (-Math.Sin(Rad(Yaw)), -Math.Cos(Rad(Yaw)));

    /// <summary>The camera position, given the height of the target point.</summary>
    public (double X, double Y, double Z) Eye(double targetY)
    {
        double yaw = Rad(Yaw), pitch = Rad(Pitch);
        return (TargetX + Distance * Math.Sin(yaw) * Math.Cos(pitch),
                targetY + Distance * Math.Sin(pitch),
                TargetZ + Distance * Math.Cos(yaw) * Math.Cos(pitch));
    }

    public OrbitCamera Orbit(double yawDegrees, double pitchDegrees) => this with
    {
        Yaw = ((Yaw + yawDegrees) % 360 + 360) % 360,
        Pitch = Math.Clamp(Pitch + pitchDegrees, MinPitch, MaxPitch),
    };

    /// <summary>Moves closer (factor above 1) or further away.</summary>
    public OrbitCamera Zoom(double factor) => this with { Distance = Math.Clamp(Distance / factor, 16, 100_000) };

    /// <summary>Moves the target so the ground follows a drag of (dx, dy) pixels in a view <paramref name="viewHeight"/> pixels tall.</summary>
    public OrbitCamera Pan(double dx, double dy, int viewHeight, double mapWidth, double mapHeight)
    {
        double unitsPerPixel = 2 * Distance * Math.Tan(Rad(FieldOfView) / 2) / Math.Max(1, viewHeight);
        double ahead = dy * unitsPerPixel / Math.Max(0.25, Math.Sin(Rad(Pitch)));
        var (rx, rz) = Right;
        var (ax, az) = Ahead;
        return this with
        {
            TargetX = Math.Clamp(TargetX - dx * unitsPerPixel * rx + ahead * ax, 0, mapWidth),
            TargetZ = Math.Clamp(TargetZ - dx * unitsPerPixel * rz + ahead * az, 0, mapHeight),
        };
    }

    static double Rad(double degrees) => degrees * Math.PI / 180;
}
