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
- Phase 6: 旧ファイル削除 (MusicElement.cs, SystemLayout.cs) ✅
- GetGlobalMeasureIndex を MeasureLayout.MeasureIndex に置き換え ✅

### 未完了
- Tablature 機能の新アーキテクチャ移行
- セクション単位キャッシュの実装
- テストの新アーキテクチャ移行

### 既知の問題
- 旧 `SvgExporter` が Tablature とテスト用に残存

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
2. テストを新アーキテクチャに移行
3. 旧 SvgExporter を削除

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