using System.Text.Json.Nodes;

namespace Benchmark.Shared;

/// <summary>
/// Gerador determinístico de documentos semi-estruturados usado tanto pela API
/// PostgreSQL quanto pela API MongoDB. Dado o mesmo (seed, index, nível de
/// complexidade), sempre produz exatamente o mesmo conteúdo lógico — garantindo
/// que os dois bancos recebam massas de dados idênticas.
///
/// Listas fixas (nomes/cidades/produtos) são usadas — em vez de texto totalmente
/// aleatório — para que consultas por propriedade (nome, cidade, código de
/// produto) tenham resultados reais em qualquer volumetria.
/// </summary>
public static class DeterministicDocumentGenerator
{
    public const int DefaultSeed = 20260829;

    private static readonly string[] Nomes =
    {
        "Ana", "Bruno", "Carla", "Diego", "Elisa", "Fabio", "Gabriela", "Hugo",
        "Isabela", "Joao", "Karina", "Lucas", "Marina", "Nicolas", "Olivia",
        "Paulo", "Queila", "Rafael", "Sabrina", "Tiago"
    };

    private static readonly (string Cidade, string Estado)[] Cidades =
    {
        ("Joinville", "SC"), ("Florianopolis", "SC"), ("Blumenau", "SC"),
        ("Curitiba", "PR"), ("Porto Alegre", "RS"), ("Sao Paulo", "SP"),
        ("Chapeco", "SC"), ("Itajai", "SC"), ("Balneario Camboriu", "SC"),
        ("Criciuma", "SC")
    };

    private static readonly string[] Categorias =
    {
        "Eletronicos", "Vestuario", "Alimentos", "Moveis", "Brinquedos", "Livros",
        "Esportes", "Beleza"
    };

    private static readonly string[] Marcas = { "Marca A", "Marca B", "Marca C", "Marca D", "Marca E" };

    private static readonly string[] ProdutoNomes =
    {
        "Produto Alpha", "Produto Beta", "Produto Gama", "Produto Delta",
        "Produto Epsilon", "Produto Zeta"
    };

    private static readonly string[] StatusPedido = { "Finalizado", "Pendente", "Cancelado", "Em Processamento" };

    /// <summary>
    /// Conjunto fixo de códigos de produto reaproveitados entre documentos, para
    /// que GET /documentos/produtos/{codigoProduto} sempre retorne resultados,
    /// independentemente da volumetria.
    /// </summary>
    public const int CodigoProdutoBase = 1000;
    public const int CodigoProdutoRange = 200;

    /// <summary>Lista fixa de cidades usadas pelo gerador — exposta para que os
    /// endpoints /benchmark/sample não precisem consultar o banco para descobrir
    /// valores plausíveis de busca.</summary>
    public static IReadOnlyList<string> CidadesDisponiveis { get; } = Cidades.Select(c => c.Cidade).ToArray();

    public static IReadOnlyList<string> NomesDisponiveis { get; } = Nomes.ToArray();

