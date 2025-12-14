# Lilypond レイアウト完全模倣 - 進捗管理

## 🔧 現在の作業状態

### ブランチ
```
master
```

### 実装済みファイル
| ファイル | パス | 内容 |
|----------|------|------|
| Skyline.cs | `LilySharp.Core/Svg/Layout/` | 矩形ベースの Skyline |
| GlyphMetrics.cs | `LilySharp.Core/Svg/Layout/` | SMuFL bounding box |
| SpacingRules.cs | `LilySharp.Core/Svg/Layout/` | Skyline 生成、MinDistance 計算 |
| Spring.cs | `LilySharp.Core/Svg/Layout/` | Spring-Rod モデル |
| SpringSolver.cs | `LilySharp.Core/Svg/Layout/` | 制約ソルバー |
| BeamEngraver.cs | `LilySharp.Core/Svg/Layout/` | 連桁レイアウト |
| BeamScoringProblem.cs | `LilySharp.Core/Svg/Layout/` | 連桁角度最適化 |
| TieEngraver.cs | `LilySharp.Core/Svg/Layout/` | タイレイアウト |
| SlurEngraver.cs | `LilySharp.Core/Svg/Layout/` | スラーレイアウト |
| AccidentalPlacement.cs | `LilySharp.Core/Svg/Layout/` | 和音内臨時記号配置 |
| NoteCollision.cs | `LilySharp.Core/Svg/Layout/` | 声部間ノート衝突検出 |
| StemDirection.cs | `LilySharp.Core/Svg/Layout/` | 符幹方向自動計算 |
| EngravingDefaults.cs | `LilySharp.Core/Svg/` | Emmentaler メトリクス定数 |
| SvgRenderOptions.cs | `LilySharp.Core/Svg/Renderer/` | SVG レンダリングオプション |

### ドキュメント
| ファイル | 内容 |
|----------|------|
| docs/COORDINATE_SYSTEM.md | 座標系ガイドライン（staff positions/spaces/pixels） |
| docs/SVG_LAYOUT_ARCHITECTURE.md | SVGレイアウトアーキテクチャ |

### 次のタスク候補
| 優先度 | タスク | 理由 |
|:------:|--------|------|
| 1 | Phase 0: Semantic Layer | アーキテクチャ基盤。repeat/section 展開に必須 |
| 2 | Phase 9: Knuth-Plass 改行 | 長い楽譜の最適改行 |
| 3 | Phase 2: 斜め Skyline | Lilypond 完全等価に必須 |

### ⚠️ アーキテクチャ課題

現在の `MeasureCollector` は以下の問題を抱えている:
- section/structure の展開が不完全
- phrase 参照の解決が場当たり的
- repeat/alternative の処理が未実装

**根本解決**: Semantic Layer の導入が必要。詳細は `docs/ARCHITECTURE_REDESIGN.md` を参照。


---
## 🎯 目標

**Lilypond 等価品質のレイアウト** - 同じアルゴリズムで視覚的に同等の品質を実現

### 設計方針

| 項目 | 方針 |
|------|------|
| 品質 | Lilypond と視覚的に同等（ピクセル単位の一致は非目標） |
| フォント | Emmentaler フォント使用（Lilypond 互換） |
| 処理 | 高速化優先。Scheme 機能は移植しない |
| 表記 | 処理時間短縮のため独自表記を採用可 |

**アルゴリズム等価・パラメータ独自**: Lilypond の核心アルゴリズムを移植し、Bravura 向けにパラメータ調整

### アーキテクチャ方針

| パターン | 採用状況 | 目標 |
|----------|:--------:|:----:|
| Immutable Records | ✅ | 維持 |
| ImmutableArray | ✅ | 維持 |
| Red-Green Tree | ✅ | 維持 |
| Visitor パターン | ❌ | 導入予定 |
| Builder パターン | ⚠️ | 拡大予定 |
| Incremental 更新 | ❌ | 将来課題 |


## 📊 全体進捗

