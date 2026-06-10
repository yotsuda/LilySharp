# LilySharp プロジェクト評価
> **注 (2026-06-10):** 本評価は 2026-02 時点。その後の文法刷新（volta 統一・phrase 一本化・@レジストリ化）は HANDOFF_2026-06-09.md と docs/GRAMMAR.md を参照。

評価日: 2026-02-22 (更新: 2026-02-23 Phase A-F 完了反映)

---

## 1. プロジェクト概要

**LilySharp** は C# (.NET 9.0) で書かれた音楽記譜コンパイラ。LilyPond にインスパイアされつつ、Roslyn スタイルの Red-Green Tree アーキテクチャと LSP (Language Server Protocol) を採用したモダンな設計の楽譜作成ツール。

- **ファイル拡張子:** `.lys`
- **CLI ツール名:** `lysc` (v0.2.0)
- **出力形式:** SVG / MIDI / MusicXML
- **IDE 統合:** LSP サーバー + VS Code 拡張

---

## 2. プロジェクト規模

| 指標 | 値 |
|---|---|
| C# ソースファイル | 230 ファイル |
| 総コード行数 | 約 52,000 行 |
| テスト数 | 1,004 Passed / 0 Failed / 2 Skipped |
| Git コミット | 287+ |
| プロジェクト構成 | Core / CLI / LSP / Tests の 4 プロジェクト |
| サンプルファイル | 54 .lys ファイル |
| ドキュメント | 13 設計文書 |

---

## 3. アーキテクチャ

### 3.1 プロジェクト構造

```
LilySharp/
├── LilySharp.Core/          # コアコンパイラライブラリ
│   ├── Parser/              # Lexer + 再帰下降パーサー
│   ├── Syntax/              # Red-Green Tree 構文ノード
│   │   └── InternalSyntax/  # Green ノード (不変)
│   ├── Semantics/           # 意味解析・バインディング
│   ├── Svg/                 # SVG 楽譜描画 (87 ファイル)
│   │   ├── Collector/       # 音楽情報抽出
│   │   ├── Layout/          # レイアウトエンジン
│   │   ├── Model/           # ドメインモデル
│   │   └── Renderer/        # SVG 生成
│   ├── Midi/                # MIDI エクスポート
│   ├── MusicXml/            # MusicXML エクスポート
│   ├── Tablature/           # ギター/ベース/ウクレレ譜
│   ├── Fonts/               # Emmentaler フォント
│   └── Text/                # テキストレイアウト
├── LilySharp.Cli/           # CLI (lysc)
├── LilySharp.Lsp/           # Language Server Protocol
├── LilySharp.Tests/         # xUnit テスト
├── editors/vscode/          # VS Code 拡張 (TypeScript)
├── samples/                 # サンプル楽譜
└── docs/                    # 設計ドキュメント
```

### 3.2 コンパイルパイプライン

```
ソース (.lys)
  → Lexer (トークン化)
  → Parser (Red-Green Tree 構築)
  → SemanticCompiler (意味解析)
      → SymbolCollector (宣言収集)
      → Binder (参照解決)
      → ScoreBuilder (スコアモデル構築)
  → 出力
      → SvgGenerator (楽譜描画)
      → MidiExporter (音声)
      → MusicXmlExporter (交換形式)
```

### 3.3 主要ファイル (行数上位)

| ファイル | 行数 | 役割 |
|---|---|---|
| SvgRenderer.cs | 3,560 | SVG 楽譜描画メイン |
| LilySharpLanguageServer.cs | 1,729 | LSP サーバー |
| Parser.cs | 1,570 | 再帰下降パーサー |
| MeasureCollector.cs | 1,541 | 小節情報収集 |
| SyntaxNodes.cs | 1,411 | 構文ノード定義 |
| SpacingRules.cs | 949 | 水平スペーシング |
| GreenNodes.cs | 902 | 不変構文ノード |
| BeamScoringProblem.cs | 891 | ビームスコアリング |
| VerticalSkyline.cs | 671 | 垂直方向衝突検出 |
| SlurScoringProblem.cs | 661 | スラースコアリング |
| PdfRenderer.cs | 577 | PDF 出力 |
| PageBreaker.cs | 526 | ページ分割最適化 |
| TieFormattingProblem.cs | 516 | タイフォーマッティング |
| Lexer.cs | 524 | 字句解析 |

---

## 4. 実装済み機能

### 4.1 言語機能

