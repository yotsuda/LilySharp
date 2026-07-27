# LilyPond 座標系 忠実移植 監査 (LILYPOND-REF coordinate-fidelity audit)

2026-07-19 起票 / **2026-07-21 更新**（YFlip 配線状況の訂正・解決済み指摘のマーク・§3.I 追加）。
全 `LILYPOND-REF`(1757件/~170ファイル)のうち**幾何(座標)に関わるもの**を対象に、
LP の座標系（**方向** Y-up/Y-down と**単位** staff-space/staff-position…）を Lily# が
**字面通り**同じ形で導入できているかを監査。目的は出力の正しさ以上に**アーキテクチャ忠実性**
（[[LP_COORDINATE_MODEL.md]] の投資理由と同じ）。既存 `COORDINATE_SYSTEM.md` は Stage-4 Y-up
反転・beam quanter ss 統一の**前**の記述で一部陳腐化しており、本書が現況の正とする。

分類: **faithful**=LP と同方向・同単位（境界変換のみ）／**hack**=layout 内部で単位/方向差を
その場係数（×2・÷2・符号反転・±pixel）で埋める代償／**stale**=参照先 LP と不一致 or 行ズレ。

---

## 1. LP の座標系（洗い出し・ground truth）

LP は**単一座標系でなく grob 親子チェーンの相対参照点の木**。ただし単位・方向は一貫している。

| # | フレーム | 原点 | 方向 | 単位 | 使用箇所（LP） |
|---|---|---|---|---|---|
| **L1** | Staff-space 幾何 | grob refpoint | **Y-up** / X-right | **staff-space (ss)** | 全 layout/scoring: `beam-quanting.cc`, `slur-scoring.cc`, `skyline.cc`, `spacing-spanner.cc`, `align-interface.cc` |
| **L2** | Staff-position（音高） | 中央線=0 | **Y-up** | **half-staff-space**（音高は整数） | notehead/rest の縦位置。`staff-symbol-referencer.cc:130` `y = pos*ss/2`。幾何へは `*0.5` |
| **L3** | フォント設計単位 | glyph 原点 | Y-up | 設計単位 → `output-scale`(≈×0.04) → ss | Emmentaler/SMuFL glyph metrics・stencil |
| **L4** | Device / page 出力 | page 左上 | **Y-DOWN** | device (big-point / output-scale) | **出力時のみ** `framework-ps.scm:109`, cairo。L1→L4 は**単一フリップ** |
| **L5** | X スペーシング格子 | PaperColumn（**musical / non-musical の2種**） | X-right | ss | 水平スペーシング spring。非音楽列は `BreakAlignment` で clef→staff-bar→key→time を順序付け |

要点: **内部は例外なく L1(ss, Y-up)**。L2 は音高の整数表現で、幾何に入る時 `*0.5` で L1 化。
Y の反転は L4（出力）で**一度だけ**。per-grob 反転は存在しない。

---

## 2. Lily# の座標系（現況・target=LP mirror）

| # | フレーム | 方向 | 単位 | 対応 LP | 状態 |
|---|---|---|---|---|---|
| **LS1** | Layout staff-space | **Y-up**（Stage-4 反転後） | ss | L1 | 主内部単位（目標） |
| **LS2** | Staff-position | Y-up | half-space（整数） | L2 | 音高/notehead Y。幾何へ `*0.5` |
| **LS3** | Glyph metrics | Y-up | ss（`GlyphMetricsGenerated`） | L3 | ※agent H |
| **LS4** | Render device | **Y-down** | pixel（`×SpaceHeight`） | L4 | **単一 `YFlipDrawingContext`** フリップ（[[STAGE4_YUP_INVERSION.md]]） |
| **LS5** | X スペーシング | X-right | ss | L5 | 単位/方向 faithful。ただし **non-musical 列の型が無い**（§3.I） |

歴史的経緯: かつて layout が **page-absolute Y-down + notehead 左端アンカー**、一部 Y に
**half-space** を使用。Stage-4 で render を native Y-up 化・単一フリップに集約。beam quanter は
本セッションで half-space→ss 統一。**残る half-space/Y-down/pixel-leak/代償係数が本監査の標的**。

### 2.1 ⚠️ `staffMiddleY` は**同じ名前で逆向き**（2026-07-22 監査・**供給源は修正済／消費側は島2**）

LP の内部は `Page_layout_problem` の**ページ配置だけ Y-down**で、それ以外は全部 Y-up
（`page-layout-problem.cc:886-892` が自ら「configuration と `solution_` は紙面上端が 0 で下が正、
ただし譜内は上が正」と書き、TODO で「紛らわしい」と認めている）。
**したがって Lily# のページ積み上げ Y-down は LP 忠実**であり、直すべきは**譜内**の Y-down。

その譜内で、`staffMiddleY` という**1 つの名前が 9 ファイルで 2 つの意味**を持っている:

| ファイル | 定義 | フレーム |
|---|---|---|
| `SharedRenderer.Noteheads.cs` | `staffY - StaffHeight/2` | **Y-up** |
| `SharedRenderer.Beams.cs` | `staffY - StaffHeight/2` | **Y-up** |
| `SharedRenderer.GraceNotes.cs` | `syUp - g.StaffYOffset - StaffHeight/2` | **Y-up** |
| `TieFormattingProblem.cs` | `_y + notePos * 0.5`（page Y） | Y-up（`_y` 依存） |
| `ElementCoordinator.cs` | `staffY **+** StaffHeight/2` | **Y-down** |
| `StemCalculator.cs` | `staffTopY **+** staffHeight/2` | **Y-down** |
| `SkylineBuilder.cs` | 譜ごと: **0**（原点＝中央線）／system: `-_staffHeight/2` | **Y-up** ／ Y-up |
| `TupletBracketEngraver.cs` | `const StaffMiddleY = 2.0` | **Y-down** |
| `ElementCoordinator.cs`(2) | `StaffOffsetInSystem(...)` | **Y-down** |

**符号を見ないと frame が判らない**＝ §5.2 の「符号一致で字面移植」が成立しない層。
`LayoutUtilities.StaffOffsetInSystemDown` が Y-down 側の供給源で、
`FindStaffYInSystem` / `ResolveStaffMiddleY` / `StaffTopYUp` / `SystemTopYUp` は既に Y-up。

✅ **供給源そのものは `ff64f38e` で反転した**（島1 の atomic flip）。`StaffLayout.Y` /
`StaffGroupLayout.Y` / `GrandStaffLayout.BraceTop`・`BraceBottom` が Y-up 格納になり、
**`StaffOffsetInSystemUp` が primitive（`staff.Y` をそのまま返す）／`Down` がその否定**へ。
`FindStaffYInSystem` は `system.Y + staff.Y` ＝ LP が実際にやる素の和になった。
LILYPOND-REF: `align-interface.cc:274`（`where += stacking_dir * dy`, `stacking_dir = DOWN = -1`）。
⚠️ **上表の Y-down 行が消えたわけではない**——それらは意図的な device 島（島2）で、
`Down` はその**縁の反射**として残す。島2 に着手するときの入口がこの表。

✅ **譜ごとのスカイラインは `6bb5a1de` で refpoint 枠へ移した**（2026-07-27・**出力完全不変**）。
`SkylineBuilder.BuildStaffSkylines` の原点は**上端線 → 中央線**。LP の VerticalAxisGroup
スカイラインと同じ枠で、`Align_interface` が測るのは refpoint 間だから
（`align-interface.cc:228`）。⇒ 読み手が各自で半譜を足し戻していたアダプタが消えた
（`LyricEngraver` の 2 箇所）。

⚠️ **これは 3 つの枠が並んでいる層で、統合されたのは 1 つだけ**:

| スカイライン | 原点 |
|---|---|
| 譜ごと（`BuildStaffSkylines`） | **その譜の refpoint（中央線）** ← 今回ここを移した |
| system（`BuildSystemSkylines`） | **最上段譜の上端線**＝ system 原点（未統合・別用途） |
| seed の入力 | **2 つ**——`staffMiddleUp` 系（五線・符頭・強弱・アーティキュレーション・ビーム）と `staffTopUp` 系（tuplet ブラケット・スラー・タイ。engraver が譜 offset 無しで走るため） |

⚠️ **`staffTopUp` は消せる項ではない**。engraver 側の出力枠が上端線基準であることの反映で、
**原点から導出**して 1 箇所に置いてある（上端線原点の時代は 0 だった）。
⚠️ **tuplet 経路は seed が 2 回**（ブラケット線と**数字**）。数字は線より外へ届くので
**線だけ変換すると束縛インクが元の位置に残る**。
⚠️ **枠を触る前に `StaffSkylineFrameTests` を読むこと。** 経路ごとに枠を主張していて、
**1 回目の反転はこのテストが無かったので失敗した**（spacing の測定が 6 つ同時に落ちるだけで、
どの seed が動いたか誰も言わなかった）。島1 手順の「格納値を主張するテストを先に書く」は
比喩ではない。

⚠️ **clef をスカイラインに入れたときの `+1.110000`（HANDOFF §1）はこの層にある。**
`VerticalSkyline` 自体は正しいことを `SkylineMergeTests.Distance_BetweenFacingSystems_IsTheirInkAndNoMore`
で固定済み（`MaxHeight` ±一致・`Distance` 7.350000）。**残差は消費側＝この表の混在。**

**着手順序**: ①名前で frame を明示（改名は**ユーザーが MSVS のリファクタで**実施）→
②譜内を Y-up に統一 → ③そのうえで clef を再投入。**ページ配置（`solution_` 相当）は
LP と同じ Y-down のまま残す。**

---

## 3. サブシステム別 監査

### 3.A Beam（本セッションで深掘り済・確定）

#### 座標インベントリ
| File | 量 | 軸 | 方向 | 単位 | 根拠 |
|---|---|---|---|---|---|
| BeamScoringProblem.cs | `config.LeftY/RightY`, `_unquanted*Y`, `_musicalDy`, seed, damped | Y | Y-up | **ss** | 本セッションで half→ss 統一。LP `beam-quanting.cc` 全 ss |
| " | `_xSpan`, `_stemXPositions` | X | — | ss | L1 |
| " | `_staffPositions`,`_headMin/Max` | Y | Y-up | **half-space（整数）** | 音高=L2。幾何へ `*0.5`（`calc_stem_info` 準拠） |
| " | `StaffRadius=2.0` | Y | — | ss（=2ss 半譜高） | LP `staff_radius_=2.0` ss |
| " | `_beamThickness/…Translation`, quant 定数 | Y/scalar | — | ss | `EngravingDefaults` |
| " | Solve() 戻り値 | Y | Y-up | **half-space**（境界で ×2） | caller 契約（下記 render /2） |
| StemCalculator.cs | `StemInfo.IdealY/ShortestY` | Y | Y-up | **ss（絶対）** | `calc_stem_info` 式一致を確認 |
| BeamSubdivision.cs | rank→Y = primary + `BeamTranslation`×rank | Y | Y-up | ss | `beam.cc calc_beam_segments`（c307edcc で移植） |
| SharedRenderer.Beams.cs | `leftBeamY=staffMiddleY+beam.LeftY/2` | Y | Y-up(native) | 境界 half→ss→page | render 境界 |
| BeamCollision.MinY/MaxY | 衝突対象 Y | Y | Y-up | **half-space** | ElementCoordinator 入力（未 ss 化） |

