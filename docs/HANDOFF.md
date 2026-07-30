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

最終更新 2026-07-30（第39セッション・**TXW の分解が全項ハズレだった（和だけ合っていた）——
真の鎖は 0.46 衝突 pass ×加線。trill の残差を 3.279279 → 0.000179688 まで詰め、字面度の
自己監査で 8 件の非字面を洗い出して 4 件を直した。⚠️ 「島は閉じた」ではない**——
残り 4 件（うち 2 件は本が先）＋ **trill 多用譜の perf +14〜19%**。詳細は下の第39セッション節）
/ HEAD・ahead 数は §0 で確認すること
（⚠️ **ここに数字を書かない**——自己参照で、書いた瞬間から commit のたびに嘘になる）。
⚠️ **未 push が溜まっている**（第21セッション末から。push はユーザー・§5.1）。

**HEAD は 3541 passed / 0 failed / 3 skipped**（台帳 227 点全緑・削った pin 網 2 本と
足した `TrillWaveOutlineTests` 3 本の差）・Core 0 warn 0 err・**ワーキングツリーは clean**
（未追跡の `HANDOFF-*.md` 14 本はセッション前からのもの＝§8）。
**第39セッションの commit**: `fb5b8111`（**訂正＝TXW の分解を実 skyline dump から書き直し・
コード不変**）／`2181e311`（**port (a)＝trill の aligned_side を pointwise へ＋左バウンドの
attach-dir CENTER・snapshot 2 枚再ベース・GO 済・スカラー支持辺を削除**）／
`aa30ca83`（**port (b)+(c1)＝右バウンドは列左端・stacker の profile を 1 本に**・TXW
4.810000＝予測どおり・snapshot 2 枚）／`0a522899`（**port (c2)＝波は
`scripts.trill_element` の反復**・TXW **−0.000179688**＝平坦化族のみ・snapshot 2 枚）／
`50fc6a80`（**支持を自 voice だけに**＋`TrillSpannerItem.VoiceIndex`・出力不変＝**結果**）／
`29fe9c65`（**§7.5 の自己監査**＝死んだローカルに生きた REF・stale 散文・無名の FullSize・
不在プロパティの明記。コメントのみ）／`81c46545`（**描画も glyph run へ**＋piece ごとの Y＋
ページ extent を同じ家から＋**`TrillWaveAmplitude` 削除**＝読み手ゼロ・snapshot 2 枚）／
`20713d6b`（**残る非字面 4 件と、2 件を測る本 TXA の設計**をプローブヘッダと ▶ に）／
`d1f4df64`（**perf 実測 → マージ 1 つ撤回・残りを lever つきで名前付け**）。
⇒ **trill 台帳 8 点は 7 点 exact ＋ 1 点 1.8e-4。ただし島は閉じていない**——
非字面 4 件（▶ 参照）と perf +14〜19% が残る。
**第38セッションの commit**: `2b6fb21d`（起票＝trill-stem-support.ly＋台帳 3 点＋ミラー・
出力不変）／`daeb203c`（port＝`SupportEdgeUp`・TLS/TLB 九桁 exact・**snapshot 全 byte 不変
＝結果**。台帳 3 点だけが観測者）／`79f7c0fc`（字面箇所へ LILYPOND-REF・コメントのみ）／
`224a3cba`（**round 2＝監査が名指した未測定 3 regime を測定・台帳 4 点追加・出力不変**。
下の round 2 節）。
**第37セッションの commit**: `34a3d8d0`（pointwise 支持＋下側 pass＋X アンカー port・
GO 済・snapshot 28 枚再ベース）／`0cdb3efe`（延長＝ハック 4 件の字面化・GO 済・
snapshot 4 枚再ベース・延長 8-9）／`1e174298`（延長2＝perf 実退行 3.4× の検出と 3 修正・
**出力不変**・延長 10——merge-walk Distance／clef 輪郭キャッシュ／profile 建てスコープ）。
**第36セッションの commit（出力不変）**: `8bcf358e`（DMF/DMW＋機構訂正）。
**第35セッションの commit（どちらも出力不変）**: `300a7f54`（NoteColumnLayout）／
`c5a44c25`（dynamic-support 起票——⚠️ **この commit の「head のみ」機構主張は
第36セッションで訂正済み**・台帳 why 参照）。
**第34セッションの commit（GO 済・snapshot 3 枚再ベース）**:
`f09abbda`（起票＝プローブ＋台帳 3 点・出力不変）／`3e78ae2a`（移植＋網 2 本＋再ベース）。
**第33セッションの commit（全部 GO 済・各弾 snapshot 66 枚再ベース）**:
`df72dd5f`（tempo 移植＋台帳 3 点）／`8dffccc0`（出典コメントのみ）／
`d7422832`（残近似の字面化）／`178954cc`（第4弾＝符頭/旗/dot の skyline を実アウトラインへ）／
`1c454c58`（チャート型 tempo×label 対を撤去——**LP にこの装置が無い**のが答え）＋handoff。
詳細は第33セッション節。
★ **第2弾の所見**: trill グリフ片の**平ら台地は近似でなく LP 自身の構成**——bound text が
「straight line as the vertical skyline」ラッパで包まれている（define-grobs.scm:4054-4068 の
LP 自身のコメント・TMT が glyph top+0.46 六桁丸で束縛した理由）。**アウトライン化は逆に発明**。
mark の pair に残る唯一の named 近似は**符頭の箱**（符頭は箱を ~0.001 まで満たす・
magstep(−1) の outline 化は StaffSize 型の濫用になるため見送り＝コードに明示）。
★ **perf は測定済・退行なし**（§5.3 の最小値ベンチ・50 小節/5 section/trill/tempo・
warmup 3＋50 回×3 セットを**セッション前 `7fe0dfd8` の worktree と同一ハーネスで比較**）:
base min 36.70 対 HEAD min **34.85**＝雑音内で同等。`PlaceMusicGlyph`/等式 outline は
resolve 済み buildings を (glyph,size)/(string,size) でキャッシュ（第31セッションの
+15ms の教訓を最初から適用）。⚠️ **そのキャッシュはサイズ込みキー**——将来グリフサイズが
連続値で変わる呼び方（任意倍率 ossia へ音楽グリフ outline を使う日）はキーが増殖するので
キー設計を見直すこと。
★ **第3弾の教訓（§5.0 の親戚）**: own device を LP 準拠の量の上に温存すると、
**stacker が解いた制約を device が捨てるたびに手で再導出する羽目になる**（clef で 1 回・
trill で 1 回繕った）。**装置ごと LP の形にすれば繕いは全部不要だった**——
「own device への patch が 2 回続いたら、device 自体が LP に在るかを問う」。
**第32セッションの commit**: `afd158a5`（起票・4 点・出力不変）／
`8d4799b5`（移植＋snapshot 4 枚再ベース・**GO 済**）／`41386be3`（tempo 島 LP 側）／
`8b4823cd`（tempo 島 Lily# 側・TMQ==TMT）＋handoff 数本。

⇒ **指標は「下がったか」ではなく「残ったものが 1 方向の名前付き量か」で読む。**
⚠️ **§6 の「LP 忠実度スコア」は台帳のエコーで Lily# を測っていない**（§5.3）。
**変更の効果は全テストを走らせて落ちた id で見ること。**

★★ **方針転換（ユーザー指示・2026-07-26）**: 目標は **LP レイアウトの完全模倣**。
**一時的に byte 不一致になっても、移植忠実度が部分的にでも上がるなら受け入れる。**
⇒ snapshot は**もう網ではない**。だから**各段階の前に台帳点を開く**（§5.2.1③ は従来どおり）。
出力が動く段は**提示して GO を待つ**（承認ゲートは維持）。


### 第39セッション（2026-07-30）＝ **前セッションの分解が「和は合って全項ハズレ」だった——真犯人は 0.46 衝突 pass ×加線インク、そして支持は trill でも pointwise**

★★★ **第38セッション round 2 が TXW を「加線 4.05 ＋ trill 自身の padding 0.5 ＋ 波リーチ
0.170721」＝aligned_side の pointwise 化と読んだ。六桁で閉じていた。全部違った**——
`4.05+0.5+0.170721` と `4.100000+0.460000+0.160721` は**同じ数**（§5.2 の「打ち消し合う 2 つの
誤り」そのもの）。**推論をやめて grob の skyline を dump した**（`ly:skyline->points`）:

1. ★★★ **aligned_side は TXW では静止値 3.550000 を出す**。支持集合は**音符列そのもの**
   （scheme-engravers.scm:1830 の side-support-elements＝note-column grob。だから
   :273-281 の Stem 方向スキップは発火しない）で、tall 列のインクは**線の右端より右に丸ごと居る**
   （列左 17.841735 対 線右 17.793100＝round 2 が見た 0.0486 の隙間）——**譜 extent 2.05 が決める**。
2. ★★★ **残る 1.170721 は outside-staff 衝突 pass で、障害物は加線**。⚠️ **LedgerLineSpanner は
   `X-extent #f` / `Y-extent #f` を宣言しつつ `vertical-skylines` を stencil から持つ**
   （define-grobs.scm:2072-2074）＝**extent 計算からは完全に見えず、skyline には居る**。
   だから extent ベースの読みでは決して見つからず、**この pass 以外では絶対に binding しない**。
   dump の加線 skyline は x (17.515685 . 19.471985)＝符頭 extent を length-fraction 0.25 で
   広げたもの・UP 高さ **4.100000**＝position 8 ＋ ledger-line-thickness (1.0 . 0.1) の半分
   ＝`1.0*line-thickness + 0.1*staff-space` の 0.2/2（staff-symbol.cc:337-344）。**4.05 ではない**。
3. ★★★ **mover 側の profile は自分の vertical-skylines**（axis-group-interface.cc:770-773）＝
   **実アウトラインの 2 片**: グリフの真 X extent 上は平ら −1.000000、その先は
   `scripts.trill_element` の反復（line-interface.cc:48-108・Y CENTER 揃え）の**波形多角形**。
   加線の左端でその値は **−0.160721**（dump の点 (8.764100 . −0.360000)〜(9.192100 . 0.152000)
   を結ぶ上り building 上）。⇒ **4.100000 + 0.460000 + 0.160721 = 4.720721 六桁**。
   ★ **一定の「波リーチ」は存在しない**——binding 値は障害物が始まる X で決まる。
4. ★★★ **副産物: 第32セッションから未測定だった 0.46 対 0.5 が閉じた**。加線は
   outside-staff-priority を宣言しないので、**TXW が TSP の言う「priority の無い障害物」そのもの**
   ＝**slur の本は要らなかった**。pass は 0.46 を払う（台帳 `trill.support`・TSP の why に反映）。
5. ★★★ **port (a) を landing（`2181e311`・GO 済・snapshot 2 枚）**:
   `TrillSpannerEngraver.AlignedSideLineY`＝aligned_side の pointwise 字面
   （my_dim＝平らグリフ台地＋波／支持＝各列を `DynamicEngraver.ColumnSupportSkylines` で
   自分の X に・譜 extent が :323-330 の最小・+0.5・:433-453 の床も書く）。
   ⚠️ **左バウンドの X を「後で」にはできなかった**: LP は bound text を**列 extent の中心**に
   付け（line-spanner.cc:155-175 attach-dir CENTER）、線はその stencil の真右から始まる
   （:621-626・gap なし）。Lily# は**列の左端**に中心を置いて発明 1.6+0.3 を払っていた——
   **自分の列の符尾を覆わない台地では TXG が 8.000000 → 6.045000 に落ちる**＝半分ずつ入れると
   悪化する（§5.0 の ossia の教訓）。⇒ 同時投入。**着地: TXW 8.000000000 → 3.550000000
   （残差 +3.279279 → −1.170721＝予測どおり）・他 7 点（TXG/TLS/TLB/TLW/TSB/TSP/TRF/TRC）不動。**
6. ★★★ **スカラー支持辺が production から消えた**（承認済で削除）: 最後の消費者が trill だったので
   `DynamicEngraver.ColumnUpEdge`/`ColumnSupportEdge`/`GetHighestExtent`/`GetLowestExtent` と
   **`NoteColumnLayout.SupportEdgeUp`**（＋pin 網 2 本）を削除。**第34セッションの「列の到達距離
   4 家」は 3 家になった**——支持辺の行は dynamics（第37）と trill（今回）が pointwise に移って
   消えた。⚠️ 網が運んでいた主張は残っている（drawn stem 模型は `OutwardTip_*`＝削った read が
   変換していた同じ家／符頭の LILC インクは `DynamicSupportPointwiseTests`）。
7. **snapshot 2 枚の中身**（目視＋SVG 数値で衝突確認済）: `trill-spanner` は**中央の trill が
   3.86 降りた**（X の遠い加線付き stop 列がもう持ち上げない＝コーパスに出た TXW 欠陥）・
   全部の tr が列中心へ +0.652・波の始まりが −0.14／`trillspan-lower-staff` は X のみ。
   波は加線より左で終わるので**インク衝突なし**。
8. **残した named 事項**: ⑴ 支持がまだ**全 voice union**（LP の Trill_spanner_engraver は
   **Voice 文脈**・engraver-init.ly:376）——`TrillSpannerItem` に VoiceIndex が無い＝
   第37セッションの `DynamicItem.VoiceIndex` と同じモデル追加が先 ⑵ my_dim の波は Lily# の
   描画装置（＝半分 (c)）⑶ broken piece は 1 つの Y のまま（**per-piece の pointwise 解の max**
   にしたので各 piece は自分の system の X フレームで読む）⑷ 右バウンドは Lily# の
   BoundPadding 0.5（LP は列左端＋波要素の端数分だけ短い）。
9. ★★★ **続けて (b)(c) も同セッションで閉じた——そして (b) の記述は間違っていた**
   （`aa30ca83`・`0a522899`・どちらも GO 済・snapshot 各 2 枚）:
   - ⑴ ★★ **(b)「加線インクを staff skyline へ」は既に移植済だった**——
     `SkylineBuilder.AddNoteBoxToSkylines` は最初から加線の箱を seed している
     （同じ length-fraction・同じ厚み）。**障害物を隠していたのは線の X**: Lily# は波を
     stop 列の 0.5（`BoundPadding`・出典なしの発明）手前で止めており、LP は右バウンドを
     **列の左端**に付ける（line-spanner.cc:155-175 attach-dir LEFT・:561-562 bound-details
     padding なし）。加線は列の左 0.326 までしか届かないので、**波は clear すべきインクの
     0.174 手前で終わっていた**。⇒ **「未移植」と引き継がれた項目は、まず Lily# を読む。**
   - ⑵ **(c1) stacker が move 計算と登録で別の profile を渡していた**（move 側は全 span を
     覆う平らなグリフ高の箱）。LP は `avoid_outside_staff_collisions` と `all_v_skylines` に
     **同じ v_skylines** を渡す（axis-group-interface.cc:770-773,:798-803）。1 本にした。
     **予測を先に書いて 4.100000+0.460000+0.250000 = 4.810000・残差 +0.089279＝
     0.25−0.160721 → 実測 4.810000000 で桁まで的中。**
   - ⑶ ★★★ **(c2) 波は「振幅」ではなく `scripts.trill_element` の反復だった**
     （`TrillWaveOutline`＝make_trill_line の字面）。**グリフの箱は 2 つ両方使う**:
     LILC 幅 1.0 が**反復ステップ**、アウトライン幅 1.448 が**先頭 1 個の長さ**——差が
     「隣とブレンドするためのはみ出し」（LP 自身のコメント :72-74）。だから run 長は
     `1.448 + n*1.0` で、**線は必ずバウンドの手前で終わる**（dump の 0.0486）。
     消費者は 3 つ（engraver の my_dim／stacker の mover／renderer の線長）で**家は 1 つ**。
     **着地: TXW −0.000179688＝平坦化族**（傾き ~1.2 の building 上の点なので 1e-4 の X 差が
     そのまま出る・LP の記録値も六桁丸め）。**fit しない。**
   - ⑷ ⚠️ **誤った原因を 2 回書いて 2 回撤回した**（このセッション内で）: layout に
     **fit 済みの端**を持たせた版で TXW が静止 3.550000 に落ちた。①「Lily# の spacing では
     加線が重ならない」→ 実測で否定（span は LP と一致）②「`(int)(delta/elt_len)` が
     6.0−ε を 5 に切る」→ **自分で書いた単体テストが否定**、両順序の直接測定も否定。
     **機構は未特定**。出荷形は字面のほう（layout はバウンドを持ち、各消費者が 1 回 fit）で
     どちらでも同じなので、コード・台帳 why・プローブヘッダには**観測だけを書き、原因は
     撤回した**（§5.3「ピンできていない原因を書かない」・§5.2 の六桁トラップの一段下）。
   - ⑸ ~~**描画はまだ放物線ポリライン**~~ — **閉じた（`81c46545`）**: 描画も
     `scripts.trill_element` を並べる（`EmmentalerGlyphs.OrnTrillElement`＝U+E070・位置は
     `TrillWaveOutline` の同じ家）。**予約とインクが 1 つの計算**になり、
     ⇒ **`EngravingDefaults.TrillWaveAmplitude` は読み手ゼロになったので削除**した。
     LP に対応物が無い LILYSHARP-OWN だったので、名前を直すのではなく消すのが終端。
     同 commit で ⑵ **broken piece ごとの Y**（LP は system ごとに clone して各自
     aligned_side・`spanner.cc:36-144`。max を取るのをやめた）と ⑶ **ページ extent も
     同じ家から**（`InkReach`）も字面化した。
10. ★ **支持を自 voice だけにした**（`50fc6a80`・**出力不変＝結果**）:
    `TrillSpannerItem.VoiceIndex` を collector が START イベントの `_currentVoiceIndex` から
    焼き込み、engraver の支持walk と左バウンドの列がその voice だけになった
    （LP の Trill_spanner_engraver は **Voice 文脈**・engraver-init.ly:376／
    scheme-engravers.scm:1816,1824-1830）。他 voice のインクは衝突 pass が担う＝LP の分業。
    ⚠️ **byte 不変が「結果」である裏取り済**: trill を持つ fixture は 2 本ともに
    `voice { }` が**ゼロ**（＝多声 trill のページがコーパスに無い）で、プローブの voice 2 は
    spacer rests＝インク無し（ヘッダに明記済）。**どちらの網もこの regime を踏まない。**

### 第38セッション（2026-07-30）＝ **trill raw 3.5 を起票→port まで一気通貫——支持の「形」（平ら my_dim のスカラー）は正しく、違うのは値だけだった** ⚠️ **「スカラーで足りる」は第39セッションで反証済み（下の 4・7 と round 2 の 7 を参照）**

★★★ **第37セッションが名指した「raw 3.5 の最後の消費者 trill・点が先」を、起票（`2b6fb21d`）
→port（`daeb203c`）まで同セッションで閉じた**。新プローブ `trill-stem-support.ly`
（12 秒級・3 冊）＋台帳 3 点 `trill.{shortened-stem,beam-face,stemless-control}.staff-to-line`。
**予測は 3 冊とも主枝で六桁的中し、Lily# 側も九桁まで的中**:

1. ★★★ **TLS（+8 の強制上向き四分）**: LP **8.000000**＝full shorten の実 tip 6.5
   （dump の Stem ext 上端 6.500000 そのもの・stem.cc:519-555）+ 0.5 + 1.0。
   0.46 pass 候補 7.96 は敗退。Lily# 9.000000000 → **残差 +1.000000000 ＝ raw−短縮の純量**。
2. ★★★ **TLB（同列の強制上向き beamed pair）**: LP **8.240000**＝**quant beam 外面 6.74**
   （Stem は外面まで描かれ Stem ext 上端 == Beam ext 上端）+ 0.5 + 1.0——
   **aligned_side が勝つ**（下側の DSB では 0.46 pass が勝った・**上下で勝つ鎖が違うと確定**。
   pass 候補 8.2 は padding 差 0.04 でちょうど負け）。**Lily# は TLS ≡ TLB 九桁同一
   （9.000000000）＝beam 盲目恒等が予告どおり鳴った**（§5.3 の falsifier）。残差 +0.76。
3. **TLW（全音符 control）**: 6.045000 → **残差 0 exact**（両側 LILC 0.545・丸い鎖＝TRC の前例）。
   TLS−TLW＝1.955 が stem 項を trill 鎖ごと打ち消して単離。Stem grob は**空 extent** で dump
   された（「0 本」の構造 falsifier は実質成立・grob は在るがインクが無い）。
4. ★★★ **port 済（`daeb203c`・同セッション続行時）**: trill の my_dim は**平ら**
   （straight-line skyline wrapper・define-grobs.scm:4054-4068）なので **dynamics と違い
   スカラー edge の「形」は生き残る**——`RawSupportEdgeUp` → **`SupportEdgeUp`**＝stem 枝を
   `OutwardTipDeviceY` の変換（**1 軒 2 フレーム**・恒等を pin 網
   `SupportEdge_StemSide_IsTheDrawnStemEnd` が主張）へ、trill エングレーバへ
   `BuildBeamMembers` の beam lookup を配線（dynamics と同じ map＝2 消費者が「誰が beamed か」で
   割れない）。**着地: TLS +1.0 → 0・TLB +0.76 → 0（両方九桁 exact——Lily# の beam quanter は
   この regime で LP の face 6.74 を桁まで再現）・TLW/TRF/TRC/TMT 不動＝対の要件どおり**。
   旧 raw pin 網は退役し drawn 主張（旧模型なら落ちる 6.5 対 7.5）に差し替え。
   ★ **snapshot は全 byte 不変＝「結果」であって構成ではない**（短縮/beamed の同方向 stem 列の
   上に trill を張る fixture が corpus に無い——観測者は台帳 3 点だけ）。目視 1 枚
   （短縮四分＋beamed pair×trill）で重なり無し・beamed 側が 0.24 高い絵を確認済。
5. ★ **texture の学び（probe/score remark に記録済）**: per-note `@stemUp` では対が組めない——
   **Lily# の beam 方向は per-note override を見ない**（`BeamDetector.DefaultBeamStemUp`）ので、
   両エンジンとも **voice 強制**（voice 2 は spacer `s` のみ＝インクゼロの強制スイッチ）で組む。
6. **残した named 事項**: ⑴ `GetLowestExtent` の下側は production 到達不能（ColumnUpEdge が
   唯一の呼び手・dir=+1）だが対称性のため同じ drawn 模型 ⑵ trill の単一 pass
   「支えに 0.5 を全 entry へ」近似は不変（点なし・trill.support の why どおり）。

**Round 2（同セッション・ユーザーの字面度監査→未測定 3 regime を測定・`224a3cba`・
台帳 4 点・出力不変）**:

7. ★★★ **TXW が第3候補に落ち、二分 3 冊（TXN/TXE/TXS）で六桁分解**: 4.720721。
   ⚠️ **この節が書いた分解「加線 4.05 + trill の padding 0.5 + 波リーチ 0.170721＝aligned_side の
   pointwise 化」は第39セッションで全項ハズレと判明**（和だけ合っていた・上の第39節）。
   **正しくは 加線 ink top 4.100000 + outside-staff 0.460000 + 波アウトラインの局所値 0.160721。**
   ここで**正しかった観測**: tall の head/stem は spanner ink の外（gap 0.0486）で**何も課さない**
   （LP は stem tip が trill 線を突き抜けた絵を自分で描く）・TXS（全体を 1 小節右へ）が 13 桁同一
   ＝絶対 X（pure xc=0）仮説は死亡・**Lily# のスカラー max 8.0 ＝残差 +3.279279 が X 盲目欠陥**・
   TXG（グリフ帯 control）は両側 8.0 exact。**pointwise 化そのものは正しい port で、第39セッションで
   landing 済（TXW 3.550000000）**——ただし「支持に加線が入る」は誤りで、加線は衝突 pass 側。
8. ★★ **TSB（sloped 強制上向き pair）**: LP 8.221188659 ＝ **高側 member の stem 端
   （自分の X での face）+ 1.5・15 桁**——Beam 包絡の角 6.74 ではない（半 stem 幅の
   slope 分 0.019 離れる）。**Lily# の sloped quant face は 3e-10 で一致**＝残差 0。
9. ★★★ **TSP は falsifier が発火して別の欠陥を出した**: fermata は
   **(outside-staff-priority . 75)**（scm/script.scm）＞ trill 50 ⇒ **LP では trill が
   3.550000 に留まり fermata が trill を越える**。Lily# は script を不動 seed にするので
   trill が fermata を越える（5.235・**残差 +1.685＝priority 逆転**）。port＝**宣言 priority
   を持つ script は priority pass の mover**（無宣言 script は seed のまま＝LP 自身の分業）。
10. ⚠️ **正直に残る未測定 1 件**: 単一 pass の 0.46 対 0.5 は **priority 無しの障害物
    （slur の弓）**の対が要る——TSP では測れない（why に明記・混同禁止）。

### 第37セッション（2026-07-29）＝ **pointwise 支持＋下側 pass を同時着地——DSB は face欠片族 exact、残る e-3 対の正体は「Pango が整形幅で中心化する」だった**

★★★ **第36セッションが用意した port を実装し、GO 済で landing（`34a3d8d0`）**。5 点の着地:
**DSQ +2.977210 → +0.001512／DSB +0.899924 → −0.000076（face欠片族 EXACT・Lily# が
DSQ↔DSB を LP と同じ 2.077 で分離）／DMF +1.031307 → +0.001793／DSW・DMW・DY 不動
−0.000076**。全 3536 中 snapshot 28 枚のみ fail（=出力が動いた分・GO 対象）。

1. ★★★ **機構**: ⑴ `DynamicEngraver.ColumnSupportSkylines`＝pointwise 支持（per-voice
   head インク箱＋**実 stem の細い extent 箱**（attach X・StemThickness 幅・beamed は
   quant face・方向一致のみ :273-281）＋譜 extent 床）、my_dim＝`DynamicOutline`
   （baked feta 文字を advance+kern の pen 位置で合成・キャッシュ・@text は serif 箱
   fallback）。`BaselineY` は pointwise `Distance` の字面。⑵ **下側 outside-staff pass**:
   seed は `AddDynamicsToSkyline` を **beams の後（最後）** に移し、蓄積 down profile へ
   0.46 で衝突配置→**アウトラインを merge**；draw は `StackBelowStaff` の support を
   per-(system,staff) の実 profile（`BuildStaffSkylines`）にして pockets 解禁・
   dynamics=アウトライン・hairpins も 0.46（profile 無しの旧経路は bit 不変で温存）。
   `WidenToNeighbors` 撤去（LP に無い装置＝pass 欠落の自前補償だった）。
2. ★★★ **X も port の一部だった**: LP は DynamicText を **X-parent（自 voice の列）の
   extent 中心**に центр化（dump で確定: text 中心＝符頭中心）。Lily# は列 X（符頭左端）
   に центр していた＝**半符頭ズレ**。`DynamicItem.VoiceIndex` を collector が焼込み、
   アンカー＝自 voice の item の advance/2（rest 0.75）。**seed の note 箱も描画フレームへ
   統一**（head=[x, x+advance]・**stem=attach X の実幅 0.13**・flag=stem X・ledger 追随——
   旧 ±1.0ss の stem 箱のままでは DSQ の tuck が seed 側で壊れる）。
