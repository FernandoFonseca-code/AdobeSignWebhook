namespace AdobeSign.OnBase.Webhook.Api.OnBase;

// Settings from the "OnBase" configuration section (appsettings.json or OnBase__* environment variables).
public sealed class OnBaseOptions
{
    public const string SectionName = "OnBase";

    // Unity Application Server endpoint, e.g. http://server/AppServer64/Service.asmx
    public string ApplicationServerUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DataSource { get; set; } = string.Empty;

    // Document Types searched for the AdobeID. At least one is required, otherwise OnBase would search every type.
    public string[] DocumentTypes { get; set; } = [];

    // Keyword Type holding the Adobe Sign agreement ID.
    public string AdobeIdKeyword { get; set; } = "AdobeID";

    // Keyword Type that receives the agreement status.
    public string StatusKeyword { get; set; } = "AdobeSign Status";

    // Full path to AdobeSign.OnBase.Updater.exe. When empty, the "updater" folder next to the webhook is used.
    public string? UpdaterPath { get; set; }

    // How long one OnBase update may take before the updater is killed and the webhook returns 500.
    public int UpdaterTimeoutSeconds { get; set; } = 60;

    // Translates Adobe Sign values into the values written to the status keyword. Keys are Adobe Sign event names
    // (e.g. AGREEMENT_RECALLED) or agreement statuses (e.g. SIGNED) and are case-insensitive. Values not listed are
    // written unchanged.
    public Dictionary<string, string> StatusMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Returns the names of required settings that are missing, so a misconfigured server fails with a clear message.
    public IReadOnlyList<string> GetMissingSettings()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApplicationServerUrl)) missing.Add($"{SectionName}:{nameof(ApplicationServerUrl)}");
        if (string.IsNullOrWhiteSpace(Username)) missing.Add($"{SectionName}:{nameof(Username)}");
        if (string.IsNullOrWhiteSpace(Password)) missing.Add($"{SectionName}:{nameof(Password)}");
        if (string.IsNullOrWhiteSpace(DataSource)) missing.Add($"{SectionName}:{nameof(DataSource)}");
        if (!DocumentTypes.Any(name => !string.IsNullOrWhiteSpace(name))) missing.Add($"{SectionName}:{nameof(DocumentTypes)}");
        return missing;
    }
}
