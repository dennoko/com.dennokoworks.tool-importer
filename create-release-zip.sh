#!/usr/bin/env bash
# ==============================================================================
# VPM Package Release Zip Generator (Bash / Git Bash / macOS / Linux)
# dennokoworks Tool Importer
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if [ ! -f "package.json" ]; then
    echo "エラー: package.json が見つかりません" >&2
    exit 1
fi

PACKAGE_NAME=$(node -pe "require('./package.json').name" 2>/dev/null || grep -o '"name": *"[^"]*"' package.json | head -1 | cut -d'"' -f4)
VERSION=$(node -pe "require('./package.json').version" 2>/dev/null || grep -o '"version": *"[^"]*"' package.json | head -1 | cut -d'"' -f4)

OUTPUT_DIR="${1:-..}"
ZIP_NAME="${PACKAGE_NAME}-${VERSION}.zip"
ZIP_PATH="${OUTPUT_DIR}/${ZIP_NAME}"

echo "========================================"
echo "  VPM Package Zip Builder"
echo "========================================"
echo "Package: ${PACKAGE_NAME}"
echo "Version: ${VERSION}"
echo ""

rm -f "$ZIP_PATH"

# zip コマンドまたは git-archive による作成（.git などを除外してルート直下に圧縮）
if command -v zip >/dev/null 2>&1; then
    zip -r -q "$ZIP_PATH" . \
        -x "*.git*" \
        -x "*.vs*" \
        -x "*.idea*" \
        -x "*.zip" \
        -x "create-release-zip.*" \
        -x "vpm-repository-template.json*" \
        -x "Docs/RELEASE.md*" \
        -x "Docs/Impl/*" \
        -x "Docs/Impl.meta"
else
    echo "zip コマンドが見つかりません。PowerShell 版 (create-release-zip.ps1) をご利用ください。" >&2
    exit 1
fi

# SHA256 計算
if command -v sha256sum >/dev/null 2>&1; then
    SHA256=$(sha256sum "$ZIP_PATH" | awk '{print $1}')
elif command -v shasum >/dev/null 2>&1; then
    SHA256=$(shasum -a 256 "$ZIP_PATH" | awk '{print $1}')
else
    SHA256="unknown"
fi

echo "========================================"
echo "  ZIP 生成が完了しました！"
echo "========================================"
echo "出力先 : ${ZIP_PATH}"
echo "SHA256 : ${SHA256}"
echo ""
echo "--- vpm-repos/index.json 用設定 ---"
echo "\"url\": \"https://github.com/dennoko/${PACKAGE_NAME}/releases/download/v${VERSION}/${ZIP_NAME}\","
echo "\"zipSHA256\": \"${SHA256}\""
echo ""
