using ApexPost.Domain.Enums;

namespace ApexPost.Application.MoneyOrders;

/// <summary>Decides an identity-verification outcome. The clerk records the document; the verifier decides the result.</summary>
public interface IIdentityVerifier
{
    IReadOnlyCollection<string> SupportedDocumentTypes { get; }

    Task<IdentityVerificationOutcome> VerifyAsync(string documentType, string documentNumber, CancellationToken cancellationToken = default);
}
