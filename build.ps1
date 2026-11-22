param(
    [string]$Configuration = "Release",
    [string]$EmberwardDir = ""
)

$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    $argsList = @("build", "EmberwardCoop.csproj", "-c", $Configuration, "--nologo")
    if (-not [string]::IsNullOrWhiteSpace($EmberwardDir)) {
        $argsList += "-p:EmberwardDir=$EmberwardDir"
    }

    & dotnet @argsList
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }

    Write-Host "Built bin\$Configuration\EmberwardCoop.dll" -ForegroundColor Green
}
finally {
    Pop-Location
}
