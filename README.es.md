# Hearthtalk — diálogos con IA para Going Medieval

*[English](README.md)* · por **CarlosNahuelcoy**, autor de [EchoColony](https://github.com/CarlosNahuelcoy/EchoColony) (RimWorld)

Tus aldeanos hablan de verdad:

- Cada charla que el juego genera entre colonos («Lambert habló sobre peregrinajes con Aethelman, que sonaba interesado») se convierte en un diálogo real con IA, en burbujas sobre sus cabezas.
- Todo queda en un registro en vivo.
- Puedes hablar de tú a tú con cualquier colono o reunir a varios.
- Ellos también vienen a hablarte por su cuenta.
- Lo que les dices cambia su ánimo, su trabajo y su horario.

## Descarga

**[Descargar la última versión](https://github.com/CarlosNahuelcoy/Hearthtalk/releases/latest)**:
- **Hearthtalk and BepInEx**: todo listo. Es lo normal.
- **Hearthtalk**: solo el mod, si ya tienes BepInEx 5.

> El objeto del Steam Workshop es solo un aviso. El mod carga código, y el Workshop del juego solo admite mods de datos, así que se descarga desde aquí.

## Instalación

1. En Steam: clic derecho en Going Medieval → *Administrar* → *Ver archivos locales*.
2. Extrae **todo** el zip en esa carpeta, de modo que `winhttp.dll` y la carpeta `BepInEx` queden junto a `Going Medieval.exe`.
3. Abre el juego y carga tu aldea. En la pestaña **HEARTHTALK** → *Ajustes*, elige el proveedor de IA. Player2 es gratis y viene por defecto.
4. **Recomendado:** activa *Opciones → Gráficos → Ejecutar en segundo plano*. Si no, el juego se congela al cambiar de ventana y las respuestas esperan hasta que vuelvas.

No actives nada en el menú *Mods* del juego, porque Hearthtalk lo carga BepInEx.

En Linux o Steam Deck (Proton), añade `WINEDLLOVERRIDES="winhttp=n,b" %command%` a las opciones de lanzamiento.

**Actualizar:** reemplaza `BepInEx/plugins/Hearthtalk.dll`.

## Desinstalación

| Para quitar | Borra |
|---|---|
| Solo el mod | `BepInEx/plugins/Hearthtalk.dll` |
| Sus datos: chats, recuerdos, ajustes y tu clave de IA | `BepInEx/config/Hearthtalk/` y `BepInEx/config/hearthtalk.cfg` |
| También BepInEx | `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt` y la carpeta `BepInEx` |

Tus partidas cargan igual sin el mod. El juego descarta en silencio los efectos de ánimo que ya no conoce.

## Dónde está cada cosa

| Dónde | Qué |
|-------|-----|
| Pestaña **HEARTHTALK** (barra superior) | Registro en vivo de lo que los colonos dicen entre ellos: diálogos, pensamientos y comentarios del cronista. Abajo están **Reunión**, **Cronista** y **Ajustes** |
| Botón **Hablar** del panel del colono | Chat de tú a tú (con varios colonos seleccionados, una reunión). Abajo están **Personalidad**, **Recuerdos**, **Reunión**, **Pausa** y **Borrar chat**. Cada mensaje se puede editar, borrar o regenerar |
| Aviso en la lista de la derecha | «X quiere hablar contigo»: un colono vino por su cuenta. Haz clic para abrir su chat |
| **Esc** | Cierra la ventana de Hearthtalk que esté encima |

## Funciones

**Diálogos entre colonos.** Cada charla del juego (tema, tono y reacción) se convierte en un diálogo de 2 a 8 líneas, con burbujas y registro.

**Pensamientos.** Cuando a un colono le pasa algo (una herida, una pelea…), a veces murmura algo en una burbuja.

**Chat de tú a tú.** El colono te habla como a su señor, con su historia, ánimo, necesidades, relaciones, trabajos, horario y recuerdos.

**Mensajes privados.** Los colonos vienen a hablarte solos:
- ante incursiones, comerciantes, visitas, clima o plagas;
- cuando les pasa algo;
- cuando están hambrientos, agotados, heridos o al borde del colapso;
- de vez en cuando, sin motivo especial.

Puedes decidir que no te molesten. Aparece un aviso en la lista nativa del juego y el mensaje queda en su chat marcado con «vino a hablarte».

**Influir en el ánimo.** Si le hablas con cariño, gratitud o una promesa, o al revés con desprecio o amenazas, el colono recibe un pensamiento visible en su panel:
- Positivos: «Una buena charla con el señor» (+3), «Animado por el señor» (+6), «Inspirado por el señor» (+10).
- Negativos: «Palabras duras del señor» (−3), «Reprendido por el señor» (−6), «Humillado por el señor» (−10).

Dura de 1 a 2 días. Como mucho hay un cambio cada 12 horas de juego por colono, y uno nuevo reemplaza al anterior.

**Órdenes.** Puedes pedirle a un colono que se dedique al campo, que deje la mina o que descanse por la noche. Si acepta, cambian sus prioridades de trabajo o su horario, igual que desde los paneles del juego. Puede negarse según su carácter, salud o ánimo. Debajo de su respuesta ves qué cambió (→).

**Memoria.** Cada noche, los colonos que hablaron ese día escriben un recuerdo breve. Los últimos 7 días entran en sus conversaciones. En *Recuerdos* puedes leerlos, escribir el de hoy o borrarlos.

**Reunión.** Habla con varios a la vez. Si seleccionas varios colonos en el juego, se reúnen esos; si no, el colono y los que están cerca. Responden, discuten entre ellos y pueden aceptar órdenes.

**Cronista.** Es un narrador que lo ha visto todo:
- Comenta los eventos en el registro.
- Puedes charlar con él.
- Si activas *Puede provocar eventos*, trae lo que le pidas: comerciantes, visitas, regalos, clima, plagas o ataques. Solo trae ataques si los pides claramente.
- Su personalidad se puede editar.

**Personalidad.** Puedes escribir la personalidad de cada colono, que tiene prioridad sobre todo lo demás, e instrucciones globales en Ajustes.

**Exportar e importar.** *Exportar a texto* guarda todos los chats, recuerdos y el registro en un .txt. *Importar de otra aldea* copia el chat, la personalidad y los recuerdos de los colonos con el mismo nombre de otra partida.

## Proveedores

- **Player2** (por defecto, gratis): si la app de Player2 está abierta, se conecta sola. Si no, pulsa *Conectar con el navegador* e introduce el código.
- **Gemini**: API key de Google AI Studio (tiene capa gratuita). Modelo por defecto: `gemini-2.5-flash`.
- **OpenRouter**: API key y modelo.
- **Local / OpenAI**: LM Studio (`http://localhost:1234/v1/chat/completions`), Ollama (`http://localhost:11434/v1/chat/completions`), KoboldCpp o cualquier servidor compatible.

## Coste y frecuencia

Con 3 colonos, el juego genera unas 9 charlas por día de juego. *Pausa mínima entre diálogos IA* (20 s) limita las llamadas. Los mensajes privados tienen su propio límite: 4 minutos reales entre mensajes y 24 horas de juego por colono. Los recuerdos hacen una llamada por colono que habló, una vez al día de juego.

## Dónde se guarda todo

`BepInEx/config/hearthtalk.cfg`: ajustes, editables a mano.

`BepInEx/config/Hearthtalk/villages/<aldea>/`:
- `chatlog.jsonl`: el registro.
- `chat_*.json`: los chats.
- `persona_*.txt`: las personalidades.
- `memories_*.json`: los recuerdos.
- `storyteller.json`: el chat con el cronista.
- `export_*.txt`: las exportaciones.

`BepInEx/config/Hearthtalk/Effectors.json`: los efectos de ánimo del mod. Se regeneran solos.

`BepInEx/config/Hearthtalk/debug.log`: prompts y respuestas, si activas la depuración.

## Compilar

```bash
dotnet build -c Release
```

Requiere el juego en `D:\SteamLibrary\steamapps\common\Going Medieval`. Si está en otro sitio, cambia `GameDir` en el `.csproj`.

## Licencia

MIT con requisitos de integración de Player2 (ver [LICENSE](LICENSE)). Incluye [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.23.2 (LGPL-2.1) en el zip *Hearthtalk and BepInEx*.
