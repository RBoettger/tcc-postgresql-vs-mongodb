// Operação: ConsultaPropriedade — consulta por propriedade simples de primeiro
// nível (GET /documentos?nome=...)
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, loadTargets, pick, commonOptions, iterKey } from './lib/helpers.js';

const targets = loadTargets();

export const options = commonOptions();

export default function () {
  const nome = pick(targets.nomes, iterKey(__VU, __ITER));
  const res = http.get(`${BASE_URL}/documentos?${encodeURIComponent('nome')}=${encodeURIComponent(nome)}`);
  check(res, { 'status é 200': (r) => r.status === 200 });
}
