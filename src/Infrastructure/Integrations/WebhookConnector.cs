using System.Net;
using System.Net.Sockets;

namespace DocOps.Infrastructure.Integrations;

/// <summary>Resolves a webhook's host name. A seam so tests can answer with any address they need.</summary>
public interface IWebhookHostResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class DnsWebhookHostResolver : IWebhookHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>Why a delivery was not attempted. Its message is written for the integration log.</summary>
public sealed class WebhookDestinationRefusedException(string message) : Exception(message);

/// <summary>
/// Opens the webhook's TCP connection itself, so the address that was checked is the address that
/// is called.
///
/// Checking a host name when it is saved and letting the HTTP stack resolve it again at send time
/// leaves a gap: the name can resolve to a public address for the check and to 169.254.169.254 a
/// second later (DNS rebinding). Here the name is resolved once, per connection, every address is
/// checked, and the socket connects only to those addresses. A name with any internal address among
/// its answers is refused whole: picking the public one would still let whoever controls the zone
/// steer the next connection.
/// </summary>
public sealed class WebhookConnector(IWebhookHostResolver resolver)
{
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        string host = context.DnsEndPoint.Host;

        IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress? literal)
            ? [literal]
            : await resolver.ResolveAsync(host, cancellationToken);

        if (addresses.Length == 0)
        {
            throw new WebhookDestinationRefusedException("O nome do destino não corresponde a nenhum endereço.");
        }

        if (addresses.Any(WebhookEndpoint.IsInternal))
        {
            throw new WebhookDestinationRefusedException(
                "O destino foi recusado: o nome corresponde a um endereço interno.");
        }

        for (int index = 0; ; index++)
        {
            Socket socket = new(addresses[index].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(addresses[index], context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) when (index < addresses.Length - 1)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    /// <summary>The refusal behind a failed request, if that is why it failed.</summary>
    public static WebhookDestinationRefusedException? Refusal(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is WebhookDestinationRefusedException refusal)
            {
                return refusal;
            }
        }

        return null;
    }
}
