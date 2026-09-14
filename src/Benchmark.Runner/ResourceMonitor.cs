using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Benchmark.Runner;

public record ResourceStats(double CpuMediaPercentual, double CpuMaximaPercentual, double MemoriaMediaMb, double MemoriaMaximaMb);

/// <summary>
/// Faz polling de `docker stats` no container do banco durante a execução de
/// cada teste k6, coletando CPU% e memória (MB) para calcular média/máximo.
/// Mede exclusivamente o container do banco (decisão confirmada com o autor
/// do TCC) — responde diretamente à pergunta de pesquisa sobre uso de
/// recursos por banco de dados.
/// </summary>
public class ResourceMonitor
{
    private static readonly Regex MemRegex = new(@"([\d.]+)\s*([KMGT]i?B)", RegexOptions.Compiled);

    private readonly string _containerId;
    private readonly int _pollingMs;
    private readonly List<double> _cpuSamples = new();
    private readonly List<double> _memSamplesMb = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public ResourceMonitor(string containerId, int pollingMs)
    {
        _containerId = containerId;
        _pollingMs = pollingMs;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task<ResourceStats> StopAsync()
    {
        _cts?.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask; } catch (OperationCanceledException) { }
        }

        lock (_cpuSamples)
        {
            if (_cpuSamples.Count == 0)
                return new ResourceStats(0, 0, 0, 0);

            return new ResourceStats(
                CpuMediaPercentual: _cpuSamples.Average(),
                CpuMaximaPercentual: _cpuSamples.Max(),
                MemoriaMediaMb: _memSamplesMb.Average(),
                MemoriaMaximaMb: _memSamplesMb.Max());
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (cpu, memMb) = await SampleAsync();
                lock (_cpuSamples)
                {
                    _cpuSamples.Add(cpu);
                    _memSamplesMb.Add(memMb);
                }
            }
            catch
            {
                // Amostra perdida (ex.: container momentaneamente ocupado) — ignorada.
            }

            try { await Task.Delay(_pollingMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<(double Cpu, double MemMb)> SampleAsync()
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("stats");
        psi.ArgumentList.Add("--no-stream");
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add("{{.CPUPerc}},{{.MemUsage}}");
        psi.ArgumentList.Add(_containerId);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar 'docker stats'.");
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var partes = output.Trim().Split(',');
        var cpu = double.Parse(partes[0].TrimEnd('%'), CultureInfo.InvariantCulture);
        var memTexto = partes[1].Split('/')[0].Trim();
        return (cpu, ParseMemToMb(memTexto));
    }

    private static double ParseMemToMb(string texto)
    {
        var match = MemRegex.Match(texto);
        if (!match.Success) return 0;

        var valor = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value switch
        {
            "GiB" => valor * 1024,
            "GB" => valor * 1000,
            "MiB" => valor,
            "MB" => valor,
            "KiB" => valor / 1024,
            "KB" => valor / 1000,
            "TiB" => valor * 1024 * 1024,
            "TB" => valor * 1000 * 1000,
            _ => valor
        };
    }
}
