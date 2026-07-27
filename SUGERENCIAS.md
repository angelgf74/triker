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
- [x] Animación de reparto — animar la llegada de cartas al inicio de cada ronda
- [x] Sonidos — efectos sutiles (clic al seleccionar, fanfarria al ganar)
- [x] Indicador de turno en lista de jugadores — resaltar visualmente quién tiene el turno activo

### Técnica
- [x] Persistencia entre reinicios — guardar partidas activas en SQLite/Redis para sobrevivir deploys
- [ ] Límite de jugadores configurable — exponer el máximo de jugadores como opción al crear partida
- [x] PWA / instalable — manifest + service worker para instalar como app nativa en móvil

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

---

## Lista 4

> Detectadas jugando partidas reales a viewport de móvil (393×873, el de la app Android).

### Presentación / Pulido
- [ ] Rivales compactos — con 8 jugadores la partida ocupa 1656 px en una ventana de 873, y cada rival muestra 5 cartas boca abajo idénticas que no aportan nada. Colapsarlos a una fila (nombre · nº de cartas · turno/descarte) y reservar las cartas grandes para el reveal
- [ ] La barra de reacciones tapa contenido — `.reaction-toolbar` es `position: sticky; bottom` (`app.css:846`); con la página larga flota sobre la lista de jugadores y oculta nombres
- [x] Tu resultado sin scroll — la pantalla de resultados con 8 jugadores mide 1905 px (≈2,2 pantallas) y tu entrada queda hacia la mitad: había que bajar más de una pantalla solo para saber cómo te había ido. Resuelto con una tarjeta de resumen arriba (posición, jugada, puntos y quién ganó)
- [x] Decidir después de ver — el bloque "¿Quieres jugar otra partida?" está encima de la lista de resultados: se pregunta antes de mostrar cómo ha ido la ronda
- [ ] Cabecera responsive — un título de dos palabras ya envuelve y empuja el botón "?" a una fila propia; en la sala de espera muestra "Ronda 1" y "0 cartas", que ahí no significan nada
- [ ] Etiqueta de descartes — los badges "−3" / "−2" son cartas descartadas, pero en un juego de puntos se leen como penalización
- [ ] Copy del botón de pasar — la ayuda dice "pulsa *Descartar* sin seleccionar nada para pasar" y el botón real dice "Pasar (sin descartar)"

### Jugabilidad / Profundidad
- [ ] Jerarquía visible en resultados — "Doble Pareja Mixta" gana a "Doble Pareja" y nada en pantalla lo explica; la escala de 16 jugadas solo vive dentro del modal de ayuda
- [ ] Empates explicados — un reparto normal deja dos 🥇, ningún 🥈, dos 🥉 y tres 6º: es ranking de competición correcto, pero desconcierta sin una nota que lo aclare
- [ ] Aviso de turno agotado — `HandleTurnTimeout` hace auto-pass en silencio (`InactivityService:85`): el turno se acaba solo y el jugador no se entera de qué ha pasado
- [ ] Meta visible en el lobby — las tarjetas muestran jugadores y estado, pero no si la partida es "a 3 victorias" o "sin límite"; te unes a ciegas a algo que puede no acabar nunca

### Social / Multijugador
- [ ] Copiar el código privado — se muestra con un "Compártelo con tus amigos" pero no se puede copiar: hay que leer los dígitos y teclearlos en otra app
- [ ] Salida de la sala de espera en solitario — creas partida, te quedas solo y la única acción posible es añadir bots; no hay ninguna forma de invitar a nadie
- [ ] Espectador con información — hoy solo se ven cartas boca abajo hasta el reveal: darle el panel de estadísticas de sesión y el historial de rondas, que ya están construidos
