# Lily# 開発ハンドオフ（常設・単一）

> **このファイルが唯一の引継ぎ先。新しい `handoff-*.md` を作らないこと。**
> 引継ぎは §1「現在地」を**書き換えて**行う（追記しない）。恒久的な知識は §4 の表に従って
> それぞれの置き場所へ出す。ここに溜め込むと、以前と同じように 16 個に分裂する。

最終更新: 2026-07-22 / master `500627c9` の次（§0 で裏取りすること）

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

### ▶ 次のセッションの最初の一手 ＝ **島1 の atomic flip（`StaffLayout.Y` を Y-up 格納へ）**

**⚠️ これは「専用セッションで一気に完遂する」と決めてある作業。**
`HANDOFF-stage4-vertical-yup.md` §3.5 の「途中停止＝全崩れ。半端なら revert して緑で締める／
fresh context の専用セッションで一気に」がそのまま当てはまる。**この項目から着手すること。**
文脈を他の作業で消費してから始めないこと（前セッションはそれを理由に着手を見送った）。

**地図・変換レシピ・消費側の一覧・落とし穴は §3B の「島1 の再スコープ」に全部ある。**
実測済みなので調査からやり直す必要は無い（ただし着手時に実コードで再確認＝§5.2）。

その次: **PageBreaker が鎖と食い違っている**（§2⑧ の ⚠️）。「1ページに何本」は今も別モデル。

⚠️ **`DynamicEngraver` / `TupletBracketEngraver` が符尾長を無条件に仮定している**
（`DynamicEngraver.cs:345,359,416,424`）。下の「幻の符尾」と**同じ形**だが、
**台帳の点が 1 つも届かないので測っていない。推測で直さないこと。**
着手するなら**先に点を作る**（全音符に強弱を付けた 2 段譜）。短いので、①の前の
肩慣らしには向かない——①は fresh な文脈を丸ごと使う前提。

⚠️ **別ブランチが 1 本走っている**: `fix/note-bang-diagnostic`（ワークツリー
`C:\MyProj\LilySharp-wt2`、指示書は同ツリーの `scratch/TASK-note-bang-diagnostic.md`）。
`Parser`/`Syntax`/`Tests` しか触らない柵つきなので**①とは 1 ファイルも衝突しない**
（①は byte 不変なので snapshot も動かない）。master にマージするのはユーザー。

---

### ✅ このセッション（2026-07-22）でやったこと — **譜間を測れるようにして、2 つ閉じた**

| commit | 内容 |
|---|---|
| `b3cfb119` | **2 段譜の台帳点を 2 つ起票**（probe P/Q）。出力不変。**予測を先に書いた** |
| `89aaa29f` | **幻の符尾を止めた**。`+1.450000 → −0.050000`。snapshot 3 件 |
| `854a0e95` | **五線をインクで種にした**（±2.0 → `staffHeight/2 + StaffLineThickness/2`）。`−0.050000 → 0` ×2。snapshot 14 件 |
| `28048fd8` | §1 書き換え |
| `7f2f8ff8` | **`StaffOffsetInSystemDown` 10 箇所を全数調査**。本物の移行は 2 箇所だけと判明し、それを実施（byte 不変・snapshot 0 件）。残り 8 箇所は**意図的な device 境界**＝`Down` は消さないのが正解、と shim の remark に記録 |
| `500627c9` | **決定 2 件を記録**（糖衣 `c?`/`c??` 不採用＋記号ルール、島1 の再スコープ）。docs のみ |

**台帳 22/29 → 24/31 exact、total |residual| = 0.023777 ss（不変）。** 新しい 2 点が両方 exact。

#### ★ 教訓: **対で足したから 2 つ目の欠陥が出た**

P（上の譜から下向きに出っ張る）と Q（下の譜から上向きに出っ張る）は**同じ算術の鏡像**なので
LP はどちらも `9.595000` を返し、**残差も同じ値になるはずだった**。ならなかった
（−0.050000 と **+1.450000**）。その差が**幻の符尾**:

- `SkylineBuilder.AddNoteBoxToSkylines` が**全ての符頭に** 3.5 の符尾を生やしていた。
  **コメントは「全音符には符尾が無い」と書いてあるのに、コードに音価の判定が無かった。**
- 描画側は `noteValue >= 2` で正しく分岐している（`SharedRenderer.Noteheads.cs`）。
  だから**全音符は符尾なしで描かれ、符尾があるものとして間隔が取られていた**。
- LILYPOND-REF: `lily/stem.cc` `Stem::is_normal_stem`（duration-log >= 1 のみ）。

⚠️ **片側だけ足していたら、−0.050000 が「予測どおり」に見えて終わっていた。**
README §「片側しか測っていない点は…」の縦版。**両側を足す。**

#### ★ 2 段譜の点は「素の 2 段譜」では作れない（設計の肝・再掲用）

