$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'D:/software/unity/CGDD26107'
$cliPath = 'C:/Users/34808/AppData/Local/Unity/bin/unity.exe'
$catalogPath = 'Temp/ReferenceMap20261101/geometry-full-scene-final.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$expected = 'F5434E45AD1A4AFE940771A79081B25F5E54547B9C00F284364C8C7A936D3862'
if ((Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash -ne $expected) { throw 'Frozen catalog changed.' }
$outRoot = 'Documentation/ReferenceMap-2026-11-01/import'
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
for ($start = 0; $start -lt $catalog.assets.Count; $start += 100) {
    $argsJson = ConvertTo-Json -InputObject @($catalogPath, 'Scene', $start, 100) -Compress
    $raw = & $cliPath command run_script --caller plugin --skill unity-cli --format json --timeout 150 -- --file tools/ReferenceMap20261101/Import.cs --entry ReferenceMapImport.ImportBatch --args $argsJson --timeout_ms 135000
    $raw | Set-Content -LiteralPath (Join-Path $outRoot ('batch-{0:d4}.json' -f $start)) -Encoding utf8
    $result = $raw | ConvertFrom-Json
    if (-not $result.success -or -not $result.data.result.success) { throw ('Native import failed at {0}: {1}' -f $start, ($raw -join "`n")) }
    Write-Output ('Imported {0}/{1}' -f $result.data.result.result.end, $catalog.assets.Count)
}
$argsJson = ConvertTo-Json -InputObject @($catalogPath, 'Scene', 'ReferenceMiningStation3500') -Compress
$raw = & $cliPath command run_script --caller plugin --skill unity-cli --format json --timeout 150 -- --file tools/ReferenceMap20261101/Import.cs --entry ReferenceMapImport.CreateLayout --args $argsJson --timeout_ms 135000
$raw | Set-Content -LiteralPath (Join-Path $outRoot 'layout-final.json') -Encoding utf8
$result = $raw | ConvertFrom-Json
if (-not $result.success -or -not $result.data.result.success) { throw 'Native layout import failed.' }
Write-Output ($result.data.result.result | ConvertTo-Json -Compress)
