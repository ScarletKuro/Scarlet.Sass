#!/bin/bash
set -euo pipefail

# End-to-end test for the Scarlet.Sass.Cli .NET tool.
#
# The claim is that the RID-specific tool package carries Dart Sass, so `dotnet tool install` followed by
# `dotnet sass ...` can run without downloading a runtime. The test proves that by checking the embedded
# package contents, --scarlet-info, and the absence of a download cache.
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

process_template() {
    local template_file="$1"
    local output_file="$2"
    local workspace_escaped="${WORKSPACE_PATH//\\/\\\\}"

    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$PACKAGE_VERSION|g" \
        -e "s|{{RUNTIME_VERSION}}|$RUNTIME_VERSION|g" \
        "$template_file" > "$output_file"
}

TEST_DIR="/tmp/scarlet-sass-cli-tool-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

# Private caches keep the assertion honest: if the tool needs anything outside the local feed or downloads
# Dart Sass, the script can see it.
export NUGET_PACKAGES="$TEST_DIR/nuget-packages"
export SCARLET_SASS_CACHE_DIR="$TEST_DIR/sass-cache"

DOTNET_RID="$(detect_dotnet_rid)"
section "E2E Test: Scarlet.Sass.Cli"
echo "Workspace: $WORKSPACE_PATH"
echo "Runtime version: $RUNTIME_VERSION"
echo "✓ Created test directory: $TEST_DIR"
echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>}"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"

section "Installing Local Tool"
dotnet new tool-manifest > /dev/null
# The CLI version is $(SassVersion).$(SassCliRevision). With revision 0, NuGet normalizes it to SassVersion.
dotnet tool install Scarlet.Sass.Cli --version "$RUNTIME_VERSION" --configfile nuget.config > /dev/null
echo "✓ Installed Scarlet.Sass.Cli into a local tool manifest"

section "Verifying Embedded Runtime"
RID_PACKAGE_DIR="$NUGET_PACKAGES/scarlet.sass.cli.$DOTNET_RID"
if [ -n "$DOTNET_RID" ] && [ ! -d "$RID_PACKAGE_DIR" ]; then
    echo "✗ RID-specific CLI package was not restored (expected $RID_PACKAGE_DIR)"
    FAILED=1
    find "$NUGET_PACKAGES" -maxdepth 1 -type d -print || true
else
    echo "✓ The $DOTNET_RID tool package was restored"
fi

EMBEDDED_SASS="$(find "$RID_PACKAGE_DIR" -type f \( -name 'sass' -o -name 'sass.bat' \) 2>/dev/null | head -n 1)"
if [ -z "$EMBEDDED_SASS" ]; then
    echo "✗ No embedded Dart Sass launcher found in $RID_PACKAGE_DIR"
    FAILED=1
else
    echo "✓ The tool package ships a Dart Sass launcher"
fi

section "Verifying Argument Forwarding And Diagnostics"
REPORTED_VERSION="$(dotnet sass --version)"
if [ "$REPORTED_VERSION" != "$RUNTIME_VERSION" ]; then
    echo "✗ Expected Dart Sass $RUNTIME_VERSION, got '$REPORTED_VERSION'"
    FAILED=1
else
    echo "✓ 'dotnet sass --version' printed Dart Sass's version ($REPORTED_VERSION)"
fi

if ! dotnet sass --scarlet-info | grep -q "^Source .*embedded"; then
    echo "✗ The tool did not report the embedded runtime as its source"
    FAILED=1
    dotnet sass --scarlet-info || true
else
    echo "✓ The tool reports the embedded runtime as its source"
fi

section "Compiling SCSS"
mkdir -p Sass out
process_template "$TEMPLATES_DIR/_tokens.scss.template" "Sass/_tokens.scss"
process_template "$TEMPLATES_DIR/input.scss.template" "Sass/input.scss"
echo "✓ Created source assets (SCSS)"

dotnet sass Sass/input.scss:out/input.css --style=compressed --no-source-map
echo "✓ 'dotnet sass' compiled the stylesheet"

if [ ! -f out/input.css ]; then
    echo "✗ CLI did not create CSS output"
    FAILED=1
else
    echo "✓ CLI created CSS output"
fi

section "Verifying Exit Code Propagation"
# The successful compile above ran under set -e, so reaching this point already proves a working run exits 0.
# What is left is the failing direction: the tool forwards arguments verbatim, so it must forward the exit
# code just as literally. A build script that keys off the exit status is silently broken if this regresses.
# The exact code is Dart Sass's to choose and is not part of any contract Scarlet publishes, so this asserts
# only that it is non-zero and reports whatever it was.
printf 'a { color: ; }\n' > Sass/broken.scss
set +e
dotnet sass Sass/broken.scss:out/broken.css > /dev/null 2>&1
BROKEN_STATUS=$?
set -e

if [ "$BROKEN_STATUS" -eq 0 ]; then
    echo "✗ A failing Dart Sass compile returned 0 through the tool"
    FAILED=1
else
    echo "✓ Dart Sass's non-zero exit code ($BROKEN_STATUS) propagated through the tool"
fi

if ! grep -q ".cli-tool:hover" out/input.css; then
    echo "✗ Nested selector output was missing"
    FAILED=1
    cat out/input.css
else
    echo "✓ Compiled CSS contains nested selector output"
fi

section "Verifying No Download Was Needed"
# These three checks together prove the embedded path: a RID package was restored, --scarlet-info says the
# source is embedded, and the only configured download cache was never created.
if [ -d "$SCARLET_SASS_CACHE_DIR" ]; then
    echo "✗ The embedded-runtime path unexpectedly created a download cache"
    FAILED=1
    find "$SCARLET_SASS_CACHE_DIR" -type f | head -5
else
    echo "✓ The download cache was never created"
fi

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
if [ "$FAILED" -ne 0 ]; then
    echo "✗ E2E CLI tool test failed"
    exit 1
fi
echo "✓ E2E CLI tool test completed successfully - Sass ran offline from the embedded runtime"