LP は隣接する譜を `max(skyline距離 + padding, minimum, basic)` に置く
（`align-interface.cc:228-238`、StaffGrouper の 9 / 7 / 1）。**両側とも五線なら
2.05 + 2.05 + 1 = 5.1 で basic 9 が勝ち、五線の extent は出力に一切現れない。**
だから probe は **binding する X で片側だけが突出物**である必要がある
（P = 高音部譜の `d` が中央線の 6 下＋符頭 0.545 ↔ 低音部譜は自分の中央線）。
`staff-refpoint-extent` は system 内の全 staff refpoint の区間（`lily/system.cc:705-717`）
なので、**2 段 1 system なら `StaffGap()` がそのまま譜間距離**。実装追加は不要だった。

#### 参考: 縦の残差 4 点（合計 0.001365 ss）は原因を測って確定済み

`probes/glyph-skyline.ly` で grob に stencil と skyline を同時に聞いた結果:

| grob | `ext`（stencil） | `vertical-skylines` | Lily# |
|---|---|---|---|
| Clef G | (−2.550 . 4.800) | **(−2.540 . 4.776)** | bbox の −2.550 を使用＝**0.010 深い** |
| NoteHead | (−0.545 . 0.545) | (−0.545 . 0.545) | 一致（`22120764` が桁まで閉じた理由） |
| StaffSymbol | (−2.05 . 2.05) | (−2.05 . 2.05) | ✅ 一致（`854a0e95` で線のインクへ） |

**LP はグリフの skyline を stencil の描画プリミティブから作る**（`stencil-integral.cc`）ので、
輪郭が bbox の角から離れるグリフ（clef）だけ skyline が浅くなる。符頭は輪郭が bbox に接する
ので一致する。⚠️ **閉じるには輪郭ベースの skyline 生成が要り、C# 側に OTF パーサは無い**
（`ec7a2254` が LILC を選んだのと同じ制約）。**実測値を定数で埋めない**こと。

#### ✅ `StaffSymbol` の ±2.05 は閉じた（`854a0e95`）— **保留した判断が正しかった**

前セッションで一度直して**出荷せず戻した**のは「14 fixture が動くのに台帳点が 1 つも動かない」
＝改善かどうかを誰も判定できないため。**先に点を作る**という判断が正しく、作ってみたら
**2 つ目の欠陥（幻の符尾）まで出た**（§1 冒頭）。前セッションの実測「14 件・構造保存・
Y は −0.05〜+0.15」は今回の実測（14 件・X 完全不動・Y +0.03〜+0.15 と −0.05 が 1 件）と一致。

定数ではなく**導出形**で入れた: `_staffHeight / 2 + StaffLineThickness / 2`（§5.2.1⑤）。
`SeedStaffSymbol`（譜間用）と `SeedSystemStaffSymbol`（ページ用）の**両方**。

#### ✅ clef をスカイラインに入れた（`90efec02`）— **本体は padding 4 倍の方だった**

LP の clef は staff の VerticalAxisGroup スカイラインに入る**内部 grob**（`axis-group-interface.cc:914-940`）で、
素のスコアでは**上下ともインクの極値**（refpoint から下 3.550・上 3.800）。Lily# は種にしていなかった。

⚠️ **ただし clef 単独では直らず、一度差し戻した**（`c83a0551`）。狙った 2 点は閉じるのに
`system.natural-distance` が **+1.110000** に後退したため。**真因は別の欠陥**だった:

**`LayoutEngine` の単一ページ経路が system 間 padding に `SystemSpacing * 0.5 = 4` を使っていた。
LP は 1**（`paper-defaults-init.ly:62-65`・`page-layout-problem.cc:625-632`）。**4 倍。**
スカイラインが薄いうちは ink 項が basic-distance 12 に届かず**見えなかった**。clef が入って
ink が 9.110000（clef の下 5.550 と次 system の小節番号 3.560 が同じ左端で向き合う）に達した
瞬間に `9.11 + 4` が binding して露見した（LP なら `9.11 + 1` で効かない）。
同経路を `PageLayouter` の鎖と同じ `max(basic, max(minimum, ink + padding))` に揃えて解決。

⚠️ **教訓: 「新しい種を入れたら縦が広がった」を種のせいにしない。** 種は既存の欠陥を
**可視化しただけ**だった。LP が同じ音楽で動かないなら、動いた側が間違っている。

#### ✅ 決着: 休符の実インク化は**やらなくてよい**（測って否定した）

`22120764` の続きとして「休符も名目 1.0 → `GetRestBBox`」を予定していたが、**実測で棄却**。
128分休符（グリフ下端は中央線の 3.05 下）を敷き詰めた probe と、素の音符の probe が
**LP で 1 ビット違わぬ同じ値**（11.716074 / 12.255229）を返した。休符は中央線に座るので
**縦インクが極値になることが無く**、スペーシングに効かない。箱が名目なのは事実だが**不活性**。

---

### ⚠️ このセッションで踏んだ測定の罠（同じ手を繰り返さないこと）

- **要約された数字から逆算しない。** 「残差 0.044994＝五線の線幅の半分」と一度結論したが、
  これは**丸めた gap から算術した私の誤り**。probe は元から**全 system の生データ**を出しており
  （要約しているのは測定スクリプトの方）、直読みすれば 3.550000000 だった。
- **stencil の extent と skyline は別物。** LP の spring の床が使うのは **skyline**
  （3.540〜3.545）で、stencil の extent（3.550）ではない。
  この2つを突き合わせて「未知の 0.005」を作り出していた。**どちらの量かを必ず言うこと。**
