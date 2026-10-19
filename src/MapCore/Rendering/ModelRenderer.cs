using System.Numerics;
using MapCore.Assets;
using MapCore.Imaging;

namespace MapCore.Rendering;

/// <summary>A drawn preview, and how much of it the model covers (0-1).</summary>
public sealed record ModelRender(RgbaImage Image, double Coverage);

/// <summary>
/// Draws a model on its own for a preview: textured, lit from over the viewer's left shoulder, framed so the whole
/// model fits whichever way it is turned. Faces are drawn from both sides and lit from the side facing the viewer,
/// so a model's winding doesn't matter; texture 2's alpha cuts out leaves and the like (<see cref="ModelPreview.CutOut"/>).
/// </summary>
public static class ModelRenderer
{
    const double FieldOfView = 30;
    static readonly Vector3 Top = new(78, 88, 100), Bottom = new(38, 42, 49), Untextured = new(176, 172, 160);

    /// <param name="yaw">Degrees round the model: 0 looks at its front (from the south), positive swings the view east.</param>
    /// <param name="pitch">Degrees above the horizon.</param>
    public static ModelRender Render(ModelPreview model, int width, int height, double yaw = 35, double pitch = 20, int supersample = 2)
    {
        int w = width * supersample, h = height * supersample;
        var mesh = model.Mesh;
        var colour = new Vector3[w * h];
        var depth = new float[w * h]; // 1/distance; 0 is empty
        var used = mesh.Triangles.Distinct().Select(i => mesh.Vertices[i].Position).ToList();
        if (used.Count > 0)
        {
            var min = used.Aggregate(Vector3.Min);
            var max = used.Aggregate(Vector3.Max);
            var centre = (min + max) / 2;
            double radius = Math.Max(1e-3, used.Max(p => (p - centre).Length()));
            double y = yaw * Math.PI / 180, x = pitch * Math.PI / 180, half = FieldOfView * Math.PI / 360;
            double distance = radius / Math.Sin(half) * 1.04;
            var eye = centre + new Vector3((float)(Math.Sin(y) * Math.Cos(x)), (float)Math.Sin(x), (float)(Math.Cos(y) * Math.Cos(x))) * (float)distance;
            var forward = Vector3.Normalize(centre - eye);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.Cross(right, forward);
            var light = Vector3.Normalize(-0.45f * right + 0.8f * up - 0.55f * forward);
            double focal = Math.Min(w, h) / 2.0 / Math.Tan(half);

            var screen = mesh.Vertices.Select(v =>
            {
                var d = v.Position - eye;
                double z = Vector3.Dot(d, forward);
                return (X: w / 2.0 + focal * Vector3.Dot(d, right) / z, Y: h / 2.0 - focal * Vector3.Dot(d, up) / z, InvZ: 1 / z);
            }).ToArray();

            for (int t = 0; t + 2 < mesh.Triangles.Count; t += 3)
                Triangle(mesh.Triangles[t], mesh.Triangles[t + 1], mesh.Triangles[t + 2]);

            void Triangle(int i0, int i1, int i2)
            {
                var (a, b, c) = (screen[i0], screen[i1], screen[i2]);
                double area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (Math.Abs(area) < 1e-12 || double.IsNaN(area))
                    return;
                var (v0, v1, v2) = (mesh.Vertices[i0], mesh.Vertices[i1], mesh.Vertices[i2]);
                var faceNormal = Vector3.Cross(v1.Position - v0.Position, v2.Position - v0.Position);
                faceNormal = faceNormal.LengthSquared() > 0 ? Vector3.Normalize(faceNormal) : Vector3.UnitY;
                int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
                int rightEdge = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
                int top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
                int bottom = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
                for (int py = top; py <= bottom; py++)
                {
                    double sy = py + 0.5;
                    for (int px = left; px <= rightEdge; px++)
                    {
                        double sx = px + 0.5;
                        double w0 = ((b.X - sx) * (c.Y - sy) - (b.Y - sy) * (c.X - sx)) / area;
                        double w1 = ((c.X - sx) * (a.Y - sy) - (c.Y - sy) * (a.X - sx)) / area;
                        double w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0)
                            continue;
                        double invZ = w0 * a.InvZ + w1 * b.InvZ + w2 * c.InvZ;
                        int index = py * w + px;
                        if (invZ <= depth[index])
                            continue;
                        // Perspective-correct weights for the textures and normals.
                        double p0 = w0 * a.InvZ / invZ, p1 = w1 * b.InvZ / invZ, p2 = 1 - p0 - p1;
                        var uv = v0.Uv * (float)p0 + v1.Uv * (float)p1 + v2.Uv * (float)p2;
                        if (model.CutOut && model.Extra != null && Sample(model.Extra, uv).W < 0.5f)
                            continue;
                        var normal = v0.Normal * (float)p0 + v1.Normal * (float)p1 + v2.Normal * (float)p2;
                        normal = normal.LengthSquared() > 1e-8 ? Vector3.Normalize(normal) : faceNormal;
                        if (Vector3.Dot(normal, forward) > 0)
                            normal = -normal; // the side facing the viewer
                        var texel = model.Colour == null ? new Vector4(Untextured / 255, 1) : Sample(model.Colour, uv);
                        float shade = 0.36f + 0.74f * Math.Max(0, Vector3.Dot(normal, light));
                        float glow = model.Extra == null ? 0 : Sample(model.Extra, uv).X * 0.6f; // self-illumination
                        colour[index] = new Vector3(texel.X, texel.Y, texel.Z) * 255 * Math.Min(1.25f, shade + glow);
                        depth[index] = (float)invZ;
                    }
                }
            }
        }

