# Lily# 開発ハンドオフ（常設・単一）

> **このファイルが唯一の引継ぎ先。新しい `handoff-*.md` を作らないこと。**
> 引継ぎは §1「現在地」を**書き換えて**行う（追記しない）。恒久的な知識は §4 の表に従って
> それぞれの置き場所へ出す。ここに溜め込むと、以前と同じように 16 個に分裂する。

最終更新: 2026-07-22 / master `e38a76bf`（§0 で裏取りすること）

---

## 0. セッション開始時にやること（**必ず裏取り**）

```powershell
cd C:\MyProj\LilySharp
git log --oneline -8
git rev-list --count origin/master..master     # 未 push 数
git status --short
dotnet build LilySharp.Core\LilySharp.Core.csproj --no-incremental -v q
dotnet test  LilySharp.Tests\LilySharp.Tests.csproj -v q 2>&1 | Select-String 'Passed!|Failed!'
```

⚠️ **このドキュメントも memory もコード内コメントも、書いた時点のスナップショット。**
HEAD・テスト数・シンボル名・「完了」表記は開始時に実コードで再確認する。
過去の引継ぎでは stale な記述を毎セッション複数踏んでいる（§5.2）。

---

## 1. 現在地 ← **毎セッション書き換える**

**origin より 12 ahead で未 push**（HEAD `e38a76bf`）。push はユーザー判断・コミットは可。
**テスト 0 failed / 3131 passed / 3 skipped。** Core build 0 warn / 0 err。
**LP 忠実度（X）19/22 exact, total |residual| = 0.022412 ss**（**2.26.0 基準**）。
**作業ツリーはクリーン**（未追跡の旧 `HANDOFF-*.md` 14個 ＋ `demo-lp-compat-features.lys` を除く。§8）。

⚠️ **2026-07-22 に履歴が書き換えられた（コミット日時の変更）。** メッセージは不変だが **SHA は全部変わった**。
本ファイル・`COORDINATE_AUDIT.md`・`audit/lp-geometry/README.md` の参照は張り替え済み（28件、到達性を検証）。
**同じことが起きたら、subject で新旧を突き合わせて張り替えること** — `git log -1 --format=%s <旧SHA>`
は書き換え後も dangling オブジェクトとして引けるので、そこから `git log --format='%h %s' master` を検索する。
⚠️ `git cat-file -t` では判定できない（旧コミットは gc されるまで残る）。**`git merge-base --is-ancestor` を使う。**

### ✅ LP の「正」は **2.26.0** に確定（2026-07-22・版分岐は解決済み）

src・binary とも 2.26.0 で揃った。**もう版の混成は無い。**

- `C:\MyProj\lilypond-src` は **`v2.26.0`（`3596756be0`）を detached HEAD で checkout**。
  旧 HEAD `bc68038f76`（2.25.35 devel）はその直接の祖先で 128 commit 前だった
- 実測 binary は **`C:\bin\lilypond-2.26.0\bin\lilypond.exe`**（`audit/lp-geometry/*.ps1` の既定値も更新済）
- ⚠️ **PATH 上の `python` は今も 2.24.4 同梱のもの**で pip も fontTools も無い。
  `audit/scripts/*.py` は **`py -3.13`** で起動すること
- ⚠️ `C:\bin\lilypond-2.24.4` は残っている。**比較目的以外で使わない**

`ly/paper-defaults-init.ly` と `scm/define-grobs.scm` の 2.25.35→2.26.0 差分は**著作権年のみ**なので、
旧記述の「src 2.25.35（正）」列の値がそのまま 2.26.0 の値。**2.24.4 との差は下の §2⑧ の表に集約した。**

### ⚠️ 2.26.0 は Emmentaler を作り直している（**このセッションの最大の発見**）

**① PUA コードポイントは版をまたいで安定しない。** 2.26.0 はグリフを 34 個挿入したため、
Lily# がハードコードしていた 115 定数のうち **73 個の割り当てがずれた**
（`U+E085` は `clefs.G` でなく `clefs.varC`、`U+E0EA` は `noteheads.s2` でなく `flags.stackedu7`）。
LP は常に feta **名**で引く（`lily/clef.cc:29-52`、`lily/note-head.cc`）。
→ `EmmentalerGlyphs.Generated.cs` を **名前引きで生成**するようにした（`a2ceb2f0`）。
**名前がフォントに無ければ生成器が exit 1 で落ちる**のが唯一のガード。C# 側に OTF パーサは無いので
テストでは固定できていない。**生成器を CI で回して差分ゼロを assert するのが残件。**

**② LILC テーブルが zlib 圧縮になった**（114580 → 6175 バイト。otf が半分になる主因）。
`lily/open-type-font.cc:78-123` が透過的に inflate し、非圧縮ならそのまま読む。
⚠️ **`Extract-EmmentalerMetrics.py` が inflate しないと LILC を 0 件と判定してアウトライン
fallback に落ちる** — それは `ec7a2254` が「非 LP 方式」として捨てた経路。対応済み。

**③ LILC の bbox が design 単位で小数3桁に丸められた**（`noteheads.s2` 6.52106 → 6.521）。
共有 628 グリフ中 566 個が最大 7e-5 ss 動く。**台帳 22 点中 10 点の LP 実測値がこれで動いた。**

### 残る残差は **22点中3点 0.022412 ss**

| 残差 | 点数 | 原因 |
|---|---|---|
| −0.017672 | 1 | **水平スカイライン項**（未移植）。`barline.next.key-change-to-notehead`（♮）。LP は臨時記号の右スカイラインを符頭のスカイラインと測る（`accidental-placement.cc:412`）が Lily# は box で測る |
| −0.004735 | 1 | **TimeSignature grob 幅** 1.600000 / LP 1.604735。原因特定済＝LP は markup を組むので**テキストレイアウト経路**が幅を決める（下の ⚠️）。もう OPEN ではない |
| +0.000005449 | 1 | `barline.next.down-stems-after-clef`。**2.26.0 のフォントで新規発生・`OPEN:`**。LP の光学補正が 0.189365 → 0.189360 と動いたのに Lily# は 0.189365449 のまま。丸めのスケール内だが、どのメトリクスが `/7` を通って 5.4e-6 を運ぶかは**未特定** |

⚠️ **♯ の 2 点（`barline.next.accidental-to-notehead` / `midmeasure.key-cancel.key-to-next-note`）が
exact 化したのは「直った」からではない。** 2.26.0 で `accidentals.sharp` の右端が 1.100000
ちょうどになり、スカイライン項（−0.000010）が**見えなくなった**だけ。水平スカイラインは今も未移植で、
**それを測っている点は ♮ の 1 点だけになった**。台帳の `why` にも同じことを書いてある。
（2 点が同時に、どちらも 0.000010 で閉じたことは「同一原因」という以前の読みの裏取りにはなった。）