- **probe が何を測っているか確かめてから信じる。** 休符 probe は休符を測っておらず、
  中央線上の音符 probe は**下向き符尾**が clef を隠していた。`a`（中央線の1段下）で初めて
  clef が単離できた。**両方とも罠として probe のヘッダに書いてある。**

---

**origin より 60 ahead で未 push**（HEAD はこのコミット）。push はユーザー判断・コミットは可。
⚠️ **push は明示的に「まだしないで」と言われている**（2026-07-22）。解除まで push しないこと。
⚠️ **未 push にはフォント差し替えと紙面定数、snapshot 再ベース 8 回（186・192・2・2・80・82・3・14 件）が含まれる。**
別ブランチ `fix/vscode-extension` が `7291531a` から切られ、**master に取り込み済み**（VS Code 拡張作業・ユーザー）。
**テスト 0 failed / 3142 passed / 3 skipped。** Core build 0 warn / 0 err。
**LP 忠実度 24/31 exact, total |residual| = 0.023777 ss**（**2.26.0 基準**。X 22点＋Y 7点＋**譜間 2点**）。
**譜間 2 点は両方 exact**（`854a0e95`）。**縦 7 点の合計は 0.001365**（4 点が上記の skyline sliver、3 点は exact）。
X 3 点は `e38a76bf` から不変。
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
§2⑧ の余白4定数は `e38a76bf` で、Y 台帳の起票は `0c0d8f38` で、
`page.first-staff-refpoint` の −3.000000 は `b94487ad`＋`1dfb62d7` で閉じた。
**次の一手は 伸長 regime（LP book J）の点。**

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

### 直近セッション（2026-07-22 最終）でやったこと — **先頭 system・鎖・プレビューのフォント**

| commit | 内容 |
|---|---|
| `6ffbe7bd` | **伸長 regime を起票**（probe W ＝ V と同じ音楽を 150 小節）。着手前に予測を書いてから測定: `page.stretched.first-staff-refpoint` **−0.025482000（予測どおり）** / `system.stretched-distance` **+0.089206333**（予測は向きのみ的中） |
| `cfdf85b4` | **ページを LP の spring 鎖として解く**（`Page_layout_problem` ＋ `Simple_spacer` の字面移植）。**0.114688 → 0.004090**。転記誤り2件を同時に廃止（下記）。**snapshot 2 件再ベース** |
| `a7b96569` | 生 dump を直読みして**残差の原因の記述を訂正**（コード変更なし）。要約からの逆算が誤りだった |
| `22120764` | **縦スカイラインを符頭の実インク（LILC ±0.545）へ** — `ec7a2254` の縦版。**0.004090 → 0**、**縦は 5/5 exact**。**snapshot 80 件再ベース**（全件で行数・グリフ数一致＝構造保存、Y は −0.05〜+1.80） |
| `769529ed` | **clef がスカイラインに無いことを測れる probe S を起票**（出力不変）。予測を先に書いて桁まで的中。休符案は実測で棄却（上記） |
| `058ab13a` | `VerticalSkyline.Distance` の契約を単体テストで固定（**スカイライン算術は無罪**と確定） |
| `0b30f53d` | **座標系監査**を `COORDINATE_AUDIT.md` §2.1 へ。`staffMiddleY` が 9 ファイルで逆向きの 2 意味だった |
| `a8c75679` | **19 シンボルを `Down` 付きへ改名**（ユーザーが MSVS で実施）。純リネーム |
| `39da7084` | **Y-up shim を開設**（`StaffOffsetInSystemUp`）＋ `OutsideStaffStacker` を移設。**snapshot 0 件** |
| `511ab68c` | **`SkylineBuilder` を Y-up へ**（`ToSystemUp` が単なる加算に）。**snapshot 0 件** |
| `90efec02` | **clef を種に＋単一ページ経路の padding を LP の 1 へ**。0.162412 → **0.023777 ss**。**snapshot 82 件再ベース**（構造保存・縦のみ最大 −6.88） |
| `b94487ad` | **先頭 system の配置を refpoint フレームへ**（§2⑧ の順序①）。`CalculateFirstStaffRefpoint` を新設し、`CalculateFirstSystemY` を **halfStaff 変換の唯一の seam** に。3 呼び出し元とも spec を渡す。**出力不変・snapshot 0 件** |
| `1dfb62d7` | **先頭 system を top-system spring に載せた**（順序②）。`max(basic-distance, ink + padding)`。**`page.first-staff-refpoint` −3.000000 → 0**、22/25 exact・0.022412 ss。**snapshot 2 件再ベース**（`programmatic/hara-kiri{,-paged}`、全て +3.00） |
| `99baed0f` | **`Deploy-Lsp.ps1` が `media/` を配っていなかった**のを修正。プレビューの符頭が三角になっていた原因（下記） |

⚠️ **「全 snapshot が動く」という §2⑧ の予測は外れた。** `max()` は**五線上のインクが薄いときだけ**効くので、
**190 件の `.lys` snapshot は 1 バイトも動かず**、動いたのは programmatic 2 件だけだった。
これは「無害の証拠」ではなく**フィクスチャの穴**（先頭 system が疎なフィクスチャが1つも無い）。
台帳の点が LP に対して固定しているので優先度は低いが、**視覚フィクスチャは未追加**。

