# Lily# 開発ハンドオフ（常設・単一）

> **このファイルが唯一の引継ぎ先。新しい `handoff-*.md` を作らないこと。**
> 引継ぎは §1「現在地」を**書き換えて**行う（追記しない）。恒久的な知識は §4 の表に従って
> それぞれの置き場所へ出す。ここに溜め込むと、以前と同じように 16 個に分裂する。
>
> **置くのは「次に手を動かすために要るもの」だけ。** セッションの記録・閉じた欠陥の経緯は
> `HANDOFF-ARCHIVE.md`（逐語・2026-07-24 までの §1〜§3）へ出した。読むのは
> **同じ regime にもう一度触るとき**だけでよい。個別事例は原則に汎化して §5 に置く。
>
> ⚠️ **§4〜§8 の見出し番号はコード内コメント（`§5.2 違反`・`§5.2.1④` 等）から参照されている。
> 振り直さないこと。**

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

最終更新 2026-07-26（第11セッション・**多段譜の loose 鎖＋群なし譜間隔**）/ HEAD は §0 で確認すること
（⚠️ 自己参照。`a666b476`・origin より **89 ahead・未 push**）。
**3362 passed / 0 failed / 3 skipped**・Core 0 warn 0 err・
**LP 忠実度 98/119 exact・total |residual| 0.640858 ss / 110 distances・counts 9/9**。
snapshot 再ベースは **36 枚**（台帳キーを message に名指し済＝§5.2.1③・**GO 取得済**）。

⚠️ **total を前回の 0.212348 と比べない**（点集合が 106→110）。比べるなら点ごと。
新規 4 点の内訳は **+1.500000 ×2 →（同セッション中に移植して）0**・
移植後の残り **+0.271310**・単一譜の双子と同じ font 残差 **+0.157200**。
**従来の非ゼロ 19 点はこの回も 1 つも動いていない。**

### このセッションでやったこと（詳細は各 commit・台帳の `why`・probe ヘッダへ）

| commit | |
|---|---|
| `eac8d8f2` 台帳点 5 つを起票（book LYRMV＝2譜＋2verse） | **コード変更ゼロ・出力不変** |
| `90e47848` **鎖の room を refpoint フレームへ** | 多段譜も解く。snapshot 0 枚 |
| `c64ee958` 鎖の遠い端を**導出**に | コメントで主張していた不変条件を消す。byte 不変＝**証拠** |
| `a666b476` **`default-staff-staff-spacing` 移植** | 群なし 2 譜 10.5→9。**snapshot 36 枚**（GO 済） |

★ **LP 側の予測 3 件は 6 桁で当たり、Lily# 側の予測は丸ごと外れた**——外れたほうが収穫だった。
「force 0 の鎖は長いから system gap は 13.762110 になるはず」→ 実測 **12.157200＝単一譜の残差
そのもの**。⇒ **system gap は「予約」が決めていて「解」を見ていない。**
予約（`AlignmentMinimumBand`→`EstimateLooseLineExtents`）と解は前回の島で切り離されており、
予測はまだ両者が連動している前提だった。**欠陥を見る読みは staff→verse1 のほう**で、
それを**後から**起票した（`lyrics.two-staff.two-verse.staff-to-lyric`）。

★ **移植は台帳に先に書いた予測値ちょうどに着地**: 5.500000 → **4.009200**
（残差 +1.762110 → **+0.271310**）。0.271310 は**両者の歌詞書体差だけ**（up-extent
1.459200 対 LP 1.187880）で、**3.737890 に着地したらフォント量を fitting した証拠**。
だから台帳には 0 でなく **0.271310 を「移植の合格ライン」として先に**書いてある。

★ **snapshot 0 枚は「結果」であって「構成」ではない**（§5.2）: multi-staff＋歌詞の fixture は
`showcase/08-chorale` だけで、それは **1 verse**＝ばね 1 本の鎖は余裕があればどの force でも
ideal に座る。**コーパスはこの regime に届いていない。**守っているのは台帳点。

### ★ 穴を開けたら別の欠陥が落ち、**同じセッションで閉じた**（§5.0・今回も例外なし）

`lyrics.two-staff{,.two-verse}.staff-staff-inside` = **+1.500000 ×2 → 0**（`a666b476`）。
**LP の譜間 spec は 3 分岐で、Lily# は 2 つしか持っていなかった**（`axis-group-interface.cc:1008-1027`）:

| 上の譜の状態 | spec | basic |
|---|---|---|
| staff-grouper が**無い** | `default-staff-staff-spacing` | **9** ← Lily# に無かった |
| grouper 有・**下にまだ生きた譜** | `staff-staff-spacing` | 9 |
| grouper 有・**最下段** | `staffgroup-staff-spacing` | 10.5 |

- ★ ⚠️ **決めるのは「上の譜」で、ペアではない**（**推測は逆だった**）。`get_spacing_spec` は
  `before` から読む（`page-layout-problem.cc:1280-1281`）＝プロパティは**上の譜に付いて
  その下の間隔を表す**。`maybe_pure_within_group` は「**自分より下に**生きた spaceable 譜があるか」。
  ⇒ **PianoStaff の上に素の譜を置いてもその間は 9**（上の譜が grouper を持たないから）
- ⚠️ **ばねの強度は「宣言」でなく「算術」で一致する**: `default-staff-staff-spacing` に
  stretchability メンバが無いので `set_default_strength` が ideal 自身（9）を入れ、
  `staffgroup-staff-spacing` の明示 9 と一致する。**圧縮強度だけは別**
  （`ideal − minimum-distance` ＝ 1 対 2.5）で、**圧縮ページを測る点はまだ無い**
- ⚠️ **歌詞は無関係だった**——1 verse の LYRM でも同じ 1.500000。だから点が 2 つある
  （`barnumber.{low,high}` と同じ構成＝**不変性そのものが所見**）
- ⚠️ **コーパスが見えていなかった理由**: `page.natural.staff-staff-inside` は PianoStaff（群あり）、
  `lyrics.two-staff.staff-to-lyric` はアンカーのおかげで上の間隔に依存しない読み。
  **群なし 2 譜の内側を読む点が 1 つも無かった**
- ⚠️ **`grandstaff-*` という名前の fixture は GrandStaff ではない**（中身は素の `staff` 2 本）。
  36 枚が動いたのはそれで正しい。実測差分は**下段が 1.50 上がるだけ**で上段は不動

### ▶ 次の一手

1. **hara-kiri × 歌詞ブロックが未測定**（`c64ee958` の報告事項）。`BuildLooseChainEnds` は
   span を **system ごと**に読むようにしたが、**それを踏む fixture も台帳点も無い**
   （`hara-kiri.lys`/`ossia.lys`/`dashed-barline.lys` はどれも歌詞なし）。
   ⇒ **対の形は決まっている**: 上の譜だけ前半を空にして消し、下の譜に 2 verse を付ける。
   LP 側は**両 regime とも同じ 3.737890**（鎖の床）＝**恒等**になるはずで、
   Lily# が span を `systemsArray[0]` 相当で読んでいたら**譜間距離ちょうど**ずれる。
   ⚠️ `LyricBaselineBelowStaff` は譜を index で数えるので、**本数の点を必ず添える**（§5.0 罠 8）
2. **譜と譜のあいだの歌詞**（非最終グループの `with lyrics`）＝ loose 鎖の残り 1 枝。
   ⚠️ **room はもう問題ではない**——同じ refpoint 間 span で取れる（`:936-939`）。
   **違うのは閉じ方だけ**＝`min_offsets[k−1] − min_offsets[k]`・**null 行なし**（`:923-925`）、
   ばねは `nonstaff-unrelatedstaff-spacing`＋LARGE_STRETCH（`:1301-1312`）。
   その最小は**次の譜の up-skyline 対 最終 verse の descender**で、engraver に渡っていない入力。
3. **ossia / text ROW を持つ system は今も force 0**（`BuildLooseChainEnds` が null を返す）。
   LP はそれらを**鎖に入れる**が Lily# は独立した帯として置く（§3 の決定）＝ room の意味が
   食い違う。**「除外」ではなく「room が不明」**としてコードに明記済み。

⚠️ **frame 誤りの型は残る**（前セッションからの持ち越し・繰り返し出る）: **system スカイラインは
system 原点から／譜ごとのスカイラインはその譜の上端から**測られているので、アンカーへの
変換が 2 系統で違う（`skylineToAnchor`）。片方だけ直すと**譜間距離ちょうど**ずれる（実測 10.5）。