| Phase | 項目 | 完了 | 合計 | 進捗 |
|-------|------|-----:|-----:|-----:|
| **0** | **Semantic Layer** | **0** | **12** | **0%** |
| 1 | 基本グリフ配置 | 6 | 6 | 100% |
| 2 | Skyline 衝突回避 | 4 | 5 | 80% |
| 3 | 連桁（Beaming） | 9 | 9 | 100% |
| 4 | タイ・スラー | 6 | 6 | 100% |
| 5 | 和音内臨時記号 | 3 | 3 | 100% |
| 6 | 複数声部 | 9 | 9 | 100% |
| 7 | 記譜記号 | 0 | 3 | 0% |
| 8 | 歌詞配置 | 0 | 4 | 0% |
| 9 | ページレイアウト | 0 | 3 | 0% |
| 10 | 高度な機能 | 0 | 6 | 0% |
| 11 | グランドスタッフ | 7 | 8 | 88% |
| **合計** | | **41** | **71** | **58%** |

## 📋 ステータス凡例

| ステータス | 意味 | ワークフロー |
|:----------:|------|-------------|
| 🚀 | NotStarted | 未着手 |
| ⏳ | Working | 作業中 |
| 🔍 | Review | レビュー待ち |
| ✅ | Complete | 完了（Lilypond と等価確認済） |
| 🟡 | Hold | 保留 |
| ❌ | Error | エラー |

ワークフロー: 🚀→⏳→🔍→✅

## Phase 0: Semantic Layer 🚀 **[最優先]**

**ビジョン**: 世界でいちばん美しいデザインの楽譜作成ソフトウェア

**目的**: MeasureCollector の責務を分離し、section/structure/phrase を正しく処理する

**成功基準**: `RenderMinuet_HasExpectedStructure` テストがパスする

### Phase 0-A: Symbol Collection (3-4日)

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| Symbol.cs | 🚀 | Critical | 2h | シンボル基底クラス |
| SectionSymbol.cs | 🚀 | Critical | 1h | section 定義 |
| PhraseSymbol.cs | 🚀 | Critical | 1h | phrase 定義 |
| SymbolTable.cs | 🚀 | Critical | 4h | シンボル管理 |
| SymbolCollector.cs | 🚀 | Critical | 4h | 定義収集 (Pass 1) |

### Phase 0-B: Binder + StructureExpander (4-5日)

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| BoundMusic.cs | 🚀 | Critical | 4h | BoundNote, BoundRest, BoundChord |
| BoundMeasure.cs | 🚀 | Critical | 2h | 展開済み小節 |
| BoundScore.cs | 🚀 | Critical | 2h | 展開済みスコア |
| Binder.cs | 🚀 | Critical | 8h | 参照解決、BoundScore 生成 |
| StructureExpander.cs | 🚀 | Critical | 8h | repeat/alternative → flat sequence |
| RelativePitchResolver.cs | 🚀 | Critical | 4h | relative { } 音程解決 |

### Phase 0-C: 統合 (2-3日)

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| MeasureCollector リファクタ | 🚀 | Critical | 8h | Binder を使用するよう書き換え |
| SemanticDiagnostic.cs | 🚀 | Normal | 2h | エラーメッセージ |

**詳細設計**: `docs/ARCHITECTURE_REDESIGN.md`

**アーキテクチャ決定 (2025-12-13):**
- MusicIterator は不採用 (LilyPond の Iterator は Scheme 遅延評価用、LilySharp には不要)
- Structure は Binder 段階で事前展開 (高速化)
- Grob 概念は不採用 (既存の Layout + Renderer で十分)

**依存関係**:
```
SyntaxTree
    → SymbolCollector (定義収集)
        → Binder (参照解決 + Structure展開)
            → BoundScore (展開済み)
                → MeasureCollector (簡素化、BoundScore → Score 変換のみ)
```

---

