using System.Text.Json;
using Benchmark.Shared;

namespace Benchmark.Runner;

/// <summary>
/// Orquestra o experimento completo: Volume × Complexidade × Banco ×
/// Operação × Repetições, gravando cada execução em
/// results/raw/benchmark-results.csv. Ordem de preparação por cenário:
/// reset + seed UMA vez por (Banco, Volume, Complexidade); as 8 operações
/// então rodam (warm-up + N repetições) sobre essa mesma massa de dados —
/// decisão confirmada com o autor do TCC (repetição mede estabilidade da
/// operação, não da preparação do banco).
/// </summary>
public class ScenarioRunner
{
    private readonly BenchmarkConfig _cfg;
    private readonly string _repoRoot;
    private readonly CsvWriter _csv;

    public ScenarioRunner(BenchmarkConfig cfg, string repoRoot)
    {
        _cfg = cfg;
        _repoRoot = repoRoot;
        _csv = new CsvWriter(Path.Combine(repoRoot, cfg.ResultsDir, "raw", "benchmark-results.csv"));
    }

    public async Task RunAllAsync()
    {
        Console.WriteLine("Exportando payloads determinísticos...");
        await PayloadExporter.ExportAllAsync(_repoRoot, _cfg.PerformanceTestsDir, _cfg.Seed, _cfg.PayloadPoolSize, _cfg.Complexidades);

        foreach (var volume in _cfg.Volumes)
        {
            foreach (var complexidadeStr in _cfg.Complexidades)
            {
                // Randomização controlada e reprodutível da ordem PostgreSQL/MongoDB
                // por combinação Volume×Complexidade (evita viés de "quem roda primeiro").
                // Importante: NÃO usar HashCode.Combine/string.GetHashCode aqui — o
                // .NET randomiza esses hashes por processo (proteção contra hash
                // flooding), então a "mesma" seed produziria ordens diferentes a
                // cada execução. Combinação puramente aritmética garante
                // reprodutibilidade real entre execuções.
                var ordemSeed = unchecked(_cfg.Seed * 31 + volume * 7 + ComplexidadeIndice(complexidadeStr) * 131);
                var ordemRng = new Random(ordemSeed);
                var bancos = _cfg.Bancos.Keys.ToList();
                var ordem = bancos.OrderBy(_ => ordemRng.Next()).ToArray();
                var ordemDescricao = string.Join(">", ordem);

                foreach (var banco in ordem)
                {
                    try
                    {
                        await RunScenarioAsync(banco, volume, complexidadeStr, ordemDescricao);
                    }
                    catch (Exception ex)
                    {
                        // Falha grave de preparação (reset/seed) neste cenário: registra
                        // e segue para o próximo em vez de derrubar um experimento que
                        // pode estar rodando há horas.
                        Console.WriteLine($"[ERRO] Cenário {banco}/{volume}/{complexidadeStr} falhou por completo e foi pulado: {ex.Message}");
                    }
                }
            }
        }
    }

    private async Task RunScenarioAsync(string banco, int volume, string complexidadeStr, string ordemDescricao)
    {
        var bancoCfg = _cfg.Bancos[banco];
        var api = new ApiClient(bancoCfg.BaseUrl);
        var containerId = await DockerCli.ComposeServiceContainerIdAsync(_repoRoot, bancoCfg.DockerService);

        Console.WriteLine($"\n=== {banco} | volume={volume} | complexidade={complexidadeStr} | ordem={ordemDescricao} ===");
        Console.WriteLine($"[{banco}] reset...");
        await api.ResetAsync();

        Console.WriteLine($"[{banco}] seed de {volume} documentos...");
        await api.SeedAsync(volume, complexidadeStr);

        var sample = await api.SampleAsync(_cfg.SampleSize);
        var dataDir = Path.Combine(_repoRoot, _cfg.PerformanceTestsDir, "data");
        Directory.CreateDirectory(dataDir);

        var targetsPath = Path.Combine(dataDir, $"targets-{banco}-{volume}-{complexidadeStr}.json");
        await File.WriteAllTextAsync(targetsPath, JsonSerializer.Serialize(new
        {
            ids = sample.Ids,
            nomes = sample.Nomes,
            cidades = sample.Cidades,
            codigosProduto = sample.CodigosProduto
        }));

        await CaptureExplainAsync(banco, volume, complexidadeStr, api, sample);

        string? targetsDeletePath = null;
        if (_cfg.Operacoes.Contains("Exclusao"))
        {
            var totalExclusoes = (_cfg.WarmupIteracoes + _cfg.Repeticoes) * _cfg.Vus * _cfg.ExclusaoIteracoesPorVu;
            Console.WriteLine($"[{banco}] seed adicional de {totalExclusoes} documentos descartáveis (pool de exclusão)...");
            await api.SeedAsync(totalExclusoes, complexidadeStr, indiceInicial: volume);

            var descartaveis = await api.SampleAsync(totalExclusoes, recentes: true);
            targetsDeletePath = Path.Combine(dataDir, $"targets-delete-{banco}-{volume}-{complexidadeStr}.json");
            await File.WriteAllTextAsync(targetsDeletePath, JsonSerializer.Serialize(new { ids = descartaveis.Ids }));
        }

        foreach (var operacaoStr in _cfg.Operacoes)
        {
            await RunOperationAsync(banco, bancoCfg, containerId, volume, complexidadeStr, operacaoStr,
                targetsPath, targetsDeletePath, ordemDescricao, api);
        }
    }

