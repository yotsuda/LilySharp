# LP 忠実度コーパス（LP fidelity corpus）

**Lily# の幾何を、実際の LilyPond で測った値に対して固定する。** snapshot が Lily# を
「前回の自分」に対して固定するのに対し、こちらは **LilyPond に対して**固定し、
残っている差を「原因つきの数値」として台帳に載せる。

## なぜ必要だったか

これまで LP 実測値は毎回 scratchpad に取られて捨てられていた。結果として

- 同じ値を毎セッション測り直していた（引継ぎに「取得済」と書いてあっても実体が無い）
- **誤った解釈が何世代も引き継がれた** — bar line 直後の光学補正を「clef のせい」と
  書いた記述が複数の handoff を生き延びた。実際は**下向き符尾**が原因で、
  2×2 を測れば一発で分かる（`probes/barline-spacing.ly` の score A/B/C/D）
- snapshot は Lily# 同士の比較なので、**一度承認された誤りは永久に緑**のまま

## 構成

| 場所 | 役割 |
|---|---|
| `probes/*.ly` | **LP 側のプローブ**。committed・再実行可能。値の出所がここにある |
| `Measure-LilyPondGeometry.ps1` | **X**（system 内の anchor 間距離）のプローブを通す |
| `Measure-LilyPondPageGeometry.ps1` | **Y**（ページ縦）のプローブを通す。`probes/page-vertical.ly` 用 |
| `Measure-LilyPondProbe.ps1` | **専用プローブ 1 本**を通して、その `PROBE*` 行を生のまま印字する。上の 2 本と違い dump の形を知らないので、**プローブが自分で出力を整形する**。新しい対はこちらで固める（上の 2 本は既存の点を丸ごと引きずるので値段が桁違い） |
| `lp-geometry.json` | **台帳**。LP 実測値 ＋ 現在の residual ＋ その原因 |
| `LilySharp.Tests/LpFidelity/LpGeometryProbes.cs` | **Lily# 側のプローブ**（同じ音楽を .lys で書いたもの）と測る量の定義 |
| `LilySharp.Tests/LpFidelity/RenderedGeometry.cs` | 描画結果を LP と同じ語彙（anchor 間距離）で問い合わせる |
| `LilySharp.Tests/LpFidelity/RecordingDocumentContext.cs` | 実際の描画パスを記録する `IDocumentContext` |
| `LilySharp.Tests/LpFidelity/LpGeometryLedgerTests.cs` | 台帳を強制するテスト |

## 台帳の意味論

```
residual = lilysharp - lilypond      (単位: staff-space)
```

- residual が **0** = その量は LP と一致している
- residual が **非0** = `why` に**原因を書くことが必須**。まだ切り分けられていないなら
  `OPEN:` で始めて「何が未解明か」を具体的に書く。**禁止なのは黙って baseline 化すること**
- テストは **両方向で失敗する**。residual が増えれば回帰、**減っても失敗**する
  （改善を diff に残さず吸収してしまわないため）

「一致数 / total |residual|」がこのプロジェクトの忠実度スコア。**単調に良くなるべき数値**。

## 精度についての注意

**SVG から測ってはいけない。** `SvgGenerator` は座標を `F2` で出力する（`SvgGenerator.cs:229`）
ので 0.01 に量子化され、0.005 未満の残差は消える。LP 側は 6 桁で語る世界なので、
0.189365 と 0.142857 と 0.19 の区別がノイズに埋もれる。

そのため Lily# 側は `SharedRenderer.RenderTo` に**記録用の `IDocumentContext`** を渡して
`double` のまま取る。製品と同じ描画パスを通るので、別実装がドリフトする心配もない。

実際これのおかげで、SVG では見えなかった **0.002 / 0.0013 の閉じ側の差**
（`barline.prev.*`）が初めて可視化された。

## 点を追加する手順

