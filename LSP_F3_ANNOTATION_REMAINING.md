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