### ⚠️ プレビューの符頭が三角になるのはフォントの配り忘れ（`99baed0f` で解決）

`tools/Deploy-Lsp.ps1` は `server/` `syntaxes/` `out/` `package.json` だけを配り、
**`media/`（webview 自身の Emmentaler）を配っていなかった**。`070f1e21` で 2.26.0 に差し替えた後、
サーバは新コードポイントを出すのにプレビューは **2.24.4 の woff2 を掴んだまま**になり、
**115 定数中 73 個の割り当てズレ**がそのまま別グリフとして描かれていた（符頭が三角）。
**レイアウトのバグではない。** 実測: repo 52116 バイト `13D75317…` / 配布済み 50248 バイト `D207674F…`。
⚠️ 同じ症状を見たら**まずインストール済み拡張の `media/fonts/*.woff2` をハッシュで照合する**こと。
CLI の PNG は `LilySharp.Core\Fonts` を埋め込むので**正常に見える＝切り分けに使える**。

### その前のセッション（2026-07-22）でやったこと — **2.26.0 への移行**

| commit | 内容 |
|---|---|
| `a2ceb2f0` | **グリフ表を feta 名引きに**（`EmmentalerGlyphs.Generated.cs` ＋ `Extract-EmmentalerGlyphs.py`）。**出力不変・snapshot 0 件**。副産物で既存の誤りを 5 件発見して修正（下記） |
| `070f1e21` | **フォントと台帳を 2.26.0 へ**。otf 2 個＋派生 woff/woff2 4 個を差し替え、生成器2本を zlib＋名前引きに、弓記号の方向ペアを移植、`lp-geometry.json` を 2.26.0 実測へ、テスト7ファイルの旧 codepoint 直書き 31 箇所を定数参照へ。**snapshot 186 件再ベース** |
| `7291531a` | **3 ページのフィクスチャ `test/multi-page-vertical`**。§2⑧ が警告していた「複数ページを踏む fixture がゼロ」を埋めた |
| `e38a76bf` | **紙面 5 定数を LP の単位で読み直した**（§2⑧ 完了）。**snapshot 192 件再ベース** |
| `0c0d8f38` | **台帳を Y に開いた**（3 点）。`RenderedGeometry` を全ページ対応に。出力不変 |

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

#### ✅ Y の台帳を開いた（`0c0d8f38`）— 6 桁で 3 点

LP 2.26.0 の `page-vertical.ly` **book L**（短いスコア＋既定用紙＝そのページが最終ページで、
gap は伸長後でなく **spring の自然長**）に対して:

| 台帳キー | Lily# | LP | 残差 |
|---|---|---|---|
| `page.width` | 119.501575 | 119.501575 | **0** |
| `system.natural-distance` | 12.000000 | 12.000000 | **0** |
| `page.first-staff-refpoint` | 8.690551 | 11.690551 | **−3.000000** → **✅ 0**（`1dfb62d7`） |

⚠️ **gap が exact なのは形式ではなく実結果。** 旧記述は SVG 2 桁読みで「一致」としていたが、
それでは 12.00 と 12.004 を区別できない。6 桁で 12.000000 だった。

#### ✅ 先頭 system の −3.000000 は閉じた（`b94487ad` → `1dfb62d7`）— **順序が本体**

LP はインクが小さいとき先頭 system をインクで測らない: `top-system-spacing` は spring で、
インクはその**床**でしかなく（`page-layout-problem.cc:625-633` ＋ `spring.cc:156-159`）、
force 0 の spring は `max(min_distance, ideal_distance)`（`spring.cc:219-237`）。
つまり距離は **`max(basic-distance, ink + padding)`**。`TopSystem.BasicDistance` の 6 は
元から正しく、**読み手が居なかっただけ**だった。

**ただし字面移植の前にアンカー統一が要る**（2026-07-22 に②だけ入れて差し戻した実測）:

| | Lily# | LP |
|---|---|---|
| `firstY` / `SystemLayout.Y` が指すもの | **五線の最上線**（system 原点） | — |
| `top-system-spacing` が狙うもの | — | **staff refpoint＝中央線** |

**ずれは `halfStaff` = 2 ss ちょうど。単位も向きも同じで、アンカーだけが違う。**
①を飛ばして②だけ入れると *最上線* が LP の *refpoint* 値に着地し、残差は
**−3.000000 → +2.000000** で閉じない。実際にそうなることを実測して差し戻してある。

入れたもの: `LayoutUtilities.CalculateFirstStaffRefpoint`（refpoint フレームで距離を出す。
**header は アンカーでなく床に入る** — `page-layout-problem.cc:441-444`＋`:471-473`）と、
`CalculateFirstSystemY`（= refpoint − halfStaff。**halfStaff 変換はここ 1 箇所だけ**）。
呼び出しは `LayoutEngine`（先頭 seed と単一ページ経路）と `PageLayouter` の 3 箇所。

⚠️ Lily# の `upExtent` は**五線最上線より上のインク**（probe V では 0）で、LP の
`up_skyline.distance()` は **refpoint からのインク**（同 3.8）。**この 2 つを取り違えると
また閉じない。** 変換は `CalculateFirstStaffRefpoint` の中で `upExtent + halfStaff` として
1 箇所に閉じてある。

