using Amazon.Lambda.Core;
using AssociationRegistry.KboMutations.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;

Console.WriteLine("KBO Mutations Metrics Simulator");
Console.WriteLine("================================");
Console.WriteLine();

// Load configuration
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "kratos";
Console.WriteLine($"Environment: {environment}");

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile($"appsettings.{environment}.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// Set ENVIRONMENT variable from config so OpenTelemetry picks it up
var envFromConfig = configuration["ENVIRONMENT"];
if (!string.IsNullOrEmpty(envFromConfig))
{
    Environment.SetEnvironmentVariable("ENVIRONMENT", envFromConfig);
    Console.WriteLine($"Set ENVIRONMENT={envFromConfig}");
}

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<Program>();
var lambdaLogger = new ConsoleLogger();

// Get OTLP configuration
var metricsUri = configuration["OTLP_METRICS_URI"];
var tracesUri = configuration["OTLP_TRACES_URI"];
var logsUri = configuration["OTLP_LOGS_URI"];
var orgId = configuration["OTLP_ORG_ID"];

Console.WriteLine($"Metrics URI: {metricsUri}");
Console.WriteLine($"Org ID: {orgId?.Substring(0, Math.Min(20, orgId.Length))}...");
Console.WriteLine();

// Ask user which Lambda to simulate
Console.WriteLine("Which Lambda would you like to simulate?");
Console.WriteLine("1. KBO Mutation File Lambda (kbo_mutation_file)");
Console.WriteLine("2. KBO Mutation Lambda (kbo_mutation)");
Console.WriteLine("3. KBO Sync Lambda (kbo_sync)");
Console.Write("Enter choice (1-3): ");

var choice = Console.ReadLine();
var lambdaName = choice switch
{
    "1" => "kbo_mutation_file",
    "2" => "kbo_mutation",
    "3" => "kbo_sync",
    _ => "kbo_mutation_file"
};

Console.WriteLine($"Simulating: {lambdaName}");
Console.WriteLine();

// Ask how many invocations
Console.Write("How many invocations to simulate? (default: 10): ");
var countInput = Console.ReadLine();
var invocationCount = int.TryParse(countInput, out var count) ? count : 10;

Console.WriteLine($"Simulating {invocationCount} invocations...");
Console.WriteLine();

// Simulate invocations
for (int i = 1; i <= invocationCount; i++)
{
    Console.WriteLine($"[Invocation {i}/{invocationCount}] Starting Lambda invocation simulation...");

    // Each invocation is a separate process in Lambda
    await SimulateLambdaInvocation(lambdaLogger, metricsUri, tracesUri, logsUri, orgId, lambdaName, i == 1);

    Console.WriteLine($"[Invocation {i}/{invocationCount}] Completed");
    Console.WriteLine();

    // Wait a bit between invocations to make it realistic
    if (i < invocationCount)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

Console.WriteLine("Simulation complete!");
// Small delay to ensure final metrics are exported
await Task.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine("Done!");

static async Task SimulateLambdaInvocation(
    ILambdaLogger logger,
    string? metricsUri,
    string? tracesUri,
    string? logsUri,
    string? orgId,
    string lambdaName,
    bool isColdStart)
{
    try
    {
        Console.WriteLine($"  Setting up telemetry for service: KboMutations.{lambdaName}");

        // Create meter and metrics FIRST, before MeterProvider
        Console.WriteLine($"  Creating Meter with name: {KboMutationsMetrics.MeterName}");
        var meter = new Meter(KboMutationsMetrics.MeterName);
        var metrics = new KboMutationsMetrics(meter);

        // Each Lambda invocation creates a new OpenTelemetry setup
        using var telemetrySetup = new OpenTelemetrySetup(
            logger,
            serviceName: $"KboMutations.{lambdaName}");

        // Setup meters and tracers - MeterProvider will subscribe to existing meters
        Console.WriteLine($"  Setting up MeterProvider (will subscribe to existing meters)");
        telemetrySetup.SetupMeter(
            metricsUri,
            orgId,
            KboMutationsMetrics.MeterName);

        Console.WriteLine($"  MeterProvider created: {telemetrySetup.MeterProvider != null}");

        telemetrySetup.SetUpTracing(
            tracesUri,
            orgId,
            KboMutationsActivitySource.Source.Name);

        // Record Lambda invocation
        Console.WriteLine($"  Recording metrics...");
        metrics.RecordLambdaInvocation(lambdaName, isColdStart);
        Console.WriteLine($"  ✓ Recorded Lambda invocation (cold_start: {isColdStart})");

        // Simulate some processing time
        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 500)));

        // Force flush metrics before disposal (required for manual export strategy)
        // Console.WriteLine("  Force flushing metrics...");
        // // var flushResult = telemetrySetup.MeterProvider?.ForceFlush();
        // Console.WriteLine($"  Metrics flush result: {flushResult}");

        // Small delay to allow HTTP requests to complete
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Console.WriteLine("  Disposing telemetry providers...");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ ERROR: {ex.Message}");
        Console.WriteLine($"  Stack trace: {ex.StackTrace}");
    }
}

// Simple Lambda logger implementation for console
class ConsoleLogger : ILambdaLogger
{
    public void Log(string message) => Console.WriteLine($"[Lambda] {message}");
    public void LogLine(string message) => Console.WriteLine($"[Lambda] {message}");
}