**X 軸の「定数を1つ直せば閉じる」ネタは尽きた。** 残る3点のうち2点は**Lily# に無いパイプライン**が
要り（水平スカイライン1点／テキストレイアウト1点）、1点は `OPEN:`（5.4e-6）。
§2⑧ の余白4定数は `e38a76bf` で閉じた。**次の一手は `audit/lp-geometry` の Y 展開**
（§2⑧ の「Y コーパスの起こし方」）。縦は現在 SVG の 2 桁でしか比べられておらず、
**LP との残差を 6 桁で持てていない唯一の軸**になっている。

⚠️ **TimeSignature 幅 −0.004735 は「定数を直す」問題ではない**（2026-07-22 に実測で確定）。
`ly:time-signature::print` は **markup を組んで `grob-interpret-markup` に渡す**
（`scm/time-signature.scm:18-29`）ので、幅は**テキストレイアウト経路**が決める。同一 grob 上で:

| 経路 | 桁4 |
|---|---|
| `ly:font-get-glyph "fattened.four"`（音楽フォント＝このコーパスが使う経路） | **1.600000** |
| `(markup #:number "4")`（print が実際に通る経路） | **1.604735** |

**Lily# の 1.600000 は誤りではなく、LP 自身の音楽フォント経路の値と一致している。**
10桁すべてについて棄却済み: LILC bbox / 生 outline / advance / plain 族(U+0030-0039) /
fattened 族(U+E0B4-U+E0BF) / フォントビルド差（LP 同梱と repo は別サイズだが桁 metric は同一）。
markup 値は 0,2,8 / 3,5,7 / 6,9 が同値になるが、**フォント内のどの metric もその group を作らない**。
2桁文字列も1桁幅の和にならない（±0.102430）。**閉じるにはテキスト経路の metric が要る。**
⚠️ 以前ここに書いた「advance のまま＝§2④ の取りこぼし」という見立ては**誤りなので採用しない**。

### 直近セッション（2026-07-22 最終）でやったこと — **2.26.0 への移行**

| commit | 内容 |
|---|---|
| `a2ceb2f0` | **グリフ表を feta 名引きに**（`EmmentalerGlyphs.Generated.cs` ＋ `Extract-EmmentalerGlyphs.py`）。**出力不変・snapshot 0 件**。副産物で既存の誤りを 5 件発見して修正（下記） |
| `070f1e21` | **フォントと台帳を 2.26.0 へ**。otf 2 個＋派生 woff/woff2 4 個を差し替え、生成器2本を zlib＋名前引きに、弓記号の方向ペアを移植、`lp-geometry.json` を 2.26.0 実測へ、テスト7ファイルの旧 codepoint 直書き 31 箇所を定数参照へ。**snapshot 186 件再ベース** |
| `7291531a` | **3 ページのフィクスチャ `test/multi-page-vertical`**。§2⑧ が警告していた「複数ページを踏む fixture がゼロ」を埋めた |
| `e38a76bf` | **紙面 5 定数を LP の単位で読み直した**（§2⑧ 完了）。**snapshot 192 件再ベース** |

**`a2ceb2f0` で見つかった既存バグ 5 件**（現行フォントに対しても誤っていた）:

| 定数 | 実際に描かれていたもの | 修正後 |
|---|---|---|
| `Fermata{Short,Long}{Above,Below}` | **Henze フェルマータ** | 通常の `scripts.u/dshortfermata`・`u/dlongfermata` |
| `ArticThumb` | **`scripts.snappizzicato`** | `scripts.thumb` |

LP は両者を別の articulation として持つ（`scm/script.scm:356` shortfermata ↔ `:183`
henzeshortfermata、`:220` ↔ `:174`）。**どのフィクスチャも踏んでいないので snapshot は動かない**
＝コーパスはこの修正を見ていない。articulation を次に触るとき fixture を足すこと。

**弓記号は本物の LP 挙動変更**: 2.24.4 は `("downbow" . "downbow")` と上下で同一グリフだったが、
2.26.0 は `(ddownbow . udownbow)` / `(dupbow . uupbow)` に分割（`scm/script.scm:88`・`:453`）。
旧グリフはフォントから消えている。`ArticulationItem` / `ArticulationEngraver` を方向選択に移植した。

⚠️ **snapshot 186 件の再ベースは全行分類してから承認を取った**: 3277 行がコードポイントのみ、
26 行がコードポイント＋座標、108 行が座標のみ、**座標の最大変化は 0.01＝`F2` 丸め 1 単位**。
36 種のコードポイント移動のうち **35 種が両フォントで同一 feta 名**、残る 1 種が上の弓記号の分割。
**記号の同一性は変わっておらず、レイアウトも丸め以上には動いていない。**

**判明した2つの訂正**（どちらも過去の記述が誤り）:

1. **`11.528` は圧縮ではなく基準点の取り違えだった。** `staff-refpoint-extent` は system ごとに
   違う（小節番号を頭上に持つ system は原点がその分上に伸びる）ので、**system 原点間**で
   測ると間隔が一様でも値がばらつく。同じダンプが原点間で 11.528583 / 12.000000、
   **staff refpoint 間では 12.000000 / 12.000000**。LP はそこで圧縮していなかった。
   → **縦は staff refpoint 間で測ること**（§5.3 に追記済）。
2. **`ragged-last-bottom` は「伸ばさない」ではない。** `page-breaking.cc:570-573` は最終ページに
   **直前ページと同じ force** を渡す（`fixed_force_solution(last_page_force)`）。
   `last_page_force` の初期値 0（`:643`）なので**単ページ書籍だけ**自然長。
   Lily# は無条件に force 0 を入れていた＝**最終ページだけ違って見える**というユーザー報告の
   直接原因。`c353bc85` は「1ページに何本入るか」を直したが「最終ページをどう詰めるか」は
   残っていた。

⚠️ **snapshot が 1 件も動かないのは、複数ページを踏む committed フィクスチャが1つも無いから。**
「変更が無害」の証拠ではなく**フィクスチャの穴**。

### その前のセッション（2026-07-22 前半）でやったこと

**§2④' と §2⑥ を完了。§2⑤ は既に完遂済みと判明。** どちらも予測が全点的中した。

| commit | 内容 |
|---|---|
| `94e8996c` | **§2④' 実装**。`AccidentalNoteGap` 0.2 → **0.35**（LP の `padding` 0.2 ＋ `right-padding` 0.15）。**3点とも予測が桁まで的中**。snapshot 22件（全て `test/*`、showcase はゼロ） |
| `8bc90025` | §2⑤ は `098f5279`＋`8448749a`＋`94656b84` で完遂済みと判明。元 handoff を回収して §2⑤⑥⑦ に整理 |
| `a64ffc16` | **§2⑥ 実装**。`fills_measure` を LP の musicality 判定へ。**変更前に点を足して予測 −1.000000 を記録 → 実測 −1.000000000 → 修正後 0**。snapshot 2件 |

