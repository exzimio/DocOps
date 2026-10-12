using DocOps.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Configuration;

namespace DocOps.UnitTests.Persistence;

public class SeedOptionsTests
{
    private static SeedOptions Bind(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        SeedOptions options = new();
        SeedOptions.Bind(configuration, options);

        return options;
    }

    [Fact]
    public void The_compose_variable_sets_the_demo_password_and_wins_over_the_section()
    {
        SeedOptions options = Bind(new()
        {
            ["Seed:DefaultPassword"] = "From_The_Section_2026!",
            [SeedOptions.PasswordVariable] = "From_The_Variable_2026!"
        });

        Assert.Equal("From_The_Variable_2026!", options.DefaultPassword);
    }

    [Fact]
    public void Without_the_variable_the_section_decides()
    {
        SeedOptions options = Bind(new() { ["Seed:DefaultPassword"] = "From_The_Section_2026!", ["Seed:Enabled"] = "true" });

        Assert.Equal("From_The_Section_2026!", options.DefaultPassword);
        Assert.True(options.Enabled);
    }

    [Fact]
    public void An_empty_variable_does_not_blank_the_password()
    {
        SeedOptions options = Bind(new() { [SeedOptions.PasswordVariable] = "" });

        Assert.Equal("Demo!Pass123", options.DefaultPassword);
    }
}