#### 忠実性所見
- **[low] BeamScoringProblem.cs:213 / SharedRenderer.Beams.cs:133** — 量子化器は ss 内部なのに
  戻り値を **half-space(×2)**、render で **/2** に戻す**往復**。LP の Beam `positions` は ss。
  round-trip でクリーンだが**非 LP の caller 契約**。将来 ss 直返しに単純化可 → 分類 **hack(軽)**。
- **[med] BeamScoringProblem.cs `ScoreCollisions`（~1050-1096）** — collision scorer だけ
  **half-space island**: `centerBeamY` を ×2、`collision.MinY/MaxY` half-space、`padding×2`,
  `stackInner×2`、そこに **ss の `_beamThickness` を混入**（単位混在）。LP `score_collisions` は
  全 ss（`beam-quanting.cc:1370`）。→ **hack**。LP の segment ベース `add_collision` を
  BeamSubdivision 駆動で ss 移植する follow-up 済み記載（本ファイル冒頭コメント）。
- **[med] SharedRenderer.Beams.cs:152-153** — **tab** beam は `TabBeamQuant` が **device Y** を返し
  `pageHeight - y` で page Y-up へ**その場フリップ**。notation beam（native Y-up）と非対称 → **hack**。
  tab quanter を native Y-up 化すれば解消。
- **[✅解消] StemCalculator.cs:205（XML doc）** — 戻り値を「in staff positions/half-spaces」と記すが
  実際は **ss** → doc 修正のみ。**対処済**: 現行 :205-207 は "in staff-spaces … NOT half-spaces" と明記。
- それ以外（BeamScoringProblem の seed/damping/quant/scorer 群、BeamConfiguration、
  BeamQuantParameters、BeamSubdivision）は **ss + Y-up で faithful**（本セッションで LP 逐一照合）。

---

<!-- 以下 3.B〜3.H は並列監査エージェントの報告を統合して追記する -->
### 3.B Slur & Tie

| File | 量 | 軸 | 方向 | 単位 | 根拠 |
|---|---|---|---|---|---|
| SlurScoringProblem.cs | `_startY/_endY`（負 device） | Y | up+ | ss（絶対page=−device） | :130-132 |
| SlurScoringProblem.cs | `StaffLinePositions {0,-1,-2,-3,-4}` | Y | up+ | **ss（staff-相対・上線=0）** | :106 |
| SlurScoringProblem.cs | `InterpolateSlurY` arc `4·h·t(1−t)` | Y | up+ | ss（放物・peak=h） | :176-182 |
| TieFormattingProblem.cs | `y=pos·0.5`, `AttachmentY`, `tipPos/topPos` | Y | up+ | ss / staff-position | :211-350 |
| TieDetails.cs | Tip/CenterStaffLineClearance(0.225/0.3) | Y | n-a | ss（LP half-space ÷2） | :79,:86 |
| TieVariantEngraver.cs | `noteY`,`Control*.Y` | Y | **down+（device）** | ss | :159-178 |
| SharedRenderer.Curves.cs | `DrawBow` `pageHeight+YUp` flip, `perp=±0.5·thick` | Y | up→device flip | ss | :89-140 |

#### 忠実性所見
- **[high・frame mismatch] SlurScoringProblem.cs:106,:456-485** — LP は slur の staff-line 回避を
  **staff-相対 position** で行う（`on_staff_line(round_p)`, `slur-configuration.cc:42-76`）。Lily# は
  `config.StartY/EndY/peakY`＝**絶対-page Y-up**（`−(PositionToDevice+staffMiddleY)`）を、staff-**相対**
  ハードコード配列 `{0,-1,-2,-3,-4}` と比較 → **原点不一致**。page 頂上でない譜では距離が ~`staffMiddleY`
  （数十 ss）≫ `gapInside=0.2` となり、端点/peak の staffline ペナルティが**常時 no-op**。device-Y≈0 の譜
  以外で slur が staff line を避けない。**本監査で最も座標モデルに直結する不具合**（要修正: 配列を絶対-page 化 or
  config を staff-相対化）。
- **[med] TieFormattingProblem.cs:256** — LP `center_tie_vertically` は `Δy=−dir·(edge+mid)/2`,
  `mid=curve(0.5).y=0.75·h` ⇒ `−dir·0.375·h`。Lily# は `−dir·h/2=−dir·0.5·h`（制御点高を曲線 extent と誤認）。
  → **定数誤り**。小 h の tie が ~0.12·h だけ dir 側にずれる。
- **[med] TieFormattingProblem.cs:458-473** — tie-tie の center を `±Height`（1.0·h）とするが LP は
  `curve(0.5).y=0.75·h`。両側 ±Height で内部一貫のため edge 距離は不変だが、高さの異なる積層 tie で
  center-center 衝突/単調性判定が LP からドリフト。→ **係数誤り(1.0 vs 0.75)**。
- **[low] SlurScoringProblem.cs:176-182** — encompass/peak 標本を peak=Height 放物で近似するが実 bezier は
  0.75·Height。形状近似（方向は正）。
- **[low] TieVariantEngraver.cs:159-178** — なお **device Y-down** で動作（移行済 Tie/Slur は page Y-up 格納）。
  DrawBow 境界で `−v.Y` 反射され出力は正＝**faithful boundary** だが兄弟と frame 非統一。
- **[low・stale] SlurScoringProblem.cs:157** — `_musicalDy` の ref `slur-scoring.cc:180-190` は stem/flag extent
  で誤り（実 :334-341）。計算自体は符号・単位 faithful。BezierBow.cs:67 等も 2 行ドリフト（数式は byte-faithful）。
- **[faithful]** BezierBow, SlurScoreParameters, TieDetails（half-space→ss 境界変換を文書化・LP が half で比較する
  箇所は `×2` で再展開）, BowLayout/SlurLayout/TieLayout（全 Y を page-Y-up=−device と明記）,
  Curves.DrawBow（唯一の Y-up→device flip＋perp 反転は flip-parity 修正）, 各 scorer（SLOPE/EDGES/ENCOMPASS は
  slur-configuration.cc と符号一致）。
### 3.C Skylines

コア skyline プリミティブは **native Y-up・ss で `skyline.cc` と符号まで一致（faithful）**。
Y フリップは `StaffFrame`（device↔up の involution）に**集約**、per-grob 反転なし。

| File | 量 | 軸 | 方向 | 単位 | 根拠 |
|---|---|---|---|---|---|
| VerticalSkyline.cs | `sky=±1`, `FromBox`高さ=`sky*edge`, `Raise`, `Padded`(45°), `MaxHeight` | Y | **Y-up native** | ss | `skyline.cc:104-680` と一致 |
| SkylineBuilder.cs | `noteUp=pos*0.5`, `ToSystemUp(up)=up+staffMiddleUp`, stem-up は加算。譜ごとは `staffMiddleUp=0`（原点＝refpoint）／上端線基準の seed は `+staffTopUp` | Y | Y-up native | ss（pos=half→×0.5） | :525-620 |
| SkylineMath `Distance` | penetration=max(v1+v2) | scalar | 内部 | ss | `skyline.cc:618-649` |
| HorizontalSkyline / ItemSkylineFactory / Skyline(flat) | 値=`sky*x`／`noteY-BBox.Top` | X値 / **Y horizon=device Y-down** | X sign-conv | ss | HorizontalSkyline:105-112, ItemSkylineFactory:64-283 |
| AccidentalGlyphSkyline.cs | silhouette 矩形近似 | X/Y | device 近似 | 無次元×BBox | :49-145 |

#### 忠実性所見
- **[low] Skyline.cs:130-175（flat Distance）** — `VerticalSkyline`/`HorizontalSkyline.Distance` と
  **逆契約**（生符号 gap・最小化・空で +∞ vs 内部 penetration・最大化・空で −∞）。各々一貫し
  文書化済（:120-129）だが**混用厳禁** → faithful-but-fragile。
- **[low] HorizontalSkyline / ItemSkylineFactory / Skyline** — 水平 skyline は Y horizon を
  **device Y-down** で持つ（vertical 経路の Y-up と非共有）。X 衝突には不変で正しいが**将来 Y-up
  前提で触ると壊れる** → 注意。
- **[low] SkylineBuilder.cs:582,604** — stem X-extent を `noteRight±1`（2ss 幅箱）で近似（LP は
  thin stem ~0.13ss）。軸/単位は正（X, ss）だが横領土を過剰確保 → 近似 hack。
- **[low] AccidentalGlyphSkyline** — LP の font 由来 `horizontal-skylines`（`accidental-placement.cc:254-301`）を
  2-3矩形で近似（厳密 port でない・acknowledged）。
- **[very-low/stale]** 構造(非座標) ref に軽微な行ズレ複数（`build_system_skyline` :1075→実:1080、
  `Building::above` :169-176→実:166-173 等）。関数は正しく指すが行番号ドリフト。
- 総評: skyline は **ss・native Y-up の忠実 port**、代償 hack・方向/単位の非線形誤りなし。水平経路のみ
  自己完結の device-Y-down 変種（X 衝突目的では正）。

### 3.D Vertical Layout

**⚠️最大の非-native フレーム**: 譜間/system の縦積みは **Y-DOWN・page-absolute・staff-space**
（LP は Y-up）。**単位は忠実(ss、half-space でも pixel でもない)**が**方向が LP と逆**。Stage-4 は
note/annotation 幾何を Y-up 化（`YUp` 命名）したが、この staff/system 積みフレームは Y-down のまま残り、
境界で `StaffFrame.ToDevice/ToUp`・単項 `-YUp` の**単一フォールド反射**で整合。反射は一貫変換で
その場 ×2/÷2 ではない。

