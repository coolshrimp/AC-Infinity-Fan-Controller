$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish ACInfinityDesktop.csproj -c Release -p:PublishProfile=SingleExe --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $aciReleasePath = Join-Path $PSScriptRoot 'Releases\dotnet-0.4.2\AC-Infinity-Desktop.exe'
    if (-not (Test-Path -LiteralPath $aciReleasePath)) { throw 'Published executable is missing.' }
    Write-Host "Single-file release: $aciReleasePath"
} finally { Pop-Location }