### その次の候補

- **`lyrics.two-verse.system-gap` の残り 0.157200 は機構ではなくフォント量**。内訳は
  **0.046334**＝Lily# が小節番号の baseline 上に取る 1.3 の cap-height と LP の実字高の差、
  残りは**歌詞面のインク**（Lily# の歌詞書体は LP より約 27% 大きい）。
  ⚠️ **spacing をいじって埋めない。** 埋めるなら書体メトリクスの側。
  同じ理由で `barnumber.*` の −0.024440 も**字形の下はみ出しが無いことによる床**で、
  閉じるには数字書体の ink-bottom メトリクスが要る（台帳の `why` に明記済）。
- **`LyricEngraver.VerseSkylines` が到達不能になった**（呼び手 2 つが今回の置換で消えた）。
  §5.1 の削除手順（横断 grep →承認）に従うので**この島では消していない**。次に触る人が処理する。
- **歌詞の up-extent も書体から読む（⚠️ CJK の受け皿が先）** — ⚠️ **1 つの音節に高さが 2 つある**
  （§5.2.1②）。下降部は書体のアウトライン実測（`TextFontMetrics`）、上昇部は文字クラス別の
  em 分数のまま（`AscenderEm`/`XHeightEm`/`CjkAscenderEm`）。⚠️ **一括で移すのは試して差し戻した**:
  TeX Gyre Schola に CJK グリフが無いので、かな音節のアウトラインは空＝up-extent が 0 になり
  `CjkAscenderEm` が存在する理由が消える（実測: snapshot 14 枚が動き
  `UpperStaffLyrics_DropByFontHeight_TallCjkClearsFurtherThanLatin` が落ちる）。
  ⇒ **先に CJK の受け皿**（CJK 書体を持つか、非 Latin は表に落とす分岐か）を決めること。
- **独立 lyrics 行の枝**（`LyricRowBaseline` = 2.6）は**まだ点が無い**。同型に見えるが別経路
  （§3 の意図的乖離）なので、**自分の probe を持たせる**こと。
- **ossia ペアは rigid のまま**（`gap * OssiaScale` は Lily# 独自）。**ossia 距離そのものの移植が先**。
- **§2A の残り**: 継続行 prefix をスコア全体で 1 つ・measure 0 で計算する ⇒ **mid-piece の
  key change 後の行は古い幅のまま**。構造的な解は **per-line prefix** で、`MeasureSpringData` に
  per-measure `LineStartSpring` の受け皿が既にある。
- §2D の残り（**字面から外れた 2 件・未移植 3 件**）・§2C（LP を instrument する必要があるもの）。
- §2H に残る発明（`MinItemGap` の歌詞 4 箇所・`ownFixedFloor`・`ChordNameEngraver` の
  `Math.Max(2.0, …)` 床＝`LILYSHARP-OWN` と明示済で**実際に効いている**）。

**非ゼロで残っている台帳点は 21 点**（従来の 19 点は不動・今回 +2。
`lyrics.two-staff{,.two-verse}.staff-staff-inside` は**同セッションで閉じた**ので
`lyrics.two-staff.staff-to-lyric`・新規の count 点とともにここには無い）:

| 点 | 残差 | 正体 |
|---|---|---|
| `lyrics.two-verse.system-gap` | **+0.157200** | ★ **フォント量に落ちた**（上記）。機構は移植済 |
| `lyrics.two-staff.two-verse.system-gap` | **+0.157200** | ★ **同じ量**。多段譜でも予約側は追加作業不要という control |
| `lyrics.two-staff.two-verse.staff-to-lyric` | **+0.271310** | ★ **移植済の残り＝歌詞書体差だけ**（up-extent 1.459200 対 1.187880）。⚠️ **0 にしてはいけない**——3.737890 へ寄せたらフォント量の fitting |
| `barnumber.{low,high}-melody.staff-to-baseline` | **−0.024440** ×2 | ★ **字形の床**（LP は数字のインク下端を置く／Lily# は baseline）。閉じるには書体メトリクス |
| clef sliver（`{page.stretched,page.clef}.first-staff-refpoint`・`system.clef-bounded-distance`） | 4e-5〜8.3e-4 | LP の実効 scale 未特定（§2C）。**LP を instrument するまで動かせない** |
| Pango 量子化の族（tuplet 4・tie/slur 6・強弱 1・`barline.next.down-stems-after-clef`） | 5e-6〜1.4e-3 | Lily# に無いテキスト metric＝**閉じる予定の無い名前付き残差**。⚠️ この分類は**伝聞で未再検証**（tie の 0.001391 が Pango で説明が付くかは未確認） |
| `system.stretched-distance` | −0.000414 | ★ **これは符頭**（単一譜 book W の束縛インクが符頭）。LP 0.550000 対 LILC 0.545000 で**未説明**＝フォント metric の問題。⚠️ **そこは埋めない**。⚠️ **`page.*`/`system.*two-staff` を同族と読まないこと**——あれは符尾で、第8セッション（`96641db7`）で閉じた |

**消えた容疑者**（再走査しない）: duration space・音符間の最小・行幅・demerits の 3 項・
DP の形・小節線・**自然幅そのもの**・**行分割そのもの**・**行頭 spring そのもの**・
**loose line 再配分の不在**（今回移植）。経緯は各コミットメッセージと `probes/*.ly` のヘッダに。

⚠️ **プローブの罠の実例は §5.0 の末尾へ。** 新しい対を作るときは必ずそこを見ること。

---

## 2. 開いている作業

> ⚠️ **`keep-inside-line` は入った**（`efb3ddfb`・`622e88b4`）。全列・左右両方の rod が
> `SpringSolver.ApplyRods`（＝`Simple_spacer::add_rod` の移植）へ流れている。
>
> rod の入力は**列の ink 全体**＝テキスト（**音節は中心合わせなので左右へ半幅ずつ／和音記号は
> `dcbf08e9` 以降 ink 左が列なので右へ全幅・左へゼロ**）＋**音楽の ink**
> （`SpacingRules.MusicalInkOverhangsPerColumn`。符頭は列から右へ全幅、臨時記号は左へ届く。
> どちらも esw 抜きの素の extent＝`col->extent` が取るもの）。⚠️ 一時期テキストだけだった
> （`622e88b4`）のを `f9b3c87e` で報告し、追い移植済み。**出力は動かない**が、それは
> 「満たされているから」であって「生成していないから」ではない——区別は
> `KeepInsideLineOverhangs_IncludeTheMusicalInkNotJustTheCentredText` が入力側で主張している。
>
> ⚠️ **`audit/{property,grob}_coverage.csv` は生成物で、いま大きく stale。**
> `pwsh audit\scripts\Build-GrobCoverage.ps1` を走らせると（**約 6.5 分**）
> `keep-inside-line` は `"0","Absent"` → `"4","Used"` に正しく反転するが、**同時に無関係な
> drift が 371 行**出る（Absent 329→280 / Used 124→168 ＝何セッション分もの溜まり）。
> **手編集しないこと**。再生成は**単独の commit** にする。

### A. 予約と描画・複数モデルの統一（▶ と同じ族）

LP には break-align モデルが **1 本**しか無い。Lily# に**同じ量を計算する場所が 2 つ以上ある**なら
それが次の欠陥の住所（§5.2.1②）。現在わかっている残り:

- ~~**prefix 幅の第3のモデル＝`MultiStaffScore.LeadingKey`**~~ — **閉じた**（`8d1368d2`）。
  3 経路とも `SystemBreaker.Gate{First,Continuation}PrefixWidth` の 1 モデル。詳細と**残した
  1 件**（継続行 prefix が measure 0 固定）は §1。⚠️ §1 に `SystemLayout.PrefixWidth` を
  **dead と誤記した訂正**もある（実際はトリルの継続セグメントが読む）。
- **break-align 描画 walk の純構造化** — `sharedKeyX`/`sharedTimeX` の手組み max ループを
  `SolvePrefixColumns` 消費へ。値は一致済（出力不変）だが、**予約側は score モデル＋measure 走査、
  描画側は `ResolveKeySignature`＋`GetSystemStartKeyChange` と key 解決経路が別**——
  **この解決経路の統一が本丸**で、片方だけ挿げ替えると多分岐で壊れる。急がず focused session で。
