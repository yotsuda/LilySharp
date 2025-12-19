# LilySharp 作業手順書

## 概要

LilyPond の後継となる楽譜作成ソフトウェア。C# で実装し、**Lilypond と視覚的に等価**なレイアウトを高速に実現する。

**特徴**:
- シングルパスコンパイル（Red-Green Tree）
- インクリメンタルコンパイル
- LSP 対応（VS Code プラグイン）
- Lilypond レイアウトアルゴリズムの忠実な移植

## プロジェクト構造

```
LilySharp/
├── LilySharp.Core/        # コアライブラリ
│   ├── Parser/            # Lexer, Parser
│   ├── Syntax/            # 構文ノード (Red-Green Tree)
│   ├── Semantics/         # 意味解析
│   ├── Svg/               # SVG レンダリング（Layout, Renderer）
│   ├── Midi/              # MIDI エクスポート
│   └── MusicXml/          # MusicXML エクスポート
├── LilySharp.Lsp/         # Language Server Protocol
├── LilySharp.Cli/         # コマンドラインツール
├── LilySharp.Tests/       # テスト
└── editors/vscode/        # VS Code 拡張
```

## 参照リソース

| リソース | パス |
|---------|------|
| LilyPond ソース | `C:\MyProj\lilypond-src\lily\` |
| Roslyn ソース | `C:\MyProj\roslyn` |

## 作業ルール

### コミットポリシー
1. テストが全てパスすること
2. ユーザーのレビュー承認を得ること
3. コミットメッセージは英語一文で簡潔に

### SVG ファイルの読み込み

**重要**: SVG ファイルの4行目には base64 エンコードされたフォントが埋め込まれている。これを読むとトークンを大量に消費するため、SVG を読む際は必ず5行目以降から読むこと。

```powershell
# NG: フォントデータを読み込んでしまう
Show-TextFile output.svg

