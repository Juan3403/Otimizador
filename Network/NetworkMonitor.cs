using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Otimizador.App.Network;

public sealed record NetworkSample(double DownBytesPerSec, double UpBytesPerSec, double? PingMs);

public sealed record ProcessConnections(string Name, int Connections);

/// <summary>Velocidade de download/upload dos adaptadores ativos, latência (ping) e quais programas têm conexões abertas.</summary>
public sealed class NetworkMonitor
{
    private long _lastRx = -1, _lastTx = -1;
    private DateTime _lastTime;
    private int _pingCounter;
    private double? _lastPing;

    private static IEnumerable<NetworkInterface> Active() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(n =>
            n.OperationalStatus == OperationalStatus.Up
            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase));

    /// <summary>Chame uma vez por segundo. A primeira chamada só marca o ponto de partida.</summary>
    public async Task<NetworkSample> SampleAsync()
    {
        long rx = 0, tx = 0;
        foreach (var n in Active())
        {
            try
            {
                var st = n.GetIPv4Statistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
            }
            catch
            {
                // adaptador sem estatísticas
            }
        }

        var now = DateTime.UtcNow;
        double down = 0, up = 0;
        if (_lastRx >= 0)
        {
            double seconds = Math.Max(0.2, (now - _lastTime).TotalSeconds);
            down = Math.Max(0, (rx - _lastRx) / seconds);
            up = Math.Max(0, (tx - _lastTx) / seconds);
        }
        _lastRx = rx;
        _lastTx = tx;
        _lastTime = now;

        if (_pingCounter++ % 4 == 0) _lastPing = await PingAsync();
        return new NetworkSample(down, up, _lastPing);
    }

    private static async Task<double?> PingAsync()
    {
        try
        {
            using var p = new Ping();
            var reply = await p.SendPingAsync("1.1.1.1", 1000);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        if (bits >= 1e9) return $"{bits / 1e9:0.0} Gbps";
        if (bits >= 1e6) return $"{bits / 1e6:0.0} Mbps";
        if (bits >= 1e3) return $"{bits / 1e3:0} kbps";
        return $"{bits:0} bps";
    }

    // ---- Conexões TCP por programa ----
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const uint StateEstablished = 5;

    /// <summary>Programas com mais conexões TCP estabelecidas (sem contar as locais).</summary>
    public static List<ProcessConnections> TopConnections(int take = 8)
    {
        var counts = new Dictionary<int, int>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0) != 0) return new();
            int rows = Marshal.ReadInt32(buffer);
            IntPtr row = buffer + 4;
            for (int i = 0; i < rows; i++, row += 24)
            {
                uint state = (uint)Marshal.ReadInt32(row);
                uint remote = (uint)Marshal.ReadInt32(row + 12);
                int pid = Marshal.ReadInt32(row + 20);
                // 127.x.x.x (loopback) em ordem de rede: o primeiro byte é 127
                if (state != StateEstablished || (remote & 0xFF) == 127 || pid <= 4) continue;
                counts[pid] = counts.GetValueOrDefault(pid) + 1;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pid, n) in counts)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                byName[p.ProcessName] = byName.GetValueOrDefault(p.ProcessName) + n;
            }
            catch
            {
                // processo encerrou
            }
        }
        return byName.OrderByDescending(kv => kv.Value).Take(take).Select(kv => new ProcessConnections(kv.Key, kv.Value)).ToList();
    }
}
