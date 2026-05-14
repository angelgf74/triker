# TRIKER — Juego de Cartas

## Estructura

```
Triker.slnx
├── Triker.Shared/   — Modelos y DTOs compartidos (net10.0 classlib)
├── Triker.Api/      — API REST + SignalR (ASP.NET Core net10.0)
└── Triker.Web/      — Frontend Blazor WebAssembly (net10.0)
```

## Arrancar en desarrollo

**Terminal 1 — API:**
```bash
dotnet run --project Triker.Api --launch-profile https
# → https://localhost:7077
```

**Terminal 2 — Web:**
```bash
dotnet run --project Triker.Web --launch-profile https
# → https://localhost:7164
```

Abrir el navegador en `https://localhost:7164`.

## Reglas rápidas

- Cada carta tiene **Letra** (T,R,I,K,E), **Figura** (●■★▲♥♣) y **Color** (Rojo,Verde,Azul,Negro,Magenta,Cyan)
- Se reparten **5 cartas** por jugador
- En tu turno: selecciona cartas a descartar (0 a todas) y pulsa el botón
- Al completarse todos los turnos se revelan todas las manos
- Se forman **grupos** de cartas que comparten el mismo parámetro
- Puntuación: `(N-1)² × 2^A`  donde N = cartas del grupo, A = parámetros coincidentes
- Nombres: Pareja (2), Trío (3), Cuarteto (4), Triker (5), Escalera (ningún grupo)
- Sufijos: Simple (1 parámetro), Doble (2 parámetros), Real (Escalera perfecta)
