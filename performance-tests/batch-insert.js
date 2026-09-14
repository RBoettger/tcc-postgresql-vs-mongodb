// Operação: InsercaoLote (POST /documentos/batch)
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, JSON_HEADERS, loadPayloads, commonOptions, iterKey } from './lib/helpers.js';

const payloads = loadPayloads();
const BATCH_SIZE = parseInt(__ENV.BATCH_SIZE || '50', 10);

export const options = commonOptions();

export default function () {
  const start = iterKey(__VU, __ITER) * BATCH_SIZE;
  const documentos = [];
  for (let i = 0; i < BATCH_SIZE; i++) {
    documentos.push(payloads[(start + i) % payloads.length]);
  }

  const res = http.post(`${BASE_URL}/documentos/batch`, JSON.stringify({ documentos }), JSON_HEADERS);
  check(res, { 'status é 200': (r) => r.status === 200 });
}
