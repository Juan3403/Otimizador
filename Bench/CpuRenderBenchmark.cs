using System.Diagnostics;
using System.Numerics;

namespace Otimizador.App.Bench;

/// <summary>Imagem em construção, lida pela interface enquanto o teste de render roda (BGRA, 4 bytes por pixel).</summary>
public sealed class RenderSurface
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public byte[] Bgra { get; private set; } = Array.Empty<byte>();

    public double Seconds;
    public long Rays;
    public int Passes;
    public double Fps;
    public string Info = "";

    public void Resize(int width, int height)
    {
        Bgra = new byte[width * height * 4];
        Width = width;
        Height = height;
    }
}

public sealed record RenderResult(BenchResult Score, bool Verified);

/// <summary>
/// Teste de render na CPU (no estilo do Cinebench): um ray tracer com esferas, espelho, chão xadrez, sombras suaves e luz indireta,
/// que desenha a imagem em blocos usando todos os núcleos e fica refinando-a até acabar o tempo.
/// A nota é a quantidade de raios por segundo; a imagem é conferida antes e depois para detectar erro de cálculo.
/// </summary>
public static class CpuRenderBenchmark
{
    public const int Width = 640;
    public const int Height = 360;
    private const int Tile = 32;

    private struct Sphere
    {
        public Vector3 C;
        public float R;
        public Vector3 Color;
        public float Refl;
    }

    private static readonly Sphere[] Spheres =
    {
        new() { C = new(0f, 1f, 0f), R = 1f, Color = new(0.90f, 0.20f, 0.20f), Refl = 0.10f },
        new() { C = new(-2.2f, 0.7f, -0.6f), R = 0.7f, Color = new(0.92f, 0.92f, 1f), Refl = 0.85f },
        new() { C = new(2.1f, 0.8f, 0.3f), R = 0.8f, Color = new(0.20f, 0.50f, 0.90f), Refl = 0.30f },
        new() { C = new(0.9f, 0.35f, -1.5f), R = 0.35f, Color = new(0.95f, 0.80f, 0.20f), Refl = 0.50f },
        new() { C = new(-1.0f, 0.3f, -2.4f), R = 0.3f, Color = new(0.30f, 0.85f, 0.40f), Refl = 0.15f },
    };

