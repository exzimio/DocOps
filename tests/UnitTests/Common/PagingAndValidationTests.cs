using DocOps.Application.Common;
using DocOps.Application.Identity;
using FluentValidation.Results;

namespace DocOps.UnitTests.Common;

public class PageRequestTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void Page_number_is_never_below_one(int requested, int expected)
    {
        Assert.Equal(expected, new PageRequest { Page = requested }.NormalizedPage);
    }

    [Theory]
    [InlineData(0, PageRequest.DefaultPageSize)]
    [InlineData(-1, PageRequest.DefaultPageSize)]
    [InlineData(10, 10)]
    [InlineData(5000, PageRequest.MaxPageSize)]
    public void Page_size_is_clamped_so_a_client_cannot_request_the_whole_table(int requested, int expected)
    {
        Assert.Equal(expected, new PageRequest { PageSize = requested }.NormalizedPageSize);
    }

    [Fact]
    public void Skip_is_derived_from_the_normalized_values()
    {
        Assert.Equal(40, new PageRequest { Page = 3, PageSize = 20 }.Skip);
        Assert.Equal(0, new PageRequest { Page = -1, PageSize = 20 }.Skip);
    }

    [Fact]
    public void Paged_result_reports_navigation_state()
    {
        PagedResult<string> result = new(["a", "b"], Page: 2, PageSize: 2, TotalCount: 5);

        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasPrevious);
        Assert.True(result.HasNext);
    }

    [Fact]
    public void Empty_page_has_no_navigation()
    {
        PagedResult<string> result = PagedResult<string>.Empty(page: 1, pageSize: 25);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasNext);
        Assert.False(result.HasPrevious);
    }
}

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@no-local-part")]
    public void Rejects_an_invalid_email(string email)
    {
        ValidationResult result = _validator.Validate(new LoginRequest(email, "Demo!Pass123"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginRequest.Email));
    }

    [Fact]
    public void Rejects_an_empty_password()
    {
        ValidationResult result = _validator.Validate(new LoginRequest("ana.ferreira@asteria.example", ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginRequest.Password));
    }

    [Fact]
    public void Accepts_a_well_formed_request()
    {
        Assert.True(_validator.Validate(new LoginRequest("ana.ferreira@asteria.example", "Demo!Pass123")).IsValid);
    }

    [Fact]
    public void Caps_the_password_length_so_an_oversized_payload_is_rejected_before_hashing()
    {
        ValidationResult result = _validator.Validate(
            new LoginRequest("ana.ferreira@asteria.example", new string('a', 500)));

        Assert.False(result.IsValid);
    }
}
