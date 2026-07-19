# LilyPond 座標系 忠実移植 監査 (LILYPOND-REF coordinate-fidelity audit)

2026-07-19。全 `LILYPOND-REF`(1757件/~170ファイル)のうち**幾何(座標)に関わるもの**を対象に、
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
| **L5** | X スペーシング格子 | PaperColumn | X-right | ss | 水平スペーシング spring |

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
| **LS5** | X スペーシング | X-right | ss | L5 | ※agent H |

歴史的経緯: かつて layout が **page-absolute Y-down + notehead 左端アンカー**、一部 Y に
**half-space** を使用。Stage-4 で render を native Y-up 化・単一フリップに集約。beam quanter は
本セッションで half-space→ss 統一。**残る half-space/Y-down/pixel-leak/代償係数が本監査の標的**。

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
- **[low] StemCalculator.cs:205（XML doc）** — 戻り値を「in staff positions/half-spaces」と記すが
  実際は **ss**（統一前から `*2` して使っていた＝コメントが常に stale）→ **stale**。doc 修正のみ。
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
| SkylineBuilder.cs | `noteUp=pos*0.5`, `ToSystemUp(up)=up-staffMiddleY`, stem-up は加算 | Y | Y-up native | ss（pos=half→×0.5） | :525-620 |
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
- **[low・stale] StemCalculator.cs:205,262-265** — doc「staff positions/half-spaces を返す」＋「Convert back
  to staff-space」コメントに反し実際は**絶対 ss** を返す（`stem.cc:1213-1265` と一致確認）。統一で常に stale。
  → **doc の単位ラベル修正のみ**（§3.A と同一指摘）。
- **[faithful] StemCalculator.CalculateStemEndY**（`stem.cc:480-596`）— LP は half-space 计算
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

**renderer/layout は native Y-up で計算し、device Y-down への反転は描画境界で行う。**
（訂正: 監査エージェントは「`YFlipDrawingContext` に単一集約」と報告したが、実コードでは
`YFlipDrawingContext` は**未配線**〔同ファイル L49 "currently UNWIRED"〕。反転は draw 直前に
per-call で共有 `StaffFrame.ToDevice`（involution `staffMiddleY − x`）／`pageHeight − y` により実施され、
`SvgDrawingContext` は座標を素通し出力する＝**`IDrawingContext` は device Y-down を受け取る**。）
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
- **[取消・false-positive] IDrawingContext.cs:37-39** — 監査で「contract doc『Y downward』は stale」と
  したが**誤り**。`YFlipDrawingContext` が未配線のため renderer は draw 境界で per-call に device Y-down へ
  変換して `IDrawingContext` に渡す（`SvgDrawingContext` は素通し）。→ **doc『Y downward』は現状正しい**。
  将来 `YFlipDrawingContext` を配線し renderer が Y-up を直接渡すようになった時点で doc 更新が必要。
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
- **[high・value-stale] BreakAlignSpacing.cs:151-172 GetClefSpacing** — LP `define-grobs.scm:914-925` は
  Clef space-alist `key-signature (extra-space 0.82)/time-signature 1.52/staff-bar 0.7` だが Lily# は
  `minimum-space 3.5/3.5/4.2/3.7`（**旧 LP 値＋別スタイル**）。**単位は正(ss)**だが値もスタイルも現行 LP と乖離
  → 行頭 clef→key/time/bar が現行 LP より数 ss 広い。collapsed spring モデルへの意図的補償の可能性あるが
  参照元の値ではない。
- **[med・value-stale] :182-187 GetKeyCancellationSpacing** — key-sig `0.3`(LP0.5)/time-sig`1.15`(LP1.25) stale
  （staff-bar 0.6 は一致）。prefix 幅の軽微ドリフト。
- **[med・value-stale] :234-236 GetTimeSignatureSpacing→StaffBar** — `2.0` vs LP `1.0`(`:3952`) stale。
- **[low・line-stale] EngravingDefaults.cs:359-373** — tie/slur 太さ**値は faithful**（1.2/0.8, LP `:2039/2841`）だが
  引用行番号 `:3175/:3898` が旧レイアウト（行ドリフト）。
- **[low] TabStaffGeometry.cs:84** — TabBeamQuant が `0.6*tanh` のみで LP `:766` の `/(damping+concaveness)`
  除数を落とす。LP 既定(damping1/conc0=除数1)でのみ faithful。tab 限定・ss 一貫。