## Phase 1: 基本グリフ配置 ✅

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| SvgRenderer.cs (符頭描画) | ✅ | High | 2h | SMuFL グリフ使用 |
| SvgRenderer.cs (符幹描画) | ✅ | High | 1h | StemUp/Down 対応 |
| SvgRenderer.cs (旗描画) | ✅ | High | 1h | 8th/16th/32nd |
| SvgRenderer.cs (休符描画) | ✅ | High | 1h | 全休符〜32分休符 |
| SvgRenderer.cs (臨時記号) | ✅ | High | 2h | SMuFL metrics で配置 |
| SvgRenderer.cs (付点) | ✅ | High | 1h | 線上回避（Lilypond 同様） |

## Phase 2: Skyline ベース衝突回避 ⏳

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| Skyline.cs (矩形版) | ✅ | High | 3h | 矩形近似 - **要改修** |
| Skyline.cs (斜め Building) | 🚀 | High | 4h | Lilypond 同様の slope 対応必須 |
| GlyphMetrics.cs | ✅ | High | 2h | SMuFL bounding box |
| SpacingRules.cs (Skyline 生成) | 🟡 | High | 2h | 斜め対応後に再実装 |
| SpacingRules.cs (MinDistance) | 🟡 | High | 1h | 斜め対応後に再実装 |

**注**: 現在の矩形近似 Skyline は暫定実装。Lilypond と等価にするには斜め Building 対応が必須。

## Phase 3: 連桁（Beaming） ⏳

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| BeamGroup.cs | ✅ | High | 2h | 連桁グループモデル（BeamGroup, BeamMember, BeamLayout） |
| BeamDetector.cs | ✅ | High | 2h | 連桁検出（自動グループ化） |
| BeamEngraver.cs | ✅ | High | 4h | beam 位置計算（BeamScoringProblem 統合） |
| LayoutEngine.cs (Beam統合) | ✅ | High | 1h | LayoutEngine への Beam 統合 |
| SvgRenderer.cs (連桁描画) | ✅ | High | 2h | path 要素で描画 |
| BeamQuantParameters.cs | ✅ | High | 1h | Lilypond Beam_quant_parameters 移植 |
| BeamConfiguration.cs | ✅ | High | 1h | Lilypond Beam_configuration 移植 |
| BeamScoringProblem.cs | ✅ | High | 8h | Lilypond beam-quanting.cc 基本移植（7 scorers） |
| BeamScoringProblem.cs (Collision) | ✅ | High | 0.5h | 連桁と音符の衝突回避スコアラー（ScoreCollisions呼び出し追加） |

## Phase 4: タイ・スラー ✅ (基本実装完了)

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| TieItem.cs | ✅ | High | 1h | タイのモデル |
| TieLayout.cs | ✅ | High | 1h | タイのレイアウト（Bezier 制御点） |
| TieDetails.cs | ✅ | High | 1h | Lilypond Tie_details 移植 |
| TieDetector.cs | ✅ | High | 1h | タイ検出（プレースホルダー） |
| TieEngraver.cs | ✅ | High | 4h | Lilypond bezier-bow.cc 基本移植 |
| ScoreLayout.cs (Tie) | ✅ | High | 0.5h | TieLayouts プロパティ追加 |
| LayoutEngine.cs (Tie統合) | ✅ | High | 1h | LayoutEngine への Tie 統合 |
| SvgRenderer.cs (タイ描画) | ✅ | High | 2h | ベジェ曲線描画 |
| SlurItem.cs | ✅ | High | 1h | スラーのモデル |
| SlurLayout.cs | ✅ | High | 1h | スラーのレイアウト |
| SlurScoreParameters.cs | ✅ | High | 1h | Lilypond Slur_score_parameters 移植 |
| SlurDetector.cs | ✅ | High | 1h | スラー検出（プレースホルダー） |
| SlurEngraver.cs | ✅ | High | 2h | Lilypond bezier-bow.cc 基本移植 |
| ScoreLayout.cs (Slur) | ✅ | High | 0.5h | SlurLayouts プロパティ追加 |
| LayoutEngine.cs (Slur統合) | ✅ | High | 1h | LayoutEngine への Slur 統合 |
| SvgRenderer.cs (スラー描画) | ✅ | High | 2h | ベジェ曲線描画 |
| TieFormatting.cs | 🚀 | Low | 10h | tie-formatting-problem.cc 完全再現（1100行） |
| SlurScoring.cs | 🚀 | Low | 12h | slur-scoring.cc 完全再現（800行） |

