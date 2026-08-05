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

数の数え方（引き継ぎ用）:
```powershell
$s = (Get-Content audit\lp-regression\status.json -Raw | ConvertFrom-Json).files.PSObject.Properties
"plain $(@($s | ? { $_.Value.category -eq 'plain' }).Count) / 処理済 $(@($s | ? { $_.Value.state -notin 'pending' -and $_.Value.category -eq 'plain' }).Count)"
```
