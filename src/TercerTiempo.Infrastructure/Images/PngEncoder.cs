using System.Buffers.Binary;
using System.IO.Compression;

namespace TercerTiempo.Infrastructure.Images;

// PNG de 8 bits por canal, sin librerías: RGB si no hay ningún píxel transparente, RGBA si lo hay. Cada fila lleva el filtro
// que la deja con menos suma absoluta (la heurística de libpng) y todo va comprimido con zlib al máximo.
internal static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        var opaque = true;
        for (var i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 255)
            {
                opaque = false;
                break;
            }
        }

        var channels = opaque ? 3 : 4;
        var stride = width * channels;
        var raw = new byte[height * (stride + 1)];
        var previous = new byte[stride];
        var current = new byte[stride];
        var candidate = new byte[stride];
        var best = new byte[stride];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < channels; c++) current[x * channels + c] = rgba[(y * width + x) * 4 + c];
            }

            var bestFilter = 0;
            var bestScore = long.MaxValue;
            for (var filter = 0; filter < 5; filter++)
            {
                long score = 0;
                for (var i = 0; i < stride; i++)
                {
                    int left = i >= channels ? current[i - channels] : 0;
                    int up = previous[i];
                    int upLeft = i >= channels ? previous[i - channels] : 0;
                    var predicted = filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => up,
                        3 => (left + up) >> 1,
                        _ => Paeth(left, up, upLeft),
                    };
                    var value = (byte)(current[i] - predicted);
                    candidate[i] = value;
                    score += value < 128 ? value : 256 - value;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestFilter = filter;
                    (best, candidate) = (candidate, best);
                }
            }

            var row = y * (stride + 1);
            raw[row] = (byte)bestFilter;
            Buffer.BlockCopy(best, 0, raw, row + 1, stride);
            (previous, current) = (current, previous);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = (byte)(opaque ? 2 : 6);

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(Crc(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu);
        output.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
