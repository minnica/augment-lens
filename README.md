# Aumentos LoL

Aumentos LoL muestra recomendaciones de **aumentos**, **objetos**, **campeones** y **hechizos de invocador** en League of Legends. Detecta tu campeón a través del cliente local y reconoce los títulos de los aumentos en español. El catálogo incluido corresponde al parche **26.23** (`16.23` en el cliente y en OP.GG).

| Función | ARAM Caos | Arena |
| --- | --- | --- |
| Comparación de aumentos en partida | OP.GG, estimación local, respaldo de AramHex y popularidad de ARAMMayhem.com | Posiciones observadas de GGEZ.GG y afinidad local por campeón, objetos y nivel con mecánicas de LoL Wiki |
| Builds de OP.GG | Al abrir la tienda con **P** | Al abrir la tienda con **P** |
| Hechizos de OP.GG | En la selección de campeón | — |
| Tiers de campeones | OP.GG en la selección de campeón | GGEZ.GG en la selección de campeón |
| Invitados de Honor | — | Tier local junto a las opciones de votación |

[Ver las capturas actuales de los overlays de ARAM Caos y Arena](evidencias/overlays/README.md)

## Instalar en Windows

1. En Windows 10 (versión 2004 o posterior) o Windows 11 **x64**, haz doble clic en `Instalar-AumentosLoL-x64.exe`. El instalador incluye .NET, el lector de cartas en español y el catálogo. Se instala para tu cuenta, sin permisos de administrador.
2. Deja marcada **Iniciar Aumentos LoL en la bandeja al iniciar sesión en Windows** y pulsa **Instalar**. Al finalizar, **Abrir Aumentos LoL** te lleva al diagnóstico. En los siguientes inicios de sesión la app arrancará en la bandeja, junto al reloj.
3. Usa League en **ventana sin bordes** y déjalo en primer plano para ver los paneles. Puedes abrir **Aumentos LoL** desde el menú Inicio o hacer doble clic en su icono junto al reloj. El menú del icono permite pausar el overlay o salir.

Para cambiar el inicio automático, abre **Configuración → Aplicaciones → Inicio** en Windows. Para actualizar el programa, elige **Salir** en la bandeja y ejecuta el instalador nuevo. Para quitarlo, elige **Salir** y desinstala **Aumentos LoL** desde **Configuración → Aplicaciones**. La desinstalación retira los accesos directos y conserva tu historial y calibración en `%LOCALAPPDATA%\AumentosLoL`.

### Calibrar la lectura de aumentos

Si el diagnóstico pide calibración, abre una captura de la **ventana completa del juego** o pulsa **Capturar juego** y vuelve a League con el panel de cartas abierto. Elige el modo y el número de cartas. Marca un detalle fijo del panel, como el botón azul inferior, y luego una franja por título que cubra también nombres largos. Pulsa **Probar lectura** y **Guardar calibración**.

Se guarda una calibración por modo, tamaño de ventana y número de cartas. Se incluyen las dos referencias de tu PC, **ARAM Caos con cuatro cartas** y **Arena con tres cartas**, y una posición inicial calculada para **ARAM Caos con tres cartas**. La app ajusta la posición en ventanas 16:9 y también cuando el tamaño difiere hasta un 3 % de la referencia. Puedes guardar una calibración exacta o agregar otra distribución desde el diagnóstico.

## Comparar aumentos en partida

En **ARAM Caos** y **Arena**, cada aumento valorado muestra solo su tier en un hexágono de color centrado en el borde inferior de la carta. Las cartas sin un tier disponible quedan libres. La letra usa OP.GG, AramHex o la estimación local en ARAM Caos y GGEZ.GG o la estimación local en Arena. Las mejoras conservan su propia valoración y **S+** aparece en dorado.

En **ARAM Caos**, la app sigue comparando las cartas empatadas para el diagnóstico y el historial. Usa las mecánicas, el campeón, los objetos, las estadísticas en vivo y los rivales detectados. Cuando falta una ventaja clara, puede usar el rendimiento de AramHex del parche vigente si hay datos para todas las cartas empatadas.

| Etiqueta | Significado en el overlay |
| --- | --- |
| S / S+ | Estimación local para una sinergia excepcional. En Arena se muestra S+ |
| A | Estimación local para una mecánica afín al perfil del campeón |
| B | Estimación local intermedia para la sinergia evaluada |
| C | Estimación local para una mecánica con poca afinidad con el perfil |
| Sin letra | La carta no tiene una valoración disponible o su utilidad depende de la partida |

