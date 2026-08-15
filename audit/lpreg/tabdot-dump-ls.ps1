# tabdot: Lily# SVG の text/rect/line を一覧化（フッタ等の大きい y は除外可）。
# 用法: .\tabdot-dump-ls.ps1 <svgPath> [maxY]
param([string]$Path, [double]$MaxY = 40)
$xml = [xml](Get-Content $Path -Raw)
$out = New-Object System.Collections.Generic.List[object]
function Walk([System.Xml.XmlNode]$n) {
    foreach ($c in $n.ChildNodes) {
        switch ($c.LocalName) {
            'text' {
                $y = [double]$c.GetAttribute('y')
                if ($y -le $MaxY) {
                    $t = $c.InnerText
                    $cp = if ($t.Length -gt 0) { 'U+{0:X4}' -f [int]$t[0] } else { '' }
                    $out.Add([pscustomobject]@{ kind='text'; x=[double]$c.GetAttribute('x'); y=$y; detail="$t ($cp) fs$($c.GetAttribute('font-size')) anch=$($c.GetAttribute('text-anchor')) pos=$($c.GetAttribute('data-pos'))" })
                }
            }
            'rect' {
                $y = [double]$c.GetAttribute('y')
                if ($y -le $MaxY) { $out.Add([pscustomobject]@{ kind='rect'; x=[double]$c.GetAttribute('x'); y=$y; detail="w$($c.GetAttribute('width')) h$($c.GetAttribute('height')) pos=$($c.GetAttribute('data-pos'))" }) }
            }
            'line' {
                $y = [double]$c.GetAttribute('y1')
                if ($y -le $MaxY) { $out.Add([pscustomobject]@{ kind='line'; x=[double]$c.GetAttribute('x1'); y=$y; detail="->$($c.GetAttribute('x2')),$($c.GetAttribute('y2')) w$($c.GetAttribute('stroke-width'))" }) }
            }
            default { Walk $c }
        }
    }
}
Walk $xml.DocumentElement
$out | Sort-Object y, x | Format-Table -AutoSize | Out-String -Width 220
