param()
Add-Type -AssemblyName System.Drawing
$payloads = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size,$size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform(($size/256.0),($size/256.0))
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush([System.Drawing.Point]::new(24,24),[System.Drawing.Point]::new(232,232),[System.Drawing.Color]::FromArgb(100,80,230),[System.Drawing.Color]::FromArgb(14,170,198))
    $g.FillEllipse($background,8,8,240,240)
    $mic = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mic.AddArc(100,49,56,56,180,180)
    $mic.AddArc(100,91,56,56,0,180)
    $mic.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White,$mic)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White,12)
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen,78,75,100,104,0,180)
    $g.DrawLine($pen,128,179,128,204)
    $g.DrawLine($pen,107,204,149,204)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $payloads += ,@{Size=$size;Bytes=$stream.ToArray()}
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'dictee.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose();$pen.Dispose();$mic.Dispose();$background.Dispose();$g.Dispose();$bitmap.Dispose()
}
$writer = New-Object System.IO.BinaryWriter([System.IO.File]::Create((Join-Path $PSScriptRoot 'dictee.ico')))
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$payloads.Count)
$offset = 6+16*$payloads.Count
foreach ($payload in $payloads) {
    $dimension = if($payload.Size -eq 256){0}else{$payload.Size}
    $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0)
    $writer.Write([uint16]1);$writer.Write([uint16]32)
    $writer.Write([uint32]$payload.Bytes.Length);$writer.Write([uint32]$offset)
    $offset += $payload.Bytes.Length
}
foreach ($payload in $payloads) {$writer.Write([byte[]]$payload.Bytes)}
$writer.Dispose()