| File | 量 | 軸 | 方向 | 単位 | 根拠 |
|---|---|---|---|---|---|
| MultiStaffLayouter | `currentY` 譜/群積み, `GetStaffHeight`(4.0), `interGroupGap`, skyline gap | Y | **down+** | ss | :656-681, :203-223, :1505-1527 |
| LayoutEngine | `SystemLayout.Y`(page top から下), header/margin/spacing | Y | **down+** | ss | :187,193,:64-65 |
| LayoutEngine | prelim extent の `-YUp`/`ToDevice` 反射（tuplet/volta/mark/chord/lyric/tie/slur） | Y | Yup→down 変換 | ss | :839-967 |
| LayoutEngine | `AugmentSkylinesForPaging` は `YUp` 直用（skyline は Y-up） | Y | up+ | ss | :1087-1187 |
| OutsideStaffStacker | `DirectionalOccupancy` frontier, 各 padding | Y | device down+(dir±1) | ss | :48-65,:800-838 |
| ScoreLayout | 大半 | — | — | ss | header「全て staff spaces」 |
| ScoreLayout | **`GetRestShift`「in staff positions」** | Y | — | **half-space** | :259-267 |
| GrandStaffLayout | Brace Top/Bottom, TotalHeight=Bottom−Top | Y | down+ | ss | :76-88 |
| MeasureLayouter | 全 item/column X・幅・spring | **X のみ** | line-rel | ss | 全体 |

#### 忠実性所見
- **[low・設計] MultiStaffLayouter 縦積み全体 / LayoutEngine:187,193** — LP `align-interface.cc:228-274`
  は Y-up 累積（`where += DOWN·dy`、負が下）vs Lily# は Y-down `currentY += …`。**単位・大きさは忠実、
  方向のみ設計反転**。純進み `staffHeight+(centerToCenter−staffHeight)=centerToCenter` は LP の `dy` と一致。
  数値影響なし。だが**これが全 annotation で `-YUp`/`ToDevice` フォールドを要する残存 Y-down フレーム**。
- **[med・stale] MultiStaffLayouter:121,154,306… / StaffAffinity.cs:44,63,86** — staff-affinity spec 選択を
  `align-interface.cc:240-252` に ref するが、実体は `page-layout-problem.cc:1267-1332 get_spacing_spec`。
  → **STALE/WRONG REF（関数取り違え）**。座標影響なし（spec 選択は無次元）だがポインタが誤誘導。
- **[med・単位混在flag] ScoreLayout:259-267 `GetRestShift`** — 「in staff positions（half-space）」と明記、
  他フィールドは全 ss。consumer は ×0.5 必須で、ss と誤ると shift が2倍。混在はこの1フィールドに限定
  （consumer=note-collision, 範囲外）→ **要検証フラグ**。
- **[low・stale] OutsideStaffStacker:50-52** — 0.46 を `define-grobs.scm` 由来と注記するが同ファイルに大域0.46
  なし（値は LP 既定として正、場所が stale）。他 padding（DynLineSpanner0.6/Hairpin0.6666/DynText-0.6）は
  define-grobs 実在確認 → faithful。
- MeasureLayouter（X のみ・ss）／HaraKiri（座標なし）／CrossStaffEngraver（index 再マップのみ）は faithful。
- 非線形の単位誤りなし。跨フレーム演算は線形の単一フォールド反射のみで一貫。
### 3.E Collision / Stem / Accidental / Articulation

| File | 量 | 軸 | 方向 | 単位 | 根拠 |
|---|---|---|---|---|---|
| NoteCollision.cs | ups/downs, 閾値 | Y | up(比較のみ) | **half-space(整数)** | :241-259,:484-531 |
| NoteCollision.cs | shift 乗数 0.65/0.52/0.5/0.4/0.17/0.1, ±inner | X | right+ | 無次元(head幅比)→×noteheadWidth で ss | :162-219,:428-429 |
| AccidentalPlacement.cs | XOffset, yCenter=`StaffPosition/2` | X左−/Y | up | ss(half→×0.5) | :30-33,:202-206 |
| ChordHeadPositioning.cs | `ell`, shift `(ell−thick·rev)·dir`; dy 閾値 | X stem-dir/Y | up | ss / half-space | :57-88 |
| StemCalculator.CalculateStemEndY | length, staffRadius2.0, shorten | Y | up | **half-space(LP frame)**→減算時 ÷2 で ss | :131-190 |
| StemCalculator.CalculateBeamedStemInfo | noteStart=`headPosition*0.5*dir`, idealY, shortestY | Y | up=+ 絶対 | **ss** | :224-265 |
| ArticulationEngraver.cs | `YUp`,`noteUp=pos*0.5`; quantize `targetUp*2→*0.5` | Y | up=+ | ss / half-space(round) | :37-48,:1157-1189 |
| ElementCoordinator.cs | rest shift(beamY/edge) | Y | up(positions) | **half-space**(`ToStaffPositions`×2) | :610-630 |
| ElementCoordinator.cs | tie/slur segStart X/Y, tab は page Y-up | X/Y | device Y-down / tab page Y-up | ss | :1030-1293 |
| DotConfiguration.cs | positions, `IsOnLine %2==0` | Y | up=+ | half-space(整数) | :57-133 |

#### 忠実性所見
- **[med・hack] NoteCollision.cs:332-348** — LP `note-collision.cc:339-348` は shift を**符号付き
  head-extent 比** `(extent_down[RIGHT]−extent_up[LEFT])/extent_down.length()` で倍率化（up-head は
  stem 左・down-head は stem 右にあり等幅で比≈2）。Lily# は**全 head 左端=0 アンカー**で比が 1.0 に潰れ
  （:332-336 に文書化）、失われた ×2 を**両声部を対称 ±inner シフト**で回収（分離=2·inner）。
  → **等幅では net-faithful、混在幅（全音符×4分等）で残差**（single-column の最大幅 noteheadWidth×対称が
  LP の per-head 比を再現できない）。**本監査で見つかった最も実体ある座標モデル逸脱**。
- **[low・omit] NoteCollision.cs:307** — LP `:305-308` の half-vs-eighth 整列ナッジ
  `(1−extent_up[RIGHT]/extent_down[RIGHT])·0.5` 未移植（merge/wipe は透明化のみ）。X 軸・軽微。
- **[low・stale] NoteCollision.cs:206-219** — meshing 0.1/0.17 の ref を `check_meshing_chords():180-230` と
  するが実乗算は `:332-337`。値は正・ポインタのみ stale。
- **[✅解消] StemCalculator.cs:205,262-265** — doc「staff positions/half-spaces を返す」に反し実際は
  **絶対 ss**（`stem.cc:1213-1265` と一致確認）→ doc の単位ラベル修正のみ（§3.A と同一指摘）。**対処済**。
- **[faithful] StemCalculator.CalculateStemEndY**（`stem.cc:480-596`）— LP は half-space 計算
  (":501 WARNING: IN HALF SPACES") で、Lily# も half-space frame を保持し単一の `shorten/2.0`(÷2) で橋渡し。
  方向・単位橋渡し正。`MinStemLength` clamp は Lily# 追加(ss・benign)。
- **[faithful] ChordHeadPositioning**（`stem.cc:606-765`）— 閾値/`ell`/reverse_overlap/shift 一致。
  **[low]** breve 以上の stemless で reverse_overlap=0 にする LP:735 のみ未対応（稀・無次元）。
- **[faithful] AccidentalPlacement, DotConfiguration, ArticulationEngraver/Spacing, ElementCoordinator** —
  device↔up は全て `StaffFrame.ToUp/ToDevice/PositionToDevice` 経由。rest shift は half-space 内部一貫、
  beam-tip consumer は `OuterEdgeStaffSpaceAtX`(ss)+`ToDevice` で境界越え。単位混在・非線形単位誤りなし。
### 3.F Spanners

全 spanner は概ね ss + Y-up（frame B, `StaffFrame.ToUp` 経由格納）。方向/単位は健全、以下は値/単位適用の誤り。

| File | 量 | 軸 | 方向 | 単位 | 根拠 |
|---|---|---|---|---|---|
| TupletBracketEngraver | padding1.1/staff0.25/edge0.7, maxDy=0.5·width, `pos·0.5` | Y | up（frame B） | ss | :85-577 |
| HairpinEngraver | Height0.6666, broken 2/3・1/3 | Y | ± / frac | ss | :77,:182-184 |
| DynamicEngraver / TextSpanner / Trill / Glissando / Arpeggio | padding/staff-padding/glyph, `pos·0.5±` | Y/X | up（frame B） | ss | 各所（define-grobs 検証） |
| PedalEngraver | `PedalBracketLayout.Y`（BracketY6.5） | Y | **device down+** | ss | :60,:200-206（dead code） |
| LedgerLineSpannerEngraver | `LedgerExtension0.25`, Y=`PositionToDevice` | X/Y | device | ss | :56,:63 |

#### 忠実性所見
- **[med] HairpinEngraver.cs:182-184** — LP `hairpin.cc:307-308`: decrescendo は broken で full→1/3 then 2/3→0。
  Lily# は full→2/3 then 1/3→0 と**内側 2 分数が入替**。単位・± 方向は正、分数選択のみ誤り
  → system 跨ぎ decrescendo の taper が逆。（crescendo :177-179 は faithful。）
- **[low・wrong-unit] LedgerLineSpannerEngraver.cs:63** — LP `ledger-line-spanner.cc:230`
  `widen(length_fraction * head_extent.length())` の 0.25 は**符頭幅の比率**（≈0.25×1.3≈0.33ss/側）。Lily# は
  `LedgerExtension=0.25` を**絶対 ss** として適用 → **無次元比率を ss として誤用**。加線が各側 ~0.08ss 短い。
  Y（`pos·0.5`）は faithful。
- **[low] PedalEngraver.cs:33,200-206** — `.Y` を **device down+** で格納（兄弟は `ToUp` で Y-up）。frame 非統一の
  潜在。ただし**未配線 dead code**（既定 pedal は text）で severity 低。grob literal は faithful。
- **[low] OttavaBracketEngraver.cs:89** — `DashPeriod=2.0` に LP literal 無し（近似）。他 ottava literal は
  行番号付きで faithful。
- **[low・info] VoltaBracketEngraver.cs:65** — `YOffset=−3.0` は hand-tuned と明記（stale でない）。EdgeHeight2.0 faithful。
- **[faithful]** Tuplet（`max_dy=slope·last_x`, `tuplet-bracket.cc:570`）, Dynamic, TextSpanner, Trill,
  Glissando（`√(dx²+dy²)` は 2 ss 軸で単位混在なし）, Arpeggio（`pos·0.5±protrusion` 符号正）。
### 3.G Renderers & Marks

**renderer/layout は native Y-up で計算し、device Y-down への反転は単一の `YFlipDrawingContext` が行う。**
（⚠️ 2026-07-21 更新: 本節はかつて「`YFlipDrawingContext` は**未配線**、反転は draw 直前に per-call」と
記していたが、**その記述自体が stale だった**。実コードは `SharedRenderer.cs:99` で page context を
`new YFlipDrawingContext(doc.BeginPage(...), page.Height)` に包んでおり、**配線済み・単一集約**。
§4.6 島1 が先に訂正していた内容を本節にも反映した。監査エージェントの当初報告の方が正しかった。）
layout engraver は `StaffFrame.ToUp/ToDevice` で Y-up("frame B")格納。**これらのファイルに
ss→pixel/spaceHeight 演算は出現しない**（SVG backend 側）。全 `÷2`/`×2` は staff-position↔ss の正当変換。

