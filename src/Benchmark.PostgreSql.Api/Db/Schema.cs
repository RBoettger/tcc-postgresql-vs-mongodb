using Npgsql;

namespace Benchmark.PostgreSql.Api.Db;

/// <summary>
/// DDL da tabela única "documentos" e dos índices JSONB usados pelos cenários de
/// consulta do experimento (propriedade simples, propriedade aninhada e busca em
/// array profundamente aninhado).
/// </summary>
public static class Schema
{
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS documentos (
            id UUID PRIMARY KEY,
            data_criacao TIMESTAMPTZ NOT NULL,
            dados JSONB NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_documentos_dados_gin
            ON documentos USING GIN (dados jsonb_path_ops);

        CREATE INDEX IF NOT EXISTS idx_documentos_nome
            ON documentos ((dados ->> 'nome'));

        CREATE INDEX IF NOT EXISTS idx_documentos_cidade
            ON documentos ((dados -> 'endereco' ->> 'cidade'));
        """;

    public static async Task EnsureCreatedAsync(NpgsqlDataSource dataSource)
    {
        await using var cmd = dataSource.CreateCommand(Ddl);
        await cmd.ExecuteNonQueryAsync();
    }
}
