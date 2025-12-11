# LilySharp SVG Layout Architecture

## 設計原則

1. **Immutability**: 全てのドメインオブジェクトは不変（record）
2. **Separation of Concerns**: 収集・レイアウト・描画を完全分離
3. **Lazy Evaluation**: レイアウト計算は必要時に実行
4. **Cacheability**: 小節単位でキャッシュ可能
5. **Single Pass**: 構文木は1回だけ走査

## 楽譜レイアウトの3レベル

```
Level 1: Score Layout (どの小節をどの行に配置するか)
    ↓
Level 2: System Layout (行内の小節間スペース配分)
    ↓
Level 3: Measure Layout (小節内の音符間スペース配分)
```

## データフロー

```
┌─────────────────────────────────────────────────────────────────┐
│                        SyntaxTree                                                                                                │
│  (Parser が生成、Red-Green Tree)                                                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ MeasureCollector (1回の走査)
┌─────────────────────────────────────────────────────────────────┐
│                          Score                                                                                                   │
│  ┌─────────────────────────────────────────────────────────┐          │
│  │ Voice "rightHand"                                                                                                │          │
│  │ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐                      │          │
│  │ │Measure 1         │ │Measure 2         │ │Measure 3         │ │Measure 4         │ ...                  │          │
│  │ └─────────┘ └─────────┘ └─────────┘ └─────────┘                      │          │
│  └─────────────────────────────────────────────────────────┘          │
│  Metadata: TimeSignature, KeySignature, Tempo, Clef                                                                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ LayoutEngine
┌─────────────────────────────────────────────────────────────────┐
│                       ScoreLayout                                                                                                │
│  ┌───────────────────────────────────────────────────────────┐      │
│  │ SystemLayout 1 (Y=100)                                                                                               │      │
│  │ [Clef][Key][Time] M1 ──── M2 ──── M3 ──── M4 ────|                                                   │      │
│  │                   ↑       ↑       ↑       ↑                                                                      │      │
│  │              MeasureLayout (X, Width)                                                                                │      │
│  └───────────────────────────────────────────────────────────┘      │
│  ┌───────────────────────────────────────────────────────────┐      │
│  │ SystemLayout 2 (Y=220)                                                                                               │      │
│  │ [Clef][Key] M5 ──── M6 ──── M7 ──── M8 ───────|                                                   │      │
│  └───────────────────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ SvgRenderer
┌─────────────────────────────────────────────────────────────────┐
│                          SVG                                                                                                     │
└─────────────────────────────────────────────────────────────────┘
```

## ドメインモデル

### 音楽要素 (Music Elements)

```csharp
// 基底: 音価を持つ要素
public abstract record MusicItem
{
    public abstract Fraction Duration { get; }
}

// 音符
public sealed record NoteItem(
    int StaffPosition,      // 五線譜上の位置 (0=中央線)
    Fraction Duration,
    int Dots,
    string? Accidental,     // "sharp", "flat", etc.
    bool NeedsLedgerLines,
    int SourcePosition      // 構文木での位置（クリック対応）
) : MusicItem;

// 休符
public sealed record RestItem(
    Fraction Duration,
    int Dots,
    int SourcePosition
) : MusicItem;

// 和音
public sealed record ChordItem(
    ImmutableArray<ChordNoteInfo> Notes,
    Fraction Duration,
    int Dots,
    int SourcePosition
) : MusicItem;

public readonly record struct ChordNoteInfo(
    int StaffPosition,
    string? Accidental,
    bool NeedsLedgerLines
);
```

### 小節 (Measure)

```csharp
public sealed record Measure(
    ImmutableArray<MusicItem> Items,
    BarlineType StartBarline,  // None, RepeatStart
    BarlineType EndBarline,    // Single, Double, Final, RepeatEnd
    string? SectionLabel,      // "A", "B", etc.
    int SourceStart,           // キャッシュキー
    int SourceEnd
)
{
    // 計算済みプロパティ（遅延評価可能）
    public Fraction TotalDuration => 
        Items.Aggregate(Fraction.Zero, (sum, item) => sum + item.Duration);
}
```