1. `probes/*.ly` に LP 側のスコアを足す（`\lay "TAG"` でタグ付け）
2. `pwsh audit/lp-geometry/Measure-LilyPondGeometry.ps1` を実行し、印字された値を得る
3. `lp-geometry.json` に `lilypond` の値だけ入れ、`residual` は `null` のままにする
4. `LpGeometryProbes.cs` に **同じ音楽の .lys** と測る量を足す
5. テストを走らせる → `residual` を教えてくれるので、それを台帳に記録する
6. 非0なら `why` を書く。書けないなら `OPEN:` で何が未解明かを書く

### ⚠ 二つの側を必ず一致させること

- **オクターブ表記が違う**: Lily# の `c` は LilyPond の `c'`。
  `LpGeometryProbes.cs` の各プローブは対応する LilyPond 表記をコメントで明記している
- **グリフの数え方が違う**: LilyPond は調号を `KeySignature` 1個として dump するが、
  Lily# は臨時記号を1個ずつ描く。**インデックスで数えず、種類で選ぶ**こと
  （`BarlineRightToNextNotehead` がその例）
- 測る量は**すべて anchor 間距離**にしてある。ink 幅に依存させると、
  「監査したいはずのメトリクス表」を使って監査することになる

## 測定 regime を混ぜない

プローブは `ragged-right`（force 0 ＝ 自然長）で測っている。**同じ量でも
justify された行では binding する制約が変わる**（ragged では spring の床、
圧縮時は rod）。伸び（stretch strength）の検証は別枠で、
`SpacingInvariantTests.BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance`
が2つの行幅から force を解いて独立な spring で交差検証している。

## 現状（2026-07-22 時点）

31 点中 **22 点が LP と厳密一致**、total |residual| = **1.523777 ss**
（X 22 点 = 19 exact / 0.022412、Y 7 点 = 3 exact / 0.001365、**譜間 2 点 = 0 exact / 1.500000**）。

⚠️ **total が跳ねたのは悪化ではない。** 譜間 2 点は「LP と食い違っていることが今まで
一度も測られていなかった量」で、足した瞬間に 1.5 が可視化されただけ。**比較は同じ点集合の
中でだけ意味を持つ**（この節の末尾の履歴を参照）。

| residual | 点数 | 原因 |
|---|---|---|
| +1.450000 | 1 | **幻の符尾**。`SkylineBuilder.AddNoteBoxToSkylines` が**全ての符頭に** 3.5 の符尾を生やす。コメントは「全音符には符尾が無い」と書いてあるが**コードに音価の判定が無い**。描画側は `noteValue >= 2` で正しく分岐している（`SharedRenderer.Noteheads.cs:473`）ので、**全音符は符尾なしで描かれ、符尾があるものとして間隔が取られる**。LILYPOND-REF: `lily/stem.cc` `Stem::is_normal_stem`（duration-log >= 1 のみ符尾を持つ） |
| −0.050000 | 1 | **StaffSymbol の extent**。Lily# は五線を**線の中心** ±2.0 で種にするが、LP のスカイラインは**線のインク** ±2.05（線幅 0.1 の半分）。`probes/glyph-skyline.ly` が grob 自身に聞いて ext・`vertical-skylines` とも (−2.05 . 2.05) を返している |
| −0.017606 / +0.000010 ×2 | 3 | **水平スカイライン項**（未移植）。LP は臨時記号の右スカイラインを符頭のスカイラインと測る（`accidental-placement.cc:412`）が Lily# は box で測る。グリフ依存 |
| −0.004735 | 1 | **TimeSignature grob 幅**（Lily# 1.600000 / LP 1.604735）。原因特定済＝`ly:time-signature::print` は markup を組むので幅を**テキストレイアウト経路**が決める。Lily# の 1.600000 は LP 自身の音楽フォント経路の値と一致しており、定数の誤りではない |
| clef の sliver 4 点 | 4 | `system.clef-bounded-distance` の `why` にまとめてある（LILC bbox 3.550 vs LP の skyline 3.540） |

