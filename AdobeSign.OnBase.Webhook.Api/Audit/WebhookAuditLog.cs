using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AdobeSign.OnBase.Webhook.Api.OnBase;
using Microsoft.Extensions.Options;

namespace AdobeSign.OnBase.Webhook.Api.Audit;

// Writes one readable block per webhook call to logs\adobesign-audit-yyyy-MM-dd.log:
// the payload Adobe Sign sent and the status that was pushed to OnBase.
// Only the payload is written, never request headers or the OnBase password.
public sealed class WebhookAuditLog(IOptions<OnBaseOptions> options, ILogger<WebhookAuditLog> logger)
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        // Keep characters such as ' and + readable instead of '.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "logs");

    // Adobe Sign can deliver several events at once; the lock keeps their blocks from interleaving.
    private readonly SemaphoreSlim fileLock = new(1, 1);

    // Appends the entry to today's file. A failure is only logged so it never changes the webhook's response.
    public async Task WriteAsync(WebhookAuditEntry entry)
    {
        try
        {
            var text = Format(entry);
            var path = Path.Combine(directory, $"adobesign-audit-{entry.ReceivedAt:yyyy-MM-dd}.log");

            await fileLock.WaitAsync();
            try
            {
                Directory.CreateDirectory(directory);
                await File.AppendAllTextAsync(path, text);
            }
            finally
            {
                fileLock.Release();
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not write the Adobe Sign audit log to {Directory}", directory);
        }
    }

    private string Format(WebhookAuditEntry entry)
    {
        var settings = options.Value;
        var text = new StringBuilder();

        text.AppendLine(new string('=', 80));
        text.AppendLine($"{entry.ReceivedAt:yyyy-MM-dd HH:mm:ss}   from {entry.RemoteIp}   -> HTTP {entry.ResponseCode} {ResponseText(entry.ResponseCode)}");
        text.AppendLine(new string('-', 80));
        text.AppendLine($"Adobe event        : {Show(entry.EventName)}");
        text.AppendLine($"AdobeID            : {Show(entry.AdobeId)}");
        text.AppendLine($"Adobe status       : {Show(entry.AgreementStatus)}");
        text.AppendLine($"Written to OnBase  : {Show(entry.OnBaseStatus)}   (keyword \"{settings.StatusKeyword}\")");
        text.AppendLine($"OnBase server      : {settings.ApplicationServerUrl}   data source \"{settings.DataSource}\"");
        text.AppendLine($"OnBase result      : {Show(entry.OnBaseResult)}");
        text.AppendLine("Payload from Adobe :");
        text.AppendLine(PrettyPrint(entry.Payload));
        text.AppendLine();

        return text.ToString();
    }

    // Indents the JSON so it is easy to read; anything that is not valid JSON is written as received.
    private static string PrettyPrint(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "(none)";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return JsonSerializer.Serialize(document.RootElement, PrettyJson);
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string ResponseText(int code) => code switch
    {
        200 => "OK",
        400 => "Bad Request (not an agreement event)",
        401 => "Unauthorized (wrong client ID)",
        500 => "Error (Adobe Sign will retry)",
        _ => string.Empty,
    };
}