- **[faithful]** GlyphMetricsGenerated（font BBox 全 ss・Y-up・単一変換）、EngravingDefaults 太さ群
  （`define-grobs.scm` と値一致）、SpacingRules/Spring/SpringSolver/StaffSpacing/NoteSpacing（spring・duration
  space 式一致、無次元 force+ss）、EmmentalerGlyphs（code point のみ）。`MinItemGap0.4 vs LP horizontal-padding0.1`
  は**文書化された意図的補償**。

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
native Y-up 化。** ただし device 反転は**まだ per-call**（draw 境界で共有 `StaffFrame.ToDevice`／`pageHeight−y`；
単一集約の `YFlipDrawingContext` は実装済だが**未配線**＝`IDrawingContext` は現状 device Y-down を受ける）。
残存 Y-down フレームが2系統:
- **(a) 譜間/system 縦積み**（`MultiStaffLayouter`/`LayoutEngine`）が **Y-down page-absolute**。単位 ss で
  大きさは LP `align-interface` と一致するが**方向のみ設計反転**。全 annotation touchpoint で `-YUp`/`ToDevice`
  の単一フォールド反射を要する＝**最大の非-native フレーム**（一貫変換ゆえ数値は正、frame 忠実性としては未完）。
- **(b) 個別 device 島**（TieVariant, Pedal[dead], MusicMarkEngraver 内部, 水平 skyline の Y horizon,
  TabStaffGeometry, beam collision island, tab beam quanter）— device Y-down で動き境界で反射。faithful だが
  frame 非統一（将来の符号ミス誘発 smell）。

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
| — | (既知) | BeamScoringProblem collision island / tab beam quanter | frame island | §3.A 記載・別 follow-up |

### 4.4 doc/label のみ修正（コードは正）
- **IDrawingContext.cs:37-39** — 「Y downward」は**現状正しい**（YFlip 未配線・§3.G の訂正）。監査の当初指摘は
  false-positive。将来 YFlip 配線時に更新。
- **StemCalculator.cs:205,262-265** / **StemInfo** — 戻り値「staff positions/half-spaces」ラベルが実際は ss。
  → **修正済**（ss と明記）。
- **COORDINATE_SYSTEM.md** 陳腐化 → **修正済**（本書へのポインタ＋layer 表更新）。
- 多数の LILYPOND-REF 行番号ドリフト（cosmetic、関数は正しく指す）— 一括再採番は別途。

### 4.6 対処状況（2026-07-19）
| # | 項目 | 状況 |
|---|---|---|
| 1 | Slur staff-line frame 不一致 | ✅**修正**（staffMiddleY 導入で絶対-page frame へ・commit `1c902285`） |
| 2 | BreakAlignSpacing clef/key/time 値 | ✅**LP 値採用**（extra-space 0.82/1.52/0.7 等・目視で LP 一致確認・171 snapshot 再ベース・`9c76abf9`） |
| 3 | NoteCollision 混在幅残差 | ⏸**繰延**（=LP_COORDINATE_MODEL Stage-1 stem 相対 head X。architectural・等幅は忠実で混在幅のみ残差。partial hack は左端描画と非整合でリスク大） |
| 4 | Tie center 係数 | ✅**修正**（0.375·h / 0.75·h・`d81d379d`） |
| 5 | Hairpin decrescendo 分数 | ✅**修正**（`1342012b`） |
| 6 | BreakAlign KeyCancel/TimeSig 値 | ✅**LP 値採用**（`9c76abf9` に含む） |
| 7 | LedgerLine 単位 | ✅**修正**（比率×head幅・`f5a4f89d`） |
| 8 | GetRestShift half-space | ✅**問題なし**（consumer 無し＝vestigial・単位混在の実害なし） |
| doc | IDrawingContext / StemCalculator / COORDINATE_SYSTEM | ✅（IDraw=false-positive、他2件 doc 修正済） |
| 島 | 譜間縦積み Y-down / device 島群 / YFlip 配線 / LILYPOND-REF 行再採番 | ⏸**繰延**（frame 忠実性の残・数値は正） |

### 4.5 結論
**方向・単位の忠実移植は大部分達成**（Stage-4 Y-up 集約＋ss 統一＋単一フリップ）。ユーザー懸念の
「LP 座標系を同形で導入できていない箇所」は**局所的に残存**し、最重要は **①slur の staff-line 回避 frame 不一致
（実害あり silent no-op）**と **②譜間縦積みの Y-down 設計残存（数値は正・frame 忠実性のみ未完）**。それ以外は
値 staleness・doc ラベル・device 島の非統一で、いずれも局所修正可能。beam quanter で行った「方向 AND 単位を LP と
揃えてから字面移植」の原則を、上記 §4.3 各所へ順次適用するのが次段。