3. ★★★ **残る +0.0015/+0.0018 は fit 不能の named 族**: probe 再走の dump で分解——LP の
   pen＝中心−**整形幅**/2（DSQ f: x=(8.723849 . 9.987151)＝幅 1.263302）、Lily# の pen＝
   中心−advance run/2（1.280）＝**0.00835 左**。stem X（dump (8.7034 . 8.8334)＝当方
   attach と一致・幅 0.13・f との重なり 0.11 ✓）が f 左尾の傾斜をその分先で読む。
   dynamic-text-x.ly が「整形幅を焼くな」と自ら命じた **Pango X 量子化族**（why に記録）。
4. **網**: `DynamicSupportPointwiseTests`＝機構網（合成 advance+kern／アウトライン極値＝
   文字インク／**f=tuck・fff=stem-bind の scalar 不可能性**／beam 0.46 push の exact 算術）。
   `RawSupportEdgeUp` は **trill 専用**に降格（remark・pin 網コメント更新済・raw 3.5 は
   trill の残債として点が先）。引用ラチェット 747 維持（規則を 1 つ学んだ:
   **範囲直後・同一行にアンダースコア入り symbol**）。
5. **perf**: fur-elise min-of-50×3、HEAD 62.47 対 移植後 63.11＝雑音帯（§5.3・worktree A/B）。
6. **snapshot 28 枚の目視 15 枚**（dynamics／voice-dynamics×4／scripts-dynamics／
   multi-staff-hairpins／01-expressions／03-piano／08-chorale／hara-kiri／ossia-beams／
   tuplet-bracket-whole-notes／navigation-marks／chords／custom-text／multi-line-spanners／
   dynamics-lower-staff）: **全部 LP 形**——f が細 stem 脇に tuck（DSQ の絵）・ラベルが
   +半符頭右へ・音符に追随して上がる・page bbox の微移動組（chords 等）は相対不変。
   重なりは見つかっていない。**GO 済→28 枚再ベース→全 3534 緑→`34a3d8d0`**
   （台帳 3 点の残差・why・probe ヘッダ・LpGeometryProbes 注釈も同 commit）。
7. **残した named 負債**: ⑴ Pango X 量子化族（上 3・fit 禁止）⑵ 描画 face は serif の
   まま（予約=feta アウトライン/描画=serif の既存 debt・この port で拡大せず）⑶ 下側 pass
   の profile に slurs/ties/brackets/scripts は未合流（seed 側 gap には入っている——
   非対称は既存形・コードに明示）⑷ trill の raw 3.5（RawSupportEdgeUp・点が先）
   ⑸ ossia の下側 pointwise は gate（box のまま・測定 regime なし）。

**延長（同セッション・ユーザーの字面度監査質問→ハック 4 件を字面化・GO 済）**:

8. ★★ **「ハック風味」と自己申告した 5 件のうち 4 件をその場で LP 字面へ**（snapshot は
   4 枚だけ動いた＝voice-dynamics×3・above-dynamics・目視済・台帳 220 点は全て不動）:
   ⑴ **支持を自 voice のみに**——`Dynamic_align_engraver` は Voice 文脈
   （engraver-init.ly:359,410）。他 voice のインクは衝突 pass が担う（LP の分業）。
   全 voice union の LILYSHARP-OWN は削除 ⑵ **rest アンカー**——定数 0.75 →
   `GetRestBBox` の ink 中心（aligned_on_parent :147 `him->extent` の字面・whole/half の
   0.750 は (0..1.5)/2 として再導出される）⑶ **下側 pass の二経路を一本化**——旧経路
   （0.6・箱・flat 支持）は両 annotation pass とも profile 供給で**本番到達不能**と裏取り
   して撤去。`_allowPockets` 封印装置（LILYSHARP-OWN）ごと削除（LP に monotone 分岐が
   無いのは支持が常に実 profile だから＝`Interval_set` :672-673 の字面）
   ⑷ **above pass もアウトライン化**——`DynamicHalfWidth 0.75`（dynamic 系最後の名目箱・
   ▶⑶ の残り半分）消滅。0.6/0.46 の分業（aligned_side :361-370 対
   add_grobs_of_one_priority :747-749）は跡地コメントに出典つきで明記。
**延長2（同セッション・ユーザーの perf 質問→実退行を検出して 3 つ直した・全て出力不変）**:

10. ★★★ **pointwise 化の perf 正味を §5.3 の A/B（pre-port worktree `a4c5b721`）で測ったら
    実退行が出た**: dynamics 過多の合成譜（2譜×32小節×毎音 dynamic・~8 system）で
    **51.3ms → 177ms（3.4×）**、fur-elise も 62.3 → 68.5ms。3 つ直して全 3533 緑
    （snapshot byte 不変＝3 つとも出力不変を corpus が確認）:
    ⑴ ★★ **`SkylineMath.Distance` の全対 O(n×m) → merge-walk O(n+m)**（第31セッションが
    名指しした本丸・skyline.cc:617-649 の iterator walk＝**字面化がそのまま高速化**）。
    ⚠️ **罠を 1 つ踏んで学んだ**: kernel は `HorizontalSkyline` と共有で、そちらは
    **lazy（重なったまま）の building 列**＝merge-walk の前提が偽——全対 `Distance` を
    汎用に残し、resolved 前提の **`DistanceResolved`** を `VerticalSkyline.Distance`
    専用に分離（最初の一本化はスペーシング系テストと snapshot 多数を割った——
    falsifier が働いた実例）。177 → 86ms。
    ⑵ **clef 輪郭の resolve キャッシュ**（`SkylineBuilder.PlaceGlyphOutlineCached`＝
    resolve 済み buildings を (quads参照, dir, magnification) でキャッシュ、配置は
    shift/raise コピー——TextOutlineSkylines/DynamicOutline と同じ形・第31セッションの
    +15ms の教訓）。SeedClef は譜×build ごとに数百 edge を sort+resolve していた。
    fur-elise 65.7 → 57.7ms（**pre-port より速い**）。
    ⑶ **下側 pass の profile 建てを「実際に置く (system,staff) だけ」に**——1 個の dynamic
    が居るだけで、下側 script のたびに **placeしない譜まで** `BuildStaffSkylines` を
    建てていた（merge 先の tracker を誰も読まない＝無効果なのに全額払う）。
    **最終**: fur-elise min 51-64ms（pre-port 62.3 と同等以下）／dyn-heavy min ~76ms＝
    **pre-port 比 +47% が残る named cost**——内訳は stacker の per-(system,staff) profile
    建て（実験Aで ~11-17ms）＋ per-dynamic の pointwise 正味（outline merge・支持構築）。
    **次の lever は構造**（下 11 の「seed/draw 単一家」＝spacing が建てた per-staff skyline
    を stacker が再利用すれば profile 建てが消える——perf の半分もそこに掛かっている）。

11. **字面化の残り 1 件と次セッション行き（順不同・どれも「点が先」か「モデルが先」）**:
   - **ossia の pointwise gate 解除**: 下側 pass の ossia 分岐は box のまま。ossia の
     YUp スケール×stacker system frame の整合に測定 regime が無い——**ossia×dynamic の
     対を起票してから**（`DynamicOutline` は size 対応可能な形にしてある）。
   - **DynamicLineSpanner の 1-grob 模型**: LP は hairpin+text を 1 本の spanner に束ね
     て側位置する（define-grobs.scm:1401-1431）。Lily# は per-item。第33セッション残債⑵
     ＝**モデル追加が先**。
   - **`add_grobs_of_one_priority` の l2r polite マルチパス＋rider**（第31セッション残債・
     axis-group-interface.cc:739-767/:776-796）——同一 priority の X 重なり時の巡回順。
     機構は `OutsideStaffSkylines` に既にあり、足りないのは巡回ループ。
   - **seed/draw の単一家**: 下側配置が SkylineBuilder（gap 用）と StackBelowStaff
     （描画用）の 2 回走る。共有関数で綴りは 1 つだが、走らせる家は 2 軒のまま
     （アーキテクチャ移行・出力不変で先に骨格を作る形＝NoteColumnLayout の前例）。
     ★ **perf の残り半分もここ**（上 10 の最終行）: spacing が建てた per-staff skyline を
     stacker が再利用すれば per-(system,staff) の profile 再建（dyn-heavy の ~11-17ms）が
     消える。字面と速度が同じ扉。
   - **下側 profile への slurs/ties/brackets/scripts 合流**（上 7⑶ の非対称解消）——
     発火 regime（下向き slur×dynamic の X 重なり）の対を組めるか検討から。
   - ~~**trill の raw 3.5**（`RawSupportEdgeUp` 最後の消費者）——trill 対の起票が先~~ —
     **閉じた（第38セッション・起票 `2b6fb21d` → port `daeb203c`・TLS/TLB 九桁 exact・
     corpus byte 不変は結果）**。生 3.5 の予約系読みは**残ゼロ**（残るのは
     `SharedRenderer.GraceNotes` の描画側と tab の `RawOutwardTip`＝別島）。

### 第36セッション（2026-07-29）＝ **「stem は支持でない」を実ソースが否定した——\f の着地と \fff の着地は同じ pointwise 計算の 2 つの顔**

★★★ **第35セッション延長の機構主張（「dynamic 側は stem を acknowledge しない」）は誤りだった**。
`8bcf358e`（出力不変・全 3521 緑・snapshot 0 枚）で対 2 冊＋台帳 2 点＋訂正を landing。

1. ★★★ **ソースが逆を言っていた**: `dynamic-align-engraver.cc:108-117` は head と stem を
   **両方** support_ に積み（:222-223 `add_support`）、`grob.cc:81-85` が**全 grob**（Stem 込み）に
   extents 既定の vertical-skylines を与え、`side-position-interface.cc:353-358` は
   **my_dim＝DynamicText の実アウトライン**（define-grobs.scm:1412-1413 spanner は
   from-element-stencils・:1446 text は from-stencil）との **pointwise 距離**を取る。
   ⇒ DSQ の「符頭のみ」は **f の左端の低いインクが細い stem 帯（0.11）に tuck した
   regime の着地**であって機構ではない（§5.2「評価結果を書かない」の実例——
   head-only を構造化していたら DMF regime で割れる欠陥を植えていた）。
2. ★★★ **新対 DMF/DMW（\fff×四分/全音符・予測フォーク先書き）が Branch A を六桁で確定**:
   DMF の text top −10.844670 ＝ **stem tip −10.276 − 0.6 − fff アウトラインの stem X での
   局所差 0.055330**（tuck が今度は「勝つ側」で観測された）。DMF−DMW＝**1.923617** ≫ 0.022285。
   台帳 `staff.staff.dynamic-stem-binding{,-control}`（LP 12.706693／10.783076）。
   **Lily# ミラーは予測が桁まで的中**: 13.738000000（**DSQ/DSB と九桁同一＝scalar 支持の
   label 盲目性そのもの**）残差 +1.031307／10.783000000 残差 −0.000076（DSW の face 欠片と同族）。
3. ★★★ ⇒ **port 設計の訂正（why・probe ヘッダ・`RawSupportEdgeUp` remark に反映済）**:
   「stem を支持から出す」ではなく**「支持を pointwise にする」**——DSQ は head が勝ち
   DMF は stem が勝つ必要があり、**scalar edge はどの値でも両立不能**（それが DMF の存在意義）。
   port の形: 支持 skyline＝per-voice **head インク箱＋実 stem extent 箱**（beamed は quant
   face・DSB dump の −6.74）＋譜 extent 床、**my_dim＝dynamic 自身のアウトライン**、
   ＋**下側 outside-staff pass**（0.46・実プロファイル・DSB）。⚠️ **2 つの半分は同時着地**
   （head-chain 支持だけ入れると DSB の dynamic が beam に乗る＝単独では入れられない）。
4. **実装準備の所見（次セッションの入口）**:
   - ~~my_dim の feta 文字アウトラインは baking 経路に足す形~~ — **焼いた**（同セッション・
     `Extract-EmmentalerSkylines.py` に DYNAMICS 7 文字＋GPOS kern 8 対を追加、
     `GlyphSkylinesGenerated.cs` +646 行・既存データ byte 不変・**まだ何も消費していない**。
     アクセサ `DynamicLetterVerticalSkylineQuads(char)`／`DynamicLetterKern(char,char)`。
     網 `DynamicLetterSkylineTests`＝2 つの独立ジェネレータ（Metrics の箱と Skylines の
     アウトライン）を極値で突き合わせ。⚠️ `MaxHeight()` は**両方向とも実 y で返る**
     （DOWN の極値は box bottom そのもの・skyline.cc:667-680——網を書くとき符号を
     取り違えた実例として記録）。
   - ★★ **合成の X 模型は同セッションで測定済**（新プローブ `dynamic-text-x.ly`・20 ラベル・
     ヘッダに全表）: ⑴ **extX 左端は全ラベル 0.0 exact**＝DynamicText の X-extent は
     **logical rect（pen 走行）**・Y は ink——lsb の張り出し（f −0.408）は extent に入らない
     ⑵ 文字送り＝**hmtx advance＋GPOS kern**（f→f −0.152・m→f −0.116・m→p +0.232・
     r→f +0.116・s→p +0.348 ほか 8 対——測定は全対で符号・桁一致）。pp/fp/sf/sfz は無 kern
     加算 exact ⑶ ⚠️ **実測幅は advance と per-glyph ±0.017ss 以下で両符号にずれる**
     （f −1.3%・p +0.8%＝共通スケールでない）——**Pango 整形量子化の X 側**（Y の 2e-5 族・
     C059 位相 1e-3 族の親戚）。閉形式の復元は無い。⇒ **bake するのはフォントの
     advance＋kern**（それが LP の走らせる計算）。実測幅を焼くのは §5.2 違反。
     ⑷ **LP 2.26 同梱フォントと Lily# bundled は advance/kern/lsb 全一致**を fontTools で
     確認済（「作り直しで metrics が違う」仮説は死んだ）。
   - 描画は serif bold-italic（`DrawDynamics`）＝ **feta を描いていない**。予約=feta 箱／
     描画=serif の mismatch は既存の named debt で、この port では拡大しない（描画面は別 island）。
   - 下側 pass: `StackBelowStaff` の support は譜底平坦＋script 箱のみ（`allowPockets:false`
     の理由）。実 DOWN プロファイルは `SkylineBuilder.BuildStaffSkylines` が既に持つ——
     (system,staff) ごとに配線し、dynamics/hairpins の Place は 0.46（今は登録・衝突とも
     `DynamicLineSpannerPadding` 0.6＝第33セッション残債⑵）、`WidenToNeighbors`
     （LP に無い装置＝下側 pass 欠落の自前補償）を撤去、pockets 解禁（第31セッションの出口）。

### 第35セッション（2026-07-29）＝ **「列の到達距離」の 4 家を 1 軒にしたら、4 家は 1 マスも同じ値を計算していなかった**

★★★ **▶ の `NoteColumnLayout` を単独セッション・出力不変で閉じた**（`300a7f54`・全 3516 緑・
snapshot 0 枚・§5.4 どおり格納値網 12 本を先に書いてから読み替え）。

1. ★★★ **最大の所見: 4 家は骨格だけ共有し、値のセルは全部割れていた**——stem 模型
   （Articulation=実 EndY／Dynamic=生 3.5／Skyline=長さ規則のみ・clamp 無し／Tuplet=実 EndY
   ＋beam face）×head ink（名目 0.5／真側 ink／両側 bbox.Top）×stem 述語の綴り
   （`GetNoteValueFromFraction`≥2 対 `Numerator==1` gate）。⇒ **出力不変で統一できるのは
   「骨格 1 回＋各模型を named read として同居させ、相違表を家の doc に置く」まで**。
   値を 1 本に畳むのは全部 output-moving＝各自の台帳点が先（§5.2.1③）。
   **次の忠実化は「あちらを書き直す」でなく「家の read を 1 行切り替える」**になった。
2. **機構**: `NoteColumnLayout`（record・`Of(item, forcedStemUp, beam, beamStemX)`）＝
   到達側 head 選択・`HasStem`・head ink・beam face・実 stem 端の**単一の家**。
   4 read: `OutwardTipDeviceY`（tuplet encompass）／`StemSupportDistanceDeviceY`（articulation
   支持）／`RawSupportEdgeUp`（dynamics/trill・**最後の生 3.5**・LILYSHARP-OWN 明示＋網が pin）／
   `RendererStemLength`（skyline seed・和音は**per-head**）。**方向解決（多声強制・beam 上書き）と
   beam 所属 lookup は消費者の政策のまま**（key と gate が 3 綴りで違う——Articulation の
   staff-key voice-0 map／Tuplet の `MemberBeam`／Skyline の `BeamedItemsToSuppress`
   knee/cross-staff gate。**ここは統一していない**＝次にこの島を触る人の残件）。
3. **§5.2.1⑥ の死んだコードをその場で落とした**: 到達不能の unreadable-duration 分岐
   （`Of` が note/chord しか作らない）・`QuantizedYPosition` の `noteY`/`anchorPosition` 死引数・
   `ArticulationEngraver.StaffTop`。`beamedTips` map は精算済み tipY でなく
   **(beam, memberX, stemUp) を運び、face は家が read 時に計算**（同値）。
4. **生 3.5 の綴りを潰した後に数え直した**（§5.2.1②）: 残る計算箇所は 4——家の
   `RawSupportEdgeUp`（named read）／`RawOutwardTip`（tab gate・LILYSHARP-OWN）／
   Articulation の null-item legacy guard（実質 dead）／★ **`SharedRenderer.GraceNotes:216`
   （grace の描画 stem が生 3.5×scale）＝島の外（描画側）で見つけた綴り**。grace は
   予約側に対応する家が無く点も無い——grace 島を開く人はここから。
5. **格納値網の期待値は LP 由来で書けた**: 中央線四分の短縮 stem 10/3（台帳
   `staff.staff.tuplet-bracket-shortened-stem` の九桁）・beam face=quant 中心+thickness/2
   （0.48）・LILC ink 0.545。**dynamics の生 3.5 も網で pin**——動かすなら点を開いてから
   （網がその commit を要求する）。

**延長（同セッション・`c5a44c25`）＝ dynamics の点を開いたら、直すべき向きが逆だった**
（⚠️ この延長の機構主張は**第36セッションでさらに訂正**——下の 6・7 の取り消し線参照）:

6. ★★★ **新プローブ `dynamic-support.ly`（~11 秒・3 冊 DSQ/DSW/DSB）＋台帳 3 点
   `staff.staff.dynamic-{head-support,head-support-control,beam-avoid}`**。予測のフォークは
   **ヘッダに無い枝に落ちた**: DSQ の Stem dump は **−6.500000 六桁**（強制方向 quarter は
   確かに full shorten 1.0）——だが **gap はそれを読まない**。~~LP の dynamic 支持は符頭のみ
   （…dynamic 側は stem を acknowledge しない）~~ ⚠️ **← この機構主張は第36セッションで
   訂正済み**（stem は support に居る・pointwise で tuck していただけ。第36セッション節 1）。
   測定そのもの（spanner 近縁 = head ink − 0.6 が両書六桁・黒/全音符インク差 0.022285 が
   gap 差と 15 桁一致）は正しい。DSB は beam face −6.74 − 0.46、
   DSQ の細い stem 尖塔は押さない（f のアウトラインが横に tuck する）。
7. ★★★ ~~⇒ 生 3.5 の port の向きは「stem を支持から出す」＋下側 pass~~ ⚠️ **← 半分訂正
   （第36セッション）**: 下側 pass は正しいが、支持は「stem を出す」でなく **pointwise 化**
   （第36セッション節 3）。Lily# は **DSQ ≡ DSB 九桁同一（13.738000000）**＝
   beam 盲目の構造的恒等（LP は 2.077286 分ける）。residual +2.977210（予測 2.2e-5 まで的中・
   stem-in-support の純量）／−0.000076（**dynamic インクの持参金は face 欠片級**・対照 exact 級）／
   +0.899924（支持規則 7.5+0.6 対 衝突規則 6.74+0.46）。
   **`RawSupportEdgeUp` の remark と pin 網は測った向きに書き換え済み**——次にこの島を開く人は
   3 点の why から。

### 第34セッション（2026-07-29）＝ **描画 bracket の encompass を測ったら、恒等破れが予告どおり鳴った——そして「列の到達距離」の家が 4 つあると分かった**

★★★ **▶ⓐ を起票→移植で閉じた**（`f09abbda`＋`3e78ae2a`・GO 済）。新プローブ
`tuplet-bracket-encompass.ly`（~10 秒・3 冊・**外側 2 音を同音高＝平ら bracket・1 claim 1 量**）
＋台帳 3 点 `staff.staff.tuplet-bracket-{partial-beam,partial-beam-control,shortened-stem}`。

1. ★★★ **LP 側は 3 冊とも予測どおり六〜九桁で分解**: TPB 8.013028＝**quant 済 beam 面@外側
   stem + 1.100**＋half-ink／TPC 6.590000＝clef 束縛（TNC と同値＝恒等 control）／
   TPS 8.111050＝**10/3 + 1.100 九桁**＝**中央線四分の短縮 stem がそのまま encompass**。
   LP に beamed 専用式は無い（`calc_position_and_height:554-561`＝列の実 extent・`:504-509` が
   quantized-positions を先に発火）。
2. ★★★ **恒等破れ falsifier が予告どおり鳴った**（§5.3「同じであってはならない数が同じ」）:
   移植前の Lily# は TPB と TPS を**九桁同一**（8.277737800）に読んだ——LP は 0.098 分ける。
   原因は `CalculateSlope`/`OutwardTip` の encompass が生 `DefaultStemLength` 3.5。
3. **移植**: per-column 実 extent（`ColumnOutwardTip`）——beam member は **beam 模型の
   member X での quant 面**（ArticulationEngraver と同じ正準読み）・unbeamed は
   `StemCalculator.CalculateStemEndY`（短縮・中央線 pull＝描画の家）・stemless／逆向き stem は
   符頭インク。duration 不明は旧来 raw（予約を増やさない）・**tab は旧経路のまま**（点なし・gate 明示）。
4. **着地**: TPS **+0.000021133**（half-ink face 差のみ＝TNB 族）／TPB **+0.000958281**＝
   ★ **実描画で分解済**——番号 2〜8 個目は LP と**九桁一致**（4.335311670）、
   **行頭小節の beam だけ +0.000937 深く quant**（LP は 8 個全部同値）。⇒ **beam 島の量**
   （行頭 beam 対 行中 beam の対を組めば開く）＋half-ink 2.1e-5。**埋めない**（why に記録）。
5. **snapshot 3 枚・全部算術どおり＋png 目視済**: voice-tuplet（bracket が実 stem+1.1 へ 0.67
   降下→tempo 追随→**Main ラベルがポケットを失い上へ**＝頭 +4.25・機構どおりのカスケード）／
   multivoice-tuplet-beams（強制方向 quarter の短縮＝**ちょうど 1.0** 降下）／
   05-special-techniques（入れ子外側 bracket 0.46 降下のみ）。
6. **残した named 負債**: ⑴ **slope 機構が非字面のまま**（graphical dy `:530-549`・beam 連動
   max_slope `:566-630`・**平ら bracket の譜内量子化 `:726-746`**——今回の 3 点では発火しない。
   `CalculateSlope` の LILYSHARP-OWN ブロックに明示・**sloped/譜接 bracket の対が先**）
   ⑵ nested encompass `:646-680`／scripts `:682-706` は従来どおり未移植 ⑶ 行頭 beam quant の
   +0.000937（上の 4）。
7. ★★ **プローブの罠を 1 つ踏んで記録**: 一時 dump を相対オクターブのまま走らせ、`c'` が
   **音高もろとも別の曲**になった（slope が max clamp に張り付いて気づいた）。§5.5 の
   fixture 罠の変種＝**一時テストでも `octave absolute` を書く**。
8. ★★★ **次の島に昇格**: 「列の到達距離」の家が 4 つある（`StemSupportExtent`／
   `ColumnUpEdge`／`SkylineBuilder` の stem seed／今回の `ColumnOutwardTip`）＝§5.2.1② の形。
   **`NoteColumnLayout`（Y 側 column/stem 模型）を単独セッション・出力不変で**——▶ 参照。

### 第33セッション（2026-07-29）＝ **tempo の X を移植したら Y がついてきた——4.76 の正体は「中心化推定幅×行頭 clef」だった**

★★★ **tempo 島の移植（ユーザー指示の島・第32セッションが対を用意済）＝`df72dd5f`・GO 済。**
着地: **TMQ 2.883000（−0.000010＝Lily# の "0" の overshoot 0.033000 対 LP 0.033010・face 床・
0 にしない）／TMT 5.110000（九桁 exact）／新設 X 対 `tempo.x.mark-to-time-signature` 0 exact**。

1. ★★★ **移植前の 4.760000 は「Y の発明」単独ではなかった**: engraver 4.5 ＋ **stacker が
   +0.26**——中心化推定幅が行頭 **clef** と X 重なりを作り clef ink top 1.8+0.46+半箱 0.5 へ
   押し上げていた。LP は **time signature に左揃え**なので clef と重ならず staff+0.8 に座る＝
   **X の移植が Y の着地の前提**（台帳 why に記録）。
2. **機構**: ⑴ 新設 `MetronomeMarkGeometry`＝**1 軒**（\smaller=magstep(−1) の note・
   stem `max(3, log−1)`・upright serif em 2.2（`MetronomeMarkFontSize`=TextScript と同じ
   text-font-size 由来）・DOWN 揃え＝note 底が baseline・ink/幅/静止高）。描画・engraver・
   stacker・CoPlace の**全消費者がそこを読む**（旧: bold 1.8 幅×3 軒＋箱 1.5/0.5＋独自 Y）。
   ⑵ engraver の静止高＝`QuietBaselineAboveMiddle`（staff ink 2.05+padding 0.8+自分の ink 底・
   support は譜そのもの＝metronome-engraver.cc:136-139）⑶ X＝**line-start prefix 表の TimeX**
   （`MultiStaffLayouter.SolveLineStartPrefix` に**共通化**——spring 模型と LayoutMeasures の
   重複ブロックを 1 本化し、annotation ctx へ `PrefixTimeSignatureX` で配線。meter 無し regime
   は最初の musical column へ fallback＝LP の currentMusicalColumn）⑷ stacker の tempo pair＝
   **piecewise stencil**（テキストはアウトライン・note は箱＝vertical-skylines-from-stencil）。
3. ★★★ **TMT が 2 つの既知債を桁で割った**: 移植直後 +0.073000 ＝ **0.040（trill が entry に
   自分の 0.5 を登録——LP の all_paddings は outside-staff-padding 0.46・
   axis-group-interface.cc:747-749,:804。第32セッション残債⑴「0.04 割れる・点なし」の点が
   これ）＋ 0.033（mark 箱底の overshoot）**。⑴ `Place` に **registerPadding**（登録は
   outside-staff 値）→ +0.033000 ちょうど ⑵ trill の**登録 pair をグリフ片＋波片の 2 箱**へ
   （`registerUp/Down`——**自身の配置は extent 箱のまま**＝aligned_side は extent を読む・
   TRF/TRC 不動が要件どおり）→ **九桁 0**。束縛インクは「tr 台地の上の平ら底グリフ」＝LP の
   分解そのもの。
