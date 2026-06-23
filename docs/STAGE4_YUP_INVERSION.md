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

> **改訂(2026-06-23)**: 旧 §1 の「page 内容全体を `DrawingTransform(TranslateY: page.Height,
> ScaleY: -1)` の group で包む」案は**破棄**。理由は2つ、いずれも実コード照合で判明:
> 1. **glyph が上下反転する**。`SvgDrawingContext.BeginGroup` は `<g transform="… scale(1,-1)">`
>    を素で吐き、`DrawGlyph`/`DrawText` は per-glyph の打ち消し変換を持たない。`scale(1,-1)` の
>    親 group の下では notehead/clef/歌詞そのものが鏡像(上下逆)になる。LP の stencil-time flip は
>    **配置だけ**を反転し glyph は正立で吐くので、「この group 1個が LP flip 相当」は誤り。
> 2. **byte 一致と論理矛盾**。group を被せて全 Y を Y-up 値へ書き換えれば、出力バイト列は構造ごと
>    変わる。§4 の「42 snapshot byte 不変」は鏡を被せて達成するものではなく、**内部を Y-up に
>    作り替えても出力ピクセルが厳密に同じ=リファクタが挙動保存した証明**として使う。

**正しい機構 = 出力境界の Y-flip デコレータ(算術変換、鏡ではない)**。`IDrawingContext` を包む
薄いデコレータ `YFlipDrawingContext` を1個だけ挟み、全プリミティブの Y を `y_device = H − y_up`
で device へ落とす(H = page.Height)。座標の数値を変換するだけなので **glyph は正立のまま**=
LP の stencil-time flip と同じ。`SharedRenderer.RenderTo` の `gc = doc.BeginPage(w, h)` を
`gc = new YFlipDrawingContext(doc.BeginPage(w, h), page.Height)` に置き換える**ただ1箇所**が flip。

プリミティブ別の変換(H = page.Height):
- 点系(`DrawLine` の各端点・`DrawCircle`/`DrawEllipse` の cy・`DrawClosedBezier` の各点・
  `DrawGlyph`/`DrawText` の baseline): `y → H − y`。glyph/text は鏡像化しない(anchor 不変)。
- `DrawRectangle(x, y, w, h)`: y は「視覚上端の Y-up 座標」と定義し `y → H − y`(高さ h は下方向、不変)。
- `BeginGroup(tx, ty, sx, sy)`: device へ**共役変換**して渡す。
  `flip ∘ T_up = T_dev ∘ flip` を解くと
  `T_dev = (TranslateX=tx, TranslateY = H − ty − sy·H, ScaleX=sx, ScaleY=sy)`。
  **ScaleY は正のまま**(鏡化しない)。nest しても共役は合成する(`T1_dev∘T2_dev = flip∘T1_up∘T2_up∘flip⁻¹`)。
  既存 marginScope(tx=ML, ty=0, s=1)は `translate(ML, 0)` に落ち**今と完全一致**。

`StaffFrame` の per-staff 反射(`PositionToDevice = staffMiddleY − pos/2` 等)は、この単一境界に
吸収され**廃止**。内部は全て Y-up(`PositionToUp = staffMiddleY_up + pos/2`)で計算する。

> 注: multi-page の `SvgDocumentContext.Assemble` が各ページ content を `translate(0, yOffset)` で
> 積む処理は device 空間でデコレータの外側なので不変(各ページ content が byte 一致なら積み上げも一致)。

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

## 3.5. 改訂(2026-06-23 その2): all-or-nothing をやめ、相対 Y-up grob への漸進へ

実コード精査で2つの構造的事実が判明し、§4 の「ページ全体一括 Y-up・atomic・byte 一致」路線は
現モデルに不適と確定した。**byte 一致は要件から外す**(ユーザー判断: 正しい実装なら出力はより
正しくなってよい。検証は LP 比較 + snapshot 再ベースライン)。

- **循環**: 絶対 Y-up は `H`(page.Height)が要るが、`LayoutEngine` の prelim extent pass
  (`EnrichExtentsWithAnnotationProtrusions`)が `H` 確定前に絶対 tie/slur Y と `system.Y` を使う。
  `H` は extent に依存し、extent は絶対 Y に依存する=循環。LP は絶対フレームを最後まで持たず
  (相対 Y-up offset + refpoint)出力時に1回 flip して回避している。
- **フレーム結合**: `system.Y` は「layout が産む絶対 Y」と「render の消費」が共有する device
  フレーム。within-staff (a) は既に `StaffFrame` 経由で Y-up 記述済みだが、overlays (b) は
  `system.Y + offset`、engraver は `system.Y` から絶対 device Y を産む。だから「render パスだけ
  Y-up」は無意味な `H−(H−y)` 往復になるか engraver へ波及するかのどちらかで、分離できない。

**正しい漸進(LP 忠実)**:
- **各 increment = grob ファミリを1つずつ相対 Y-up フレームへ移す**。各 grob は自分の refpoint
  (staff/system)からの相対 Y-up offset で計算し、device へは**自分の draw 境界で**変換する
  (`StaffFrame`/skyline が per-staff で既にやっているのと同じ)。小さく・独立に検証可能・holistic
  結合なし。検証は LP 比較と snapshot(改善なら再ベースライン)。
- **page-level decorator(`YFlipDrawingContext`)は最終ステップ**。全 grob が相対 Y-up に揃った
  時点で、per-grob の device 変換を畳んで単一 flip に置換する。`5e8f899` の decorator は
  「到達点」であって今は配線しない(正しく死にコード)。
- 推奨初手 = layout 時 engraver(移植の痛点)を1つ。自己完結で検証しやすいものから。

## 4. 旧・実行戦略(破棄: all-or-nothing。§3.5 に置換)

- **byte 一致は全反転完了まで検証不能**(途中は「一部 Y-up・一部 device」でパイプライン破綻)。
  ⇒ 一括反転 → build → snapshot 全 byte 比較 → 符号ミスを潰す、の atomic な進め方。
- 成功条件: **42 snapshot が byte 不変**(出力同値)。差が出たら符号ミスなので修正、リベースしない
  (Stage 4 は出力を変えてはいけない)。
- 順序の目安: (0) `YFlipDrawingContext` デコレータを追加(未配線=死にコード、挙動不変・commit 可)→
  (1) `SharedRenderer.RenderTo` の `gc` を decorator で包む(この時点で全部上下反転して壊れる)→
  (2) layout の Y 起点(CalculateFirstSystemY / currentY 累積 / FindStaffYInSystem)を Y-up へ →
  (3) StaffFrame を PositionToUp へ → (4) renderer の Y 算術を Y-up へ →
  (5) 各 engraver → (6) skyline → build green → snapshot byte 比較で詰める。
  (1)〜(6) は atomic(途中は壊れる)。(0) だけ先行 commit してよい。

## 5. 注意

- これは **出力便益ゼロ**・構造忠実性のための投資(LP 式を符号一致で移植できる素地)。
- master は安定(本ブランチで隔離)。完成・byte 一致確認後に master へマージ。
