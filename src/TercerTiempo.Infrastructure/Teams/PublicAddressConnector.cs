using System.Net;
using System.Net.Sockets;

namespace TercerTiempo.Infrastructure.Teams;

// Las imágenes se descargan de direcciones que escribe alguien (el administrador, al importar un escudo): solo se conecta
// a direcciones públicas, nunca a la red interna (Docker, el router, 127.0.0.1, metadatos de la nube). Se comprueba la IP
// a la que se conecta de verdad, así que tampoco cuela por una redirección ni por un DNS que cambie entre consultas.
internal static class PublicAddressConnector
{
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var address = addresses.FirstOrDefault(IsPublic)
            ?? throw new HttpRequestException($"{host} no apunta a una dirección pública.");

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast && (first & 0xFE) != 0xFC;
        }

        var b = address.GetAddressBytes();
        return !(b[0] == 0
            || b[0] == 10
            || b[0] == 127
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || b[0] >= 224);
    }
}
