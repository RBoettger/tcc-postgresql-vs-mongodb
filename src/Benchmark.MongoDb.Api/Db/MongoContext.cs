using MongoDB.Bson;
using MongoDB.Driver;

namespace Benchmark.MongoDb.Api.Db;

/// <summary>
/// Encapsula o acesso à coleção "documentos" no banco "benchmark" — equivalente
/// funcional da tabela "documentos" da API PostgreSQL.
/// </summary>
public class MongoContext
{
    public const string CollectionName = "documentos";
    public const string DatabaseName = "benchmark";

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }
    public IMongoCollection<BsonDocument> Documentos { get; }

    public MongoContext(string connectionString)
    {
        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase(DatabaseName);
        Documentos = Database.GetCollection<BsonDocument>(CollectionName);
    }

    public async Task EnsureIndexesAsync()
    {
        var modelos = new[]
        {
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("dados.nome")),
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("dados.endereco.cidade")),
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("dados.pedidos.produtos.codigo"))
        };

        await Documentos.Indexes.CreateManyAsync(modelos);
    }

    public async Task ResetAsync()
    {
        await Database.DropCollectionAsync(CollectionName);
        await EnsureIndexesAsync();
    }
}
