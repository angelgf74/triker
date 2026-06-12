# Sugerencias de mejora — Triker

## Lista 1 (completada)

### Jugabilidad / UX
- [x] Contador de tiempo visible
- [x] Explicación del resultado
- [x] Feedback de descarte
- [x] Reconexión automática (SignalR)

### Funcionalidades del juego
- [x] Bot con más estrategia (4 personalidades)
- [x] Estadísticas de sesión
- [x] Lobby en tiempo real

### Robustez técnica
- [x] Memory leak en partidas (GameCleanupService)
- [x] Race condition en descartes (SyncLock)
- [x] Logging de eventos críticos

### Pulido / accesibilidad
- [x] Responsive en resultados
- [x] Ejemplos visuales en la ayuda
- [x] Nombre persistente (localStorage)

---

## Lista 2

### Social / Multijugador
- [x] Chat en partida — reacciones o chat simple para comunicarse con los demás jugadores
- [x] Partidas privadas — sala con código/contraseña para jugar solo con amigos
- [x] Modo espectador — unirse a una partida en curso para observar sin jugar

### Jugabilidad / Profundidad
- [x] Partida a X victorias — configurar "primero a N victorias" o "N rondas" como fin de sesión
- [x] Vista de descartes — mostrar cuántas cartas descartó cada jugador en su turno
- [x] Historial de ronda — poder revisar el resultado de la ronda anterior al empezar una nueva

### Presentación / Pulido
- [ ] Animación de reparto — animar la llegada de cartas al inicio de cada ronda
- [x] Sonidos — efectos sutiles (clic al seleccionar, fanfarria al ganar)
- [x] Indicador de turno en lista de jugadores — resaltar visualmente quién tiene el turno activo

### Técnica
- [ ] Persistencia entre reinicios — guardar partidas activas en SQLite/Redis para sobrevivir deploys
- [ ] Límite de jugadores configurable — exponer el máximo de jugadores como opción al crear partida
- [ ] PWA / instalable — manifest + service worker para instalar como app nativa en móvil

---

## Lista 3

### Social / Multijugador
- [ ] Invitación por enlace directo — URL compartible que abra una partida privada con el código pre-rellenado
- [ ] Jugadores recientes — recordar con quién has jugado para volver a invitarles fácilmente
- [ ] Ranking global — tabla de victorias acumuladas entre sesiones (requiere persistencia)

### Jugabilidad / Profundidad
- [ ] Dificultad del bot — Fácil/Normal/Difícil al añadirlo, además de la personalidad
- [ ] Modo rápido 1v1 — partida corta con menos cartas para una jugada veloz
- [ ] Logros desbloqueables — primera victoria, escalera real, racha de 3 victorias, etc.

### Presentación / Pulido
- [ ] Animación de descarte — las cartas seleccionadas vuelan al mazo de descartes al confirmar
- [ ] Animación de revelación — flip 3D al mostrar las manos en la pantalla de resultados
- [ ] Avatar con iniciales y color — identidad visual por jugador en lobby y partida

### Accesibilidad
- [ ] Atajos de teclado — números 1-5 para seleccionar cartas, Enter para descartar, Esc para deseleccionar
- [ ] Modo daltónico — añadir patrones/texturas a las cartas además del color
- [ ] Lector de pantalla — labels ARIA en componentes interactivos clave

### Técnica
- [ ] Tests automatizados — al menos `ScoringService` y `BotService` (no hay ninguno hoy)
- [ ] Telemetría básica — métricas anónimas de partidas activas y jugadores conectados
- [ ] Logs estructurados — sink JSON/remoto para producción en lugar de stdout plano
