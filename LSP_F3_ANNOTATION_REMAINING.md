# F3 注釈パス memoization — 残作業メモ

> 作成 2026-06-30。親文書: `LSP_F3_QUERY_GRAPH_DESIGN.md`（§0.5 と S-stage 進捗）、`docs/DEV_BUGFIX_WORKFLOW.md` §19。
> 行番号は目安。着手時は識別子で再 grep すること。

## 現在地

S5-3a/c で **per-system の spring 解（`LayoutMeasures`）＋ skyline（`BuildSystemSkylines`）をキャッシュ済み**
（`SystemLayoutCache`、`IncrementalCompiler` が override 無しの単一/多譜表に設置）。内容不変な段を再利用し、
編集で変わった段のみ再計算。増分==フル証明・byte-identical。

残るのは **LayoutEngine の注釈パス（layout の ~39%）**＝ beams/ties/slurs/glissandos ＋
`CalculateAnnotationLayouts`（dynamics/articulations/lyrics/marks/tuplet/volta/…）＋ voice-collision offsets。
**これは現状どの編集でも毎回フル実行**される。

## 調査結論（2つの道。いずれも今回は保留・revert 済み）

### 1. per-system 注釈メモ化 — 構造的に不可（engraver 大改修が要る）

- `ElementCoordinator.LayoutBeams/LayoutTies/LayoutSlurs/LayoutGlissandos`（`Svg/Layout/ElementCoordinator.cs`）は
  `systemsArray` を一括で受け、**全段にまたがる flat 配列**を返す。per-system 起動の口は無い。
- tie/slur solver は `existingTies` / `existingSlurs`（既配置の全ピース）を **段を跨いだ衝突回避**に渡す
  （`TieFormattingProblem` / `SlurScoringProblem` 呼び出し、`existingTies: tieLayouts`）。1段だけ独立計算すると
  衝突解が変わりうる＝per-system 分離は unsound になりうる。
- `CalculateAnnotationLayouts`（`LayoutEngine.cs` ~825-1037）の `OutsideStaffStacker.StackBelow/AboveStaff` は
  **スコア全体の積層**（全 dynamics/hairpins/marks を見て再配置）。per-system に切れない。
- 段跨ぎ broken spanner（tie/slur）は `SpannerBreakSubstitution.Split` で per-system ピースに分割され各ピースは1段に属す。
  出力は `MeasureIndex` で段ごとに**分割可能**だが、計算自体は分割できない（compute-then-partition では節約ゼロ）。
- → やるなら solver を衝突回避から切り離す／段ごとにインクリメンタルに解く設計への大改修。

### 2. whole-layout 再利用（内容不変時に ScoreLayout 丸ごと再利用）— 幾何的に健全だが data-pos で詰まる

- 内容不変の編集（空白/コメント/カーソル）では layout 幾何は**完全に位置非依存**。
  実証: `full(src)` と `full("\n"+src)` は **data-pos 属性を除けば byte 完全一致**（grammar-tour 88136==88136）。
- → per-measure content key が全一致 ＋ 行分割 gate 不変なら、`LayoutEngine.Layout` を丸ごと skip して
  前回 ScoreLayout を再利用でき、**注釈39%含め layout 100% を省ける**（render は new score で再実行）。
- **ブロッカー**: 一部の annotation grob が **data-pos 用に source 位置を layout に焼き込む**
  （例 `GlissandoLayout.SourcePosition` ほか annotation layout）。ScoreLayout を再利用すると data-pos が
  編集 delta 分だけ stale になり editor の click-to-source がずれる（byte 非一致）。
- 試作（`IncrementalCompiler` に `_cachedLayout` ＋ content-key 厳密一致で reuse）→ 上記で unsound 判明 → **revert 済み（未コミット）**。

## 次にやるなら（選択肢）

- **A. whole-layout 再利用 ＋ data-pos 後処理 remap**（小〜中）
  ScoreLayout を再利用し、render 後 SVG の `data-pos="N"` を「前回 layout 時のツリー→現ツリー」の位置写像で remap
  （編集列の累積 delta で N→N'）。利点: 内容不変編集で layout 100% skip。リスク: 写像を編集列に厳密追従させる必要、
  誤ると click-to-source がずれる。
- **B. render が annotation の source 位置を new score から引く**（中）
  annotation layout に source 位置を焼き込まず、render 時に new score の対応 grob（`MeasureIndex`/`ItemIndex` 経由）から
  data-pos を引く。これで ScoreLayout が完全に位置非依存になり whole-layout 再利用が無条件 byte-identical に。
  利点: 設計的に綺麗。リスク: 全 annotation renderer の data-pos 取得経路を score 参照へ統一する改修（波及中）。
- **C. per-system 注釈の段ごと化**（大）
  上記1の大改修。solver を「自段のピース＋上流確定段の境界」だけ参照する形に再設計し、annotation も per-system キャッシュ
  （key = 段の content key slice ＋ 段Y ＋ 段X）。**width-changing 編集でも注釈を変段のみ再計算**できる最大効果だが最難・最高リスク。

