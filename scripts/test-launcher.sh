#!/usr/bin/env bash
# Exercises scripts/kashflow-mcp against a local release directory: checksum
# success, cache hit, offline cache, checksum rejection, and KASHFLOW_MCP_BIN.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LAUNCHER="$ROOT/scripts/kashflow-mcp"
WORKDIR="$(mktemp -d)"
SERVER_PID=""

cleanup() {
  if [ -n "${SERVER_PID}" ]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  rm -rf "$WORKDIR"
}
trap cleanup EXIT

fail() { printf 'test-launcher: %s\n' "$*" >&2; exit 1; }

WWW="$WORKDIR/www"
mkdir -p "$WWW/stage/linux-x64"
cat > "$WWW/stage/linux-x64/KashflowMCP" <<'EOF'
#!/bin/sh
printf 'kashflow-mcp-ok\n'
EOF
chmod +x "$WWW/stage/linux-x64/KashflowMCP"

python3 - "$WWW" <<'PY'
import hashlib, sys, zipfile
from pathlib import Path
www = Path(sys.argv[1])
binary = www / "stage" / "linux-x64" / "KashflowMCP"
archive = www / "KashflowMCP-linux-x64.zip"
with zipfile.ZipFile(archive, "w") as zf:
    zf.write(binary, "linux-x64/KashflowMCP")
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
(www / "SHA256SUMS").write_text(f"{digest}  KashflowMCP-linux-x64.zip\n")
(www / "good-sha").write_text(digest)
PY

start_server() {
  local port
  port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
  python3 -m http.server "$port" --bind 127.0.0.1 --directory "$WWW" >"$WORKDIR/http.log" 2>&1 &
  SERVER_PID=$!
  local i
  for i in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
    if curl -fsS "http://127.0.0.1:${port}/SHA256SUMS" >/dev/null 2>&1; then
      printf '%s\n' "$port"
      return
    fi
    sleep 0.1
  done
  fail "local release server did not start"
}

stop_server() {
  if [ -n "${SERVER_PID}" ]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
    SERVER_PID=""
  fi
}

PORT="$(start_server)"
export KASHFLOW_MCP_RELEASE_BASE="http://127.0.0.1:${PORT}"
export KASHFLOW_MCP_CACHE="$WORKDIR/cache"
export KASHFLOW_MCP_VERSION="1.1.0"
export KASHFLOW_MCP_RID="linux-x64"
unset KASHFLOW_MCP_BIN || true

out="$("$LAUNCHER")"
[ "$out" = "kashflow-mcp-ok" ] || fail "first run stdout was '$out'"

out="$("$LAUNCHER")"
[ "$out" = "kashflow-mcp-ok" ] || fail "cache hit stdout was '$out'"

stop_server
out="$("$LAUNCHER")"
[ "$out" = "kashflow-mcp-ok" ] || fail "offline cache stdout was '$out'"

PORT="$(start_server)"
export KASHFLOW_MCP_RELEASE_BASE="http://127.0.0.1:${PORT}"
printf '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  KashflowMCP-linux-x64.zip\n' > "$WWW/SHA256SUMS"
rm -rf "$WORKDIR/cache"
set +e
"$LAUNCHER" >"$WORKDIR/bad.out" 2>"$WORKDIR/bad.err"
code=$?
set -e
[ "$code" -ne 0 ] || fail "bad checksum was accepted"
if [ -s "$WORKDIR/bad.out" ] && grep -q kashflow-mcp-ok "$WORKDIR/bad.out"; then
  fail "bad checksum executed the binary"
fi
grep -q -i mismatch "$WORKDIR/bad.err" || fail "expected a checksum mismatch error, got: $(cat "$WORKDIR/bad.err")"

stop_server
export KASHFLOW_MCP_BIN="$WWW/stage/linux-x64/KashflowMCP"
out="$("$LAUNCHER")"
[ "$out" = "kashflow-mcp-ok" ] || fail "KASHFLOW_MCP_BIN stdout was '$out'"

printf 'launcher ok\n'
