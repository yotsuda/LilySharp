# LilySharp レイアウトエンジン実装ロードマップ — 作業手順書

## 概要

LilySharp の浄書（エングレービング）エンジンを LilyPond と同等の品質まで引き上げるための段階的実装計画。

- **対象プロジェクト:** `C:\MyProj\LilySharp`
- **参照ソース:** `C:\MyProj\lilypond-src`
- **現在の到達度:** 約 25-30%（コア機能ベース）
- **目標:** LilyPond の浄書レイアウトの完全模倣

---

## 絶対原則

### 1. アドホック実装の禁止

- 独自の推測や経験則に基づく実装を**一切行わない**
- すべての浄書アルゴリズムは LilyPond のソースコードに基づくこと
- パラメータ（ペナルティ値、閾値、デフォルト値）は LilyPond から正確に移植すること

### 2. LilyPond 参照の義務

各機能の実装時、以下のソースを必ず参照する:

| ソース種別 | パス | 内容 |
|-----------|------|------|
| C++ エンジン | `lilypond-src/lily/*.cc` | コアアルゴリズム |
| C++ ヘッダ | `lilypond-src/lily/include/*.hh` | インターフェース定義 |
| Scheme 定義 | `lilypond-src/scm/define-grobs.scm` | Grob プロパティ・デフォルト値 |
| Scheme 関数 | `lilypond-src/scm/music-functions.scm` | 音楽関数 |
| 回帰テスト | `lilypond-src/input/regression/*.ly` | 期待される動作 |

### 3. テスト駆動

- 実装前に `.lys` テストケースと期待出力を作成する
- LilyPond で同等の入力をコンパイルし、SVG 出力を視覚的基準とする
- 回帰テスト（xUnit）を必ず追加する

---

## 各タスクの共通作業手順

すべての機能実装は以下の 7 ステップで進める:

### Step 1: LilyPond ソース読解

```
対象ファイルを精読し、以下を把握する:
- アルゴリズムの全体構造
- 入出力データの型と形式
- ペナルティ値・閾値などの定数
- エッジケース処理
```

### Step 2: アルゴリズム文書化

```
docs/ 以下にマークダウンで記録する:
- アルゴリズムのフロー図（疑似コード）
- LilyPond のパラメータ一覧と意味
- LilySharp での対応クラス・メソッド
```

### Step 3: 設計マッピング

```
LilyPond (C++/Scheme) → LilySharp (C#) の対応表を作成:
- クラス対応
- メソッド対応
- データ構造対応
```

### Step 4: テストケース作成（実装前）

```
1. samples/test/ に .lys テストファイルを作成
2. LilyPond で同等の入力をコンパイルし参照 SVG を取得
3. LilySharp.Tests/ に xUnit テストを追加
```

### Step 5: 実装

```
- LilyPond のアルゴリズムを C# に移植
- 定数・パラメータは LilyPond の値を正確に使用
- コメントに LilyPond の対応ファイル:行番号を記載
```

### Step 6: 視覚比較検証

```
1. lysc svg でテストケースをコンパイル
2. LilyPond 出力 SVG と並べて目視比較
3. 差異があればアルゴリズムを見直す
```

### Step 7: 回帰テスト追加

```
1. xUnit テストで自動検証可能な項目を追加
2. work_progress.md を更新
3. コミット
```

---

## Phase 1: 既存機能の品質向上（LilyPond アルゴリズム完全準拠）

**目的:** 実装済みだが LilyPond との品質差が大きい機能を強化

### 1.1 Beam quanting 強化

- **LilySharp:** `BeamScoringProblem.cs` (635行)
- **LilyPond:** `lily/beam.cc` (1554行), `lily/beam-quanting.cc` (1403行), `lily/include/beam.hh`
- **Scheme:** `scm/define-grobs.scm` → Beam grob 定義
- **サブタスク:**
  - [ ] Beam position quantization（スタッフライン吸着）
  - [ ] Stem length demerit 計算の完全移植
  - [ ] Collision penalty（スタッフライン・他 grob との衝突）
  - [ ] Damping direction penalty (800)
  - [ ] Musical direction factor (400)
  - [ ] Ideal slope factor (10)
  - [ ] Round-to-zero-slope 処理 (0.02)
  - [ ] Gap management (0.8 staff-space)
  - [ ] パラメータ移植: secondary-beam-demerit=10, stem-length-demerit-factor=5, region-size=2, collision-penalty=500, collision-padding=0.35, hint-direction-penalty=20

### 1.2 Tie formatting 強化

