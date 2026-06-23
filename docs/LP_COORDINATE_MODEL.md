# LilyPond 座標モデルの完全模倣 — 設計ドキュメント

`lp-coordinate-model` ブランチの作業基盤。Lily# のレイアウトを LilyPond の座標モデル
そのものに揃えることで、LP レイアウトの完全模倣の**土台**を作る。散文は日本語、識別子・
パス・LP 引用は原文どおり。引用元は `C:\MyProj\lilypond-src`。

---

## 0. なぜやるか

LP は**単一座標系ではなく、grob 親子チェーンによる相対参照点の木**(複数の座標系)を
使い分けている。Lily# は現在**ページ絶対座標(Y-down)+ notehead を左端アンカー**で
近似しており、出力は(補償により)概ね正しいが、内部表現が LP と別物なため:

- LP の式を移植するたびに座標系の差を手で補償する必要がある(`NoteCollision` の magic
  1.0 がその典型 = `0.52 × 2.0` を left-edge フレームで表した値)。
- `note-collision.cc:343-348` の extent 正規化のような「head extent を stem 基準の符号付き
  区間で扱う」LP コードが、left-edge 基準(`[LEFT]=0`)では成立しない。

座標系を LP と同一化すれば、LP の式・定数がそのまま一対一で移植できる。出力の正しさの
ためというより、**完全模倣のアーキテクチャ忠実性**のための投資。

> 重要な前提: この作業は **byte-neutral にならない**(中間表現・絶対座標が変わる)。
> 安全網は「**衝突等のサンプルを LP で描画し、出力一致を実測**」(§3 of DEV_BUGFIX_WORKFLOW)
> + 検証付き snapshot リベース。一時的な回帰は許容する方針(ユーザー合意済み)。

---

## 1. LP の機構(grob.cc)

- **親は軸ごとに独立**(`grob.hh:192-197`): `dim_cache_[X_AXIS].parent_` /
  `dim_cache_[Y_AXIS].parent_`。`set_parent(e,a)` / `get_x_parent()` / `get_y_parent()`。
- **累積 = 親チェーンを上って offset を総和**(`grob.cc:380-397 relative_coordinate`):
  ```cpp
  Real Grob::relative_coordinate (Grob const *refp, Axis a) {
    Real result = 0.0;
    for (Grob *ancestor = this; ancestor != refp; ancestor = ancestor->get_parent(a)) {
      if (!ancestor) break;
      result += ancestor->get_offset(a);
    }
    return result;
  }
  ```
  `get_offset(a)`(`grob.cc:452-474`)は親に対する自分の offset を callback で1回評価して
  キャッシュ。**絶対 X/Y = 共通 refpoint まで get_offset(a) を総和**。
- すべて **staff-space・Y-up**。device フリップは stencil/page 出力時の1回だけ(§5)。

---

## 2. 参照点ツリー(コア grob)

親は `Axis_group_interface::add_element`(`axis-group-interface.cc:53-74`)で確立:group の
`axes` プロパティの各軸について `if (!e->get_parent(a)) e->set_parent(me, a)`。`axes` は
`define-grobs.scm`。`axes` 行を持つ grob は axis-group(子の refpoint)、無いものは leaf Item。

| Grob | X-parent | Y-parent | 根拠 |
|---|---|---|---|
| NoteHead | NoteColumn | NoteColumn(初期)/ staff-position で Y | note-column.cc:134-156; head `X-offset ly:note-head::stem-x-shift`(0 を返し stem を起動), `Y-offset staff-symbol-referencer::callback` |
| Stem | NoteColumn | NoteColumn | note-column.cc:121-125; `X-offset ly:stem::offset-callback` |
| NoteColumn | PaperColumn(X)/衝突時は NoteCollision | VerticalAxisGroup(Staff) | axis-group `(,X ,Y)` define-grobs.scm:2572; 衝突時 note-collision.cc:624-629 |
| Rest | NoteColumn | staff(`ly:rest::y-offset-callback`) | note-column.cc:137-156 |
| Accidental | head / AccidentalPlacement | (Y 継承) | `X-offset ly:grob::x-parent-positioning`; note-column.cc:225-229 |
| Dots | DotColumn | NoteHead(staff-pos) | note-column.cc:238-241 |
| Script/Articulation | side-position support(head/stem) | side-position | `script-interface::calc-x/y-offset`; side-position/outside-staff |
| Beam | (stems を span) | staff | staff-symbol-referencer; stems 経由で配置 |
| Flag | Stem | Stem | `ly:flag::calc-x/y-offset`; note-column.cc:71-79 |
| StaffSymbol | System(X) | VerticalAxisGroup | **Y=0 の基準**(head が参照) |
| VerticalAxisGroup | System(X) | VerticalAlignment(Y) | axes `(,Y)` define-grobs.scm:4235 |
| VerticalAlignment | System | System | axes `(,Y)` 4212; align-interface |
| System | (root) | Page | axes `(,X ,Y)` 3625 |
| PaperColumn | System(X) | — | axes `(,X)` 2737(水平スペーシング格子) |
| NonMusicalPaperColumn | System(X) | — | axes `(,X)` 2518(breakable) |
| Clef / TimeSignature / BarLine | NonMusicalPaperColumn(X) | staff | break-aligned; leaf Item |

