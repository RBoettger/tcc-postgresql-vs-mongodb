#!/usr/bin/env python3
"""
Lê results/raw/benchmark-results.csv (uma linha por execução individual) e
produz:
  - results/consolidated/benchmark-summary.csv  (estatísticas agregadas por
    Banco x Volume x Complexidade x Operacao — média, mediana, mínimo,
    máximo, desvio padrão e percentis, nunca apenas a média)
  - results/consolidated/comparison.csv         (PostgreSQL x MongoDB lado a
    lado, com vencedor por métrica e diferença percentual)

Nenhum valor é inventado: tudo vem exclusivamente do CSV bruto gerado pelo
Benchmark.Runner.
"""
import sys
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parent.parent
RAW_CSV = ROOT / "results" / "raw" / "benchmark-results.csv"
CONSOLIDATED_DIR = ROOT / "results" / "consolidated"

GROUP_COLS = ["Banco", "Volume", "Complexidade", "Operacao"]


def percentile(q):
    def _fn(serie):
        return np.percentile(serie.dropna(), q) if len(serie.dropna()) > 0 else np.nan

    _fn.__name__ = f"p{q}"
    return _fn


def build_summary(df: pd.DataFrame) -> pd.DataFrame:
    agg = df.groupby(GROUP_COLS).agg(
        Execucoes=("Execucao", "count"),
        LatenciaMedia=("LatenciaMediaMs", "mean"),
        LatenciaMediana=("LatenciaMediaMs", "median"),
        LatenciaMinima=("LatenciaMediaMs", "min"),
        LatenciaMaxima=("LatenciaMediaMs", "max"),
        LatenciaDesvioPadrao=("LatenciaMediaMs", "std"),
        LatenciaP95=("LatenciaMediaMs", percentile(95)),
        LatenciaP99=("LatenciaMediaMs", percentile(99)),
        P95Medio=("P95Ms", "mean"),
        P99Medio=("P99Ms", "mean"),
        ThroughputMedio=("Throughput", "mean"),
        ThroughputDesvioPadrao=("Throughput", "std"),
        RequestsPerSecondMedio=("RequestsPerSecond", "mean"),
        CpuMedia=("CpuMediaPercentual", "mean"),
        CpuDesvioPadrao=("CpuMediaPercentual", "std"),
        CpuMaxima=("CpuMaximaPercentual", "mean"),
        MemoriaMedia=("MemoriaMediaMb", "mean"),
        MemoriaDesvioPadrao=("MemoriaMediaMb", "std"),
        MemoriaMaxima=("MemoriaMaximaMb", "mean"),
        EspacoDiscoMedio=("EspacoDiscoMb", "mean"),
        TamanhoIndicesMedio=("TamanhoIndicesMb", "mean"),
        TaxaErroMedia=("TaxaErroPercentual", "mean"),
        DuracaoMediaSegundos=("DuracaoSegundos", "mean"),
    ).reset_index()

    return agg.round(4)


def winner(a, b, banco_a, banco_b, lower_is_better):
    if pd.isna(a) or pd.isna(b):
        return ""
    if abs(a - b) < 1e-9:
        return "Empate"
    if lower_is_better:
        return banco_a if a < b else banco_b
    return banco_a if a > b else banco_b


def pct_diff(base, comparado):
    """Diferença percentual de `comparado` em relação a `base` (positivo =
    comparado maior que base)."""
    if pd.isna(base) or pd.isna(comparado) or base == 0:
        return np.nan
    return round((comparado - base) / base * 100, 2)