**0.338987 → 0.022361 ss、17/21 → 18/22 exact。** ④' の exact 数は予測どおり動かない
（−0.000010 は tolerance 1e-6 より大きい）。⑥ で足した点が exact で着地して +1。

参考: この前の 2026-07-21 セッションは §2①③④ と §2② の掃除を入れた
（`ec10fd4b` 計測ハーネスの stderr 混入修正 / `f4b94f64` 行中変更 item の LP モデル導出 /
`4eb8cf16` §2① / `ec7a2254` §2④ LILC / `d056b5e5` §2③ 境界列 / `2745d603` §2② 掃除）。
**台帳の値自体は全部正しく、壊れていたのは再現手段のほうだった。**

### 進行中で中断しているものは無い

**X 軸は行中・行頭・臨時記号とも完結。**

⚠️ **`total |residual|` の履歴は点集合が違う。同じ集合の中でだけ比較すること。**
15点 4.592405 → 19点 11.435647（`5c4126d6` が行中4点を追加＝それまで測っていなかった発散の可視化）
→ 21点 4.747978（`4eb8cf16`＋MKA 2点）→ 4.738987（`ec7a2254`）→ 0.338987（`d056b5e5`）
→ 21点 0.022361（`94e8996c`）→ 22点 **0.022412**（`070f1e21`）。

⚠️ **`070f1e21` で比較の基準そのものが 2.24.4 → 2.26.0 に変わった。**
点集合は同じ 22 点だが LP 側の実測値が 10 点動いているので、**0.022361 と 0.022412 は
厳密には別物差**（悪化ではない。exact は 18 → 19 に増えている）。
**この行より上の日付が古い記述に出てくる LP 実測値は全部 2.24.4 のもの**なので、
数値を引き写すときは版を確認すること。

---

## 2. 短期ロードマップ（次の数セッション）

優先順。**①②は COORDINATE_AUDIT §4.7 の残り**で、ユーザー合意済みの順序。

### ① ✅ 完了（`4eb8cf16`）— 行中（mid-measure）の変更 item を LP の専用列として価格付け

> **2026-07-21 に再スコープ → 実装。** 旧①は「3つの extent ヘルパの frame ＋ 定数2つ」だった。
> **着手前に測った結果、それでは 4 点は 0 にならないと分かった**（§5.3「変更する前に測る」が
> 効いた例）。導出済みモデルと実測は `COORDINATE_AUDIT.md` **§4.7.2**。
>
> **結果**: 行中4点 6.843242 → 0.000453 ss（`clef→次の音符`は**厳密一致**）。
> 残差は全て `GlyphMetricsGenerated.cs` の4桁丸め（符頭 1.3040 vs LP ink 1.304212 等）。
> **残った未了**: 分配が正しいのは force 0 のみ。LP は2本の spring を独立に伸ばし、
> key/time の右側は**伸びない**（`shrink-space`/`semi-shrink-space`）。これは本物の
> 第2列が要るので③と同じ仕事。`SpacingRules.MidMeasureChangeGaps` に記録してある。
>
> 以下はモデルの記録（③も `d056b5e5` で完了済み）。

**LP は行中の clef/key 変更に non-musical 列を1本立て、左右を別の式で価格付けする**:

| | 式 | 出典 |
|---|---|---|
| 列原点 | 変更グリフの **ink 左端** | 実測 |
| 左 gap | `max(ideal − 列幅, (ideal + min_dist)/2)` ＝実際は常に床側 | `note-spacing.cc:105-107` |
| 右 gap | `ink幅 + space-alist 距離` | `staff-spacing.cc:147-198` |
| 　clef | `next-note` **1.0** | `define-grobs.scm:924` |
| 　key | `next-note` が**無い**ので `first-note` **2.5**（shrink） | `define-grobs.scm:1947` |
| 　time | 同じく `first-note` **2.0**（semi-shrink） | `define-grobs.scm:3948` |
| `min_dist` の左 esw | Clef=既定 0.1 / Key=**0.0** / Time=**0.0** | `define-grobs.scm:1936` / `:3933` |

**MC/MK の左右4点すべて 6 桁一致でモデル確定済み。**

Lily# 側は列が無く、`ChangeItemPrefixWidth`（= W + 2×0.5）を**1本の spring に丸ごと加算**し、
描画側が `列X − (W + 0.5 + 次の臨時記号)` で**ぶら下げる**。実測分解:
`head2→head3 = (1.304 + CalculateLeftExtent(clef) 1.505 + 0.4) + 3.010 + 0.3 = 6.519000`（実測一致）。

→ **frame だけ直すと `+1.119 → +0.612` に減るが、左右の分配は変わらず逆符号のまま。**

**同時に直したもの**: 変更 clef の幅 `FClefAdvance × 0.75 = 2.010` → LP の `clefs.F_change`
ink **2.146680**（現在は `ec7a2254` で生成器から LILC 由来）。

### ② ✅ 完了 — extent ヘルパの中心基準と、①③が殺したシンボルの掃除

**出力は完全に中立**（snapshot 0件、LP 忠実度 17/21・0.338987 のまま）。削除したもの:

| シンボル | 経緯 |
|---|---|
| `SpacingRules.ChangeItemPrefixWidth` | ③が最後の呼び出し元を外した |
| `SharedRenderer.FollowingAccidentalLeftExtent` | ①で臨時記号が rod 経由になった |
| `SpacingRules.CalculateRightExtent` | 元からの②。テストを `CalculateNoteheadRightExtent`（左端基準）へ寄せた |
| `GlyphMetrics.ClefChangePadding` | 上の掃除で参照ゼロに。**そもそも LP の量ではなかった**（0.5 は `right-edge`） |

`CalculateLeftExtent` / `CalculateNoteheadRightExtent` の変更 item 分岐は
**左端基準（0 / 全幅）**へ。

⚠️ **副産物で本物のバグが1つ出た**: `MeasureLayouter.ItemStartingAt` が zero-duration の
変更 item を返しており、**音符列同士の rod が変更グリフから測られていた**。
音符を返すよう直したところ、分岐が到達不能になり、かつ出力が中立に戻った
（つまり従来は rod が binding していなかっただけで、式は間違っていた）。

⚠️ **未検証で残したもの**: `GetItemToBarlineSpace` / `GetBarlineToItemMinimum` の
変更 item エントリ（`1.0 / 1.0 / 0.75`）。「中心基準だから」という根拠は消えたが、
**LP と照合し直していない**。到達経路は「変更 item が小節の最後の timing を共有する」
という **LP に存在しない構図**だけで、fixture も踏まない。

### ③ ✅ 完了（`d056b5e5`）— 行頭 key/time の境界列

モデルは `COORDINATE_AUDIT.md` §4.7.3。**着手前の予測が4点とも桁まで的中**した:

