using Benchmark.Runner;
using Benchmark.Shared;

if (args.Contains("--describe-complexity"))
{
    foreach (var nivel in Enum.GetValues<ComplexidadeNivel>())
    {
        var stats = DeterministicDocumentGenerator.DescreverComplexidade(nivel);
        Console.WriteLine($"\n=== {stats.Nivel} ===");
        Console.WriteLine($"Propriedades: {stats.QuantidadePropriedades}");
        Console.WriteLine($"Objetos aninhados: {stats.QuantidadeObjetosAninhados}");
        Console.WriteLine($"Profundidade máxima: {stats.ProfundidadeMaxima}");
        Console.WriteLine($"Arrays: {stats.QuantidadeArrays}");
        Console.WriteLine($"Elementos em arrays: {stats.QuantidadeElementosEmArrays}");
        Console.WriteLine($"Tamanho aproximado: {stats.TamanhoAproximadoBytes} bytes");
        Console.WriteLine($"Exemplo: {stats.ExemploJson}");
    }
    return;
}

var repoRoot = LocalizarRaizDoRepositorio();

var configPath = "benchmark.config.json";
var pularSubidaDocker = false;
var pularAnalise = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--quick":
            configPath = "benchmark.quick.config.json";
            break;
        case "--config":
            configPath = args[++i];
            break;
        case "--skip-docker-up":
            pularSubidaDocker = true;
            break;
        case "--skip-analysis":
            pularAnalise = true;
            break;
        default:
            Console.WriteLine($"Aviso: argumento desconhecido '{args[i]}' ignorado.");
            break;
    }
}

var configAbsPath = Path.IsPathRooted(configPath) ? configPath : Path.Combine(repoRoot, configPath);
Console.WriteLine($"Repositório: {repoRoot}");
Console.WriteLine($"Configuração: {configAbsPath}");

var cfg = BenchmarkConfig.Load(configAbsPath);

if (!pularSubidaDocker)
{
    Console.WriteLine("\nSubindo containers (docker compose up -d --build)...");
    await DockerCli.ComposeUpAsync(repoRoot);
}

Console.WriteLine("\nAguardando disponibilidade das APIs...");
foreach (var (banco, bancoCfg) in cfg.Bancos)
{
    var api = new ApiClient(bancoCfg.BaseUrl);
    var ok = await api.WaitHealthyAsync(TimeSpan.FromMinutes(3));
    if (!ok)
    {
        Console.Error.WriteLine($"API {banco} ({bancoCfg.BaseUrl}) não respondeu a /health a tempo. Abortando.");
        Environment.Exit(1);
    }
    Console.WriteLine($"  {banco}: OK ({bancoCfg.BaseUrl})");
}

Console.WriteLine("\nGravando metadados da execução (hardware/versões)...");
await RunMetadata.WriteAsync(repoRoot, cfg);

var runner = new ScenarioRunner(cfg, repoRoot);
await runner.RunAllAsync();

if (!pularAnalise)
{
    Console.WriteLine("\nExecutando pipeline de análise (Python)...");
    await PythonPipeline.RunAsync(repoRoot, cfg.MargemResultadoSemelhantePercentual.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

Console.WriteLine("\nExperimento concluído.");

static string LocalizarRaizDoRepositorio()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
        dir = dir.Parent;

    if (dir != null)
        return dir.FullName;

    // Fallback: assume execução via `dotnet run --project src/Benchmark.Runner`
    // a partir da raiz do repositório.
    return Directory.GetCurrentDirectory();
}
