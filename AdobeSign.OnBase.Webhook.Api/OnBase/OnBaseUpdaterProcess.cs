using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace AdobeSign.OnBase.Webhook.Api.OnBase;

// Hyland.Unity only runs on .NET Framework 4.8, so the OnBase calls live in AdobeSign.OnBase.Updater.exe (see that project's Program.cs).
// This class runs it once per event and turns its exit code into success or failure.
public sealed class OnBaseUpdaterProcess(IOptions<OnBaseOptions> options, ILogger<OnBaseUpdaterProcess> logger) : IOnBaseStatusUpdater
{
    // Exit codes returned by AdobeSign.OnBase.Updater.exe; keep in sync with that project's Program.cs.
    private const int ExitSuccess = 0;
    private const int ExitNoDocumentsFound = 2;

    // Starts the updater with the AdobeID and status, waits for it and throws if it fails or times out.
    // "No documents found" is only logged: retrying would not help, so the webhook still returns 200.
    public async Task<string> UpdateAgreementStatusAsync(string adobeId, string status, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var missing = settings.GetMissingSettings();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"OnBase settings are missing: {string.Join(", ", missing)}");
        }

        var updaterPath = string.IsNullOrWhiteSpace(settings.UpdaterPath)
            ? Path.Combine(AppContext.BaseDirectory, "updater", "AdobeSign.OnBase.Updater.exe")
            : settings.UpdaterPath;

        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("OnBase updater not found. Publish the solution so it is copied to the 'updater' folder.", updaterPath);
        }

        using var process = Process.Start(CreateStartInfo(updaterPath, adobeId, status, settings))
            ?? throw new InvalidOperationException("Could not start the OnBase updater.");

        var timeout = TimeSpan.FromSeconds(settings.UpdaterTimeoutSeconds);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        // Read both streams while waiting, otherwise a full output buffer could block the updater.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"OnBase updater did not finish within {timeout.TotalSeconds:0} seconds for AdobeID {adobeId}.");
        }

        var outputText = (await output).Trim();
        var errorText = (await error).Trim();

        switch (process.ExitCode)
        {
            case ExitSuccess:
                logger.LogInformation("OnBase updater finished for AdobeID {AdobeId}: {Output}", adobeId, outputText);
                return outputText;
            case ExitNoDocumentsFound:
                logger.LogWarning("No OnBase documents found for AdobeID {AdobeId}", adobeId);
                return $"No OnBase documents found for AdobeID {adobeId} (nothing updated).";
            default:
                // A non-2xx response makes Adobe Sign retry the webhook.
                throw new InvalidOperationException($"OnBase updater failed (exit code {process.ExitCode}) for AdobeID {adobeId}: {errorText}");
        }
    }

    // Builds the updater command. The AdobeID and status are arguments; the connection settings are environment variables.
    private static ProcessStartInfo CreateStartInfo(string updaterPath, string adobeId, string status, OnBaseOptions settings)
    {
        var startInfo = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // ArgumentList passes each value as-is (no shell), so payload text can't inject extra arguments.
        startInfo.ArgumentList.Add(adobeId);
        startInfo.ArgumentList.Add(status);

        // The password goes through environment variables, not the command line, so other processes can't see it.
        startInfo.Environment["ONBASE_APPSERVER_URL"] = settings.ApplicationServerUrl;
        startInfo.Environment["ONBASE_USERNAME"] = settings.Username;
        startInfo.Environment["ONBASE_PASSWORD"] = settings.Password;
        startInfo.Environment["ONBASE_DATASOURCE"] = settings.DataSource;
        startInfo.Environment["ONBASE_DOCUMENT_TYPES"] = string.Join('|', settings.DocumentTypes);
        startInfo.Environment["ONBASE_ADOBEID_KEYWORD"] = settings.AdobeIdKeyword;
        startInfo.Environment["ONBASE_STATUS_KEYWORD"] = settings.StatusKeyword;

        return startInfo;
    }
}
