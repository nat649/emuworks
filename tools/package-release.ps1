# SPDX-License-Identifier: MIT
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version = '0.1.0',
    [string]$PublishedDirectory
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $sourceRoot 'releases'
New-Item -ItemType Directory $releaseRoot -Force | Out-Null
if (-not $PublishedDirectory) {
    $PublishedDirectory = Join-Path $releaseRoot ('publish-v' + $Version)
    & dotnet publish (Join-Path $sourceRoot 'app/EmuWorks.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UseSharedCompilation=false -p:NuGetAudit=false "-p:Version=$Version" -o $PublishedDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
}
$publishedExe = Join-Path $PublishedDirectory 'EmuWorks.exe'
if (-not (Test-Path -LiteralPath $publishedExe)) { throw 'Published EmuWorks.exe is missing.' }
$archive = Join-Path $releaseRoot "EmuWorks-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $archive) { throw 'Release archive already exists. Choose a new version or preserve the existing artifact before rebuilding.' }
$stage = Join-Path $releaseRoot ('package-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'EmuWorks'
New-Item -ItemType Directory $package | Out-Null
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $package 'EmuWorks.exe')
# Only versioned files are eligible; user firmware and working ROMs are excluded.
$files = & git -C $sourceRoot ls-files renode firmware docs
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked package files.' }
foreach ($relative in $files) {
    $destination = Join-Path $package $relative
    New-Item -ItemType Directory (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $relative) -Destination $destination
}
foreach ($name in @('README.md', 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $package }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'docs/windows-release.md') -Destination (Join-Path $package 'QUICKSTART.md')
$assets = Get-Content -LiteralPath (Join-Path $sourceRoot 'app/obj/project.assets.json') -Raw | ConvertFrom-Json
$licenses = Join-Path $package 'licenses/dotnet'
New-Item -ItemType Directory $licenses -Force | Out-Null
$runtimePackages = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies |
    Where-Object { $_.name -match '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$' } |
    ForEach-Object { $_.name + '/' + $_.version.Trim('[', ']').Split(',')[0].Trim() } |
    Select-Object -Unique)
if ($runtimePackages.Count -ne 2) { throw 'Expected both self-contained .NET runtime packages.' }
foreach ($identity in $runtimePackages) {
    $parts = $identity.Split('/')
    $pkg = Join-Path $assets.project.restore.packagesPath $identity.ToLowerInvariant()
    $destination = Join-Path $licenses $parts[0]
    New-Item -ItemType Directory $destination | Out-Null
    $license = @('LICENSE', 'LICENSE.TXT') | ForEach-Object { Join-Path $pkg $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $license) { throw "Missing runtime license: $identity" }
    Copy-Item -LiteralPath $license -Destination $destination
    $notices = Join-Path $pkg 'THIRD-PARTY-NOTICES.TXT'
    if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination $destination }
}
$commit = & git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
@{ version = $Version; platform = 'win-x64'; sourceCommit = $commit; selfContained = $true; runtimePackages = $runtimePackages } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'RELEASE.json') -Encoding utf8
Compress-Archive -LiteralPath $package -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
Write-Output $archive
Write-Output "SHA256: $hash"
