var builder = WebApplication.CreateBuilder(args);

// ---- CORS ------------------------------------------------------------------------
// Origins come from configuration so a deployment can add its own without a rebuild.
const string CorsPolicy = "AllowFrontend";

var allowedOrigins = builder.Configuration.GetSection("Frontend:Origins").Get<string[]>()
                     ?? new[] { builder.Configuration["Frontend:Url"] ?? "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Required for the httpOnly refresh cookie to be sent and set cross-origin.
        // AllowCredentials cannot be combined with AllowAnyOrigin, which is why the
        // origins above are explicit.
        .AllowCredentials());
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors(CorsPolicy);

// ---- Block service-to-service endpoints at the ingress ----------------------------
// The identity service exposes /api/auth/internal/* for the timesheet service. Those
// routes leak the whole user directory (names, emails, roles, reporting lines) and must
// never be reachable from a browser. This literal-prefixed route out-ranks the proxy's
// "api/auth/{**catch-all}" pattern, so the request is answered here and never forwarded.
app.Map("/api/auth/internal/{**rest}", () => Results.NotFound());

app.MapHealthChecks("/health");

app.MapReverseProxy();

app.Run();
