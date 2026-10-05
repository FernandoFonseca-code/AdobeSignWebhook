using AdobeSign.OnBase.Webhook.Api.AdobeSign;
using AdobeSign.OnBase.Webhook.Api.Audit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AdobeSign.OnBase.Webhook.Api.Controllers;

// The URL registered in Adobe Sign: https://<your server>/<app path>/webhooks/adobesign
[ApiController]
[Route("webhooks/adobesign")]
public sealed class AdobeSignWebhookController(
    AdobeAgreementEventProcessor processor,
    WebhookAuditLog auditLog,
    IOptions<AdobeSignOptions> options,
    ILogger<AdobeSignWebhookController> logger) : ControllerBase
{
    private const string ClientIdHeader = "X-AdobeSign-ClientId";

    // Adobe Sign calls this once when the webhook is registered. It must answer 2xx and echo the client ID.
    [HttpGet]
    public IActionResult Verify()
    {
        if (!TryAcceptClientId(out var clientId))
        {
            return Unauthorized();
        }

        return Ok(new { xAdobeSignClientId = clientId });
    }

    // Receives an agreement event. Any non-2xx response makes Adobe Sign retry the delivery later.
    // 200 = processed, 400 = payload is not an agreement event, 401 = wrong client ID, 500 = OnBase update failed.
    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(1_000_000)]
    // Every call, including rejected ones, is written to the audit log.
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var audit = new WebhookAuditEntry { RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() };
        try
        {
            // The payload of a rejected request isn't read, so unknown callers can't fill the audit log.
            if (!TryAcceptClientId(out _))
            {
                audit.OnBaseResult = $"Not sent to OnBase: request rejected, {ClientIdHeader} did not match.";
                audit.ResponseCode = StatusCodes.Status401Unauthorized;
                return Unauthorized();
            }

            using var reader = new StreamReader(Request.Body);
            audit.Payload = await reader.ReadToEndAsync(cancellationToken);

            await processor.ProcessAsync(audit.Payload, audit, cancellationToken);
            audit.ResponseCode = StatusCodes.Status200OK;
            return Ok();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Invalid Adobe Sign webhook payload");
            audit.OnBaseResult = $"Not sent to OnBase: {exception.Message}";
            audit.ResponseCode = StatusCodes.Status400BadRequest;
            return BadRequest();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Adobe Sign webhook processing failed");
            audit.OnBaseResult = $"FAILED: {exception.Message}";
            audit.ResponseCode = StatusCodes.Status500InternalServerError;
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
        finally
        {
            await auditLog.WriteAsync(audit);
        }
    }

    // Checks the X-AdobeSign-ClientId header and, if accepted, echoes it back as Adobe Sign requires.
    // When AdobeSign:ClientId is not configured every caller is accepted.
    // Adobe Sign does not sign payloads, so this (plus HTTPS and an IP allow-list) is the available check.
    private bool TryAcceptClientId(out string clientId)
    {
        clientId = Request.Headers[ClientIdHeader].ToString();
        var expected = options.Value.ClientId;

        if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(clientId, expected, StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected request with unexpected {Header} from {RemoteIp}", ClientIdHeader, HttpContext.Connection.RemoteIpAddress);
            return false;
        }

        Response.Headers[ClientIdHeader] = clientId;
        return true;
    }
}
