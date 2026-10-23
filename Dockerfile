# Framework-dependent image. The same Dockerfile runs on linux/amd64 and linux/arm64
# because the official SDK and runtime tags are multi-arch and the published output is IL.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/DungeonForge/DungeonForge.csproj src/DungeonForge/
RUN dotnet restore src/DungeonForge/DungeonForge.csproj
COPY src/DungeonForge/ src/DungeonForge/
RUN dotnet publish src/DungeonForge/DungeonForge.csproj -c Release -o /out -p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0

# Host mounts are often root-owned. The entrypoint fixes that, then the bot runs as this user.
ARG UID=1000
ARG GID=1000

# The Ubuntu-based runtime image already has uid 1000 (ubuntu) and uid 1654 (app).
# Reuse whichever account matches UID/GID and name it dungeonforge, with no extra groups.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && if id -u "${UID}" >/dev/null 2>&1; then \
         current=$(getent passwd "${UID}" | cut -d: -f1); \
         if [ "$current" != "dungeonforge" ]; then usermod -l dungeonforge "$current"; fi; \
       fi \
    && if getent group "${GID}" >/dev/null; then \
         gname=$(getent group "${GID}" | cut -d: -f1); \
         if [ "$gname" != "dungeonforge" ]; then groupmod -n dungeonforge "$gname"; fi; \
       else \
         groupadd --system --gid "${GID}" dungeonforge; \
       fi \
    && if ! id dungeonforge >/dev/null 2>&1; then \
         useradd --system --uid "${UID}" --gid dungeonforge --home-dir /app --no-create-home --shell /usr/sbin/nologin dungeonforge; \
       fi \
    && usermod -u "${UID}" -g dungeonforge -d /app -s /usr/sbin/nologin -G dungeonforge dungeonforge

WORKDIR /app
COPY --from=build /out/ ./
COPY docker/entrypoint.sh /entrypoint.sh

# Keep a copy of the shipped config and templates outside the mount points.
# An empty host folder hides whatever was published into /app.
RUN chmod 755 /entrypoint.sh \
    && mkdir -p /opt/dungeonforge-defaults \
    && cp -a /app/Configurations /opt/dungeonforge-defaults/Configurations \
    && cp -a /app/EventConfigurations /opt/dungeonforge-defaults/EventConfigurations \
    && mkdir -p /app/Data \
    && chown -R dungeonforge:dungeonforge /app/Configurations /app/EventConfigurations /app/Data

ENTRYPOINT ["/entrypoint.sh"]
