namespace AdobeSign.OnBase.Webhook.Api.AdobeSign;

// Settings from the "AdobeSign" configuration section (appsettings.json or AdobeSign__* environment variables).
public sealed class AdobeSignOptions
{
    public const string SectionName = "AdobeSign";

    // Client ID of the Adobe Sign API application. When set, requests must carry it in X-AdobeSign-ClientId.
    public string ClientId { get; set; } = string.Empty;
}
