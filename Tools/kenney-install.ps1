# 항목 19B: Kenney CC0 키트 설치.
# temp-assets\kenney_<kit>*.zip(버전 표기 허용)을 풀어 FBX와 텍스처(png), License.txt만 Assets\ExternalAssets\Kenney\<kit>\로 복사한다.
# glb·obj·mtl·미리보기는 저장소 크기를 줄이려고 빼고, 받은 zip은 그대로 둔다.
# zip이 없으면 kenney.nl 페이지에서 zip 링크를 찾아 본다. 페이지에 링크가 없으면(자바스크립트 버튼) 우회하지 않고 경고만 낸다.
$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"
$root = "C:\Users\MinGyu\FactoryWars"
$dest = if ($env:FW_KENNEY_DEST) { $env:FW_KENNEY_DEST } else { "$root\Game\Assets\ExternalAssets\Kenney" }
$temp = "$root\temp-assets"
New-Item -ItemType Directory -Force -Path $dest,$temp | Out-Null

$kits = @("nature-kit", "city-kit-commercial", "city-kit-industrial", "city-kit-roads", "factory-kit", "tower-defense-kit", "train-kit",
          "car-kit", "modular-buildings", "city-kit-suburban", "building-kit", "modular-space-kit", "space-station-kit", "mini-characters",
          "furniture-kit", "prototype-kit", "mini-forest", "blaster-kit", "mini-arcade", "animated-characters-protagonists", "watercraft-pack")
foreach ($kit in $kits) {
    try {
        $found   = Get-ChildItem $temp -Filter "kenney_$kit*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
        $zipFile = if ($found) { $found.FullName } else { "$temp\kenney_$kit.zip" }
        $exDir   = "$temp\$kit"
        if (-not (Test-Path $zipFile)) {
            $page   = "https://kenney.nl/assets/$kit"
            $html   = Invoke-WebRequest -Uri $page -UseBasicParsing
            $zipUrl = ($html.Links | Where-Object { $_.href -like "*.zip" } | Select-Object -First 1).href
            if (-not $zipUrl) { Write-Warning "$kit : 페이지에서 zip 링크를 못 찾음"; continue }
            if ($zipUrl -notmatch '^https?://') { $zipUrl = ([Uri]::new([Uri]$page, $zipUrl)).AbsoluteUri }
            Invoke-WebRequest -Uri $zipUrl -OutFile $zipFile -UseBasicParsing
        }
        if (Test-Path $exDir) { Remove-Item $exDir -Recurse -Force }
        Expand-Archive -Path $zipFile -DestinationPath $exDir -Force

        $out = "$dest\$kit"
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $fbxDir = Get-ChildItem $exDir -Recurse -Directory | Where-Object { $_.Name -eq "FBX format" } | Select-Object -First 1
        if ($fbxDir) {
            Get-ChildItem $fbxDir.FullName -Filter *.fbx | Copy-Item -Destination $out -Force
            $textures = Join-Path $fbxDir.FullName "Textures"
            if (Test-Path $textures) {
                New-Item -ItemType Directory -Force -Path "$out\Textures" | Out-Null
                Get-ChildItem $textures -Filter *.png | Copy-Item -Destination "$out\Textures" -Force
            }
        } else {
            Get-ChildItem $exDir -Recurse -Filter *.fbx | Copy-Item -Destination $out -Force
            $skin = Get-ChildItem $exDir -Recurse -Filter "cyborgFemaleA.png" | Select-Object -First 1
            if ($skin) {
                New-Item -ItemType Directory -Force -Path "$out\Textures" | Out-Null
                Copy-Item $skin.FullName -Destination "$out\Textures\colormap.png" -Force
            }
        }
        Get-ChildItem $exDir -Recurse -Include "License*.txt" | Select-Object -First 1 | Copy-Item -Destination "$out\License.txt" -Force

        "{0}: FBX {1}개, 텍스처 {2}개, zip {3:N1}MB" -f $kit, (Get-ChildItem $out -Filter *.fbx).Count, (Get-ChildItem $out -Recurse -Filter *.png).Count, ((Get-Item $zipFile).Length/1MB)
    } catch {
        Write-Warning "$kit 실패: $($_.Exception.Message)"
    } finally {
        if (Test-Path $exDir) { Remove-Item $exDir -Recurse -Force }
    }
}
