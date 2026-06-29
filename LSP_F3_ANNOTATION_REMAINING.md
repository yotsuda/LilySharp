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

## B 実装の進捗（2026-06-30 更新）

B（render が data-pos を score から引く＝layout を完全に位置非依存化）。**機構は確立・実証済み**。
**MusicMark 移行（`8e8755e`）＋ B-2 whole-layout reuse（`706d36d`）まで完了・push 済み**。
通常スコア（lyrics/hairpin 等を含まない override 無しスコア）の内容不変編集で **layout 100% skip（reuse 発火）が
増分==フルで実証済み**（`IncrementalCompilerTests.ContentUnchangedEdit_ReusesWholeLayout_AndMatchesFull`）。
注釈の data-pos 出力源は当初想定より多く **計28箇所/約24型**で、いくつかは bespoke。

### 完了（2026-06-30）
- **MusicMark 移行**（`8e8755e`）: `MusicMarkLayout.SourceIndex`＝`BuildAllMarks()` への index。`BuildAllMarks(musicMarks,
  measures, int? tempo)` を public 化、`SharedRenderer.ResolveDataPos` で再構築解決。`MigratedDataPosTests` に MusicMark 追加
  （section ラベルが位置ずれ編集下で正しく再導出されることを全 fixture で実証、tempo は data-pos 0 で skip）。snapshot byte-identical。
- **B-2 whole-layout reuse**（`706d36d`）: `IncrementalCompiler` が「line-break gate 不変＋content key 全一致＋global key
  (Title/Composer/Tempo) 一致＋`ReuseSafe`（下記7アレイ empty）」で `_cachedLayout` を丸ごと再利用、`LayoutEngine.Layout`
  を完全 skip。`LastEditReusedLayout` で発火可視化。健全性=content-key 一致⟹幾何位置非依存で同一、data-pos は render 時に
  新 score から再導出（header data-pos も新 score から）。**global key が load-bearing**（title 編集は reuse 拒否＝専用テストで実証）。

### 確立した機構（commit `28edb88` + `69c4db5`、snapshot byte-identical）
- 各注釈 `*Layout` struct に `int SourceIndex = -1`（その注釈が来た score side-table の index＝位置非依存な参照）。
- `SharedRenderer.RenderTo` 冒頭で `ResolveDataPos(layout, score)`＝各注釈の `SourcePosition` を**生 score から `SourceIndex` 経由で再導出**。
  通常レンダは同値（snapshot 一致）、reuse 時は編集後 score の新位置になる。`ResolveArr<T,TItem>` が共通ヘルパ。
- `SourceIndex = -1` 既定で、layout struct を直接組むユニットテストは無改修で通る。
- **移行済み11型（MultiStaffScore side-table 直結）**: Dynamic, Articulation, Arpeggio, CustomText, FiguredBass,
  VoltaBracket, TupletBracket, PercentRepeat, GraceNote(+tab grace), ChordName, TrillSpanner。

### data-pos を出す未移行アレイ＝7つ（`ScoreLayout` 上）。reuse はこれら全 empty 時のみ byte-identical（`ReuseSafe`）
`LyricLayouts, HairpinLayouts, OttavaBracketLayouts, GlissandoLayouts, FingeringLayouts,
TextSpannerLayouts, PedalBracketLayouts`（**MusicMark は移行済みで除外**）。**beams/ties/slurs/barnumber/stanza/
tievariant/mmrest/partcombine/crossstaff は data-pos を出さない**（`gc.Source` 無し）→ reuse を妨げない（確認済み）。
PedalBracketLayouts は常に empty（pedal は text mark で描画）。**この7アレイの検出元は全て content key に被覆**
（Lyric←score.Lyrics / Hairpin←musicMarks+dynamics / Ottava・TextSpanner←musicMarks / Glissando・Fingering←note items）
＝content-key 一致なら空のまま、ゆえに「cached layout が空」の検査＝「編集後 score も空」の検査と等価（健全）。