| 台帳キー | 実装前 | 予測 | 実測 |
|---|---|---|---|
| `barline.next.key-change-glyph` | −0.500000 | 0 | **0** |
| `barline.next.time-change-glyph` | −0.250000 | 0 | **0** |
| `barline.next.key-change-to-notehead` | −2.234272 | −0.034272 | **−0.034272** |
| `barline.next.time-change-to-notehead` | −1.454735 | −0.004735 | **−0.004735** |

**4.439007 → 0.039007 ss、exact 15/21 → 17/21。** snapshot 14件。

**未モデル化（意図的）**: LP は key 変更を `KeyCancellation` と `KeySignature` の**2 grob**に分け
間に 0.5 を置くが、Lily# は1つの `KeySignatureChangeItem` に畳んでいる。コーパスは踏まない
（probe K は 0→3 個で cancellation が出ない）。`BoundaryChangePrefix` に記録。

⚠️ **`BoundaryColumn.cs`（clef を bar line の前に置く既存の型）とは別物**。今回入れたのは
`SpacingRules.BoundaryChangePrefix` ＋ `BarlineToFirstColumnSpring` の `last_grob` 切替。
両者の統合は未着手。

### ④' ✅ 完了（`94e8996c`）— 臨時記号 → 符頭の距離

`GlyphMetrics.AccidentalNoteGap` は LP の `padding` 0.2 **だけ**で、`right-padding` 0.15 が
抜けていた（`accidental-placement.cc:397` / `:400`、適用は `:412-416`）。**0.35 に。**
式・摂動法の裏取り・グリフ別のスカイライン項は**その定数の `<remarks>` に全部書いてある**。

| 台帳キー | 実装前 | 予測 | 実測 |
|---|---|---|---|
| `barline.next.accidental-to-notehead`（♯） | −0.149990 | −0.000010 | **+0.000010** |
| `midmeasure.key-cancel.key-to-next-note`（♯） | −0.149990 | −0.000010 | **+0.000010** |
| `barline.next.key-change-to-notehead`（♮） | −0.034272 | −0.017606 | **−0.017606** |

**0.338987 → 0.022361 ss。** 絶対値は3点とも桁まで的中（符号は逆＝0.35 が LP の 0.349990 を
わずかに超える分）。exact 数は予測どおり不変。snapshot 22件。

**残った未了 = スカイライン項そのもの**（意図的に未移植）。LP は `:412` で臨時記号の右
**スカイライン**を符頭のスカイラインと測るので、縦に細いグリフだけ box より外に出る
（♮ +0.017606 / ♯♯ +0.047704 / ♯ −0.000010 / ♭ −0.000004 / ♭♭ −0.001996）。
移植には**水平スカイラインの基盤**が要る（§3B②の島に接続）。

### ④ ✅ 完了（`ec7a2254`）— グリフメトリクスを LILC 由来に

**LP はグリフ bbox を、フォント埋込の `LILC` テーブルから読む**
（`lily/open-type-font.cc:288` `load_scheme_table("LILC")` ＋ `:389-407`。生アウトラインは fallback）。
`GlyphMetricsGenerated.cs` はアウトライン（`BoundsPen`）から取っていた＝**非 LP 方式**で、
これが台帳に残っていた 1e-4〜1e-3 級の残差の**唯一の原因**だった。

入れたもの: 生成器を LILC 優先に／出力を **6桁**に／`ApplyLeftHeadWidth` と
`GetKeySignatureAccidentalWidth` を **advance → ink extent** に／変更 clef も生成器から。

**7点が 0 に**（`barline.prev.whole-note` `.half-note` `barline.next.down-stems-after-clef`
`midmeasure.clef.prev-note-to-clef` `midmeasure.key.prev-note-to-key`
`midmeasure.key.key-to-next-note` `midmeasure.key-cancel.prev-note-to-key`）。
**8/21 → 15/21 exact。** snapshot 184件・承認のうえ再ベース。

⚠️ **踏んだ罠（再発しやすい）**: 生成 bbox から派生する定数を `static readonly` にすると、
**partial クラス間の静的初期化順序は C# で未定義**なので既定値の `BBox`（=0）を読む。
変更グリフの幅が全部 0 になり clef が自分の gap から消えた。**プロパティにすること。**

⚠️ `down-stems-after-clef` が閉じたのは**予測外**。残差 +0.00002 は「符尾の符頭接続オフセット差」
と帰属されていたが、実際は符頭メトリクスだった。**帰属は閉じてみるまで確定しない**例。

### ⑤ ✅ 完了 — MMR run のグルーピング

**「LP は clef があっても run を保つが Lily# は弾く」は既に解消済み。** 3層とも入っている:

| 層 | commit |
|---|---|
| run グルーピング（`IsBreakAlignedChange` に `ClefChangeItem`） | `098f5279` |
| 描画（clef を bar line の**前**へ＝`BoundaryColumn` / `BoundaryClefAllowance`） | `8448749a` |
| 視覚フィクスチャ `test/mmr-clef-change-bound` | `94656b84` |

2026-07-22 に LP 2.24.4 と PNG を並べて再確認済み（単一 "5" ＝ longa 4 ＋ whole 1、bass clef は
bar line の前）。旧 handoff が「描画位置の方針をユーザーに仰げ」としていた論点は**測定で解決済み**:
clef は列の**原点**を左へ動かすだけで bar line は動かさない（bar line 間は clef の有無によらず
14.133856）。だから rod には足さず、`BoundaryClefAllowance` として**前の小節の閉じ側**に付ける。

**元資料 `handoff-2026-07-21-mmr-runs.md` は本項をもって回収完了**（§8）。残っていた未解決は
下の ⑥⑦ に移した。

### ⑥ ✅ 完了（`a64ffc16`）— `fills_measure` の述語を LP に合わせた

`spacing-spanner.cc:446-472` は**列の musicality しか見ない**のに、Lily# は2箇所とも
`NoteItem or ChordItem` に絞っていた＝**全休符の小節に `full-measure-extra-space` 1.0 が
付かなかった**。2箇所を同時に `SpacingRules.IsMusicalColumn` へ。

⚠️ **ここで推論が外れた。** 「musical＝duration を持つ」ではない。`Paper_column::is_musical` は
`shortest-starter-duration` を読む＝**engrave された grob が決める**ので、**skip は非 musical**。
摂動実測（`full-measure-extra-space` → 0、LP 2.24.4）:

| 列 | 差 | |
|---|---|---|
| `r1` | **−1.000000** | 効く |
| `R1` | **−1.000000** | 効く |
| `s1` | 0 | **効かない** |
| 四分音符×4 | 0 | 効かない |

**コーパスは構造的に盲目だった**: スコア F は閉じ側（`barline.prev.whole-rest`）しか測っておらず、
fmes が乗るのは開き側。`barline.next.whole-rest` を足し、**`residual: null` ＋ 予測 −1.000000 を
先に書いてから**実装 → 変更前 −1.000000000 → 変更後 **0**（LP 1.900000 = Lily# 1.900000000）。

