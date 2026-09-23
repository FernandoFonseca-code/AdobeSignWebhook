# Adobe Acrobat Sign → OnBase Status Webhook

Keeps an OnBase keyword in step with the status of Adobe Acrobat Sign agreements.

When an agreement is sent, signed, declined, recalled or expires, Adobe Sign calls this webhook. The webhook finds every OnBase document whose **AdobeID** keyword holds that agreement's ID and writes the agreement status (for example `Awaiting Signature`, `Completed`, `Cancelled`) to a **status keyword**. OnBase Workflow can then act on the keyword change, for example to route, notify or import the signed copy.

```
Adobe Acrobat Sign ──HTTPS POST──▶ IIS ▶ AdobeSign.OnBase.Webhook.Api   (.NET 8)
                                          │  one process per event
                                          ▼
                              updater\AdobeSign.OnBase.Updater.exe      (.NET Framework 4.8)
                                          │  Hyland Unity API
                                          ▼
                                 OnBase Application Server
```

| Project | Target | Purpose |
|---------|--------|---------|
| `AdobeSign.OnBase.Webhook.Api` | .NET 8, ASP.NET Core in IIS | Answers Adobe Sign's registration check, accepts agreement events, maps the status and launches the updater |
| `AdobeSign.OnBase.Updater` | .NET Framework 4.8 console app | Connects with the Unity API, finds the documents by AdobeID and sets the status keyword |

The OnBase code runs in a separate .NET Framework program because `Hyland.Unity.dll` cannot load in a .NET 8 process.

## Contents