| File | 量 | 軸 | 方向 | 単位 | 根拠 |
|---|---|---|---|---|---|
| Noteheads.cs | `noteY=staffMiddleY+StaffPosition/2`, ledger, stem device 往復 | Y | up+ | staff-position→ss | :348,:705 |
| Marks.cs | text baseline `(pageHeight−sy)+YUp`, tempo, MMR, tremolo | Y | up+ | ss | :49,:589,:799 |
| Overlays.cs | dynamics/artic/hairpin/tuplet/trill/gliss/arp via `os.YUp` | Y | up+ | ss | :59-666 |
| Barlines/Connectors/Prefix/GraceNotes/Tab | rect top-edge, delimiter, clef/key/time, grace, tab string | Y | up+ | ss（一部 staff-position→ss） | 各所 |
| MusicMarkEngraver.cs | 内部積み math は device（`−cn.YUp`）、出力は `ToUp(…,2.0)` で Y-up | Y | 内部down+ / 出力up+ | ss | :253,:353 |
| FingeringEngraver.cs | `yUp=StaffPosition*0.5`, 端 clamp ±2.0 | Y | up+ | staff-position→ss | :166,:218 |
| Connectors.cs | brace glyph 選択（unitsPerEm 1000, pow0.8） | — | n-a | font-design-unit/無次元 | :448-459 |

#### 忠実性所見
- **[low・contract 曖昧] IDrawingContext.cs:37-39** — remark は「Origin is top-left, Y axis points
  downward」の**1フレームしか書いていない**が、この interface は**2つのフレームで使われる**:
  renderer → `YFlipDrawingContext` は **page Y-up**、`YFlipDrawingContext` → `SvgDrawingContext` は
  **device Y-down**。つまり doc は「後段」だけを述べており、前段の呼び出し側には当てはまらない。
  実害はない（各実装は一貫）が、**同じ型が2フレームを運ぶ**のは符号ミス誘発 smell → contract に
  「装飾前=Y-up / 装飾後=Y-down」を明記するのが望ましい。**⇒ 2026-07-26 に明記（§4.4）。**
  ⚠️ 経緯: 本監査は当初「doc は stale」と指摘 →「YFlip 未配線につき false-positive」と取消 →
  **その取消の前提（未配線）が誤り**（`SharedRenderer.cs:99` で配線済み、`YFlipDrawingContext.cs:49`
  自身が "This is WIRED" と明記）。doc を実コードで裏取りせずに書き換えた失敗例として残す。
- **[low] Marks.cs:650-664（DrawBigRest）** — LP `multi-measure-rest.cc:203-212` の end cap は **2.0ss** 高
  （`Interval(−ss,ss)`）だが Lily# は `2*0.8=1.6ss`（0.4ss 短）＋ ref コメント「full staff-space height」も不正確。
  軸/方向/単位は正、定数のみドリフト。
- **[low・規約smell] 混在 layout-Y 格納** — 大半は `YUp`(up+) 格納だが一部は **device-Y offset** を draw 時に
  inline 反転: `PedalBracketLayout.Y`(:518), `LyricHyphen dash.Y/ExtenderY`(:723/732), `TieVariantLayout.Y`
  /制御点(:693-696)。各々**正しく**処理（`−v.Y` は `DrawBow` の device→page-Y-up 入力）だが device-Y struct と
  Y-up struct の**併存は将来の符号ミス誘発** → faithful だが規約未統一。
- **[faithful]** MusicMarkEngraver（内部 device・境界で `ToUp`）、Fingering/Lyric（`−y` 格納で Y-up）、
  UnscaledXDrawingContext（**X のみ補正・Y は素通し**＝単一フリップを壊さない正しい設計）。
  非線形（brace `pow(ratio,0.8)`・grace-slur `atan`）は無次元/ss 入力で単位健全。
### 3.H Font Metrics & Horizontal Spacing

**単位リークなし。** 全量が ss（or 無次元 force）。フォントメトリクスは font-unit→ss を**単一点**
（抽出スクリプト、`unitsPerEm/4=250 font-unit/ss`）で変換。フォント/layout 幾何は Y-up。Y-down は
renderer-local staff frame（`EngravingDefaults.StaffMiddle`/Rest 位置）と `TabStaffGeometry` device Y のみ
（各々一貫・文書化）。

| File | 量 | 軸 | 方向 | 単位 | 根拠 |
|---|---|---|---|---|---|
| GlyphMetricsGenerated.cs | 全 BBox+Advance | X+Y | Y-up | **ss**（抽出時1回変換） | header:5 |
| GlyphMetrics.cs | anchor, gap/padding heuristics | X（Y=±0.168） | Y-up | ss（手調整） | :62-141 |
| EngravingDefaults.cs | line 太さ族（stem0.13/beam0.48/…）, attach, spacing 定数 | scalar/X | n-a | ss | :40-373 |
| EngravingDefaults.cs | `StaffMiddle 2.0`, Rest/RepeatDot 位置 | Y | **Y-down(device/local)** | staff-position/ss | :175-256 |
| SpacingRules / Spring / SpringSolver / NoteSpacing / StaffSpacing | duration space, spring ideal/min, extents, force | X/scalar | n-a | ss + 無次元 force | 各所 |
| BreakAlignSpacing.cs | space-alist 値 | X | n-a | ss | :151-384 |
| TabStaffGeometry.cs | StaffY/StringY(device), StringSpace(ss) | X+Y | **device Y-down** | ss | :157-175 |

#### 忠実性所見（座標=単位/方向は全 faithful。以下は**値/スタイルの staleness**）
- **[✅解消] BreakAlignSpacing.cs GetClefSpacing** — 監査時は `minimum-space 3.5/3.5/4.2/3.7`（旧 LP 値＋
  別スタイル）で行頭 clef→key/time/bar が数 ss 広かった。**対処済**（`9c76abf9`）: 現行は LP
  `define-grobs.scm:914-925` の extra-space をそのまま持つ — key-cancellation 0.82 / key-signature 0.82 /
  time-signature 1.52 / first-note `minimum-fixed-space` 5.0 / right-edge 0.5 / **staff-bar 0.7**。
  （2026-07-21 再確認。この staff-bar 0.7 は clef が bar line の**前**に立つときの間隙で、実測一致 → §3.I。）
- **[✅解消] GetKeyCancellationSpacing / GetTimeSignatureSpacing→StaffBar** — key-sig `0.3`(LP0.5)/
  time-sig `1.15`(LP1.25)/`2.0`(LP1.0) が stale だった。**対処済**（`9c76abf9` に含む）。
- **[low・line-stale] EngravingDefaults.cs:359-373** — tie/slur 太さ**値は faithful**（1.2/0.8, LP `:2039/2841`）だが
  引用行番号 `:3175/:3898` が旧レイアウト（行ドリフト）。
- **[low] TabStaffGeometry.cs:84** — TabBeamQuant が `0.6*tanh` のみで LP `:766` の `/(damping+concaveness)`
  除数を落とす。LP 既定(damping1/conc0=除数1)でのみ faithful。tab 限定・ss 一貫。
- **[faithful]** GlyphMetricsGenerated（font BBox 全 ss・Y-up・単一変換）、EngravingDefaults 太さ群
  （`define-grobs.scm` と値一致）、SpacingRules/Spring/SpringSolver/StaffSpacing/NoteSpacing（spring・duration
  space 式一致、無次元 force+ss）、EmmentalerGlyphs（code point のみ）。`MinItemGap0.4 vs LP horizontal-padding0.1`
  は**文書化された意図的補償**。

### 3.I X スペーシング格子（L5/LS5）— 2026-07-21 追加

§3.H は水平スペーシングの**単位**（全 ss・リークなし）を監査したが、**格子そのものの構造**は
未監査のまま「※agent H」で残っていた。本節がそれを埋める。**単位・方向は faithful だが、
LP の格子の半分が型として存在しない。**

| LP | Lily# | 状態 |
|---|---|---|
| `PaperColumn`（musical） | `ColumnLayout(Fraction Timing, double X, double Width)` | 対応あり |
| **`NonMusicalPaperColumn`**（breakable） | `BoundaryColumn`（**Phase 1 で導入**・`a87f2e6d`） | 🔄**型は入った/載せ替え途中** |
| **`BreakAlignment` group**（clef→staff-bar→key-cancel→key-sig→time-sig） | `BreakAlignSpacing`（順序・space-alist 完備）＋ `BoundaryColumn` が行内境界で参照 | 🔄**行頭専用ではなくなった** |

> **Phase 1 完了（`a87f2e6d`, `b8fecfca`）**: `BoundaryColumn` が LP の unbroken break-align 順で
> clef/staff-bar/key/time を列内 X に配置し、間隔は `BreakAlignSpacing` の space-alist を引く。
> `MmrRodMinimumDistance` はこの列の `RightSkylineFromBarLine()` を使う形に載せ替え済み。
> **出力は1バイトも変えていない**（snapshot 移動ゼロが合格条件）。
> 副産物として `GetStaffBarSpacing` の値誤り（staff-bar→time-signature が既定 1.0 に落ちていた。
> LP は `0.75`）を修正。`CalculatePrefixWidth` が staff-bar を左シンボルとして歩かないため
> 本番未到達で露見していなかった。`b8fecfca` で自己レビューし、`space-alist` の `SpacingStyle`
> 無視（`minimum-space` を黙って誤配置する）と空 extent grob の非スキップを字面移植に直した。
>
> **Phase 2 完了（`a47029bc`, `94656b84`）**: clef の描画を bar line の**前**へ。幅は
> `BoundaryClefAllowance` で**前の小節の終端 spring の min** に加算し（ideal は LP が
> `note-spacing.cc:99-100` で bar line 基準に落としているため不変）、`BarlineToFirstColumnSpring`
> 側の二重予約を撤回。描画も同じ `BoundaryColumn` から位置を読むので定数の二重持ちが無い。
> 両 spring 系統＋`SystemBreaker` にミラー。**動いた既存 snapshot は `test/clef-change` の1件のみ**
> （LP 照合済み・承認済み）。`test/mmr-clef-change-bound` を新規追加。
>
> **残**: 「4か所の再発明」のうち `GetBarlineToItemSpace` / `GetItemToBarlineSpace` /
> `ChangeItemPrefixWidth` の3つ。および item→bar line の**最小値そのもの**が Lily# 独自形式で、
> LP の `padding + skyline 距離` になっていない（clef 分は既存最小値への**加算**で入れてあるので、
> clef の無い境界は不変）。置き換えると全小節が動くため別段階。

