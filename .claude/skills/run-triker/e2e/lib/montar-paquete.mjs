// Monta el paquete de la web como lo hace `srv subir`, pero en local y sin tocar
// el servidor: dotnet publish, su wwwroot a <paquete>/web y los pasos propios de
// srv.json (deploy/*.mjs) con SRV_PAQUETE apuntando ahí.
//
// Sirve para probar lo que solo existe en lo publicado: los nombres con huella, el
// <base href> en la raíz y la política de seguridad (ver politica-de-seguridad.mjs).
//
//   node lib/montar-paquete.mjs        -> deja el paquete en e2e/.paquete (fuera de git)
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const AQUI = path.dirname(fileURLToPath(import.meta.url));
export const RAIZ_REPO = path.resolve(AQUI, '..', '..', '..', '..', '..');
const TRABAJO = path.resolve(AQUI, '..', '.paquete');

// -> la carpeta web del paquete montado. Lanza si algún paso falla.
export function montarPaquete({ decir = console.log } = {}) {
  const publicado = path.join(TRABAJO, 'publicado');
  const paquete = path.join(TRABAJO, 'paquete');
  fs.rmSync(TRABAJO, { recursive: true, force: true });
  fs.mkdirSync(paquete, { recursive: true });

  decir('  dotnet publish Triker.Web (tarda un minuto)...');
  execFileSync('dotnet', ['publish', path.join(RAIZ_REPO, 'Triker.Web'), '-c', 'Release', '-o', publicado, '--nologo'], { stdio: 'pipe' });
  fs.cpSync(path.join(publicado, 'wwwroot'), path.join(paquete, 'web'), { recursive: true });

  const { pasos = [] } = JSON.parse(fs.readFileSync(path.join(RAIZ_REPO, 'srv.json'), 'utf8'));
  for (const paso of pasos) {
    decir(`  > ${paso.programa} ${paso.argumentos.join(' ')}`);
    execFileSync(paso.programa, paso.argumentos, {
      cwd: RAIZ_REPO, stdio: 'pipe',
      env: { ...process.env, SRV_PAQUETE: paquete, SRV_APP: 'triker', SRV_RELEASE: 'prueba-local' },
    });
  }
  return path.join(paquete, 'web');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const web = montarPaquete();
  const n = fs.readdirSync(web, { recursive: true }).filter((f) => fs.statSync(path.join(web, f)).isFile()).length;
  console.log(`paquete: ${web} (${n} ficheros)`);
}
