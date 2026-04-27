# LilySharp LilyPond レイアウト再現度向上ロードマップ V3

## Status (2026-04-25 final)

- **前提**: V2 → V3 update。Phase 1 静的レビュー (citation drift, magic constants, grob coverage, top 10 algorithm audit) で **V2 の評価ズレ**と **未列挙ギャップ**を確定。
- **再評価された到達度**: 現状 ~62-67% → **(完了後) ~89-90%**
- **目標**: ~80-85% (LP 完全模倣の現実的天井。Pango font 不在 / callback property system 不在は除外)
- **絶対原則**: LP ソース準拠。全変更に `LILYPOND-REF: lily/<file>.cc:<lines>` 必須。LP 2.25.35 基準で行番号記録。

## Sprint 1-3 + ext 完了サマリ (2026-04-25)

| タスク | 状態 | 備考 |
|---|---|---|
| K-1 break-substitution | ✅ 完了 | Slur/Tie + 6 engraver (Hairpin/Ottava/Volta/TextSpanner/TrillSpanner/Glissando) 統一 |
| K-1b cross-measure beam (full) | ✅ 完了 | Detector + Engraver + cross-system broken pieces 全実装 |
| G-2 outside-staff-priority | ⚠ 未着手 | OutsideStaffStacker.cs 既実装、リファクタ不要と判断 |
| G-3' staff-affinity | ✅ 完了 | direction-aware spec selection (UP/DOWN/CENTER) |
| G-4 pure height | ✅ Already implemented | `CalculatePureSystemHeight` で確認 |
| H-1 multi-voice tracking | ✅ 完了 | `ComputeShortestPlayingAt` + `CreateTimingSpringMultiVoice` |
| H-2 strict_note_spacing | ✅ Already implemented | Sprint 1 H-1 と同時に確認 |
| H-3 separating padding | ✅ 完了 | `NoteSpacingParameters.MinItemGap` 可変化 |
| H-4 grace spacing | ✅ Already implemented | GraceSpacingParameters + Engraver |
| H-5 break alignment | ✅ Already implemented | BreakAlignSpacing.GetSpacing |
| H-6 3-tier permission | ✅ 完了 | `MinPermission` + Effective(Page/Turn)Permission |
| I-3a accidental glyph skyline | ✅ 完了 | flat/doubleFlat の bowl + stem 分離 stepped skyline |
| I-3b editorial accidental | ✅ 完了 | EditorialFontFactor + parens 統合 |
| K-1 break-substitution | (上記) | |
| L-1 MultiMeasureRest | ✅ 完了 | parser `R1*N` + church_rest (1-10) + big_rest (>10) |
| L-2 Fingering | ✅ 完了 | `@finger.N` + chord pitch articulations (ext 含む) |
| L-3 Tie variants (LV/RT/Column) | ✅ 完了 | `@laissezVibrer`, `@repeatTie` + chord ties (ext 含む) |
| L-4 SpanBar | ✅ 完了 | `DrawSystemBarlines` を BarlineType-aware 化 |
| L-5 LedgerLineSpanner | ✅ 完了 | adjacent ledger lines の merge span detection |
| L-6 BarNumber | ✅ 完了 | system 先頭 measure に bar number 描画 |
| L-7 LyricExtender / LyricSpace / StanzaNumber | ✅ 完了 | LyricHyphen に既実装 + StanzaNumberEngraver 新規 |
| L-8 TupletNumber 独立 grob | ✅ 完了 | TupletBracketLayout に NumberX/Y アクセサ追加 |
| M-1 FileMissing citation | ✅ Already done | 旧 sprint で完了済 |
| M-2 SpacingSettings citation | ✅ 完了 | per-property paper-defaults-init.ly:62-87 行番号付加 |

### 計測値の推移
| 指標 | Sprint 0 | 完了後 | 増分 |
|---|---:|---:|---:|
| Test count | 1,160 | **1,324** | **+164** |
| Failed tests | 0 | 0 | — |
| 到達度 | ~62-67% | **~89-90%** | +25pt |
| 累計工数見積 | — | ~80-90h 相当 | — |

---

## V2 → V3 主な変更

