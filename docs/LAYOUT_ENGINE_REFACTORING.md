# LayoutEngine 分割設計提案書

## 1. 現状分析

### 1.1 問題点

| 項目 | 現状 | 問題 |
|------|------|------|
| ファイルサイズ | 68KB (1,686行) | 可読性・保守性低下 |
| メソッド数 | 33 | 認知負荷が高い |
| 責務 | 7+ | 単一責務原則違反 |
| テスト容易性 | 低 | モック困難 |

### 1.2 現在の責務（混在状態）

```
LayoutEngine.cs
├── System Breaking (行分割)
├── Measure Layout (小節内レイアウト)
├── System Layout (システムレイアウト)
├── Page Layout (ページレイアウト)
├── Skyline Building (スカイライン構築)
├── Multi-Staff Layout (多譜表レイアウト)
└── Element Coordination (Beam/Tie/Slur統合)
```

### 1.3 LilyPond の責務分離（参考）

| ファイル | 責務 | サイズ |
|----------|------|--------|
| page-breaking.cc | ページ分割基底 | 63KB |
| page-layout-problem.cc | ページレイアウト問題 | 50KB |
| spacing-spanner.cc | スペーシング全般 | 18KB |
| page-spacing.cc | ページスペーシング | 13KB |
| note-spacing.cc | 音符スペーシング | 10KB |
| optimal-page-breaking.cc | 最適ページ分割 | 9KB |

---

## 2. 分割設計

### 2.1 新しいクラス構造

```
LilySharp.Core/Svg/Layout/
├── LayoutEngine.cs           # ファサード（オーケストレーション）
├── SystemBreaker.cs          # 行分割ロジック
├── MeasureLayouter.cs        # 小節内レイアウト
├── SystemLayouter.cs         # システムレイアウト
├── PageLayouter.cs           # ページレイアウト
├── SkylineBuilder.cs         # スカイライン構築
├── MultiStaffLayouter.cs     # 多譜表レイアウト
├── ElementCoordinator.cs     # Beam/Tie/Slur/Voice統合
└── LayoutUtilities.cs        # 共通ユーティリティ
```

### 2.2 各クラスの責務

#### LayoutEngine.cs (ファサード)
```csharp
/// <summary>
/// Orchestrates the layout process by coordinating specialized layouters.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-spanner.cc:1-565 (coordination role)
/// </remarks>
public sealed class LayoutEngine
{
    private readonly LayoutOptions _options;
    private readonly SystemBreaker _systemBreaker;
    private readonly MeasureLayouter _measureLayouter;
    private readonly SystemLayouter _systemLayouter;
    private readonly PageLayouter _pageLayouter;
    private readonly SkylineBuilder _skylineBuilder;
    private readonly ElementCoordinator _elementCoordinator;

    public ScoreLayout Layout(Score score)
    {
        // 1. Break into systems
        var systemMeasures = _systemBreaker.BreakIntoSystems(score);
        
        // 2. Layout each system
        var systems = _systemLayouter.LayoutSystems(systemMeasures, score);
        
        // 3. Create pages
        var pages = _pageLayouter.CreatePages(systems, score);
        
        // 4. Coordinate elements (beams, ties, slurs)
        var elements = _elementCoordinator.Coordinate(score, systems);
        
        return new ScoreLayout(pages, systems, elements);
    }
}
```

#### SystemBreaker.cs
```csharp
/// <summary>
/// Breaks measures into systems (lines) using optimal or greedy algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/constrained-breaking.cc
/// LILYPOND-REF: lily/page-breaking.cc (break decisions)
/// </remarks>
public sealed class SystemBreaker
{
    private readonly LayoutOptions _options;
    private readonly KnuthPlassBreaker _knuthPlassBreaker;

    public List<List<Measure>> BreakIntoSystems(Score score);
    private List<List<Measure>> OptimalBreak(ImmutableArray<Measure> measures);
    private List<List<Measure>> GreedyBreak(ImmutableArray<Measure> measures);
}
```

#### MeasureLayouter.cs
```csharp
/// <summary>
/// Calculates item positions within a measure using Spring-Rod model.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-basic.cc:100-130 note_spacing()
/// LILYPOND-REF: lily/simple-spacer.cc (spring solver)
/// </remarks>
public sealed class MeasureLayouter
{
    private readonly SpringSolver _springSolver;

    public ImmutableArray<ItemLayout> LayoutItems(Measure measure, double totalWidth);
    public ImmutableArray<ColumnLayout> LayoutColumns(Measure measure, double totalWidth, List<Fraction> timings);
}
```

