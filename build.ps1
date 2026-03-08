$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = "$PSScriptRoot\src\HandTeller"
$out = "$PSScriptRoot\HandTeller.exe"

& $csc /out:$out /target:winexe /unsafe+ /platform:x64 `
    /r:System.Windows.Forms.dll `
    /r:System.Drawing.dll `
    "$src\MemoryReader.cs" `
    "$src\AnchorNav.cs" `
    "$src\HandEvaluator.cs" `
    "$src\Overlay.cs" `
    "$src\Program.cs"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded: $out"
} else {
    Write-Host "Build FAILED."
}
