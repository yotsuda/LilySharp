LilySharp の F3（意味解析〜レイアウトの増分化）を再開する。現在地は **B = whole-layout reuse が稼働中**。
MusicMark 移行（`8e8755e`）＋ B-2 whole-layout reuse（`706d36d`）＋ Lyric 移行（`7d1d436`）まで完了・push 済み。
まず現状を読んでから、次の一手（**note 由来 Glissando/Fingering/TieVariant 移行＝note-index resolver を作り `ReuseSafe` から外す**）に着手して。

## 最初に読む（この順で）
1. `C:\MyProj\LilySharp\docs\DEV_BUGFIX_WORKFLOW.md` の §0（アドホック禁止）と §19（F3 引き継ぎ・運用）。
2. `C:\MyProj\LilySharp\LSP_INCREMENTAL_IMPROVEMENT_PROPOSAL.md`（親提案・全体像 F0〜F4）。
3. `C:\MyProj\LilySharp\LSP_F3_QUERY_GRAPH_DESIGN.md` の §0.5（検証済み前提）と S-stage 進捗。
4. **`C:\MyProj\LilySharp\LSP_F3_ANNOTATION_REMAINING.md`** ← **今回の本丸**。B の設計・残型・**MusicMark 移行の具体手順**・B-2 復活手順が全部ここにある。

## 現在地（2026-06-30 更新）
- リポジトリ `C:\MyProj\LilySharp`、ブランチ `f3-incremental` = `origin/master`（最新 tip `7d1d436`）。全テスト **1847 passed / 3 skipped**。作業ツリー clean。
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
- **MusicMark 移行**（`8e8755e`）: 12型目。`MusicMarkLayout.SourceIndex`＝`BuildAllMarks()` の index、`BuildAllMarks` public 化、
  `ResolveDataPos` で再構築解決。section ラベルを位置ずれ編集下で正しく再導出することを `MigratedDataPosTests` で実証。snapshot byte-identical。
- **B-2 whole-layout reuse**（`706d36d`）: `IncrementalCompiler` が「line-break gate 不変＋content key 全一致＋global key
  (Title/Composer/Tempo) 一致＋`ReuseSafe`（残アレイ empty）」で `_cachedLayout` を丸ごと再利用、`LayoutEngine.Layout` を完全 skip。
  override 無し通常スコアの内容不変編集で reuse 発火を **増分==フルで実証**（`LastEditReusedLayout`）。global key guard が load-bearing。
- **Lyric 移行**（`7d1d436`、13型目）: `LyricLayout.SourceIndex`＝score.Lyrics への index、`LyricEngraver` の (row,verse) GroupBy に
  元 index を threading、`ResolveDataPos` で nested `Item.SourcePosition` を再導出。`LyricLayouts` を `ReuseSafe` から外し、
  **歌詞入りスコアでも reuse 発火**を `ContentUnchangedEdit_WithLyrics` で実証。残 `ReuseSafe`=6アレイ
  （Hairpin/Ottava/Glissando/Fingering/TextSpanner/Pedal）。

## 次にやること（B の続き ＝ `ReuseSafe` を1つずつ外して reuse eligible を広げる）
**`LSP_F3_ANNOTATION_REMAINING.md` の「残型の移行方針」をそのまま実行する。** 残6アレイの検出元は全て content key 被覆済み
（健全性の論拠は同ファイル参照）。各型を移行→`MigratedDataPosTests` に追加→`IncrementalCompiler.ReuseSafe` から該当アレイを外す。

1. **note 由来 Glissando/Fingering/TieVariant（次の最優先・新機構）**: side-table と違い data-pos は **start note の
   `SourcePosition`** 由来（`GlissandoDetector`=`score.Voice.Measures[mi].Items[ii]` の `NoteItem`）。検出は **per-staff**
   （`ElementCoordinator.LayoutGlissandos(staffScore,…,staffIndex)`）で layout は現状 StaffIndex 非保持→ layout に
   `(StaffIndex, MeasureIndex, ItemIndex)` を載せ、`ResolveDataPos` に **note-index resolver**（新 score の
   `staff.PrimaryVoice.Measures[mi].Items[ii]` から `SourcePosition` を引く）を追加。詳細は `LSP_F3_ANNOTATION_REMAINING.md`。
2. **detected Hairpin/Ottava/Pedal/Ornament/TextSpanner**（score 非保持、musicMarks/dynamics 由来）: 検出元 item を
   `ScoreLayout` に載せて render 到達可能に。各移行ごとに hairpin 入りスコアでも reuse 発火を増分==フルで実証。

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
