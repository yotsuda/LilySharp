# Stage 4 — internal Y-up + single output flip (design)

`stage4-yup-inversion` ブランチの設計。Lily# の垂直座標を LP と同じ「**内部 Y-up・出力時に
単一フリップ**」へ移す。**出力は byte 一致が成功条件**(設計 doc が言う「出力同値」)。

## 0. 現状の確定事実(精査済み)

- Lily# は **native device-Y-down**。`page.scm`/`framework-ps.scm` 的な「per-grob `pageHeight−y`
  フリップ」は**存在しない**(設計 doc の「per-grob フリップを単一に置換」前提は Lily# に不一致)。
  ⇒ Stage 4 は「最後の機械的フリップ」ではなく、**device→Y-up の本格的な座標反転**(~170箇所)。
- device-Y の起点:
  - `LayoutUtilities.CalculateFirstSystemY = headerBottom + systemUpExtent + topSystemPadding`
    (header を上端に、下方向へ)。
  - `LayoutEngine`: `currentY += StaffHeight + SystemSpacing`(下方向に累積)。
  - `FindStaffYInSystem` が staff の絶対 device-Y を返す。
  - `StaffFrame.PositionToDevice = staffMiddleY − pos/2`(per-staff の Y-up→device 反射)。
- backend は **3つ(SVG/PNG/PDF)**、すべて `IDrawingContext`。`DrawingTransform`(TranslateY/ScaleY)
  を group に適用できる。`SharedRenderer` は既に page 内容を `BeginGroup(Translate(MarginLeft,0))`
  (marginScope)で包んでいる。

## 1. 単一フリップの機構(backend 非依存)

page 内容全体を **1個の flip transform group** で包む:

```csharp
// y_device = page.Height - y_up   ( = TranslateY + y_up * ScaleY )
var flip = new DrawingTransform(TranslateY: page.Height, ScaleY: -1);
using (gc.BeginGroup(flip)) { /* draw everything in Y-up */ }
```

SVG/PNG/PDF は既存の margin scope と同じ経路で ScaleY を処理する=**この1個が LP の
stencil-time フリップに相当**。`StaffFrame` の per-staff 反射は**廃止**(フリップが1箇所に集約)。

## 2. 内部 Y-up 規約

- **Y=0 は page 下端、上方向が正**(`y_up = page.Height − y_device`)。
- 全座標を Y-up で native に再導出する(`page.Height − x` を撒くのではなく、式ごと反転):
  - header: device `MarginTop` → Y-up `page.Height − MarginTop`。
  - system/staff 配置: 下方向累積 → **上方向累積**(`currentY -= …` 方向、原点は page 上端の
    Y-up 値から)。
  - `StaffFrame.PositionToDevice(staffMiddleY − pos/2)` → `PositionToUp(staffMiddleY_up + pos/2)`
    (staff-position が高い=Y-up が大きい、符号反転)。
  - skyline の up/down extent、tie/slur/beam の Y、annotation の上下、すべて符号反転。

## 3. 影響範囲(~170 箇所 / ~20 ファイル)

`SharedRenderer`(60)・`SkylineBuilder`(16)・`StaffFrame`(12・廃止/反転)・`ElementCoordinator`(7)
・各 engraver(Tie/Glissando/Stem/Arpeggio/Fingering/TupletBracket/TieVariant…)・`MultiStaffLayouter`
・`LayoutEngine`・`LayoutUtilities`。

## 4. 実行戦略(all-or-nothing)

- **byte 一致は全反転完了まで検証不能**(途中は「一部 Y-up・一部 device」でパイプライン破綻)。
  ⇒ 一括反転 → build → snapshot 全 byte 比較 → 符号ミスを潰す、の atomic な進め方。
- 成功条件: **42 snapshot が byte 不変**(出力同値)。差が出たら符号ミスなので修正、リベースしない
  (Stage 4 は出力を変えてはいけない)。
- 順序の目安: (1) flip group を SharedRenderer に追加(この時点で全部上下反転して壊れる)→
  (2) layout の Y 起点(CalculateFirstSystemY / currentY 累積 / FindStaffYInSystem)を Y-up へ →
  (3) StaffFrame を PositionToUp へ → (4) renderer 60 箇所の Y 算術を Y-up へ →
  (5) 各 engraver → (6) skyline → build green → snapshot byte 比較で詰める。

## 5. 注意

- これは **出力便益ゼロ**・構造忠実性のための投資(LP 式を符号一致で移植できる素地)。
- master は安定(本ブランチで隔離)。完成・byte 一致確認後に master へマージ。
