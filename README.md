<p align="center"><img src="assets/dungeon-forge-logo.svg" alt="Dungeon Forge logo" width="200"></p>

# Dungeon Forge

A Discord bot (C#, **.NET 10**, console application, [Discord.Net](https://discordnet.dev) 3.20) that organizes raids and dungeons.
A raid/dungeon is an *event* at a given date and time for 5, 10, 20 … players. Players sign up with a
role (Tank, Heal, …) just by reacting to the event message. The bot keeps the event message up to date.

The bot is generic: dungeons, roles, emoji, channel and Discord role names are all configuration. It was written for
"WoW Forever" style raids, but nothing in the code is game specific.

## What it does

* `/dfcreateevent <event> <date> <time>` (e.g. `/dfcreateevent MoltenCore40 24.10.2026 18:00`) posts an embed in the
  **dungeon-forge** channel and opens a thread for it (named like `Molten Core – 24.10.2026 18:00`).
* The embed shows display name, date/time (as Discord timestamp markup, so everybody sees their own time zone),
  duration, required slots per role, current sign-ups per role (user mentions), Bench, Cant attend and a counter like `17/20`.
* The bot adds one reaction per role (🛡️ 💚 ⚔️ 🏹) plus 🪑 *Bench* and ❌ *Cant attend*. Players sign up by clicking
  a reaction. **One role per player per event**: choosing another role moves the player (the old reaction is removed),
  removing the reaction signs off. Bench and Cant attend do not count towards the required attendees.
* Only members with the Discord role **df-dungeoneer** can sign up; other reactions are removed and noted in the log.
* Events are persisted (`Data/events.json`). After a restart the bot reloads them and reconciles the reactions that exist
  on the Discord messages with the stored sign-ups (people may have reacted while the bot was offline).
* The event channel stays free of chat: stray messages from users are deleted (configurable).
* Everything important is logged to the console and to the **dungeon-forge-log** channel.

### Commands (guild commands, registered for your `guildId` on startup)

| Command | Description |
| --- | --- |
| `/dfcreateevent event date time` | Creates an event. `event` = file name in `EventConfigurations` (autocomplete), `date` = `dd.MM.yyyy`, `time` = `HH:mm` (24h) in the configured time zone. |
| `/dfcancelevent event` | Cancels an event (marks the message cancelled, clears reactions, archives + locks the thread). Autocomplete lists active events. |
| `/dflistevents` | Lists active events with links. |
| `/dflisteventtemplates` | Lists every template in `EventConfigurations`: name (the value for `/dfcreateevent`), display name, attendiesRequired, durationInHours and the per-role counts (e.g. `2 Tank, 4 Heal`). Invalid files are listed under **Invalid templates** with the error and do not abort the command. |

All commands only work in the **dungeon-forge-manage** channel and only for members with the **df-manager** role.
Answers are ephemeral (only you see them). Errors are explained: unknown event file, bad date/time, date in the past,
wrong channel, missing role.

## Setup

### 1. Create the bot in the Discord Developer Portal
1. <https://discord.com/developers/applications> → **New Application** → name it *Dungeon Forge*.
2. **Bot** tab → **Reset Token** → copy the token (you only see it once).
3. Same tab → **Privileged Gateway Intents**:
   * **Server Members Intent – must be ON** (needed to check the df-dungeoneer / df-manager roles).
   * Message Content Intent – *not* needed, leave it off.
   * Presence Intent – not needed.
4. Keep "Public Bot" off if only you want to invite it.

### 2. Invite the bot
Replace `YOUR_CLIENT_ID` with the *Application ID* (General Information tab):

```
https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&scope=bot%20applications.commands&permissions=326417869888
```

The permission integer `326417869888` contains: View Channels, Send Messages, Embed Links, Add Reactions, Use External Emojis,
Read Message History, Manage Messages, Create Public Threads, Send Messages in Threads, Manage Threads.
(Scopes: `bot applications.commands`.)

### 3. Prepare roles and channels on your server
Roles (names are configurable in `global.json`):

| Role | Purpose |
| --- | --- |
| `df-dungeoneer` | may sign up/off by reacting to events – give it to your raiders |
| `df-manager` | may create, cancel and list events |
| `df-logs` | may see the log channel |

Text channels (names or ids are configurable):

| Channel | Purpose | Recommended permissions |
| --- | --- | --- |
| `dungeon-forge` | events are posted here, one thread each | `@everyone`: **deny View Channel**. `df-dungeoneer`: **allow** View Channel, Read Message History, Add Reactions; **deny** Send Messages, Create Public/Private Threads, (Send Messages in Threads – see below). Bot: **allow** View Channel, Send Messages, Embed Links, Add Reactions, Read Message History, Manage Messages, Manage Threads, Create Public Threads, Send Messages in Threads. |
| `dungeon-forge-manage` | managers use the slash commands | `@everyone`: deny View Channel. `df-manager`: allow View Channel, Send Messages, Use Application Commands. Bot: allow View Channel, Send Messages. |
| `dungeon-forge-log` | log messages from the bot | `@everyone`: **deny View Channel**. `df-logs`: **allow** View Channel, Read Message History; deny Send Messages. Bot: allow View Channel, Send Messages. |

Notes on "nobody can chat in dungeon-forge":
* The permission setup above is the reliable way: players can only look at the events and react.
* As a safety net the bot deletes messages posted by users in that channel (`deleteStrayMessages`, default **on**, needs *Manage Messages*; if the permission is missing it just logs one warning).
* Threads: players can write in the event threads if they have *Send Messages in Threads*. Either deny that permission for `df-dungeoneer`, or set
  `"lockEventThreads": true` in `global.json` – then the bot locks each new thread so only moderators (Manage Threads) can post. The default is `false`, because a thread is usually wanted for event discussion.
* A permission *overwrite on the bot role* is needed in the `dungeon-forge` channel if you deny Send Messages for `@everyone`.

### 4. Configure
Open `src/DungeonForge/Configurations/global.json` and replace the placeholders:

```jsonc
{
  "token": "YOUR_BOT_TOKEN",          // or set the environment variable DUNGEONFORGE_TOKEN (preferred – never commit tokens)
  "guildId": 0,                        // your server id (Discord: Settings → Advanced → Developer Mode; right-click server → Copy Server ID)
  "timeZone": "Europe/Vienna",         // time zone used for /dfcreateevent date+time
  "channels": {                        // id 0 = look the channel up by name; a non-zero id takes precedence
    "events": { "name": "dungeon-forge",        "id": 0 },
    "manage": { "name": "dungeon-forge-manage", "id": 0 },
    "log":    { "name": "dungeon-forge-log",    "id": 0 }
  },
  "roles": {                           // id 0 = match by name (case-insensitive)
    "dungeoneer": { "name": "df-dungeoneer", "id": 0 },
    "manager":    { "name": "df-manager",    "id": 0 },
    "logs":       { "name": "df-logs",       "id": 0 }
  },
  "deleteStrayMessages": true,
  "lockEventThreads": false,
  "logToDiscord": true,
  "dataDirectory": "Data",
  "finishedEventRetentionHours": 24
}
```

The file allows `//` comments and trailing commas. If something is missing or still a placeholder the program prints what to fix and exits with code 1 (nothing crashes with a stack trace).

Other configuration files in `src/DungeonForge/Configurations/`:

* `subscription-roles.json` – the roles people can pick: `key` (used in event files), `displayName`, `emoji`. Initially Tank, Heal, Melee-DPS, Range-DPS.
* `fixed-subscription-roles.json` – always available, never counted towards required slots: *Bench* (standby) and *Cant attend* (declined).
* `emoji` is either a unicode emoji (`"🛡️"`) or a custom server emoji in Discord markup (`"<:tank:123456789012345678>"`; type `\:tank:` in Discord to get it). Every role needs its own emoji.

### 5. Run
**Visual Studio 2022 (17.14+) / 2026:** open `DungeonForge.sln`, set `DungeonForge` as startup project, press **F5**.
Configuration files are copied to the output directory on every build (`bin/Debug/net10.0/Configurations` and `EventConfigurations`), so edit them in the project and rebuild/restart.
To provide the token without touching `global.json`: *Debug → DungeonForge Debug Properties → Environment variables* → `DUNGEONFORGE_TOKEN=…`.

**Command line:**
```
dotnet run --project src/DungeonForge
# or with the token from the environment
DUNGEONFORGE_TOKEN=xxxxx dotnet run --project src/DungeonForge        # Linux/macOS
$env:DUNGEONFORGE_TOKEN="xxxxx"; dotnet run --project src/DungeonForge  # PowerShell
```
Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

On startup the console shows the loaded events/files and the bot posts "Dungeon Forge is online…" into the log channel.
Slash commands can take a few seconds to show up after the first start.

> Note: the persisted state lives in `Data/events.json` next to the executable (`bin/Debug/net10.0/Data` when started from Visual Studio). Set `dataDirectory` to an absolute path if you want it somewhere stable. Run the published build for production use (`dotnet publish -c Release`).

## Adding a dungeon / raid

Create `src/DungeonForge/EventConfigurations/<name>.json`. The file name (without `.json`) **must equal** `name`:

```json
{
  "name": "MoltenCore40",
  "displayName": "Molten Core",
  "attendiesRequired": 40,
  "attendyPerRole": { "Tank": 4, "Heal": 9, "Melee-DPS": 13, "Range-DPS": 14 },
  "dateTime": "2026-10-24T18:00:00",
  "durationInHours": 4
}
```

* `attendyPerRole` keys must exist in `subscription-roles.json`; counts must be positive and **add up to `attendiesRequired`**. Only roles listed here get a reaction on the event.
* `dateTime` is only a template/default. The real date and time come from `/dfcreateevent`.
* `durationInHours` may have decimals (`1.5`).
* Files are validated when used (and checked at startup); the error messages tell you exactly what is wrong. `MoltenCore40` is one of the shipped raids; the full list is in the table below.
* `/dflisteventtemplates` (manage channel, `df-manager`) lists those templates. The **name** is what you pass to `/dfcreateevent`. A file that fails validation is shown in an **Invalid templates** section with the reason, instead of failing the command. A long list is split across messages so each one stays within Discord's length limit.

Shipped templates (the role splits and durations are editable defaults):

| Name | Display name | Players | Duration (hours) |
| --- | --- | --- | --- |
| `AhnQirajTemple40` | Ahn'Qiraj Temple | 40 | 5 |
| `BlackfathomDeeps5` | Blackfathom Deeps | 5 | 1.5 |
| `BlackrockDepths5` | Blackrock Depths | 5 | 3 |
| `BlackrockSpire10` | Blackrock Spire | 10 | 3 |
| `BlackwingLair40` | Blackwing Lair | 40 | 4 |
| `CityOfDalaran5` | City of Dalaran | 5 | 1.5 |
| `Deadmines5` | Deadmines | 5 | 1 |
| `DireMaul5` | Dire Maul | 5 | 2 |
| `ExcavationSiteWetlands5` | Excavation Site: Wetlands | 5 | 1 |
| `Gnomeregan5` | Gnomeregan | 5 | 1.5 |
| `HallOfThanes5` | The Hall of Thanes | 5 | 1 |
| `Maraudon5` | Maraudon | 5 | 2 |
| `MoltenCore40` | Molten Core | 40 | 4 |
| `Naxxramas40` | Naxxramas | 40 | 6 |
| `OnyxiasLair40` | Onyxia's Lair | 40 | 2 |
| `RagefireChasm5` | Ragefire Chasm | 5 | 1 |
| `RazorfenDowns5` | Razorfen Downs | 5 | 1.5 |
| `RazorfenKraul5` | Razorfen Kraul | 5 | 1.5 |
| `RuinsOfAhnQiraj20` | Ruins of Ahn'Qiraj | 20 | 3 |
| `RuinsOfLordaeron5` | Ruins of Lordaeron | 5 | 1 |
| `ScarletMonastery5` | Scarlet Monastery | 5 | 2 |
| `Scholomance5` | Scholomance | 5 | 2 |
| `ShadowfangKeep5` | Shadowfang Keep | 5 | 1.5 |
| `StormwindStockade5` | Stormwind Stockade | 5 | 1 |
| `Stratholme5` | Stratholme | 5 | 2 |
| `SunkenTemple5` | Sunken Temple | 5 | 2 |
| `Uldaman5` | Uldaman | 5 | 1.5 |
| `WailingCaverns5` | Wailing Caverns | 5 | 1.5 |
| `ZulFarrak5` | Zul'Farrak | 5 | 1.5 |
| `ZulGurub20` | Zul'Gurub | 20 | 3 |

Dungeons announced for WoW Forever but not yet in the game (Alcaz Prison, Blackmaw Hold, Drowned City, Krol'dok Stronghold, Shaper's Terrace) and the raids Barrow Deeps and Hyjal Summit are not added yet, because they have no published data.

New files are picked up without restarting the bot only if they are in the *running* output directory – otherwise rebuild.

## Project layout

```
DungeonForge.sln
src/DungeonForge/
  Program.cs                      Generic Host + DI wiring, graceful config errors
  Configurations/                 global.json, subscription-roles.json, fixed-subscription-roles.json
  EventConfigurations/            one file per dungeon/raid
  Configuration/                  config models, BotConfiguration, ConfigurationException
  Models/                         EventDefinition (template), EventInstance + Signup (persisted), RoleDefinition
  Services/                       ConfigurationLoader, EventDefinitionRepository(+Validator), EventStore,
                                  EventRenderer (embed), SignupLogic (pure rules), EventDateParser,
                                  EventTemplateListFormatter (template list text),
                                  EventService (Discord orchestration), BotLog, EventWorkQueue
  Bot/                            BotService (client, gateway handlers, startup), EventCommands (slash commands + autocomplete),
                                  DiscordHelpers, EmojiKey, ResolvedChannels
tests/DungeonForge.Tests/         xUnit tests for the pure logic (run: dotnet test)
```

## Behaviour details / limits

* Gateway intents: `Guilds`, `GuildMembers` (privileged), `GuildMessages`, `GuildMessageReactions`. No `MessageContent`.
* Reactions on messages that are not in the cache are handled (reaction events are processed by message id and are serialized in arrival order).
* Role slots are *targets*, not hard limits: more people than slots may sign up for a role (the embed shows e.g. `3/2`) so managers can decide. The counter `x/y` counts all signed-up role members.
* Finished events are forgotten `finishedEventRetentionHours` after they ended. If an event message is deleted in Discord the event is removed from the state.
* The bot must have been able to *see* a channel to find it by name; if channel lookup by name is ambiguous the first match wins – use ids then.

## Development

```
dotnet build
dotnet test
```

## Branding

The Discord app icon file is assets/dungeon-forge-discord-icon-1024.png.
