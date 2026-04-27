# Visual Regression Corpus

## ファイル
10 個の最小 .ly テストファイル (1〜2 段、4〜12 小節)。各カテゴリで LP の load-bearing 機能をピンポイントに測る。

| # | ファイル | カテゴリ | 着目点 |
|---|---|---|---|
| 01 | basic_spacing.ly | 水平スペーシング | 全音符→16分音符スペース比 (Gourlay) |
| 02 | accidentals.ly | 臨時記号 | 和音内の sharp/flat 衝突、courtesy / cautionary |
| 03 | beams.ly | Beam quanting | 単純8分、16分 + 6連符 + 4/6 不規則 |
| 04 | slurs_ties.ly | 曲線 | nested slur, tie + slur 共存 |
| 05 | lyrics.ly | 歌詞 | 通常歌詞 + extender (`__`) |
| 06 | dynamics_hairpins.ly | ダイナミクス | 連続 hairpin + dynamic + cresc |
| 07 | multi_voice.ly | 多声部 | voiceOne/voiceTwo 同一 staff |
| 08 | grand_staff.ly | 多段譜 | PianoStaff (treble + bass) |
| 09 | articulation_stack.ly | 装飾記号 | スクリプト・スタック・mark・markup |
| 10 | line_break.ly | 改行 | 12 小節を狭幅 paper-width で強制 wrap |

## 実行

**重要**: `lilypond.exe` の起動は本機の company policy でブロックされている (Claude の sandbox / antivirus いずれか)。Claude が直接 LP を呼ぶことはできないので、**ユーザー手動 (or admin 権限) で実行**する。

### Step 1: LP 出力生成
```powershell
.\audit\scripts\Run-LilyPond.ps1
```
- 各 .ly を `lilypond.exe -dbackend=svg --output=audit\corpus\out_lp\<base> -s <file>` で SVG 化
- 期待結果: `audit\corpus\out_lp\01_basic_spacing.svg` 等 10 ファイル

### Step 2: LilySharp 出力生成
- 各 .ly に対応する `.lys` を `audit\corpus\` に作成 (Phase 4 の着手と並行)
- LilySharp.Cli で SVG 化:
```powershell
foreach ($f in Get-ChildItem audit\corpus\*.lys) {
  .\bin\LilySharp.Cli.exe render $f.FullName -o audit\corpus\out_lily\$($f.BaseName).svg
}
```

### Step 3: 数値差分
```powershell
.\audit\scripts\Compare-Svg.ps1
```
- 出力: `audit\visual_regression_baseline.csv`
- 列: File / Status / Count / MeanDx / MaxDx / P95Dx

## ベースライン更新
Sprint ごとに Step 1〜3 を再実行し、結果を git にコミット (`audit/visual_regression_<sprint>.csv`)。
worst-10 が改善し続け、悪化が起きないことを継続確認。

## バージョン情報
- LilyPond binary: 2.24.4 (stable, `C:\bin\lilypond-2.24.4\bin\`)
- LilyPond source (LILYPOND-REF 行番号基準): 2.25.35 (devel, `C:\MyProj\lilypond-src`)
- LilySharp: 現状 (Phase 4 開始時点) のリポジトリ HEAD