# OK: 5行目以降を読む
Show-TextFile output.svg -LineRange 5,-1
```

### 進捗・学習更新
- 作業状態が変化したら即座に `work_progress.md` を更新
- 実装中に得た知見は本ファイルまたは `docs/` に追記

## Lilypond ソースコードとの相互参照

### ライセンス

LilySharp は GPLv3（`LICENSE` 参照）。Lilypond のコードを参照・翻訳可能。

### コメント形式

**LilySharp (C#) - 公開**
```csharp
// LILYPOND-REF: <file>:<line> <function>
```

**Lilypond (C++) - 作業用メモ（非公開）**
```cpp
// LILYSHARP-REF: <file>:<line> <function>
```

### 規則
1. タグは正確に: `LILYPOND-REF:` / `LILYSHARP-REF:`
2. プロジェクトルートからの相対パス: lily/spacing-options.cc
3. 行番号・関数名は必須
4. 実装コードの直前に配置

### 計算式を写す前のチェックリスト

1. **重複確認**:
   ```powershell
   Show-TextFile LilySharp.Core/Svg/Layout/*.cs -Contains "LILYPOND-REF:.*<function>"
   ```
2. **対応表更新**: `docs/spacing-mapping.md`
3. **Lilypondソースにメモ追加**（作業用）
4. **LilySharpに参照コメント追加**

### コード検索

コメントやコードの検索には `Show-TextFile` を使う:

```powershell
# LilySharpの LILYPOND-REF を検索
Show-TextFile LilySharp.Core/Svg/Layout/*.cs -Contains "LILYPOND-REF:"

# 正規表現で特定の関数を検索
Show-TextFile LilySharp.Core/Svg/Layout/*.cs -Pattern "LILYPOND-REF:.*Spring"

# Lilypondの LILYSHARP-REF を検索
Show-TextFile C:\MyProj\lilypond-src\lily\spacing-*.cc, C:\MyProj\lilypond-src\lily\spring.cc -Contains "LILYSHARP-REF:"

# 特定行範囲の表示
Show-TextFile LilySharp.Core/Svg/Layout/SpacingRules.cs -LineRange 260,290
```

### 現在の実装対応表

| LilySharp | Lilypond |
|-----------|----------|
| SpacingRules.cs:262-281 CreateSpring() | lily/spacing-basic.cc:100-130 note_spacing() |
| SpacingRules.cs:294-319 CalculateDurationSpace() | lily/spacing-options.cc:58-73 get_duration_space() |
| Spring.cs:82-89 Length() | lily/spring.cc:218-236 Spring::length() |
| SpringSolver.cs:55-101 SolveForWidth() | lily/simple-spacer.cc:150-200 solve() |


## VS Code 拡張デプロイ

### デプロイスクリプト

```powershell
# 開発デプロイ（バージョン自動インクリメント: 0.1.1-dev.1 → 0.1.1-dev.2）
.\deploy-extension.ps1

# リリースデプロイ（プレリリースタグ削除: 0.1.1-dev.2 → 0.1.1）
.\deploy-extension.ps1 -Release

# VS Code 設定更新をスキップ
.\deploy-extension.ps1 -SkipVSCodeSettings
```

### スクリプトの機能

1. LSP プロセス停止（ファイルロック解除）
2. バージョン自動インクリメント
3. 古い VSIX ファイルのクリーンアップ（最新2つを残す）
4. LSP サーバービルド
5. TypeScript コンパイル
6. VSIX パッケージ作成
7. VS Code 設定の自動更新（`lilysharp.serverPath`）
8. 拡張機能のアンインストール・インストール

### デプロイ後の確認

1. VS Code を**完全に閉じる**（タスクバーからも終了）
2. VS Code を再起動
3. `Ctrl+Shift+X` → `Lily#` 検索 → バージョン確認
4. `.lys` ファイルを開いて `Ctrl+Shift+V` でプレビュー確認

### トラブルシューティング

| 症状 | 原因 | 対処 |
|------|------|------|
| プレビューが表示されない | LSP サーバーパスが古い | `.\deploy-extension.ps1` で設定を自動更新 |
| "Language server not found" | .NET ランタイムがない | `dotnet --list-runtimes` で確認 |
| 拡張機能が古いまま | VS Code が再起動されていない | タスクバーからも終了して再起動 |
| ビルドエラー | DLL ロック | VS Code を閉じてから再実行 |

### バージョニング規則

| 種別 | 形式 | 例 |
|------|------|-----|
| 開発ビルド | `major.minor.patch-dev.N` | `0.1.1-dev.5` |
| リリース | `major.minor.patch` | `0.1.1` |

### ビルド成果物

| ファイル | 場所 |
|---------|------|
| LSP サーバー | `LilySharp.Lsp/bin/Debug/net10.0/lilysharp-lsp.exe` |
| VSIX パッケージ | `editors/vscode/lilysharp-*.vsix` |

## 技術メモ

### 文法設計方針

LilyPond の良い表記を継承しつつ、シングルパスで処理しやすい文法を採用。
詳細: `docs/GRAMMAR.md`

### Red-Green Tree

- **Green Node**: 不変、親への参照なし、共有可能
- **Red Node**: Green をラップ、親・位置情報を持つ

### 構文ノード追加チェックリスト

1. `SyntaxKind` に種別追加
2. `GreenNodes.cs` に Green node 追加
3. `SyntaxNodes.cs` に Red node 追加
4. `SyntaxNode.CreateChild` に対応追加
5. `Parser.cs` にパースロジック追加
6. テスト作成
7. エクスポーター対応（必要な場合）

### pager の無効化

Git コマンドで pager が起動すると止まる。常に `git --no-pager` を使用。

### DLL ロック問題

`Add-Type` 使用後に DLL がロックされたら `Stop-Process -Id $PID` で再起動。

## Spring-Rod モデル

### 核心概念

| 概念 | 説明 |
|------|------|
| Reference Point | 各アイテムの基準点（符頭の中心） |
| LeftExtent | 基準点から左への張り出し（臨時記号） |
| RightExtent | 基準点から右への張り出し（符頭、付点） |
| MinDistance | 衝突回避距離 = PrevRight + NextLeft + Gap |
| IdealDistance | 音価に基づく理想距離 |

### Spring.Length() の動作
```
Length(force) = max(IdealDistance + force/Stiffness, MinDistance)
```
- force > 0: 伸張
- force < 0: 圧縮（MinDistance を下回らない）

## 実装フェーズ

| Phase | 内容 | 状態 |
|-------|------|------|
| 1 | 基本グリフ配置 | ✅ 完了 |
| 2 | Skyline 衝突回避 | ✅ 完了 |
| 3 | 連桁（Beaming） | 🔄 進行中 |
| 4 | タイ・スラー | 未着手 |
| 5 | 和音内臨時記号 | 未着手 |
| 6 | 複数声部 | 未着手 |
| 7-10 | 記譜記号、歌詞、ページレイアウト、高度な機能 | 未着手 |

## 品質基準

1. **視覚的一致**: Lilypond 出力と明らかな差異がないこと
2. **衝突なし**: グリフ同士の重なりがないこと
3. **テスト通過**: 全テストがパスすること
4. **性能**: fur-elise 規模で 10ms 以下

## 関連ドキュメント

| ドキュメント | 内容 |
|------------|------|
| `docs/GRAMMAR.md` | 文法仕様 |
| `docs/SVG_LAYOUT_ARCHITECTURE.md` | レイアウトアーキテクチャ |
| `docs/cross-reference-spec.md` | 相互参照コメント仕様 |
| `docs/spacing-mapping.md` | Lilypond-LilySharp対応表 |
| `docs/COORDINATE_SYSTEM.md` | 座標系の説明 |

## Lilypond ソース参照

| ファイル | 役割 |
|---------|------|
| spacing-options.cc | 定数、get_duration_space() |
| spacing-basic.cc | note_spacing() |
| note-spacing.cc | get_spacing()、skyline計算 |
| spring.cc | Spring クラス |
| simple-spacer.cc | SpringSolver 相当 |
| beam-quanting.cc | 連桁の傾き最適化 |
| tie-formatting-problem.cc | タイの曲線計算 |
| slur-scoring.cc | スラーのスコアリング |
