using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Triker.Web;
using Triker.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7077";
if (!apiBase.EndsWith('/')) apiBase += '/';

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
jsonOptions.Converters.Add(new JsonStringEnumConverter());
builder.Services.AddSingleton(jsonOptions);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase) });
builder.Services.AddScoped<ApiService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<HubService>();
builder.Services.AddScoped<AudioService>();
builder.Services.AddScoped<ShareService>();
builder.Services.AddScoped<AccessibilityService>();

await builder.Build().RunAsync();
