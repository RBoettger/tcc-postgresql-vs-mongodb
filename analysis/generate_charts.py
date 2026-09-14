#!/usr/bin/env python3
"""
Lê results/consolidated/{benchmark-summary,comparison}.csv e gera os PNGs
comparativos em results/charts/. Todos os valores vêm exclusivamente dos
CSVs consolidados — nenhum número é inventado aqui.
"""
import sys
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import pandas as pd

ROOT = Path(__file__).resolve().parent.parent
CONSOLIDATED_DIR = ROOT / "results" / "consolidated"
CHARTS_DIR = ROOT / "results" / "charts"

BANK_COLORS = {"PostgreSQL": "#336791", "MongoDB": "#00684A"}
COMPLEXIDADE_ORDEM = ["Baixa", "Media", "Alta"]

plt.rcParams.update({
    "figure.dpi": 120,
    "axes.grid": True,
    "grid.alpha": 0.3,
    "font.size": 10,
})


def salvar(fig, nome):
    CHARTS_DIR.mkdir(parents=True, exist_ok=True)
    caminho = CHARTS_DIR / nome
    fig.savefig(caminho, bbox_inches="tight")
    plt.close(fig)
    print(f"  {caminho.relative_to(ROOT)}")


def linha_por_volume(summary: pd.DataFrame, metrica: str, titulo: str, ylabel: str, prefixo_arquivo: str):
    for (complexidade, operacao), grupo in summary.groupby(["Complexidade", "Operacao"]):
        fig, ax = plt.subplots(figsize=(7, 4.5))
        algum = False
        for banco, cor in BANK_COLORS.items():
            dados = grupo[grupo.Banco == banco].sort_values("Volume")
            if dados.empty:
                continue
            ax.plot(dados["Volume"], dados[metrica], marker="o", label=banco, color=cor)
            algum = True

        if not algum:
            plt.close(fig)
            continue

        ax.set_xscale("log")
        ax.set_xlabel("Volume de documentos (escala log)")
        ax.set_ylabel(ylabel)
        ax.set_title(f"{titulo}\ncomplexidade={complexidade} · operação={operacao}")
        ax.legend()
        salvar(fig, f"{prefixo_arquivo}_{complexidade}_{operacao}.png")


def disco_por_volume(summary: pd.DataFrame):
    agrupado = summary.groupby(["Banco", "Volume", "Complexidade"], as_index=False)["EspacoDiscoMedio"].mean()

    for complexidade, grupo in agrupado.groupby("Complexidade"):
        fig, ax = plt.subplots(figsize=(7, 4.5))
        algum = False
        for banco, cor in BANK_COLORS.items():
            dados = grupo[grupo.Banco == banco].sort_values("Volume")
            if dados.empty:
                continue
            ax.plot(dados["Volume"], dados["EspacoDiscoMedio"], marker="o", label=banco, color=cor)
            algum = True

        if not algum:
            plt.close(fig)
            continue

        ax.set_xscale("log")
        ax.set_xlabel("Volume de documentos (escala log)")
        ax.set_ylabel("Espaço em disco médio (MB)")
        ax.set_title(f"Espaço em disco por volumetria\ncomplexidade={complexidade} "
                      "(média entre as operações executadas no cenário)")
        ax.legend()
        salvar(fig, f"disco_por_volume_{complexidade}.png")


def impacto_complexidade(summary: pd.DataFrame):
    for operacao, grupo in summary.groupby("Operacao"):
        volume_max = grupo["Volume"].max()
        dados_volume = grupo[grupo.Volume == volume_max]
        if dados_volume.empty:
            continue

        fig, ax = plt.subplots(figsize=(7, 4.5))
        algum = False
        for banco, cor in BANK_COLORS.items():
            dados = dados_volume[dados_volume.Banco == banco]
            dados = dados.set_index("Complexidade").reindex(COMPLEXIDADE_ORDEM).dropna(subset=["LatenciaMedia"])
            if dados.empty:
                continue
            ax.plot(dados.index, dados["LatenciaMedia"], marker="o", label=banco, color=cor)
            algum = True

        if not algum:
            plt.close(fig)
            continue

        ax.set_xlabel("Complexidade do documento")
        ax.set_ylabel("Latência média (ms)")
        ax.set_title(f"Impacto da complexidade na latência\noperação={operacao} · volume={volume_max}")
        ax.legend()
        salvar(fig, f"impacto_complexidade_{operacao}_vol{volume_max}.png")