このコーパスが潰してきたもの:

- **行中の clef/key 変更**（4点・6.843242 ss）→ `4eb8cf16`
- **グリフメトリクス**（7点）→ `ec7a2254`。LP と同じく `LILC` テーブルから読むようにした
- **行頭の key/time 変更**（4点・4.439007 ss）→ `d056b5e5`。
  **着手前に置いた4点の予測が桁まで的中**した（`COORDINATE_AUDIT.md` §4.7.3）
- **臨時記号 padding**（3点・0.334252 ss）→ `94e8996c`。LP の `right-padding` 0.15 が抜けていた
- **`fills_measure` が rest を弾いていた**（1点・1.000000 ss）→ `a64ffc16`

### ★ 片側しか測っていない点は、欠陥に構造的に盲目になる

`a64ffc16` の教訓。スコア F（`r1 | r1`）は**閉じ側** `barline.prev.whole-rest` だけを持っていて
**厳密一致で緑**だった。しかし `full-measure-extra-space` が乗るのは**開き側**なので、
Lily# が全休符に 1.0 を払っていないことをコーパスは**見ることができなかった**。
`barline.next.whole-rest` を足した瞬間に −1.000000 が出た。
**点を足すときは両側を足す。**「その量は緑だから大丈夫」は片側だけ見ている証拠かもしれない。

**2026-07-22 に同じ教訓をもう一度**: 譜間の 2 点（`staff.staff.*`）は「上の譜から下向きに
出っ張る」形と「下の譜から上向きに出っ張る」形の**対**で足した。同じ音楽・同じ算術なので
LP はどちらも 9.595000 を返し、残差も**同じ値になるはずだった**。ならなかった
（−0.050000 と +1.450000）。**その差が幻の符尾**で、片方だけ足していたら見えていない。

## 譜間（staff-to-staff）の測定 — `probes/page-vertical.ly` book P / Q

`staff-refpoint-extent` は system 内の**全 spaceable staff の refpoint の区間**
（`lily/system.cc:705-717`）なので、**2 段譜の system ではその幅がそのまま譜間距離**になる。
つまり page プローブのダンプが既に持っていて、測定スクリプトが印字していなかっただけ。

⚠️ **素の 2 段譜では何も測れない。** LP は隣接する譜を
`max(skyline距離 + padding, minimum, basic)` に置く（`align-interface.cc:228-238`、
StaffGrouper の 9 / 7 / 1）ので、両側とも五線なら 2.05 + 2.05 + 1 = 5.1 で **basic 9 が勝ち、
五線の extent は出力に一切現れない**。**binding する X で片側だけが突出物**である必要がある。

## 縦（Y）の測定 — `probes/page-vertical.ly`

X の台帳とは別枠で、**ページ縦**を測る手段がある（点はまだ台帳に入れていない）。

```powershell
pwsh audit\lp-geometry\Measure-LilyPondPageGeometry.ps1
```

**LP 2.26.0**・A4・markup 無しでの実測（すべて staff-space）:

| 量 | **LP 2.26.0** | 参考: 2.24.4 |
|---|---|---|
| `paper-height` / `paper-width` | 169.009370 / 119.501575 | 同じ |
| `top-margin` / `bottom-margin` | **5.690551 / 5.690551**（各 10 mm） | 2.845276（5mm）/ 3.414331（6mm） |
| `line-width`（左右余白 15 mm 各） | **102.429921** | 108.120472（10 mm 各） |
| 縦の使用可能帯 | **157.628268** | 162.749764 |
| system 間の**自然**距離 | **12.000000**（= `system-system-spacing` basic-distance） | 12.000000 |
| 満杯ページでの伸長後距離（本プローブ book J） | **12.254816**（12 gap すべて同値） | 11.801982 |
| 上余白 → 先頭 staff refpoint | **6.000000**（= `top-system-spacing` basic-distance ちょうど） | 4.779000 |

