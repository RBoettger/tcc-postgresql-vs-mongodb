using System.Net.Http.Json;
using Benchmark.Shared;

namespace Benchmark.Runner;

/// <summary>
/// Cliente HTTP fino para as rotas /benchmark/* — usado pelo Runner apenas
/// para preparar cada cenário (reset/seed/sample/storage-stats). As
/// operações efetivamente medidas passam pelo k6, não por este cliente.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    public async Task<bool> WaitHealthyAsync(TimeSpan timeout)
    {
        var prazo = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < prazo)
        {
            try
            {
                var res = await _http.GetAsync("/health");
                if (res.IsSuccessStatusCode) return true;
            }
            catch { /* API ainda subindo */ }

            await Task.Delay(1000);
        }

        return false;
    }

    public async Task ResetAsync()
    {
        var res = await _http.DeleteAsync("/benchmark/reset");
        res.EnsureSuccessStatusCode();
    }

    public async Task<SeedResponse> SeedAsync(int quantidade, string complexidade, int indiceInicial = 0)
    {
        var res = await _http.PostAsJsonAsync("/benchmark/seed",
            new SeedRequest { Quantidade = quantidade, Complexidade = complexidade, IndiceInicial = indiceInicial });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SeedResponse>())!;
    }

    public async Task<SampleResponse> SampleAsync(int count, bool recentes = false)
    {
        var res = await _http.GetAsync($"/benchmark/sample?count={count}&recentes={recentes.ToString().ToLowerInvariant()}");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SampleResponse>())!;
    }

    public async Task<StorageStatsResponse> StorageStatsAsync()
    {
        var res = await _http.GetAsync("/benchmark/storage-stats");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<StorageStatsResponse>())!;
    }

    public async Task<string> ExplainAsync(string caminhoComQuery)
    {
        var res = await _http.GetAsync(caminhoComQuery);
        return await res.Content.ReadAsStringAsync();
    }
}
