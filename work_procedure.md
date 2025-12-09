# Lilysharp 作業手順書

## 概要

LilyPond の後継となる楽譜作成ソフトウェア「Lilysharp」の開発。
C# で実装し、以下の特徴を持つ:

- **シングルパスコンパイル**: Lexer → Parser → SyntaxTree を単一パスで処理
- **インクリメンタルコンパイル**: TextChange API による差分更新
- **Red-Green Tree**: Roslyn スタイルの不変構文木
- **LSP 対応**: VS Code 拡張機能による高品質な編集体験
- **複数出力形式**: MIDI、MusicXML エクスポート

## プロジェクト構造

```
Lilysharp/
├── Lilysharp.Core/        # コアライブラリ
│   ├── Parser/            # Lexer, Parser
│   ├── Syntax/            # 構文ノード (Red-Green Tree)
│   ├── Semantics/         # 意味解析 (Duration, Measure)
│   ├── Midi/              # MIDI エクスポート
│   └── MusicXml/          # MusicXML エクスポート
├── Lilysharp.Lsp/         # Language Server Protocol 実装
├── Lilysharp.Cli/         # コマンドラインツール
├── Lilysharp.Tests/       # テストプロジェクト
└── editors/vscode/        # VS Code 拡張機能
```

## 作業手順

### 1. 機能追加

1. Green node (InternalSyntax) を追加
2. SyntaxKind を追加
3. Red node (SyntaxNodes) を追加
4. SyntaxNode.CreateChild に対応追加
5. Parser にパースロジック追加
6. テスト作成・実行
7. エクスポーター対応 (MIDI/MusicXML)
8. LSP 対応 (Hover, Completion 等)

### 2. リファクタリング

1. 変更対象の特定
2. 既存テストの確認
3. 変更実施
4. テスト実行・確認
5. 関連コードの更新

### 3. バグ修正

1. 問題の再現・特定
2. テストケース作成
3. 修正実施
4. 全テスト実行

## 品質基準

- **テストカバレッジ**: 主要機能に対するテストが存在すること
- **ビルド成功**: 警告なしでビルドが通ること
- **テスト通過**: 全テストがパスすること
- **コード品質**: 重複コードの排除、適切な抽象化

## リスク

| リスク | 対策 |
|--------|------|
| 構文互換性 | LilyPond の良い表記法のみ採用、悪い慣例は除外 |
| パフォーマンス | インクリメンタルパース、効率的な木走査 |
| LSP 安定性 | DocumentManager による状態管理 |

## コミットポリシー

1. テストが全てパスすること
2. ユーザーのレビュー承認を得ること
3. コミットメッセージは英語一文で簡潔に

## 進捗更新ルール

- 作業状態が変化したら即座に work_progress.md を更新
- タスク完了時は必ずステータスを更新

## 学習更新ルール

- 作業中に得た知見はこのファイルの適切な場所に追記
- ドキュメントは簡潔に保つ

## 技術メモ

### 文法設計方針

**基本原則**: LilyPond の表記を尊重しつつ、要件に沿う新しく洗練された文法を導入。古く一貫性のない表記はサポートしない。

**採用する表記**:
- シャープ: `is` (cis, dis, fis...)
- フラット: `es` (des, ees, bes...)
- ダブルシャープ/フラット: `isis`, `eses`
- 相対オクターブ: `'` (上), `,` (下)
- 音価: 数字 (1, 2, 4, 8...) + 付点 (`.`, `..`)

**採用しない表記**:
- `as` (aes の省略形) - 一貫性がない
- `s` を臨時記号として使用 - 休符 (spacer rest) と混同
- ドイツ語式の `h` (B音) - 国際標準の `b` を使用

### Red-Green Tree アーキテクチャ

- **Green Node**: 不変、親への参照なし、共有可能
- **Red Node**: Green をラップ、親・位置情報を持つ、オンデマンド生成
- **利点**: インクリメンタル更新時に変更のない部分を再利用

### 構文ノード追加チェックリスト

1. [ ] `SyntaxKind` に種別追加
2. [ ] `GreenNodes.cs` に Green node 追加
3. [ ] `SyntaxNodes.cs` に Red node 追加
4. [ ] `SyntaxNode.CreateChild` に対応追加
5. [ ] `Parser.cs` にパースロジック追加
6. [ ] テスト作成
7. [ ] `MidiExporter` 対応 (必要な場合)
8. [ ] `MusicXmlExporter` 対応 (必要な場合)
9. [ ] LSP Hover/Completion 対応

### PitchSyntax プロパティ

- `PitchName`: 全体 (例: "cis")
- `BaseName`: 基本音名 (c, d, e, f, g, a, b)
- `Accidental`: 臨時記号接尾辞 (is, es, isis, eses)
- `AccidentalOffset`: 半音オフセット (-2 to +2)
- `OctaveOffset`: オクターブマーク数

### DurationSyntax プロパティ

- `Value`: 音価 (1, 2, 4, 8, 16...)
- `DotCount`: 付点の数
- `ToFraction()`: Fraction への変換

### テスト命名規則

- `Parse<構文名>`: パースのテスト
- `Export<形式>_<機能>`: エクスポートのテスト
- `<クラス名>_<プロパティ/メソッド>`: プロパティ/メソッドのテスト

### pager の無効化

Git コマンドで pager が起動するとインタラクティブ状態で止まる。
常に `git --no-pager` を使用すること。

### DLL ロック問題

PowerShell で `Add-Type` を使うと DLL がロックされ、ビルドが失敗する。
その場合は PowerShell コンソールを再起動 (`Stop-Process -Id $PID`) すること。

### コミットメッセージ

- 英語一文で簡潔に記述
- 動詞から始める (Add, Fix, Refactor, Update, Remove...)
- 例: "Add MusicXML chord export support"

### シングルパス化の準備

将来的に Lexer → Parser のストリーミング処理（真のシングルパス）を実現するため、以下を守る:

**禁止事項**:
- `Peek(n)` で n >= 2 の先読み（現状 LL(2) を維持）
- バックトラック（`_position--`）の新規追加
- トークンリストの複数回走査

**現状の制約** (将来解消予定):
- Parser コンストラクタで `tokens.ToList()` している（1箇所）
- Articulation パースで1トークンのバックトラックあり（行571）

**シングルパス化時の変更方針**:
- Lexer: 2トークンバッファ保持に変更
- Parser: Lexer から直接トークン取得
- バックトラック: 先読みロジックに書き換え