def contagem_vitorias(comparison: pd.DataFrame):
    colunas_vencedor = ["MelhorLatencia", "MelhorThroughput", "MenorCpu", "MenorMemoria", "MenorDisco", "MenorTaxaErro"]
    colunas_presentes = [c for c in colunas_vencedor if c in comparison.columns]
    if not colunas_presentes:
        return

    contagens = {"PostgreSQL": 0, "MongoDB": 0, "Empate": 0}
    for coluna in colunas_presentes:
        vc = comparison[coluna].value_counts()
        for chave in contagens:
            contagens[chave] += int(vc.get(chave, 0))

    fig, ax = plt.subplots(figsize=(6, 4.5))
    cores = [BANK_COLORS.get(k, "#999999") for k in contagens]
    ax.bar(contagens.keys(), contagens.values(), color=cores)
    ax.set_ylabel("Nº de métricas·cenários em que foi o melhor")
    ax.set_title("Contagem de cenários vencidos por métrica\n"
                  "(NÃO usar isoladamente para concluir qual banco é melhor)")
    for i, v in enumerate(contagens.values()):
        ax.text(i, v, str(v), ha="center", va="bottom")
    salvar(fig, "contagem_vitorias.png")


def tabela_vencedor_latencia(comparison: pd.DataFrame):
    if comparison.empty:
        return

    tabela = comparison[["Volume", "Complexidade", "Operacao", "MelhorLatencia", "DiferencaLatenciaPercentual"]].copy()
    tabela = tabela.sort_values(["Volume", "Complexidade", "Operacao"])
    tabela["DiferencaLatenciaPercentual"] = tabela["DiferencaLatenciaPercentual"].map(
        lambda v: f"{v:+.1f}%" if pd.notna(v) else "")

    altura = 0.35 * len(tabela) + 1.2
    fig, ax = plt.subplots(figsize=(9, min(altura, 40)))
    ax.axis("off")
    mpl_table = ax.table(
        cellText=tabela.values,
        colLabels=["Volume", "Complexidade", "Operação", "Melhor (latência)", "Diferença % (Mongo vs Postgres)"],
        loc="center",
        cellLoc="center",
    )
    mpl_table.auto_set_font_size(False)
    mpl_table.set_fontsize(8)
    mpl_table.scale(1, 1.3)
    ax.set_title("Vencedor por cenário — latência média", pad=20)
    salvar(fig, "tabela_vencedor_latencia.png")


def main():
    summary_path = CONSOLIDATED_DIR / "benchmark-summary.csv"
    comparison_path = CONSOLIDATED_DIR / "comparison.csv"

    if not summary_path.exists():
        sys.exit(f"Arquivo não encontrado: {summary_path}\nRode analysis/analyze.py antes.")

    summary = pd.read_csv(summary_path)
    comparison = pd.read_csv(comparison_path) if comparison_path.exists() else pd.DataFrame()

    print("Gerando gráficos de latência por volumetria...")
    linha_por_volume(summary, "LatenciaMedia", "Latência média por volumetria", "Latência média (ms)", "latencia_por_volume")

    print("Gerando gráficos de throughput por volumetria...")
    linha_por_volume(summary, "ThroughputMedio", "Throughput por volumetria", "Throughput (unid./s)", "throughput_por_volume")

    print("Gerando gráficos de P95 por volumetria...")
    linha_por_volume(summary, "P95Medio", "P95 por volumetria", "P95 (ms)", "p95_por_volume")

    print("Gerando gráficos de P99 por volumetria...")
    linha_por_volume(summary, "P99Medio", "P99 por volumetria", "P99 (ms)", "p99_por_volume")

    print("Gerando gráficos de CPU por volumetria...")
    linha_por_volume(summary, "CpuMedia", "Consumo de CPU por volumetria", "CPU média (%)", "cpu_por_volume")

    print("Gerando gráficos de memória por volumetria...")
    linha_por_volume(summary, "MemoriaMedia", "Consumo de memória por volumetria", "Memória média (MB)", "memoria_por_volume")

    print("Gerando gráficos de espaço em disco...")
    disco_por_volume(summary)

    print("Gerando gráficos de impacto da complexidade...")
    impacto_complexidade(summary)

    if not comparison.empty:
        print("Gerando gráfico de contagem de vitórias...")
        contagem_vitorias(comparison)

        print("Gerando tabela de vencedor por cenário (latência)...")
        tabela_vencedor_latencia(comparison)
    else:
        print("comparison.csv vazio ou ausente — pulando gráficos de comparação direta.")

    print("\nGráficos gerados em results/charts/.")


if __name__ == "__main__":
    main()
