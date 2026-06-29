# F3 次セッション開始プロンプト

> 新しい Claude Code セッションの最初に、下の `---` 以降の本文をそのまま貼る。
> 現在の最前線は **B（whole-layout reuse のための「render 側 data-pos 解決」）**。S0〜S5-3c は完了済み。

---

LilySharp の F3（意味解析〜レイアウトの増分化）を再開する。現在地は **B = whole-layout reuse の基盤づくり**。
まず現状を読んでから、次の一手（MusicMark 移行）に着手して。

## 最初に読む（この順で）
1. `C:\MyProj\LilySharp\docs\DEV_BUGFIX_WORKFLOW.md` の §0（アドホック禁止）と §19（F3 引き継ぎ・運用）。
2. `C:\MyProj\LilySharp\LSP_INCREMENTAL_IMPROVEMENT_PROPOSAL.md`（親提案・全体像 F0〜F4）。
3. `C:\MyProj\LilySharp\LSP_F3_QUERY_GRAPH_DESIGN.md` の §0.5（検証済み前提）と S-stage 進捗。
4. **`C:\MyProj\LilySharp\LSP_F3_ANNOTATION_REMAINING.md`** ← **今回の本丸**。B の設計・残型・**MusicMark 移行の具体手順**・B-2 復活手順が全部ここにある。

## 現在地（2026-06-30）
- リポジトリ `C:\MyProj\LilySharp`、ブランチ `f3-incremental` = `origin/master`（最新 tip）。全テスト **1843 passed / 3 skipped**。作業ツリー clean。
- 着手前に必ず `dotnet test LilySharp.Tests`（ripple MCP の pwsh で）でベースライン確認。

### 完了済み（コミット済・push 済）
- **S0〜S4b**: 設計接地／差分安全網／`MeasureContext` 鎖（key/time/clef）／行分割ゲート明示化／`IncrementalCompiler`（gate 不変なら行分割 DP skip、増分==フル証明）。
- **S5a**: `MeasureContentKey`（解決済 Items の reflection ハッシュ、位置非依存・編集局所性証明）。`Compute(Score)` / `Compute(MultiStaffScore)`。
- **S5-1**: 編集レイテンシ定量化。律速は layout（内訳 per-system spring 61% / skyline がその中で支配的 / 注釈グローバル 39%）。
- **S5-2**: per-measure 完全キー（side-table を MeasureIndex でバケット畳込＋entry context）。
- **S5-3a/b/c**: `SystemLayoutCache`＝**per-system の spring 解＋skyline をキャッシュ**。内容不変な段を再利用、変段のみ再計算。
  **単一譜・多譜表とも net win**（warm 幅不変編集 multi 中央値 9.7ms、~36%減）。増分==フル証明・byte-identical。
- **B-1**（`28edb88`,`69c4db5`,`8afaf0e`）: **render 側 data-pos 解決の機構＋11型移行**。各注釈 `*Layout` に `SourceIndex`（score
  side-table への位置非依存な参照）を持たせ、`SharedRenderer.RenderTo` 冒頭の `ResolveDataPos` が生 score から data-pos を再導出。
  通常レンダは snapshot byte-identical、reuse 時は編集後の正しい位置になる。**11型（Dynamic, Articulation, Arpeggio, CustomText,
  FiguredBass, VoltaBracket, TupletBracket, PercentRepeat, GraceNote, ChordName, TrillSpanner）は頑健性を実証済み**
  （`MigratedDataPosTests` が位置ずれ編集下で正しく再導出されることを全 fixture で証明）。

## 次にやること（B の続き ＝ whole-layout reuse を実際に動かす）
**`LSP_F3_ANNOTATION_REMAINING.md` の「MusicMark 移行の具体設計」と「B-2 復活手順」をそのまま実行する。**

1. **MusicMark 移行（最優先 unblock）**: `section X` ラベルは全スコアに必ず生成され、未移行のため whole-layout reuse が
   どの実スコアでも発火しない。`MusicMarkEngraver` を migrate（`allMarks` の index を `SourceIndex` に、構築点2箇所に threading、
   `BuildAllMarks` を public 化、`ResolveDataPos` で再構築解決）。tempo マークは data-pos を出さず無害、section ラベルは
   `measure.SectionLabelPosition` から、実マークは `score.MusicMarks` から解決。**`MigratedDataPosTests` に MusicMark を足せば
   全 fixture が即検証**（section ラベルは全 fixture にある）。
2. **B-2 復活**: `IncrementalCompiler` に「content key 全一致＋gate 不変なら `_cachedLayout` を丸ごと再利用、`ReuseSafe`（未移行で
   data-pos を出す8アレイ＝Lyric/Hairpin/Ottava/Glissando/Fingering/MusicMark/TextSpanner/Pedal が全 empty）で gate」を実装
   （前回試作・revert 済。MusicMark 移行後は `ReuseSafe` から MusicMark を外す）。**増分==フルで reuse 発火を実証**。
3. **残型を順次移行**（Lyric → note 由来 Glissando/Fingering/TieVariant → detected Hairpin/Ottava/Pedal/Ornament/TextSpanner）。
   各移行ごとに `MigratedDataPosTests` に足して検証し、`ReuseSafe` から外す＝eligible なスコアが広がる。

## 検証
- `MigratedDataPosTests`（B-1 の頑健性ガード）: 移行型を足したら必ずここに追加（位置ずれ編集下の resolution 正当性を実証）。
- snapshot（通常レンダ byte-identical）＋ `IncrementalCompilerTests`/`SystemLayoutCacheTests`（増分==フル）。

## 厳守事項（F3 運用・通常ルールを上書き）
- ブランチ `f3-incremental` はユーザー承認済み。各段は **ビルド緑＋全テスト緑＋（純 substrate 時）byte-identical** を確認後に
  `git branch -f master f3-incremental`（ff）＋`git push origin master` で段階マージ。
- **byte-identical は純 substrate の既定であって目的ではない（正しさ＞現状維持）。** 出力がより正しくなる変更は歓迎（snapshot を意図的に貼り直す）。
- シェルは **ripple MCP の `execute_command`(pwsh)**。PowerShell ツール / Bash ツールは使わない。特殊文字を含むファイルは `Write` で作る。
- コミットは1論点1コミット、`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`。lilypond 比較が要る時は §3 のデッドロック回避起動。
- master 直やブランチ新規作成は勝手にやらない（F3 は既存 `f3-incremental` で継続）。大きな方向転換は提案して確認。
- 未追跡 `.md`（`AI_POSITIONING_HANDOFF.md` / `RELEASE_BLOCKERS.md`）は別件・温存。

まず上記の読む物（特に `LSP_F3_ANNOTATION_REMAINING.md`）を読み、ベースラインを確認し、**MusicMark 移行**に着手して。
