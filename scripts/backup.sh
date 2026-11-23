#!/usr/bin/env bash
set -euo pipefail
umask 077
backup_root=${1:?Usage: bash scripts/backup.sh /secure/backup/directory}
backup_target="$backup_root/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_target"
# Pause writes for a consistent database/audio snapshot. Preserve keys too.
docker compose stop view api
trap 'docker compose start api view' EXIT
docker compose exec -T db sh -c 'MYSQL_PWD="$MYSQL_PASSWORD" mysqldump -u speakready --single-transaction --no-tablespaces --set-gtid-purged=OFF SpeakReadyDB' > "$backup_target/database.sql"
docker compose run --rm --no-deps --user root --entrypoint tar api -C /data/audio -cf - . > "$backup_target/audio.tar"
docker compose run --rm --no-deps --user root --entrypoint tar view -C /data/keys -cf - . > "$backup_target/keys.tar"