⚠️ **未了**: LP の top spring は**ページ justify で伸びる**（`set_default_stretch_strength
= ideal_distance`、`spring.cc:213-216`）が、Lily# は先頭 system を固定して system 間 spring だけ
伸ばす。force 0 では見えないので台帳は緑。**伸長 regime に着手するとき最初に触る乖離。**

#### ⚠️ 意図的な乖離: 単一ページは紙面サイズにならない

`page.height` を台帳に入れようとして落とした。**Lily# は 1 ページに収まるスコアを内容サイズの
ページで出し、溢れて初めて紙面に切り替える**（`LayoutEngine.cs:606-611`、明示的な設計）。
LP は常に紙面に組む。この probe では **−109.468268**。実在し理解もできているが閉じる予定が無く、
台帳に載せると `total |residual|` が ~109.5 になり指標が壊れるのでここに書く。
**ページ分割される経路は紙面をそのまま使う**（`test/multi-page-vertical` は 3 × 169.009370）。

#### ✅ 伸長 regime も鎖として移植した（`6ffbe7bd` → `cfdf85b4`）

**LP はページごとに `[top spring][system pair ×N][last-bottom spring]` を1本の鎖にして
`Simple_spacer` で `page_height_` に対して解く**（`page-layout-problem.cc:406-545`・`:780-804`）。
だから**ページ上の全 spring が同じ force を持つ**＝ top も bottom も一緒に伸びる。
Lily# は鎖を持たず、先頭を固定し、足に spring を置かず、**最終 system のインクが下余白に
届くまで**system 間だけ伸ばしていた。solver は既存の `SpringSolver`（`Simple_spacer` 移植済）で足りた。

**同時に廃止した転記誤り2件**:

| 誤り | LP の実際 |
|---|---|
| `TopSystem.Stretchability = 0` | `paper-defaults-init.ly:78-80` に **stretchability キーは無い**＝`set_default_strength` で inverse stretch は **ideal（6）**（`spring.cc:213-216`）。「0」を「未指定」と読むようにした |
| `InverseHooke = max(0.1, Stretchability / 60)` | **`/60` も下限 0.1 も LP に無い。** stretchability をそのまま使う |

⚠️ **compress strength は spec の minimum-distance から取る**（`ensure_min_distance` は
最小値を上げるが**強度を張り直さない** — `spring.cc:156-159`）。上げた後の min を渡すと
全 blocking force が静かに変わる。

⚠️ **PageBreaker は鎖と食い違ったまま**: 「1ページに何本入るか」は今も `SystemDetails` と
`constrained-breaking.cc` 系の `spring_length`/`tallness` が決めており、**top spring と
last-bottom spring を知らない**。committed フィクスチャでは本数が偶然一致していて誰も落ちない。
加えて `PageLayouter` は `systemDetails[0]` を **`vs.SystemSystem`** から作る（配置側は `vs.TopSystem`）。

⚠️ **`LayoutEngine` の単一ページ経路は今も自前で積んでいる**（force 0 なので鎖と一致するが**二重実装**）。

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
**`1dfb62d7` で読み手が付いた**ので、もう死んでいない（`CalculateFirstStaffRefpoint` が読む）。
⚠️ ただし `PageLayouter` は **systemDetails の `i == 0` でまだ `vs.SystemSystem` を使う**（`:101-104`）。
配置側は `vs.TopSystem` なので**ブレーカーと配置で spec が食い違っている**。
ページあたりの本数見積りにしか効かないが、**伸長 regime に着手するとき最初に確認すること。**

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

**現状 24/31 exact, total |residual| = 0.023777 ss**（`audit/lp-geometry/`・**LP 2.26.0 基準**）。
**X 22 点は 19 exact / 0.022412、Y 7 点は 3 exact / 0.001365、譜間 2 点は 2 exact / 0**
（`0c0d8f38` で Y に開き、`1dfb62d7` で自然長、`cfdf85b4` で伸長、`22120764` でインク、
`90efec02` で clef、`b3cfb119`＋`854a0e95` で譜間を閉じた）。
**Y の残り 4 点は同一原因**（clef の LILC bbox 3.550 vs LP の skyline 3.540〜3.545）。
X 3 点のうち 2 点は Lily# に無いパイプライン（水平スカイライン／テキストレイアウト）が要る。

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

⚠️ **未測定の疑い（2026-07-22 記録）**: `DynamicEngraver`（`:345,359,416,424`）と
`TupletBracketEngraver`（`:573,585`）が `DefaultStemLength` を**音価によらず**足している。
`89aaa29f` で潰した `SkylineBuilder` の幻の符尾と**同じ形**だが、**台帳の点が 1 つも届かない**。
LP は `Stem::is_normal_stem`（duration-log >= 1）でしか符尾を持たないので、全音符に
強弱記号やタプレット括弧が付く形は乖離している**はず**——**推測で直さないこと。先に点を作る**
（全音符＋強弱の 2 段譜。probe P/Q と同じ作り方で `staff.staff.*` に足せる）。

### B. 座標系の LP 統一を完了させる（COORDINATE_AUDIT §4.6）