- **LilySharp:** `TieFormattingProblem.cs`
- **LilyPond:** `lily/tie-formatting-problem.cc` (1286行), `lily/tie.cc`, `lily/include/tie.hh`
- **Scheme:** `scm/define-grobs.scm` → Tie grob 定義
- **サブタスク:**
  - [ ] S-curve Bezier 制御点計算の精密化
  - [ ] center-staff-line-clearance (0.6)
  - [ ] tip-staff-line-clearance (0.45)
  - [ ] Multi-tie stacking (TieColumn)
  - [ ] Tie-tie collision distance (0.45)
  - [ ] Tie-tie collision penalty (25.0)
  - [ ] Intra-space threshold (1.25)
  - [ ] Outer-tie symmetry penalties (vertical=10, length=10)
  - [ ] Vertical distance penalty factor (7)
  - [ ] Single/multi tie region size (4/3)
  - [ ] Between-length-limit (1.0)

### 1.3 Slur scoring 強化

- **LilySharp:** `SlurScoringProblem.cs` (521行)
- **LilyPond:** `lily/slur-scoring.cc` (906行), `lily/slur.cc`, `lily/include/slur.hh`
- **サブタスク:**
  - [ ] Obstacle type 拡張（現在: NoteHead, Stem, Accidental, Articulation）
  - [ ] Height limit パラメータ (2.0)
  - [ ] Ratio パラメータ (0.25)
  - [ ] 衝突回避の inside/outside/around モード
  - [ ] Cross-staff slur 対応
  - [ ] Phrasing slur の視覚的区別

### 1.4 Stem 長計算の精密化

- **LilySharp:** `SvgRenderer.cs` 内
- **LilyPond:** `lily/stem.cc` (1258行), `lily/include/stem.hh`
- **サブタスク:**
  - [ ] Duration-based stem length（8th/16th/32nd で異なる長さ）
  - [ ] Beamed stem shortening 3 段階 (1.0, 0.5, 0.25)
  - [ ] Beamed minimum free lengths (1.83, 1.5, 1.25)
  - [ ] Cross-staff stem 対応
  - [ ] Tremolo stem 特殊処理

### 1.5 Accidental placement 完全準拠

- **LilySharp:** `AccidentalPlacement.cs`
- **LilyPond:** `lily/accidental-placement.cc`, `lily/accidental.cc`
- **サブタスク:**
  - [ ] Column-based ordering の完全移植
  - [ ] Right-padding (0.15 staff-space)
  - [ ] Horizontal skyline 衝突検出
  - [ ] Courtesy accidental（括弧付き）
  - [ ] Extra spacing width (-0.2, 0.0)

---

## Phase 2: スペーシングとレイアウトの完全化

**目的:** 楽譜全体のレイアウト品質を LilyPond 水準に引き上げ

### 2.1 Spring-Rod モデル完全実装

- **LilySharp:** `SpringSolver.cs`
- **LilyPond:** `lily/simple-spacer.cc`, `lily/spacing-spanner.cc`, `lily/note-spacing.cc`
- **サブタスク:**
  - [ ] Spring 弾性パラメータの精密化
  - [ ] Rod（最小距離制約）の完全実装
  - [ ] Note spacing correction: knee=1.0, same-direction=0.25, stem=0.5
  - [ ] Space-to-barline オプション
  - [ ] Loose column 処理

### 2.2 Grace note spacing

- **LilySharp:** `GraceNoteEngraver.cs`
- **LilyPond:** `lily/grace-spacing-engraver.cc`, `lily/grace-spacing.cc`
- **サブタスク:**
  - [ ] Grace spacing increment (0.8 vs normal 1.2)
  - [ ] Shortest-duration-space (1.6)
  - [ ] Grace beam グループ化

### 2.3 Page breaking 最適化

- **LilySharp:** `PageBreaker.cs`
- **LilyPond:** `lily/page-breaking.cc` (1768行), `lily/constrained-breaking.cc`
- **サブタスク:**
  - [ ] Optimal page breaking（デフォルト戦略）
  - [ ] Minimal page breaking
  - [ ] Page turn optimization
  - [ ] Badness 計算関数の完全移植
  - [ ] Footnote/in-note 高さ計算

### 2.4 Vertical justification

- **LilySharp:** `PageLayouter.cs`
- **LilyPond:** `lily/page-layout-problem.cc` (1369行)
- **サブタスク:**
  - [ ] System 間スペーシングの最適化
  - [ ] Title/markup spacing
  - [ ] Bottom padding
  - [ ] Tight spacing モード
  - [ ] Compressed lines カウント

### 2.5 System spacing

