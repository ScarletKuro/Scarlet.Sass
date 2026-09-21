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
echo "RID: ${DOTNET_RID:-<empty>}"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
ok "Created nuget.config with local package source"

section "Installing Local Tool"
dotnet new tool-manifest > /dev/null
# The CLI version is $(SassVersion).$(SassCliRevision). With revision 0, NuGet normalizes it to SassVersion.
dotnet tool install Scarlet.Sass.Cli --version "$RUNTIME_VERSION" --configfile nuget.config > /dev/null
ok "Installed Scarlet.Sass.Cli into a local tool manifest"

section "Verifying Embedded Runtime"
RID_PACKAGE_DIR="$NUGET_PACKAGES/scarlet.sass.cli.$DOTNET_RID"
if [ -n "$DOTNET_RID" ] && [ ! -d "$RID_PACKAGE_DIR" ]; then
    echo "Expected RID-specific CLI package at $RID_PACKAGE_DIR"
    find "$NUGET_PACKAGES" -maxdepth 1 -type d -print || true
    fail "RID-specific CLI package was not restored"
fi
ok "The $DOTNET_RID tool package was restored"

EMBEDDED_SASS="$(find "$RID_PACKAGE_DIR" -type f \( -name 'sass' -o -name 'sass.bat' \) 2>/dev/null | head -n 1)"
if [ -z "$EMBEDDED_SASS" ]; then
    fail "No embedded Dart Sass executable found in $RID_PACKAGE_DIR"
fi
ok "The tool package ships a Dart Sass executable"

section "Verifying Argument Forwarding And Diagnostics"
REPORTED_VERSION="$(dotnet sass --version)"
if [ "$REPORTED_VERSION" != "$RUNTIME_VERSION" ]; then
    fail "Expected Dart Sass $RUNTIME_VERSION, got '$REPORTED_VERSION'"
fi
ok "'dotnet sass --version' printed Dart Sass's version ($REPORTED_VERSION)"

if ! dotnet sass --scarlet-info | grep -q "^Source .*embedded"; then
    echo "dotnet sass --scarlet-info did not report embedded source"
    dotnet sass --scarlet-info || true
    fail "The tool did not report the embedded binary as its source"
fi
ok "The tool reports the embedded binary as its source"

section "Compiling SCSS"
mkdir -p Sass out
process_template "$TEMPLATES_DIR/_tokens.scss.template" "Sass/_tokens.scss"
process_template "$TEMPLATES_DIR/input.scss.template" "Sass/input.scss"

dotnet sass Sass/input.scss:out/input.css --style=compressed --no-source-map

if [ ! -f out/input.css ]; then
    fail "CLI did not create CSS output"
fi
ok "CLI created CSS output"

if ! grep -q ".cli-tool:hover" out/input.css; then
    echo "Compiled CSS does not contain nested selector output"
    cat out/input.css
    fail "Nested selector output was missing"
fi
ok "Compiled CSS contains nested selector output"

section "Verifying No Download Was Needed"
# These three checks together prove the embedded path: a RID package was restored, --scarlet-info says the
# source is embedded, and the only configured download cache was never created.
if [ -d "$SCARLET_SASS_CACHE_DIR" ]; then
    echo "Download cache was created even though the embedded runtime was used"
    find "$SCARLET_SASS_CACHE_DIR" -type f | head -5
    fail "The embedded-runtime path unexpectedly created a download cache"
fi
ok "The download cache was never created"

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
ok "E2E CLI tool test completed successfully - Sass ran offline from the embedded binary"