起票時の実バグ8件は全て対処済み。残るのは「数値は正だが frame 忠実性が未完」の3系統:

| | 内容 | 状況 |
|---|---|---|
| ① | 譜間/system 縦積みの Y-down 残存（**島1**） | 🔄 **残るは `StaffLayout.Y` の格納だけ**（下記で再スコープ） |
| ② | device 島群（**島2**） | ⏸ 繰延。TieVariant / 水平 skyline の Y horizon / TabStaffGeometry / beam collision island |
| ③ | non-musical PaperColumn の欠落 | 🔄 §2 ③ |

**X（③）と Y（①）は独立に進められる。** 島1 は boundary-shim で byte 不変移行できることが実証済。

#### 島1 の再スコープ（2026-07-22・実コードで裏取り）

⚠️ **旧記述「残＝共有 device stacking の de-island（`OutsideStaffStacker` 等）＋
`system.Y`/`staff.Y` の Y-up 格納」は 3 分の 2 が既に済んでいた。**

- `StaffFrame` は**参照ゼロ**（削除済み）
- `OutsideStaffStacker` は `StaffOffsetInSystemUp` へ移行済み（`39da7084`）
- `system.Y` は page Y-up 格納済み（`477c5452`）

**`StaffOffsetInSystemDown` の残り 10 箇所も、実は移行対象ではなかった。** 全件を調べた結果
（`7f2f8ff8`）、本物の移行は **`LayoutEngine` の Y-up スカイライン 2 パスだけ**で、これは
同コミットで完了。残る 8 箇所は**意図的に device な計算の境界**（`TabStaffGeometry` の
タブ/弧の幾何 2 件・スラー scorer・TieVariant scorer・ページング extent パス・`SkylineDrop`
の床・ledger span と MMR の格納 device Y）で、`HANDOFF-stage4-vertical-yup.md` §0 が
「内部アルゴリズムの自然な device frame＝共有 chokepoint 不要」と明記している通り。
**device 島には端で反射が要り、この accessor がその反射そのもの**なので、`Down` は残すのが正解。

**したがって島1に残っているのは `StaffLayout.Y` の Y-up 格納 1 点のみ。**
⚠️ ただしこれは**単独では終わらない**——同じフレームを `StaffGroupLayout.Y` と
`GrandStaffLayout.BraceTop/BraceBottom` が共有している。**着手するなら専用セッションを 1 本
これに当てること**（`HANDOFF-stage4-vertical-yup.md` §3.5：途中停止＝全崩れ）。

#### 島1 atomic flip の作業マップ（2026-07-22 実測。着手時に実コードで再確認すること）

**スコープ**

| 対象 | 数 | 中身 |
|---|---|---|
| ディスパッチャ | 3 | `LayoutStaffGroups` の 3 系統（素 `:273` ／ hara-kiri `:343` ／ skyline `:1269`） |
| グループ構築 | 9 | grand/bracket/single × 3 系統（`:504,:537,:568` `:606,:652,:696` `:1344,:1391,:1438`） |
| 消費側 | 15 | `SharedRenderer.Connectors` 7（`:101,:115,:158,:162,:163,:179,:202,:203,:226,:227` のうち）／`SharedRenderer` 2（`:542,:543`）／`LayoutEngine` 3（`:348,:856,:1351`）／`MusicMarkEngraver:238`／`OutsideStaffStacker:128`／`PedalEngraver:180` |
| テスト | 8 | `BraceCollapseTests`（`:88,:114,:144,:202,:206`）／`SystemStartDelimiterTests`（`:175,:178,:204`） |

**変換レシピ**（各構築メソッドで 3 箇所）

```
currentY += staffHeight + gap;              →  currentY -= staffHeight + gap;
double totalHeight = currentY + staffHeight - y;  →  double totalHeight = y - currentY + staffHeight;
BraceBottom: y + totalHeight                →  BraceBottom: y - totalHeight
```

ディスパッチャ側は `currentY += layout.Height` / `+= interGroupGap` /
`+= NoteBoundLyricExtraGap(...)` をすべて `-=` へ。

★ **これは近似ではなく字面移植**: LP の `Align_interface` は
`where += stacking_dir * dy`（`align-interface.cc:274`）で `stacking_dir = DOWN = -1`、
つまり**LP のアキュムレータは元から負に歩いている**。`translates` もそのまま格納される。

**型と派生プロパティ**

- `StaffLayout.Y` / `StaffGroupLayout.Y` / `BraceTop` / `BraceBottom` が Y-up（下ほど小さい・
  先頭 staff が 0）。`Height` は**長さなので正のまま**
- `GrandStaffLayout.TotalHeight => BraceBottom - BraceTop` → **`BraceTop - BraceBottom`**
- 譜の下端は `Y + Height` → **`Y - Height`**（`LayoutEngine:856` `MusicMark:238` `Pedal:180`）
- `OrderBy(s => s.Y)`（`Connectors:158,:179`）は**降順**へ
- `LayoutUtilities`: `StaffOffsetInSystemUp` が素の `staff.Y`、`Down` が `-staff.Y`、
  **`FindStaffYInSystem` が引き算から足し算になる**（`system.Y + staff.Y`）＝これが本来の狙い

