# 항목 19B: Kenney CC0 키트 설치. temp-assets\<kit>.zip이 있으면 내려받기를 건너뛰고 풀기만 한다.
# 사이트가 zip 링크를 페이지에 두지 않으면(자바스크립트 버튼) 우회하지 않고 경고만 낸다. 그때는 kenney.nl에서 직접 받아 temp-assets\에 넣는다.
$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"
$root = "C:\Users\MinGyu\FactoryWars"
$dest = if ($env:FW_KENNEY_DEST) { $env:FW_KENNEY_DEST } else { "$root\Game\Assets\ExternalAssets\Kenney" }
$temp = "$root\temp-assets"
New-Item -ItemType Directory -Force -Path $dest,$temp | Out-Null

$kits = @("nature-kit", "city-kit-industrial", "city-kit-commercial", "tower-defense-kit")
foreach ($kit in $kits) {
    try {
        # 직접 받은 zip은 이름이 kenney_<kit>_1.0.zip 같은 꼴이다.
        $found   = Get-ChildItem $temp -Filter "*$kit*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
        $zipFile = if ($found) { $found.FullName } else { "$temp\$kit.zip" }
        $exDir   = "$temp\$kit"
        if (-not (Test-Path $zipFile)) {
            $page   = "https://kenney.nl/assets/$kit"
            $html   = Invoke-WebRequest -Uri $page -UseBasicParsing
            $zipUrl = ($html.Links | Where-Object { $_.href -like "*.zip" } | Select-Object -First 1).href
            if (-not $zipUrl) { Write-Warning "$kit : 페이지에서 zip 링크를 못 찾음"; continue }
            if ($zipUrl -notmatch '^https?://') { $zipUrl = ([Uri]::new([Uri]$page, $zipUrl)).AbsoluteUri }
            "$kit zip: $zipUrl"
            Invoke-WebRequest -Uri $zipUrl -OutFile $zipFile -UseBasicParsing
        }
        Expand-Archive -Path $zipFile -DestinationPath $exDir -Force

        $out = "$dest\$kit"
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $fbxDir = Get-ChildItem $exDir -Recurse -Directory | Where-Object { $_.Name -eq "FBX format" } | Select-Object -First 1
        if ($fbxDir) { Copy-Item -Path "$($fbxDir.FullName)\*" -Destination $out -Recurse -Force }
        else         { Get-ChildItem $exDir -Recurse -Include *.fbx, *.png | Copy-Item -Destination $out -Force }
        Get-ChildItem $exDir -Recurse -Include "License*.txt" | Select-Object -First 1 |
            Copy-Item -Destination "$out\License.txt" -Force

        "{0}: FBX {1}개, zip {2:N1}MB" -f $kit, (Get-ChildItem $out -Recurse -Filter *.fbx).Count, ((Get-Item $zipFile).Length/1MB)
    } catch {
        Write-Warning "$kit 실패: $($_.Exception.Message)"
    }
}
# 받은 zip은 남기고 풀어 둔 폴더만 지운다(사용자가 넣어 준 zip을 다시 쓸 수 있게).
foreach ($kit in $kits) { if (Test-Path "$temp\$kit") { Remove-Item "$temp\$kit" -Recurse -Force } }
