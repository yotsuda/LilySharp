# Grob & Property Coverage Matrix (Phase 1-3)

**生成日**: 2026-04-25
**LP 入力**: `scm/define-grobs.scm` (165 grobs), `scm/define-grob-properties.scm` (483 properties)
**LilySharp 入力**: `LilySharp.Core/**/*.cs` (168 ファイル)
**抽出スクリプト**: `audit/scripts/Build-GrobCoverage.ps1`
**生データ**: `audit/grob_coverage.csv`, `audit/property_coverage.csv`

---

## サマリー

| 項目 | Total | Used (≥3 出現) | Mention (1-2) | Absent (0) |
|---|---:|---:|---:|---:|
| Grobs | 165 | 52 (32%) | 16 (10%) | **97 (59%)** |
| Properties | 483 | 124 (26%) | 30 (6%) | **329 (68%)** |

---

## Grob 不在 (97件) - カテゴリ別

### HIGH IMPACT (実装すべき欠落、~25件)

#### 1. ページ・コラム基盤 (アーキテクチャ要素、最高優先度)
- `PaperColumn`, `NonMusicalPaperColumn` — **LP の水平レイアウト基盤**。LilySharp には対応概念が不在 (or 別名で存在?) → Phase 1-4 で要確認
- `BreakAlignment`, `BreakAlignGroup` — break-aligned 要素のコンテナ
- `VerticalAlignment` — VerticalAxisGroup の親

#### 2. 多小節休符 (`MultiMeasureRest` ファミリー)
- `MultiMeasureRest`, `MultiMeasureRestNumber`, `MultiMeasureRestScript`, `MultiMeasureRestText`
- LilySharp に MultiMeasureRest 関連の処理は存在? → Phase 1-1 でも `multi-measure-rest.cc` 引用ゼロを確認済

#### 3. 装飾・記号関連
- `Fingering`, `FingeringColumn` — フィンガリング (基本記譜)
- `Glissando` — グリッサンド線 (LilySharp に GlissandoEngraver はある→ grob 名一致しない可能性)
- `BarNumber`, `MeasureCounter`, `MeasureGrouping` — 小節番号
- `BendSpanner`, `BendAfter` — ベンド
- `TextMark` — テキストマーク (`\textMark`)
- `TupletNumber` — Tuplet 数字 (TupletBracket とは別 grob)
- `LedgerLineSpanner` — 加線
- `SpanBar`, `SpanBarStub` — 多段譜のバーライン
- `ScriptColumn`, `ScriptRow` — Script のスタッキング
- `RepeatTie`, `LaissezVibrerTie`, `LaissezVibrerTieColumn`, `RepeatTieColumn`, `TieColumn` — Tie 派生

#### 4. 歌詞拡張
- `LyricExtender` — 拡張線 (Phase J で言及)
- `LyricSpace` — 歌詞間スペーシング grob
- `StanzaNumber` — 連番

#### 5. ダイナミクス・ペダル拡張
- `SostenutoPedal`, `SostenutoPedalLineSpanner`, `UnaCordaPedal`, `UnaCordaPedalLineSpanner` — Sustain 以外のペダル

#### 6. その他
- `DotColumn` — Phase 1-1 で確認済。Phase I-4 で実装予定。
- `RestCollision` — 多声部での休符配置
- `Footnote` — Phase J-5 で予定
- `StemStub` — 部分ステム

### MEDIUM IMPACT (~30件)

#### Figured bass 拡張
- `BassFigureAlignment`, `BassFigureAlignmentPositioning`, `BassFigureBracket`, `BassFigureContinuation`, `BassFigureLine`

#### Cue / 異譜表
- `CueClef`, `CueEndClef` (Mention のみ - 1 〜 2件)
- `InstrumentSwitch`

#### 解析記法
- `BalloonText` — 注釈バルーン
- `HorizontalBracket`, `HorizontalBracketText`, `OptionalMaterialBracket` — 解析ブラケット
- `MeasureSpanner` — 小節スパナ
- `MelodyItem` — メロディ判定

#### Repeat 拡張
- `DoublePercentRepeat`, `DoublePercentRepeatCounter`, `PercentRepeatCounter`, `RepeatSlash`, `DoubleRepeatSlash`, `SignumRepetitionis`

#### 章 / 区分マーカー
- `JumpScript`, `CaesuraScript` — Coda 等のジャンプ記号

#### Tab / ギター
- `TabNoteHead`, `FretBoard`

#### Trill 拡張
- `TrillPitchHead`, `TrillPitchAccidental`, `TrillPitchGroup`, `TrillPitchParentheses`

### LOW IMPACT (~40件)

#### 古典 / 中世記譜法
- `KievanLigature`, `MensuralLigature`, `VaticanaLigature`, `LigatureBracket`
- `Custos`, `Divisio`, `Episema`
- `Ambitus`, `AmbitusAccidental`, `AmbitusLine`, `AmbitusNoteHead`

#### Cluster / Chord 視覚
- `ChordBracket`, `ChordSlur`, `ChordSquare`, `ClusterSpanner`, `ClusterSpannerBeacon`

#### Grid (Chord 表記グリッド)
- `GridChordName`, `GridLine`, `GridPoint`

#### モダン記法
- `DurationLine`, `BendSpanner`, `FingerGlideSpanner`, `VowelTransition`
- `ApproximatePitchNoteHead`, `StaffHighlight`
- `Parentheses`, `LyricRepeatCount`, `NoteName`, `StrokeFinger`
- `VoiceFollower`

#### 編集 / デバッグ
- `ControlPoint`, `ControlPolygon` (slur/tie 編集)
- `AccidentalCautionary`, `AccidentalSuggestion` (Used)

