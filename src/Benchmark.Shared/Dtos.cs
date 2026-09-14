using System.Text.Json.Serialization;

namespace Benchmark.Shared;

/// <summary>
/// Corpo de requisição para POST /documentos e PUT /documentos/{id}.
/// "Dados" carrega o payload semi-estruturado (JSONB no Postgres, subdocumento no Mongo).
/// </summary>
public class DocumentoRequest
{
    public JsonRaw Dados { get; set; } = JsonRaw.Empty;
}

/// <summary>
/// Corpo de requisição para POST /documentos/batch.
/// </summary>
public class DocumentoBatchRequest
{
    public List<JsonRaw> Documentos { get; set; } = new();
}

/// <summary>
/// Formato de resposta comum às duas APIs para um documento.
/// </summary>
public class DocumentoResponse
{
    public Guid Id { get; set; }
    public DateTime DataCriacao { get; set; }
    public JsonRaw Dados { get; set; } = JsonRaw.Empty;
}

public class DocumentoBatchResponse
{
    public int Inseridos { get; set; }
    public List<Guid> Ids { get; set; } = new();
}

/// <summary>
/// POST /benchmark/seed
/// </summary>
public class SeedRequest
{
    public int Quantidade { get; set; }
    public string Complexidade { get; set; } = "Media";

    /// <summary>
    /// Índice inicial usado pelo gerador determinístico (default 0). Permite ao
    /// Runner popular um lote adicional "descartável" (usado pelo teste de
    /// exclusão) sem colidir com os índices já usados pela massa base do cenário,
    /// mantendo tudo determinístico. Campo opcional — não quebra o contrato
    /// { "quantidade": ..., "complexidade": ... } do enunciado.
    /// </summary>
    public int IndiceInicial { get; set; } = 0;
}

public class SeedResponse
{
    public int Inseridos { get; set; }
    public double DuracaoSegundos { get; set; }
}

public class ResetResponse
{
    public bool Sucesso { get; set; } = true;
}

/// <summary>
/// GET /benchmark/sample — usado pelo Runner para montar os "targets" consumidos
/// pelos scripts k6 (ids/cidades/códigos de produto que realmente existem na base).
/// </summary>
public class SampleResponse
{
    public List<Guid> Ids { get; set; } = new();
    public List<string> Cidades { get; set; } = new();
    public List<int> CodigosProduto { get; set; } = new();
    public List<string> Nomes { get; set; } = new();
}

/// <summary>
/// GET /benchmark/storage-stats
/// </summary>
public class StorageStatsResponse
{
    public double TamanhoTotalMb { get; set; }
    public double TamanhoIndicesMb { get; set; }
    public long QuantidadeDocumentos { get; set; }
}

/// <summary>
/// Wrapper leve em torno de um texto JSON já serializado, para evitar que o
/// System.Text.Json binder da Minimal API precise conhecer um esquema fixo do
/// payload semi-estruturado (que varia por nível de complexidade).
/// </summary>
[JsonConverter(typeof(JsonRawConverter))]
public readonly struct JsonRaw
{
    public string RawText { get; }

    public JsonRaw(string rawText)
    {
        RawText = string.IsNullOrWhiteSpace(rawText) ? "{}" : rawText;
    }

    public static readonly JsonRaw Empty = new("{}");

    public override string ToString() => RawText;
}
