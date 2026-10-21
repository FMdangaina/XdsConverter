# Builds XdsConverter.exe (.NET Framework 4.x, already present on Windows 10/11).
# texconv.exe is embedded in the exe: you only need to hand over the exe.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path 'app.ico')) {
    $sizes = 256,48,32,16
    $pngs = foreach ($s in $sizes) {
        $b = New-Object System.Drawing.Bitmap $s,$s
        $g = [System.Drawing.Graphics]::FromImage($b)
        $g.SmoothingMode = 'AntiAlias'
        $g.Clear([System.Drawing.Color]::Transparent)
        $p = [int]($s*0.06)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $r = [int]($s*0.18); $x=$p; $y=$p; $w=$s-2*$p; $h=$s-2*$p
        $path.AddArc($x,$y,$r,$r,180,90); $path.AddArc($x+$w-$r,$y,$r,$r,270,90)
        $path.AddArc($x+$w-$r,$y+$h-$r,$r,$r,0,90); $path.AddArc($x,$y+$h-$r,$r,$r,90,90); $path.CloseFigure()
        $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0,0)),([System.Drawing.Point]::new($s,$s)),([System.Drawing.Color]::FromArgb(255,30,150,200)),([System.Drawing.Color]::FromArgb(255,20,60,140))
        $g.FillPath($br,$path)
        # checkerboard (transparency) in the lower half + a light "sun"
        $cell = [Math]::Max(2,[int]($s/8))
        $light = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235,255,255,255))
        $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120,255,255,255))
        for ($cy = 0; $cy -lt 4; $cy++) { for ($cx = 0; $cx -lt 6; $cx++) {
            $br2 = if (($cx + $cy) % 2 -eq 0) { $light } else { $dark }
            $g.FillRectangle($br2, $s*0.17 + $cx*$cell*0.94, $s*0.50 + $cy*$cell*0.94, $cell*0.94, $cell*0.94)
        } }
        $g.FillEllipse($light, $s*0.60, $s*0.14, $s*0.22, $s*0.22)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)
        ,@($s,$ms.ToArray())
    }
    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $out
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
    $offset = 6 + 16*$pngs.Count
    foreach ($e in $pngs) {
        $s = $e[0]; $d = $e[1]
        $bw.Write([byte]($(if ($s -ge 256) {0} else {$s}))); $bw.Write([byte]($(if ($s -ge 256) {0} else {$s})))
        $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$d.Length); $bw.Write([uint32]$offset); $offset += $d.Length
    }
    foreach ($e in $pngs) { $bw.Write($e[1]) }
    [System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
}

$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /out:XdsConverter.exe /win32icon:app.ico `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    /resource:texconv.exe,texconv.exe `
    Dxt.cs XenosTiling.cs XdsTexture.cs XdsTexture.Layout.cs XdsTexture.Encode.cs XdsTexture.Create.cs App.cs
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "OK -> $PSScriptRoot\XdsConverter.exe"
