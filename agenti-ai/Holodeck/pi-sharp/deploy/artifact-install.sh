#!/usr/bin/env bash
# install_entry of a pi-sharp artifact (built by the ai-factory builders, scripts/package-actor.sh). Run as root from
# the artifact root on the seat host (coder-06). Swaps the binaries of both installs:
#   /opt/pi-sharp           the CLI that pi-run / pi-work coder runs start
#   /opt/pi-sharp-attached  what the role seats (pi-seat@N) run
# A browserless artifact carries no browser/: each root's existing active pi-browser wrapper is then copied
# unchanged into its new bin, so the active /opt/pi-sharp-browser integration survives the swap.
# Nothing is restarted: a running process keeps the files it loaded, the next run picks up the new build. Restarting
# the role seats is a separate, announced step (pi-seat@N restarts interrupt whoever is mid-work).
# Builds since 2026-10-30 carry their own .NET (self-contained) and start by their own executables, bin/pi-sharp and
# browser/PiSharp.Browser; an older, framework-dependent artifact still starts through dotnet.
set -euo pipefail

# How to run a published .NET program: its own executable when the build carries its .NET, else through dotnet.
runner() {
  if grep -q '"includedFrameworks"' "$1.runtimeconfig.json" 2>/dev/null && [ -x "$1" ]; then printf '%s' "$1"
  else printf 'dotnet %s.dll' "$1"; fi
}

[ -f bin/pi-sharp.dll ] || { echo 'bin/pi-sharp.dll missing from the artifact' >&2; exit 1; }
[ -f VERSION ] || { echo 'VERSION missing from the artifact' >&2; exit 1; }
# A seat host: the old seats box has an agent-maker user that owns the installs; a box that took seats by a move (AI-TLT,
# relay seatMoves) has their unit instead, and root owns the installs there (2026-10-16: host-02 refused this install).
if id -u agent-maker >/dev/null 2>&1; then owner=agent-maker:agent-maker
elif [ -f /etc/systemd/system/pi-seat@.service ]; then owner=root:root
else echo 'not a pi-sharp seat host: no agent-maker user and no pi-seat@.service' >&2; exit 1; fi
version=$(cat VERSION)
$(runner bin/pi-sharp) --version >/dev/null 2>&1 || { echo "the new build does not start ($(runner bin/pi-sharp) --version)" >&2; exit 1; }
if [ -d browser ]; then
  $(runner browser/PiSharp.Browser) install-deps chromium
  browser_target="/opt/pi-sharp-browser/$version"
  if [ ! -e "$browser_target" ]; then
    mkdir -p /opt/pi-sharp-browser
    browser_stage=$(mktemp -d /opt/pi-sharp-browser/.stage-XXXXXX)
    cp -a browser "$browser_stage/bin"
    cp -a browsers "$browser_stage/browsers"
    test -x "$browser_stage/bin/.playwright/node/linux-x64/node"
    mv "$browser_stage" "$browser_target"
  fi
  chmod -R a+rX "$browser_target"
fi

swapped=()
rollback() {
  for target in "${swapped[@]}"; do
    if [ -d "$target/bin.old" ]; then mv "$target/bin" "$target/bin.failed-$version"; mv "$target/bin.old" "$target/bin"; fi
    [ ! -f "$target/VERSION.old" ] || cp "$target/VERSION.old" "$target/VERSION"
  done
}
trap rollback ERR
for target in /opt/pi-sharp /opt/pi-sharp-attached; do
  stage="$target/bin.new"
  mkdir -p "$target"
  rm -rf "$stage"
  cp -a bin/. "$stage/"
  if [ -d browser ]; then
    printf '#!/usr/bin/env bash\nexport PLAYWRIGHT_BROWSERS_PATH=/opt/pi-sharp-browser/%s/browsers\nexec %s "$@"\n' "$version" "$(runner "/opt/pi-sharp-browser/$version/bin/PiSharp.Browser")" > "$stage/pi-browser"
    chmod 755 "$stage/pi-browser"
  elif [ -f "$target/bin/pi-browser" ]; then
    cp -a "$target/bin/pi-browser" "$stage/pi-browser"
    chmod 755 "$stage/pi-browser"
  fi
  $(runner "$stage/pi-sharp") --version >/dev/null
done
for target in /opt/pi-sharp /opt/pi-sharp-attached; do
  stage="$target/bin.new"
  [ ! -f "$target/VERSION" ] || cp "$target/VERSION" "$target/VERSION.old"
  rm -rf "$target/bin.old"
  [ -d "$target/bin" ] && mv "$target/bin" "$target/bin.old"
  swapped+=("$target")
  mv "$stage" "$target/bin"
  printf '#!/usr/bin/env bash\nexec %s "$@"\n' "$(runner "$target/bin/pi-sharp")" > "$target/pi-sharp"
  chmod 755 "$target/pi-sharp"
  echo "$version $(date -Is) artifact" > "$target/VERSION"
  chown -R "$owner" "$target/bin" "$target/VERSION" "$target/pi-sharp"
  chmod -R a+rX "$target"
  echo "pi-sharp $version in place at $target"
done
trap - ERR

# Old browser versions go once nothing uses them (2026-10-30: a self-contained one is some 480 MB with its Chromium, and
# strata kept six, 2.3 GB). Kept: this version, any version a pi-browser wrapper on the box names (the installs, their
# previous bin, canaries, the seats' self-updated releases) and any a running process was started from.
if [ -d browser ]; then
  for dir in /opt/pi-sharp-browser/*/; do
    old=$(basename "$dir")
    [ "$old" != "$version" ] || continue
    if grep -qsF -- "/opt/pi-sharp-browser/$old/" /opt/pi-sharp*/bin*/pi-browser /opt/pi-sharp-seat/*/bin/pi-browser \
      /home/*/.local/share/pi-sharp/releases/*/bin/pi-browser; then continue; fi
    if grep -qsaF -- "/opt/pi-sharp-browser/$old/" /proc/[0-9]*/cmdline; then continue; fi
    rm -rf -- "/opt/pi-sharp-browser/$old" && echo "removed the unused browser $old" || true
  done
fi
