# KBO Mutations Metrics Simulator

This tool simulates Lambda invocations and pushes metrics to Grafana using OpenTelemetry with Delta temporality.

## Configuration

### appsettings.kratos.json

You need to fill in your Grafana Cloud credentials:

1. Log into your Grafana Cloud instance
2. Go to Settings > API Keys or get your OTLP credentials
3. Update `appsettings.kratos.json`:

```json
{
  "ENVIRONMENT": "dev",
  "OTLP_METRICS_URI": "https://otlp-gateway-prod-eu-west-0.grafana.net/otlp/v1/metrics",
  "OTLP_TRACES_URI": "https://otlp-gateway-prod-eu-west-0.grafana.net/otlp/v1/traces",
  "OTLP_LOGS_URI": "https://otlp-gateway-prod-eu-west-0.grafana.net/otlp/v1/logs",
  "OTLP_ORG_ID": "<instance_id>:<grafana_cloud_token>"
}
```

**Replace `<instance_id>:<grafana_cloud_token>`** with your actual credentials.

Example:
```json
"OTLP_ORG_ID": "123456:glc_eyJrIjoiYWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXoiLCJuIjoibXktdG9rZW4iLCJpZCI6MTIzNDU2fQ=="
```

## Running the Simulator

```bash
cd tools/AssociationRegistry.KboMutations.MetricsSimulator
dotnet run
```

The simulator will:
1. Ask which Lambda to simulate (mutation file, mutation, or sync)
2. Ask how many invocations to simulate
3. Simulate realistic Lambda invocations with:
   - Cold start on first invocation
   - Warm starts on subsequent invocations
   - Delta temporality metrics
   - Proper flush behavior

## What it Tests

- Delta temporality configuration
- Lambda invocation metrics with cold_start tags
- Proper metric flushing before disposal
- Realistic timing between invocations

## Expected Metrics

Three metrics are emitted for Lambda invocations:

### 1. Counter: `kbo_mutations_lambda_invocations_total`
- **Purpose**: Calculate invocation rate over time
- **Type**: Counter (delta temporality)
- **Tags**: `lambda_name`, `cold_start`, `environment`

### 2. Histogram: `kbo_mutations_lambda_invocation_events`
- **Purpose**: Count total invocations and visualize when they occurred
- **Type**: Histogram
- **Tags**: `lambda_name`, `cold_start`, `environment`
- **Use `_count`** suffix to get total invocation count

### 3. Gauge: `kbo_mutations_lambda_last_invocation_timestamp`
- **Purpose**: Show when each Lambda last ran
- **Type**: Gauge (Observable)
- **Value**: Unix timestamp (seconds)
- **Tags**: `lambda_name`, `cold_start`, `environment`

## Grafana Queries

### Invocation Rate (events/second)
```promql
sum(rate(kbo_mutations_lambda_invocations_total{environment="dev"}[$__rate_interval])) by (lambda_name, cold_start)
```

### Total Invocation Count
```promql
sum(kbo_mutations_lambda_invocation_events_count{environment="dev"}) by (lambda_name)
```

### Last Seen (time since last invocation)
```promql
time() - kbo_mutations_lambda_last_invocation_timestamp{environment="dev"}
```
Use this with "seconds" unit to see how long ago each Lambda last ran.

### Invocation Timeline
Use `kbo_mutations_lambda_invocation_events` as a heatmap or graph to visualize when invocations occurred.