#### 忠実性所見
- **[high・frame 欠落] 小節境界に列が無い** — `ColumnLayout` は `Fraction Timing` で鍵付けされ、
  かつ `MeasureLayout` の**内側**（`ScoreLayout.cs:38-42`「X offset from measure start」）にある。
  LP の `NonMusicalPaperColumn` は「同じ moment に musical 列とは**別に**立つ、小節をまたぐ列」
  なので、**Lily# の型では構造的に表現できない**。`NonMusicalPaperColumn` はコード中コメントにしか
  出現しない。
- **帰結: 同じ「境界の列」が4か所で部分的に再発明されている** —
  `SpacingRules.BuildBoundColumnRightSkyline`（MMR 専用に列の右スカイラインをその場で組む）／
  `GetBarlineToItemSpace`・`GetItemToBarlineSpace`（列を空間定数に畳んだ形）／
  `ChangeItemPrefixWidth`（列内 change grob 幅）。境界は「左小節の `EndBarline`」＋
  「右小節の先頭 change items」に**分割して**保持される。
- **帰結: 境界に関わる LP 移植は毎回フレーム変換を要する** — 実例（2026-07-21 実測）:
  LP `note-spacing.cc:77-108` は ideal も min も**列→列**で計算し、最後に
  `ideal -= staff_bar_group->extent(right_col, X)[LEFT]` で ideal だけ bar line 基準へ落とす。
  Lily# の終端 spring は**最初からその減算後のフレーム**（item→bar line）にあるため、
  この行は no-op になる一方、**列→列である min には clef が入る**ので、そこだけ変換が要る。
  `break-align-orders` の unbroken 順（`define-grobs.scm:650-664`）で clef だけが staff-bar の
  **前**に立つことが原因。実測: `R1*5` の bar line 間 span は clef 有無で **14.133856 と不変**、
  動くのは列原点のみ（clef 幅＋`Clef.space-alist staff-bar 0.7` = 2.84668）。
- **§4.2(a) と同じクラスの未完** — (a) が「方向は逆だが型はある」なのに対し、こちらは
  **型そのものが無い**。単位・方向由来の数値バグは出ていない（現行出力は LP 一致）が、
  「字面通り移植」を境界で行う限り毎回変換が挟まる。

#### Phase 0: 境界幾何の導出済みモデル（2026-07-21・LP 2.24.4 で検証）

列を型として入れる前提となる LP 側のモデル。**推定を含まない**（各項に出典行、末尾に実測照合）。

**① 列の中身と順序** — `BreakAlignment` group。unbroken 順は
`… breathing-sign, clef, cue-clef, staff-bar, key-cancellation, key-signature, time-signature …`
（`scm/define-grobs.scm:650-664`）。**clef だけが staff-bar の前**、key/time は後。

**② 列内の配置** — 隣接する break-align group 間は、左 grob の `space-alist` の右シンボル向けエントリ。
`Clef.space-alist (staff-bar . (extra-space . 0.7))`（`define-grobs.scm:916`）。

**③ 列のスカイライン** — `Separation_item::boxes`（`separation-item.cc:120-190`）: grob ごとに
`Box(X-extent + extra-spacing-width, pure_y_extent + extra-spacing-height)`。
**既定 esw = `Interval(-0.1, 0.1)`、既定 esh = `(0, 0)`**（`:166-169`）。`Axis_group_interface` の
group は skip し、内包 grob を個別に box 化（`:160-161`）。

**④ 列間の rod（最小距離）** — spring とは**別系統**。`Spacing_spanner::set_column_rods`
（`spacing-spanner.cc:228-290`）→ `Separation_item::set_distance`（`separation-item.cc:48-68`）:
```
dist = padding + lines[LEFT][RIGHT].distance (right)
```
`padding` は `spacing-spanner.cc:315` の `get_property (prev, "padding")` で、**`PaperColumn` にも
`NonMusicalPaperColumn` にも定義が無いため常に既定 0.1**。

**⑤ spring の ideal** — `Note_spacing::get_spacing`（`note-spacing.cc:77-108`）。duration ベース＋
`left_head_end` 補正のあと、右が非音楽列かつ `space-to-barline` なら
`ideal -= staff_bar_group->extent (right_col, X)[LEFT]` ＝ **ideal だけ bar line 基準へ落とす**。

**⑥ 境界での spring 合流** — `Spacing_spanner::breakable_column_spacing`（`:478-536`）が
`Staff_spacing::get_spacing` の wish を集めて `merge_springs`。`full-measure-extra-space` は
**ここで、後続小節を鍵に**入る（`:484-488`、`r` が musical かつ `l->break_status_dir()==CENTER` のとき）。

**⑦ spring の min と `merge_springs` の 0.3 ヘッドルーム**（2026-07-21 追加・§1.2 の +0.2 の正体）
— `Note_spacing::get_spacing`（`note-spacing.cc:78-83`）が spring の min を
`max (0, skys[LEFT].distance (skys[RIGHT]))` で置く。**この min に padding は入らない**（④の rod とは別物）。
そのうえで `Spacing_spanner::note_spacing` は wish が1つでも必ず `merge_springs` を通す
（`spacing-spanner.cc:380-393`）。そこに**ハードコードされた 0.3**がある:

```c
// lily/spring.cc:104-129 merge_springs
//   "leave a little headroom above the largest minimum distance
//    so that things don't get too cramped"
avg_distance = std::max (min_distance + 0.3, avg_distance);   // :122
```

→ **列間距離 = `max (ideal, skyline距離 + 0.3)`**。④の rod は `skyline距離 + padding(0.1)` なので
**常に spring 側の床（+0.3）の方が大きい**。force ≥ 0（ragged-right = Lily# の既定）では
必ず spring の床が効き、rod は行を圧縮したときにだけ顔を出す。

**検証**（`c'2 c'2 \clef bass c'1`、2つ目の符頭の列 → 境界列原点=clef 左端。
符頭 ink 1.377346 ＋ esw 0.1、clef の esw 左 0.1 ⇒ skyline 距離 1.577346）:

| 条件 | 予測 | 実測 |
|---|---|---|
| `ragged-right = ##t`（force 0） | ⑦ `1.577346 + 0.3` = **1.877346** | **1.877346** ✓ |
| `ragged-right = ##f` ＋ `line-width = 40\mm`（圧縮） | ④ `1.577346 + 0.1` = **1.677346** | **1.677346** ✓ |

⚠️ **本 doc の旧記述は下段（圧縮）だけを測って④を「検証済」としていた**。式④自体は正しいが、
**既定の ragged-right で実際に効くのは⑦**。片方の regime だけで検証すると、もう片方で 0.2 ずれる。

⑦の独立検証（`c'4 d' e' f' \clef bass g4 a b c'`、符頭 ink 右端 → clef ink 左端の gap）:

| 摂動 | 予測 gap | 実測 gap |
|---|---|---|
| 素（既定 esw） | 0.1 + 0.1 + 0.3 = **0.500** | **0.500000** |
| `Clef.extra-spacing-width = (0 . 0)` | 0.1 + 0 + 0.3 = **0.400** | **0.400000** |
| `Clef.extra-spacing-width = (-0.5 . 0.1)` | 0.1 + 0.5 + 0.3 = **0.900** | **0.900000** |
| `Clef.extra-spacing-width = (-1.0 . 0.1)` | 0.1 + 1.0 + 0.3 = **1.400** | **1.400000** |
| `Stem.extra-spacing-width = (-0.1 . 0.5)` | 0.5 + 0.1 + 0.3 = **0.900** | **0.900000** |
| `padding = 0.0` | 変化なし（padding は⑦に入らない） | **0.500000** |
| `SpacingSpanner.shortest-duration-space = 4.0` | ideal 2.555577 が床を上回る → gap **1.251365** | **1.251365** |

**gap が音価（4分/8分/2分）にも clef 幅にも依らず 0.500 で一定**なのが「床が効いている」証拠。
`shortest-duration-space` を上げて ideal が床を超えた最後の行だけ gap が動く。

**潰した候補**（再調査不要）: `Stem` は `extra-spacing-width` を**持たない**（`define-grobs.scm` で esw を
宣言する 28 grob に Stem は無い）。上向き符尾の box 右端は符頭 ink 右端と**一致**する（実測
stem `rel+0.065` = head `rel+1.304212`）ので寄与ゼロ。境界列は loose column でもない
（`between-cols` / `maybe-loose` とも未設定を実測）。rod も実測でダンプ済で
`skyline + padding` ちょうど（`minimum-distances` を直接ダンプ）。

**⚠️ 測り方の落とし穴**（本 Phase で一度誤診）: 「行幅を詰めれば min が出る」は**誤り**。
`ragged-right = ##f` は均等割りなので spring は ideal 側に居ることが多く、実測 1.0956 を rod と
誤読しかけた（真の rod 下限は 0.300 = esw 0.1 + esw 0.1 + padding 0.1）。逆に改行禁止＋極小行幅は
**grob が重なる不正レイアウト**になり値が無意味になる。rod を測るなら「予測値と一致するか」を
④の式から立てて照合するのが確実。

---

## 4. 総括

### 4.1 単位（unit）
**LP は全内部 ss、Lily# もほぼ ss で faithful。単位リークなし**（フォントは font-unit→ss を単一点で変換、
layout に pixel は出ない）。残る **half-space(staff-position)** は概ね LP と同じ正当な使い分け:
「音高は整数 position、幾何へ `×0.5`」（NoteCollision/DotConfiguration/Fingering/notehead Y）＝L2 相当で問題なし。
境界変換の half-space も文書化済（TieDetails の `×0.5`/`×2`、beam quanter 戻り値）。**単位由来の実バグは
2件のみ**: ①LedgerLine が比率 0.25 を絶対 ss として適用（wrong-unit）、②ScoreLayout.`GetRestShift` が ss 群中で
唯一 half-space（要 consumer 検証）。

### 4.2 方向（direction）
**LP は Y-up 一貫＋device で単一フリップ。Lily# は Stage-4 で renderer / skyline(vertical) / 大半の engraver を
native Y-up 化。** device 反転は **`YFlipDrawingContext` に単一集約済み**（`SharedRenderer.cs:99` で page
context を包む＝LP の L1→L4 単一フリップと同形）。〔2026-07-21 訂正: 本節はかつて「未配線・per-call」と
記していたが stale だった。§3.G 参照。〕残存 Y-down フレームが2系統:
- **(a) 譜間/system 縦積み**（`MultiStaffLayouter`/`LayoutEngine`）が **Y-down page-absolute**。単位 ss で
  大きさは LP `align-interface` と一致するが**方向のみ設計反転**。全 annotation touchpoint で `-YUp`/`ToDevice`
  の単一フォールド反射を要する＝**最大の非-native フレーム**（一貫変換ゆえ数値は正、frame 忠実性としては未完）。