おすすめ順は **B → A → C**（B が最も健全で whole-layout 再利用を恒久 byte-identical 化、A は B の前の安価な近道、C は本丸だが大工事）。

## 効果の見積もり

- S5-1 内訳（grammar-tour 1編集, cold）: layout 7.7ms のうち **注釈 = ~39%（prelim 18% ＋ final 21%）**。
  prelim は extent 推定用に算出後 discard。skyline は per-system の支配コスト（spring の約3倍）で S5-3c で対応済み。
- **A / B は内容不変編集にのみ効く**（layout 全 skip）。**C は全編集に効く**（変段のみ再計算）が大改修。

## ポインタ（着手時に再 grep）

- `LayoutEngine.Layout(MultiStaffScore)`: `LilySharp.Core/Svg/Layout/LayoutEngine.cs`
  — 親 prelim 注釈パス ~312-350 / `CreatePages` ~352 / final 注釈 ~356-465 / `CalculateAnnotationLayouts` ~825-1037 /
  `EnrichExtentsWithAnnotationProtrusions` ~716-827。
- engraver: `ElementCoordinator.cs` — `LayoutBeams` 186- / `LayoutTies` 566- / `LayoutSlurs` 752- / `LayoutGlissandos` 861-。
- 段跨ぎ分割: `SpannerBreakSubstitution`。
- 既存キャッシュ: `SystemLayoutCache.cs`（spring ＋ skyline の2相、typed memo）/ `IncrementalCompiler.cs`（gate・content key・cache 設置）。
- per-measure 識別子: `MeasureContentKey.cs`（`Compute(Score)` / `Compute(MultiStaffScore)`）。

## B 実装の進捗（2026-06-30）

B（render が data-pos を score から引く＝layout を完全に位置非依存化）に着手。**機構は確立・実証済み**だが、
注釈の data-pos 出力源は当初想定より多く **計28箇所/約24型**で、いくつかは bespoke。

### 確立した機構（commit `28edb88` + `69c4db5`、snapshot byte-identical）
- 各注釈 `*Layout` struct に `int SourceIndex = -1`（その注釈が来た score side-table の index＝位置非依存な参照）。
- `SharedRenderer.RenderTo` 冒頭で `ResolveDataPos(layout, score)`＝各注釈の `SourcePosition` を**生 score から `SourceIndex` 経由で再導出**。
  通常レンダは同値（snapshot 一致）、reuse 時は編集後 score の新位置になる。`ResolveArr<T,TItem>` が共通ヘルパ。
- `SourceIndex = -1` 既定で、layout struct を直接組むユニットテストは無改修で通る。
- **移行済み11型（MultiStaffScore side-table 直結）**: Dynamic, Articulation, Arpeggio, CustomText, FiguredBass,
  VoltaBracket, TupletBracket, PercentRepeat, GraceNote(+tab grace), ChordName, TrillSpanner。

### data-pos を出す未移行アレイ＝8つ（`ScoreLayout` 上）。reuse はこれら全 empty 時のみ byte-identical
`LyricLayouts, HairpinLayouts, OttavaBracketLayouts, GlissandoLayouts, FingeringLayouts, MusicMarkLayouts,
TextSpannerLayouts, PedalBracketLayouts`。**beams/ties/slurs/barnumber/stanza/tievariant/mmrest/partcombine/crossstaff は
data-pos を出さない**（`gc.Source` 無し）→ reuse を妨げない（確認済み）。

### 重大ブロッカー：MusicMark（セクションラベル）
`section X` ラベルは `MusicMarkEngraver` が `MergeSectionLabels(score.MusicMarks, measures) + MergeTempoMark` で
**全スコアに1つ以上生成**する（実測 MusicMark=1）。よって **MusicMark を移行しない限り whole-layout reuse は
どの実スコアでも発火しない**。これが B-2 の最優先 unblock。

### B-2（whole-layout reuse）
`IncrementalCompiler` に「content key 全一致＋gate 不変なら `_cachedLayout` を丸ごと再利用、`ReuseSafe`(上記8アレイ empty)で
gate」を実装→**試作したが、MusicMark で全スコア gate off＝死にコードのため未コミット（revert 済）**。MusicMark 移行後に復活させる。

### 残型の移行方針（次の道）
- **MusicMark**（最優先）: `allMarks`（merge）を render 時に再構築し index 解決。セクションラベルは `measure.SectionLabelPosition`
  （再 collect で新値）から、実マークは `score.MusicMarks` から。merge 順は content 不変で安定。`Score?` vs `MultiStaffScore` の
  型差に注意（tempo 等）。
- **Lyric**: verse グルーピングを通る→`score.Lyrics` への index を layout に threading。
- **Glissando/Fingering/TieVariant**（note 由来）: note の `SourcePosition` を `(MeasureIndex, ItemIndex)`＋多譜表 staff 経路で
  measures から引く **note-index resolver** が要る。
- **Hairpin/Ottava/Pedal/Ornament/TextSpanner**（detected, score 非保持）: 検出元 item を `ScoreLayout` に載せて render 到達可能に。
- 各移行後、`ReuseSafe` から該当アレイを外し、B-2 を復活＋増分==フルで reuse 発火を実証。