- **ossia 自身の key が全記譜譜より広い regime** — 幅 union には入れた（LP どおり scaled stencil）が
  corpus に fixture が無く**未測定**。踏む対を起票する価値はある。

### B. スカイライン／beam の未測定領域

いずれも**先に LP を dump して対で起票**（発明回避）。アーキ上の不利は無いと確認済み。

- ~~**同一譜 knee の実 ink seed**~~ — ⚠️ **測った。ページには届かない**（`system.knee-beam-notes`
  = 18.090000 exact・§1）。knee の stem は内向きで、帯も stem も符頭の間にある。
  **構造の乖離は残るが観測不能**で、点が guard になっている。
- **`BuildSystemSkylines` の全譜 union** — ⚠️ **測った。内側譜は届かない**（probe `IS3`/`IS3C`・
  §1）。「内側譜の ink が edge 譜の silhouette を突き抜ける」は**音高では起こらない**（詰め offset
  9 ss ＝ 約 2.5 オクターブ）。
- ~~**offset が minimum_translations か最終位置か**~~ — **閉じた**（`e467d51e`＋`c309b751`）。
  問いは元々成立していなかった（譜間ばねが無く minimum＝最終位置）。譜間ばねが入った今、
  **スカイラインは最小高で作ったまま**＝`page-layout-problem.cc:1080-1095` の
  `minimum_translations` に一致する。⚠️ **伸びた位置で作り直さないこと**（LP 自身の
  `:1070-1074` のコメントが「詰めたと仮定する」と言っている）。
- **cross-staff beam 機能そのもの** — `BeamMember.TargetStaffIndex` を立てる producer が皆無で
  `IsCrossStaff` は到達不能（`@cross` は描画側にしか流れない）。skyline 方針（＝LP は除外）は
  `72905813` でピン済み。**機能が届いてから** E2E の対を起票する。
- **mid-line clef change の origin** — 行頭 clef で閉じた origin ズレ（percussion）と同型の疑い。
  台帳点が無いので未着手。

### C. 保留＝先に LP を instrument する必要があるもの

- **clef の LILC-vs-skyline sliver（Y 4 点）** — LP の実効 scale が **0.004**、Skia 直読みは
  **0.003989**（0.27% 差）。差は LP 内部の `get_unscaled_indexed_char_dimensions × magnification`
  由来で **SKPath からは出せない**。フォントは byte 一致なのでフォント差ではない。
  ⚠️ **SKPath の値に定数を合わせるのは fitting＝§5.2 違反。** LP を instrument して
  `get_glyph_outline_bbox` / magnification を dump するのが先（payoff は小さい）。
- **スラーの `move_away_from_staffline` 未移植**（`slur-scoring.cc:640-658`）＝端点が五線の線上
  （±0.2）に落ちると 0.15ss 外へ弾く。既存の点では発火しない＝**端点が線に載る fixture を対で**。

### D. Y 軸（ページ縦）の残り

- ~~**譜間ばねがページの鎖に無い**~~ — **移植済**（`c309b751`）。**圧縮側も台帳点あり**
  （`8b7b2615`。`page.compressed.staff-staff-inside` ほか）。残る名前付き乖離は
  **ossia ペアが rigid**（§1 の候補）。~~**loose line 再配分の不在**~~ — **移植済**
  （`ce3be1af`＋`90e47848`）。⚠️ **譜数によらず「最後の spaceable 譜の下」の鎖は解く**
  ようになった（`90e47848`）。force 0 のまま残るのは**グループ間歌詞**と
  **ossia / text ROW を持つ system**＝§1 の ▶。歌詞行 1 本では **LP も動かさない**
  （`6faa4d5a` で実測）ので、効くのは **同じ譜間に loose line が 2 本以上**あるときだけ、
  という当時の読みは正しかった
- ~~**圧縮 regime は未実装**~~ — ⚠️ **この記述は stale だった**（2026-07-26 に実測で確認）。
  ページは両方向に solve しており、`page.compressed.staff-staff-inside` /
  `system.compressed-distance.two-staff`（book JSK）は **exact**。⚠️ **圧縮強度は伸長強度と別**
  （`ideal − minimum`。staff 2 / system 4 に対し伸長は 5 / 60）なので、**片方だけ緑の移植は
  もう片方で落ちる**——`8b7b2615` が実際にそれで移植の欠陥を捕まえている
- ~~**LP の top spring はページ justify で伸びる**が Lily# は先頭 system を固定~~ —
  ⚠️ **この記述は stale だった**（2026-07-26 に実コードで確認）。`PageLayouter.cs:290-294` が
  spring 0 として top spring を鎖に積んでおり、`page.stretched.first-staff-refpoint` は
  残差 **−0.000042**（＝符頭インク族。§1 の非ゼロ表）。**乖離ではない**
- **`PageLayouter` は systemDetails の `i == 0` で `vs.SystemSystem`、配置側は `vs.TopSystem`**＝
  ブレーカーと配置で spec が食い違う（本数見積りにしか効かない）
- **`LayoutEngine` の単一ページ経路が今も自前で積む**（force 0 なので鎖と一致するが二重実装）
- **Y コーパスの拡張**（`page.top-margin` / `page.bottom-margin` / `page.last-page-gap` 等）

#### ★ 譜間ばね移植（`c309b751`+`8b7b2615`）で**字面から外れた 2 件と未移植 3 件**

⚠️ **出力は正しいが LP の書き方ではない**＝§5.2 の「報告する」に該当。コード側にも同じ注記あり。

| | 現状 | 字面の姿 |
|---|---|---|
| ① **ばねの床の作り方** | `drawn − max(0, basic − alignmentMin)` と**逆算** | LP は `get_minimum_translations` が返す**ベクタの差**をそのまま使う（`:699-704`）。⇒ **`internal_get_minimum_translations` 相当（`translates[]` を返す）を配置とばねで共有する**のが本来。**構造変更＝独立した島**（配置は譜の上端フレーム・ばねは refpoint フレームで、今は refpoint 側の最小を誰も保持していない） |
| ② **フレーム変換の置き場所** | ばねを作る側で span を引く（`PageLayouter`） | LP は `build_system_skyline` 内で**スカイラインを raise**（`:1120-1126`）。⚠️ 移すと `SkylineBuilder` の**読み手が全部巻き込まれる**（`CalculateUpExtent`/`CalculateDownExtent`・paging パス）＝frame 移行なので、島1 の手順（格納値を主張するテスト→生産側を同時に→縁で 1 回だけ反射）に従うこと |

**未移植（`StaffSprings` の remarks に列挙済）**: ⑴ `alignment-distances`（`:706-717`＝
`line-break-system-details` 由来の手動指定でばねを**剛体**にする。**Lily# に言語表面が無い**ので
入れるなら文法から）⑵ 最初の spaceable 譜の loose line 用の床（`:667-670`）
⑶ `include_fixed_spacing` の第2制約（`align-interface.cc:240-267`）。⑵⑶ は
**loose line 再配分の不在と同根**なので、そちらと一緒に。

⚠️ **`StaffSpacingParameters.ApplyOverrides` の `alignment_distances` REF は誤りだった**
（2026-07-26 に削除）。実装は `\override StaffGrouper.staff-staff-spacing.*` で**別量**
＝§5.2.1① の「REF の隣が別の式」の 2 例目。**REF を見たら隣の式を読むこと。**

### E. 未移植の LP 計算・座標系の島2

- **未移植 LP 計算**: tuplet on-line / volta shorten / hairpin niente / ledger / brace / 開 chord /
  Ignatzek。出典 `HANDOFF-lp-calc-incorporation.md`（§8）。**伝聞なので着手前に実コードで裏取り。**
- **座標系の島2（device 島群）は繰延**: TieVariant / 水平 skyline の Y horizon / TabStaffGeometry /
  beam collision island。`StaffOffsetInSystemDown` の残り呼び出しは**意図的な device 境界＝消さない**。
  島1 が残した手順: ①格納を反転する前に格納値を主張するテストを書く ②生産側は全部同時に
  ③**device 島の縁では 1 回だけ反射する**（反射を島の内側へ押し込まない）。

### F. 言語・ツール側（X/Y とは独立・**一覧は伝聞。着手前に実コードで確認**）