**やらないこと**

`StaffOffsetInSystemDown` の残り 8 呼び出しは**移行しない**（上記のとおり意図的な device 境界。
`Down` は消さない）。詳細は `LayoutUtilities.StaffOffsetInSystemUp` の remark に書いてある。

⚠️ **byte 不変だけでは足りない。** 生産側と消費側が**打ち消し合う符号ミス**は出力を変えないので、
オラクルをすり抜ける。**フレームを直接固定する単体テストを同時に足すこと**——2 段譜で
`staves[0].Y == 0` かつ `staves[1].Y < 0`、および `BraceTop > BraceBottom`。
これが無いと「緑だがフレームが逆」で着地しうる。

### C. 未移植 LP 計算の取り込み

tuplet on-line / volta shorten / hairpin niente / ledger / brace / 開 chord / Ignatzek。
出典 `HANDOFF-lp-calc-incorporation.md`（§8）。**未検証の一覧なので、着手前に実コードで裏取り。**

### D. 言語・ツール側（X/Y 座標系とは独立）

いずれも**この一覧は伝聞。着手前に実コードで確認すること。**

- MusicXML インポート — ほぼ完遂、実ファイル検証が残
- AI 協調編集 M1–5（Ctrl+I / 譜面選択 / 補完 / BYO-key）— 実機 E2E 未検証
- 文法改善 5 件 — **完了。糖衣は入れないと決定した**（2026-07-22、下記）。0.3.0 リリースは GO 待ち
- **`note!` 密着の診断 — 別ブランチで進行中**（`fix/note-bang-diagnostic`／ワークツリー
  `C:\MyProj\LilySharp-wt2`）。指示書は同ワークツリーの
  `scratch/TASK-note-bang-diagnostic.md`（`scratch/` は gitignore なので master に混ざらない）
- Dead-code 監査 — アナライザ検出分は完了、手動分が残
- `LILYPOND-REF` 行番号の一括再採番（cosmetic・繰延）
- `IDrawingContext.cs:37-39` の remark が装飾前後2フレームを記述していない（§4.4）
- **対応の取れないスラーが無警告で消える** — `SlurDetector.cs:49-56` は開きスタックが
  空のまま `)` を読むと黙って捨て、開いたまま終わった `(` も捨てる。`(e c4 d)` のように
  `(` の前に音符が無い形（`MusicWalk.cs:103-108` の通り `(` は直前の音符に結び付く）は
  スラーが1本消えるのに診断が出ない。タイの LYS4007 に相当する警告を足す。
  LilyPond も unterminated slur を警告する。**未着手・別ブランチでやる**（2026-07-22 記録）
- **`editors/vscode/src/smartBrackets.ts` を `smartTyping.ts` に改名** — 角括弧
  （`<`/`>` の和音・アルペジオ）だけでなくスラー `(`/`)` とオクターブ記号 `'`/`,` も
  扱うようになり、名前が実態から離れた。`registerSmartBrackets` も同様。変更は
  `extension.ts` の import 1 箇所。**未着手・競合を避けて後で**（2026-07-22 記録）

#### ✅ 決定: 臨時記号の糖衣 `c?` / `c??` は**入れない**（2026-07-22・蒸し返さないこと）

⚠️ 旧記述は「糖衣 `c?` / `c!` 未実装」で、**却下済みと未着手を同じ「未実装」に潰していた**。
`c!` は検討して**撤回した**もの（`3e4188b`）で、未着手ではない。

| | 状態 |
|---|---|
| `c!`（LP の強制臨時記号） | **却下**。`!` は点線小節線トークン（`Lexer.cs:192`、LP `\bar "!"`）。密着判定は空白に意味を持たせ、`c4! d` と詰めた既存の点線小節線の意味を黙って変える |
| `c?`（LP の cautionary） | **不採用**。痛み自体は既に解消済み（`Parser.cs:70-72` が `@courtesy`/`@editorial` を案内する専用エラーを出す）。残りはキーストローク節約だけ |
| `c??`（`@editorial` の糖衣） | **不採用**。LP に無い記号の発明 |

`c?` を落とした決め手は**単独では `!` の罠を悪化させること**: LP の `?`/`!` は対なので、
片方だけ通すと「LP の書き方が効く」と学習させ、`c!` を試す導線をこちらから作る。そして
`c!` は今**黙って小節を割り**、LYS2006（「弱起なら partial を宣言しろ」）という見当違いの
助言しか返さない。さらに和音では LP に無い配置設計が要る（LP は `<cis? e g>` の**音符単位**で、
和音全体に付ける `<c e g>?` は存在しない）。

**→ 代わりに `note!` 密着の診断をやる**（上記・別ブランチ）。静かな誤動作を名指しの説明に
変える方が価値が高く、言語の表面積を増やさない。

★ **ここから出た線引き（今後の記号追加はこれで判断する）**:
> **記号（sigil）は LilyPond が既に記号で綴っているものにだけ使う。
> Lily# 固有のものは全部 `@name` で書く。**

