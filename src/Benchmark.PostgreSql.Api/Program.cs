using System.Diagnostics;
using System.Text.Json.Nodes;
using Benchmark.PostgreSql.Api.Db;
using Benchmark.Shared;
using Npgsql;
using NpgsqlTypes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonRawConverter());
});

var connectionString = builder.Configuration["POSTGRES_CONNECTION"]
    ?? builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Username=benchmark;Password=benchmark;Database=benchmark";

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
await using var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Benchmark.PostgreSql.Api",
        Version = "v1",
        Description = "API experimental (TCC) — armazenamento de dados semi-estruturados em PostgreSQL/JSONB."
    });
});

var app = builder.Build();

await Schema.EnsureCreatedAsync(dataSource);

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok", banco = "PostgreSQL" }));

// ---------------------------------------------------------------------------
// CRUD principal
// ---------------------------------------------------------------------------

app.MapPost("/documentos", async (DocumentoRequest req, NpgsqlDataSource ds) =>
{
    var id = Guid.NewGuid();
    var dataCriacao = DateTime.UtcNow;

    await using var cmd = ds.CreateCommand(
        "INSERT INTO documentos (id, data_criacao, dados) VALUES ($1, $2, $3::jsonb)");
    cmd.Parameters.AddWithValue(id);
    cmd.Parameters.AddWithValue(dataCriacao);
    cmd.Parameters.AddWithValue(req.Dados.RawText);
    await cmd.ExecuteNonQueryAsync();

    return Results.Created($"/documentos/{id}", new DocumentoResponse { Id = id, DataCriacao = dataCriacao, Dados = req.Dados });
});

app.MapPost("/documentos/batch", async (DocumentoBatchRequest req, NpgsqlDataSource ds) =>
{
    var dataCriacao = DateTime.UtcNow;
    var ids = new List<Guid>(req.Documentos.Count);

    await using var conn = await ds.OpenConnectionAsync();
    await using var batch = new NpgsqlBatch(conn);

    foreach (var doc in req.Documentos)
    {
        var id = Guid.NewGuid();
        ids.Add(id);

        var cmd = new NpgsqlBatchCommand("INSERT INTO documentos (id, data_criacao, dados) VALUES ($1, $2, $3::jsonb)");
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(dataCriacao);
        cmd.Parameters.AddWithValue(doc.RawText);
        batch.BatchCommands.Add(cmd);
    }

    await batch.ExecuteNonQueryAsync();

    return Results.Ok(new DocumentoBatchResponse { Inseridos = ids.Count, Ids = ids });
});

app.MapGet("/documentos/{id:guid}", async (Guid id, NpgsqlDataSource ds) =>
{
    await using var cmd = ds.CreateCommand("SELECT id, data_criacao, dados::text FROM documentos WHERE id = $1");
    cmd.Parameters.AddWithValue(id);
    await using var reader = await cmd.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
        return Results.NotFound();

    return Results.Ok(new DocumentoResponse
    {
        Id = reader.GetGuid(0),
        DataCriacao = reader.GetDateTime(1),
        Dados = new JsonRaw(reader.GetString(2))
    });
});

app.MapGet("/documentos", async (string? nome, int? limit, NpgsqlDataSource ds) =>
{
    if (string.IsNullOrWhiteSpace(nome))
        return Results.BadRequest(new { erro = "Parâmetro 'nome' é obrigatório." });

    var take = Math.Clamp(limit ?? 100, 1, 1000);

    await using var cmd = ds.CreateCommand(
        "SELECT id, data_criacao, dados::text FROM documentos WHERE dados ->> 'nome' = $1 LIMIT $2");
    cmd.Parameters.AddWithValue(nome);
    cmd.Parameters.AddWithValue(take);

    var resultados = new List<DocumentoResponse>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        resultados.Add(new DocumentoResponse
        {
            Id = reader.GetGuid(0),
            DataCriacao = reader.GetDateTime(1),
            Dados = new JsonRaw(reader.GetString(2))
        });
    }

    return Results.Ok(resultados);
});