| V2 タスク | V2 評価 | V3 で確定 | アクション |
|---|---|---|---|
| G-1 build_system_skyline | 未実装 (4h) | **実装済 (Faithful)** | **削除**、4h を新規へ振替 |
| I-1 meshing multipliers | 未実装 (2h) | **実装済 (Faithful)** | 削除 |
| I-2 head wipe | 未実装 (2h) | **実装済 (Faithful)** | 削除 |
| I-4 dot collision | 未実装 (2h) | **実装済 (Faithful)** | 削除 |
| I-5 multi-voice cascading | 未実装 (1h) | **実装済 (Faithful)** | 削除 |
| **(新規) break-substitution** | — | **完全不在、HIGH** | **新規追加 (12h)** |
| **(新規) staff-affinity 完全版** | G-3 (2h) | HIGH 重要、見積拡大 | **+3h (合計 5h)** |
| **(新規) line-break permission 3-tier** | — | ヒューリスティック | 新規 (2h) |
| **(新規) FileMissing citation 一括修正** | — | 74件の参照ズレ | 新規 (1h) |
| **(新規) MultiMeasureRest ファミリー** | — | 4 grob 不在 | 新規 (6h) |
| **(新規) Fingering / FingeringColumn** | — | 不在 | 新規 (3h) |
| **(新規) LedgerLineSpanner** | — | 不在 | 新規 (2h) |
| **(新規) SpanBar / SpanBarStub** | — | 多段譜未対応 | 新規 (4h) |
| **(新規) BarNumber** | — | 不在 | 新規 (2h) |
| **(新規) RepeatTie / LaissezVibrerTie / TieColumn** | — | 不在 | 新規 (5h) |
| **(新規) LyricExtender / LyricSpace / StanzaNumber** | — | 不在 | 新規 (4h) |
| **(新規) TupletNumber 独立 grob** | — | 確認要 | 新規 (1-2h) |
| **(新規) Editorial Accidentals** | I-3 内 | スタブ | 切り出し (3h) |

工数差分: V2 = 46h, V3 = **78-82h** (新規タスクの方が多い)

---

## Phase G': 垂直レイアウト精度 (削減版、推定 8h)

V2 G の中で実装済を除外。

### G-2. outside-staff-priority stacking (3h)
- **問題**: 譜表外グロブの垂直積み上げ順が不定
- **影響**: 複数の外部要素が重なった際の配置が LP と異なる
- **ファイル**: `MultiStaffLayouter.cs`, `OutsideStaffStacker.cs`
- **参照**: `lily/axis-group-interface.cc:359-474` (outside_staff_priority)
- 状態: V2 から変更なし

### G-3'. staff-affinity 完全実装 (V2 G-3 の拡張、5h)
- **問題**: lyrics / dynamics / figured bass が target staff に吸着しない
- **作業**:
  1. `Staff.StaffAffinity` を `Direction (UP/DOWN/CENTER) + TargetStaff` に拡張
  2. `MultiStaffLayouter.CalculateSystemSpacing()` で affinity 行を spaceable staff 間距離に **割り込ませる** (LP `align-interface.cc:240-252` 準拠)
  3. `non-staff-related-staff-spacing` / `non-staff-unrelated-staff-spacing` 使い分けロジック
- **参照**: `lily/align-interface.cc:240-252`, `lily/page-layout-problem.cc:1174-1182`
- **HIGH severity** (Phase 1-4 audit 確定)

### G-4. pure height estimation (V2 G-4、状態確認のみ、0-2h)
- LilySharp の `MultiStaffLayouter.CalculatePureSystemHeight` で **代替実装あり** (Phase 1-4 audit)
- LP `axis-group-interface.cc:138-173` との挙動一致を確認するテストを追加

---

## Phase H': 水平スペーシング精度 (V2 H、+1新規、推定 11h)

### H-1. Multi-voice shortest_playing_duration tracking (V2 H-1、再見積、2-3h)
- **更新**: duration→space formula は **Faithful (Phase 1-4)**。**多声部での `shortest_playing` 集約のみ**残課題
- **作業**: 各 musical column で全声部の current duration を集計、min を取得
- **参照**: `lily/spacing-spanner.cc:266-310` (musical_column_spacing 内 shortest_playing_duration)

### H-2. Column spacing strict_note_spacing モード (V2 H-2、変更なし、2h)
### H-3. Separating group padding refinement (V2 H-3、変更なし、2h)
### H-4. Grace note spacing dynamics (V2 H-4、変更なし、2h)
### H-5. Break alignment order (V2 H-5、変更なし、2h)

