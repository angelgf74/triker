// Paso propio de srv (pasos en srv.json). En desarrollo la web se sirve en /triker/
// (dotnet run --pathbase=/triker, skill run-triker) y en el servidor en la raíz de
// triker-app.angelgf.com.es: cambia el <base href> del index.html del paquete.
// Falla si no lo encuentra justo una vez: mejor no publicar que publicar una web que no carga.
import fs from 'node:fs';
import path from 'node:path';

const paquete = process.env.SRV_PAQUETE;
if (!paquete) {
    console.error('falta SRV_PAQUETE: este paso lo lanza srv subir');
    process.exit(1);
}

const index = path.join(paquete, 'web', 'index.html');
const antes = '<base href="/triker/" />';
const despues = '<base href="/" />';

const html = fs.readFileSync(index, 'utf8');
const veces = html.split(antes).length - 1;
if (veces !== 1) {
    console.error(`${index}: se esperaba ${antes} una vez y aparece ${veces}`);
    process.exit(1);
}
fs.writeFileSync(index, html.replace(antes, despues));

// Las copias comprimidas llevarían el base viejo: fuera, para que nada sirva la web rota.
for (const copia of [`${index}.br`, `${index}.gz`]) fs.rmSync(copia, { force: true });

console.log(`base href: /triker/ -> / en ${index}`);