4. ★★ **png 目視が第2の欠陥を捕まえ、その場で閉じた**（§5.0）: trill-spanner A 小節の
   「120」×tr 交差が**移植後も残った**——label 対の tempo は `CoPlaceTempoWithLabels`
   （チャート型 "[A] ♩=120"・LILYSHARP-OWN）を通り、そこが **stacker の解を `Math.Min` で
   捨てて和音とだけ再解決**していた。⇒ CoPlace に **stacked trills を渡して 2 片 profile の
   床を再適用**（`TrillFloorUp`）。目視で交差解消・beat-units/swing/grandstaff/change/
   lead-sheet/piano も目視済（旗の右リーチを `NoteRight` に入れる修正も 1 件）。
5. **snapshot 66 枚が動く理由の分解**（ほぼ全 fixture が tempo を持つ）: ⑴ note が
   1.6 → 3.564（\smaller の正寸・LP の見た目）⑵ 等式が bold 1.8 → upright 2.2 ⑶ X が
   clef 脇 → TS 左（または label 対はチャート位置のまま）⑷ 静止高 4.5/4.76 → 2.883 系
   ⑸ trill 上の text/mark が波の上へ降りる（2 片 profile）。**GO 済・再ベース済**（§6 手順・
   動いたのは 66 枚ちょうど・混入なしを `git status` で確認済）。
6. **残した named 負債**: ⑴ mark の stacker pair の note 片・segno/coda・swing 箱は箱のまま
   ⑵ 下側 pass の dynamics 登録 0.6 は LP の 1-grob（DynamicLineSpanner）と別模型のまま
   ⑶ mid-line meter change の bar に tempo が来ても TS 揃えにならない（command 列模型の欠落・
   §2H ⑶ と同根・コードに LILYSHARP-OWN 明示）⑷ swing 等式は Lily# 固有装置（寸法は
   `SwingNoteSize` 1.6 に隔離）。
7. **チェックリスト済**: 引用ラチェット（+3 を同セッションで 0 に）・§7.5（+461 行に
   REF 11＋OWN 4・全定数導出形）・台帳 3 点の why 更新済・プローブヘッダ更新済。

### 第32セッション（2026-07-29）＝ **staff-padding を測ったら、trill では床が主役ですらなかった**

★★★ **新プローブ `spanner-floors.ly`（12 秒級・4 book）＋台帳 4 点
`trill.{quiet,support}.staff-to-line`／`textspanner.{floor,support}.staff-to-line`。**
第30セッションが名指した「床 3 grob」の残り 2 つ（Trill 1.0／TextSpanner 0.8。
**DynamicLineSpanner 0.1 は `DynamicEngraver.BaselineY` に移植済みと判明**——点は開けていない）。
**起票段は `afd158a5`（出力不変）・移植段は `8d4799b5`（snapshot 4 枚・GO 済）**。

1. ★★★ **予測のフォーク（床 vs 0.46 pass）を外れて第3候補が答えだった**: TRF **3.550000
   ＝譜インク 2.05 + 自分の padding 0.5 + グリフ下リーチ 1.0**。staff-padding 宣言の実効は
   `include_staff`（`side-position-interface.cc:219-222`・`:323-330 set_minimum_height`＝
   **譜 extent が SUPPORT に入る**）で、`:433-453` の refpoint 床は**リーチ >
   staff-padding − padding なら subsumed**。床がそのまま立つのは浅リーチの特殊例
   （TextScript/Ottava/TextSpanner）。TSF は **2.850000 = 2.05 + 0.8 の裸の床**（六桁丸）。
   TRC **9.545000**／TSC **8.555000** は箱上端 8.045 +（0.5+1.0）/（0.46+0.05）に六桁分解。
2. ★★★ **ext dump が LP の trill グリフ配置を確定**: 左バウンドテキストは
   **`stencil-offset (0 . -1)`**（`define-grobs.scm:4068`）＝ **"tr" グリフは線より 1 下**
   （ext (−1.0 . 1.1)）。X は **`make-with-true-dimension-markup`＝アウトライン左**
   （左ループが bbox からはみ出す、と LP 自身のコメント）。
3. **Lily# 移植前実測は 4 点とも予測どおりに割れた**（+0.650000＝発明 2.2／−0.790000＝
   (0.46−0.5)+(0.25−1.0)／−0.040000＝**2つの誤項の打ち消しで小さく見えるだけ**
   （2.05+0.46+0.3 対 床 2.85）／+0.250000＝発明 descent 0.3 対 0.05）。
4. ★★ **移植（`8d4799b5`・GO 済）**: ⑴ **ⓔ の宣言表**を `EngravingDefaults` に新設
   （TextScript/Trill/TextSpanner/Ottava/DynamicLineSpanner の padding・staff-padding・
   stencil-offset を 1 軒に。stacker/各 engraver の ad hoc 定数は全部そこを読む）
   ⑵ trill エングレーバの静止高を aligned_side の字面へ（**support = スパン下の列
   `DynamicEngraver.ColumnUpEdge` ∪ 譜インク**、+0.5+リーチ、床は max で併記。発明
   `StaffPadding+TrillGlyphHeight=2.2` は消滅）⑶ 描画も stencil-offset どおりグリフを
   線−1 へ・stacker 登録 X はアウトライン左・波振幅 0.2 は 1 軒
   （`TrillWaveAmplitude`）⑷ TextSpanner は床を Place 前に（PlaceCustomTexts と同順）・
   facing extent は描画インク（**発明 `TextSpannerAscent/Descent 1.2/0.3` は削除**＝
   ▶⑶ の TextSpanner 項が閉じた）。**4 点とも exact 着地・全テスト緑・動いた snapshot は
   4 枚だけ**（01-expressions／multi-staff-text-spanners／trillspan-lower-staff／
   trill-spanner）。
5. ★★★ **png 目視が回帰を 1 つ捕まえ、その場で閉じた**（§5.0「対応した配置は 1 枚描く」）:
   下段譜の trill は stacker をスキップする（staff-0 のみ）ので、静止高が下がったら
   **符幹がグリフを貫通した**。⇒ ⑵ の列 support をエングレーバに入れて解消
   （下段も stem tip + 0.5 + 1.0 に座る）。**台帳 4 点は列 support を足しても不動**＝
   対の要件どおり。
6. ⚠️★★ **残る近接 1 箇所は tempo 島**: trill-spanner の A 小節で「♩= 120」と tr グリフの
   インクが交差する。**ユーザー指示（2026-07-29）: Lily# の tempo 表記は LP を模倣できて
   いない——直すなら tempo が先**。tempo mark の幾何（中心化された推定幅・priority 押し上げ）
   には触っていない。**tempo 島を開くときにこの近接が最初の点**。
7. **残した named 負債**: ⑴ ~~stacker は単一 pass なので trill は全 entry に 0.5 を払う~~ —
   **半分閉じた（第33セッション）**: 登録 padding は LP の outside-staff 値になった
   （registerPadding・TMT の 0.040 がその点）。**残る半分**＝trill 自身が支えに払う 0.5 は
   単一 pass の近似のまま（LP は aligned_side 0.5 → 衝突 pass 0.46 の 2 段）
   ⑵ 下段譜 TextSpanner はエングレーバの譜下配置のまま（stacker は staff-0 のみ＝
   既存の staff-0 限定の族）⑶ TextSpanner のテキスト em は描画 2.0 対 LP 2.2 のまま
   （ascent 側は点が無い）⑷ `DynamicHalfWidth 0.75` は残る（▶⑶ の残り半分）。

### 第31セッション（2026-07-29）＝ **stacker が skyline を持ったら、残差の正体は「アウトライン」でなく「X 揃え」だった**

★★★ **▶1 の本丸（interval 箱）を 2 段で閉じた**。予測は台帳 why に先に書き、**算術枝が桁まで的中**
（予測 "l-ascender対oco ≈ 2.111・残差 ~+0.006" → 実測 2.111800・**+0.006825**）:

1. ★★★ **道具（`a931104f`・出力不変）**: `TextOutlineSkylines`＝テキスト文字列のアウトライン
   UP/DOWN skyline。`TextFontMetrics.GetTextPath` の**実パスを単一供給源**にし、
   `freetype.cc` の平坦化（max(2, len/0.2)・最終セグメントは両側）と
   `lazy-skyline-pair.hh` の向き分類を字面移植。quad 形式は `FromGlyphOutline` と同一。
   **検証は LP の実 face で**: C059 Italic を同じ walk に通すと **Schola と六桁同値**
   ＝**双子面はアウトラインまで一致**（`TheWalkOverC059` が pin）。LP dump との +0.0011 は
   平坦化位相＋float32＝face の差ではない。
2. ★★★ **プローブの罠を 1 つ検出**: 原点揃えで測ると 0.016 ずれる——
   **LP は同一音符上の 2 つの TextScript を ink 左端で揃える**（TXS/TXL dump・両方 x=21.650926）。
   ⇒ 対を組む・stacker に置く際は**描画の pen 原点**に profile を置くこと。
3. ★★★ **移植（`019027fb`・GO 済）**: `DirectionalOccupancy` →
   `OutsideStaffSkylines`＝**skyline pair のリスト＋エントリごとの padding＋forbidden intervals**
   （`avoid_outside_staff_collisions` の形・nearest allowed move）。TextScript／BarNumber／
   素テキスト mark は**アウトライン pair**・他は従来と同数値の箱 pair（**箱対箱は旧 frontier と
   同値**——だから snapshot は 3 枚しか動かない）。volta は**システムごと 1 つの合成 pair**。
   above の support は**up-skyline を生 merge**（beam の斜面が残る・旧は中点平坦化）。
4. ~~**TXS 着地 +0.006825 ＝ X 揃えの named 残差**~~ — **同セッション延長（`f138c9b2`）で
   X 揃えごと閉じた（下の 8-10）**。中間着地の falsifier は全成立
   （TXL −4.8e-5／TXD/TXP／OTC/OTF 全部不動）。
5. ★★★ **ポケット配置の回帰を png 目視で捕まえた**（§5.0 の「対応した配置は 1 枚描く」）:
   below パスにポケットを許すと **hairpin が pp との隙間に入り加線上の低音符に乗った**。
   原因は below の support が「譜底平坦線＋script」だけで**音符の下インクを持たない**こと
   （LP は support に実プロファイルを持つからポケットが安全）。⇒ **below は単調配置のまま**
   （`allowPockets: false`・LILYSHARP-OWN・**出口は below support への実下側プロファイル合流**）。
   above は up-skyline が音符インクを持つのでポケット有効＝trill-spanner の B/C が
   LP 形のポケットに座った（目視で重なり無し確認済）。
6. ⚠️ **踏んだ罠**: `dotnet build Core` だけして `dotnet test --no-build` すると
   **Tests の bin に stale な Core.dll**（§5.5 の親戚）。偽の回帰を 1 回追いかけた——
   **test の前に Tests プロジェクトをビルドする**こと。
7. **snapshot 3 枚**（全部目視済）: custom-text＝B マークが「poco a poco dim.」の
   x-height 上へ 0.59 詰まる（pointwise text-over-text＝網の機構そのもの）／
   trill-spanner＝B/C がポケットへ・ページ −3.86／navigation-marks＝To Coda +0.02。

**延長（ユーザーの字面監査質問 → `f138c9b2`・GO 済）＝「残差の正体は X 揃え」の X 揃えを
移植したら、その途中で第31段自身の字面欠陥が 1 つ割れた**:

8. ★★★ **`outside-staff-horizontal-padding 0.2` の配線漏れ**（TXL の box−1.6e-5 を
   再導出して発見）。TextScript と **mark 族**（Rehearsal/SectionLabel/Segno/Coda/
   TextMark/JumpScript/MetronomeMark・全部 0.2）が宣言し、`avoid_outside_staff_collisions`
   は profile を **0.2 の平坦＋45° で padded してから** pointwise を取る（`Skyline::padded`）。
   **box regime が box regime なのはこの台地のおかげ**（無 padding だと descender が
   m アーチの斜面に落ちて 0.0165 低い）。⚠️ **前段の tooling テストの「ink 左揃えで LP と一致」は
   2 つの誤りの打ち消しだった**（台帳 why に訂正記録）。宣言なしの grob
   （BarNumber/trill/ottava/dynamics/TextSpanner/volta）は既定 0。
9. ★★★ **X の規則を点で確定**: `self-alignment-X` も `parent-alignment-X` も `#f` なので
   `aligned_on_parent` は**両項不発＝X-offset 0**（`X-align-on-main-noteheads` は alignment が
   数値のときしか効かない）。プローブに NoteHead 行を足して実測——**テキストの x左 ＝
   アンカー符頭の左端・15 桁一致・文字列によらず**（＝pen 基準。字形の side bearing では不可能）。
   新設対 `textscript.x.pen-to-notehead-left{,.descender}`（LP 0.000000・双子は
   「pen が最初の字形の lsb に乗らない」の網）。
10. **移植**: `_"text"` のアンカーを「小節末 −1.0・中央揃え」（LILYSHARP-OWN）から
    「**小節の最初の音符列の原点・Start 揃え**」へ。engraver・draw・stacker が同一 pen を読む。
    着地: X 対 **+8.468502 → exact ×2**／TXS **→ −0.001037**（C059 検証済み walk の
    位相床 1.643938 対 LP 1.644975 そのもの・**fitted 定数ゼロ**）／box-step −6.4e-5
    （padded pointwise 対 box 算術・LP 自身の 1.7e-5 と同桁）。snapshot 4 枚
    （custom-text=音符上へ／volta-labels・04-advanced=0.2 の効き・行頭 B 箱 +0.36／
    navigation-marks 0.02・全部目視済）。
11. ★★ **perf 退行をユーザー質問で測って潰した（`71d35b3c`・byte 不変）**。
    50 回 Layout の最小値×3（7 system・50 小節・§5.3）: 旧 36.4ms → 機構段 36.6-40.8
    → **0.2 padding 段で 51.4-52.8（+15ms 退行）** → 修正後 **38.1-42.5**。
    犯人は ⑴ `Place` がエントリごとに `Skyline::padded` を再構築（→ hPad ごとに 1 回へ・
    `distance(other,hp)==paddedBy(hp).distance(other)` の恒等）⑵ アウトラインを quad で
    cache して配置ごとに resolve（→ **resolve 済み buildings を cache**・配置は
    shift/raise コピー＝単調変換は resolve と可換）。⚠️ **残る +2〜5ms は pointwise 機構の
    正味代金**（support 生 merge＋全対 distance）——次に削るなら `SkylineMath.Distance` の
    全対ループを merge-walk へ（コミットメッセージに明記）。
    ⚠️ **`dotnet test` 総秒数はこの退行を見せなかった**（§5.3 どおり）——プレビュー系を
    触ったら**最小値ベンチを 1 回書いて捨てる**こと。

**残した宣言済みの負債**（コードに named・全部「支え側」）:
- ⚠️ **`add_grobs_of_one_priority` の l2r polite マルチパスと rider を移植していない**
  （`axis-group-interface.cc:739-767` の `last_end`＋skip ループ＝同一 priority 内で
  X 重なりの grob を次パスへ回す／`:776-796` の rider skyline merge）。Lily# は type ごとに
  リスト順で 1 列に置くだけ。**割れるのは同一 priority の grob が同じ system で X 重なり
  するときだけ**（現コーパスでは snapshot 不動＝結果であって構成ではない）。
  次に stacker を触る人はここから——機構は `OutsideStaffSkylines` に既にあるので、
  足りないのは巡回順序の 1 ループ。
- ~~**TextScript の X 揃え**~~ — **閉じた**（延長 `f138c9b2`・X 対 exact ×2）。
- **below support に下側実プロファイルが無い**（→ ポケット封印の出口）。
- **箱 pair のまま**: dynamics（グリフ）・ottava・TextSpanner（定数負債 ⑶)・boxed mark・
  segno/coda。**trill は第33セッションで 2 片箱（グリフ台地／波）へ・tempo は piecewise
  stencil（テキスト＝アウトライン・note＝箱）へ**——残るのはグリフ片の実アウトライン。
- **support の遠端は通過不能**（LILYSHARP-OWN）——support が実プロファイルを持てば消える。

### 第30セッション（2026-07-29）＝ **0.26 を割ったら「seed と draw は共有」という前セッションの読みが誤りだった**

★★★ **▶⓪ の手順（beam 下端を dump して引き算）どおりに割れた。beam は無罪**——
Lily# の描画 beam 下端は **3.240000 = LP と六桁一致**。0.26 は 2 欠陥に割れた:

1. ★★★ **「seed と draw は NumberYUp を共有」（第29セッション・台帳の why）は細部が誤り**。
   実体は**別の regime を通る 2 回の `Calculate` 呼び出し**だった:
   - **draw**（最終パス・beam あり）: 中心 = beam 縁 + **(0.5 + digitHeight 1.7 − 0.8) = +1.4**
     ＝ **もう存在しないレンダラー text offset の補償**（`99ecd3aa` が
     `VerticalAnchor.Middle` 描画にした時点で根拠が消えていた）→ 4.640（誤差 +0.300）。
   - **seed**（per-staff パス・`beamLayouts: default`）: beamed 分岐に入れず fallback＝
     **生の `DefaultStemLength` 3.5 + padding 1.1 = 4.600**（誤差 +0.260 = 台帳の残差**そのもの**。
     quant 済 beam 縁 3.24 との差 0.04 は台帳の外でだけ見えていた）。
2. **移植 2 つ**: ⑴ engraver の beamed 分岐＝**中心を「quant 済 beam 外縁 + TupletBracket
   padding 1.1」（不可視ブラケット位置・tuplet-number.cc:342 midpoint）**へ。
   ⑵ `StaffTupletBracketLayouts` が **`StaffBeamLayouts` を受け取り**、seed が draw と
   同じ分岐・同じ beam モデルを通る（⚠️ **tab 譜は fallback のまま**——trivial system の
   `StaffIndex` 0 が engraver の tab guard を譜の位置で反転させる・点も無い。why に明記）。
3. **着地 +0.0000208 ＝ 番号の half-ink face 差**（0.627738 対 0.627717・歌詞 −0.000100 と同族。
   **0 にしない**）。網 `BeamedTupletNumber_CentresOnTheInvisibleBracket_BeamEdgePlusPadding`
   （上下両 arm・1.1 は測定値として直書き）。
4. **snapshot 6 枚**が予測どおりに動いた: 下向き番号 −0.3／上向き −0.6（旧 clearance 0.5 →
   1.1）／`tab-as-numbers` は **tab 譜が 1.52 上がった**＝記譜譜の番号 seed が生 stem tip 予約
   だった過剰分の解消（描画インクとの一致）。重なりは無し（目視確認済）。
5. **台帳印字**: 156/190 exact・|residual| 総和 **0.450268**（163 距離・TNB/TNC の 2 点が
   前回印字後に増えた分を含む）・counts 25/27。**動いたのは TNB の 1 点だけ**。
6. ⚠️ **同型の残り**: engraver の **tab 分岐**はレンダラー offset 補償（+0.3/−0.8）を
   まだ持っている——draw が `VerticalAnchor.Middle` になった今、tab の beamed 番号は
   描画位置も補償分ずれている疑い。**点が無いので点が先**（digitHeight 1.7 も tab 分岐だけに残存）。
7. ★★★ **同セッション延長で ▶1⑴「staff-padding の refpoint 床」も閉じた**。
   `PlaceCustomTexts` が **anchor（baseline）を「譜インク縁 2.05 + staff-padding 0.5」で
   床上げしてから Place に渡す**＝`aligned_side` の順序（床が先・0.46 の outside-staff
   raise はその上から）。`textscript.no-descender` **−0.007000 → 0.000000000（九桁）**
   ——床は両側とも 2.0+0.05+0.5 の正確な算術なので exact。**snapshot 197 枚は全て byte 不変**
   ＝コーパス内で床が bind するのはプローブ regime だけ。
   網 2 件: `CustomTextWithoutDescender_SitsOnTheStaffPaddingFloor`（床 regime）／
   既存の descent 摂動網は**frontier を上げた regime へ移した**（素の譜上では対の差が
   「床対エッジ」= LP 自身の 0.404430 になり、生の descent 差 0.4114 ではなくなったため）。
8. ⚠️ **床は TextScript だけに入れた**。LP が同じ床を宣言する stacker 配下の残り＝
   **OttavaBracket 2.0／TrillSpanner 1.0／TextSpanner 0.8／DynamicLineSpanner 0.1**
   （`define-grobs.scm` 全数走査済・定数のコメントにも明記）。**点が無いので点が先**——
   盲移植は各 regime の出力を未測定のまま動かす。**→ Ottava は同セッションで点を作って閉じた（9）。**
9. ★★★ **さらに延長で Ottava の床も「点が先」どおりに閉じた**。新プローブ
   `ottava-floor.ly`（7 秒級・2 book）＋台帳 2 点 `ottava.{floor,support}.staff-to-line`。
   アンカーは**破線の線そのもの**（`ottava-bracket.cc` は線を stencil Y=0 に置き
   ラベルのインクを線に中心合わせ・hook は Y-extent を持たない）。LP 実測:
   **床 4.050000（六桁丸＝譜インク 2.05 + staff-padding 2.0）／支持 5.777520
   （列上端 4.485489 + padding 0.5 + ラベル半インク 0.792031＝エッジ制約）**。
   ★ **falsifier が半分鳴って、それが所見**: Lily# は 4.000000 を読んだ——stacker のエッジ
   （予測 3.3）ではなく **`OttavaBracketEngraver.AboveStaffYUp = StaffPadding` が既に床を
   持っていて、基準が譜インクでなく上端線**だった＝残差 −0.05 は線中心対インク縁の
   半太さ（2.05 対 2.0 の既知族）。⇒ 移植は `StaffLineThickness/2` を above/below 両方へ
   （**1 claim の 2 つの半分**＝§5.0 の cap/baseline の教訓で分割しない。below は未測定と明記）。
   **床側 exact 着地・支持側は移植前後で不動 +0.027480**（対の要件どおり）＝
   **box対outline支持 +0.0595／padding 0.46対0.5 −0.04／hook 0.8対半インク +0.008 の net**
   （why に分解記録・**埋めない**——interval 箱の島で割れる量）。
   **snapshot は 1 枚だけ**（multi-staff-ottava の 8vb が +0.05 下へ＝below 双子の動き。
   8va 側は支持 regime 5.80 で不動が正しい）。
10. ⚠️ **アクセサの罠を 1 つ記録**: ottava の破線は**長い水平 0.1 罫**なので
    `StaffRefpoints` の譜線述語がそれを 6 本目に数えてページを拒否する。
    `OttavaLineAboveStaff` は**左端で分別**（譜線=システム左端から・ottava=ラベルの後から）
    する自己完結型にした。**trill の波線も同じ述語に掛かる**——trill の点を作る人は同じ罠。
11. ★★ **字面度の自己監査（ユーザー要請）と、除去したハック 1 件**。
    除去済: **seed の `staff.IsTab ? default : beamLayouts` ガード**——根本原因は
    `StaffBeamLayouts` が trivial system の `StaffIndex` 0 で返すことだったので、
    `StaffTupletBracketLayouts` が**実 staff index に再スタンプ**してから engraver へ渡す。
    tab の seed も draw と同じ tab-beam 縁の分岐を通るようになり、**全 snapshot byte 不変**
    （tab の tuplet 予約はどの fixture でも binding していない）。
    **除去しなかったもの（未測定 regime を動かすため・点が先）は ▶ の「字面度負債」へ。**

### 第29セッション（2026-07-29）＝ **「Own tuning」3 定数を測ったら、em の取り違えが 3 例目として落ちた**

★★★ **新プローブ `textscript-ink.ly`（3 秒級・4 book）＋台帳 4 点 `textscript.*`。**
Lily# の `_"text"`（CustomText）＝ LP の `^\markup \italic`（TextScript）。対の設計は
「descender の有無だけが違う 2 冊」＋「2 段重ね（箱が成立する組／しない組）」。
**予測 4 本とも的中し、外れた細部（TXD が edge 制約でなく別の床に座った）が第 2 の機構を出した**:

1. ★★★ **LP の staff-padding は grob の refpoint（baseline）に掛かる床**
   （`side-position-interface.cc:401-453 aligned_side`・コメント自身が
   "Ensure 'staff-padding' from my refpoint to the staff"）。TXD "dolce" = **2.550000 六桁丸**
   = 譜インク 2.05 + 0.5。**Lily# はこの床を持っていない**（下の残債 ⑴）。
2. **outside-staff-padding 0.46 はエッジに掛かり、baseline は文字列自身の descent に乗る**：
   TXP "poco" = **2.510000 六桁丸** + descent 0.444430。Lily# の `Place()` の形
   （frontier + 0.46 + descent）は**この regime では LP の binding 制約そのもの**で、
   descent だけが文字クラス定数だった。
3. ★★★ **TextScript の em は 2.2**（font-size 宣言なし＝ text-font-size 11pt ÷ 5pt/ss）。
   プローブの 4 つの独立した読み（poco descent／dolce ascender／mum x-height／overshoot）が
   **全部 2.2000x** を返した。Lily# の 2.4 は**歌詞 3.2・和音 2.6 に続く取り違えの 3 例目**
   （§5.0 の「フォント量札は弱い」がまた当たった）。`EngravingDefaults.TextScriptFontSize`
   1 本に統一（描画＝予約）。
4. **移植の着地（全予測どおり）**: descender **+0.155570 → −0.000030**・box-step
   **+0.921552 → −0.000048**（em と face の照合）／no-descender **+0.560000 → −0.007000**
   ＝残差全体が ⑴ の staff-padding 床の欠落**そのもの**（この点がその網）／outline-step
   **+0.755025 → +0.420825**＝**箱対アウトラインの名前付き残差**（LP は
   `add_grobs_of_one_priority` でテキストの**アウトライン skyline** を pointwise に
   突き合わせる。Lily# の `DirectionalOccupancy` は interval 箱＝構造的に読めない。
   **⚠️ 0.42 を定数で埋めない**——文字列対ごとの量。box-step の双子が −4.8e-5 に居るので、
   アウトライン移植以外でここが動いたら fitting）。
5. **tuplet seed は SkylineBuilder の正規予約（数字のインク箱＋線の外縁）の鏡写しになり、
   出所不明の `+0.1` は消えた**。volta/ottava/mark も各描画 face の ink に置換。
   網 `CustomTextDescent_ComesFromTheStringsOwnInk_NotAClassConstant`（§5.4 の摂動型）。
6. ⚠️ **引用ラチェットの罠 2 つ**（§5.2.1⑦ の運用知識）: 末尾アンダースコアのメンバー名
   （`default_outside_staff_padding_`）は **SymbolPattern が構造的にマッチできない**
   （`_` は \w なので \b が立たない）→ getter 名で引く。単語 1 個の grob 名（TextScript）と
   2 節ハイフン（font-size・Y-extent）は「名前」に数えられない → 3 節トークンを同じ行に置く。
