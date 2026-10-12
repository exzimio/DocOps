using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace DocOps.Api.Infrastructure;

/// <summary>
/// The proxies whose <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> the API believes.
///
/// Behind nginx every request arrives from the proxy's address. Without these headers the login
/// limiter would count every client as one, so a single attacker could lock everyone out, and the
/// audit trail would record the proxy instead of the person. Trusting the headers from anyone else
/// would be worse: a client could name any address it liked. With nothing configured only loopback
/// is trusted, which is the framework's default.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Single proxy addresses, such as <c>10.0.0.5</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Networks in CIDR notation, such as the Docker Compose network <c>10.89.0.0/24</c>.</summary>
    public string[] KnownNetworks { get; set; } = [];

    public void ApplyTo(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // nginx appends the address it saw to whatever the client sent. Only that last entry is the
        // proxy's own statement; anything before it was written by the client.
        options.ForwardLimit = 1;

        foreach (string proxy in KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (string network in KnownNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    }
}