app.MapGet("/documentos/cidade/{cidade}", async (string cidade, int? limit, NpgsqlDataSource ds) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 1000);

    await using var cmd = ds.CreateCommand(
        "SELECT id, data_criacao, dados::text FROM documentos WHERE dados -> 'endereco' ->> 'cidade' = $1 LIMIT $2");
    cmd.Parameters.AddWithValue(cidade);
    cmd.Parameters.AddWithValue(take);

    var resultados = new List<DocumentoResponse>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        resultados.Add(new DocumentoResponse
        {
            Id = reader.GetGuid(0),
            DataCriacao = reader.GetDateTime(1),
            Dados = new JsonRaw(reader.GetString(2))
        });
    }

    return Results.Ok(resultados);
});

app.MapGet("/documentos/produtos/{codigoProduto:int}", async (int codigoProduto, int? limit, NpgsqlDataSource ds) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 1000);

    // dados @? jsonpath é indexável pelo GIN (jsonb_path_ops). O código do
    // produto é um int já validado pelo binder de rota — seguro embutir via
    // format() no jsonpath.
    await using var cmd = ds.CreateCommand(
        """
        SELECT id, data_criacao, dados::text
        FROM documentos
        WHERE dados @? (format('$.pedidos[*].produtos[*] ? (@.codigo == %s)', $1::int))::jsonpath
        LIMIT $2
        """);
    cmd.Parameters.AddWithValue(codigoProduto);
    cmd.Parameters.AddWithValue(take);

    var resultados = new List<DocumentoResponse>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        resultados.Add(new DocumentoResponse
        {
            Id = reader.GetGuid(0),
            DataCriacao = reader.GetDateTime(1),
            Dados = new JsonRaw(reader.GetString(2))
        });
    }

    return Results.Ok(resultados);
});

app.MapPut("/documentos/{id:guid}", async (Guid id, DocumentoRequest req, NpgsqlDataSource ds) =>
{
    await using var cmd = ds.CreateCommand(
        "UPDATE documentos SET dados = $2::jsonb WHERE id = $1 RETURNING data_criacao");
    cmd.Parameters.AddWithValue(id);
    cmd.Parameters.AddWithValue(req.Dados.RawText);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.NotFound();

    return Results.Ok(new DocumentoResponse { Id = id, DataCriacao = reader.GetDateTime(0), Dados = req.Dados });
});

app.MapDelete("/documentos/{id:guid}", async (Guid id, NpgsqlDataSource ds) =>
{
    await using var cmd = ds.CreateCommand("DELETE FROM documentos WHERE id = $1");
    cmd.Parameters.AddWithValue(id);
    var linhas = await cmd.ExecuteNonQueryAsync();
    return linhas == 0 ? Results.NotFound() : Results.NoContent();
});

// ---------------------------------------------------------------------------
// Infraestrutura de benchmark (idêntica em forma à API MongoDB)
// ---------------------------------------------------------------------------

app.MapPost("/benchmark/seed", async (SeedRequest req, NpgsqlDataSource ds) =>
{
    if (req.Quantidade <= 0)
        return Results.BadRequest(new { erro = "'quantidade' deve ser maior que zero." });

    if (!ComplexidadeNivelExtensions.TryParsePtBr(req.Complexidade, out var nivel))
        return Results.BadRequest(new { erro = "Complexidade inválida. Use Baixa, Media ou Alta." });

    var sw = Stopwatch.StartNew();
    var agora = DateTime.UtcNow;

    await using var conn = await ds.OpenConnectionAsync();
    await using var importer = await conn.BeginBinaryImportAsync(
        "COPY documentos (id, data_criacao, dados) FROM STDIN (FORMAT BINARY)");

    for (var i = 0; i < req.Quantidade; i++)
    {
        var dadosJson = DeterministicDocumentGenerator.GenerateRawJson(
            DeterministicDocumentGenerator.DefaultSeed, req.IndiceInicial + i, nivel);

        await importer.StartRowAsync();
        await importer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
        await importer.WriteAsync(agora, NpgsqlDbType.TimestampTz);
        await importer.WriteAsync(dadosJson, NpgsqlDbType.Jsonb);
    }

    await importer.CompleteAsync();
    sw.Stop();

    return Results.Ok(new SeedResponse { Inseridos = req.Quantidade, DuracaoSegundos = sw.Elapsed.TotalSeconds });
});

