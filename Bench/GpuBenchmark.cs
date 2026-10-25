using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Otimizador.App.Bench;

public sealed record GpuResult(string Adapter, BenchResult Compute, BenchResult Memory);

/// <summary>
/// Teste de GPU com Direct3D 11 (compute shaders): mede poder de cálculo em GFLOPS e a velocidade da memória de vídeo em GB/s.
/// A conta da GPU é conferida com a mesma conta feita na CPU; se não bater, o teste acusa erro.
/// </summary>
public static class GpuBenchmark
{
    private const int ComputeThreads = 1 << 20;   // 1 milhão de threads
    private const int ComputeIterations = 1024;
    private const int FmaPerIteration = 8;
    private const int MemElements = 1 << 23;      // 8 milhões de float4 = 128 MB

    private const string ComputeShader = @"
RWStructuredBuffer<float> Output : register(u0);
[numthreads(256, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    float a = (id.x & 1023) * 0.0009765625f + 1.0f;
    float b = 1.0000001f;
    float c = 0.5f;
    [loop]
    for (int i = 0; i < 1024; i++)
    {
        a = a * b + c; c = c * b + a * 0.000001f;
        a = a * b + c; c = c * b + a * 0.000001f;
        a = a * b + c; c = c * b + a * 0.000001f;
        a = a * b + c; c = c * b + a * 0.000001f;
    }
    Output[id.x] = a + c;
}";

    private const string CopyShader = @"
StructuredBuffer<float4> Source : register(t0);
RWStructuredBuffer<float4> Dest : register(u0);
[numthreads(256, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    Dest[id.x] = Source[id.x] + 1.0f;
}";

    internal static BufferDescription Desc(int bytes, BindFlags bind, ResourceUsage usage, CpuAccessFlags cpu, ResourceOptionFlags opt, int stride) =>
        new((uint)bytes, bind, usage, cpu, opt, (uint)stride);

    internal static ID3D11ComputeShader Compile(ID3D11Device device, string source)
    {
        Compiler.Compile(source, "CSMain", "gpu-test", "cs_5_0", out var blob, out var errors).CheckError();
        errors?.Dispose();
        using (blob) return device.CreateComputeShader(blob.AsBytes());
    }

    internal static ID3D11UnorderedAccessView Uav(ID3D11Device device, ID3D11Buffer buffer, int elements) =>
        device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription
        {
            Format = Format.Unknown,
            ViewDimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new BufferUnorderedAccessView { FirstElement = 0, NumElements = (uint)elements },
        });

    /// <summary>Espera a GPU terminar o que foi enviado, copiando um pedaço pequeno para a memória do PC.</summary>
    internal static void Sync(ID3D11DeviceContext ctx, ID3D11Buffer staging, ID3D11Buffer source, int bytes)
    {
        ctx.CopySubresourceRegion(staging, 0u, 0u, 0u, 0u, source, 0u, new Box(0, 0, 0, bytes, 1, 1));
        var mapped = ctx.Map(staging, 0u, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        ctx.Unmap(staging, 0u);
    }

    /// <summary>Roda o teste. Retorna null se não houver GPU compatível com Direct3D 11.</summary>
    public static GpuResult? Run(TimeSpan perPhase, CancellationToken ct)
    {
        try
        {
            if (D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.None, null, out ID3D11Device? device).Failure || device is null)
                return null;
            using (device)
            {
                var ctx = device.ImmediateContext;
                string adapter = "GPU";
                try
                {
                    using var dxgi = device.QueryInterface<IDXGIDevice>();
                    using var ad = dxgi.GetAdapter();
                    adapter = ad.Description.Description;
                }
                catch
                {
                    // nome opcional
                }

                var compute = RunCompute(device, ctx, perPhase, ct);
                ct.ThrowIfCancellationRequested();
                var memory = RunMemory(device, ctx, perPhase, ct);
                return new GpuResult(adapter, compute, memory);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw; // a GPU calculou errado: o usuário precisa saber
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Teste da memória de vídeo: grava um padrão que depende do endereço e depois confere, contando divergências na GPU.</summary>
    private sealed class VramTest : IDisposable
    {
        private const int Elements = 1 << 25;                  // 128 MB de uint
        private const int Groups = Elements / (256 * 4);        // cada thread cuida de 4 posições
        private const string Common = @"
RWStructuredBuffer<uint> Mem : register(u0);
RWStructuredBuffer<uint> Bad : register(u1);
uint H(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
[numthreads(256, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    for (uint k = 0; k < 4; k++)
    {
        uint i = id.x * 4 + k;
        uint want = H(i ^ SALT);
";
        private const string Fill = Common + "        Mem[i] = want;\n    }\n}";
        private const string Verify = Common + "        if (Mem[i] != want) { uint o; InterlockedAdd(Bad[0], 1, o); }\n    }\n}";

        private readonly ID3D11Buffer _mem, _bad, _staging;
        private readonly ID3D11UnorderedAccessView _memUav, _badUav;
        private readonly ID3D11ComputeShader[] _fill = new ID3D11ComputeShader[2];
        private readonly ID3D11ComputeShader[] _verify = new ID3D11ComputeShader[2];
        private uint _lastBad;

        public VramTest(ID3D11Device device)
        {
            _mem = device.CreateBuffer(Desc(Elements * 4, BindFlags.UnorderedAccess, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
            _bad = device.CreateBuffer(Desc(16, BindFlags.UnorderedAccess, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
            _staging = device.CreateBuffer(Desc(16, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, ResourceOptionFlags.None, 0));
            _memUav = Uav(device, _mem, Elements);
            _badUav = Uav(device, _bad, 4);
            string[] salts = { "0x1234ABCDu", "0xA5C3F00Du" };
            for (int i = 0; i < 2; i++)
            {
                _fill[i] = Compile(device, Fill.Replace("SALT", salts[i]));
                _verify[i] = Compile(device, Verify.Replace("SALT", salts[i]));
            }
        }

        /// <summary>Confere o padrão da rodada anterior e grava o desta. Retorna quantas posições estavam erradas.</summary>
        public uint Step(ID3D11DeviceContext ctx, int round, CancellationToken ct)
        {
            ctx.CSSetUnorderedAccessView(0u, _memUav);
            ctx.CSSetUnorderedAccessView(1u, _badUav);
            if (round > 0)
            {
                ctx.CSSetShader(_verify[(round - 1) & 1]);
                ctx.Dispatch((uint)Groups, 1, 1);
            }
            ctx.CSSetShader(_fill[round & 1]);
            ctx.Dispatch((uint)Groups, 1, 1);

            ctx.CopySubresourceRegion(_staging, 0u, 0u, 0u, 0u, _bad, 0u, new Box(0, 0, 0, 4, 1, 1));
            var mapped = ctx.Map(_staging, 0u, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            uint total = (uint)Marshal.ReadInt32(mapped.DataPointer);
            ctx.Unmap(_staging, 0u);
            uint fresh = total - _lastBad;
            _lastBad = total;
            return fresh;
        }

        public void Dispose()
        {
            foreach (var s in _fill) s?.Dispose();
            foreach (var s in _verify) s?.Dispose();
            _memUav.Dispose(); _badUav.Dispose();
            _mem.Dispose(); _bad.Dispose(); _staging.Dispose();
        }
    }

    /// <summary>
    /// Carga contínua para o teste de estabilidade: repete o cálculo e compara 1024 resultados com os da primeira passada
    /// (a mesma conta no mesmo chip tem de dar sempre o mesmo número; se mudar, a GPU errou).
    /// Retorna false se não houver GPU compatível com Direct3D 11.
    /// </summary>
    public static bool StressLoop(CancellationToken ct, Action onPass, Action onError)
    {
        try
        {
            if (D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.None, null, out ID3D11Device? device).Failure || device is null)
                return false;
            using (device)
            {
                var ctx = device.ImmediateContext;
                int bytes = ComputeThreads * sizeof(float);
                using var cs = Compile(device, ComputeShader);
                using var output = device.CreateBuffer(Desc(bytes, BindFlags.UnorderedAccess, ResourceUsage.Default,
                    CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, sizeof(float)));
                using var uav = Uav(device, output, ComputeThreads);
                using var staging = device.CreateBuffer(Desc(4096, BindFlags.None, ResourceUsage.Staging,
                    CpuAccessFlags.Read, ResourceOptionFlags.None, 0));
                ctx.CSSetShader(cs);
                ctx.CSSetUnorderedAccessView(0u, uav);
                int groups = ComputeThreads / 256;

                // Memória de vídeo: grava um padrão em 128 MB, e em outra passada confere se tudo continua igual.
                VramTest? vram = null;
                try { vram = new VramTest(device); } catch { /* pouca memória de vídeo: segue só com o cálculo */ }

                float[]? baseline = null;
                var current = new float[1024];
                int round = 0;
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        ctx.CSSetShader(cs);
                        ctx.CSSetUnorderedAccessView(0u, uav);
                        for (int i = 0; i < 4; i++) ctx.Dispatch((uint)groups, 1, 1);
                        ctx.CopySubresourceRegion(staging, 0u, 0u, 0u, 0u, output, 0u, new Box(0, 0, 0, 4096, 1, 1));
                        var mapped = ctx.Map(staging, 0u, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                        Marshal.Copy(mapped.DataPointer, current, 0, current.Length);
                        ctx.Unmap(staging, 0u);

                        if (baseline is null) baseline = (float[])current.Clone();
                        else if (!current.AsSpan().SequenceEqual(baseline)) onError();
                        onPass();

                        if (vram is not null && vram.Step(ctx, round++, ct) > 0) onError();
                    }
                }
                finally
                {
                    vram?.Dispose();
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static BenchResult RunCompute(ID3D11Device device, ID3D11DeviceContext ctx, TimeSpan duration, CancellationToken ct)
    {
        int bytes = ComputeThreads * sizeof(float);
        using var cs = Compile(device, ComputeShader);
        using var output = device.CreateBuffer(Desc(bytes, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, sizeof(float)));
        using var uav = Uav(device, output, ComputeThreads);
        using var staging = device.CreateBuffer(Desc(4096, BindFlags.None, ResourceUsage.Staging,
            CpuAccessFlags.Read, ResourceOptionFlags.None, 0));

        ctx.CSSetShader(cs);
        ctx.CSSetUnorderedAccessView(0u, uav);
        int groups = ComputeThreads / 256;

        // aquecimento (e confirma que o shader roda)
        ctx.Dispatch((uint)groups, 1, 1);
        Sync(ctx, staging, output, 4096);

        int dispatches = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            for (int i = 0; i < 8; i++) ctx.Dispatch((uint)groups, 1, 1);
            Sync(ctx, staging, output, 4096);
            dispatches += 8;
        }
        double seconds = sw.Elapsed.TotalSeconds;

        // confere o resultado de uma thread (id 0) com a conta feita na CPU
        ctx.CopySubresourceRegion(staging, 0u, 0u, 0u, 0u, output, 0u, new Box(0, 0, 0, 4096, 1, 1));
        var mapped = ctx.Map(staging, 0u, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        float gpuValue = Marshal.PtrToStructure<float>(mapped.DataPointer);
        ctx.Unmap(staging, 0u);

        float a = 1.0f, b = 1.0000001f, c = 0.5f;
        for (int i = 0; i < ComputeIterations; i++)
            for (int k = 0; k < 4; k++)
            {
                a = a * b + c; c = c * b + a * 0.000001f;
            }
        float cpuValue = a + c;
        if (float.IsNaN(gpuValue) || Math.Abs(gpuValue - cpuValue) > Math.Abs(cpuValue) * 0.01f)
            throw new InvalidOperationException($"A GPU calculou {gpuValue}, esperado {cpuValue}: possível instabilidade.");

        // cada iteração do laço: 4 x (2 FMAs) = 8 FMAs = 16 FLOPs por thread
        double flops = (double)ComputeThreads * ComputeIterations * FmaPerIteration * 2 * dispatches;
        return new BenchResult("Test_GpuCompute", flops / seconds / 1e9, "GFLOPS");
    }

    private static BenchResult RunMemory(ID3D11Device device, ID3D11DeviceContext ctx, TimeSpan duration, CancellationToken ct)
    {
        int stride = 16;
        int bytes = MemElements * stride;
        using var cs = Compile(device, CopyShader);
        using var src = device.CreateBuffer(Desc(bytes, BindFlags.ShaderResource, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, stride));
        using var dst = device.CreateBuffer(Desc(bytes, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, stride));
        using var srv = device.CreateShaderResourceView(src, new ShaderResourceViewDescription
        {
            Format = Format.Unknown,
            ViewDimension = Vortice.Direct3D.ShaderResourceViewDimension.Buffer,
            Buffer = new BufferShaderResourceView { FirstElement = 0, NumElements = (uint)MemElements },
        });
        using var uav = Uav(device, dst, MemElements);
        using var staging = device.CreateBuffer(Desc(4096, BindFlags.None, ResourceUsage.Staging,
            CpuAccessFlags.Read, ResourceOptionFlags.None, 0));

        ctx.CSSetShader(cs);
        ctx.CSSetShaderResource(0u, srv);
        ctx.CSSetUnorderedAccessView(0u, uav);
        int groups = MemElements / 256;

        ctx.Dispatch((uint)groups, 1, 1);
        Sync(ctx, staging, dst, 4096);

        int dispatches = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            for (int i = 0; i < 4; i++) ctx.Dispatch((uint)groups, 1, 1);
            Sync(ctx, staging, dst, 4096);
            dispatches += 4;
        }
        double seconds = sw.Elapsed.TotalSeconds;

        // lê e escreve o buffer inteiro a cada passada
        double gb = 2.0 * bytes * dispatches / 1e9;
        return new BenchResult("Test_GpuMem", gb / seconds, "GB/s");
    }
}