- 音高 (`c d e f g a b`)、臨時記号 (`cis dis ees`)、オクターブ (`c' c,,`)
- 音価 (`1 2 4 8 16 32 64 128 breve longa`)、付点 (`4. 4..`)
- 和音 (`<c e g>4`)、連符 (`tuplet 3/2 { ... }`)
- 装飾音 (`grace { c16 d } e4`)
- タイ (`c4~ c`)、スラー (`c4( d e)`)
- アーティキュレーション (`@staccato @accent @fermata`)
- ダイナミクス (`@pp @p @mp @mf @f @ff @fff`)
- 並行声部 (`<< { } \\ { } >>`)
- 歌詞 (`lyrics { Hap -- py }`)
- 手動ビーム (`c8[ d e f]`)
- 反復 (`|: ... :|` (+`:|*N`)、インライン volta `[1. ...] [2. ...]`。旧 `repeat volta`/`alternative` は 2026-06 に削除)
- パート定義・セクション・ストラクチャ構文
- フレーズ (再利用可能な楽句)

### 4.2 出力形式

- **SVG:** Emmentaler フォント使用、マルチシステムレイアウト
- **PDF:** PdfSharpCore による PDF 出力 (A4/Letter, フォント埋め込み)
- **MIDI:** ダイナミクス・アーティキュレーション対応
- **MusicXML:** 基本エクスポート (マルチセクション未完)

### 4.3 IDE 統合 (LSP 13+ 機能)

- リアルタイム診断、コード補完、ホバー情報
- ドキュメントシンボル、定義ジャンプ、参照検索
- セマンティックハイライト、折りたたみ
- リネーム、フォーマット、コードアクション、シグネチャヘルプ

---

## 5. コード品質評価

| 項目 | 評価 | 備考 |
|---|---|---|
| ビルド | **A** | 警告 0、エラー 0 |
| テスト | **A** | 1,004/1,006 パス (0 失敗) |
| アーキテクチャ | **A** | Roslyn パターン、明確な関心分離 |
| ドキュメント | **A-** | 設計文書充実、ユーザードキュメントは未整備 |
| 型安全性 | **A** | C# record 型・不変データ構造を活用 |
| 拡張性 | **B+** | モジュール分離は良好、プラグインシステムは未整備 |
| CI/CD | **C** | 自動ビルド/テスト設定なし |

---

## 6. LilyPond との比較

### 6.1 LilyPond の規模

LilyPond は 1996 年から 30 年以上開発されている GNU プロジェクト。

| 指標 | LilyPond | LilySharp |
|---|---|---|
| 開発期間 | 30+ 年 | - |
| コア言語 | C++ / Scheme / Python | C# |
| C++/C# コード行数 | 122,498 行 | 52,334 行 |
| Scheme 層 | 51,839 行 | (該当なし) |
| 総コード量 | ~174,000+ 行 | ~52,000 行 |
| パーサー | 4,862 行 (Bison) + 1,387 行 (Flex) | 1,570 行 + 524 行 |
| ソースファイル数 | 450 .cc + 230 .hh + 86 .scm | 230 .cs |
| 回帰テスト | 1,894 .ly ファイル | 1,004 ユニットテスト + 54 .lys |
| Engraver 数 | 120 | (統合設計で ~13) |
| Performer 数 | 16 | (MidiExporter に統合) |
| MetaFont ソース | 103 .mf | (Emmentaler WOFF2 利用) |
| 出力形式 | PDF / PS / SVG / PNG / MIDI | SVG / PDF / MIDI / MusicXML |
| ドキュメント | 319 章 (11 言語) | 10+ 設計文書 |

### 6.2 浄書機能カバー率

LilyPond の 120 Engraver に対する LilySharp の対応状況:

