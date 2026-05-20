// Production service worker — caches all static assets for offline play.
// On publish, the build system replaces service-worker.js with this file.

self.importScripts('./service-worker-assets.js');

self.addEventListener('install',  event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch',    event => { if (shouldHandle(event)) event.respondWith(onFetch(event)); });

const CACHE_PREFIX   = 'triker-';
const cacheName      = `${CACHE_PREFIX}${self.assetsManifest.version}`;

// File types to pre-cache (Blazor WASM + app assets)
const INCLUDE = [
    /\.dll$/, /\.wasm/, /\.pdb$/,
    /\.js$/, /\.css$/, /\.json$/,
    /\.html$/, /\.svg$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/,
    /\.woff2?$/, /\.blat$/, /\.dat$/,
];
const EXCLUDE = [/^service-worker\.js$/];

// Base path of the app (must match <base href>)
const BASE      = '/triker/';
const baseUrl   = new URL(BASE, self.origin);
const relPath   = url => new URL(url, self.origin).pathname.replace(baseUrl.pathname, '/');

// --- lifecycle -----------------------------------------------------------

async function onInstall() {
    console.info('[SW] Install — cache:', cacheName);

    const requests = self.assetsManifest.assets
        .filter(a => INCLUDE.some(p => p.test(a.url)))
        .filter(a => !EXCLUDE.some(p => p.test(a.url)))
        .map(a => new Request(a.url, { integrity: a.hash, cache: 'no-cache' }));

    const cache = await caches.open(cacheName);
    await cache.addAll(requests);

    // Activate immediately — no need to wait for old tabs to close
    self.skipWaiting();
}

async function onActivate() {
    console.info('[SW] Activate — removing old caches');

    const keys = await caches.keys();
    await Promise.all(
        keys
            .filter(k => k.startsWith(CACHE_PREFIX) && k !== cacheName)
            .map(k => caches.delete(k))
    );

    // Take control of all open clients immediately
    await self.clients.claim();
}

// --- fetch ---------------------------------------------------------------

// CSS and HTML use network-first so deployments are always visible.
const NETWORK_FIRST = [/\.css$/, /\.html$/];

function shouldHandle(event) {
    // Only GET requests on same origin (SignalR / API are cross-origin → not intercepted)
    return event.request.method === 'GET'
        && new URL(event.request.url).origin === self.origin;
}

async function onFetch(event) {
    const cache = await caches.open(cacheName);

    // Navigation (index.html) → network-first, cached fallback for offline
    if (event.request.mode === 'navigate') {
        try {
            const response = await fetch(event.request);
            await cache.put(BASE + 'index.html', response.clone());
            return response;
        } catch {
            return await cache.match(BASE + 'index.html') ?? Response.error();
        }
    }

    const pathname = new URL(event.request.url).pathname;

    // CSS → network-first so style changes are immediately visible
    if (NETWORK_FIRST.some(p => p.test(pathname))) {
        try {
            const response = await fetch(event.request);
            await cache.put(event.request, response.clone());
            return response;
        } catch {
            return await cache.match(event.request) ?? Response.error();
        }
    }

    // Immutable assets (DLLs, WASM, JS) → cache-first for performance
    return await cache.match(event.request) ?? fetch(event.request);
}
