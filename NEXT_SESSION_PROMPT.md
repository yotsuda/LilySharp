C:\MyProj\LilySharp の LayoutRoadmap V3 Sprint 1 残タスクを実装する。

## パス
- Lily# 本体:        C:\MyProj\LilySharp
- Lily# テスト:      C:\MyProj\LilySharp\LilySharp.Tests
- Lily# 監査成果物:  C:\MyProj\LilySharp\audit
- LP ソース:         C:\MyProj\lilypond-src       (devel 2.25.35, LILYPOND-REF 行番号の基準)
- LP バイナリ:       C:\bin\lilypond-2.24.4\bin   (stable 2.24.4, sandbox から起動不可)

## 前提状況
- 前回セッションで Phase 1-3 のレビューを完了し、Phase 4 のうち M-1 (FileMissing citation 74→0)
  と M-2 (SpacingSettings/EngravingRules への LP citation 補完) を完了済み。
- 現在テストは 1,160/1,162 パス (skip 2)。

## まず以下を順に読んで全体像を把握する
1. C:\MyProj\LilySharp\REVIEW_REPORT.md          — Executive Summary、独立評価値 (~62-67%)、完了項目
2. C:\MyProj\LilySharp\LAYOUT_ROADMAP_V3.md      — Sprint 1〜5 の優先順位と LP 参照行番号
3. C:\MyProj\LilySharp\audit\algorithm_audits.md — Top 10 algorithm fidelity 精読 (LP ↔ Lily# 抜粋付き)
4. C:\MyProj\LilySharp\audit\citation_drift.md   — 引用整合性 (参考)
5. C:\MyProj\LilySharp\audit\grob_coverage.md    — grob/property 不在マトリクス (参考)

## 次に着手するのは Sprint 1 残タスクの先頭: K-1 break-substitution (推定 12h, HIGH severity)
- 目的: 改行をまたぐ spanner (slur / tie / beam / hairpin / ottava / volta / text-spanner /
  glissando) を LP の break_substitute と同様に分割し、broken piece の bound を改行点に
  再アタッチする。現状 LilySharp はこの処理が完全不在で、multi-line 楽譜で線が途切れる。
- LP 参照: lily/break-substitution.cc (全体), lily/spanner.cc::find_broken_piece
- LilySharp 修正先 (見込み):
    LilySharp.Core/Svg/Layout/KnuthPlassBreaker.cs
    LilySharp.Core/Svg/Layout/LayoutEngine.cs
    各 spanner engraver: SlurEngraver.cs, TieEngraver.cs, BeamEngraver.cs,
                         HairpinEngraver.cs, TextSpannerEngraver.cs,
                         OttavaBracketEngraver.cs, VoltaBracketEngraver.cs,
                         GlissandoEngraver.cs, TrillSpannerEngraver.cs
- 設計方針 (algorithm_audits.md「重大発見 2」より):
    1. spanner 抽象 IBreakable を導入 (各 spanner grob が Split(int breakColumnIndex) で
       broken piece 配列を返す)
    2. KnuthPlassBreaker が改行決定後に Split を呼んで前段/後段の broken piece を生成
    3. broken piece の bound (LeftEnd/RightEnd) を改行点に再アタッチ
    4. broken piece 用 rendering バリアント (右端 cut-off / 左端 continuation 印) 実装

## 絶対ルール (memory: lilysharp_rules.md にも記載)
- LilyPond ソース (C:\MyProj\lilypond-src devel 2.25.35) 準拠を絶対原則とする
- 独自の近似/ヒューリスティックを混入させない
- 全変更に LILYPOND-REF: lily/<file>.cc:<lines> コメントを必須付与
- 行番号は LP 2.25.35 基準で記録 (バイナリは 2.24.4 stable で別物)
- エラーメッセージ等で "lilysharp" でなく "Lily#" 表記 (memory)

## 開発ルール
- TDD: xUnit テストを先行作成
- スナップショット: LILYSHARP_UPDATE_SNAPSHOTS=1 dotnet test
- 全テストパス (1,160+) 維持
- ripple MCP で pwsh 実行 (memory: feedback_prefer_ripple)
- ビルド済 CLI を直接使う (dotnet run でなく)

## 実行制約
- C:\bin\lilypond-2.24.4\bin\lilypond.exe は company policy で Claude sandbox から起動不可。
- 視覚回帰 (audit/scripts/Run-LilyPond.ps1, Compare-Svg.ps1) はユーザー手動実行。
- audit/corpus/*.ly に 10 件の最小テストファイルあり、ユーザー側で SVG 比較可能。

## Sprint 1 残タスク (K-1 完了後の続き)
- G-3' staff-affinity 完全実装 (5h, HIGH)
    LP: lily/align-interface.cc:240-252, lily/page-layout-problem.cc:1174-1182
    Lily#: MultiStaffLayouter.cs:37, 103-127 (現状スタブ)
- H-1 multi-voice shortest_playing_duration tracking (2-3h)
    LP: lily/spacing-spanner.cc:266-310
    Lily#: SpacingRules.cs (duration→space formula は Faithful、multi-voice 集約のみ未実装)
- H-6 line-break permission 3-tier 階層 (2h)
    LP: lily/constrained-breaking.cc:520-535
    Lily#: KnuthPlassBreaker.cs:217-234 (現状二値、Forbid/Force のみ)

タスク管理は TaskCreate で立てて進めて。まず 1〜3 のドキュメントを読んでから設計を確定し、
ユーザーに承認を求めてから実装に入ること (Plan モード推奨)。
