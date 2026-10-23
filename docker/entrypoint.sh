#!/bin/sh
# Seed empty Configurations and EventConfigurations mounts, then run the bot as dungeonforge.
# Existing files are left alone, so a later image can add new templates without replacing edits.
set -eu

seed_missing() {
  src="$1"
  dest="$2"
  if [ ! -d "$src" ]; then
    echo "Dungeon Forge: no seed files at $src" >&2
    return 0
  fi
  mkdir -p "$dest"
  find "$src" -type d | while IFS= read -r dir; do
    rel="${dir#"$src"}"
    mkdir -p "${dest}${rel}"
  done
  find "$src" -type f | while IFS= read -r file; do
    rel="${file#"$src"}"
    target="${dest}${rel}"
    if [ ! -e "$target" ]; then
      cp "$file" "$target"
    fi
  done
}

seed_missing /opt/dungeonforge-defaults/Configurations /app/Configurations
seed_missing /opt/dungeonforge-defaults/EventConfigurations /app/EventConfigurations
mkdir -p /app/Data

chown -R dungeonforge:dungeonforge /app/Configurations /app/EventConfigurations /app/Data

cd /app
# setpriv keeps DUNGEONFORGE_TOKEN and the other variables passed in by Compose.
exec setpriv --reuid=dungeonforge --regid=dungeonforge --init-groups --inh-caps=-all \
  dotnet DungeonForge.dll