---

## 3. NoteColumn の X 基準 = STEM(衝突 port の核心)

**X=0 は stem。head は片側に寄せて配置。**

- NoteColumn の main extent = **first head の extent を column 自身に対して測ったもの**
  (`note-column.cc:184-203 main_item->extent(me, X_AXIS)`)。
- head は **`Stem::calc_positioning_done`**(`stem.cc:606-664`)で `translate_axis(amount, X)`。
  amount は `internal_calc_stem_offset_from_head`(`stem.cc:1050-1069`):
  ```cpp
  Real attach = Note_head::stem_attachment_coordinate(head, X_AXIS);
  Real real_attach = head_wid.linear_combination(attach); // head 箱上の点
  ```
  `stem-attachment` X は **−1..+1 スケール**(−1=左端, +1=右端;`note-head.cc:158-161`)。
  - **up-stem**: attach ≈ +1 → stem は head の**右端** → head は **[−w, 0]**(基準の左)。
  - **down-stem**: attach ≈ −1 → stem は**左端** → head は **[0, +w]**(基準の右)。
  - `note-head.cc:99` コメント「TODO: make stem X-parent of notehead」= stem が論理的 X 原点。
- `note-collision.cc:343-348` は shift を **head 自身の box フレームでの extent**で正規化
  (`extent_up/extent_down = sh->extent(sh, X)`、`[LEFT]`=左端 `[RIGHT]`=右端の符号付き offset)。
  最終的に down-stem head 幅 `wid` を乗算(`calc_positioning_done`)。

> **移植上の帰結**: 衝突計算は head extent が **stem/column アンカーに対する符号付き区間**で
> あることを前提とする。Lily# の left-edge アンカー(`[LEFT]=0`)では `extent_up[RIGHT] −
> extent_down[LEFT]` の差(:345/:348)が壊れる。**これが最優先で直すべき点。**

---

## 4. Y 参照フレーム

- **NoteHead Y=0 = 譜の中央線**。staff-position p は half-staff-space、0=中央線。
  `y = p * staff_space / 2`(`staff-symbol-referencer.cc:130-138,175-176`)、逆 `p = 2y/space`。
- **System Y=0** = system 自身の参照。譜ごとの Y は VerticalAlignment が累積。
- **Page Y=0** = 上部。system は負 Y で下に伸びる(§5)。
- **譜間 Y 累積 = `Align_interface::get_minimum_translations`**(`align-interface.cc:128-296`):
  譜 skyline を上から下へ積む(`stacking_dir` 既定 DOWN)。各要素 `dy =
  down_skyline.distance(...) + padding`、spacing-spec の min/basic と max、
  `where += stacking_dir*dy; translates.push_back(where)`。適用は
  `all_grobs[j]->translate_axis(translates[j], a)`。

---

## 5. 出力フリップ(内部 Y-up → device Y-down)

per-grob 反転は**しない**。単一変換が2段:

1. **ページ合成 `make-page-stencil`**(`scm/page.scm:159-227`): 各 system を
   `(- 0 y top-margin)` に配置(`y = system 'Y-offset`)。内容は上マージンから下へ**負 Y 帯**。
2. **device 出力 `dump-page`**(`scm/framework-ps.scm:109-122`):
   `gsave 0 paper-height translate set-ps-scale-to-lily-scale` で原点をページ**上端**へ。
   `output-scale` が staff-space → device 変換。Cairo backend も同じ `output-scale`。

= LP の唯一の Y フリップは「ページ→device の原点を paper-height へ移す変換」。

---

## 6. Lily# 実装の staging