def build_comparison(summary: pd.DataFrame) -> pd.DataFrame:
    bancos = summary["Banco"].unique().tolist()
    if "PostgreSQL" not in bancos or "MongoDB" not in bancos:
        print("Aviso: comparison.csv requer dados de PostgreSQL e MongoDB; "
              f"bancos encontrados: {bancos}. Nenhuma comparação gerada.")
        return pd.DataFrame()

    idx = ["Volume", "Complexidade", "Operacao"]
    pg = summary[summary.Banco == "PostgreSQL"].set_index(idx)
    mg = summary[summary.Banco == "MongoDB"].set_index(idx)

    chaves_comuns = pg.index.intersection(mg.index)
    faltantes_pg = mg.index.difference(pg.index)
    faltantes_mg = pg.index.difference(mg.index)
    if len(faltantes_pg) or len(faltantes_mg):
        print(f"Aviso: {len(faltantes_pg) + len(faltantes_mg)} cenário(s) sem par "
              "PostgreSQL/MongoDB foram excluídos de comparison.csv (ver benchmark-summary.csv "
              "para os dados individuais).")

    linhas = []
    for chave in chaves_comuns:
        volume, complexidade, operacao = chave
        p, m = pg.loc[chave], mg.loc[chave]

        linhas.append({
            "Volume": volume,
            "Complexidade": complexidade,
            "Operacao": operacao,
            "PostgreSqlLatencia": p.LatenciaMedia,
            "MongoDbLatencia": m.LatenciaMedia,
            "MelhorLatencia": winner(p.LatenciaMedia, m.LatenciaMedia, "PostgreSQL", "MongoDB", True),
            "DiferencaLatenciaPercentual": pct_diff(p.LatenciaMedia, m.LatenciaMedia),
            "PostgreSqlP95": p.P95Medio,
            "MongoDbP95": m.P95Medio,
            "PostgreSqlP99": p.P99Medio,
            "MongoDbP99": m.P99Medio,
            "PostgreSqlThroughput": p.ThroughputMedio,
            "MongoDbThroughput": m.ThroughputMedio,
            "MelhorThroughput": winner(p.ThroughputMedio, m.ThroughputMedio, "PostgreSQL", "MongoDB", False),
            "DiferencaThroughputPercentual": pct_diff(p.ThroughputMedio, m.ThroughputMedio),
            "PostgreSqlCpu": p.CpuMedia,
            "MongoDbCpu": m.CpuMedia,
            "MenorCpu": winner(p.CpuMedia, m.CpuMedia, "PostgreSQL", "MongoDB", True),
            "DiferencaCpuPercentual": pct_diff(p.CpuMedia, m.CpuMedia),
            "PostgreSqlMemoria": p.MemoriaMedia,
            "MongoDbMemoria": m.MemoriaMedia,
            "MenorMemoria": winner(p.MemoriaMedia, m.MemoriaMedia, "PostgreSQL", "MongoDB", True),
            "DiferencaMemoriaPercentual": pct_diff(p.MemoriaMedia, m.MemoriaMedia),
            "PostgreSqlDisco": p.EspacoDiscoMedio,
            "MongoDbDisco": m.EspacoDiscoMedio,
            "MenorDisco": winner(p.EspacoDiscoMedio, m.EspacoDiscoMedio, "PostgreSQL", "MongoDB", True),
            "DiferencaDiscoPercentual": pct_diff(p.EspacoDiscoMedio, m.EspacoDiscoMedio),
            "PostgreSqlTaxaErro": p.TaxaErroMedia,
            "MongoDbTaxaErro": m.TaxaErroMedia,
            "MenorTaxaErro": winner(p.TaxaErroMedia, m.TaxaErroMedia, "PostgreSQL", "MongoDB", True),
        })

    return pd.DataFrame(linhas).round(4)


def main():
    if not RAW_CSV.exists():
        sys.exit(f"Arquivo não encontrado: {RAW_CSV}\nRode o Benchmark.Runner antes de analisar os resultados.")

    df = pd.read_csv(RAW_CSV)
    if df.empty:
        sys.exit(f"{RAW_CSV} está vazio — nenhuma execução registrada ainda.")

    CONSOLIDATED_DIR.mkdir(parents=True, exist_ok=True)

    summary = build_summary(df)
    summary_path = CONSOLIDATED_DIR / "benchmark-summary.csv"
    summary.to_csv(summary_path, index=False)
    print(f"{summary_path.relative_to(ROOT)}: {len(summary)} linha(s).")

    comparison = build_comparison(summary)
    comparison_path = CONSOLIDATED_DIR / "comparison.csv"
    comparison.to_csv(comparison_path, index=False)
    print(f"{comparison_path.relative_to(ROOT)}: {len(comparison)} linha(s).")


if __name__ == "__main__":
    main()