#### 中央化バーナンバー
- `CenteredBarNumber`, `CenteredBarNumberLineSpanner`

---

## Property 不在 (329件) - 最重要カテゴリ

### 致命的: Callback Property の欠落

LP では grob のレイアウト処理は **Scheme コールバック (property に lambda を入れる)** で組み立てる。LilySharp は C# 直接実装で代替するが、**仕様としての callback property は存在しない**:

| Property | LP 役割 | LilySharp 影響 |
|---|---|---|
| `springs-and-rods` (0) | 各 grob が spring/rod を生成する callback | LilySharp は engraver で直接生成。**override 不可** |
| `before-line-breaking` (0) | 改行前に走る callback chain | 「after-line-breaking 概念」自体が無い |
| `after-line-breaking` (0) | 改行後 callback | 同上 |
| `positioning-done` (0) | Y軸配置完了通知 | 不在 |
| `pure-Y-extent` (0) | 改行前の高速 Y 推定 | LilySharp は MultiStaffLayouter.CalculatePureSystemHeight で代替実装あり (ただし property 名で expose されない) |
| `pure-relevant-grobs` (0) | Pure 計算対象 | 同上 |
| `vertical-skylines` (0) | 各 grob の Y 軸 skyline 生成 | LilySharp は外部 SkylineBuilder で集中実装 |
| `horizontal-skylines` (0) | X 軸 skyline | 同上 |

**結論**: LilySharp は LP の callback chain アーキテクチャを **採用していない**。これは故意の設計判断 (C# は dynamic dispatch コストが高い) だが、**LP の `\override Foo.before-line-breaking = #my-fn` 系のユーザーカスタマイズパスは利用不可**。完全 mimicry の意味では gap だが、Phase 4 で取り組むには大規模な refactor が必要。

### Spacing/Layout 重要 property の不在

| Property | 状態 | コメント |
|---|---|---|
| `shortest-playing-duration` | Absent | Phase H-1 で必要 (`SpacingRules.cs` に投入予定) |
| `encompass-objects` | Absent | Slur scoring の核心。LilySharp の `SlurScoringProblem.cs` で別名で実装済の可能性 |
| `keep-alive-with` | Absent | hara-kiri 入力 |
| `spacing-pair` | Absent | break-align ペア |
| `allow-loose-spacing` | Absent | loose column |
| `break-align-symbols` | Absent | break-align grouping |
| `side-axis` | Absent | side-position-interface 軸選択 |
| `head-direction` | Absent | NoteHead 方向制御 |
| `no-stem-extend` | Absent | stem 終端処理 |
| `line-break-system-details` | Absent | line-break per-system 上書き |
| `page-break-permission` | Absent | page break コントロール |

これらは Phase 4 タスクキューに追加。

### よく使われている property (健全部分)

`text` (662), `length` (539), `width` (454), `stem` (409), `height` (381), `spacing` (306), `beam` (288), `direction` (287), `bracket` (279), `padding` (234), `positions` (225), `font` (218), `details` (198), `slur` (173), `dots` (160), `tie` (156)... コアの property は概ね使われている。

---

## アクションサマリー

### Phase 4 即時対応 (HIGH IMPACT 不在 grob)
| 対象 | LP 参照 | 想定工数 |
|---|---|---:|
| `MultiMeasureRest` ファミリー (4 grobs) | `lily/multi-measure-rest.cc` + `define-grobs.scm` | 6h |
| `Fingering`, `FingeringColumn` | `lily/fingering-engraver.cc` + `lily/fingering-column.cc` | 3h |
| `Glissando` (確認: 既存 `GlissandoEngraver.cs` で grob 名 `Glissando` を出力するか) | `lily/line-spanner.cc` + `define-grobs.scm:Glissando` | 1h |
| `BarNumber` | `lily/bar-number-engraver.cc` | 2h |
| `LedgerLineSpanner` | `lily/ledger-line-spanner.cc` | 2h |
| `LyricExtender`, `LyricSpace` | `lily/extender-engraver.cc` | 3h |
| `SpanBar`, `SpanBarStub` | `lily/span-bar.cc` | 4h |
| `TupletNumber` (TupletBracket とは別 grob として) | `lily/tuplet-number.cc` | 2h |
| `RepeatTie`, `LaissezVibrerTie`, `TieColumn` | `lily/repeat-tie.cc`, `lily/laissez-vibrer-tie.cc`, `lily/tie-column.cc` | 5h |
| `RestCollision` | `lily/rest-collision.cc` | 3h |
| `DotColumn` | `lily/dot-column.cc` | 2h (Phase I-4 で予定) |
| `Footnote` | Phase J-5 (`lily/footnote-engraver.cc`) | 3h |

合計: ~36h を Phase G-J ロードマップに追加要

### Phase 4 中期対応 (MEDIUM)
- BassFigure 拡張 (5 grobs): figured bass 完全対応
- TabNoteHead + FretBoard: タブ譜
- TrillPitch ファミリー: pitched trill
- Coda / Segno まわりの整備

### 後回し (LOW、当面 out-of-scope)
- 中世記譜法 (Kievan/Mensural/Vaticana/Custos/Episema/Divisio)
- Ambitus
- Cluster, GridChord
- ControlPoint/Polygon (デバッグ用)

### Property システムは Phase 4 範囲外
LP の callback 駆動 property システム (`before-line-breaking` 等) を完全模倣するには C# 側で動的 callback resolution を導入する大規模 refactor が必要。今フェーズは **値型 property のみ**カバーし、callback property は **将来課題** として REVIEW_REPORT.md に明記。
