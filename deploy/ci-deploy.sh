#!/bin/bash
# Server side of the CI deploy (installed at /opt/docker/neurovox/ci-deploy.sh).
# It is the *forced command* of the CI SSH key: the key can do nothing else.
#   stdin                = `docker save neurovox-api:<tag> neurovox-web:<tag> | gzip`
#   SSH_ORIGINAL_COMMAND = <tag> (git short sha)
# Loads the images, switches api+web to the tag, waits for both to be healthy and rolls back to the previous tags otherwise.
set -euo pipefail

TAG="${SSH_ORIGINAL_COMMAND:-}"
[[ "$TAG" =~ ^[0-9a-f]{7,40}$ ]] || { echo "bad tag: '$TAG'" >&2; exit 2; }

cd /opt/docker/neurovox
exec 9>/tmp/neurovox-deploy.lock
flock -n 9 || { echo "another deploy is running" >&2; exit 3; }

echo "==> loading images"
gzip -dc | docker load

cp docker-compose.yml docker-compose.yml.prev
sed -i -E "s#(image: neurovox-api:).*#\1$TAG#; s#(image: neurovox-web:).*#\1$TAG#" docker-compose.yml

healthy() {   # both containers report healthy
  for c in neurovox-api neurovox-web; do
    [ "$(docker inspect -f '{{.State.Health.Status}}' "$c" 2>/dev/null)" = healthy ] || return 1
  done
}

echo "==> starting $TAG"
docker compose up -d api web
for _ in $(seq 1 30); do sleep 5; healthy && ok=1 && break; done

if [ "${ok:-0}" != 1 ]; then
  echo "==> NOT healthy, rolling back" >&2
  docker logs --tail 30 neurovox-api >&2 || true
  cp docker-compose.yml.prev docker-compose.yml
  docker compose up -d api web
  exit 1
fi

echo "==> healthy, pruning old images (keeping the 4 newest per repo)"
for repo in neurovox-api neurovox-web; do
  docker images "$repo" --format '{{.Tag}} {{.CreatedAt}}' | sort -k2 -r | awk 'NR>4{print $1}' | while read -r t; do
    docker rmi "$repo:$t" >/dev/null 2>&1 || true   # fails (and is skipped) while a container still uses it
  done
done
echo "OK: $TAG deployed"
