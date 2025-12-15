# LilySharp 座標系ガイドライン

## 概要

LilySharp では3つの座標単位を使用します。レイヤー間で一貫した変換ルールに従うことで、混乱を防ぎます。

## 座標単位

### 1. Staff Spaces（譜表間隔）- 主要内部単位

五線の線間を1とする実数単位。**LilyPond と互換性のある主要な内部単位**。

```
1 staff space = 五線の線間の距離
```

**用途:**
- Layout 層のすべての計算
- EngravingDefaults の定数（beam-thickness: 0.48 など）
- SMuFL グリフメトリクス
- 符幹長、連桁間隔などのパラメータ

### 2. Staff Positions（譜表位置）

五線の位置を表す単位。主に音符の垂直位置に使用。

```
1 staff position = 0.5 staff spaces
2 staff positions = 1 staff space
```

**特徴:**
- 整数値で計算しやすい
- 上方向が正（高い音）
- 五線は -4, -2, 0, +2, +4 の位置

```
位置  音符（ト音記号）
───────────────────
+10   G5 (上第2線)
 +8   E5 (上第1線)
 +6   C5 (上第1間)
 +4   A4 (第5線)
 +2   F4 (第4線)
  0   D4 (第3線 = 中央線)
 -2   B3 (第2線)
 -4   G3 (第1線)
 -6   E3 (下第1間)
 -8   C4 (下第1線 = 中央C)
-10   A2 (下第2線)
```

### 3. Pixels（ピクセル）

SVG 描画の実座標。**Renderer 層でのみ使用**。

```
1 staff space = SpaceHeight pixels (デフォルト 10)
```

**注意:** 
- SVG 座標系は Y 軸が反転（下が正）
- Staff spaces/positions とは Y 方向が逆
- Layout 層では使用しない

## 変換式

```csharp
// Staff spaces → Staff positions
double staffPos = staffSpaces * 2;

// Staff positions → Staff spaces
double staffSpaces = staffPos / 2;

// Staff spaces → Pixels (Renderer層のみ)
double pixels = staffSpaces * SpaceHeight;

// Staff positions → Pixels (Renderer層のみ)
double pixels = staffPos * SpaceHeight / 2;

// Staff positions → SVG Y座標 (Y軸反転)
double svgY = staffMiddleY - (staffPos * SpaceHeight / 2);
```

## レイヤー別の使用単位

| レイヤー | 使用単位 | 理由 |
|----------|----------|------|
| LayoutOptions | Staff spaces | 設定値の一貫性 |
| LayoutEngine | Staff spaces | LilyPond 互換 |
| SpacingRules | Staff spaces | LilyPond 互換 |
| BeamScoringProblem | Staff positions | 音符位置計算 |
| Skyline | Staff spaces | LilyPond 互換 |
| TieEngraver/SlurEngraver | Staff spaces | LilyPond 互換 |
| SvgRenderer | Pixels | SVG 出力 |

## 定数の定義場所

### EngravingDefaults.cs

LilyPond 互換の定数を一箇所に集約。**すべて staff spaces で定義**:

```csharp
public static class EngravingDefaults
{
    // 単位: staff spaces
    public const double BeamThickness = 0.48;
    public const double LineThickness = 0.1;
    public const double IdealStemLength = 3.5;
    public const double MinStemLength = 2.5;
}
```

### SpacingRules.cs

スペーシング関連の定数。**すべて staff spaces で定義**:

```csharp
public static class SpacingRules
{
    // 単位: staff spaces
    public const double QuarterNoteWidth = 3.6;
    public const double MinNoteWidth = 2.0;
    public const double ClefWidth = 3.0;
}
```

### LayoutOptions

ページレイアウトの設定。**すべて staff spaces で定義**（SpaceHeight を除く）:

```csharp
public record LayoutOptions
{
    public double SpaceHeight { get; init; } = 10;  // pixels per staff space
    public double PageWidth { get; init; } = 80;    // staff spaces
    public double MarginLeft { get; init; } = 2;    // staff spaces
    public double StaffHeight { get; init; } = 4;   // staff spaces (always 4)
}
```

## よくある間違い

### 1. Layout 層で pixels を使う

```csharp
// ❌ 間違い: Layout 層で pixels を計算
double width = noteheadWidth * SpaceHeight;

// ✅ 正しい: staff spaces のまま計算
double width = noteheadWidth;  // already in staff spaces
```

### 2. 単位の混在

```csharp
// ❌ 間違い: staff spaces と staff positions を混ぜる
double stemLength = 3.5; // staff spaces
double beamY = staffPos + stemLength;   // staffPos は staff positions!

// ✅ 正しい: 単位を揃える
double stemLengthPos = 3.5 * 2; // → 7 staff positions
double beamY = staffPos + stemLengthPos;
```

### 3. Y軸の方向を間違える（Renderer層）

```csharp
// ❌ 間違い: Y軸反転を忘れる
double noteY = systemY + (staffPos * SpaceHeight / 2);

// ✅ 正しい: SVG は Y 下が正なので反転
double noteY = staffMiddleY - (staffPos * SpaceHeight / 2);
```

## Units.cs

変換ユーティリティクラス `Units` を提供:

```csharp
// Staff spaces ↔ Staff positions
Units.SpacesToPositions(3.5)  // → 7.0
Units.PositionsToSpaces(7.0)  // → 3.5

// Staff spaces/positions → Pixels (Renderer層のみ)
Units.SpacesToPixels(3.5, spaceHeight)
Units.PositionsToPixels(7.0, spaceHeight)

// SVG Y座標変換
Units.StaffPositionToSvgY(staffPos, staffMiddleY, spaceHeight)
```

## 参考資料

- LilyPond Internals Reference: https://lilypond.org/doc/v2.24/Documentation/internals/
- SMuFL Specification: https://w3c.github.io/smufl/latest/
- LilyPond Source: `scm/define-grobs.scm`
