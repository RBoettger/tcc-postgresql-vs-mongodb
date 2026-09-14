# Análise de Desempenho e Armazenamento em Dados Semi-Estruturados: PostgreSQL vs MongoDB

Projeto experimental de apoio a um Trabalho de Conclusão de Curso (TCC), construído
para responder de forma controlada e reprodutível à pergunta:

> **Como PostgreSQL e MongoDB se comportam no armazenamento de dados semi-estruturados
> em diferentes volumes de dados?**

O objetivo **não** é provar previamente que um banco é melhor que o outro. O projeto
gera evidência experimental (CSVs, gráficos e um relatório) que deve ser interpretada
depois de rodar os testes — nunca antes.

---

## 1. Contexto do TCC

- **Tema:** Análise de Desempenho e Armazenamento em Dados Semi-Estruturados: Um
  Estudo Comparativo entre PostgreSQL e MongoDB.
- **Objetivo:** comparar o desempenho do PostgreSQL (JSONB) e do MongoDB (BSON) no
  armazenamento e processamento de dados semi-estruturados, fornecendo dados que
  ajudem a decidir qual tecnologia usar em diferentes cenários de volumetria e
  complexidade — identificando em que cenários cada banco se sai melhor, como volume
  e complexidade afetam cada um, qual usa menos recursos, qual é mais eficiente em
  armazenamento, e em que cenários o Postgres dispensa a adoção de um banco
  documental dedicado.

---

## 2. Arquitetura do experimento

```
src/
  Benchmark.Shared/          DTOs, enum de complexidade e o gerador determinístico
                              de documentos — usado pelas duas APIs, garantindo que
                              PostgreSQL e MongoDB recebam exatamente os mesmos dados.
  Benchmark.PostgreSql.Api/  ASP.NET Core Minimal API + Npgsql puro (sem EF Core) +
                              JSONB.
  Benchmark.MongoDb.Api/     ASP.NET Core Minimal API + MongoDB.Driver + BSON.
  Benchmark.Runner/          Orquestrador (roda no host): sobe containers, gera
                              massa, executa os cenários via k6, coleta métricas
                              (docker stats, storage stats) e grava o CSV bruto.
performance-tests/           8 scripts k6, byte-idênticos para as duas APIs — a
                              única diferença de execução é a variável BASE_URL.
analysis/                    Scripts Python (pandas/matplotlib) que consolidam o
                              CSV bruto, comparam os bancos e geram gráficos/relatório.
results/                     Saída do experimento (raw/, consolidated/, charts/,
                              analysis/).
```

**Princípio central:** as duas APIs têm os mesmos endpoints, os mesmos DTOs, a mesma
regra de geração de dados e os mesmos cenários de teste. A única variável controlada
entre uma execução e outra é **qual banco está por trás da API** — Postgres/JSONB ou
MongoDB/BSON. Nenhum cache externo (Redis ou similar) é usado, para não beneficiar
artificialmente uma das implementações.

### Por que Npgsql puro (sem EF Core)?

Decisão deliberada: o EF Core adiciona uma camada de tradução LINQ→SQL que pode
distorcer a latência medida e tem suporte limitado a alguns operadores JSONB
avançados (paths profundamente aninhados). Usar Npgsql diretamente (ADO.NET) mantém
a API PostgreSQL no mesmo nível de abstração que o MongoDB.Driver na API MongoDB —
ambos são drivers de baixo nível, sem ORM — o que torna a comparação mais justa.

---

## 3. Modelo de dados

As duas APIs armazenam o mesmo envelope lógico:

```json
{ "id": "guid", "dataCriacao": "2026-08-29T12:00:00Z", "dados": { /* payload semi-estruturado */ } }
```

- **PostgreSQL:** tabela `documentos(id UUID PK, data_criacao TIMESTAMPTZ, dados JSONB)`,
  com índice GIN (`jsonb_path_ops`) para buscas profundas e índices de expressão
  para `nome` e `endereco.cidade`.
- **MongoDB:** coleção `documentos` com documentos `{ _id, dataCriacao, dados }` —
  mesma forma lógica — e índices equivalentes em `dados.nome`,
  `dados.endereco.cidade` e `dados.pedidos.produtos.codigo`.

### Níveis de complexidade

A complexidade é definida objetivamente pela estrutura gerada (ver
`src/Benchmark.Shared/DeterministicDocumentGenerator.cs`), não por adjetivos:

