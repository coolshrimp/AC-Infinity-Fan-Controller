$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish ACInfinityDesktop.csproj -c Release -p:PublishProfile=SingleExe --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    [xml]$aciProject = Get-Content -LiteralPath 'ACInfinityDesktop.csproj'
    $aciVersion = $aciProject.Project.PropertyGroup.Version
    $aciReleasePath = Join-Path $PSScriptRoot "Releases\dotnet-$aciVersion\AC-Infinity-Desktop.exe"
    if (-not (Test-Path -LiteralPath $aciReleasePath)) { throw 'Published executable is missing.' }
    Write-Host "Single-file release: $aciReleasePath"
} finally { Pop-Location }
