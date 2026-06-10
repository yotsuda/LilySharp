# LilySharp アーキテクチャ再設計
> **STATUS (2026-06-10): historical redesign plan.** Since this was written,
> the `repeat volta` / `alternative` keywords were removed from the language
> (volta repeats are `|: … :|` (+`:|*N`) with inline voltas `[1. …]`), and the
> semantic layer described here (SemanticCompiler/BoundTree/ScoreBuilder) was
> built, never wired into any output pipeline, and **deleted on 2026-06-10**
> (recoverable from git history). The live pipeline is
> MeasureCollector → LayoutEngine → SharedRenderer; shared semantics are
> being extracted from it instead (e.g. `Semantics/RelativeOctave.cs`).
> See LILYSHARP_STANDALONE_REVIEW.md §1/§2 for the analysis that led here.

## ビジョン

**世界でいちばん美しいデザインの楽譜作成ソフトウェア**

LilyPond の高品質レイアウトアルゴリズムを、Roslyn の美しいコンパイラ設計と融合し、
高速かつ保守性の高い楽譜エンジンを実現する。

## 設計原則

| 原則 | 説明 |
|------|------|
| **Immutable by Default** | すべてのモデルは不変。変更は新しいインスタンスを生成 |
| **Separation of Concerns** | 各層は単一の責務を持つ |
| **Fail Fast** | エラーは早期に検出し、診断メッセージで報告 |
| **Incremental Ready** | 将来の増分コンパイルに対応可能な設計 |

---

## 調査から得た学び

### LilyPond のアーキテクチャ (C:\MyProj\lilypond-src)

```
Parser → Music Expression (SCM objects)
              ↓
         Music Iterator (時間順評価、遅延実行)
              ↓
         Context + Engravers (Stream Events → Grobs)
              ↓
         Grobs → Paper_column → Page Layout
              ↓
         Output (PDF/SVG/etc)
```

**主要ファイル:**
- `lily/music.cc` - Music は duration と pitch を持つ抽象概念
- `lily/music-sequence.cc` - Sequential/Simultaneous の長さ計算
- `lily/music-iterator.cc` - 時間順評価の基底クラス
- `lily/sequential-iterator.cc` - 順次音楽の評価
- `lily/volta-repeat-iterator.cc` - リピート記号の処理
- `lily/alternative-sequence-iterator.cc` - 1番カッコ/2番カッコの処理
- `lily/context.cc` - 状態を保持する環境 (29,407行)
- `lily/engraver.cc` - Stream Events → Grobs 変換
- `lily/grob.cc` - Graphical Object (29,016行)

**重要な洞察:**
1. Iterator パターンは **Scheme の遅延評価** に対応するため必要
2. `repeat unfold` は Parser 段階で展開される (scm/music-functions.scm)
3. `repeat volta` は Iterator が Runtime で処理
4. Context は Translator (Engraver) のホスト環境

### Roslyn のアーキテクチャ (C:\MyProj\roslyn)

```
SourceText → Lexer → SyntaxTree (Red-Green Tree)
                          ↓
                     Binder (Name Resolution + Type Checking)
                          ↓
                     BoundTree (Semantic Tree)
                          ↓
                     Lowering → Emit (IL/PDB)
```

**主要ファイル:**
- `Binder/Binder.cs` - Chain of Responsibility パターン
- `Binder/Binder_Expressions.cs` - 式のバインディング (606,368行!)
- `Binder/Binder_Symbols.cs` - シンボル解決
- `BoundTree/BoundNode.cs` - BoundTree の基底クラス
- `BoundTree/BoundExpression.cs` - 評価済み式
- `Symbols/Symbol.cs` - シンボルの基底クラス
- `Compilation/CSharpSemanticModel.cs` - 公開 API

**重要な洞察:**
1. SyntaxTree は **完全に immutable** で、エラーがあっても構築される
2. Binder は **Syntax → BoundTree** の変換を担当
3. BoundTree は **型情報とシンボル参照を持つ** Semantic Tree
4. SemanticModel は **lazy evaluation** で必要な部分だけ計算

### LilySharp への適用

| LilyPond 概念 | Roslyn 概念 | LilySharp 実装 |
|---------------|-------------|----------------|
| Music Expression | SyntaxNode | SyntaxNode (既存) |
| Music Iterator | - | **不要** (事前展開) |
| Context | Binder | Binder + SymbolTable |
| Stream Event | BoundNode | BoundMusic |
| Engraver | Lowering | LayoutEngine (既存) |
| Grob | - | MeasureLayout, BeamLayout, etc. |

**重要な決定:**

