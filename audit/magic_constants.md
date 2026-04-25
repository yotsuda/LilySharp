# Magic Constant Hunt (Phase 1-2)

**生成日**: 2026-04-25
**対象**: 47 ファイル (`Svg/Layout/*`, `Svg/EngravingDefaults.cs`, `Svg/EngravingRules.cs`, `Svg/PaperSettings.cs`, `Svg/SpacingSettings.cs`)
**抽出スクリプト**: `audit/scripts/Find-MagicConstants.ps1`
**生データ**: `audit/magic_constants.csv` (662行)

---

## サマリー

| 判定 | 件数 | 意味 |
|---|---:|---|
| Green | 272 | ±5行以内に `LILYPOND-REF` あり (個別追跡可) |
| Yellow | 259 | ファイル先頭に `LILYPOND-REF` (一括説明) または approximation 注記 |
| Red | 131 | LILYPOND-REF 一切なし、approximation 注記もなし |

---

## Red 件数 by ファイル

| 件数 | ファイル | コメント |
|---:|---|---|
| 37 | `Svg/PaperSettings.cs` | **誤検知中心**: ISO 紙サイズ (A4=210x297) と単位変換 (1 inch=25.4mm) は物理定数。LP 引用不要。 |
| 31 | `Svg/Layout/GlyphMetrics.cs` | **2026-04-26 解消**: Bravura 由来定数を完全除去。`GlyphMetricsGenerated.cs` に emmentaler-20.otf からの自動抽出値を出力 (`audit/scripts/Extract-EmmentalerMetrics.py`)。`GlyphMetrics.cs` には hand-tuned のみ残置 (engraving thicknesses、LP grob defaults 等)。 |
| 31 | `Svg/Layout/SpacingRules.cs` | **真のヒューリスティック (要修正)**: `QuarterNoteWidth=3.6`, `MinNoteWidth=2.0`, `AccidentalWidth=1.2`, `DotWidth=0.6`, `BarlineWidth=0.8` 等は Gourlay (1987) ベースで LP 引用ナシ。 |
| 12 | `Svg/Layout/TieFormattingProblem.cs` | **大半は幾何プリミティブ** (`staffHeight=4.0` = 5線×1スペース; `0.5` = half-staff-space)。一部 demerit (`epsilon`, `defaultOffset=0.3`) は要 LP 引用。 |
| 10 | `Svg/EngravingRules.cs` | **要 LP 引用**: `StandardStemLength=3.5`, `ShortestDurationSpace=2.0`, `SpacingIncrement=1.2`, `MinimumNoteSpacing=1.5`, `SpaceAfterBarline=1.3`, `SpaceAfterClef=1.0` は全て LP デフォルト由来 (`scm/define-grobs.scm` Stem / SpacingSpanner / NonMusicalPaperColumn)。 |
| 9 | `Svg/SpacingSettings.cs` | **値は LP 完全一致 (要 citation のみ追加)**: SystemSystem(basic=12, min=8, pad=1, stretch=60) 等は `ly/paper-defaults-init.ly:62-83` に**100%一致**。citation を貼るだけで Green 化。 |
| 1 | `Svg/Layout/NoteCollision.cs` | `shiftAmount = 1.0` (line 369) 単発。要文脈精査 (Phase 1-4)。 |

---

## 詳細所見

### A. 誤検知 (PaperSettings.cs)

`A4: 210 x 297 mm` 等の紙サイズは ISO 216、`1 inch = 25.4 mm` は SI 定義値。**LP 引用は不要、物理定数なので特例扱い**。スクリプトは「ファイル先頭に LILYPOND-REF が無い」だけで Red 判定したので False Positive。
**処置**: ファイル先頭に `// 物理定数 (ISO 216 / SI)。LilyPond 由来ではない。` を 1 行入れて分類除外。

### B. 〜2026-04-25 旧設計: SMuFL Bravura 経由 (解消済)

旧 `GlyphMetrics.cs` は Bravura SMuFL metadata から定数を写して使っていたが、LilySharp は実際には Emmentaler を描画していたため定数値とフォント実体が ±0.05〜0.2sp 単位で乖離していた (例: AccidentalSharp width 0.996 vs 実 1.100)。

**2026-04-26 解消** (commit 8495976):
- `audit/scripts/Extract-EmmentalerMetrics.py` で `editors/vscode/server/Fonts/emmentaler-20.otf` から BBox / advance を fontTools 経由で抽出
- `GlyphMetricsGenerated.cs` (auto-generated `partial class`) に出力。`// AUTO-GENERATED — DO NOT EDIT MANUALLY` ヘッダ付き
- `GlyphMetrics.cs` には hand-tuned 定数 (engraving thicknesses、spacing heuristics、LP grob defaults) のみ残置
- フォント差し替え時は script 1 発で再生成可

### C. 真のヒューリスティック - SpacingRules.cs (修正必須)

このファイルは Phase 0-E で既に flag 済の **最大の heuristic 集中点**。

