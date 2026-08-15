# scripttie-ls.svg / scripttie-lp.svg から script glyph の Y を抽出（staff 相対 Y-down）
param([string]$Path = 'audit\lpreg\scripttie-ls.svg')

$svg = Get-Content $Path -Raw
# system groups: <g transform="translate(tx,ty) ...>...</g> (top-level)
$groups = [regex]::Matches($svg, '<g transform="translate\(([\d.\-]+),([\d.\-]+)\)[^"]*"[^>]*>')
"groups: $($groups.Count)"
foreach ($gm in $groups) {
    "  translate($($gm.Groups[1].Value),$($gm.Groups[2].Value)) at offset $($gm.Index)"
}
# 各 text 要素: 属する group の translate を足して絶対座標にする
$texts = [regex]::Matches($svg, '<text class="music" x="([\d.\-]+)" y="([\d.\-]+)"[^>]*?(?:data-pos="(\d+)")?[^>]*>(?:<tspan[^>]*>)?(&#x[0-9A-Fa-f]+;|[^<])')
"texts: $($texts.Count)"
foreach ($t in $texts) {
    $tx = 0.0; $ty = 0.0
    foreach ($gm in $groups) {
        if ($gm.Index -lt $t.Index) { $tx = [double]$gm.Groups[1].Value; $ty = [double]$gm.Groups[2].Value }
    }
    $raw = $t.Groups[4].Value
    if ($raw -match '&#x([0-9A-Fa-f]+);') { $cp = [Convert]::ToInt32($Matches[1],16) } else { $cp = [int][char]$raw[0] }
    $ax = [math]::Round($tx + [double]$t.Groups[1].Value, 2)
    $ay = [math]::Round($ty + [double]$t.Groups[2].Value, 2)
    "U+{0:X4} x={1} y={2} pos={3}" -f $cp, $ax, $ay, $t.Groups[3].Value
}