    public static JsonObject Generate(int seed, int index, ComplexidadeNivel nivel)
    {
        var rng = CreateRng(seed, index);
        var (cidade, estado) = Cidades[index % Cidades.Length];
        var nome = Nomes[index % Nomes.Length];

        var dados = new JsonObject
        {
            ["nome"] = nome
        };

        if (nivel == ComplexidadeNivel.Baixa)
        {
            dados["idade"] = 18 + rng.Next(0, 60);
            dados["email"] = $"cliente{index:D7}@email.com";
            dados["ativo"] = rng.Next(0, 2) == 1;
            return dados;
        }

        dados["idade"] = 18 + rng.Next(0, 60);

        var endereco = new JsonObject
        {
            ["cidade"] = cidade,
            ["estado"] = estado
        };

        if (nivel == ComplexidadeNivel.Media)
        {
            dados["endereco"] = endereco;

            var telefones = new JsonArray();
            var qtdTelefones = 1 + (index % 2);
            for (var i = 0; i < qtdTelefones; i++)
            {
                var linha = 900000000 + ((index * 7 + i * 13) % 99999999);
                telefones.Add((JsonNode)$"47{linha:D9}");
            }

            dados["telefones"] = telefones;
            return dados;
        }

        // Alta complexidade: múltiplos níveis de aninhamento (endereco.coordenadas,
        // pedidos[].produtos[].atributos).
        endereco["coordenadas"] = new JsonObject
        {
            ["latitude"] = Math.Round(-26.3 + rng.NextDouble(), 4),
            ["longitude"] = Math.Round(-48.8 + rng.NextDouble(), 4)
        };
        dados["endereco"] = endereco;

        var pedidos = new JsonArray();
        var qtdPedidos = 1 + (index % 4);
        for (var p = 0; p < qtdPedidos; p++)
        {
            var produtos = new JsonArray();
            var qtdProdutos = 1 + ((index + p) % 3);
            for (var pr = 0; pr < qtdProdutos; pr++)
            {
                var codigo = CodigoProdutoBase + ((index + p * 7 + pr) % CodigoProdutoRange);
                produtos.Add(new JsonObject
                {
                    ["codigo"] = codigo,
                    ["nome"] = ProdutoNomes[codigo % ProdutoNomes.Length],
                    ["quantidade"] = 1 + rng.Next(0, 5),
                    ["atributos"] = new JsonObject
                    {
                        ["categoria"] = Categorias[codigo % Categorias.Length],
                        ["marca"] = Marcas[codigo % Marcas.Length]
                    }
                });
            }

            pedidos.Add(new JsonObject
            {
                ["numero"] = 1000 + index * 10 + p,
                ["status"] = StatusPedido[(index + p) % StatusPedido.Length],
                ["produtos"] = produtos
            });
        }

        dados["pedidos"] = pedidos;

        return dados;
    }

    public static string GenerateRawJson(int seed, int index, ComplexidadeNivel nivel)
        => Generate(seed, index, nivel).ToJsonString();

    public static IEnumerable<JsonObject> GenerateBatch(int seed, int startIndex, int count, ComplexidadeNivel nivel)
    {
        for (var i = 0; i < count; i++)
            yield return Generate(seed, startIndex + i, nivel);
    }

    /// <summary>
    /// Estatísticas objetivas de complexidade (usadas na documentação/README),
    /// calculadas a partir de um documento real gerado — nunca valores fixos
    /// inventados.
    /// </summary>
    public static ComplexidadeEstatisticas DescreverComplexidade(ComplexidadeNivel nivel, int seed = DefaultSeed, int amostraIndex = 0)
    {
        var doc = Generate(seed, amostraIndex, nivel);
        var json = doc.ToJsonString();

        int propriedades = 0, objetos = 0, arrays = 0, elementosEmArrays = 0, profundidadeMaxima = 0;

        void Percorrer(JsonNode? node, int profundidade)
        {
            profundidadeMaxima = Math.Max(profundidadeMaxima, profundidade);
            switch (node)
            {
                case JsonObject obj:
                    if (profundidade > 0) objetos++;
                    foreach (var (_, valor) in obj)
                    {
                        propriedades++;
                        Percorrer(valor, profundidade + 1);
                    }
                    break;
                case JsonArray arr:
                    arrays++;
                    elementosEmArrays += arr.Count;
                    foreach (var item in arr)
                        Percorrer(item, profundidade + 1);
                    break;
            }
        }

        Percorrer(doc, 0);

        return new ComplexidadeEstatisticas(
            Nivel: nivel,
            QuantidadePropriedades: propriedades,
            QuantidadeObjetosAninhados: objetos,
            ProfundidadeMaxima: profundidadeMaxima,
            QuantidadeArrays: arrays,
            QuantidadeElementosEmArrays: elementosEmArrays,
            TamanhoAproximadoBytes: System.Text.Encoding.UTF8.GetByteCount(json),
            ExemploJson: json);
    }

    private static Random CreateRng(int seed, int index)
        => new(unchecked(seed * 397 + index));
}

public record ComplexidadeEstatisticas(
    ComplexidadeNivel Nivel,
    int QuantidadePropriedades,
    int QuantidadeObjetosAninhados,
    int ProfundidadeMaxima,
    int QuantidadeArrays,
    int QuantidadeElementosEmArrays,
    int TamanhoAproximadoBytes,
    string ExemploJson);
