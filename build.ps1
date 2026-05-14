param(
    [switch]$SkipZip
)

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

function Resolve-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    [xml]$xml = Get-Content -LiteralPath $ProjectPath
    $version = $xml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Project version was not found in $ProjectPath"
    }

    return $version.Trim()
}

function Try-SignFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $signTool = $env:SIGNTOOL_PATH
    $pfx = $env:CODE_SIGN_PFX
    $password = $env:CODE_SIGN_PASSWORD
    $timestamp = if ($env:CODE_SIGN_TIMESTAMP_URL) { $env:CODE_SIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }

    if (-not $signTool -or -not (Test-Path $signTool) -or -not $pfx -or -not (Test-Path $pfx)) {
        Write-Host "Skipping code signing (SIGNTOOL_PATH / CODE_SIGN_PFX not configured)."
        return
    }

    & $signTool sign /fd SHA256 /f $pfx /p $password /tr $timestamp /td SHA256 $FilePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $FilePath"
    }

    Write-Host "Signed: $FilePath"
}

$version = Resolve-ProjectVersion -ProjectPath $project
$packageName = "HeartopiaPhotoReplacer-v$version-win-x64"
$packageDir = Join-Path $distDir $packageName
$zipPath = Join-Path $distDir "$packageName.zip"

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

if (Test-Path $packageDir) {
    Remove-Item -Recurse -Force $packageDir
}

Invoke-Dotnet @(
    'publish', $project,
    '-c', 'Release',
    '-p:RuntimeIdentifier=win-x64',
    '-p:SelfContained=true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $publishDir
)

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $packageDir -Recurse -Force

foreach ($file in @('README.md', 'CHANGELOG.md', 'NOTICE.txt', 'EULA.txt')) {
    $source = Join-Path $root $file
    if (Test-Path $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $packageDir $file) -Force
    }
}

$exePath = Join-Path $packageDir 'HeartopiaPhotoReplacer.exe'
Try-SignFile -FilePath $exePath

if (-not $SkipZip) {
    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }

    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
}

Write-Host "Built self-contained package: $packageDir"
if (-not $SkipZip) {
    Write-Host "Packaged zip: $zipPath"
}
