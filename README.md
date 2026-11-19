# Aumentos LoL

Aumentos LoL muestra recomendaciones de **aumentos**, **objetos** y **hechizos de invocador** dentro de League of Legends. Detecta tu campeón a través del cliente local y reconoce los títulos de los aumentos en español. El catálogo incluido corresponde al parche **26.23** (`16.23` en el cliente y en OP.GG).

| Función | ARAM Caos | Arena |
| --- | --- | --- |
| Comparación de aumentos en partida | OP.GG, estimación local y ARAMMayhem.com | OP.GG, estimación local y GGEZ.GG |
| Builds de OP.GG | Al abrir la tienda con **P** | Al abrir la tienda con **P** |
| Hechizos de OP.GG | En la selección de campeón | — |

## Iniciar en Windows

1. Abre `dist/win-x64/Iniciar.cmd`. La carpeta contiene la app, el runtime y el modelo de español. También incluye `AumentosLoL.exe` para el inicio directo donde la política de Windows lo permita.
2. Usa League en **ventana sin bordes** y déjalo en primer plano cuando quieras ver los paneles. Desde el icono de **Aumentos LoL** en la bandeja puedes abrir **Diagnóstico y calibración**, **Pausar overlay** o salir.

### Calibrar la lectura de aumentos

Si el diagnóstico pide calibración, abre una captura de la **ventana completa del juego** o pulsa **Capturar juego** y vuelve a League con el panel de cartas abierto. Elige el modo y el número de cartas. Marca un detalle fijo del panel, como el botón azul inferior, y luego una franja por título que cubra también nombres largos. Pulsa **Probar lectura** y **Guardar calibración**.

Se guarda una calibración por modo, tamaño de ventana y número de cartas. Se incluyen las dos referencias de tu PC, **ARAM Caos con cuatro cartas** y **Arena con tres cartas**, y una posición inicial calculada para **ARAM Caos con tres cartas**. La app ajusta la posición en ventanas 16:9 y también cuando el tamaño difiere hasta un 3 % de la referencia. Puedes guardar una calibración exacta o agregar otra distribución desde el diagnóstico.

## Comparar aumentos en partida

**OP.GG** muestra la letra que publica para tu campeón en ARAM Caos (S, A, B, C, D o E). En Arena la carta encabeza con su **tier local** (S+, A, B o C) si tiene una regla de sinergia. Debajo aparecen los porcentajes de elección que publiquen OP.GG y GGEZ.GG. En el caso de **¡¡¡Abejas!!!** con Lux, la estimación local es **A**.

| Etiqueta | Significado en el overlay |
| --- | --- |
| S / S+ | Estimación local para una sinergia excepcional. En Arena se muestra S+ |
| A | Estimación local para una mecánica afín al perfil del campeón |
| B | Estimación local intermedia indicada en la tabla editorial |
| C | Estimación local para una mecánica con poca afinidad con el perfil |
| Sin dato | La carta se reconoció y esa fuente carece de una valoración disponible para el campeón, modo y parche |
| Variable | La utilidad depende de una condición de la partida. El overlay muestra cuál |
| ? | La app todavía está intentando identificar la carta |

Al cambiar una carta, el overlay vuelve a leer esa posición y actualiza sus valoraciones. Si dos cartas comparten título, la app también puede consultar la descripción. Para **¡Estadísticas sobre Estadísticas!**, lee cuántos yunques ofrece para distinguir la versión de tres de la de cuatro.

Al detectar el modo y el campeón, la app carga en segundo plano la tabla de su página pública de OP.GG. Mientras llega por primera vez, aparece «Consultando…» junto a la estimación local. Las imágenes del juego se procesan en memoria. La consulta externa contiene el modo y el campeón. Cada cambio de carta usa la tabla que ya está en memoria.

