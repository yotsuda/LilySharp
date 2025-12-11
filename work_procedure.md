# Lily# 作業手順書

## 概要

LilyPond の後継となる楽譜作成ソフトウェア「Lily#」の開発。
C# で実装し、以下の特徴を持つ:

- **シングルパスコンパイル**: Lexer → Parser → SyntaxTree を単一パスで処理
- **インクリメンタルコンパイル**: TextChange API による差分更新
- **Red-Green Tree**: Roslyn スタイルの不変構文木
- **LSP 対応**: VS Code 拡張機能による高品質な編集体験
- **複数出力形式**: MIDI、MusicXML エクスポート

## プロジェクト構造

```
LilySharp/
├── LilySharp.Core/        # コアライブラリ
│   ├── Parser/            # Lexer, Parser
│   ├── Syntax/            # 構文ノード (Red-Green Tree)
│   ├── Semantics/         # 意味解析 (Duration, Measure)
│   ├── Midi/              # MIDI エクスポート
│   └── MusicXml/          # MusicXML エクスポート
├── LilySharp.Lsp/         # Language Server Protocol 実装
├── LilySharp.Cli/         # コマンドラインツール
├── LilySharp.Tests/       # テストプロジェクト
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
# Lilypond レイアウト完全模倣 - 作業手順書

## 概要

LilySharp の SVG レンダリングを **Lilypond と完全に等価** にする。
同じ入力に対して、Lilypond が出力するレイアウトと **ピクセル単位で同一** の結果を得ることを目標とする。

## 目標

```
入力: fur-elise.lys
出力: LilySharp SVG ≡ Lilypond PDF（座標・サイズ・配置が完全一致）
```

## アプローチ

1. Lilypond のソースコード（`C:\MyProj\lilypond-master\lily\`）を精読
2. アルゴリズムを **忠実に** C# で再実装（簡略化しない）
3. Lilypond 出力と差分比較し、差異をゼロにする

## スコープ

### 対象
- Lilypond の **全て** の視覚的レイアウトアルゴリズム
- グリフ配置、スペーシング、衝突回避の完全再現
- 曲線（タイ・スラー）の制御点計算の完全再現
- 改行・ページ分割の完全再現

### 対象外
- MIDI 出力（別プロジェクト）
- Scheme インタプリタおよび Scheme 依存機能
- 処理時間が長くなる機能（動的評価など）

## 設計方針

### 結果等価・プロセス独自

```
目標: Lilypond 出力 ≡ LilySharp 出力（ピクセル単位）
手段: 独自実装可（Lilypond のコードをそのまま移植する必要なし）
```

### 処理時間優先

| Lilypond | LilySharp |
|----------|-----------|
| Scheme で動的計算 | C# で静的計算 |
| 汎用アルゴリズム | 特化アルゴリズム |
| 後方互換性維持 | 新規設計 |

### 表記変更の許容

処理時間短縮のため、LilySharp 独自の表記を採用する場合がある：

| Lilypond | LilySharp | 理由 |
|----------|-----------|------|
| 小節線自動挿入 | 小節線必須 | パース簡略化 |
| `\override` 動的評価 | 静的プロパティ | Scheme 不要化 |
| （今後追加） | | |

### 移植しない機能

- Scheme callback による動的スタイル変更
- `\override` の実行時評価
- その他、処理時間を著しく増加させる機能

これらは静的な代替手段を提供するか、サポート外とする。

## 実装フェーズ

### Phase 1: 基本グリフ配置 ✅ 完了
- 符頭・符幹・旗・休符
- 臨時記号配置
- 付点配置（線上回避）

### Phase 2: Skyline ベース衝突回避 ✅ 完了
- Skyline クラス（矩形近似）
- 音符間の MinDistance 計算
- SMuFL glyph metrics 参照

### Phase 3: 連桁（Beaming）
- 連桁グループの検出
- 傾き計算（beam-quanting.cc 参照）
- 連桁と符幹の接続

### Phase 4: タイ・スラー
- タイの曲線計算（tie-formatting-problem.cc 参照）
- スラーの曲線計算（slur-scoring.cc 参照）
- 衝突回避

### Phase 5: 和音内臨時記号
- 複数臨時記号のスタッキング（accidental-placement.cc 参照）
- 衝突回避とスペーシング

### Phase 6: 複数声部
- Voice の分離
- 衝突回避（note-collision.cc 参照）
- 符幹方向の自動決定

### Phase 7: 記譜記号
- 音部記号（clef.cc）
- 調号（key-signature-interface.cc）
- 拍子記号（time-signature-*.cc）

### Phase 8: 歌詞配置
- 歌詞と音符の紐付け
- ハイフン・エクステンダー
- 複数番の歌詞

### Phase 9: ページレイアウト最適化
- Knuth-Plass 行分割（page-layout-problem.cc 参照）
- ページ分割最適化（page-spacing.cc）
- 余白・マージン調整

### Phase 10: 高度な機能
- ダイナミクス（強弱記号）
- アーティキュレーション
- 装飾音
- 繰り返し記号
- トレモロ

## 品質基準

1. **視覚的一致**: Lilypond 出力と並べて比較し、明らかな差異がないこと
2. **衝突なし**: グリフ同士の重なりがないこと
3. **テスト通過**: 全ての既存テストが通過すること
4. **性能**: fur-elise 規模で 10ms 以下

## リスク

| リスク | 対策 |
|--------|------|
| Lilypond のアルゴリズムが複雑すぎる | 簡略化版を実装し、段階的に精度向上 |
| Scheme 依存部分がある | C# で同等ロジックを再実装 |
| 性能劣化 | ベンチマークを継続監視 |

## コミットポリシー

- テスト全通過 AND ユーザーレビュー承認後にコミット
- feature/smufl-spacing ブランチで作業
- 機能単位でコミット（英語一文）

## 進捗更新ルール

- 作業進捗があるたびに work_progress.md を即座に更新
- ステータス変更、タスク完了時に必ず更新

## 学習更新ルール

- 実装中に得た知見は本ファイルに追記
- Lilypond ソースの重要な参照箇所を記録

## Lilypond ソース参照メモ

### Skyline
- `skyline.cc`: Building 構造体、distance() メソッド
- 斜めの建物（slope）をサポート - LilySharp は矩形近似

### Beaming
- `beam.cc`: 連桁の描画
- `beam-quanting.cc`: 連桁の傾き最適化（41KB、最も複雑）

### Tie/Slur
- `tie-formatting-problem.cc`: タイの曲線計算（38KB）
- `slur-scoring.cc`: スラーのスコアリング（26KB）

### Spacing
- `note-spacing.cc`: 音符間隔
- `spacing-spanner.cc`: スペーシング全体制御

## 参照ドキュメント

### LilySharp 内部ドキュメント

詳細なアーキテクチャは以下を参照:
- **`docs/SVG_LAYOUT_ARCHITECTURE.md`** - 3層アーキテクチャ、Spring-Rod モデル、実装ノート

### Roslyn 参照ファイル（重要）

Roslyn の Formatting Engine を参考にすべき点:

| Roslyn パス | 参考ポイント |
|------------|-------------|
| `src/Workspaces/Core/Portable/Formatting/` | フォーマッティング全般 |
| `src/.../Formatting/Engine/TokenStream` | トークン間の間隔計算 |
| `src/.../Formatting/Engine/ChainedFormattingRules` | ルールの連鎖適用 |
| `src/Workspaces/Core/Portable/Workspace/Solution/DocumentState` | インクリメンタル更新 |

**Roslyn ソースパス**: `C:\MyProj\roslyn`

### Lilypond 参照ファイル

| Lilypond パス | 役割 |
|--------------|------|
| `lily/spring.cc` | バネの定義（理想距離、最小距離、伸縮性） |
| `lily/simple-spacer.cc` | 制約ソルバー（バネとロッドから位置計算） |
| `lily/spacing-spanner.cc` | バネとロッドの生成 |
| `lily/note-spacing.cc` | 音符間のバネ生成 |
| `lily/skyline.cc` | Skyline（衝突検出） |
| `lily/beam-quanting.cc` | 連桁の傾き最適化 |
| `lily/tie-formatting-problem.cc` | タイの曲線計算 |
| `lily/slur-scoring.cc` | スラーのスコアリング |

**Lilypond ソースパス**: `C:\MyProj\lilypond-master`

## 設計原則（SVG_LAYOUT_ARCHITECTURE.md より）

1. **Immutability**: 全てのドメインオブジェクトは不変（record）
2. **Separation of Concerns**: 収集・レイアウト・描画を完全分離
3. **Lazy Evaluation**: レイアウト計算は必要時に実行
4. **Cacheability**: 小節単位でキャッシュ可能
5. **Single Pass**: 構文木は1回だけ走査
6. **Lilypond equality**: Lilypond のロジックと等価なものを実装

## Spring-Rod モデル 気づき（実装時の注意）

### 気づき 1: アイテム幅 vs 間隔距離
- 従来: アイテム自体の幅を計算
- Spring-Rod: 隣接アイテム間の距離を扱う
- 臨時記号が**左側**に張り出す問題を正しく扱える

### 気づき 2: Reference Point
- 各アイテムに基準点（符頭の中心など）
- LeftExtent: 基準点から左への張り出し（臨時記号）
- RightExtent: 基準点から右への張り出し（符頭、付点）

### 気づき 3: MinDistance による衝突回避
```
MinDistance = PrevItem.RightExtent + NextItem.LeftExtent + MinGap
```
- 衝突回避が**暗黙的に**行われる
- 特別な衝突チェックロジック不要

### 気づき 4: Spring.Length() の動作
```
Length(force) = max(IdealDistance + force/Stiffness, MinDistance)
```
- force > 0: 伸張
- force < 0: 圧縮（MinDistance を下回らない）
- force = 0: IdealDistance と MinDistance の大きい方