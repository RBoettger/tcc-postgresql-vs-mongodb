namespace Benchmark.Runner;

/// <summary>
/// Dispara, em sequência, os três scripts de analysis/ que transformam o CSV
/// bruto em consolidados + gráficos + relatório final.
/// </summary>
public static class PythonPipeline
{
    public static async Task RunAsync(string repoRoot, string margemPercentual)
    {
        var python = ResolvePythonExecutable();

        await RunStepAsync(repoRoot, python, "analysis/analyze.py");
        await RunStepAsync(repoRoot, python, "analysis/generate_charts.py");
        await RunStepAsync(repoRoot, python, "analysis/generate_report.py", "--margem", margemPercentual);
    }

    private static async Task RunStepAsync(string repoRoot, string python, params string[] args)
    {
        Console.WriteLine($"\n>>> {python} {string.Join(' ', args)}");
        var result = await DockerCli.RunAsync(repoRoot, python, args);
        Console.WriteLine(result.StdOut);

        if (result.ExitCode != 0)
        {
            Console.WriteLine(result.StdErr);
            throw new InvalidOperationException($"Etapa Python '{args[0]}' falhou (exit {result.ExitCode}).");
        }
    }

    private static string ResolvePythonExecutable() => "python3";
}
