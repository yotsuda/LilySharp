# LilySharp LilyPond準拠監査 & 実装計画 — 作業手順書

## 概要

LilySharp の全レイアウトコードを LilyPond ソースと突き合わせ、未ポート・重複・独自実装を漏れなく特定し、優先度付き実装計画を立案する。

- **対象プロジェクト:** `C:\MyProj\LilySharp`
- **LilyPond ソース:** `C:\MyProj\lilypond-src`
- **LilySharp レイアウト:** `LilySharp.Core/Svg/` (112ファイル: Layout 66, Model 33, Collector 9, Renderer 4)
- **LILYPOND-REF あり:** 103 ファイル
- **前回作業:** Phase 1-9 完了 (51/52 タスク, 98%)

---

## 絶対原則（前手順書より継承）

1. **アドホック実装の禁止** — すべての浄書アルゴリズムは LilyPond ソース準拠
2. **LILYPOND-REF 義務** — 変更時は `lily/*.cc` の該当箇所をコメントで明記
3. **テスト駆動** — 実装前にテストケースを作成

---

## 監査の進め方

### 各監査ユニットの作業手順

1. **LilySharp 読解** — 対象ファイルの全メソッド・定数・アルゴリズムを把握
2. **LILYPOND-REF 確認** — 既存参照コメントの網羅性を確認
3. **LilyPond ソース読解** — 対応する `lily/*.cc` + `scm/define-grobs.scm` を精読
4. **ギャップ分析** — 以下の3カテゴリで差分を記録:
   - 🔴 **未ポート**: LilyPond にあって LilySharp にないロジック（関数・分岐・パラメータ）
   - 🟡 **独自実装**: LilyPond に対応がない独自ヒューリスティック（要削除/置換）
   - 🟠 **重複実装**: 同じロジックが複数箇所に散在（要統合）
5. **実装工数見積** — 各ギャップの修正工数を High/Medium/Low で評価
6. **進捗更新** — `work_progress.md` を即座に更新

### 監査ユニットの分類

機能領域ごとに LilySharp ファイル群 ↔ LilyPond ファイル群を対応付け。

---

## 監査ユニット一覧

### A. コアアルゴリズム（浄書品質に直結）

| ID | 領域 | LilySharp | LilyPond |
|----|------|-----------|----------|
| A1 | Beam scoring | BeamScoringProblem.cs, BeamConfiguration.cs, BeamQuantParameters.cs, BeamEngraver.cs | beam.cc, beam-quanting.cc |
| A2 | Slur scoring | SlurScoringProblem.cs, SlurScoreParameters.cs, SlurEngraver.cs, SlurLayout.cs | slur-scoring.cc, slur.cc |
| A3 | Tie formatting | TieFormattingProblem.cs, TieDetails.cs, TieEngraver.cs, TieLayout.cs | tie-formatting-problem.cc, tie.cc |
| A4 | Stem calculation | StemCalculator.cs, StemDirection.cs | stem.cc |
| A5 | Note collision | NoteCollision.cs | note-collision-interface.cc |
| A6 | Accidental placement | AccidentalPlacement.cs | accidental-placement.cc, accidental.cc |

### B. スペーシング & レイアウト

| ID | 領域 | LilySharp | LilyPond |
|----|------|-----------|----------|
| B1 | Horizontal spacing | SpacingRules.cs, Spring.cs, SpringSolver.cs, NoteSpacingParameters.cs, StaffSpacingParameters.cs | spacing-spanner.cc, simple-spacer.cc, note-spacing.cc |
| B2 | System breaking | SystemBreaker.cs, SystemLayouter.cs | constrained-breaking.cc, system.cc |
| B3 | Page breaking | PageBreaker.cs, PageBreakingParameters.cs, KnuthPlassBreaker.cs | page-breaking.cc, optimal-page-breaking.cc |
| B4 | Vertical layout | PageLayouter.cs, VerticalSpacingParameters.cs | page-layout-problem.cc |
| B5 | Skyline | Skyline.cs, SkylineBuilder.cs, HorizontalSkyline.cs, VerticalSkyline.cs | skyline.cc |
| B6 | Multi-staff layout | MultiStaffLayouter.cs, GrandStaffLayout.cs, ScoreLayout.cs | axis-group-interface.cc |

### C. Engraver（個別浄書機能）

