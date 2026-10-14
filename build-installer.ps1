# Builds the Eleu installer: publishes the program with .NET included (whoever installs it needs nothing else)
# and builds artifacts\Eleu-<version>.msi with WiX 5 (a local tool of the project: dotnet tool restore).
param([string]$Version = "")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ($Version -eq "") {
    $Version = ([xml](Get-Content "$root\src\Eleu\Eleu.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$publish = "$root\artifacts\publish"
$msi = "$root\artifacts\Eleu-$Version.msi"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root\src\Eleu\Eleu.csproj" -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
# Debug symbols are not needed by whoever installs.
Get-ChildItem $publish -Filter *.pdb | Remove-Item

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Tool restore failed" }
dotnet wix build "$root\installer\Eleu.wxs" -arch x64 -d Version=$Version -bindpath Publish=$publish -o $msi
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Get-ChildItem $msi | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
