# Hearthtalk — AI dialogue for Going Medieval

*[Español](README.es.md)* · by **CarlosNahuelcoy**, author of [EchoColony](https://github.com/CarlosNahuelcoy/EchoColony) (RimWorld)

Your villagers really talk:

- Every chat the game generates between colonists ("Lambert talked about pilgrimages with Aethelman, who sounded interested") becomes a real AI dialogue, shown in bubbles above their heads.
- Everything goes into a live log.
- You can talk one-on-one with any colonist, or gather several.
- They come to talk to you on their own.
- What you tell them changes their mood, their work and their schedule.

## Download

**[Download the latest release](https://github.com/CarlosNahuelcoy/Hearthtalk/releases/latest)**:
- **Hearthtalk and BepInEx**: everything included. Use this one.
- **Hearthtalk**: the mod only, if you already have BepInEx 5.

> The Steam Workshop item is only a pointer. Hearthtalk loads code, and the game's Workshop only supports data mods, so the mod is downloaded from here.

## Install

1. In Steam: right-click Going Medieval → *Manage* → *Browse local files*.
2. Extract **everything** from the zip into that folder, so that `winhttp.dll` and the `BepInEx` folder sit next to `Going Medieval.exe`.
3. Start the game and load your village. In the **HEARTHTALK** tab → *Settings*, pick the AI provider. Player2 is free and the default.
4. **Recommended:** turn on *Options → Graphics → Run in background*. Otherwise the game freezes when you alt-tab and replies wait until you return.

Don't enable anything in the game's *Mods* menu, because BepInEx loads Hearthtalk.

On Linux or Steam Deck (Proton), add `WINEDLLOVERRIDES="winhttp=n,b" %command%` to the launch options.

**Update:** replace `BepInEx/plugins/Hearthtalk.dll`.

## Uninstall

| To remove | Delete |
|---|---|
| Just the mod | `BepInEx/plugins/Hearthtalk.dll` |
| Its data: chats, memories, settings and your AI key | `BepInEx/config/Hearthtalk/` and `BepInEx/config/hearthtalk.cfg` |
| BepInEx too | `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt` and the `BepInEx` folder |

Your saves load fine without the mod. The game silently drops mood effects it no longer knows.

## Where things are

| Where | What |
|---|---|
| **HEARTHTALK** tab (top bar) | Live log of what colonists say to each other: dialogues, thoughts and the chronicler's comments. At the bottom are **Meeting**, **Chronicler** and **Settings** |
| Colonist panel **Talk** button | One-on-one chat (with several colonists selected, a meeting). At the bottom are **Personality**, **Memories**, **Meeting**, **Pause** and **Clear chat**. Every message can be edited, deleted or regenerated |
| Notice in the game's right-hand list | "X wants to talk to you": a colonist came to you on their own. Click it to open their chat |
| **Esc** | Closes the topmost Hearthtalk window |

## Features

**Colonist dialogue.** Each game chat (topic, tone and reaction) becomes a 2–8 line dialogue, with bubbles and a log entry.

**Thoughts.** When something happens to a colonist (a wound, a fight…), they sometimes mutter a line in a bubble.

**One-on-one chat.** The colonist speaks to you as their lord. They know their backstory, mood, needs, relationships, jobs, schedule and memories.

**Private messages.** Colonists come to you on their own:
- during raids, traders, visitors, weather or plagues;
- when something happens to them;
- when they are starving, exhausted, hurt or about to break;
- now and then, for no particular reason.

You can tell them not to bother you.

**Mood.** Kindness, gratitude or a promise, or the opposite (contempt or threats), gives the colonist a thought visible in their panel. It ranges from ±3 to ±10 and lasts 1–2 days, with at most one every 12 game hours.

**Orders.** You can ask a colonist to focus on farming, stop mining or rest at night. If they agree, their job priorities or schedule change, just like in the game's panels. They may refuse, depending on their character, health or mood.

**Memory.** Every night, colonists who talked that day write a short memory. The last 7 days go into their conversations.

**Meeting.** Talk to several colonists at once: the ones you selected, or the colonist and those nearby. They answer, argue with each other and can accept orders.

**Chronicler.** A narrator who has seen everything:
- comments on events in the log;
- can be chatted with;
- can trigger events if you allow it (traders, visitors, weather, raids).

**Personality.** You can write a personality for each colonist, plus global instructions in Settings.

**Export / import.** Export everything to a .txt file, or carry chats, personalities and memories over from another save. Colonists are matched by name.

## Providers

- **Player2** (default, free): connects automatically if the Player2 app is running. Otherwise press *Connect with browser*.
- **Gemini**: Google AI Studio API key (has a free tier).
- **OpenRouter**: API key and model.
- **Local / OpenAI-compatible**: LM Studio, Ollama, KoboldCpp…

## Data

- Settings: `BepInEx/config/hearthtalk.cfg`.
- Chats, memories and personalities: `BepInEx/config/Hearthtalk/villages/<village>/`.

## Build

```bash
dotnet build -c Release
```

This expects the game at `D:\SteamLibrary\steamapps\common\Going Medieval`. Change `GameDir` in the `.csproj` if yours is elsewhere.

## License

MIT with Player2 integration requirements (see [LICENSE](LICENSE)). The *Hearthtalk and BepInEx* zip bundles [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.23.2 (LGPL-2.1).
