Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens | Sort-Object { $_.Bounds.X }
for ($i = 0; $i -lt $screens.Count; $i++) {
    $s = $screens[$i]
    $bmp = New-Object System.Drawing.Bitmap($s.Bounds.Width, $s.Bounds.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($s.Bounds.Location, [System.Drawing.Point]::Empty, $s.Bounds.Size)
    $bmp.Save("ps_mon_$i.jpg", [System.Drawing.Imaging.ImageFormat]::Jpeg)
    $g.Dispose()
    $bmp.Dispose()
    Write-Output "Saved ps_mon_$i.jpg: $($s.DeviceName) $($s.Bounds)"
}
