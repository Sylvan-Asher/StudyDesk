$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'StudyDesk.cs'
$out = Join-Path $PSScriptRoot '..\StudyDesk.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
$core = Join-Path $wpf 'PresentationCore.dll'
$framework = Join-Path $wpf 'PresentationFramework.dll'
$windowsBase = Join-Path $wpf 'WindowsBase.dll'
& $compiler /nologo /target:winexe /optimize+ /out:$out "/reference:$core" "/reference:$framework" "/reference:$windowsBase" /reference:System.Xaml.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output (Resolve-Path $out)
