# tabdot: LP SVG の全要素を (kind, x, y, src, detail) で一覧化する。
# <a xlink:href="textedit:...:line:col:col"> 内のグロブは src=line:col を付ける。
# 用法: .\tabdot-dump.ps1 <svgPath>
param([string]$Path)
$xml = [xml](Get-Content $Path -Raw)
$out = New-Object System.Collections.Generic.List[object]

function Add-Elems([System.Xml.XmlNode]$node, [string]$src) {
    foreach ($child in $node.ChildNodes) {
        switch ($child.LocalName) {
            'a' {
                $href = $child.Attributes['xlink:href'].Value
                $s = if ($href -match ':(\d+):(\d+):\d+$') { "$($Matches[1]):$($Matches[2])" } else { '?' }
                Add-Elems $child $s
            }
            'g' {
                if ($child.transform -match 'translate\(([-0-9.]+),\s*([-0-9.]+)\)') {
                    $x = [double]$Matches[1]; $y = [double]$Matches[2]
                    foreach ($gc in $child.ChildNodes) {
                        switch ($gc.LocalName) {
                            'text' { $out.Add([pscustomobject]@{ kind='text'; x=$x; y=$y; src=$src; detail=($gc.InnerText).Trim() }) }
                            'path' { $d = $gc.GetAttribute('d'); $out.Add([pscustomobject]@{ kind='path'; x=$x; y=$y; src=$src; detail=$d.Substring(0,[Math]::Min(24,$d.Length)) }) }
                            'line' { $out.Add([pscustomobject]@{ kind='line'; x=$x; y=$y; src=$src; detail="$($gc.GetAttribute('x1')),$($gc.GetAttribute('y1'))->$($gc.GetAttribute('x2')),$($gc.GetAttribute('y2'))" }) }
                            'rect' { $out.Add([pscustomobject]@{ kind='rect'; x=$x; y=$y; src=$src; detail="x$($gc.GetAttribute('x')) y$($gc.GetAttribute('y')) w$($gc.GetAttribute('width')) h$($gc.GetAttribute('height'))" }) }
                            'polygon' { $out.Add([pscustomobject]@{ kind='poly'; x=$x; y=$y; src=$src; detail=$gc.GetAttribute('points') }) }
                        }
                    }
                } else {
                    Add-Elems $child $src   # 色ラッパ <g color=...> など
                }
            }
        }
    }
}
Add-Elems $xml.svg ''
$out | Sort-Object y, x | Format-Table -AutoSize | Out-String -Width 200