この一本で、`?` を入れない理由・`!` を点線小節線のまま残す理由・将来「括弧なし強制」を
足すなら `@force`（記号ではない）にする理由が、すべて同じ根拠で説明できる。
なお**「強制」自体は既に可能**——`@courtesy` は規則上出ない臨時記号を強制的に出したうえで
括弧を付ける（`MeasureCollector.ItemFactory.cs:73-79`）。無いのは「括弧なしの強制」だけ。

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

### 5.2.1 発明を機械に見つけさせる（3 つの仕組み）

**このセッションで見つけた欠陥は 1 件の例外もなく「Lily# が発明した箇所」だった**
（padding 4 倍・`/60`・名目 1.0 の箱・鎖の欠落・clef の欠落）。
「LP の式を正しく移植したが合わなかった」は**ゼロ**。だから方針は正しい。
問題は**発明が何年も気づかれない**ことで、散文の警告では防げなかった。以下は機械が落とす形。

**① 出典の無い定数はテストが落とす** — `LpProvenanceTests`（`UnsourcedBaseline`）。
`EngravingDefaults` / `LayoutOptions` / `VerticalSpacingParameters` の数値定数は
**`LILYPOND-REF:`（LP の式の出典）か `LILYSHARP-OWN:`（Lily# 独自である理由）を必ず持つ**。
台帳と同じラチェットで、**下げるのは可・上げるのは不可**。
⚠️ **初期値 13 は実在の負債**。`NoteheadHeight` `NoteheadHalfHeight` `RestHeight` `RestWidth`
——**今回噛まれた名目の箱がそのまま並んでいる**。1 つ潰すたびに baseline を下げること。
⚠️ **`LILYPOND-REF` が付いていても式が一致しているとは限らない**（§5.2）。
このテストは「出典を書け」までしか強制できない。**REF の隣の式が別物だった実例が
`LayoutEngine` の padding 4 倍**（`page-layout-problem.cc:1070-1127` の真下にあった）。

**② 二重実装は「一致する」不変条件テストで縛る** — spring 2 系統＋改行 gate は既に §5.4 で
縛ってある。**縦も同じ扱いにする**。⚠️ **padding 4 倍のバグは「複製された側」（単一ページ経路）に
住んでいた。** 重複は美観ではなく**移植が半分しか当たらない場所**。新しい経路を足すときは
「既存の経路と同じ答えを出す」テストを同時に足すか、**そもそも足さず統合する**。

**③ 再ベースは台帳点とセットにする** — snapshot を再ベースするコミットは、
**その差分を正当化する台帳キーを message に名指しする**。名指せないなら、
**先に台帳点を作る**（`StaffSymbol` の ±2.05 を保留したのがこの適用例。§1）。
⚠️ 190 snapshot に対し台帳は 29 点。**snapshot は「前回の自分」との比較**なので、
再ベースのたびに誤りを承認する機会が生まれる。網は見た目より薄い。

**⑤ 実測値をコードに書かない。書くのは LP の式。** ★このセッションで指摘された癖
実測は**不具合を見つけるヒントと、移植の照合**にだけ使う。**コードに入るのは LP の
`lily/*.cc` / `scm/*.scm` の式**であって、LilyPond の出力から読んだ数値ではない。
- ⚠️ **判定法**: その定数を**LP のどの行から導いたか**を書けるか。書けないなら、それは
  実測を貼っただけ。`LILYSHARP-OWN:` を付けて**独自と明示する**か、式を探しに戻る。
- ⚠️ **導出形で書く**。`2.05` ではなく `staffHeight/2 + StaffLineThickness/2`。
  数値が同じでも、**前者は測定の写しで、後者は式**。フォントや設定が変われば差が出る。
- ⚠️ **既にある独自定数を別ファイルにコピーしない。** 実例: clef のスカイラインを入れたとき
  `ClefGlyphXOffset = 0.3` を `SkylineBuilder` に**書き足した**。0.3 は LP の量ではなく
  `DrawClef` の独自値で、**発明の家が 1 つから 2 つに増えた**。`EngravingDefaults` に
  1 つだけ置き `LILYSHARP-OWN:` を付けて解消済み。**`SystemSpacing * 0.5` が何年も
  生き延びたのと同じ形**。

**④ コーパスの穴を数える** — 台帳が薄いのは~~多段譜~~・**ページ跨ぎ・伸長**
（多段譜は `b3cfb119` で 2 点入った）。過去の最大級の欠陥 2 つ（padding 4 倍・clef 欠落）は
**長期間コーパスから見えず**、clef が padding を偶然あぶり出して初めて露見した。
**新しい点を足すときは、既存の点が測っていない regime を優先する。**
⚠️ **2026-07-22 に同じことがもう一度起きた**: 多段譜の regime を開いた瞬間、
狙っていた欠陥（五線 0.05）と**一緒に、狙っていなかった欠陥（幻の符尾 1.45）が落ちた**。
**穴を開けると、そこに何が溜まっていたかは開けるまで分からない。**

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
6. [ ] **snapshot を再ベースしたなら、それを正当化する台帳キーを message に名指したか**（§5.2.1③）
7. [ ] **定数を足したなら `LILYPOND-REF:` か `LILYSHARP-OWN:` を付けたか**
      （`LpProvenanceTests` が落ちる。baseline を上げて通すのは禁止・§5.2.1①）
8. [ ] `git status` で意図しないファイルが混ざっていないか確認
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