- MusicXML インポート — ほぼ完遂、**実ファイル検証が残**
- AI 協調編集 M1–5 — **実機 E2E 未検証**
- 文法改善 5 件は完了。**0.3.0 リリースは GO 待ち**
- `override` の消費側は 4 つだけ（文法側は元から開いている）。⚠️ **値に小数リテラルが書けない**。
  ⚠️ **page 系（`paper-height`/`top-system-spacing`/`systems-per-page`）を `override` に載せない**——
  LP ではそれらは `\paper` 変数であって grob プロパティではない（コーパスはハーネス引数で解決済み）
- **chords 行 / lyrics 行が `PartReferenceFinder` に無い**（2026-07-26 実コードで再確認）— 
  `AllPartNameTokens`/`ReferenceTokens` の switch は `PartDeclaration`・`PartBlock`・
  `MidiPartRender`・`StaffRender`・`OssiaRender`・`TabRender` だけで、
  **`ChordRowRenderSyntax` と `LyricsRowRenderSyntax` が無い**⇒ その part 参照は検証も改名も
  されない。⚠️ `staff … with chords NAME` の NAME は**意図的に除外**（別種の名前）なので
  そちらと混同しないこと。足すと「未定義の chord/lyrics part を参照するスコアを新たに弾く」
  挙動変更になる＝**要判断**
- ~~**対応の取れないスラーが無警告で消える**~~ — **完了**（**LYS4010**・ユーザー判断で master 直）。
  ペアリング規則は**レンダラのものを読む**（`SlurPairingScanner` が collector の副作用として記録し
  `SlurPairingValidator` が出す＝タイ LYS4007 と同じ形）。描かれる結果と食い違う警告を出さないため、
  規則を再実装していない。既存 208 ファイル（samples＋fixtures）で**誤爆ゼロ**を確認済み
- ~~`smartBrackets.ts` → `smartTyping.ts` 改名~~ — **完了**（`registerSmartTyping`・ログ接頭辞も。
  `out/` は未追跡の生成物なので触っていない）
- ~~`IDrawingContext` の remark~~ — **完了**（2フレーム＋「誰が flip のどちら側か」を明記）
- Dead-code 監査の手動分 / `LILYPOND-REF` 行番号の一括再採番（cosmetic・**島2 に紐づく繰延**＝
  `COORDINATE_AUDIT.md` §4.5 の島2 行。単独でやると差分が巨大なわりに何も守らない）

### G. 保守性の負債・未 commit のプローブ

- `DrawingTransform.Identity` は `new()` なので **`ScaleX/ScaleY = 0`**（record struct はプライマリ
  コンストラクタの既定値を適用しない）。出荷 3 backend は無害だが記録用コンテキストの作者を
  2 人捕まえた。`Identity => new(0,0,1,1)` に直す価値あり（要判断）
- 記録用コンテキストが **2 実装**（`SharedRendererBeamTests` と `LpFidelity/RecordingDocumentContext`）
- `GlyphMetrics.RestMaximaWidth = 1.8` が**手動値**。フォントメトリクスなので、生成器が `rests.M3` を
  出すようになったら `GlyphMetricsGenerated.cs` へ
- `SystemBreaker.BreakIntoSystemsGreedy` は **MMR run 非対応**。ただし `UseOptimalLineBreaking` が
  既定 `true` なので**既定出力に影響しない**うえ、greedy は LP のアルゴリズムでもない
  （LP＝`constrained-breaking.cc`＝optimal）＝**忠実度は上がらない**。優先度低
- ⚠️ **LP 検証の数値がコメントにだけ残り、プローブが未 commit** の 2 件（コーパスの「再実行可能」
  原則から外れている。次に触るとき `audit/lp-geometry/probes/` へ移す）:
  **stretch strength 0.45 の検証**（数値は `SpacingInvariantTests.BarlineToFirstNoteSpring_…` に）と
  **符尾 Y extent のダンプ**（数値は `SpacingRules.BarlineToNextNotesCorrection` の remarks に）

### H. 音符間 spacing に残る発明 ← **音符間そのものは 2026-07-25 に片付いた**

~~`GlyphMetrics.MinItemGap = 0.4`（音符間）~~ — **移植完了**。LP の 3 段（①箱に esw
`separation-item.cc:166-179` ②spring 最小＝縦 padding 0.08 込みの padding-free 距離
`note-spacing.cc:78-83` ③rod＝**縦 padding 無し**の距離＋spanner の padding 0.1
`separation-item.cc:47-68` ＋ `spacing-spanner.cc:315-316`）に置換。`compressed.note-to-note.quarter`
が **1.604200 で exact**。`SeparatingPaddingTests` は LP 由来の期待値に書き直し済みで、
「`MinItemGap` を何に設定しても音符間が動かない」ことを主張するテストを追加＝**戻ってこない**。

⚠️ このとき **§2H の旧記述は 2 つとも外れていた**ので、同じ推論を繰り返さないこと:

- 「Lily# の最小は **0.2 広い**」→ 圧縮域では **0.2521 狭く見えた**（加線の混入）。実際は
  rod で **+0.1** ちょうど。**加線のない音高で測ること**
- 「snapshot 24 枚が動くのに台帳は 1 点も動かない」→ **鍵になる点が無かっただけ**。
  圧縮 regime の点（`compressed.note-to-note.quarter`）を開いたら正当化できた。
  ⇒ **鍵が無いのは「移植できない」ではなく「まだ測っていない」**

**残っている発明**:

- **`LyricSpacing` の `MinItemGap` 4 箇所**（歌詞 extent＝**横**）。⚠️ **音符間と同じ発明だと
  決めつけない。** Lily# の歌詞モデルは LP と違い（音符に束縛され、**小節線で区切る**）、
  LP に対応物が無い可能性がある＝**必要な独自量かもしれない**。どちらかを確かめてから触ること。
  ⚠️ **縦の基本距離は 2026-07-26 に発明と確定し、移植して片付いた**（旧 `StaffPadding = 2.5` →
  `LyricParameters.RelatedStaffBasicDistance = 5.5`・`2b901484`）。
  **横も同じとは限らないので、この結論を横へ流用しないこと**
- **行頭 wish の `ownFixedFloor` ガード**（`LineStartSpringForLine` → `LineStartColumn.LineStartSpring`）
  — LP は leading grace と lyrics を**独立した paper column** にするので min_dist がそこまで測る。
  Lily# は spring に畳み込んでいる＝**「今の構造では表現できないから畳み込む」型**（§5.2 が
  名指す形）。本来の移植は **paper column 表現の導入**で、実測: 外すと snapshot 21 枚が動く
- ~~**中心合わせされた 2 つの text grob**~~ — **両方とも片付いた**（和音記号 `dcbf08e9`・
  音節 `98672c3a`）。⚠️ ただし `ChordNameEngraver` の `Math.Max(2.0, …)` 幅の床は**残っている**
  （`LILYSHARP-OWN` と明示済・1 文字の "C" 1.877882 を上書きするので**実際に効く**）
- ⚠️ **`KnuthPlassBreaker` は `LpProvenanceTests` の監視範囲外**＝§5.2.1① の網の穴。
  `OverfullPenalty` の誤った `LILYPOND-REF` が何年も生き延びたのはそのため

---

## 3. 決定済み ← **蒸し返さない**

