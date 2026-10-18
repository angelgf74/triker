# -*- coding: utf-8 -*-
"""Captura pantallas reales de la app web (lo mismo que muestra la app Android,
sin anuncios) a tamano telefono, para la ficha de Google Play.
Usa el Chrome instalado (channel='chrome'), no descarga Chromium."""

import os
import time
from playwright.sync_api import sync_playwright

URL = "https://triker-app.angelgf.com.es/"
OUT = os.path.join(os.path.dirname(__file__), "screenshots")
os.makedirs(OUT, exist_ok=True)

# 360x700 CSS * dsf 3 = 1080x2100 (ratio 1.94, dentro del limite 2:1 de Play)
VW, VH, DSF = 360, 700, 3


def shot(page, name):
    path = os.path.join(OUT, name)
    page.screenshot(path=path, full_page=False)
    print("  ->", name)


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="chrome", headless=True)
        ctx = browser.new_context(
            viewport={"width": VW, "height": VH},
            device_scale_factor=DSF,
            is_mobile=True,
            locale="es-ES",
        )
        page = ctx.new_page()
        print("Cargando", URL)
        page.goto(URL, wait_until="domcontentloaded", timeout=60000)

        # Esperar a que Blazor arranque y muestre el lobby
        page.wait_for_selector("input.name-input", timeout=90000)
        time.sleep(1.5)

        # Nombre de jugador
        page.fill("input.name-input", "Triker")
        time.sleep(0.6)
        shot(page, "01_lobby.png")

        # Reglas (modal de ayuda)
        try:
            page.click("button.btn-help", timeout=5000)
            time.sleep(1.0)
            shot(page, "02_reglas.png")
            # cerrar modal
            for sel in ["button:has-text('Cerrar')", ".modal-close", "button:has-text('✕')", "button:has-text('×')"]:
                try:
                    page.click(sel, timeout=1500)
                    break
                except Exception:
                    pass
            page.keyboard.press("Escape")
            time.sleep(0.5)
        except Exception as e:
            print("  (reglas omitidas:", e, ")")

        # Crear partida
        page.click("button:has-text('Nueva partida')", timeout=8000)
        time.sleep(0.8)
        page.click(".create-form button:has-text('Crear')", timeout=8000)

        # Sala de espera
        page.wait_for_url("**/game/**", timeout=20000)
        page.wait_for_selector("button:has-text('Añadir bot')", timeout=20000)
        time.sleep(1.0)

        # Añadir 3 bots
        for _ in range(3):
            try:
                page.click("button:has-text('Añadir bot')", timeout=4000)
                time.sleep(0.6)
            except Exception:
                break
        time.sleep(0.6)
        shot(page, "03_sala_espera.png")

        # Iniciar partida
        page.click("button:has-text('Iniciar partida')", timeout=8000)
        page.wait_for_selector(".hand-view, .my-area", timeout=20000)
        time.sleep(2.0)
        shot(page, "04_partida.png")

        # Intentar llegar a resultados: pasar varias rondas
        got_results = False
        for attempt in range(8):
            try:
                btn = page.locator("button.btn-discard")
                if btn.count() > 0 and btn.first.is_visible():
                    btn.first.click(timeout=3000)
            except Exception:
                pass
            time.sleep(2.5)
            try:
                if page.locator("text=Resultados").count() > 0:
                    got_results = True
                    break
            except Exception:
                pass

        if got_results:
            time.sleep(1.0)
            shot(page, "05_resultados.png")
        else:
            print("  (no se alcanzaron resultados automaticamente)")

        ctx.close()
        browser.close()
        print("Listo.")


if __name__ == "__main__":
    main()
