<p align="center"><img src="assets/dungeon-forge-logo.svg" alt="Dungeon Forge logo" width="200"></p>

# Dungeon Forge

A Discord bot (C#, **.NET 10**, console application, [Discord.Net](https://discordnet.dev) 3.20) that organizes raids and dungeons.
A raid/dungeon is an *event* at a given date and time for 5, 10, 20 … players. Players sign up with a
role (Tank, Heal, …) just by reacting to the event message. The bot keeps the event message up to date.

The bot is generic: dungeons, roles, emoji, channel and Discord role names are all configuration. It was written for
"WoW Forever" style raids, but nothing in the code is game specific.

## What it does

* `/dfcreateevent <event> <date> <time>` (e.g. `/dfcreateevent MoltenCore40 24.10.2026 18:00`) creates a **standalone public thread**
  in the **dungeon-forge** channel (named like `Molten Core – 24.10.2026 18:00`) and posts the event embed **inside that thread**.
  The channel itself only lists threads. Discord also shows a small "thread created" system line in the parent channel for each thread; the bot leaves that line alone.
  Optional `awardpoints` turns raid points on or off (default **on** for raids). Dungeons cannot award points.
* The embed shows display name, date/time (as Discord timestamp markup, so everybody sees their own time zone),
  duration, required slots per role, current sign-ups per role (user mentions), Bench, Cant attend, a counter like `17/20`,
  whether **Points** are enabled, and (for point raids) the bosses killed so far. A finished event shows **Completed**.
* The bot adds one reaction per role (🛡️ 💚 ⚔️ 🏹) plus 🪑 *Bench* and ❌ *Cant attend* on the message inside the thread. Players sign up by clicking
  a reaction. **One role per player per event**: choosing another role moves the player (the old reaction is removed),
  removing the reaction signs off. Bench and Cant attend do not count towards the required attendees.
* Only members with the Discord role **df-dungeoneer** can sign up; other reactions are removed and noted in the log.
* Events are persisted (`Data/events.json`). After a restart the bot reloads them, joins/unarchives their threads when it needs to, and reconciles the reactions that exist
  on the Discord messages with the stored sign-ups (people may have reacted while the bot was offline).
  Events saved by an older version (embed posted in the channel, thread attached to that message) keep working as before.
* The parent channel stays free of chat: top-level messages from users are deleted (configurable). Inside event threads, non-bot messages are deleted too (`deleteStrayThreadMessages`, default **on**). If the permission is missing, the bot logs one warning and continues.
* At most `maxOpenEvents` events can be **open** at once (default 10). Completed events do not count. `/dflistevents` shows a counter like `3/10 open events`.
* Raid points: during the raid a manager records each boss with `/dfbosskill` (that stores who was signed up, who was benched, and whether the raid was full). `/dfcompleteevent` pays those snapshots once. The ledger is `Data/points.json`. A **dungeon-forge-points** channel shows a leaderboard and the points for every raid boss.
* Everything important is logged to the console and to the **dungeon-forge-log** channel.

### Commands (guild commands, registered for your `guildId` on startup)

| Command | Description |
| --- | --- |
| `/dfcreateevent event date time awardpoints` | Creates an event inside its own thread. `event` is the template name (autocomplete searches `EventConfigurations` and every subfolder; the label looks like `Molten Core (Raid, 40)` and the value is the name, e.g. `MoltenCore40`). At most 25 matches, raids first then dungeons, each alphabetical by display name. `date` = `dd.MM.yyyy`, `time` = `HH:mm` (24h) in the configured time zone. `awardpoints` is optional: default **true** for raids. Passing true for a dungeon is an error. Passing false on a raid creates it without points. Rejected when there are already `maxOpenEvents` open events. |
| `/dfcancelevent event` | Cancels an event (marks the message cancelled, clears reactions, archives + locks the thread). Autocomplete lists stored events, including completed ones. Points already paid stay in the ledger. |
| `/dfcompleteevent event` | Marks an open event **Completed**. For a points raid, pays every recorded kill from that kill's roster snapshot (see below) and posts a summary (bosses, and each player with points and how many kills they were paid for). A second call says it is already completed and does not pay again. Zero killed bosses completes and pays nothing. A non-points event is only marked done. |
| `/dfbosskill event boss` | Records a boss kill on an open points raid. `event` autocompletes open points events; `boss` autocompletes that raid's bosses that are not killed yet. Stores the kill and the roster at that moment. No points are paid yet. |
| `/dfundobosskill event boss` | Removes a recorded kill while the event is still open. |
| `/dfeditkill event boss user action` | While the event is open, `action` is `add` or `remove`: add or remove a player on that kill's attendee list. Adding someone who was on that kill's bench takes them off the bench for that kill. |
| `/dfplayerleft event user` | From now on, leave that player out of future kill snapshots for this event, even if they are still signed up. |
| `/dfplayerjoined event user` | Removes that exclusion. The player must be signed up with a normal role or Bench; otherwise the command explains why. Past kills are unchanged. |
| `/dflistevents` | Lists stored events with links, open/completed, and points on or off. Shows `x/10 open events` (the limit comes from `maxOpenEvents`). |
| `/dflisteventtemplates` | Lists every template: a **Raids** section, then a **Dungeons** section, each alphabetical by display name. Shows the name (the value for `/dfcreateevent`), display name, attendiesRequired, durationInHours and the per-role counts (e.g. `2 Tank, 4 Heal`). Invalid files are listed under **Invalid templates** with the error and do not abort the command. |
| `/dflistraidtemplates` | Lists raid templates only, alphabetical by display name. Same fields as `/dflisteventtemplates`. |
| `/dflistdungeontemplates` | Lists dungeon templates only, alphabetical by display name. Same fields as `/dflisteventtemplates`. |
| `/dfpoints user` | Total points and recent ledger lines. `user` is optional (default: you). |
| `/dfleaderboard` | Top 20 totals. |
| `/dfhelp` | Lists every Dungeon Forge command with its parameters and a one-line description. Commands that need the manager role are marked **manager-only**. |

Commands only work in the **dungeon-forge-manage** channel. Answers are ephemeral (only you see them).
Manager commands need the **df-manager** role. `/dfpoints` and `/dfleaderboard` can also be used by **df-dungeoneer** (managers can use them too).
Errors are explained: unknown event file, bad date/time, date in the past, wrong channel, missing role, dungeon with points requested, too many open events, boss already killed, event already completed.

### Points workflow

1. Create a raid with points left on (the default), or pass `awardpoints: false` if this run should not pay.
2. Players sign up by reacting, as usual.
3. During the raid, a manager runs `/dfbosskill` after each boss. The bot snapshots normal-role sign-ups and the bench, and whether the raid was full (normal-role sign-ups ≥ `attendiesRequired`, counting people who are still signed up even if a manager has excluded them).
4. Someone who leaves: `/dfplayerleft` so later kills skip them, or they remove their reaction (then they are simply not in the next snapshot). `/dfeditkill` fixes one kill's attendee list. `/dfplayerjoined` puts a signed-up player back into future snapshots.
5. `/dfcompleteevent` pays once. Each attendee on a kill gets that boss's points. Each benched player gets `benchPointsPercent` of that boss's points (nearest integer, 0.5 away from zero, minimum 0) **only if the raid was full at that kill**. Cant attend gets nothing. A player who was present for two kills and then left is paid only for those two.
6. Boss points come from `pointsPerBossKill` in `global.json` (default 10). A template boss may set its own `"points"` to override that. The value used is the one in effect when the event is completed.

A completed event ignores new sign-up reactions (a new reaction is removed). When `archiveCompletedEventThreads` is on (the default), the thread is archived and locked; if Discord refuses, the bot logs a warning and the event is still completed.

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
That set is what the bot needs to create a standalone thread, post and edit the embed inside it, add and remove reactions, and write in the log, manage, and points channels.
(Scopes: `bot applications.commands`.)

### 3. Prepare roles and channels on your server
Roles (names are configurable in `global.json`):

| Role | Purpose |
| --- | --- |
| `df-dungeoneer` | may sign up/off by reacting to events – give it to your raiders |
| `df-manager` | may use the manager slash commands (`/dfhelp` lists them) |
| `df-logs` | may see the log channel |

Text channels (names or ids are configurable):

| Channel | Purpose | Recommended permissions |
| --- | --- | --- |
| `dungeon-forge` | one standalone public thread per event; the embed and the reactions live **inside** the thread. The channel listing is the threads. Discord also posts a short "thread created" system line in this channel. | `@everyone`: **deny View Channel**. `df-dungeoneer`: **allow** View Channel, Read Message History, Add Reactions; **deny** Send Messages, **deny** Send Messages in Threads, **deny** Create Public Threads. Bot: **allow** View Channel, Send Messages, Embed Links, Add Reactions, Read Message History, Manage Messages, Manage Threads, Create Public Threads, Send Messages in Threads. |
| `dungeon-forge-manage` | managers use the slash commands (dungeoneers may use `/dfpoints` and `/dfleaderboard` here) | `@everyone`: deny View Channel. `df-manager`: allow View Channel, Send Messages, Use Application Commands. `df-dungeoneer`: allow View Channel, Use Application Commands (Send Messages only if you want them to chat here). Bot: allow View Channel, Send Messages. |
| `dungeon-forge-log` | log messages from the bot | `@everyone`: **deny View Channel**. `df-logs`: **allow** View Channel, Read Message History; deny Send Messages. Bot: allow View Channel, Send Messages. |
| `dungeon-forge-points` | leaderboard and the raid boss-point table, edited in place | `@everyone`: **deny View Channel**. `df-dungeoneer`: **allow** View Channel, Read Message History; **deny** Send Messages. Bot: **allow** View Channel, Send Messages, Manage Messages, Embed Links, Read Message History. |

Notes on "nobody can chat in dungeon-forge":
* The permission setup above is the reliable way: players can only look at the events and react. Deny **Send Messages**, **Send Messages in Threads**, and **Create Public Threads** for `df-dungeoneer`.
* Discord shows a small "thread created" system line in the parent channel for each thread. The bot leaves those lines alone.
* As a safety net the bot deletes top-level user messages in that channel (`deleteStrayMessages`, default **on**, needs *Manage Messages*; if the permission is missing it logs one warning).
* Inside event threads, non-bot messages are deleted when `deleteStrayThreadMessages` is on (the default). A missing permission fails the same way: one warning, then the bot continues.
* `"lockEventThreads": true` locks each new thread so only moderators (Manage Threads) can post. The default is `false`; the dungeoneer permission denies above are what stop chat.
* A permission overwrite on the bot role is needed in the `dungeon-forge` channel if you deny Send Messages for `@everyone`.

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
    "log":    { "name": "dungeon-forge-log",    "id": 0 },
    "points": { "name": "dungeon-forge-points", "id": 0 }  // missing or still a placeholder: warn and skip, do not abort startup
  },
  "roles": {                           // id 0 = match by name (case-insensitive)
    "dungeoneer": { "name": "df-dungeoneer", "id": 0 },
    "manager":    { "name": "df-manager",    "id": 0 },
    "logs":       { "name": "df-logs",       "id": 0 }
  },
  "deleteStrayMessages": true,         // delete top-level user messages in the events channel; keep "thread created" system lines
  "deleteStrayThreadMessages": true,   // delete non-bot messages inside event threads
  "lockEventThreads": false,           // lock new event threads (Manage Threads)
  "pointsPerBossKill": 10,             // default points for a boss that has no "points" override; must be > 0
  "benchPointsPercent": 25,            // 0..100; benched players get this percent of each kill, only when the raid was full at that kill
  "archiveCompletedEventThreads": true,// archive + lock the thread on /dfcompleteevent
  "maxOpenEvents": 10,                 // open events only; at least 1
  "logToDiscord": true,
  "dataDirectory": "Data",             // events.json, points.json, points-channel.json
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
Configuration files are copied to the output directory on every build (`bin/Debug/net10.0/Configurations` and `EventConfigurations`, with `Raids` and `Dungeons` kept as subfolders), so edit them in the project and rebuild/restart.
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