### 声部とスコア (Voice and Score)

```csharp
public sealed record Voice(
    string Name,
    ImmutableArray<Measure> Measures
);

public sealed record Score(
    ImmutableArray<Voice> Voices,
    TimeSignature TimeSignature,
    KeySignature KeySignature,
    string Clef,
    int? Tempo,
    string? Title,
    string? Composer
);
```

### レイアウト (Layout)

```csharp
// 小節のレイアウト情報
public sealed record MeasureLayout(
    int MeasureIndex,
    double X,              // 小節開始X座標
    double Width,          // 小節幅
    double ContentWidth,   // 音符部分の幅（小節線除く）
    ImmutableArray<double> ItemOffsets  // 各音符のオフセット
);

// 行のレイアウト
public sealed record SystemLayout(
    int SystemIndex,
    double Y,
    double StartX,         // 譜表記号後の開始位置
    ImmutableArray<MeasureLayout> Measures
);

// スコア全体のレイアウト
public sealed record ScoreLayout(
    double Width,
    double Height,
    ImmutableArray<SystemLayout> Systems
);
```

## インターフェース

```csharp
// 収集: SyntaxTree → Score
public interface IMeasureCollector
{
    Score Collect(SyntaxTree tree, string? voiceName = null);
}

// レイアウト: Score → ScoreLayout
public interface ILayoutEngine
{
    ScoreLayout Layout(Score score, LayoutOptions options);
}

// 描画: Score + ScoreLayout → SVG
public interface IScoreRenderer
{
    string Render(Score score, ScoreLayout layout);
}
```

## レイアウトアルゴリズム

### 1. 小節幅の計算

各小節の最小幅は、含まれる音符の音価に基づく:

```
MinWidth = Σ (SpacingFunction(item.Duration) + AccidentalWidth + DotWidth)
```

SpacingFunction は Gourlay (1987) に基づく対数関数:
```
spacing = baseWidth * (1 + log2(duration / quarterNote))
```

### 2. 行分割（貪欲アルゴリズム）

```
currentLine = []
currentWidth = prefixWidth  // Clef + Key + Time

for each measure in measures:
    if currentWidth + measure.MinWidth > pageWidth:
        finishLine(currentLine)
        currentLine = []
        currentWidth = continuationPrefixWidth  // Clef + Key
    
    currentLine.add(measure)
    currentWidth += measure.MinWidth

finishLine(currentLine)
```

### 3. Justification（行内の調整）

```
extraSpace = pageWidth - sum(measure.MinWidth) - prefixWidth - barlineWidth
stretchPerMeasure = extraSpace / measureCount

for each measure:
    measure.ActualWidth = measure.MinWidth + stretchPerMeasure
```

### 4. 小節内の音符配置

```
totalStretch = extraWidth / totalStretchWeight
for each item in measure:
    item.X = currentX
    item.Width = item.MinWidth + (item.StretchWeight * totalStretch)
    currentX += item.Width
```

## キャッシュ戦略

```csharp
public sealed class LayoutCache
{
    // SourcePosition → MeasureLayout
    private readonly Dictionary<(int start, int end), MeasureLayout> _measureCache;
    
    // 小節の内容が変わっていなければキャッシュを使用
    public MeasureLayout GetOrCompute(Measure measure, Func<Measure, MeasureLayout> compute)
    {
        var key = (measure.SourceStart, measure.SourceEnd);
        if (_measureCache.TryGetValue(key, out var cached))
            return cached;
        
        var layout = compute(measure);
        _measureCache[key] = layout;
        return layout;
    }
}
```

## ファイル構成