### MusicMark（セクションラベル）＝移行済み（旧ブロッカー解消）
旧: `section X` ラベルが全スコアに1つ以上生成され、未移行のため reuse が死にコードだった。→ `8e8755e` で移行し unblock 済み。
B-2（`706d36d`）も復活＝override 無しの通常スコアで内容不変編集の reuse が増分==フルで発火する。

### 残型の移行方針（次の道）— `ReuseSafe` を1つずつ外して eligible を広げる
各型を移行するたびに `MigratedDataPosTests` で位置ずれ編集下の resolution を実証し、`IncrementalCompiler.ReuseSafe` から
該当アレイを外す（＝lyrics/hairpin 等を含むスコアでも reuse 発火）。
- **Lyric**（次の最優先）: verse グルーピングを通る→`score.Lyrics` への index を layout に threading。
- **Glissando/Fingering/TieVariant**（note 由来）: note の `SourcePosition` を `(MeasureIndex, ItemIndex)`＋多譜表 staff 経路で
  measures から引く **note-index resolver** が要る。
- **Hairpin/Ottava/Pedal/Ornament/TextSpanner**（detected, score 非保持）: 検出元 item を `ScoreLayout` に載せて render 到達可能に。
- 各移行後、`ReuseSafe` から該当アレイを外し、増分==フルで reuse 発火を実証（lyrics/hairpin 入りスコアでも）。

### B-1 の頑健性は実証済み（commit `8afaf0e`）
`MigratedDataPosTests`：全 fixture を「元」vs「先頭改行コピー（全 offset +1）」でレイアウトし、移行済み各アレイで
**「cached の `SourceIndex` を編集後 score に解決した値 == 編集後フルレイアウトの値」かつ「値が実際に変化」**をアサート。
10型を fixture で網羅（カバレッジ強制）＋CustomText はエングレーバ直接テスト。**11型すべて pass**＝位置がずれる編集下で
正しく再導出される＝reuse 用途での正当性を証明（snapshot は同一ツリーの同値しか保証しないため、この追加が肝）。

### MusicMark 移行の具体設計（**実装済み `8e8755e`** — 以下は実行記録、12型に拡大）
`MusicMarkEngraver`：
1. `MusicMarkLayout` に `int SourceIndex = -1`（**allMarks への index**）。
2. `Calculate` 内 `foreach (var mark in allMarks)` を**indexed for** にし、`markEntries` を `(Mark, X, SourceIndex)` の3-tuple化。
   GroupBy/OrderBy はそのまま3-tupleを保持。**2つの構築点（above ~227 / below ~296）**で `var (mark, x, si) = ...` から `si` を渡す。
3. `MergeSectionLabels` + `MergeTempoMark` を **public `BuildAllMarks(musicMarks, measures, int? tempo)`** に括り出す
   （`MergeTempoMark` の `Score?` → `int? tempo` に変更）。
4. `SharedRenderer.ResolveDataPos`：`var allMarks = MusicMarkEngraver.BuildAllMarks(score.MusicMarks,
   score.PrimaryContentStaff.PrimaryVoice.Measures, score.Tempo);` で再構築し `ResolveArr(layout.MusicMarkLayouts, allMarks, …)`。
   - tempo マークは `SourcePosition==0`→`gc.Source` が NullScope＝data-pos を出さない＝reuse に無害。
   - section ラベルは `measure.SectionLabelPosition`（再 collect で新値）、実マークは `score.MusicMarks` から解決。
5. `MigratedDataPosTests` に `MusicMark` を追加（section ラベルは全 fixture にある＝即カバー）＝移行の正当性を即実証。
6. その後 B-2（`IncrementalCompiler` の `_cachedLayout` 再利用＋`ReuseSafe`）を復活し、`ReuseSafe` から MusicMark を外す。
   → **`706d36d` で完了**。global key (Title/Composer/Tempo) guard も追加（title 等の score-global 変更は reuse 拒否）。
   「lyrics/hairpin 等を含まない通常スコア」で内容不変編集の reuse 発火＋増分==フルを `IncrementalCompilerTests` で実証済み。
