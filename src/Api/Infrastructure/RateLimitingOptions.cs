namespace DocOps.Api.Infrastructure;

/// <summary>
/// Request limits, configurable so a shared-IP environment (a NAT'd office, a CI runner, a local
/// machine running the whole test suite) can be tuned without weakening the defaults that ship.
/// The primary brute-force control is the per-account lockout in Identity, which no shared address
/// can dilute.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int GlobalPermitLimit { get; set; } = 300;
    public int GlobalWindowSeconds { get; set; } = 60;

    public int AuthenticationPermitLimit { get; set; } = 10;
    public int AuthenticationWindowSeconds { get; set; } = 300;
}
