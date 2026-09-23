# Adobe Sign OnBase Webhook

- `AdobeSign.OnBase.Webhook.Api` targets .NET 8 and is hosted in IIS. It receives Adobe Sign events at `POST /webhooks/adobesign` and answers the verification `GET`.
- `AdobeSign.OnBase.Updater` targets .NET Framework 4.8 because `Hyland.Unity.dll` does not run on .NET 8. The webhook launches it once per event.
- The updater sets an OnBase status keyword on documents matched by an AdobeID keyword, for the Document Types listed in `OnBase:DocumentTypes`.
- Status values written to OnBase come from `OnBase:StatusMap` in configuration, not from code.
- Keep OnBase code in the updater simple and readable, in the style of the OnBase console utilities (connect with `Application.Connect`, query, loop, `try/catch` per document).
- Keep OnBase Studio Workflow responsible for downstream actions.
- Do not commit Hyland assemblies, Adobe Sign credentials, OnBase credentials, site-specific Document Types or environment-specific endpoints.
