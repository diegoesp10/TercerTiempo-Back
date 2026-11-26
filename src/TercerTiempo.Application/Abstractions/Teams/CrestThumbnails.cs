namespace TercerTiempo.Application.Abstractions.Teams;

// Las listas pintan los escudos de 20 a 56 px y el original puede tener 500×829 px y 120 KB: se sirven miniaturas.
public interface ICrestThumbnails
{
    Task<CrestFile?> GetAsync(string crestKey, int size, Func<CancellationToken, Task<CrestFile?>> openOriginal, CancellationToken cancellationToken);
}

public static class CrestThumbnailSizes
{
    private static readonly int[] Sizes = [64, 128, 192];

    // Solo hay tres miniaturas por escudo: lo pedido sube a la siguiente; por encima de la mayor, el original.
    public static int? Snap(int requested)
    {
        foreach (var size in Sizes)
        {
            if (requested <= size) return size;
        }

        return null;
    }
}
