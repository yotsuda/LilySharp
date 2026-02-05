# LilySharp 文法設計実装 - 進捗管理

## 全体進捗

| フェーズ | 状態 | 完了 | 合計 | 進捗率 |
|----------|:----:|-----:|-----:|-------:|
| Phase 1: ダイナミクス (`@p`) | ⏳ | 2 | 4 | 50% |
| Phase 2: 変数参照 (`$name`) | 🚀 | 0 | 4 | 0% |
| Phase 3: オクターブ管理 | 🚀 | 0 | 4 | 0% |
| **合計** | | 0 | 12 | 0% |

---

## ステータス凡例

| 状態 | 意味 | ワークフロー |
|:----:|------|--------------|
| 🚀 | NotStarted | 未着手 |
| ⏳ | Working | AI作業中 |
| 🔍 | Review | AI完了、レビュー待ち |
| ✅ | Complete | ユーザー承認済み |
| 🟡 | Hold | 保留中 |
| ❌ | Error | エラー発生 |

**フロー**: 🚀 → ⏳ → 🔍 → ✅

---

## Phase 1: ダイナミクス表記変更 (`\p` → `@p`)

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| LilySharp.Core/Parser/Lexer.cs | 🚀 | High | 2h | @p, @f 等のトークン追加 |
| LilySharp.Core/Parser/Parser.cs | ✅ | High | 1h | @p, @f 対応完了 |
| LilySharp.Core/Syntax/SyntaxKind.cs | 🚀 | High | 0.5h | 新トークン種別追加 |
| LilySharp.Tests/ParserTests.cs | ✅ | Normal | 1h | 新構文テスト3件追加 |

---

## Phase 2: 変数参照変更 (`name` → `$name`)

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| LilySharp.Core/Parser/Lexer.cs | 🚀 | High | 1h | $プレフィックス認識 |
| LilySharp.Core/Parser/Parser.cs | 🚀 | High | 1h | 変数参照構文更新 |
| LilySharp.Core/Svg/Collector/MeasureCollector.cs | 🚀 | High | 1h | 参照展開ロジック |
| samples/*.lys | 🚀 | Normal | 2h | サンプルファイル更新 |

---

## Phase 3: オクターブ管理改善

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| LilySharp.Core/Svg/Model/InstrumentDefaults.cs | 🚀 | High | 1h | 新規: 楽器マッピング |
| LilySharp.Core/Syntax/SyntaxNodes.cs | 🚀 | Normal | 0.5h | PartDeclaration拡張 |
| LilySharp.Core/Svg/Collector/MeasureCollector.cs | 🚀 | High | 2h | セクションリセット実装 |
| LilySharp.Tests/OctaveTests.cs | 🚀 | Normal | 1h | 新規: オクターブテスト |

---

## 完了済み作業（参考）

| 日付 | 内容 |
|------|------|
| 2026-02-06 | Tuplet Bracket 実装完了 |
| 2026-02-06 | GRAMMAR_ANALYSIS.md 作成 |