| Nível | Estrutura | Profundidade | Arrays |
|---|---|---|---|
| **Baixa** | `nome`, `idade`, `email`, `ativo` — só propriedades primitivas | 1 nível | nenhum |
| **Média** | + `endereco { cidade, estado }` (objeto aninhado) + `telefones[]` | 2 níveis | 1 array simples |
| **Alta** | + `endereco.coordenadas { latitude, longitude }` + `pedidos[] { produtos[] { atributos {...} } }` | 4+ níveis | arrays dentro de arrays |

Para obter os números exatos (quantidade de propriedades, objetos aninhados,
profundidade máxima, arrays, elementos em arrays e tamanho aproximado em bytes) a
partir de um documento real gerado, use:

```bash
dotnet run --project src/Benchmark.Runner -- --describe-complexity
```

Todos os documentos são gerados por `DeterministicDocumentGenerator`, com uma seed
fixa (`20260829` por padrão) — dado o mesmo `(seed, índice, complexidade)`, o
resultado é sempre byte-idêntico. Isso garante que PostgreSQL e MongoDB recebam
exatamente a mesma massa de dados em cada cenário.

---

## 4. Endpoints (idênticos nas duas APIs)

| Método | Rota | Descrição |
|---|---|---|
| POST | `/documentos` | Insere 1 documento |
| POST | `/documentos/batch` | Insere um lote |
| GET | `/documentos/{id}` | Busca por id |
| GET | `/documentos?nome=Ana` | Consulta por propriedade simples |
| GET | `/documentos/cidade/{cidade}` | Consulta por propriedade interna (1 nível) |
| GET | `/documentos/produtos/{codigoProduto}` | Consulta em estrutura profundamente aninhada |
| PUT | `/documentos/{id}` | Atualiza |
| DELETE | `/documentos/{id}` | Remove |
| POST | `/benchmark/seed` | Popula a base com N documentos determinísticos |
| DELETE | `/benchmark/reset` | Limpa a base completamente |
| GET | `/benchmark/sample` | Ids/nomes/cidades/códigos existentes (infra de medição, usada pelo Runner/k6) |
| GET | `/benchmark/storage-stats` | Tamanho total e de índices em disco (infra de medição) |
| GET | `/benchmark/explain/...` | `EXPLAIN ANALYZE` / `explain()` — material qualitativo de apoio |

As rotas `/benchmark/*` são infraestrutura de medição — implementadas de forma
idêntica nas duas APIs, não favorecem nenhum banco.

Swagger disponível em `/swagger` em cada API.

---

## 5. Subindo o ambiente

Pré-requisitos: **Docker** (com Compose v2), **.NET 9 SDK**, **Python 3.10+**.

```bash
docker compose up -d --build
```

Isso sobe: `postgres` (16.4, 2 CPUs / 2 GB RAM), `mongo` (7.0.14, 2 CPUs / 2 GB RAM),
`benchmark-postgresql-api` (porta 5001, 1 CPU / 1 GB RAM) e
`benchmark-mongodb-api` (porta 5002, 1 CPU / 1 GB RAM) — limites equivalentes entre
as duas stacks. O serviço `k6` não sobe por padrão (perfil `tools`); é chamado sob
demanda pelo Runner via `docker compose run --rm k6 ...`.

