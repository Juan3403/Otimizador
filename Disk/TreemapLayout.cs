using System.Windows;

namespace Otimizador.App.Disk;

/// <summary>Treemap "squarified" (Bruls et al.): retângulos proporcionais ao tamanho, o mais próximo possível de quadrados.</summary>
public static class TreemapLayout
{
    public static void Layout(IReadOnlyList<DiskNode> items, Rect bounds, List<(DiskNode Node, Rect Rect)> output)
    {
        var nodes = items.Where(n => n.Size > 0).ToList();
        if (nodes.Count == 0 || bounds.Width < 1 || bounds.Height < 1) return;
        double total = nodes.Sum(n => (double)n.Size);
        double scale = bounds.Width * bounds.Height / total;

        var r = bounds;
        int start = 0;
        while (start < nodes.Count)
        {
            double side = Math.Min(r.Width, r.Height);
            if (side < 0.5) break;

            double sum = 0, min = double.MaxValue, max = 0, prevWorst = double.MaxValue;
            int end = start;
            while (end < nodes.Count)
            {
                double a = nodes[end].Size * scale;
                double s2 = sum + a, mn = Math.Min(min, a), mx = Math.Max(max, a);
                double worst = Math.Max(side * side * mx / (s2 * s2), s2 * s2 / (side * side * mn));
                if (end > start && worst > prevWorst) break;
                sum = s2; min = mn; max = mx; prevWorst = worst;
                end++;
            }

            if (r.Width >= r.Height)
            {
                // coluna à esquerda
                double w = sum / r.Height;
                double y = r.Y;
                for (int i = start; i < end; i++)
                {
                    double h = nodes[i].Size * scale / w;
                    output.Add((nodes[i], new Rect(r.X, y, w, h)));
                    y += h;
                }
                r = new Rect(r.X + w, r.Y, Math.Max(0, r.Width - w), r.Height);
            }
            else
            {
                // linha no topo
                double h = sum / r.Width;
                double x = r.X;
                for (int i = start; i < end; i++)
                {
                    double w = nodes[i].Size * scale / h;
                    output.Add((nodes[i], new Rect(x, r.Y, w, h)));
                    x += w;
                }
                r = new Rect(r.X, r.Y + h, r.Width, Math.Max(0, r.Height - h));
            }
            start = end;
        }
    }
}
