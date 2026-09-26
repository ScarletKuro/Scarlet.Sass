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

FAILED=0

section() {
    echo ""
    echo "=========================================="
    echo "$1"
    echo "=========================================="

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
    echo "✗ Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
    exit 1
fi

section "E2E Test: Multi-TFM Razor Class Library"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "✓ Created test directory: $TEST_DIR"
echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>} package=$RUNTIME_PACKAGE"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"
dotnet new razorclasslib -n TestRclMultiTfm > /dev/null
cd TestRclMultiTfm
echo "✓ Created Razor Class Library"
process_template "$TEMPLATES_DIR/TestRclMultiTfm.csproj.template" "TestRclMultiTfm.csproj"
echo "✓ Updated project file with multi-target frameworks and the Sass package references"

mkdir -p Sass
process_template "$TEMPLATES_DIR/_variables.scss.template" "Sass/_variables.scss"
process_template "$TEMPLATES_DIR/style.scss.template" "Sass/site.scss"
echo "✓ Created source assets (SCSS)"

dotnet restore --configfile ../nuget.config
echo "✓ Packages restored"
section "Building Multi-TFM Project"
dotnet build --no-restore --verbosity normal 2>&1 | tee build.log
echo "✓ Build completed"

section "Verifying Generated CSS"
if [ ! -f wwwroot/css/site.css ]; then
    echo "✗ CSS output was not created"
    FAILED=1
else
    echo "✓ CSS output was created"
fi

RUN_COUNT="$(grep -c "Executing:" build.log || true)"
# If this count becomes 0, the target did not run. If it becomes greater than 1, Sass has leaked into inner
# TFM builds and multi-targeted RCLs will do duplicate work or fight over the same wwwroot files.
if [ "$RUN_COUNT" -ne 1 ]; then
    echo "✗ Sass did not run exactly once in the multi-TFM outer build, saw $RUN_COUNT run(s)"
    FAILED=1
    grep "Executing:" build.log || true
else
    echo "✓ Sass ran once in the multi-TFM outer build"
fi

# The outer build is served by buildMultiTargeting/*.props, not build/*.props - the inner TFM builds import
# the latter and never reach the task. So this line is what proves the multi-targeting props contribute
# @(SassRuntimePack) at all; without it the whole multi-TFM path could resolve from somewhere else.
if ! grep -qE "(Using|Selected) Sass runtime pack $RUNTIME_PACKAGE" build.log; then
    echo "✗ Build log did not show runtime pack resolution for $RUNTIME_PACKAGE in the outer build"
    FAILED=1
    grep -E "Sass runtime pack|Using Sass at" build.log || true
else
    echo "✓ Sass was resolved from runtime pack $RUNTIME_PACKAGE (outer build)"
fi

for tfm in net8.0 net9.0 net10.0; do
    if [ ! -d "bin/Debug/$tfm" ]; then
        echo "✗ $tfm build output directory is missing"
        FAILED=1
    else
        echo "✓ $tfm build output directory exists"
    fi
done

section "Verifying Packaged Static Web Assets"
# --verbosity normal is load-bearing: "Executing:" is logged at normal importance, so at the default
# minimal verbosity the re-run check below would find nothing to count and could never fail.
dotnet pack --no-build --configuration Debug -o nupkg --verbosity normal 2>&1 | tee pack.log > /dev/null
echo "✓ dotnet pack --no-build succeeded"

# The target carries a '$(NoBuild)' != 'true' guard. Re-running Sass here would rewrite wwwroot after the
# integrity manifest was computed, so the package would ship bytes that do not match their own hashes.
PACK_RUNS="$(grep -c "Executing:" pack.log || true)"
if [ "$PACK_RUNS" -ne 0 ]; then
    echo "✗ dotnet pack --no-build re-ran Sass $PACK_RUNS time(s); the NoBuild guard did not hold"
    FAILED=1
    grep "Executing:" pack.log || true
else
    echo "✓ dotnet pack --no-build did not re-run any Sass steps"
fi
NUPKG="$(find nupkg -name '*.nupkg' | head -n 1)"
if [ -z "$NUPKG" ]; then
    echo "✗ No NuGet package was produced"
    exit 1
fi
echo "✓ NuGet package was produced"

CONTENTS="$(unzip -l "$NUPKG")"
if ! printf '%s' "$CONTENTS" | grep -q "staticwebassets/css/site.css"; then
    echo "✗ Generated CSS was not packed under staticwebassets/"
    FAILED=1
    printf '%s\n' "$CONTENTS" | grep -E "site\.css|staticwebassets" || true
else
    echo "✓ Generated CSS was packed under staticwebassets/"
fi

section "Verifying Clean"
dotnet clean --configuration Debug > /dev/null
if [ -f "wwwroot/css/site.css" ]; then
    echo "✗ dotnet clean left generated CSS behind"
    FAILED=1
else
    echo "✓ dotnet clean removed generated CSS from the multi-TFM project"
fi

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
if [ "$FAILED" -ne 0 ]; then
    echo "✗ E2E multi-TFM test failed"
    exit 1
fi
echo "✓ E2E multi-TFM test completed successfully - Sass output was packed as a static web asset"