**副産物**: `SpacingInvariantTests.FullMeasureRest_CompactsOnCombinedPath` の
`< noteWidth / 2` は**LP 自身が満たしていない**主張だった（LP の `R1` 小節 7.890000 :
四分音符4つ 13.525735 ＝ 0.583）。`* 0.6` に緩め、**移植定数ではなく粗い gate** だと明記。

**残った未了**: `R1` は幅を MMR rod 経由で決めるので、この spring は rod が binding しない
場面でしか効かない。LP の `max()` と全ケースで一致するかは未測定。

### ⑧ Y 軸（ページ縦方向）— 着手済み。**残りは「Lily# は圧縮できない」**

**ユーザー報告「複数ページで最終ページだけ改行幅が狭い」を実測 → 原因はページブレーカーだった。**

LP 2.24.4 実測（`ragged-bottom` ＋ `systems-per-page` を絞り、伸長も圧縮も起きない条件）:
**自然 system 間距離 = 12.000 ちょうど。Lily# の最終ページも 12.000 で厳密一致。**
つまり**最終ページが正しく、それ以外のページが伸びすぎていた**。

`c353bc85` で `tallness_` / `spring_length` / `refpoint_extent_` を字面移植（§2⑥ と同じく
「rod と spring の二重計上」だった）。A4・30 反復のプローブで:

| | 変更前 | 変更後 | LP |
|---|---|---|---|
| ページ1 のシステム数 | 11 | **13** | 14 |
| ページ1 の gap | 14.55 | **12.12** | 11.528 |
| 最終ページの gap | 12.00 | 12.00 | 12.000 |

ページ間の見た目の差 **2.55 ss → 0.12 ss**。

#### ✅ 最終ページの force 継承は完了（`4ac3df8e`）

`page-breaking.cc:570-573` — `ragged-last-bottom` は「伸ばさない」ではなく、最終ページに
**直前ページと同じ force** を渡す。移植して予測どおり **12.000000 → 12.450000**（page 1 と一致）。
単ページ書籍は `lastPageForce` 初期値 0 のまま＝ 12.000000 で不変。両方テストで固定済み。

#### ✅ 紙面・余白の 5 定数は完了（`e38a76bf`）

**原因は単位の取り違えだった。** LP の point は `lily/include/dimensions.hh:27` の
`INCH_TO_PT = 72.270`＝**TeX point**（1/72.27"）で、PostScript の big point（1/72"）ではない
（`:31` に `INCH_TO_BP = 72` が別にある）。既定 staff は 20pt なので
**1 staff space = 5pt = 127/72.27 mm = 1.757299 mm**。168.4 は同じ 5pt を PostScript point で
換算した値だった。全定数は `<mm> × 72.27/127` で導ける（下表の「LP 2.26.0」列と 6 桁一致）。

**実測した影響**（推測でなく）: 192 snapshot 全てで **system 数もグリフ数も不変＝改行は動かなかった**。
グリフの最大移動は **0.70 ss** で、これは上余白の増分 +0.690551 そのもの。
つまり内容が下へ 0.69・右へ 0.036 ずれ、五線が 0.38 伸び、紙面が 0.61 高くなっただけ。
**着手前の予測は 2 つとも外れた**（「横が広がって行あたり小節が増える」→ 0.38 では届かない、
「帯が狭まって fixture が 4 ページになる」→ 3 ページのまま 13+13+8）。

⚠️ **`HaraKiriVisualTests.PagedRendering` が本当に壊れた。** `PageHeight=30` だけ固定して
余白は製品既定を継承していたため、帯が 20 → 18.62 になり分割されなくなった。
**ページブレーカーのテストは自分が割るページを所有すべき**なので、上下余白を明示させて直した
（再ピン止めではない）。横は幅に依存しないので製品既定のままにしてある。

#### 残っているのは **縦の spring/force モデル**（紙面ではない）

SVG 読み（`F2` なので 2 桁精度・§5.3 に従い残差としては扱わない）:

| | 変更前 | 変更後 | LP 2.26.0 |
|---|---|---|---|
| staff-to-staff gap | 12.36 | **12.29** | 12.254816 |
| 先頭 staff refpoint | 11.56 | **12.25** | 12.431170 |

**近づいたが閉じていない。** 6 桁で詰めるには `audit/lp-geometry` に **Y の点**が要る
（下の「Y コーパスの起こし方」）。⚠️ `RenderedGeometry` は現在 **1 ページ目しか記録しない**
X 専用構造なので、Y の点を足すには `RecordingDocumentContext` の複数ページ対応から。

#### 参考: 旧記述の紙面比較表

`Measure-LilyPondPageGeometry.ps1`（LP **2.26.0** 実測、A4、markup 無し、単位 ss）:

| | Lily# 旧 | **Lily# 現在＝LP 2.26.0** | 参考: 2.24.4 |
|---|---|---|---|
| `PageHeight` | 168.4 | **169.009370** | 169.009370 |
| `MarginTop` | 5 | **5.690551**（10 mm） | 2.845276（5 mm） |
| `MarginBottom` | 5 | **5.690551**（10 mm） | 3.414331（6 mm） |
| 縦の使用可能帯 | 158.4 | **157.628268** | 162.749764 |
| `MarginLeft` / `MarginRight` | 8.5 / 8.5 | **8.535827**（15 mm） | 5.690551（10 mm） |
| `ContentWidth` | 102.05 | **102.429921** | 108.120472 |
| `PageWidth` | 119.05 | **119.501575** | 119.501575 |
| system 間の自然距離 | 12.000000 | **12.000000**（一致） | 12.000000 |

**この表は `e38a76bf` で解消済み**（Lily# 列＝LP 列）。2.24.4 列は過去の記述との突き合わせ用。

（旧記述の「LP 5.68 / Lily# 8.93」はプローブの `section Main` マーク由来の交絡で、
markup 無しで測り直すと LP の先頭インク上端は **3.824272**。）

（この4定数は `e38a76bf` で完了。上の ✅ を参照。）

#### 圧縮は**まだ入れない**（旧記述の訂正は維持）

PASS 2 を `SpringSolver` による両方向 solve に置き換えて測ったところ、**プローブも snapshot も
1 バイトも動かなかった**（§3.4 に従い revert 済み）。理由は Lily# の使用可能帯が狭く、
ブレーカーが伸ばす側の本数しか選ばないため。**上の余白を LP に寄せると圧縮側に入る見込み**
なので、順序は「余白 → 圧縮」。置換箇所は `PageLayouter` PASS 2 → `SpringSolver.SolveForWidth`
＋ `GetPositions`、PASS 1 で `gapMinimum[]` を拾う必要あり。
**入れるときは圧縮が実際に走るケース（`SystemsPerPage` 強制など）を同時に用意すること。**