```
LilySharp.Core/
├── Svg/
│   ├── Model/
│   │   ├── MusicItem.cs       # MusicItem, NoteItem, RestItem, ChordItem
│   │   ├── Measure.cs         # Measure, BarlineType
│   │   ├── Voice.cs           # Voice
│   │   └── Score.cs           # Score
│   ├── Layout/
│   │   ├── LayoutOptions.cs   # LayoutOptions
│   │   ├── MeasureLayout.cs   # MeasureLayout
│   │   ├── SystemLayout.cs    # SystemLayout  
│   │   ├── ScoreLayout.cs     # ScoreLayout
│   │   ├── ILayoutEngine.cs   # インターフェース
│   │   ├── LayoutEngine.cs    # 実装
│   │   └── SpacingRules.cs    # 間隔計算ルール
│   ├── Collector/
│   │   ├── IMeasureCollector.cs
│   │   └── MeasureCollector.cs
│   ├── Renderer/
│   │   ├── IScoreRenderer.cs
│   │   └── SvgRenderer.cs
│   └── SvgExporter.cs         # ファサード（既存API維持）
```

## 移行計画

1. Phase 1: ドメインモデル作成 (MusicItem, Measure, Voice, Score)
2. Phase 2: MeasureCollector 作成 (SyntaxTree → Score)
3. Phase 3: LayoutEngine 作成 (Score → ScoreLayout)
4. Phase 4: SvgRenderer 作成 (Score + ScoreLayout → SVG)
5. Phase 5: SvgExporter をファサードとして再構成
6. Phase 6: 既存の MusicElement.cs, SystemLayout.cs を削除

## 実装状況 (2025-12)

### 完了
- Phase 1: ドメインモデル作成 ✅
- Phase 2: MeasureCollector 作成 ✅
- Phase 3: LayoutEngine 作成 ✅
- Phase 4: SvgRenderer 作成 ✅
- Phase 5: CLI/LSP を新アーキテクチャに切り替え ✅
- Phase 6: 旧ファイル削除 (MusicElement.cs, SystemLayout.cs, SvgExporter.cs) ✅
- GetGlobalMeasureIndex を MeasureLayout.MeasureIndex に置き換え ✅
- SvgTests/BenchmarkTest を新アーキテクチャに移行 ✅
- シンプルファイル（セクション/構造なし）のフォールバック処理追加 ✅

### 未完了
- Tablature 機能の新アーキテクチャでの再実装（旧実装は削除済み）
- セクション単位キャッシュの実装


## 設計決定事項

### 表記法
| 項目 | 決定 | 理由 |
|------|------|------|
| 小節線 | 入力必須 | 小節を基本単位として扱うため |
| 状態リセット | セクション開始時 | 入力の利便性を維持しつつ、エラー伝播を制限 |
| スコープ単位 | セクション | キャッシュ・インクリメンタル更新の粒度として適切 |
| relative モード | 維持 | 入力の利便性のため |
| 音価継承 | セクション内で継承 | 入力の利便性のため |

### アーキテクチャ
| 項目 | 決定 | 理由 |
|------|------|------|
| 基本単位 | 小節 (Measure) | 拍子検証、レイアウト、キャッシュに最適 |
| データ構造 | 不変 (record) | 安全性、テスト容易性 |
| レイヤー分離 | Model/Collector/Layout/Renderer | 関心の分離、テスト容易性 |
| 間隔計算 | Gourlay アルゴリズム | 対数スケーリングで自然な音符間隔 |

## 状態管理

### セクション開始時にリセットされる状態
- `_currentOctave` - 現在のオクターブ (デフォルト: 4)
- `_lastPitchName` - 最後の音名 (デフォルト: 'c')
- `_defaultDuration` - デフォルト音価 (デフォルト: 4分音符)

### セクション内で継承される状態
- relative モードでのオクターブ計算
- 音価の継承

## 次のステップ

### 短期 (次回の作業)
1. Tablature 機能を新アーキテクチャに移行
2. 旧 SvgExporter を削除

### 中期
1. セクション単位キャッシュの実装
2. インクリメンタル更新の実装

### 長期
1. パフォーマンス計測と最適化
2. パラレル処理の検討（セクション単位）
3. エラーメッセージの改善

## 参照リソース

### 外部ソースコード
| プロジェクト | パス | 参照ポイント |
|-------------|------|-------------|
| Roslyn | `C:\MyProj\roslyn` | Formatting Engine, Red-Green Tree, Workspace |
| LilyPond | (未取得) | Spacing アルゴリズム、レイアウト |