1. **MusicIterator は不要**
   - LilyPond の Iterator は Scheme の遅延評価のため必要
   - LilySharp は高速化が目標、Scheme 機能なし
   - BoundMusic を直接 LayoutEngine に渡せばよい

2. **Structure は事前展開**
   - LilyPond: `repeat volta` は Runtime で Iterator が処理
   - LilySharp: Binder 段階で展開 → 高速化

3. **Grob 概念は採用しない**
   - LilySharp は既に Layout + Renderer が分離
   - Grob の動的プロパティ解決は不要

---

## 現状の問題

```
Parser → SyntaxTree → MeasureCollector → Score → LayoutEngine → SvgRenderer
                            ↑
                       複数の責務が混在:
                       1. Symbol resolution (section, phrase, variable)
                       2. Structure expansion (repeat, alternative)
                       3. Time calculation (relative pitch, duration)
                       4. Music collection (notes, rests, chords)
                       5. Metadata extraction (title, composer, tempo)
```

**問題点:**
1. MeasureCollector が 700行超、5つの責務を持つ
2. Semantic analysis 層がない
3. section/structure の参照解決が場当たり的
4. repeat/alternative の展開が未実装
5. エラー時の診断メッセージが不十分

---

## 提案: 3層アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│ Layer 1: Syntax (現状維持)                                      │
├─────────────────────────────────────────────────────────────────┤
│ SourceText → Lexer → Parser → SyntaxTree                        │
│                                                                 │
│ - Lexer.cs, Parser.cs (既存)                                    │
│ - SyntaxTree, SyntaxNode (Red-Green Tree, immutable)            │
│ - Diagnostics (parse errors)                                    │
│                                                                 │
│ 責務: 字句解析・構文解析のみ。意味解析は行わない                │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 2: Semantics (新規)                                       │
├─────────────────────────────────────────────────────────────────┤
│ SyntaxTree → SymbolTable → Binder → BoundScore                  │
│                                                                 │
│ 2a. Symbol Collection (Pass 1)                                  │
│     - SymbolCollector: 定義を収集                               │
│     - SymbolTable: section, phrase, part, variable を管理       │
│                                                                 │
│ 2b. Binding (Pass 2)                                            │
│     - Binder: 参照を定義に解決                                  │
│     - StructureExpander: repeat/alternative → flat sequence     │
│     - RelativePitchResolver: relative { } の音程計算            │
│                                                                 │
│ 2c. BoundMusic (出力)                                           │
│     - BoundScore: 展開済みスコア                                │
│     - BoundMeasure: 展開済み小節                                │
│     - BoundNote, BoundRest, BoundChord: 展開済み音符            │
│                                                                 │
│ 責務: 名前解決、構造展開、意味検証                              │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 3: Engraving (既存を活用)                                 │
├─────────────────────────────────────────────────────────────────┤
│ BoundScore → LayoutEngine → ScoreLayout → SvgRenderer → SVG     │
│                                                                 │
│ 3a. LayoutEngine (既存)                                         │
│     - Spring-Rod spacing                                        │
│     - Beam/Tie/Slur layout                                      │
│     - NoteCollision, AccidentalPlacement                        │
│                                                                 │
│ 3b. Renderer (既存)                                             │
│     - SvgRenderer: ScoreLayout → SVG                            │
│                                                                 │
│ 責務: 視覚的配置とレンダリング                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 実装計画

### Phase A: Symbol Collection (3-4日)

**目的:** 定義の収集と管理

**ファイル:**
```
LilySharp.Core/Semantics/
├── Symbols/
│   ├── Symbol.cs              # 基底クラス
│   ├── SectionSymbol.cs       # section 定義
│   ├── PhraseSymbol.cs        # phrase 定義  
│   ├── PartSymbol.cs          # part 定義
│   └── VariableSymbol.cs      # 変数定義
├── SymbolTable.cs             # シンボル管理
└── SymbolCollector.cs         # 定義収集 (Pass 1)
```