        var image = new RgbaImage(width, height);
        long covered = 0;
        int samples = supersample * supersample;
        for (int py = 0; py < height; py++)
        {
            var background = Vector3.Lerp(Top, Bottom, (py + 0.5f) / height);
            for (int px = 0; px < width; px++)
            {
                var sum = Vector3.Zero;
                int hits = 0;
                for (int sy = 0; sy < supersample; sy++)
                    for (int sx = 0; sx < supersample; sx++)
                    {
                        int index = (py * supersample + sy) * w + px * supersample + sx;
                        if (depth[index] > 0)
                        {
                            sum += colour[index];
                            hits++;
                        }
                        else
                            sum += background;
                    }
                covered += hits;
                var c = Vector3.Clamp(sum / samples, Vector3.Zero, new Vector3(255));
                int o = (py * width + px) * 4;
                (image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]) = ((byte)(c.X + 0.5f), (byte)(c.Y + 0.5f), (byte)(c.Z + 0.5f), 255);
            }
        }
        return new ModelRender(image, covered / (double)(w * h));
    }

    /// <summary>Bilinear, wrapping; S3O texture coordinates put v = 0 at the image's top row.</summary>
    internal static Vector4 Sample(RgbaImage image, Vector2 uv)
    {
        double x = uv.X * image.Width - 0.5, y = uv.Y * image.Height - 0.5;
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            return Vector4.Zero;
        double fx = x - Math.Floor(x), fy = y - Math.Floor(y);
        int x0 = Wrap((long)Math.Floor(x), image.Width), y0 = Wrap((long)Math.Floor(y), image.Height);
        int x1 = (x0 + 1) % image.Width, y1 = (y0 + 1) % image.Height;
        var top = Vector4.Lerp(Texel(image, x0, y0), Texel(image, x1, y0), (float)fx);
        var bottom = Vector4.Lerp(Texel(image, x0, y1), Texel(image, x1, y1), (float)fx);
        return Vector4.Lerp(top, bottom, (float)fy);
    }

    static int Wrap(long v, int n) => (int)(((v % n) + n) % n);

    static Vector4 Texel(RgbaImage image, int x, int y)
    {
        int o = (y * image.Width + x) * 4;
        var p = image.Pixels;
        return new Vector4(p[o], p[o + 1], p[o + 2], p[o + 3]) / 255f;
    }
}
