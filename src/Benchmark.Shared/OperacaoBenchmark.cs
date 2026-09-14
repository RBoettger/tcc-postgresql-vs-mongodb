namespace Benchmark.Shared;

/// <summary>
/// As 8 operações de benchmark, mapeadas 1:1 para os scripts em performance-tests/.
/// </summary>
public enum OperacaoBenchmark
{
    Insercao,
    InsercaoLote,
    ConsultaPorId,
    ConsultaPropriedade,
    ConsultaAninhada,
    Atualizacao,
    Exclusao,
    CargaConcorrente
}

public static class OperacaoBenchmarkExtensions
{
    /// <summary>Nome do arquivo .js correspondente em performance-tests/.</summary>
    public static string NomeScript(this OperacaoBenchmark operacao) => operacao switch
    {
        OperacaoBenchmark.Insercao => "insert.js",
        OperacaoBenchmark.InsercaoLote => "batch-insert.js",
        OperacaoBenchmark.ConsultaPorId => "query-by-id.js",
        OperacaoBenchmark.ConsultaPropriedade => "query-property.js",
        OperacaoBenchmark.ConsultaAninhada => "query-nested.js",
        OperacaoBenchmark.Atualizacao => "update.js",
        OperacaoBenchmark.Exclusao => "delete.js",
        OperacaoBenchmark.CargaConcorrente => "concurrent-load.js",
        _ => throw new ArgumentOutOfRangeException(nameof(operacao))
    };

    public static bool EhDestrutiva(this OperacaoBenchmark operacao) => operacao == OperacaoBenchmark.Exclusao;
}
