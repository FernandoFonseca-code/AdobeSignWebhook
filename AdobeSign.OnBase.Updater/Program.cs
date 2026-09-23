using System;
using System.Linq;
using Hyland.Unity;

namespace AdobeSign.OnBase.Updater
{
	// Updates the status keyword on every OnBase document whose AdobeID keyword matches.
	// Usage:  AdobeSign.OnBase.Updater.exe <AdobeID> <Status>
	// Settings come from environment variables (set by the webhook) so the password never appears on a command line.
	// Exit codes (keep in sync with OnBaseUpdaterProcess.cs in the webhook): 0 = updated, 1 = error, 2 = no documents found
	class Program
	{
		const int ExitSuccess = 0;
		const int ExitError = 1;
		const int ExitNoDocumentsFound = 2;

		// Most documents one AdobeID is expected to be attached to; any beyond this are not updated.
		const int MaxDocuments = 100;

		// Entry point: runs the update and turns any unexpected exception into exit code 1.
		static int Main(string[] args)
		{
			try
			{
				return Run(args);
			}
			catch (Exception ex)
			{
				// Connection / login problems end up here
				Console.Error.WriteLine($"OnBase update failed: {ex.Message}");
				return ExitError;
			}
		}

		// Connects to OnBase, finds the documents for the AdobeID and sets their status keyword.
		static int Run(string[] args)
		{
			if (args.Length != 2)
			{
				Console.Error.WriteLine("Usage: AdobeSign.OnBase.Updater.exe <AdobeID> <Status>");
				return ExitError;
			}

			string adobeId = args[0];
			string status = args[1];

			string appServerUrl = Setting("ONBASE_APPSERVER_URL");
			string userName = Setting("ONBASE_USERNAME");
			string password = Setting("ONBASE_PASSWORD");
			string dataSource = Setting("ONBASE_DATASOURCE");
			string[] documentTypeNames = Setting("ONBASE_DOCUMENT_TYPES")
				.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(name => name.Trim())
				.Where(name => name.Length > 0)
				.ToArray();
			string adobeIdKeywordName = Setting("ONBASE_ADOBEID_KEYWORD");
			string statusKeywordName = Setting("ONBASE_STATUS_KEYWORD");

			// Without a Document Type the query would search every Document Type in OnBase.
			if (documentTypeNames.Length == 0)
			{
				Console.Error.WriteLine("ONBASE_DOCUMENT_TYPES is empty; configure at least one Document Type.");
				return ExitError;
			}

			// Connect via Application Server using explicit Service Account credentials
			OnBaseAuthenticationProperties authProps =
				Application.CreateOnBaseAuthenticationProperties(appServerUrl, userName, password, dataSource);

			using (Application app = Application.Connect(authProps))
			{
				KeywordType statusKeywordType = app.Core.KeywordTypes.Find(statusKeywordName);
				if (statusKeywordType == null)
				{
					Console.Error.WriteLine($"Keyword Type not found: {statusKeywordName}");
					return ExitError;
				}

				DocumentQuery query = app.Core.CreateDocumentQuery();

				foreach (string documentTypeName in documentTypeNames)
				{
					DocumentType documentType = app.Core.DocumentTypes.Find(documentTypeName);
					if (documentType == null)
					{
						Console.Error.WriteLine($"Document Type not found: {documentTypeName}");
						return ExitError;
					}

					query.AddDocumentType(documentType);
				}

				query.AddKeyword(adobeIdKeywordName, adobeId);

				// Run the query once. Unlike a purge, updated documents still match, so re-querying in a loop would never end.
				DocumentList docList = query.Execute(MaxDocuments);

				if (docList.Count == 0)
				{
					Console.WriteLine($"No documents found for AdobeID {adobeId}.");
					return ExitNoDocumentsFound;
				}

				int failures = 0;

				foreach (Document doc in docList)
				{
					try
					{
						UpdateStatusKeyword(doc, statusKeywordType, status);
					}
					catch (Exception ex)
					{
						failures++;
						app.Diagnostics.WriteIf(Diagnostics.DiagnosticsLevel.Error, $"Error updating DocID {doc.ID}: {ex.Message}");
						Console.Error.WriteLine($"Error updating DocID {doc.ID}: {ex.Message}");
					}
				}

				return failures == 0 ? ExitSuccess : ExitError;
			}
		}

		// Sets the status keyword on one document: adds it when blank, replaces it when different,
		// and leaves the document alone when it already has this status (e.g. Adobe re-delivered the same webhook).
		static void UpdateStatusKeyword(Document doc, KeywordType statusKeywordType, string status)
		{
			KeywordRecord keywordRecord = doc.KeywordRecords.Find(statusKeywordType);
			Keyword oldKeyword = keywordRecord?.Keywords.Find(statusKeywordType);
			bool hasValue = oldKeyword != null && !oldKeyword.IsBlank;

			if (hasValue && oldKeyword.Value.ToString() == status)
			{
				Console.WriteLine($"DocID {doc.ID} already has status {status}.");
				return;
			}

			Keyword newKeyword = statusKeywordType.CreateKeyword(status);
			KeywordModifier keyModifier = doc.CreateKeywordModifier();

			if (hasValue)
			{
				keyModifier.UpdateKeyword(oldKeyword, newKeyword);
			}
			else
			{
				keyModifier.AddKeyword(newKeyword);
			}

			keyModifier.ApplyChanges();
			Console.WriteLine($"Updated DocID {doc.ID} to status {status}.");
		}

		// Reads a setting passed by the webhook; missing values become empty strings.
		static string Setting(string name)
		{
			return Environment.GetEnvironmentVariable(name) ?? string.Empty;
		}
	}
}