| 定数 | 値 | LP 由来推定 | 修正案 |
|---|---:|---|---|
| `QuarterNoteWidth` | 3.6 | `spacing-spanner.cc::standard_breakable_column_widths` 系 + `define-grobs.scm:NonMusicalPaperColumn` | `LILYPOND-REF: lily/spacing-spanner.cc + scm/define-grobs.scm:SpacingSpanner` で再導出 |
| `MinNoteWidth` | 2.0 | `note-spacing.cc:NoteSpacing::stem_dir_correction` 周辺 | 引用付与 + 実 LP 計算と乖離があれば調整 |
| `AccidentalWidth` | 1.2 | `accidental.cc` glyph extent + `accidental-placement.cc` | LP 実値と数値比較してから |
| `DotWidth` | 0.6 | `dots.cc` (= dots-engraver.cc + Dots grob extent) | `define-grobs.scm:Dots` 参照 |
| `BarlineWidth` | 0.8 | `bar-line.cc` + `define-grobs.scm:BarLine` thickness | 引用付与 |
| `RepeatBarlineWidth` | 1.6 | LP の `:|.` バーライン幅 | 引用付与 |
| `DoubleBarlineWidth` | 1.2 | LP の `||` バーライン幅 | 引用付与 |

**根本的な問題**: そもそも LP の spacing は **spring + rod 解** で動的計算する設計で、固定定数は使わない。LilySharp が Gourlay (1987) ベースで固定値を持っているのは **アーキテクチャ的乖離**。Phase H-1〜H-5 (LAYOUT_ROADMAP_V2) と同根。

### D. 値完全一致だが citation 欠落 (SpacingSettings.cs) - 即修正可能

LilySharp の値 vs LP `ly/paper-defaults-init.ly:62-83`:

| LilySharp プロパティ | 値 | LP 該当行 | 一致 |
|---|---|---|---|
| SystemSystem | basic=12, min=8, pad=1, stretch=60 | line 62-65 | ✓ |
| ScoreSystem | basic=14, min=8, pad=1, stretch=120 | line 66-69 | ✓ |
| MarkupSystem | basic=5, pad=0.5, stretch=30 | line 70-72 | ✓ |
| ScoreMarkup | basic=12, pad=0.5, stretch=60 | line 73-75 | ✓ |
| MarkupMarkup | basic=1, pad=0.5 | line 76-77 | ✓ |
| TopSystem | basic=6, min=0, pad=1 | line 78-80 | ✓ |
| TopMarkup | basic=4, min=0, pad=1 | line 81-83 (推定) | ✓ |

**処置**: `// LILYPOND-REF: ly/paper-defaults-init.ly:62-83` 追記のみ (5min)

### E. EngravingRules.cs - 要 LP 引用付与

| 定数 | 値 | LP 由来 |
|---|---:|---|
| `StandardStemLength` | 3.5 | `define-grobs.scm:Stem.length` (default) — 既に EngravingDefaults.cs と重複の可能性 |
| `MinimumStemLength` | 2.5 | 同上 |
| `ShortestDurationSpace` | 2.0 | `define-grobs.scm:SpacingSpanner.shortest-duration-space` |
| `SpacingIncrement` | 1.2 | `define-grobs.scm:SpacingSpanner.spacing-increment` |
| `MinimumNoteSpacing` | 1.5 | `note-spacing.cc` |
| `SpaceAfterBarline` | 1.3 | `define-grobs.scm:NonMusicalPaperColumn` 関連 (要特定) |
| `SpaceAfterClef` | 1.0 | 同上 |

**処置**: 各定数に LP 引用追加 (30-45min)

### F. TieFormattingProblem.cs - 大半は幾何だが要再確認

`staffHeight = 4.0` は staff の物理高 (5本線 × 1スペース間隔)。`0.5` は half-staff-space (LP の position 単位は half-staff-space)。これらは **LP 数学的不変量** であり LILYPOND-REF より型/単位コメントが妥当。
ただし `defaultOffset=0.3`, `epsilon` 系の demerit weight は `tie-formatting-problem.cc` 内の `tie_configuration.cc` 等から由来するはず。

---

## アクションサマリー

### 即修正 (合計 ~1.5h)
| 優先 | 作業 | 工数 | 効果 |
|---|---|---:|---|
| HIGH | SpacingSettings.cs に LP citation 追加 | 5min | 9 Red → Green |
| HIGH | EngravingRules.cs の 7 定数に citation 追加 | 30min | 7 Red → Green |
| MED | PaperSettings.cs に "ISO/SI 物理定数" コメント追加 | 5min | 37 誤検知解除 |
| ~~MED~~ ✅ | ~~GlyphMetrics.cs ファイル先頭の Bravura 注記を強化~~ → 2026-04-26 解消、`Extract-EmmentalerMetrics.py` で font 自動抽出に置換 | done | 31 件解消 |
| MED | TieFormattingProblem.cs 真の demerit 定数 (`defaultOffset` 等) に citation | 20min | ~5 Red → Green |
| MED | NoteCollision.cs:369 の `shiftAmount=1.0` 文脈確認 | 10min | Phase 1-4 で対応 |

### Phase 4 で対処 (アーキテクチャ修正、合計 H phase 規模)
- **SpacingRules.cs の Gourlay (1987) ベース固定定数を LP 由来 spring/rod 動的計算に切替**: LAYOUT_ROADMAP_V2 Phase H-1, H-2, H-3, H-5 と同根。固定値の citation 付与だけでなく、計算自体を LP 準拠に。

---

## 結論

- **真の Red (要修正): 約 50-60 件** (SpacingRules / TieFormatting / EngravingRules / SpacingSettings)
- **誤検知 (Red 判定だが LP 由来でないもの): 約 70 件** (PaperSettings / GlyphMetrics の大部分)
- 最も着手価値の高い 2 件: ① SpacingSettings.cs の citation 補完 (5分)、② SpacingRules.cs のアーキテクチャ修正 (Phase H と統合)