    private async Task RunOperationAsync(
        string banco, BancoConfig bancoCfg, string containerId, int volume, string complexidadeStr,
        string operacaoStr, string targetsPath, string? targetsDeletePath, string ordemDescricao, ApiClient api)
    {
        Console.WriteLine($"[{banco}] operação={operacaoStr} — warm-up ({_cfg.WarmupIteracoes}x) + {_cfg.Repeticoes} repetições");

        var payloadFile = Path.Combine(_repoRoot, _cfg.PerformanceTestsDir, "data", $"payloads-{complexidadeStr}.json");
        var scriptFile = ParseOperacao(operacaoStr).NomeScript();
        var exclusaoOffset = 0;

        for (var w = 0; w < _cfg.WarmupIteracoes; w++)
        {
            try
            {
                var env = BuildEnv(bancoCfg, operacaoStr, targetsPath, targetsDeletePath, payloadFile, ref exclusaoOffset);
                var summaryRel = $"raw/k6-summaries/_warmup-{Guid.NewGuid():N}.json";
                await K6Runner.RunAsync(_repoRoot, scriptFile, env, summaryRel);
            }
            catch (Exception ex)
            {
                // Um warm-up é descartável por natureza — uma falha transiente aqui
                // não deve interromper um experimento de várias horas.
                Console.WriteLine($"[aviso] {banco}/{operacaoStr} warm-up {w + 1}/{_cfg.WarmupIteracoes} falhou e foi ignorado: {ex.Message}");
            }
        }

        for (var rep = 1; rep <= _cfg.Repeticoes; rep++)
        {
            try
            {
                var testId = Guid.NewGuid();
                var env = BuildEnv(bancoCfg, operacaoStr, targetsPath, targetsDeletePath, payloadFile, ref exclusaoOffset);

                var monitor = new ResourceMonitor(containerId, _cfg.StatsPollingMs);
                monitor.Start();

                var cronometro = System.Diagnostics.Stopwatch.StartNew();
                var summary = await K6Runner.RunAsync(_repoRoot, scriptFile, env, $"raw/k6-summaries/{testId:N}.json");
                cronometro.Stop();

                var stats = await monitor.StopAsync();
                var storage = await ObterStorageStatsComRetryAsync(api, banco, operacaoStr, rep);

                if (summary is null)
                {
                    Console.WriteLine($"[aviso] {banco}/{operacaoStr} repetição {rep}: k6 não retornou resumo válido — linha ignorada.");
                    continue;
                }

                var throughput = operacaoStr == "InsercaoLote"
                    ? summary.RequestsPerSecond * _cfg.BatchSize
                    : summary.RequestsPerSecond;

                _csv.AppendRow(new RawResultRow
                {
                    TestId = testId.ToString(),
                    Timestamp = DateTime.UtcNow,
                    Banco = banco,
                    Volume = volume,
                    Complexidade = complexidadeStr,
                    Operacao = operacaoStr,
                    Execucao = rep,
                    OrdemExecucao = ordemDescricao,
                    NumeroRequisicoes = summary.NumeroRequisicoes,
                    Concorrencia = _cfg.Vus,
                    LatenciaMediaMs = summary.LatenciaMediaMs,
                    MedianaMs = summary.MedianaMs,
                    P95Ms = summary.P95Ms,
                    P99Ms = summary.P99Ms,
                    Throughput = throughput,
                    RequestsPerSecond = summary.RequestsPerSecond,
                    CpuMediaPercentual = stats.CpuMediaPercentual,
                    CpuMaximaPercentual = stats.CpuMaximaPercentual,
                    MemoriaMediaMb = stats.MemoriaMediaMb,
                    MemoriaMaximaMb = stats.MemoriaMaximaMb,
                    EspacoDiscoMb = storage.TamanhoTotalMb,
                    TamanhoIndicesMb = storage.TamanhoIndicesMb,
                    TaxaErroPercentual = summary.TaxaErroPercentual,
                    DuracaoSegundos = cronometro.Elapsed.TotalSeconds
                });

                Console.WriteLine($"[{banco}] {operacaoStr} rep {rep}/{_cfg.Repeticoes}: " +
                    $"lat.média={summary.LatenciaMediaMs:F1}ms p95={summary.P95Ms:F1}ms rps={summary.RequestsPerSecond:F1} erro={summary.TaxaErroPercentual:F2}%");
            }
            catch (Exception ex)
            {
                // Um experimento de várias horas não pode morrer por causa de uma
                // falha transiente isolada (timeout de rede, container sob pressão
                // momentânea, etc.). Registra o aviso e segue para a próxima
                // repetição — a linha desta repetição simplesmente não é gravada.
                Console.WriteLine($"[aviso] {banco}/{operacaoStr} repetição {rep}/{_cfg.Repeticoes} falhou e foi ignorada: {ex.Message}");
            }
        }
    }

