using System.Globalization;

namespace Benchmark.Runner;

public class RawResultRow
{
    public required string TestId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Banco { get; init; }
    public required int Volume { get; init; }
    public required string Complexidade { get; init; }
    public required string Operacao { get; init; }
    public required int Execucao { get; init; }
    public required string OrdemExecucao { get; init; }
    public required int NumeroRequisicoes { get; init; }
    public required int Concorrencia { get; init; }
    public required double LatenciaMediaMs { get; init; }
    public required double MedianaMs { get; init; }
    public required double P95Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double Throughput { get; init; }
    public required double RequestsPerSecond { get; init; }
    public required double CpuMediaPercentual { get; init; }
    public required double CpuMaximaPercentual { get; init; }
    public required double MemoriaMediaMb { get; init; }
    public required double MemoriaMaximaMb { get; init; }
    public required double EspacoDiscoMb { get; init; }
    public required double TamanhoIndicesMb { get; init; }
    public required double TaxaErroPercentual { get; init; }
    public required double DuracaoSegundos { get; init; }
}

/// <summary>
/// Escreve em modo append no CSV bruto — nunca sobrescreve execuções
/// anteriores. Cria o cabeçalho apenas se o arquivo ainda não existir.
/// </summary>
public class CsvWriter
{
    private static readonly string[] Header =
    {
        "TestId", "Timestamp", "Banco", "Volume", "Complexidade", "Operacao", "Execucao", "OrdemExecucao",
        "NumeroRequisicoes", "Concorrencia", "LatenciaMediaMs", "MedianaMs", "P95Ms", "P99Ms", "Throughput",
        "RequestsPerSecond", "CpuMediaPercentual", "CpuMaximaPercentual", "MemoriaMediaMb", "MemoriaMaximaMb",
        "EspacoDiscoMb", "TamanhoIndicesMb", "TaxaErroPercentual", "DuracaoSegundos"
    };

    private readonly string _path;
    private readonly object _lock = new();

    public CsvWriter(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        lock (_lock)
        {
            if (!File.Exists(_path))
                File.WriteAllText(_path, string.Join(',', Header) + "\n");
        }
    }

    public void AppendRow(RawResultRow row)
    {
        var campos = new object[]
        {
            row.TestId,
            row.Timestamp.ToString("o", CultureInfo.InvariantCulture),
            row.Banco,
            row.Volume,
            row.Complexidade,
            row.Operacao,
            row.Execucao,
            row.OrdemExecucao,
            row.NumeroRequisicoes,
            row.Concorrencia,
            row.LatenciaMediaMs,
            row.MedianaMs,
            row.P95Ms,
            row.P99Ms,
            row.Throughput,
            row.RequestsPerSecond,
            row.CpuMediaPercentual,
            row.CpuMaximaPercentual,
            row.MemoriaMediaMb,
            row.MemoriaMaximaMb,
            row.EspacoDiscoMb,
            row.TamanhoIndicesMb,
            row.TaxaErroPercentual,
            row.DuracaoSegundos
        };

        var linha = string.Join(',', campos.Select(Escape));

        lock (_lock)
        {
            File.AppendAllText(_path, linha + "\n");
        }
    }

    private static string Escape(object valor)
    {
        var texto = valor switch
        {
            double d => d.ToString("F4", CultureInfo.InvariantCulture),
            _ => Convert.ToString(valor, CultureInfo.InvariantCulture) ?? ""
        };

        if (texto.Contains(',') || texto.Contains('"') || texto.Contains('\n'))
            return "\"" + texto.Replace("\"", "\"\"") + "\"";

        return texto;
    }
}
