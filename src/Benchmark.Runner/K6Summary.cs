using System.Text.Json;

namespace Benchmark.Runner;

/// <summary>
/// Subconjunto do JSON exportado por `k6 run --summary-export=...` que
/// interessa ao experimento. Parsing tolerante (todos os campos têm default
/// 0) porque nem toda métrica aparece em toda execução (ex.: "checks" só
/// existe se o script chamar check()).
/// </summary>
public record K6Summary(
    int NumeroRequisicoes,
    double RequestsPerSecond,
    double LatenciaMediaMs,
    double MedianaMs,
    double P95Ms,
    double P99Ms,
    double TaxaErroPercentual)
{
    public static K6Summary Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var metrics = doc.RootElement.GetProperty("metrics");

        var httpReqs = GetMetric(metrics, "http_reqs");
        var httpDur = GetMetric(metrics, "http_req_duration");
        var checks = GetMetric(metrics, "checks");

        var numeroRequisicoes = (int)GetDouble(httpReqs, "count");
        var rps = GetDouble(httpReqs, "rate");

        var avg = GetDouble(httpDur, "avg");
        var med = GetDouble(httpDur, "med");
        var p95 = GetDouble(httpDur, "p(95)");
        var p99 = GetDouble(httpDur, "p(99)");

        double taxaErro;
        if (checks.HasValue && checks.Value.TryGetProperty("value", out var checksValue))
        {
            taxaErro = (1.0 - checksValue.GetDouble()) * 100.0;
        }
        else
        {
            var failed = GetMetric(metrics, "http_req_failed");
            taxaErro = GetDouble(failed, "value") * 100.0;
        }

        return new K6Summary(numeroRequisicoes, rps, avg, med, p95, p99, Math.Max(0, taxaErro));
    }

    private static JsonElement? GetMetric(JsonElement metrics, string nome)
        => metrics.TryGetProperty(nome, out var el) ? el : null;

    private static double GetDouble(JsonElement? metric, string campo)
    {
        if (metric is null) return 0;
        var el = metric.Value;
        if (el.TryGetProperty(campo, out var v))
        {
            return v.ValueKind switch
            {
                JsonValueKind.Number => v.GetDouble(),
                _ => 0
            };
        }
        return 0;
    }
}