- **(b) 個別 device 島**（TieVariant, Pedal[dead], MusicMarkEngraver 内部, 水平 skyline の Y horizon,
  TabStaffGeometry, beam collision island, tab beam quanter）— device Y-down で動き境界で反射。faithful だが
  frame 非統一（将来の符号ミス誘発 smell）。

なお **X 側にも1系統の未完がある**（方向でなく**型の欠落**）: LP の non-musical PaperColumn に
対応する型が無く、小節境界の列が2小節に分割保持されている → §3.I / §4.3 #9。

### 4.3 要修正（座標モデル/値の実バグ、severity 順）
| # | Sev | 箇所 | 種別 | 内容 |
|---|---|---|---|---|
| 1 | **high** | SlurScoringProblem.cs:106,456-485 | frame-mismatch | staff-line 回避が絶対-page vs staff-相対の原点不一致で**常時 no-op**（page 頂上譜以外で slur が staff line を避けない） |
| 2 | **high** | BreakAlignSpacing.cs:151-172 | value-stale | 行頭 Clef space-alist が旧 LP 値＋別スタイル（現行 LP より数 ss 広い） |
| 3 | med | NoteCollision.cs:332-348 | frame/hack | 左端アンカーで LP の符号付き extent 比≈2 を 1.0 に潰し対称シフトで代償＝**混在幅で残差** |
| 4 | med | TieFormattingProblem.cs:256,458-473 | wrong-const | center 係数 0.5/1.0 vs LP 0.375/0.75（0.75·h の取り違え） |
| 5 | med | HairpinEngraver.cs:182-184 | wrong-value | decrescendo broken 分数入替（full→2/3→0 を full→1/3→0 に） |
| 6 | med | BreakAlignSpacing.cs:182-236 | value-stale | KeyCancellation/TimeSig→StaffBar が旧値（0.3/1.15/2.0 vs 0.5/1.25/1.0） |
| 7 | low | LedgerLineSpannerEngraver.cs:63 | wrong-unit | 0.25 を符頭幅比率でなく絶対 ss で適用（加線が各側 ~0.08ss 短い） |
| 8 | low | ScoreLayout.cs:259-267 | unit-mix-flag | `GetRestShift` が唯一 half-space（consumer 検証要） |
| 9 | **high** | `ColumnLayout`/`MeasureLayout`（§3.I） | **frame 欠落** | LP の `NonMusicalPaperColumn`（小節境界に立つ breakable 列）に対応する型が無い。境界が「左小節 EndBarline＋右小節先頭 items」に分割され、同じ列が4か所で部分再発明。**現行出力は正**だが境界の LP 移植は毎回フレーム変換を要する |
| 10 | **high** | `SpacingRules` の extent ヘルパ3種（§4.7） | **frame-mixed** | 列原点の基準が「左端」と「中心」で混在。**同じ box の左右が別 frame**。0.8 という非 LP 定数が差を埋めていたため値の辻褄は合っており露見しなかった（2026-07-21 発見） |
| — | (既知) | BeamScoringProblem collision island / tab beam quanter | frame island | §3.A 記載・別 follow-up |

### 4.4 doc/label のみ修正（コードは正）
- **IDrawingContext.cs:37-39** — ✅**対処済**。remark に**2フレームを明記**した（装飾前＝renderer が
  書く page Y-up／装飾後＝backend が受け取る device Y-down）。あわせて**誰がどちらに居るか**を
  規約として書いた: **実装者＝backend＝flip の後ろ／呼び出し側＝renderer＝flip の前**、
  両方を兼ねる decorator（`TextFontDrawingContext`）は**渡されたフレームのまま・変換しない**。
  X は両フレームで同一。
- **StemCalculator.cs:205,262-265** / **StemInfo** — 戻り値「staff positions/half-spaces」ラベルが実際は ss。
  → **修正済**（ss と明記）。
- **COORDINATE_SYSTEM.md** 陳腐化 → **修正済**（本書へのポインタ＋layer 表更新）。
- 多数の LILYPOND-REF 行番号ドリフト（cosmetic、関数は正しく指す）— 一括再採番は別途。

### 4.5 対処状況（2026-07-19 起票／2026-07-21 更新）
| # | 項目 | 状況 |
|---|---|---|
| 1 | Slur staff-line frame 不一致 | ✅**修正**（staffMiddleY 導入で絶対-page frame へ・commit `1c902285`） |
| 2 | BreakAlignSpacing clef/key/time 値 | ✅**LP 値採用**（extra-space 0.82/1.52/0.7 等・目視で LP 一致確認・171 snapshot 再ベース・`9c76abf9`） |
| 3 | NoteCollision 混在幅残差 | ✅**LP式化（②局所修正）**: extent 比 `(extent_down[R]−extent_up[L])/downW`＋down-note 幅 scaling を literal 移植（`note-collision.cc:339-348`）。等幅は identity、up>down 幅の稀ケースのみ LP 通り tighten。全 177 snapshot 不変。実測で混在幅は既に LP 近似と判明したため、フル stem-anchor 移行(Stage-1)でなく局所修正を採用（保守性優先）。 |
| 4 | Tie center 係数 | ✅**修正**（0.375·h / 0.75·h・`d81d379d`） |
| 5 | Hairpin decrescendo 分数 | ✅**修正**（`1342012b`） |
| 6 | BreakAlign KeyCancel/TimeSig 値 | ✅**LP 値採用**（`9c76abf9` に含む） |
| 7 | LedgerLine 単位 | ✅**修正**（比率×head幅・`f5a4f89d`） |
| 8 | GetRestShift half-space | ✅**問題なし**（consumer 無し＝vestigial・単位混在の実害なし） |
| 9 | **non-musical PaperColumn の欠落**（§3.I） | 🔄**進行中**。①LP 境界幾何の導出＝**完了**（`820504e2`、rod の式を実測と6桁一致で検証）／②列の導入＝**完了**（`a87f2e6d`+`b8fecfca`、出力中立・`GetStaffBarSpacing` の値誤りも修正）／③clef を bar line の前へ＝**完了**（`a47029bc`+`94656b84`、既存 snapshot 1件のみ変更・LP 照合済）／④残る3ヘルパの吸収と item→bar line 最小値の LP 式化＝**未着手**（全小節が動くため要承認） |
| 10 | **X 基準点未統一**（§4.7） | 🔄**進行中**。①音符/休符を左端基準へ＋`GetItemToBarlineSpace`=0.2＝**完了**（`8448749a`）／③`CalculateLeftExtent` の stale doc＝**完了**（`86dbb093`）／④`BarlineToFirstColumnSpring` の `Staff_spacing::get_spacing` 字面移植（min/stretch/0.3補正/compress/optical）＝**完了**（`9b31c2ba`+`9ffeef8f`、snapshot 43+59 再ベース・LP 実測照合済）／①変更 item の frame＋定数と②`CalculateRightExtent` の統廃合＝**未着手** |
| doc | StemCalculator / COORDINATE_SYSTEM | ✅ doc 修正済 |
| doc | IDrawingContext.cs:37-39 | ✅**対処済**（2026-07-26）。装飾前=Y-up/装飾後=Y-down の2フレームと「実装者=backend=flip の後ろ／呼び出し側=renderer=flip の前／decorator は変換しない」を remark に明記。当初「false-positive」としたのは**誤り**だった |
| 島1 | **譜間/system 縦積み Y-up 化＋YFlip 配線（=Stage-4 全体）** | ✅**完了**。YFlip 配線＋全 grob レイアウト Y-up 化（Phase 2i〜2z、`e09d4e72`ほか）→ `system.Y` の page Y-up 格納（`477c5452`）→ 共有 device stacking の de-island（DynamicEngraver `ece55e9a`・SkylineBuilder `db7b0c5b`・OutsideStaffStacker `39da7084`・Y-up skyline 2 パス `7f2f8ff8`）→ **`staff.Y` の Y-up 格納（`ff64f38e`）で締めた**。全段階が boundary-shim で byte 不変。詳細は §2.1 |
| 島2 | device 島群（TieVariant/Pedal[dead]/水平 skyline/TabStaffGeometry/beam collision island）/ LILYPOND-REF 行再採番 | ⏸**繰延**（frame 忠実性の残・数値は正）。島1 が完了したので次はここ |

### 4.6 結論（2026-07-21 更新）
**方向・単位の忠実移植は大部分達成**（Stage-4 Y-up 集約＋ss 統一＋`YFlipDrawingContext` による単一フリップ）。
起票時の実バグ8件は**すべて対処済**（§4.5）。残るのは「数値は正だが frame 忠実性が未完」の3系統:

- ~~**①譜間/system 縦積みの Y-down 設計残存**~~ — ✅**解消**（`ff64f38e`。§4.2(a)・島1）。
  `StaffLayout.Y` / `StaffGroupLayout.Y` / `BraceTop`・`BraceBottom` が Y-up 格納になり、
  `FindStaffYInSystem` が `system.Y + staff.Y`（LP の素の和）へ
- **②device 島群** — 各々一貫・境界で反射（§4.2(b)・繰延）
- **③non-musical PaperColumn の欠落** — **型そのものが無い**（§3.I・新規）

①②は Y（縦）側、③は X（横）側。**③は本監査で唯一「LP にある型が Lily# に無い」ケース**で、
他2件（方向差・島化）より深い。境界に関わる LP 移植を「字面通り」で行いたい限り、先に型を入れる
必要がある。

beam quanter で確立した「**方向 AND 単位を LP と揃えてから字面移植**」の原則は、③では
「**まず必要な座標系（列）を導入し、その上にロジックを移植**」という形を取る。

### 4.7 ④X 軸の「基準点」未統一（2026-07-21 新規・§4.3 #10）

③が「型の欠落」なのに対し、こちらは**同じ軸内で列原点の基準が2種類混在**という別種。
**LP は列原点＝grob の左端**（`Separation_item::boxes` の box は `il->extent(pc, X_AXIS)`＝
paper column フレームでの extent。2.24.4 で PaperColumn と NoteHead の
`ly:grob-relative-coordinate` が同値であることを実測確認）。

実 frame の棚卸し（**実測ベース**。doc コメントは信用しない — 下表のとおり stale がある）:

| ヘルパ | 音符/休符 | 変更 item | production 呼び出し元 |
|---|---|---|---|
| `CalculateLeftExtent`（public） | **左端基準**（変換済・符頭で 0） | 中心基準 `width/2` | Grace / `ChangeItemPrefixWidth` / `CalculateSkylineDistance` |
| `CalculateRightExtent`（public） | **中心基準** `W−CenterX` | 中心基準 `width/2` | **無し**（`SvgTests.cs` のみ） |
| `CalculateNoteheadRightExtent`（private） | **左端基準**（`8448749a` で変換） | 中心基準 `width/2` | `CalculateSkylineDistance` |