### Roslyn の参考ファイル
- `src/Workspaces/Core/Portable/Formatting/` - フォーマッティング全般
- `src/Workspaces/SharedUtilitiesAndExtensions/Compiler/Core/Formatting/Engine/` - TokenStream, ChainedFormattingRules
- `src/Workspaces/Core/Portable/Workspace/Solution/` - DocumentState, インクリメンタル更新

### 学術参考文献
- Gourlay (1987): "Spacing a Line of Music" - 音符間隔の対数スケーリング

## Spring-Rod モデル（計画）

現在の実装は「前から順に配置」するヒューリスティックベースですが、
Lilypond のような高品質なレイアウトを実現するには、
**制約ソルバー**ベースのアプローチが必要です。

### 概念

```
┌─────────────────────────────────────────────────────────────┐
│  Spring（バネ）                                              │
│  - 理想距離: 時間比例で計算                                 │
│  - 伸縮性: 短い音符ほど硬い、長い音符ほど柔らかい           │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Rod（ロッド）                                               │
│  - 最小距離: 視覚的な衝突を防ぐ                             │
│  - 臨時記号、符頭、付点などの物理的な幅から計算             │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Constraint Solver                                          │
│  - 全ての Rod 制約を満たしながら                            │
│  - Spring の理想位置にできるだけ近い配置を計算              │
│  - 行幅に応じて Force を調整して伸縮                        │
└─────────────────────────────────────────────────────────────┘
```

### Lilypond の実装（参考）

| ファイル | 役割 |
|---------|------|
| `spring.cc` | バネの定義（理想距離、最小距離、伸縮性） |
| `simple-spacer.cc` | 制約ソルバー（バネとロッドから位置計算） |
| `spacing-spanner.cc` | バネとロッドの生成 |
| `note-spacing.cc` | 音符間のバネ生成 |
| `staff-spacing.cc` | 小節線と音符間のスペーシング |
| `spacing-options.cc` | Gourlay アルゴリズムによる理想距離計算 |

### Spring クラス（案）

```csharp
public sealed record Spring(
    double IdealDistance,    // 理想距離（時間比例）
    double MinDistance,      // 最小距離（衝突回避）
    double Stiffness         // 剛性（短い音符ほど高い）
)
{
    /// <summary>
    /// 与えられた Force での実際の長さを計算
    /// </summary>
    public double Length(double force)
    {
        double result = IdealDistance + force / Stiffness;
        return Math.Max(result, MinDistance);
    }
}
```

### 制約ソルバー（案）

```csharp
public sealed class SpringSolver
{
    private readonly List<Spring> _springs;
    
    /// <summary>
    /// 目標の総幅を達成する Force を二分探索で計算
    /// </summary>
    public double SolveForWidth(double targetWidth)
    {
        // Binary search for the force that achieves target width
        // while respecting all minimum distance constraints
    }
    
    /// <summary>
    /// 計算された Force で各位置を取得
    /// </summary>
    public ImmutableArray<double> GetPositions(double force)
    {
        var positions = new List<double>();
        double currentX = 0;
        
        foreach (var spring in _springs)
        {
            positions.Add(currentX);
            currentX += spring.Length(force);
        }
        
        return positions.ToImmutableArray();
    }
}
```

### 移行計画

1. **Phase A**: Spring と Rod の定義
2. **Phase B**: SpringSolver の実装（二分探索）
3. **Phase C**: SpacingRules から Spring/Rod を生成するロジック
4. **Phase D**: LayoutEngine を SpringSolver ベースに置き換え
5. **Phase E**: 微調整と最適化

### 期待される効果

- **一貫性**: 全ての衝突回避が統一されたロジックで処理
- **拡張性**: 新しい要素（連桁、タイなど）も Rod として追加可能
- **品質**: Lilypond に近いプロフェッショナルな出力
- **保守性**: ヒューリスティックの積み重ねではなく、原理に基づいた実装

## 実装ノート

