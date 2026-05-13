$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\HeartopiaPhotoReplacer\HeartopiaPhotoReplacerApp.csproj'
$publishDir = Join-Path $root 'publish\win-x64-selfcontained'
$distDir = Join-Path $root 'dist'

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($Arguments -join ' ')"
    }
}

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

Invoke-Dotnet @(
    'publish', $project,
    '-c', 'Release',
    '-o', $publishDir
)

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Copy-Item -LiteralPath (Join-Path $publishDir 'HeartopiaPhotoReplacer.exe') `
    -Destination (Join-Path $distDir 'HeartopiaPhotoReplacer.exe') `
    -Force

Write-Host "Built (framework-dependent): $distDir\HeartopiaPhotoReplacer.exe"