| 決定 | 根拠（要点） |
|---|---|
| **`SystemBreaker` の再入可能化は入れない** | LP はページブレーカーが行分割を選ぶ（`optimal-page-breaking.cc:139-173`）が、入れると F3 の tier-1 skip の健全性論拠が壊れる（break 解が縦の関数になり、gate を計算するのに gate が守る結果が要る＝循環）。⚠️ **判断し直すなら順序は「①まず頻度を測る（コード変更ゼロ）→ ②有意ならオプション分離＋一致不変条件テストとセットで」**。性能が理由ではない |
| **臨時記号の糖衣 `c?` / `c!` / `c??` は入れない** | `!` は点線小節線トークン。`c?` 単独では `!` の罠への導線を作る。痛みは `@courtesy`/`@editorial` の専用エラーで解消済み |
| ★ **記号（sigil）は LP が既に記号で綴っているものにだけ使う。Lily# 固有は全部 `@name`** | 上の決定から出た一般則。今後の記号追加はこれで判断する |
| **休符の実インク化はやらない** | 実測で棄却。休符は中央線に座るので縦インクが極値にならず、LP でも 1 ビット違わない＝箱が名目なのは事実だが**不活性** |
| **単一ページは紙面サイズにしない**（意図的乖離） | Lily# は 1 ページに収まるスコアを内容サイズで出す（明示的な設計）。台帳に載せると total が ~109 になり指標が壊れるので**載せない** |
| **本数（count）の点は ss の総和に入れない** | 距離ではないから（`unit` フィールドで分離） |
| **LP の「正」は 2.26.0** | 版で PUA コードポイントも Emmentaler も動く。**必ず feta 名で引く** |
| **cross-staff beam は skyline から除外**（LP の字面） | `axis-group-interface.cc:850-858` の LP 自身のコメント。Lily# の「固定 3.5 stem を残す」は発明だった |
| ★ **和音記号は LP に合わせる＝中心合わせしない**（ユーザー判断・2026-07-25 明示） | 意図的乖離かを問うたうえでの決定。`ChordName` は X-offset も self-alignment も持たない（`define-grobs.scm:837-855`）＝ink 左が列。`dcbf08e9` で移植し `staffless.line-start.chords-vs-staff` が閉じた。⚠️ **和音グリッドは別 grob（`GridChordName`）で LP も中心合わせする**が、中心を取る相手は小節の四角。Lily# に四角は無いので chords-only シートは ChordName 経路のまま＝**「グリッドも直す」で触らない** |
| ★ **独立 lyrics 行（`lyrics NAME`）を「譜のような帯」として置くのは意図的乖離**（ユーザー判断・2026-07-26 明示） | **複数行歌詞のレンダは Lily# の拡張機能**であり、独立行は「譜に付く歌詞」ではなく**リードシートの word トラック**（自前の小節線・verse を積む帯）。だから譜グループとして `staffgroup-staff-spacing` で置く。**量は測ってある: 9.600000 対 LP 5.500000＝+4.100000**（`2.0 +(9−4.0)+ 2.6`）。
⚠️ **2026-07-26 に +5.600000 から縮んだが、決定は変わっていない**——帯を置く spec が
`staffgroup-staff-spacing`(10.5) だったのを LP どおり `default-staff-staff-spacing`(9) に
直しただけ（台帳 `lyrics.two-staff{,.two-verse}.staff-staff-inside`）。**模型は不動。**⚠️ **LP 側は綴りで変わらない**（`\lyricsto` 有無で probe の全項目一致）ので、差はまるごと Lily# のもの——それを承知の決定。⚠️ **台帳には載せない**（total が 0.006 → 5.6 になり指標が壊れる＝`page.height` と同じ理由）。代わりに `LyricRowIsSpacedAsAStaffLikeBand` が**導出形で**主張し、`LyricRowBaseline` に `LILYSHARP-OWN` を付けた。⚠️ **note-bound（`with lyrics`）は別枝で、そちらは LP の 5.5 に揃っている**（`2b901484`）——混同しないこと |
| ★ **タブのフレット数字を LP より大きく描くのは意図的乖離**（ユーザー判断・2026-07-24 明示） | LP のタブ数字は小さくて読みにくい。Lily# は `TabConstants.FretFontSize = 2.6`（単数字幅 1.625・高さ 1.7875）＝LP の TabNoteHead 幅 0.990155 の約 1.64 倍。和音で数字が被る問題は**じぐざぐ配置**（`SpacingRules.ApplyTabChordSpacing` ほか）で解いてある。**「LP と違う＝発明だから消す」で削らないこと。** ⚠️ 弦間隔（`TabStringSpace`）は別の話で、そちらは LP の 1.5 に揃える |

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
| **現在地・次の一手・開いている作業** | **このファイル §1–§3** | |
| **過去のセッション記録** | `docs/HANDOFF-ARCHIVE.md`（§8） | 閉じた欠陥をどう測ったか |
| **ユーザーの好み・作業規律** | user memory | 「done は push 済みで」 |

**ここに書かない**: LP の式の導出、実測値の生データ、アーキテクチャ解説、コードの説明。
それらは上表の置き場所へ。このファイルには**ポインタだけ**置く。

> 判断に迷ったら: 「これは**次のセッションだけ**必要か、**ずっと**必要か？」
> ずっと必要ならこのファイル以外へ。

---

## 5. 恒久ルール（滅多に変わらない）

### 5.0 進め方の型 ← **8 セッション連続で成立している。迷ったらこれ**

1. **点を対で起票する**（コード変更ゼロ・出力不変の commit）。
2. **予測を先に `why` へ書く**（実装前に。反証可能にするため）。
3. **移植する**（LP の式だけ・§5.2）。
4. **対の食い違いが第2の欠陥を出す**。

各ステップに、繰り返し噛まれてきた理由がある:

- ⚠️ **必ず「対」で作る。** 片側だけだと予測が当たってしまい、**欠陥ごと出荷する**。
  対の設計で最も強いのは **LP 側が恒等になる対**（同じものを二通りに書く／LP が読まない差だけを
  変える）——LP の差が 0 なので、Lily# 側の差が**そのまま欠陥の量**になる。
  ★ **恒等の対は「片側が exact になった瞬間」に第2の測定器へ変わる。** 2026-07-25 の
  tab-concert / tab-keyed がこれ: merge_springs で control が exact になり、双子の側だけ
  +0.067136 残った。LP 側が恒等なのだから**その残差は定義により別の欠陥**で、算術が犯人を
  名指した（幻の臨時記号 1.134272 → spring 0 の最小 +0.134272 → merge が半分＝0.067136、
  15 桁一致）。**恒等の対は閉じたあとも捨てない。**
- ⚠️ **予測が外れたときこそ収穫**。外れの方向が真因を指す。当たったら移植の照合になる。
- ⚠️ **穴を開けるまで、そこに何が溜まっているかは分からない。** 点を開く／種を入れると、
  狙っていた欠陥と**一緒に狙っていなかった欠陥が落ちる**（これまで一度の例外もなく起きた）。
  だから **control（対の基準側）が非ゼロで開くのは正常**——それは持参金であって失敗ではない。
- ⚠️ **`exact` は「正しい」ではなく「その regime では動かない」かもしれない。**
  新しい点は**既存の点が測っていない regime**を優先する。
- ⚠️ **床に座らせない。** 距離が spec の下限に張り付く配置では、両側 exact になって**何も測らない**。
- ⚠️ **probe が何を測っているか確かめてから信じる**（別の grob を測っていた事例が複数）。
- ★ ⚠️ **対の両側が同じ音楽であることを、音を並べて確かめる。** これが 3 回外れて、うち 2 回は
  「Lily# のスペーシング欠陥」として何セッションも台帳に居座った（下の罠 5・6）。確かめる 3 点:
  **①音高**（Lily# は phrase 参照ごとに相対オクターブを既定へ戻す。fixture を写すなら
  `phrase` 構造ごと写す。LP `c'` ↔ Lily# `c`）**②小節線**（LP は `\bar "|."` を書かないと
  終止線を細い `|` にする＝0.9 の差）**③紙面**（`indent`・`line-width`）。
  ⚠️ **「対照」と名前が付いていても対照とは限らない。** 自然幅を測る ragged 対照は、
  **本体と同じ音楽が 1 行に収まる幅**で組むこと（収まらないと LP は行を割り、
  列ごとの引き算が静かに壊れる）。
- ★ ⚠️ **残差の内訳を spring／列ごとに出す。** 総和だけ見ていると「幅ではなく force の問題」
  のような誤った切り分けに落ちる。列ごとに出せば **1 本だけ符号が逆**のような形が見え、
  符号違いは定数誤りではなく**入力（符尾向き・音高）の違い**を指す。
- ★ ⚠️ **恒等の対は「規約が正しい」ことを言えない。** 両側を同じ規約で読んだ差は、規約が
  丸ごと間違っていても 0 のまま（2026-07-25 の和音アンカー：`staffless.*` の 4 点は
  中心合わせのまま全部 exact だった）。⇒ **恒等が守れない量は、恒等の外で直接主張する**
  （`ChordSymbolsAreAnchoredAtTheirInkLeft`）。
- ★ ⚠️ **対が「機構の仮説」を反証しても、量はちゃんと測れている。仮説が外れたから対を捨てる、
  にはならない。**（2026-07-26 の小節番号 BNL/BNH）「Lily# は番号を音符の上に積んでいる」という
  仮説で 2 冊組んだら、**Lily# 側も 4.260000 で不動**＝仮説は外れた。しかし対は
  「両engraver とも音高に依存しない」ことを示し、**欠陥が定数オフセットだと確定させた**
  ——`OutsideStaffStacker` の作り直しではなく式 1 行、と分かったのはこの反証のおかげ。
  ⚠️ **1 冊だけなら「正しく置いている」と「一番高いものを避けている」が同じ数を返す。**
  ⇒ **仮説を確かめる対は、仮説が外れる側にも意味があるように組む。**

