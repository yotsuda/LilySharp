# Skyline Architecture — 提案（2025-12-15）と、実際に出荷されたもの

> ## ⚠️ このファイルは *設計案* であって、実装の説明ではない
>
> 下の「提案」以下は **2025-12-15 の提案文書を逐語で残したもの**で、**実装はその提案とは
> 別の形になった**。提案の型名（`ISkylineProvider` / `GlyphSkylineCache` / `SkylinePair` /
> `SkylineFactory`）は **1 つも存在しない**し、「現状の問題」に挙がっている 3 点のうち
> **2 点は既に閉じている**。⇒ **ここを実装の説明として読むと、在る器を無いものとして
> 作り直すことになる**（`RULES.md` §5.0「見出しと本文が食い違っている本は、見出しだけが古い」）。
>
> **実装の説明はコードにある。** 下の §「実際に出荷されたもの」は**住所の一覧**であって、
> 機構の再説明ではない（`RULES.md` §4——同じ知識を 2 か所に置かない）。
>
> ⚠️ **2026-08-24 に主張を 1 つずつコードに当てて検算した。** 検算の方法も併記してある——
> **次に疑う人が同じ 1 コマンドで確かめられるように**。

---

## 実際に出荷されたもの（2026-08-24 検算）

| 役 | 型 | 備考 |
|---|---|---|
| LP の Building 模型（傾き＋切片） | `SkylineBuilding` ＋ `SkylineMath` | **軸中立**。垂直・水平が**この 1 つの幾何プリミティブを共有**する |
| 垂直方向 | `VerticalSkyline` | `Merge` / `Distance(other, horizonPadding)` / `Padded` / `FromGlyphOutline` |
| 水平方向 | `HorizontalSkyline` | 同じ Building 上の転置 |
| 系・譜のスカイライン生成 | `SkylineBuilder` | `BuildSystemSkylines` / `BuildStaffSkylines`。**箱か輪郭かを grob ごとに決める場所** |
| 音符間スペーシングの箱 | `ItemSkylineFactory` | LP の `Separation_item::boxes` |
| 焼き込んだ Emmentaler の実輪郭 | `GlyphSkylinesGenerated` | 生成物（`audit/scripts/Extract-EmmentalerSkylines.py`） |
| テキスト grob の輪郭 | `TextOutlineSkylines` | Skia のパスから |
| outside-staff の衝突解決 | `OutsideStaffStacker`（内部 `OutsideStaffSkylines`） | LP の `skyline_spacing` ＋ `avoid_outside_staff_collisions` |

### 「現状の問題」はいまどうなっているか

- **① 後付け／連桁・スラー・タイが入っていない → 閉じた。** `SkylineBuilder` に
  `AddBeamsToSkyline` / `AddSlursToSkyline` / `AddTiesToSkyline` /
  `AddTupletBracketsToSkyline` がある。⚠️ ただし **提案どおりの形ではない**——
  スカイラインは **MusicItem が持つのではなく、レイアウト結果から `SkylineBuilder` が組む**。
  提案 1「各 MusicItem がスカイラインを持つ」は**採られなかった**。
- **② 衝突検出がアドホック → 半分は誤り。** `BeamScoringProblem.ScoreCollisions` が
  スカイラインと別なのは**LP がそうだから**（`lily/beam-quanting.cc:1370`
  `Beam_scoring_problem::score_collisions`）。**LP に無い統合を入れるのは移植ではない**
  （`CLAUDE.md`「独自の近似を入れない」）。`SpacingRules.CreateRightSkyline` は今も在るが、
  `ItemSkylineFactory` が LP の住所つきで裏にいる。
- **③ グリフの形状を活用していない → 誤り（もう古い）。** `VerticalSkyline.FromGlyphOutline`
  が在り、**臨時記号・音部記号は実輪郭**（`GlyphSkylinesGenerated`）、**テキストは Skia の
  パス**（`TextOutlineSkylines`）。**箱か輪郭かは grob ごとの宣言**で、これは LP と同じ構造
  ——LP でも `vertical-skylines` を宣言しない grob は
  `Grob::simple_vertical_skylines_from_extents`（`lily/grob.cc:81-85`）の箱に落ちる。
  ⇒ **「まずは矩形で実装し、必要に応じて精度を上げる」は*どちらか*ではなく*両方*に落ち着いた。**

### 提案のうち何が採られ、何が採られなかったか

| 提案 | 結果 | 証拠 |
|---|---|---|
| `ISkylineProvider` | **不採用** | 0 hit |
| `GlyphSkylineCache` | **不採用**（役は `GlyphSkylinesGenerated` が生成物として担う） | 0 hit |
| `SkylinePair` 型 | **不採用**。対は C# のタプル `(HorizontalSkyline Left, HorizontalSkyline Right)` | 型宣言 0。`GlyphSkylinePair` 等は**メソッド名**の接尾辞 |
| `SkylineFactory` | **名前と射程が変わった**＝`ItemSkylineFactory`（水平の音符間スペーシング専用） | — |
| Skyline の統一 | **採られた。ただし別の形**——ジェネリック基底ではなく、**`SkylineBuilding` という軸中立プリミティブの共有** | `SkylineBuilding` の doc |
| Immutable 化（`MergedWith`） | **不採用**。`Merge` は今も破壊的で、**LP と同じ** | `MergedWith` 0 hit |

**検算の 1 コマンド**（`Skyline` を含む型がどれだけ在るか・提案の型が無いこと）:

```powershell
Select-String -Path (Get-ChildItem -Recurse -Include *.cs -Path LilySharp.Core, LilySharp.Tests) `
  -Pattern '(class|struct|record|interface)\s+\w*Skyline\w*'
foreach ($s in 'ISkylineProvider','GlyphSkylineCache','MergedWith') {
  "$s = $(@(Select-String -Path (Get-ChildItem -Recurse -Include *.cs -Path LilySharp.Core) -Pattern $s -SimpleMatch).Count)"
}
```

---

## 開いているもの（測ってある）

- **⚠️ 旧 `Skyline` クラス（`Skyline.cs`）は死んでいる。** `new Skyline(` は
  **`Skyline.cs` 自身の中の 4 か所にしか無く**、**型名を外から名指す行が 1 本も無い**
  （2026-08-24 実測）。`SkylineMergeTests` が試すのも `VerticalSkyline` のほう。
  ⇒ **削除の候補**だが、**削除はユーザー承認が要る**（`CLAUDE.md`）。
  自分の doc は「矩形のみの簡易版」と正直に名乗っており、**嘘ではなく、ただ誰も使っていない**。
- **⚠️ テキスト行（歌詞／和音の行）は per-(system, staff) のインクプロファイルを持たない**
  ——**空**（2026-08-24 実測）。行の墨は歌詞・和音の engraver が描き、**staff のプロファイルに
  一度も seed されない**。⇒ **outside-staff のパスがテキスト行の上では何も持ち上げられない**。
  これは**測れる形で台帳に載っている**: `barnumber.rows-only.row-to-ink-bottom`
  （五線なし系の小節番号が LP より帯 1 つぶん高い所に居る理由がこれ）。
  **閉じると絵が変わるので、ユーザーの判断が要る島。**

---

---

# 以下は 2025-12-15 の提案（逐語・**実装の説明ではない**）

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