#### SystemLayouter.cs
```csharp
/// <summary>
/// Layouts measures within a system and calculates system geometry.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
/// LILYPOND-REF: lily/system.cc
/// </remarks>
public sealed class SystemLayouter
{
    private readonly LayoutOptions _options;
    private readonly MeasureLayouter _measureLayouter;
    private readonly SkylineBuilder _skylineBuilder;

    public ImmutableArray<SystemLayout> LayoutSystems(List<List<Measure>> systemMeasures, Score score);
    public SystemLayout LayoutSystem(int systemIndex, List<Measure> measures, double y, int keySharps, bool isFirstSystem, int firstMeasureIndex);
}
```

#### PageLayouter.cs
```csharp
/// <summary>
/// Creates pages from systems using optimal page breaking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-spacing.cc Page_spacer class
/// LILYPOND-REF: lily/page-layout-problem.cc
/// </remarks>
public sealed class PageLayouter
{
    private readonly LayoutOptions _options;
    private readonly PageBreaker _pageBreaker;
    private readonly SkylineBuilder _skylineBuilder;

    public ImmutableArray<PageLayout> CreatePages(ImmutableArray<SystemLayout> systems, Score score);
    public double CalculateHeaderHeight(string? title, string? composer);
    public double CalculateFirstSystemY(double headerBottom, double systemUpExtent);
}
```

#### SkylineBuilder.cs
```csharp
/// <summary>
/// Builds vertical and horizontal skylines for collision detection.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline()
/// LILYPOND-REF: lily/skyline.cc
/// </remarks>
public sealed class SkylineBuilder
{
    public (VerticalSkyline Up, VerticalSkyline Down) BuildSystemSkylines(List<Measure> measures, ImmutableArray<MeasureLayout> layouts);
    public (VerticalSkyline Up, VerticalSkyline Down) BuildSystemSkylines(MultiStaffScore score, ImmutableArray<MeasureLayout> layouts);
    public void AddMusicItemToSkylines(MusicItem item, double x, double staffMiddleY, VerticalSkyline upSkyline, VerticalSkyline downSkyline);
}
```

#### MultiStaffLayouter.cs
```csharp
/// <summary>
/// Handles layout for grand staff and multi-staff scores.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/system.cc (multi-staff coordination)
/// </remarks>
public sealed class MultiStaffLayouter
{
    private readonly LayoutOptions _options;
    private readonly SystemLayouter _systemLayouter;

    public ScoreLayout Layout(MultiStaffScore score);
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(MultiStaffScore score, double systemY);
    public double CalculateSystemHeight(MultiStaffScore score);
}
```

#### ElementCoordinator.cs
```csharp
/// <summary>
/// Coordinates layout of beams, ties, slurs, dynamics, and other elements.
/// </summary>
public sealed class ElementCoordinator
{
    private readonly BeamDetector _beamDetector;
    private readonly BeamEngraver _beamEngraver;
    private readonly TieDetector _tieDetector;
    private readonly TieEngraver _tieEngraver;
    private readonly SlurDetector _slurDetector;
    private readonly SlurEngraver _slurEngraver;
    private readonly VoiceCollector _voiceCollector;
    private readonly NoteCollision _noteCollision;

    public ElementLayouts Coordinate(Score score, ImmutableArray<SystemLayout> systems);
    public ImmutableArray<BeamLayout> LayoutBeams(Score score, ImmutableArray<SystemLayout> systems);
    public ImmutableArray<TieLayout> LayoutTies(Score score, ImmutableArray<SystemLayout> systems);
    public ImmutableDictionary<VoiceItemKey, double> CalculateVoiceOffsets(Score score);
    public ImmutableDictionary<RestShiftKey, double> CalculateRestShifts(Score score, ImmutableArray<SystemLayout> systems, ImmutableArray<BeamLayout> beamLayouts);
}

/// <summary>
/// Container for all element layouts.
/// </summary>
public sealed record ElementLayouts(
    ImmutableArray<BeamLayout> Beams,
    ImmutableArray<TieLayout> Ties,
    ImmutableArray<SlurLayout> Slurs,
    ImmutableArray<DynamicLayout> Dynamics,
    ImmutableArray<ArticulationLayout> Articulations,
    ImmutableArray<GraceNoteLayout> GraceNotes,
    ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
    ImmutableDictionary<RestShiftKey, double> RestShifts);
```

#### LayoutUtilities.cs
```csharp
/// <summary>
/// Common utility methods for layout calculations.
/// </summary>
public static class LayoutUtilities
{
    public static int GetNoteValueFromFraction(Fraction duration);
    public static double CalculateFlagHeight(int noteValue);
    public static Dictionary<int, MeasureLayout> BuildMeasureLayoutMap(ImmutableArray<SystemLayout> systems);
    public static double CalculateUpExtent(VerticalSkyline upSkyline);
    public static double CalculateDownExtent(VerticalSkyline downSkyline, double staffHeight);
}
```

