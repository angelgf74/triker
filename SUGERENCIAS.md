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
- [ ] Chat en partida — reacciones o chat simple para comunicarse con los demás jugadores
- [ ] Partidas privadas — sala con código/contraseña para jugar solo con amigos
- [ ] Modo espectador — unirse a una partida en curso para observar sin jugar

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
