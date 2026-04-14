using Microsoft.AspNetCore.HttpOverrides;
using StevensSupportHelper.AdminWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ApiClient>();
builder.Services.AddSingleton<DemoClientDataService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

var configuredPathBase = app.Configuration["PathBase"];
if (!string.IsNullOrWhiteSpace(configuredPathBase))
{
    app.UsePathBase(configuredPathBase);
}

app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("X-Forwarded-Prefix", out var forwardedPrefix))
    {
        var pathBase = forwardedPrefix.ToString().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            context.Request.PathBase = pathBase.StartsWith('/') ? pathBase : $"/{pathBase}";
        }
    }

    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
