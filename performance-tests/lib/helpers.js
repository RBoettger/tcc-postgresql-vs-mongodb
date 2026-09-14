// Utilitários compartilhados pelos 8 scripts de benchmark. Nenhum destes
// scripts contém lógica específica de banco — a única diferença entre uma
// execução PostgreSQL e uma execução MongoDB é o valor de BASE_URL.
import { SharedArray } from 'k6/data';

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:5001';
export const JSON_HEADERS = { headers: { 'Content-Type': 'application/json' } };

// Lê o arquivo de "alvos" (ids/nomes/cidades/códigos de produto que realmente
// existem na base) gerado pelo Benchmark.Runner antes de cada cenário.
export function loadTargets() {
  const path = __ENV.TARGETS_FILE;
  if (!path) return { ids: [], nomes: [], cidades: [], codigosProduto: [] };
  return JSON.parse(open(path));
}

// Carrega os documentos determinísticos pré-gerados (mesmo arquivo usado nas
// execuções PostgreSQL e MongoDB) via SharedArray, evitando duplicar memória
// entre VUs.
export function loadPayloads() {
  const path = __ENV.PAYLOAD_FILE;
  if (!path) return [];
  return new SharedArray('payloads', function () {
    return JSON.parse(open(path));
  });
}

export function pick(arr, index) {
  if (!arr || arr.length === 0) return null;
  return arr[index % arr.length];
}

// Opções de execução comuns: VUS (concorrência) e DURATION (ou ITERATIONS),
// sempre lidos de variáveis de ambiente para garantir carga idêntica nas duas
// APIs.
export function commonOptions() {
  const vus = parseInt(__ENV.VUS || '10', 10);

  if (__ENV.ITERATIONS) {
    return { vus, iterations: parseInt(__ENV.ITERATIONS, 10) };
  }

  return { vus, duration: __ENV.DURATION || '30s' };
}

export function iterKey(vu, iter) {
  return iter + vu * 1000000;
}
