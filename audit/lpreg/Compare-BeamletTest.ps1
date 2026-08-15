# beamlet-test: dump polygon bboxes (staff-space units) per file, walking points PAIRWISE
# (LP uses "x y x y" space-separated — never split on comma; README trap #1).
param(
  [string]$LysSvg = 'C:\MyProj\LilySharp\audit\lpreg\beamlet-test.lys.svg',
  [string]$LpSvg  = 'C:\MyProj\LilySharp\audit\lpreg\beamlet-test.svg'
)

function Get-PolyBoxes([string]$path) {
  $raw = Get-Content $path -Raw
  # Polygons may sit inside <g transform="translate(x,y)"> wrappers (Lily#) or carry
  # transform attributes (LP). Capture the nearest enclosing translate, if any.
  $boxes = @()
  foreach ($m in [regex]::Matches($raw, '<polygon[^>]*points="([^"]+)"[^>]*/?>')) {
    $pts = ($m.Groups[1].Value -replace ',', ' ') -split '\s+' | Where-Object { $_ -ne '' }
    $xs = @(); $ys = @()
    for ($i = 0; $i + 1 -lt $pts.Count; $i += 2) {
      $xs += [double]$pts[$i]; $ys += [double]$pts[$i+1]
    }
    # nearest preceding translate() before this polygon
    $head = $raw.Substring(0, $m.Index)
    $tm = [regex]::Matches($head, 'translate\(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)')
    $tx = 0.0; $ty = 0.0
    if ($tm.Count -gt 0) { $tx = [double]$tm[$tm.Count-1].Groups[1].Value; $ty = [double]$tm[$tm.Count-1].Groups[2].Value }
    $boxes += [pscustomobject]@{
      MinX = ($xs | Measure-Object -Minimum).Minimum + $tx
      MaxX = ($xs | Measure-Object -Maximum).Maximum + $tx
      Y    = (($ys | Measure-Object -Average).Average) + $ty
    }
  }
  $boxes | ForEach-Object { $_ | Add-Member -PassThru NoteProperty Width ([math]::Round($_.MaxX - $_.MinX, 3)) }
}

foreach ($f in @($LysSvg, $LpSvg)) {
  Write-Host "=== $f ==="
  Get-PolyBoxes $f | Sort-Object Y, MinX | ForEach-Object {
    '{0,8:F3} {1,8:F3}  w={2,7:F3}  y={3,8:F3}' -f $_.MinX, $_.MaxX, $_.Width, $_.Y
  }
}
