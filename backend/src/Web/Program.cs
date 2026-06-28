using JobOfferMatcher.Application;
using JobOfferMatcher.Infrastructure;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Web.Endpoints;
using JobOfferMatcher.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Structured logging only (no Console.WriteLine — constitution Forbidden list).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Application use cases + Infrastructure (EF Core, sources, scheduling).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

// Apply append-only migrations + seed config at startup (research §5 / Principle IX).
// Skippable for tests that manage their own schema lifecycle.
if (!app.Configuration.GetValue("SkipDatabaseInitialization", false))
{
    await DatabaseInitializer.InitializeAsync(app.Services);
}

// SPA hosting (classic pipeline — MapStaticAssets does not compose with MapFallbackToFile,
// research §5). In Development, SpaProxy serves the Vite dev server instead.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapApiEndpoints();

app.MapFallbackToFile("index.html");

await app.RunAsync();

// Exposed so the integration-test WebApplicationFactory can bootstrap the real host.
public partial class Program;
