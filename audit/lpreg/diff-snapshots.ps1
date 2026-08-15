# Line-level diff of failing snapshot SVGs: what moved, by how much.
$dir = 'C:\MyProj\LilySharp\artifacts\visual-diff'
foreach ($name in 'test__articulations','test__articulations-lower-staff','test__ornaments','test__figbass-below-script','test__scripts-dynamics') {
  $base = Get-Content "$dir\$name.baseline.svg"
  $act  = Get-Content "$dir\$name.actual.svg"
  "== $name (base $($base.Count) lines / act $($act.Count)) =="
  $n = [Math]::Min($base.Count, $act.Count)
  $shown = 0
  for ($i = 0; $i -lt $n; $i++) {
    if ($base[$i] -ne $act[$i]) {
      $b = $base[$i].Trim(); $a = $act[$i].Trim()
      if ($b.Length -gt 130) { $b = $b.Substring(0,130) }
      if ($a.Length -gt 130) { $a = $a.Substring(0,130) }
      "  L$($i+1) - $b"
      "  L$($i+1) + $a"
      $shown++
      if ($shown -ge 8) { "  ...(more)"; break }
    }
  }
  if ($base.Count -ne $act.Count) { "  (line count differs)" }
}
