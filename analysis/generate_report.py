#!/usr/bin/env python3
"""
Lê results/consolidated/{benchmark-summary,comparison}.csv e
results/raw/run-metadata.json e monta results/analysis/benchmark-analysis.md.

Todas as frases são geradas programaticamente a partir dos números dos CSVs
— nada é hardcoded ou assumido antes da leitura dos dados. O relatório NUNCA
declara "X é melhor que Y" de forma genérica; só descreve cenários concretos
suportados pelos dados ("MongoDB apresentou menor latência em ... com ...").
"""
import argparse
import json
import sys
from pathlib import Path

import pandas as pd

ROOT = Path(__file__).resolve().parent.parent
CONSOLIDATED_DIR = ROOT / "results" / "consolidated"
ANALYSIS_DIR = ROOT / "results" / "analysis"
METADATA_PATH = ROOT / "results" / "raw" / "run-metadata.json"

COMPLEXIDADE_ORDEM = ["Baixa", "Media", "Alta"]


def df_to_md(df: pd.DataFrame, max_rows: int | None = None) -> str:
    if df.empty:
        return "_Sem dados para esta tabela._\n"
    if max_rows and len(df) > max_rows:
        df = df.head(max_rows)
        rodape = f"\n_(mostrando as primeiras {max_rows} de mais linhas — ver o CSV completo em results/consolidated/)_\n"
    else:
        rodape = ""
    # NaN aparece quando uma métrica não pôde ser calculada (ex.: desvio padrão
    # com uma única repetição) — mostrado como célula vazia, não "nan" literal.
    df = df.fillna("")
    return df.to_markdown(index=False) + "\n" + rodape


def secao_resumo(metadata: dict | None, cfg_path_hint: str) -> str:
    if metadata is None:
        return ("## Resumo do experimento\n\n"
                f"_Arquivo {METADATA_PATH.relative_to(ROOT)} não encontrado — "
                "rode o Benchmark.Runner para gerar os metadados de hardware/versões._\n")

    host = metadata.get("host", {})
    versoes = metadata.get("versoes", {})
    cfg = metadata.get("configuracao", {})

    linhas = [
        "## Resumo do experimento",
        "",
        f"- **Gerado em:** {metadata.get('geradoEm', '?')}",
        f"- **CPUs disponíveis (host):** {host.get('cpusDisponiveis', '?')}",
        f"- **Memória total (host):** {host.get('memoriaTotalMb', '?')} MB",
        f"- **Sistema operacional:** {host.get('sistemaOperacional', '?')}",
        f"- **.NET:** {versoes.get('dotnet', '?')}",
        f"- **Docker:** {versoes.get('docker', '?')}",
        f"- **Docker Compose:** {versoes.get('dockerCompose', '?')}",
        f"- **Imagens Docker fixadas:** {', '.join(versoes.get('imagensDocker', [])) or '?'}",
        "",
        "**Configuração do experimento:**",
        "",
        f"- Volumetrias: {cfg.get('volumes', '?')}",
        f"- Complexidades: {cfg.get('complexidades', '?')}",
        f"- Operações: {cfg.get('operacoes', '?')}",
        f"- Repetições por cenário: {cfg.get('repeticoes', '?')}",
        f"- Warm-up por operação: {cfg.get('warmupIteracoes', '?')} execuções descartadas",
        f"- Concorrência (VUs): {cfg.get('vus', '?')}",
        f"- Duração por execução (operações não-destrutivas): {cfg.get('duracaoSegundosPorExecucao', '?')}s",
        f"- Seed determinística: {cfg.get('seed', '?')}",
        "",
    ]
    return "\n".join(linhas)


def secao_cenarios(summary: pd.DataFrame) -> str:
    cenarios = summary[["Banco", "Volume", "Complexidade", "Operacao", "Execucoes"]].sort_values(
        ["Volume", "Complexidade", "Operacao", "Banco"])
    return "## Cenários testados\n\n" + df_to_md(cenarios, max_rows=200)


def secao_resultados(summary: pd.DataFrame) -> str:
    colunas = ["Banco", "Volume", "Complexidade", "Operacao", "LatenciaMedia", "LatenciaDesvioPadrao",
               "P95Medio", "P99Medio", "ThroughputMedio", "CpuMedia", "MemoriaMedia",
               "EspacoDiscoMedio", "TaxaErroMedia"]
    colunas = [c for c in colunas if c in summary.columns]
    tabela = summary[colunas].sort_values(["Volume", "Complexidade", "Operacao", "Banco"])
    return "## Resultados consolidados\n\n" + df_to_md(tabela, max_rows=200)


