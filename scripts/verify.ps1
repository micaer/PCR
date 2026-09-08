$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
$oldAppData = $env:APPDATA
$oldCliHome = $env:DOTNET_CLI_HOME
$oldNuget = $env:NUGET_PACKAGES
try {
    $env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet'
    $env:NUGET_PACKAGES = Join-Path $repo '.nuget'
    $env:APPDATA = Join-Path $repo '.dotnet/appdata'
    foreach ($project in @('src/Pcr.Simulator/Pcr.Simulator.csproj','tests/Pcr.Tests/Pcr.Tests.csproj')) {
        dotnet restore $project --configfile NuGet.Config --nologo
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        dotnet build $project --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    Get-ChildItem php,tests/fixtures -Recurse -Filter '*.php' | ForEach-Object {
        php -l $_.FullName
        if ($LASTEXITCODE -ne 0) { throw "PHP syntax error: $($_.FullName)" }
    }
    dotnet tests/Pcr.Tests/bin/Debug/net9.0/Pcr.Tests.dll --integration $repo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally {
    $env:APPDATA = $oldAppData
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:NUGET_PACKAGES = $oldNuget
    Pop-Location
}
