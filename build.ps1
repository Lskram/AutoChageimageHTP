$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\HeartopiaPhotoReplacer\HeartopiaPhotoReplacerApp.csproj'
$publishDir = Join-Path $root 'publish\win-x64-selfcontained'
$distDir = Join-Path $root 'dist'

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Copy-Item -LiteralPath (Join-Path $publishDir 'HeartopiaPhotoReplacer.exe') `
    -Destination (Join-Path $distDir 'HeartopiaPhotoReplacer.exe') `
    -Force

Write-Host "Built: $distDir\HeartopiaPhotoReplacer.exe"
