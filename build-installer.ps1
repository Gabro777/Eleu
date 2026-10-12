# Prepara l'installer di Eleu: pubblica il programma con .NET incluso (chi lo installa non deve installare altro)
# e costruisce artifacts\Eleu-<versione>.msi con WiX 5 (strumento locale del progetto: dotnet tool restore).
param([string]$Versione = "")

$ErrorActionPreference = "Stop"
$radice = $PSScriptRoot
if ($Versione -eq "") {
    $Versione = ([xml](Get-Content "$radice\src\Eleu\Eleu.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$publish = "$radice\artifacts\publish"
$msi = "$radice\artifacts\Eleu-$Versione.msi"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$radice\src\Eleu\Eleu.csproj" -c Release -r win-x64 --self-contained true -p:Version=$Versione -o $publish
if ($LASTEXITCODE -ne 0) { throw "Pubblicazione non riuscita" }
# I simboli di debug non servono a chi installa.
Get-ChildItem $publish -Filter *.pdb | Remove-Item

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Ripristino degli strumenti non riuscito" }
dotnet wix build "$radice\installer\Eleu.wxs" -arch x64 -d Versione=$Versione -bindpath Publish=$publish -o $msi
if ($LASTEXITCODE -ne 0) { throw "Costruzione dell'installer non riuscita" }
Get-ChildItem $msi | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
