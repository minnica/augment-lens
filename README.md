# Aumentos LoL

Aumentos LoL muestra recomendaciones de **aumentos**, **objetos**, **campeones de Arena** y **hechizos de invocador** en League of Legends. Detecta tu campeón a través del cliente local y reconoce los títulos de los aumentos en español. El catálogo incluido corresponde al parche **26.23** (`16.23` en el cliente y en OP.GG).

| Función | ARAM Caos | Arena |
| --- | --- | --- |
| Comparación de aumentos en partida | OP.GG, estimación local y ARAMMayhem.com | Tier local por campeón, objetos y nivel con mecánicas de LoL Wiki. Elección en OP.GG y GGEZ.GG |
| Builds de OP.GG | Al abrir la tienda con **P** | Al abrir la tienda con **P** |
| Hechizos de OP.GG | En la selección de campeón | — |
| Tiers de campeones de GGEZ.GG | — | En la selección de campeón |

## Iniciar en Windows

1. Después de iniciar sesión en Windows, abre manualmente `dist/win-x64-wiki/Iniciar.cmd` para usar la versión con la Wiki de este equipo. Si vuelves a publicar con el destino predeterminado, abre `dist/win-x64/Iniciar.cmd`. Cada paquete incluye la app, el runtime y el modelo de español.
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
| B | Estimación local intermedia para la sinergia evaluada |
| C | Estimación local para una mecánica con poca afinidad con el perfil |
| Sin dato | La carta se reconoció y esa fuente carece de una valoración disponible para el campeón, modo y parche |
| Variable | La utilidad depende de una condición de la partida. El overlay muestra cuál |
| ? | La app todavía está intentando identificar la carta |

Al cambiar una carta, el overlay vuelve a leer esa posición y actualiza sus valoraciones. Si dos cartas comparten título, la app también puede consultar la descripción. En **ARAM Caos**, para **¡Estadísticas sobre Estadísticas!**, lee cuántos yunques ofrece para distinguir la versión de tres de la de cuatro.

Al detectar el modo y el campeón, la app carga en segundo plano la tabla de su página pública de OP.GG. Mientras llega por primera vez, aparece «Consultando…» junto a la estimación local. Las imágenes del juego se procesan en memoria. La consulta externa contiene el modo y el campeón. Cada cambio de carta usa la tabla que ya está en memoria.

