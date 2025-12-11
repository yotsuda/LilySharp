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
│                        SyntaxTree                               │
│  (Parser が生成、Red-Green Tree)                                │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ MeasureCollector (1回の走査)
┌─────────────────────────────────────────────────────────────────┐
│                          Score                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Voice "rightHand"                                        │   │
│  │ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │   │
│  │ │Measure 1│ │Measure 2│ │Measure 3│ │Measure 4│ ...    │   │
│  │ └─────────┘ └─────────┘ └─────────┘ └─────────┘        │   │
│  └─────────────────────────────────────────────────────────┘   │
│  Metadata: TimeSignature, KeySignature, Tempo, Clef            │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ LayoutEngine
┌─────────────────────────────────────────────────────────────────┐
│                       ScoreLayout                               │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │ SystemLayout 1 (Y=100)                                     │ │
│  │ [Clef][Key][Time] M1 ──── M2 ──── M3 ──── M4 ────|        │ │
│  │                   ↑       ↑       ↑       ↑               │ │
│  │              MeasureLayout (X, Width)                      │ │
│  └───────────────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │ SystemLayout 2 (Y=220)                                     │ │
│  │ [Clef][Key] M5 ──── M6 ──── M7 ──── M8 ───────|           │ │
│  └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼ SvgRenderer
┌─────────────────────────────────────────────────────────────────┐
│                          SVG                                    │
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