7. ★ **ユーザー指摘（GO 済・`53930190`）**: volta 番号が「あるべきより少し下」。**em port は
   無罪**（snapshot 座標は移植前後で同一）で、**前からの描画の性質**——
   `VerticalAnchor.Hanging` は 3 backend とも **typographic ascent 落とし**で実装されており
   （契約の「y=グリフ上端」と不一致）、cap 高しかない数字は線から ≈0.55 下に落ちていた。
   ⇒ **baseline アンカー**（唯一 backend 一様）で `線 − 0.3 − Ink.Top` に置き、インク上端が
   ちょうど 0.3 下に。**同日入れた予約式 0.3 + InkHeight と構成上一致**した。
   ⚠️ **Hanging を使う新規描画は同じ罠を踏む**——予約と合わせるなら baseline で書く。
8. ★★★ **同セッション延長で beamed tuplet の番号も閉じた（`b5a69388`＋`62674f98`・GO 済）**。
   新プローブ `tuplet-number-beamed.ly`：LP は **beamed の番号を beam 下端 + padding 1.100 の
   不可視ブラケット位置に置き**（2 つの音楽で六桁）、**普通の譜 skyline インクとして数える**。
   `staff.staff.beamed-tuplet-number` は Lily# が**自分の control と九桁一致**（番号がどの
   skyline にも居ない）→ SkylineBuilder の `!ShowBracket` skip を「線だけ」に絞って
   **−1.434229 → +0.260021**。⚠️ **残る +0.26 は予約でなく描画位置**（seed と draw は
   NumberYUp を共有）＝ **Lily# は beamed の番号を LP より 0.26 深く描く**。engraver の
   不可視ブラケット Y か beam 自身の Y かは**未分割**——次の一手はこの fixture で Lily# の
   beam 下端を dump して引き算。⚠️ プローブの罠を 2 つ踏んで記録済（ヘッダ参照）:
   treble×treble だと **clef 対 clef 7.210039+1 が番号に 0.0023 差で勝ち**、両書同値の
   「もっともらしい 8.210039」を返す／control は beam でなく **clef 3.540 対 譜線**が
   binding（`staff.staff.beamed-tuplet-control` −0.006512 = clef 族の欠片の網を兼ねる）。
9. **mark 幅も描画 style の advance へ**（`9588ef79`）——SerifBold 推定は BoldItalic 描画と
   別 face だった。**byte 不変は結果**（重なり判定がどの fixture でも反転しなかった）。

**残した宣言済みの負債**（このセッションで名前を付けた・未着手。**次セッションの推奨順**）:
- ~~⓪ **beamed tuplet 番号の 0.26 を割る**~~ — **閉じた**（第30セッション・+0.0000208 に着地）。
- ~~⑴ **staff-padding の refpoint 床が無い**~~ — **全部閉じた**（TextScript/Ottava は
  第30セッション・**Trill/TextSpanner は第32セッション（4 点 exact）・DynamicLineSpanner は
  `DynamicEngraver.BaselineY` に移植済みと判明**）。⚠️ trill で分かったこと: 深リーチ grob
  では床でなく **include_staff＋自分の padding** が効く（第32セッション節 1）。
- ~~⑵ **stacker が interval 箱**~~ — **閉じた**（第31セッション・+0.006825 に着地＝残りは
  X 揃えの named 残差）。**終着点の「SkylineBuilder との skyline の家 1 つ」（§5.2.1②）は
  まだ先**——stacker は skyline を持ったが、seed（support）はまだ SkylineBuilder の鏡写しの
  箱で、below support は下側実プロファイルを持たない（第31セッション節の負債）。
- ⑶ ~~`TextSpannerAscent/Descent`（1.2/0.3）~~ — **削除済**（第32セッション・facing extent は
  描画インクへ。TSC の +0.25 がその網だった）。**残るのは `DynamicHalfWidth`（0.75）**。
  mark/volta/ottava の**描画サイズ自体**（2.8/2.4）と TextSpanner のテキスト em
  （描画 2.0 対 LP 2.2）も Lily#-own のまま＝em を測って移植済なのは text script と
  **tempo（第33セッション・em 2.2 へ）**だけ。
  サイズを直すなら各 grob の LP 宣言から em を導出して点を先に。

### 第28セッション（2026-07-29）＝ **和音記号の幅を初めて測ったら、weight のほかに 3 つ落ちた**

commit の一覧はアーカイブ行き（`c4da07d0`〜`ab0047be` の 6 本＋台帳 `chord.symbol-width.*` の
`why` に全部ある）。以下がセッションの中身。

★★★ **新プローブ `chord-symbol-width.ly`（13 秒級・5 book）＋台帳 3 点
`chord.symbol-width.{minor-pair-gap,quarter-spring-control,half-spring-control}`。**
量は**同一テキストの隣接和音記号のギャップ**（両者とも ink-left アンカー＝規約が消え、
rod が binding なら `w + 0.5 + 0.5 + 0.1` で**幅そのものが読める**）。予測は桁まで的中
（+0.162120）。**対が出したもの**:

1. ★★★ **プローブ側の罠（最大の所見）**: `-dbackend=svg` では LP が **`fonts.sans` を
   generic "sans" に落とす**（`ly/paper-defaults-init.ly:174-177`）＝ fontconfig が
   **このマシンの Verdana** を掴み、ext("Am") が 4.336200（正典 3.926480）になっていた。
   ⇒ probe に pin を追加（page-vertical.ly の serif pin と同型）。
   **page-vertical.ly にも sans pin を追加して全 62 冊を再測定**（20 分・完走）——
   **動いたのは LYRMC の 1 点だけ**（4.608814 → 4.585369＝旧値は Verdana 汚染。
   LYRCH/LYROS の 4.027851 は不動）。⚠️ **今後 sans テキストを測る probe は必ず pin。**
2. **weight**: LP の ChordName は **font-series 宣言なし＝regular**。Lily# は SansBold＋
   stale な素の 2.6 が 6 箇所（SpacingRules ×2・LayoutEngine・MusicMarkEngraver ×3）。
   ⇒ 移植: `EngravingDefaults.ChordNameFontStyle`（regular）＋
   `ChordNameEngraver.SymbolInkWidth` の**単一の家**に統一（描画・予約・spacing・衝突箱）。
   `minor-pair-gap` **+0.162120 → −0.002097**＝sans face 床（歌詞の −0.000100 と同族。
   **0 にしない**）。LYRMC も同じ床 **−0.002157** に着地。
3. **rod padding 0.1 の欠落**: LP は列 rod に spanner padding 0.1 を足す
   （`spacing-spanner.cc:315-316` `set_column_rods`）。`ApplyChordRowSpacing` に移植。
4. ★★★ **和音行の時価ばね——同セッションで機構を割って閉じた（両控え exact）**。
   ギャップは 1 本のばねではなかった: **staff-less 行では拍ごとの空 command 列が
   刈られず生き残り**（`is_loose_column` は note column の neighbor を要求・
   `spacing-determine-loose-columns.cc:82-90`）、各拍 = `musical→command`
   （素の duration space・**wishes=() を dump で確認**）＋ `command→musical`
   （dt=0 の `min+0.5`・`spacing-basic.cc:71-77`）。ALLCOL dump が starter 無し列を
   全 musical 列の 0.5 左に実証。4 regime 全て `duration space + 0.5` に六桁分解
   （末尾 whole→bar 5.298045 のみ +0.5 なし＝bar が command を兼ねる）。
   ⇒ 移植 2 つ: **⑴ `ApplyLeftHeadWidth` は spacer rest を left head にしない**
   （行は不可視 spacer rest 刻み——**彫られない半休符のグリフ幅 1.5 を徴収していた**。
   `s` 休符にも同じ規則）／**⑵ `ApplyRowCommandColumnSprings`**（列間ばねへ 0.5 を
   直列合成・強度も +0.5）。⚠️ **旧 −0.200000 は 2 欠陥の打ち消し**（幻の休符 +0.3 −
   欠けた 0.5）＝「clean な単独項」に見えた読みは誤りだった。
   quarter −0.409108 → **−1e-9**・half −0.200000 → **−1e-9**（予測どおり exact）。
5. **ユーザー指摘（実 GO 済の修正）**: リードシート最終小節の 1 拍目（"you"/"C"）が
   小節線から 4.52 ss（他小節 2.87/2.94）。犯人は `EnsureLeadSheetBarWidth`
   （LILYSHARP-OWN の 10 ss 床）の**全ばね等分**——1 列小節では床の半分が 1 拍目の前に
   入っていた。⇒ **不足分は最後のばね（末尾余白）だけへ**。⚠️ 内側ばねに入れると
   4 の控え点を汚す（一度やって落ちた）。網 `LeadSheetBarFloor_IsTrailingRoomOnly`。
6. ★ **副次の確定**: LP の ChordName ext ＝**素の文字列幅そのもの**（Ignatzek markup の
   空要素・hspace・super は幅ゼロ＝probe CAL の PLAIN 較正で恒等）。正典値は
   Nimbus Sans regular の advance（Pango 量子化 ±0.005/glyph）。

**残した宣言済みの負債**（このセッションで名前を付けた・未着手）:
- `Widen` は rod を **ideal にも** max で乗せる（LP の rod は min のみ）。force 0 では同値、
  伸長行で配分が割れうる。§2H の発明群の隣。
- `LayoutEngine`/`MusicMarkEngraver` の和音衝突箱は `cn.X` を**中心**扱いのまま
  （ink-left 化 `dcbf08e9` の取り残しの疑い。点が無い）。
- 台帳 quarter-spring-control の why に「真値は遮蔽」と明記——割るには記号なし／
  細い記号の regime が要る。

### 第27セッション（2026-07-28）＝ **clef シルエット移植を landing。点を先に作る値段は 13 秒だった**

**commit 2 本**: `f0582bbd`（**道具のみ・出力不変**）／`ceb73c30`（**移植＋台帳 4 点＋snapshot 2 枚**）。

★★★ **▶0 の「点を開く値段を下げる」は当たっていた。ただし見積りが 6 倍甘かった。**
`Measure-LilyPondProbe.ps1`（~80 行・単一プローブを raw で走らせるだけ）に加えて、
**`Measure-LilyPondPageGeometry.ps1` は最初から `-Probe` を取る**ので、
**PROBEV 形の新プローブはそのまま既存パーサに食わせられる**。実測 **13.5 秒**
（`page-vertical.ly` の 20 分に対して）。⇒ **専用プローブの値段はもう「1 ツールコール」ですらない。**

★★★ **新しい点 `system.clef-floor.*`（プローブ `probes/system-clef-floor.ly`・book SCF/SCC）。
これが移植を LP に対して初めて測った。**

| | LP | Lily# | residual |
|---|---|---|---|
| 箱（HEAD） | 8.316000 | 8.890000 | **+0.574000** |
| シルエット（移植後） | 8.316000 | 8.359376 | **+0.043376** |

⇒ **移植は 0.530624 を LP に向かって閉じる。** snapshot 2 枚は Lily# 対 過去の自分なので
**どちらが正しい向きかを言えなかった**——その 1 点だけが足りなかった、というのが ▶ の診断どおり。
だから歌詞 3 点が **+0.165349 → +0.271310** に**上がったのは移植が効いた証拠**（drift は
予測どおり +0.105961 ちょうど）。**台帳の |residual| 総和は 2.606965 → 2.968224**（§5.3）。

★★ **「2 譜なら床は自然に binding する」は誤りだった**（引き継がれていた前提・§5.0 の
「stale になるのは数だけでなく理由もである」の実例）。`build_system_skyline`
（`page-layout-problem.cc:1080-1127`）は **up を最初の spaceable 譜・down を最後の spaceable 譜**に
上げ直すので、**system の高さは距離に入らない**。⇒ 出荷紙では clef どうしは
`3.776 + 3.540 + padding 1 = 8.316 < basic-distance 12` で**原理的に床が binding しない**。
**床を binding させたのはインクを足すことではなく ideal を消すこと**——
`system-system-spacing` の `basic-distance`/`minimum-distance` を 0 にすると
**読みが `ensure_min_distance` の引数そのもの**になる（padding は出荷値 1 のまま）。
⚠️ **Lily# 側は `LayoutEngine.cs:926-993` の単一ページ経路**——コーパスは
`UseOptimalPageBreaking` を立てないので、**page.\* の点は全部この経路**を通る。
★ 対は **SCC（同じ音楽・出荷 spacing ＝ ideal 12.000000）**。これが無いと
「床に座っている」という主張の裏が取れない。頭の refpoint と system 本数も添えてある（罠 8）。

★★★ **予測は外れ、その外れが所見だった**（§5.0-2 の実演）。
SCF は「アウトラインなら箱より 0.105961 低い」と予測して書いたが、**LP は箱の和ちょうどを返す**。
理由は **horizon padding**——`skyline.cc:557-615 Skyline::padded` は各 building を
**まず平坦に `horizon_padding` だけ広げてから 45° で落とす**ので、1.0 では clef の頂点
（x=2.228）の平坦部が **x≈1.23〜3.23** を覆い、**最深点 x=1.84 を丸ごと飲み込む**。
⇒ ★★ **system 段では箱とシルエットは同値。差が出るのは padding 無しの
`align-interface.cc:228`（譜と譜）だけ**（第26セッションの `skyline-binding.ly` はそちら）。
⇒ この点は今後 **「Lily# の水平 padding が平坦＋斜面になっているか」の網**として働く。

★★★ **残り +0.043376 も同じセッションで割れた。正体は clef ではなく「小節番号の書体」**
（台帳の `why` に算術ごと記録・`OPEN:` は解消）。**割り方は摂動でも算術でもなく
`LayoutEngine.cs:965` の呼び出し地点に transient dump を挿しただけ**（台帳の
`line-start.clef-to-first-note.treble` が使っているのと同じ手）:

1. ★★ **padding 無しの距離は 7.210039** ＝ **LP が第26セッションに答えた
   `7.210038725633767` と六桁一致**。⇒ **シルエットのプロファイル自体は完全に正しい。
   移植にはもう答えるべきものが残っていない。**
2. **当たりは x=1.378816 で、そこの up は clef ではなく小節番号の 45° padding 斜面**:
   `2.350000 − 0.378816 = 1.971184` ＋ clef の down `5.388192` = **7.359376**（六桁ちょうど）。
   LP の小節番号は同じ場所で **2.305433** しか無い（dump の
   `BarNumber ink about refpoint = [3.050000, 4.305433]`・上端線は 2.0）ので
   `1.926617 + 5.388192 = 7.314809` ＜ **clef 自身の平坦部 `1.776 + 5.540 = 7.316000`**
   ⇒ ★ **LP 側では小節番号はそもそも当たっていない。Lily# 側だけが紙一重で clef を抜く。**
3. ⇒ **これはフォント量**で、`barnumber.*.staff-to-baseline`（−0.024440）と**同じ 1 つの書体の
   2 つの読み**: Lily# の数字は**baseline が 0.024440 低く、インク上端は 0.044567 高い**
   ＝ **自分の baseline から 0.069 ほど背が高い**。⚠️ **閉じるのは書体メトリクス側**
   （小節番号のインクを face から読む）で、**この点に定数を合わせない**。

⇒ ★★ **繰延 ⑴（平坦化の量子化）は容疑から外れた**——padding 無しが六桁で合う以上、
量子化はこの点に効いていない。**着手根拠は ossia の実害だけになった**（優先度は下がる）。

★★★ **そしてその場で閉じた（同セッション・ユーザー GO 済）。** 小節番号の予約インク高さが
`LayoutEngine` の 2 箇所で**素の `1.3`** だったのを **`TextFontMetrics.Ink(...).Top`** にした
（**幅は最初から同じ face の同じ呼びで測っていた。高さだけ取り残されていた**）。
LP の実測は **baseline 3.076208 ／ インク上端 4.305433 ＝ 1.229225**（books BNL/BNH）。

⚠️★★★ **そしてそれは「半分」で、半分だけ入れると別の点が悪化した**（§5.0 の
「1 つの claim が N 個の量に分かれているとき、分割すると悪化することがある」の実演）。
**小節番号の face は cap 高さと baseline のはみ出しの 2 つで 1 つの主張**だった:

| 点 | 直す前 | cap だけ | **cap＋baseline（landing 済）** |
|---|---|---|---|
| `barnumber.{low,high}-melody.staff-to-baseline` ×2 | −0.024440 | −0.024440 | **+0.000006** |
| `system.clef-floor.floor-bound-distance` | +0.043376 | 0.000000 | **+0.004090** |
| `lyrics.*.system-gap` ×2 | +0.207200 | +0.143468 | **+0.167914** |

**予約するインク上端で読むと 2.350000 →(cap のみ) 2.286268 →(両方) 2.310714** に対し
**LP は 2.303666**。⇒ **cap 単独は答えを行き過ぎ**、`system.clef-floor` が exact に見えたのは
**2 つの誤差の打ち消し**だった。⚠️ **中間状態を「良い方」と読まない。**
⇒ ★ **§5.0 のこの規則は「移植」だけでなく「台帳の読み」にも効く。**

★★ **`BarNumberEngraver` は「Lily# には数字の下はみ出しの実測が無い」と書いていたが、
もう嘘になっていた**（`TextFontMetrics.Ink` が実パスを測る＝丸い数字 0.024446／`1` は 0）。
LP の規則は **「インク下端＝五線インク 2.05 ＋ padding 1.0」で baseline はそこに数字自身の
はみ出しを足した所**（LP の dump も numeral ごとに 3.074440／3.076208 と変わる）。
⇒ **`BarNumberTests` の主張も「baseline」から「インク下端」へ直した**——
**テスト名（`BarNumberInkBottom_...`）が最初から正しいことを言っていた。**

⚠️ **副作用が 1 つ出て、それは欠陥ではなく残差の別の顔**: Lily# の system gap が
**numeral ごとに不揃いになった**（12.167914 / 12.143468 / 12.167914）。
LP も予約は numeral ごとに変わるが、**LP ではその床が binding しない**（ideal 12.000000 に座る）
ので一様に見える。⇒ `lyrics.two-verse.system-gap` は `StaffGapAt(0)` に切り替え、理由を書いた。

snapshot **合計 16 枚**（cap 段で 4 枚＋baseline 段で 12 枚）。
台帳 |residual| 総和 **2.968224 → 2.801498**・exact **127 → 127**（barnumber 2 点が exact 級に
入れ替わり、`system.clef-floor` が抜けた）。
⚠️ **私が cap 段で「台帳の 0.046334 は 0.017398 の過小評価」と訂正したのは誤りで、
0.046334 のほうが正しかった**（2.350000 − 2.303666 ちょうど）。**台帳で再訂正済。**
⚠️ **残っているのは全部 face の差**（インク上端 0.007048・はみ出し 0.000006）。

★★★ **同じ型で歌詞と和音記号の族も閉じた（ユーザー GO 済）。台帳総和 2.80 → 0.012541・
exact 129/154・残る最大 0.004090。** どちらも**書体の差ではなく定数の取り違え**だった:

| | Lily# | LP の宣言 |
|---|---|---|
| 歌詞の em | **3.2**（予約側と描画側で**2 つ**持っていた） | `LyricText font-size 1.0` ⇒ **2.469417**（**29.6% 大**） |
| 和音記号の em | **2.6**（`LILYSHARP-OWN` だが**自分のコメントが引用した規則の近似**・こちらも 2 つの家） | `ChordName font-size 1.5` ⇒ **2.616256** |

**正しい em で*同じ face* を実測すると LP と一致**（歌詞 `Ink("no").Top` = 1.187789 対 1.187880）。
⇒ `EngravingDefaults.{LyricTextFontSize,ChordNameFontSize}` **各 1 本**。
`LyricUpExtent` は **`LyricDownExtent` と同じハイブリッド**（アウトライン実測＋CJK は宣言値で床）。
★ **「CJK の受け皿を決めるのが先」で何セッションも止まっていたが、受け皿は同じ関数の
下側に既にあった。** 文字クラス表（`AscenderEm`/`XHeightEm`/`AscenderLetters`）は**削除済**。
⚠️ **1 点だけ上がった**: `chord-row.between-systems` は**フォント 3 項の打ち消し**だったので
歌詞面が正ると解けた（+0.002047 → +0.011320）。**和音 em を直して +0.000963 に戻った。**
⚠️★ **和音記号の weight は直していない**——LP は `font-series` を宣言しない（＝normal）が
Lily# は **bold**。**コーパスはアンカーしか測らず記号の幅は打ち消される＝点が無い。**

⚠️★★ **量を直すと、それを直書きしている網が一斉に落ちた（5 件）。全部「測っていない」向き。**
`RenderedGeometry.LyricSyllables` が音節を `FontSize == 3.2` で選別（18 点が「音節が
描かれていない」で落ちた）／PDF テストの 19.2pt／skyline テスト 2 件の 2.6／
`LyricStaffOrderTests` は**床が binding しなくなり両書とも ideal**（**これは正しさ**——LP も
そう読む。book SCF と同じ「ideal を 0 にした紙」へ移し、regime の assert を足した）。
⇒ **§5.0 に汎化済。**

**移植が残した繰延 2 件**（第26セッションの §7.5 読み直しで宣言済・**landing 後の今が着手時期**）:

1. ⏳ **量子化数がフレーム依存なのに焼き込みは全サイズ 1 種類**（`freetype.cc:128-150` は
   `max(2,|end-start|/0.2)` を **transform 後**の長さで数えるので、ossia では本数が変わる）。
   ⇒ **直し方は「折った線分」ではなく**輪郭パス（font units の制御点）**を焼き、
   平坦化・向き分類をレイアウト時に C# で行う**こと。乖離は回避でなく**消える**。
   値段は C# に `Path_interpreter` 相当 100〜150 行＋全 clef の再照合、データはむしろ小さくなる。
   ★ **上の +0.043376 の容疑者 ⑵ でもある**ので、点は**もうある**。
2. ⏳ **clef の X アンカーが近似のまま**（`systemLeft + ClefGlyphXOffset`）。
   ⚠️★ **箱のときは平坦なので鈍感で無害だったのが、シルエットにすると効き始める**——
   台帳の `why` の「**この数は clef の X に鈍感**」は**箱だったから**で、もう成立しない。
   正しいアンカーは break-align 群の左インク（`SpacingRules.ClefGroupExtent`・`DrawClef` には
   移植済み）だが `SkylineBuilder` は 2 譜しか持たないので**配線が要り、出力が動く**。
   ⇒ ★ **`system.clef-floor.*` がその島の点**（行頭・clef・system 間）。**点で測ってから動かす。**

（**第25・26セッションの節は落とした**。commit `2feb6021`/`c2955ba3`/`6c6be1af`/`94705160`/
`7fdace7f`/`ceb73c30` と台帳の `why` に全部ある。**原則は §5 へ汎化済み。**）

### ▶ 次の一手

★★★ **推奨順（第27セッション末に棚卸し・第30セッション末に字面度負債を追記）**

⚠️ **第26セッション末の ▶0〜▶3 は全部済**。**残す 1 文だけ**: **専用プローブの実測は 13.5 秒**で、
`-Probe` は既存の `Measure-LilyPondPageGeometry.ps1` が最初から取る。
⇒ **もう「点を作る値段」を理由に先送りしない。**
⚠️★★ **残差はもう最大 0.027480（OTC・支持側の箱）。「総和が下がったか」では何も見えない**——
**変更の効果は落ちた点の id で読むこと**（§5.0）。**残っているのは全部「点が無い regime」。**

★★★ **trill 島は第39セッションで閉じた**（8 点中 7 点 exact ＋ TXW 1.8e-4＝平坦化族）。
**次に手を動かせる候補（点が既に開いているもの）**:
- ★★ **script の outside-staff-priority を priority pass の mover に**（第38セッションが
  起票した残りの port・台帳 `trill.fermata-priority` TSP・**残差 +1.685**）。fermata は
  `(outside-staff-priority . 75)`（scm/script.scm）＞ trill 50 なので LP では trill が先に
  置かれ fermata がそれを越える。Lily# は script を**不動 seed** にするので逆に鳴る
  （`OutsideStaffStacker.SeedAboveTrackers`）。⇒ **宣言 priority を持つ script は mover、
  無宣言は seed**（LP 自身の分業）。⚠️ stacker が articulations を**返す**必要がある
  （今は seed するだけ）＝`LayoutEngine` 側の配線も動く。fermata はコーパスに多いので
  snapshot は広めに割れる見込み。
- ~~**trill の描画を glyph run へ**~~ — **済（`81c46545`・GO 済・snapshot 2 枚）**。
- ~~**`TrillSpannerItem` に VoiceIndex**~~ — **済（`50fc6a80`・出力不変＝結果）**。
★★ **trill 島に残る非字面は 4 件**（第39セッションの自己監査＝`29fe9c65`／`81c46545`）。
**うち 2 件は「本が先」で、その本は 1 冊で両方測れる**——設計はプローブヘッダ
（`trill-stem-support.ly` の「STILL NOT LITERAL」節）に fork つきで書いた:

- ★ ⑴ **支持が head+stem** で、LP は **NoteColumn の skyline 全体**（`side-support-elements`
  は列 grob そのもの）。`DynamicEngraver.ColumnSupportSkylines` は **dynamics 用には字面**
  （`dynamic-align-engraver.cc:108-117` は head と stem を**個別に** acknowledge する）で、
  **流用がギャップを持ち込んだ**。**過小予約**になり得る。
  ★ **絞り込み済**（本が小さくなる理由）: 列の要素のうち trill 側で binding し得るのは
  **臨時記号だけ**。dot は符頭と同じ高さで右（越えない）・旗は符尾側の X 内で先端より内側。
  **臨時記号だけが自分の符頭より背が高い**（sharp は中心から約 0.7 対 符頭 LILC 0.545）ので、
  **符尾が下向きのとき head も stem も越える**。予想欠陥量 **約 0.15**。
- ⑵ **左バウンド中心が列X+advance/2** で、LP は列 extent 全体
  （`line-spanner.cc:171-175` `robust_relative_extent`.`linear_combination`）。
  **臨時記号が extent を左へ広げる**ので、Lily# のグリフは LP より臨時記号の半幅ぶん右。
- **本 TXA**: TXN の texture（単一 voice・自然な**下向き**符尾＝上へ伸びる stem が無い）に
  **♯ を 1 個足すだけ**。波の下に置けば ⑴、開始列に置けば ⑵。
  **fork**: 線 = 臨時記号インク上端+0.5+波の局所リーチ ⇒ 臨時記号は支持で Lily# が約 0.15
  過小／線 = 符頭インク上端+0.5+リーチ（＝TXN と同値）⇒ **臨時記号は支持に居らず、直すのは
  コメントのほう**。**どちらの枝も所見で、どちらもパッチではない。**
  ⚠️ **この本より先に支持を広げないこと**——LP が読まない要素を予約することになり、
  どちらが動いたか既存の点では区別できない。
- ⑶ **`BoundPadding 0.5`** が barline バウンド（`to-barline #t`）と継続 piece の左端に残る
  （LP は bar line と system 開始列に attach）。**点が無い。**