- **LilySharp:** `VerticalSkyline.cs`
- **LilyPond:** `lily/system.cc` (1060行), `lily/axis-group-interface.cc` (1061行)
- **サブタスク:**
  - [ ] Skyline-based system spacing の精密化
  - [ ] Systems-per-page 制約
  - [ ] Min/max systems per page
  - [ ] Ragged-right / ragged-bottom / ragged-last-bottom

---

## Phase 3: 未実装の主要浄書機能

**目的:** ユーザーが日常的に使用する浄書機能を追加

### 3.1 Hairpin (crescendo/decrescendo)

- **LilyPond:** `lily/hairpin.cc`, `lily/dynamic-engraver.cc`
- **Scheme:** `scm/define-grobs.scm` → Hairpin grob
- **サブタスク:**
  - [ ] 文法拡張: hairpin 構文
  - [ ] Hairpin grob: wedge 形状描画
  - [ ] Grow-direction 計算
  - [ ] Height (0.6666 staff-space)
  - [ ] Circled tip (al/del niente)
  - [ ] Minimum length (2.0 staff-space)
  - [ ] DynamicLineSpanner コンテナ
  - [ ] Outside-staff-priority (250)

### 3.2 Text spanners

- **LilyPond:** `lily/text-spanner.cc`, `lily/line-spanner.cc`
- **サブタスク:**
  - [ ] 文法拡張: text spanner 構文
  - [ ] Bound-details (start/end text)
  - [ ] Dash-fraction (0.2), dash-period (3.0)
  - [ ] Line style variants

### 3.3 Ottava brackets

- **LilyPond:** `lily/ottava-bracket.cc`, `lily/ottava-engraver.cc`
- **サブタスク:**
  - [ ] 文法拡張: ottava 構文
  - [ ] OttavaBracket grob
  - [ ] Direction: UP/DOWN (8va/8vb/15ma/15mb)
  - [ ] Edge height
  - [ ] Pitch transposition 処理

### 3.4 Glissando

- **LilyPond:** `lily/glissando.cc`, `lily/glissando-engraver.cc`
- **サブタスク:**
  - [ ] 文法拡張: glissando 構文
  - [ ] Line style (straight, zigzag, trill)
  - [ ] Gap (0.5), zigzag-width (0.75)
  - [ ] Start-at-dot, end-on-accidental

### 3.5 Arpeggio

- **LilyPond:** `lily/arpeggio.cc`, `lily/arpeggio-engraver.cc`
- **サブタスク:**
  - [ ] 文法拡張: arpeggio 構文
  - [ ] Wavy line 描画
  - [ ] Direction (arrow optional)
  - [ ] Cross-staff arpeggio
  - [ ] Span arpeggio (multi-staff)

### 3.6 Rehearsal marks

- **LilyPond:** `lily/mark-engraver.cc`, `lily/mark-tracking-translator.cc`
- **サブタスク:**
  - [ ] 自動番号付け (A, B, C... or 1, 2, 3...)
  - [ ] カスタムマークアップ
  - [ ] Break-alignment

### 3.7 Piano pedal

- **LilyPond:** `lily/piano-pedal-engraver.cc`, `lily/piano-pedal-bracket.cc`
- **サブタスク:**
  - [ ] Sustain pedal (Ped. / *)
  - [ ] Sostenuto pedal
  - [ ] Una corda pedal
  - [ ] Bracket style vs text style

### 3.8 Feathered beams

- **LilyPond:** `lily/beam.cc` (feather 関連セクション)
- **サブタスク:**
  - [ ] 文法拡張
  - [ ] Fan-in / fan-out beam rendering
  - [ ] Beam width gradation

### 3.9 Kneed beams

- **LilyPond:** `lily/beam.cc` (auto-knee-gap セクション)
- **サブタスク:**
  - [ ] Auto-knee-gap 閾値 (5.5 staff-space)
  - [ ] Kneed beam slope 計算
  - [ ] Cross-staff beam rendering

### 3.10 Nested tuplets

- **LilyPond:** `lily/tuplet-bracket.cc` (891行), `lily/tuplet-engraver.cc`
- **サブタスク:**
  - [ ] 入れ子構造のパース対応
  - [ ] 親子 bracket の配置調整
  - [ ] Full-length-note プロパティ

---

## Phase 4: 高度な記法と出力

**目的:** 専門的な記法とプロフェッショナル出力形式の追加

### 4.1 Figured bass

- **LilyPond:** `lily/figured-bass-engraver.cc`, `lily/figured-bass-position-engraver.cc`
- **サブタスク:**
  - [ ] 文法拡張: figured bass 構文
  - [ ] BassFigure grob
  - [ ] Alignment/positioning
  - [ ] Bracket/continuation/line

### 4.2 Chord names