Al cambiar una carta, el overlay vuelve a leer esa posición y actualiza sus valoraciones. Para distinguir las versiones de **¡Estadísticas sobre Estadísticas!** en **ARAM Caos** y **Arena**, lee cuántos yunques ofrece la carta. En Arena también busca el nivel visible.

Al detectar el modo y el campeón, la app carga en segundo plano la tabla de su página pública de OP.GG. Durante la consulta, la vista compacta muestra la afinidad local si está disponible. Las imágenes del juego se procesan en memoria. La consulta externa contiene el modo y el campeón. Cada cambio de carta usa la tabla que ya está en memoria.

La copia por parche, modo y campeón se guarda en %LOCALAPPDATA%\AumentosLoL\opgg y se actualiza tras 12 horas de antigüedad cuando vuelves a usar ese campeón. Si falla la red, la app puede seguir usando su copia. El diagnóstico indica cuándo se actualizó o por qué falta. [Datos de aumentos de OP.GG](https://op.gg/lol/modes/aram-mayhem/lux/augments) · [Uso de los datos de OP.GG](https://help.op.gg/hc/en-us/articles/31091405109401-Can-I-use-OP-GG-data)

En **Arena**, **GGEZ.GG** aporta la frecuencia de elección del aumento con ese campeón y el tamaño de la muestra, del parche vigente o del inmediatamente anterior de la misma temporada. El menú **Datos de GGEZ.GG** abre la página del último campeón de Arena que detectó la app. [Ejemplo en Arena de Elise](https://ggez.gg/champion/Elise/arena) · [Licencia de GGEZ.GG · CC BY 4.0](https://ggez.gg/about#using-our-numbers)

**GGEZ.GG** también aporta posiciones medias. La app calcula un tier observado para el parche vigente con al menos **50 partidas** de esa carta para ese campeón, comparándola con cartas de su rareza y moderando las muestras pequeñas. **GGEZ global** cubre huecos cuando faltan el tier del campeón y el local, usando al menos **100 participaciones** del formato global validado. [Resultados globales de aumentos de Arena](https://ggez.gg/tierlist/arena/augments)

**League of Legends Wiki** aporta las mecánicas de cartas de Arena. La app calcula la letra a partir de esas mecánicas, las características de tu campeón y los objetos que el cliente confirma en tu inventario. El menú **Mecánicas de League of Legends Wiki** abre la revisión que se usó. [Módulo de los colaboradores de League of Legends Wiki, revisión 4070515](https://wiki.leagueoflegends.com/en-us/Special:PermanentLink/4070515) · [Datos de la Wiki adaptados bajo CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/)

El nivel de las mejoras influye en su tier cuando se conoce la diferencia. Con **Hábil** en Jax, pasar de nivel 2 a 3 lleva la velocidad de ataque del 80 % al 130 % y elimina su límite. Cuando solo se conoce el efecto base, se usa su afinidad.

Las ofertas de **Sube de nivel un aumento** muestran un tier cuando la mejora tiene una valoración. **Omnialma Nv. 2** recibe **B** local. **Reemplazar Aumento** se queda sin insignia porque su utilidad depende de qué aumento entregues y cuál salga.

Los **fragmentos del Yunque de estadísticas** en **ARAM Caos** y **Arena** muestran el tier local en el mismo hexágono inferior que los aumentos. En **Arena**, los objetos legendarios y prismáticos también usan esa insignia. Durante una oferta prismática, el panel lateral muestra los **cinco objetos prismáticos con mayor afinidad** y sus puestos. Sobre cada carta aparece únicamente su tier.

Las insignias de selección, cartas, votación y Top 5 comparten la misma [paleta de tiers](evidencias/overlays/paleta-de-tiers.png). **S+** usa un marco prismático con cristales, **S** un marco prismático azul y violeta, **A** oro brillante, **B** oro antiguo, **C** plata, **D** acero y **E** grafito. La app incluye los [PNG originales](assets/tier-icons/).

En **Arena**, **Poder Mejorado** tiene en cuenta los objetos equipados que generan daño por sí mismos. Los objetos que solo aportan estadísticas, como las botas de penetración, no cuentan como fuentes de daño. Cuando la API local no entrega los aumentos elegidos, la valoración señala que faltan por confirmar.

Para ajustar el tamaño de las insignias de **ARAM Caos** y **Arena**, pulsa **Tamaño de los tiers** en el borde derecho de la pantalla durante una oferta. Mueve el deslizador de **75 % a 300 %** para ver el cambio en las cartas y aparta el cursor para cerrar el ajuste. Las insignias más grandes se desplazan hacia arriba para dejar libres los botones de cambio. El tamaño se guarda automáticamente. Desde la bandeja, **Tamaño de los tiers de cartas…** abre la vista previa de escritorio.

Para ajustar el panel **Top 5 prismáticos**, pulsa **Tamaño…** encima del panel y ajusta su escala en partida. El panel guarda su propio tamaño y se ajusta al espacio disponible junto a las cartas. Desde la bandeja, **Tamaño del panel de prismáticos…** abre la vista previa de escritorio.

El diagnóstico sigue registrando los desempates de Arena. Con Renata Glasc, **Bendición Oscura** puede ganar el empate en A frente a **Fuego Sagrado** y **Sacrificio Voluntario** por la aceleración al escudar aliados.

En **ARAM Caos**, **ARAMMayhem.com** aporta la frecuencia global de elección al diagnóstico y al historial. Se consulta el parche actual o el inmediatamente anterior de la misma temporada y se comparte la copia entre campeones. [Datos de ARAMMayhem.com · CC BY 4.0](https://arammayhem.com/data/)

**AramHex** aporta una letra de respaldo para el campeón a partir de partidas de **China** con reglas normales y al menos **300 partidas** para la combinación. Cuando se usa el parche anterior, solo entran cartas con mecánicas verificadas entre ambos parches y la letra publicada de OP.GG o la estimación local conserva prioridad. [AramHex · CC BY-NC 4.0](https://github.com/czgreat/aramhex-data)

Para auditar la cobertura desde PowerShell con el paquete de desarrollo, ejecuta `.\dist\win-x64\Iniciar.cmd --verificar-tiers Arena Renata` o cambia `Arena` por `Caos`. El resultado cuenta las cartas del catálogo por fuente de su letra visible e indica cuáles quedan sin muestra.

## Votar por Invitados de Honor en Arena

Cuando aparecen los **Invitados de Honor**, la app reconoce las tres opciones del cliente en español de México. Al lado de cada opción muestra un tier local de **S a C** y el motivo principal. Si una opción tiene una ventaja clara sobre las otras dos, lleva la marca **Recomendado**. Las insignias se retiran al cerrar la votación y puedes seguir votando directamente en el cliente. [Invitados de Honor en Arena, Riot Games](https://www.leagueoflegends.com/es-mx/news/dev/dev-leveling-up-arena/)

Cada invitado cambia las reglas de todo el lobby. La letra estima cuánto favorece al equipo según tu campeón, los aliados confirmados, las compras y los rivales detectados. También considera la ronda y la vida del equipo cuando puede leerlas en el marcador. Antes de que la partida confirme a los aliados por sus combates, la app puede usar la composición fijada en la selección si la reconoce en la lista de jugadores. Por ejemplo, **Negociación Oscura** de Tahm Kench puede perder valor en composiciones que dependen de la vida máxima. Las reglas se contrastaron con los datos del cliente de **26.23**. [Textos del cliente 16.23](https://raw.communitydragon.org/16.23/game/es_mx/data/menu/en_us/lol.stringtable.json)

## Builds de OP.GG en partida

Pulsa **P**, la tecla predeterminada de la tienda, en **ARAM Caos** o **Arena**. La build aparece automáticamente a un lado de la tienda y se oculta al cerrarla. El panel también se aparta durante la elección de aumentos. Si se detecta el modo pero falta el campeón, aparece **No se reconoció tu campeón**.

El panel muestra hasta tres objetos pendientes para la etapa de compra, con su icono y nombre en español. La compra prioritaria lleva un fondo tenue y la etiqueta dorada **El mejor**. La app considera tu campeón, inventario, rivales y las sinergias de aumentos que puede confirmar. Si la API local omite tus aumentos, la app registra los clics detectados sobre una carta de una oferta que se cierra. Las botas tienen su propia sección y se retiran al comprarlas. En Arena también aparecen los prismáticos de la fuente. [Build de Zyra en ARAM Caos](https://op.gg/es-419/lol/modes/aram-mayhem/zyra/build) · [Build de Elise en Arena](https://op.gg/es-419/lol/modes/arena/elise/build)

Con la tienda abierta en **ARAM Caos** o **Arena**, arrastra el encabezado **Build de…** para colocar el panel dentro de la ventana del juego. Las recomendaciones siguen actualizándose en esa posición mientras compras.

Los porcentajes de **elección** y **victorias** son los que publica la fuente para cada objeto. En **Arena**, OP.GG los publica para el campeón. En **ARAM Caos**, HexMayhem aporta las cifras globales del modo. El panel indica la fuente y el parche. Cuando una fila carece de estadísticas publicadas, sigue mostrando el objeto. [Objetos de Jax en OP.GG](https://op.gg/es-419/lol/modes/arena/jax/items) · [Objetos de ARAM Caos en HexMayhem](https://hexmayhem.com/items)

En **ARAM Caos**, **Objetos iniciales** se oculta al confirmar una pieza inicial recomendada o un objeto terminado. Si falta un núcleo de OP.GG, **Ruta local** propone compras con el perfil del campeón, su inventario y los datos rivales.

En **Arena**, antes de confirmar un rival, la ruta usa tu inventario, el perfil del campeón y el núcleo de OP.GG cuando está disponible. Después, puede adaptarse a las compras y al perfil de hasta tres rivales de rondas jugadas. [API local de Riot](https://developer.riotgames.com/docs/lol#live-client-data-api)

Con seis objetos equipados, aparece **Build completa**. Si una sustitución se ajusta mejor a la partida, **Cambio sugerido** muestra el objeto que sale, el que entra y el motivo. Cuando vendes el objeto señalado, el reemplazo aparece como **El mejor** en la etapa de compra. La sugerencia prioriza los cambios en objetos flexibles y protege las piezas clave y las respuestas a otras amenazas activas.

En **ARAM Caos**, el cambio de una build completa también considera el marcador rival. Si uno o varios campeones llevan ventaja en bajas, muertes y asistencias y sus compras apuntan al mismo tipo de daño, la app da prioridad a esa defensa. Un solo rival de AP puede justificar resistencia mágica aunque los otros cuatro hayan comprado daño físico. El panel muestra quién motivó el cambio. Si el marcador está incompleto o parejo, se usa la comparación por compras. [API local de Riot](https://developer.riotgames.com/docs/lol#live-client-data-api)

Para cambiar un objeto de una build completa, la app compara cuánta armadura o resistencia mágica perderías frente al daño relevante. Si lideras a tu equipo, exige además un beneficio defensivo claro antes de cambiar un objeto que aporta mucha vida a tu build de tanque. Las opciones de Heridas Graves tienen en cuenta si un aliado ya puede aplicarlas al hacer daño.

Al pie del panel, **Daño rival estimado** muestra el daño **físico** en naranja y el **mágico** en violeta. Estima la proporción a partir del perfil y los objetos ofensivos terminados de cada rival identificable, y se actualiza durante la partida. En Arena usa hasta tres rivales de rondas jugadas e indica cuándo está esperando confirmar al primero. La barra **Daño del lobby** reúne solo a los rivales que la app identifica, incluidos equipos de otras rondas, y se amplía a medida que los eventos de combate permiten distinguirlos.

Para consultar la build manualmente, pulsa **Ctrl + Alt + B** o usa **Mostrar u ocultar build de OP.GG** en la bandeja y vuelve al juego. Dentro de la tienda, ese control permite ocultar el panel hasta la siguiente apertura. Usa **Abrir build de OP.GG** para visitar la página del último campeón consultado.

En **ARAM Caos**, pulsa **−** en la esquina superior izquierda de la build para ocultar las recomendaciones con la tienda abierta. Pulsa **+** junto al borde de la tienda para recuperarlas. El icono se oculta al cerrar la tienda. La app recuerda si la build está plegada cuando vuelves a abrirla durante la misma sesión.

Con la build visible, pulsa **Tamaño…** encima del panel o usa **Ctrl + Shift + 9** con el **9 de la fila superior**. Mueve el deslizador de **75 % a 150 %** para ver el cambio en la build mientras usas la tienda. El ajuste se cierra al apartar el cursor y espera a que sueltes el deslizador si aún estás arrastrando. El tamaño se guarda automáticamente, se aplica en ARAM Caos y Arena y respeta el espacio disponible junto a la tienda. Desde la bandeja, **Tamaño de las builds…** abre la vista previa de escritorio, donde puedes usar **Restablecer 100 %**.

La app guarda una copia de las recomendaciones por campeón, modo y parche en `%LOCALAPPDATA%\AumentosLoL\opgg-builds`. Cuando muestra datos del parche inmediatamente anterior de la misma temporada, el panel indica **Parche anterior** y su número. Los iconos se descargan de OP.GG y se reutilizan en memoria durante la sesión. Los nombres aparecen mientras se cargan las imágenes.

Desde PowerShell, en la raíz del proyecto, puedes consultar la build publicada por OP.GG con `.\dist\win-x64\Iniciar.cmd --verificar-build Caos Kennen` o `.\dist\win-x64\Iniciar.cmd --verificar-build Arena Elise`.

## Selección de campeón

En **Arena**, el **Top 30 general** aparece en el lateral derecho durante la selección en el cliente de League. Muestra la posición, el campeón, el tier y su porcentaje de top 3 en una sola columna. La lista ordena por tier de GGEZ.GG y, dentro de la misma letra, por frecuencia de terminar entre los primeros tres. Al preseleccionar o fijar un campeón, el panel muestra su tier si está disponible, aunque quede fuera de los primeros 30. El panel se retira al salir de selección.

El panel abre con la carencia principal del equipo, por ejemplo **Falta daño sostenido**, y un tier de S a D cuando preseleccionas un campeón y el cliente informa al menos un aliado. La estimación local considera el control, el seguimiento de daño, la protección y el aguante en las rondas de Arena. Cuando GGEZ.GG publica al menos 20 partidas del trío exacto en el parche vigente, la letra usa su puesto medio ajustado hacia 3.5 para moderar muestras pequeñas. Debajo de la carencia aparecen el puesto medio observado y el número de partidas. La lista de tríos publicada tiene cobertura parcial y la letra local cubre las combinaciones sin muestra suficiente. [Tríos de Arena en GGEZ.GG](https://ggez.gg/tierlist/arena/teams) · [Riot Games sobre Arena 3×6](https://www.leagueoflegends.com/en-gb/news/dev/dev-leveling-up-arena/)

La tabla del equipo muestra a tu campeón y a los aliados reconocidos, con columnas para su estado (**Preselección** o **Confirmado**) y su tier individual. La evaluación y las sugerencias se actualizan al cambiar un campeón. **Para tu equipo** ofrece hasta cuatro campeones distintos por cada una de las dos categorías de mayor prioridad. La carencia del encabezado aparece primero cuando hay opciones para cubrirla. Las sugerencias omiten los campeones de tu equipo, los bloqueos y las elecciones rivales que informa el cliente.

La lista de campeones usa la muestra de Arena de todas las regiones. GGEZ.GG calcula la letra con un ajuste por tamaño de muestra. La app reutiliza la copia de `%LOCALAPPDATA%\AumentosLoL\ggez-arena\<parche>\champion-tiers.json` y muestra **Parche anterior** cuando usa datos de ese parche. El acceso de la bandeja **Tier de campeones de Arena** abre la fuente. [Tier list de Arena](https://ggez.gg/tierlist/arena) · [Licencia de GGEZ.GG · CC BY 4.0](https://ggez.gg/about#using-our-numbers)

En la selección de campeón de **ARAM Caos**, el panel lateral abre con la evaluación del equipo, por ejemplo **Falta daño sostenido**. La estimación local de S a D considera la primera línea, el control, la protección y el daño del equipo. Si el equipo está incompleto, el panel indica cuántos campeones reconoce el cliente. **Para tu equipo** sugiere hasta tres campeones y explica cómo aporta cada uno, por ejemplo **Daño físico con básicos** o **Daño mágico a distancia**. Con el equipo completo, también avisa si al cambiar quedaría sin cubrir una función como la primera línea. Ordena de S a D, con las opciones sin tier al final. Entre campeones del mismo tier prioriza la afinidad con el equipo. Al alcanzar tier S con el equipo completo, aparece **Equipo equilibrado** y se retiran las sugerencias.

La lista de **Campeones disponibles** ordena por tier de OP.GG las cartas de tu oferta inicial y, después de elegir, reúne a tu campeón y los del banco. Tu campeón se destaca en dorado con la etiqueta **TÚ**. La evaluación y la lista se actualizan al elegir o intercambiar un campeón. [Tier list de campeones de ARAM Caos en OP.GG](https://op.gg/es-419/lol/modes/aram-mayhem)

También aparece una letra debajo de cada carta reconocida en ofertas de dos o tres cartas. En **Campeones disponibles**, los retratos reconocidos muestran su letra en la esquina inferior derecha. Las letras **S, A, B, C y D** corresponden a los tiers **1 a 5** de la tabla de OP.GG. La app guarda la tabla en la caché local y avisa en el panel cuando usa datos del parche anterior.

Los paneles de selección, tienda y ajustes comparten un fondo azul oscuro translúcido y un marco fino de oro viejo inspirados en la tienda de League. La app usa Beaufort for LOL en títulos y Spiegel en texto cuando están disponibles en Windows, con Cambria y Segoe UI como alternativas. Las filas de selección muestran el retrato y una insignia hexagonal con el color de su tier. Al elegir un campeón en ARAM Caos, su retrato y tier aparecen en el panel de hechizos de OP.GG, abajo a la derecha. El panel se actualiza cuando cambias de campeón. Muestra la combinación principal y una alternativa cuando OP.GG publica otra. La selección de los hechizos se hace en el cliente de League.

Cuando se agota el tiempo final de selección o el cliente anuncia **¡Prepárate para la batalla!**, se ocultan a la vez las letras del banco y los paneles de composición y hechizos.

## Historial de aumentos

Abre **Abrir historial de aumentos** desde el icono de la bandeja. El archivo CSV se guarda en `%LOCALAPPDATA%\AumentosLoL\historial-aumentos.csv` y se puede abrir en Excel o cualquier lector de CSV.

Cada fila guarda la carta ofrecida, su posición, el campeón, el modo, el parche y la fecha. La columna `tier` contiene la estimación local y `opgg` contiene la valoración o popularidad mostrada. Las columnas `arammayhem_popularidad_global_pct` y `arammayhem_parche` guardan el porcentaje de ARAMMayhem.com y el parche de sus datos. Las columnas `ggez_*` guardan el porcentaje de elección, cuántas veces se eligió el aumento, el tamaño de la muestra y el parche de GGEZ.GG. Las filas previas a cada integración llevan sus columnas nuevas vacías. Una carta nueva o una valoración que cambie en pantalla se registra en la misma selección. Las filas marcadas como **Captura** contienen los valores recalculados con los datos vigentes al importarla.

Desde la raíz del proyecto, comprueba una imagen de Arena con `.\dist\win-x64\Iniciar.cmd --verificar Arena .\evidencias\image4.png Elise`. Para agregar sus cartas al historial, cierra primero el overlay desde **Salir** en la bandeja y ejecuta `.\dist\win-x64\Iniciar.cmd --registrar-capturas Arena Elise .\evidencias\image4.png`. Para otra captura, cambia el archivo, la clave del campeón y el modo (`Arena` o `Caos`).

En Arena, `arena_nivel_aumento` conserva el nivel leído, `arena_fuente_mecanica` identifica la Wiki cuando intervino y `arena_motivo` guarda la explicación mostrada. Al importar una captura se pueden recuperar el campeón indicado y los niveles visibles. La sinergia por objetos se calcula durante la partida con el inventario del cliente local.

## Construir y actualizar

Con la app abierta en la bandeja, el catálogo se actualiza al detectar un parche nuevo instalado. Fuera de partida, la app descarga y valida el catálogo en una carpeta temporal y guarda la versión válida en `%LOCALAPPDATA%\AumentosLoL\patch-catalogs`. Se reinicia automáticamente para cargarla. Si los datos esenciales aún no están disponibles, vuelve a intentar la descarga cada 30 minutos. El diagnóstico y `estado.log` muestran el avance. El overlay exige un catálogo que coincida con el parche del juego.

La app precarga las estadísticas generales y las de hasta seis combinaciones recientes de modo y campeón a partir del día 2 desde que detecta el parche instalado. Si falta respaldo en alguna fuente, vuelve a revisar alrededor del día 7. Hace esas revisiones fuera de partida y reutiliza las copias por parche. La consulta en segundo plano por campeón sigue disponible cuando juegas uno nuevo. El diagnóstico guarda cuántas cartas de Caos tienen datos tanto en OP.GG como en AramHex y qué objetos de la build de Arena también son frecuentes en GGEZ.GG cuando el campeón y el parche coinciden. `--verificar-build Arena Elise` muestra ese cotejo si hay datos compatibles. **Consultar otras fuentes** en la bandeja abre U.GG, Mobalytics y Wasfun para revisión manual.

### Generar el instalador para compartir

En el equipo de desarrollo necesitas el SDK de .NET 10 e Inno Setup 7. Desde la raíz del proyecto, instala el compilador de instaladores una vez y genera el archivo con PowerShell.

```powershell
winget install --id JRSoftware.InnoSetup.7 -e --scope user
powershell -File scripts/Crear-Instalador.ps1
```

Comparte **`dist/Instalar-AumentosLoL-x64.exe`**. El script crea una publicación limpia y autocontenida e incorpora las bibliotecas x64 de Visual C++ que usa el lector de cartas. El destinatario puede completar la instalación sin conexión. Las estadísticas en línea se consultan al usar la app. [Inno Setup](https://jrsoftware.org/inno-setup/downloads) · [Bibliotecas redistribuibles de Microsoft](https://learn.microsoft.com/es-es/visualstudio/releases/2022/redistribution)

El instalador generado carece de firma de editor. Si Windows exige una firma de confianza, será necesario firmar la app y el instalador para distribuirlos en ese equipo. [Control inteligente de aplicaciones de Windows](https://support.microsoft.com/es-es/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)

### Verificar la versión después de cada cambio

1. Para cada cambio que afecte a la app, genera el instalador actualizado y ejecútalo en este equipo. Cierra primero el overlay desde **Salir** en la bandeja. Si hay una partida activa, coordina el reinicio para cuando termine.
2. Abre Aumentos LoL desde el menú Inicio. Con `Get-CimInstance Win32_Process`, comprueba que la única instancia se ejecuta desde `%LOCALAPPDATA%\Programs\AumentosLoL\AumentosLoL.exe`. Busca también procesos `dotnet.exe` que ejecuten `AumentosLoL.dll` en otra carpeta. Una instancia anterior puede impedir que la recién instalada se abra.
3. Confirma con `Get-FileHash` que el `AumentosLoL.dll` instalado coincide con el de la nueva publicación y que el proceso arrancó después de instalarlo. Comprueba que el acceso de inicio automático apunta al ejecutable instalado con `--bandeja` y prueba el cambio en el panel o flujo correspondiente. Si parte de la verificación queda pendiente, indícalo en la entrega.

### Publicar para desarrollo

Ejecuta `powershell -File scripts/Publicar.ps1` para crear el paquete de desarrollo en `dist/win-x64`, con el lanzador `Iniciar.cmd` que usan los ejemplos. Puedes elegir otra carpeta con `-Destination`.

Para actualizar el catálogo incluido en una compilación, ejecuta `powershell -File scripts/Get-Assets.ps1 -Patch 16.23 -ChampionVersion 16.23.1` con las versiones nuevas. Revisa los cambios de mecánicas y las excepciones en `src/Aumentos.App/Assets/ratings.json`, corre las pruebas y vuelve a publicar.

Después de actualizar el catálogo, ejecuta `powershell -File scripts/Get-AramHexCompatibility.ps1` para verificar las cartas de ARAM Caos cuyas mecánicas coinciden con el parche anterior de la misma temporada.

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
- `--arena-champion-overlay-smoke` comprueba el Top 30 general, el tier de tu elección, el tier del equipo y las sugerencias de composición de Arena.
- `--arena-vote-overlay-smoke --arena-vote-shot "ruta\votacion.png"` comprueba la posición de las insignias y reproduce la votación de referencia de Yone, Evelynn y Tahm Kench.
- `--arena-vote-round-eight-shot "ruta\votacion-ronda-8.png"` reproduce la votación de Garen, Jarvan IV y Annie y verifica la lectura de la ronda y la vida del equipo.
- `--arena-vote-kayle-shot "ruta\votacion-kayle.png"` verifica que la captura con Tahm Kench, Atakhan y Kayle produce tres valoraciones en sus filas.
- `--arena-prismatic-goredrinker-shot "ruta\prismaticos-aurelion.png"` comprueba los tres tiers y el Top 5 en una oferta con Bebedor de Sangre.
- `--arena-augmented-power-shot "ruta\aumentos-aurelion.png"` verifica la lectura de Tirador Mágico con el prefijo «Quebrantado», Hora del Slime y Poder Mejorado.
- `--patch-update-smoke` verifica con datos públicos el catálogo que se descarga para el parche incluido y la lectura de tríos y objetos de GGEZ.GG. Genera las copias de prueba en una carpeta temporal.
- `--mayhem-draft-shot "ruta\oferta.png" --mayhem-locked-shot "ruta\campeon-elegido.png"` comprueba la oferta de referencia de Kai'Sa, Jinx y Aurelion Sol y el cierre al elegir.
- `--mayhem-pair-shot "ruta\oferta-doble.png"` comprueba la oferta de Camille y Bel'Veth con una carta ampliada.
- `--arena-jax-upgrade-shot "ruta\fuegos.png"` comprueba el nivel 2 de **¡Enciéndelos!** en la carta central de la captura de referencia.
- `--arena-eureka-upgrade-shot "ruta\eureka.png"` comprueba la lectura de **Eureka Nv. 3** entre dos cartas base.
- `--arena-level-shot evidencias/image4.png` comprueba dos mejoras de nivel 2 en la misma oferta, incluida la relectura independiente de cada posición.
- `--arena-build-snapshot "ruta\allgamedata.json" --arena-build-overlay-smoke` comprueba la ruta con un registro de Arena que incluya rivales confirmados y el panel de tienda.
- `--live` comprueba la API local durante una partida de ARAM Caos o Arena.

## Hallazgos para futuras sesiones

- En **26.23**, la estimación base cubre **27,686 de 48,786** combinaciones de 173 campeones y 282 cartas de Arena (**56.75 %**). Antes de incorporar la Wiki cubría **17,131** (**35.11 %**). El recuento usa un inventario vacío y el efecto base de cada carta. **159** cartas tienen letra para todos los campeones, **6** para algunos, **15** muestran **Variable** y **102** siguen sin letra base. Las **165** cartas con alguna letra incluyen 159 para Jax, 162 para Cho’Gath y 161 para Tristana. Los objetos y niveles pueden cambiar la valoración de una oferta concreta.
- La revisión **4070515** de LoL Wiki vinculó mecánicas con **241** cartas del catálogo. Para comprobar niveles usa los datos de Arena del parche, incluidos los ajustes específicos del modo. Este catálogo identifica el máximo de **260** cartas y tiene cambios numéricos comprobados para **20**. Por ejemplo, **Ritmo Crítico** llega a Nv. 2 y **Hábil** y **Eureka** a Nv. 3. Las cifras de la carta **En las Cuerdas** incluyen cuatro pulsos del efecto. [Datos específicos del modo Arena](https://raw.communitydragon.org/16.23/game/maps/modespecificdata/cherry.bin.json)
- En el catálogo actual, **Abrecaminos** (221) y **Abrazo Demoniaco** (383) comparten título con objetos que tienen prioridad en la detección. **Putrificador Quimtech** (347) se distingue del objeto por la descripción de la carta. En Arena, las versiones de **¡Estadísticas sobre Estadísticas!** (227 y 228) ofrecen tres y cuatro yunques en su nivel base, respectivamente. **¡Estadísticas!** (226) es la carta distinta de dos yunques en su nivel base.
- Para ampliar los tiers, actualiza el catálogo, revisa mecánicas y etiquetas de cartas en los datos públicos del cliente y asigna reglas por perfil. Esto se puede hacer con el catálogo y pruebas de regresión sin jugar con todos los campeones. Conserva capturas limpias para comprobar OCR, rerolls y cierres de panel. Una captura con el propio overlay encima puede tapar parte de un título al volver a leerla. [Catálogo es-MX de Arena, parche 16.23](https://raw.communitydragon.org/16.23/plugins/rcp-be-lol-game-data/global/es_mx/v1/cherry-augments.json)
- Las tres recompensas de Ivern y las mejoras de aumentos de Arena tienen casos de regresión propios. Para el parpadeo de Ivern, usa `--arena-ivern-shot "ruta\ivern.png"`. Para validar el puesto **Top #5** del Manto de la Noche Estrellada de Cho’Gath, usa `--arena-prismatic-rank-shot "ruta\prismaticos.png"`.
- En el registro observado de Arena, el campo de equipo no distingue a todos los bandos. La ruta deduce aliados por asistencias y confirma rivales con cruces de combate. Para probar esa ruta, guarda una muestra de `allgamedata` con nombres redactados de forma consistente entre participantes y eventos. `--verificar-build` comprueba únicamente la fuente de OP.GG.

[Riot Developer Portal](https://developer.riotgames.com/docs/lol) · [CommunityDragon](https://communitydragon.org/docs/assets)

Aumentos LoL no cuenta con el respaldo de Riot Games ni representa sus opiniones. Riot Games y sus propiedades asociadas son marcas de Riot Games, Inc.
