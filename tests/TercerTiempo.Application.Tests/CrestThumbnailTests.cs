using Microsoft.Extensions.Logging.Abstractions;
using StbImageSharp;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Infrastructure.Images;
using TercerTiempo.Infrastructure.Teams;

namespace TercerTiempo.Application.Tests;

public class CrestThumbnailTests
{
    [Theory]
    [InlineData(1, 64)]
    [InlineData(64, 64)]
    [InlineData(65, 128)]
    [InlineData(192, 192)]
    public void El_tamaño_pedido_sube_a_la_siguiente_miniatura(int requested, int expected) =>
        Assert.Equal(expected, CrestThumbnailSizes.Snap(requested));

    [Fact]
    public void Por_encima_de_la_mayor_se_sirve_el_original() => Assert.Null(CrestThumbnailSizes.Snap(193));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void El_png_se_lee_igual_que_se_escribio(bool transparent)
    {
        var (width, height) = (37, 23);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            pixels[i * 4] = (byte)(i * 7);
            pixels[i * 4 + 1] = (byte)(i * 13 + 5);
            pixels[i * 4 + 2] = (byte)(255 - i);
            pixels[i * 4 + 3] = transparent ? (byte)(i * 3) : (byte)255;
        }

        var decoded = ImageResult.FromMemory(PngEncoder.Encode(pixels, width, height), ColorComponents.RedGreenBlueAlpha);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(pixels, decoded.Data);
    }

    [Fact]
    public void Cada_pixel_nuevo_es_la_media_de_los_que_cubre_sin_oscurecer_los_bordes()
    {
        // 4×2 → 2×1: a la izquierda, rojo opaco con un píxel transparente (negro); a la derecha, blanco y negro opacos.
        byte[] pixels =
        [
            255, 0, 0, 255, 0, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 255,
            255, 0, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255,
        ];

        var shrunk = ImageShrink.AreaAverage(pixels, 4, 2, 2, 1);

        // El transparente no aporta color: sigue siendo rojo puro, con tres cuartos de opacidad.
        Assert.Equal([255, 0, 0, 191], shrunk[..4]);
        Assert.Equal([128, 128, 128, 255], shrunk[4..]);
    }

    [Fact]
    public async Task Un_escudo_grande_se_sirve_reducido_y_uno_pequeño_tal_cual()
    {
        var thumbnails = new CrestThumbnails(NullLogger<CrestThumbnails>.Instance);
        var big = Png(500, 829);
        var small = Png(40, 40);

        var reduced = await thumbnails.GetAsync("big-1.png", 128, _ => Open(big), CancellationToken.None);
        var same = await thumbnails.GetAsync("small-1.png", 128, _ => Open(small), CancellationToken.None);

        var image = ImageResult.FromStream(reduced!.Content, ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((77, 128), (image.Width, image.Height));
        Assert.Equal(small, ToArray(same!.Content));
    }

    [Fact]
    public async Task Lo_que_no_se_puede_leer_se_sirve_tal_cual()
    {
        var thumbnails = new CrestThumbnails(NullLogger<CrestThumbnails>.Instance);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        byte[] broken = [137, 80, 78, 71, 1, 2, 3];

        var vector = await thumbnails.GetAsync("a-1.svg", 64, _ => Task.FromResult<CrestFile?>(new CrestFile(new MemoryStream(svg), "image/svg+xml")), CancellationToken.None);
        var bad = await thumbnails.GetAsync("b-1.png", 64, _ => Open(broken), CancellationToken.None);

        Assert.Equal("image/svg+xml", vector!.ContentType);
        Assert.Equal(broken, ToArray(bad!.Content));
    }

    private static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(i / 4 % width);
            pixels[i + 1] = (byte)(i / 4 / width);
            pixels[i + 2] = 90;
            pixels[i + 3] = 255;
        }

        return PngEncoder.Encode(pixels, width, height);
    }

    private static Task<CrestFile?> Open(byte[] bytes) => Task.FromResult<CrestFile?>(new CrestFile(new MemoryStream(bytes), "image/png"));

    private static byte[] ToArray(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