### H-6. (新規) Line-break permission 3-tier 階層 (2h)
- **問題**: `KnuthPlassBreaker` が permission を二値処理 (Forbid skip / Force skip-spanning)。LP は `min_permission()` で 3 段階集約
- **影響**: `\noBreak` / `\allowBreak` / `\pageTurn` のセマンティクスが LP と乖離
- **ファイル**: `KnuthPlassBreaker.cs`, `LayoutEngine.cs` (permission propagate)
- **参照**: `lily/constrained-breaking.cc:520-535`, `scm/define-grob-properties.scm` の `line-break-permission`, `page-break-permission`, `page-turn-permission`

---

## Phase I': 音符衝突・配置精度 (削減版、推定 6h)

V2 I のうち I-1/I-2/I-4/I-5 は実装済を確認。残るのは I-3 のみ + editorial の切出。

### I-3. Accidental skyline collision + editorial 統合 (V2 I-3 の拡張、5-6h)

**Sub-task I-3a: BBox→Skyline (3h)**
- 現状 BBox + Skyline.FromBox。LP は glyph metrics (font メトリクス) 由来 skyline
- **作業**: Bravura font metadata (or LP Emmentaler) から各 accidental glyph の skyline を生成
- **参照**: `lily/accidental-placement.cc:254-301` (set_ape_skylines)

**Sub-task I-3b: Editorial / Cautionary / Suggestion accidental 完全レンダリング (3h)** ← **新規切り出し**
- 現状 `IsEditorial=false` スタブ
- **作業**:
  1. `AccidentalSuggestion`, `AccidentalCautionary` grob 定義の cs 化
  2. parenthesized rendering (small parentheses 描画)
  3. smaller-size font factor (LP は 0.6 倍)
- **参照**: `lily/accidental.cc:130-166`, `scm/define-grobs.scm:AccidentalCautionary`/`AccidentalSuggestion`

---

## Phase J': ページ最適化・自動化 (V2 J、推定 14h、変更なし)

V2 と同じ J-1〜J-5。
- J-1 Hara-kiri (4h)
- J-2 fixed_force_solution (2h)
- J-3 alignment-distances manual override (3h)
- J-4 Bracket/brace collapse (2h)
- J-5 Footnote heights (3h)

---

## Phase K (新規): break-substitution 実装 (12h)

### K-1. Spanner break-substitution 移植 (HIGH severity, 12h)
- **問題**: 改行をまたぐ slur / tie / beam / hairpin / ottava / volta / text-spanner / glissando が分割されない
- **影響**: multi-line 楽譜で線が途切れる、または計算が破綻する重大な視覚劣化
- **ファイル**: `KnuthPlassBreaker.cs`, `LayoutEngine.cs`, 各 spanner engraver (`SlurEngraver.cs`, `TieEngraver.cs`, `BeamEngraver.cs` 等)
- **参照**: `lily/break-substitution.cc::break_substitute`, `lily/spanner.cc:Spanner::find_broken_piece`
- **作業**:
  1. spanner 抽象 `IBreakable` を C# で導入 (各 spanner grob が `Split(int breakColumnIndex)` を返す)
  2. KnuthPlassBreaker が改行決定後に `Split` を呼んで前段/後段の broken piece を生成
  3. 各 broken piece の bound (LeftEnd / RightEnd) を改行点に再アタッチ
  4. broken piece 用の rendering バリアント (右端が cut-off か / 左端が continuation 印か) 実装
- **テスト**: 段をまたぐ slur, multi-line lyrics + extender, multi-line hairpin, beam continuation

---

## Phase L (新規): grob ファミリー拡張 (推定 27h)

Phase 1-3 で発見した HIGH IMPACT 不在 grob を埋める。優先度順。

### L-1. MultiMeasureRest ファミリー (6h)
- 4 grobs: `MultiMeasureRest`, `MultiMeasureRestNumber`, `MultiMeasureRestScript`, `MultiMeasureRestText`
- **参照**: `lily/multi-measure-rest.cc`

### L-2. Fingering + FingeringColumn (3h)
- **参照**: `lily/fingering-engraver.cc`, `lily/fingering-column.cc`, `lily/script-interface.cc` (派生)

### L-3. RepeatTie / LaissezVibrerTie / TieColumn (5h)
- **参照**: `lily/repeat-tie.cc`, `lily/laissez-vibrer-tie.cc`, `lily/tie-column.cc`

### L-4. SpanBar / SpanBarStub (4h)
- 多段譜のバーライン
- **参照**: `lily/span-bar.cc`, `scm/define-grobs.scm:SpanBar`

### L-5. LedgerLineSpanner (2h)
- 加線
- **参照**: `lily/ledger-line-spanner.cc`

### L-6. BarNumber (2h)
- **参照**: `lily/bar-number-engraver.cc`

