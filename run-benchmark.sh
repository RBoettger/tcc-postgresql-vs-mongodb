#!/usr/bin/env bash
# Executa o experimento completo de ponta a ponta:
#   1. sobe os containers (docker compose up -d --build)
#   2. instala as dependências Python de analysis/
#   3. roda o Benchmark.Runner (orquestra os cenários, grava o CSV bruto e,
#      ao final, chama analyze.py -> generate_charts.py -> generate_report.py)
#
# Uso:
#   ./run-benchmark.sh            # experimento completo (benchmark.config.json)
#   ./run-benchmark.sh --quick    # perfil reduzido, para validar o pipeline
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

RUNNER_ARGS=()
if [[ "${1:-}" == "--quick" ]]; then
  RUNNER_ARGS+=(--quick)
  shift
fi

echo "==> Subindo containers (docker compose up -d --build)"
docker compose up -d --build

echo "==> Instalando dependências Python (analysis/requirements.txt)"
python3 -m pip install --quiet -r analysis/requirements.txt

echo "==> Executando Benchmark.Runner"
dotnet run --project src/Benchmark.Runner -c Release -- --skip-docker-up "${RUNNER_ARGS[@]}" "$@"

echo "==> Concluído. Resultados em results/ (raw/, consolidated/, charts/, analysis/)."
