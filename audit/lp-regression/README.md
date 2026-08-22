# LP regression corpus → Lily# test cases（第99セッション第5便 開始）

`C:\MyProj\lilypond-src\input\regression`（2097 本）を端から `.lys` に書き直し、
LP と同じレイアウトが得られない本を **LP の字面移植で** 直すワークストリーム。

## 選別規則（status.json の category・機械生成）

- **scheme** — Scheme スクリプト（`#(`, `#'`, `#{`, `#name` …）を含む本は**利用不可**
  （ユーザー指示 2026-08-05）。1631 本。
- **markup** — `\markup` 主体（Lily# に対応物なし）。55 本。
- **override** — `\override`/`\set`/`\with` を含む。原則対象外だが、Lily# が同じ
  override を文法で持つ場合は個別判断で拾ってよい。89 本。
- **plain** — 上のどれにも当たらない 322 本。**これが作業キュー**（アルファベット順）。

plain でも文法ギャップ（例: 強制臨時記号 `f'!` — Lily# では LYS4009・`!` は点線小節線）
で書けない本は state=skip・reason に記録して先へ進む。

## 1 本の処理手順

1. `.ly` を読む。**texidoc の主張**（この本が回帰させたい性質）を控える。
2. `.lys` に翻訳して `lys/<name>.lys` に置く。⚠️ **octave absolute の綴りは LP の
   1 アポストロフィ下**（Lily# `c'` = LP `c''`。LpGeometryProbes.cs FSF8 の remarks 参照）。
3. 両方レンダリング（lysc svg／lilypond --svg。LP は `cmd /d /s /c "… < NUL"` で起動）。
   **コマンドの正確な形と別マシンでの復元は `../lpreg/REGENERATE.md`**。
4. **texidoc の主張と gap 構造**を SVG 座標（両者とも staff space 単位）で比較。
   ピクセル比較はしない（visual-diff は 1x ラスタ）。
5. 一致 → state=exact。乖離 → **LP の該当ロジックを字面移植**して修正
   （実測値のフィットは禁止・feedback_lilysharp_mimic_lp_source）。修正したら
   fixture / 台帳点 / 単体テストの観測者を残し、state=fixed。
   移植が 1 セッションに収まらない場合 state=open で乖離の実測値を記録。
6. status.json を更新（visited が途切れないこと＝frontier はアルファベット順で
   最初の未訪問 plain）。

## status.json

```
{ "files": { "<name>.ly": {
    "category": "scheme|markup|override|plain",
    "state":    "pending|skip|exact|residual|fixed|open",   (plain のみ更新)
    "claim":    "<texidoc の要約>",
    "notes":    "<測った数字・skip 理由など>"
} } }
```

status 更新ヘルパー（揮発・毎セッション貼り直す）:
```powershell
function global:Set-RegStatus([string]$name, [string]$state, [string]$claim, [string]$notes) {
  $p = 'C:\MyProj\LilySharp\audit\lp-regression\status.json'
  $j = Get-Content $p -Raw | ConvertFrom-Json
  $e = $j.files.$name; if ($null -eq $e) { throw "no entry $name" }
  $e | Add-Member -Force NoteProperty state $state
  if ($claim) { $e | Add-Member -Force NoteProperty claim $claim }
  if ($notes) { $e | Add-Member -Force NoteProperty notes $notes }
  $j | ConvertTo-Json -Depth 4 | Set-Content $p -Encoding utf8
}
```

比較の小技: beam の組み方は両エンジンとも SVG の `<polygon>` なので、
`[regex]::Matches($svg,'<polygon').Count` の一致が「同じ組み方・同じ細分」の強い指紋になる。

比較器の罠（踏んだ順・全部実話）:
- **LP polygon の points は `x y x y` の空白区切り**（`x,y` ペアではない）。`,` で split
  すると Y 値が X の極値に混入して幅が全部嘘になる。座標は必ずペアで歩く。
  ついでに LP は stroke-width 0.08 の stroked polygon（ink = 幾何 ±0.04）、Lily# は
  塗り polygon（ink = 幾何そのまま）——0.04〜0.08 の系統差はこれ。
- **段の出力順はエンジン間で不安定**。quant offset の列比較は (system, x) でグループ化
  して**集合**比較（beam-quanting-32nd で 46/78 の偽乖離が消えた）。
- **Lily# は A4 紙幅（line-width ≈102ss・LayoutOptions 既定）に収まらない行を圧縮 justify
  する**。`paper { }` の文法は第232（2026-08-23）で入ったが、**exporter が双子に
  `\paper` を書かない**（warning で名指す）ので、比較の枠は引き続き**小節ごとに切った
  .lys / .ly の対**で揃える（自然幅どうしになる）。LP 側は paper-width/line-width を
  広げれば 1 段に伸ばせる（line-width だけだと紙幅でクランプされる）。
- **spacing は score 全体の common shortest に依存**（LP spacing-spanner は score 単位）。
  小節を切り出すと最短音価が変わって列間隔ごと変わる——切り出しの対には
  最短音価が同じ小節を選ぶ（bar2 だけ切ると 1/64 が消えて 12.4ss vs 17.2ss を読む）。
- **treble_8 の部は LP と同じ綴りが正解**。Lily# の treble_8 部は「記譜=実音+1oct」の
  移調を持ち、いつもの「Lily# c = LP c'」のオクターブ差をちょうど相殺する
  （automatic-polyphony-tabstaff で発見。譜面位置・実音・タブのフレットが全部揃う）。

数の数え方（引き継ぎ用）:
```powershell
$s = (Get-Content audit\lp-regression\status.json -Raw | ConvertFrom-Json).files.PSObject.Properties
"plain $(@($s | ? { $_.Value.category -eq 'plain' }).Count) / 処理済 $(@($s | ? { $_.Value.state -notin 'pending' -and $_.Value.category -eq 'plain' }).Count)"
```
