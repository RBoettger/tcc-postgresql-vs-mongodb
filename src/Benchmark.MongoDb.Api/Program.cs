using System.Diagnostics;
using Benchmark.MongoDb.Api.Db;
using Benchmark.Shared;
using MongoDB.Bson;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonRawConverter());
});

var connectionString = builder.Configuration["MONGO_CONNECTION"]
    ?? builder.Configuration.GetConnectionString("Mongo")
    ?? "mongodb://localhost:27017";

var mongoContext = new MongoContext(connectionString);
builder.Services.AddSingleton(mongoContext);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Benchmark.MongoDb.Api",
        Version = "v1",
        Description = "API experimental (TCC) — armazenamento de dados semi-estruturados em MongoDB/BSON."
    });
});

var app = builder.Build();

await mongoContext.EnsureIndexesAsync();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok", banco = "MongoDB" }));

const int SeedChunkSize = 5000;

static DocumentoResponse ToResponse(BsonDocument doc) => new()
{
    Id = Guid.Parse(doc["_id"].AsString),
    DataCriacao = doc["dataCriacao"].ToUniversalTime(),
    Dados = new JsonRaw(doc["dados"].AsBsonDocument.ToJson())
};

// ---------------------------------------------------------------------------
// CRUD principal
// ---------------------------------------------------------------------------

app.MapPost("/documentos", async (DocumentoRequest req, MongoContext ctx) =>
{
    var id = Guid.NewGuid();
    var dataCriacao = DateTime.UtcNow;

    var doc = new BsonDocument
    {
        { "_id", id.ToString() },
        { "dataCriacao", dataCriacao },
        { "dados", BsonDocument.Parse(req.Dados.RawText) }
    };

    await ctx.Documentos.InsertOneAsync(doc);

    return Results.Created($"/documentos/{id}", new DocumentoResponse { Id = id, DataCriacao = dataCriacao, Dados = req.Dados });
});

app.MapPost("/documentos/batch", async (DocumentoBatchRequest req, MongoContext ctx) =>
{
    var dataCriacao = DateTime.UtcNow;
    var ids = new List<Guid>(req.Documentos.Count);
    var docs = new List<BsonDocument>(req.Documentos.Count);

    foreach (var d in req.Documentos)
    {
        var id = Guid.NewGuid();
        ids.Add(id);
        docs.Add(new BsonDocument
        {
            { "_id", id.ToString() },
            { "dataCriacao", dataCriacao },
            { "dados", BsonDocument.Parse(d.RawText) }
        });
    }

    if (docs.Count > 0)
        await ctx.Documentos.InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false });

    return Results.Ok(new DocumentoBatchResponse { Inseridos = ids.Count, Ids = ids });
});

app.MapGet("/documentos/{id:guid}", async (Guid id, MongoContext ctx) =>
{
    var doc = await ctx.Documentos.Find(Builders<BsonDocument>.Filter.Eq("_id", id.ToString())).FirstOrDefaultAsync();
    return doc is null ? Results.NotFound() : Results.Ok(ToResponse(doc));
});

app.MapGet("/documentos", async (string? nome, int? limit, MongoContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(nome))
        return Results.BadRequest(new { erro = "Parâmetro 'nome' é obrigatório." });

    var take = Math.Clamp(limit ?? 100, 1, 1000);

    var resultados = await ctx.Documentos
        .Find(Builders<BsonDocument>.Filter.Eq("dados.nome", nome))
        .Limit(take)
        .ToListAsync();

    return Results.Ok(resultados.Select(ToResponse));
});

app.MapGet("/documentos/cidade/{cidade}", async (string cidade, int? limit, MongoContext ctx) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 1000);

    var resultados = await ctx.Documentos
        .Find(Builders<BsonDocument>.Filter.Eq("dados.endereco.cidade", cidade))
        .Limit(take)
        .ToListAsync();

    return Results.Ok(resultados.Select(ToResponse));
});

app.MapGet("/documentos/produtos/{codigoProduto:int}", async (int codigoProduto, int? limit, MongoContext ctx) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 1000);

    var resultados = await ctx.Documentos
        .Find(Builders<BsonDocument>.Filter.Eq("dados.pedidos.produtos.codigo", codigoProduto))
        .Limit(take)
        .ToListAsync();

    return Results.Ok(resultados.Select(ToResponse));
});

app.MapPut("/documentos/{id:guid}", async (Guid id, DocumentoRequest req, MongoContext ctx) =>
{
    var filtro = Builders<BsonDocument>.Filter.Eq("_id", id.ToString());
    var update = Builders<BsonDocument>.Update.Set("dados", BsonDocument.Parse(req.Dados.RawText));

    var atualizado = await ctx.Documentos.FindOneAndUpdateAsync(
        filtro, update, new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After });

    return atualizado is null ? Results.NotFound() : Results.Ok(ToResponse(atualizado));
});

app.MapDelete("/documentos/{id:guid}", async (Guid id, MongoContext ctx) =>
{
    var resultado = await ctx.Documentos.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id.ToString()));
    return resultado.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
});

