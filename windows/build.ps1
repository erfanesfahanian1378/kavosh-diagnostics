# Builds Kavosh Diagnostics for Windows as one portable Kavosh.exe per architecture and zips it.
#
#   ./build.ps1                 # x64 and ARM64
#   ./build.ps1 -Runtimes win-x64
#
# Needs the .NET 8 SDK. Output: windows/dist/<runtime>/Kavosh.exe and windows/dist/*.zip
param(
    [string[]] $Runtimes = @("win-x64", "win-arm64")
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$dist = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

foreach ($runtime in $Runtimes) {
    $out = Join-Path $dist $runtime
    Write-Host "== Publishing $runtime"
    dotnet publish src/Kavosh.App/Kavosh.App.csproj -c Release -r $runtime --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $runtime" }

    Copy-Item THIRD-PARTY-NOTICES.txt $out -Force
    $label = if ($runtime -eq "win-arm64") { "ARM64" } else { "x64" }
    $zip = Join-Path $dist "Kavosh-Diagnostics-Windows-$label.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out "Kavosh.exe"), (Join-Path $out "THIRD-PARTY-NOTICES.txt") -DestinationPath $zip
    $size = [math]::Round((Get-Item (Join-Path $out "Kavosh.exe")).Length / 1MB, 1)
    Write-Host "   Kavosh.exe: $size MB -> $zip"
}