- ★★ ⑸ **perf: trill 多用譜で +14〜19% が残る**（`d1f4df64`・§5.3 の min-of-50×3・
  pre-session worktree `6406d0cf` と同一ハーネス A/B）。
  **base 41.6ms → 49.6ms**（8×4小節 trill）・**~41 → 46.8**（32×1小節）。
  **trill の無い譜は中立**（showcase/04: 11.68 → 9.98）。
  **原因**: 線の profile が実 glyph run になったので**要素×グリフ辺ぶんの building** を持つ
  （旧: 箱 1 個）。コストは譜全体の**要素数に比例**（2 つの合成譜が数 % 内で一致＝要素数
  8800 対 7400）。
  ⇒ ★ **次の lever は skyline 層**: run profile は **trill ごとに 2 回**建つ
  （engraver の my_dim と stacker の mover pair）うえ、呼ぶたびに cache 済み resolved
  buildings を**コピー**する（`TrillWaveOutline.PlaceResolved`）。**`Merge(buildings, dx, dy)`
  を足せばコピー 2 回と resolve 1 回が消える**——trill の変更ではなく skyline 層の追加で、
  それ自体の測定が要る。⚠️ **箱に戻す理由にはならない**（箱は LP に対応物の無い発明）。
  ⚠️ **最初の測定は逆の答えを出した**: base worktree のビルド直後は HEAD が全譜で速く見えた。
  **1 セットは測定ではない**——**複数 RUN の最小**を採る。
- ⑷ **`TrillWaveOutline` の `StaffSize.FullSize`** は **annotation 島の共有債務**——
  annotation layout が自分の譜を持たないので bracket/slur/tie の seeding も同じ
  `FullSize` を抱えている（`LayoutEngine` の該当箇所に同じ一文）。**まとめて閉じる。**

★★ **字面度負債（第30セッションの自己監査で名指し・全部「点が先」）**。
~~▶1 の「stacker の interval 箱」~~ — **閉じた**（第31セッション・X 揃え＋0.2 padding まで込み）。
⚠️ **ottava の +0.027480 はまだ割れていない**——stacker は skyline を持ったが ottava 自身と
support seed が箱のまま（OTC の分解には support のアウトラインが要る＝SkylineBuilder 統一の島）。
**次セッションの候補は ⓐ〜ⓔ**:
- ~~ⓐ **beamed tuplet の 2 分岐を LP の単一経路へ**~~ — **閉じた（第34セッション・
  `3e78ae2a`・GO 済）**。encompass は実 column extent（beam 面／実 stem／符頭インク）。
  残: slope 機構の非字面（`CalculateSlope` の LILYSHARP-OWN ブロック・対が先）と
  行頭 beam quant +0.000937（beam 島）。詳細は第34セッション節。
- ~~★★ **`NoteColumnLayout`（Y 側の column/stem 模型）を作る**~~ — **できた（第35セッション・
  `300a7f54`・出力不変・snapshot 0 枚）**。4 家は `NoteColumnLayout` の named read になり、
  **相違表は家の doc にある**（値のセルは 4 家とも割れていた＝どれを畳むのも output-moving・
  点が先）。**残り**: ~~⑴ dynamics の生 3.5~~ — **閉じた（第37セッション・`34a3d8d0`・
  GO 済）**。DSB face欠片族 exact／DSQ・DMF は Pango X 量子化の named 族
  （+0.001512／+0.001793・fit 禁止）。~~**raw 3.5 の最後の消費者は trill**~~ —
  **閉じた（第38セッション・`daeb203c`）**: `RawSupportEdgeUp` は `SupportEdgeUp`＝
  `OutwardTipDeviceY` の変換になり、予約系の生 3.5 は残ゼロ。
  ⑵ beam 所属 lookup は綴りが残る（key/gate が消費者ごと——dynamics は第37セッションで
  **全 voice の 4 キー map**（`BuildBeamMembers`）を足したので 4 綴り目）⑶ grace 描画 stem の
  生 3.5（`SharedRenderer.GraceNotes`・島の外）⑷ 予告どおり行頭 beam quant 差・slope 非字面・
  seed/draw 二重パスは消えていない。詳細は第35セッション節。
- ⓑ **譜 extent の定数直書き**: TextScript 床の 2.05・Ottava 床の半太さ・`BelowStaffYUp` の
  名目 4.0。LP は `staff_extent[dir]` を**実 StaffSymbol から読む**ので、線数の違う譜・
  `magnifyStaff` 相当で割れる。**tab/ossia 島の「名目 4.0」族と同じ 1 個の物体**——
  `StaffLayout` から高さを引く配線が本体。点は**倍率譜×床 grob の対**を新設。
- ⓒ **tab 分岐の +0.3/−0.8 補償と digitHeight 1.7**（第30セッション節 6）——tab の
  beamed 番号の対（`tuplet-number-beamed.ly` の tab 版）が先。
- ⓓ **TextScript の支持側 side-position padding 0.3 が未移植**（測定 regime では 0.46 か
  床が勝って影）。発火するのは**支持インクが譜インクより高く、かつ skyline に入らない**組
  ——対を組めるかから検討。
- ~~ⓔ **床 grob の宣言表**~~ — **できた**（第32セッション・`EngravingDefaults` の
  outside-staff declaration table。TextScript/Trill/TextSpanner/Ottava/DynamicLineSpanner の
  5 grob が 1 軒・全消費者がそこを読む）。**次に床 grob を足す人は表へ**。
- ~~★★ **tempo 島**~~ — **移植済（第33セッション・`df72dd5f`・GO 済）**。TMQ −0.000010／
  TMT 九桁 exact／X 対 0 exact。詳細・残債は第33セッション節。

1. ★ **テキスト量の残り**:
   ~~⑴ 和音記号の weight~~ — **移植済（第28セッション・GO 済）**。
   ~~⑵ `OutsideStaffStacker` の「Own tuning」~~ — **閉じた（第29セッション・`99ecd3aa`）**。
     3 定数は消え、5 消費箇所とも描画と同じ face の ink。**残した負債は第29セッション節**
     （~~staff-padding refpoint 床~~＝**TextScript 分は第30セッションで閉じた**／
     stacker の interval 箱＝点あり／
     TextSpanner 1.2/0.3・DynamicHalfWidth 0.75・mark 描画サイズ＝**点が無いので点が先**）。
   ~~⑶ 和音行の時価ばね~~ — **閉じた**（第28セッション。両控え exact・
     `chord.symbol-width.{quarter,half}-spring-control` がラチェット網として残る）。
2. **clef シルエット移植が残した繰延 2 件**（上のセッション節に詳細）＝
   **⑴ 平坦化をレイアウト時へ**（ossia の量子化。**1 番の容疑者 ⑵ と同じ物体なので、
   1 番を割ると着手根拠がそのまま出る**）／
   **⑵ clef の X アンカー**（`system.clef-floor.*` がその島の点。**箱でなくなった今は効く**）。
3. **annotation pass の magnification**（第25セッションが名指しで残した「1 フレーム外の同じ問い」）。
   ⚠️ ~~`TupletBracketLayout` は `MeasureIndex` しか持たず譜の帰属が無い~~ — **stale と確認**
   （2026-07-29・第35セッション末・grep で裏取り）: **3 種とも譜の帰属を持っている**——
   `TupletBracketLayout.StaffIndex`（`TupletBracketEngraver.cs` の record・ossia shrink 用）と
   `BowLayout.StaffIndex`（slur/tie の基底・`BowLayout.cs:30`）。**残る実体は `LayoutEngine` の
   3 か所（:1590-1663 の AddTupletBrackets/AddSlurs/AddTies）が `FullSize` を渡すことだけ**——
   「モデル変更が本体」ではもう無く、layout の `StaffIndex` から `StaffSize` を引く配線に
   縮んだ。出力は動かない見込み＝**テストが網**（着手時に「本当に動かないか」は §5.4 どおり
   格納値網を先に）。
4. **棚卸し ⑶（walk がペアごと・本丸）**／**▶ の silhouette**／**§2D 未移植 ⑵⑶** — **全部「台帳点が先」**。
   発火する regime（譜と譜の間に loose line）を作る本を起票するのが最初の作業。
   ⇒ ★ **その値段はもう払い終わっている**（第27セッション）——**専用プローブ 1 本＝13 秒**なので、
   この 3 件が「点が無い」で止まる理由はもう無い。**書けばよい。**

~~★★★ **`SkylineBuilder` の X の単位を割る**~~ — **閉じた**（`94705160`・§4.2.1 へ）。
`StaffSize` が LP の 2 量（`Modified_font_metric` の magnification と `staff-space`）を持ち、
**seed は掛け算をしない**。`VerticalSkyline.Scale` は消えた。**出力は 1 ビットも動かない。**
★ **残す規則は 1 文**: **seed に残る素の数は X の「位置」だけ。**
⚠️ **1 フレーム外に同じ問いが残る**＝ system 側 annotation 経路（`LayoutEngine` の 3 か所は
`FullSize` を渡す。**annotation pass が magnification を知らない**ので ossia の装飾は素のまま。
**annotation layout が自分の譜を持つ日に消える**）。

★★★ **ossia の島は閉じた**（第24セッション・`489ac6d7`）。tab/ossia フレーム島の扉 ⑴〜⑸ に続いて
**「ossia は spaceable な譜」も入った**（ばね＋spec＋spaceable 性の 3 つ同時）。
**この島に残るのは次の 1 件**:

1. **silhouette の選択が「最外の要素」**なので、外端が text row の system は譜が 1 枚も
   skyline に入らない。⚠️ **spaceable 譜に変えるだけでは直らない**（LYRHKG の床が
   8.900000 → 1.707200 に落ちて譜が重なる）＝**歌詞行を skyline から外していることと同じ 1 個**。
   **両方同時に、そして先に点を作る。**

以下は同じ島に属さない残り:

~~0. 独立 lyrics 行を loose 鎖で解く（⑵b）~~ — **閉じた**（第20セッション）。
**その島が残した件**は**台帳点が無いので次の人は先に点を作る**:

~~0. system スカイラインの DOWN が union でない~~ — **大半が閉じた**（`327e3bb9`＋`8e4857c8`）。
   各 edge 譜が**自分の 2 本の線を両方 seed** するようになり、応急修理だった鎖の種の merge は
   **消えた**（`LILYSHARP-OWN` が 1 つ減り、Core は +38 / −148 行）。
   ★ **残り半分は独立した島ではなかった**——これが今回の一番の所見:
   **両端とも text row の system**（chords 行が上・lyrics 行が下のリードシート）は、
   narrowing が**外側の「要素」**を採るので**譜が 1 枚も silhouette に入らない**。直すには
   外側の **spaceable 譜**を**置かれた offset** で採る必要があり、**試したら snapshot が 19 枚
   動いた——全部 TAB**（tab 譜の高さは、この関数が仮定している名目 4.0 ではない）。
   ⇒ ⚠️ **これは §1 が前から留保している tab/ossia の refpoint 枠の島そのもの。2 つではなく 1 つ**
   （下の表の扉 ⑷）。⚠️ **19 枚は「選択＋中央線＋clef を一度に置き換えた版」の値段**で、
   譜線だけに絞った扉 ⑶ は**1 枚で通った**（`8e4857c8`）。踏む fixture も本も今は無い。
1. **LEADING lyrics 行（行が system の先頭・譜の上）は鎖に入っていない**。
   `LayoutEngine.RowSkylinesOf` が lyrics 行に空を返し、`LeadingLinesOfSystem` が降りる。
   ⚠️ **「ここで行のインクを返す」では直らない**: LP では行の**verse ごと**が別の Lyrics context
   ＝別の loose line なので（`:948-990`）、**verse ごとに `LeadingLine` を 1 本**出し、
   **音節も解から動かす**必要がある（いまは和音記号だけが `ApplySolvedRowPositions` を通る）。
   踏む fixture も台帳点も無い。
2. ★ **内容サイズ紙 × ページ端の鎖**（2026-07-27・ユーザーが目視で見つけた）。
   ページ**最後**の block の鎖はページ下端まで走る（`:1004-1013`）が、Lily# は単一ページを
   内容サイズで出す（§3）ので下端に slack が無く、鎖が floor まで圧縮される。実測（2 system
   の本）: 1 行目 **5.500000**／最終行 **4.009200**。⚠️ **chords 行の有無で同じ数字**＝
   今回の移植のせいではなく、**歌詞のある全スコアで前からこうなっていた**（移植前は text row
   があると鎖ごと降りていたので、リードシートだけ 5.5/5.5 に見えていた）。
   LP は実紙面なので最後の block にも slack があり 5.500000 のまま。⇒ **「§3 の紙」と
   「ページ端の鎖」が噛み合っていない**。台帳点は全部 real paper なので**この regime を測る点が無い**。
3. **system の高さはまだ band を予約する**。row は LP のフレームで置かれ・解かれるように
   なったが `GetStaffHeight` は chords 行で `TextRowHeight = 2.5`、lyrics 行で
   `StaffHeight + (verses−1) * TextRowVerseSpacing` のままなので、インクより広く取る。
   ⚠️ **`TextRowVerseSpacing`（3.2）は最後に残った平坦な verse step**——鎖に入った行の
   verse 間は `max(2.8, ink+0.2)` で解かれるのに、band の**高さ**だけ 3.2 刻みで数える。
   効くのは**行を譜の上に置いた regime だけ**（実測・係数 1）で踏む本が無い。
   **LP の alignment 最小を高さにも通すのが本体**（＝`SystemHeightOf` ではなく**配置側**が
   落としている量。上の ⚠️ と同じ話）。⚠️ **鎖が入ったからといって消さないこと**（§5.3）。
4. **ページ先頭 system の row は鎖で解かれていない**。LP はページ上端から走らせる
   （`:963-988`・`:1004-1013`）。今のコーパスでは**そのばねがどちらの経路でも最小に座る**ので
   同じ数（実測: 両 system とも譜 refpoint の 3.576200 上）だが、**圧縮ページでは割れる**。

★★ **tab／ossia の refpoint 枠の島＝5 つの扉は全部閉じた**（2026-07-28）。どれも同じ変換を
要求していた——**譜を「名目 4.0」でなく自分の置かれた高さで扱う**——ので、まとめて終わった。
再走査しない。⑶ system silhouette の外側譜線（`8e4857c8`・snapshot 1 枚）／
⑴⑵ `GapSpan` の名目 `staffHeight` と `IsSpecSpacedRowBoundary` の門（`13de8545`・20 枚）／
⑷ ページアンカーの名目 halfStaff（`0a2b4e5d`・22 枚）／**⑸ ossia 距離 `gap * OssiaScale`**
（`663be3cc`・**3 枚**・第22セッション）／★ **⑹ ossia の spaceable 性そのもの**
（`489ac6d7`・**3 枚**・第24セッション＝ばね・spec・アンカーの 3 つ同時）。**残るのは上の 1.**

⚠️★★ **「この島は snapshot が ~20 枚動く」は粗すぎた**（何セッションも引き継がれた）。
**扉ごとに値段が違った**: ⑶ **1 枚**・⑴⑵ **20 枚**・⑷ **22 枚**（うちアンカー半分だけなら
**0 枚**）・⑸ **3 枚**。⇒ **島を「何枚動く」で見積もらない。どの量を変えるかまで割ってから数える。**
⚠️ **同じ粗さが「値段」以外にも出た**（第22セッション）: ⑸ は「Lily# の描画欠陥を先に直すか、
断片形の対を組むか」という**分岐として引き継がれていたが、どちらも要らなかった**——
塞いでいた 3 つの数が**スケールグループの中の座標**だっただけ（§5.3）。
⇒ ★ **島の残件を「作業の選択」として引き継ぐときは、その選択の前提を 1 回測り直す。**

- **ページブレーカーの refpoint extent は名目のまま**（動かすとページが割り直され、その count を
  測る点が無い）。`LILYSHARP-OWN` 明示済＝島の外の残件。
1. ~~⛔ **BLOCKED — `between-staves.staff-to-lyric` に残る 0.139961**~~ — **割れた**
   （第26セッション・`7fdace7f`。clef の箱が 0.034 を隠していたので実額は **0.105961**）。
   **BLOCKED ではなくなった**ので下の記述は経緯として読むこと。⇒ ▶0。
   （以下は旧記述）**BLOCKED — `between-staves.staff-to-lyric` に残る 0.139961**（§2C 行き）。
   Lily# 側の機構は算術で再現する（上の譜が 4.009200 下がり自分の clef が refpoint の
   3.550000 下 ⇒ 3.800000 − 0.459200 + 1.500000 = **4.840800**）。同じ算術を LP の 3.737890 で
   やると 5.112110 だが **LP は 4.972149**。⇒ **LP の `Skyline::distance` は素朴な max どうしの
   組より 0.139961 低いところで当たっている。** **LP を instrument して当たり点の対を dump**
   するまで割れない。⚠️ **この 0.139961 に定数を合わせない。**
   （`between-staves.two-verse.staff-staff-inside` に残る 0.012826 の内訳も特定済み＝
   0.010956 は**歌詞の descent の書体差**（両者ともアウトライン実測なので純粋な書体差）＋
   0.001870 は同じ未特定。台帳の `why` 参照。）
2. ~~★ **ossia が Lily# では非 spaceable**~~ — **閉じた**（第24セッション・`489ac6d7`）。
   ⚠️ **残るのは `staff-refpoint-extent` だけ**: LP でもこの extent は spaceable 譜だけを張るので、
   **LYROS が 18.000000 対 Lily# の 2 譜スパン**。**測る点がまだ無い**ので次の人は先に点を作る。
3. **pure スカイラインが無い**。`CalculatePureSystemHeight` は製品から呼ばれておらず
   （`PageLayouter` の remarks に NOT WIRED と明記済）、LP の `get_pure_minimum_translations`
   は**同じ walk を pure スカイラインで走らせる**。生かすならそれを作るのが本体。

⚠️ **字面から外れて残っている 1 件**: `BuildLooseLinesBetween` の**「群境界だけ」条件**。
LP に該当するテストは無い（Lyrics はどこに居ても alignment の要素）。Lily# のモデル
（note-bound は群の下にぶら下がる）に従った条件なので、**閉じるならモデルのほうを先に動かす**。
（`AlignmentWalk` の remarks にある 2 件——`Seed` が `align-interface.cc:217` の第1要素 dy を
取らない／空スカイラインで 0 を返すガード——も据え置き。）

⚠️ **frame 誤りの型は残る**（前セッションからの持ち越し・繰り返し出る）: **system スカイラインは
system 原点から／譜ごとのスカイラインはその譜の上端から**測られているので、アンカーへの
変換が 2 系統で違う（`skylineToAnchor`）。片方だけ直すと**譜間距離ちょうど**ずれる（実測 10.5）。

### ★★ 残っている「非字面」の棚卸し（2026-07-28・**別々の項目ではなかった**）

⚠️ **§2D に 3 件・§1 に数件と散っていたが、縦の非字面は次の 3 つに畳める。**
着手順の推奨もこの順。★ **⑴ は第25セッションで閉じた。次は ⑶（本丸・ただし点が先）**——
⑵ はモデル追加が要るので急がない。

~~**⑴ 代理表現 A＝`is_spaceable` を「型」で書いている**~~ — **閉じた**（第25セッション・
`2feb6021`）。`StaffLayout` が `StaffAffinity` を運び、**5 綴り**（引き継ぎは 3 と書いていた）が
`StaffAffinity.IsSpaceable` 1 本になった。**出力不変は測って確認済**で、
**除外を足していないので「結果」**（§1 のセッション節）。網は規則テスト。

**⑵ 代理表現 B＝「grouper があるか」を `StaffGroupType.Single` で代理**
LP は grob の `staff-grouper` を引く（`axis-group-interface.cc:1007-1027`）。
Lily# は群の種別で代理。⚠️ **今日は構造的に正しい**（`RenderSpec` が ossia/tab を必ず
`CreateSingle` にする）が、**モデルに grouper が無い**ので字面にはできない。
⇒ **⑴ より重い**（モデル追加）。**急がない。**

**⑶ ★★ 本丸＝walk が「ペアごと」で「system ごと」ではない**
`AlignmentWalk` **自体は :228-275 の忠実な移植**（Seed/Advance/Distance）。欠けているのは
**呼び出し側**で、LP は `internal_get_minimum_translations` を **system の要素列全体に 1 回**
走らせて `translates[]` を返すのに、Lily# は**ペアごとに上の譜から seed し直す**。
⇒ **これ 1 つで次が全部閉じる**（別々の項目として引き継がない）:
- §2D 未移植 ⑶ `include_fixed_spacing` の第2制約（`align-interface.cc:240-267`）＝
  **直前の spaceable 譜**に対する床。**run をまたぐ量なのでペアごとでは書けない**
- §2D 未移植 ⑵ 最初の spaceable 譜の loose line 用の床（`:667-670`）
- `AlignmentWalk.Seed` が取らない `:217` の dy＝**`min_offsets[0]`**。
  ★ **`BuildLooseChainEnds` がそれを手で再構成している**（`LayoutEngine` の
  「up extent は上端線基準なので半譜足す」の段落）＝**今日消した床の逆算と同じ形が
  もう 1 箇所残っている**
- `CalculateStaffGapWithSkylines` の `max(basic, alignmentMin)`（LP は非 spaceable の隣人に
  basic を足さない。ideal は force 0 の loose line 鎖から来る＝`:1035`）
⚠️ **測る点が無い**（発火するのは**譜と譜の間に loose line が立つ**とき＝
`ComputeBetweenStavesEnd` が declines している regime）。⇒ **点が先。**
⚠️ **frame 移行なので §5.4 どおり「格納値を主張するテスト」を先に書く**
（`StaffSkylineFrameTests` の前例。無しでやった 1 回目は差し戻している）。

**⑷ 言語表面が無いもの**: §2D 未移植 ⑴ `alignment-distances`
（`line-break-system-details` 由来の手動指定）。**文法から**なので別系統。

**⑸ ★ 代理表現 C＝譜の倍率を `IsOssia` で代理**（2026-07-28・**自分で入れたので自分で起票**）
`StaffSize.Of(staff)` は `staff is { IsOssia: true } ? OssiaScale : 1.0` と**型を列挙**している。
LP は文脈の `fontSize`（→ `magstep`）を読み、**ossia かどうかは訊かない**
（`magnifyStaff` は任意の譜に倍率を与えられる）。⇒ **⑴ で閉じたのと同じ形**。
⚠️ **障害はモデル**: `Staff` に fontSize / magnification が無く `IsOssia` しかない
（`Staff.cs` を確認済）。⑵ の grouper と同じく**モデル追加が先**。
⚠️ **今日は構造的に正しい**（倍率譜は ossia だけ）ので**急がない**が、
**`magnifyStaff` 相当を文法に入れる日には必ず割れる**。⇒ **入口は `Staff` に倍率を持たせること**で、
そうすれば `StaffSize.Of` は 1 行の property lookup になる（⑴ とまったく同じ決着）。

### その次の候補

- ~~**ossia 距離そのもの（`gap * OssiaScale`）**~~ — **閉じた**（第22セッション・`663be3cc`）。
  `staff.ossia-pair.staff-staff-inside` −1.636081 → exact、対照は不動。**§1 のセッション節へ。**
- ~~群間ギャップの上側高さが名目 `staffHeight`~~ — **閉じた**（`13de8545`・▶0 の扉 ⑴⑵）。
- ~~**`lyrics.*.system-gap` の残りはフォント量**~~／~~**歌詞の up-extent も書体から読む
  （CJK の受け皿が先）**~~ — **両方閉じた**（第27セッション・§1 の上のほう）。
  ★ **「フォント量だから埋めない」も「CJK の受け皿が先」も誤りだった**——前者は定数の
  取り違え（§5.0 の新規則）、後者は**受け皿が `LyricDownExtent` に既にあった**。
  ⚠️ **`AscenderEm`/`XHeightEm`/`AscenderLetters` は削除済**（2026-07-29・承認済）。
  **文字クラスで高さを当てる表を復活させないこと**——face が文字列ごとに答える。
  残るのは `CjkAscenderEm` だけで、**`LILYSHARP-OWN` 明示済＝持っていない書体の代替**。
- ~~**独立 lyrics 行の枝に点が無い**~~ — **点はできた**（第19セッション・▶0）。残るのは移植。
- ~~**ossia ペアは rigid のまま**~~ — **閉じた**（第24セッション・`489ac6d7`）。
- ~~★★ **top ばねの床**~~ — **1 個でなく 2 個だった**（第25セッション）。**⑴ は閉じた**
  （`c2955ba3`＝ossia のインクの scale・+0.073104 → **−0.001429**）。
  **⑵ は §2C へ移した**——対照の頭 +0.024000 は**単独の量ではなく `clefs.G` の箱**で、
  **足の +0.010000 ×2 と同じ 1 個の物体**だった（摂動で確定・§2C 参照）。
  ⚠️ **「同じ字形の下向きは 8e-5 で一致」という前セッションの読みは誤り**——
  book S の −8.3e-5 は**足の 0.010000 が force 経由で 1/120 に薄まった姿**で、
  **clef の下端も 0.010000 ずれている**。⇒ ★ **薄まった残差を「一致」と読まない**（§5.3 へ汎化）。
- **§2A の残り**: 継続行 prefix をスコア全体で 1 つ・measure 0 で計算する ⇒ **mid-piece の
  key change 後の行は古い幅のまま**。構造的な解は **per-line prefix** で、`MeasureSpringData` に
  per-measure `LineStartSpring` の受け皿が既にある。
- §2D の残り（**字面から外れた 1 件・未移植 3 件**。① は `de270892` で閉じた）・
  §2C（LP を instrument する必要があるもの）。
- ★ **§2D の未移植 ⑶ が「本体」になった**（`include_fixed_spacing` の第2制約・
  `align-interface.cc:240-267`）。① を閉じても **Lily# の walk はペアごとに上の譜から seed し直す**ので、
  LP の「**直前の spaceable 譜**に対しても床にする」＝ run 全体をまたぐ量は**構造上まだ表現できない**。
  発火するのは**譜と譜の間に loose line が立つ**ときで、それは `ComputeBetweenStavesEnd` が
  いま declines している regime と同じ 1 個。⚠️ **台帳点が無いので次の人は先に点を作る。**
- §2H に残る発明（`MinItemGap` の歌詞 4 箇所・`ownFixedFloor`・`ChordNameEngraver` の
  `Math.Max(2.0, …)` 床＝`LILYSHARP-OWN` と明示済で**実際に効いている**）。

⚠️ **ここに点数を書かない**（§0 と同じ理由・第26セッションで「33 点」が stale だった）。
**数は `Corpus_ReportsTotalDivergence` が印字する**（§6。⚠️ それは**台帳のエコー**で Lily# を
測っていない・§5.3）。⚠️ **下の表は第24セッション時点の並び**——
**次に触る人は「残差」列を信じる前に §0 でテストを走らせること**（§5.2）。

★★★ **数より効く読み方は「残っているものが何で出来ているか」で、それは 2026-07-28 現在
ほとんど閉じた**（2026-07-29 の再印字: **156/188 exact・|residual| 総和 0.443735**（161 距離点）・
counts 25/27。⚠️ **総和の跳ねは新設 `textscript.stacked.outline-step` の持参金 +0.420825**＝
箱対アウトラインの**名前付き**残差で、機構の後退ではない。それを除く総和は従来水準・
count は別勘定＝§3 の決定。⚠️ **符号付きの和は別の数**——引用するときはどちらを足したか言うこと。
⚠️ **第27セッションは 2.606965 → 2.968224 → 2.801498 → 0.022898 → 0.012541 と動いた**。上がった 2 段は
clef シルエット移植が**打ち消しを解いた +0.105961 ×3** と**新設点の持参金 +0.043376**、
落とした 2 段は**小節番号**と**歌詞**の**書体量を face から読んだ分**。
★★★ **教訓は「フォント量だから閉じない」と書いた族が 2 つとも閉じたこと**——
どちらも**書体の差ではなく、出典のある定数の取り違え**（小節番号の 1.3、歌詞の em 3.2）だった。
⇒ ★ **「フォント量」は残差の分類として弱い。次に同じ札を貼るときは、
LP 側の定数を引いてから貼ること。** ⚠️ **ここに数を書き足すなら実測してから**——
`Corpus_ReportsTotalDivergence` か `lp-geometry.json` を集計する）:

