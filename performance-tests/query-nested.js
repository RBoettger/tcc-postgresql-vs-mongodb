// Operação: ConsultaAninhada — alterna, por iteração, entre as duas consultas
// de propriedade não-superficial do enunciado:
//   (a) GET /documentos/cidade/{cidade}          — propriedade interna (1 nível)
//   (b) GET /documentos/produtos/{codigoProduto} — estrutura profundamente
//       aninhada (array dentro de array)
// Ambas fazem parte do mesmo "cenário de consulta aninhada"; alternar garante
// que a mesma carga combinada seja aplicada de forma idêntica às duas APIs.
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, loadTargets, pick, commonOptions, iterKey } from './lib/helpers.js';

const targets = loadTargets();

export const options = commonOptions();

export default function () {
  const key = iterKey(__VU, __ITER);

  if (key % 2 === 0) {
    const cidade = pick(targets.cidades, key);
    const res = http.get(`${BASE_URL}/documentos/cidade/${encodeURIComponent(cidade)}`);
    check(res, { 'status é 200 (cidade)': (r) => r.status === 200 });
  } else {
    const codigo = pick(targets.codigosProduto, key);
    const res = http.get(`${BASE_URL}/documentos/produtos/${codigo}`);
    check(res, { 'status é 200 (produto)': (r) => r.status === 200 });
  }
}
