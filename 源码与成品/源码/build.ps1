# ============================================================================
# EXPAS build script
#   usage:  powershell -ExecutionPolicy Bypass -File build.ps1
#   output: ..\成品\EXPAS.NP.dll   (copy into LabAPI plugins\global\)
#
#   ！！本文件必须保存为 UTF-8 **带 BOM**！！
#   PowerShell 5.1 读取无 BOM 的 .ps1 时按系统 ANSI(GBK) 解码,
#   文件里的中文(含下面的目录名字面量)会变成乱码, 进而建出乱码目录。
#
#   Plugin full name : EXPAS  (Experience, eXtended Plugins, Audio, and Status)
#   Official dll name: EXPAS.NP.dll
#
#   Embeds native audio codec DLLs found in .\native\ as resources named
#   "native.<file>.bin".  They are released at runtime into
#   configs\<port>\EXPAS\bin\native\ .  No .exe is embedded.
#   (".bin" suffix is required - LabAPI treats resources ending in ".dll"
#    as managed assemblies and would throw BadImageFormatException.)
# ============================================================================
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }

$srcs = @(Get-ChildItem -LiteralPath $root -Filter '*.cs' -File | Sort-Object Name)
# 输出到 源码与成品\成品\ (本脚本在 源码与成品\源码\ 下)
$outDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($root, '..', '成品'))
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$out  = [System.IO.Path]::Combine($outDir, 'EXPAS.NP.dll')

if ($srcs.Count -eq 0) { throw "no *.cs source found in $root" }
Write-Host ("srcs: " + (($srcs | ForEach-Object { $_.Name }) -join ', '))

# ---- 1. locate Roslyn csc.dll (any installed .NET SDK) ----
$csc = @(Get-ChildItem "$env:ProgramFiles\dotnet\sdk\*\Roslyn\bincore\csc.dll" -ErrorAction SilentlyContinue)
if ($csc.Count -eq 0) { throw "csc.dll not found, install a .NET SDK first" }
$cscPath = $csc[0].FullName
Write-Host "csc : $cscPath"

# ---- 2. locate the game Managed folder (reference assemblies) ----
$managedCandidates = @(
  'D:\SteamLibrary\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed',
  'C:\Program Files (x86)\Steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed',
  'C:\SteamLibrary\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed'
)
$managed = $null
foreach ($p in $managedCandidates) {
  $probe = [System.IO.Path]::Combine($p, 'LabApi.dll')
  if (Test-Path -LiteralPath $probe) { $managed = $p; break }
}
if (-not $managed) { throw "Managed folder with LabApi.dll not found - edit managedCandidates in this script" }
Write-Host "refs: $managed"

$refs = @(
  'mscorlib','netstandard','System','System.Core',
  'Assembly-CSharp','LabApi','UnityEngine.CoreModule','UnityEngine.PhysicsModule',
  'CommandSystem.Core','Mirror','Assembly-CSharp-firstpass','NorthwoodLib','Pooling','Unity.TextMeshPro'
)

# ---- 3. compile ----
$cscArgs = @(
  $cscPath,'-noconfig','-nostdlib+','-target:library','-warn:4',
  '-nowarn:CS0169,CS0219,CS0414',
  "-out:$out","-lib:$managed"
)
foreach ($r in $refs) { $cscArgs += "-r:$r.dll" }
foreach ($s in $srcs) { $cscArgs += $s.FullName }

# ---- 4. embed native decoder DLLs from .\native\ ----
#   Resource names MUST NOT end with ".dll" / ".dll.compressed":
#   LabAPI's AssemblyUtils.ResolveEmbeddedResources() (called for every plugin)
#   treats any resource ending in ".dll" as a managed assembly and calls
#   AssemblyName.GetAssemblyName() on it -> BadImageFormatException for native
#   binaries.  We therefore suffix them with ".bin"; ExpasNative.cs strips it.
$nativeDir = [System.IO.Path]::Combine($root, 'native')
$nativeDlls = @()
if (Test-Path -LiteralPath $nativeDir) {
  $nativeDlls = @(Get-ChildItem -LiteralPath $nativeDir -Filter '*.dll' -File | Sort-Object Name)
}
if ($nativeDlls.Count -gt 0) {
  $sum = 0
  foreach ($dll in $nativeDlls) {
    $cscArgs += "-resource:$($dll.FullName),native.$($dll.Name).bin"
    $sum += $dll.Length
    Write-Host ("embed : native.{0}.bin ({1} bytes)" -f $dll.Name, $dll.Length)
  }
  Write-Host ("embed : native DLL total {0} bytes ({1} files)" -f $sum, $nativeDlls.Count)
} else {
  Write-Host "embed : native\*.dll not found - build without native codec (only WAV + external ffmpeg.exe)"
}

& dotnet @cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed (exit $LASTEXITCODE)" }

$dll = Get-Item -LiteralPath $out
Write-Host ""
Write-Host ("BUILD OK -> {0} ({1} bytes)" -f $dll.FullName, $dll.Length)
Write-Host "next: copy EXPAS.NP.dll into  %APPDATA%\SCP Secret Laboratory\LabAPI\plugins\global\  and restart the server"