⚠️ **余白の既定は版で違う**（2.24.4 は top 5mm / bottom 6mm / 左右 10mm、2.26.0 は
top/bottom 10mm・左右 15mm）。上の 2.24.4 列は**過去の記述との突き合わせ用**で、
正は 2.26.0 列。プローブの既定 exe も 2.26.0 を指している。

✅ 先頭 staff refpoint が **`top-margin + 6.000000` ちょうど**に来ることは、
`VerticalSpacingParameters.TopSystem.BasicDistance = 6` が 2.26.0 基準で正しいことの実測裏取り。

### ★ 縦は **staff refpoint 間**で測る。system 原点間で測ると嘘の値が出る

`staff-refpoint-extent` は system 原点からの staff の位置で、**system ごとに違う**
（小節番号を頭上に持つ system は原点がその分だけ上に伸びる）。そのため原点間距離は
間隔が一様でも system ごとに変わる。同じダンプを

- **原点間**で測ると: 最初のペア 11.528583、次 12.000000 —「圧縮されている」ように見える
- **staff refpoint 間**で測ると: どちらも **12.000000** ちょうど

`HANDOFF.md` に「LP は 11.528 に圧縮する」として残っていた数値はこの取り違えで、
圧縮ではなかった。§5.3 の「同じ box の左右が同じ基準点か」の縦版。

### ★ 最終ページは自然長で放置されない

`ragged-last-bottom = ##t` は「伸ばさない」ではなく、**直前ページと同じ force で解く**:

```
lily/page-breaking.cc:570-573
  else if (rag && !ragged ())
    // If we're ragged-last but not ragged, make the last page
    // have the same force as the previous page.
    config = layout.fixed_force_solution (last_page_force);
```

`last_page_force` の初期値は 0（`:643`）なので**単ページの書籍だけ**自然長になる。
プローブの book N/J/L がこの 3 者を分離している（J は page1・page2 とも 11.801982、
L は単ページで 12.000000）。

✅ **src と binary は 2.26.0 で揃っている**（`C:\MyProj\lilypond-src` は tag `v2.26.0`、
実測 exe は `C:\bin\lilypond-2.26.0`）。以前ここにあった「src 2.25.35 / binary 2.24.4 で
既定値が食い違う」という警告は**解消済み**。src から定数を引き写してよい。

⚠️ **ただしフォントは版で作り直されている。** 2.26.0 の Emmentaler は
(a) LILC テーブルが **zlib 圧縮**（`lily/open-type-font.cc:78-123` が透過的に inflate）、
(b) LILC の bbox が **design 単位で小数3桁に丸め**（`noteheads.s2` 6.52106 → 6.521）。
**この丸めが台帳 22 点中 10 点の LP 実測値を最大 7e-5 ss 動かした**（`070f1e21` で反映済み）。
グリフの private-use コードポイントも版をまたいで安定しないので、
`audit/scripts/Extract-Emmentaler*.py` は**必ず feta 名で引く**こと。

⚠️ **点を足すと total は増えうるので、比較は同じ点集合の中でのみ意味を持つ。**
15点 4.592405 → 19点 11.435647（`5c4126d6` が**それまで測っていなかった**行中の発散を可視化）
→ 21点 4.747978（`4eb8cf16`＋MKA 2点）→ 4.738987（`ec7a2254`）→ 0.338987（`d056b5e5`）
→ 0.022361（`94e8996c`）→ 22点 0.022361（`a64ffc16`、足した点が exact で着地）
→ 22点 **0.022412 / 19 exact**（`070f1e21`）。

⚠️ **最後の一手で基準そのものが 2.24.4 → 2.26.0 に変わった**ので、0.022361 と 0.022412 は
厳密には別物差。exact は 18 → 19 に増えている。**この行より前の LP 実測値は 2.24.4 のもの。**