Lily# 現状: **絶対累積ページ座標・Y-down・head X は左端アンカー**。LP に揃えるため
以下の参照フレームを導入。2つは**ジオメトリ変更**(衝突/スペーシング結果が変わる)、
2つは**純粋に conventional**(一貫適用すれば出力同値)。

### ジオメトリ変更(必須)

1. **stem 相対 head X(核心・最優先)**
   stem を note-column の X アンカーにし、各 head の X-extent を `stem-attachment`(−1..+1
   head-box スケール)で stem 周りの符号付き区間として表す。`note-collision.cc:343-348` の
   `extent_up[RIGHT] − extent_down[LEFT]` が意味を持つために必須。これ無しでは衝突 port は
   何をしても誤り。**単一音の最終 head X は不変に保てる**(stem 相対で計算→従来と同じ device
   X へ)はず=単一音は出力中立、衝突/多声のみ変化。LP の raw 定数
   (full 0.5 / stem_to_stem 0.65 / touch 0.5 / close_half 0.52 / distant_half 0.4 /
   mesh 0.17・dotted 0.1; `note-collision.cc:319-337`)+ extent 正規化に統一。
   - 検証: `samples/test/collision.lys`(close_half)で実測済 = 現 magic 1.0 ≈ LP。
     full(unison)/touch/mesh の各サンプルを追加し LP 描画と突合してリベース。
   - `ChordHeadPositioning.cs` は既に `ell`(right ink extent)+ dir で chord 内変位を LP 忠実に
     計算済(`stem.cc:606-760`)= 部分的に stem/extent モデルが存在。これと統一する。

2. **staff 相対 Y(notehead)**
   head Y を staff-position から `y = p * staff_space / 2`(中央線 0)。譜間 Y は
   Align/`get_minimum_translations` 風の上→下 skyline スタック。多声/多譜で縦スペーシングが
   変わる。

### conventional(出力同値・一貫適用)

3. **System 相対フレーム**: System axis-group `(,X ,Y)` が PaperColumn(X 格子)と
   VerticalAlignment(Y)を所有。絶対座標 + 定数 system 原点と等価、bookkeeping のみ差。

4. **Page 相対フレーム + 単一フリップ**: grob は内部 Y-up のまま、system を `(- 0 y
   top-margin)` に置き、device 出力時に1回だけ原点を paper-height へ。per-grob の
   `y → page_height − y` を単一変換に置換。現 Y-down 数式が正しければ出力同一・かつ LP 一致。

### 導入順(least-breakage first)

1. **stem 相対 head X**(note-column サブツリーに局所・衝突 port を解放・per-chord で検証)
2. **staff 相対 Y**(staff-position 変換)
3. **Align 風 譜間 Y スタック**(多譜 fixture が要る)
4. **System/Page フレーム + 単一出力フリップ**(相対チェーン完成後の機械的な出力リファクタ)

frames 1-2 は局所・per-chord でテスト可。3 は多譜。4 は出力のみのリファクタ。

---

## 7. 主要参照ファイル(LP 側)

- `lily/grob.cc`: relative_coordinate:380, get_offset:452, translate_axis:364
- `lily/note-column.cc`: add_head:134, calc_main_extent:184, set_stem:121
- `lily/stem.cc`: calc_positioning_done:606, internal_calc_stem_offset_from_head:1050
- `lily/note-head.cc`: stem-attachment:151-196, X-parent TODO:99
- `lily/note-collision.cc`: shift 定数:319-337, extent 正規化:343-348, calc_positioning_done:403
- `lily/staff-symbol-referencer.cc`: 75-176
- `lily/align-interface.cc`: get_minimum_translations:128-320
- `lily/axis-group-interface.cc`: add_element:53
- `scm/define-grobs.scm`: axes/offset(§2 の表)
- `scm/page.scm`: 159-227(ページ合成・負 Y 帯)
- `scm/framework-ps.scm`: 109-122(device 出力・単一フリップ)

---

## 8. 進捗

- [x] LP 座標モデルの参照ドキュメント化(本書)
- [ ] Stage 1: stem 相対 head X + 衝突 port(extent 正規化・LP raw 定数)
- [ ] Stage 2: staff 相対 Y(notehead）
- [ ] Stage 3: Align 風 譜間 Y スタック
- [ ] Stage 4: System/Page フレーム + 単一出力フリップ

> master(origin)には検証済みの座標 Y-up 化(stem/slur)・dead 掃除・実 findings 修正の
> 10コミットが入っている。本ブランチはそれを土台に、相対参照点モデルを導入する。