## Phase 5: 和音内臨時記号 ✅

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| AccidentalPlacement.cs | ✅ | High | 6h | 臨時記号配置計算 |
| AccidentalPlacementParameters.cs | ✅ | High | 0.5h | パラメータ設定 |
| SvgRenderer.cs (和音臨時記号) | ✅ | High | 2h | 複数臨時記号描画 |
| AccidentalPlacementTests.cs | ✅ | High | 1h | テスト |

## Phase 6: 複数声部 ✅

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| VoiceColumn.cs | ✅ | High | 2h | 声部列モデル |
| NoteCollision.cs | ✅ | High | 6h | Lilypond note-collision.cc 移植 |
| StemDirection.cs | ✅ | High | 3h | 自動符幹方向計算 |
| NoteCollisionTests.cs | ✅ | High | 1h | 衝突テスト |
| StemDirectionTests.cs | ✅ | High | 1h | 符幹方向テスト |
| VoiceCollector.cs | ✅ | High | 3h | 声部分離 |
| LayoutEngine.cs (声部統合) | ✅ | High | 2h | NoteCollision を LayoutEngine に統合、VoiceOffsets 計算 |
| SvgRenderer.cs (複数声部) | ✅ | High | 4h | 複数声部描画、衝突回避オフセット適用 |
| MeasureCollector.cs | ✅ | Medium | 1h | MeasureBuilder 抽出によるリファクタリング |

## Phase 7: 記譜記号 🚀

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| ClefRenderer.cs | 🚀 | Normal | 3h | 音部記号描画（位置完全一致） |
| KeySignatureRenderer.cs | 🚀 | Normal | 4h | 調号描画（位置完全一致） |
| TimeSignatureRenderer.cs | 🚀 | Normal | 3h | 拍子記号描画（位置完全一致） |

## Phase 8: 歌詞配置 🚀

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| LyricItem.cs | 🚀 | Normal | 1h | 歌詞モデル |
| LyricCollector.cs | 🚀 | Normal | 3h | 歌詞と音符の紐付け（Lilypond 同様） |
| LyricHyphen.cs | 🚀 | Normal | 3h | ハイフン・エクステンダー |
| SvgRenderer.cs (歌詞描画) | 🚀 | Normal | 3h | テキスト配置（位置完全一致） |

## Phase 9: ページレイアウト最適化 🚀

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| KnuthPlassBreaker.cs | 🚀 | High | 12h | page-layout-problem.cc 完全再現 |
| PageBreaker.cs | 🚀 | High | 6h | page-spacing.cc 完全再現 |
| ScoreLayout.cs (最適化) | 🚀 | High | 6h | 既存コードの Lilypond 等価化 |

## Phase 10: 高度な機能 🚀

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| DynamicsRenderer.cs | 🚀 | Normal | 4h | 強弱記号（位置完全一致） |
| ArticulationRenderer.cs | 🚀 | Normal | 4h | アーティキュレーション |
| GraceNoteRenderer.cs | 🚀 | Normal | 6h | 装飾音 |
| RepeatRenderer.cs | 🚀 | Normal | 4h | 繰り返し記号 |
| TremoloRenderer.cs | 🚀 | Normal | 4h | トレモロ |
| OrnamentRenderer.cs | 🚀 | Normal | 4h | 装飾記号 |

---

## 📐 検証方法

各フェーズ完了時に以下を実施:

1. **Lilypond で同じファイルをレンダリング**
2. **座標・サイズを数値比較**
3. **差異が許容範囲（0.1px）以内であることを確認**

## 📚 参照ドキュメント

**詳細アーキテクチャ**: `docs/SVG_LAYOUT_ARCHITECTURE.md`
- 3層レイアウトアーキテクチャ
- Spring-Rod モデルの設計と気づき
- Roslyn 参照ファイル一覧

