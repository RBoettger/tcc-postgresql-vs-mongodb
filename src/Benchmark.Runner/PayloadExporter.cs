using System.Text.Json;
using Benchmark.Shared;

namespace Benchmark.Runner;

/// <summary>
/// Exporta, uma única vez por nível de complexidade, um conjunto de
/// documentos determinísticos para performance-tests/data/payloads-*.json.
/// Os scripts k6 leem esse arquivo via SharedArray e o ciclam via módulo —
/// como o arquivo é o mesmo para as duas APIs, PostgreSQL e MongoDB recebem
/// exatamente os mesmos corpos de requisição.
/// </summary>
public static class PayloadExporter
{
    public static async Task ExportAllAsync(string repoRoot, string performanceTestsDir, int seed, int poolSize, IEnumerable<string> complexidades)
    {
        var dataDir = Path.Combine(repoRoot, performanceTestsDir, "data");
        Directory.CreateDirectory(dataDir);

        foreach (var complexidadeStr in complexidades)
        {
            if (!ComplexidadeNivelExtensions.TryParsePtBr(complexidadeStr, out var nivel))
                throw new InvalidOperationException($"Complexidade inválida em configuração: '{complexidadeStr}'.");

            var caminho = Path.Combine(dataDir, $"payloads-{complexidadeStr}.json");

            var documentos = DeterministicDocumentGenerator.GenerateBatch(seed, 0, poolSize, nivel).ToList();

            await using var stream = File.Create(caminho);
            await JsonSerializer.SerializeAsync(stream, documentos);
        }
    }
}
