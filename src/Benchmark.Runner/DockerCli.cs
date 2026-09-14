using System.Diagnostics;

namespace Benchmark.Runner;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Wrapper fino sobre a CLI do Docker/Docker Compose. O Runner nunca fala
/// diretamente com o socket do Docker — sempre via processo `docker`, o que
/// evita dependências extras e mantém o Runner simples de auditar.
/// </summary>
public static class DockerCli
{
    public static Task<ProcessResult> RunAsync(string workingDirectory, string file, params string[] args)
        => RunAsync(workingDirectory, file, args, env: null);

    public static async Task<ProcessResult> RunAsync(string workingDirectory, string file, string[] args, Dictionary<string, string>? env)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        if (env != null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Falha ao iniciar processo '{file}'.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return new ProcessResult(proc.ExitCode, await stdoutTask, await stderrTask);
    }

    public static async Task<string> ComposeServiceContainerIdAsync(string workingDirectory, string service)
    {
        var result = await RunAsync(workingDirectory, "docker", "compose", "ps", "-q", service);
        var id = result.StdOut.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException(
                $"Não foi possível localizar o container do serviço '{service}'. " +
                "Confirme que 'docker compose up -d --build' foi executado antes do Runner. " +
                $"stderr: {result.StdErr}");
        }

        return id.Trim();
    }

    public static async Task ComposeUpAsync(string workingDirectory)
    {
        var result = await RunAsync(workingDirectory, "docker", "compose", "up", "-d", "--build");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'docker compose up' falhou: {result.StdErr}");
    }
}
