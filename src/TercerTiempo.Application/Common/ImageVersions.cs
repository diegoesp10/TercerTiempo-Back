namespace TercerTiempo.Application.Common;

// Las claves de escudos y fotos son {dueño}-{versión}.ext y nunca cambian de contenido: la versión va en la URL (?v=) para
// que el navegador no enseñe la imagen anterior después de cambiarla, y con ella la imagen se puede guardar un año. Las
// claves antiguas, sin versión, van sin ?v= y con la caché de un día.
public static class ImageVersions
{
    public static string Query(string key)
    {
        var name = Path.GetFileNameWithoutExtension(key);
        var dash = name.LastIndexOf('-');
        return dash >= 0 ? $"?v={name[(dash + 1)..]}" : "";
    }
}
