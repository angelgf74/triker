# Recursos para la ficha de Google Play — Triker

Todo lo necesario para publicar **Triker** en Google Play, generado a partir de la
identidad real de la app (colores, logo y pantallas).

## Contenido

```
playstore/
├─ ficha-google-play.md      Textos: título, descripciones, novedades, categoría…
├─ graphics/
│  ├─ icon-512.png           Icono de la app (512×512)
│  └─ feature-graphic.png    Gráfico de función (1024×500)
├─ screenshots/
│  ├─ 01_lobby.png           Pantalla de inicio
│  ├─ 02_reglas.png          Cómo se juega (reglas)
│  ├─ 03_sala_espera.png     Sala de espera con bots
│  ├─ 04_partida.png         Partida en curso (la mano)
│  └─ 05_resultados.png      Resultados de la ronda
├─ generate_graphics.py      Regenera icono y gráfico de función (Pillow)
└─ capture_screenshots.py    Regenera las capturas (Playwright + Chrome)
```

## Requisitos gráficos de Google Play (cumplidos)

| Recurso | Requisito Play | Este paquete |
|---|---|---|
| Icono | 512×512 PNG 32-bit | ✅ `icon-512.png` |
| Gráfico de función | 1024×500 PNG/JPG 24-bit | ✅ `feature-graphic.png` |
| Capturas de teléfono | 2–8, lado 320–3840 px, ratio ≤ 2:1 | ✅ 5 × 1080×2100 (ratio 1,94) |

> Las capturas se toman de la web (idéntico a lo que muestra la app) **sin anuncios**,
> que es lo recomendado para la ficha. En el dispositivo, MIUI/HyperOS renderiza el
> WebView en blanco con `adb screencap`, por eso se generan con navegador.

## Regenerar recursos

```bash
# Icono + gráfico de función
python playstore/generate_graphics.py

# Capturas (usa el Chrome instalado, no descarga Chromium)
python playstore/capture_screenshots.py
```

## PENDIENTE antes de publicar (no es generable automáticamente)

1. **Política de privacidad (URL obligatoria).** Al usar AdMob, Play exige una URL
   pública de política de privacidad. Hay que crear la página (puede ir en
   `triker-app.angelgf.com.es/privacidad`) y declararla en la ficha.
2. **Sección "Seguridad de los datos".** Declara que el SDK de AdMob recopila
   identificadores del dispositivo y datos de uso con fines publicitarios.
3. **Casilla "Contiene anuncios".** Márcala en la ficha y en el cuestionario IARC.
4. **APK/AAB firmado.** Para Play hay que subir un **Android App Bundle** firmado:
   `cd android && ./gradlew :app:bundleRelease` (configura antes el keystore de release).
5. **Sustituir el ID de intersticial** por uno de formato **Intersticial** real en
   `android/app/src/main/res/values/strings.xml` (el actual es un banner).
```