✅ **複数ページ fixture は入った**（`7291531a`）。`test/multi-page-vertical` は **3 ページ**で、
ページ1（埋まったページ）・中間ページ（ヘッダ無し）・**最終ページ（前ページの force を継承）**の
3 ケースを分離する。2 ページだと最終ページ規則と単ページ規則が区別できないので 3 が最小。
全音符なのは、ページ分割が価格付けるのは **system** であり、1 小節 1 グリフなら同じ 37 system を
88 KB で買えるから（四分音符だと 210 KB でツリー最大を超える）。最初の system だけ小節番号が
無いので `staff-refpoint-extent` が system ごとに違う＝ §5.3 の取り違えを検出できる。

**これで余白 4 定数を動かす準備は整った。**

**Y コーパスの起こし方（未着手）**: `audit/lp-geometry` の台帳は X 専用のまま。LP 側の
プローブと測定スクリプトは `d57defcd` で入ったので、あとは `lp-geometry.json` に点を足すだけ。
候補は `page.top-margin` / `page.bottom-margin` / `page.height` / `system.natural-distance`
（現状 exact）/ `page.last-page-gap`。⚠️ 版差のある量には印を付けること（§1）。

#### ✅ 解決: `VerticalSpacingParameters.TopSystem.BasicDistance` は **6 で正しい**

2.26.0 実測で **最初の staff refpoint が `top-margin + 6.000000` ちょうど**に来ることを確認した
（`Measure-LilyPondPageGeometry.ps1`、book N）。**「1 は `last-bottom-spacing` からの転記ミス」と
書いたコメントのほうが誤り**（1 は 2.24.4 の値）。潜在バグではない。
ただし `PageLayouter` は `i == 0` で `vs.SystemSystem` を使い、配置側も `topSpec.Padding` しか
読まないので **6 は今のところ出力に効いていない**（死んでいる）点は変わらない。
余白 4 定数を動かすときに配線ごと見直すこと。

### ⑦ MMR まわりの小さい未解決（記録のみ）

- **`GlyphMetrics.RestMaximaWidth = 1.8` が手動側**。フォントメトリクスなので生成器が `rests.M3` を
  出すようになったら `GlyphMetricsGenerated.cs` へ（`GlyphMetrics.cs:89-96` に記録済）
- **`SystemBreaker.BreakIntoSystemsGreedy` は MMR run 非対応**。ただし
  `LayoutOptions.UseOptimalLineBreaking` が既定 `true`（`LayoutOptions.cs:100`）なので**既定出力に
  影響しない**。かつ greedy は LP のアルゴリズムではない（LP = `constrained-breaking.cc` = optimal）
  ので**忠実度は上がらない**。優先度低

---

## 3. 長期ロードマップ

### A. LP 忠実度を測定可能にし、単調に上げる ★中心

**現状 19/22 exact, total |residual| = 0.022412 ss**（`audit/lp-geometry/`。X のみ・**LP 2.26.0 基準**）。

これがこのプロジェクトの品質指標。snapshot は「前回の自分」との比較なので、一度承認した誤りは
永久に緑のまま。台帳は **LP との距離**を数値で持ち、増減どちらでもテストが落ちる。

- 短期: ✅ X 軸（§2 ①③④④'）は完結。**定数1つで閉じる残差はもう無い**
- 中期: **コーパスを縦（Y）にも広げる** — 現在は bar line 周りの X のみ。
  ページ縦は LP 側のプローブと測定スクリプトが `d57defcd` で入った（§2⑧）ので、
  次は `lp-geometry.json` に点を足す番。ほかに譜間距離・スラー/タイ・ビーム・臨時記号配置
- 原則: **snapshot を再ベースするたびに、LP 照合済みの点が増えているべき**

⚠️ **既知の穴**: 以下2つの LP 検証は**数値がコメントに残っているだけで、プローブが未 commit**
（scratchpad に置いたまま消える）。コーパスの「再実行可能」原則から外れているので、
次に触るとき `audit/lp-geometry/probes/` に移すこと。
- **stretch strength 0.45 の検証**（同じ音楽を 120mm / 180mm で justify し、force を解いて
  独立な spring で交差検証）→ 数値は
  `SpacingInvariantTests.BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance` に
- **符尾 Y extent のダンプ**（光学補正の 2×2 の裏取り）→ 数値は
  `SpacingRules.BarlineToNextNotesCorrection` の remarks に

### B. 座標系の LP 統一を完了させる（COORDINATE_AUDIT §4.6）

起票時の実バグ8件は全て対処済み。残るのは「数値は正だが frame 忠実性が未完」の3系統:

| | 内容 | 状況 |
|---|---|---|
| ① | 譜間/system 縦積みの Y-down 残存（**島1**） | 🔄 YFlip 配線と全 grob の Y-up 化は完了。残＝共有 device stacking の de-island（`OutsideStaffStacker` 等）＋ `system.Y`/`staff.Y` の Y-up 格納（W2） |
| ② | device 島群（**島2**） | ⏸ 繰延。TieVariant / 水平 skyline の Y horizon / TabStaffGeometry / beam collision island |
| ③ | non-musical PaperColumn の欠落 | 🔄 §2 ③ |

**X（③）と Y（①）は独立に進められる。** 島1 は boundary-shim で byte 不変移行できることが実証済。

### C. 未移植 LP 計算の取り込み

tuplet on-line / volta shorten / hairpin niente / ledger / brace / 開 chord / Ignatzek。
出典 `HANDOFF-lp-calc-incorporation.md`（§8）。**未検証の一覧なので、着手前に実コードで裏取り。**

### D. 言語・ツール側（X/Y 座標系とは独立）

いずれも**この一覧は伝聞。着手前に実コードで確認すること。**

- MusicXML インポート — ほぼ完遂、実ファイル検証が残
- AI 協調編集 M1–5（Ctrl+I / 譜面選択 / 補完 / BYO-key）— 実機 E2E 未検証
- 文法改善 5 件 — 糖衣 `c?` / `c!` 未実装。0.3.0 リリースは GO 待ち
- Dead-code 監査 — アナライザ検出分は完了、手動分が残
- `LILYPOND-REF` 行番号の一括再採番（cosmetic・繰延）
- `IDrawingContext.cs:37-39` の remark が装飾前後2フレームを記述していない（§4.4）

### E. 保守性の負債（このセッションで見つけたもの）

- `DrawingTransform.Identity` は `new()` なので **`ScaleX/ScaleY = 0`**、
  `Identity.IsIdentity` 自体が false。record struct はプライマリコンストラクタの既定値を
  適用しない。出荷 3 backend は無害だが、**記録用コンテキストの作者を2人捕まえている**。
  `Identity => new(0,0,1,1)` に直す価値あり（未実施・要判断）