| ID | 領域 | LilySharp | LilyPond |
|----|------|-----------|----------|
| C1 | Dynamics & Hairpin | DynamicEngraver.cs, HairpinEngraver.cs | dynamic-engraver.cc, hairpin.cc |
| C2 | Articulation & Ornament | ArticulationEngraver.cs, OrnamentEngraver.cs | script-engraver.cc, script-column.cc |
| C3 | Tuplet bracket | TupletBracketEngraver.cs | tuplet-bracket.cc, tuplet-engraver.cc |
| C4 | Volta & Repeat | VoltaBracketEngraver.cs, PercentRepeatEngraver.cs | volta-bracket.cc, percent-repeat-engraver.cc |
| C5 | Ottava bracket | OttavaBracketEngraver.cs | ottava-bracket.cc, ottava-engraver.cc |
| C6 | Text spanner | TextSpannerEngraver.cs | text-spanner.cc, line-spanner.cc |
| C7 | Trill spanner | TrillSpannerEngraver.cs | trill-spanner-engraver.cc |
| C8 | Glissando & Arpeggio | GlissandoEngraver.cs, ArpeggioEngraver.cs | glissando.cc, arpeggio.cc |
| C9 | Pedal | PedalEngraver.cs | piano-pedal-engraver.cc, piano-pedal-bracket.cc |
| C10 | Grace notes | GraceNoteEngraver.cs, GraceSpacingParameters.cs | grace-spacing.cc, grace-engraver.cc |
| C11 | Lyrics | LyricEngraver.cs, LyricHyphen.cs, LyricLayout.cs | lyric-engraver.cc, lyric-hyphen.cc, lyric-extender.cc |
| C12 | Tremolo & Feathered beam | TremoloEngraver.cs (+ BeamScoringProblem feather部) | stem-tremolo.cc, beam.cc (feather) |
| C13 | Figured bass & Chord name | FiguredBassEngraver.cs, ChordNameEngraver.cs | figured-bass-engraver.cc, chord-name.cc |

### D. インフラ & レンダラー

| ID | 領域 | LilySharp | LilyPond |
|----|------|-----------|----------|
| D1 | Collector | MeasureCollector.cs, BeamDetector.cs, SlurDetector.cs, TieDetector.cs, etc. | (各 engraver の acknowledge 相当) |
| D2 | Renderer | SvgRenderer.cs, BraceRenderer.cs | stencil 描画相当 |
| D3 | Grob properties | GrobPropertyResolver (Semantics内?) + EngravingDefaults.cs + EngravingRules.cs | grob.cc, define-grobs.scm |
| D4 | Element coordinator | ElementCoordinator.cs, LayoutEngine.cs, MeasureLayouter.cs | paper-column.cc, system.cc |
| D5 | Music mark | MusicMarkEngraver.cs | mark-engraver.cc, metronome-engraver.cc |

---

## 監査結果の出力形式

各ユニットの監査結果は `work_progress.md` の備考欄に要約を記載。
詳細が必要な場合は `docs/audit/` 配下に個別 MD を作成。

---

## 注意事項

1. **Scheme 層を見落とさない** — パラメータ・デフォルト値の多くは `scm/define-grobs.scm` (4028行) や `scm/layout-slur.scm` 等に定義。`lily/*.cc` だけでは定数の出所が不明になる。
2. **行番号ではなく関数名で照合** — 既存 `LILYPOND-REF` の行番号は旧クローン時のもの。最新ソースでは移動・リファクタされている可能性がある。
3. **アーキテクチャの違いを受け入れる** — LilyPond は Engraver/Translator/Grob パターン、LilySharp は統合設計。1:1 の関数マッピングが不可能な箇所がある。**出力品質が同等か**が判断基準。
4. **監査と実装を混ぜない** — 監査中にコード修正しない。全ユニット完了後に優先度付けしてから着手。
5. **LilyPond の不要コードを見極める** — LilySharp が対応しない機能（PostScript 出力、Scheme コールバック、古代記法）のコードは「未ポート」に含めない。
6. **PROJECT_EVALUATION.md の到達度は再評価** — Phase 1-9 で大幅に進んだが評価未更新。監査結果で実際の到達度を再計算する。

---

## 品質基準

- **網羅性**: LilyPond の対応ファイルの全 public 関数がマッピングされていること
- **正確性**: パラメータ値（ペナルティ、閾値）が `define-grobs.scm` と一致すること
- **独自実装ゼロ**: LILYPOND-REF のない計算ロジックが残っていないこと

---

## コミットポリシー

- すべてのコミットには **テストパス** と **ユーザーレビュー承認** が必要
- 監査フェーズのコミットには `[Audit]` プレフィックスを使用

## 進捗更新ルール

- 作業が進行するたびに `work_progress.md` を即座に更新する

## 学習更新ルール

- 監査中に得た知見を本手順書の該当箇所に追記する
