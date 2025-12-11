# Lily# 作業進捗

## 📊 全体進捗

- **実装済み**: 34/34 ファイル (100%)
- **テスト**: 220 件パス
- **ステータス**: ✅ 基本機能完成、拡張フェーズ

## 📌 ステータス凡例

| ステータス | 意味 | ワークフロー |
|:----------:|------|--------------|
| 🚀 | NotStarted | 未着手 |
| ⏳ | Working | 作業中 |
| 🔍 | Review | レビュー待ち |
| ✅ | Complete | 完了 |
| 🟡 | Hold | 保留 |
| ❌ | Error | エラー |

**フロー**: 🚀 → ⏳ → 🔍 → ✅

---

## 📁 LilySharp.Core - Parser

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| Parser/Lexer.cs | ✅ | - | - | トークン化、臨時記号対応済 |
| Parser/Parser.cs | ✅ | - | - | 再帰下降パーサー、797行 |

## 📁 LilySharp.Core - Syntax

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| Syntax/SyntaxKind.cs | ✅ | - | - | 全トークン/ノード種別 |
| Syntax/SyntaxNode.cs | ✅ | - | - | Red node 基底クラス |
| Syntax/SyntaxNodes.cs | ✅ | - | - | 全 Red node 定義 |
| Syntax/SyntaxTree.cs | ✅ | - | - | インクリメンタル対応 |
| Syntax/TextChange.cs | ✅ | - | - | 差分更新 API |
| Syntax/TextSpan.cs | ✅ | - | - | テキスト範囲 |
| Syntax/Diagnostic.cs | ✅ | - | - | 診断メッセージ |
| Syntax/MusicEnums.cs | ✅ | - | - | ArticulationType, DynamicLevel |

## 📁 LilySharp.Core - InternalSyntax

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| InternalSyntax/GreenNode.cs | ✅ | - | - | Green node 基底 |
| InternalSyntax/GreenNodes.cs | ✅ | - | - | 全 Green node 定義 |
| InternalSyntax/SyntaxToken.Internal.cs | ✅ | - | - | トークン構造 |

## 📁 LilySharp.Core - Semantics

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| Semantics/Fraction.cs | ✅ | - | - | 分数演算 |
| Semantics/DurationCalculator.cs | ✅ | - | - | 音価計算 |
| Semantics/MeasureValidator.cs | ✅ | - | - | 小節検証 |

## 📁 LilySharp.Core - Midi

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| Midi/MidiFile.cs | ✅ | - | - | MIDI ファイル構造 |
| Midi/MidiEvents.cs | ✅ | - | - | MIDI イベント |
| Midi/MidiExporter.cs | ✅ | - | - | MIDI エクスポート |

## 📁 LilySharp.Core - MusicXml

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| MusicXml/MusicXmlTypes.cs | ✅ | - | - | MusicXML 型定義 |
| MusicXml/MusicXmlExporter.cs | ✅ | - | - | MusicXML エクスポート |

## 📁 LilySharp.Lsp

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| DocumentManager.cs | ✅ | - | - | ドキュメント状態管理 |
| LilySharpLanguageServer.cs | ✅ | - | - | LSP 実装 (13機能) |
| Program.cs | ✅ | - | - | エントリポイント |

## 📁 LilySharp.Cli

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| Program.cs | ✅ | - | - | CLI (midi/xml/check) |

## 📁 LilySharp.Tests

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| LexerTests.cs | ✅ | - | - | Lexer テスト |
| ParserTests.cs | ✅ | - | - | Parser テスト (62件) |
| SyntaxNodeTests.cs | ✅ | - | - | 構文ノードテスト |
| SemanticTests.cs | ✅ | - | - | 意味解析テスト |
| MidiTests.cs | ✅ | - | - | MIDI エクスポートテスト |
| MusicXmlTests.cs | ✅ | - | - | MusicXML テスト |
| LspTests.cs | ✅ | - | - | LSP テスト (13件) |
| IncrementalParsingTests.cs | ✅ | - | - | インクリメンタルテスト |
| IntegrationTests.cs | ✅ | - | - | 統合テスト |

---

## 🎯 実装済み機能

### 構文要素
- ✅ 音符 (Note), 休符 (Rest), 和音 (Chord)
- ✅ 音高 (Pitch) + 臨時記号 (is/es/isis/eses)
- ✅ 音価 (Duration) + 付点
- ✅ 調号 (KeySignature), 拍子 (TimeSignature)
- ✅ テンポ (TempoDeclaration), 音部記号 (Clef)
- ✅ 連符 (Tuplet), 装飾音 (Grace)
- ✅ アーティキュレーション, ダイナミクス
- ✅ 反復 (Repeat), 並行 (Parallel)
- ✅ 歌詞 (Lyrics)
- ✅ 変数 (let/use)

### LSP 機能 (13/13)
- ✅ Diagnostics, Completion, Hover
- ✅ Document Symbols, Go to Definition, Find References
- ✅ Semantic Highlighting, Folding, Rename
- ✅ Formatting, Code Actions, Signature Help
- ✅ Document Highlight


## 📋 現在のタスク

| タスク | status | priority | effort | notes |
|--------|:------:|:--------:|-------:|-------|
| SVG 余白計算ロジック | ✅ | High | 中 | LilyPond 準拠スケーリング修正、LineDetails 拡張完了 |
| VS Code リアルタイムプレビュー | ✅ | High | 中 | LSP カスタムリクエスト + WebView 実装完了 |
| SVG 新アーキテクチャ (Phase 1-4) | ✅ | High | 高 | Model/Collector/Layout/Renderer 分離、`svg2` コマンドで動作確認 |
| SVG 新アーキテクチャ (Phase 5-6) | ⏳ | High | 中 | SvgExporter 統合、旧ファイル削除、LSP 連携 |

## 🔧 技術的改善候補

| 対象 | priority | effort | 内容 |
|------|:--------:|-------:|------|
| LayoutEngine.cs | Low | 低 | Footnote 対応 (LilyPond の account_for_footnotes 相当) |
| LineDetails.SpringLength | Low | 低 | refpoint_extent_ を使った正確な計算（現在は Tallness で近似） |
| PaperSettings.cs | Low | 低 | MmPerPoint を 25.4/72 で計算（現在は 0.3528 のハードコード） |

---

## 📋 今後の候補

| 機能 | priority | effort | notes |
|------|:--------:|-------:|-------|
| PDF/SVG レンダリング | Normal | 高 | Emmentaler フォント必要 |
| MusicXML インポート | Low | 中 | 逆変換 |
| マルチファイルプロジェクト | Normal | 中 | include 構文 |
| コード記法 | Normal | 低 | <c e g> の名前付け |