namespace Benchmark.Shared;

/// <summary>
/// Níveis de complexidade dos documentos semi-estruturados usados no experimento.
/// A definição objetiva de cada nível está documentada no README (propriedades,
/// profundidade, arrays e tamanho aproximado).
/// </summary>
public enum ComplexidadeNivel
{
    Baixa,
    Media,
    Alta
}

public static class ComplexidadeNivelExtensions
{
    public static bool TryParsePtBr(string? valor, out ComplexidadeNivel nivel)
    {
        switch (valor?.Trim().ToLowerInvariant())
        {
            case "baixa":
                nivel = ComplexidadeNivel.Baixa;
                return true;
            case "media":
            case "média":
                nivel = ComplexidadeNivel.Media;
                return true;
            case "alta":
                nivel = ComplexidadeNivel.Alta;
                return true;
            default:
                nivel = default;
                return false;
        }
    }
}
