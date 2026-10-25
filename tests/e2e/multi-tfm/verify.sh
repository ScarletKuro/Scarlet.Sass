#!/bin/bash
set -euo pipefail

# End-to-end test for the multi-targeted Razor Class Library path.
#
# The important contract is that Sass runs once in the outer build before static web assets are resolved,
# not once per inner TFM. The produced CSS must also survive dotnet pack --no-build as a package static web
# asset under staticwebassets/.
#
# Inner builds reached some other way ask the outer build to compile instead, so the script also covers the
# two ways that happens: a rebuild, whose Clean phase evaluates the inner builds before anything is compiled,
# and an app that references the library, whose build never runs the library's outer build at all.
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
# Physical path from here on. On macOS /tmp is a symlink to /private/tmp, and a process started inside it sees
# the resolved path as its current directory. Builds started from a project's own directory then record
# /private/tmp paths, while a build addressing the same project through /tmp records the other spelling - and
# the SDK's incremental clean treats the first set as orphans of the second and deletes them, including the
# reference assembly a referencing app is about to compile against (CS0006).
TEST_DIR="$(pwd -P)"

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

# Counts the primary static web asset for site.css in a build manifest. Identity is the CSS file's path; the
# compressed alternatives end in .gz", so they do not match.
count_manifest_css() {
    grep -oE '"Identity"[[:space:]]*:[[:space:]]*"[^"]*"' "$1" | grep -Fc 'site.css"' || true
}

section "Rebuild after deleting the CSS"
# A rebuild's Clean phase evaluates and caches the inner builds while the CSS is missing. Before inner builds
# asked the outer build for the compilation's changes, Content registered in the outer build never reached
# them: the manifests came out empty and pack --no-build shipped a package without its CSS.
rm wwwroot/css/site.css
if ! dotnet build --no-restore --no-incremental --verbosity normal > rebuild.log 2>&1; then
    echo "✗ dotnet build --no-incremental failed"
    tail -n 40 rebuild.log
    exit 1
fi

REBUILD_RUNS="$(grep -c "Executing:" rebuild.log || true)"
if [ "$REBUILD_RUNS" -eq 1 ]; then
    echo "✓ The rebuild ran Sass exactly once"
else
    echo "✗ Expected the rebuild to run Sass once, it ran $REBUILD_RUNS time(s)"
    FAILED=1
fi

for tfm in net8.0 net9.0 net10.0; do
    MATCHES="$(count_manifest_css "obj/Debug/$tfm/staticwebassets.build.json")"
    if [ "$MATCHES" -eq 1 ]; then
        echo "✓ $tfm static web assets manifest carries the CSS"
    else
        echo "✗ Expected the CSS once in the $tfm manifest, found $MATCHES"
        FAILED=1
    fi
done

if ! dotnet pack --no-build --configuration Debug -o nupkg-rebuild --verbosity minimal > pack-rebuild.log 2>&1; then
    echo "✗ dotnet pack --no-build after the rebuild failed"
    tail -n 40 pack-rebuild.log
    exit 1
fi
REBUILD_NUPKG="$(find nupkg-rebuild -name '*.nupkg' | head -n 1)"
ENTRIES="$(unzip -Z1 "$REBUILD_NUPKG" | grep -Fcx "staticwebassets/css/site.css" || true)"
if [ "$ENTRIES" -eq 1 ] && [ "$(unzip -p "$REBUILD_NUPKG" staticwebassets/css/site.css | wc -c)" -gt 0 ]; then
    echo "✓ pack --no-build after the rebuild carries the CSS once, non-empty"
else
    echo "✗ Expected exactly one non-empty staticwebassets/css/site.css after the rebuild, found $ENTRIES"
    FAILED=1
fi

section "Building a referencing app on its own"
# What dotnet run on an app does: its ProjectReference builds the library's net10.0 inner build directly and
# never the library's outer build, so the inner build has to ask the outer one to compile. Before it did, Sass
# never ran here and the app answered 404 for the library's CSS.
RCL_DIR="$(pwd)"
APP_DIR="$TEST_DIR/TestApp"
mkdir -p "$APP_DIR"
cp "$TEMPLATES_DIR/TestApp.csproj.template" "$APP_DIR/TestApp.csproj"
cp "$TEMPLATES_DIR/Program.cs.template" "$APP_DIR/Program.cs"
rm wwwroot/css/site.css

if ! dotnet build "$APP_DIR/TestApp.csproj" --configuration Debug --verbosity normal > app-build.log 2>&1; then
    echo "✗ Building the referencing app failed"
    tail -n 40 app-build.log
    exit 1
fi
echo "✓ Referencing app built"

APP_RUNS="$(grep -c "Executing:" app-build.log || true)"
if [ "$APP_RUNS" -eq 1 ]; then
    echo "✓ Building the app ran Sass exactly once"
else
    echo "✗ Expected building the app to run Sass once, it ran $APP_RUNS time(s)"
    FAILED=1
fi

MATCHES="$(count_manifest_css "$APP_DIR/obj/Debug/net10.0/staticwebassets.build.json")"
if [ "$MATCHES" -eq 1 ]; then
    echo "✓ App manifest carries the library's CSS"
else
    echo "✗ Expected the library's CSS once in the app manifest, found $MATCHES"
    FAILED=1
fi

# Serve it: the manifest is the means, the app answering for the CSS is the point. Development is the
# environment in which an app serves referenced projects' static web assets straight from their wwwroot.
APP_PORT=$((20000 + RANDOM % 10000))
ASPNETCORE_ENVIRONMENT=Development dotnet "$APP_DIR/bin/Debug/net10.0/TestApp.dll" \
    --urls "http://127.0.0.1:$APP_PORT" --contentRoot "$APP_DIR" > app-run.log 2>&1 &
APP_PID=$!
CSS_URL="http://127.0.0.1:$APP_PORT/_content/TestRclMultiTfm/css/site.css"
STATUS=000
for _ in $(seq 1 60); do
    STATUS="$(curl --silent --max-time 2 --output served.css --write-out '%{http_code}' "$CSS_URL" || true)"
    if [ "$STATUS" != "000" ]; then
        break
    fi
    sleep 0.5
done
kill "$APP_PID" 2>/dev/null || true
wait "$APP_PID" 2>/dev/null || true

if [ "$STATUS" = "200" ] && cmp -s served.css "$RCL_DIR/wwwroot/css/site.css"; then
    echo "✓ App serves the library's CSS at /_content/TestRclMultiTfm/css/site.css"
else
    echo "✗ App answered $STATUS for $CSS_URL, or served something other than the generated CSS"
    tail -n 20 app-run.log || true
    FAILED=1
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