- **LilyPond:** `lily/chord-name.cc`
- **Scheme:** `scm/chord-name.scm`
- **サブタスク:**
  - [ ] 文法拡張: chord name 構文
  - [ ] ChordName grob
  - [ ] Jazz chord naming
  - [ ] Grid/square notation variant

### 4.3 Percent/Tremolo repeats

- **LilyPond:** `lily/percent-repeat-engraver.cc`, `lily/chord-tremolo-engraver.cc`
- **サブタスク:**
  - [ ] Percent repeat symbol (%)
  - [ ] Double percent (%%)
  - [ ] Repeat counter
  - [ ] Slash repeat
  - [ ] Stem tremolo

### 4.4 Cross-staff notation

- **LilyPond:** cross-staff 関連処理 (beam.cc, stem.cc, slur.cc 内)
- **サブタスク:**
  - [ ] Cross-staff stem
  - [ ] Cross-staff beam
  - [ ] Cross-staff slur/tie
  - [ ] Note placement across staves

### 4.5 PDF 出力

- **LilyPond:** `lily/cairo.cc` (1535行)
- **サブタスク:**
  - [ ] PDF レンダリングライブラリ選定 (SkiaSharp / PDFSharp 等)
  - [ ] SVG→PDF 変換 or ネイティブ PDF 描画
  - [ ] Emmentaler フォント埋め込み
  - [ ] ページ管理
  - [ ] メタデータ (title, composer)

---

## Phase 5: 特殊記法とプロパティシステム

**目的:** LilyPond との完全互換性

### 5.1 Ancient notation

- **LilyPond:** `lily/gregorian-ligature-engraver.cc`, `lily/vaticana-ligature-engraver.cc`, `lily/mensural-ligature-engraver.cc`, `lily/kievan-ligature-engraver.cc`
- **サブタスク:**
  - [ ] Gregorian chant notation
  - [ ] Mensural notation
  - [ ] Kievan notation
  - [ ] Vaticana notation

### 5.2 Part combination

- **LilyPond:** `lily/part-combine.cc`, `lily/part-combine-engraver.cc`
- **サブタスク:**
  - [ ] Part combiner logic
  - [ ] Voice merge/split
  - [ ] Solo/a2 表示

### 5.3 Grob property override system

- **LilyPond:** `lily/grob.cc` (1021行), `scm/define-grobs.scm` (4028行)
- **サブタスク:**
  - [ ] Override 構文設計 (LilyPond: `\override Grob.property = value`)
  - [ ] Color property
  - [ ] Transparency/opacity
  - [ ] Font-size
  - [ ] X/Y offset, extra-offset
  - [ ] Padding
  - [ ] Layer control
  - [ ] Callback system (C# delegates)

---

## 品質基準

### 視覚品質
- LilyPond の SVG 出力と並べて目視比較し、同等の品質であること
- 音符間隔、ビーム角度、スラー曲線、タイ形状が LilyPond と一致すること

### パラメータ正確性
- すべてのデフォルト値が LilyPond の `define-grobs.scm` と一致すること
- ペナルティ計算の定数が LilyPond の C++ ソースと一致すること

### テストカバレッジ
- 各機能に最低 3 つの xUnit テストケース
- エッジケース（空の小節、極端な音域、多声部）のテスト
- 回帰テストスイートが全パスであること

### コード品質
- LilyPond の対応ファイル:行番号をコメントに記載
- ビルド警告 0

---

## リスクと対策

| リスク | 影響 | 対策 |
|--------|------|------|
| LilyPond の Scheme コールバックの C# 移植困難 | 高 | プロパティシステムを C# のデリゲート/インターフェースで代替設計 |
| Beam quanting の数値最適化の精度差 | 中 | LilyPond の全テストケースで比較検証 |
| Cross-staff 機能のアーキテクチャ変更 | 高 | Phase 1 完了後に設計レビュー |
| フォントメトリクスの差異 | 中 | 同一の Emmentaler フォントを使用して最小化 |

---

## コミットポリシー

- すべてのコミットには **テストパス** と **ユーザーレビュー承認** が必要
- コミットメッセージに Phase/タスク番号を含める（例: `[Phase1.1] Implement beam position quantization`）
- 機能単位でコミット（1 コミット = 1 サブタスク）

## 進捗更新ルール

- 作業が進行するたびに `work_progress.md` を即座に更新する
- ステータス変更、タスク完了、新規発見事項をすべて記録する

## 学習更新ルール

- 作業中に得た知見（LilyPond のアルゴリズム理解、パラメータ意味等）を本手順書の該当箇所に追記する
- 末尾への追記ではなく、適切な位置に整理して挿入する