    private static async Task<StorageStatsResponse> ObterStorageStatsComRetryAsync(ApiClient api, string banco, string operacaoStr, int rep)
    {
        try
        {
            return await api.StorageStatsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[aviso] {banco}/{operacaoStr} repetição {rep}: falha ao coletar storage-stats ({ex.Message}); tentando novamente em 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5));

            try
            {
                return await api.StorageStatsAsync();
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"[aviso] {banco}/{operacaoStr} repetição {rep}: storage-stats falhou novamente ({ex2.Message}); usando zeros para esta linha.");
                return new StorageStatsResponse();
            }
        }
    }

    private Dictionary<string, string> BuildEnv(
        BancoConfig bancoCfg, string operacaoStr, string targetsPath, string? targetsDeletePath,
        string payloadFile, ref int exclusaoOffset)
    {
        if (operacaoStr == "Exclusao")
        {
            if (targetsDeletePath is null)
                throw new InvalidOperationException("Operação Exclusao configurada sem pool de documentos descartáveis.");

            var iteracoesDesteRun = _cfg.Vus * _cfg.ExclusaoIteracoesPorVu;
            var env = new Dictionary<string, string>
            {
                ["BASE_URL"] = bancoCfg.BaseUrl,
                ["VUS"] = _cfg.Vus.ToString(),
                ["ITERATIONS"] = iteracoesDesteRun.ToString(),
                ["OFFSET"] = exclusaoOffset.ToString(),
                ["TARGETS_FILE"] = ToContainerPath(targetsDeletePath)
            };
            exclusaoOffset += iteracoesDesteRun;
            return env;
        }

        var envComum = new Dictionary<string, string>
        {
            ["BASE_URL"] = bancoCfg.BaseUrl,
            ["VUS"] = _cfg.Vus.ToString(),
            ["DURATION"] = $"{_cfg.DuracaoSegundosPorExecucao}s",
            ["TARGETS_FILE"] = ToContainerPath(targetsPath),
            ["PAYLOAD_FILE"] = ToContainerPath(payloadFile)
        };

        if (operacaoStr == "InsercaoLote")
            envComum["BATCH_SIZE"] = _cfg.BatchSize.ToString();

        return envComum;
    }

    private string ToContainerPath(string hostAbsolutePath)
    {
        var perfRoot = Path.Combine(_repoRoot, _cfg.PerformanceTestsDir);
        var rel = Path.GetRelativePath(perfRoot, hostAbsolutePath).Replace('\\', '/');
        return $"/scripts/{rel}";
    }

    private async Task CaptureExplainAsync(string banco, int volume, string complexidadeStr, ApiClient api, SampleResponse sample)
    {
        var dir = Path.Combine(_repoRoot, _cfg.ResultsDir, "raw", "explain");
        Directory.CreateDirectory(dir);

        try
        {
            if (_cfg.Operacoes.Contains("ConsultaPropriedade") && sample.Nomes.Count > 0)
            {
                var texto = await api.ExplainAsync($"/benchmark/explain/propriedade?nome={Uri.EscapeDataString(sample.Nomes[0])}");
                await File.WriteAllTextAsync(Path.Combine(dir, $"{banco}-{volume}-{complexidadeStr}-propriedade.txt"), texto);
            }

            if (_cfg.Operacoes.Contains("ConsultaAninhada"))
            {
                if (sample.Cidades.Count > 0)
                {
                    var texto = await api.ExplainAsync($"/benchmark/explain/cidade/{Uri.EscapeDataString(sample.Cidades[0])}");
                    await File.WriteAllTextAsync(Path.Combine(dir, $"{banco}-{volume}-{complexidadeStr}-cidade.txt"), texto);
                }

                if (sample.CodigosProduto.Count > 0)
                {
                    var texto = await api.ExplainAsync($"/benchmark/explain/produtos/{sample.CodigosProduto[0]}");
                    await File.WriteAllTextAsync(Path.Combine(dir, $"{banco}-{volume}-{complexidadeStr}-produtos.txt"), texto);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[aviso] Falha ao capturar EXPLAIN ({banco}/{volume}/{complexidadeStr}): {ex.Message}");
        }
    }

    private static OperacaoBenchmark ParseOperacao(string valor) => Enum.Parse<OperacaoBenchmark>(valor);

    private static int ComplexidadeIndice(string complexidade) => complexidade switch
    {
        "Baixa" => 0,
        "Media" => 1,
        "Alta" => 2,
        _ => 3
    };
}