---

## 3. 依存関係図

```
                    ┌─────────────────┐
                    │  LayoutEngine   │ (ファサード)
                    └────────┬────────┘
                             │
         ┌───────────────────┼───────────────────┐
         │                   │                   │
         ▼                   ▼                   ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│  SystemBreaker  │ │  SystemLayouter │ │  PageLayouter   │
└────────┬────────┘ └────────┬────────┘ └────────┬────────┘
         │                   │                   │
         │          ┌────────┴────────┐          │
         │          ▼                 │          │
         │  ┌─────────────────┐       │          │
         │  │ MeasureLayouter │       │          │
         │  └────────┬────────┘       │          │
         │           │                │          │
         │           ├────────────────┤          │
         │           ▼                ▼          │
         │   ┌─────────────────────────────┐     │
         │   │      SkylineBuilder         │◄────┘
         │   └─────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────┐
│              ElementCoordinator                 │
│  (beams, ties, slurs, dynamics, voices)         │
└─────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────┐
│  Existing Engravers (unchanged)                 │
│  BeamEngraver, TieEngraver, SlurEngraver, etc.  │
└─────────────────────────────────────────────────┘
```

---

## 4. 移行計画

### Phase 1: LayoutUtilities 抽出 (1時間)
1. `LayoutUtilities.cs` を作成
2. 静的ユーティリティメソッドを移動
3. テスト通過を確認

### Phase 2: SkylineBuilder 抽出 (2時間)
1. `SkylineBuilder.cs` を作成
2. スカイライン関連メソッドを移動
3. テスト通過を確認

### Phase 3: MeasureLayouter 抽出 (2時間)
1. `MeasureLayouter.cs` を作成
2. 小節内レイアウトメソッドを移動
3. テスト通過を確認

### Phase 4: SystemLayouter 抽出 (3時間)
1. `SystemLayouter.cs` を作成
2. システムレイアウトメソッドを移動
3. テスト通過を確認

### Phase 5: PageLayouter 抽出 (2時間)
1. `PageLayouter.cs` を作成
2. ページレイアウトメソッドを移動
3. テスト通過を確認

### Phase 6: SystemBreaker 抽出 (2時間)
1. `SystemBreaker.cs` を作成
2. 行分割ロジックを移動
3. テスト通過を確認

### Phase 7: ElementCoordinator 抽出 (3時間)
1. `ElementCoordinator.cs` を作成
2. Beam/Tie/Slur/Voice 統合メソッドを移動
3. テスト通過を確認

### Phase 8: MultiStaffLayouter 抽出 (2時間)
1. `MultiStaffLayouter.cs` を作成
2. 多譜表レイアウトメソッドを移動
3. テスト通過を確認

### Phase 9: LayoutEngine 簡素化 (1時間)
1. LayoutEngine をファサードに変換
2. 各コンポーネントを注入
3. 統合テスト実行

---

## 5. 期待される効果

### 5.1 定量的改善

| メトリクス | Before | After |
|-----------|--------|-------|
| LayoutEngine 行数 | 1,686 | ~150 |
| 最大ファイルサイズ | 68KB | ~15KB |
| 平均メソッド数/クラス | 33 | ~5 |
| テストカバレッジ容易性 | 低 | 高 |

### 5.2 定性的改善

1. **単一責務**: 各クラスが1つの責務を持つ
2. **テスト容易性**: 各コンポーネントを独立にテスト可能
3. **拡張性**: 新しい Layouter を追加しやすい
4. **可読性**: 各ファイルが理解しやすいサイズ
5. **保守性**: 変更の影響範囲が限定される

### 5.3 LILYPOND-REF の整理

| LilySharp | LilyPond |
|-----------|----------|
| SystemBreaker | constrained-breaking.cc, page-breaking.cc |
| MeasureLayouter | spacing-basic.cc, simple-spacer.cc |
| SystemLayouter | spacing-spanner.cc, system.cc |
| PageLayouter | page-spacing.cc, page-layout-problem.cc |
| SkylineBuilder | skyline.cc, page-layout-problem.cc |
| ElementCoordinator | beam.cc, tie.cc, slur.cc |

---

## 6. リスクと対策

| リスク | 影響 | 対策 |
|--------|------|------|
| 回帰バグ | 高 | 各フェーズ後にテスト実行 |
| パフォーマンス低下 | 中 | ベンチマーク監視 |
| 循環依存 | 中 | インターフェース導入 |
| 移行期間の不安定性 | 低 | 段階的移行 |

---

## 7. 次のステップ

1. [ ] 本提案のレビュー・承認
2. [ ] Phase 1 から段階的に実装開始
3. [ ] 各フェーズ完了後に work_progress.md を更新