def formatar_cenario(row) -> str:
    return f"{row.Volume} documentos / complexidade {row.Complexidade} / operação {row.Operacao}"


def secao_melhores(comparison: pd.DataFrame, banco: str) -> str:
    if comparison.empty:
        return f"## {banco} apresentou melhor resultado em\n\n_Sem cenários com par PostgreSQL/MongoDB completo._\n"

    linhas = []
    metricas = [
        ("MelhorLatencia", "DiferencaLatenciaPercentual", "latência"),
        ("MelhorThroughput", "DiferencaThroughputPercentual", "throughput"),
        ("MenorCpu", "DiferencaCpuPercentual", "uso de CPU"),
        ("MenorMemoria", "DiferencaMemoriaPercentual", "uso de memória"),
        ("MenorDisco", "DiferencaDiscoPercentual", "espaço em disco"),
    ]

    for coluna_vencedor, coluna_diff, rotulo in metricas:
        if coluna_vencedor not in comparison.columns:
            continue
        vencidos = comparison[comparison[coluna_vencedor] == banco]
        for _, row in vencidos.iterrows():
            diff = row.get(coluna_diff)
            diff_txt = f" (diferença de {abs(diff):.1f}% em relação ao outro banco)" if pd.notna(diff) else ""
            linhas.append(f"- **{rotulo.capitalize()}** em {formatar_cenario(row)}{diff_txt}")

    corpo = "\n".join(linhas) if linhas else "_Nenhum cenário com esse banco como melhor nas métricas avaliadas._"
    return f"## {banco} apresentou melhor resultado em\n\n{corpo}\n"


def secao_semelhantes(comparison: pd.DataFrame, margem: float) -> str:
    if comparison.empty or "DiferencaLatenciaPercentual" not in comparison.columns:
        return "## Resultados semelhantes\n\n_Sem dados suficientes para esta análise._\n"

    semelhantes = comparison[comparison["DiferencaLatenciaPercentual"].abs() <= margem]
    linhas = [f"Cenários em que a diferença de latência entre os bancos foi menor que {margem:.1f}%:\n"]
    if semelhantes.empty:
        linhas.append("_Nenhum cenário com diferença de latência dentro dessa margem._")
    else:
        for _, row in semelhantes.iterrows():
            linhas.append(f"- {formatar_cenario(row)} — diferença de {row.DiferencaLatenciaPercentual:+.1f}%")

    return "## Resultados semelhantes\n\n" + "\n".join(linhas) + "\n"


def secao_impacto_volumetria(summary: pd.DataFrame) -> str:
    linhas = ["## Impacto da volumetria\n"]
    for banco, grupo_banco in summary.groupby("Banco"):
        variacoes = []
        for (complexidade, operacao), grupo in grupo_banco.groupby(["Complexidade", "Operacao"]):
            grupo = grupo.sort_values("Volume")
            if len(grupo) < 2:
                continue
            primeiro, ultimo = grupo.iloc[0], grupo.iloc[-1]
            if primeiro.LatenciaMedia == 0 or pd.isna(primeiro.LatenciaMedia) or pd.isna(ultimo.LatenciaMedia):
                continue
            variacao = (ultimo.LatenciaMedia - primeiro.LatenciaMedia) / primeiro.LatenciaMedia * 100
            variacoes.append(variacao)

        if not variacoes:
            continue

        media_variacao = sum(variacoes) / len(variacoes)
        linhas.append(
            f"- **{banco}**: a latência média variou, em média, {media_variacao:+.1f}% "
            f"do menor para o maior volume testado (média sobre {len(variacoes)} combinações de "
            "complexidade × operação com pelo menos dois volumes medidos).")

    if len(linhas) == 1:
        linhas.append("_Dados insuficientes (é necessário mais de uma volumetria por cenário)._")

    return "\n".join(linhas) + "\n"