La copia por parche, modo y campeón se guarda en `%LOCALAPPDATA%\AumentosLoL\opgg` y se actualiza tras 12 horas de antigüedad cuando vuelves a usar ese campeón. En Arena, si OP.GG solo publica el parche inmediatamente anterior de la misma temporada, el overlay identifica el parche junto al porcentaje y vuelve a consultar a los 15 minutos. Si falla la red, la app puede seguir usando su copia. El diagnóstico indica cuándo se actualizó o por qué falta. [Datos de aumentos de OP.GG](https://op.gg/lol/modes/aram-mayhem/lux/augments) · [Uso de los datos de OP.GG](https://help.op.gg/hc/en-us/articles/31091405109401-Can-I-use-OP-GG-data)

En **Arena**, **GGEZ.GG** muestra el porcentaje de elección del aumento con ese campeón y el número de partidas que lo respalda, por ejemplo «59/760 partidas · 26.23». La muestra abarca todas las regiones que recoge la fuente. Se usa el parche vigente o el inmediatamente anterior de la misma temporada con la marca «parche ant.». El menú **Datos de GGEZ.GG** abre la página del último campeón de Arena que detectó la app. [Ejemplo en Arena de Elise](https://ggez.gg/champion/Elise/arena) · [Licencia de GGEZ.GG · CC BY 4.0](https://ggez.gg/about#using-our-numbers)

**League of Legends Wiki** aporta las mecánicas de cartas de Arena. Cuando una mecánica ayuda a valorar tu oferta, aparece **Mecánica de LoL Wiki**. La app calcula la letra a partir de esa mecánica, las características de tu campeón y los objetos que el cliente confirma en tu inventario. Con Jax y la misma oferta, **Goliat** puede quedar primero si llevas Corazón de Acero y Égida de Fuego Solar. Con Espada del Rey Arruinado y Espadafuria de Guinsoo puede quedar primero **Tifón**, porque aplica sus efectos al impacto a un segundo rival. Cuando las dos opciones tienen igual respaldo, ambas conservan su letra. La comparación usa la copia de la Wiki incluida en la app. El menú **Mecánicas de League of Legends Wiki** abre la revisión que se usó. [Módulo de los colaboradores de League of Legends Wiki, revisión 4070515](https://wiki.leagueoflegends.com/en-us/Special:PermanentLink/4070515) · [Datos de la Wiki adaptados bajo CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/)

**Nv. 2** y **Nv. 3** corresponden a la mejora ofrecida en esa posición. Si hay cifras comprobadas para el parche, el panel muestra qué cambia. Con **Hábil** en Jax, pasar de nivel 2 a 3 lleva la velocidad de ataque del 80 % al 130 % y elimina su límite. En algunas mejoras, como **Grandes jugadas Nv. 2** con Elise, la etiqueta **Tier del efecto base** indica que la letra valora la afinidad del efecto inicial.

Al elegir un **objeto legendario o prismático de Arena**, o un **fragmento del yunque de estadísticas**, las cartas valoradas muestran el tier local y su motivo. Durante una oferta prismática, un panel lateral muestra los **cinco prismáticos con mayor afinidad** y esas cartas llevan su posición, como **Top #5**. Si la oferta completa tiene una opción claramente superior, el overlay la destaca.

Las cartas de aumentos de Arena muestran **Recomendado** cuando la comparación de la oferta completa identifica una ganadora. Con Jax sin compras que cambien el desempate, **Bailarín de Tap** gana ante **Goliat** para sostener ataques y **Hábil** gana ante **Tifón** para atacar a un solo objetivo. **¡Enciéndelos! Nv. 2** muestra el cambio de cuatro a cinco fuegos y el modificador de daño del 90 % al 100 %, y puede ganar el empate frente a **Cerbero**. El porcentaje de elección indica la frecuencia observada en la muestra de cada fuente.

En **ARAM Caos**, **ARAMMayhem.com** muestra el porcentaje de elección global de la carta en la muestra de esa fuente. La app prioriza el archivo del parche actual y, si aún falta, usa el inmediatamente anterior de la misma temporada. En ese caso, debajo del porcentaje aparece «Parche anterior» con el número. La fuente se consulta en segundo plano y se comparte entre todos los campeones. La app vuelve a comprobar los archivos anteriores cada 15 minutos y los del parche actual cada 12 horas mientras se usa el modo. [Datos de ARAMMayhem.com · CC BY 4.0](https://arammayhem.com/data/)

## Builds de OP.GG en partida

Pulsa **P**, la tecla predeterminada de la tienda, en **ARAM Caos** o **Arena**. La build aparece automáticamente a un lado de la tienda y se oculta al cerrarla. El panel también se aparta durante la elección de aumentos.

Lee los **iconos y las flechas de izquierda a derecha** para seguir el orden de compra. Debajo de cada icono aparece el nombre del objeto en español. El panel incluye objetos iniciales, botas, la build principal y su alternativa cuando OP.GG la publica. En Arena también muestra los objetos prismáticos disponibles. [Build de Zyra en ARAM Caos](https://op.gg/es-419/lol/modes/aram-mayhem/zyra/build) · [Build de Elise en Arena](https://op.gg/es-419/lol/modes/arena/elise/build)

En **ARAM Caos**, la **Ruta a 6 objetos** cuenta las botas mejoradas y los demás objetos terminados de tu inventario. Parte del núcleo de OP.GG y sugiere cómo cubrir los espacios libres según la composición y las compras que reporta el marcador del cliente local. Debajo de cada objeto situacional aparece el motivo. La app actualiza la ruta con los cambios de la partida y consulta los datos de los objetos del mismo parche en Data Dragon. [API local de Riot](https://developer.riotgames.com/docs/lol#live-client-data-api)

En **Arena**, la ruta cuenta las botas mejoradas, los legendarios y los prismáticos equipados. Destaca la **Siguiente compra**, ordena los pendientes y agrupa los objetos **Comprados**. Antes del primer enfrentamiento que puede confirmar, usa el inventario, el perfil de tu campeón y el núcleo de OP.GG cuando está disponible. Después, puede adaptar la ruta a las compras y al perfil de hasta tres rivales confirmados en rondas jugadas. Los prismáticos de OP.GG sirven como referencia en su propia sección. [API local de Riot](https://developer.riotgames.com/docs/lol#live-client-data-api)

Para consultar la build manualmente, pulsa **Ctrl + Alt + B** o usa **Mostrar u ocultar build de OP.GG** en la bandeja y vuelve al juego. Dentro de la tienda, ese control permite ocultar el panel hasta la siguiente apertura. Usa **Abrir build de OP.GG** para visitar la página del último campeón consultado.

La app guarda una copia de las recomendaciones por campeón, modo y parche en `%LOCALAPPDATA%\AumentosLoL\opgg-builds`. Cuando muestra datos del parche inmediatamente anterior de la misma temporada, el panel indica **Parche anterior** y su número. Los iconos se descargan de OP.GG y se reutilizan en memoria durante la sesión. Los nombres aparecen mientras se cargan las imágenes.

Desde PowerShell, en la raíz del proyecto, puedes consultar la build publicada por OP.GG con `.\dist\win-x64-wiki\Iniciar.cmd --verificar-build Caos Kennen` o `.\dist\win-x64-wiki\Iniciar.cmd --verificar-build Arena Elise`.

## Selección de campeón

En **Arena**, durante la selección en el cliente de League, el panel muestra **Opciones visibles** cuando reconoce la fila de **Valentía**. Ordena los campeones leídos por tier de GGEZ.GG y, dentro de la misma letra, por su frecuencia de terminar entre los primeros tres. **Valentía** es la opción aleatoria. Cuando la fila no se puede leer, el panel muestra un **Top 5 general** de Arena como referencia. Se retira al salir de selección.

La lista de campeones usa la muestra de Arena de todas las regiones y muestra su parche. GGEZ.GG calcula la letra con un ajuste por tamaño de muestra. La app reutiliza la copia de `%LOCALAPPDATA%\AumentosLoL\ggez-arena\<parche>\champion-tiers.json` y puede mostrar el parche inmediatamente anterior con su etiqueta. El acceso de la bandeja **Tier de campeones de Arena** abre la fuente. [Tier list de Arena](https://ggez.gg/tierlist/arena) · [Licencia de GGEZ.GG · CC BY 4.0](https://ggez.gg/about#using-our-numbers)

En la selección de campeón de **ARAM Caos**, busca el panel de OP.GG abajo a la derecha. Se actualiza cuando cambias de campeón y se retira al salir de esa pantalla. Muestra la combinación principal y una alternativa cuando OP.GG publica otra. Si ya tienes los dos hechizos recomendados, aparece **Ya tienes estos hechizos**, independientemente de cuál esté en **D** o en **F**. La selección de los hechizos se hace en el cliente de League.

## Historial de aumentos

Abre **Abrir historial de aumentos** desde el icono de la bandeja. El archivo CSV se guarda en `%LOCALAPPDATA%\AumentosLoL\historial-aumentos.csv` y se puede abrir en Excel o cualquier lector de CSV.

Cada fila guarda la carta ofrecida, su posición, el campeón, el modo, el parche y la fecha. La columna `tier` contiene la estimación local y `opgg` contiene la valoración o popularidad mostrada. Las columnas `arammayhem_popularidad_global_pct` y `arammayhem_parche` guardan el porcentaje de ARAMMayhem.com y el parche de sus datos. Las columnas `ggez_*` guardan el porcentaje de elección, cuántas veces se eligió el aumento, el tamaño de la muestra y el parche de GGEZ.GG. Las filas previas a cada integración llevan sus columnas nuevas vacías. Una carta nueva o una valoración que cambie en pantalla se registra en la misma selección. Las filas marcadas como **Captura** contienen los valores recalculados con los datos vigentes al importarla.

Desde la raíz del proyecto, comprueba una imagen de Arena con `.\dist\win-x64-wiki\Iniciar.cmd --verificar Arena .\evidencias\image4.png Elise`. Para agregar sus cartas al historial, cierra primero el overlay desde **Salir** en la bandeja y ejecuta `.\dist\win-x64-wiki\Iniciar.cmd --registrar-capturas Arena Elise .\evidencias\image4.png`. Para otra captura, cambia el archivo, la clave del campeón y el modo (`Arena` o `Caos`).

En Arena, `arena_nivel_aumento` conserva el nivel leído, `arena_fuente_mecanica` identifica la Wiki cuando intervino y `arena_motivo` guarda la explicación mostrada. Al importar una captura se pueden recuperar el campeón indicado y los niveles visibles. La sinergia por objetos se calcula durante la partida con el inventario del cliente local.

## Construir y actualizar

Con el SDK de .NET 10, ejecuta `powershell -File scripts/Publicar.ps1`. El script descarga el modelo español si falta y publica en `dist/win-x64`.

Para preparar una versión mientras se usa la anterior, puedes publicar en otra carpeta con `powershell -File scripts/Publicar.ps1 -Destination dist/win-x64-wiki`. Si Windows muestra `0x800711C7`, el evento 3077 de **CodeIntegrity/Operational** identifica la política. En este equipo fue **Control inteligente de aplicaciones** y el paquete inició después de desactivar esa protección. Si se mantiene activa y Windows bloquea la compilación sin firma, la solución para el paquete es firmarlo con un certificado que Windows acepte. [Preguntas frecuentes de Microsoft](https://support.microsoft.com/es-es/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)

Para actualizar el catálogo, ejecuta `powershell -File scripts/Get-Assets.ps1 -Patch 16.23 -ChampionVersion 16.23.1` con las versiones nuevas. Revisa los cambios de mecánicas y las excepciones en `src/Aumentos.App/Assets/ratings.json`, corre las pruebas y vuelve a publicar. El overlay se suspende si detecta que el parche del juego difiere del catálogo.

Después de regenerar el catálogo, ejecuta `powershell -File scripts/Get-ArenaWikiMechanics.ps1` para vincularlo con la revisión pública más reciente. Con `-Revision 4070515` puedes reproducir la revisión incluida, del 29 de septiembre de 2026. El generador conserva los textos, la atribución y la licencia de la Wiki, y vincula títulos en inglés con IDs y claves del catálogo en español. Comprueba las reglas de `ArenaAugmentAffinity.cs` al cambiar de revisión. Las cifras que muestra el overlay provienen de los datos del parche del catálogo. [Módulo de mecánicas de Arena](https://wiki.leagueoflegends.com/en-us/Module:ArenaAugmentData/data) · [Datos del parche 16.23](https://raw.communitydragon.org/16.23/cdragon/arena/es_mx.json)

Ejecuta estos comandos en PowerShell desde la raíz del proyecto. Usan el SDK de .NET 10 del sistema o el incluido en AugmentLens en este equipo.

```powershell
$sdk = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (!$sdk) { $sdk = "$env:LOCALAPPDATA\AugmentLens\dotnet\dotnet.exe" }
& $sdk build .\AumentosLoL.slnx -c Release
& $sdk .\tests\Aumentos.Tests\bin\Release\net10.0-windows10.0.19041.0\Aumentos.Tests.dll
```

La suite verifica las fuentes, el historial, las builds, la selección de campeón y la afinidad de los aumentos por inventario y nivel. Agrega estas opciones al comando que ejecuta las pruebas para comprobar la detección de cartas y los paneles.

- `--caos evidencias/captura.png --arena evidencias/image.png --items evidencias/image2.png` reproduce los paneles de referencia de aumentos y de objetos.
- `--shop-shot "ruta\tienda.png" --shop-closed-shot "ruta\partida.png"` comprueba la detección con la tienda abierta y cerrada.
- `--capture-smoke` verifica la captura de Windows y los paneles de aumentos, builds con iconos y hechizos.
- `--arena-draft-shot "ruta\seleccion.png" --arena-champion-overlay-smoke` comprueba la fila de Valentía de la captura de referencia y el panel de campeones.
- `--arena-jax-upgrade-shot "ruta\fuegos.png"` comprueba el nivel 2 de **¡Enciéndelos!** en la carta central de la captura de referencia.
- `--arena-level-shot evidencias/image4.png` comprueba dos mejoras de nivel 2 en la misma oferta, incluida la relectura independiente de cada posición.
- `--arena-build-snapshot "ruta\allgamedata.json" --arena-build-overlay-smoke` comprueba la ruta con un registro de Arena que incluya rivales confirmados y el panel de tienda.
- `--live` comprueba la API local durante una partida de ARAM Caos o Arena.

## Hallazgos para futuras sesiones

- En **26.23**, la estimación base cubre **25,782 de 48,786** combinaciones de 173 campeones y 282 cartas de Arena (**52.85 %**). Antes de incorporar la Wiki cubría **17,131** (**35.11 %**). El recuento usa un inventario vacío y el efecto base de cada carta. **148** cartas tienen letra para todos los campeones, **5** para algunos, **10** muestran **Variable** y **119** siguen sin letra base. Las **153** cartas con alguna letra incluyen 148 para Jax, 151 para Cho’Gath y 150 para Tristana. Los objetos y niveles pueden cambiar la valoración de una oferta concreta.
- La revisión **4070515** de LoL Wiki vinculó mecánicas con **241** cartas del catálogo. Para comprobar niveles usa los datos de Arena del parche, incluidos los ajustes específicos del modo. Este catálogo identifica el máximo de **260** cartas y tiene cambios numéricos comprobados para **19**. Por ejemplo, **Ritmo Crítico** llega a Nv. 2 y **Hábil** a Nv. 3. Las cifras de la carta **En las Cuerdas** incluyen cuatro pulsos del efecto. [Datos específicos del modo Arena](https://raw.communitydragon.org/16.23/game/maps/modespecificdata/cherry.bin.json)
- En el catálogo actual, **Abrecaminos** (221), **Putrificador Quimtech** (347) y **Abrazo Demoniaco** (383) comparten título con objetos que tienen prioridad en la detección. Las dos versiones de **¡Estadísticas sobre Estadísticas!** (227 y 228) siguen ambiguas en Arena. **¡Estadísticas!** (226) es la carta distinta de dos yunques.
- Para ampliar los tiers, actualiza el catálogo, revisa mecánicas y etiquetas de cartas en los datos públicos del cliente y asigna reglas por perfil. Esto se puede hacer con el catálogo y pruebas de regresión sin jugar con todos los campeones. Conserva capturas limpias para comprobar OCR, rerolls y cierres de panel. Una captura con el propio overlay encima puede tapar parte de un título al volver a leerla. [Catálogo es-MX de Arena, parche 16.23](https://raw.communitydragon.org/16.23/plugins/rcp-be-lol-game-data/global/es_mx/v1/cherry-augments.json)
- Las tres recompensas de Ivern y las mejoras de aumentos de Arena tienen casos de regresión propios. Para el parpadeo de Ivern, usa `--arena-ivern-shot "ruta\ivern.png"`. Para validar la posición **Top #5** sobre el Manto de la Noche Estrellada de Cho’Gath, usa `--arena-prismatic-rank-shot "ruta\prismaticos.png"`.
- En el registro observado de Arena, el campo de equipo no distingue a todos los bandos. La ruta deduce aliados por asistencias y confirma rivales con cruces de combate. Para probar esa ruta, guarda una muestra de `allgamedata` con nombres redactados de forma consistente entre participantes y eventos. `--verificar-build` comprueba únicamente la fuente de OP.GG.

[Riot Developer Portal](https://developer.riotgames.com/docs/lol) · [CommunityDragon](https://communitydragon.org/docs/assets)

Aumentos LoL no cuenta con el respaldo de Riot Games ni representa sus opiniones. Riot Games y sus propiedades asociadas son marcas de Riot Games, Inc.