- [Before you start](#before-you-start)
- [1. Prepare OnBase](#1-prepare-onbase)
- [2. Add the Hyland assemblies](#2-add-the-hyland-assemblies)
- [3. Build and publish](#3-build-and-publish)
- [4. Create the IIS application](#4-create-the-iis-application)
- [5. Configure](#5-configure)
- [6. Test](#6-test)
- [7. Register the webhook in Adobe Sign](#7-register-the-webhook-in-adobe-sign)
- [Configuration reference](#configuration-reference)
- [Security](#security)
- [Troubleshooting](#troubleshooting)
- [Limitations](#limitations)
- [License](#license)

## Before you start

**Build machine**

- .NET 8 SDK
- .NET Framework 4.8 Developer Pack (targeting pack)
- `Hyland.Unity.dll` and `Hyland.Types.dll` from your OnBase installation (see step 2)

**Web server (Windows Server with IIS)**

- IIS with the **ASP.NET Core Module V2**, installed by the [.NET 8 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0)
- .NET Framework 4.8
- Network access from the web server to your OnBase Application Server
- A public HTTPS address with a certificate from a public certificate authority. Adobe Sign must be able to reach it from the internet, and it does not accept self-signed certificates or follow redirects.

**OnBase**

- The Unity API licensed for your environment
- A service account, described in step 1

**Adobe Acrobat Sign**

- An account or group administrator who can create webhooks

## 1. Prepare OnBase

1. **AdobeID keyword.** Create a Keyword Type that stores the Adobe Sign agreement ID (default name `AdobeID`). Whatever sends documents to Adobe Sign must fill it in. For example, that could be an integration, a Workflow action or a Unity script that stores the ID returned when the agreement is created. This webhook only reads the keyword.
2. **Status keyword.** Create an alphanumeric Keyword Type for the status (default name `AdobeSign Status`) and assign it to the Document Types you want updated. If you limit the keyword to a set of allowed values, include every value from your [status map](#status-map).
3. **Service account.** Create an OnBase user that can log in through the Application Server. Give it rights to retrieve documents of those Document Types and to modify keywords on them, and nothing more.
4. Write down the Application Server URL (typically `http://<server>/AppServer64/Service.asmx`) and the data source name.

## 2. Add the Hyland assemblies

Hyland licenses its assemblies, so this repository doesn't include them. Copy the files below from your OnBase installation into `AdobeSign.OnBase.Updater\lib\OnBase\`:

- `Hyland.Unity.dll`
- `Hyland.Types.dll`

Use the version that matches your Application Server. The project was built against Unity API 25.1. If the files are missing, the build stops with a message saying so. See [lib/OnBase/README.md](AdobeSign.OnBase.Updater/lib/OnBase/README.md) for where to find them.

## 3. Build and publish

```powershell
git clone <this repository> C:\Source\AdobeSignWebhook
cd C:\Source\AdobeSignWebhook
dotnet publish AdobeSign.OnBase.Webhook.Api -c Release -o C:\inetpub\AdobeSignWebhook
```

The publish also builds the updater into `C:\inetpub\AdobeSignWebhook\updater\`.

> **Keep the source and the site outside `C:\inetpub\wwwroot`.** Anything under the Default Web Site root can be downloaded over HTTP, including `appsettings.json` and the Hyland DLLs.

## 4. Create the IIS application

These commands create the webhook as an application named `adobesign` under the Default Web Site. Change the names to suit your server.

```powershell
Import-Module WebAdministration
New-WebAppPool -Name AdobeSignPool
Set-ItemProperty IIS:\AppPools\AdobeSignPool managedRuntimeVersion ""   # "No Managed Code", required for ASP.NET Core
New-WebApplication -Site "Default Web Site" -Name adobesign -PhysicalPath C:\inetpub\AdobeSignWebhook -ApplicationPool AdobeSignPool
icacls C:\inetpub\AdobeSignWebhook /grant "IIS AppPool\AdobeSignPool:(OI)(CI)RX"
```

The webhook URL will be `https://<your public host>/adobesign/webhooks/adobesign`. Make sure the site has an HTTPS binding with your public certificate.

## 5. Configure

Configuration comes from three places, and later ones override earlier ones:

1. `appsettings.json`: shared defaults and the status map
2. `appsettings.Production.json`: your site's non-secret settings. It isn't in the repository, so create it next to `appsettings.json`.
3. Environment variables on the IIS app pool: secrets. Use `__` for each level, for example `OnBase__Password`.

**Non-secret settings.** Create `C:\inetpub\AdobeSignWebhook\appsettings.Production.json`:

```json
{
  "OnBase": {
    "DocumentTypes": [
      "Contracts - Signed Agreement",
      "Contracts - Supporting Document"
    ],
    "AdobeIdKeyword": "AdobeID",
    "StatusKeyword": "AdobeSign Status"
  }
}
```

Tip: keep a copy of this file in the project folder (`AdobeSign.OnBase.Webhook.Api\appsettings.Production.json`). `.gitignore` keeps it out of Git, and `dotnet publish` copies it to the site every time.

**Secrets.** Set the secrets as app pool environment variables so no password sits in a file:

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
$vars = [ordered]@{
  "OnBase__ApplicationServerUrl" = "http://<onbase-server>/AppServer64/Service.asmx"
  "OnBase__DataSource"           = "<data source>"
  "OnBase__Username"             = "<service account>"
  "OnBase__Password"             = "<password>"
  "AdobeSign__ClientId"          = "<Adobe Sign API application Client ID>"
}
foreach ($k in $vars.Keys) {
  & $appcmd set config -section:system.applicationHost/applicationPools "/+[name='AdobeSignPool'].environmentVariables.[name='$k',value='$($vars[$k])']" /commit:apphost
}
Restart-WebAppPool AdobeSignPool
```

To change a value later, remove the variable first (`/-[name='AdobeSignPool'].environmentVariables.[name='OnBase__Password']`), then add it again.

### Status map   **OPTIONAL**

`OnBase:StatusMap` in `appsettings.json` decides which value is written to the status keyword:

- Each key is an Adobe Sign **event name** (for example `AGREEMENT_RECALLED`) or **agreement status** (for example `SIGNED`). Keys are not case-sensitive.
- The event name is checked first. Adobe reports several different actions with the same status: a sender recall and a signer decline both arrive as `CANCELLED`, and only the event name tells them apart.
- If Adobe sends no status, the event name is used.
- Any value that isn't in the map is written to OnBase unchanged.

| Adobe Sign | Written to OnBase (default) |
|---|---|
| event `AGREEMENT_RECALLED` | Manually Cancelled |
| event `AGREEMENT_REJECTED` | User Abandoned |
| `SIGNED`, `APPROVED`, `ACCEPTED`, `DELIVERED`, `FORM_FILLED` | Completed |
| `OUT_FOR_SIGNATURE`, `OUT_FOR_APPROVAL`, `OUT_FOR_ACCEPTANCE`, `OUT_FOR_DELIVERY`, `OUT_FOR_FORM_FILLING`, `WAITING_FOR_VERIFICATION`, `WAITING_FOR_NOTARIZATION` | Awaiting Signature |
| `CANCELLED` | Cancelled |
| `EXPIRED` | Expired |
| `DRAFT`, `AUTHORING`, `PREFILL`, `DOCUMENTS_NOT_YET_PROCESSED` | Draft |

To use your own wording, override entries in `appsettings.Production.json`, for example `"StatusMap": { "SIGNED": "Signed" }`. Entries you don't override keep their defaults.

## 6. Test

**Updater only.** This tests the OnBase connection and permissions without Adobe Sign. Try it against a test OnBase system first.

```powershell
$env:ONBASE_APPSERVER_URL   = "http://<onbase-server>/AppServer64/Service.asmx"
$env:ONBASE_DATASOURCE      = "<data source>"
$env:ONBASE_USERNAME        = "<service account>"
$env:ONBASE_PASSWORD        = "<password>"
$env:ONBASE_DOCUMENT_TYPES  = "Contracts - Signed Agreement|Contracts - Supporting Document"   # separated by |
$env:ONBASE_ADOBEID_KEYWORD = "AdobeID"
$env:ONBASE_STATUS_KEYWORD  = "AdobeSign Status"
C:\inetpub\AdobeSignWebhook\updater\AdobeSign.OnBase.Updater.exe "<an AdobeID on a test document>" "Completed"
$LASTEXITCODE   # 0 = updated, 2 = no documents with that AdobeID, 1 = error (message printed)
```

The updater receives the final OnBase value, so the status map isn't applied here.

**Through the webhook:**

```powershell
$url = "https://<your public host>/adobesign/webhooks/adobesign"
$h   = @{ "X-AdobeSign-ClientId" = "<client id>" }

# Registration check: expect 200 and the client ID echoed back
Invoke-WebRequest $url -Headers $h -UseBasicParsing

# Agreement event: expect 200 and the keyword set to "Completed"
Invoke-WebRequest $url -Method POST -Headers $h -ContentType application/json -UseBasicParsing `
  -Body '{"event":"AGREEMENT_ACTION_COMPLETED","agreement":{"id":"<AdobeID>","status":"SIGNED"}}'
```

For local development, run the API project (`dotnet run --project AdobeSign.OnBase.Webhook.Api`) and use the requests in `AdobeSign.OnBase.Webhook.Api.http`.

## 7. Register the webhook in Adobe Sign

1. In Adobe Acrobat Sign, open **Account > Webhooks** (or the group settings for a group-level webhook) and create a webhook.
2. **URL:** `https://<your public host>/adobesign/webhooks/adobesign`
3. **Scope:** Account or Group
4. **Events:** the agreement events you want reflected in OnBase, for example:
   - `AGREEMENT_CREATED`
   - `AGREEMENT_ACTION_COMPLETED`
   - `AGREEMENT_WORKFLOW_COMPLETED`
   - `AGREEMENT_REJECTED`
   - `AGREEMENT_RECALLED`
   - `AGREEMENT_EXPIRED`
5. **Notification parameters:** turn on **Agreement Info** so the payload includes `agreement.status`.
6. Save. Adobe Sign immediately sends a `GET` request carrying its client ID. The webhook echoes the ID back, and Adobe then activates the webhook.

You can also create the webhook with the Adobe Sign REST API (`POST /api/rest/v6/webhooks`).

Set `AdobeSign__ClientId` (step 5) to the client ID Adobe Sign sends in the `X-AdobeSign-ClientId` header. Requests with any other client ID are rejected with `401`.

## Configuration reference

| Setting | Default | Meaning |
|---|---|---|
| `OnBase:ApplicationServerUrl` | *(required)* | Unity Application Server URL |
| `OnBase:DataSource` | *(required)* | OnBase data source |
| `OnBase:Username` | *(required)* | Service account |
| `OnBase:Password` | *(required)* | Service account password. Set it as an environment variable. |
| `OnBase:DocumentTypes` | *(required)* | Document Types searched for the AdobeID. With env vars use `OnBase__DocumentTypes__0`, `__1`, and so on. |
| `OnBase:AdobeIdKeyword` | `AdobeID` | Keyword Type holding the agreement ID |
| `OnBase:StatusKeyword` | `AdobeSign Status` | Keyword Type that receives the status |
| `OnBase:StatusMap` | see [Status map](#status-map) | Adobe Sign value → OnBase value |
| `OnBase:UpdaterTimeoutSeconds` | `60` | How long one update may take before it is stopped and the webhook returns 500 |
| `OnBase:UpdaterPath` | `updater\AdobeSign.OnBase.Updater.exe` next to the site | Only needed if you install the updater somewhere else |
| `AdobeSign:ClientId` | *(empty)* | If set, requests must carry this `X-AdobeSign-ClientId`. Strongly recommended. |

**Webhook responses.** Adobe Sign retries any delivery that doesn't get a 2xx response.

| Response | Meaning |
|---|---|
| `200` | Processed. This includes "no OnBase document has this AdobeID", which is logged as a warning because a retry wouldn't help. |
| `400` | The payload isn't an agreement event (no `event` or `agreement.id`) |
| `401` | `X-AdobeSign-ClientId` doesn't match `AdobeSign:ClientId` |
| `500` | OnBase couldn't be updated: missing settings, connection or login failure, unknown Document Type or Keyword Type, or a timeout. Adobe Sign retries. |

## Security

- **Adobe Sign doesn't sign webhook payloads.** Use all three of these checks:
  - Set `AdobeSign:ClientId`.
  - Serve the webhook over HTTPS only.
  - Allow only Adobe Sign's published outbound IP ranges for your region, using IIS *IP Address and Domain Restrictions* on the application with "Deny" for unspecified clients.
- **Keep secrets in app pool environment variables**, not in `appsettings*.json`. The updater also receives the password through an environment variable, never on its command line.
- **Payload values can't inject commands.** They are passed to the updater as separate arguments with no shell involved.
- **Requests are capped at 1 MB.**
- **Use a least-privilege OnBase account**, limited to modifying keywords on the configured Document Types.
- **Keep the site and source outside the web root**, so configuration files and DLLs can't be downloaded.
- **Watch the IIS logs** (`C:\inetpub\logs\LogFiles`) for repeated 401, 400 or 500 responses.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Build error "Hyland.Unity.dll and Hyland.Types.dll are missing" | Step 2 not done |
| HTTP 500.19 / 500.30 when the site starts | Hosting Bundle not installed, or app pool not set to "No Managed Code" |
| 500, log says "OnBase settings are missing" | An environment variable or `DocumentTypes` isn't set |
| 500, log says "OnBase updater not found" | The `updater` folder wasn't published. Publish the API project (not just build it). |
| 500, "Unable to connect to the remote server" | Wrong `ApplicationServerUrl`, or a firewall between IIS and OnBase |
| 500, "Document Type not found" / "Keyword Type not found" | The name in configuration doesn't exactly match OnBase |
| 500, "did not finish within 60 seconds" | Slow Application Server. Raise `UpdaterTimeoutSeconds`. |
| 200 but nothing changes, log says "No OnBase documents found" | The AdobeID keyword isn't populated, or the document is a type that isn't in `DocumentTypes` |
| 401 | `X-AdobeSign-ClientId` doesn't match `AdobeSign:ClientId` |
| Adobe Sign won't save the webhook | URL not publicly reachable, certificate not publicly trusted, the URL redirects, or client ID mismatch |

To see why the site won't start, set `stdoutLogEnabled="true"` in the published `web.config` for a short time and check the `logs` folder.

## Limitations

- **Each event is a separate OnBase login.** The webhook starts the updater and connects to the Application Server once per event. That's fine for typical agreement volumes. At high volume, consider queuing events and processing them in the background.
- **Events can arrive out of order.** Adobe Sign can retry or deliver late, so an older event can overwrite a newer status. OnBase only skips the write when the value is unchanged.
- **Each AdobeID updates at most 100 documents.** This is set by `MaxDocuments` in the updater's `Program.cs`.

## License

Released under the [MIT License](LICENSE). Hyland, OnBase and Unity are trademarks of Hyland Software, and Adobe Acrobat Sign is a trademark of Adobe. This project isn't affiliated with or endorsed by either company.