### L-7. LyricExtender / LyricSpace / StanzaNumber (4h)
- **参照**: `lily/extender-engraver.cc` (現名), `lily/lyric-extender.cc`, `lily/stanza-number-engraver.cc`

### L-8. TupletNumber 独立 grob 化確認 (1-2h)
- 現状 TupletBracket 内 inline。LP では独立 grob。grob coverage 用に分離

---

## Phase M (新規): Citation hygiene + アーキテクチャ整備 (推定 4h)

### M-1. FileMissing 一括修正 (1h)
74件の参照を `audit/citation_drift.md` に従い:
- `note-collision-interface.cc` → `note-collision.cc` (34件)
- `grace-spacing.cc` → `grace-spacing-engraver.cc` (13件)
- `trill-spanner-engraver.cc` / `glissando-engraver.cc` / `lyric-extender-engraver.cc` → `scm/scheme-engravers.scm` + 関連 .cc (17件)
- `dots.cc` → `dots-engraver.cc` (2件)
- `skyline.hh` → `include/skyline.hh` (2件)
- `spacing-determine-shortest-duration-op.cc` → `spacing-spanner.cc` (6件)

### M-2. SpacingSettings.cs / EngravingRules.cs LP citation 補完 (1h)
- `audit/magic_constants.md` の "値完全一致だが citation 欠落" カテゴリを Green 化

### M-3. PaperSettings.cs / GlyphMetrics.cs ファイル先頭注記強化 (30min)
- 物理定数 / SMuFL Bravura source の明記で誤検知排除

### M-4. SpacingRules.cs Gourlay→LP 引用置換 (1.5h)
- `QuarterNoteWidth=3.6` 等の定数に `lily/spacing-spanner.cc` + `scm/define-grobs.scm:SpacingSpanner` の出典を付与
- 計算ロジック自体は H-1 と統合 (multi-voice tracking)

---

## 推奨実装順序 (severity × 依存)

### Sprint 1 (HIGH severity 解消、22-25h)
1. **M-1 FileMissing 一括修正** (1h, 機械的)
2. **K-1 break-substitution 実装** (12h, 最大インパクト)
3. **G-3' staff-affinity 完全実装** (5h)
4. **H-1 multi-voice shortest_playing_duration** (2-3h)
5. **H-6 line-break permission 3-tier** (2h)
6. **M-2 SpacingSettings citation 補完** (1h)

### Sprint 2 (MED severity + grob 拡張、20-22h)
7. **L-1 MultiMeasureRest ファミリー** (6h)
8. **I-3a Accidental skyline glyph-based** (3h)
9. **I-3b Editorial accidental 完全実装** (3h)
10. **L-2 Fingering** (3h)
11. **L-3 RepeatTie / LaissezVibrerTie / TieColumn** (5h)

### Sprint 3 (Phase H 残 + 多段譜系、~13h)
12. **H-2 strict_note_spacing** (2h)
13. **H-3 Separating group padding** (2h)
14. **H-4 Grace note spacing** (2h)
15. **H-5 Break alignment order** (2h)
16. **L-4 SpanBar** (4h)
17. **L-5 LedgerLineSpanner** (2h)

### Sprint 4 (Phase J ページ最適化 + grob、~17h)
18. **J-1〜J-5** (14h, V2 と同じ)
19. **L-6 BarNumber** (2h)
20. **L-7 LyricExtender 等** (4h)
21. **L-8 TupletNumber 確認** (1-2h)

### Sprint 5 (細部仕上げ、~3h)
22. **M-3, M-4 documentation cleanup** (2h)
23. **G-2 outside-staff-priority** (3h, 既に G-1 が実装済なので G の最後でOK)
24. **G-4 pure height 確認** (0-2h)

---

## 達成目標 (V3)

| マイルストーン | 到達度 | テスト数 |
|---|---|---|
| 現状 (V3 起点 = V2 評価ズレ修正後) | ~62-67% | 1,004+ |
| Sprint 1 完了 | ~70-73% | ~1,070 |
| Sprint 2 完了 | ~74-77% | ~1,100 |
| Sprint 3 完了 | ~77-80% | ~1,140 |
| Sprint 4 完了 | ~80-83% | ~1,180 |
| Sprint 5 完了 | ~82-85% | ~1,200 |

---

## 残存リスク (Phase 4 範囲外、長期課題)

