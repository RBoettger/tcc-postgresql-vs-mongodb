// Operação: Insercao (POST /documentos)
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, JSON_HEADERS, loadPayloads, pick, commonOptions, iterKey } from './lib/helpers.js';

const payloads = loadPayloads();

export const options = commonOptions();

export default function () {
  const dados = pick(payloads, iterKey(__VU, __ITER));
  const res = http.post(`${BASE_URL}/documentos`, JSON.stringify({ dados }), JSON_HEADERS);
  check(res, { 'status é 201': (r) => r.status === 201 });
}