La copia por parche, modo y campeón se guarda en `%LOCALAPPDATA%\AumentosLoL\opgg` y se actualiza tras 12 horas de antigüedad cuando vuelves a usar ese campeón. En Arena, si OP.GG solo publica el parche inmediatamente anterior de la misma temporada, el overlay identifica el parche junto al porcentaje y vuelve a consultar a los 15 minutos. Si falla la red, la app puede seguir usando su copia. El diagnóstico indica cuándo se actualizó o por qué falta. [Datos de aumentos de OP.GG](https://op.gg/lol/modes/aram-mayhem/lux/augments) · [Uso de los datos de OP.GG](https://help.op.gg/hc/en-us/articles/31091405109401-Can-I-use-OP-GG-data)

En **Arena**, **GGEZ.GG** muestra el porcentaje de elección del aumento con ese campeón y el número de partidas que lo respalda, por ejemplo «59/760 partidas · 26.23». La muestra abarca todas las regiones que recoge la fuente. Se usa el parche vigente o el inmediatamente anterior de la misma temporada con la marca «parche ant.». El menú **Datos de GGEZ.GG** abre la página del último campeón de Arena que detectó la app. [Ejemplo en Arena de Elise](https://ggez.gg/champion/Elise/arena) · [Licencia de GGEZ.GG · CC BY 4.0](https://ggez.gg/about#using-our-numbers)

Al elegir un **objeto legendario o prismático de Arena**, o un **fragmento del yunque de estadísticas**, cada carta reconocida muestra un tier local y el motivo de su afinidad con el campeón. Durante una oferta prismática, un panel lateral muestra los **cinco prismáticos con mayor afinidad** y señala cuáles están en oferta. Si el overlay identifica todas las cartas y hay una opción claramente superior, la destaca. Las cartas de aumentos de Arena muestran su tier local y los porcentajes que publica cada fuente, con su parche.

En **ARAM Caos**, **ARAMMayhem.com** muestra el porcentaje de elección global de la carta en la muestra de esa fuente. La app prioriza el archivo del parche actual y, si aún falta, usa el inmediatamente anterior de la misma temporada. En ese caso, debajo del porcentaje aparece «Parche anterior» con el número. La fuente se consulta en segundo plano y se comparte entre todos los campeones. La app vuelve a comprobar los archivos anteriores cada 15 minutos y los del parche actual cada 12 horas mientras se usa el modo. [Datos de ARAMMayhem.com · CC BY 4.0](https://arammayhem.com/data/)

## Builds de OP.GG en partida

Pulsa **P**, la tecla predeterminada de la tienda, en **ARAM Caos** o **Arena**. La build aparece automáticamente a un lado de la tienda y se oculta al cerrarla. El panel también se aparta durante la elección de aumentos.

Lee los **iconos y las flechas de izquierda a derecha** para seguir el orden de compra. Debajo de cada icono aparece el nombre del objeto en español. El panel incluye objetos iniciales, botas, la build principal y su alternativa cuando OP.GG la publica. En Arena también muestra los objetos prismáticos disponibles. [Build de Zyra en ARAM Caos](https://op.gg/es-419/lol/modes/aram-mayhem/zyra/build) · [Build de Elise en Arena](https://op.gg/es-419/lol/modes/arena/elise/build)

En **ARAM Caos**, la **Ruta a 6 objetos** cuenta las botas mejoradas y los demás objetos terminados de tu inventario. Parte del núcleo de OP.GG y sugiere cómo cubrir los espacios libres según la composición y las compras que reporta el marcador del cliente local. Debajo de cada objeto situacional aparece el motivo. La app actualiza la ruta con los cambios de la partida y consulta los datos de los objetos del mismo parche en Data Dragon. [API local de Riot](https://developer.riotgames.com/docs/lol#live-client-data-api)

Para consultar la build manualmente, pulsa **Ctrl + Alt + B** o usa **Mostrar u ocultar build de OP.GG** en la bandeja y vuelve al juego. Dentro de la tienda, ese control permite ocultar el panel hasta la siguiente apertura. Usa **Abrir build de OP.GG** para visitar la página del último campeón consultado.

La app guarda una copia de las recomendaciones por campeón, modo y parche en `%LOCALAPPDATA%\AumentosLoL\opgg-builds`. Cuando muestra datos del parche inmediatamente anterior de la misma temporada, el panel indica **Parche anterior** y su número. Los iconos se descargan de OP.GG y se reutilizan en memoria durante la sesión. Los nombres aparecen mientras se cargan las imágenes.

Puedes comprobar las recomendaciones desde una terminal con `dist\win-x64\Iniciar.cmd --verificar-build Caos Kennen` o `dist\win-x64\Iniciar.cmd --verificar-build Arena Elise`.

## Hechizos de invocador antes de la partida

En la selección de campeón de **ARAM Caos**, busca el panel de OP.GG abajo a la derecha. Se actualiza cuando cambias de campeón y se retira al salir de esa pantalla. Muestra la combinación principal y una alternativa cuando OP.GG publica otra. Si ya tienes los dos hechizos recomendados, aparece **Ya tienes estos hechizos**, independientemente de cuál esté en **D** o en **F**. La selección de los hechizos se hace en el cliente de League.

## Historial de aumentos

Abre **Abrir historial de aumentos** desde el icono de la bandeja. El archivo CSV se guarda en `%LOCALAPPDATA%\AumentosLoL\historial-aumentos.csv` y se puede abrir en Excel o cualquier lector de CSV.

Cada fila guarda la carta ofrecida, su posición, el campeón, el modo, el parche y la fecha. La columna `tier` contiene la estimación local y `opgg` contiene la valoración o popularidad mostrada. Las columnas `arammayhem_popularidad_global_pct` y `arammayhem_parche` guardan el porcentaje de ARAMMayhem.com y el parche de sus datos. Las columnas `ggez_*` guardan el porcentaje de elección, cuántas veces se eligió el aumento, el tamaño de la muestra y el parche de GGEZ.GG. Las filas previas a cada integración llevan sus columnas nuevas vacías. Una carta nueva o una valoración que cambie en pantalla se registra en la misma selección. Las filas marcadas como **Captura** contienen los valores recalculados con los datos vigentes al importarla.

Para comprobar una imagen con un campeón concreto, ejecuta `dist\win-x64\Iniciar.cmd --verificar Caos "ruta\captura.png" Lux`. Para agregar sus cartas al historial como captura, usa `dist\win-x64\Iniciar.cmd --registrar-capturas Caos Lux "ruta\captura.png"`. En Arena usa `Arena` y la clave del campeón correspondiente.

## Construir y actualizar

Con el SDK de .NET 10, ejecuta `powershell -File scripts/Publicar.ps1`. El script descarga el modelo español si falta y publica en `dist/win-x64`.

Para actualizar el catálogo, ejecuta `powershell -File scripts/Get-Assets.ps1 -Patch 16.23 -ChampionVersion 16.23.1` con las versiones nuevas. Revisa los cambios de mecánicas y las excepciones en `src/Aumentos.App/Assets/ratings.json`, corre las pruebas y vuelve a publicar. El overlay se suspende si detecta que el parche del juego difiere del catálogo.

Ejecuta `dotnet build AumentosLoL.slnx -c Release` y luego `dotnet tests/Aumentos.Tests/bin/Release/net10.0-windows10.0.19041.0/Aumentos.Tests.dll`. La suite verifica la comparación de fuentes, el historial, el contenido de las builds y la lectura de los datos de selección de campeón. Puedes agregar estas opciones al segundo comando para comprobar la detección y los paneles.

- `--caos evidencias/captura.png --arena evidencias/image.png --items evidencias/image2.png` reproduce los paneles de referencia de aumentos y de objetos.
- `--shop-shot "ruta\tienda.png" --shop-closed-shot "ruta\partida.png"` comprueba la detección con la tienda abierta y cerrada.
- `--capture-smoke` verifica la captura de Windows y los paneles de aumentos, builds con iconos y hechizos.
- `--live` comprueba la API local durante una partida de ARAM Caos o Arena.

[Riot Developer Portal](https://developer.riotgames.com/docs/lol) · [CommunityDragon](https://communitydragon.org/docs/assets)

Aumentos LoL no cuenta con el respaldo de Riot Games ni representa sus opiniones. Riot Games y sus propiedades asociadas son marcas de Riot Games, Inc.