#### ⚠️ プローブの罠（同じ形で 6 回噛まれた実例）

⚠️ **5 と 6 は「対の両側が同じ音楽ではなかった」型で、2 セッション分の誤診の原因**だった。
新しい対を作ったら **まず両側の音高と小節線を突き合わせる**こと。

1. LP は `\bar "|."` を書かない限り終止線を細い `|` で終える
2. `tempo` の**メトロノーム記号は符頭グリフを描く**ので `TimeSignatureToFirstNotehead` が
   最初にそれを拾う（2.438400 と読んだ）
3. **`indent` を書き忘れた book**（JN で 8.42 を spring の伸びと誤認・book T で 6 系を
   ページ分割と誤認）。⚠️ **新しい book には必ず `indent = 0` を書く**か、書かないなら
   **Lily# 側に同じ indent を渡す**
4. **加線**（2026-07-25）。`c'` は treble で加線を持ち、加線は符頭の左右に張り出して列の
   スカイラインに入る。圧縮 rod を 1.604200 でなく **1.956300** と読み、あやうく
   「§2H の移植は符号が逆」という誤結論を出すところだった。**音符間を測る対の音高は
   加線のないものにする**（`g'` など）
5. ★ **プローブの Lily# 側で fixture の `phrase` を平坦化すると音高が変わる**（2026-07-25）。
   Lily# は **phrase 参照ごとに相対フレームを既定へ戻す**（`Svg/Collector/RelativeResetMarker`
   の doc に明記）。TSJ は fixture の 3 phrase を 1 つの `melody { }` に潰していたので、
   `slurs` の `c4(` が「`a` の最寄りの c」＝**c''** になり、第4〜8小節が 1 オクターブ上・
   **符尾が逆向き**になった。符尾向きは 2 つの spacing 規則が読む
   （`note-spacing.cc:288-301` の符号／`staff-spacing.cc:55` は `d == DOWN` のみ発火）ので、
   **自然幅が 0.314107 ずれ、2 セッション「機構未特定」として残っていた**。
   ⇒ **対の Lily# 側は fixture を phrase 構造ごと写す。**
6. ★ **「同じ音楽の ragged 対照」が同じ音楽ではなかった**（2026-07-25）。
   `ties-slurs-breaks-ragged.ly` は `\bar "|."` を欠いており、終止線はちょうど 0.900000＝
   その 101.907014 は真の自然幅 102.807014 − 0.9。しかも終止線を足すと **LP は 8 小節を
   1 行に収めない**（2 系になる）＝列ごとの引き算そのものが成立しない。
   「自然幅は 2e-5 一致」はこの引き算から出た**誤り**で、撤回済み
   （正しい自然幅は `compressed-line-force.ly` の CLW）。
7. ★ **`ragged-bottom` は伸長しか止めない**（2026-07-26）。ページが埋まると LP は
   **ragged 指定でも圧縮する**（警告は出るが出力は圧縮）。⇒ 縦の対を「justified vs ragged」で
   作ったのに**両側とも圧縮域**に落ち、差が 1 桁も出ない形で「乖離なし」と読むところだった。
   **伸長 regime を測るなら、ページに slack が残る本数まで systems を減らす**
   （`max-systems-per-page`）。⚠️ **どちらの regime に居るかは、量が spring の ideal より
   大きいか小さいかで判定できる**（ここでは inside 8.651797 < ideal 9 ＝圧縮）。
8. ★ **paper 変数が Lily# 側で fallback を踏むと、存在しないページを測る**（2026-07-26）。
   `systems-per-page = #6` は LP では効くが、Lily# の `PageBreaker` は
   **本数が厳密に一致しない候補を全部落とす**ので最終ページが 5 本になる曲では解が無くなり、
   **1 ページに全 system**（内容サイズ紙）へ落ちる。距離の点は 3 つとも「もっともらしい数」を
   返し、**気づいたのは本数の点だけ**。⇒ **index で読む点には必ず「そのページの本数」の点を
   添える**（§5.2.1 の網の実例）。

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

#### ★ シンボルのリネーム＝**簡単なら Claude が直接やる／面倒ならユーザーに依頼**（2026-07-25 更新）

クラス・メソッド・プロパティ・フィールドの改名は、**オカレンスが少なく機械的なら Claude が
そのまま実行してよい**。⚠️ ただし危険の中身は変わっていない——**grep 不可視の消費者**
（`<see cref>`・XML doc・`<c>Foo</c>` や地の文のコメント・テスト名・文字列・`.md`・
`.ly` プローブのヘッダ）を取りこぼすこと。だから自分でやるときは:

1. **`.cs` 以外も横断 grep** して全オカレンスを数えてから始める
2. 置換後に**旧名で再 grep**して 0 件を確認する
3. **ビルド＋全テスト**まで回す

**オカレンスが多い／分岐が読み切れないときはユーザーに依頼する**（Visual Studio の F2 が
一括で当ててくれる）。**依頼するときに伝える 4 点**:

| | |
|---|---|
| ファイルパス | `C:\MyProj\LilySharp\...\Foo.cs` |
| 行番号 | `123` |
| 現在の名前 | `OldName` |
| 新しい名前 | `NewName` |

⚠️ **オカレンスは 1 箇所だけ伝えればよい。** そこから全オカレンスが一括で改名される。
複数箇所を列挙する必要はない。複数シンボルを頼むときは上の 4 点を 1 行ずつ並べる。

⚠️ **MSVS のリファクタは「コメント内のシンボル名」を取りこぼすことがある。**
改名後に Claude 側で `grep -r '<旧名>'` を掛け、コメント・XML doc（`<see cref>` は追随するが
`<c>Foo</c>` や地の文は追随しない）・`.md`・`.ly` プローブのヘッダ・
コミットメッセージ用のメモに残った旧名を拾って直すこと。**依頼した場合もセットで必ず行う**
（自分で改名した場合は上の手順 2 がこれに当たる）。

⚠️ 新規シンボルの追加・不要になったシンボルの削除は対象外（通常どおり Claude が行う。
削除は上の「未使用に見える ≠ 消してよい」に従う）。ローカル変数も対象外。

### 5.2 LP 移植の原則

> ## ★★ 実測値に基づいてコードを直すことは**禁止**（ユーザー指示・2026-07-22 再確認）
>
> **コードに入るのは LP の `lily/*.cc` / `scm/*.scm` の式だけ。**
> LilyPond の出力から読んだ数値でコードを書いてはならない。可能な限り**字面通りに移植**する。
>
> 実測の役割は 2 つだけ:
> 1. **欠陥を見つけるヒント**（どこがズレているかを知る）
> 2. **移植の照合**（LP の式から導いた値が実測と一致するかを確かめる）
>
> ⚠️ **「実測に合う定数を選ぶ」は 1 も 2 も満たさない。** 合ってしまうので気づきにくい。
> ⚠️ **判定法**: その値を **LP のどの関数のどの行から導いたか**をコメントに書けるか。
> 書けないなら実測を貼っただけ。`LILYSHARP-OWN:` を付けて独自と明示するか、式を探しに戻る。
> ⚠️ **偶然一致に注意**: 2026-07-22 の強弱記号では、誤った frame（五線の中心 2.0 ＋
> staff-padding 0.1）と誤った定数（名目 descent 0.64）が**打ち消し合って** LP 実測の
> 1.342 を 0.002 以内で再現していた。実測に合わせて選んだ定数は、こういう形で
> **2 つの欠陥を固定する**。
>
> ★ **これは何度も繰り返されている指摘。破ったら差し戻し。**

