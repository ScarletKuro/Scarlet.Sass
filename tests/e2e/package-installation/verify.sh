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
    echo "✗ Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
    exit 1
fi

section "E2E Test: Package Installation"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "✓ Created test directory: $TEST_DIR"
echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>} package=$RUNTIME_PACKAGE"

# The temporary project is deliberately created from the same kind of templates a consumer would author:
# explicit SassBeforeStaticWebAssets items, no JSON configuration convention, and a runtime package reference.
process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"
dotnet new web -n TestSassPackage > /dev/null
cd TestSassPackage
echo "✓ Created test ASP.NET Core application"

mkdir -p Sass
process_template "$TEMPLATES_DIR/_variables.scss.template" "Sass/_variables.scss"
process_template "$TEMPLATES_DIR/site.scss.template" "Sass/site.scss"

process_template "$TEMPLATES_DIR/TestSassPackage.csproj.template" "TestSassPackage.csproj"
echo "✓ Created Sass inputs and project file"

section "Restoring And Building"
dotnet restore --configfile ../nuget.config
dotnet build --no-restore --verbosity normal 2>&1 | tee build.log
echo "✓ Build completed"

section "Verifying Sass Output"
if [ ! -f wwwroot/css/site.css ]; then
    echo "✗ CSS output was not created"
    FAILED=1
else
    echo "✓ CSS output was created"
fi

if [ ! -f wwwroot/css/site.css.map ]; then
    echo "✗ Source map output was not created"
    FAILED=1
else
    echo "✓ Source map output was created"
fi

if ! grep -q ".package-installation:hover" wwwroot/css/site.css; then
    echo "✗ Nested selector output was missing"
    FAILED=1
    cat wwwroot/css/site.css
else
    echo "✓ Compiled CSS contains nested selector output"
fi

section "Verifying The Runtime Pack Contract"
# This proves the packed runtime package's build assets were imported and bound into the task through
# @(SassRuntimePack). A project-reference test can pass while this is broken in the nupkg.
if ! grep -qE "(Using|Selected) Sass runtime pack $RUNTIME_PACKAGE" build.log; then
    echo "✗ Build log did not show runtime pack resolution for $RUNTIME_PACKAGE"
    FAILED=1
    grep -E "Sass runtime pack|Using Sass at" build.log || true
else
    echo "✓ Sass was resolved from runtime pack $RUNTIME_PACKAGE"
fi

section "Verifying Static Web Assets"
dotnet publish --no-build --configuration Debug -o publish-output > /dev/null
if [ ! -f publish-output/wwwroot/css/site.css ]; then
    echo "✗ Published output is missing generated static web asset"
    FAILED=1
else
    echo "✓ Generated CSS was published as a static web asset"
fi

section "Verifying A Project-Authored Pack Override"
# The README documents declaring your own SassRuntimePack to point the build at a Sass you supply. That path
# is evaluated differently from the package one - the package's props are imported before the project body -
# so only a real build proves a project-authored item merges with the package-provided pack and that Priority
# decides between them. The copy is what makes the two packs distinct: same RID and same directory would be
# de-duplicated, and the package pack (declared first) would win.
RESOLVED_SASS="$(grep -m1 'Using Sass at:' build.log | sed 's/.*Using Sass at: //' | tr -d '\r' | tr '\\' '/')"

if [ -z "$RESOLVED_SASS" ] || [ ! -f "$RESOLVED_SASS" ]; then
    echo "✗ Could not determine the Sass launcher resolved by the first build"
    FAILED=1
else
    # Dart Sass is a directory tree, not a single file: the launcher execs into src/dart and src/sass.snapshot
    # beside it, so the whole dart-sass folder has to move together. Layout is <rid>/native/dart-sass/<launcher>.
    LAUNCHER_DIR="$(dirname "$RESOLVED_SASS")"
    SASS_RID="$(basename "$(dirname "$(dirname "$LAUNCHER_DIR")")")"
    CUSTOM_RUNTIMES="$TEST_DIR/custom-sass/runtimes"

    mkdir -p "$CUSTOM_RUNTIMES/$SASS_RID/native"
    cp -r "$LAUNCHER_DIR" "$CUSTOM_RUNTIMES/$SASS_RID/native/"
    # NuGet carries no permission bits and cp preserves what it found, so re-assert both executables.
    find "$CUSTOM_RUNTIMES" -type f \( -name sass -o -name dart \) -exec chmod +x {} \; 2>/dev/null || true

    # MSBuild needs a native path; /tmp/... would resolve to C:\tmp\... on Windows
    if command -v cygpath > /dev/null 2>&1; then
        CUSTOM_RUNTIMES_MSBUILD="$(cygpath -m "$CUSTOM_RUNTIMES")"
    else
        CUSTOM_RUNTIMES_MSBUILD="$CUSTOM_RUNTIMES"
    fi

    echo "✓ Staged a custom Dart Sass at $CUSTOM_RUNTIMES_MSBUILD"

    # The pack id deliberately sorts after "Scarlet.*" because at equal priority the alphabetically first id
    # wins - so Priority is the only thing that can explain this pack being chosen.
    {
        sed 's|</Project>||' TestSassPackage.csproj
        cat <<EOF
  <ItemGroup>
    <SassRuntimePack Include="Zephyr.Sass.Custom">
      <Rid>$SASS_RID</Rid>
      <RuntimesPath>$CUSTOM_RUNTIMES_MSBUILD</RuntimesPath>
      <Priority>100</Priority>
    </SassRuntimePack>
  </ItemGroup>
</Project>
EOF
    } > TestSassPackage.csproj.new && mv TestSassPackage.csproj.new TestSassPackage.csproj

    dotnet build --target:Rebuild --verbosity normal 2>&1 | tee override.log
    OVERRIDE_STATUS=${PIPESTATUS[0]}

    if [ "$OVERRIDE_STATUS" -ne 0 ]; then
        echo "✗ Build with a project-authored pack failed with exit code $OVERRIDE_STATUS"
        FAILED=1
    elif ! grep -qE "Selected Sass runtime pack Zephyr\.Sass\.Custom .* out of 2 candidates" override.log; then
        echo "✗ The project-authored pack did not win over $RUNTIME_PACKAGE"
        grep -E "Sass runtime pack|Using Sass at" override.log || echo "  (no runtime-related log lines)"
        FAILED=1
    elif ! grep -q "Using Sass at: .*custom-sass" override.log; then
        echo "✗ The winning pack was reported but a different Sass was executed"
        grep -E "Using Sass at" override.log || echo "  (none)"
        FAILED=1
    else
        echo "✓ A project-authored SassRuntimePack with a higher Priority overrode the package-provided pack"
        echo "✓ The custom Dart Sass is the one that actually ran"
    fi
fi

section "Verifying Clean"
dotnet clean > /dev/null
if [ -f wwwroot/css/site.css ] || [ -f wwwroot/css/site.css.map ]; then
    echo "✗ dotnet clean did not remove generated Sass outputs"
    FAILED=1
else
    echo "✓ dotnet clean removed generated Sass outputs"
fi

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
if [ "$FAILED" -ne 0 ]; then
    echo "✗ E2E package installation test failed"
    exit 1
fi
echo "✓ E2E test completed successfully - Sass executed via the SassRuntimePack contract"