> Note: persisted state lives next to the executable (`bin/Debug/net10.0/Data` when started from Visual Studio): `events.json` (events, kill snapshots, exclusions), `points.json` (append-only ledger), and `points-channel.json` (the two board message ids). Set `dataDirectory` to an absolute path if you want it somewhere stable. Run the published build for production use (`dotnet publish -c Release`).

## Adding a dungeon / raid

Put a raid in `src/DungeonForge/EventConfigurations/Raids/<name>.json` and a dungeon in `src/DungeonForge/EventConfigurations/Dungeons/<name>.json`. A file directly in `EventConfigurations` (or in any other subfolder, at any depth) is still loaded. The file name (without `.json`) **must equal** `name`, and that name must be unique across all folders.

```json
{
  "name": "MoltenCore40",
  "type": "Raid",
  "displayName": "Molten Core",
  "attendiesRequired": 40,
  "attendyPerRole": { "Tank": 4, "Heal": 9, "Melee-DPS": 13, "Range-DPS": 14 },
  "dateTime": "2026-10-24T18:00:00",
  "durationInHours": 4,
  "bosses": [
    { "name": "Lucifron" },
    { "name": "Ragnaros", "points": 15 }
  ]
}
```

* `type` is required: `"Raid"` or `"Dungeon"` (any capitalization). When the file's folder is named `Raids` or `Dungeons`, `type` must match that folder.
* `attendyPerRole` keys must exist in `subscription-roles.json`; counts must be positive and **add up to `attendiesRequired`**. Only roles listed here get a reaction on the event.
* `bosses` is required for a raid and must list at least one boss. Names are unique (case-insensitive) and are the encounter order used by `/dfbosskill`. `"points"` is optional: omit it to use `pointsPerBossKill` from `global.json` (default 10). A set value must be an integer greater than 0 and overrides the global default for that boss only. The shipped raid templates list names only, so they all use the global default. A dungeon must omit `bosses` or leave it empty.
* `dateTime` is only a template/default. The real date and time come from `/dfcreateevent`.
* `durationInHours` may have decimals (`1.5`).
* Files are validated when used (and checked at startup); the error messages tell you exactly what is wrong. The shipped templates are listed below.
* `/dfcreateevent` takes the template **name** (for example `MoltenCore40`) and finds it in any subfolder. Autocomplete shows the type and size, for example `Molten Core (Raid, 40)`.
* `/dflisteventtemplates`, `/dflistraidtemplates` and `/dflistdungeontemplates` (manage channel, `df-manager`) list those templates. Raids come first, then dungeons; each section is alphabetical by display name. The **name** is what you pass to `/dfcreateevent`. A file that fails validation is shown in an **Invalid templates** section with the reason, instead of failing the command. A long list is split across messages so each one stays within Discord's length limit, and a section heading is kept with the lines that follow it. `/dfhelp` lists every command the same way.

