LilySharp の F3（意味解析〜レイアウトの増分化）。現在地は **B = whole-layout reuse が稼働中、注釈移行は完了**。
data-pos を出す全注釈型（18型）を移行済みで `ReuseSafe` は実質空（Pedal のみ）＝override 無しスコアほぼ全てで内容不変編集の reuse が発火。
**未 push のローカルコミットあり（`e56dc23`・`1c41f8c`、push 保留指示中）。master=origin/master=`0dfec86` のまま。push 解禁後に `git branch -f master f3-incremental` + push。**
次は **(a) reuse 実効の benchmark 計測**、または **(b) 道 C＝width-changing 編集にも効かせる per-system 注釈の段ごと化（最難・大工事）**。まず現状を読んで方針を確認して。

## 最初に読む（この順で）
1. `C:\MyProj\LilySharp\docs\DEV_BUGFIX_WORKFLOW.md` の §0（アドホック禁止）と §19（F3 引き継ぎ・運用）。
2. `C:\MyProj\LilySharp\LSP_INCREMENTAL_IMPROVEMENT_PROPOSAL.md`（親提案・全体像 F0〜F4）。
3. `C:\MyProj\LilySharp\LSP_F3_QUERY_GRAPH_DESIGN.md` の §0.5（検証済み前提）と S-stage 進捗。
4. **`C:\MyProj\LilySharp\LSP_F3_ANNOTATION_REMAINING.md`** ← **今回の本丸**。B の設計・残型・**MusicMark 移行の具体手順**・B-2 復活手順が全部ここにある。

## 現在地（2026-06-30 更新）
- リポジトリ `C:\MyProj\LilySharp`、ブランチ `f3-incremental`（**`0dfec86`=origin/master 以降は未 push**、`git log origin/master..` で確認）。全テスト **1851 passed / 3 skipped**。作業ツリー clean（未追跡の別件 .md 2本除く）。
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
- **Lyric 移行**（`7d1d436`、13型目）: `LyricLayout.SourceIndex`＝score.Lyrics への index。歌詞入りスコアも eligible に。
- **Glissando 移行**（`7f0a12d`、14型目、**新機構 note-index resolver**）: data-pos は start note の `SourcePosition` 由来。
  `GlissandoLayout` に `(StaffIndex,MeasureIndex,ItemIndex)` を載せ、`ResolveDataPos` の `ResolveNoteArr` が新 score の
  `staff.PrimaryVoice.Measures[mi].Items[ii]`（基底 `MusicItem`）から `SourcePosition` を引く。`BuildStaffMeasures` で lazy 構築。
- **Fingering 移行**（`aebafb5`、15型目、resolver 再利用）: `FingeringLayout` に `StaffIndex` 追加。host は note も **chord** も可
  なので `ResolveNoteArr` は基底 `MusicItem.SourcePosition` を読む。和音 fingering も対応。
- **TieVariant は移行不要**を確認（`DrawTieVariants` が data-pos を出さない）。
- **detected Hairpin/Ottava/TextSpanner 移行**（`e56dc23`、16〜18型目）: 検出元 cresc/ottava/rit mark（`score.MusicMarks`、単一テーブル）の
  元 index を threading、`ResolveArr(…, score.MusicMarks, …)` で再導出。3型とも `ReuseSafe` から除外＝残は Pedal（常に空）のみ。
- **beam reuse 穴を修正**（`e56dc23`、B-2 潜在バグ）: detected 移行で beamed multi-staff の reuse が初発火し露見。レンダラの beamed-item 集合が
  MusicItem 値（SourcePosition 込み）判定で、reuse 時に cached ビームメンバーが live ノートと値不一致→ビーム上に再ステム（+47 stray）。
  **位置キー `(staff,measure,item)` 化＋voice 1 のみ照合**で修正（ビームは primary voice 由来）。通常レンダ byte-identical。

## 次にやること（注釈移行は完了。方針確認から）
**まず `LSP_F3_ANNOTATION_REMAINING.md` 冒頭の「★ B 完了」を読む。** 残る選択肢は2つ、いずれも着手前にユーザーに方針確認:
1. **reuse 実効の benchmark 計測**: `IncrementalSessionBenchmark` 等で「内容不変編集の reuse 発火時 vs フル」のレイテンシ差を実測し、
   S5-1 の数値（layout 7.7ms＝全体の最大）がどれだけ削れたか定量化。安価・低リスク。
2. **道 C＝per-system 注釈の段ごと化（最難・大工事）**: width-CHANGING 編集にも効かせる。solver の段跨ぎ衝突回避を「自段＋上流確定段境界」に
   再設計し annotation も per-system キャッシュ。最大効果だが最高リスク（`LSP_F3_ANNOTATION_REMAINING.md` の「道 C」参照）。

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
