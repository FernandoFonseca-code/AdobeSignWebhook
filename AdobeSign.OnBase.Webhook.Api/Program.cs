using AdobeSign.OnBase.Webhook.Api.AdobeSign;
using AdobeSign.OnBase.Webhook.Api.Audit;
using AdobeSign.OnBase.Webhook.Api.OnBase;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Bind the "AdobeSign" and "OnBase" sections (appsettings*.json, then environment variables such as OnBase__Password).
builder.Services.Configure<AdobeSignOptions>(builder.Configuration.GetSection(AdobeSignOptions.SectionName));
builder.Services.Configure<OnBaseOptions>(builder.Configuration.GetSection(OnBaseOptions.SectionName));

builder.Services.AddSingleton<IOnBaseStatusUpdater, OnBaseUpdaterProcess>();
builder.Services.AddSingleton<AdobeAgreementEventProcessor>();
builder.Services.AddSingleton<WebhookAuditLog>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();
