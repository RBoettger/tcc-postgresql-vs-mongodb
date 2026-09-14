using System.Text.Json;
using System.Text.Json.Serialization;

namespace Benchmark.Runner;

public class BancoConfig
{
    public string BaseUrl { get; set; } = "";
    public string DockerService { get; set; } = "";
}

/// <summary>
/// Configuração completa de uma execução do experimento. Ver
/// benchmark.config.json (execução completa) e benchmark.quick.config.json
/// (perfil reduzido para validar o pipeline de ponta a ponta).
/// </summary>
public class BenchmarkConfig
{
    public int Seed { get; set; } = 20260829;

    public Dictionary<string, BancoConfig> Bancos { get; set; } = new()
    {
        ["PostgreSQL"] = new BancoConfig { BaseUrl = "http://localhost:5001", DockerService = "postgres" },
        ["MongoDB"] = new BancoConfig { BaseUrl = "http://localhost:5002", DockerService = "mongo" }
    };

    public List<int> Volumes { get; set; } = new() { 10_000, 100_000, 500_000, 1_000_000 };

    public List<string> Complexidades { get; set; } = new() { "Baixa", "Media", "Alta" };

    public List<string> Operacoes { get; set; } = new()
    {
        "Insercao", "InsercaoLote", "ConsultaPorId", "ConsultaPropriedade",
        "ConsultaAninhada", "Atualizacao", "Exclusao", "CargaConcorrente"
    };

    public int Repeticoes { get; set; } = 10;
    public int WarmupIteracoes { get; set; } = 2;
    public int Vus { get; set; } = 10;
    public int DuracaoSegundosPorExecucao { get; set; } = 30;

    /// <summary>Iterações por VU usadas apenas na operação Exclusao (ver delete.js).</summary>
    public int ExclusaoIteracoesPorVu { get; set; } = 50;

    public int BatchSize { get; set; } = 50;
    public int SampleSize { get; set; } = 500;
    public int PayloadPoolSize { get; set; } = 5000;
    public double MargemResultadoSemelhantePercentual { get; set; } = 5;
    public int StatsPollingMs { get; set; } = 500;

    public string ResultsDir { get; set; } = "results";
    public string PerformanceTestsDir { get; set; } = "performance-tests";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static BenchmarkConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BenchmarkConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Não foi possível ler a configuração em '{path}'.");
    }
}
