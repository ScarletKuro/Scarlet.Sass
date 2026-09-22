param (
  [string]$OutputDirectory,
  [string]$DownloadFilename,
  [string]$SassVersion
)

$ErrorActionPreference = 'Stop'

$executableName = if ($DownloadFilename -like '*windows*') { 'sass.bat' } else { 'sass' }
$executablePath = Join-Path $OutputDirectory $executableName
$versionFile = "$executablePath.version"

if ((Test-Path $executablePath) -and (Test-Path $versionFile)) {
  $storedVersion = (Get-Content $versionFile -Raw).Trim()
  if ($storedVersion -eq $SassVersion) {
    Write-Host "Dart Sass $SassVersion already available at $OutputDirectory"
    exit 0
  }
}

$releaseUrl = "https://github.com/sass/dart-sass/releases/download/$SassVersion"
$downloadUrl = "$releaseUrl/$DownloadFilename"
$unique = [System.Guid]::NewGuid().ToString('N').Substring(0, 8)
$tempArchive = Join-Path $env:TEMP "dart-sass-$unique.archive"
$tempExtract = Join-Path $env:TEMP "dart-sass-extract-$unique"

try {
  Write-Host "Downloading Dart Sass from $downloadUrl"
  Invoke-WebRequest -Uri $downloadUrl -OutFile $tempArchive -UseBasicParsing

  New-Item -ItemType Directory -Path $tempExtract -Force | Out-Null

  if ($DownloadFilename.EndsWith('.zip')) {
    Expand-Archive -Path $tempArchive -DestinationPath $tempExtract -Force
  } else {
    tar -xzf $tempArchive -C $tempExtract
  }

  $extracted = Join-Path $tempExtract 'dart-sass'
  if (-not (Test-Path (Join-Path $extracted $executableName))) {
    throw "The archive '$DownloadFilename' did not contain dart-sass/$executableName."
  }

  Remove-Item -Path $OutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Path (Split-Path $OutputDirectory) -Force | Out-Null
  Move-Item -Path $extracted -Destination $OutputDirectory -Force
  Set-Content -Path $versionFile -Value $SassVersion -NoNewline
  Write-Host "Dart Sass setup complete at $OutputDirectory"
}
finally {
  Remove-Item -Path $tempArchive -Force -ErrorAction SilentlyContinue
  Remove-Item -Path $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
}
