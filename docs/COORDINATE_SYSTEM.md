# LilySharp 座標系ガイドライン

## 概要

LilySharp では3つの座標単位を使用します。レイヤー間で一貫した変換ルールに従うことで、混乱を防ぎます。

## 座標単位

### 1. Staff Positions（譜表位置）

五線の位置を表す整数単位。レイアウト計算の基本単位。

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

**特徴:**
- 整数値で計算しやすい
- 上方向が正（高い音）
- 1 position = 0.5 staff space
- 五線は -4, -2, 0, +2, +4 の位置

### 2. Staff Spaces（譜表間隔）

五線の線間を1とする実数単位。Lilypond と SMuFL で使用。

```
1 staff space = 2 staff positions
```

**用途:**
- Lilypond 定数との互換性（beam-thickness: 0.48）
- SMuFL グリフメトリクス
- 符幹長、連桁間隔などのパラメータ

### 3. Pixels（ピクセル）

SVG 描画の実座標。Y軸は下方向が正。

```
SpaceHeight = 10 pixels  (1 staff space のピクセル数)
StaffHeight = 40 pixels  (4 staff spaces = 五線全体)
```

**注意:** 
- SVG 座標系は Y 軸が反転（下が正）
- Staff positions/spaces とは Y 方向が逆

## 変換式

```csharp
// Staff positions → Pixels (Y座標)
double pixelY = staffMiddleY - (staffPos * SpaceHeight / 2);

// Staff spaces → Staff positions
double staffPos = staffSpaces * 2;

// Staff spaces → Pixels
double pixels = staffSpaces * SpaceHeight;
```

## レイヤー別の使用単位

| レイヤー | 使用単位 | 理由 |
|----------|----------|------|
| Model (NoteItem.StaffPosition) | Staff positions | 整数で正確 |
| Layout (BeamLayout.LeftY) | Staff positions | 計算の統一 |
| Scoring (BeamScoringProblem) | Staff positions | 整数比較が容易 |
| Constants (BeamThickness) | Staff spaces | Lilypond 互換 |
| Renderer (SvgRenderer) | Pixels | SVG 出力 |

## 定数の定義場所

### EngravingDefaults.cs（推奨：将来作成）

Lilypond 互換の定数を一箇所に集約：

```csharp
public static class EngravingDefaults
{
    // 単位: staff spaces（Lilypond 互換）
    public const double BeamThickness = 0.48;
    public const double LineThickness = 0.1;
    public const double BeamTranslation = (2.0 + LineThickness - BeamThickness) / 2.0;
    
    public const double IdealStemLength = 3.5;
    public const double MinStemLength = 2.5;
}
```

### 変換時の注意

```csharp
// BeamScoringProblem では staff positions を使用
private const double IdealStemLength = 3.5 * 2; // staff spaces → staff positions

// SvgRenderer では pixels を使用
double beamThicknessPx = BeamThickness * SpaceHeight; // staff spaces → pixels
```

## 座標原点

### 五線の基準点

```
systemY (引数) = 五線の最上線の Y 座標（pixels）
staffMiddleY   = systemY + StaffHeight / 2  (中央線)
staffBottomY   = systemY + StaffHeight      (最下線)
```

### 符頭の基準点

```
x = 符頭の左端（音符中心ではない）
y = 符頭の縦中心（staff position から計算）
```

## よくある間違い

### 1. Y軸の方向を間違える

```csharp
// ❌ 間違い: staff positions で計算した後に反転を忘れる
double noteY = systemY + (staffPos * SpaceHeight / 2);

// ✅ 正しい: SVG は Y 下が正なので反転
double noteY = staffMiddleY - (staffPos * SpaceHeight / 2);
```

### 2. 単位の混在

```csharp
// ❌ 間違い: staff spaces と staff positions を混ぜる
double stemLength = IdealStemLength; // 3.5 staff spaces
double beamY = noteY + stemLength;   // noteY は staff positions

// ✅ 正しい: 単位を揃える
double stemLengthStaffPos = IdealStemLength * 2; // → 7 staff positions
double beamY = noteY + stemLengthStaffPos;
```

### 3. 変換係数を間違える

```csharp
// ❌ 間違い: staff positions → pixels で SpaceHeight を直接使う
double pixelY = staffPos * SpaceHeight;

// ✅ 正しい: 1 staff position = 0.5 staff space
double pixelY = staffPos * SpaceHeight / 2;
```

## Lilypond 定義値

調号の配置位置など、Lilypond の `define-grobs.scm` で定義されている値：

```scheme
;; 調号のシャープ位置（treble clef）: F#, C#, G#, D#, A#, E#, B#
(sharp-positions . (4 5 4 2 3 2 3))

;; 調号のフラット位置（treble clef）: Bb, Eb, Ab, Db, Gb, Cb, Fb  
(flat-positions . (2 3 4 2 1 2 1))
```

Bass clef の場合は treble から -2 した位置。

## 参考資料

- Lilypond Internals Reference: https://lilypond.org/doc/v2.24/Documentation/internals/
- SMuFL Specification: https://w3c.github.io/smufl/latest/
- Lilypond Source: `scm/define-grobs.scm` (調号位置など)