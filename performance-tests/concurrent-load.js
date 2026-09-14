// Operação: CargaConcorrente — mix fixo de operações não-destrutivas
// (40% insert, 20% consulta por id, 20% consulta por propriedade, 20% update)
// simulando uma carga realista. As proporções são idênticas nas duas APIs.
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, JSON_HEADERS, loadTargets, loadPayloads, pick, commonOptions, iterKey } from './lib/helpers.js';

const targets = loadTargets();
const payloads = loadPayloads();

export const options = commonOptions();

export default function () {
  const key = iterKey(__VU, __ITER);
  const roll = key % 10;

  if (roll < 4) {
    const dados = pick(payloads, key);
    const res = http.post(`${BASE_URL}/documentos`, JSON.stringify({ dados }), JSON_HEADERS);
    check(res, { 'insert 201': (r) => r.status === 201 });
  } else if (roll < 6) {
    const id = pick(targets.ids, key);
    const res = http.get(`${BASE_URL}/documentos/${id}`);
    check(res, { 'get 200': (r) => r.status === 200 });
  } else if (roll < 8) {
    const nome = pick(targets.nomes, key);
    const res = http.get(`${BASE_URL}/documentos?nome=${encodeURIComponent(nome)}`);
    check(res, { 'query 200': (r) => r.status === 200 });
  } else {
    const id = pick(targets.ids, key);
    const dados = pick(payloads, key);
    const res = http.put(`${BASE_URL}/documentos/${id}`, JSON.stringify({ dados }), JSON_HEADERS);
    check(res, { 'update 200': (r) => r.status === 200 });
  }
}
