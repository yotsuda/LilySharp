# LilySharp 文法設計実装 - 進捗管理

## 全体進捗

| フェーズ | 状態 | 完了 | 合計 | 進捗率 |
|----------|:----:|-----:|-----:|-------:|
| Phase 1: ダイナミクス (`@p`) | ✅ | 4 | 4 | 100% |
| Phase 2: オクターブ管理 | ✅ | 4 | 4 | 100% |
| Phase 3: treble_8 Clef | 🔍 | 10 | 10 | 100% |
| **合計** | | 18 | 18 | 100% |

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

## Phase 3: treble_8 Clef 実装 🔍レビュー待ち

ギター・テノール声部用の treble_8 clef（ト音記号 + 下に "8"）を実装。

### 変更ファイル一覧

| filename | status | notes |
|----------|:------:|-------|
| LilySharp.Core/Syntax/SyntaxKind.cs | 🔍 | `Treble8Keyword` 追加 |
| LilySharp.Core/Parser/Lexer.cs | 🔍 | `"treble_8"` → `Treble8Keyword` 追加 |
| LilySharp.Core/Parser/Parser.cs | 🔍 | `IsClefKeyword` に `Treble8Keyword` 追加 |
| LilySharp.Core/Svg/Model/Staff.cs | 🔍 | `ClefType.Treble8Below` 追加、`ParseClef` 両方更新 |
| LilySharp.Core/Svg/Model/InstrumentDefaults.cs | 🔍 | guitar→`(Treble8Below, 4)`, tenor→`(Treble8Below, 4)`, `GetDefaultOctave` 追加 |
| LilySharp.Core/Svg/Collector/MeasureCollector.cs | 🔍 | `CalculateStaffPosition`, `ParseClefType`, `GetPartDefaults` 更新 |
| LilySharp.Core/Svg/Collector/RenderSpecParser.cs | 🔍 | `ParseStaff`, `GetPartClef` に treble_8 追加 |
| LilySharp.Core/Svg/Layout/LayoutEngine.cs | 🔍 | ClefType→string 変換に `Treble8Below` 追加 |
| LilySharp.Core/Svg/Renderer/SvgRenderer.cs | 🔍 | multi-staff/single-staff 両方で GClef + "8" テキスト描画 |
| LilySharp.Tests/OctaveTests.cs | 🔍 | guitar テスト更新、treble_8 パーステスト・tenor テスト追加 (計34テスト全通過) |

### 設計判断
- **グリフ**: 通常の GClef + `<text>8</text>` で描画（"8" は italic font-size 1.2、clef 下部 y+5.2）
- **譜面位置計算**: treble と同一（B4 = position 0）
- **初期オクターブ**: treble_8 のデフォルト = 4（treble と同じ書記音高）
- **楽器変更**: guitar は `(Treble, 3)` → `(Treble8Below, 4)`、tenor も同様

### テスト結果
- `dotnet build`: 成功 (0 warnings, 0 errors)
- `dotnet test --filter OctaveTests`: 34/34 passed
- `dotnet test` (全体): 446 passed, 0 failed, 2 skipped

### 残作業
- [x] SVG 出力の目視確認: treble8-test.svg で 3 staff すべてに "8" 表示確認済み
- [x] RenderSpecParser.GetPartClef() バグ修正: instrument プロパティから clef 推定ロジック追加
- [ ] feature-demo.lys などに treble_8 使用例追加（任意）

---

## Phase 1: ダイナミクス表記変更 (`\p` → `@p`) ✅完了

| filename | status | notes |
|----------|:------:|-------|
| LilySharp.Core/Parser/Parser.cs | ✅ | @p, @f 対応完了 |
| LilySharp.Core/Svg/Renderer/SvgRenderer.cs | ✅ | フォントサイズ調整完了 |
| LilySharp.Tests/ParserTests.cs | ✅ | 新構文テスト3件追加 |
| samples/dynamics-test.lys | ✅ | テストサンプル作成 |

---

## Phase 2: オクターブ管理改善 ✅完了

| filename | status | priority | effort | notes |
|----------|:------:|:--------:|-------:|-------|
| LilySharp.Core/Svg/Model/InstrumentDefaults.cs | ✅ | High | 1h | 楽器マッピング実装済み |
| LilySharp.Core/Syntax/SyntaxNodes.cs | ✅ | Normal | 0.5h | PartDeclaration拡張済み |
| LilySharp.Core/Svg/Collector/MeasureCollector.cs | ✅ | High | 2h | instrument→clef/octave推定、octave属性、セクションリセット実装 |
| LilySharp.Tests/OctaveTests.cs | ✅ | Normal | 1h | 34テスト作成・全通過 |

---

## 完了済み作業（参考）

| 日付 | 内容 |
|------|------|
| 2026-02-06 | Phase 1完了: ダイナミクス @p 記法 |
| 2026-02-06 | 文法設計見直し: プレフィックス廃止、キーワード採用 |
| 2026-02-06 | GRAMMAR_ANALYSIS.md 更新 |
| 2026-02-06 | Tuplet Bracket 実装完了 |
| 2026-02-06 | Phase 2: OctaveTests 29テスト、instrument→clef/octave推定、octave属性、セクションリセット実装 |
| 2026-02-06 | Phase 3: treble_8 clef 実装 (10ファイル変更、全446テスト通過) |
| 2026-02-06 | Phase 3 バグ修正: RenderSpecParser.GetPartClef() に instrument→clef 推定追加、treble8-test.svg 目視確認OK |