using DocOps.Domain.Signatures;

namespace DocOps.UnitTests.Signatures;

/// <summary>
/// The rules that decide who may act on a signature request, and when.
///
/// The one worth pinning is that signing is personal: no permission, seniority or administrative
/// role lets one person sign in another's name, because a signature is a statement by someone about
/// a document and there is no meaningful sense in which it is made on their behalf.
/// </summary>
public class SignatureLifecycleTests
{
    private static readonly Guid Signer = Guid.CreateVersion7();
    private static readonly Guid Requester = Guid.CreateVersion7();
    private static readonly Guid Stranger = Guid.CreateVersion7();

    private static SignatureRequest Request(SignatureRequestStatus status = SignatureRequestStatus.Pending) => new()
    {
        RequestedContentHash = "abc123",
        SignerDisplayName = "Quem assina",
        SignerEmail = "assina@exemplo.test",
        RequestedByDisplayName = "Quem pede",
        SignerUserId = Signer,
        RequestedByUserId = Requester,
        Status = status
    };

    [Fact]
    public void Only_the_named_signer_signs()
    {
        Assert.True(SignatureLifecycle.CanSign(Request(), Signer));
        Assert.False(SignatureLifecycle.CanSign(Request(), Requester));
        Assert.False(SignatureLifecycle.CanSign(Request(), Stranger));
    }

    [Theory]
    [InlineData(SignatureRequestStatus.Signed)]
    [InlineData(SignatureRequestStatus.Declined)]
    [InlineData(SignatureRequestStatus.Cancelled)]
    public void A_closed_request_cannot_be_signed_again(SignatureRequestStatus status) =>
        Assert.False(SignatureLifecycle.CanSign(Request(status), Signer));

    [Fact]
    public void Both_sides_may_withdraw_while_it_is_open()
    {
        Assert.True(SignatureLifecycle.CanCancel(Request(), Requester));
        Assert.True(SignatureLifecycle.CanCancel(Request(), Signer));
        Assert.False(SignatureLifecycle.CanCancel(Request(), Stranger));
        Assert.False(SignatureLifecycle.CanCancel(Request(SignatureRequestStatus.Signed), Requester));
    }

    [Fact]
    public void Content_that_differs_by_one_character_is_content_that_changed()
    {
        SignatureRequest request = Request();

        Assert.False(SignatureLifecycle.ContentHasChanged(request, "abc123"));
        Assert.True(SignatureLifecycle.ContentHasChanged(request, "abc124"));
        Assert.True(SignatureLifecycle.ContentHasChanged(request, string.Empty));

        // Hexadecimal digests are compared exactly, never case-insensitively: a comparison that
        // forgave case would forgive the one thing this field exists to detect.
        Assert.True(SignatureLifecycle.ContentHasChanged(request, "ABC123"));
    }
}
