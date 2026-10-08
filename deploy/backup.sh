#!/bin/bash
# NeuroVox daily backup: database dump, audio + models volumes, .env/compose (the secret key decrypts the Kaggle tokens).
set -euo pipefail
D=/opt/docker/neurovox
B=/opt/backups/neurovox
TS=$(date -u +%Y%m%d-%H%M%S)
umask 077
mkdir -p "$B"
cd "$D"
set -a; . ./.env; set +a
docker exec neurovox-postgres pg_dump -U "$POSTGRES_USER" -d NeuroVoxDb --no-owner | gzip -9 > "$B/db-$TS.sql.gz"
gzip -t "$B/db-$TS.sql.gz"
tar -czf "$B/files-$TS.tar.gz" -C /var/lib/docker/volumes neurovox_audio/_data neurovox_models/_data
tar -czf "$B/config-$TS.tar.gz" -C "$D" .env docker-compose.yml
find "$B" -name 'db-*' -mtime +14 -delete
find "$B" -name 'files-*' -mtime +14 -delete
find "$B" -name 'config-*' -mtime +14 -delete
echo "$(date -u +%FT%TZ) ok $(du -sh "$B" | cut -f1)"
