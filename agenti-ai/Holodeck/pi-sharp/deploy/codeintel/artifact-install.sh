#!/usr/bin/env bash
set -euo pipefail

root=/opt/ai-factory-codeintel
state=/var/lib/ai-factory-codeintel
version=$(cat VERSION)
previous=$(readlink "$root/current" 2>/dev/null || true)
if [ -n "$previous" ] && [ ! -d "$previous" ]; then
  previous=''
fi
[ -f "bin/PiSharp.CodeIntel.dll" ] || { echo 'PiSharp.CodeIntel.dll missing from artifact' >&2; exit 1; }
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "invalid VERSION: $version" >&2; exit 1; }

rollback() {
  rc=$?
  trap - ERR
  if [ -n "$previous" ]; then
    ln -sfn "$previous" "$root/current.rollback"
    mv -Tf "$root/current.rollback" "$root/current"
    systemctl restart ai-factory-codeintel.service || true
  else
    systemctl stop ai-factory-codeintel.service || true
    rm -f "$root/current"
  fi
  exit "$rc"
}
trap rollback ERR

if ! id ai-factory-codeintel >/dev/null 2>&1; then
  useradd --system --home-dir "$state" --create-home --shell /usr/sbin/nologin ai-factory-codeintel
fi
install -d -o ai-factory-codeintel -g ai-factory-codeintel -m 0750 "$state" "$state/workspaces"
install -d -o root -g root -m 0755 "$root/releases"
stage="$root/releases/.stage-$version"
target="$root/releases/$version"
rm -rf "$stage"
install -d -o root -g root -m 0755 "$stage"
cp -a bin/. "$stage/"
chown -R root:root "$stage"
chmod -R a+rX "$stage"
if [ -d "$target" ]; then
  if ! diff -qr "$stage" "$target" >/dev/null; then
    echo "refusing to overwrite immutable release payload: $target" >&2
    false
  fi
  rm -rf "$stage"
else
  mv "$stage" "$target"
fi
ln -sfn "$target" "$root/current.new"
mv -Tf "$root/current.new" "$root/current"

install -D -o root -g root -m 0644 deploy/ai-factory-codeintel.service \
  /etc/systemd/system/ai-factory-codeintel.service
install -d -o root -g root -m 0755 /etc/ai-factory
if [ ! -f /etc/ai-factory/codeintel.env ]; then
  printf '%s\n' 'CODEINTEL_LS_CSHARPLS_COMMAND=/var/lib/ai-factory-codeintel/.dotnet/tools/csharp-ls' \
    > /etc/ai-factory/codeintel.env
  chmod 0640 /etc/ai-factory/codeintel.env
  chown root:ai-factory-codeintel /etc/ai-factory/codeintel.env
fi

if [ ! -x "$state/.dotnet/tools/csharp-ls" ]; then
  timeout 180 runuser -u ai-factory-codeintel -- env HOME="$state" DOTNET_CLI_HOME="$state" \
    sh -c 'cd "$HOME" && exec dotnet tool install --global csharp-ls --version 0.28.0'
fi

systemctl daemon-reload
systemctl enable ai-factory-codeintel.service
timeout 90 systemctl restart ai-factory-codeintel.service
for _ in $(seq 1 30); do
  if curl --fail --silent --show-error --max-time 2 http://127.0.0.1:8084/health | \
      grep -Fq "\"version\":\"$version\""; then
    echo "ai-factory-codeintel $version healthy on :8084"
    exit 0
  fi
  sleep 1
done
systemctl --no-pager --full status ai-factory-codeintel.service || true
journalctl -u ai-factory-codeintel.service -n 80 --no-pager || true
echo "ai-factory-codeintel $version did not become healthy" >&2
false
