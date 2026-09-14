using System.Text.Json;
using System.Text.Json.Serialization;

namespace Benchmark.Shared;

/// <summary>
/// Serializa/desserializa <see cref="JsonRaw"/> preservando o JSON bruto, sem
/// mapear para um esquema C# fixo — necessário porque "Dados" tem forma
/// diferente conforme o nível de complexidade (baixa/média/alta).
/// </summary>
public class JsonRawConverter : JsonConverter<JsonRaw>
{
    public override JsonRaw Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return new JsonRaw(doc.RootElement.GetRawText());
    }

    public override void Write(Utf8JsonWriter writer, JsonRaw value, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.Parse(value.RawText);
        doc.RootElement.WriteTo(writer);
    }
}
