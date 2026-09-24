$current = Get-Location
Get-ChildItem -Path "tmp_pkg" -Directory | ForEach-Object {
    $guidDir = $_.FullName
    $pathFile = Join-Path $guidDir "pathname"
    $assetFile = Join-Path $guidDir "asset"
    $metaFile = Join-Path $guidDir "asset.meta"
    
    if ((Test-Path $pathFile) -and (Test-Path $assetFile)) {
        $targetRel = (Get-Content $pathFile -Raw).Trim()
        $target = Join-Path $current $targetRel
        $targetDir = Split-Path $target
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }
        Copy-Item -Force $assetFile $target
        if (Test-Path $metaFile) {
            Copy-Item -Force $metaFile ($target + ".meta")
        }
        Write-Host "Restored: $targetRel"
    }
}
Remove-Item -Recurse -Force "tmp_pkg"
