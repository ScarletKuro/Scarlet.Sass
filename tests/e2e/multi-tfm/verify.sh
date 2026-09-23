#!/bin/bash
set -euo pipefail

# End-to-end test for the multi-targeted Razor Class Library path.
#
# The important contract is that Sass runs once in the outer build before static web assets are resolved,
# not once per inner TFM. The produced CSS must also survive dotnet pack --no-build as a package static web
# asset under staticwebassets/.
#
# Usage: ./verify.sh <workspace-path> <package-version> <runtime-version>

if [ $# -ne 3 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <runtime-version>"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
RUNTIME_VERSION="$3"

section() {
    echo ""
    echo "=========================================="
    echo "$1"
    echo "=========================================="
}

ok() {
    echo "✓ $1"
}

fail() {
    echo "✗ $1"
    exit 1
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATES_DIR="$SCRIPT_DIR/templates"

detect_dotnet_rid() {
    dotnet --info 2>/dev/null | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -n 1
}

select_runtime_package() {
    case "$1" in
        win-arm64) echo "Scarlet.Sass.Runtime.windows-arm64" ;;
        win-x64) echo "Scarlet.Sass.Runtime.windows-x64" ;;
        linux-arm64) echo "Scarlet.Sass.Runtime.linux-arm64" ;;
        linux-x64) echo "Scarlet.Sass.Runtime.linux-x64" ;;
        linux-musl-arm64) echo "Scarlet.Sass.Runtime.linux-arm64-musl" ;;
        linux-musl-x64) echo "Scarlet.Sass.Runtime.linux-x64-musl" ;;
        osx-arm64) echo "Scarlet.Sass.Runtime.darwin-arm64" ;;
        osx-x64) echo "Scarlet.Sass.Runtime.darwin-x64" ;;
        *) echo "" ;;
    esac
}

process_template() {
    local template_file="$1"
    local output_file="$2"
    local workspace_escaped="${WORKSPACE_PATH//\\/\\\\}"

    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$PACKAGE_VERSION|g" \
        -e "s|{{RUNTIME_PACKAGE}}|$RUNTIME_PACKAGE|g" \
        -e "s|{{RUNTIME_VERSION}}|$RUNTIME_VERSION|g" \
        "$template_file" > "$output_file"
}

TEST_DIR="/tmp/scarlet-sass-multi-tfm-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

DOTNET_RID="$(detect_dotnet_rid)"
RUNTIME_PACKAGE="$(select_runtime_package "$DOTNET_RID")"
if [ -z "$RUNTIME_PACKAGE" ]; then
    fail "Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
fi

section "E2E Test: Multi-TFM Razor Class Library"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "RID: $DOTNET_RID"
echo "Runtime package: $RUNTIME_PACKAGE"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
ok "Created nuget.config with local package source"
dotnet new razorclasslib -n TestRclMultiTfm > /dev/null
cd TestRclMultiTfm
process_template "$TEMPLATES_DIR/TestRclMultiTfm.csproj.template" "TestRclMultiTfm.csproj"
ok "Created multi-targeted RCL project"

mkdir -p Sass
process_template "$TEMPLATES_DIR/_variables.scss.template" "Sass/_variables.scss"
process_template "$TEMPLATES_DIR/style.scss.template" "Sass/site.scss"

dotnet restore --configfile ../nuget.config
section "Building Multi-TFM Project"
dotnet build --no-restore --verbosity normal 2>&1 | tee build.log
ok "Build completed"

section "Verifying Generated CSS"
if [ ! -f wwwroot/css/site.css ]; then
    fail "CSS output was not created"
fi
ok "CSS output was created"

RUN_COUNT="$(grep -c "Executing:" build.log || true)"
# If this count becomes 0, the target did not run. If it becomes greater than 1, Sass has leaked into inner
# TFM builds and multi-targeted RCLs will do duplicate work or fight over the same wwwroot files.
if [ "$RUN_COUNT" -ne 1 ]; then
    echo "Expected Sass to run once in the multi-TFM outer build, saw $RUN_COUNT run(s)"
    grep "Executing:" build.log || true
    fail "Sass did not run exactly once"
fi
ok "Sass ran once in the multi-TFM outer build"

section "Verifying Packaged Static Web Assets"
dotnet pack --no-build --configuration Debug -o nupkg > /dev/null
NUPKG="$(find nupkg -name '*.nupkg' | head -n 1)"
if [ -z "$NUPKG" ]; then
    fail "No NuGet package was produced"
fi
ok "NuGet package was produced"

CONTENTS="$(unzip -l "$NUPKG")"
if ! printf '%s' "$CONTENTS" | grep -q "staticwebassets/css/site.css"; then
    echo "Generated CSS was not packed under staticwebassets/"
    printf '%s\n' "$CONTENTS" | grep -E "site\.css|staticwebassets" || true
    fail "Generated CSS was not packed under staticwebassets/"
fi
ok "Generated CSS was packed under staticwebassets/"

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
ok "E2E multi-TFM test completed successfully - Sass output was packed as a static web asset"
