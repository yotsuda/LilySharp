# Skyline Architecture Redesign

## 現状の問題

### 1. スカイラインが後付け
- `VerticalSkyline` / `HorizontalSkyline` は存在するが、MusicItem と統合されていない
- `BuildSystemSkylines()` で音符のみを手動で追加
- 連桁、スラー、タイなどがスカイラインに含まれない

### 2. 衝突検出がアドホック
- `SpacingRules.CreateRightSkyline()` - 音符間の水平衝突
- `BeamScoringProblem.ScoreCollisions()` - 連桁と音符の衝突
- `LayoutEngine.BuildSystemSkylines()` - 垂直スペーシング
- 各所でバラバラに実装、重複あり

### 3. グリフの形状を活用していない
- SMuFL の矩形 bounding box のみ使用
- グリフの実際の形状（凹凸）を考慮していない
- 結果として過剰なスペースや衝突が発生する可能性

---

## LilyPond のアプローチ

```
grob (graphical object)
  ├── horizontal-skylines  (左右の衝突検出)
  ├── vertical-skylines    (上下の衝突検出)
  └── stencil (描画データ)

各 grob がスカイラインを持ち、FreeType でグリフアウトラインから生成
```

**利点:** 統一されたモデル
**欠点:** Scheme による動的計算、C++ と Scheme の混在

---

## 新しいアーキテクチャ提案

### 原則

1. **各 MusicItem がスカイラインを持つ** - 責務の明確化
2. **スカイラインは Immutable** - 再利用可能、キャッシュ可能
3. **グリフスカイラインの事前計算** - フォントロード時にキャッシュ
4. **合成パターン** - 複合アイテム（和音等）は子のスカイラインを合成

### コンポーネント

```
ISkylineProvider
  │
  ├── NoteItem         → GlyphSkylineCache から取得
  ├── RestItem         → GlyphSkylineCache から取得
  ├── ChordItem        → 子ノートのスカイラインを合成
  ├── BeamGroup        → 斜め Building を生成
  ├── TieLayout        → Bézier 曲線からスカイラインを生成
  └── SlurLayout       → Bézier 曲線からスカイラインを生成

GlyphSkylineCache
  - SMuFL グリフごとにスカイラインをキャッシュ
  - アプリケーション起動時に主要グリフを事前計算
  - 必要に応じて遅延計算

SkylineFactory
  - グリフからスカイラインを生成
  - 矩形 (Box) / Bézier 曲線 / 複合形状に対応
```

### インターフェース

```csharp
/// <summary>
/// スカイラインを提供できるオブジェクト
/// </summary>
public interface ISkylineProvider
{
    /// <summary>左右方向のスカイライン（音符間の衝突検出用）</summary>
    SkylinePair HorizontalSkylines { get; }
    
    /// <summary>上下方向のスカイライン（システム間スペーシング用）</summary>
    SkylinePair VerticalSkylines { get; }
}

/// <summary>
/// UP/DOWN または LEFT/RIGHT のペア
/// </summary>
public readonly record struct SkylinePair(Skyline Primary, Skyline Secondary);
```

### データフロー

```
1. フォントロード
   SMuFL JSON → GlyphSkylineCache (主要グリフのスカイラインを事前計算)

2. パース & モデル構築
   Source → SyntaxTree → Score → MusicItem (ISkylineProvider)

3. レイアウト計算
   MusicItem.HorizontalSkylines → 音符間スペーシング
   MusicItem.VerticalSkylines → システム間スペーシング
   
4. 衝突回避
   SkylinePair.distance() → 最小距離
   SkylinePair.merge() → 複合スカイライン
```

---

## 実装計画

### Phase 1: 基盤整備
- [ ] `ISkylineProvider` インターフェース定義
- [ ] `SkylinePair` 構造体
- [ ] `GlyphSkylineCache` クラス
- [ ] 既存の `Skyline` を統一（HorizontalSkyline / VerticalSkyline を共通基盤に）

### Phase 2: グリフスカイライン
- [ ] SMuFL グリフの矩形スカイライン生成
- [ ] 主要グリフ（音符、休符、臨時記号）の事前キャッシュ
- [ ] `NoteItem`, `RestItem` に `ISkylineProvider` 実装

### Phase 3: 複合スカイライン
- [ ] `ChordItem` - 子ノートの合成
- [ ] `BeamGroup` - 斜め Building 生成
- [ ] `TieLayout`, `SlurLayout` - Bézier からスカイライン

### Phase 4: レイアウト統合
- [ ] `SpacingRules` をスカイラインベースに統一
- [ ] `LayoutEngine` の衝突検出を `ISkylineProvider` 経由に
- [ ] アドホック実装の削除

### Phase 5: 最適化
- [ ] 遅延計算とキャッシュ戦略
- [ ] 並列計算の検討
- [ ] ベンチマーク

---

## 設計判断

### Q: グリフのアウトライン形状を使うか？

**選択肢:**
1. **矩形 bounding box のみ** - 簡単だが精度が低い
2. **グリフアウトライン** - 精度は高いが FreeType 依存
3. **中間解: 多角形近似** - SMuFL の anchor points を活用

**提案:** まずは矩形で実装し、必要に応じて精度を上げる

### Q: Skyline の統一

**現状:**
- `Skyline` (水平、矩形のみ)
- `HorizontalSkyline` (水平、斜め対応)
- `VerticalSkyline` (垂直、斜め対応)

**提案:** 
- `Skyline<TAxis>` ジェネリック基底クラス
- または単一の `Skyline` クラスに方向パラメータ

### Q: Immutability

**LilyPond:** スカイラインは mutable、merge で変更
**提案:** Immutable にして新しいインスタンスを返す

```csharp
// Before (mutable)
skyline.Merge(other);

// After (immutable)
var merged = skyline.MergedWith(other);
```

**利点:** スレッドセーフ、キャッシュ可能、デバッグしやすい

---

## 次のアクション

1. このドキュメントをレビュー
2. `ISkylineProvider` の定義を確定
3. Phase 1 の実装開始
