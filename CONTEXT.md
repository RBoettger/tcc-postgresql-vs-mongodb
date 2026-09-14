# Contexto do projeto — PostgreSQL vs MongoDB (TCC)

> Arquivo pensado para ser carregado como conhecimento de projeto em um workspace
> Claude. Resume tudo que é preciso saber para colaborar neste repositório sem
> precisar reler o código-fonte inteiro.

## 1. O que é este projeto

Experimento de apoio a um Trabalho de Conclusão de Curso (TCC) cujo tema é:

> **Análise de Desempenho e Armazenamento em Dados Semi-Estruturados: Um Estudo
> Comparativo entre PostgreSQL e MongoDB.**

Pergunta de pesquisa: como PostgreSQL (JSONB) e MongoDB (BSON) se comportam no
armazenamento e processamento de dados semi-estruturados, em diferentes volumes e
níveis de complexidade estrutural?

**Postura importante:** o projeto não parte da premissa de que um banco é melhor
que o outro. Ele gera evidência experimental (CSVs, gráficos, relatório) que deve
orientar a conclusão — nunca o contrário. Ao redigir texto do TCC ou interpretar
resultados, manter linguagem neutra e contextualizada ("PostgreSQL foi mais rápido
neste cenário específico", nunca "PostgreSQL é melhor que MongoDB").

## 2. Autor e propósito de uso

- Autor: Renan Boettger (renan.boettger18@gmail.com).
- Repositório local: `~/Desktop/Projects/tcc-postgresql-vs-mongodb`.
- Uso deste arquivo: dar contexto completo a uma sessão Claude nova (ex.: revisão do
  texto do TCC, interpretação de resultados, dúvidas de metodologia) sem precisar
  reprocessar todo o código.

## 3. Arquitetura do experimento

```
src/
  Benchmark.Shared/          DTOs, enum de complexidade e gerador determinístico de
                              documentos — compartilhado pelas duas APIs, garantindo
                              que PostgreSQL e MongoDB recebam dados idênticos.
  Benchmark.PostgreSql.Api/  ASP.NET Core Minimal API + Npgsql puro (SEM EF Core) +
                              coluna JSONB.
  Benchmark.MongoDb.Api/     ASP.NET Core Minimal API + MongoDB.Driver + BSON.
  Benchmark.Runner/          Orquestrador (roda no host): sobe containers, gera
                              massa de dados, executa os cenários via k6, coleta
                              métricas (docker stats, storage stats) e grava CSV.
performance-tests/           8 scripts k6, byte-idênticos entre as duas APIs — a
                              única diferença é a env var BASE_URL.
analysis/                    Scripts Python (pandas/matplotlib) que consolidam o
                              CSV bruto, comparam os bancos e geram gráficos/relatório.
results/                     Saída do experimento (raw/, consolidated/, charts/,
                              analysis/).
```

**Princípio central de comparação justa:** mesmos endpoints, mesmos DTOs, mesma
geração de dados, mesmos cenários de teste nas duas APIs. A única variável
controlada é qual banco está por trás. Nenhum cache externo (Redis etc.) é usado.
Decisão deliberada de não usar EF Core no lado PostgreSQL: mantém as duas APIs no
mesmo nível de abstração (drivers de baixo nível, sem ORM), evitando que a camada
LINQ→SQL do EF distorça a latência medida.

## 4. Modelo de dados e complexidade

Envelope lógico comum: `{ id, dataCriacao, dados }`, onde `dados` é o payload
semi-estruturado.

- PostgreSQL: tabela `documentos(id UUID PK, data_criacao TIMESTAMPTZ, dados JSONB)`
  com índice GIN (`jsonb_path_ops`) + índices de expressão para `nome` e
  `endereco.cidade`.
- MongoDB: coleção `documentos` com a mesma forma lógica e índices equivalentes em
  `dados.nome`, `dados.endereco.cidade`, `dados.pedidos.produtos.codigo`.

Três níveis de complexidade, definidos objetivamente pela estrutura (não por
adjetivo), gerados por `DeterministicDocumentGenerator` com seed fixa
(`20260829`) — reprodutibilidade byte-a-byte:

| Nível | Estrutura | Profundidade | Arrays |
|---|---|---|---|
| Baixa | `nome`, `idade`, `email`, `ativo` | 1 nível | nenhum |
| Média | + `endereco{cidade,estado}` + `telefones[]` | 2 níveis | 1 array simples |
| Alta | + `endereco.coordenadas{lat,lng}` + `pedidos[]{produtos[]{atributos{}}}` | 4+ níveis | arrays aninhados |

## 5. Endpoints (idênticos nas duas APIs)

CRUD padrão (`POST /documentos`, `POST /documentos/batch`, `GET /documentos/{id}`,
`GET /documentos?nome=`, `GET /documentos/cidade/{cidade}`,
`GET /documentos/produtos/{codigoProduto}`, `PUT /documentos/{id}`,
`DELETE /documentos/{id}`) + infraestrutura de medição, idêntica nas duas APIs:
`POST /benchmark/seed`, `DELETE /benchmark/reset`, `GET /benchmark/sample`,
`GET /benchmark/storage-stats`, `GET /benchmark/explain/...`.

## 6. Ambiente e execução

- Pré-requisitos: Docker (Compose v2), .NET 9 SDK, Python 3.10+.
- `docker compose up -d --build` sobe: `postgres` (16.4, 2 CPU/2 GB),
  `mongo` (7.0.14, 2 CPU/2 GB), `benchmark-postgresql-api` (porta 5001, 1 CPU/1 GB),
  `benchmark-mongodb-api` (porta 5002, 1 CPU/1 GB) — limites equivalentes.
- `k6` sobe sob demanda (`docker compose run --rm k6 ...`), usa
  `network_mode: host` (funciona nativo só em Linux; em Docker Desktop usar k6
  local).
- 8 scripts k6 em `performance-tests/` (insert, batch-insert, query-by-id,
  query-property, query-nested, update, delete, concurrent-load), idênticos entre
  bancos — só muda `BASE_URL`.
- Exceção metodológica documentada: `delete.js` roda em modo `iterations` (não
  `duration`), pois excluir é destrutivo e não repetível; o Runner pré-popula um
  lote descartável dimensionado para warm-up + repetições, com `OFFSET` para
  fatias exclusivas por execução.

### Rodando o experimento

```bash
./run-benchmark.sh          # completo (benchmark.config.json)
./run-benchmark.sh --quick  # perfil reduzido, valida o pipeline em minutos
```

Config principal (`benchmark.config.json`): volumes `[10000, 100000, 500000,
1000000]`, complexidades `[Baixa, Media, Alta]`, 8 operações, repetições e
warm-up configuráveis, 10 VUs, 30s por execução. A ordem PostgreSQL/MongoDB é
sorteada por combinação Volume×Complexidade com seed reprodutível (não usa
`HashCode.Combine`, que o .NET randomiza por processo) e fica registrada na coluna
`OrdemExecucao` do CSV.

Perfis reduzidos para validar o pipeline: `benchmark.quick.config.json` e
`benchmark.smoke.config.json`.

## 7. Saída: CSVs, gráficos, relatório

- `results/raw/benchmark-results.csv`: uma linha por execução individual (repetição
  de uma operação, por banco, por cenário volume/complexidade). Colunas incluem
  latência média/mediana/P95/P99, throughput, RPS, CPU média/máxima, memória
  média/máxima, espaço em disco, tamanho de índices, taxa de erro.
- `results/consolidated/benchmark-summary.csv`: agregado por
  Banco×Volume×Complexidade×Operação (média, mediana, min, max, desvio padrão,
  percentis).
- `results/consolidated/comparison.csv`: PostgreSQL vs MongoDB lado a lado, com
  vencedor por métrica e diferença percentual (positivo = MongoDB maior).
- `results/charts/*.png`: latência/throughput/P95/P99 por volumetria, CPU/memória,
  disco, impacto da complexidade, contagem de vitórias, tabela de vencedor.
- `results/analysis/benchmark-analysis.md`: relatório final gerado
  automaticamente — resumo do experimento, cenários testados, resultados
  consolidados, cenários vencidos por cada banco, cenários semelhantes (dentro da
  margem configurada), impacto de volumetria/complexidade, considerações finais
  neutras.

Pipeline de análise (chamado automaticamente pelo `run-benchmark.sh`):

```bash
python3 analysis/analyze.py
python3 analysis/generate_charts.py
python3 analysis/generate_report.py --margem 5
```

## 8. Estado atual dos dados (na última atualização deste arquivo)

- Já existe uma execução completa registrada em `results/raw/benchmark-results.csv`
  (gerada em 2026-08-29), cobrindo os 4 volumes (10000, 100000, 500000, 1000000) e
  as 3 complexidades, com 5 repetições por cenário — metadados de hardware/versões
  em `results/raw/run-metadata.json`.
- `results/consolidated/` e `results/analysis/benchmark-analysis.md` já foram
  gerados a partir dessa execução.
- `results/_quick-validation/` e `results/_crashed-run-partial/` são execuções
  auxiliares (validação do pipeline reduzido e uma execução que não terminou) —
  não usar como dado final do TCC.
- **Nota:** por padrão, `results/raw/*.csv`, `results/consolidated/*.csv`,
  `results/charts/*.png` e `results/analysis/*.md` estão no `.gitignore` (dados
  gerados, não versionados). Ver seção 10 abaixo sobre o que efetivamente está no
  Git.

## 9. Limitações conhecidas (documentadas no README)

- Experimento completo é demorado, principalmente em 500 mil/1 milhão de
  documentos.
- CPU/memória são medidos apenas no container do banco, não da API .NET — decisão
  deliberada para isolar a variável "banco de dados" da variável "driver/linguagem".
- `delete.js` roda em modo `iterations`, não `duration` (única exceção
  metodológica).
- `network_mode: host` do serviço `k6` é conveniência específica de Linux.

## 10. Controle de versão

- Repositório Git local inicializado neste projeto; ver `.gitignore` para o que é
  ignorado (builds .NET, dados gerados deterministicamente em
  `performance-tests/data/`, CSVs/gráficos/relatório brutos em `results/` exceto os
  `.gitkeep`).
- README completo (`README.md`) tem todos os detalhes operacionais passo a passo —
  este `CONTEXT.md` é um resumo para carregar em outra ferramenta/sessão Claude,
  não substitui o README.

## 11. Como uma sessão Claude pode ajudar a partir daqui

Usos típicos deste contexto:
- Ajudar a redigir/revisar o capítulo de metodologia ou resultados do TCC com base
  nos números reais em `results/consolidated/` e no relatório
  `results/analysis/benchmark-analysis.md`.
- Interpretar gráficos e decidir quais destacar no texto do TCC.
- Sugerir ajustes de configuração (`benchmark.config.json`) para novos cenários.
- Revisar/estender código em `src/` mantendo o princípio de paridade entre as duas
  APIs (qualquer mudança em uma API que afete comportamento observável deve ser
  espelhada na outra, ou justificada explicitamente como assimetria documentada).