| 残る族 | 量 | 種類 |
|---|---|---|
| `textscript.stacked.outline-step` | **−0.001037**（旧 +0.420825） | **閉じた（第31セッション・3 段: アウトライン→0.2 水平 padding→pen 原点 X）**。残りは max(2,len/0.2) 平坦化の**サンプル位相＋float32**（C059 と Schola 共通＝face 差ではない）。**0 にしない**。双子 box-step −6.4e-5・X 対 exact ×2 |
| `textscript.no-descender.staff-to-baseline` | **0.000000**（旧 −0.007000） | **閉じた（第30セッション）**＝staff-padding refpoint 床を `PlaceCustomTexts` に移植（`aligned_side` の順序）。descender 側と box-step は −3e-5/−4.8e-5 で face 差の床のまま |
| `staff.staff.beamed-tuplet-number` | **+0.0000208**（旧 −1.434229 → +0.260021 → 今） | **閉じた（第30セッション）**。残りは番号の half-ink face 差（0.627738 対 0.627717）＝歌詞 −0.000100 と同族。**0 にしない** |
| `staff.staff.beamed-tuplet-control` | **−0.006512** | clef down 3.533488 対 LP 3.540000＝clef 族の欠片（clef 対 平坦線という新しい組の網） |
| `system.clef-floor.floor-bound-distance` | **+0.004090** | **フォント**（小節番号の face の cap 差 0.007048 が padding 斜面で薄まった姿）。⚠️ **clef ではない**——padding 無しの距離は LP と六桁一致 |
| `between-staves.two-verse.staff-staff-inside` | **+0.001767**（旧 +0.284136） | 歌詞 descent の書体差ほか |
| `lyrics.chord-row.between-systems.staff-to-lyric` | **+0.000963**（旧 +0.002047 → +0.011320 → 今） | **和音記号の em を直して 91% 落ちた**。⚠️ 途中で上がったのは**フォント 3 項の打ち消しが解けた**から |
| `lyrics.*.staff-to-lyric` ×8 | **−0.000100** | **face の差そのもの**（LP 1.187880 対 Lily# 1.187789） |
| `barnumber.*.staff-to-baseline` ×2 | **+0.000006** | 同上（数字の下はみ出し） |
| Pango 量子化・tie/slur/tuplet/強弱 | 5e-6〜1.4e-3 | 閉じる予定の無い名前付き残差 |
| `lyrics.hara-kiri.*.staves-on-first-page` | **−2** ×2（count） | **ページブレーカーの欠陥**（両書に共通・ss の総和に入らない） |

⇒ ★★★ **コーパスが測っている範囲では、機構もテキストメトリクスもほぼ閉じた**
（かつて「歌詞書体 +0.271310 が 9 エントリ、総和の 2.4 以上」と書いてあった族は消えた）。
**残っているのは「点が 1 つも無い regime」だけ**——だから **▶ は全部「点を作る」から始まる**。
⚠️★★ **総和が 0.02 になったので、これからは「総和が下がったか」では何も見えない。**
**§5.0 どおり、変更の効果は落ちた点の id で読むこと。**
⚠️ **次の投資先は依然「もっと移植する」ではなく「点を増やす」**が、
★ **その値段は第27セッションで落ちた**（専用プローブ＝13 秒）。⇒ **▶ が「点が無い」で
止まっている項目は、もう止まる理由が無い。**

| 点 | 残差 | 正体 |
|---|---|---|
| `system.clef-floor.floor-bound-distance` | **+0.004090** | ★★ **第27セッションで新設**。system 間ばねが**床に座り、当たりが行頭 clef** の唯一の点＝**clef シルエット移植を LP に対して測った器**（箱 +0.574000 → シルエット +0.043376 → 小節番号の cap だけ直して 0.000000 → **baseline も直して +0.004090**）。⚠️ **途中の exact は 2 つの誤差の打ち消し**——中間状態を良い方と読まない。⚠️ **LP は箱の和ちょうど 8.316000 を返す**——horizon padding 1.0 の**平坦部**が clef の最深 x=1.84 と最高 x=2.228 を橋渡しするので、**system 段では箱とシルエットは同値**。⚠️ **clef と小節番号が百分の一以内で競う網**なので、どちらが動いても鳴る |
| `system.clef-floor.ideal-bound-distance` ほか 3 点 | **exact** | 対（同じ音楽・出荷 spacing ＝ ideal 12.000000）＋ 頭の refpoint ＋ system 本数。**「床に座っている」という主張の裏** |
| `page.ossia-pair.compressed.first-staff-refpoint` | **−0.001429** | ★★ **2 度移植した**（−6.850208 → +0.073104 → **−0.001429**）。2 度目は**ossia のインクの scale**。残りは対照の力残差と同じ桁で、**もう ossia 固有ではない** |
| `page.ossia-pair.compressed.last-staff-to-foot` | **+0.010000** | ★★ **移植済（旧 +0.843439）＝対照と六桁で同一になった**。⇒ **足に ossia 固有の量は最初から無く、頭の誤りが鎖の反対端に出ていただけ** |
| `system.ossia-pair.compressed-distance` | **−0.000953** | ★ **inside の ちょうど 4 倍のまま**（4 × 0.000238 = 0.000952）＝第2の量ではない。**鎖の頭を動かしても比が生き残った** |
| `staff.ossia-pair.compressed.staff-staff-inside` | **−0.000238** | ★★ **2 度移植した**（+0.212184 → −0.002309 → **−0.000238**）。ばね・spec・spaceable 性の 3 つ＋**インクの scale** |
| `page.ossia-control.compressed.first-staff-refpoint` | **+0.024000** | ★★ **字形の量**（第25セッションで摂動により確定）。**両者とも床に座っており**、その床は `ClefG.Top`＝LP の treble clef は中央線の上 3.776000・Lily# は 3.800000。⚠️ **clef sliver 族ではない**（下向きは 8e-5 で一致）。⚠️ **旧 +3.884000 はプローブの section mark だった**（撤回済） |
| `page.ossia-control.compressed.last-staff-to-foot` | **+0.010000** | 足の固定項。頭の +0.024 と合わせて **0.034000 == 36 × 0.000945**（＝下の 2 点の force）で閉じる |
| `system.ossia-control.compressed-distance` | **−0.003778** | ★ **第2の量ではない**——inside の残差 **ちょうど 4 倍**（強度 4・比 3.998）。§5.3 の「残差÷強度」の実演 |
| `staff.ossia-control.compressed.staff-staff-inside` | **−0.000945** | ★★ **対照はほぼ exact＝Lily# は非群の圧縮ページを千分の一で解く**。強度 1 と 4 は移植済で、残るのは上の 2 固定項だけ |
| `lyrics.hara-kiri.{shown,hidden}-system.staff-to-lyric` | **+0.271310** ×2 | ★ **Stage 1 で 1.500000 が取れて 2 点が一致した**（旧 shown = 1.771310）。残りは**歌詞書体の床**＝`lyrics.two-staff.two-verse.staff-to-lyric` と同じ量。⚠️ **0 にしない** |
| `lyrics.hara-kiri.declared-only.staves-on-first-page` | **−2**（count） | ★ **宣言あり／なしが一致した**（旧 −4）。残る −2 は**両書に共通のページブレーカー欠陥**で、hara-kiri 島ではない。⚠️ ss の総和には入らない（`unit`） |
| `lyrics.two-verse.system-gap` | **+0.207200** | ★ **予約も walk になり、打ち消しが解けて上がった**（旧 +0.157200・予測どおり）。残りはフォント量＋小節番号 cap-height 0.046334 |
| `lyrics.two-staff.two-verse.system-gap` | **+0.207200** | ★ **双子と同値のまま**（六桁一致）＝ system gap は下の譜数を知らない、という control が移植後も成立 |
| `lyrics.two-staff.two-verse.staff-to-lyric` | **+0.271310** | ★ **移植済の残り＝歌詞書体差だけ**（up-extent 1.459200 対 1.187880）。⚠️ **0 にしてはいけない**——3.737890 へ寄せたらフォント量の fitting |
| `lyrics.between-staves.staff-to-lyric` | **+0.131349** | ★ **clef を譜スカイラインへ入れて 0.424149 から下がった**（旧 +1.472149）。**また両向きの net**＝歌詞書体 +0.271310 対 閉じ方 −0.139961。後者は **LP の skyline 当たり点が素朴な max の組より低い**ことで、**LP の instrument 待ち**＝▶0。⚠️ **定数で埋めない** |
| `lyrics.between-staves.two-verse.staff-to-lyric` | **+0.271310** | ★ **移植済（旧 +1.762110）＝台帳が事前に名指した床へ着地**。⚠️ **0 にしない**——3.737890 はフォント量の fitting |
| `lyrics.between-staves.two-verse.staff-staff-inside` | **+0.284136** | ★ **room を walk へ移して上がった（旧 +0.126936）＝打ち消しが解けた**。内訳は歌詞書体 +0.271310 ＋**閉じ方 0.012826**＝▶0。⚠️ **戻さない。**1 番の対照は exact＝そこでは ideal 9 が walk の和を上回る |
| `lyrics.chord-row.staff-to-lyric` | **+0.131349** | ★ **今回開いて今回移植した**（5.500000 → 4.159200）。**LP はこの本で LYRB と恒等**なので残差は Lily# の欠陥そのもの——中身は上の `between-staves.staff-to-lyric` と**同じ 1 個** |
| `lyrics.ossia.staff-to-lyric` | **+0.131349** | ★ **同じ量**。narrowing が ossia 側も同時に閉じたことの control。⚠️ **inside は開いていない**（LP 18.000000 対 Lily# 2 譜スパン＝`staff-refpoint-extent`。**spaceable 性そのものは閉じた**が、この extent を測る点はまだ無い） |
| `lyrics.chord-row.between-systems.staff-to-lyric` | **+0.002047** | ★★ **移植済（旧 +0.891186）**。残りは**フォント量 3 項の和だけ**（歌詞 descent +0.010956／row 上インク −0.010099／row descent −0.003277、×0.846154 で 3e-7 一致）。⚠️ **0 にしない** |
| `barnumber.{low,high}-melody.staff-to-baseline` | **−0.024440** ×2 | ★ **字形の床**（LP は数字のインク下端を置く／Lily# は baseline）。閉じるには書体メトリクス |
| clef sliver（`{page.stretched,page.clef}.first-staff-refpoint`・`system.clef-bounded-distance`） | 4e-5〜8.3e-4 | LP の実効 scale 未特定（§2C）。**LP を instrument するまで動かせない** |
| Pango 量子化の族（tuplet 4・tie/slur 6・強弱 1・`barline.next.down-stems-after-clef`） | 5e-6〜1.4e-3 | Lily# に無いテキスト metric＝**閉じる予定の無い名前付き残差**。⚠️ この分類は**伝聞で未再検証**（tie の 0.001391 が Pango で説明が付くかは未確認） |
| `system.stretched-distance` | −0.000414 | ★ **これは符頭**（単一譜 book W の束縛インクが符頭）。LP 0.550000 対 LILC 0.545000 で**未説明**＝フォント metric の問題。⚠️ **そこは埋めない**。⚠️ **`page.*`/`system.*two-staff` を同族と読まないこと**——あれは符尾で、第8セッション（`96641db7`）で閉じた |

**消えた容疑者**（再走査しない）: duration space・音符間の最小・行幅・demerits の 3 項・
DP の形・小節線・**自然幅そのもの**・**行分割そのもの**・**行頭 spring そのもの**・
**loose line 再配分の不在**（今回移植）。経緯は各コミットメッセージと `probes/*.ly` のヘッダに。

★ **縦 spacing spec の全数突き合わせは済んだ**（`5fba1ad7`・**再走査しない**）。
paper 7 件＋staff/loose 6 件を LP の宣言と 1 つずつ照合し、`MarkupMarkup` の 1 件を摘出して閉じた。
残る唯一の畳みは **`VerticalSpacingSpec` の `BasicDistance`/`MinimumDistance`/`Padding` を
「宣言なし」に出来ないこと**（`Stretchability` だけ `3193a851` で nullable 化済み）。
⚠️ **かつて名指していた「実害の候補 `nonstaff-unrelatedstaff-spacing`」は今回の対で否定された**
（§1 のセッション節）。**行き先の無い項目**なので、次に触る人は**別の実害を挙げるか落とすか**を
決めること。⚠️ **同時に「記録の誤り」が 3 件出た**
（台帳・probe ヘッダ・コード注釈が「`set_default_strength` が ideal を入れる」と主張していたが、
LP は当該 spec で stretchability を**明示宣言**している）。**コードは正しく記録が誤り**という向きで、
そのまま移植すると**正しいコードを壊す**——`LILYPOND-REF` の隣の主張を疑う理由がこれ（§5.2.1①）。

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

- ~~**loose line の量の 4 モデル**~~ — **閉じた**（2026-07-27・§1）。`AlignmentWalk` 1 本。
  ★ **この島の教訓は「モデルが何個あるかを数える前に、どれが効いているかを摂動で測る」**——
  コメントも台帳も**別のものを持ち主として名指していた**（§5.3 に汎化）。
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

- ~~★★ **clef の箱そのものが LP より大きい（Y 6 点）**~~ — ★★★ **閉じた**（第25セッション・
  `6c6be1af`）。**グリフの skyline は extent ではなくアウトライン**で、**どちらを使うかは
  grob ごとに宣言されている**（`scm/define-grobs.scm`: Clef:902・Flag:1625 は
  `always-vertical-skylines-from-stencil`／Accidental:35・Rest:2958 は unpure 形／
  **NoteHead:2595・StaffSymbol:3391・Dots:1272 は宣言なし＝ extent**）。
  ⇒ **12 点が 1e-7 まで閉じ、3 点は exact**。**clef sliver 族は消滅**し、
  `system.stretched-distance` の「未説明の 0.005＝フォント量」も**符頭ではなく clef だった**。
  ⚠️ **一般則を一律に当てるのは誤り**（notehead は extent のまま＝アウトラインを seed したら
  0.001 の発明になる）。**新しい grob を足すときは define-grobs.scm の行を先に読む。**
  ⚠️ 残った lyrics 3 点の上昇は**打ち消しの解除**（§5.3）。
  ★★★ ⚠️ **ただしこれは移植の半分だった**（2026-07-28・第26セッション）。
  `6c6be1af` が入れたのは**アウトラインの箱**で、LP が skyline に入れるのは
  **アウトラインの多角形**（`freetype.cc:174-202` は `add_box` ではなく輪郭を折って振り分ける）。
  箱は `max_height` を再現するので**1 枚に当たる読みは全部合い、2 枚の pointwise 比較だけ
  外れる**——それが `lyrics.*.staff-to-lyric` に残っていた **−0.105961**。
  ⇒ **残り半分は書いてある（未 commit・▶0）。** 下の「移植の道筋は確定」は**箱までの話**。
  ⚠️ **同じ半分が Flag / Accidental / Rest にも残っている**（`define-grobs.scm` が stencil から
  と宣言している grob 全部）。clef と違って**台帳点も踏む本も無い**ので、次は点が先。
- （以下は上の項目の旧記述・**経緯として残す**）★★ **clef の箱そのものが LP より大きい** —
  **LILC の `clefs.G` は LP の stencil より上に 0.024000・下に 0.010000 はみ出している**。
  ⇒ **中央線の上**: Lily# 3.800000（＝`ClefG.Top` − 1.0）対 LP **3.776000**。
  **中央線の下**: Lily# 3.550000 対 LP **3.540000**。
  ★ **摂動で確定済**（bbox の top / bottom を振ると、対応する点だけが係数 1 で動く）。
  ⇒ **これ 1 個で次が全部説明できる**: `page.ossia-{control,pair}.compressed.first-staff-refpoint`
  の頭（+0.024000）／同 `last-staff-to-foot` の足（+0.010000 ×2）／
  `page.clef.first-staff-refpoint`（−8.3e-5＝**足の 0.010 が force 経由で薄まった姿**）。
  ⚠️ **はみ出しは非対称なので scale ではない**。⚠️ **既知の 0.27% 実効 scale でもない**
  （0.27% は下の 0.010 は説明するが、上は 0.012960 にしかならず実測 0.024000 に届かない）。
  ★★★ **機構は割れた（2026-07-28・LP を dump した）＝「保留」ではなくなった。**
  **グリフの skyline は extent ではない**:
  `PROBEG CLEF-G ext=(-2.550 . 4.800) skyline=(-2.540 . 4.776)`
  （notehead と staff symbol は ext == skyline。**箱を埋めるグリフだけ一致する**）。
  **LILYPOND-REF: `lily/stencil-integral.cc:535-563` `add_named_glyph_segments`** ——
  宣言 bbox（LILC）と**アウトラインの bbox**（`get_glyph_outline_bbox`）を両方取り、
  **`bbox[X].length() / real_bbox[X].length()`（＝幅の比）**でアウトラインを scale して
  skyline に入れる。⇒ **縦の数はアウトライン自身の値**を幅の比で運んだもの。
  ★★ **これが §2C に「未特定の 0.27%」として何セッションも載っていた「実効 scale 0.004 対
  0.003989」の正体**——**定数ではなく「宣言幅 ÷ アウトライン幅」でグリフごとに違う**。
  **定数だと思って探していたから閉じなかった。**
  ★★★ **そして実効 scale は素の単位換算 0.004 だった**（同日・fontTools と LP の dump で確認）。
  `clefs.G` のアウトライン bbox は **(2, −635)〜(645, 1194)**（font units・
  `freetype.cc:68 ly_FT_get_glyph_outline_bbox` は `FT_LOAD_NO_SCALE` + `FT_Outline_Get_BBox`
  ＝**素の font units**）で、**635×0.004 = 2.540 / 1194×0.004 = 4.776**＝**LP の dump と六桁一致**。
  ⚠️ **`bbox[X]/real_bbox[X]` は CFF では 1**（`get_unscaled_indexed_char_dimensions` が
  アウトラインと一致する。LP 自身のコメント `:549-550` が「real extents に基づくなら」と書いている）
  ⇒ **残るのは LILC を staff space に直す係数そのもの**で、**生成器が既に使っている 0.004**。
  ⚠️ **旧記述の 0.003989 は単位の取り違え**（`2.565`＝**staff space** ÷ `643`＝**font unit**）。
  ⇒ **移植の道筋は確定・instrument も SKPath も不要**: ⑴ 生成器（fontTools）が
  **`outlineBBox × 0.004`** を**第2の箱**として出す ⑵ スカイラインはそれを seed する
  （`GlyphMetrics` の extent 側は LILC のまま＝**LP と同じ 2 本立て**）。
  ⚠️ ⚠️ **ただし値段が大きい**——**clef を持つ全ての本の予約が動く**ので snapshot は大規模。
  **単独セッション＋承認ゲートで。** ⚠️ **bbox を実測に合わせるのは §5.2 違反のまま**
  （**上の 6 桁一致は「アウトラインから導いた」ものであって「実測に合わせた」ものではない**）。
- **スラーの `move_away_from_staffline` 未移植**（`slur-scoring.cc:640-658`）＝端点が五線の線上
  （±0.2）に落ちると 0.15ss 外へ弾く。既存の点では発火しない＝**端点が線に載る fixture を対で**。

### D. Y 軸（ページ縦）の残り

- ~~**譜間ばねがページの鎖に無い**~~ — **移植済**（`c309b751`）。**圧縮側も台帳点あり**
  （`8b7b2615`。`page.compressed.staff-staff-inside` ほか）。~~残る名前付き乖離は
  **ossia ペアが rigid**~~ — **閉じた**（`489ac6d7`）。~~**loose line 再配分の不在**~~ — **移植済**
  （`ce3be1af`＋`90e47848`）。⚠️ **譜数によらず「最後の spaceable 譜の下」の鎖は解く**
  ようになった（`90e47848`）。⚠️ **グループ間歌詞も、chords 行を持つ system も
  2026-07-27 に解けるようになった**（§1・`9660e5d8`）。**ossia も 2026-07-28 に入った**
  （`489ac6d7`）。force 0 のまま残るのは
  **lyrics 行／譜間に立つ row**＝§1 の 0 番。歌詞行 1 本では **LP も動かさない**
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

#### ★ 譜間ばね移植（`c309b751`+`8b7b2615`）で**字面から外れた 1 件と未移植 3 件**

⚠️ **出力は正しいが LP の書き方ではない**＝§5.2 の「報告する」に該当。コード側にも同じ注記あり。

| | 現状 | 字面の姿 |
|---|---|---|
| ~~① **ばねの床の作り方**~~ | **閉じた**（`de270892`・2026-07-28） | 床は `AlignmentMinimumWithSkylines` を**直接読む**（＝`minimum_offsets_with_min_dist[i]−[i+1]`・`page-layout-problem.cc:699-704`）。★★ **逆算は消せなかったのではなく、消すと壊れる状態だった**——`StaffGap` の第2引数が**呼び手によって別の量**（群間は refpoint スパン／群内は上の譜の**全高**）で、群内は中心間距離を**上端間距離として扱っていた**。逆算はその誤りを**吸収して**「ばねの静止長＝描かれた距離」を保っていた。⇒ **2 つ同時**（スパンへの統一＋直接読み）で閉じた。**byte 不変**（踏める本が無い＝§5.2 の裏面で書いた）。網は `UnequalStavesInOneGroup_ArePlacedCentreToCentre`（**修正前 7.250000 対 9.000000**）。⚠️ **`RefpointSpanToGap` の「群内は名目のまま」注記もこれで消えた** |
| ② **フレーム変換の置き場所** | ばねを作る側で span を引く（`PageLayouter`） | LP は `build_system_skyline` 内で**スカイラインを raise**（`:1120-1126`）。⚠️ これは **system スカイライン**の話で、**譜ごとのスカイラインは `6bb5a1de` で refpoint 枠へ移した**（§1）＝別件。移すと `SkylineBuilder` の読み手が巻き込まれるのは同じなので、**島1 の手順を実際に踏むこと**——`6bb5a1de` がその実演で、**先に `StaffSkylineFrameTests` を書かずに試した 1 回目は失敗した**（どの seed が動いたか誰も言えなかった） |

**未移植（`StaffSprings` の remarks に列挙済）**: ⑴ `alignment-distances`（`:706-717`＝
`line-break-system-details` 由来の手動指定でばねを**剛体**にする。**Lily# に言語表面が無い**ので
入れるなら文法から）⑵ 最初の spaceable 譜の loose line 用の床（`:667-670`）
⑶ `include_fixed_spacing` の第2制約（`align-interface.cc:240-267`）。⑵⑶ は
**loose line 再配分の不在と同根**なので、そちらと一緒に。

⚠️ **`StaffSpacingParameters.ApplyOverrides` の `alignment_distances` REF は誤りだった**
（2026-07-26 に削除）。実装は `\override StaffGrouper.staff-staff-spacing.*` で**別量**
＝§5.2.1① の「REF の隣が別の式」の 2 例目。**REF を見たら隣の式を読むこと。**

### E. 未移植の LP 計算・座標系の島2

- **未移植 LP 計算**: tuplet on-line / volta shorten / hairpin niente / ~~ledger~~ / brace /
  開 chord / Ignatzek。出典 `HANDOFF-lp-calc-incorporation.md`（§8）。
  **伝聞なので着手前に実コードで裏取り。**
  ★ **その裏取りを 1 件やった（2026-07-30・第39セッション）——「ledger」は半分 stale だった**:
  **加線インクは最初から staff skyline に入っている**（`SkylineBuilder.AddNoteBoxToSkylines`・
  `LedgerLengthFraction * headWidth` で左右に広げ厚みは `LegerLineThickness`）。
  第38セッションが TXW を「加線が支持に入る」と誤読したのは**この事実を知らなかったから**でもある。
  **本当に未移植なのは `LedgerLineSpanner` 自身の計算**: 隣接加線が近いときの
  `max_ledger_extent` 短縮と `ledger_shortening_range`（`ledger-line-spanner.cc:279-330`）、
  `Staff_symbol::ledger_positions`（線位置を変えた譜）。⚠️ そして
  **`LedgerLineSpannerEngraver` の出力（`LedgerLineSpan`）は `ScoreLayout` に載るだけで
  誰も描かない**（描くのは符頭経路）＝**加算メタデータのまま**。その engraver は
  `MergeThreshold 1.5` という独自装置を持つので、**短縮を移植する人はそこが家**。
- **座標系の島2（device 島群）は繰延**: TieVariant / 水平 skyline の Y horizon / TabStaffGeometry /
  beam collision island。`StaffOffsetInSystemDown` の残り呼び出しは**意図的な device 境界＝消さない**。
  島1 が残した手順: ①格納を反転する前に格納値を主張するテストを書く ②生産側は全部同時に
  ③**device 島の縁では 1 回だけ反射する**（反射を島の内側へ押し込まない）。

### F. 言語・ツール側（X/Y とは独立・**一覧は伝聞。着手前に実コードで確認**）

- ★ **`font "NAME"` を指定すると、予約と描画が別の face になる**（2026-07-27・▶0 の
  P1 を設計中に実コードで発見。**まだ台帳点も対も無い**）。`TextFontDrawingContext` は
  **描画時に**デコレータで generic family を指定 face へ差し替える（`:105-112`）が、
  `TextFontMetrics` は**レイアウト時に常に束ねた TeX Gyre Schola / Heros を測る**
  （`Faces` のキーが `(Sans, Style)` だけ）。⇒ **歌詞の descent も強弱も小節番号も
  和音記号の幅も、指定 face では予約が合わない。** ⚠️ 束ねた 2 面は LP 自身の text face
  （C059 / Nimbus Sans の metric 双子）なので**既定では正しい**——壊れるのは
  `font` を書いたときだけ。⚠️ **新しくテキスト量を測るコードは自前で face を持たず
  `TextFontMetrics` に乗せること**（1 つの家）。ここを塞げば全部同時に直る。
  **着手するなら対を先に**（`font` 指定つきの fixture が要る）
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