Shipped templates (the role splits and durations are editable defaults):

| Name | Type | Display name | Players | Duration (hours) | Bosses |
| --- | --- | --- | --- | --- | --- |
| `AhnQirajTemple40` | Raid | Ahn'Qiraj Temple | 40 | 5 | 9 |
| `BlackfathomDeeps5` | Dungeon | Blackfathom Deeps | 5 | 1.5 | — |
| `BlackrockDepths5` | Dungeon | Blackrock Depths | 5 | 3 | — |
| `BlackrockSpire10` | Dungeon | Blackrock Spire | 10 | 3 | — |
| `BlackwingLair40` | Raid | Blackwing Lair | 40 | 4 | 8 |
| `CityOfDalaran5` | Dungeon | City of Dalaran | 5 | 1.5 | — |
| `Deadmines5` | Dungeon | Deadmines | 5 | 1 | — |
| `DireMaul5` | Dungeon | Dire Maul | 5 | 2 | — |
| `ExcavationSiteWetlands5` | Dungeon | Excavation Site: Wetlands | 5 | 1 | — |
| `Gnomeregan5` | Dungeon | Gnomeregan | 5 | 1.5 | — |
| `HallOfThanes5` | Dungeon | The Hall of Thanes | 5 | 1 | — |
| `Maraudon5` | Dungeon | Maraudon | 5 | 2 | — |
| `MoltenCore40` | Raid | Molten Core | 40 | 4 | 10 |
| `Naxxramas40` | Raid | Naxxramas | 40 | 6 | 15 |
| `OnyxiasLair40` | Raid | Onyxia's Lair | 40 | 2 | 1 |
| `RagefireChasm5` | Dungeon | Ragefire Chasm | 5 | 1 | — |
| `RazorfenDowns5` | Dungeon | Razorfen Downs | 5 | 1.5 | — |
| `RazorfenKraul5` | Dungeon | Razorfen Kraul | 5 | 1.5 | — |
| `RuinsOfAhnQiraj20` | Raid | Ruins of Ahn'Qiraj | 20 | 3 | 6 |
| `RuinsOfLordaeron5` | Dungeon | Ruins of Lordaeron | 5 | 1 | — |
| `ScarletMonastery5` | Dungeon | Scarlet Monastery | 5 | 2 | — |
| `Scholomance5` | Dungeon | Scholomance | 5 | 2 | — |
| `ShadowfangKeep5` | Dungeon | Shadowfang Keep | 5 | 1.5 | — |
| `StormwindStockade5` | Dungeon | Stormwind Stockade | 5 | 1 | — |
| `Stratholme5` | Dungeon | Stratholme | 5 | 2 | — |
| `SunkenTemple5` | Dungeon | Sunken Temple | 5 | 2 | — |
| `Uldaman5` | Dungeon | Uldaman | 5 | 1.5 | — |
| `WailingCaverns5` | Dungeon | Wailing Caverns | 5 | 1.5 | — |
| `ZulFarrak5` | Dungeon | Zul'Farrak | 5 | 1.5 | — |
| `ZulGurub20` | Raid | Zul'Gurub | 20 | 3 | 10 |

