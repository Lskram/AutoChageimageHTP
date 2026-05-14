$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$testsProject = Join-Path $root "tests\HeartopiaPhotoReplacer.Tests\HeartopiaPhotoReplacer.Tests.csproj"

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

Invoke-Dotnet @('restore', $testsProject)
Invoke-Dotnet @('run', '--project', $testsProject, '-c', 'Release', '--no-restore')

Write-Host "Automated tests passed"
