using System.Net;
using DocOps.Infrastructure.Integrations;

namespace DocOps.UnitTests.Integrations;

/// <summary>Answers every name with the addresses it was given, and counts the lookups.</summary>
file sealed class FixedResolver(params string[] addresses) : IWebhookHostResolver
{
    public int Lookups { get; private set; }

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult(addresses.Select(IPAddress.Parse).ToArray());
    }
}

public class WebhookEndpointTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("10.0.0.5")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:a9fe:a9fe::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    public void Internal_addresses_are_recognised_in_both_families(string address) =>
        Assert.True(WebhookEndpoint.IsInternal(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("100.128.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("93.184.216.34")]
    [InlineData("::ffff:93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    public void Public_addresses_are_not_internal(string address) =>
        Assert.False(WebhookEndpoint.IsInternal(IPAddress.Parse(address)));

    [Theory]
    [InlineData("https://[fd00::1]/hook")]
    [InlineData("https://[::ffff:10.0.0.1]/hook")]
    [InlineData("https://100.64.0.1/hook")]
    [InlineData("https://0.0.0.0/hook")]
    [InlineData("https://[::1]/hook")]
    public void A_literal_internal_address_is_refused_when_saved(string endpoint) =>
        Assert.False(WebhookEndpoint.IsAcceptable(endpoint, out _));

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("fd00::5")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("93.184.216.34", "10.0.0.5")]
    public async Task A_name_that_resolves_to_any_internal_address_is_refused_before_connecting(params string[] answers)
    {
        FixedResolver resolver = new(answers);
        WebhookConnector connector = new(resolver);

        using SocketsHttpHandler handler = new() { ConnectCallback = connector.ConnectAsync, UseProxy = false };
        using HttpClient client = new(handler);

        HttpRequestException failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://hooks.example/docops"));

        Assert.NotNull(WebhookConnector.Refusal(failure));
        Assert.Equal(1, resolver.Lookups);
    }
}