Raid boss names (encounter order, no per-boss point overrides): Molten Core — Lucifron, Magmadar, Gehennas, Garr, Shazzrah, Baron Geddon, Sulfuron Harbinger, Golemagg the Incinerator, Majordomo Executus, Ragnaros. Blackwing Lair — Razorgore the Untamed, Vaelastrasz the Corrupt, Broodlord Lashlayer, Firemaw, Ebonroc, Flamegor, Chromaggus, Nefarian. Onyxia's Lair — Onyxia. Zul'Gurub — High Priestess Jeklik, High Priest Venoxis, High Priestess Mar'li, Bloodlord Mandokir, Edge of Madness, Gahz'ranka, High Priest Thekal, High Priestess Arlokk, Jin'do the Hexxer, Hakkar. Ruins of Ahn'Qiraj — Kurinnaxx, General Rajaxx, Moam, Buru the Gorger, Ayamiss the Hunter, Ossirian the Unscarred. Ahn'Qiraj Temple — The Prophet Skeram, Bug Trio, Battleguard Sartura, Fankriss the Unyielding, Viscidus, Princess Huhuran, The Twin Emperors, Ouro, C'Thun. Naxxramas — Anub'Rekhan, Grand Widow Faerlina, Maexxna, Noth the Plaguebringer, Heigan the Unclean, Loatheb, Instructor Razuvious, Gothik the Harvester, The Four Horsemen, Patchwerk, Grobbulus, Gluth, Thaddius, Sapphiron, Kel'Thuzad.