> ## ★★ byte 一致を維持するための細工は**禁止**（ユーザー指示・2026-07-24 再確認）
>
> **目標は出力の一致ではなく、LP の内部ロジックの完全模倣。**
> 出力を動かさないために、LP に無い分岐・除外・条件・近似を入れてはならない。
>
> ⚠️ **判定法**: その分岐／除外／条件は **LP のどの行にあるか**。答えられないなら、
> それは移植ではなく byte 一致を守るための細工。
>
> よく出る形（全部やってはいけない）:
> - **両側から同じものを除外して「一致した」と言う**（LP では両側に在る）
> - **既存 snapshot が動かないほうの実装を選ぶ**
> - 「今のデータ構造では LP の量を表現できない」を**除外の理由にする**
>   （表現できるように直すのが移植。2026-07-24 の ossia 幅がこの形で差し戻された）
>
> ⚠️ **byte 不変は「結果」ならよいが「構成」にしてはいけない。** 報告・commit message でも
> **どちらなのかを書く**（例:「corpus が byte 不変なのは結果であって構成ではない——
> 既存 fixture がその regime を踏まないから」）。
>
> ⚠️ 裏面も同じ強さで成立する: **観測可能な差が無いことは、字面移植をしない理由にならない。**
> 今は差が出なくても、regime が広がった日に欠陥として現れる。
> ⚠️ 同様に **byte 不変は移行が正しい証拠にもならない**（打ち消し合っていても byte は動かない）。

> ## ★★ LP が**計算している**ものは Lily# も計算する。**評価結果を書かない**
>
> **これが 2026-07-26 に 1 セッションで 2 回破られた形で、しかも 2 回とも「規則は分かっていた」。**
> 規則は**すぐ隣のコメントに正しく書いてあった**。知識の欠落ではない。
>
> ⚠️ **判定法**: その値を LP は**計算しているか、宣言しているか**。
> 計算しているなら Lily# も計算する。**「ここでは必ず X になる」は書かない理由ではなく、書く理由。**
>
> 実例 2 件（どちらも出力は完全に同一で、**どんなテストにも捕まらなかった**）:
>
> | 書いたもの | LP の規則 | なぜ通ってしまったか |
> |---|---|---|
> | `Stretchability = 9`（`default-staff-staff-spacing`） | **stretchability を宣言しない**⇒ `set_default_strength` が ideal 自身を入れる（`spring.cc:213-216`）。`CreateSpring` に「**0 が LP の absent**」という規約が既にあった | 9 は ideal と同値。basic-distance を override した瞬間だけ食い違う |
> | 鎖の遠い端の span を**省略**（`BuildLooseChainEnds`） | `-solution_[spring_idx]`＝**次の system の最初の spaceable 譜の refpoint**（`:936-939`） | ガードのもとで必ず 0。**別ファイル（`MultiStaffLayouter`）を読まないと正当化できない不変条件**をコメントで主張していた |
>
> ★★ **原因は無知でも不注意でもなく「同点決着の付け方」。** 「LP を字面で写す」と
> 「ここで何もしないコードを足さない」がぶつかったとき、**最小性の側に倒していた**。
> ⚠️ **最悪なのは、§5.2 の語彙そのものを使って §5.2 破りを正当化できてしまうこと**——
> 「必ずゼロになる項を足すのは*自分の発明*では？」と考えて省いた。**逆。**
> **LP に在る項を書くのは移植で、値に畳むのが発明。**
>
> ⚠️ **既存のどの網もこの型を捕まえられない**（だから散文の警告では止まらなかった）:
> `LpProvenanceTests` は「出典を書いたか」だけ＝**2 件とも正しい REF を持っていた**（§5.2.1①）。
> コーパスも snapshot も**定義により無力**——今日の値は規則の出力と一致するのだから。
> ⇒ **捕まえられるのは §5.4 の「規則を摂動で主張するテスト」だけ。値でなく規則を assert する。**
> 実例: `DefaultStaffStaff_TakesItsStretchStrengthFromTheIdeal_NotFromALiteral`
> （basic-distance を 14 に振り、伸長強度が追随することを要求＝リテラル版なら落ちる）。
> ⚠️ 実際に 2 件を見つけたのは**コミット後に自分の差分を §5.2 片手に読み直したとき**なので、
> §7 のチェックリストに手順として入れてある（運任せにしない）。

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
**先に台帳点を作る**（適用例はアーカイブの `StaffSymbol` ±2.05）。
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

**④ コーパスの穴を数える** — 台帳が薄いのは~~多段譜~~・~~ページ跨ぎ~~・**伸長**
（多段譜は `b3cfb119`、ページ本数は `920cf4dc` で入った）。
⚠️ **2026-07-22 に「点が exact でも regime に入っていないだけ」という新しい形が出た**:
ページ本数の点を A4 に足したら最初から exact で、測ってみると**A4 では本数を容量が決めていない**
（第1システムを 4 オクターブ上げても LP は 13 のまま）。**紙面を縮めて初めて binding した。**
**exact は「正しい」ではなく「その regime では動かない」かもしれない。**過去の最大級の欠陥 2 つ（padding 4 倍・clef 欠落）は
**長期間コーパスから見えず**、clef が padding を偶然あぶり出して初めて露見した。
**新しい点を足すときは、既存の点が測っていない regime を優先する。**
⚠️ **2026-07-22 に同じことがもう一度起きた**: 多段譜の regime を開いた瞬間、
狙っていた欠陥（五線 0.05）と**一緒に、狙っていなかった欠陥（幻の符尾 1.45）が落ちた**。
**穴を開けると、そこに何が溜まっていたかは開けるまで分からない。**

**⑥ 死んだ引数は grep に「生きている」と見える。診断の根拠が識別子なら、値が読まれる行まで追う。**
★ 2026-07-26。「縦スカイラインの符頭は名目 1.0（`EngravingDefaults.NoteheadHeight`）」という
診断が**台帳の `why`・コード・§1 の ▶ に 1 セッション居座った**。実際は `cff877c8` で
LILC インクに移っており、`NoteheadHeight` は **5 つのシグネチャを貫通する未使用引数**として
残っていただけ。grep は「渡している行」を全部見せるので、生きているように読める。
- ⚠️ **「この定数が使われている」は、渡されていることではなく計算に入ることで確かめる。**
  最後の引数がどの式に現れるかを目で追うか、**値を振って観測に出るかを試す**（今回は
  0.545→0.550 の摂動で 9 点が動き、狙いの 4 点が不動＝反証が 1 回のテスト実行で出た）。
- ⚠️ **移植で使わなくなった引数はその場で落とす。** 残すと、次に読む人への誤情報になる。
- これは §5.2.1① の網の穴でもある: `LpProvenanceTests` は「出典を書け」までしか強制せず、
  **その定数が実際に効いているか**は見ていない。

### 5.3 測定の原則

- **推論せず測る。** 実測 → 予測との照合 → 一致しなければ**まず自分の当てはめを検算**
- **摂動法が強力**: `\override` で esw / padding を振り、係数1で追随するか不変かを見る。
  **全部ゼロにして残った定数**がハードコード値
- **測定 regime を混ぜない。** ragged-right（force 0）では spring の床、圧縮時は rod が
  binding する。**どちらで測ったか必ず記録する**
- ★ **ダンプは「1 レコード 1 行」にする。** 行を分けると**落ちる**（LilyPond の stdout に
  診断が割り込む）。2026-07-22 に「2 ページなのに PAGE 1 の行しか出ない」形で落ち、
  そこから誤った結論を出した。**内訳は 1 回の format にまとめ、合計が既知の総数と
  一致するかを必ず突き合わせる**（本数の和 ≠ システム総数なら、その読みは捨てる）
- **配置は「両側」を測る。** ある grob の位置は前後2つの間隙で決まる。
  さらに**同じ box の左右が同じ基準点か**を確かめる
- ★ **縦は staff refpoint 間で測る。system 原点間で測ると嘘の値が出る。**
  `staff-refpoint-extent` は system ごとに違う（小節番号を頭上に持つ system は原点が
  その分だけ上に伸びる）ので、**間隔が一様でも原点間距離はばらつく**。同じ LP ダンプが
  原点間で 11.528583 / 12.000000、staff refpoint 間で 12.000000 / 12.000000。
  前者が「LP は圧縮している」という誤った結論として引き継がれていた
- ★ ⚠️ **N 個の残差を、まず「N 個の量」だと思わない。ばね鎖では残差 ÷ そのばねの強度を出す。**
  （2026-07-26）4 点の残差 −0.002480 / −0.029762 / −0.006666 / −0.013334 は独立に見えたが、
  それぞれの強度 5 / 60 / 2 / 4 で割ると **ページごとに 1 つの数**になった＝**4 距離ではなく
  2 force**。そして **force は「slack ÷ 総強度」なので、犯人はばねではなく鎖の固定項**である。
  ここまでで捜索範囲が「12 本のばね」から「両端の固定項」へ落ちる。
  ⇒ さらに**両 regime（伸長・圧縮）で固定項を逆算すると、測る前に量が出る**:
  伸長 `336×0.000496` と圧縮 `50×0.003333` が**同じ 0.166664**。
  **独立な 2 regime が同じ数を返したら、それは項であって偶然ではない**——この一致が
  「LP を測りに行く前の予測」になり、実測（LP 3.333333 対 Lily# 3.500000）がそのまま照合になった。
