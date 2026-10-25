using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Otimizador.App.Bench;

/// <summary>
/// Teste de render na placa de vídeo (no estilo dos testes gráficos clássicos): uma cena 3D animada (esferas que se fundem,
/// anel, chão, sombras suaves e oclusão ambiente) desenhada por ray marching em compute shader, quadro a quadro.
/// A nota é a média de quadros por segundo; a imagem aparece na tela enquanto o teste roda.
/// </summary>
public static class GpuRenderBenchmark
{
    private const string TickShader = @"
RWStructuredBuffer<float> Clock : register(u1);
[numthreads(1, 1, 1)]
void CSMain() { Clock[0] += 0.0166667f; }";

    private const string RenderShader = @"
RWStructuredBuffer<uint> Pixels : register(u0);
RWStructuredBuffer<float> Clock : register(u1);
#define W @W@
#define H @H@

float Smin(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

float Map(float3 p, float t)
{
    float floorD = p.y + 1.0;
    float3 c1 = float3(sin(t * 0.9) * 1.7, -0.1 + sin(t * 1.7) * 0.3, cos(t * 0.9) * 1.7);
    float3 c2 = float3(sin(t * 0.9 + 2.094) * 1.7, -0.1 + sin(t * 1.3 + 1.0) * 0.3, cos(t * 0.9 + 2.094) * 1.7);
    float3 c3 = float3(sin(t * 0.9 + 4.188) * 1.7, -0.1 + sin(t * 1.9 + 2.0) * 0.3, cos(t * 0.9 + 4.188) * 1.7);
    float s = length(p - c1) - 0.55;
    s = Smin(s, length(p - c2) - 0.55, 0.5);
    s = Smin(s, length(p - c3) - 0.55, 0.5);
    s = Smin(s, length(p - float3(0.0, 0.1 + sin(t) * 0.2, 0.0)) - 0.8, 0.6);
    float2 q = float2(length(p.xz) - 2.9, p.y + 0.55);
    float ring = length(q) - 0.12;
    return min(min(floorD, s), ring);
}

float3 Normal(float3 p, float t)
{
    float2 e = float2(0.002, 0.0);
    return normalize(float3(
        Map(p + e.xyy, t) - Map(p - e.xyy, t),
        Map(p + e.yxy, t) - Map(p - e.yxy, t),
        Map(p + e.yyx, t) - Map(p - e.yyx, t)));
}

float Shadow(float3 ro, float3 rd, float t)
{
    float res = 1.0;
    float tt = 0.03;
    [loop] for (int i = 0; i < 32; i++)
    {
        float h = Map(ro + rd * tt, t);
        res = min(res, 10.0 * h / tt);
        tt += clamp(h, 0.02, 0.4);
        if (res < 0.001 || tt > 14.0) break;
    }
    return saturate(res);
}

float Occlusion(float3 p, float3 n, float t)
{
    float occ = 0.0;
    float w = 1.0;
    [unroll] for (int i = 1; i <= 5; i++)
    {
        float h = 0.06 * i;
        occ += (h - Map(p + n * h, t)) * w;
        w *= 0.7;
    }
    return saturate(1.0 - 2.2 * occ);
}

float3 Shade(float2 pixel, float t)
{
    float2 uv = pixel / float2(W, H) * 2.0 - 1.0;
    uv.y = -uv.y;
    uv.x *= (float)W / (float)H;

    float3 ro = float3(sin(t * 0.25) * 5.2, 2.3 + sin(t * 0.4) * 0.5, -cos(t * 0.25) * 5.2);
    float3 f = normalize(float3(0.0, -0.2, 0.0) - ro);
    float3 r = normalize(cross(float3(0.0, 1.0, 0.0), f));
    float3 u = cross(f, r);
    float3 rd = normalize(f * 1.6 + r * uv.x + u * uv.y);

    float dist = 0.0;
    bool hit = false;
    [loop] for (int i = 0; i < 128; i++)
    {
        float d = Map(ro + rd * dist, t);
        if (d < 0.001) { hit = true; break; }
        dist += d;
        if (dist > 30.0) break;
    }

    float3 col = lerp(float3(0.05, 0.07, 0.12), float3(0.25, 0.4, 0.7), saturate(rd.y * 0.8 + 0.4));
    if (hit)
    {
        float3 p = ro + rd * dist;
        float3 n = Normal(p, t);
        float3 L = normalize(float3(0.6, 0.8, -0.4));
        float diff = saturate(dot(n, L)) * Shadow(p + n * 0.01, L, t);
        float occ = Occlusion(p, n, t);
        float3 albedo;
        if (p.y < -0.99)
        {
            float check = fmod(abs(floor(p.x) + floor(p.z)), 2.0);
            albedo = lerp(float3(0.15, 0.15, 0.17), float3(0.75, 0.75, 0.78), check);
        }
        else
        {
            albedo = 0.55 + 0.45 * cos(float3(0.0, 2.0, 4.0) + p.y * 2.0 + t * 0.7);
        }
        float3 refl = reflect(rd, n);
        float spec = pow(saturate(dot(refl, L)), 32.0) * diff;
        float sky = 0.5 + 0.5 * n.y;
        col = albedo * (diff * float3(1.05, 0.95, 0.85) + sky * float3(0.18, 0.22, 0.32) * occ) + spec * 0.6;
        col = lerp(col, float3(0.25, 0.4, 0.7), saturate(dist / 30.0) * 0.5);
    }

    return col;
}

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= W || id.y >= H) return;
    float t = Clock[0];
    // suavização 2x2: quatro raios por pixel (é o que deixa o teste pesado de verdade)
    float3 col = Shade(float2(id.xy) + float2(0.25, 0.25), t) + Shade(float2(id.xy) + float2(0.75, 0.25), t)
               + Shade(float2(id.xy) + float2(0.25, 0.75), t) + Shade(float2(id.xy) + float2(0.75, 0.75), t);
    col = pow(saturate(col * 0.25), 1.0 / 2.2);
    uint3 c = (uint3)(col * 255.0 + 0.5);
    Pixels[id.y * W + id.x] = 0xFF000000u | (c.x << 16) | (c.y << 8) | c.z;
}";

    /// <summary>Roda o teste. Retorna null se não houver GPU compatível com Direct3D 11.</summary>
    public static BenchResult? Run(TimeSpan duration, RenderSurface surface, CancellationToken ct)
    {
        try
        {
            if (D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.None, null, out ID3D11Device? device).Failure || device is null)
                return null;
            using (device)
            {
                int w = 1920, h = 1080;
                while (true)
                {
                    var result = RunAt(device, w, h, duration, surface, ct, allowDowngrade: w > 640, out bool tooSlow);
                    if (!tooSlow) return result;
                    w /= 2; // placa fraca: reduz a imagem para não travar o driver (o Windows reinicia a GPU se um quadro demorar demais)
                    h /= 2;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static BenchResult? RunAt(ID3D11Device device, int w, int h, TimeSpan duration, RenderSurface surface,
        CancellationToken ct, bool allowDowngrade, out bool tooSlow)
    {
        tooSlow = false;
        var ctx = device.ImmediateContext;
        int pixelBytes = w * h * 4;
        string size = $"{w}×{h}";

        using var tick = GpuBenchmark.Compile(device, TickShader);
        using var render = GpuBenchmark.Compile(device, RenderShader.Replace("@W@", w.ToString()).Replace("@H@", h.ToString()));
        using var pixels = device.CreateBuffer(GpuBenchmark.Desc(pixelBytes, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
        using var clock = device.CreateBuffer(GpuBenchmark.Desc(16, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
        using var pixelsUav = GpuBenchmark.Uav(device, pixels, w * h);
        using var clockUav = GpuBenchmark.Uav(device, clock, 4);
        using var small = device.CreateBuffer(GpuBenchmark.Desc(4096, BindFlags.None, ResourceUsage.Staging,
            CpuAccessFlags.Read, ResourceOptionFlags.None, 0));
        using var frame = device.CreateBuffer(GpuBenchmark.Desc(pixelBytes, BindFlags.None, ResourceUsage.Staging,
            CpuAccessFlags.Read, ResourceOptionFlags.None, 0));

        ctx.CSSetUnorderedAccessView(0u, pixelsUav);
        ctx.CSSetUnorderedAccessView(1u, clockUav);
        uint gx = (uint)((w + 15) / 16), gy = (uint)((h + 15) / 16);

        void OneFrame()
        {
            ctx.CSSetShader(tick);
            ctx.Dispatch(1, 1, 1);
            ctx.CSSetShader(render);
            ctx.Dispatch(gx, gy, 1);
            GpuBenchmark.Sync(ctx, small, pixels, 4096);
        }

        void CopyPreview()
        {
            ctx.CopySubresourceRegion(frame, 0u, 0u, 0u, 0u, pixels, 0u, new Box(0, 0, 0, pixelBytes, 1, 1));
            var mapped = ctx.Map(frame, 0u, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            Marshal.Copy(mapped.DataPointer, surface.Bgra, 0, pixelBytes);
            ctx.Unmap(frame, 0u);
        }

        // aquecimento: o primeiro quadro paga a compilação do driver; o segundo mede o custo real
        OneFrame();
        var probe = Stopwatch.StartNew();
        OneFrame();
        if (allowDowngrade && probe.ElapsedMilliseconds > 250)
        {
            tooSlow = true;
            return null;
        }

        surface.Resize(w, h);
        surface.Info = size;
        int frames = 0;
        var sw = Stopwatch.StartNew();
        var preview = Stopwatch.StartNew();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            OneFrame();
            frames++;
            surface.Seconds = sw.Elapsed.TotalSeconds;
            surface.Passes = frames;
            surface.Fps = frames / Math.Max(0.001, surface.Seconds);
            if (preview.ElapsedMilliseconds >= 250)
            {
                CopyPreview();
                preview.Restart();
            }
        }
        double seconds = Math.Max(0.001, sw.Elapsed.TotalSeconds);
        CopyPreview();
        return new BenchResult("Test_GpuRender", frames / seconds, "FPS");
    }
}
