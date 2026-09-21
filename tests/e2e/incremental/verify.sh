#!/bin/bash
set -euo pipefail

# End-to-end test for the Scarlet.Sass incremental contract.
#
# Dart Sass owns Sass dependency freshness through --update, so an unchanged input/output pair should not be
# rewritten. Scarlet owns the MSBuild-facing pieces around that: settings/runtime stamps, output manifests,
# stale output deletion, FileWrites, and clean.
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

mtime() {
    local file="$1"
    stat -c %Y "$file" 2>/dev/null || stat -f %m "$file"
}

TEST_DIR="/tmp/scarlet-sass-incremental-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

DOTNET_RID="$(detect_dotnet_rid)"
RUNTIME_PACKAGE="$(select_runtime_package "$DOTNET_RID")"
if [ -z "$RUNTIME_PACKAGE" ]; then
    fail "Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
fi

section "E2E Test: Incremental Sass Compilation"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "RID: $DOTNET_RID"
echo "Runtime package: $RUNTIME_PACKAGE"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
ok "Created nuget.config with local package source"
dotnet new razorclasslib -n TestRclIncremental > /dev/null
cd TestRclIncremental
process_template "$TEMPLATES_DIR/TestRclIncremental.csproj.template" "TestRclIncremental.csproj"
ok "Created incremental RCL project"

mkdir -p Sass
process_template "$TEMPLATES_DIR/_variables.scss.template" "Sass/_variables.scss"
process_template "$TEMPLATES_DIR/style.scss.template" "Sass/site.scss"

dotnet restore --configfile ../nuget.config
section "First Build"
dotnet build --no-restore --verbosity minimal
ok "First build completed"

CSS="wwwroot/css/site.css"
if [ ! -f "$CSS" ]; then
    fail "CSS output was not created"
fi
ok "CSS output was created"

section "Second Build Without Changes"
FIRST_MTIME="$(mtime "$CSS")"
sleep 2
dotnet build --no-restore --verbosity minimal
SECOND_MTIME="$(mtime "$CSS")"
# This is the heart of the scenario: Scarlet should call Dart Sass with --update and let Dart Sass skip
# already-current outputs instead of forcing a rewrite on every build.
if [ "$FIRST_MTIME" != "$SECOND_MTIME" ]; then
    fail "Unchanged Sass inputs rewrote CSS output; Dart Sass --update should skip it"
fi
ok "Unchanged Sass inputs left CSS output untouched"

section "Build After Editing Sass"
sleep 2
# The changed input comes from a checked-in fixture too; the script only performs the mutation the scenario
# is specifically testing.
cat "$TEMPLATES_DIR/update.scss.template" >> Sass/site.scss
dotnet build --no-restore --verbosity minimal
THIRD_MTIME="$(mtime "$CSS")"
if [ "$THIRD_MTIME" = "$SECOND_MTIME" ]; then
    fail "Changed Sass input did not rewrite CSS output"
fi
ok "Changed Sass input rewrote CSS output"

if ! grep -q ".incremental-updated" "$CSS"; then
    echo "Updated selector missing from compiled CSS"
    cat "$CSS"
    fail "Updated selector missing from compiled CSS"
fi
ok "Updated selector is present in compiled CSS"

section "Verifying Clean"
dotnet clean > /dev/null
# Clean must remove generated files through @(FileWrites). If this fails, consumers can keep stale CSS after
# cleaning the project.
if [ -f "$CSS" ]; then
    fail "dotnet clean did not remove generated CSS"
fi
ok "dotnet clean removed generated CSS"

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
ok "E2E incremental test completed successfully - Dart Sass --update and clean behaved correctly"