- `SharedRendererBeamTests` と `LpFidelity/RecordingDocumentContext` に記録用コンテキストが
  **2実装ある**。統合は既存の通っているテストに触るので要判断

---

## 4. 知識の置き場所 ← **増殖防止の核心**

**引継ぎ文書が増えたのは、寿命の違うものを1つに混ぜたから。** 種類ごとに置き場所を決める。

| 知識の種類 | 置き場所 | 例 |
|---|---|---|
| **LP の幾何の実測値** | `audit/lp-geometry/`（プローブ＋台帳） | bar line → 符頭 = 0.900000 |
| **LP の式・定数の出典** | **コード内 `// LILYPOND-REF:`** | `staff-spacing.cc:213` の 0.3 |
| **LP の挙動で驚いたこと** | コード内コメント（数値つき）＋ user memory | 光学補正は clef でなく符尾 |
| **座標系の現状と残作業** | `docs/COORDINATE_AUDIT.md` | §4.5 の対処状況表 |
| **アーキテクチャの意図** | `docs/*.md`（既存の該当ファイル） | `SKYLINE_ARCHITECTURE.md` |
| **不変条件** | **テスト**（`SpacingInvariantTests` 等） | 両 spring 系の一致 |
| **現在地・次の一手・ロードマップ** | **このファイル §1–§3** | |
| **ユーザーの好み・作業規律** | user memory | 「done は push 済みで」 |

**ここに書かない**: LP の式の導出、実測値の生データ、アーキテクチャ解説、コードの説明。
それらは上表の置き場所へ。このファイルには**ポインタだけ**置く。

> 判断に迷ったら: 「これは**次のセッションだけ**必要か、**ずっと**必要か？」
> ずっと必要ならこのファイル以外へ。

---

## 5. 恒久ルール（滅多に変わらない）

### 5.1 ワークフロー規律

- **master 直コミット。ブランチを勝手に作らない**（作成・削除は GO 待ち）
- **1 島 / 1 関心 = 1 commit**。ただし**依存があるなら同時投入**し、message に
  「単独では入れられない」と書く（frame と定数のように、片方だけだと壊れるケース）
- **巨大ファイルを分割しない**
- **Co-Authored を付けない**。message に「何を・なぜ・**検証結果の数値**」＋
  **未完・残差・意図的に触らなかった点**を明記
- コミットは**関係ファイルのみ明示 `git add`**（無関係の `.py` / handoff を混ぜない）
- **push はユーザー。「done」は push 済みでのみ主張。ship = 全緑 ＋ 明示承認**
- **出力を変える変更はユーザー承認前に出荷しない。** snapshot 再ベースも
  **LP 照合 → 承認 → 実行**
- **シェルは pwsh MCP / ripple（bash 禁止）**。ファイル書き込みは Write ツール
  （`Set-Content` 直書き禁止。**PowerShell に heredoc は無い** — commit message は
  ファイルに書いて `git commit -F`）
- **「未使用に見える」≠「消してよい」。** 削除前に `.cs` 以外も横断 grep →`<see cref>` 確認
  → 削除後にヘルパが孤立しないか再 grep →**ユーザー承認**

### 5.2 LP 移植の原則

- レイアウト/描画は `C:\MyProj\lilypond-src` の `lily/*.cc` を**符号一致で字面移植**。
  関数名・変数名・符号・丸めまで揃える。**独自の近似・辻褄合わせを入れない**
- **移植したら必ず `// LILYPOND-REF: lily/xxx.cc:行` を付ける**（定数1つ、式1つでも）
- **座標系が揃っていなくて字面移植が難しいときは、勝手に変換して押し込まず報告する**
- **既存の移植を先に探す。**「未実装」でなく「書いてあるが呼ばれていない/引数が違う/
  frame が違う」ことが本当に多い
- **分岐は全部書く**（`space-alist` の型を無視して値だけ使う類の手抜きをしない）
- ⚠️ **doc / コメント / 過去の自分の結論を疑う。ただし「疑った結果」も裏取りする。**
  `LILYPOND-REF` が付いていても式が一致しているとは限らない
- ⚠️ **同名プロパティが grob ごとに別の値**を持つ（`stem-spacing-correction` は
  StaffSpacing 0.4 / NoteSpacing 0.5）。**単位も別**（staff-spacing.cc は staff-space、
  note-spacing.cc は staff position。どちらも /7 するので2倍ずれる）

### 5.3 測定の原則

- **推論せず測る。** 実測 → 予測との照合 → 一致しなければ**まず自分の当てはめを検算**
- **摂動法が強力**: `\override` で esw / padding を振り、係数1で追随するか不変かを見る。
  **全部ゼロにして残った定数**がハードコード値
- **測定 regime を混ぜない。** ragged-right（force 0）では spring の床、圧縮時は rod が
  binding する。**どちらで測ったか必ず記録する**
- **配置は「両側」を測る。** ある grob の位置は前後2つの間隙で決まる。
  さらに**同じ box の左右が同じ基準点か**を確かめる
- ★ **縦は staff refpoint 間で測る。system 原点間で測ると嘘の値が出る。**
  `staff-refpoint-extent` は system ごとに違う（小節番号を頭上に持つ system は原点が
  その分だけ上に伸びる）ので、**間隔が一様でも原点間距離はばらつく**。同じ LP ダンプが
  原点間で 11.528583 / 12.000000、staff refpoint 間で 12.000000 / 12.000000。
  前者が「LP は圧縮している」という誤った結論として引き継がれていた（§2⑧）
- ★ **残差の符号で原因を切り分ける。** あるグリフの**左右の残差が逆符号**なら
  **frame（基準点）の誤り**、**同符号**なら**定数の誤り**。定数が違えば両側とも同じ向きに
  ずれるが、基準点がずれていると片側が広がった分だけ反対側が狭まるため。
  行中 clef/key 変更でこれを使って診断した（`midmeasure.*` の4点。§2①）
- ★ **変更する前に測る。** 変更後に測ると「LP に近づいたか」を判定できない。
  着手前にコーパスへ点を足しておけば、**反証可能な予測**（この4点が揃って 0 に向かうはず）
  になり、外れたときに診断が違うと分かる
- **「悪化した」＝「変更が間違い」ではない。** 間違った定数が別の欠陥を隠している構図は実在する
- ⚠️ **SVG から精密測定をしない。** 座標は `F2`（`SvgGenerator.cs:229`）で2桁に丸められる。
  6桁の LP 値と比べるなら `LpFidelity/RecordingDocumentContext` を使う
- ⚠️ **紛らわしい数値に飛びつかない。** 6桁一致しないなら別物と疑う
  （残差 0.189365 を「bar line 幅 0.19」と誤認した実例あり）

### 5.4 テストの原則

- **実装の定数を実装自身と比べるテストは何も守っていない。**
  LP 由来の期待値を書き、なぜその値かを `LILYPOND-REF` で示す