// ---------------------------------------------------------------------------
// Infraestrutura de benchmark (idêntica em forma à API PostgreSQL)
// ---------------------------------------------------------------------------

app.MapPost("/benchmark/seed", async (SeedRequest req, MongoContext ctx) =>
{
    if (req.Quantidade <= 0)
        return Results.BadRequest(new { erro = "'quantidade' deve ser maior que zero." });

    if (!ComplexidadeNivelExtensions.TryParsePtBr(req.Complexidade, out var nivel))
        return Results.BadRequest(new { erro = "Complexidade inválida. Use Baixa, Media ou Alta." });

    var sw = Stopwatch.StartNew();
    var agora = DateTime.UtcNow;

    var buffer = new List<BsonDocument>(SeedChunkSize);
    for (var i = 0; i < req.Quantidade; i++)
    {
        var dadosJson = DeterministicDocumentGenerator.GenerateRawJson(
            DeterministicDocumentGenerator.DefaultSeed, req.IndiceInicial + i, nivel);

        buffer.Add(new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() },
            { "dataCriacao", agora },
            { "dados", BsonDocument.Parse(dadosJson) }
        });

        if (buffer.Count == SeedChunkSize)
        {
            await ctx.Documentos.InsertManyAsync(buffer, new InsertManyOptions { IsOrdered = false });
            buffer.Clear();
        }
    }

    if (buffer.Count > 0)
        await ctx.Documentos.InsertManyAsync(buffer, new InsertManyOptions { IsOrdered = false });

    sw.Stop();

    return Results.Ok(new SeedResponse { Inseridos = req.Quantidade, DuracaoSegundos = sw.Elapsed.TotalSeconds });
});

app.MapDelete("/benchmark/reset", async (MongoContext ctx) =>
{
    await ctx.ResetAsync();
    return Results.Ok(new ResetResponse());
});

app.MapGet("/benchmark/sample", async (int? count, bool? recentes, MongoContext ctx) =>
{
    var take = Math.Clamp(count ?? 100, 1, 5000);

    var projecao = Builders<BsonDocument>.Projection.Include("dados.nome").Include("dataCriacao");
    var query = ctx.Documentos.Find(FilterDefinition<BsonDocument>.Empty).Project(projecao);

    if (recentes ?? false)
        query = query.Sort(Builders<BsonDocument>.Sort.Descending("dataCriacao"));

    var docs = await query.Limit(take).ToListAsync();

    var ids = new List<Guid>();
    var nomes = new HashSet<string>();
    foreach (var d in docs)
    {
        ids.Add(Guid.Parse(d["_id"].AsString));
        if (d.TryGetValue("dados", out var dados) && dados.AsBsonDocument.TryGetValue("nome", out var nomeValor))
            nomes.Add(nomeValor.AsString);
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

app.MapGet("/benchmark/storage-stats", async (MongoContext ctx) =>
{
    try
    {
        var stats = await ctx.Database.RunCommandAsync<BsonDocument>(
            new BsonDocument { { "collStats", MongoContext.CollectionName } });

        var storageSizeMb = stats.GetValue("storageSize", 0).ToDouble() / 1024.0 / 1024.0;
        var indexSizeMb = stats.GetValue("totalIndexSize", 0).ToDouble() / 1024.0 / 1024.0;
        var count = stats.GetValue("count", 0).ToInt64();

        return Results.Ok(new StorageStatsResponse
        {
            TamanhoTotalMb = storageSizeMb + indexSizeMb,
            TamanhoIndicesMb = indexSizeMb,
            QuantidadeDocumentos = count
        });
    }
    catch (MongoCommandException)
    {
        // Coleção ainda não existe (nenhum seed executado).
        return Results.Ok(new StorageStatsResponse());
    }
});

// explain() — material qualitativo de apoio (fora do CSV principal).
app.MapGet("/benchmark/explain/propriedade", async (string nome, MongoContext ctx) =>
{
    var filtro = new BsonDocument("dados.nome", nome);
    return Results.Text(await ExplainAsync(ctx, filtro));
});

app.MapGet("/benchmark/explain/cidade/{cidade}", async (string cidade, MongoContext ctx) =>
{
    var filtro = new BsonDocument("dados.endereco.cidade", cidade);
    return Results.Text(await ExplainAsync(ctx, filtro));
});

app.MapGet("/benchmark/explain/produtos/{codigoProduto:int}", async (int codigoProduto, MongoContext ctx) =>
{
    var filtro = new BsonDocument("dados.pedidos.produtos.codigo", codigoProduto);
    return Results.Text(await ExplainAsync(ctx, filtro));
});

app.Run();

static async Task<string> ExplainAsync(MongoContext ctx, BsonDocument filtro)
{
    var comando = new BsonDocument
    {
        { "explain", new BsonDocument { { "find", MongoContext.CollectionName }, { "filter", filtro } } },
        { "verbosity", "executionStats" }
    };

    var resultado = await ctx.Database.RunCommandAsync<BsonDocument>(comando);
    return resultado.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { Indent = true });
}
