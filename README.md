<p align="center"><img src="assets/dungeon-forge-logo.svg" alt="Dungeon Forge logo" width="200"></p>

# Dungeon Forge

A Discord bot (C#, **.NET 10**, console application, [Discord.Net](https://discordnet.dev) 3.20) that organizes raids and dungeons.
A raid/dungeon is an *event* at a given date and time for 5, 10, 20 … players. Players sign up with a
role (Tank, Heal, …) just by reacting to the event message. The bot keeps the event message up to date.

The bot is generic: dungeons, roles, emoji, channel and Discord role names are all configuration. It was written for
"WoW Forever" style raids, but nothing in the code is game specific.

## What it does

* `/dfcreateevent <event> <date> <time>` (e.g. `/dfcreateevent MoltenCore40 24.10.2026 18:00`) creates an event in the **dungeon-forge-forum** channel
  (named like `Molten Core – 24.10.2026 18:00`). That channel may be a normal text channel or a Discord **forum**.
  A text channel gets a standalone public thread and the embed is posted inside it. A forum gets one post per event: the post title is that same name, and the sign-up embed is the post's starter message (Discord uses the same id for the post and the starter message).
  The channel itself only lists threads or posts. In a text channel, Discord also shows a small "thread created" system line; the bot leaves that line alone.
  The default channel name used to be `dungeon-forge`. If the bot cannot find `dungeon-forge-forum` and that name is still the configured default, it logs a warning that tells you to rename the channel or set `channels.events` in `global.json`.
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
* Deleting an event thread, forum post, or sign-up message in Discord removes that event from the bot. Discord sends a thread-deleted event, not a delete for each message inside the thread, so the bot listens for the thread itself. The log line keeps its wording and appends who did it (`by @user`, or `(deleter unknown)`). A thread or forum post is matched to a thread-delete audit entry (the entry carries the thread id). A sign-up message is matched to a message-delete entry in that channel whose author is the bot; Discord's message-delete entry does not include the message id, so the newest row from the last 15 seconds is used. A forum starter shares its id with the post, so that removal also accepts the thread-delete entry. The event is saved as removed first. The audit read then retries three times, about a second and a half apart, and does not block other event work. Discord writes no audit entry when a member deletes their own message, so that line stays `(deleter unknown)` even when the bot has View Audit Log. The first time a deleter cannot be named, the bot adds one warning about that permission. `/dfcancelevent` already names the manager who ran the command. Startup cleanup (the thread or message is gone while the bot was offline) keeps its own line and does not guess a person. A network error, a rate limit, or a missing permission on the channel leaves the event stored. Points already in `Data/points.json` stay. Boss kills that were not paid yet are dropped with the event.
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
| `/dfleaderboard` | Every player's points, highest total first (ties go to the lower user id). Split across messages when the list is long. The first message is the command reply; the rest are follow-ups. Mentions do not ping. |
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
https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&scope=bot%20applications.commands&permissions=326417870016
```

The permission integer `326417870016` contains: View Channels, View Audit Log, Send Messages, Embed Links, Add Reactions, Use External Emojis,
Read Message History, Manage Messages, Create Public Threads, Send Messages in Threads, Manage Threads.
That set covers a text events channel and a forum events channel, plus the log, manage, and points channels.
View Audit Log is a server permission, not a channel overwrite. The bot uses it to name who deleted an event thread, forum post, or sign-up message. At startup it warns when that permission is missing.
A text events channel uses **Create Public Threads** to open a standalone thread, and **Send Messages in Threads** to write inside it.
A forum events channel uses **Create Posts** (the Send Messages bit) to create each post, and **Send Messages in Posts** (Send Messages in Threads) to write inside it. The startup check uses those names for a forum.
(Scopes: `bot applications.commands`.)

### 3. Prepare roles and channels on your server
Roles (names are configurable in `global.json`):

| Role | Purpose |
| --- | --- |
| `df-dungeoneer` | may sign up/off by reacting to events – give it to your raiders |
| `df-manager` | may use the manager slash commands (`/dfhelp` lists them) |
| `df-logs` | may see the log channel |

Channels (names or ids are configurable). The events channel may be a text channel or a forum; the others stay text channels.

| Channel | Purpose | Recommended permissions |
| --- | --- | --- |
| `dungeon-forge-forum` | one event per thread (text channel) or one event per forum post. The embed and the reactions live on the message inside that thread or on the post's starter message. The default name was `dungeon-forge`. | `@everyone`: **deny View Channel**. `df-dungeoneer` on a text channel: **allow** View Channel, Read Message History, Add Reactions; **deny** Send Messages, **deny** Send Messages in Threads, **deny** Create Public Threads. `df-dungeoneer` on a forum: **allow** View Channel, Read Message History, Add Reactions; **deny** Create Posts, **deny** Send Messages in Posts. Bot on a text channel: **allow** View Channel, Send Messages, Embed Links, Add Reactions, Read Message History, Manage Messages, Manage Threads, Create Public Threads, Send Messages in Threads. Bot on a forum: **allow** View Channel, Create Posts, Send Messages in Posts, Embed Links, Add Reactions, Read Message History, Manage Messages, Manage Threads. |
| `dungeon-forge-manage` | managers use the slash commands (dungeoneers may use `/dfpoints` and `/dfleaderboard` here) | `@everyone`: deny View Channel. `df-manager`: allow View Channel, Send Messages, Use Application Commands. `df-dungeoneer`: allow View Channel, Use Application Commands (Send Messages only if you want them to chat here). Bot: allow View Channel, Send Messages. |
| `dungeon-forge-log` | log messages from the bot | `@everyone`: **deny View Channel**. `df-logs`: **allow** View Channel, Read Message History; deny Send Messages. Bot: allow View Channel, Send Messages. |
| `dungeon-forge-points` | full leaderboard, then the raid boss-point table. Both are edited in place and may span several messages | `@everyone`: **deny View Channel**. `df-dungeoneer`: **allow** View Channel, Read Message History; **deny** Send Messages. Bot: **allow** View Channel, Send Messages, Manage Messages, Embed Links, Read Message History. |

Notes on "nobody can chat in dungeon-forge-forum":
* The permission setup above is the reliable way: players can only look at the events and react. On a text channel, deny **Send Messages**, **Send Messages in Threads**, and **Create Public Threads** for `df-dungeoneer`. On a forum, deny **Create Posts** and **Send Messages in Posts**.
* Forum posts still allow members to reply when they have Send Messages in Posts. `deleteStrayThreadMessages` (default **on**) deletes non-bot messages inside those posts, the same way it does inside text-channel threads.
* A text channel shows a small "thread created" system line for each thread. The bot leaves those lines alone. A forum lists the posts themselves.
* As a safety net the bot deletes top-level user messages in a text events channel (`deleteStrayMessages`, default **on**, needs *Manage Messages*; if the permission is missing it logs one warning). A forum does not accept normal messages in the parent channel.
* Inside event threads and forum posts, non-bot messages are deleted when `deleteStrayThreadMessages` is on. A missing permission fails the same way: one warning, then the bot continues.
* `"lockEventThreads": true` locks each new thread or post so only moderators (Manage Threads) can post. The default is `false`; the dungeoneer permission denies above are what stop chat.
* A permission overwrite on the bot role is needed in the events channel if you deny Send Messages for `@everyone`.
* If the forum has **Require tag** turned on, the bot applies the first tag in the forum's tag list. With no tags, event creation stops and the log explains that a tag has to be added (or the requirement turned off). A moderated-only tag needs the bot to have Manage Threads.

### 4. Configure
Open `src/DungeonForge/Configurations/global.json` and replace the placeholders:

```jsonc
{
  "token": "YOUR_BOT_TOKEN",          // or set the environment variable DUNGEONFORGE_TOKEN (preferred – never commit tokens)
  "guildId": 0,                        // your server id (Discord: Settings → Advanced → Developer Mode; right-click server → Copy Server ID)
  "timeZone": "Europe/Vienna",         // time zone used for /dfcreateevent date+time
  "channels": {                        // id 0 = look the channel up by name; a non-zero id takes precedence
    "events": { "name": "dungeon-forge-forum",  "id": 0 },   // text channel or forum; default name (was dungeon-forge)
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

## Docker

The image is framework-dependent and uses `mcr.microsoft.com/dotnet/runtime:10.0`. The same Dockerfile works on **linux/amd64** and **linux/arm64** (a 64-bit Raspberry Pi): build it on the machine that will run it. The bot process runs as user `dungeonforge` (uid 1000 unless you change the build arg). The entrypoint is root only long enough to seed empty folders and make those mounts writable by that user.

`DUNGEONFORGE_TOKEN`, when it is set to a non-empty value, replaces the `token` field in `global.json`. The token can stay the placeholder in the file.

State is the `dataDirectory` from `global.json`. The shipped value is `Data`, which the container resolves to `/app/Data`. That folder holds `events.json`, `points.json`, and `points-channel.json`. Compose mounts it from the host, so it survives image updates.

### 1. Clone
```
git clone https://github.com/HannesHold/DungeonForge.git
cd DungeonForge
```

### 2. Start, then edit the seeded config
```
docker compose up -d
```
The first start creates `docker/configurations`, `docker/event-configurations`, and `docker/data` when they are missing. Empty config and template folders receive the files shipped in the image. Files you already edited are not replaced. The container then exits, because the seeded `global.json` still has the placeholder token and `guildId` 0, and Compose starts it again until that is fixed (`restart: unless-stopped`).

Stop it, then edit:
```
docker compose stop
```
* `docker/configurations/global.json` — set `guildId` (and `token`, unless you use the environment variable below). Channel and role names are already filled in.
* `docker/event-configurations/` — the shipped raid and dungeon templates. Add or edit JSON here; this folder is what the running container reads.

Token from the environment instead of the file: create a `.env` file next to `docker-compose.yml` (it is gitignored):
```
DUNGEONFORGE_TOKEN=your-bot-token
```
`guildId` still has to be set in `global.json`.

If your host user is not uid 1000, build with your ids so you can edit the seeded files without `sudo`:
```
UID=$(id -u) GID=$(id -g) docker compose build
```

### 3. Run and watch the log
```
docker compose up -d
docker compose logs -f dungeonforge
```
A healthy start prints `Dungeon Forge is online…`. Slash commands can take a few seconds to appear.

### 4. Update
```
git pull
docker compose up -d --build
```
New template files from the image are copied in when that file is not already in `docker/event-configurations`. Your `global.json`, edited templates, and `docker/data` stay as they are.

### 5. Backup
Copy `docker/data` (`events.json`, `points.json`, `points-channel.json`). Also copy `docker/configurations` and `docker/event-configurations` if you have changed them. Stop the container first if you want a quiet copy:
```
docker compose stop
tar -czf dungeonforge-backup.tgz docker/data docker/configurations docker/event-configurations
docker compose up -d
```

### systemd
Compose already restarts the container with the Docker daemon (`unless-stopped`). To start that compose project at boot, save this as `/etc/systemd/system/dungeonforge.service` and set `WorkingDirectory` to the clone:
```
[Unit]
Description=Dungeon Forge (Docker Compose)
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
WorkingDirectory=/opt/DungeonForge
ExecStart=/usr/bin/docker compose up -d
ExecStop=/usr/bin/docker compose stop

[Install]
WantedBy=multi-user.target
```
```
sudo systemctl daemon-reload
sudo systemctl enable --now dungeonforge
```

Without Docker, publish once (`dotnet publish src/DungeonForge -c Release -o /opt/dungeonforge`) and run the dll under systemd as the same kind of unit with `Type=simple`, `Restart=on-failure`, `WorkingDirectory=/opt/dungeonforge`, `ExecStart=/usr/bin/dotnet DungeonForge.dll`, and `Environment=DUNGEONFORGE_TOKEN=…`. Put `Configurations`, `EventConfigurations`, and `Data` in that directory. `dataDirectory` stays `Data` unless you point it at another absolute path.

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
Dockerfile                      framework-dependent image (linux/amd64 and linux/arm64)
docker-compose.yml              service dungeonforge; mounts config, templates, and Data
docker/entrypoint.sh            seeds empty mounts, then runs the bot as user dungeonforge
src/DungeonForge/
  Program.cs                      Generic Host + DI wiring, graceful config errors
  Configurations/                 global.json, subscription-roles.json, fixed-subscription-roles.json
  EventConfigurations/            Raids/*.json and Dungeons/*.json (a file in the root is allowed too)
  Configuration/                  config models, BotConfiguration, ConfigurationException
  Models/                         EventDefinition + BossDefinition, EventType, EventStatus,
                                  EventInstance + Signup, BossKillRecord, PointsLedgerEntry, RoleDefinition
  Services/                       ConfigurationLoader, EventDefinitionRepository(+Validator), EventStore,
                                  EventRenderer (embed), SignupLogic, EventMessagePolicy, EventsChannelSupport, OpenEventLimit,
                                  PointsLogic, PointsSummaryFormatter, PointsBoardFormatter, DeletionAudit,
                                  PointsStore (Data/points.json), PointsChannelStore, PointsBoardService,
                                  EventDateParser, EventTemplateListFormatter, EventTemplateAutocomplete,
                                  EventService (Discord orchestration), BotLog, EventWorkQueue
  Bot/                            BotService (client, gateway handlers, startup), EventCommands (slash commands + autocomplete),
                                  CommandHelpFormatter, DiscordHelpers, EmojiKey, ResolvedChannels
tests/DungeonForge.Tests/         xUnit tests for the pure logic (run: dotnet test)
```

## Behaviour details / limits

* Gateway intents: `Guilds`, `GuildMembers` (privileged), `GuildMessages`, `GuildMessageReactions`. No `MessageContent`.
* Each new event is either a standalone public thread in a text events channel (`CreateThreadAsync` with no parent message, `ThreadType.PublicThread`, which needs Create Public Threads) or a forum post (`CreatePostAsync`, which needs Create Posts) when `dungeon-forge-forum` is a forum. Startup names missing permissions the way that channel's settings screen does. A forum report lists Create Posts and Send Messages in Posts. The auto-archive duration is one week, then three days, then one day, if the guild rejects the longer value. The embed, the role reactions, and the sign-up updates live on the message inside that thread. For a forum post that message is the starter message, and its id is the post id. Deleting the starter message deletes the post; the thread-deleted handler removes the stored event, and a following message-deleted event for the same id does not remove it twice. Whichever handler removes the event is the one that looks up the deleter. The parent channel only lists threads or posts. A text channel also shows Discord's "thread created" system line.
* Events already stored with the embed in the parent channel (`embedInThread` missing or false) stay on that layout: reactions, edits, cancel, and completion still use that channel message.
* The bot unarchives a thread (and joins it) when it needs to edit the embed or reconcile reactions. Completing an event archives and locks the thread when `archiveCompletedEventThreads` is on.
* Reactions on messages that are not in the cache are handled (reaction events are processed by message id and are serialized in arrival order). A completed event drops new sign-up reactions.
* Role slots are *targets*, not hard limits: more people than slots may sign up for a role (the embed shows e.g. `3/2`) so managers can decide. The counter `x/y` counts all signed-up role members.
* Open events count toward `maxOpenEvents`. Completed events stay until `finishedEventRetentionHours` after they ended, and `/dflistevents` still shows them. Cancelled events are removed immediately; the log names the manager who ran `/dfcancelevent`, and points already written to `points.json` stay. If an event message is deleted in Discord the event is removed from the state. If the whole thread or forum post is deleted, the event is removed the same way (live, and again at the next startup if the bot was offline). A live delete appends `by @user` from the audit log, or `(deleter unknown)` when the entry is missing. A member deleting their own message does not produce an audit entry. Startup removal does not name a person. That removal does not touch the points ledger: a completed event keeps every awarded point, and an open event simply loses any boss kills that had not been paid yet. A lookup that fails for a reason other than "not found" (network, rate limit, missing permission) keeps the stored event.
* Boss points are taken from the template and `pointsPerBossKill` at completion time. A missing template or a renamed boss falls back to `pointsPerBossKill`.
* The points channel is optional at startup: if it is missing, the bot logs a warning and skips the leaderboard and the boss table. The leaderboard lists every player with points (same order as `/dfleaderboard`) across as many messages as needed. Those message ids are stored in `points-channel.json` as `leaderboardMessageIds`. A file that still has the older single `leaderboardMessageId` is read as a one-message list. The bot edits those messages, adds more when the list grows, and deletes extras when it shrinks. New messages are created at the bottom of the channel, so when the leaderboard needs more messages than it already has, the bot deletes the boss-table messages, appends the extra leaderboard messages, and posts the boss table again. The channel then stays leaderboard first, boss table after. A shorter leaderboard only deletes the spare leaderboard messages and leaves the boss table where it is. Startup posts the leaderboard first, then the boss table.
* The bot must have been able to *see* a channel to find it by name; if channel lookup by name is ambiguous the first match wins – use ids then.

## Development

```
dotnet build
dotnet test
```

## Branding

The Discord app icon file is assets/dungeon-forge-discord-icon-1024.png.
