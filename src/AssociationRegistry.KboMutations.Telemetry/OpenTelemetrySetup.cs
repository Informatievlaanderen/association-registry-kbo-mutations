namespace AssociationRegistry.KboMutations.Telemetry;

using Amazon.Lambda.Core;
using global::OpenTelemetry;
using global::OpenTelemetry.Exporter;
using global::OpenTelemetry.Logs;
using global::OpenTelemetry.Metrics;
using global::OpenTelemetry.Resources;
using global::OpenTelemetry.Trace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;

public class OpenTelemetrySetup : IDisposable
{
    private readonly OpenTelemetryResources _resources;
    private readonly ILambdaLogger _logger;
    public TracerProvider TracerProvider { get; private set; }
    public MeterProvider MeterProvider { get; private set; }

    public OpenTelemetrySetup(
        ILambdaLogger contextLogger,
        string serviceName)
    {
        _logger = contextLogger;
        _logger.LogInformation("OpenTelemetrySetup: Starting setup");

        _resources = GetResources(contextLogger, serviceName);
    }

    public MeterProvider SetupMeter(string? metricsUri, string? orgId, params string[] meterNames)
    {
        var resourceBuilder = ResourceBuilder.CreateEmpty();
        _resources.ConfigureResourceBuilder(resourceBuilder);

        var builder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddRuntimeInstrumentation()
            .AddHttpClientInstrumentation();

        foreach (var meterName in meterNames)
        {
            _logger.LogInformation($"Registering meter: {meterName}");
            builder.AddMeter(meterName);
        }

        if (!string.IsNullOrEmpty(metricsUri))
        {
            _logger.LogInformation($"Adding OTLP metrics exporter: {metricsUri}");

            builder.AddOtlpExporter((exporterOptions, readerOptions) =>
            {
                exporterOptions.Endpoint = new Uri(metricsUri);
                exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
                exporterOptions.Headers = !string.IsNullOrEmpty(orgId)
                    ? $"X-Scope-OrgID={orgId}"
                    : null;

                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 60000;
            });
            builder.AddConsoleExporter();
        }

        MeterProvider = builder.Build();
        return MeterProvider;
    }

    public TracerProvider SetUpTracing(string? tracesUri, string? orgId, params string[] activitySourceNames)
    {
        var builder = Sdk.CreateTracerProviderBuilder()
                         .AddHttpClientInstrumentation()
                         .ConfigureResource(_resources.ConfigureResourceBuilder);

        foreach (var sourceName in activitySourceNames)
        {
            builder.AddSource(sourceName);
        }

        if (!string.IsNullOrEmpty(tracesUri))
        {
            _logger.LogInformation($"Adding OTLP traces exporter: {tracesUri}");
            builder.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(tracesUri);
                options.Protocol = OtlpExportProtocol.HttpProtobuf;

                AddHeaders(options, orgId);

                _logger.LogInformation($"Traces - Endpoint: {options.Endpoint}");
                _logger.LogInformation($"Traces - Protocol: {options.Protocol}");
                _logger.LogInformation($"Traces - Headers: {options.Headers}");
            });
        }
        else
        {
            _logger.LogInformation("No traces URI configured, skipping OTLP traces exporter");
        }

        TracerProvider = builder.Build();

        return TracerProvider;
    }

    public void SetUpLogging(string? logsUri, string? orgId, ILoggingBuilder builder)
    {
        builder.AddOpenTelemetry(options =>
        {
            var resourceBuilder = ResourceBuilder.CreateDefault();
            _resources.ConfigureResourceBuilder(resourceBuilder);
            options.SetResourceBuilder(resourceBuilder);

            if (!string.IsNullOrEmpty(logsUri))
            {
                _logger.LogInformation($"Adding OTLP logs exporter: {logsUri}");
                options.AddOtlpExporter((exporterOptions, processorOptions) =>
                {
                    exporterOptions.Endpoint = new Uri(logsUri);
                    exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
                    exporterOptions.TimeoutMilliseconds = 2000;

                    processorOptions.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 1000;
                    processorOptions.BatchExportProcessorOptions.MaxExportBatchSize = 512;
                    processorOptions.BatchExportProcessorOptions.MaxQueueSize = 2048;
                    processorOptions.BatchExportProcessorOptions.ExporterTimeoutMilliseconds = 2000;

                    AddHeaders(exporterOptions, orgId);

                    _logger.LogInformation($"Logs - Endpoint: {exporterOptions.Endpoint}");
                    _logger.LogInformation($"Logs - Protocol: {exporterOptions.Protocol}");
                    _logger.LogInformation($"Logs - Headers: {exporterOptions.Headers}");
                    _logger.LogInformation($"Logs - Timeout: {exporterOptions.TimeoutMilliseconds}ms");
                    _logger.LogInformation($"Logs - Scheduled Delay: 1000ms");
                });
            }
            else
            {
                _logger.LogInformation("No logs URI configured, skipping OTLP logs exporter");
            }
        });
    }

    private OpenTelemetryResources GetResources(ILambdaLogger contextLogger, string serviceName)
    {
        var assemblyVersion = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "unknown";
        var serviceInstanceId = Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME") ?? Environment.MachineName;
        var environment = Environment.GetEnvironmentVariable("ENVIRONMENT")?.ToLowerInvariant() ?? "unknown";

        Action<ResourceBuilder> configureResource = r =>
        {
            r.AddService(
                    serviceName,
                    serviceVersion: assemblyVersion,
                    serviceInstanceId: serviceInstanceId)
               .AddAttributes(
                    new Dictionary<string, object>
                    {
                        ["deployment.environment"] = environment,
                    });
        };

        contextLogger.LogInformation($"Resource configuration: Service name '{serviceName}', ServiceVersion '{assemblyVersion}', Service Instance Id '{serviceInstanceId}', Env '{environment}'");

        return new OpenTelemetryResources(serviceName, configureResource);
    }

    public void Dispose()
    {
        MeterProvider?.Dispose();
        TracerProvider?.Dispose();
    }

    private static void AddHeaders(OtlpExporterOptions options, string? orgScope)
    {
        var headersList = new List<string>();

        if (!string.IsNullOrEmpty(orgScope))
            headersList.Add($"X-Scope-OrgID={orgScope}");

        if (headersList.Any())
        {
            options.Headers = string.Join(",", headersList);
        }
        else
        {
            options.Headers = null;
        }
    }
}

public record OpenTelemetryResources(string ServiceName, Action<ResourceBuilder> ConfigureResourceBuilder);

public class LoggingHttpMessageHandler : DelegatingHandler
{
    private readonly ILambdaLogger _logger;

    public LoggingHttpMessageHandler(ILambdaLogger logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"======== OTLP EXPORT REQUEST ========");
        Console.WriteLine($"Sending to: {request.RequestUri}");

        var response = await base.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"✓ OTLP Export SUCCESS: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            _logger.LogLine($"OTLP Export succeeded: {response.StatusCode}");
        }
        else
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"======== OTLP EXPORT ERROR ========");
            Console.WriteLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            Console.WriteLine($"Response Body: {responseBody}");
            Console.WriteLine($"Request URL: {request.RequestUri}");
            Console.WriteLine($"Request Headers: {string.Join(", ", request.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}");
            Console.WriteLine($"===================================");

            _logger.LogLine($"OTLP ERROR: {response.StatusCode} - {responseBody}");
        }
        Console.WriteLine($"=====================================");

        return response;
    }
}
