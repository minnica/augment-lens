# Aumentos LoL

Aumentos LoL muestra **S, A, B o C** junto a las cartas de ARAM Caos y Arena. Detecta el campeón con la API local de League y lee los títulos en español con OCR. La primera edición usa el catálogo del parche **26.23** (`16.23` en los datos del cliente).

## Iniciar en Windows

1. Abre `dist/win-x64/Iniciar.cmd`. La carpeta contiene la app, el runtime y el modelo de español. También incluye `AumentosLoL.exe` para el inicio directo donde la política de Windows lo permita.
2. Usa League en **ventana sin bordes**. Cambia al juego para activar la detección. El icono de la bandeja permite abrir el diagnóstico, pausar el overlay o salir.
3. Si el diagnóstico pide calibración, abre una captura de la **ventana completa del juego** o pulsa **Capturar juego** y vuelve a League con el panel de cartas abierto. Elige el modo y el número de cartas. Marca un detalle fijo del panel, como el botón azul inferior, y luego una franja por título que cubra también nombres largos. Pulsa **Probar lectura** y **Guardar calibración**.

Se guarda una calibración por modo, tamaño de ventana y número de cartas. Se incluyen las dos referencias de tu PC, **ARAM Caos con cuatro cartas** y **Arena con tres cartas**, y una posición inicial calculada para **ARAM Caos con tres cartas**. La app ajusta la posición en ventanas 16:9 y también cuando el tamaño difiere hasta un 3 % de la referencia. Puedes guardar una calibración exacta o agregar otra distribución desde el diagnóstico.

## Interpretar las letras

| Letra | Significado |
| --- | --- |
| S | Sinergia excepcional incluida en la tabla editorial |
| A | Mecánica afín al perfil del campeón |
| B | Utilidad general según las reglas actuales |
| C | Mecánica con poca afinidad para el perfil |
| ? | Lectura de título ambigua o incompleta |

Los tiers se calculan con reglas de sinergia. Dos o más cartas pueden tener la misma letra. Las capturas usadas por el OCR se procesan en memoria. La app se comunica con el juego por HTTPS local y guarda las calibraciones en `%LOCALAPPDATA%/AumentosLoL`.

En Arena, el overlay queda oculto cuando aparecen cartas de objetos.

## Historial de aumentos

Abre **Abrir historial de aumentos** desde el icono de la bandeja. El archivo CSV se guarda en `%LOCALAPPDATA%\AumentosLoL\historial-aumentos.csv` y se puede abrir en Excel o cualquier lector de CSV.

Cada fila guarda la carta ofrecida, su posición, el tier mostrado, el campeón, el modo, el parche del catálogo y la fecha. Las opciones reconocidas al cambiar una carta se agregan a la misma selección. Las filas marcadas como **Captura** contienen tiers recalculados a partir de la imagen con las reglas vigentes al importarla.

## Construir y actualizar

Con el SDK de .NET 10, ejecuta `powershell -File scripts/Publicar.ps1`. El script descarga el modelo español si falta y publica en `dist/win-x64`.

Para actualizar el catálogo, ejecuta `powershell -File scripts/Get-Assets.ps1 -Patch 16.23 -ChampionVersion 16.23.1` con las versiones nuevas. Revisa los cambios de mecánicas y las excepciones en `src/Aumentos.App/Assets/ratings.json`, corre las pruebas y vuelve a publicar. El overlay se suspende si detecta que el parche del juego difiere del catálogo.

Ejecuta `dotnet build AumentosLoL.slnx -c Release` y luego `dotnet tests/Aumentos.Tests/bin/Release/net10.0-windows10.0.19041.0/Aumentos.Tests.dll`. Agrega `--caos evidencias/captura.png --arena evidencias/image.png --items evidencias/image2.png` para probar las tres referencias, `--capture-smoke` para comprobar la captura de ventanas y `--live` durante una partida de uno de los dos modos para comprobar la API.

[Riot Developer Portal](https://developer.riotgames.com/docs/lol) · [CommunityDragon](https://communitydragon.org/docs/assets)

Aumentos LoL no cuenta con el respaldo de Riot Games ni representa sus opiniones. Riot Games y sus propiedades asociadas son marcas de Riot Games, Inc.
