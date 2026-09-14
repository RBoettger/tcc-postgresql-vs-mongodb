using System.Text.Json;
using System.Text.RegularExpressions;

namespace Benchmark.Runner;

/// <summary>
/// Coleta hardware/versões usadas na execução e grava
/// results/raw/run-metadata.json — consumido por analysis/generate_report.py
/// para a seção "Resumo do experimento". Nada aqui é inventado: cada valor
/// vem de uma chamada real ao ambiente no momento da execução.
/// </summary>
public static class RunMetadata
{
    public static async Task WriteAsync(string repoRoot, BenchmarkConfig cfg)
    {
        var dotnetVersion = (await DockerCli.RunAsync(repoRoot, "dotnet", "--version")).StdOut.Trim();
        var dockerVersion = (await DockerCli.RunAsync(repoRoot, "docker", "--version")).StdOut.Trim();
        var composeVersion = (await DockerCli.RunAsync(repoRoot, "docker", "compose", "version")).StdOut.Trim();

        var composeYml = await File.ReadAllTextAsync(Path.Combine(repoRoot, "docker-compose.yml"));
        var imagens = Regex.Matches(composeYml, @"image:\s*(\S+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        var metadata = new
        {
            geradoEm = DateTime.UtcNow.ToString("o"),
            host = new
            {
                cpusDisponiveis = Environment.ProcessorCount,
                sistemaOperacional = Environment.OSVersion.ToString(),
                memoriaTotalMb = ObterMemoriaTotalMb()
            },
            versoes = new
            {
                dotnet = dotnetVersion,
                docker = dockerVersion,
                dockerCompose = composeVersion,
                imagensDocker = imagens
            },
            configuracao = cfg
        };

        var destino = Path.Combine(repoRoot, cfg.ResultsDir, "raw", "run-metadata.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        // camelCase explícito: "configuracao" embute o BenchmarkConfig (propriedades
        // C# em PascalCase) e analysis/generate_report.py lê as chaves em camelCase.
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        await File.WriteAllTextAsync(destino, JsonSerializer.Serialize(metadata, jsonOptions));
    }

    private static double ObterMemoriaTotalMb()
    {
        try
        {
            if (File.Exists("/proc/meminfo"))
            {
                var linha = File.ReadLines("/proc/meminfo").First(l => l.StartsWith("MemTotal:"));
                var kb = long.Parse(Regex.Match(linha, @"\d+").Value);
                return Math.Round(kb / 1024.0, 0);
            }
        }
        catch { /* segue para o fallback */ }

        return Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0, 0);
    }
}