- ★ **残差の符号で原因を切り分ける。** あるグリフの**左右の残差が逆符号**なら
  **frame（基準点）の誤り**、**同符号**なら**定数の誤り**。定数が違えば両側とも同じ向きに
  ずれるが、基準点がずれていると片側が広がった分だけ反対側が狭まるため。
  行中 clef/key 変更でこれを使って診断した（`midmeasure.*` の 4 点）
- ★ **変更する前に測る。** 変更後に測ると「LP に近づいたか」を判定できない。
  着手前にコーパスへ点を足しておけば、**反証可能な予測**（この4点が揃って 0 に向かうはず）
  になり、外れたときに診断が違うと分かる
- **「悪化した」＝「変更が間違い」ではない。** 間違った定数が別の欠陥を隠している構図は実在する
- ⚠️ **SVG から精密測定をしない。** 座標は `F2`（`SvgGenerator.cs:229`）で2桁に丸められる。
  6桁の LP 値と比べるなら `LpFidelity/RecordingDocumentContext` を使う
- ⚠️ **紛らわしい数値に飛びつかない。** 6桁一致しないなら別物と疑う
  （残差 0.189365 を「bar line 幅 0.19」と誤認した実例あり）
- ★ ⚠️ **両者のフォント量が混ざる対は、LP が「床に座る」regime を選んで断ち切る。**（2026-07-25）
  テキスト grob の位置を比べる対は、両engraver の**文字幅が違う**ので生の差にフォント差が乗る
  （Lily# の歌詞面は LP より約 27% 広い）。LP 側の量が**下限に張り付いて入力に無反応になる**
  regime（rod が最小値なので、reach が行頭ばねの 0.5 を下回る＝狭い音節）を選ぶと、
  **LP の答えが定数になりフォント量が 1 つも入らない**＝差はまるごと Lily# の欠陥になる。
  ⚠️ 「床に座らせない」（上）と矛盾しない: **座らせてはいけないのは Lily# 側**で、
  **LP 側が座るのは恒等を作る道具**。両方座ると何も測らないので、**窓の両端を必ず確かめる**
  （ここでは LP `w < 2.35` かつ Lily# `w > 1.0`）。
- ★ ⚠️ **「両方のスコアを再現できた」は模型が正しい証拠にならない。**（2026-07-25・staffless）
  摂動 2 点が「移動量＝幅の変化のちょうど半分」を **15 桁**で満たしたので「音節は列に中心
  合わせ＋列は w/2」という模型を立てたが、**外れ**だった。正解は「音節は幅 1.35 の
  placeholder に中心合わせ（`−w/2 + 0.675`）＋ rod が列を動かす」で、**同じ 2 点を同じ精度で
  再現する**。数点を通る模型は複数ある。⇒ **摂動を 3 点目・4 点目と足すより、
  対象そのもの（ここでは paper column）を 1 回 dump するほうが速く、かつ決定的。**
  「grob が動いたのか、grob が乗っている**器**が動いたのか」は、器を dump しないと分からない。

### 5.4 テストの原則

- **実装の定数を実装自身と比べるテストは何も守っていない。**
  LP 由来の期待値を書き、なぜその値かを `LILYPOND-REF` で示す
- ★ **値が「LP の規則の出力」なら、値でなく規則を摂動で主張する**（2026-07-26）。
  規則の入力を振って、出力が追随することを要求する。**この型の欠陥はこれ以外の網に掛からない**
  ——出力は同一・snapshot も台帳も動かず・`LpProvenanceTests` は出典しか見ないから（§5.2 の
  ★★「評価結果を書かない」）。⚠️ **摂動法を §5.3 の「測定」だけでなく「テスト」に使う**のが要点。
  実例 `DefaultStaffStaff_TakesItsStretchStrengthFromTheIdeal_NotFromALiteral`:
  `Stretchability = 0`（＝LP の absent）を assert したうえで **basic-distance を 14 に振り**、
  伸長強度が 14 になることを要求する。リテラル `9` を書いた版はここで落ちる
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
2. [ ] **§1「現在地」を書き換える**（追記しない）— HEAD / ahead 数 / テスト数 / 非ゼロ台帳点 / ▶
3. [ ] §2「開いている作業」が動いたなら更新する。**完了した項目は消す**（経緯を残したいなら
      §5 へ**汎化**するか、アーカイブ（§8）へ落とす。§1・§2 に溜めない）
      ⚠️ **§1 が 1 画面に収まらなくなったら、それは落とす合図。**
4. [ ] このセッションで得た**恒久的な知識**を §4 の表に従って置き場所へ出す
      （LP 実測値 → `audit/lp-geometry/`、LP の式 → コード内 REF、座標系の状態 →
      `COORDINATE_AUDIT.md`）
5. [ ] **新しい `handoff-*.md` を作っていないことを確認**
6. [ ] **snapshot を再ベースしたなら、それを正当化する台帳キーを message に名指したか**（§5.2.1③）
7. [ ] **定数を足したなら `LILYPOND-REF:` か `LILYSHARP-OWN:` を付けたか**
      （`LpProvenanceTests` が落ちる。baseline を上げて通すのは禁止・§5.2.1①）
7.5 [ ] ★ **移植した差分を §5.2 片手に読み直したか**（**REF が付いているかではなく、
      REF の隣の式が LP と同じ形か**）。各項について:
      **「LP はこれを計算しているか、宣言しているか」**——計算しているなら Lily# も計算する。
      **「ここでは必ず X になる」で畳んだ項はないか。**
      **「別ファイルを読まないと正当化できない不変条件」をコメントで主張していないか。**
      ⚠️ **この手順でしか見つからない**（出力が同一なのでテストもコーパスも無力）。
      2026-07-26 は 2 件ともここで見つかった。**運任せにせず必ず走らせる。**
      見つけたら §5.4 の**摂動テスト**を同時に足す（次は機械が落とす）
8. [ ] `git status` で意図しないファイルが混ざっていないか確認
      （特に `audit/scripts/__pycache__/` — 生成器を走らせると必ず出る。commit しない）

---

## 8. 過去の記録の在り処

### `docs/HANDOFF-ARCHIVE.md` ← **このファイルから外した §1〜§3（逐語・2026-07-24 まで）**

セッションごとの経緯・閉じた欠陥の詳細・完了したロードマップ項目は全部そこにある。
**通読する必要は無い。** 読むのは次のどちらか:

- **同じ regime にもう一度触るとき**（その欠陥をどう測って何が第2欠陥だったか）
- **本ファイルの記述を疑うとき**（「完了」表記の裏取り。ただし最終的な裏取りは常に実コード＝§0）

⚠️ **アーカイブへ追記しない。** 本ファイル §1 を書き換えるのが引継ぎで、
§1 が長くなったら**そのときアーカイブへ落とす**（原則・学びは §5 へ汎化してから）。

### root に残る旧 handoff（未追跡）

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
| `HANDOFF-stage4-vertical-yup.md` | **島1 は `ff64f38e` で完了したので、この用途は消えた。** 残る価値は §3B ②（島2 = device 島群）の記述だけ。⚠️ 島1 に関する記述は全部 stale。**次に島2 へ着手する人が回収して削除する候補**（削除はユーザー承認） |
| `HANDOFF-lp-calc-incorporation.md` | §3C 着手時に |
| `HANDOFF-dead-code-audit.md` | §3D 着手時に |
| `HANDOFF-2026-07-20-*.md`（5本） | 過去セッションの記録。LP 事実は概ね吸収済 |
| `HANDOFF-beam-quanter-unification.md` / `HANDOFF-coord-frame-unification.md` / `HANDOFF-layout-x-unification.md` | 完了済み作業の記録 |
| `REVIEW-HANDOFF.md` | 規律は §5.1 に吸収済 |

**回収方針**: 各テーマに着手するとき、そのファイルを読んで**必要な知識を §4 の置き場所へ移し、
読み終えたファイルを削除する**（削除は都度ユーザー承認）。一気にやらない。

なお `AI_POSITIONING_HANDOFF.md` と `docs/arpeggio-rework-handoff.md` は**追跡済み**で
別系統。本ファイルの対象外。
