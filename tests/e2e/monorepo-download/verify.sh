#!/bin/bash
set -euo pipefail

# End-to-end test for SassRuntimeDownload=true in a monorepo-shaped build.
#
# This deliberately does not reference a Scarlet.Sass.Runtime.* package. The task must download official
# Dart Sass into a shared SassRuntimeDirectory and multiple projects must be able to build against it without
# observing a partially written runtime.
#
# Usage: ./verify.sh <workspace-path> <package-version> <sass-version>

if [ $# -ne 3 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <sass-version>"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
SASS_VERSION="$3"

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
    local package_escaped="${PACKAGE_VERSION//\\/\\\\}"
    local sass_version_escaped="${SASS_VERSION//\\/\\\\}"
    local shared_runtime_escaped="${SHARED_RUNTIME_DIR//\\/\\\\}"

    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$package_escaped|g" \
        -e "s|{{Sass_VERSION}}|$sass_version_escaped|g" \
        -e "s|{{SHARED_RUNTIME_DIR}}|$shared_runtime_escaped|g" \
        "$template_file" > "$output_file"
}

create_app_sources() {
    local app_dir="$1"
    local class_name="$2"

# The app source fixtures are checked in; only the selector differs so App1 and App2 can be asserted
# independently if this scenario needs to grow.
    mkdir -p "$app_dir/Sass"
    process_template "$TEMPLATES_DIR/_tokens.scss.template" "$app_dir/Sass/_tokens.scss"
    sed -e "s|{{CLASS_NAME}}|$class_name|g" \
        "$TEMPLATES_DIR/site.scss.template" > "$app_dir/Sass/site.scss"
}

TEST_DIR="/tmp/scarlet-sass-monorepo-download-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"
TEST_DIR="$(pwd -W 2>/dev/null || pwd)"
SHARED_RUNTIME_DIR="$TEST_DIR/tools"
mkdir -p "$SHARED_RUNTIME_DIR"

section "E2E Test: Monorepo Download"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Sass version: $SASS_VERSION"
echo "RID: $(detect_dotnet_rid)"
echo "Shared runtime: $SHARED_RUNTIME_DIR"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
process_template "$TEMPLATES_DIR/Directory.Build.props.template" "Directory.Build.props"
ok "Created nuget.config and shared Directory.Build.props"

dotnet new web -n App1 -o App1 > /dev/null
dotnet new web -n App2 -o App2 > /dev/null
process_template "$TEMPLATES_DIR/App.csproj.template" "App1/App1.csproj"
process_template "$TEMPLATES_DIR/App.csproj.template" "App2/App2.csproj"
create_app_sources "App1" "app-one"
create_app_sources "App2" "app-two"
ok "Created App1 and App2 Sass projects"

dotnet new sln -n MonorepoTest > /dev/null
dotnet sln add App1/App1.csproj App2/App2.csproj > /dev/null
ok "Created solution with two projects"

section "Restoring And Building"
dotnet restore --configfile nuget.config
dotnet build --no-restore --verbosity minimal
ok "Solution build completed"

FAILED=0
section "Verifying Sass Output"
for app in App1 App2; do
    if [ ! -f "$app/wwwroot/css/site.css" ]; then
        echo "✗ $app did not create CSS output"
        FAILED=1
    else
        echo "✓ $app created CSS output"
    fi
done

section "Verifying Shared Runtime Directory"
# One executable is enough here: this scenario is about shared download/publication, not runtime package
# resolution. The task-side resolver decides the exact platform archive.
RUNTIME_COUNT=$(find "$SHARED_RUNTIME_DIR" -type f \( -name "sass" -o -name "sass.bat" \) | wc -l)
if [ "$RUNTIME_COUNT" -lt 1 ]; then
    echo "✗ No Dart Sass executable found in shared runtime directory"
    FAILED=1
else
    echo "✓ Shared runtime directory contains Dart Sass executable(s)"
    find "$SHARED_RUNTIME_DIR" -type f \( -name "sass" -o -name "sass.bat" \) -print
fi

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

if [ "$FAILED" -eq 0 ]; then
    section "Result"
    ok "E2E monorepo download test completed successfully - shared SassRuntimeDirectory worked"
    exit 0
fi

section "Result"
fail "E2E monorepo download test failed"
exit 1
