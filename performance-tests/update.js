// Operação: Atualizacao (PUT /documentos/{id})
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, JSON_HEADERS, loadTargets, loadPayloads, pick, commonOptions, iterKey } from './lib/helpers.js';

const targets = loadTargets();
const payloads = loadPayloads();

export const options = commonOptions();

export default function () {
  const key = iterKey(__VU, __ITER);
  const id = pick(targets.ids, key);
  const dados = pick(payloads, key);

  const res = http.put(`${BASE_URL}/documentos/${id}`, JSON.stringify({ dados }), JSON_HEADERS);
  check(res, { 'status é 200': (r) => r.status === 200 });
}
