using System.Numerics;

namespace LotusECMLogger.Services.Tuning.Plotting;

/// <summary>
/// Orbit camera for the 3D surface: an orthographic view of the unit cube
/// (x across, y height, z depth, each in [-0.5, 0.5]) rotated by yaw and pitch.
/// </summary>
public sealed class SurfaceCamera
{
    public const double DefaultYaw = -35;
    public const double DefaultPitch = 30;

    private double _pitch = DefaultPitch;

    /// <summary>Rotation around the vertical axis, in degrees.</summary>
    public double Yaw { get; set; } = DefaultYaw;

    /// <summary>Tilt towards the viewer, in degrees (5..89: always looking down on the surface).</summary>
    public double Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, 5, 89);
    }

    /// <summary>Height of the surface relative to its width/depth.</summary>
    public double HeightScale { get; set; } = 0.55;

    public void Reset()
    {
        Yaw = DefaultYaw;
        Pitch = DefaultPitch;
    }

    /// <summary>
    /// Projects a point given as fractions (0..1) along X, Y (depth) and value.
    /// Returns screen-space x/y in cube units (y up is negative, as on screen) and depth (larger is nearer).
    /// </summary>
    public Vector3 Project(double fx, double fy, double fz)
    {
        double x = fx - 0.5;
        double z = 0.5 - fy;             // first row in front, rows go away from the viewer
        double y = (fz - 0.5) * HeightScale;

        double yaw = Yaw * Math.PI / 180, pitch = Pitch * Math.PI / 180;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
        double xr = x * cy - z * sy;
        double zr = x * sy + z * cy;

        double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        double yr = y * cp - zr * sp;
        double depth = y * sp + zr * cp;
        return new Vector3((float)xr, (float)-yr, (float)depth);
    }

    /// <summary>
    /// Order in which to paint the surface's quads (back to front) for a grid of
    /// <paramref name="sizeX"/> × <paramref name="sizeY"/> points with heights <paramref name="fz"/> (0..1).
    /// </summary>
    public IReadOnlyList<(int I, int J)> PaintOrder(int sizeX, int sizeY, Func<int, int, double> fz)
    {
        var quads = new List<(int I, int J, float Depth)>((sizeX - 1) * (sizeY - 1));
        for (int i = 0; i + 1 < sizeX; i++)
        {
            for (int j = 0; j + 1 < sizeY; j++)
            {
                double cx = (i + 0.5) / (sizeX - 1), cy = (j + 0.5) / (sizeY - 1);
                double cz = (fz(i, j) + fz(i + 1, j) + fz(i, j + 1) + fz(i + 1, j + 1)) / 4;
                quads.Add((i, j, Project(cx, cy, cz).Z));
            }
        }
        quads.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        return quads.Select(q => (q.I, q.J)).ToList();
    }
}