**設計:**
```csharp
// Symbol 基底クラス (Roslyn パターン)
public abstract record Symbol(
    string Name,
    SyntaxNode DeclaringSyntax,
    SymbolKind Kind);

public record SectionSymbol(
    string Name,
    SyntaxNode DeclaringSyntax,
    ImmutableArray<SyntaxNode> Body) : Symbol(Name, DeclaringSyntax, SymbolKind.Section);

// SymbolTable (スコープ付き)
public sealed class SymbolTable
{
    private readonly Dictionary<string, SectionSymbol> _sections = new();
    private readonly Dictionary<string, PhraseSymbol> _phrases = new();
    private readonly Dictionary<string, PartSymbol> _parts = new();
    private readonly Dictionary<string, VariableSymbol> _variables = new();
    
    public bool TryGetSection(string name, out SectionSymbol? symbol);
    public bool TryGetPhrase(string name, out PhraseSymbol? symbol);
    public void AddSection(SectionSymbol symbol);
    // ...
}

// SymbolCollector (Pass 1)
public sealed class SymbolCollector
{
    public SymbolTable Collect(SyntaxTree tree)
    {
        var table = new SymbolTable();
        foreach (var node in tree.Root.DescendantNodes())
        {
            switch (node)
            {
                case SectionDeclarationSyntax section:
                    table.AddSection(new SectionSymbol(...));
                    break;
                // ...
            }
        }
        return table;
    }
}
```

**テスト:**
- `SymbolCollectorTests.cs` - 定義収集のテスト
- `SymbolTableTests.cs` - シンボル管理のテスト

### Phase B: Binder + StructureExpander (4-5日)

**目的:** 参照解決と構造展開

**ファイル:**
```
LilySharp.Core/Semantics/
├── Binding/
│   ├── Binder.cs              # メインバインダー
│   ├── StructureExpander.cs   # repeat/alternative 展開
│   └── RelativePitchResolver.cs # relative { } 解決
├── BoundTree/
│   ├── BoundMusic.cs          # 基底クラス
│   ├── BoundScore.cs          # 展開済みスコア
│   ├── BoundMeasure.cs        # 展開済み小節
│   ├── BoundNote.cs           # 展開済み音符
│   ├── BoundRest.cs           # 展開済み休符
│   └── BoundChord.cs          # 展開済み和音
└── Diagnostics/
    └── SemanticDiagnostic.cs  # 意味エラー
```

**設計:**
```csharp
// BoundMusic 基底 (immutable record)
public abstract record BoundMusic(SyntaxNode Syntax);

public record BoundNote(
    SyntaxNode Syntax,
    Pitch Pitch,           // 絶対音程 (relative 解決済み)
    Duration Duration,
    int Dots,
    Accidental Accidental,
    bool HasTieStart,
    bool HasTieEnd) : BoundMusic(Syntax);

public record BoundMeasure(
    SyntaxNode Syntax,
    ImmutableArray<BoundMusic> Items,
    BarlineType StartBarline,
    BarlineType EndBarline,
    string? SectionLabel) : BoundMusic(Syntax);

public record BoundScore(
    ImmutableArray<BoundMeasure> Measures,
    ScoreMetadata Metadata,
    ImmutableArray<SemanticDiagnostic> Diagnostics);

// Binder
public sealed class Binder
{
    private readonly SymbolTable _symbols;
    private readonly List<SemanticDiagnostic> _diagnostics = new();
    
    public BoundScore Bind(SyntaxTree tree, SymbolTable symbols)
    {
        _symbols = symbols;
        
        // structure ブロックがあれば展開
        var expander = new StructureExpander(_symbols, _diagnostics);
        var expandedNodes = expander.Expand(tree.Root);
        
        // relative ブロックの音程解決
        var pitchResolver = new RelativePitchResolver();
        
        // BoundMusic の構築
        var measures = BuildMeasures(expandedNodes, pitchResolver);
        
        return new BoundScore(measures, metadata, _diagnostics.ToImmutableArray());
    }
}

// StructureExpander
public sealed class StructureExpander
{
    // structure { |: A [1. B] [2. C] :| } を展開
    // → A, B, A, C の順序で展開
    public IEnumerable<SyntaxNode> Expand(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            if (node is StructureDeclarationSyntax structure)
            {
                foreach (var expanded in ExpandStructure(structure))
                    yield return expanded;
            }
            else if (node is SectionReferenceSyntax sectionRef)
            {
                if (_symbols.TryGetSection(sectionRef.Name, out var section))
                    foreach (var child in section.Body)
                        yield return child;
                else
                    _diagnostics.Add(new SemanticDiagnostic(...));
            }
            // ...
        }
    }
}
```

**テスト:**
- `BinderTests.cs` - バインディング全体のテスト
- `StructureExpanderTests.cs` - repeat/alternative 展開のテスト
- `RelativePitchResolverTests.cs` - 相対音程解決のテスト

### Phase C: MeasureCollector 簡素化 + 統合 (2-3日)

**目的:** MeasureCollector を Binder のクライアントに変更

