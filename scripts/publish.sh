#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIGURATION="${CONFIGURATION:-Release}"
RIDS=(linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64)

PUBLISH_ROOT="$ROOT/artifacts/publish"
RELEASE_ROOT="$ROOT/artifacts/release"
mkdir -p "$PUBLISH_ROOT" "$RELEASE_ROOT"

echo "Building solution..."
dotnet build "$ROOT/KashflowConnector.slnx" -c "$CONFIGURATION"

for rid in "${RIDS[@]}"; do
  out="$PUBLISH_ROOT/$rid"
  echo "Publishing KashflowMCP for $rid -> $out"
  rm -rf "$out"
  mkdir -p "$out"
  dotnet publish "$ROOT/src/KashflowMCP/KashflowMCP.csproj" \
    -c "$CONFIGURATION" \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishTrimmed=false \
    -o "$out"

  zip_path="$RELEASE_ROOT/KashflowMCP-$rid.zip"
  rm -f "$zip_path"
  (
    cd "$PUBLISH_ROOT"
    if command -v zip >/dev/null 2>&1; then
      zip -qr "$zip_path" "$rid"
    else
      python3 - <<PY
import shutil
shutil.make_archive("${zip_path[:-4]}", "zip", "$PUBLISH_ROOT", "$rid")
PY
    fi
  )
  echo "  packed $zip_path"
done

echo "Writing SHA256SUMS..."
(
  cd "$RELEASE_ROOT"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum KashflowMCP-*.zip | sort -k2 > SHA256SUMS
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 KashflowMCP-*.zip | sort -k2 > SHA256SUMS
  else
    python3 - <<'PY'
import hashlib, pathlib
root = pathlib.Path(".")
lines = []
for path in sorted(root.glob("KashflowMCP-*.zip")):
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    lines.append(f"{digest}  {path.name}")
pathlib.Path("SHA256SUMS").write_text("\n".join(lines) + "\n")
PY
  fi
)

echo "Done. Binaries in $PUBLISH_ROOT ; zips and SHA256SUMS in $RELEASE_ROOT"
ls -lh "$RELEASE_ROOT"
