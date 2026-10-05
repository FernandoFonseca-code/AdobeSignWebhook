namespace AdobeSign.OnBase.Webhook.Api.Audit;

// What happened to one webhook call. The controller creates it, the processor fills in what it read from
// the payload and what it sent to OnBase, and WebhookAuditLog writes it to the audit file.
public sealed class WebhookAuditEntry
{
    public DateTime ReceivedAt { get; } = DateTime.Now;

    public string? RemoteIp { get; set; }

    public string Payload { get; set; } = string.Empty;

    // Read from the payload.
    public string? EventName { get; set; }

    public string? AdobeId { get; set; }

    public string? AgreementStatus { get; set; }

    // The value written to the OnBase status keyword after OnBase:StatusMap was applied.
    public string? OnBaseStatus { get; set; }

    // What the OnBase updater reported (documents updated, no documents found, or the error).
    public string? OnBaseResult { get; set; }

    public int ResponseCode { get; set; }
}
