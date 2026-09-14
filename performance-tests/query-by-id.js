// Operação: ConsultaPorId (GET /documentos/{id})
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, loadTargets, pick, commonOptions, iterKey } from './lib/helpers.js';

const targets = loadTargets();

export const options = commonOptions();

export default function () {
  const id = pick(targets.ids, iterKey(__VU, __ITER));
  const res = http.get(`${BASE_URL}/documentos/${id}`);
  check(res, { 'status é 200': (r) => r.status === 200 });
}
