// Operação: Exclusao (DELETE /documentos/{id})
//
// Exceção deliberada e documentada: como delete é destrutivo (não pode repetir
// o mesmo id), este script roda em modo "iterations" (contagem fixa de
// requisições) em vez de "duration" (janela de tempo fixa) usado pelas demais
// operações. O Benchmark.Runner pré-popula um lote descartável de documentos
// (mesma forma/complexidade do cenário) dimensionado para cobrir todas as
// execuções de warm-up + repetições, e passa OFFSET para que cada execução
// consuma uma fatia exclusiva e não sobreponha ids já excluídos por execuções
// anteriores. Ver README (seção "Testes de desempenho") para detalhes.
import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { BASE_URL, loadTargets } from './lib/helpers.js';

const targets = loadTargets();
const offset = parseInt(__ENV.OFFSET || '0', 10);
const totalIteracoes = parseInt(__ENV.ITERATIONS || '1', 10);

export const options = {
  vus: parseInt(__ENV.VUS || '10', 10),
  iterations: totalIteracoes,
};

export default function () {
  const globalIndex = offset + exec.scenario.iterationInTest;
  const id = targets.ids[globalIndex % targets.ids.length];
  const res = http.del(`${BASE_URL}/documentos/${id}`);
  check(res, { 'status é 204': (r) => r.status === 204 });
}
