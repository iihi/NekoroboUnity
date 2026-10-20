# EOS（Epic Online Services）の C# SDK をこのプロジェクトに入れる。
#
# SDK は Epic の物なので GitHub（公開）には上げていない。クローンしたら1度これを流す。
# 流さなくてもプロジェクトは開けて遊べる（EOS の「つなぎ方」が出ないだけ。部屋サーバーは使える）。
#
#   使い方（PowerShell で、プロジェクトの直下から）:
#     powershell -ExecutionPolicy Bypass -File Tools\eos_setup.ps1
#     powershell -ExecutionPolicy Bypass -File Tools\eos_setup.ps1 -Zip D:\EOS-SDK-CSharp-xxxx.zip
#   -Zip を省くと、ダウンロードのフォルダの一番新しい EOS-SDK-CSharp-*.zip を使う。
#   SDK は Epic Developer Portal（dev.epicgames.com）の「SDK」から C# 版を落とす。
#
# することは3つ:
#   1. zip から SDK/Source（C# のソース）と SDK/Bin/EOSSDK-Win64-Shipping.dll だけを Assets/Plugins/EOS へ写す
#   2. DLL の設定（.meta）を書く。エディタでは読まない（エディタは再生のたびに自分で読んで外す。EosHost.cs）、EXE（Win64）だけ
#   3. Assets/csc.rsp に -define:NEKOROBO_EOS を書く（EOS のコード NetEos.cs / EosHost.cs はこれがあるときだけ働く）
#
# 鍵（eos.json）は別。プロジェクトの直下に置く（README の「EOS」を参照）。

param([string]$Zip = "")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ($Zip -eq "") {
    $dl = Join-Path $env:USERPROFILE "Downloads"
    $cand = Get-ChildItem -Path $dl -Filter "EOS-SDK-CSharp-*.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $cand) { throw "EOS-SDK-CSharp-*.zip がダウンロードのフォルダにありません。-Zip で場所を入れてください。" }
    $Zip = $cand.FullName
}
Write-Host "SDK: $Zip"

$dst = Join-Path $root "Assets\Plugins\EOS"
if (Test-Path (Join-Path $dst "Source")) { Remove-Item -Recurse -Force (Join-Path $dst "Source") }
New-Item -ItemType Directory -Force (Join-Path $dst "Bin") | Out-Null

# 1. 要る物だけ取り出す（zip 全体は 500MB を超えるので、全部は広げない）
$z = [System.IO.Compression.ZipFile]::OpenRead($Zip)
$n = 0
try {
    foreach ($e in $z.Entries) {
        $name = $e.FullName
        $out = $null
        if ($name.StartsWith("SDK/Source/") -and $name.EndsWith(".cs")) { $out = Join-Path $dst ($name.Substring(4) -replace "/", "\") }
        elseif ($name -eq "SDK/Bin/EOSSDK-Win64-Shipping.dll") { $out = Join-Path $dst "Bin\EOSSDK-Win64-Shipping.dll" }
        if ($null -eq $out) { continue }
        New-Item -ItemType Directory -Force (Split-Path -Parent $out) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $out, $true)
        $n++
    }
} finally { $z.Dispose() }
if ($n -lt 100) { throw "SDK の中身が見つかりません（$n 個）。C# 版の SDK の zip か確かめてください。" }
Write-Host "写した: $n ファイル → Assets\Plugins\EOS"

# 2. DLL の設定。エディタは外し、Windows 64bit の EXE だけ
$meta = Join-Path $dst "Bin\EOSSDK-Win64-Shipping.dll.meta"
if (-not (Test-Path $meta)) {
    $guid = [guid]::NewGuid().ToString("N")
    $text = @"
fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 0
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
    Win:
      enabled: 0
      settings:
        CPU: None
    Win64:
      enabled: 1
      settings:
        CPU: x86_64
    WindowsStoreApps:
      enabled: 0
      settings:
        CPU: x64
  userData:
  assetBundleName:
  assetBundleVariant:
"@
    [System.IO.File]::WriteAllText($meta, $text.Replace("`r`n", "`n"))
}

# 3. EOS のコードを働かせる印
[System.IO.File]::WriteAllText((Join-Path $root "Assets\csc.rsp"), "-define:NEKOROBO_EOS`n")
Write-Host "Assets\csc.rsp に NEKOROBO_EOS を書きました。"

if (-not (Test-Path (Join-Path $root "eos.json"))) {
    Write-Host "注意: 鍵（eos.json）がまだありません。プロジェクトの直下に置いてください（README の「EOS」）。"
}
Write-Host "できました。Unity に戻ると読み込み直してコンパイルします。"