> **§2G の債務は 2026-07-27 に一掃した**（`921787a7`／`10267f6f`／`b06f7391`／`6c9fba1b`）。
> 残すのは**次の人が蒸し返しやすい 4 つの判断**だけ:
>
> - **テスト専用に見える 3 メソッドは消さない**（`CalculateSystemHeight(3 引数)`・
>   `LayoutStaffGroups(score)`・`LayoutStaffGroups(score, start, end, isFirstSystem)`）。
>   支えているのはフレーム不変条件・liveness と括弧の幾何・delimiter 種別＝実在の主張。
>   スカイライン無し経路は **LP の pure 見積り**（`align-interface.cc:234-238`）に対応するので、
>   **spec を摂動するテストはむしろそちらが正しい**（`HaraKiriSystemHeight_*` は意図的にそのまま。
>   `BraceCollapseTests` は描画幾何なので製品経路へ移した）
> - **`Layout()` の prologue と `CalculateAnnotationLayouts` の共有機構は意図的に残した**——
>   前者は 11 値＋ローカル関数、後者は全エングレーバが読む機構で、出しても引数で戻すだけ
> - **歌詞と和音記号の skyline lookup は遅延構築が仕様**（該当スコアが無ければ一切働かない）。
>   「簡素化」で eager にしないこと
> - **`StaffSprings` の `staffSkylines` は非 nullable**。null 経路＝「床＝描画距離」は
>   Stage 2 が閉じた欠陥そのものなので、復活させない

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
  ★ **これは単独の島ではない（2026-07-29 に束ねた）**——**同じ「paper column モデルの欠落」を
  指す件が 3 つある**: ⑴ この `ownFixedFloor`（grace/歌詞の独立列）⑵ **和音行の command 列**
  （第28セッションで発見・`ApplyRowCommandColumnSprings` は 2 本のばねの**直列合成**で数値は
  厳密だが、LP は空の command 列を実体として持つ）⑶ **mid-measure clef/key/time**（LP はそれを
  command 列に載せる。Lily# は `MidMeasureChangeGaps` が代役・§2B の mid-line clef 残件と同根）。
  モデルに列を足す日はこの 3 つを一緒に見ること（⑵ grouper・⑸ 倍率と同じ「モデル追加が先」型）。
  ⚠️ ただし**数値の乖離は現状ゼロ**（合成が厳密なので）——着手根拠は点が出た regime だけ
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
| ❌ **撤回（2026-07-27・ユーザー判断）: 独立 lyrics 行を「譜のような帯」として置く** — **もう決定ではない。蒸し返し禁止の対象から外れた。** | **旧決定**（2026-07-26）: 独立行は「譜に付く歌詞」ではなく**リードシートの word トラック**なので譜グループとして置く＝**9.600000 対 LP 5.500000＝+4.100000**、台帳には載せず導出形で主張。**撤回の理由**は「間違いだったから」ではなく**射程が二度狭まって残らなかった**から: ①2026-07-27 に「鎖に参加しない」部分が `lyrics.chord-row.between-systems.*` の実測で落ち、②同日 LYRR/LYRRV が **LP 側の恒等を 59 行の機械差分で確定**させ（`\lyricsto` の有無で LP は 1 行も変わらない）、**残っていた「距離」も Lily# 単独の量**だと分かった。⇒ 行は `nonstaff-relatedstaff-spacing` で自分のインクから置かれる（`dee2c045` 系）。**いまの状態**: `lyrics.row.staff-to-lyric` は**台帳点で exact**、`LyricRowIsSpacedLikeTheLyricsContextItIs` が**2 つの綴りが一致すること**（＝LP の恒等の再現）を主張する。⚠️ **帯そのものは残っている**——行は自前の小節線を持ち verse を band 内に積む（`LyricRowBaseline` は `LILYSHARP-OWN` のまま）。**消えたのは「どこに置くか」だけ。** ★ **2026-07-28 に鎖にも入った**（§1 の第20セッション）。**帯そのものはまだ残る**が、
system の最後の spaceable 譜の下に立つ行は **verse ごとに鎖の要素**で、帯の上端は解に従う。
`lyrics.row.two-verse.verse-step` は exact、LYRRV ≡ LYRV|
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
- ★ ⚠️ **予測には「向き」も書く。向きが外れたら機構が違う**（2026-07-27）。LYRMC で
  「chords 行が room を食うので歌詞は**押し出される**」と予測して、実際は **4.608814 へ
  引き寄せられた**。**room は固定**（loose line はページの鎖に居ないので system 間距離は
  増えない）なので、**占有者が増えると変位ではなく圧縮**する。量だけ予測して符号を書かないと、
  この種の取り違えは当たったように見えてしまう。
- ★★ ⚠️ **和に対する falsifier は、項ごとの符号が分かっていない限り和の符号を決め打たない**
  （2026-07-27・LYRMC で実際に踏んだ）。「移植が効けば 4.608814 より**下**に着地する。上なら
  row は鎖に入っていない」と書いたが、**正しい移植が上に着地した**——残差は
  `0.846154 ×（LP の m2+m3 − Lily# の m2+m3）`で、Lily# のフォント量は**全部が大きいわけでは
  なかった**（serif の歌詞面は大きく、bold sans の和音面は**小さい**）。⇒ 決め打ちの根拠は
  「歌詞面が 27% 広い」という**和音面については測っていない**話だった。
  ⚠️ **仕事をしたのは「5.500000 のままなら入っていない」のほう**——機構を名指す枝は残し、
  量の符号を名指す枝は**測ってからでないと書かない**。⚠️ そして**falsifier が鳴ったら、まず
  falsifier を疑う**: 内訳が 6 桁で閉じるなら外れたのは予測の符号であって移植ではない。
- ★★ ⚠️ **対を「フォーク」として組む——2 つの結果が別の数でなく、別の種類の作業を選ぶように。**
  （2026-07-27）LYRMC の設計は「LP が control と**同値なら**ガードを狭める作業／**違えば**
  モデルを動かす作業」。測る前にどちらの枝も書いてあるので、**結果が出た瞬間に次の作業が
  確定する**。数だけ狙う対は「で、これをどう直すのか」を после に残す。
- ★★ ⚠️ **「その島は snapshot が N 枚動く」を見積りに使わない**（2026-07-28）。N は**何を
  変えるか**で桁が変わる: 同じ島の同じ目的で、**譜線の span だけ**なら 1 枚、**選択と中央線と
  clef も一度に**なら 19 枚だった。⇒ **島を「値段」で先送りする前に、量ごとに割って測る。**
  ⚠️ そして**その台帳点が既にあるかを先に見る**——今回 LP の 7.600000 は
  `tab.staff.line-span.six-string` の `why` に**最初から書いてあり**、20 分の実測は不要だった。
- ★ **既存の本に 1 行足しただけの本は、LP 側が恒等になりやすい**（2026-07-27・LYRCH）。
  上の「恒等の対が最強」を**狙って作る**手口がこれ: 既に理解できている本に**加えるだけ**なら、
  LP がその追加を無視する量については差が 0 になり、**Lily# の差がそのまま欠陥の量**になる。
  ⚠️ ただし**恒等かどうかは測って確かめる**こと。LYROS（ossia を 1 本足した本）は chain は
  恒等だったが inside は 18.000000 対 9.000000 で**別物**だった——`staff-refpoint-extent` は
  spaceable な譜だけを張るので、LP で spaceable な行を足すと**測っている量の意味が変わる**。
- ★ ⚠️ **恒等を主張するなら dump 全体で比較する。予測が名指した数だけ照合しない**（2026-07-27）。
  LYRRV の予測は 3 つの数を挙げていたが、照合は**両 book の dump を機械的に行単位で差分**した
  （59 行・完全一致）。3 つだけ見ていたら**4 つ目が動いても「恒等」と書いていた**。
  ⚠️ 目視で「同じに見える」も同じ穴——**差分は道具に取らせる。**
- ★★★ ⚠️ **六桁で閉じた分解は、各項を出典から読むまで証拠にならない**（2026-07-30・第39セッション
  が第38セッションの TXW 分解を全項訂正）。`加線 4.05 + padding 0.5 + 波 0.170721` と
  `加線 4.100000 + 0.460000 + 0.160721` は**同じ 4.720721**——項が 3 つあれば、誤った機構でも
  和はいくらでも合わせられる（§5.2 の「打ち消し合う 2 つの誤り」の 3 項版）。
  ⚠️ **判定法**: その項の**数**を LP のどの行から引いたか（4.05 は引けない・4.1 は
  `staff-symbol.cc:337-344` から引ける）。引けない項が 1 つでもあれば、分解ではなく当てはめ。
  ⇒ ★★ **そして当てはめは「どの pass が勝ったか」を取り違える**——0.5 と 0.46 のどちらを
  払っているかは**port 先が別のファイルになる**ほどの違いだった（engraver か stacker か）。
- ★★★ ⚠️ **extent から推論すると、skyline にしか居ない grob を構造的に見落とす**
  （同上）。LP には **`X-extent #f`/`Y-extent #f` を宣言しつつ `vertical-skylines` を stencil から
  持つ grob**が居る（LedgerLineSpanner・`define-grobs.scm:2072-2074`）＝**どんな extent 計算にも
  現れず、衝突 pass だけが見る**。ext dump だけを読んでいた第38セッションはこれを
  「支持に加線が入る」と誤読した。⇒ ★ **分解は grob の skyline を dump して読む**:
  `ly:skyline->points` / `ly:skyline-max-height`（`lily/skyline-scheme.cc`）で
  **プロファイルそのもの**が出る。**mover 側の 2 片アウトラインも、障害物の X も、これで見える。**
- ★★ ⚠️ **commit message で「この配置に対応した」と書いたら、その配置を 1 回描く**
  （2026-07-28・同セッションで回帰を出して直した）。`fe679617` は
  「音符束縛と行が同じアンカーに共存する譜のためにキーを一般化した」と**名指したうえで、
  その本を一度も走らせなかった**——実際には別の機構（族の分裂）で割れていて、**2 行が重なって
  commit された**。全テスト緑・snapshot 不動・台帳不動で、**網は 1 つも鳴らない**。
  ⇒ ⚠️ **「対応した」と書いた配置は、テストが無いなら最低でも `png --crop` を 1 枚。**
  名指しは仮説であって観測ではない（§5.3 の「推論せず測る」の、文章版）。
  ★ **散文にも同じ規則が要る**（2026-07-28・`de270892` → `e1d5c5f8`）: commit message に
  「この注記を消した」と書いて**消していなかった**。残った注記は**コードと逆のことを言う**ので、
  触らなかったより悪い。⇒ **「消した」と書く前に旧文言で grep する**（1 行で済む）。
- ★★ ⚠️ **1 つの claim が N 個の量に分かれているとき、「1 つずつ当てて良くなったものだけ入れる」は
  使えない。分割すると悪化することがある**（2026-07-28・ossia）。「ossia は spaceable」は
  ばね・spec・spaceable 性の 3 つで、**ばねだけ入れると両方の読みが遠くなった**
  （inside 9.000000 → 8.350000・system → 17.350000。鎖にばねができたのに配置は外に置いたまま＝
  **フレームが割れる**）。⇒ **摂動は「持ち主の特定」には使えるが「入れるかどうかの判定」には
  使えない**（§5.3 の摂動は前者）。**判定は台帳の falsifier に置く**——今回それは
  「**4 読みが揃って着地しなければ移植は終わっていない**」で、前セッションが移植の前に書いていた。
  ⇒ ★ **点を開けるとき、「何が揃えば終わりか」を同時に書く。**
- ★★ ⚠️ **撤回した一致が、別の観測として戻ってくることがある。撤回したのは数ではなく導出**
  （2026-07-28・ossia の 8.414200）。前セッションは**section mark 混入の 2 読みの引き算**から
  この数を出して撤回した。今回それは **snapshot 3 枚のページ高の実測差**として現れ、
  **アンカーが名目半譜 2.000000 から対の 9 を上がって ossia の scaled 半譜 1.414200 へ移る**という
  **経路の帰結**だった。⇒ **撤回した数を「禁止された数」として扱わない。禁止されたのは
  「共通の混入を確かめずに一致を証拠にすること」**であって、数そのものではない。
- ★★ ⚠️ **「構造上できない」と引き継がれた項目は、その構造の主張を 1 回確かめる**
  （2026-07-28・§2D ①）。「ばねの床の逆算は、配置が譜の上端フレームで答えを持っていて
  refpoint 側の最小を誰も保持していないから消せない」と何セッションも引き継がれていたが、
  **理由のほうが誤りだった**——`StaffGap` の第2引数が**呼び手によって別の量**（群間は refpoint
  スパン／群内は上の譜の全高）で、**逆算はその食い違いを吸収していた**。量を統一したら
  逆算は**訂正するものが無くなって**消えた。⇒ **stale になるのは数だけではない。理由もである。**
  ⚠️ **入口は「その主張が正しければ何が観測されるはずか」を 1 つ書いて測ること**——
  今回は「直接呼びに替えれば出力が動くはず」と書いて**動かなかった**のが端緒だった。
- ⚠️ **穴を開けるまで、そこに何が溜まっているかは分からない。** 点を開く／種を入れると、
  狙っていた欠陥と**一緒に狙っていなかった欠陥が落ちる**（これまで一度の例外もなく起きた）。
  だから **control（対の基準側）が非ゼロで開くのは正常**——それは持参金であって失敗ではない。
- ★ ⚠️ **「どの fixture がこの経路を踏むか」を宣言の grep で数えない。暗黙に立てる経路がある。**
  （2026-07-26）hara-kiri の高さ分岐を直す前に「`removeEmpty` を持つ fixture は 2 つだけ・
  どちらも 1 群なので出力は動かない」と数えたが、**ossia は `removeEmpty` を暗黙に立てる**ので
  snapshot 3 枚が動いた。⇒ **数えるなら宣言ではなく、分岐が読むフラグが立つ場所**
  （`RemoveEmpty` を**書き込む**側）を grep する。⚠️ そして動いた 3 枚は
  **その分岐が第2の量（ossia スケール）も落としていた**ことを見せた——
  **予測から漏れた fixture は、たいてい漏れた理由のほうが所見**。
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

- ★★★ ⚠️ **「フォント量」は残差の分類として弱い。札を貼る前に LP 側の定数を引くこと**
  （2026-07-28・**1 セッションで 3 回同じことが起きた**）。台帳は 3 つの族に
  「これは両者の書体の差だから 0 にしない」と書いていたが、**3 つとも書体の差ではなく、
  出典のある定数の取り違えだった**:
  **小節番号**（素の `1.3` ＋ baseline を平坦と仮定 ⇒ LP は `side-position` でインク下端を置く）／
  **歌詞の em**（`3.2` ⇒ LP の `LyricText font-size 1.0` = 2.469417・**29.6% 大**）／
  **和音記号の em**（`2.6` ⇒ `ChordName font-size 1.5` = 2.616256）。
  ⇒ **正しい定数で*同じ face* を実測すると LP と 1e-4 で合う**（歌詞は 0.000091）。
  台帳総和は **2.80 → 0.0125** に落ちた。
  ⚠️ **見分け方**: 「書体が違う」なら**比が glyph ごとにばらつく**はず。**全エントリが同じ量**
  （+0.271310 が 8 点）なら、それは**スカラー**——つまりサイズか、掛かっている定数である。
  ⚠️ **そして「サイズ」と「メトリクスの出所」は 1 つの主張の 2 つの半分**で、
  片方だけ入れると**行き過ぎる**（歌詞: 旧 em でアウトラインを読むと 1.539200 で*悪化*）。
  ⇒ ★ **`LILYSHARP-OWN` の隣に LP の規則が引用してあるなら、その規則を計算してみる**——
  和音記号の 2.6 は**自分のコメントが名指した数の近似**だった。

- ★★★ ⚠️ **量を直すと、その量を直書きしている「網」が一斉に落ちる。落ち方は全部
  「測っていない」向き**（2026-07-28・**1 セッションで 5 件**）。
  `RenderedGeometry.LyricSyllables` は音節を **`FontSize == 3.2`** で選んでいた（18 点が
  「音節が描かれていない」で落ちた）／`SharedRendererPdfTests` は **19.2pt**／
  `StaffSkylineFrameTests` と `SkylineStaffSpacingTests` は **2.6**／
  `LyricStaffOrderTests` は**床が binding しなくなって両書とも ideal を読んだ**。
  ⇒ ★ **大声で落ちるのは良い方**（値を pin した網は必ず落ちる）。**黙って regime を外れるのが悪い方**
  ——だから §5.0 罠 7 の「自分がその regime に居ることを assert させる」が要る。
  ⇒ ★ **網が値を pin していたら、それは「同じ量の N 個目の家」**（§5.2.1⑤）。**共有定数に変える。**

- ★★★ ⚠️ **新しい対は「専用プローブ 1 ファイル」で固める。`page-vertical.ly` に足さない**
  （2026-07-28・第26セッション）。**測る値段が桁で違う**: `page-vertical.ly` は **book 62 冊**で
  **全冊 20 分以上**・MCP のタイムアウトでは完走できずデタッチ＋ポーリングが要る（§5.5）のに対し、
  **専用プローブ（4 冊）は約 80 秒で 1 ツールコールに収まる**。第26セッションは後者を 3 回まわして
  何セッションも BLOCKED だった量を割った。
  ★★ **そして台帳は `probe` を*エントリ単位*で持つ**——既に 6 ファイルに分散している
  （`page-vertical.ly` 111／`barline-spacing.ly` 51／`staffless-system.ly` 8 ほか）。
  **`\book` は互いに独立**なので、**専用プローブに置いた点は 80 秒で再測定でき、
  既存 111 点の 20 分を一切引きずらない。**
  ⇒ ⚠️ **`page-vertical.ly` に足してよいのは「その dump の形そのもの」を測る点だけ。**
  それ以外を足すと、**以後その点を触る全員が 20 分を払う**（値段は足した人ではなく次の人が払う）。

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
   ★ **2026-07-26 に同じ罠でもう一度落ちかけた**（Stage 2）。ばねの最小を測る対を
   LYRHKD/LYRHKN で組んだら**修正前も後も pass**——歌詞で system が高く 7 本しか載らず
   **inside 9.166134 ＞ ideal ＝伸長**で、**最小はそもそも binding していなかった**。
   ⚠️ **`max-systems-per-page` は「上限」なので圧縮を作れない**。この本の本数は
   内容律速で、8→10→12→14 と上げても 7 のまま。**regime を変えるには音楽か紙を替える**
   （JSK の音楽へ移して解決）。⇒ ★ **不変条件テストには「自分がその regime に居ること」を
   assert させる**。そうしないと**黙って測定をやめる**。実例
   `RemoveEmptyDeclaration_WithNothingEmpty_ChangesNothing`（2 regime の Theory）。
8. ★ **paper 変数が Lily# 側で fallback を踏むと、存在しないページを測る**（2026-07-26）。
   `systems-per-page = #6` は LP では効くが、Lily# の `PageBreaker` は
   **本数が厳密に一致しない候補を全部落とす**ので最終ページが 5 本になる曲では解が無くなり、
   **1 ページに全 system**（内容サイズ紙）へ落ちる。距離の点は 3 つとも「もっともらしい数」を
   返し、**気づいたのは本数の点だけ**。⇒ **index で読む点には必ず「そのページの本数」の点を
   添える**（§5.2.1 の網の実例）。
9. ★ **キャッシュを測る probe で「同じ編集を往復させる」と 0 回と出る**（2026-07-27）。
   増分キャッシュの効きを見るのに `d'`↔`e'` を交互に打ったら**再構築 0 回**。
   `TypedCache` は世代方式で**2 手前のエントリをまだ持っている**ので、測っていたのは
   メモではなく**世代方針**だった。⇒ **編集は毎回別の値へ**（`e' f' g' …`）。
   正しく測ると 1 回（＝変更された system だけ）。⚠️ **0 は「速い」ではなく
   「測れていない」の顔をして出てくる。**
11. ★★ **Lily# 側だけがセクション記号を彫っていた**（2026-07-28）。`form main { A }` は
    **記号を 1 個engrave して約 3.86 の頭を予約する**が、`.ly` 側は `\header` も title も
    markup も持たない（page-vertical.ly の冒頭が「system が title でないこと」を要件として
    明記している）。⇒ **ページの頭・足・force を読む点が全部 3.884 ずれ**、しかも
    **残差 ÷ 強度が六桁で閉じた**ので「Lily# が鎖の頭を下げすぎ」という**もっともらしい所見**に
    化けた。⚠️ **抑止は `~Name`**（§SYNTAX_REFERENCE「空文字列は記号を抑止する」）。
    ⇒ ★ **見破ったのは不動性**: その頭は**圧縮・伸長・1 system ページで同一**だった。
    **ばねなら regime で動く**（§5.3 の「摂動して動かない」の裏返し＝**不動が証拠になる側**）。
    ⚠️ **そして「六桁で閉じた」は共通の混入を除外していない**——
    **一致は、両側に同じものが混ざっていないことを確かめてから証拠になる。**
    ⚠️ 対の**ページ量**を読む本は `~` を確かめること（**inside だけ読む本は force 0 なら無害**で、
    OSSU/OSSUN が実際そうだった）。
    ★ **コーパス全体を監査済み（2026-07-28・結果ゼロ）**: 記号を彫る本は 17 冊あるが、
    **`page.*` / `system.*` の点が乗っている本は 1 冊も無い**（支えているのは barline・
    line-start・note-to-note・midmeasure・inside 距離・tab＝**頭の予約が動かさない量**）。
    ⇒ **この罠が他の点に座っている可能性は潰してある。再走査しない。**
    ⚠️ ただし**新しくページ量の点を足すときは、その本の `form` を必ず見ること。**
10. ★ **`PianoStaff` では譜は 1 つだけ消えない**（2026-07-27）。`\consists`
    `Keep_alive_together_engraver`＝「まとめてしか消えない」と **LP 自身が書いている**
    （`ly/engraver-init.ly:535-544`）。hara-kiri の対を既存の P/Q/D/TU に倣って
    `\new PianoStaff` で組んだら**何も消えず**、宣言あり／なしが**同一ページを出し全距離が
    一致した**。⇒ **hara-kiri の本は `GrandStaff`**（Lily# の `grandStaff` は分離除去する側で、
    `test/hara-kiri` がそれに依存している）。⚠️ 一般形は §5.2.1④ と同じ:
    **両側 exact は「合っている」ではなく「regime に入っていない」ことがある。**
    **その本で狙った現象が起きたことを、別の点（ここでは譜の本数）で必ず確かめる。**

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

- ★★★ ⚠️ **「掛け忘れ」を名前で防ごうとする前に、LP が掛けていないかを見る**
  （2026-07-28・第25セッション・`94705160`）。ossia のインクを縮める作業は「幅を持つ seed 15 か所で
  掛ける・漏らさない」に見えたが、**LP には掛ける場所が無い**——grob は**自分の文脈のフォント**から
  寸法を読み、そのフォントが既に縮めている（`modified-font-metric.cc:62-68` の
  `b.scale(magnification_)` の 3 行）。**字面に寄せたら掛け算が消え、漏れようがなくなった。**
  ⇒ ★ **規律（名前・チェックリスト）で守る前に、その作業自体が LP に存在するかを問う。**
  ★ **副産物として掃き漏れはコンパイラが列挙した**——単位を型（`StaffSize`）にして引数にしたので、
  未変換の呼び出しが**ビルドエラー**になり、**監査表が挙げていなかった 3 か所が出た**。
  ⇒ ★ **「grep で数える」より「型にして通らなくする」**（§5.2.1① の網の思想を座標系へ）。
- ★★ ⚠️ **「1 つの規則」に見えたら、それが grob ごとの宣言で分岐していないか先に見る**
  （2026-07-28・第25セッション）。「グリフの skyline はアウトライン」は**規則ではなく既定値**で、
  `scm/define-grobs.scm` が **grob ごとに** `vertical-skylines` を宣言している
  （Clef/Flag は stencil から＝アウトライン・NoteHead/StaffSymbol/Dots は**宣言なし＝extent**）。
  ⚠️ **一律に当てていたら符頭に 0.001 の発明を入れていた**——止めたのは **LP の dump**
  （`NOTEHEAD ext=0.545 skyline=0.545` に対し outline は 0.544）。
  ⇒ **`define-grobs.scm` の該当行は「その grob がどの機構に乗るか」の索引**として先に引く。
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
★★ ⚠️ **そして「出典を書け」すら、数値定数にしか効かない**（2026-07-27 に実際に踏み抜いた）。
歌詞行の島は **Core に 348 行足して `LILYPOND-REF` 1 本**——それも移動してきた既存行——
という状態で**このテストは緑のまま**だった。**数値定数を足さずに式・述語・メソッドだけ
足すと、網は 1 度も発火しない。** ⇒ ★ **「`LpProvenanceTests` が緑」を「出典を書いた」の
証拠にしないこと。** 移植したら **`git diff | grep -c 'LILYPOND-REF'` を自分で 1 回打つ**
（§7.5 の手順に入れてある）。⚠️ 出典を**書こうとする**と、書けない箇所がそのまま
**発明の在処**を教える——このときも 4 件の発明に名前が付き、それが次の島の設計になった。

**② 二重実装は「一致する」不変条件テストで縛る** — spring 2 系統＋改行 gate は既に §5.4 で
縛ってある。**縦も同じ扱いにする**。⚠️ **padding 4 倍のバグは「複製された側」（単一ページ経路）に
住んでいた。** 重複は美観ではなく**移植が半分しか当たらない場所**。新しい経路を足すときは
「既存の経路と同じ答えを出す」テストを同時に足すか、**そもそも足さず統合する**。
★★ ⚠️ **「1 つの walk」でも種が 2 綴りなら 2 モデル**（2026-07-28）。同じ `AlignmentWalk` を
使っていても、**どこから歩き始めるか**が別々に書いてあれば同じ欠陥。実例: 鎖は **system
スカイライン**から、予約は**アンカー譜自身のスカイライン**から歩いていた。**アンカーが system の
最下端である限り両者は同じ数**なので、何セッションも見えない——下に非 spaceable な行を 1 本
置いた瞬間に **1.050000** 割れた。⇒ ⚠️ **「同じ関数を呼んでいる」は一致の証拠にならない。
入力が同じ式から来ているかまで見る。** そして**「今は一致する」の理由が『たまたま最下端だから』
なら、それは regime であって不変条件ではない**（§5.2.1④ の親戚）。
⚠️ **frame は「どの要素か」と「どの高さか」を必ず一緒に運ぶ**: 片方だけ per-system にして
もう片方を score 全体のままにしたら、hara-kiri で**system gap ちょうど**ずれた。
★★ ⚠️ **家が 3 つあるとき、生きている家は regime で変わる**（2026-07-28）。同じ「半譜」が
`LayoutEngine.Layout` / `LayoutEngine.CreatePages` / `PageLayouter` の 3 か所にあり、**どれが
効くかは paper（単一ページか optimal paging か）で切り替わる**。摂動を 1 か所目に当てて
「出力が動かない＝この量は効いていない」と読みかけた（§5.3 の「摂動して動かない」の親戚だが、
原因は regime ではなく**複製**）。⇒ ⚠️ **摂動が動かなかったら、まず自分が当てた家が
その入力で生きているかを確かめる**（呼び出し側の分岐を 1 つ読む）。**そして 3 つ全部直す。**
★ ⚠️ **「綴りは N 個」と引き継がれた数も stale になる**（2026-07-28・第25セッション）。
`is_spaceable` は「`ClassifySystem` と他 2 つ」と引き継がれていたが、**実際は 5 つ**だった
（`BuildStaffAnchorTables` の中に 2 つ・`ComputeBetweenStavesEnd` にもう 1 つ）。
⇒ **潰す前に grep で数え直す。3 つ直して「1 本になった」と書くのが一番悪い**
——残り 2 つは**直したという記述に守られて**次の人から見えなくなる。

