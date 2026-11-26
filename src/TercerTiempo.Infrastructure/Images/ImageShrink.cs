namespace TercerTiempo.Infrastructure.Images;

internal static class ImageShrink
{
    // Reducción por media de área: cada píxel nuevo es la media de los que cubre, ponderada por cuánto de cada uno cae
    // dentro. Los colores se ponderan con su alfa para que los bordes transparentes no se oscurezcan. Solo reduce.
    public static byte[] AreaAverage(byte[] rgba, int width, int height, int newWidth, int newHeight)
    {
        var columns = Weights(width, newWidth);
        var rowsWeights = Weights(height, newHeight);

        // Primera pasada, a lo ancho: cada canal guarda la suma de color × alfa, y el alfa su media.
        var rows = new float[newWidth * height * 4];
        for (var y = 0; y < height; y++)
        {
            var source = y * width * 4;
            var target = y * newWidth * 4;
            for (var x = 0; x < newWidth; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                foreach (var (index, weight) in columns[x])
                {
                    var p = source + index * 4;
                    var alpha = rgba[p + 3] * weight;
                    r += rgba[p] * alpha;
                    g += rgba[p + 1] * alpha;
                    b += rgba[p + 2] * alpha;
                    a += alpha;
                }

                var o = target + x * 4;
                rows[o] = r;
                rows[o + 1] = g;
                rows[o + 2] = b;
                rows[o + 3] = a;
            }
        }

        // Segunda pasada, a lo alto, y vuelta a color sin alfa (color = suma de color × alfa / alfa).
        var result = new byte[newWidth * newHeight * 4];
        for (var y = 0; y < newHeight; y++)
        {
            for (var x = 0; x < newWidth; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                foreach (var (index, weight) in rowsWeights[y])
                {
                    var p = (index * newWidth + x) * 4;
                    r += rows[p] * weight;
                    g += rows[p + 1] * weight;
                    b += rows[p + 2] * weight;
                    a += rows[p + 3] * weight;
                }

                if (a < 0.5f) continue;
                var o = (y * newWidth + x) * 4;
                result[o] = ToByte(r / a);
                result[o + 1] = ToByte(g / a);
                result[o + 2] = ToByte(b / a);
                result[o + 3] = ToByte(a);
            }
        }

        return result;
    }

    private static (int Index, float Weight)[][] Weights(int source, int target)
    {
        var scale = (double)source / target;
        var weights = new (int, float)[target][];
        for (var i = 0; i < target; i++)
        {
            var start = i * scale;
            var end = Math.Min(source, (i + 1) * scale);
            var first = (int)Math.Floor(start);
            var last = Math.Min(source - 1, (int)Math.Ceiling(end) - 1);
            var taps = new (int, float)[last - first + 1];
            for (var j = first; j <= last; j++)
            {
                var overlap = Math.Min(end, j + 1) - Math.Max(start, j);
                taps[j - first] = (j, (float)(overlap / scale));
            }

            weights[i] = taps;
        }

        return weights;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}