- **テストが LP と食い違ったら、テストを実測に合わせる**（再ピン止めしない）
- **追加したテストが「修正前なら落ちる」ことを実証する**
- **1点狙い撃ちにせず掃く**（掃引テストは改行位置が動いても空振りしない）
- 増分再利用（F3）: **小節幅に影響する新要素は `MeasureContentKey` に必ず畳み込む**。
  「隣の小節の内容で決まる」量は intrinsic hash から復元できないので**明示的に**足す
- spring は 2 系統＋改行 gate の 3 箇所を**必ず一致**させる
  （`MeasureLayouter.CreateTimingSprings` / `SpacingRules.CreateSpringsForMeasure` /
  `SystemBreaker`）

### 5.5 環境の落とし穴

- **dotnet の増分ビルドが腐る** → 前後比較では `--no-incremental` でビルドして
  `dotnet run --no-build`。なお `dotnet test` は `--no-incremental` を受け付けない
- **LilyPond は Guile デッドロックする** → `cmd /c "... < NUL"` でデタッチ必須。
  終了コード 1 でもダンプは出ている
- **コンソールの文字化けに騙されない。** ファイル実体は正しいことが多い。Read で確認してから判断
- **fixture**: Lily# の `octave absolute` は LP より一段高い（**LP `c'` ↔ Lily# `c`**）。
  既定は相対オクターブ。mid-music の key/time/clef 変更はバックスラッシュ無し。
  空小節は `| |` ペア。part 名に予約語を避ける（`p` は dynamic）

---

## 6. コマンド集

```powershell
# ビルド（--no-incremental 必須）
dotnet build LilySharp.Core\LilySharp.Core.csproj --no-incremental -v m   # 0 warn/err 期待
dotnet build LilySharp.Tests\LilySharp.Tests.csproj --no-incremental -v q

# 全テスト
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build -v q 2>&1 | Select-String 'Passed!|Failed!|\[FAIL\]'

# LP 忠実度スコア
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build `
  --filter 'FullyQualifiedName~Corpus_ReportsTotalDivergence' --logger 'console;verbosity=detailed'

# LP 実測（プローブを LilyPond に通す。既定 exe は 2.26.0）
pwsh audit\lp-geometry\Measure-LilyPondGeometry.ps1        # X（小節線まわり）
pwsh audit\lp-geometry\Measure-LilyPondPageGeometry.ps1    # Y（ページ縦）

# フォント由来の生成物（フォント更新後は必ず両方。py -3.13 必須 — PATH の python は
# LilyPond 2.24.4 同梱で fontTools も pip も無い）
py -3.13 audit\scripts\Extract-EmmentalerGlyphs.py     # → Svg\EmmentalerGlyphs.Generated.cs
py -3.13 audit\scripts\Extract-EmmentalerMetrics.py    # → Svg\Layout\GlyphMetricsGenerated.cs
# どちらも「feta 名がフォントに無ければ exit 1」。差分が出たらフォントが変わった証拠

# snapshot 再ベース（LP 照合＋ユーザー承認の後のみ・フィルタを掛けない）
$env:LILYSHARP_UPDATE_SNAPSHOTS = "1"
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build -v q
Remove-Item Env:\LILYSHARP_UPDATE_SNAPSHOTS
"ENV NOW = [$($env:LILYSHARP_UPDATE_SNAPSHOTS)]"    # ← 空であることを必ず目視
# → env を消して再実行し全緑を確認 ＋ git status で「動いたのは意図した snapshot だけ」を確認

# 目視用 PNG / SVG
dotnet run --project LilySharp.Cli -- png --crop --scale 4.0 "NAME.lys" "out.png"
```

- fixtures = `LilySharp.Tests\Fixtures\{test,showcase}\*.lys`、
  snapshot は `Snapshots\<dir>__<name>.svg`
- snapshot テストは `SvgSnapshotTests.TestSamples()` の**明示 `yield return` リスト**
  （`.lys` を置くだけでは走らない）

---

## 7. セッション終了時チェックリスト

1. [ ] 全緑を確認（`Passed!` の数を §1 に書く）
2. [ ] **§1「現在地」を書き換える**（追記しない）— HEAD / ahead 数 / テスト数 / やったこと
3. [ ] ロードマップ（§2/§3）が動いたなら更新する。**完了した項目は消す**
4. [ ] このセッションで得た**恒久的な知識**を §4 の表に従って置き場所へ出す
      （LP 実測値 → `audit/lp-geometry/`、LP の式 → コード内 REF、座標系の状態 →
      `COORDINATE_AUDIT.md`）
5. [ ] **新しい `handoff-*.md` を作っていないことを確認**
6. [ ] `git status` で意図しないファイルが混ざっていないか確認
      （特に `audit/scripts/__pycache__/` — 生成器を走らせると必ず出る。commit しない）

---

## 8. 旧 handoff ファイルの棚卸し

root に **14 個の未追跡 `HANDOFF-*.md` / `handoff-*.md` / `REVIEW-HANDOFF.md`** が残っている
（`handoff-2026-07-21-mmr-runs.md` は回収完了ののち削除済み）。
各ファイルが「原則・手順」を丸ごと重複コピーしており、これが増殖の主因だった。
**本ファイルがそれらを置き換える。**

⚠️ ただし中身には未回収の知識が残っている可能性がある。**一括削除しないこと。**
以下は着手時に参照する価値がある順:

| ファイル | 参照価値 |
|---|---|
| `handoff-2026-07-21-x-frame-unification.md` | ✅ 内容は完了・`COORDINATE_AUDIT.md` §4.7 と本ファイルに吸収済。**光学補正を clef のせいとする誤記あり**（訂正済） |
| `handoff-2026-07-21-boundary-column.md` | §2③ 着手時に。LP 事実の記録として有用 |
| `HANDOFF-stage4-vertical-yup.md` | §3B 島1 着手時に。**Stage-4 の正確な現状はここ** |
| `HANDOFF-lp-calc-incorporation.md` | §3C 着手時に |
| `HANDOFF-dead-code-audit.md` | §3D 着手時に |
| `HANDOFF-2026-07-20-*.md`（5本） | 過去セッションの記録。LP 事実は概ね吸収済 |
| `HANDOFF-beam-quanter-unification.md` / `HANDOFF-coord-frame-unification.md` / `HANDOFF-layout-x-unification.md` | 完了済み作業の記録 |
| `REVIEW-HANDOFF.md` | 規律は §5.1 に吸収済 |

**回収方針**: 各テーマに着手するとき、そのファイルを読んで**必要な知識を §4 の置き場所へ移し、
読み終えたファイルを削除する**（削除は都度ユーザー承認）。一気にやらない。

なお `AI_POSITIONING_HANDOFF.md` と `docs/arpeggio-rework-handoff.md` は**追跡済み**で
別系統。本ファイルの対象外。