**変更:**
```csharp
// Before: MeasureCollector が全てを担当
public Score Collect(SyntaxTree tree)
{
    CollectDefinitions(tree.Root);      // ← SymbolCollector へ移動
    CollectMetadata(...);               // ← Binder へ移動
    ProcessStructure(...);              // ← StructureExpander へ移動
    // ...
}

// After: MeasureCollector は BoundScore → Score 変換のみ
public Score Collect(SyntaxTree tree)
{
    // 1. シンボル収集
    var symbolCollector = new SymbolCollector();
    var symbols = symbolCollector.Collect(tree);
    
    // 2. バインディング (参照解決 + 構造展開)
    var binder = new Binder();
    var boundScore = binder.Bind(tree, symbols);
    
    // 3. BoundScore → Score 変換 (単純なマッピング)
    return ConvertToScore(boundScore);
}

private Score ConvertToScore(BoundScore bound)
{
    // BoundMeasure → Measure の変換
    // BoundNote → NoteItem の変換
    // ...
}
```

**成功基準:**
- `RenderMinuet_HasExpectedStructure` テストがパスする
- 既存のテストが全てパスする

---

## 移行戦略

### 段階的移行 (推奨)

```
Week 1: Phase A - SymbolCollector + SymbolTable
        - 既存コードと並行して新しい Semantics 層を構築
        - テストで新旧の出力を比較

Week 2: Phase B - Binder + StructureExpander
        - BoundMusic 型の実装
        - repeat/alternative の展開ロジック

Week 3: Phase C - 統合
        - MeasureCollector の簡素化
        - 古いコードの削除
        - RenderMinuet テストのパス確認
```

### テスト戦略

```csharp
// 新旧比較テスト
[Theory]
[InlineData("happy-birthday.lys")]
[InlineData("fur-elise.lys")]
[InlineData("minuet.lys")]
public void OldAndNewProduceSameOutput(string filename)
{
    var tree = Parser.Parse(File.ReadAllText(filename));
    
    var oldScore = new MeasureCollector().Collect(tree);
    var newScore = new SemanticPipeline().Process(tree);
    
    Assert.Equal(oldScore.Measures.Length, newScore.Measures.Length);
    // ...
}
```

---

## 期待される効果

| 現状 | Phase A | Phase B | Phase C |
|------|---------|---------|---------|
| MeasureCollector 733行 | 変更なし | 変更なし | **300行以下** |
| section 参照解決: 場当たり的 | SymbolTable で管理 | Binder で解決 | 完全動作 |
| repeat/alternative: 未実装 | - | StructureExpander | 完全動作 |
| minuet.lys: 失敗 | - | - | **成功** |
| エラーメッセージ: なし | - | Diagnostics | 詳細なエラー |

### 長期的効果

1. **関心の分離**: 各層が明確な責務を持つ
2. **テスト容易性**: 各層を独立してテスト可能
3. **拡張性**: 新しい構文や機能を追加しやすい
4. **診断機能**: IDE 統合に必要な SemanticModel を提供可能
5. **Incremental 対応**: 将来の増分コンパイルに対応可能

---

## 参考: ソースコード対応表

| LilySharp Component | LilyPond Reference | Roslyn Reference |
|---------------------|-------------------|------------------|
| SymbolTable | lily/context-def.cc | Symbols/ |
| SymbolCollector | - | Binder/BinderFactory.cs |
| Binder | lily/context.cc | Binder/Binder.cs |
| StructureExpander | lily/volta-repeat-iterator.cc | Lowering/ |
| BoundMusic | lily/music.cc | BoundTree/BoundNode.cs |
| BoundScore | - | BoundTree/BoundStatement.cs |
| LayoutEngine | lily/engraver.cc + lily/grob.cc | Emit/ |

---

## 設計決定の記録

### 2025-12-13: MusicIterator 不採用の決定

**背景:**
- ARCHITECTURE_REDESIGN.md v1 では LilyPond の Iterator パターンを採用予定だった

**調査結果:**
- LilyPond の Iterator は Scheme の遅延評価 (`ly_call`) に対応するため必要
- `volta-repeat-iterator.cc` は Runtime で repeat を処理
- `alternative-sequence-iterator.cc` は Context の状態を見て分岐

**決定:**
- LilySharp は Scheme 機能を持たないため、Iterator は不要
- 代わりに Binder 段階で structure を事前展開
- これにより:
  - 実装が簡素化
  - 処理が高速化
  - デバッグが容易

### 2025-12-13: Grob 概念の不採用

**背景:**
- LilyPond は Grob (Graphical Object) で動的プロパティ解決

**決定:**
- LilySharp は既に Layout (MeasureLayout, BeamLayout) と Renderer が分離
- Grob の動的コールバック機構は不要
- 静的な immutable records で十分

---

*Last updated: 2025-12-13*
*Version: 2.0*
