# Session 129, second delivery: part-combine-relative.
function Set-RegStatus([string]$name, [string]$state, [string]$claim, [string]$notes) {
  $p = 'C:\MyProj\LilySharp\audit\lp-regression\status.json'
  $j = Get-Content $p -Raw | ConvertFrom-Json
  $e = $j.files.$name; if ($null -eq $e) { throw "no entry $name" }
  $e | Add-Member -Force NoteProperty state $state
  if ($claim) { $e | Add-Member -Force NoteProperty claim $claim }
  if ($notes) { $e | Add-Member -Force NoteProperty notes $notes }
  $j | ConvertTo-Json -Depth 4 | Set-Content $p -Encoding utf8
}

Set-RegStatus 'part-combine-relative.ly' 'exact' `
  'The pitches in \partCombine are unaffected by an outer \relative; the expected output is three identical measures' @'
exact(第129第2便・コード変更0・第112 skip→綴りゲート解除で再測)。双子=scratch/lpreg/pcrel.ly(逐語3小節)
＋pcrel-ctl.ly(第2小節を抜いた2小節=双子と枠を揃えるため。spacing は score 全体の性質なので
2小節の頁と3小節の頁は列で比べられない)対 pcrel-probe.lys。
LP 実測: **3小節とも完全同一**(pos -4/-6 → -3/-5 = E4/C4 → F4/D4・列 8.585/12.860 で以降 9.891 ごと・
CombineTextScript レコードは 1 つも無い=両パートは apart で a2/Solo は出ない)。
Lily# も **-3.0/-2.0 → -2.5/-1.5 ss**・列 8.59/12.86/18.48/22.75 ＝ **LP 全一致**・ラベル 0。
⚠️⚠️ **枠外=本の第2小節**。LP の `\relative` は音楽を**囲む**ので内と外が在り、
本の主張は「合体器は*外*を無視する」。**Lily# の `octave` は流れの切替**(部は既定で relative・
`octave absolute` が opt-out)なので、**無視されるべき「外」が存在しない**。
⇒ 書けるのは残り半分=「絶対綴りと相対綴りが同じ小節を印字する」で、それは**読者が頁で見るもの**そのもの。
(枠外を含んで exact とするのは whole-note-tremolo-accidentals と同じ扱い)。
⚠️ **陽性対照が要る本**: `e f` は**両モードで同じ2音**(だから本の3小節が同一になる)ので、
`octave relative` が**何もしない実装でも双子は通る**。対照 pcrel-ctl-probe.lys=
**D4 の後の相対 `a` は A3(-4.0ss)・絶対 `a` は A4(-0.5ss)** ⇒ 実測 -4.00 で切替は効いている。
★ **対照は 2 回外した**(`b` after F4・`c` after F4 はどちらも両モード一致)——
**規則は「4度以内の step では両モードが一致する」**。probe のコメントに明記済。
観測者 PartCombineRelativeTests 3本(主張・**陽性対照**・**ラベルゼロ=apart の枠検分**)。
snapshot 0・LP 側 warning 0。
'@

$f = (Get-Content 'C:\MyProj\LilySharp\audit\lp-regression\status.json' -Raw | ConvertFrom-Json).files
$p = @($f.PSObject.Properties | Where-Object { $_.Value.category -eq 'plain' })
"plain $(@($p).Count)"
$p | Group-Object { $_.Value.state } | Sort-Object Name | ForEach-Object { "{0,-8} {1}" -f $_.Name, $_.Count }