> `k6` usa `network_mode: host`, o que funciona nativamente em Linux (o container
> enxerga `localhost:5001`/`:5002` como o próprio host). Em Docker Desktop
> (macOS/Windows), `network_mode: host` não tem o mesmo efeito — nesse caso, instale
> o k6 localmente (https://k6.io/docs/get-started/installation/) e ajuste
> `performance-tests/` para rodar via CLI nativa em vez de `docker compose run k6`.

---

## 6. Testes de performance (k6)

8 scripts em `performance-tests/`, **idênticos** para as duas APIs — a única
diferença de execução é a variável de ambiente `BASE_URL`:

| Script | Operação |
|---|---|
| `insert.js` | Inserção individual |
| `batch-insert.js` | Inserção em lote |
| `query-by-id.js` | Consulta por id |
| `query-property.js` | Consulta por propriedade simples (`nome`) |
| `query-nested.js` | Consulta aninhada — alterna entre `cidade` (1 nível) e `produtos` (profundamente aninhado) |
| `update.js` | Atualização |
| `delete.js` | Exclusão |
| `concurrent-load.js` | Carga mista (40% insert / 20% get / 20% query / 20% update) |

Execução manual de um script isolado, por exemplo contra a API PostgreSQL:

```bash
docker compose run --rm \
  -e BASE_URL=http://localhost:5001 \
  -e VUS=10 -e DURATION=30s \
  -e TARGETS_FILE=/scripts/data/targets-PostgreSQL-10000-Media.json \
  -e PAYLOAD_FILE=/scripts/data/payloads-Media.json \
  k6 run /scripts/query-by-id.js --summary-export=/results/tmp/manual.json
```

Contra a API MongoDB, o **mesmo comando**, trocando apenas `BASE_URL` para
`http://localhost:5002` e os arquivos de `TARGETS_FILE`/`PAYLOAD_FILE` equivalentes.

### Exceção documentada: `delete.js`

Como excluir é uma operação destrutiva (não dá para repetir o mesmo id), `delete.js`
roda em modo `iterations` (contagem fixa de requisições) em vez de `duration` (janela
de tempo fixa) — usado por todas as outras operações. O Runner pré-popula um lote
descartável de documentos (mesma forma/complexidade do cenário) dimensionado para
cobrir todo o warm-up + repetições, e passa `OFFSET` para que cada execução consuma
uma fatia exclusiva, sem reutilizar ids já excluídos. Essa é a única exceção
metodológica do projeto, e é idêntica para as duas APIs.

---

## 7. Executando o experimento completo

```bash
./run-benchmark.sh          # experimento completo (benchmark.config.json)
./run-benchmark.sh --quick  # perfil reduzido — valida o pipeline de ponta a ponta em minutos
```

O script:

1. sobe os containers (`docker compose up -d --build`);
2. instala as dependências Python (`analysis/requirements.txt`);
3. roda `dotnet run --project src/Benchmark.Runner`, que:
   - aguarda `/health` das duas APIs;
   - grava `results/raw/run-metadata.json` (hardware/versões);
   - para cada combinação **Volume × Complexidade**, sorteia (com seed reprodutível)
     a ordem PostgreSQL/MongoDB e registra essa ordem no CSV;
   - para cada banco, na ordem sorteada: reset → seed do volume → captura de
     amostras (`/benchmark/sample`) → as 8 operações, cada uma com warm-up +
     N repetições;
   - grava cada execução em `results/raw/benchmark-results.csv` (append-only —
     nunca sobrescreve execuções anteriores);
   - ao final, chama `analysis/analyze.py` → `generate_charts.py` →
     `generate_report.py`.

### Configuração (`benchmark.config.json`)

```jsonc
{
  "volumes": [10000, 100000, 500000, 1000000],
  "complexidades": ["Baixa", "Media", "Alta"],
  "operacoes": ["Insercao", "InsercaoLote", "ConsultaPorId", "ConsultaPropriedade",
                "ConsultaAninhada", "Atualizacao", "Exclusao", "CargaConcorrente"],
  "repeticoes": 10,
  "warmupIteracoes": 2,
  "vus": 10,
  "duracaoSegundosPorExecucao": 30,
  "exclusaoIteracoesPorVu": 50,
  "margemResultadoSemelhantePercentual": 5
}
```

Adicionar uma nova volumetria é só incluir o número em `volumes`. O experimento
completo (4 volumes × 3 complexidades × 8 operações × 10 repetições × 2 bancos) é
**demorado**, principalmente nos volumes de 500 mil e 1 milhão de documentos — valide
o pipeline antes com um dos dois perfis reduzidos:

- `benchmark.quick.config.json`: volumes pequenos, 2 repetições, duração curta —
  `./run-benchmark.sh --quick` ou `dotnet run --project src/Benchmark.Runner --
  --quick`.
- `benchmark.smoke.config.json`: ainda menor (1 volume, 1 complexidade, poucas
  operações, 1 repetição) — só para confirmar em segundos que o ambiente está de pé
  e o pipeline roda de ponta a ponta —
  `dotnet run --project src/Benchmark.Runner -- --config benchmark.smoke.config.json`.

Ordem de execução: **não é sempre PostgreSQL primeiro** — a cada combinação
Volume×Complexidade, a ordem é sorteada com uma combinação aritmética determinística
de `seed`, `volume` e complexidade (não usa `HashCode.Combine`/`string.GetHashCode`,
que o .NET randomiza por processo e quebraria a reprodutibilidade) e fica registrada
na coluna `OrdemExecucao` do CSV bruto.

---

## 8. Formato dos CSVs

### `results/raw/benchmark-results.csv`

Uma linha por execução individual (uma repetição de uma operação, para um banco, em
um cenário de volume/complexidade):

```
TestId,Timestamp,Banco,Volume,Complexidade,Operacao,Execucao,OrdemExecucao,
NumeroRequisicoes,Concorrencia,LatenciaMediaMs,MedianaMs,P95Ms,P99Ms,Throughput,
RequestsPerSecond,CpuMediaPercentual,CpuMaximaPercentual,MemoriaMediaMb,
MemoriaMaximaMb,EspacoDiscoMb,TamanhoIndicesMb,TaxaErroPercentual,DuracaoSegundos
```

`TestId` e `OrdemExecucao` são acréscimos ao esquema do enunciado (rastreabilidade e
registro da ordem sorteada) — todas as colunas originalmente pedidas estão presentes.

### `results/consolidated/benchmark-summary.csv`

Uma linha por `Banco × Volume × Complexidade × Operacao`, com média, mediana,
mínimo, máximo, desvio padrão e percentis (nunca apenas a média) — ver
`analysis/analyze.py`.

### `results/consolidated/comparison.csv`

PostgreSQL e MongoDB lado a lado por `Volume × Complexidade × Operacao`, com o
vencedor de cada métrica (`menor é melhor` para latência/CPU/memória/disco/erro,
`maior é melhor` para throughput) e a diferença percentual (positivo = MongoDB maior
que PostgreSQL nessa métrica).

---

## 9. Gráficos e relatório

```bash
python3 analysis/analyze.py            # benchmark-summary.csv + comparison.csv
python3 analysis/generate_charts.py    # PNGs em results/charts/
python3 analysis/generate_report.py --margem 5   # results/analysis/benchmark-analysis.md
```

(O `run-benchmark.sh`/Runner já chama os três em sequência automaticamente.)

Gráficos gerados (por combinação de complexidade/operação presente nos dados):
latência, throughput, P95 e P99 por volumetria; consumo de CPU e memória; espaço em
disco por volumetria; impacto da complexidade na latência (no maior volume
disponível); contagem de cenários vencidos por métrica; e uma tabela de vencedor por
cenário (latência).

O relatório (`benchmark-analysis.md`) traz: resumo do experimento (hardware,
versões, configuração), cenários testados, resultados consolidados, cenários em que
cada banco teve melhor resultado, cenários semelhantes (dentro da margem
configurada), impacto da volumetria, impacto da complexidade e considerações finais
— **neutras e contextualizadas**, nunca do tipo "X é melhor que Y" de forma
genérica.

---

## 10. Reproduzindo em outra máquina

1. Instale Docker, .NET 9 SDK e Python 3.10+.
2. Clone o repositório.
3. Rode `./run-benchmark.sh --quick` primeiro para validar que o pipeline completo
   funciona no ambiente novo (gera CSVs/gráficos/relatório em minutos).
4. Ajuste `benchmark.config.json` conforme o hardware disponível (os limites de CPU
   e memória dos containers estão fixos em `docker-compose.yml` — 2 CPUs/2 GB para
   os bancos, 1 CPU/1 GB para as APIs — para garantir que a comparação continue
   controlada mesmo em outra máquina; ajuste os dois lados igualmente se precisar
   mudar).
5. Rode `./run-benchmark.sh` para o experimento completo.

Todo o hardware e as versões efetivamente usadas ficam registrados em
`results/raw/run-metadata.json` a cada execução, para citar no TCC.

---

## 11. Limitações conhecidas

- O experimento completo (4 volumes × 3 complexidades × 8 operações × 10 repetições
  × 2 bancos) é demorado, principalmente em 500 mil/1 milhão de documentos — use
  `--quick` para validar antes.
- CPU/memória são medidos apenas no container do banco (não da API .NET) — decisão
  deliberada para isolar a variável "banco de dados" da variável "driver/linguagem".
- `delete.js` roda em modo `iterations` (não `duration`) por ser uma operação
  destrutiva — ver seção 6.
- `network_mode: host` do serviço `k6` é uma conveniência específica de Linux; em
  Docker Desktop, use k6 instalado localmente.
