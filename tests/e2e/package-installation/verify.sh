#!/bin/bash
set -euo pipefail

# End-to-end test for consuming Scarlet.Sass.MSBuild from packed NuGet packages.
#
# This catches package-layout problems unit tests cannot see: the task assembly must load with its packaged
# dependencies, the runtime package must contribute @(SassRuntimePack), Sass must run before static web
# assets, generated CSS must be publishable content, and dotnet clean must remove generated outputs.
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
    local package_escaped="${PACKAGE_VERSION//\\/\\\\}"
    local runtime_package_escaped="${RUNTIME_PACKAGE//\\/\\\\}"
    local runtime_version_escaped="${RUNTIME_VERSION//\\/\\\\}"

    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$package_escaped|g" \
        -e "s|{{RUNTIME_PACKAGE}}|$runtime_package_escaped|g" \
        -e "s|{{RUNTIME_VERSION}}|$runtime_version_escaped|g" \
        "$template_file" > "$output_file"
}

TEST_DIR="/tmp/scarlet-sass-package-installation-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

DOTNET_RID="$(detect_dotnet_rid)"
RUNTIME_PACKAGE="$(select_runtime_package "$DOTNET_RID")"
if [ -z "$RUNTIME_PACKAGE" ]; then
    fail "Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
fi

section "E2E Test: Package Installation"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "RID: $DOTNET_RID"
echo "Runtime package: $RUNTIME_PACKAGE"

# The temporary project is deliberately created from the same kind of templates a consumer would author:
# explicit SassBeforeStaticWebAssets items, no JSON configuration convention, and a runtime package reference.
process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
ok "Created nuget.config with local package source"
dotnet new web -n TestSassPackage > /dev/null
cd TestSassPackage
ok "Created test ASP.NET Core application"

mkdir -p Sass
process_template "$TEMPLATES_DIR/_variables.scss.template" "Sass/_variables.scss"
process_template "$TEMPLATES_DIR/site.scss.template" "Sass/site.scss"

process_template "$TEMPLATES_DIR/TestSassPackage.csproj.template" "TestSassPackage.csproj"
ok "Created Sass inputs and project file"

section "Restoring And Building"
dotnet restore --configfile ../nuget.config
dotnet build --no-restore --verbosity normal 2>&1 | tee build.log
ok "Build completed"

section "Verifying Sass Output"
if [ ! -f wwwroot/css/site.css ]; then
    fail "CSS output was not created"
fi
ok "CSS output was created"

if [ ! -f wwwroot/css/site.css.map ]; then
    fail "Source map output was not created"
fi
ok "Source map output was created"

if ! grep -q ".package-installation:hover" wwwroot/css/site.css; then
    echo "Compiled CSS does not contain nested selector output"
    cat wwwroot/css/site.css
    fail "Nested selector output was missing"
fi
ok "Compiled CSS contains nested selector output"

section "Verifying The Runtime Pack Contract"
# This proves the packed runtime package's build assets were imported and bound into the task through
# @(SassRuntimePack). A project-reference test can pass while this is broken in the nupkg.
if ! grep -qE "(Using|Selected) Sass runtime pack $RUNTIME_PACKAGE" build.log; then
    echo "Build log did not show runtime pack resolution for $RUNTIME_PACKAGE"
    grep -E "Sass runtime pack|Using Sass at" build.log || true
    fail "Expected runtime pack resolution was not logged"
fi
ok "Sass was resolved from runtime pack $RUNTIME_PACKAGE"

section "Verifying Static Web Assets"
dotnet publish --no-build --configuration Debug -o publish-output > /dev/null
if [ ! -f publish-output/wwwroot/css/site.css ]; then
    fail "Published output is missing generated static web asset"
fi
ok "Generated CSS was published as a static web asset"

section "Verifying Clean"
dotnet clean > /dev/null
if [ -f wwwroot/css/site.css ] || [ -f wwwroot/css/site.css.map ]; then
    fail "dotnet clean did not remove generated Sass outputs"
fi
ok "dotnet clean removed generated Sass outputs"

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
ok "E2E test completed successfully - Sass executed via the SassRuntimePack contract"
