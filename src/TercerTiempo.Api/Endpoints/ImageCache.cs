namespace TercerTiempo.Api.Endpoints;

internal static class ImageCache
{
    // Escudos y fotos públicas llevan ?v= con la versión de la imagen: si la imagen cambia, cambia la URL, así que el
    // navegador puede guardarla un año sin volver a preguntar. Sin versión (enlaces antiguos), un día.
    public static void SetPublic(HttpContext context) =>
        context.Response.Headers.CacheControl = context.Request.Query.ContainsKey("v") ? "public, max-age=31536000, immutable" : "public, max-age=86400";
}
