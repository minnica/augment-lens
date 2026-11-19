# Aumentos LoL

Aumentos LoL compara las cartas de **ARAM Caos** y **Arena** en el momento en que aparecen. Detecta tu campeón con la API local de League, reconoce los títulos en español y muestra junto a cada carta el dato de OP.GG y la estimación local. El catálogo corresponde al parche **26.23** (`16.23` en el cliente y en OP.GG).

## Iniciar en Windows

1. Abre `dist/win-x64/Iniciar.cmd`. La carpeta contiene la app, el runtime y el modelo de español. También incluye `AumentosLoL.exe` para el inicio directo donde la política de Windows lo permita.
2. Usa League en **ventana sin bordes**. Cambia al juego para activar la detección. El icono de la bandeja permite abrir el diagnóstico, pausar el overlay o salir.
3. Si el diagnóstico pide calibración, abre una captura de la **ventana completa del juego** o pulsa **Capturar juego** y vuelve a League con el panel de cartas abierto. Elige el modo y el número de cartas. Marca un detalle fijo del panel, como el botón azul inferior, y luego una franja por título que cubra también nombres largos. Pulsa **Probar lectura** y **Guardar calibración**.

Se guarda una calibración por modo, tamaño de ventana y número de cartas. Se incluyen las dos referencias de tu PC, **ARAM Caos con cuatro cartas** y **Arena con tres cartas**, y una posición inicial calculada para **ARAM Caos con tres cartas**. La app ajusta la posición en ventanas 16:9 y también cuando el tamaño difiere hasta un 3 % de la referencia. Puedes guardar una calibración exacta o agregar otra distribución desde el diagnóstico.

## Leer la comparación

**OP.GG** muestra la letra que publica para ese campeón en ARAM Caos (S, A, B, C, D o E). En Arena indica «Elección» con el porcentaje de veces que se eligió la carta, cuando la fuente publica ese dato. **Estimación local** usa las reglas de sinergia del proyecto.

| Letra | Significado |
| --- | --- |
| S | Sinergia excepcional incluida en la tabla editorial |
| A | Mecánica afín al perfil del campeón |
| B | Valoración intermedia indicada en la tabla editorial |
| C | Mecánica con poca afinidad para el perfil |
| Sin dato | Carta reconocida sin valoración de esa fuente para el campeón, modo y parche actuales |
| Variable | La utilidad depende del aumento que reemplaces o del resultado de una transformación |
| ? | El OCR todavía no ha identificado el título |

Al detectar el modo y el campeón, la app carga en segundo plano la tabla de su página pública de OP.GG. Mientras llega por primera vez, aparece «Consultando…» junto a la estimación local. Las imágenes del juego se procesan en memoria. La consulta externa contiene el modo y el campeón. Cada cambio de carta usa la tabla que ya está en memoria.

La copia por parche, modo y campeón se guarda en `%LOCALAPPDATA%\AumentosLoL\opgg` y se actualiza tras 12 horas de antigüedad cuando vuelves a usar ese campeón. Si falla la red, la app puede seguir usando una copia del mismo parche. El diagnóstico indica cuándo se actualizó o por qué falta. [Datos de aumentos de OP.GG](https://op.gg/lol/modes/aram-mayhem/lux/augments) · [Uso de los datos de OP.GG](https://help.op.gg/hc/en-us/articles/31091405109401-Can-I-use-OP-GG-data)

En Arena, el overlay queda oculto cuando aparecen cartas de objetos.

En **ARAM Caos**, **ARAMMayhem.com** muestra el porcentaje de elección global de la carta en la muestra de esa fuente. La app prioriza el archivo del parche actual y, si aún falta, usa el inmediatamente anterior de la misma temporada. En ese caso, debajo del porcentaje aparece «Parche anterior» con el número. La fuente se consulta en segundo plano y se comparte entre todos los campeones. La app vuelve a comprobar los archivos anteriores cada 15 minutos y los del parche actual cada 12 horas mientras se usa el modo. [Datos de ARAMMayhem.com · CC BY 4.0](https://arammayhem.com/data/)

## Historial de aumentos

Abre **Abrir historial de aumentos** desde el icono de la bandeja. El archivo CSV se guarda en `%LOCALAPPDATA%\AumentosLoL\historial-aumentos.csv` y se puede abrir en Excel o cualquier lector de CSV.

Cada fila guarda la carta ofrecida, su posición, el campeón, el modo, el parche y la fecha. La columna `tier` contiene la estimación local y `opgg` contiene la valoración o popularidad mostrada. Las columnas `arammayhem_popularidad_global_pct` y `arammayhem_parche` guardan el porcentaje de ARAMMayhem.com y el parche de sus datos. Las filas previas a cada integración llevan sus columnas nuevas vacías. Una carta nueva o una valoración que cambie en pantalla se registra en la misma selección. Las filas marcadas como **Captura** contienen los valores recalculados con los datos vigentes al importarla.

Para comprobar una imagen con un campeón concreto, ejecuta `dist\win-x64\Iniciar.cmd --verificar Caos "ruta\captura.png" Lux`. Para Arena usa `Arena` y la clave del campeón correspondiente.

## Construir y actualizar

Con el SDK de .NET 10, ejecuta `powershell -File scripts/Publicar.ps1`. El script descarga el modelo español si falta y publica en `dist/win-x64`.

Para actualizar el catálogo, ejecuta `powershell -File scripts/Get-Assets.ps1 -Patch 16.23 -ChampionVersion 16.23.1` con las versiones nuevas. Revisa los cambios de mecánicas y las excepciones en `src/Aumentos.App/Assets/ratings.json`, corre las pruebas y vuelve a publicar. El overlay se suspende si detecta que el parche del juego difiere del catálogo.

Ejecuta `dotnet build AumentosLoL.slnx -c Release` y luego `dotnet tests/Aumentos.Tests/bin/Release/net10.0-windows10.0.19041.0/Aumentos.Tests.dll`. Agrega `--caos evidencias/captura.png --arena evidencias/image.png --items evidencias/image2.png` para probar las tres referencias, `--capture-smoke` para comprobar la captura de ventanas y `--live` durante una partida de uno de los dos modos para comprobar la API.

[Riot Developer Portal](https://developer.riotgames.com/docs/lol) · [CommunityDragon](https://communitydragon.org/docs/assets)

Aumentos LoL no cuenta con el respaldo de Riot Games ni representa sus opiniones. Riot Games y sus propiedades asociadas son marcas de Riot Games, Inc.