1. **Pango font / Emmentaler 完全模倣** — 現状 Bravura 使用。文字幅 / instrument-name / lyric は ±5-10% 差。SkiaSharp + HarfBuzzSharp 統合が必要、~30h+。
2. **Callback property system** (`before-line-breaking`, `after-line-breaking`, `pure-Y-extent`, `springs-and-rods` 等の callback) — Phase 1-3 で 47 件 absent 確認。LP の `\override Foo.calc-positions = #my-fn` カスタマイズパスが利用不可。完全対応は C# dynamic dispatch 設計の見直しが必要、~50h+。
3. **stencil-integral.cc** 風の精密 extent — 現状 BBox 近似。slur/tie の "曲線部分の食み出し" 計算が粗い。実害は I-3a (accidental skyline) で部分対応するが完全模倣には ~10h。
4. **中世記譜法** (Gregorian/Mensural/Vaticana ligature, Custos, Episema, Divisio) — Phase 1-3 で全 absent 確認。スコア対象として優先度低。out-of-scope 継続。
5. **Tab / FretBoard** — タブ譜と fret diagram (新規 grob ~10件)。ギター譜対応で必要だが現状 out-of-scope。

---

## 検証方法 (V2 から強化)

### 自動検証
1. **単体テスト** (各タスク TDD)
2. **スナップショットテスト** (`LILYSHARP_UPDATE_SNAPSHOTS=1`)
3. **回帰テスト** (1,004+ 全パス維持)
4. **(新) 視覚回帰スクリプト** (`audit/scripts/Compare-Svg.ps1`, Phase 2 で構築): LP 2.24.4 の SVG と数値メトリクス比較。Phase 1 baseline を超えない / Sprint ごとに改善

### 視覚比較
5. **LP リファレンス**: 各テストの .ly を `C:\bin\lilypond-2.24.4\bin\lilypond.exe --svg` で出力、Lily# SVG と並列比較
6. **53 サンプル SVG 再生成**: 各 Sprint 完了後

### 定量比較
7. **note head X 座標差分** (mean / max / p95)
8. **system 上下端 Y 座標差分**
9. **bar line X 差分**
10. **slur / tie 制御点差分**

### 版差ノイズ
- LP 2.24.4 (binary) ↔ 2.25.35 (source) ノイズフロアを Phase 2 で記録。ベースラインから "Lily# 由来差" を切り分け

---

## LP ソース参照インデックス (V3)

| タスク | LilyPond ソースファイル | 行番号 | 状態 |
|---|---|---|---|
| G-2 | axis-group-interface.cc | 359-474 | 未実装 |
| G-3' | align-interface.cc + page-layout-problem.cc | 240-252 + 1174-1182 | 未実装 (HIGH) |
| G-4 | axis-group-interface.cc | 138-173 | 実装済 (確認要) |
| H-1 | spacing-spanner.cc | 266-310 | duration 部実装済、multi-voice 部未実装 |
| H-2 | note-spacing.cc | 229-264 | 未実装 |
| H-3 | separation-item.cc | 49-70 | 未実装 |
| H-4 | grace-spacing-engraver.cc | 1-120 | 未実装 |
| H-5 | break-alignment-interface.cc | 1-200 | 未実装 |
| H-6 | constrained-breaking.cc | 520-535 | ヒューリスティック |
| I-3a | accidental-placement.cc | 254-301 | 部分実装 (BBox) |
| I-3b | accidental.cc | 130-166 | スタブ |
| J-1 | hara-kiri-group-spanner.cc | 1-100 | 未実装 |
| J-2 | page-layout-problem.cc | 808-823 | 未実装 |
| J-3 | page-layout-problem.cc | 656-717 | 未実装 |
| J-4 | system-start-delimiter.cc | 127-129 | 未実装 |
| J-5 | page-layout-problem.cc | 186-310 | 未実装 |
| K-1 | break-substitution.cc + spanner.cc | 全体 | **完全不在 (HIGH)** |
| L-1 | multi-measure-rest.cc | 1-200 | 未実装 |
| L-2 | fingering-engraver.cc + fingering-column.cc | 全体 | 未実装 |
| L-3 | repeat-tie.cc / laissez-vibrer-tie.cc / tie-column.cc | 全体 | 未実装 |
| L-4 | span-bar.cc | 全体 | 未実装 |
| L-5 | ledger-line-spanner.cc | 全体 | 未実装 |
| L-6 | bar-number-engraver.cc | 全体 | 未実装 |
| L-7 | extender-engraver.cc + lyric-extender.cc + stanza-number-engraver.cc | 全体 | 未実装 |
| L-8 | tuplet-number.cc | 全体 | 確認要 |
