using System.Text.Json;
using AdobeSign.OnBase.Webhook.Api.Audit;
using AdobeSign.OnBase.Webhook.Api.OnBase;
using Microsoft.Extensions.Options;

namespace AdobeSign.OnBase.Webhook.Api.AdobeSign;

// Turns one Adobe Sign webhook payload into an OnBase status keyword update.
public sealed class AdobeAgreementEventProcessor(
    IOnBaseStatusUpdater onBaseStatusUpdater,
    IOptions<OnBaseOptions> options,
    ILogger<AdobeAgreementEventProcessor> logger)
{
    // Copied with a case-insensitive comparer so "signed" and "SIGNED" match the same entry.
    private readonly Dictionary<string, string> statusMap = new(options.Value.StatusMap, StringComparer.OrdinalIgnoreCase);

    // Reads the event, agreement ID and status from the payload, maps the status and hands it to OnBase.
    // Records each step in the audit entry, so the audit log shows how far a failed event got.
    // Throws JsonException when the payload is not an Adobe Sign agreement event (the controller returns 400).
    public async Task ProcessAsync(string payload, WebhookAuditEntry audit, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        // Adobe Sign payload: { "event": "AGREEMENT_ACTION_COMPLETED", "agreement": { "id": "...", "status": "OUT_FOR_SIGNATURE" } }
        var eventName = GetString(root, "event");
        var adobeId = GetString(root, "agreement", "id");
        var agreementStatus = GetString(root, "agreement", "status");

        audit.EventName = eventName;
        audit.AdobeId = adobeId;
        audit.AgreementStatus = agreementStatus;

        if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(adobeId))
        {
            throw new JsonException("Adobe Sign payload must contain an event name and agreement ID.");
        }

        var status = MapStatus(eventName, agreementStatus);
        audit.OnBaseStatus = status;

        audit.OnBaseResult = await onBaseStatusUpdater.UpdateAgreementStatusAsync(adobeId, status, cancellationToken);
        logger.LogInformation("Pushed Adobe Sign event {EventName} (status {Status}) for AdobeID {AdobeId} to OnBase", eventName, status, adobeId);
    }

    // Picks the value written to the OnBase status keyword, using OnBase:StatusMap.
    // The event name is checked first because Adobe reports some different actions with the same status
    // (a sender recall and a signer decline are both CANCELLED). Then the agreement status is checked.
    // If Adobe omits the status, the event name is used; anything not in the map is written unchanged.
    private string MapStatus(string eventName, string? agreementStatus)
    {
        var status = string.IsNullOrWhiteSpace(agreementStatus) ? eventName : agreementStatus;

        if (statusMap.TryGetValue(eventName, out var mappedFromEvent))
        {
            return mappedFromEvent;
        }

        return statusMap.TryGetValue(status, out var mappedFromStatus) ? mappedFromStatus : status;
    }

    // Walks a nested path, e.g. GetString(root, "agreement", "id") reads root.agreement.id.
    // Returns null when any part of the path is missing or the final value is not a string.
    private static string? GetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}