## 参照ファイル（Lilypond）

| LilySharp ファイル | Lilypond 参照 | 行数 | 複雑度 |
|-------------------|--------------|-----:|:------:|
| Skyline.cs | skyline.cc | 600 | 中 |
| BeamQuanting.cs | beam-quanting.cc | 1200 | **高** |
| TieFormatting.cs | tie-formatting-problem.cc | 1100 | **高** |
| SlurScoring.cs | slur-scoring.cc | 800 | **高** |
| AccidentalPlacement.cs | accidental-placement.cc | 400 | 中 |
| NoteCollision.cs | note-collision.cc | 600 | 中 |
| KnuthPlassBreaker.cs | page-layout-problem.cc | 1400 | **高** |


## Phase 11: グランドスタッフ（大譜表） ✅

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| Parser (GrandStaffRender) | ✅ | High | 1h | 構文解析対応 |
| StaffGroup.cs | ✅ | High | 2h | 譜表グループモデル（GrandStaff, StaffGroup） |
| Staff.cs | ✅ | High | 2h | 個別譜表モデル（音部記号、声部紐付け） |
| GrandStaffLayout.cs | ✅ | High | 3h | 大譜表レイアウト計算 |
| BraceRenderer.cs | ✅ | High | 2h | 大括弧描画（ベジェ曲線） |
| SystemBarlineRenderer.cs | ✅ | High | 2h | 譜表間を結ぶ小節線 |
| RenderSpec.cs | ✅ | High | 1h | render ブロックの構造化表現 |
| RenderSpecParser.cs | ✅ | High | 2h | RenderDeclarationSyntax → RenderSpec 変換 |
| MultiStaffScore.cs | ✅ | High | 2h | 複数譜表のスコアモデル |
| MeasureCollector (MultiStaff) | ✅ | High | 1h | CollectMultiStaff() メソッド追加 |
| LayoutEngine.cs (多譜表) | ✅ | High | 4h | Layout(MultiStaffScore) メソッド追加 |
| SvgRenderer.cs (多譜表) | ✅ | High | 4h | 複数譜表描画 |

## 更新履歴

| 日付 | 更新内容 |
|------|----------|
| 2025-12-11 | Phase 1 完了。Phase 2 部分完了（矩形 Skyline 暫定実装） |
| 2025-12-11 | 目標を「完全等価」に修正。工数見積もり上方修正 |
| 2025-12-12 | Phase 6 完了。NoteCollision を LayoutEngine に統合、MeasureBuilder リファクタリング |
| 2025-12-12 | 目標を「LilyPond 等価品質」に再定義。Roslyn パターン採用方針を明確化 |
| 2025-12-12 | Phase 11: グランドスタッフ（大譜表）を計画に追加 |
| 2025-12-13 | Phase 3 完了。ScoreCollisions 呼び出し追加 |
| 2025-12-13 | Phase 11: Model, Layout基盤, Renderer コンポーネント完了 |
| 2025-12-13 | Phase 11: RenderSpec, MultiStaffScore, Collector 統合完了 |
| 2025-12-13 | Emmentaler フォントに移行。EngravingDefaults/EngravingRules に整理 |
| 2025-12-13 | SVG フォント埋め込みオプション追加（CLI --embed-font, VS Code プレビュー対応） |
| 2025-12-13 | Phase 11: LayoutEngine.Layout(MultiStaffScore) 実装完了 |
| 2025-12-13 | Phase 0: Semantic Layer を追加。アーキテクチャ再設計計画を統合 |
| 2025-12-14 | Phase 11 完了。SvgRenderer multi-staff 描画、CLI 統合 |
| 2025-12-13 | LilyPond/Roslyn ソースコード調査。MusicIterator 不採用を決定。Phase 0 を3段階に分割 |
| 2025-12-14 | 🔍 音符間スペーシング修正。Lilypond spacing-options.cc アルゴリズム移植。CalculateDurationSpace/CalculateMeasureIdealWidth 実装 |