Dungeons announced for WoW Forever but not yet in the game (Alcaz Prison, Blackmaw Hold, Drowned City, Krol'dok Stronghold, Shaper's Terrace) and the raids Barrow Deeps and Hyjal Summit are not added yet, because they have no published data.

New files are picked up without restarting the bot only if they are in the *running* output directory – otherwise rebuild.

## Project layout

```
DungeonForge.sln
src/DungeonForge/
  Program.cs                      Generic Host + DI wiring, graceful config errors
  Configurations/                 global.json, subscription-roles.json, fixed-subscription-roles.json
  EventConfigurations/            Raids/*.json and Dungeons/*.json (a file in the root is allowed too)
  Configuration/                  config models, BotConfiguration, ConfigurationException
  Models/                         EventDefinition + BossDefinition, EventType, EventStatus,
                                  EventInstance + Signup, BossKillRecord, PointsLedgerEntry, RoleDefinition
  Services/                       ConfigurationLoader, EventDefinitionRepository(+Validator), EventStore,
                                  EventRenderer (embed), SignupLogic, EventMessagePolicy, OpenEventLimit,
                                  PointsLogic, PointsSummaryFormatter, PointsBoardFormatter,
                                  PointsStore (Data/points.json), PointsChannelStore, PointsBoardService,
                                  EventDateParser, EventTemplateListFormatter, EventTemplateAutocomplete,
                                  EventService (Discord orchestration), BotLog, EventWorkQueue
  Bot/                            BotService (client, gateway handlers, startup), EventCommands (slash commands + autocomplete),
                                  CommandHelpFormatter, DiscordHelpers, EmojiKey, ResolvedChannels
tests/DungeonForge.Tests/         xUnit tests for the pure logic (run: dotnet test)
```