**発見の経緯**: 音符の左が左端基準・右が中心基準という**同じ box の左右不一致**があり、
`GetItemToBarlineSpace = 0.8`（非 LP 値）がその差を埋めていたため値としては辻褄が合っていた。
`8448749a` で音符/休符側を左端基準へそろえ、0.8 を LP の `esw+esw = 0.2` へ置換。

**残り（本項目の作業内容）**:
1. 🔲**未着手**. **変更 item（clef/key/time）が3ヘルパとも中心基準** — 内部的には一貫しているが LP と異なる。
   そのため `GetItemToBarlineSpace` の変更 item エントリ 1.0 も、`GetBarlineToItemMinimum` の
   1.0/1.0/0.75 も据え置いてある。**frame と定数は同時に直す**。
   ⚠️ **ただしこれだけでは行中 clef/key の残差は 0 にならない**（**§4.7.2** で実測確定）。
   行中変更の幾何は extent ヘルパではなく `ChangeItemPrefixWidth` ＋ 描画側のぶら下げ式が
   決めており、LP は**専用の non-musical 列＋左右で別の式**で価格付けしている。
   frame 修正で残差は +1.119 → +0.612 に減るが**逆符号は残る**。実体は③と同型の**列の欠落**。
2. 🔲**未着手**. **`CalculateRightExtent` が中心基準のまま production 未使用** — `SvgTests.cs:166-167` が
   左（左端基準）と右（中心基準）を**ペアで**使っており、テスト自体が2 frame で box を測っている。
   §3.11 の手順（横断 grep →`<see cref>` →承認）を経てから統廃合すること。
3. ✅**完了**（`86dbb093`）. `CalculateLeftExtent` の XML doc が stale だった。
4. ✅**完了**（`9b31c2ba` ＋ `9ffeef8f`）. `BarlineToFirstColumnSpring` を
   `Staff_spacing::get_spacing` の字面移植へ。**§4.7.1 参照**。

#### 4.7.1 `Staff_spacing::get_spacing` の導出済みモデル（`lily/staff-spacing.cc:118-221`）

```c
last_grob = Spacing_interface::extremal_break_aligned_grob (me, LEFT, break_dir, &last_ext);
//   ext = break_item->extent (col, X_AXIS)   ← 列原点フレーム（spacing-interface.cc:217）
//   d==LEFT の選択条件は「右端が最大」＝最右の break-align grob ＝ staff-bar
space_def = (break_status_dir()==CENTER) ? alist['next-note] : alist['first-note]
Real fixed = last_ext[RIGHT];                      // :166  ★列原点から見た bar line の右端
// semi-fixed-space:
fixed += distance / 2;  ideal = fixed + distance / 2;   // :176-179
Real stretchability = is_stretchable ? ideal - fixed : 0;   // :200
ideal += situational_space;                                  // :204  full-measure-extra-space
Real optical = next_notes_correction (me, last_grob);        // :206
fixed += optical;  ideal += optical;
Real min_dist = Paper_column::minimum_distance (left_col, right_col);   // :210
// ★ merge_springs とは別の、2つ目のハードコード 0.3
Real min_dist_correction = std::max (0.0, 0.3 + min_dist - fixed);      // :213
fixed += min_dist_correction;  ideal = std::max (ideal, fixed);         // :214-215
Spring ret (ideal, min_dist);
ret.set_inverse_stretch_strength (max (0.0, stretchability));           // :218
```

**実測照合**（`BarLine.space-alist (next-note . (semi-fixed-space . 0.9))`、ragged-right）:

| | last_ext[RIGHT] | 予測 ideal | 実測（列原点→次の音符列） |
|---|---|---|---|
| clef 無し | 0.19（bar line のみ） | `0.19+0.45+0.45` = **1.09** | **1.090000** ✓ |
| clef 有り | 3.03668（clef+0.7+bar line） | `3.03668+0.9` = 3.93668 ＋optical 0.189365 | **4.126045** ✓ |

→ **clef 有無で `last_ext[RIGHT]` が変わるのは、それが列原点基準だから。**

> ⚠️ **訂正（2026-07-21・実測）**: この表の残差 0.189365 を「clef 有りのときの optical」と読むのは**誤り**。
> `next_notes_correction` は **clef ではなく下向き符尾**に反応する。bar line ink 右端 → 次の符頭 ink 左端で
> 2×2 を測ると:
>
> | | 上向き符尾 | 下向き符尾 |
> |---|---|---|
> | clef 無し | **0.900000** | **1.042857** |
> | clef 有り | **0.900000** | **1.089365** |
>
> **clef 有り＋上向きは補正ゼロ**（0.900000）で、**clef 無し＋下向きが第3の値**（1.042857）を出す。
> clef を変数にしたモデルではこの3値を説明できない。移植は `9ffeef8f`、
> `SpacingRules.BarlineToNextNotesCorrection` に 2×2 ごと記録してある。

**Lily# との差分**（`BarlineToFirstColumnSpring`）:

| | LP | Lily# 現状 |
|---|---|---|
| ideal | `last_ext[RIGHT] + 0.9`（＋optical＋situational） | 1.09 相当で**既に一致**（実測 plain.lys 21.02−19.93=1.09） |
| **min** | `Paper_column::minimum_distance` = skyline = **0.39** | **0.9**（`next-note` 値をそのまま最小値に）＝**2倍過大** |
| **stretch** | `ideal − fixed` = **0.45** | **0**（`inverseStretchStrength: 0` の剛体） |
| 0.3 補正 | `max(0, 0.3 + min_dist − fixed)` | 無し |

**ideal は既に LP 一致なので位置は合っている**が、min と stretch が違うため
`merge_springs` の floor を掛けると全小節頭が +0.3 太る（`8448749a` で floor 適用を見送った理由）。
min を 0.39 側へ直せば floor は no-op（0.39+0.3=0.69 < 1.09）になり、全 spring に floor を
掛けられるようになる。**min・stretch・0.3 補正・floor は同時に入れる**こと。

> **実装後の追記**: 実際には `ApplyMergeSpringsHeadroom` の呼び出しは**足していない**。
> :213 の補正が `ideal ≥ fixed ≥ 0.3 + min_dist` を保証するので、floor は**証明可能に no-op**。
> 「両方効く」（§2.2 相当の記述）は機構としては正しいが、この spring では結果が同じになる。

**⚠️ frame 変換（実装時に必須）**: LP の spring は**列原点→列原点**、Lily# の spring は
**bar line の ink 右端→次の item 列**。差は `last_ext[RIGHT]`＝clef 無しなら bar line 幅 0.19
（Lily# はこの 0.19 を描画側で別に持つ）。**Lily# フレームに直した目標値**:

| 量 | LP（列原点） | **Lily#（ink 右端）** | Lily# 現状 |
|---|---|---|---|
| `fixed` | 0.19+0.45 = 0.64 | **0.45**（= distance/2） | ✅ |
| `ideal` | 1.09 | **0.9** | ✅ |
| `min_dist` | 0.29+0.1 = 0.39 | **0.1 + `CalculateLeftExtent` + 左端 grob の esw** | ✅（旧: 0.9） |
| `stretch` | ideal−fixed = 0.45（:200、**0.3 補正の前**に確定） | **0.45** | ✅（旧: 0＝剛体） |
| 0.3 補正 | `fixed = max(fixed, 0.3+min_dist)` → 0.50 | 同 | ✅（旧: 無し） |
| optical | `next_notes_correction`（:206-208） | 同（下向き符尾のみ） | ✅ `9ffeef8f` |
| compress | `ideal − fixed`（:219） | 同 | ✅（旧: `ideal − min`） |

**`min_dist` の esw は grob 依存**: 既定は 0.1 だが **Accidental は `(-0.2 . 0.0)`**
（`define-grobs.scm:40`）。列の最左 ink が臨時記号なら左側は 0.2。実測で確定（`c'4 d' e' f' | cis'4 …`）:
dump した rod 2.04 ＝ min_dist 1.94 ＋ padding 0.1、`1.94 = 0.19 + 0.1 + 1.45 + 0.2`、
配置は `min_dist + 0.3 = 2.240` ＝ 実測 `23.185729 − 20.945729`。
なお `AccidentalPlacement.left-padding` は**摂動しても動かない**ので犯人ではない（0/0.5 で不変を確認）。

**`stretch` は実測で裏取り済**（源典読みだけではない）。`c'4 d' e' f' | g'4 a' b' c''` を
justify すると当該 gap は 0.900000 → 1.996558（120mm）→ 3.091335（180mm）。
strength 0.45 として force を解くと 2.43680 / 4.86963、その force を**別の musical spring**
（natural 3.002257 → 7.140047 / 11.271114）に入れると両方 1.69805 で一致する。

min が `0.2 + 左到達` になるのは②で `GetItemToBarlineSpace` を `esw+esw = 0.2` にしたのと同じ根拠
（`Paper_column::minimum_distance` は純 skyline）。**`GetBarlineToItemSpace` の 0.9 は ideal 用の
space-alist 値であって min ではない** — `CalculateSkylineDistance(null, item)` がそれを
min として使っているのが不具合の実体。

**最大のリスクは `stretch` 0 → 0.45**。現状 `inverseStretchStrength: 0` の剛体なので、
LP 化すると**均等割り時の力配分が全体で変わる**（snapshot 再ベース必須・要 LP 照合）。

**なぜ③より先か**: `BoundaryColumn`（③）を入れても、その中身を測る extent ヘルパが別 frame だと
境界ロジックを字面移植するたびに変換が要る。`8448749a` で実際にそれが起き、2か所で移植を
見送らざるを得なかった。**④は③の下層**。

#### 4.7.2 行中（mid-measure）変更 item の導出済みモデル（2026-07-21・LP 2.24.4 で実測確定）

**LP は行中の clef/key 変更に「専用の non-musical 列」を1本立てる。** 音符列と音符列の間に
列が1本挟まり、gap は**2本の spring** で決まる。Lily# には列が無く、**1本の spring に
`ChangeItemPrefixWidth`（＝ W ＋ 2×0.5）を丸ごと足す**だけなので、構造が違う。

列原点は**変更グリフの ink 左端**（実測: MC の clef anchor 13.955485 を原点とすると
右 gap が `ink幅 2.146680 ＋ 1.0` で 6 桁一致する）。

**左 gap（前の音符列原点 → 変更列原点）** — `Note_spacing::get_spacing`:

```c
Real ideal = base.ideal_distance () - increment + left_head_end;      // :77
Real min_dist = skys[LEFT].distance (skys[RIGHT], ...);               // :79-82
if (!Paper_column::is_musical (right_col) && ... && !staff_bar_group) // :87-102
  {
    Real min_desired_space = (ideal + min_dist) / 2.0;                // :105 ★
    ideal -= right_col->extent (right_col, X_AXIS)[RIGHT];            // :106
    ideal = std::max (ideal, min_desired_space);                      // :107
  }
```