★★ ⚠️ **最悪の形は「本経路と fallback」**（2026-07-27）。同じ量に精密版と概算版があり、
概算版が**片方が空のときだけ**使われると、そこへ書いた移植は**コンパイルも通りテストも緑で、
ただ一度も効かない**。実例: 独立 lyrics 行の描画 extent は `perSystemExtents` に入っていたが、
行間ばねを床にするのは system **スカイライン**で、`perSystemExtents` は
**スカイラインが空のときだけ**読まれる——行は 2 番が次の system に重なったまま
出荷されていた。⇒ **量を足したら「それが読まれる経路」を摂動で 1 回確かめる**
（値を振って観測が動くか。動かなければ届いていない）。

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

**⑦ 出典の「中身」でなく「宛先」を機械に照合させる** — `LpReferenceCitationTests`
（2026-07-28・`74b78f71`）。①は**出典を書いたか**しか見ず、**書いた出典が正しいか**は
何年も誰も見ていなかった。⚠️ **これは実際に踏み抜いて作った網**である（§1 のセッション節）。
- ★★ **効くのは `CitationsThatNameNothing_DoNotGrow`**（**LP ツリー不要**・747 のラチェット）。
  行範囲を書いて**記号名を書いていない**引用を数える。⚠️ **数が網なのではない**——
  **満たすための代償が網**: 「:240-267 に何があるか名前を書け」と言われたら **:240-267 を読む**
  ことになり、そこにあるのは `include_fixed_spacing` / `is_spaceable` / `get_fixed_spacing`
  ＝**範囲自身が「お前が言いたい行はここか？」と答える**。①の「出典を書こうとすると
  書けない箇所が発明の在処を教える」の**一段内側**。
  ⚠️ **今朝の誤りで落ちることを実証済**（747 → 748）。
- 他 3 本: ファイル実在（hard 0）／行範囲がファイル内（hard 0・**今は何も落ちないが、
  LP の版が動いた日に全ファイルが一斉に狂う**ので置く）／名指した記号がそのファイルに在る
  （**17 件の明示リスト**。数でなくリストなのは、古い 1 件が開いたまま新しい 1 件を落とすため。
  **直ったら消さないと落ちる**＝台帳の「改善は diff に出す」を引用にも適用）。
- ★ **初回で 7 つの誤住所・35 箇所**が出た（`lily/bar-line.cc`・`ly/tablature-init.ly`・
  `lily/note-collision-interface.cc` ×19・`lily/grace-spacing.cc` ×10 ほか。
  **7 件中 4 件は LP が一度も持ったことのないファイル名**）。
- ★★ **うち 1 件は引用の誤りでは済まなかった**: `ly/tablature-init.ly` の実家を追うと、
  **タブの半音符の二重符尾 0.355 は「LP の出力から測った」値**で、LP は
  `double-stem-separation` **既定 0.5**（`scm/tablature.scm:107`）を読む。
  ⇒ **実測の貼り付け（§5.2 違反）が、出典が何も指していなかったせいで残っていた。**
  `LILYSHARP-OWN` を付けて明示。**0.5 で描くとタブ snapshot が動く**ので未実施。
- ⚠️ **精度は 2 つ要る**（コードに明記）: ラチェットは**再現率**（偽陽性は「名前を書いた」に
  数えるだけで無害）、実在検査は**適合率**（偽陽性は正しい引用でビルドを落とす）。
  だから**ハイフン 3 語は「名前」として数えるが、実在を主張するのは下線付きだけ**——
  実測で `end-to-end`・`staff-affinity-aware`・`if-no-beam` が引用の隣に出る。
  **Scheme 名を検証できない**ことは放棄として書いてある（塞ぐには LP の記号索引が要る）。
- ★ **実際に落ちた**（2026-07-28・第24セッション。**作った翌セッションに 1 回**）。原因は
  引用そのものではなく**書き方**——**記号名を行範囲と同じ行に置かず、次の行に書いた**。
  検査は**アドレスより後ろの同じ行**しか読まない（`AllCitations`）。
  ⇒ **REF を折り返すときは記号名をアドレスと同じ行に置く。**
- ⚠️ **やらないこと**: 「引用行が主張どおりのことを言っているか」。**機械には無理**で、
  できるふりをする checker は §5.4 の「推測するヘルパ」。**宛先を検証し、論証は検証しない。**

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
- ★★★ ⚠️ **perf の A/B は「1 セット」では測れない。最小は RUN をまたいで採る**
  （2026-07-30・第39セッション。**逆の答えを報告しかけた**）。base worktree を作った直後に
  min-of-50×3 を回したら **HEAD が全譜で速く**見えた——落ち着いてから base の 3 セットは
  41/41/42 になり、最初の **65/65/44** が雑音だったと判明（HEAD は退行側で +25%）。
  ⚠️ **判定法**: **同じ label の 3 セットが互いに 10% 以内か**。1 セットだけ大きく外れて
  いたら、それは測定ではなくマシンの状態。**両ツリーを 2 回以上まわし、label ごとの
  全体最小**を採る。⚠️ **ビルド直後・テスト実行直後のツリーは遅い側に偏る**（JIT/GC/
  ディスク）。
- ★★ ⚠️ **perf コメントに測っていない内訳を書かない**（同セッション・**§5.2 の「実測を
  貼るな」の裏返しで、こちらは「測ってない配分を書くな」**）。最適化のコメントに
  「マージは trill 多用譜の約 1/4」と書いたが**測っていなかった**（実測は「小さい」）。
  ⇒ **最適化のコメントには実測値 2 つ（前・後）だけを書き、原因の配分を主張しない。**
- **摂動法が強力**: `\override` で esw / padding を振り、係数1で追随するか不変かを見る。
  **全部ゼロにして残った定数**がハードコード値
- **測定 regime を混ぜない。** ragged-right（force 0）では spring の床、圧縮時は rod が
  binding する。**どちらで測ったか必ず記録する**
- ★ ⚠️ **圧縮はばねを「最小」へ押すので、最小が高い音楽では何も測れない**（2026-07-27）。
  譜間ばねの最小＝配置距離なので、**インクが spec を超える本を圧縮しても床に座るだけ**
  （高インクの対 HKW は 9.595000 で不動）。⇒ **圧縮で測るなら床がインクでない形を選ぶ**
  （JSK の音楽＝最小 7.545 ＜ 解 8.651797 ＜ ideal 9）。⚠️ **裏面**: 「その regime の点が
  無い」と「その regime は測れない」は別物。**書く前にどちらか確かめる**——前者として
  引き継ぎに書いてから後者だと分かった実例がある。
- ★★ ⚠️ **ばねの点を開けるときは、鎖の両端の点を「後で」ではなく同じ commit で開ける**
  （2026-07-28・第23セッション。上の「残差 ÷ 強度」の実践形）。ギャップの点だけ並べると、
  **1 個の固定項が全部の点に化けて出る**のに、ギャップの側にはそれを名指す材料が無い。
  実例: OSSK/OSSKN の 4 ギャップは Lily# でも LP でも **ちょうど 1 : 4**（九桁）＝
  **強度は両方正しく違うのは force だけ**と分かったが、**どちらの端が犯人かはギャップからは
  出ない**。頭と足の点を同時に開けたら **36 ×(force 差) = 3.894004 == 頭 3.884000 ＋ 足 0.010000**
  で 1 発で住所が出た。⇒ ★ **「4 つの点が動いた」ではなく「1 つの項が 4 回見えている」**。
  ⚠️ **端の点は対の両側に置く**——片側だけだと「その端が ossia のせいか鎖のせいか」が割れない
  （足は control +0.010000 対 ossia +0.685498 で、**割れた**）。
  ★★ ⚠️ **そしてその「割れた」の読み方には裏がある**（2026-07-28・移植して分かった）:
  足の +0.843439 は **ossia 固有の量ではなく、頭の誤りが鎖の反対端に出ていただけ**で、
  頭を直したら**対照と六桁で同一（+0.010000）になった**。⇒ **一方の端の固定項は、どの単独の
  読みから見ても他方の端の固定項と区別が付かない。** 対の両側に端を置くのは
  **「どちらの端か」を割るためであって、「その端に固有の量がある」ことの証拠にはならない。**
- ★ ⚠️ **剛体化したばねは局所の欠陥ではない。** force は**ページの slack ÷ 総強度**なので、
  動かないばね 1 本は**他の全ばねに出る**。⇒ **1 本だけ読む点は乖離を過小報告する**
  （実例: 譜ばねが `(9−9)/2 = 0` で吸わなかった分が system ばねの
  `(12−10.927848)/4 = 0.268038` に出ていた）。**対の片側には距離を 2 本持たせる。**
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
- ★ ⚠️ **性能は「回数」で測る。時間で測ると機械の雑音に負ける**（2026-07-27）。
  同一コードで median が 3 倍振れる環境なので、**時間は 10〜15 回の最小値**を見る。
  それより強いのは**呼び出し回数を数えること**——`BuildAllStaffSkylines` の
  2 → 2N → N →（キャッシュ後）1 は全部決定的で、時間の揺れに一切影響されない。
  ⚠️ **`dotnet test` の総秒数は性能の網ではない**。fixture が小さいので、
  **system 数に比例する劣化を 1 つも検出しなかった**（実際に見逃した）。
- ★ ⚠️ **`Corpus_ReportsTotalDivergence` は台帳ファイルの `residual` を印字するだけで、Lily# を
  測っていない**（2026-07-27）。§6 の「LP 忠実度スコア」コマンドは**台帳のエコー**で、
  コードをどう変えても**台帳を編集するまで 1 桁も動かない**。実測は per-id の
  `Geometry_MatchesLilyPondWithinTheRecordedResidual`。⇒ **摂動の効果はこのコマンドで
  判定しない**——`AlignmentMinimumBand` を 0 にしても +10 にしても「118/147 exact・2.277183」
  が出続け、危うく「この関数は死んでいる」と結論するところだった（実際は
  `lyrics.*.system-gap` 2 点が読んでいる）。**変更の効果を見るなら全テストを走らせて
  落ちた id を数える。**
- ★★ ⚠️ **「摂動して動かない」は、1 つの regime では「死んでいる」の証拠にならない**
  （2026-07-27・自分で誤診して同セッション中に訂正した）。`TextRowVerseSpacing` を振っても
  本 LYRRV は六桁不動だったので「`GetStaffHeight` の先で死んでいる疑い」と引継ぎに書いたが、
  **行を譜の上に置いた瞬間に係数 1 で効いた**（読む式は `HeightBelowRefpoint` → 
  `RefpointSpanToGap`）。コーパスの本が**すべて行を最後に置いていた**ので、
  下側の境界が一度も存在しなかっただけ。⇒ **§5.2.1⑥ とセットで使う**: 値が読まれる式まで
  追い、**その式が発火する形を自分で作ってから**「死んでいる」と言う。
  ⚠️ **不動を報告するときは「どの regime で振ったか」を必ず書く。**
- ★★ ⚠️ **「同じであってはならない数が同じ」は、残差より鋭い**（2026-07-28・第25セッション）。
  ossia 本と対照本は**別々の力を解かなければならない**（インクの量が違うのだから）のに、
  Lily# は **0.214493 / 0.214493 と九桁一致**していた。⇒ **小さいインクがページに 1 度も
  届いていない**という診断が、残差を 1 つも読まずに出る。
  ⚠️ **残差は他の誤りと打ち消し合えるが、恒等の破れは打ち消されない**——だから
  **移植の合否は残差でなくこちらに置く**（§5.0「点を開けるとき、何が揃えば終わりかを同時に書く」の
  具体形）。★ **「摂動して動かない」の兄弟**で、**動かないのが 2 冊の差のほう**。
- ★ ⚠️ **摂動幅は「効果が見える最小」ではなく「regime を出ない最大」で決める**
  （2026-07-28）。`ClefG.Top` を 4.8→6.0 に振ったら**両本とも圧縮 regime を出て**（force が
  符号反転）何も読めなかった（§5.0 罠 7）。+0.05 に落として regime を保ったら**係数 1 で
  追随することが一発で出た**。⇒ **大きく振るほどよく見える、ではない。**
- ★★ ⚠️ **鎖の中では、同じ 1 個の誤りが「床の点」では原寸・「ばねの点」では 1/N に薄まって出る。
  薄まったほうを「一致」と読まない**（2026-07-28・第25セッション）。clef の下端の 0.010000 は、
  **足の点では +0.010000 そのまま**・**book S の頭では −0.000083**（総強度 726 で割られ、
  top ばねの強度 6 を掛けた姿）。前セッションは後者を見て**「同じ字形の下向きは 8e-5 で一致」**と
  書き、**上下で別の話だと結論していた**——実際は**1 個の箱が上下ともずれている**。
  ⇒ ★ **残差を比べるときは「その点が床か、ばねか」を先に言う**（§5.3 の「残差÷強度」の逆方向：
  **床の点は割らずに読める＝一番強い証拠**）。
- ★★★ ⚠️ **総和が上がる移植がある。指標は「打ち消しが解けたか」で読む**
  （2026-07-28・第25セッション・`6c6be1af`）。glyph skyline 移植は **12 点を 1e-7 まで閉じ**
  （うち 3 点 exact）ながら **lyrics 3 点を +0.034000 ずつ押し上げ**、総和を
  **2.557670 → 2.606964** にした。⚠️ **その 3 点は符号の逆な 2 つの誤差の net** で、
  **負のほうが 0.034 縮んだ**だけ（`0.271310 − 0.105961 = 0.165349` が六桁で閉じる。
  旧は `0.271310 − 0.139961 = 0.131349`）。⇒ **clef の箱が別の欠陥を 1/4 隠していた**＝
  §5.2 が名指す「2 つの欠陥が打ち消し合う」形そのもの。
  ⇒ ★ **総和が上がったら、まず「どちらの項が動いたか」を分解する。**
  **分解できるなら上がってよい。分解できないなら止める。**
- ★★★ ⚠️ **「未特定の定数」を探し続けているとき、それが定数でない可能性を先に潰す**
  （2026-07-28・第25セッション）。clef の「実効 scale 0.004 対 0.003989＝0.27%」は
  **何セッションも「LP を instrument するまで動かせない」として §2C に居座っていた**が、
  実体は **`宣言 bbox の幅 ÷ アウトラインの幅`**（`stencil-integral.cc:535-563`）で
  **グリフごとに違う量**だった。⇒ **定数として探している限り、どのグリフで測っても合わない。**
  ★ **割ったのは実測ではなく LP のソース 1 関数**——**実測は「合わない」までしか言えず、
  「なぜ合わないか」は字面にしか無い**（§5.2 の裏返し: 移植の前に読むのは実測ではなくソース）。
- ★★★ ⚠️ **「LP を instrument するまで割れない」と引き継がれた項目は、その量が既に Scheme に
  生えていないかを先に見る**（2026-07-28・第26セッション。**何セッションも BLOCKED だった**）。
  `lyrics.*.staff-to-lyric` の当たり点は `lily/skyline-scheme.cc:26-216` の
  `ly:skyline-distance` / `ly:skyline-touching-point` / `ly:skyline->points` / `ly:skyline-merge`
  で**そのまま dump できた**。⇒ ★ **入口は `ls lily/*-scheme.cc` と `grep LY_DEFINE`**。
  LP は内部量の多くを Scheme へ出しており、**`\override X.after-line-breaking` か
  `page-post-process` から呼べば grob の実物に当たる**。⚠️ **「instrument が要る」は
  「C++ を書き換える必要がある」の意味で引き継がれがちだが、たいてい違う。**
- ★★★ ⚠️ **箱は `max_height` を再現するが、2 枚のプロファイルの pointwise 比較は再現できない。
  だから「1 枚に当たる読みが全部合っている」は箱で足りている証拠にならない**
  （2026-07-28・第26セッション）。`Skyline::distance` は
  `i->height(x) + j->height(x)` の**各 x での和の最大**（`lily/skyline.cc:618-645`）なので、
  極値の x が違う 2 枚は**最大どうしの和より低く**当たる。G clef 2 枚で **0.105961**。
  ⇒ ★ **判定法**: その量は**1 枚を読んでいるか、2 枚を突き合わせているか**。
  後者なら箱では足りない。⚠️ **そして箱→シルエットにすると、それまで無害だった
  「X アンカーの近似」が効き始める**——箱は平坦なので x がずれても答えが同じだった。
- ★ ⚠️ **摂動は「旧実装をグリフの一部だけ復元する」形でも打てる。当たり x が局在できる**
  （2026-07-28）。clef の箱を左から `split` 割だけ merge し直して snapshot を見ると、
  当たりが尾（x>2.06）にあることが 4 回の実行で出た。
  ⇒ ★ **応答が段差なら相手は平坦、連続なら相手は斜面**（ここでは horizon padding の 45°）。
  **どちらかを見るだけで消費者の種類が割れる。**
- ★ ⚠️ **「どのモデルがこの点を作っているか」は識別子でなく摂動で決める**（2026-07-27・§5.2.1⑥
  の同型）。同じ量のモデルが 3 つあると、コメントと台帳の `why` が**もっともらしく別のものを
  名指す**。実例: `lyrics.between-staves.two-verse.staff-staff-inside` は台帳もコードも
  「予約側（`AlignmentMinimumBand`）の読み」と書いていたが、0 にしても +10 にしても**不動**。
  実際の持ち主は `MultiStaffLayouter.NoteBoundLyricExtraGap`（1 節あたり平坦な 3.2）で、
  そちらを 0 にすると**その点だけ**が 11.200000 → 9.000000 に動いた。
  ⇒ **候補を 1 つずつ 0 に振り、落ちた id の集合で持ち主を決める。**
- ★★ ⚠️ **「間隔が exact」は「重なっていない」を意味しない**（2026-07-27）。間隔の点は**器の
  位置**を測るので、**器から中身がはみ出す**型の欠陥に**構造的に盲目**。実例: 2 番を持つ独立
  lyrics 行は次の system の五線に **−0.800000 めり込む**のに、`system-gap` は LP と同じ
  **12.000000 で exact** だった——行がどのスカイラインにも居ないので、はみ出しがばねの床に
  一切届かない。⇒ **はみ出しは、間隔ではなく「はみ出す側の量」を測る点で捕まえる。**
  それが無い regime では **`png --crop` の目視を 1 回入れる**（今回それが決め手だった）。
  ⚠️ そして**その exact な点は捨てずに載せる**——移植後も動かないことが要件になる（LP は
  loose line で system を広げない）ので、**盲目な量は「変わらないことの網」として価値がある**。
- ⚠️ **SVG から精密測定をしない。** 座標は `F2`（`SvgGenerator.cs:229`）で2桁に丸められる。
  6桁の LP 値と比べるなら `LpFidelity/RecordingDocumentContext` を使う
- ★★ ⚠️ **スケールグループの中で読んだ座標は、ページ上の座標ではない。変換を合成する前の数を
  「欠陥」と呼ばない**（2026-07-28・**1 セッション分の作業を丸ごと塞いでいた**）。
  ossia は `BeginGroup(translate, scale)` の中に描かれ、しかも**中では別のフレームを使う**
  （`SharedRenderer.cs:445` は ossia のとき `localStaffY = pageHeight` に置き、変換に運ばせる）。
  そこを合成せずに読むと「**譜線が system 原点に・間隔 1.000000・幅 135.55（紙幅 119.50）**」
  という**もっともらしい 3 点セット**が出る。合成すると **6.158232…8.986632・0.707100・
  95.844921（紙 119.501575）** で、**描画は最初から正しかった**。
  ⇒ ★ **見分け方は算術**: 疑わしい数を**そのグループの scale で割る／掛ける**と既知の量に
  なるか（`95.844921 ÷ 0.7071 = 135.546`＝報告された幅そのもの・六桁）。
  ⇒ ★ **道具の側で決める**: `RecordingDocumentContext` は `BeginGroup` で変換を合成するので、
  **測るならそこを通す**（SVG の `<line>` 属性や描画関数の引数を直に読まない）。
  ⚠️ **これは §5.2.1① の「REF の隣を疑う」の測定版**——前セッションの記述は**数値つきで
  具体的**だったので疑われずに引き継がれ、しかも**「どちらの作業をするか」の分岐**にまで
  育っていた。**引き継がれた数は、それが作業を止めているならまず測り直す。**
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
- ★★ ⚠️ **代理表現を潰す網は、コーパスが綴れない配置を model から作るしかない**
  （2026-07-28・第25セッション）。「型で書いた `is_spaceable`」は**言語で書けるどのスコアでも
  正しい答えを返す**ので、snapshot も台帳も距離も**定義により無力**。落とせるのは
  **規則の入力を振ったとき**だけ——`ALineIsNonSpaceableBecauseItDeclaresAnAffinity_NotBecauseOfItsKind`
  は**普通の音楽譜に `staff-affinity` を立てる**（`.lys` には綴れない）。
  ⇒ ★ **「コーパスが届かないから測れない」は「網が書けない」ではない。**
  model 直叩きのテストが届く。⚠️ そして**届かなかったこと自体が、その代理表現が
  何年も生き延びた理由**なので、閉じたら必ず網を残す（次の綴りが生えても落ちる）。
- ★ ⚠️ **不等号の向きが「そのテストが存在する理由」を要求しているか確かめる**（2026-07-27）。
  `SkylineSpacing_ExtremeLedgerLines_IncreasesGap` は名前どおり「広がる」ことを主張していたが
  `>=` しか要求しておらず、**兄弟テストが「中央の音符なら両者は等しい」ことを示している**
  ＝ **自分が区別したいはずのケースで既に満たされる**不等号だった。⇒ **`>` に締めた**
  （実測 15.090000 対 13.000000）。同族で `>= x − 0.01` は**逆向きにずれても通る**ので
  等号へ。⚠️ **判定法**: その主張は、**測っている効果が丸ごと消えても通るか**。
- ★ ⚠️ **frame を動かす前に「格納値を主張するテスト」を書くのは儀式ではない。無いと診断が
  出ない**（2026-07-27 に実演）。譜スカイラインの原点を上端線→refpoint へ移す 1 行を、
  テスト無しで先に試したら **spacing の測定が 6 つ同時に落ちて、どれも「どこかがズレた」と
  しか言わなかった**——原因（tuplet の seed が線と数字の 2 回で、線しか変換していない）に
  たどり着けず差し戻した。経路ごとに枠を主張するテストを 7 本入れてから同じ反転をしたら
  **一発で出力完全不変**。⇒ **N 個の測定が同時に落ちる形の網は、frame には効かない。**
  効くのは**入力を 1 つ与えて出力の座標を名指す**テスト（`StaffSkylineFrameTests`）。
  ⚠️ **副産物としてそのテストが「枠が 2 つある」ことを数えた**——移行の設計はそれが分かって
  から決まった。**枠の数を数えずに frame 移行を設計しない。**
- ★★ ⚠️ **ハーネスの不変条件（「gap は一様か」）が、もっともらしい数を出す配線バグを捕まえる**
  （2026-07-27・2 回）。⑴ 予約を walk に載せた最初の版は、**system ごとの切片**を
  **score 全体の配列を期待する API**（`CalculateSyllableLayout` は measure layout を**位置**で
  引く）へ渡していたので、**最初の system 以外は空のブロック**になった。症状は「台帳の点が 2 つ
  exact になった」——**良い知らせの顔**で出た。捕まえたのは `RenderedGeometry.StaffGap` の
  「一様でなければ例外」で、非一様 (12.207200, 12.000000, 12.000000) が出て初めて分かった。
  ⑵ 2 譜の本に `StaffGap()` を使ったら例外（gap が 9/12 と交互）＝**誤った accessor が
  黙って平均されずに済んだ**。⇒ **測定ヘルパには「その読みが意味を持つ前提」を assert させる。**
  緩めると、配線バグが改善に化ける。
- ★★ ⚠️ **台帳点はリテラルで通せる。恒等の対は通せない**（2026-07-28）。
  `lyrics.row.two-verse.verse-step` は「2.8 と書いて閉じるな」と `why` に書いてあったが、
  **その禁止を守らせる網は台帳の側に無い**——点は数を 1 つ留めるだけだから。
  ⇒ **恒等の対を「Lily# の 2 綴りが一致すること」としてテストに落とす**
  （`LyricRowIsSolvedLikeTheLyricsContextsItIs`: LYRRV ≡ LYRV を system ごとに）。
  ★ **利点が 2 つある**: ⑴ 定数を書いた実装は**片方だけ**当たるので落ちる、
  ⑵ **両側とも自分の書体**なのでフォント量が相殺し、**書体を変えても生き続ける**
  （台帳点のほうは +0.271310 を抱えたままなので、そちらは書体に依存する）。
  ⚠️ **regime を assert してから比べる**（この対は圧縮ページでしか何も言わない）。
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
- ★ **`Measure-LilyPondPageGeometry.ps1` は 20 分以上かかる**（2026-07-27 実測・**book は 62 冊**
  ——2026-07-28 に数え直した。40 と書いてあったのは stale——で 1 冊が 120 小節×480 音節）。
  ⚠️ **だから新しい点をこのファイルに足さない**（§5.0 の「対は専用プローブで固める」）。**しかも出力は最後にまとめて出る**ので、途中経過は 0 バイト＝
  ハングと見分けが付かない。⚠️ **MCP のツールタイムアウト（170 秒）では絶対に完走しない**——
  `Start-Process pwsh -RedirectStandardOutput <file>` で**切り離してから**ポーリングする
  （`$env:TEMP\lp-page-*` のファイル数が増えていれば生きている。完走すると消える）。
  ⚠️ **コンソールごと失うと道連れになる**ので、MCP セッションが落ちたら**もう一度最初から**
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
# ⚠️ これは台帳の記録値を印字するだけで Lily# を測っていない（§5.3）。
#    コードを変えた効果はこれでは出ない。全テストを走らせて落ちた id を見ること。
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
      ⚠️ **`LILYPOND-REF` を足したなら、行範囲だけでなく「そこに何があるか」の記号名も書く**
      （`LpReferenceCitationTests` のラチェットが落ちる・§5.2.1⑦）。**名前を書くために
      その行を読むことが検査そのもの**——2026-07-28 に、範囲だけ書いて別の分岐を指した。
7.5 [ ] ★ **移植した差分を §5.2 片手に読み直したか**。
      ★ **まず数える**（30 秒・機械的・2026-07-27 に追加）:
      `git -c color.ui=false diff <base> HEAD -- LilySharp.Core` の **`+` 行**に対して
      **`LILYPOND-REF` と `LILYSHARP-OWN` が何本あるか**。**0 本や 1 本なら、そこが監査対象。**
      ⚠️ `LpProvenanceTests` は数値定数しか見ないので**式だけ足すと緑のまま素通りする**
      （§5.2.1①）。実例: 348 行足して REF 1 本、それも移動してきた既存行だった。
      ⚠️ **出典を書こうとすると、書けない箇所が発明の在処を教える**——このとき 4 件に
      名前が付き、そのまま次の島の設計になった。**書けないものは `LILYSHARP-OWN:` に
      「どの LP 行から外れたか」と「いつ消えるか」を書く。**
      そのうえで（**REF が付いているかではなく、REF の隣の式が LP と同じ形か**）各項について:
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