    private static readonly Vector3 Sun = Vector3.Normalize(new Vector3(0.6f, 0.9f, -0.5f));
    private static readonly Vector3 CamPos = new(0f, 1.8f, -6.5f);
    private static readonly Vector3 CamForward = Vector3.Normalize(new Vector3(0f, 0.8f, 0f) - CamPos);
    private static readonly Vector3 CamRight = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, CamForward));
    private static readonly Vector3 CamUp = Vector3.Cross(CamForward, CamRight);
    private static readonly float HalfFov = MathF.Tan(0.5f * 45f * MathF.PI / 180f);

    private struct Hit
    {
        public float T;
        public Vector3 N;
        public Vector3 Albedo;
        public float Refl;
    }

    private static float Next(ref uint s)
    {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return (s >> 8) * (1f / 16777216f);
    }

    private static uint HashSeed(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
        return x == 0 ? 1u : x;
    }

    private static bool Intersect(Vector3 o, Vector3 d, float tmax, out Hit h)
    {
        h = default;
        float best = tmax;
        bool any = false;
        for (int i = 0; i < Spheres.Length; i++)
        {
            ref readonly var s = ref Spheres[i];
            var oc = o - s.C;
            float b = Vector3.Dot(oc, d);
            float c = Vector3.Dot(oc, oc) - s.R * s.R;
            float disc = b * b - c;
            if (disc < 0f) continue;
            float sq = MathF.Sqrt(disc);
            float t = -b - sq;
            if (t < 1e-3f) t = -b + sq;
            if (t > 1e-3f && t < best)
            {
                best = t;
                any = true;
                var p = o + d * t;
                h = new Hit { T = t, N = (p - s.C) / s.R, Albedo = s.Color, Refl = s.Refl };
            }
        }
        if (d.Y < -1e-5f)
        {
            float t = -o.Y / d.Y;
            if (t > 1e-3f && t < best)
            {
                best = t;
                any = true;
                var p = o + d * t;
                bool check = ((int)MathF.Floor(p.X) + (int)MathF.Floor(p.Z) & 1) == 0;
                h = new Hit { T = t, N = Vector3.UnitY, Albedo = check ? new Vector3(0.85f) : new Vector3(0.18f), Refl = 0.15f };
            }
        }
        return any;
    }

    private static bool Occluded(Vector3 o, Vector3 d)
    {
        for (int i = 0; i < Spheres.Length; i++)
        {
            ref readonly var s = ref Spheres[i];
            var oc = o - s.C;
            float b = Vector3.Dot(oc, d);
            float c = Vector3.Dot(oc, oc) - s.R * s.R;
            float disc = b * b - c;
            if (disc < 0f) continue;
            float t = -b - MathF.Sqrt(disc);
            if (t > 1e-3f) return true;
        }
        return false;
    }

    private static Vector3 Sky(Vector3 d)
    {
        float t = 0.5f * (d.Y + 1f);
        return Vector3.Lerp(new Vector3(1f, 1f, 1f), new Vector3(0.45f, 0.65f, 1f), t);
    }

    private static Vector3 RandomUnit(ref uint rng)
    {
        while (true)
        {
            var v = new Vector3(Next(ref rng) * 2f - 1f, Next(ref rng) * 2f - 1f, Next(ref rng) * 2f - 1f);
            float l = v.LengthSquared();
            if (l > 1e-4f && l <= 1f) return v / MathF.Sqrt(l);
        }
    }

    private static Vector3 Trace(Vector3 o, Vector3 d, ref uint rng, ref long rays, int depth)
    {
        rays++;
        if (!Intersect(o, d, 1e30f, out var h)) return Sky(d);

        var p = o + d * h.T;
        var n = h.N;
        var origin = p + n * 1e-3f;

        // luz do sol com sombra suave
        var l = Vector3.Normalize(Sun + RandomUnit(ref rng) * 0.06f);
        float ndl = MathF.Max(0f, Vector3.Dot(n, l));
        float direct = 0f;
        if (ndl > 0f)
        {
            rays++;
            if (!Occluded(origin, l)) direct = ndl;
        }

        var color = h.Albedo * (new Vector3(1f, 0.96f, 0.88f) * (direct * 1.1f) + new Vector3(0.12f, 0.15f, 0.2f));

        // luz indireta (um quique difuso)
        if (depth == 0)
        {
            var dir = Vector3.Normalize(n + RandomUnit(ref rng));
            color += h.Albedo * Trace(origin, dir, ref rng, ref rays, depth + 1) * 0.35f;
        }

        // reflexo
        if (h.Refl > 0.05f && depth < 4)
        {
            var r = d - 2f * Vector3.Dot(d, n) * n;
            color = Vector3.Lerp(color, Trace(origin, r, ref rng, ref rays, depth + 1), h.Refl);
        }
        return color;
    }

    private static Vector3 Pixel(int x, int y, int pass, ref long rays)
    {
        uint rng = HashSeed((uint)(y * Width + x) * 9781u + (uint)pass * 6271u + 1u);
        float u = ((x + Next(ref rng)) / Width * 2f - 1f) * HalfFov * Width / Height;
        float v = (1f - (y + Next(ref rng)) / Height * 2f) * HalfFov;
        var dir = Vector3.Normalize(CamForward + CamRight * u + CamUp * v);
        return Trace(CamPos, dir, ref rng, ref rays, 0);
    }

    private static byte Gamma(float v) => (byte)(MathF.Sqrt(Math.Clamp(v, 0f, 1f)) * 255f + 0.5f);

    private static long RenderTile(int tx, int ty, int pass, float[] acc, int[] cnt, byte[] bgra)
    {
        long rays = 0;
        int x1 = Math.Min(Width, (tx + 1) * Tile), y1 = Math.Min(Height, (ty + 1) * Tile);
        for (int y = ty * Tile; y < y1; y++)
            for (int x = tx * Tile; x < x1; x++)
            {
                var c = Pixel(x, y, pass, ref rays);
                int i = y * Width + x;
                acc[i * 3] += c.X; acc[i * 3 + 1] += c.Y; acc[i * 3 + 2] += c.Z;
                int n = ++cnt[i];
                bgra[i * 4] = Gamma(acc[i * 3 + 2] / n);
                bgra[i * 4 + 1] = Gamma(acc[i * 3 + 1] / n);
                bgra[i * 4 + 2] = Gamma(acc[i * 3] / n);
                bgra[i * 4 + 3] = 255;
            }
        return rays;
    }

    /// <summary>Assinatura de um pedaço fixo da imagem (uma passada, sem threads): tem de dar sempre o mesmo número.</summary>
    public static uint Checksum(int size = 24)
    {
        uint hash = 2166136261;
        long rays = 0;
        for (int y = Height / 2; y < Height / 2 + size; y++)
            for (int x = Width / 2; x < Width / 2 + size; x++)
            {
                var c = Pixel(x, y, 7, ref rays);
                hash = (hash ^ Gamma(c.X)) * 16777619;
                hash = (hash ^ Gamma(c.Y)) * 16777619;
                hash = (hash ^ Gamma(c.Z)) * 16777619;
            }
        return hash ^ (uint)rays;
    }

    public static RenderResult Run(TimeSpan duration, bool multiThread, RenderSurface surface, CancellationToken ct)
    {
        surface.Resize(Width, Height);
        surface.Info = $"{Width}×{Height}";
        var acc = new float[Width * Height * 3];
        var cnt = new int[Width * Height];
        var bgra = surface.Bgra;

        int tilesX = (Width + Tile - 1) / Tile, tilesY = (Height + Tile - 1) / Tile;
        var tiles = Enumerable.Range(0, tilesX * tilesY)
            .Select(i => (X: i % tilesX, Y: i / tilesX))
            .OrderBy(t => Math.Abs(t.X - tilesX / 2.0) + Math.Abs(t.Y - tilesY / 2.0)) // do centro para fora, como no Cinebench
            .ToArray();

        uint before = Checksum();
        long rays = 0;
        int pass = 0;
        var sw = Stopwatch.StartNew();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = multiThread ? Environment.ProcessorCount : 1,
            CancellationToken = ct,
        };
        try
        {
            while (sw.Elapsed < duration)
            {
                int p = pass;
                Parallel.For(0, tiles.Length, options, i =>
                {
                    if (sw.Elapsed >= duration) return;
                    long r = RenderTile(tiles[i].X, tiles[i].Y, p, acc, cnt, bgra);
                    surface.Seconds = sw.Elapsed.TotalSeconds;
                    surface.Rays = Interlocked.Add(ref rays, r);
                });
                surface.Passes = ++pass;
            }
        }
        catch (OperationCanceledException)
        {
            // interrompido pelo usuário: vale o que já foi feito
        }
        double seconds = Math.Max(0.001, sw.Elapsed.TotalSeconds);
        bool verified = Checksum() == before;
        double points = Interlocked.Read(ref rays) / seconds / 1e4;
        return new RenderResult(new BenchResult(multiThread ? "Test_CpuRender" : "Test_CpuRender1", points, "pts"), verified);
    }
}
