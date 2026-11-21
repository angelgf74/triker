// Lo que tiene que pasar antes de que arranque Blazor. Se carga al principio de
// <body>, sin async ni defer. Estaba escrito dentro de index.html; va en un
// fichero porque la politica de seguridad (CSP, deploy/csp.mjs) no deja
// ejecutar scripts en linea.

// Antes de pintar nada: si no, las cartas aparecen sin los codigos de color y
// se los ve aparecer al arrancar Blazor. Ver AccessibilityService.
if (localStorage.getItem('triker_colorblind') === '1') {
    document.body.classList.add('colorblind');
}

// Las cartas son role="button" (CardView): Espacio las selecciona y no debe
// hacer scroll. Blazor no puede cancelar solo una tecla.
document.addEventListener('keydown', function (e) {
    if (e.key === ' ' && e.target && e.target.getAttribute && e.target.getAttribute('role') === 'button') {
        e.preventDefault();
    }
});

if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js');
}