def secao_impacto_complexidade(summary: pd.DataFrame) -> str:
    linhas = ["## Impacto da complexidade\n"]
    for banco, grupo_banco in summary.groupby("Banco"):
        variacoes = []
        for (volume, operacao), grupo in grupo_banco.groupby(["Volume", "Operacao"]):
            grupo = grupo.set_index("Complexidade").reindex(COMPLEXIDADE_ORDEM).dropna(subset=["LatenciaMedia"])
            if len(grupo) < 2:
                continue
            primeiro, ultimo = grupo.iloc[0], grupo.iloc[-1]
            if primeiro.LatenciaMedia == 0:
                continue
            variacao = (ultimo.LatenciaMedia - primeiro.LatenciaMedia) / primeiro.LatenciaMedia * 100
            variacoes.append(variacao)

        if not variacoes:
            continue

        media_variacao = sum(variacoes) / len(variacoes)
        linhas.append(
            f"- **{banco}**: a latência média variou, em média, {media_variacao:+.1f}% "
            "do nível de complexidade mais baixo para o mais alto disponível no mesmo cenário "
            f"(média sobre {len(variacoes)} combinações de volume × operação).")

    if len(linhas) == 1:
        linhas.append("_Dados insuficientes (é necessário mais de um nível de complexidade por cenário)._")

    return "\n".join(linhas) + "\n"


def secao_consideracoes(comparison: pd.DataFrame, margem: float) -> str:
    total = len(comparison)
    if total == 0:
        return ("## Considerações\n\n"
                "_Não há cenários com dados completos de PostgreSQL e MongoDB para comparação direta._\n")

    vitorias_pg = int((comparison["MelhorLatencia"] == "PostgreSQL").sum())
    vitorias_mg = int((comparison["MelhorLatencia"] == "MongoDB").sum())
    empates = int((comparison["MelhorLatencia"] == "Empate").sum())
    semelhantes = int((comparison["DiferencaLatenciaPercentual"].abs() <= margem).sum())

    texto = f"""
Dos {total} cenários com dados completos para os dois bancos, PostgreSQL apresentou menor
latência média em {vitorias_pg}, MongoDB em {vitorias_mg}, com {empates} empate(s) técnico(s).
Em {semelhantes} cenário(s) a diferença de latência ficou dentro da margem de {margem:.1f}%
configurada para "resultado semelhante".

Esses números descrevem o que foi observado nas condições específicas deste experimento
(hardware, limites de recursos, volumetrias e complexidades definidos em benchmark.config.json)
e não devem ser generalizados como "PostgreSQL é melhor" ou "MongoDB é melhor" de forma
absoluta. A contagem de cenários vencidos por métrica (ver
results/charts/contagem_vitorias.png) é informativa, mas não é o critério de conclusão do
estudo — cada métrica (latência, throughput, CPU, memória, disco, taxa de erro) deve ser
interpretada individualmente, à luz do cenário de uso pretendido.
""".strip()

    return "## Considerações\n\n" + texto + "\n"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--margem", type=float, default=5.0,
                         help="Margem percentual (padrão 5%%) para considerar dois resultados 'semelhantes'.")
    args = parser.parse_args()

    summary_path = CONSOLIDATED_DIR / "benchmark-summary.csv"
    comparison_path = CONSOLIDATED_DIR / "comparison.csv"

    if not summary_path.exists():
        sys.exit(f"Arquivo não encontrado: {summary_path}\nRode analysis/analyze.py antes.")

    summary = pd.read_csv(summary_path)
    comparison = pd.read_csv(comparison_path) if comparison_path.exists() else pd.DataFrame()

    metadata = None
    if METADATA_PATH.exists():
        metadata = json.loads(METADATA_PATH.read_text())

    partes = [
        "# Análise do experimento — PostgreSQL vs MongoDB (dados semi-estruturados)",
        "",
        "_Relatório gerado automaticamente por analysis/generate_report.py a partir dos CSVs "
        "em results/consolidated/. Nenhum valor abaixo foi editado manualmente._",
        "",
        secao_resumo(metadata, str(summary_path)),
        secao_cenarios(summary),
        secao_resultados(summary),
        secao_melhores(comparison, "PostgreSQL"),
        secao_melhores(comparison, "MongoDB"),
        secao_semelhantes(comparison, args.margem),
        secao_impacto_volumetria(summary),
        secao_impacto_complexidade(summary),
        secao_consideracoes(comparison, args.margem),
    ]

    ANALYSIS_DIR.mkdir(parents=True, exist_ok=True)
    destino = ANALYSIS_DIR / "benchmark-analysis.md"
    destino.write_text("\n".join(partes))
    print(f"{destino.relative_to(ROOT)} gerado.")


if __name__ == "__main__":
    main()
