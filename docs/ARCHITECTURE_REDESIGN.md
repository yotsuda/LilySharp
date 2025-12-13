# LilySharp アーキテクチャ再設計

## 現状の問題

```
Parser → SyntaxTree → MeasureCollector → Score → LayoutEngine → SvgRenderer
                           ↑
                      複数の責務が混在:
                      - Symbol resolution (section, phrase, variable)
                      - Structure expansion (repeat, alternative)
                      - Time calculation (relative pitch, duration)
                      - Music collection
```

**問題点:**
1. MeasureCollector が多すぎる責務を持っている
2. Semantic analysis 層がない
3. Music expression の評価モデルが不明確
4. 場当たり的なパッチで対応している

## LilyPond のアーキテクチャ

```
Parser → Music Expression (AST)
              ↓
         Music Iterator (時間順評価)
              ↓
         Context + Engravers (Stream Events → Grobs)
              ↓
         Grobs → Paper_column → Page Layout
              ↓
         Output (PDF/SVG/etc)
```

**核心概念:**
- **Music Expression**: 入れ子構造の音楽表現 (Sequential, Simultaneous, etc.)
- **Music Iterator**: Music を時間順に展開し、Stream Events を生成
- **Context**: 状態を保持する環境 (Staff, Voice, etc.)
- **Engraver**: Stream Events を受け取り Grobs を生成
- **Grob**: Graphical Object (符頭、符幹、連桁など)

## 提案: 3層アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│ Layer 1: Syntax (現状維持)                                      │
├─────────────────────────────────────────────────────────────────┤
│ Parser → SyntaxTree (Red-Green Tree)                            │
│                                                                 │
│ - Lexer.cs, Parser.cs                                           │
│ - SyntaxTree, SyntaxNode (immutable)                            │
│ - Diagnostics                                                   │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 2: Semantics (新規)                                       │
├─────────────────────────────────────────────────────────────────┤
│ SyntaxTree → SemanticModel → BoundMusicTree                     │
│                                                                 │
│ 2a. Symbol Resolution                                           │
│     - SymbolTable: section, phrase, variable, part の管理       │
│     - Binder: 参照を定義に解決                                  │
│                                                                 │
│ 2b. Music Tree Construction                                     │
│     - BoundMusic: 評価済み Music Expression                      │
│       - BoundSequential { children }                            │
│       - BoundSimultaneous { children }                          │
│       - BoundNote { pitch, duration }                           │
│       - BoundRest { duration }                                  │
│       - BoundChord { notes, duration }                          │
│     - Structure expansion: repeat/alternative → flat sequence   │
│                                                                 │
│ 2c. Type Checking (optional)                                    │
│     - Duration validation                                       │
│     - Pitch range checking                                      │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 3: Engraving (リファクタ)                                 │
├─────────────────────────────────────────────────────────────────┤
│ BoundMusicTree → MusicIterator → Engravers → Grobs → Layout     │
│                                                                 │
│ 3a. Music Iterator                                              │
│     - 時間順に BoundMusic を評価                                │
│     - MusicEvent を生成 (NoteEvent, RestEvent, BarlineEvent)    │
│                                                                 │
│ 3b. Engravers (現在の LayoutEngine を分解)                       │
│     - NoteHeadEngraver: NoteEvent → NoteHeadGrob                │
│     - StemEngraver: NoteHeadGrob → StemGrob                     │
│     - BeamEngraver: StemGrobs → BeamGrob                        │
│     - TieEngraver: TieEvent → TieGrob                           │
│     - SlurEngraver: SlurEvent → SlurGrob                        │
│                                                                 │
│ 3c. Layout                                                      │
│     - Spring-Rod spacing                                        │
│     - Skyline collision avoidance                               │
│     - Line breaking (Knuth-Plass)                               │
│                                                                 │
│ 3d. Renderer                                                    │
│     - Grobs → SVG/PDF                                           │
└─────────────────────────────────────────────────────────────────┘
```

## 実装計画

### Phase A: Semantic Layer 基盤 (2-3 days)

1. **SymbolTable.cs**
   ```csharp
   public class SymbolTable
   {
       Dictionary<string, SectionSymbol> Sections;
       Dictionary<string, PhraseSymbol> Phrases;
       Dictionary<string, PartSymbol> Parts;
       Dictionary<string, VariableSymbol> Variables;
   }
   ```

2. **Binder.cs**
   ```csharp
   public class Binder
   {
       public SemanticModel Bind(SyntaxTree tree);
       // Phase 1: Collect declarations
       // Phase 2: Resolve references
   }
   ```

3. **BoundMusic.cs** (immutable records)
   ```csharp
   public abstract record BoundMusic(Moment StartTime, Moment Duration);
   public record BoundSequential(ImmutableArray<BoundMusic> Children) : BoundMusic;
   public record BoundSimultaneous(ImmutableArray<BoundMusic> Children) : BoundMusic;
   public record BoundNote(Pitch Pitch, Duration Duration) : BoundMusic;
   ```

### Phase B: Structure Expansion (1-2 days)

1. **StructureExpander.cs**
   - `structure { |: A [1. B] [2. C] :| }` → flat sequence
   - repeat 展開
   - alternative 処理

### Phase C: Music Iterator (2-3 days)

1. **MusicIterator.cs**
   ```csharp
   public abstract class MusicIterator
   {
       public abstract Moment PendingMoment { get; }
       public abstract void Process(Moment until);
       public abstract IEnumerable<MusicEvent> Events { get; }
   }
   ```

2. **SequentialIterator.cs**, **SimultaneousIterator.cs**

### Phase D: Engraver リファクタ (3-5 days)

1. 現在の LayoutEngine を分解
2. 各 Engraver を独立させる
3. Grob 間の依存関係を明確化

## 移行戦略

1. **既存コードを維持しながら新アーキテクチャを並行実装**
   - 新しい Semantics 層を追加
   - MeasureCollector は徐々に置き換え
   - テストで両方の出力を比較

2. **段階的移行**
   ```
   Week 1: SymbolTable + Binder (基本)
   Week 2: BoundMusic + StructureExpander
   Week 3: MusicIterator
   Week 4: Engraver 分解開始
   ```

## 期待される効果

1. **関心の分離**: 各層が明確な責務を持つ
2. **テスト容易性**: 各層を独立してテスト可能
3. **拡張性**: 新しい構文や機能を追加しやすい
4. **LilyPond 互換**: 同じ概念モデルで理解しやすい
5. **パフォーマンス**: 必要な部分だけ再計算可能 (incremental)

## 参考: LilyPond ソースコード

| LilySharp Component | LilyPond Reference |
|---------------------|-------------------|
| SymbolTable | lily/context-def.cc |
| MusicIterator | lily/music-iterator.cc |
| SequentialIterator | lily/sequential-iterator.cc |
| SimultaneousIterator | lily/simultaneous-music-iterator.cc |
| Engraver | lily/engraver.cc |
| NoteHeadEngraver | lily/note-heads-engraver.cc |
| StemEngraver | lily/stem-engraver.cc |
| BeamEngraver | lily/beam-engraver.cc |
| Grob | lily/grob.cc |
