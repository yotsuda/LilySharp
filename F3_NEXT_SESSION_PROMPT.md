# F3 次セッション開始プロンプト

> 新しい Claude Code セッションの最初にこの本文（下の `---` 以降）をそのまま貼る。
> 詳細な引き継ぎは `docs/DEV_BUGFIX_WORKFLOW.md` §19。自動メモリ（`project_lilysharp_f3_incremental.md`）も同じ現在地を保持。
> 速度を先に数値で見たい場合は、S5 の前に §19.7「F0 benchmark に session-edit ケースを追加（`[IterationSetup]` でリセット）」を頼む一文を足す。

---

LilySharp の F3（意味解析〜レイアウトの増分クエリDAG化）を再開する。まず現状を把握してから着手して。

## 最初に読む（この順で）
1. `C:\MyProj\LilySharp\docs\DEV_BUGFIX_WORKFLOW.md` の §19「次セッションへの引き継ぎ ― F3 増分コンパイル」
2. `C:\MyProj\LilySharp\LSP_F3_QUERY_GRAPH_DESIGN.md` の §0.5（検証済み前提と S-stage 進捗）
3. `C:\MyProj\LilySharp\LSP_INCREMENTAL_IMPROVEMENT_PROPOSAL.md`（親提案・全体像）

## 現在地
- リポジトリ `C:\MyProj\LilySharp`、ブランチ `f3-incremental`（= origin/master = `c7fe181`）。
- 全テスト 1824 passed / 3 skipped。S0〜S4b 完了（S4b = `IncrementalCompiler` で行分割 DP をカットオフ、増分==フル証明済み）。
- 着手前に必ずベースライン確認: `dotnet test LilySharp.Tests`（期待 1824/3 skipped）。

## 次にやること（S5）
per-measure semantics/layout のメモ化（render/layout が律速ゆえ効果の本丸）。
- まず「安定 measure 識別子（編集で生き残る content key）」を製造する（§19.4/§19.5）。
- deferred の context 拡張（octave 基準 / ottava / pending ties / open spanners）は walk/green 駆動で本段に取り込む。
- 既存の `IncrementalCompiler` の「増分==フル」差分テストを CI ゲートとして各スライスで証明する。
- まず S5 の設計を現行コードで grounding してから、検証可能な小スライスに分けて提案して。

## 厳守事項（F3 作業の運用）
- ブランチ `f3-incremental` は承認済み。各段は「ビルド緑＋全テスト緑＋snapshot byte-identical（純 substrate 時）」を確認後に
  `git branch -f master f3-incremental`（ff）＋ `git push origin master` で段階マージ。
- byte-identical は純 substrate の既定であって目的ではない。出力がより正しくなる変更は歓迎（その時は snapshot を意図的に貼り直し、改善であることを示す）。基準は「正しさ＞現状維持」。
- シェルは ripple MCP の `execute_command`(pwsh)。PowerShell ツール / Bash ツールは使わない。特殊文字を含むファイルは `Write` で作成。
- lilypond 比較が要る時は §3 のデッドロック回避起動（`cmd /c ... < NUL > log`）。
- コミットは 1 論点 1 コミット、`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`。
- master 直やブランチ新規作成は勝手にやらない（F3 は既存 `f3-incremental` で継続）。大きな方向転換は提案して確認。

まず上記 3 点を読み、ベースラインを確認し、S5 の grounding 調査の結果と最初の小スライス案を提示して。
