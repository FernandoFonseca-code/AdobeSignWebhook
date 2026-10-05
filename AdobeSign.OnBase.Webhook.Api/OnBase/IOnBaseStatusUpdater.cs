namespace AdobeSign.OnBase.Webhook.Api.OnBase;

// Writes an Adobe Sign agreement status to the OnBase documents that carry the agreement's AdobeID.
public interface IOnBaseStatusUpdater
{
    // Returns what the update did (e.g. the DocIDs updated) for the audit log.
    // Throws when OnBase could not be updated, so the webhook returns 500 and Adobe Sign retries.
    Task<string> UpdateAgreementStatusAsync(string adobeId, string status, CancellationToken cancellationToken);
}