行中変更列には bar line が無いので `staff_bar_group` は null、**:105-107 の枝**に入る。
変更列の幅を丸ごと引くので `ideal` は負にもなり、**実際に効くのは常に `(ideal + min_dist)/2` の床**。

**右 gap（変更列原点 → 次の音符列原点）** — `Staff_spacing::get_spacing`:

```c
SCM space_def = scm_sloppy_assq ("first-note", alist);                // :147
if (break_status_dir () == CENTER)                                    // :148  行中は常に CENTER
  { nndef = scm_sloppy_assq ("next-note", alist);
    if (pair) space_def = nndef; }                                    // :150-152
Real fixed = last_ext[RIGHT];                                         // :166  ＝グリフ ink 幅
  extra-space:       ideal = fixed + distance                         // :174-175
  shrink-space:      ideal = fixed + distance;  is_stretchable=false  // :188-192
  semi-shrink-space: fixed += d/2; ideal = fixed + d/2; 同上          // :193-198
```

⚠️ **`next-note` を持つのは Clef だけ。** KeySignature / TimeSignature の `space-alist` に
`next-note` は**無い**ので、:147 の既定である **`first-note` に落ちる**。行中なのに
`first-note` が使われるのは直感に反するが、これが LP の実挙動（実測で確定）。

| grob | 使われるエントリ | 出典 | 右 gap |
|---|---|---|---|
| `Clef` | `(next-note . (extra-space . 1.0))` | `define-grobs.scm:924` | `ink幅 + 1.0` |
| `KeySignature` | `(first-note . (shrink-space . 2.5))` | `define-grobs.scm:1947` | `ink幅 + 2.5`（伸びない） |
| `KeyCancellation` | `(first-note . (shrink-space . 2.5))` | `define-grobs.scm:1996` | 同上 |
| `TimeSignature` | `(first-note . (semi-shrink-space . 2.0))` | `define-grobs.scm:3948` | `ink幅 + 2.0`（伸びない） |

**`min_dist` の左 esw は grob ごとに違う**（`separation-item.cc:167` の既定は `(-0.1 . 0.1)`）:

| grob | `extra-spacing-width` | 出典 |
|---|---|---|
| `Clef` | 宣言なし＝既定 `(-0.1 . 0.1)` | — |
| `KeySignature` / `KeyCancellation` | **`(0.0 . 1.0)`** | `define-grobs.scm:1936` / `:1982` |
| `TimeSignature` | **`(0.0 . 0.8)`** | `define-grobs.scm:3933` |
| `Accidental` | `(-0.2 . 0.0)` | `define-grobs.scm:40` |

**実測照合**（probe `MC` / `MK`、ragged-right、符頭 ink 幅 1.304212、四分音符の ideal 3.002257）:

| | `min_dist` の内訳 | 予測 左 gap | 実測 | 予測 右 gap | 実測 |
|---|---|---|---|---|---|
| MC (clef) | `1.304212+0.1+0.1` = 1.504212 | `(3.002257+1.504212)/2` = **2.253234** | **2.253234** ✓ | `2.146680+1.0` = **3.146680** | **3.146680** ✓ |
| MK (key) | `1.304212+0.1+0.0` = 1.404212 | `(3.002257+1.404212)/2` = **2.203234** | **2.203234** ✓ | `3.300030+2.5` = **5.800030** | **5.800030** ✓ |

**両側・両ケースとも 6 桁一致。モデルは確定。**（`min_dist` の 0.1 と 0.0 の差＝KeySignature の
左 esw が 0 であること、が MC と MK の左 gap の 0.05 差をちょうど説明する。）

#### 4.7.3 行頭（小節境界）変更 item の導出済みモデル（2026-07-21・LP 2.24.4 で実測確定）

§4.7.2 の行中版に対する**行頭版**。§3.I／ロードマップ③の実装仕様。**probe K/T の4点すべて 6 桁一致。**

行頭では変更 item は bar line と**同じ non-musical 列**に入り、列の中は **break alignment** で並ぶ。

**列原点 ＝ bar line の ink 左端**（＝その anchor）。§4.7.1 の `last_ext[RIGHT]`＝0.19 がこの前提。

**(a) 列の中**（break alignment）: 各 break-align group の左端 ＝ 直前 group の ink 右端
＋ **左側 group の `space-alist` の、右側 group の `break-align-symbol` エントリ**。

| | 出典 | 予測 | 実測 |
|---|---|---|---|
| bar line ink 右 → key signature | `BarLine.space-alist (key-signature . (extra-space . 1.0))` | 1.000000 | **1.000000** ✓ |
| bar line ink 右 → time signature | 同 `(time-signature . (extra-space . 0.75))` | 0.750000 | **0.750000** ✓ |

順序は `clef, cue-clef, staff-bar, key-cancellation, key-signature, time-signature`
（`define-grobs.scm:650-664`）。**clef だけ bar line の前**に来るのはこの順序が理由。

**(b) 列 → 次の音符列**: `Staff_spacing::get_spacing`（§4.7.2 と**同じ関数**）。
`last_grob` は列内の**最右**の break-align grob なので、key/time があればそれ。

```c
fixed = last_ext[RIGHT]                    // 列原点(bar line ink 左)からの右端
  KeySignature : first-note shrink-space 2.5      → ideal = fixed + 2.5
  TimeSignature: first-note semi-shrink-space 2.0 → fixed += 1.0; ideal = fixed + 1.0
min_dist = (fixed + esw_right) + (次列の最左 ink 到達 + その esw_left)
fixed = max(fixed, 0.3 + min_dist);  ideal = max(ideal, fixed)      // :213-215
```

**実測照合**（列原点 = 20.945729）:

| | `fixed` | ideal（space-alist） | `min_dist` | `0.3+min_dist` | 採用 | 実測（列原点→符頭） |
|---|---|---|---|---|---|---|
| K（key、次の音符に♮） | 4.490030 | 6.990030 | 5.490030+1.234272 = 6.724302 | **7.024302** | 7.024302 | **7.024302** ✓ |
| T（time、次の音符は素） | 3.544735 | 4.544735 | 3.344735+0.1 = 3.444735 | 3.744735 | **4.544735** | **4.544735** ✓ |

→ **K は :213 の補正が binding、T は space-alist の ideal が binding。両方の枝を1組の probe で踏んでいる。**
（K の `1.234272` は♮が符頭より左に届く量 1.034272 ＋ Accidental の esw 0.2。）

##### Lily# の現状（実測分解）

行頭は列を持たず、次の2つが決めている:

1. `SharedRenderer.EnumerateStaffItems` の `openChangeX = ml.X + afterBar + ClefChangePadding`
   → 変更グリフは **bar line ink 右 + 0.5**（LP は key 1.0 / time 0.75）
   → 台帳の `-0.500000` / `-0.250000` は**ちょうどこの差**
2. `BarlineToFirstColumnSpring` の min に `ChangeItemPrefixWidth(firstItems, excludeClef:true)`
   ＝ `W + 2×0.5` を入れ、`ApplyMergeSpringsHeadroom` が `min + 0.3` に持ち上げる
   → K: `(3.300030+1.0) + 0.3 = 4.600030`（LP 6.834302）＝ 台帳の `-2.234272`

##### 実装後の予測（反証可能）

上のモデルを入れると:

| 台帳キー | 現在 | **予測** | 予測残差の帰属 |
|---|---|---|---|
| `barline.next.key-change-glyph` | −0.500000 | **0** | — |
| `barline.next.time-change-glyph` | −0.250000 | **0** | — |
| `barline.next.key-change-to-notehead` | −2.234272 | **−0.034272** | 臨時記号→符頭の距離（Lily# 0.866666 / LP 1.034272）が `min_dist` 経由で入る。**③ではなく padding 側の欠陥** |
| `barline.next.time-change-to-notehead` | −1.454735 | **−0.004735** | TimeSignature grob 幅 Lily# 1.600000 / LP 1.604735。**OPEN**（`fattened.four` の LILC は 1.600000 ちょうどなので、LP がどこで 0.004735 を足しているか未特定） |

**合計 4.439007 → 0.039007 ss。exact は 15/21 → 17/21 の見込み。**
**外れたら診断が違う。**

##### これが §4.7 項目1（＝ HANDOFF §2①）に意味すること

**「3つの extent ヘルパの frame ＋ 定数2つ」では `midmeasure.*` の4点は 0 にならない。**
Lily# の行中変更の幾何を実際に決めているのは:

1. `SpacingRules.ChangeItemPrefixWidth` ＝ `W + 2×ClefChangePadding` を**1本の spring に加算**
2. `SharedRenderer.EnumerateStaffItems` の**ぶら下げ式** `itemX = 列X − (W + ClefChangePadding + 次の臨時記号)`

extent ヘルパは `CalculateSkylineDistance` の skyline フォールバック経由でしか効かない。
実測で分解すると Lily# の MC は

```
head2→head3 = min + 0.3
            = (符頭右 1.304 + CalculateLeftExtent(clef) 1.505 + MinItemGap 0.4) + prefix 3.010 + 0.3
            = 6.519000        ← 実測 6.519000
```

なので frame を左端基準にすると `CalculateLeftExtent(clef)` が 1.505 → 0 になり
`6.519 → 6.012`（＝ ideal 3.002 + prefix 3.010 が勝つ）。LP は 5.399914 なので
**残差 +1.119 → +0.612 に減るだけで 0 にはならない**。しかも**分配は直らない**:
ぶら下げ式は変わらないので右 gap は 2.510 のまま（LP 3.146680）、左 gap が 3.502（LP 2.253234）で
**逆符号のまま残る**。

⚠️ したがって「①が正しければ4点が 0 に向かう」という予測は**外れる**。
§5.3 の「変更する前に測る」に従って着手前に測っておいたので、これが**着手前に**分かった。

**正しい切り分け**: 行中変更の残差は frame 単独の問題ではなく、**列が1本足りない**問題
（＝③と同じ型の欠落）。`ClefChangePadding = 0.5` が両側に使われているのも誤りで、LP は
**左右で別の式**（左＝`(ideal+min_dist)/2`、右＝grob ごとの space-alist 1.0 / 2.5 / 2.0）。
`GlyphMetrics.ClefChangePadding` の `LILYPOND-REF: scm/define-grobs.scm:914-925 — Clef space-alist`
も**エントリの取り違え**（0.5 は `right-edge`、行中で効くのは `next-note` の 1.0）。

**もうひとつ独立した差**: 変更 clef の幅を Lily# は `FClefAdvance × 0.75 = 2.010` と近似しているが、
LP の `clefs.F_change` の ink 幅は **2.146680**。`_change` グリフは実在するので 0.75 倍の
近似をやめて実メトリクスを引くべき（`GlyphMetrics.cs:141-151`）。これは frame とも列とも独立。
