using HarnessDemo.BlazorClient.Components;
using HarnessDemo.BlazorClient.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// AG-UI sunucusu (HarnessDemo.AgentServer). Uzun çalışan agent akışları için zaman aşımı kapalı.
builder.Services.AddHttpClient<AgUiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AgentServer:BaseUrl"] ?? "http://localhost:5275/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