| カテゴリ | LilyPond | LilySharp | カバー率 |
|---|---|---|---|
| 音符/符頭/符幹 | note-heads, dots, stem, rest, collision | SvgRenderer 統合 + StemCalculator + NoteCollision (LP shift値) | ~90% |
| 拍子/調号/音部記号 | clef, key, time-signature | 対応済み | ~80% |
| ビーム | beam, beam-collision, auto-beam | BeamDetector + BeamScoringProblem (LP定数準拠) | ~75% |
| 小節線 | bar, span-bar | SystemBarlineRenderer | ~60% |
| スペーシング | spacing, note-spacing, separating-line-group | SpacingRules (common-shortest-duration動的計算) + SpringSolver | ~65% |
| Skyline 衝突検出 | stencil-integral (990行) | VerticalSkyline (671行) + skyline-based staff spacing | ~60% |
| 歌詞 | lyric, hyphen, extender, stanza-number | LyricCollector + 改善フォントメトリクス | ~55% |
| スラー/タイ | slur, tie, phrasing-slur, laissez-vibrer, bend | SlurScoringProblem (LP scorer順序) + TieFormattingProblem (方向判定・dot collision) | ~70% |
| アーティキュレーション | script, fingering, arpeggio, dynamic | ArticulationEngraver + DynamicEngraver + ArpeggioEngraver (LP protrusion) | ~60% |
| タブラチュア | tab-note-heads, tab-staff-symbol, fretboard | Tablature モジュール | ~40% |
| 装飾音 | grace, grace-spacing | GraceSpacingParameters 対応 | ~40% |
| 反復記号 | repeat-acknowledge, volta, percent-repeat | StructureExpander + PercentRepeatEngraver | ~45% |
| ページレイアウト | page-breaking (1,768行) | PageBreaker (LP定数) + PageLayouter + vertical loose lines | ~55% |
| ヘアピン | hairpin, dynamic-engraver | HairpinEngraver (broken hairpin heights + endpoint alignment) | ~70% |
| テキストスパナー | text-spanner, line-spanner | TextSpannerEngraver | ~50% |
| オッターバ | ottava-bracket, ottava-engraver | OttavaBracketEngraver | ~60% |
| グリッサンド | glissando, glissando-engraver | GlissandoEngraver | ~50% |
| リハーサルマーク | mark-engraver | MusicMarkType.Rehearsal + StackGap (LP準拠) | ~55% |
| ピアノペダル | piano-pedal, piano-pedal-bracket | PedalEngraver | ~50% |
| 通奏低音 | figured-bass, figured-bass-position | FiguredBassEngraver | ~40% |
| コードネーム | chord-name | ChordNameEngraver | ~40% |
| パート結合 | part-combine | PartCombineAnalyzer | ~40% |
| 連符ブラケット | tuplet-bracket, tuplet-engraver | TupletBracketEngraver (bracket-visibility, slope計算) | ~60% |
| トレモロ | stem-tremolo | TremoloEngraver + SvgRenderer (LP動的幅/傾斜 + stem extension) | ~65% |
| Kneed beams | beam (auto-knee-gap) | BeamGroup.IsKnee | ~40% |
| Feathered beams | beam (grow-direction) | BeamGroup.GrowDirection | ~40% |
| Cross-staff | beam/stem/slur cross-staff | CrossStaffEngraver | ~30% |
| Grob override | grob property system | GrobPropertyResolver (パイプライン接続済み) | ~45% |
| 装飾音記号 | script-interface | OrnamentEngraver (quantize-position準拠) | ~55% |
| 臨時記号配置 | accidental-placement | AccidentalPlacement (skyline collision + priority sorting) | ~60% |
| 古代記法 | gregorian, kievan, vaticana, mensural (7個) | 未対応 | 0% |
| バルーン注釈 | balloon | 未対応 | 0% |
| フットノート | footnote | 未対応 | 0% |

**Engraver 機能カバー率: 約 55-60%** (全53サンプルSVG検証済み)

### 6.3 アルゴリズム比較

| アルゴリズム | LilyPond (行数) | LilySharp (行数) | 到達度 |
|---|---|---|---|
| Beam quanting | 1,554 + 1,403 (2ファイル) | 891 (BeamScoringProblem) + LP定数準拠 | ~40% |
| Slur scoring | 906 | 661 (SlurScoringProblem) + LP scorer順序 + staff line avoidance | ~80% |
| Tie formatting | 1,286 | 516 (TieFormattingProblem) + 方向判定 + dot collision | ~55% |
| Page breaking | 1,768 (最適化) | 526 (PageBreaker) + PageLayouter + LP demerit formula | ~50% |
| Spacing | spacing-spanner + simple-spacer | SpacingRules (949行) + SpringSolver (321行) + common-shortest-duration | ~65% |
| Skyline collision | 990 | VerticalSkyline (671行) + skyline-based staff spacing | ~60% |
| Stem/Notehead | 1,258 | StemCalculator (237行) + SvgRenderer + tremolo stem extension | ~55% |
| Note collision | note-collision-interface (400行) | NoteCollision + LP shift multipliers (0.52/0.5/0.4/0.65) | ~60% |
| Accidental placement | accidental-placement (800行) | AccidentalPlacement + skyline collision + priority sorting | ~50% |

### 6.4 出力バックエンド比較

