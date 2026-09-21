#!/bin/bash
set -e

OUTPUT_DIRECTORY="$1"
DOWNLOAD_FILENAME="$2"
SASS_VERSION="$3"

if [[ "$DOWNLOAD_FILENAME" == *windows* ]]; then
  EXECUTABLE_NAME="sass.bat"
else
  EXECUTABLE_NAME="sass"
fi

EXECUTABLE_PATH="$OUTPUT_DIRECTORY/$EXECUTABLE_NAME"
VERSION_FILE="${EXECUTABLE_PATH}.version"

if [ -f "$EXECUTABLE_PATH" ] && [ -f "$VERSION_FILE" ] && [ "$(cat "$VERSION_FILE")" = "$SASS_VERSION" ]; then
  echo "Dart Sass $SASS_VERSION already available at $OUTPUT_DIRECTORY"
  exit 0
fi

RELEASE_URL="https://github.com/sass/dart-sass/releases/download/$SASS_VERSION"
DOWNLOAD_URL="$RELEASE_URL/$DOWNLOAD_FILENAME"
TMP_ARCHIVE="$(mktemp -t dart-sass-download-XXXXXXXX)"
TMP_EXTRACT="$(mktemp -d -t dart-sass-extract-XXXXXXXX)"

cleanup() {
  rm -f "$TMP_ARCHIVE" 2>/dev/null || true
  rm -rf "$TMP_EXTRACT" 2>/dev/null || true
}
trap cleanup EXIT

echo "Downloading Dart Sass from $DOWNLOAD_URL"
curl -fL "$DOWNLOAD_URL" -o "$TMP_ARCHIVE"

echo "Extracting to $TMP_EXTRACT"
case "$DOWNLOAD_FILENAME" in
  *.zip)
    unzip -o -q "$TMP_ARCHIVE" -d "$TMP_EXTRACT"
    ;;
  *.tar.gz)
    tar -xzf "$TMP_ARCHIVE" -C "$TMP_EXTRACT"
    ;;
  *)
    echo "Unsupported archive type: $DOWNLOAD_FILENAME" >&2
    exit 1
    ;;
esac

if [ ! -f "$TMP_EXTRACT/dart-sass/$EXECUTABLE_NAME" ]; then
  echo "Error: the archive '$DOWNLOAD_FILENAME' did not contain dart-sass/$EXECUTABLE_NAME." >&2
  exit 1
fi

rm -rf "$OUTPUT_DIRECTORY"
mkdir -p "$(dirname "$OUTPUT_DIRECTORY")"
mv "$TMP_EXTRACT/dart-sass" "$OUTPUT_DIRECTORY"
chmod +x "$EXECUTABLE_PATH" 2>/dev/null || true
echo -n "$SASS_VERSION" > "$VERSION_FILE"
echo "Dart Sass setup complete at $OUTPUT_DIRECTORY"