### 気づき 1: アイテム幅 vs 間隔距離

現在の `CalculateItemWidth` は**アイテム自体の幅**を計算している：
```
itemWidth = baseWidth + accidentalWidth + dotWidth
```

しかし Spring-Rod モデルでは**隣接アイテム間の距離**を扱う：
```
spring[i] = distance between item[i] and item[i+1]
```

この違いは本質的。アイテム幅ベースの考え方では、臨時記号が**左側**に張り出す問題を正しく扱えない。

```
従来: [acc][note]────[note]────[note]
       ↑ accidentalWidth がここに含まれるが、
         実際には「前のアイテムとの距離」に影響する

Spring-Rod:
      item[0]──spring[0]──item[1]──spring[1]──item[2]
                 ↑
      spring[0].MinDistance = item[0].RightExtent + item[1].LeftExtent
                              (符頭の右端)      (臨時記号の左端)
```

### 気づき 2: Reference Point の概念

Lilypond では各アイテムに **reference point**（基準点）がある。
- 音符: 符頭の中心
- 休符: 中心

Spring は reference point 間の距離を扱う。これにより：
- LeftExtent: 基準点から左への張り出し（臨時記号）
- RightExtent: 基準点から右への張り出し（符頭、付点）

```
      ←LeftExtent→←RightExtent→
          ♯        ●    ・
                   ↑
            reference point
```


### 気づき 3: MinDistance による衝突回避

Spring-Rod モデルでは、衝突回避が**暗黙的に**行われる：

```
MinDistance = PrevItem.RightExtent + NextItem.LeftExtent + MinGap
```

臨時記号付きの音符の場合：
- `LeftExtent = NoteheadWidth/2 + AccidentalWidth + 2`

この設計により：
- 前のアイテムが何であれ、最小距離が保証される
- 特別な「臨時記号衝突チェック」ロジックは不要
- 新しい要素（連桁、タイなど）も同じパターンで追加可能

### 気づき 4: Spring.Length() の動作

```
Length(force) = max(IdealDistance + force/Stiffness, MinDistance)
```

- **force > 0** (伸張): IdealDistance より長くなる
- **force < 0** (圧縮): IdealDistance より短くなるが、MinDistance を下回らない
- **force = 0**: IdealDistance と MinDistance の大きい方

これにより、行幅に合わせて自然に伸縮しつつ、衝突は常に回避される。


### 気づき 5: SpringSolver.GetPositions のインデックス

GetPositions が返す配列は、Springs の数 + 1 の要素を持つ：

```
Springs:    [barline→item0] [item0→item1] [item1→item2] [item2→barline]
                 ↓              ↓              ↓              ↓
Positions:    pos[0]        pos[1]         pos[2]         pos[3]         pos[4]
              (start)       (item0)        (item1)        (item2)        (end)
```

**重要**: `positions[i]` ではなく `positions[i+1]` が `item[i]` の位置。

```csharp
// 正しい使い方
for (int i = 0; i < items.Length; i++)
{
    double x = positions[i + 1];        // item[i] の位置
    double width = positions[i + 2] - positions[i + 1];  // item[i] の幅
}
```

### 気づき 6: CalculateMeasureMinWidth の整合性 ✅ 完了

~~現在の `CalculateMeasureMinWidth` は旧実装の `CalculateItemWidth` を使っている。~~
~~Spring-Rod モデルとの一貫性のため、将来的には Spring の MinDistance の合計から~~
~~計算するように変更すべき。~~

**実装完了**: Spring の MinDistance 合計から計算するように変更済み。
また、使われなくなった `CalculateItemWidth` と `CalculateStretchWeight` も削除済み。

```csharp
public static double CalculateMeasureMinWidth(Measure measure)
{
    double width = GetBarlineWidth(measure.StartBarline)
                 + GetBarlineWidth(measure.EndBarline);
    
    if (measure.Items.Length > 0)
    {
        var springs = CreateSpringsForMeasure(measure);
        foreach (var spring in springs)
            width += spring.MinDistance;
    }
    
    return width;
}
```