| 形式 | LilyPond | LilySharp |
|---|---|---|
| PDF | Cairo 経由 (1,535行) | PdfSharpCore (577行) |
| PostScript | ネイティブ | 未対応 |
| SVG | framework-svg.scm | **主力出力** (SvgRenderer 3,560行) |
| PNG | Ghostscript 経由 | 未対応 |
| MIDI | 完全実装 | 対応済み |
| MusicXML | インポート対応 | エクスポート対応 (部分的) |

### 6.5 LilySharp が LilyPond を上回る領域

| 領域 | 説明 |
|---|---|
| **LSP / IDE 統合** | LilyPond にはネイティブ LSP がない。LilySharp は 13+ 機能を実装 |
| **インクリメンタルパース** | Red-Green Tree により設計レベルで対応。LilyPond は毎回フルパース |
| **ビルド容易性** | `dotnet build` のみ vs Autotools + Bison + Flex + Guile + FreeType + Pango |
| **単一言語** | C# 一本で完結 vs C++/Scheme/Python/MetaFont の混在 |
| **テスト構造** | 構造化された xUnit テスト (全パス) |
| **モダンな型システム** | C# record 型、パターンマッチング、null 安全性 |

---

## 7. 総合到達度

| 領域 | 到達度 | 評価 |
|---|---|---|
| パーサー/文法 | **40-45%** | 主要構文+override/revert対応。Scheme 埋め込み等は未対応 |
| 浄書品質 | **60-65%** | Phase A-F で LP ソース準拠の定数・アルゴリズム修正完了。全53サンプルSVG検証済み |
| レイアウトアルゴリズム | **55-60%** | force-based demerit + common-shortest-duration + skyline staff spacing + vertical loose lines |
| 楽器/記法対応 | **45-50%** | トレモロ/ヘアピン/オッターバ/ペダル/アルペジオ/グリッサンド等 LP 値準拠 |
| 出力形式 | **50%** | SVG + PDF + MIDI 対応。PS/PNG は未対応 |
| IDE 統合 | **300%+** | LilyPond を大幅に凌駕 |
| テスト | **~53%** | 1,004 テスト (Phase A-F で 139 テスト追加)。回帰テスト数は依然 LilyPond に劣る |
| コード規模 | **~30%** | 174K 行 vs 52K 行 |

### **総合到達度: 約 55-60% (コア機能ベース、全SVG検証済み)**

---

## 8. 品質改善実績 (Phase A-F)

30ユニットの LP ソース監査に基づく品質改善ロードマップを Phase A-F で全て完了。

| Phase | 内容 | 主な変更 |
|---|---|---|
| **A** | 定数修正 (低リスク) | NoteCollision shift multipliers、Page breaking 定数、common-shortest-duration 動的計算 |
| **B** | コアアルゴリズム | System breaking force-based demerit formula、Beam CollisionPadding/FIXED_DEMERIT/FUDGE、Slur scorer LP 順序 |
| **C** | インフラ接続 | GrobPropertyResolver パイプライン接続 (override/revert がレンダリングに反映) |
| **D** | Engraver 改善 | Broken hairpin heights、OrnamentEngraver quantize-position、Tie direction/dot collision、Accidental skyline collision |
| **E** | レイアウト改善 | Tuplet bracket (bracket-visibility/slope)、Skyline-based staff spacing、Vertical layout loose lines |
| **F** | 特殊ケース | Tremolo 動的幅/傾斜 + stem extension、Arpeggio protrusion、MusicMark StackGap、Lyrics font metrics |

**検証**: 全 1,004 テストパス、53 サンプル SVG 生成・目視確認完了。

---

## 9. 到達度向上のための優先領域

| 優先度 | 領域 | 理由 |
|---|---|---|
| **高** | Beam quanting 深化 | LilyPond の 2,957 行に対して 891 行。forbidden quants 等の定量化にまだ差 |
| **高** | 回帰テスト拡充 | LilyPond 1,894 件 vs LilySharp 1,004 件。リファレンス比較の自動化 |
| **中** | Cross-staff 完全化 | beam/stem/slur の cross-staff は基本実装のみ |
| **中** | Grob override 深化 | パイプライン接続済みだが property lookup chain の完全化が必要 |
| **中** | Musical/non-musical columns 分離 | LP の column 概念の完全実装 (アーキテクチャ変更) |
| **中** | Collector/Engraver 分離 | MeasureCollector のモノリシック構造を LP 流に分離 |
| **低** | 古代記法 | 唯一の未着手カテゴリ。ニッチ機能 |
| **低** | PostScript / PNG 出力 | SVG + PDF で多くのユースケースをカバー |
