$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HeartopiaPhotoReplacer\HeartopiaPhotoReplacerApp.csproj"

dotnet restore $project
dotnet build $project -c Release --no-restore

Write-Host "dotnet restore/build passed"
