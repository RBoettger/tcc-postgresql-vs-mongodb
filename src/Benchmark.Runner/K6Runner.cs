namespace Benchmark.Runner;

/// <summary>
/// Executa um dos 8 scripts de performance-tests/ via
/// `docker compose run --rm k6 run ...`. Os mesmos scripts, com os mesmos
/// parâmetros, são usados para PostgreSQL e MongoDB — a única variação entre
/// execuções é o conteúdo do dicionário de variáveis de ambiente (BASE_URL,
/// TARGETS_FILE, etc.).
/// </summary>
public static class K6Runner
{
    private static string? _hostUidGid;

    public static async Task<K6Summary?> RunAsync(
        string repoRoot,
        string scriptFileName,
        Dictionary<string, string> env,
        string summaryRelativePathUnderResults)
    {
        // A imagem grafana/k6 roda como UID 12345 por padrão, que não tem
        // permissão de escrita em ./results (dono é o usuário do host). Rodar
        // com --user <uid host>:<gid host> evita "permission denied" ao gravar
        // o --summary-export. (Específico de Linux — ver README.)
        var uidGid = await GetHostUidGidAsync(repoRoot);

        // Importante: as variáveis precisam ser passadas como `-e` do próprio
        // binário k6 (populam __ENV dentro do script). Variáveis definidas via
        // `docker compose run -e` ficam apenas no ambiente do processo do
        // container e NÃO chegam a __ENV — por isso os `-e` abaixo vêm depois
        // de "k6 run", nunca antes do nome do serviço.
        var args = new List<string> { "compose", "run", "--rm", "--user", uidGid, "k6", "run" };

        foreach (var (chave, valor) in env)
        {
            args.Add("-e");
            args.Add($"{chave}={valor}");
        }

        args.Add($"/scripts/{scriptFileName}");
        args.Add("--summary-trend-stats=avg,min,med,max,p(90),p(95),p(99)");
        args.Add($"--summary-export=/results/{summaryRelativePathUnderResults}");

        var summaryAbsolutePath = Path.Combine(repoRoot, "results", summaryRelativePathUnderResults);
        Directory.CreateDirectory(Path.GetDirectoryName(summaryAbsolutePath)!);

        var resultado = await DockerCli.RunAsync(repoRoot, "docker", args.ToArray());

        if (!File.Exists(summaryAbsolutePath))
        {
            throw new InvalidOperationException(
                $"k6 não gerou o resumo esperado em '{summaryAbsolutePath}' (exit code {resultado.ExitCode}).\n" +
                $"stdout: {resultado.StdOut}\nstderr: {resultado.StdErr}");
        }

        var json = await File.ReadAllTextAsync(summaryAbsolutePath);
        return K6Summary.Parse(json);
    }

    private static async Task<string> GetHostUidGidAsync(string repoRoot)
    {
        if (_hostUidGid != null) return _hostUidGid;

        var uid = (await DockerCli.RunAsync(repoRoot, "id", "-u")).StdOut.Trim();
        var gid = (await DockerCli.RunAsync(repoRoot, "id", "-g")).StdOut.Trim();
        _hostUidGid = $"{uid}:{gid}";
        return _hostUidGid;
    }
}