## Behaviour details / limits

* Gateway intents: `Guilds`, `GuildMembers` (privileged), `GuildMessages`, `GuildMessageReactions`. No `MessageContent`.
* Each new event is a standalone public thread in the events channel (`CreateThreadAsync` with no parent message, `ThreadType.PublicThread`). The auto-archive duration is one week, then three days, then one day, if the guild rejects the longer value. The embed, the role reactions, and the sign-up updates are a message inside that thread. The parent channel only lists threads, plus Discord's "thread created" system line.
* Events already stored with the embed in the parent channel (`embedInThread` missing or false) stay on that layout: reactions, edits, cancel, and completion still use that channel message.
* The bot unarchives a thread (and joins it) when it needs to edit the embed or reconcile reactions. Completing an event archives and locks the thread when `archiveCompletedEventThreads` is on.
* Reactions on messages that are not in the cache are handled (reaction events are processed by message id and are serialized in arrival order). A completed event drops new sign-up reactions.
* Role slots are *targets*, not hard limits: more people than slots may sign up for a role (the embed shows e.g. `3/2`) so managers can decide. The counter `x/y` counts all signed-up role members.
* Open events count toward `maxOpenEvents`. Completed events stay until `finishedEventRetentionHours` after they ended, and `/dflistevents` still shows them. Cancelled events are removed immediately; points already written to `points.json` stay. If an event message is deleted in Discord the event is removed from the state.
* Boss points are taken from the template and `pointsPerBossKill` at completion time. A missing template or a renamed boss falls back to `pointsPerBossKill`.
* The points channel is optional at startup: if it is missing, the bot logs a warning and skips the leaderboard and the boss table. Both messages are edited in place; deleted ones are recreated.
* The bot must have been able to *see* a channel to find it by name; if channel lookup by name is ambiguous the first match wins – use ids then.

## Development

```
dotnet build
dotnet test
```

## Branding

The Discord app icon file is assets/dungeon-forge-discord-icon-1024.png.