app.MapDelete("/benchmark/reset", async (NpgsqlDataSource ds) =>
{
    await using var cmd = ds.CreateCommand("TRUNCATE TABLE documentos");
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new ResetResponse());
});

app.MapGet("/benchmark/sample", async (int? count, bool? recentes, NpgsqlDataSource ds) =>
{
    var take = Math.Clamp(count ?? 100, 1, 5000);
    var ordem = (recentes ?? false) ? "ORDER BY data_criacao DESC" : "";

    await using var cmd = ds.CreateCommand($"SELECT id, dados ->> 'nome' FROM documentos {ordem} LIMIT $1");
    cmd.Parameters.AddWithValue(take);

    var ids = new List<Guid>();
    var nomes = new HashSet<string>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        ids.Add(reader.GetGuid(0));
        if (!reader.IsDBNull(1)) nomes.Add(reader.GetString(1));
    }

    return Results.Ok(new SampleResponse
    {
        Ids = ids,
        Nomes = nomes.ToList(),
        Cidades = DeterministicDocumentGenerator.CidadesDisponiveis.ToList(),
        CodigosProduto = Enumerable.Range(
            DeterministicDocumentGenerator.CodigoProdutoBase,
            DeterministicDocumentGenerator.CodigoProdutoRange).ToList()
    });
});

app.MapGet("/benchmark/storage-stats", async (NpgsqlDataSource ds) =>
{
    // Importante: usa a estimativa de linhas do planner (pg_class.reltuples), NÃO
    // "SELECT count(*)". Um count(*) exato é um seq scan completo — sob carga de
    // escrita sustentada (testes de inserção/lote com centenas de milhares de
    // linhas) isso ficou lento o suficiente para estourar o timeout do Npgsql e
    // derrubar a aplicação. reltuples é atualizado por autovacuum/analyze e é
    // suficiente aqui, já que essa contagem é só informativa.
    await using var cmd = ds.CreateCommand(
        """
        SELECT
            pg_total_relation_size('documentos') / 1024.0 / 1024.0,
            pg_indexes_size('documentos') / 1024.0 / 1024.0,
            (SELECT GREATEST(reltuples, 0)::bigint FROM pg_class WHERE oid = 'documentos'::regclass)
        """);
    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();

    return Results.Ok(new StorageStatsResponse
    {
        TamanhoTotalMb = reader.GetDouble(0),
        TamanhoIndicesMb = reader.GetDouble(1),
        QuantidadeDocumentos = reader.IsDBNull(2) ? 0 : reader.GetInt64(2)
    });
});

// EXPLAIN ANALYZE — material qualitativo de apoio (fora do CSV principal).
app.MapGet("/benchmark/explain/propriedade", async (string nome, NpgsqlDataSource ds) =>
{
    await using var cmd = ds.CreateCommand(
        "EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) SELECT id FROM documentos WHERE dados ->> 'nome' = $1");
    cmd.Parameters.AddWithValue(nome);
    return Results.Text(await ExplainToTextAsync(cmd));
});

app.MapGet("/benchmark/explain/cidade/{cidade}", async (string cidade, NpgsqlDataSource ds) =>
{
    await using var cmd = ds.CreateCommand(
        "EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) SELECT id FROM documentos WHERE dados -> 'endereco' ->> 'cidade' = $1");
    cmd.Parameters.AddWithValue(cidade);
    return Results.Text(await ExplainToTextAsync(cmd));
});

app.MapGet("/benchmark/explain/produtos/{codigoProduto:int}", async (int codigoProduto, NpgsqlDataSource ds) =>
{
    await using var cmd = ds.CreateCommand(
        """
        EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT)
        SELECT id FROM documentos
        WHERE dados @? (format('$.pedidos[*].produtos[*] ? (@.codigo == %s)', $1::int))::jsonpath
        """);
    cmd.Parameters.AddWithValue(codigoProduto);
    return Results.Text(await ExplainToTextAsync(cmd));
});

app.Run();

static async Task<string> ExplainToTextAsync(NpgsqlCommand cmd)
{
    var linhas = new List<string>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        linhas.Add(reader.GetString(0));
    return string.Join('\n', linhas);
}
