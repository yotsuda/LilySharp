# Lily# 開発ハンドオフ（常設・単一）

> **このファイルが唯一の引継ぎ先。新しい `handoff-*.md` を作らないこと。**
> 引継ぎは §1「現在地」を**書き換えて**行う（追記しない）。恒久的な知識は §4 の表に従って
> それぞれの置き場所へ出す。ここに溜め込むと、以前と同じように 16 個に分裂する。
>
> **置くのは「次に手を動かすために要るもの」だけ。** セッションの記録・閉じた欠陥の経緯は
> `HANDOFF-ARCHIVE.md`（逐語）へ出した。**§1 に残すのは直近 2 便の経緯だけ**で、
> それより古いものは §7 の終了時チェックリストで落とす。読むのは
> **同じ regime にもう一度触るとき**だけでよい。個別事例は原則に汎化して §5 に置く。
>
> **恒久ルール（§4〜§8）は `RULES.md` へ出した。** セッション開始時に**通しで読める大きさ**に
> するため——このファイルが 1.7 MB あったころ、§5 の 1470 行は「grep で当たったものしか
> 存在しない」状態だった。**見出し番号は動かしていない**ので `§5.2` はそのまま `§5.2`。
>
> ⚠️ **§4〜§8 の見出し番号はコード内コメント（`§5.2 違反`・`§5.2.1④` 等）から
> 60 箇所・35 ファイルで参照されている。ファイルが分かれても番号は振り直さないこと。**

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

⚠️ ★ **数を引き継ぐときは「数え方」も書く。** 2026-07-30 に「台帳 236 点」が
**開始時点で既に嘘**だった（実数 225）——`--filter LpGeometryLedger` の**テスト数**を
点数として書き写したのが出所で、同ファイルには点でないテストが 11 本ある。
**台帳の点数はこれで数える**:
```powershell
(Get-Content audit\lp-geometry\lp-geometry.json -Raw | ConvertFrom-Json).entries.PSObject.Properties.Name.Count
```
⚠️ ★★ **「非ゼロ」と「総和」も同じ罠を踏んだ**（2026-07-31・第52セッション）。引継ぎの
「非ゼロ 74・総和 4.108590402」は、**`unit: count` の 2 点（各 −2）を staff space の総和に
足していた**——台帳自身が「count を ss の総和に入れるのは*悪い数*ではなく*無意味な数*」と
書いてある（`LpGeometryLedgerTests` の `Unit` の doc）。**単位の違う点は別に数える**:
```powershell
$e = (Get-Content audit\lp-geometry\lp-geometry.json -Raw | ConvertFrom-Json).entries.PSObject.Properties
$nz = $e | Where-Object { $_.Value.residual -ne 0 -and $_.Value.unit -ne 'count' }
"ss 非ゼロ $(@($nz).Count) / 総和 $((($nz | ForEach-Object { [math]::Abs($_.Value.residual) }) | Measure-Object -Sum).Sum)"
```
⚠️ ★ **4 例目（2026-07-31・第53セッション）＝「count 点 2」**。これは **count 点の個数ではなく
その中の非ゼロの個数**だった（実際は **count 点 41・非ゼロ 2**）。**count 点は別に数え、
「全部」と「非ゼロ」を両方書く**:
```powershell
$c = $e | Where-Object { $_.Value.unit -eq 'count' }
"count 点 $(@($c).Count) / うち非ゼロ $(@($c | Where-Object { $_.Value.residual -ne 0 }).Count)"
```
⚠️ **`$e.Count` は使えない**（`$e` は PSPropertyInfo の配列なので各要素の `Count` が返る）。
**必ず `@($e).Count`。**

---

## 1. 現在地 ← **毎セッション書き換える**

最終更新 第168セッション＝**▶ ⒯⒟（`@name(引数)` の島）に着手した便。ユーザー決定は 2 つ
（⑴ 引数の文法＝**値の列**・⑵ `(` の曖昧性＝**閉じた語彙**）。第1便が「配置修飾子の点落ち」の
修復、第2便が「引数を取る名前を閉じた語彙に」、第3便が測ったことの台帳化**。
開始時の裏取りは**全項目を走らせた**——build 通過・suite **4621 passed / 0 failed / 4 skipped**・
**台帳 514 点・ss 非ゼロ 97・総和 3.609963181・count 点 106/うち非ゼロ 2**（**数は全部引継ぎ通り**）。
⚠️ **stale だったのは git だけ**——引継ぎの「HEAD `cb2bf398`・未 push 30」に対し、
**第167 の 5 便は 5 コミットに畳まれて push 済み**（開始時 HEAD **`5a2ae8c8`**・**未 push 0**）。
終了時 **HEAD ＝ この handoff commit**（その親は第2便の `b3e74a34`）・**未 push 3**
（⚠️ **handoff commit は自分自身を数えられない**ので、次の人が見るときは **3** のまま合う）・
suite **4643 passed / 0 failed / 4 skipped**（+22＝新しい網ちょうど。第3便は文書のみ）・
**台帳 514 点で不動**（ss 非ゼロ 97・総和 3.609963181）・
**snapshot は 5 枚 70 行動いた——全部 `data-pos`**（ユーザー承認済み・下 ⑵）。
- **⑴ ★★★ 第1便＝`@staccato.up` の点が木に残るようになった**（`9f9a7e14`）。
  **本体は round-trip ではなく*位置*だった**——ノードの位置は**手前の green 幅の累計**なので、
  **食われた点 1 個ぶん後続が全部 1 字ずつ手前を指していた**（実測: `root.FullWidth 35` 対
  `src.Length 36`・`d4` の位置が空白を指す。**対照＝点なしの同じ小節はぴったり**）。
  着地先は **SVG の `data-pos`・LSP のジャンプ先・`PartSectionLayoutConverter`**（.lys を
  木から書き戻す編集器＝**利用者のファイルから点が消える**）。
  ⚠️ ★★ **第167 起票 ⑻ の記述は 2 か所とも外れていた**（監査 §3.3 に訂正を書いた）:
  例に挙がっていた `@feather.up` は**未知名なので警告が出る**——**診断ゼロなのは
  `@staccato.up`＝正しい綴りのほう**（コーパス＋フィクスチャに **34 site**）。
  そして **`@bend.half` は `@bendhalf` にならない**（`.half` は配置語ではないので
  `Undefined variable or phrase: 'half'` ではっきり落ちる）＝**点を食うのは
  `.up`/`.down` の枝だけ**。⇒ ★ **起票の「例」は、直す便が必ず測り直すこと。**
- **⑵ ★★ 穴そのものを測った＝299 冊の round-trip**（「穴も測る」の型）。
  **壊れていたのは 15 冊で、3 島に割れた**: ⑴ この点落ち **10 冊**（閉じた）
  ⑵ **注釈とスラー/タイの順序が入れ替わる 4 冊**（`empty-chord`／`script-stack-order1`／
  `slur-vertical-skylines`／`feature-tour`＝マークが書かれた位置の `(` や `)~` より**前**に
  出し直される・**未修正**） ⑶ **`volta-labels` が `[1. …]` の手前の `|` を落とす**（**未修正**）。
  ⇒ 網 `EveryBook_SpellsItselfBackOutOfItsTree` が**残り 5 冊を名指しで持つラチェット**。
  ⚠️ **snapshot 5 枚の再ベースはユーザー承認済み**——**140 行の差は数字を伏せると新旧が
  完全一致＝幾何ゼロ**で、**新しい `data-pos` は音符に着地し、古いのは空白・小節線・
  トークンの途中を指していた**（増分は先行する点の個数ちょうど＝+1〜+8）。
- **⑶ ★★★ 第2便＝`(` は「引数を取る名前」の後だけ引数になった**（`b3e74a34`）。
  `IsArticulationName()` が**任意の識別子を通す**ので `(` はどんな名前の後でも引数で、
  **`c4@staccato (d4 e4 f4)` がスラー群を丸ごと飲んでいた**（音符 3 つが小節から消え、
  診断は `Unknown annotation '@staccato(d 4 e 4 f 4)'` だけ）。
  ⚠️ **コーパス実害 0 冊＝live な欠陥ではなく罠**（ごく普通の音楽で落ちる）。
  ★ **クラスは 2 つではなく 3 つにした**——**未知の名前は引数を読み続ける**。
  `@notehed(x)` の `(` をスラーに渡すと **誤字が 2 つの誤りに増え**、しかも
  **点つき名前から計算される "did you mean '@notehead(x)'?" が消える**。
  ⚠️ `ottava`/`quindicesima`/`ds`/`dc` が語彙に居るのは**裸マークでもある**から。
  ★ **陽性対照**: 語彙なしだと**新網 13 本のうち 2 本だけ**落ちる（飲み込みの 2 件ちょうど）
  ＝**振る舞いが変わったのはそこだけ**、という言明。
- **⑷ ★★★ 第3便＝設計の制約を 1 つ測って台帳にした（監査 §9）。**
  **値に落とすと壊れる引数がある**——**`@frame(032010)` は `Int(32010)` になって
  先頭の 0 が消える**（フレット図は**位置文字列**なので 6 弦が 5 弦に化ける）。
  `@chord(c:m7)` の `c` は **`PitchC` トークン**＝引数は音楽トークンを借りた部分言語。
  ⇒ ★★ **引数ノードは「値」と「書かれた字」の両方を持つこと。**
  ⚠️ **`@frame(` を書く本は 0 冊なので、壊しても網は鳴らない**——
  **「測ってから設計する」が効いた実例。**
- **⑸ ★ 実在する引数は 5 種だけ**（全数・監査 §9.1）＝文字列 61／整数と整数列 52／
  識別子 15／和音の部分言語 16。**算術・変数参照を要求している消費者は 1 つも無い。**
  ⇒ **「値の列」で足りる**（本物の式は入れない＝ユーザー決定 ⑴ の根拠）。
- **⑹ 値が文字列で 3 往復する現物**（監査 §9.3）: `@chord(c:m7)` は
  トークン列→`"chord.c.:.m7"`→`Split('.')+Join("")`→`"c:m7"`→もう一度パース。
  **`MarkName` の読み手は 9 ファイル・30+ site**、うち **`AnnotationNameValidator` は
  他の 9 つの再実装**（§8「同じ run を N 回歩く」の 2 例目）。**次便はここから。**
- **未追跡 1 件**: `audit/lp-regression/lp-vs-lilysharp.html`（第156 開始時から。触っていない）。

---

**次の一手**（⒟ ⒩ は取り下げ・⒟′ は第135第2便・**⒟⁵ と ⒟‴ は第136 で閉じた・
⒟⁶ の warm 側は第138 で・⒫ は第139 で閉じた**）。
★★ **打鍵の順位は第159 の Release 実測（ユーザー許可の窓・§1 ⑷・A/B同一窓・edit@0.50・
生データ `scratch\stagebench-159.txt`）**——**床 plain 114.2 / fingbeam 227.4 / v2bow 166.2。
⒟⁶⑵⑴ の返済で全冊共通の首位は消えた——本ごとに違う**:
**v2bow: break+pages（36+34＝最大塊）＞ prelim.slurs 26-27 ＞（annpass は 7.3 に陥落）**；
**fingbeam: ~~ann 族 ~71 が首位~~ —— stackabove は第161 の per-system memo で ~1 ms/打鍵へ、
~~fingscripts ~33~~ も第163 で 50.4→6.8 ms へ陥落（§1。⚠️ 「~33」は 1 つの数で照準も外れていた）
＝ann 族は帯へ落ちた。次に打鍵を詰める人は▶の順位から取り直すこと**＞
~~⒭ overlay render 36-37~~（**第160 で断片化＋capture O(n) 化＝overlay 項の床は打鍵あたり
~6 ms 帯へ陥落**）＞ collect 29-36**；
**plain: 支配項なし（break 20-23 / pages 18 / annpass 14-17 / keys 13-18 / collect 10-19 の帯）**。
**cold の順位は第158 の第1切片を正とする**（第159 の cold は 1標本＝参考。総 2531/1624/786・
persys 首位・段の向きは第158 と整合・annpass cold は plain 104.7→43.5 に落ちた）:
**cold（第158・総 1812/1842/994）: persys（770/616/231）＞ ⒟⁶⑴ 量子器 prelim.beams（311/200/—）＞
collect（143/403/60）＞ render（286/190/86）＞ ⒪′ augment（v2bow 217）＞
springs＋keys（113/117/102＝cold は memo 群が効かない）**。ms は明記なきものは Release。
~~次の一手は打鍵の首位＝collect の残りの分割から~~ — **第151第2便で割った**（第151経緯の表・
静かな窓の再測値）。~~順に ⑴ plan（checkpoint 二分探索）…⑸ parseagree~~ — **⑴ と ⑸ は
第152 で閉じた**（§1。⚠️ 名指しされていた「checkpoint 二分探索」は**支配項の取り違え**で、
実体は parse-agreement の red 木歩き＝green 直歩き化で ⑴⑸ が同時に落ちた。走査は 5000
checkpoint 全数でも 0.2 ms＝線形のまま）。~~⑵ defs~~ — **前半（finder 化）は第152第3便で
閉じた**（defs 行 59→3.5/4.0 ms・red 生成 234,016→5・第152経緯 第3便）。
~~⑵′ gather の遅延 red 化~~ — **前半（打鍵経路の全木 red walker の全滅＝green finder 化）は
第153 で閉じた**（red 生成/打鍵 plain 43k→9.1k・fingbeam 234k→57k・v2bow 37k→22.6k/4.1k・
打鍵床 −21.6/−45.7/−15.2 ms・ARCHIVE 第153経緯。⚠️ gather 単独では合計 red は 1 個も動かず、
**意味層の全木走査が次々に first-touch を相続した**——PartTranspose/ChordName/Lyrics/RowGrid/
PartHasStructure を同じ finder に載せて落ちた）。**後半（flat list の lazy 化）も第155 で
閉じた**（`9bd5002f`・red/打鍵 plain 9,075→83・fingbeam 57,207→263・ARCHIVE 第155経緯。⚠️ ここでも
**WalkBars（canonical bars）が相続した**——相続 3 例目・2 つで 1 commit）。**残る red は
v2bow 編集面の 18.5k＝第2声部再構築**——`BuildExtraVoiceTracks` が `<< \\ >>` の追加声部を
毎打鍵 live で歩き直す。lazy の器はできたので、閉じるなら**打鍵またぎの別鍵 memo**
（第154 の content-key の型）＝別切片。⚠️ ★ **ただし動機を red 数に置かないこと**——
第155第2便の A/B で **red −99% は Release 壁時計を動かさなかった**（ARCHIVE 第155経緯。red 生成は
この床では帯以下の費用）。**この項の値段は「省ける第2声部の意味処理 ms」を割ってから。**
~~⑷ splice 適用相の残余（fingbeam ~23 ms）~~ — **第155第3便で割ったら消えていた（stale・
ARCHIVE 第155経緯 第3便）**。ok.total 6.9 ms で内訳が閉じ、疑いの「側表 per-entry append」は 12,000 entry で
0.22 ms＝無罪。**⇒ 第151 の collect 分割リスト ⑴〜⑸ は完走**。
⚠️ **第155 の Release A/B は取得済み＝不動（帯内 ±5 ms・ARCHIVE 第155経緯 第2便）**。
~~★★ 次に打鍵を詰める人は、段の内訳の取り直しから~~ — **第158 で取り直した**（↑の順位が
その結果・第158経緯 ⑶。第149 の名指し候補はこの取り直しで裁定済み: **annpass は首位で確定**・
collect の残りは陥落・v2bow は break+pages が最大塊に浮上・fingbeam の render は 149.3→37.7
（断片 cache 後の残り＝overlay 側）**）。
~~⑶ beamdirs~~ — **第154 で閉じた**（検出の per-measure content-key memo・`322543ef`＋
鍵の値段の修理 `1133d07d`・第154経緯。第153 の再値付けどおり detect が支配項（plain 6.2 /
fingbeam 18.2 ms）で、そこが memo の対象。**静かな窓の A/B で床 −7.2/−17.2/−5.0 ms**
（fingbeam は detect 床 18.2 と整合・第154経緯の表）。bake 3 本＝合計 3〜4 ms の batch 化は
値付けどおり**やらないまま**。⚠️ **reflection の content key は 1000 小節で walk と同額**
（10.5〜37.9 ms）——**memo の鍵は「省く walk より安いこと」を測ってから**（第154経緯 第2便）。
層側の staff 量検出（`StaffBeamGroupsOf` 族）は**別鍵の別切片**として残る＝⒫ の項）。
⚠️ plan/parseagree は splice の soundness 層そのもの——**安くする改造は
保証を落とさない形でだけ**（第152 の型: 同じ述語の別綴り＋カウンタ一致＋毒の陽性対照）。
⚠️ **以下が名指す「第135第N便」〜「第155」の経緯と第143 の旧表は `HANDOFF-ARCHIVE.md` へ
落ちた**（終了時チェックリスト 3.5。**第151 の collect 分割表も ARCHIVE**——defs 行と
col.walk 行は第152/153 で stale。**▶ の値段の根拠は第158 の表（↑と stagebench-158.txt）**——
第149 の表は「取り直した便が落とす」の但し書きどおり第158 が落とした（ARCHIVE 第149経緯に逐語）。

⚠️⚠️ **着手前に読むこと: 「打鍵」には 3 つの regime があり、混ぜると測れない**（第135第2便・第136第1便）。
**⑴ 内容不変の木**（`PreviewUpdateBench` が測るもの）は**全体再利用が発火して layout が丸ごと消える**ので、
費用は collect＋key＋springs＋render。**⑵ 本物の編集で gate が動かない**（幅の変わらない打ち直し）は
**再利用が降りて layout が走るが改行 DP は `precomputedLineSizes` で飛ぶ**。
**⑶ 本物の編集で gate が動く**（臨時記号を足す等）は**改行 DP も走る**。
**⑴ と ⑵⑶ は桁が違う**（`plain1k` で 0.7 秒 対 2.1 秒＝第136第2便の後）。**どれを直しているのか毎回書くこと。**
★ ⚠️ **`c8` を打ち直す形で ⑵ を作ろうとすると ⑶ が混ざる**——Lily# の絶対 `c` は中央ハ＝**加線を持つ**ので、
`d8` にすると列の幅が変わって gate が動く。~~**⑵ を狙うなら五線内の音**（`g8`→`d8`/`e8`/`f8`/`a8`）~~
⚠️ ★★ **↑第138 の実測でこの処方は plain1k では偽と出た**——**五線内の音高交換はどれも
（`e8 f8`→`f8 e8` の順序入れ替えすら）編集小節の spring `IdealWidth` を動かす**（g8→a8 は
+0.25）。**⑵ を八分音符の本で作る綴りはまだ見つかっていない**（四分音符の fixture への
articulation 追加は ⑵ に落ちる＝`WidthPreservingContentEdit_SkipsLineBreak_…` が現物）。
**どちらに落ちたかは `LastEditSkippedLineBreak` が言う——ラベルに書く前に必ず読むこと**
（実編集の測定は `EditKeystrokeBench` が gateMoved / reusedLayout を行に印字する）。

▶ ★★★ **⒯ 文法に「値と式」を入れる**（第165 起票・**site の台帳は `docs/VALUE_SITE_AUDIT.md`**）。
  **スクリプトの有無と独立に黒字**——いま `override` の値も `time` も `tempo` もリテラル
  トークンの直読みなので、式が入る日に**消費者を全部直すことになる**（**枠を揃える**）。
  着手順は監査 §5 のとおり:
  ~~⑵ 軸A を数え切る~~ — **第166 で完了**（**19 → 68**・4 クラスに分割・監査 §1.1。
     ⚠️ 同時に**除外の線が 4 件を隠していた**ことが判明＝**文法内は 39 → 43**・監査 §0.1）。
  ~~⑴ `override` / part property の文字列パイプラインを型付きの値にする~~ —
     **第166第2便で返済**（`0b846ea6`・§1 ⑽）。`LysValue`（判別共用体 record）が入り、
     **読み手 5 つが全部「読む」側**になった（`StaffSpacingParameters` の迂回路も含む）。
     **出力は 1 バイトも動いていない。**
     ⚠️⚠️ ★★★ **残る A3 7 件は「同じ形の続き」ではない**（第166第2便の終わりに現物で確認し、
     **だからこの便では着手しなかった**）。**5 つに割れる。まとめて 1 便に入れないこと**:
     ~~⒜ **同形＝決定不要（1 件）**: MIDI part render の `instrument:N`/`octave:N`~~ —
        ⚠️⚠️ ★★ **第167 実測で分類が変わった＝これは ⒜ ではなく ⒠**（**やらない**）。
        `MidiPartRenderSyntax` は **`PartName` しか公開しておらず、option の値を読む者が
        リポジトリに 1 人もいない**（`MidiExporter` は score/render 側を見ず `part` 宣言から
        取る）。**しかも `MidiFile` は ProgramChange を 1 つも書かない**（書くのは
        NoteOn/NoteOff/PitchBend/Tempo/TimeSignature/TrackName/Lyric だけ＝実測）＝
        **GM 番号の着地先そのものが無い**。⇒ **やるなら MIDI 側の機能が先。**
     ~~⒝ **構造化した値の設計が要る（1 件）**: `tempo` の値 run~~ — **第167第2便で完了**
        （`e6e2bfb0`・§1 ⑷⑸）。`TempoValue`（`LysValue` の隣の record）が**1 パスで 5 つ**を
        答える。⚠️ **引継ぎの「3 つの状態機械」は 6 つだった**（`BeatUnit`・`BeatDots`＋
        `MeasureCollector.CollectTempo` の正規表現の歩き）。**しかも 2 つの読みは
        食い違っていた**＝振る舞いが 1 つ変わっている（§1 ⑸）。
     ⒞ **文法を育てる話（1 件）**: **`drummap` 本体**（`Directives:112`）は
        **部分言語がまるごと生トークン**で、**値の位置ですらない**。型を付けるには
        **小さなパーサを書く**＝受理される綴りが変わりうる。⚠️ **無断で言語を育てない**
        （§5 の家訓）。**ユーザー決定が要る。**
     ⒟ **式の文法そのもの（1 件）**: `@name(引数)` run（`Music:875`）は token を無差別に
        集めている＝**式が着地するならここ**。**⇒ 第168 で着手・設計はユーザーが決めた**:
        **⑴ 引数は「値の列」**（`LysValue` をそのまま・**本物の式は入れない**——
        **算術も変数参照も、今日の消費者が 1 つも要求していない**＝監査 §9.1）
        **⑵ `(` は閉じた語彙の後だけ引数**（第168第2便で実装済み・§1 ⑶）。
        ⚠️ ★★★ **残る作業は引数ノードそのもの＝監査 §9.5 が入口**（§9 全体が実測台帳）。
        ⚠️ ★★ **設計の制約が 1 つ測れている**: **値に落とすと壊れる引数がある**
        （`@frame(032010)`→`Int(32010)` で先頭の 0 が消える・`@chord(c:m7)` は部分言語）
        ⇒ **引数ノードは「値」と「書かれた字」の両方を持つこと**（§1 ⑷）。
        ⚠️ **`MarkName` の読み手 30+ site は一便では終わらない**——**消す順は
        「値として読める家族」から**（text/fig/finger/bend/notehead/pluck）、
        **字が意味の frame と部分言語の chord は最後**（監査 §9.5）。
        ⚠️ ★★ **この島は ⒯⑷ の 3 系統もまとめて抱えている**——
        `@finger.3` / `@bend.5` / `fig.3.5` は**内部の mark 名**で、綴りは全部この
        `@name(引数)`（第167 §1 ⑺）。**ここを設計すると ⒯⑷ が 3 系統ぶん軽くなる。**
        ~~⚠️ ★★★ **同じ島に未修正の欠陥が 1 つ載っている**（`@name.suffix` の点落ち）~~
        — **第168第1便で閉じた**（`9f9a7e14`・§1 ⑴。⚠️ **起票の例も症状も外れていた**——
        本体は round-trip ではなく**位置ずれ**で、診断ゼロなのは `@staccato.up` のほう）。
        ~~`(` の曖昧性~~ — **第168第2便で閉じた**（`b3e74a34`・§1 ⑶）。
     ⒠ **型付けが何も買わない（3 件・対象から外す）**: metadata / font の**回復** run
        （`Declarations:227,255`）＋**上の MIDI part render**。誤り形を**丸ごと保持している**
        だけで診断は既に出ており、型を付けても読む者が居ない。
        ⚠️ **触ると診断が動く危険のほうが大きい。**
     ⇒ ★ **残るユーザー決定は ⒞（drummap）と ⒟（式の文法）の 2 つだけ。⒜⒠ は「やらない」。**
  ~~⑶ 値の型集合を決める~~ — **第166 で決定（ユーザー判断）＝判別共用体 record**。
     `LysValue.Int/Real/Str/Bool/Symbol`。**将来 C# 埋め込みへそのまま公開型にできる形**を
     条件に選んだ。⚠️ **`Pitch`/`Dur` はまだ作っていない**——今回の範囲（override と
     part property）に要らなかったので**要る便が足す**（`PartTranspose` が自前で音高を
     読んでいる site が既にある）。
  ⑷ **名前に数が埋まった系統は ~~3 ではなく 6~~ → 実は 3**（第167 実測・監査 §3.3）:
     **トレモロ `:8`・弦番号 `\4`・和音品質 `maj7`** だけ。⚠️ **`@finger.N`・`@bend.N`・
     `@fig.N` を別系統に数えていたのは誤り**——それは**内部の mark 名**で、綴りは 3 つとも
     `@name(引数)`（⒟ の run そのもの）。⇒ **⒟ を設計するとこの 3 つは一緒に閉じる。**
     ＋**反復で数を表す 8 site**（オクターブ記号 `'` `,`＝`c''` の「2」は token の個数）。
     **式の層では閉じない別項**（監査 §1.1 A4・§3.3）。
  ⚠️ **`DurationSyntax.Value`（`SyntaxNodes.cs:210`）は据え置き**——全音符が通る＝式にすると
  打鍵ごとに全音符分の評価。**式の層で唯一、性能が設計を決める site**（監査 §4）。
  ⚠️ **C# 埋め込み自体は v1 に載せない**（第165 の決定・§1 ⑴）。この項はその可否と独立。
  ~~▶▶ ⒯⑸ 小数リテラルを足す~~ — **第167第1便で閉じた**（`52ec1f0f`・§1 ⑴⑵⑶・
  ユーザー決定は ⒜ lexer）。`DecimalLiteral` が入り、**`LysValue.Real` は .lys で書ける値**。
  番人 4 つは全部網になっている。**`c4.5` は LYS0021・`tempo 4.5` は LYS0022**。
  ⚠️ ★ **残った 1 つだけ憶えておくこと: `IncrementalLexer.Guard = 2` は
  `ScanNumber` の先読み（`.` ＋数字）でちょうど埋まっている。**縮める人は
  `IncrementalParseTests` の `e1.5` 行で落ちる。
  ⚠️ ★ **第166 の教訓＝「文法の外」は置き場所で決めない**。除外を「どのファイルにあるか」で
  引いたせいで、**`@bend.N` が int→文字列→int と 3 ホップしている**のを 1 便見落とした
  （監査 §7 ③）。**その数はどこで書かれたかで決めること。**
  ⚠️ ★★ **第167 の教訓はその続き＝「綴り」はコメントで決めない**。`@fig.6` は
  **remark が「Syntax:」と書いていただけ**で 1 度もパースできなかった綴りで、
  それが**監査の表と、1 便ぶんの推論**に乗った。**綴りを書く前に 1 回パースさせる**
  （§1 ⑺・remark は第167 で訂正済み）。
▶ ★★ **⒮ 第156 の残件**（フィデリティ・perf 順位とは独立。**⑵ volta ending と ⑶ 小節途中
  repeat は第157 で閉じた**＝§1）:
  ⑴ **行頭 prefix 調号＋中間 clef/time 変更グリフの skyline seed**（§2A 同族の続き。
  中間 clef/time は第156⑷ の `KeyChangeSeedX` と同じ再アンカーで載る形。**prefix の x は
  SeedClef の remark が名指す「builder に届かない break-align X」＝配管設計から**。全書の
  行頭に効くので snapshot/コーパスの動く範囲が広い＝**承認ゲート付き単独セッション**）
▶ ★★★ **⒭ collect（と render）の増分化＝打鍵の首位**（第143 起票・**第144 が第1切片、
  第145 が測定第2弾＋走査 3 site、第146 が checkpoint/resume 装置（Δ=0）＋完全性網を返済**）。
  ~~⑴ spec 探索の全木走査~~ — **第144 で閉じた**（`14605044`・§1。第143 の collect 行は
  **FindFirst＋CollectScore の 2 文の合算**で、FindFirst が約半分＝fingbeam −208 ms 級。
  `DescendantNodes` の反復化も同便・出力同一の証明 3 点つき）。
  ~~⑴′ ProcessSection 族の冗長走査（secover / part 探索 / canonical bars）~~ — **第145 で
  閉じた**（`5b9abea2`・~10ms 級・fingbeam の走査 234,009→5 ノード/打鍵。**これが最後の
  冗長走査**＝gather の DescendantNodes は flat list 構築の本務）。
  ~~⑵ collector 本体の per-item 意味処理が打鍵ごとに O(全書)~~ — **第148 の splice で
  per-item 意味処理は編集窓の内側しか live で走らなくなった**（第145 の ncoll/ncreate の
  内訳は splice 前の数＝ARCHIVE）。~~第149 実測の collect 残り＝50/158/27~~ — **第158 で
  9.2/21.3/8.1 ms（床・安い側）まで陥落**（第151〜155 の分割・green 化・memo 群の合算が
  効いた＝**collect はもう打鍵の名指し候補ではない**。割るなら第145 の CollectClock の形で）。
  **checkpoint/resume 装置は第146（`350d0824`・Δ=0）、cross-edit の prefix 側と
  `IncrementalCompiler` 配線は第147（`952baffe`）、suffix 継ぎ（位置シフタ＋状態等値
  splice＋旧 tree 参照の再解決＋canonical bars ガード）は第148 で立った**——
  `CollectWalkProbe`＋`CollectResumePlanner`＋`CollectTailShifter`＋網 3 枚
  （`CollectResumeTests`／`CollectEditResumeTests`／`CollectTailShifterTests`）。
  **打鍵は今、編集窓の外を両側とも collect しない**（編集側 median plain −105 /
  fingbeam −93 ms・第148）。
  ★★ **第149 の再値付け（§1 発見1/2）**: ~~残件「v2bow 型＝splice も prefix も届かない」~~ は
  **半分 stale だった**——**v2bow は毎打鍵 splice している**（編集 token が第2声部＝primary walk の
  採用物の外。バイト一致検証済み）。**splice が届かないのは「全身 1 span の本の voice-0 編集」
  だけ**で、そこに span 内部 checkpoint を立てても買えるのは ~25〜50 ms（decline→live full walk
  の差）——**▶ の下位に降格**。~~⒭ の生きている首位は render の増分化~~ — **DrawSystem 側は
  第151 の断片 cache で閉じた**（§1・`a3aa3416`）。~~⑴ collect 残りの分割~~ — **第158 で
  陥落を確認（上の ⑵）**。~~⒭ に残るのは overlay の断片化だけ~~ — **第160 で閉じた・測定済み**（`9cf3560b`＋
  `c468b019`・§1。per-(drawer, 頁) の断片＝DrawFingerings のみ・鍵は**生入力の値 fold**
  （⚠️ 第1版の「解決済み x/y を fold」は鍵が walk と同額で効果ゼロだった＝§1 ⑸①）・網 3 枚。
  **同窓 A/B/A2 で機構単独 −26〜51 ms・overlay 項の床 72.7→6.4 ms/打鍵**。おまけの本丸＝
  **capture の TrySplit 二次走査（第151 から居た）を O(n) 化**——fingbeam −80〜107・
  plain/v2bow も −10〜29（§1 ⑶⑸②）。
  ⚠️ 「同じ島の finalann 31.7」は annotation *pass* 側＝⒟⁶⑵⑶ の土地であってこの項ではない。
  **⇒ ⒭ 完了**。他 drawer を載せる日の型は §1 ⑷——ただし①の家訓つきで）。
  ⚠️ ~~観測者の返済が先~~ — **第149第2便で返済済み**（`V2bowWholeBookSpan` 網 2 枚＋
  perf 網の splice-only 形・§1 の第2便 bullet）。
  ⚠️ **第147 の家訓: 「バイト同一の prefix」は「パース同一の prefix」ではない**（先読みが
  窓を跨ぐ）——suffix 側も同じ罠を踏む（第148 で `ParseSuffixAgrees` として実装・
  **plan 時でなく初回 splice 試行時に遅延**——splice できない本への毎打鍵課税を実測して退けた）。
  ⚠️ **collect は score 全体の側表（tuplets/dynamics/lyrics…）と臨時記号状態を持つ**——
  §2C ⑴（多声 walk の moment 順再設計）と同じ土地に立つので、**部分修理で 3 つ目の walk を
  生やさないこと**（§2C ⑴ の警告そのまま。checkpoint/resume は同じ walk への再入として設計）。
  ~~render の増分化は別の島~~ — **第151 で立った**（system 単位の SVG 断片 cache＝
  `SvgSystemFragmentCache`・data-pos は slot 化＋窓写像＋live 位置指紋。§1）。
▶ ★★ **⒟⁶ 梁のレイアウトの memo 化**（**第136第3便が 2 回→1 回・第138 が warm のその 1 回を
  per-system memo に入れ・第139 が検出を 1 memo に畳んだ**＝warm と検出は閉じた。
  plain1k 実編集の床 1937.7→1435.4 ms（第138）・cold 床 15386.5→3773.9 ms（第139）。**残りは 2 つ**:
  ⑴ **cold の量子器は不変**——検出の 404 回は第139 で消えたが**彫版は残る**（cold の
  prelim.beams＝**Release 実測 plain1k 311.0 / fingbeam1k 200.0 ms（第158・cold 総の 17%/11%）**・
  量子器 2,030 本×0.16 ms＝**本物の彫版**・第136第3便の表は ARCHIVE へ）。
  ⑵ **`prelim.annotationpass`** は memo 外のまま（第136第1便の線形項。**Release 打鍵床
  21.9 / 58.0 / 17.3 ms＝plain/fingbeam/v2bow・第158＝いまの打鍵首位**）。
  ★★ **第158第2切片が内訳を割った（第158経緯 ⑶）**: 支配項は **StackAboveStaff（prelim+final で
  plain 18.3 / fingbeam 49.5 / v2bow 21.9）**・第2項は fingbeam の **fingscripts 30.6**・
  **他の副段は全部 ~1 ms 未満**。⇒ 着手の照準は ~~⑴ **staffProfile を 2 パス（prelim/final）で
  共有する hoist**~~ — **第159 で閉じた**（`e20e3376`・§1。prelim pass が
  `placed.StaffSpanners`/`placed.StaffInside` を運ぶ形＝remark の予告した検査は
  「参照同一性」で成立していた。prelim の fallback 全システム walk が render あたり
  33/6/9/2/3→0（call 数）・**同時に prelim/final の profile 分岐＝ページの mover
  取りこぼしも閉じた**——網 `PreliminaryPassSeedTests`。**ms は同便第2便で A/B 実測**:
  pann.stackabove 床 14.7→5.5／36.0→14.9／17.9→6.1・§1 ⑷）
  ~~⑵ stackabove 自体の per-system 増分化~~ — **第161 で閉じた**（`a3905c96`・§1。
  `AboveStackMemo`＝per-system 生入力 fold・prelim/final 別 store・profile は StaffInside の
  格納 instance を参照比較（`ctx.InsideIdentityOf`）。**同一窓・同一 binary の A/B で
  stackabove 項床 OFF 16.5/50.6/19.2 → ON ~1 ms/打鍵（plain/fingbeam/v2bow）**・網 4 枚。
  他の pass を載せる日は同じ型で——ただし stackBelow は実測 ≈0＝張らない）。
  ~~残るは ⑶ fingbeam の fingscripts（script column walk）の memo~~ — **第163 で閉じた**
  （`4c2f0f42`・§1）。⚠️ **引継ぎの「~33 ms」は 1 つの数で、しかも照準が島側に見えていた**
  ——割ると **islands 28.1 ＋ walk 39.1** で walk のほうが大きかった（§1 ⑴）。
  単位は (staff, system)・**鍵は `MeasureLayout` の instance**（モデルは打鍵ごとに作り直される
  ＝▶ ⒫ と同じ壁・§1 ⑶）。**同一窓 A/B で 50.42 → 6.80 ms/打鍵**。
  ⚠️ **script を持つ単位は memo しない**（宣言したゲート・§1 ⑷）＝**digit と script が同じ
  system に同居する本は旧価格のまま**。届く本が測れたら、そこは articulation 出力の
  再組み立てと `SourceIndex` の付け替えから＝別切片。
  ⚠️ **残る 6.8 ms** は pass ごとの全書 fold（tip 表・beams-by-measure）＋編集単位の live 分。
  ⇒ **⒟⁶ は ⑴（cold の量子器）だけが残る。**
  ⚠️ **system をまたぐ梁を持つ譜は丸ごと旧経路にフォールバック**（出力は同一）——
  その本では memo が効かない。**効かせたいなら鍵に隣 system の被覆を足す設計から。**
▶ ~~**⒫ `DetectBeamGroups` が 1 打鍵に 6 回走る**~~ — **第139 で閉じた**（§1。検出は
  ⑴ collect の焼き込みプローブ ⑵ 注釈の量（第138 carry）⑶ 譜の量（第139 の
  `StaffBeamGroupsOf` memo）の**別の量 3 つ**に畳まれ、**打鍵 4→3・cold 404→3**。
  **残る 3 回は畳めない——量が違う**）。~~残る改善は検出そのものの範囲化~~ — **⑴ collect の
  焼き込みプローブ側は第154 で閉じた**（per-measure content-key memo・§1）。**残るは
  ⑵⑶＝層側の検出**: **Staff instance の打鍵またぎ再利用**（memo は Staff キーなので、モデルが
  Staff を打鍵ごとに作り直す限り打鍵内共有どまり＝実測で cross-keystroke hit 0。第154 の
  content-key memo をこちらへ延ばすなら bake 後 items＋voice fan 入力込みの**別鍵**が要る）。
  **着手するなら ▶ の順位を測り直してから。**
▶ ~~**⒪ prelim の spacing は第2声部のタイ・スラーを見ていない**~~ — **第140 で閉じた**（§1。
  第1便が起票した 4 点の上で、第2便が final の `staffSpannerScore` 綴りを prelim に写した。
  slur −1.122500648 → **+0.000534361**・tie −0.917560328 → **+0.000442473**＝**単声対の残差
  そのものに収束**・対照 exact・既存 507 点不動・snapshot 0 動・コーパス 0/82）。**残り 2 つ**:
  ⑴ **+0.0005/+0.0004 の sliver は単声対と共通の OPEN**（X 軸系・弓形状の族——単声
  `system.slur-under-notes` / `system.tie-under-notes` の why が台帳。voice-2 対はそれに
  相乗りしているだけなので、単声側が閉じればこちらも動くはず＝動かなければ別の欠陥）。
  ⑵ → **▶ ⒪′ に昇格（値段が付いた）**。
▶ ★★ **⒪′ タイ・スラーの残債＝carry（第141）＋augment memo＋Distance memo（第142）で返済済み**。
  第141 の carry が final の重複弓 layout（~152 ms・cold Debug）を消し、第142 の augment memo が
  `AugmentSkylinesForPaging` を編集 system だけに（209.5 → 3.7 ms/打鍵・Release）、
  同第4便の per-pair Distance memo が positioning を対の再測だけに（85.4 → 0.9 ms）。
  **v2bow1k 打鍵床は第141 開始時の Debug 2428 ms から Release 実測 ~270〜380 ms 帯へ**（§1）。
  ⚠️ ★★ **batch 化は第141第2便が A/B 網で棄却済み——蒸し返さないこと**:
  `VerticalSkyline.Merge` の remark の「batch は byte-identical」は **flat 箱専用の真実**で、
  slope は分割順で交点の丸めが変わり **4,878 building が ULP 不一致・コーパス本にも及ぶ**。
  ⚠️ **skyline の segment 数を減らす向きは出力が動くので不可**（第141 の注意そのまま）。
  **残りは cold（初回 layout）だけ**——memo 2 つはどちらも warm の装置。cold を詰めるなら
  ⑵ **MergeInternal の同一算術高速化**（全 sort+rebuild を局所挿入にする——**同じ operand 対に
  同じ演算**を証明できる形でだけ。できなければ触らない）が唯一の残存候補。
  **cold の段内訳は第158 が Release で取り直した**（▶の★★）: **v2bow1k cold の prelim.augment
  217.3 ms＝cold 総 994.0 の 22%**（plain 0.6 / fingbeam 0.9＝弓の無い本は払っていない）。
  `MergeInternal` 内部の内訳までは割っていない——**着手する便がそこから**。
▶ ~~**⒟″ の残り: 改行 DP の line-count ループ**~~・~~**⒟‴′ 改頁 DP の p ループの空振り**~~ —
  **両方とも第141第3便で閉じた**（§1。1 つの手口で 2 か所＝到達可能バンド。kLoop
  6,979,000 → 2,983,035・pLoop 652,800 → 295,280・出力同一は構成＋3 点証明）。
  ⚠️ **「唯一の二乗」だった 2 項が消えた**——第136 の断面（250/500/1000 で ×3.79/×3.95）は
  もう成立しない。**次に打鍵の段の内訳を取り直す人は、この 2 段の次数から測り直すこと。**
▶ ~~**⒟⁗ 打鍵ごとに全曲のばねベクタを作り直している**~~ — **第138第2便（内容不変＝ベクタ丸ごと
  参照再利用）＋第150（regime ⑵⑶＝per-measure memo・`049baab5`）で閉じた**。内容が動く編集も
  **content key の隣接窓（i−1..i+1）が不変の小節はばねを再利用**し、動いた近傍だけ同じループ
  本体で再計算（§1。同日 A/B で床 −151/−204/−72 ms＝plain/fingbeam/v2bow）。**安全網の除去は
  両便とも remarks に明記**・番人は incremental==full 網＋memo==from-scratch 深比較網 4 枚。
  ⚠️ **残るのは cold（初回 compile）だけ**——前打鍵のベクタが無いので memo は効かない
  （cold springs 56/101/80 ＋ keys 57/16/22 ms・第158）。cold を詰めるなら per-measure の値 cache ではなく
  ループ内部（CreateTimingSprings の skyline rod 側）の測り直しから。
▶ ★ **⒟″ スラーの 64 サンプル平坦化は「精度」の課題として残る**（⒟ の perf 側が消えても
  **これは消えない**）。`fingering.slur.bound-note` の残差 −0.011491008 のうち
  **−0.011291475 が 64 サンプルの平坦化**で、4096 にすると −0.000199533 まで落ちる。
  **値段が付いていなかったのが perf のほうだった**ので、**サンプル数の費用は測り直しから**。
▶ **⒠ 行末 courtesy 群を `BreakAlignSpacing` に通す**（§2H ⑷・**第131 から据え置きの最古参**。
  **定数で埋めない**・台帳に「courtesy 拍子の*右側*」の点が無いので**点が先**）。
▶ ~~⒡ **fetaText の光学サイズ選択**~~ — **閉じた**（第164。指番号と図形バスは第134、
  残りは第164）。⚠️ ★★ **残り 2 件は光学サイズではなかった**——`DynamicText` も
  `TimeSignature` も LP は `font-size` を宣言しないので **20 デザインが正解**で、
  「20 を読んでいる」は欠陥の記述として誤りだった（§1 ⑴）。**⒡ の名前で残っていた点は
  全部 Pango hinting＝下の ⒧**（`staff.staff.dynamic-under-whole-note` の −0.000076 も、
  ⒡ 自身の「観測者を先に確かめること」どおり ⒧ 側だった）。
  ★★★ **同じ島に実在したのは*活字*のほう**＝拍子桁が平文カットでなく `fattened.*` を
  読んでいた（＋kern 無し＋多桁を足していない）。**第164 で点 3 つを起こしてから閉じた**
  （§1 ⑵〜⑹・`e8b57afa`＋`0e03a3e3`）。**`FetaTextRun` の抽出も同便で返済済み**
  （§5.1 の「3 つ目のコピーを書く便に括り付ける」どおり）。
  ⚠️ **残るのは `DynamicText` の側の 1 つだけ**で、それは ⒧ なので**手を付けない**。
  ⚠️ **拍子の島に残した 2 つ**（どちらも観測者ゼロ・**点が先**）: ⑴ 複合拍子の `+` は
  serif fallback のまま（LP は自分の markup で組む） ⑵ **拍子桁のアウトラインは未抽出**
  （advance のみ）なので `MeterGlyphRun` は ink を出さない——**読む消費者が出てから**。
▶ ⒨ **棚卸しの読み方**——~~OWN 116 を疑うこと~~ **第158 で完走**（`debce47f`・§1。OWN の
  札違い 16 件を張り替え・歴史的言及 ~15 件を数えから除外・APPROX/UNWATCHED は同型で通読して
  **両バケット白**＝現在 **APPROX 48 ／ UNWATCHED 45 ／ OWN 83 ＝計 176**、全て「現役の宣言」の
  行数——⚠️ site 数ではない・§1 第158 ⑵）。**残るのは 2 つ**:
  ⑴ **境界線 5 件の再訪**（§1 第158 に列挙・全て理由はコメント内）
  ⑵ **UNWATCHED の観測者主張の実測突合**（「no observer」と書かれた site に台帳の点が後から
  生えて stale 化していないか——「穴も測る」の型・claim→台帳キーの対応付けは 1 件ずつ判断）。
  ⚠️ **載っていること自体は欠陥ではない**——ほとんどは意図的で論証付き。
▶ ⒧ **Pango hinting 残差の族**（**まだ手を付けるな**）。`figbass.*` 7 点の **−0.0000883**、
  `fingering.chord.*` 3 点の **−0.000065859**、`fingering.chord.dynamic-cleared` の **+0.000090187**、
  `staff.staff.dynamic-under-whole-note` の **−0.000076**——**すべて「Pango が ink rect を
  自前の単位で量子化する」1 つの原因**で、**Lily# に Pango は無い**。**当てはめるな**（§5.0）。
▶ **⒢ 指番号の「下向き」がスラーを避ける**（第133 起票＝経緯は `HANDOFF-ARCHIVE.md` の
  第133セッション・**点が先**）。移植は**上向きだけ**（下向き digit は script column に入らない）。
  **コーパスの目視はこちら**だが、**LP はその本で 1 mm も動かさない**ので、**動く texture を探すところから**。
▶ **⒣ 指番号の予約に slur/beam の答を乗せる**（第133 起票＝同上・**点が先**）。
  `MultiStaffLayouter.StaffFingeringLayouts` は島を直接呼ぶので、**持ち上がった digit は
  持ち上がる前の帯しか予約しない**。届く本が出たら点から。
▶ ⒤ **`'inside` の印がフィンガリングの上に積まれる本**——`InsideSlurScriptLayouts` は
  **fingering 抜きの overload** を呼んでいる。**その組み合わせに届く本は今の所ゼロ**。
▶ ⒥ **指番号の flag support**——LP は flag も support に足す（`new-fingering-engraver.cc:188-190`）。
  移植していない理由は `FingeringEngraver.BuildLayouts` の remark にあり、**CFF がその根拠を測ってある**。
  **覆すなら別の texture の点が先。**
▶ ⒦ **cv47 系の grob の `.alt`**（**点が先**）。⚠️⚠️ **この項の前半は誤りだったので第164 で
  消した**——「`TimeSignature` は features 無しなので `fattened.<n>` の**基底**でよい
  （現状どおり・確認済）」と書いてあったが、**features が無ければ fattened ではない**
  （`ss01` が fattened を選ぶ）。**拍子は平文カット**で、幅もグリフも第164 で移した（§1）。
  ⇒ ★★ **「確認済」と書いてあっても、その確認が何を見たかまで書いていなければ再確認する。**
  **残っているのは `.alt` だけ**: **`StringNumber` / `MeasureCounter` / MMR 番号 /
  `PercentRepeatCounter` は `("cv47")`** ＝**4 と 7 は `.alt`**（ただし**基底は平文**であって
  fattened ではない——第164 で MMR 番号ごと平文へ移っている）。**寸法は基底と全 8 デザインで
  完全一致**（実測）なので**ペンだけの差**——**絵は変わるが観測者がゼロ**なので、
  **点を作るまで触らない**。効く本は 4 か 7 を含む MMR 番号・弦番号・小節番号。
▶ ★ **⒬ system-start ブラケットの X 原点が LP と 0.225 ss ずれる**（**点が先**。
  先端グリフを実グリフへ直した便で**名指しただけ・直していない**）。
  **LP は縦線をグリフ原点から右へ伸ばす**——`system-start-delimiter.cc:47` の
  `Box (Interval (0, thickness), …)`。**Lily# は `BraceX` に中心を置く**。
  ⇒ **ブラケット全体が thickness/2 ＝ 0.225 ss 左に立つ**（thickness は同じ便で LP の宣言値
  `(thickness . 0.45)` に合わせたので、この 0.225 は現在値）。
  ★ **「原点＝縦線の左端」は推測ではなく LP 自身の SVG で実測した**——2.26.0 に同じ ChoirStaff を
  描かせると、縦線 `translate(7.2258, 6.1874) rect x="0" width="0.45"` と**上下の先端グリフの
  `translate` が同じ 7.2258 を共有する**（先端は縦線の左端から生える）。
  ⚠️ **`SystemStartDelimiterInkLeft` は Lily# の描き方に合わせて `BraceX − thickness/2` を返しており、
  内部では整合している。**⇒ **片方だけ動かすと楽器名とインデントがずれる。2 つ同時にやること。**
  ⚠️ ★ **0.8 が 2 か所に居るので、足す前に「`BraceX` はどちらを既に吸っているか」を確かめること**——
  `staff_bracket` は最後に `bracket.translate_axis (-0.8, X_AXIS)`（`:63`）を掛け、
  **それとは別に** SystemStartBracket が `(padding . 0.8)` を宣言している。
  **確かめずに 0.225 だけ足すと 0.8 の二重計上になりうる。**
  ★ **台帳に system-start ブラケットの X の点は 1 つも無い**（`bracket` で当たる 18 点は
  **全部 tuplet bracket と chord bracket**・実測）。**観測者ゼロなので点が先。**
  **効く本はインデントと楽器名を持つ多譜**（`staffGroup` / `choirStaff`）。
  ⚠️ ★ **同じ島の第2項＝先端の高さを誰も予約していない**（**当たってはいない・実測済み**）。
  **LP はブラケットの Y extent に先端を*入れている***——`:57-59` の LP 自身のコメントが
  「In Y-direction we have to take the tips into account」と書いている。**Lily# は入れていない**:
  先端グリフを実グリフにした便で**五線の外へ 1.593 ss 出るようになった**のに
  （旧 serif は**内向き**で外に 0）、**snapshot は 2 冊 3 行しか動かなかった**
  ＝**縦の消費者が 1 つも反応していない**。★ **実害は今のところゼロ**——ブラケットを描く本を
  全部測って**重なり 0**、2 system の既定間隔で**譜間 4.32 ss のうち先端が 3.19 ss を食い、
  余白 1.134 ss**。⇒ **詰まった本が来たら当たる**。**⒬ の X と同じ便で見ること。**


---

## 以下は第167セッションの経緯

最終更新 第167セッション＝**▶ ⒯ を 4 便で進めた便。第1便が「小数リテラルを足す」（⒯⑸・
ユーザー決定＝⒜ lexer）、第2便が「tempo の値を構造化」（⒯⑴ ⒝・ユーザー決定）、
第3便が「`@fig.6` の stale remark」の返済と、その測り直しが出した訂正 2 件、
**第4・5便が「自分が falsify した文書の返済」**（`GRAMMAR.md`／`GRAMMAR_FOR_LLM.md`＝第4便、
`VALUE_SITE_AUDIT.md`＝第5便）**。
開始時の裏取りは**全項目を走らせた**——build 通過・suite **4575 passed / 0 failed / 4 skipped**・
**台帳 514 点・ss 非ゼロ 97・総和 3.609963181・count 点 106/うち非ゼロ 2**（**数は全部引継ぎ通り**）。
⚠️ **stale だったのは git の 2 つだけ**——HEAD は `0b846ea6` ではなく **`eff6104f`**、
未 push は 18 ではなく **23**（第166 の handoff commit とその後の 2 便が数えられていない＝
⑼ の罠の 6 例目。**散文のほうは正しかった**のも前回と同じ）。
終了時 **HEAD `cb2bf398`**・**未 push 30**（5 便＋handoff 2 回ぶんのコミット後に計測。
⚠️ **handoff commit は自分自身を数えられない**ので、この数は次の人が見るときには **31**）・
suite **4621 passed / 0 failed / 4 skipped**（+46＝新しい網ちょうど。**第5便は文書のみ**）・
**snapshot 0 動・台帳 514 点で不動**（ss 非ゼロ 97・総和 3.609963181）。
- **⑴ ★★★ 第1便＝lexer に `DecimalLiteral` が入った**（`52ec1f0f`。**⒯⑸ 完了**）。
  `ScanNumber` が「数字 `.` 数字」を 1 トークンにし、**`LysValue.Real` が .lys で書ける値になった**
  （書けないのは `Bool` だけになった）。**点の後に数字を要求する**のが安全弁で、
  文法が既に持つ点——付点（`c4.` `R2.*3` `partial 2.` `tempo 4. = 116`）と grob プロパティの
  区切り——は**どれも次が数字でない**。
  ★ **衝突 0 は自分で測り直した**（引継ぎの数を信じない・§0）: コーパス 80 冊＋フィクスチャ
  209 枚＝**289 ファイルの `数.数` は全部 `//` コメントの中**で、唯一の本文が
  `chordnames.lys:21` の `g2:m7.5-`。**これも届かない**（`m7` が識別子として一括で食われる）
  ＝網 `AnIdentifierEndingInADigitStillSwallowsTheDigit` で釘付け。
  ⚠️ **番人 4 つは全部網になっている**——round-trip・付点 4 綴りの**陽性対照**（トークン種別
  だけでなく**付点として読めているか**まで見る）・`3es`/`8va`/`:8` の既存分岐・
  **増分 lexer**（`IncrementalParseTests` に 3 行足した）。
  ★ **`Guard = 2` はちょうど足りていた**——`ScanNumber` の先読みは 2 文字で、
  それは trivia scanner と同じ幅。**両方の remark にそう書いた**（次に縮める人が落ちる）。
- **⑵ ★★ 落ちた網は宣言どおり 1 本だけ**——`LysValueTests.ARealIsNotWritableInTheGrammar`
  （4579 中 1 本）。第166 が「落ちたら報せ」と書いて残した網が、そのとおりに報せた。
  **`ARealIsWritableInTheGrammar` に書き換えた**（消さずに、何が変わったかを本文に残す形で）。
- **⑶ `c4.5` は LYS0021 になった**（黙って「付点四分＋迷子の 5」だったのが、はっきり落ちる）。
  ⚠️ ★ **ただし round-trip はしない**——**列の skip recovery がトークンを木から落とす**ので。
  **これは新しい振る舞いではない**（detached duration LYS0016 が昔からそう）ので、
  **2 つを対で釘付けにした**＝どちらかだけ直すと網が落ちる。
- **⑷ ★★★ 第2便＝`TempoValue` が入った**（`e6e2bfb0`。**⒯⑴ ⒝ 完了**）。
  ⚠️ ★★ **引継ぎの「3 つの状態機械」は 6 つだった**——`Marking`/`SwingSubdivision`/`Bpm` に
  **`BeatUnit` と `BeatDots`** が加わり（同じ node の別プロパティ）、さらに
  **`MeasureCollector.CollectTempo` が 6 つ目**を持っていた（`=` から点を遡って
  **正規表現**で単位を取る独自の歩き）。**同じ run の 6 綴り。**
  ⇒ `TempoValue`（`LysValue` の隣の record）が**1 パスで 5 つ全部**を答え、
  プロパティは 1 行ずつになった。**規則は「整理」せず転記した**——どれも綴りの誰かが
  依存している（bpm は最後の整数・marking は第1位置の裸語だけ・`swing 0` は 8 ではなく 0・
  `tempo 4 =` の 4 は単位でも bpm でもある）ので、**全部が網の 1 行**。
- **⑸ ★★★ そして 2 つの読みは実際に食い違っていた**＝**純粋なリファクタではない**。
  `tempo 8 = 120` のあと `tempo "x" = 90` を書くと、**collector 側は単位 8 を残し**
  （`=` の直前に数トークンが要る歩きだったので何も一致しなかった）、
  **node 側の規則は「単位のない `=` は四分」**。⇒ **node の規則に揃えた**（振る舞い変更 1 件・
  網つき）。**`=` の無い `tempo 100` は据え置き**——という対も同時に釘付け。
- **⑹ tempo の小数は LYS0022**。⚠️ **これは第1便が作った借金の返済**——`tempo 4.5 = 116` は
  第1便のあと**run がそこで終わって `= 116` ごと落ちる**形になっていた。
  **トークンを run の中へ取り込んでから拒否する**ので、宣言は丸ごと残り round-trip もする。
- **⑺ ★★★ 第3便＝点つきの mark 名は「内部表現」だった**（`16f7253b`）。
  引継ぎが名指した stale remark（`CollectFiguredBass` の「Syntax: @fig.6」）を直しに行って、
  **測ったら表のほうが間違っていた**。**実測**: `@fig(6)`→`fig.6` ／ `@fig(3 5)`→`fig.3.5` ／
  `@fig(6 s)`→`fig.6.s` ／ `@finger(3)`→`finger.3` ／ `@bend(5)`→`bend.5`（全部 診断ゼロ）。
  **`@fig.6` は LYS0016 で落ちる。** コーパス実測: **`@finger(` 28 件・`@fig(` 5 件・`@bend` 0 件**。
  ⇒ ★★ **綴りは `@name(引数)` の 1 つだけ**で、監査 §3.3 が「6 系統」と数えたうちの
  **3 系統は同じ 1 つを内側から見たもの**。**⇒ ⒯⑷ は 6 ではなく 3。**
  ⇒ ★ **`ExpectMarkName` の「別途要確認」も答が出た＝用途なし**（呼び手は form 直下の
  `@ds.al.fine` 1 つだけ・図付き低音は音符付きで別の島）。**3 腕は消さず「unobserved」と注記**
  （「到達不能」は主張・測る機械が無い）。**島違いの `LILYPOND-REF` は消した。**
- **⑻ ★★★ その測り直しが未修正の欠陥を 1 つ出した（起票のみ・監査 §3.3 末尾）**:
  **音符付きの `@name.suffix` は点を黙って落とす**。`c4@feather.up` → **`@featherup`**、
  `c4@bend.half` → **`@bendhalf`**。**診断ゼロ・round-trip も壊れる**（`.` が消える）。
  ★ **通っている網の理由まで変わる**——`AnnotationNameValidatorTests:49` は
  「feather の方向ではないから warn」とコメントしているが、**実際は点が食われて
  `featherup` という未知名になっているから**。**綴り誤りが「知らない名前」に化ける**ので
  書き手には直しようがない。⇒ **直すのは ⒯⑴ ⒟（`@name` の文法）の島。**
- **⑼ ⒯⑴ ⒜（MIDI part render）は ⒜ ではなく ⒠ だった**（実測・**着手しないこと**）。
  `instrument:N`/`octave:N` は **red node に accessor すら無く、リポジトリに読み手が 1 人もいない**
  （`MidiPartRenderSyntax` は `PartName` だけ・`MidiExporter` は score/render 側を見ず
  `part` 宣言から取る）。しかも **`MidiFile` は ProgramChange を 1 つも書かない**
  （書くのは NoteOn/NoteOff/PitchBend/Tempo/TimeSignature/TrackName/Lyric だけ＝実測）
  ＝**GM 番号の着地先が無い**。⇒ **「型を付けても読む者が居ない」＝⒠ と同じ分類**で、
  やるなら**先に MIDI 側の機能**（program change）から。
- **⑽ ★★ 第4便＝文法ドキュメントを実物に合わせた**（`f919af62`）。**新しい項目ではなく
  第1・2便の後始末**——`GRAMMAR.md:643` の `OverrideValue = Integer | '-' Integer | …` は
  **第1便が falsify した仕様**で、そのままだと「もう存在しないコンパイラ」を説明していた。
  `Decimal` の生成規則（**点の後に数字を要求する理由も同じ場所に**）・`OverrideValue = Number`・
  **値は型で運ばれる**（`= "10"` は数ではない）を入れ、**`TempoDecl` を書き切った**
  ——裸語の marking・bpm 省略・付点単位に加えて、**run の*読み方***（最後の整数・feel 語で
  打ち切り・`=` 無しなら単位なし）を仕様側にも 1 部だけ書いた（**`TempoValueTests` と同じ文**）。
  `GRAMMAR_FOR_LLM.md` も同じ 2 点＋「小数は値であって音価ではない」1 行。
  ★ **書いた綴りは全部先にパースさせた**（⑺ の教訓を自分に適用）——唯一無網だった
  **`override NoteColumn.force-hshift = 1.5`**（ハイフン再結合と小数走査が 1 宣言で出会う・
  **⒯⑸ がそもそも論拠にした綴り**）は網にした。
  ⚠️ **`GRAMMAR_STATUS.md` の「Multi-file projects 未実装」は据え置き**（第165 ⑺①）——
  **stale ではあるが本便が壊したものではなく**、`using` の受理する綴りを書くには
  **`Parser\UsingExpander.cs` の意味論を先に測る**必要がある。**文法ドキュメントを
  整理する便が落とすこと。**
- **⑾ ★★ 第5便＝`VALUE_SITE_AUDIT.md` も同じ理由で返済した**（`cb2bf398`）。
  **この監査は次便が最初に開く台帳**なのに、**本便のコミットがその主張を 4 つ偽にしていた**
  （§5 ⑴ の「A3 7 件は手つかず」・「`Real` は書けない」・§5 ⑶ の「6 系統」・§7 ⑤ 丸ごと）。
  ★ **冒頭に「もう*数えただけ*の文書ではない」と書いた**——打ち消し線の行が歴史・
  それ以外が現状、という読み方と、閉じたものの索引を置いた。
  ⚠️ ★★ **`§1.2 の B=17 / C=20` は数え直していない**（`CollectTempo` と第166 の
  `GrobProperty` 2 で少なくとも 3 減っているはず）。**数えていない数を書かないため**に
  **「第165 の基準値・返済ぶんは未減算」と明記しただけ**にした＝§0 の家訓そのまま。
  **要る人が数え直し、そのとき「数え方」も書くこと。**
  ★ **新 §8＝「同じ run を N 回歩く」の台帳**（§7 が「値を文字列で運ぶ」の台帳なのに対して）。
  tempo の 6 つの読みを住所つきで残し、**食い違った 2 つ**（§5.2.1② の再演）を書いた。
  ⚠️ おまけの教訓 1 つ: **消した正規表現の半分は最初から死んでいた**
  （`^([0-9]+)(\.*)$` の第2群＝トークン内部の点を拾う気だったが、**点は必ず別トークン**）。
  **コードが「何をするつもりか」と「実際に何を見ているか」は別に確かめること。**
- **未追跡 1 件**: `audit/lp-regression/lp-vs-lilysharp.html`（第156 開始時から。触っていない）。

---
---

**これより古いセッションの経緯は `docs/HANDOFF-ARCHIVE.md`**（新しい順・逐語）。
同じ regime にもう一度触るときだけ読めばよい（§8）。
**§1 に残すのは直近 2 便まで**——落とすのは §7 の終了時チェックリスト 3.5。

**恒久ルール・コマンド集・終了時チェックリスト（§4〜§8）は `docs/RULES.md`。**
番号はそのまま——コード内の `HANDOFF §5.2` はその番号で引ける。

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

- ★★★ **この族の親玉: skyline の参加者列挙が手動**（2026-08-07・第107セッション・
  ユーザー指示で起票・**未着手＝workstream**）。
  - **現状**: `SkylineBuilder` は参加者を家族別に手列挙する（`Add*ToSkyline` 約 10 本＋
    `SeedClef`/`SeedStaffSymbol`）。**「seed に居ない参加者」欠陥が測定されるたびに 1 本
    生えた系譜**: accidental・rest（第93頃）・tie・slur・beam・script・**dots（第107・
    `910300ee`）**。LP は grob が一様に `vertical-skylines` プロパティを持ち、
    `skyline_spacing` はそれを列挙して merge するだけ（axis-group-interface.cc:914-935）
    ——**汎用性は skyline 機構でなくプロパティシステムの副産物**。Lily# に一様な grob 層は
    無いので、同じ汎用性は**録画層**からしか生えない。
  - **終点の形**: レイアウトと renderer の間に**インクイベントの録画層（display list）**を
    置き、renderer と skyline が**同じ一次資料**を消費する（`MergeScriptProfile` の注記
    「LP は grob ごとに 1 つの vertical-skylines を全消費者に配る」の一般化）。
    **プロファイル選択規則は残す**——LP が箱と宣言するもの（符頭・Dots）は箱・
    stencil 宣言（Clef/Accidental/Script）は輪郭。全輪郭化は忠実度でも perf でも損。
  - **壁は perf でなく相（phase）**: skyline はインク確定**前**に要る（staff 間距離・
    mover 配置・改頁）。LP は遅延プロパティ＋pure/unpure 二重高さで解いている
    （LP 本体でも有数のバグ源）。Lily# でやるなら **inside-staff インクを先に録画→収穫→
    mover を置く→merge** の相分割を録画層の上で守る（既存 `PlaceDynamicsOn` の 75→250 順は
    そのまま相の骨になる）。
  - **perf の条件（実測済みの根拠）**: seed はレイアウトごとに建て直される——
    multi-page 本で **66 回**（第41セッション実測・回数で測る島）。素朴な
    「フルレンダ×建て直し回数」は負ける。**per-item プロファイルのキャッシュ＋placement は
    shift** の形なら払える（前例 3 つ: `GlyphOutlineCache`・script の padded profile cache＝
    箱比 1% 以内・resolved copy 0.29%）。
  - **束ねる相手**: F3/増分アーキテクチャ（録画層は増分再描画の前提でもある）と、
    下の第92項の残り近似「部屋は mover を engraver 位置で予約する＝消すなら部屋が pass を
    走らせるしかない」——録画層＋相分割はその解でもある。
  - **着手前にこの棚で決めること**: ⑴ 録画層の API 案（engraver が emit する型付き
    インク primitive の粒度＝grob 相当か描画 primitive か）⑵ 消費者の移行順
    （page stacking→staff 間→部屋→pass の順に「後で読む」消費者から）⑶ 建て直し回数の
    再実測（キャッシュ キーの設計が回数で決まる）。**単独の修理として着手しないこと。**
- ✅✅ ★★★ **閉じた（2026-08-05・第97セッション）。臨時記号の列は譜のモーメントに 1 本になった。**
  **LP の `AccidentalPlacement` は譜のモーメントに 1 個**で、**声部をまたいで詰め**、
  **note-collision のシフトに乗らない**（`accidental-placement.cc:479-518`）。Lily# は
  `position_apes` を**item ごと**に解き、その答えを衝突シフトごと運んでいた＝
  **シフトされた声部の臨時記号が隣の声部の符頭の上**。実測・分解は §1（第97セッション）。
  ★★ **この項の教訓は「2 つ目の綴りは*声部の向こう側*に居ることがある」**——§5.2.1② は
  「場所が 2 つ」を探し、第95セッションは「**軸**が 2 つ」を足したが、**同じ量を持つ
  もう 1 つの*文脈***でも同じ欠陥になる。
  ⚠️ **残りは cue が混ざる列だけ**（§1 ⑵）＝`AccidentalPlacement` が font を 1 つしか読まないので
  **cue と原寸が同じ列に立つと item ごとの経路に落ちる**。**コーパスに 1 本も無い**。
  ★ **予約側は元から列の枠で測っていた**ので、**シフトを足していた描画だけが直った**＝
  **予約と描画は今度こそ 1 つ**（列は 1.04 ss 広くなり、描く分を取っている）。
- ✅✅ ★★★ **閉じた（2026-08-05・第97セッション）。`check_meshing_chords` は字面順になった。**
  **LP は `touch` を `close_half`/`full_collide` より先に消費する**ので**2 度も同度も
  下向き符尾の声部が右へ 1 符頭**動く。Lily# は touch 分岐を「full/close/distant が無いとき」に
  限っていて、**そこに来るのはその 2 形だけ**だったので**分岐ごと到達不能**だった。
  ★ **advance/ink の 3 例目**も同じ関数から出た（`HeadWidth`・半音符の腕欠落つき）。
  ⇒ ★★★ **教訓は「到達不能になった分岐は、消えるのではなく*別の分岐に化ける*」**——
  0.52 は正しい定数で、正しくない場所に効いていた。**定数表の照合では絶対に出ない。**
  ⚠️ **advance/ink の 4 例目 `GetColumnNoteheadWidth` は残っている**（§1 ⑶・今は読者ゼロ）。
- ✅✅ ★★★ **閉じた（2026-08-05・第95セッション）。符頭の X 枠は `ink` 1 本になった。**
  **LP の符頭 grob extent は ink**（1.9620 / 1.3774 / 1.3042）**で advance ではない**
  （1.960 / 1.376 / 1.304）——`dynamic-support.ly` の本に `NoteHead` を足して dump した。
  **7 site が advance で枠取りしていて、うち 2 site は Y を ink・X を advance で
  *同じ 1 式の中に*持っていた。** 詳細・予測・分解は §1（第95セッション）。
  ★★ **この項の教訓は「2 つ目の綴りは*同じ式の中*に居ることがある」**——
  §5.2.1② は「場所が 2 つ」を探すが、**軸が 2 つ**でも同じ欠陥になる。
  ⚠️ **残り 1 件は `ElementCoordinator:1578`（タブタイ）**で、**LP に対応物が無い**ので
  この規則の対象外（注記が自分でそう書いている）。
  ⚠️ **レッジャ線 X・付点 X・運指 X には台帳点が無い**まま直した＝**観測者ゼロ**。
  点を起こすなら §1 ⑶。
- ✅✅ ★★★ **閉じた（2026-08-05・第92セッション）。`inside_staff_skylines` は 1 本になった。**
  **`SkylineBuilder.BuildInsideStaffSkylines`（priority を持たない ink だけ）を部屋が 1 回作り、
  4 消費者（chain closing / figured bass / stacker seed / chord row）が
  `AnnotationLayoutContext.InsideOf` で読む。mover は `PlaceDynamicsOn` が 75 → 250 の順に置く。**
  経緯・実測・残った snapshot 2 枚は §1 ⑨ に。
  ⚠️ **perf の借りは返っていない**（+11%・理由は「太った profile × 消費者数」＝▶ ⑴'）。
  ⚠️ **部屋の profile と inside は*別物*であり続ける**——**部屋は mover を engraver 位置で
  予約する**（Lily# の outside-staff pass は部屋より後に走る）。**LP は 1 パスでそれを解く**ので、
  **ここは今も近似**。**次にこの近似を消すなら「部屋が pass を走らせる」しかない。**
- ★ **多声の譜が `VoiceCollector.Collect` と `NoteCollision` を 2 周する**（2026-08-05・
  第97セッション。**測って名指しただけ・未修正**・**着手はコスト対効果の判断が要る**）。
  `StaffAccidentalColumns`（collect 時）と `ElementCoordinator.CalculateVoiceOffsets`
  （layout 時）が**同じ 2 つを同じ入力に対して別々に回す**。⇒ §2 A の主題（同じ量を計算する
  場所が 2 つ）の**perf 版**。
  ★ **実測**（`MeasureCollector.Collect` n=2000・min×3・§1 ⑨）: **grammar-tour の collect が
  861us → 950us＝+10%**。⚠️ **collect は全描画の約 3%** なので**端から端では +0.3%**、
  **単声はゼロ**（`voices.Length <= 1` で即 return）。
  ⚠️⚠️ **効きどころはここだが、素直には畳めない**——**ステージが違う**（collect は Voice の
  モデル、Coordinator は `MultiStaffScore` の staff.Voices から組んだ `Score`）。畳むなら
  ⑴ collect が出した offset をモデルに載せて Coordinator が読む（＝**幾何をモデルに載せる**
  ことになるので §1 の `AccidentalX` と同じ議論が要る）か、⑵ 両者が読む
  **staff 単位の解決済みキャッシュ**を 1 つ作る、のどちらか。
  ⚠️ **+0.3% に対して払う額として妥当かは、着手前に決めること。**
- ✅✅ ★★★ **閉じた（2026-08-07・第104セッション）。付点の向きと side support は LP の 3 層になった。**
  dot-column-note-collision.ly（fixed 第23号）が名指し済みの両欠落に踏む対を出した。移植は
  ⑴ `:352-372` side support（`DotAdjustment.ColumnMinX`＝縦重なりの support 頭 ink右+dot幅へ
  dot 列を押す）⑵ `:374-397` 正シフト→down 声部の dots direction=UP ⑶ ★★★ **voice-props 層**
  ——`make-voice-props-set`（music-functions.scm:616-631）は **Dots/DotColumn にも direction を
  配る**。\voiceTwo の付点は**衝突と無関係に既定 DOWN**・:374 は正シフト時の**上書き**。
  ⇒ ★★ **教訓: 「規則が別物」と測って書いた読みも半分だった**——⑶ 抜きの port は
  fixture `test/dot-force-down` を LP から遠ざけた（旧 Lily# 規則「線上→DOWN」は
  **負シフト側で結果だけ正しかった**。3 層で snapshot はバイト復帰＝data-pos のみ）。
  **grob の direction を疑うときは direction-polyphonic-grobs の配布先一覧を先に引く。**
  ⚠️ 残: `:578-586`（up 群の dot column が後続 up stem を避ける——3声+付点第1up声の形が要る・
  コーパス未踏）と **pushed dot の予約側**（spacing は押しを知らない・束縛する本が出たら配線）。
  ⚠️ **`audit/citation_drift.csv` は旧偽引用（:411-448）を "OK" と言っていた**（**範囲が実在する
  かしか見ない**）。しかも **2026-04-25 生成で `Svg/Renderer/SvgRenderer.cs`＝存在しないファイルを
  監査している**。⇒ **この検査は債務を返す前に監査対象**（§5）。
- ✅✅ ★★★ **閉じた（2026-08-05・第98セッション・`58415901`）。cue region は per-voice walk でも
  1 個の wrapper になった。** 正典 `IsInsideProcessedContainer` は cue を知っていたが、
  **手組みの skip リストが 2 か所**（`GatherVoiceMusicNodes`・`CollectMeasuresFromNode`）
  **とも cue を欠き**、span の cue 本体が region（縮尺）と flatten（原寸）の **2 回**歩かれていた
  ——第1ブロックなら小節が 1 つ増え（layout 3 対 `lysc ly` 2）、第2ブロックなら**次小節の
  空 placeholder を静かに上書き**。手組みを廃して `IsInsideProcessedContainerExceptParallel` に
  統一。実測・陽性対照・fixture は §1（第98セッション）。
  ⚠️ **起票の solo 側（「2 小節目に何も描かれない」）は起票時点から不正確**——`477b9fba` でも
  描かれる（HEAD とバイト一致で確認）。solo の実欠陥は**cue 内の休符が原寸**（LP は 0.0025 に
  縮める・実測）＝**別の claim として §1 ⑵' に起票**（`RestItem` に `IsCue` が無い・未修正）。
  ★★ **この項の教訓は「skip リストも walk の呼び出しと同じで、全部数える」**——正典の doc 自身が
  「per-walk の whitelist は drift する」と書いていて、その通りに drift していた。
  ⇒ **▶ ⑵（cue 混在列の packing）はこれで解禁**——cue と原寸が 1 つの列に立つ綴りが
  書けるようになった（踏む対が作れる）。
- ★★★ **符尾の attachment X が「符頭ごと」でなく「黒玉固定」**（2026-08-03・第77セッション。
  **測って名指しただけ・未修正**・▶ の先頭）。`LayoutUtilities.StemAttachX` は
  `NoteheadBlackStemAttachment.X` を**符頭によらず**返す。LP は**符頭ごとの ink 右端 − thickness/2**
  （実測 6 桁一致: 黒玉 1.304200 − 0.065 ／ 半玉 1.377400 − 0.065）。
  ⇒ **半音符の上向き符尾は 0.073200 左**。⚠️ ★★ **これは「綴りが 2 つ」ではなく「house が 1 つ
  足りない」型**——`MetronomeMarkGeometry.StemAttachment` は**同じ知識を拍単位で選び分けている**
  ので、**engine は答えを持っていて 1 か所だけが訊いていない**。
  ★ **対はもう開いた**（`97737c2f`）: `stem.up.right-edge.{half,black}-head`＝発散 −0.073200000 と
  **exact な対照**。⇒ **次は移植そのものから始められる**（▶ の先頭）。
- ★★★ **符尾の長さに綴りが 3 つあり、cue はどれにも属していない**（第84セッション・**測って
  名指しただけ・未修正**）。`StemCalculator.CalculateStemEndY`（記譜・音符も和音もここ）／
  `SharedRenderer.GraceNotes.cs:325` の `DefaultStemLength × scale`（**grace は自分で縮めている**）／
  `SharedRenderer.Tab.cs:307` の `3.0 × stringSpace`。**cue はどこにも scale を渡さない**ので
  **予約（`SpacingRules.StemSpacingInfo`）も描画もフルサイズ**。
  ⇒ **これは §2 A の主題そのもの**——**engine は符尾を縮める術を持っていて、cue の経路だけが
  訊いていない**（第83セッション ⑬ の `ApplyLeftHeadWidth` と同じ形）。
  ★ **LP 側の法則は測ってある**（`voice-boundary-spacing.ly` §E・▶ の cue の項に要約）。
  ⚠️ **床を一緒に入れないと「中央線付近だけ exact」になる。cue の snapshot が動く＝要承認。**
- ★★ **タイの列を「1 本ずつ」から「列ごと」へ**（第77セッションで 2 か所が同じ restructuring を
  名指しした: `TieFormattingProblem.ScoreColumnSymmetry` と `ScoreDirectionAgainstStems`）。
  LP は `Ties_configuration` を丸ごと振る（`tie-formatting-problem.cc:915-1001`）。
  **今は列の back のタイだけが対称性を払う greedy**。⚠️ **踏む対がまだ無い**（3 本以上のタイを
  持つ和音の本）。

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
- ~~**figured bass の row 深さ＝3 綴りのうち 1 つが残っている**~~ — **閉じた**
  （2026-07-30・第46セッション・`5edd9481`）。`EstimateLooseLineExtents` の `2.0 + n × 1.5` は
  **観測者（台帳 6 点）を作ってから削除**し、down extent は down スカイライン 1 本に戻った。
  ★ **同じ本が出した第2の欠陥（system 間の過剰予約）も閉じた**（2026-07-31・第47セッション・
  `dad91418`）。**ページブレーカは `SystemDetails.Shape` の 2 バケツで行を値段付ける**ようになり、
  **breaker と配置チェーンが同じ pair を同じ 12.672462 で見る**。
  ⇒ ★★ **§2 の主題そのものの実例**——「同じ量を計算する場所が 2 つ以上ある」の 2 つ目は
  **skyline を見ない側**で、**点が 1 つあるまで誰も気づかなかった**。
  ★ **残る figured bass の綴り債務は箱の「幅」だけ**——inter-system seed が
  `MinFigureBoxWidth` を**半幅**として使う（箱 1.6 対 実グリフ run 0.898）。
  ⚠️ **「これを変えると system 間が動く」は反証済**（半分にしても 3566 テスト・237 点すべて不動）
  ＝**不活性な綴り債務**で、閉じるのは X の対（▶ の ⒝）と一緒。LP の字面は
  `FiguredBassGlyphRun.Width`（stencil の X-extent・行内で左揃え）。

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
- ~~★★ **ビーム数が端で変わるビームの傾き**~~ — **閉じた**（第57セッション・`4b78405b`＋`5df1b0e1`・
  §1 ①②）。**`beamCount` はステム自身の多重度ではなく、その向きの最大値**
  （`stem.cc:1158` → `beam.cc:1517-1532`）。★ **残す教訓は 3 つ**: ⑴ **LP のソースが
  自分で反例を書いていることがある**（`stem.cc:1196-1202` の `a8[ a32]`）——**関数を最後まで
  読めば対の設計まで出てくる** ⑵ **同じ名前の「数」が 3 つある**（ideal 用＝向きの最大／
  端の検査用＝ステム自身／`calc_stem_shorten` 用＝全体）。**畳むと必ずどれかがずれる**
  ⑶ **「片端だけ 1 量子」は 2 本並べるまで傾きに見えない**。
- ~~★★★ **同じ 8-32-8 が片方だけ閉じた**~~ — **同じセッションで閉じた**（`bb4a5076`・§1 ④）。
  **正体は移植が取りこぼした 3 つ目の呼び出し**（`ScoreStemLengths`）。⇒ ★★ **教訓 2 つ**:
  ⑴ **値の *意味* を変える移植は、動機になった site ではなく grep 全件に当てる**——
  取りこぼした site は**落ちる点を持っていなかった**（床が binding する regime にだけ効く）
  ⑵ **フォークの 2 枝は「別々に起きうる」ものでなければならない**（§5.0 に汎化）。
  ★ **`test/beamlet-peaks` は 6 本とも LP exact**＝**双子で丸ごと閉じた最初の fixture**。
- ~~★★★ **`knee_correction` が未移植**~~ — **閉じた**（第56セッション・`bdf35ef0`・§1①②）。
  **フレームと同じ commit**。★ **残した教訓は 3 つ**: ⑴ **「説明のつかない差」は項が足りない**
  （0.13 ＝ `Stem::thickness`）⑵ **観測者ゼロの宣言は、移植と同時に観測者を足す**
  （`SpringRodModelTests` の 3 本が property を 0/0.5/2 に振る＝LP の E/F/G 冊と同じ形）
  ⑶ **`property_coverage.csv` の "Mention" は「宣言だけ」の索引**——他にも同じ形が居る。
- ~~★★ **拍グリッドが 2 軒ある**~~ — **閉じた**（第53セッション・`5e2dd497`・§1②）。
- ~~**`test/` に `8-16-8` の本が無い**~~ — **入れた**（`5c989f68`・`test/beamlet-peaks`）。
- ~~★ **1/12 の `beamExceptions` が未移植**~~ — **測って閉じた。移植するものが無かった**
  （`8ebcce6f`・9 点が最初から exact・§1③）。**1/12 が要求する群＝拍**なので拍構造と同値で、
  仕事は「3連に 1/8 の例外を届かせない」ことだけ。Lily# は**別の装置**（tuplet 境界で beam を
  切る）で同じことをしている。⚠️ **`three-eight` と同じ「答えだけ一致」**なので点は残す。
- ~~★★★ **`tupletBoundaries`／`tupletInteriors` は発明**~~ ・
  ~~**1/16・1/32 の `beamExceptions` 未移植**~~ ・~~**2 pass では届かない**~~ —
  **全部閉じた**（第54セッション・`bf00fecc`＋`7abab0f3`・§1 ①②）。`AutoBeamCheck` が
  `default-auto-beam-check` の 1 pass で、発明 2 つと merge 一式は同じ commit で退場した。
  ★ **残したのは「LP の決定関数」の要点だけ**（次に触る人が読む必要のある分）:
  `pos = 小節位置 mod 周期`／`pos == 0` か、**その時点の最短音価 `type` で選んだ grouping の
  ending moments に `pos` が*厳密に*入る**なら終える。**entry 選択は⑴ `type` 完全一致
  ⑵ 無ければ `larger-setting`＝`type` 以上で最小のキー（`:48-49`）⑶ それも無ければ拍構造**。
  ⚠️ **⑵ を「拍構造に落ちる」と書いてはいけない**（4/4 では同値だが 6/4 で割れる。
  **この一文は延べ 5 か所で誤って書かれ、5 か所とも訂正済み**）。
  ⚠️ **`recheck_beam` は 1 beam 内で最悪 O(n²)**（分割したら `i=0` に戻る）が、
  **発火は最短音価が縮んだときだけ**。
- ~~**`test/` に meshing の本が無い**~~ — **入れた**（`8bf5bb1a`・`test/beam-over-stem`・§1）。
  ★ **教訓は「点の値を本の検証に流用しない」**——点は別々の score の値で、本は 3 小節を
  1 行に置く別入力。**双子を新しく 1 本書いて測り直した**（`probes/beam-over-stem-book.ly`）。
- ~~★★★ **tab の梁が量子器を通っていない**~~ — **閉じた**（第67セッション・`03a54cfb`・§1 ⑦⑧）。
  **staff の定数 3 つ（線の太さ・radius・梁の length-fraction）を通しただけ**で、
  **`test/tab-string-pinned` は両譜とも三桁一致**。★ **残る tab の不一致 3 冊は運指の話**（§1 ⑨）。
  ★ **教訓**: **LP は tab の梁のレシピを `ly/engraver-init.ly` に 2 行で書いている**——
  **`lily/*.cc` を測る前に `ly/` の context 定義を読む。**
- ⚠️⚠️ **tab の「弦の選び方」は LP に合わせない**（**ユーザー明言・§1 ⑨**）。**LP の
  `determine-frets-and-strings` は開放弦優先で、Lily# は手の位置（`nearFret`）と小節内の
  弦の一貫性（`barString`）を見る意図的な固有機能**。**「LP と違う」を欠陥として起票しないこと。**
  ★ **帰結**: **弦を明示しない tab 本は LP と恒久的に比較できない**——
  **比べたい本は `\N` で固定する**（`test/tab-string-pinned` がその形）。
- ★ **`DefaultBeamStemUp` の「完全同数」tiebreak が LP と別物**（2026-08-01・第67セッションで
  **名指しただけ・未測定**）。**LP は方向ごとに `max(-dir × head_positions[-dir], 0)` を足し、
  `total[UP]/count[UP] − total[DOWN]/count[DOWN]` で比べ、それも同数なら `total` の差**
  （`lily/beam.cc:913-935`）／**Lily# は `BeamMember.StaffPosition`（＝和音の頭の平均）の総和の符号**。
  ⚠️ **`BeamMember.StaffPosition` が今も存在する唯一の理由がこれ**——**梁の幾何はもう読まない**
  （第67セッションで `BeamSideHead` に統一）。**踏む対がまだ無いので、先に probe を書くこと。**
- ★★ **fixture のコメントを直すと snapshot が動く**（`data-pos` は**ソース offset**）。
  直すこと自体は正しい（stale な prose を残さない）が、**GO ゲートになる**ので
  ⑴ **属性を落として 1 行ずつ照合し「data-pos だけ」を証明する** ⑵ **その証明を
  commit message に書く**。2026-07-31 に 3 冊でこれをやった。

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
- ★ **歌詞行が譜間の「中で」LP と別の位置に立つ**（2026-08-14・双子実測・**未着手**）。
  ⚠️ **上の「force 0 のまま」とは別件**——あちらは*再配分*の話で、これは*静止位置*。
  grandStaff の 2 譜の間に `staff upper with lyrics words` を置いた双子で、
  **歌詞行なしなら両者 9.000 ss で完全一致**（＝計測の陽性対照）、歌詞行を入れると：

  | | 上譜中心→歌詞ベースライン | 歌詞→下譜中心 | 譜間合計 |
  |---|---|---|---|
  | LilyPond 2.26.0 | **6.739** | **4.500** | 11.239 |
  | Lily# | 5.650 | 6.050 | 11.700 |
  | 差 | **−1.089**（近すぎ） | **+1.550**（遠すぎ） | +0.461 |

  ⚠️ **正味の 0.461 は逆向きの 2 つの誤差の残差**なので、合計だけ見ると小さく見える。
  効くのは `nonstaff-relatedstaff-spacing`（上）と `nonstaff-unrelatedstaff-spacing`（下）の
  どちらを Lily# がどう読んでいるか。**⚠️ SpanBarStub 説は否定済**——
  `span-bar-stub-engraver.cc` は未引用で Lily# に概念が無いが、stub は Lyrics 文脈に
  純粋高さを**足す**ので、欠けているなら Lily# は*狭く*なるはず。実測は逆。
  再現（`lysc svg` と `lilypond --svg` で `<line>` / `<text>` の y を読むだけ）:

  ```
  octave absolute
  time 4/4
  part upper { clef treble }
  part lower { clef bass }
  section Main {
    upper { d'4 d' e' d' | b4 a b2 | }
    lower { g,4 b, c a, | d4 d d2 | }
    lyrics words { Praise God from whom | all bless- ings | }
  }
  form main { Main }
  score main "out" { grandStaff { staff upper with lyrics words
                                  staff lower } }
  ```

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
- ★★★ **タイの列アウトライン（2026-08-03・第76セッション・点あり＝`tie.width.seconds.upper`
  +0.888699999）**。Lily# の `TieFormattingProblem` は**そのタイ自身の符頭の箱**しか知らないので、
  候補が自分の箱を出た瞬間に**符頭の中心へ後退する**。LP は列の箱を全部持つ:
  `set_column_chord_outline`（`tie-formatting-problem.cc:96-287`）＝各符頭・付点（LEFT のみ）・
  **符尾**・旗（LEFT のみ）・臨時記号（RIGHT のみ）・同じ列の他の符頭。後退箱は `:243-258` で
  **列の一番外の符頭**から立つので、**和音の内側では後退しない**。そのうえで `:583-609` が
  **符尾の Y 範囲に入る attachment を `stem端 − stem_gap(0.35)` へ引き戻し**、`:565-579` が
  短いタイで `close_by` と intersect する。
  ⚠️ **`tie.width.clears-head` と `tie.width.seconds.lower` は今 9 桁 EXACT** ＝**この移植の
  falsifier**。⚠️ **snapshot は動く**（第76セッションで動いた 9 枚のうち 3 枚は戻る側）。
  ★★ **先に `Interval` 型を作ると字面移植になる**（2026-08-03 の自己監査で名前が付いた ⒝ 債務）。
  LP の `Interval`（`lily/interval.hh`）は `distance` / `widen` / `linear_combination` /
  `intersect` を持つ**一級の値**で、**タイのコードだけで 4 つ全部**を使う——
  水平距離罰（今は手で展開）・`GetAttachment` の 2 つの `widen`・そして**この島が要る `intersect`**
  （`:565-579` の `close_by`）。**器が無いから開いたコードになっている**のであって判断ではない。
- ★★ **`Bezier` 型が無い**（同じ自己監査の ⒝ 債務）。`BezierBow.MidpointHeight` は LP の
  `slur_shape(…).curve_point(0.5)` を **`0.75 * h` の閉じた式**で書いている（係数は厳密）。
  **読み手は 2 つになる**——`SlurScoringProblem.InterpolateSlurY` も自前で曲線を標本化している。
  ⇒ **`curve_point` を持つ Bezier を 1 つ作れば両方が LP の字面になる。**
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
- ★★ **`lysc ly`（双子 exporter）の穴**。**塞ぐたびに LP と突き合わせられる本が増える**ので、
  忠実度作業の**測定可能面積そのもの**が懸かっている。
  ~~⑴ `voice { }`~~・~~⑵ `grandStaff` の入れ子~~・~~⑶ `ossia`／`part` 宣言なし~~・
  ~~⑷ section のヘッダ~~・~~⑸ 和音のオクターブ記号~~・~~⑹ grace のあとの音価~~ — **すべて完了**
  （第61〜63セッション。最後の 2 つは `275c12ee`）。
  ~~⑻ `@stemUp`/`@stemDown` を落とす~~ — **完了**（2026-08-03・engine 側と同じ commit で。
  `\once \override Stem.direction`。理由は §1 ⑥）。
  **残っているのは 1 つ**:
  ⑺ **度数和音が `<>` になる**（`<1 3 5>`・`<d 3 5 7,>`）。**今は警告を出すだけ**で、
     **解決は独立した移植**——`MeasureCollector.ItemFactory` が root ＋ 調に対して解決している
     側から**字面で写せる**。**閉じれば `chord-octave-marks` の bar check が消える。**
  ⚠️ **「exporter が黙って空を返す」欠陥はこれで 6 度目**（第55・56・61・62・63）。
  ⇒ ★ **落とすなら必ず `Warnings` に出す**。**`<>` や空の part 変数を黙って書かない。**
  ⇒ ★ **塞いだら双子 199 本の before/after を全数比較する**（第62セッション ② の手順。
     1 回目で本物の退行を捕まえている）
- ★ **fixture 5 本が今の文法で parse しない**（`test/beamed-rest`・`test/cue-notes`・
  `test/dot-force-down`・`test/multi-movement`・`showcase/grammar-2026-06-09`）。
  `p`/`chords` の予約語化・`name = …` 宣言の撤去・`time`/`tempo` の score レベル化で置き去りに
  なったもの。**snapshot リストには載っていないので誰も落ちない**⇒ **直すか消すかを決める**
  （未リリースなので後方互換は考えない・§3）
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

> ## ★ 引用の **OVERRUN 検査**（範囲が関数の外へはみ出す）は **C# に無い**（2026-08-14・**未移植**）
>
> 2026-08-14 に PowerShell の使い捨て検出器で回したところ、**8 件の実害**を出した:
> `BeamScoringProblem` の 5 件（`set_minimum_dy` は実際 462-482 なのに `:470-489` 等、
> **系統的に +3〜+6 ずれ**）、`stem.cc:1006-1018`（`is_valid_stem` 993-1010 と
> `Stem::print` 1013-1048 を**跨いでいた**）ほか。全部直したので**いま回すと 0**。
> ⚠️ **既存の `CitationRangesHoldTheirNamedSymbol` は原理的に見えない**——範囲の*始点*が
> 正しい関数の中に落ちていれば通るため。
>
> **移すなら規則はこれ**（3 段の絞りは実測で決めた。素朴にやると偽陽性 309 件）:
> 1. 正当なのは「**名前の定義が範囲に載っている**」か「**範囲が本体の内側**」のどちらか。
>    本体の終わりは LP の作法どおり **列 0 の `}`** で取れる
> 2. ⚠️ **CamelCase のクラス名を除く**（`Beam_scoring_problem` は自分のコンストラクタに
>    一致してしまう）。LP の関数は**小文字始まり**なのでそこで切る → 131 件に落ちる
> 3. ⚠️ **主語は住所直後の *最初* の記号だけ**。後続は散文が挙げた callee/近傍 → 44 件
>
> 残る OUTSIDE 36 件は**正当な「呼び出し箇所を指して被呼び出し関数を名指す」引用**で、
> 散文自身がそう書いている。**defect 一覧ではないので、そのままラチェットにはできない。**
>
> ★ **una corda の積み順**（`MusicMarkEngraver.PedalFamilyRank`）は未実測の推測のまま。
> 決めるには **3 種同時の双子 1 本**でよい:
> `pf { c1@ped@sost@una.corda | c1@ped(off)@sost(off)@tre.corde | }`（`part` に `pedal text`）を
> `lysc svg` と `lilypond --svg` で描いて `<text>` の y を読む。sustain / sostenuto の対は
> **LP 2.26.0 で 2.443 離れ、sostenuto が譜に近い**と実測済（§D の隣に同じ手順）。

> ## ★★ XML doc の警告 476 件が **Release だけで出て、誰も見ていない**（2026-08-11・第135セッションで起票・**未着手**）
>
> **§0 の開始時ビルドは Debug で、Debug は 0 件**。`LilySharp.Core.csproj:20-21` が
> **`GenerateDocumentationFile` を Release の `PropertyGroup` にだけ置いている**ので、
> **doc コメントが検査されるのは Release ビルドのときだけ**——`lysc` を Release で建てた人しか見ない。
> **数え方**（`-v n` でないと警告行が出ない）:
> ```powershell
> $rel = dotnet build LilySharp.Core\LilySharp.Core.csproj -c Release --no-incremental -v n 2>&1 |
>        Select-String 'warning CS'
> "$($rel.Count) 本"   # 2026-08-11 実測 = 476（Debug は 0）
> ```
> **内訳**: **CS1573 312**（`<param>` が一部だけ書かれている）／**CS0419 48**（cref が曖昧）／
> **CS1574 36**（cref が解決できない）／**CS1570 24**（XML が壊れていてタグが閉じていない）／
> **CS1734 16**（`paramref` の相手が居ない）／**CS1591 14**（public に doc が無い）／
> **CS1587 10**（doc が置ける場所に無い）／**CS1571 8**（`<param>` の重複）／**CS1572 8**（`<param>` の相手が居ない）。
> 密度の上位は `SpacingRules.cs` 34 ／ `ElementCoordinator.cs` 28 ／
> `DynamicEngraver.cs`・`OutsideStaffStacker.cs`・`LayoutEngine.cs` 各 24。
>
> ★★★ **これは体裁の問題ではない。少なくとも 84 件（CS1574＋CS0419）は「`<see cref>` の相手がもう居ない」**
> ——**§5.1 のリネーム規律が名指しで警戒している「grep 不可視の消費者の取りこぼし」そのもの**を、
> **コンパイラが既に検出して報告している**のに、**その報告が出る構成を誰も建てていない**。
> **CS1570 の 24 件は `<remarks>`/`<para>` の入れ子が壊れている**＝**その doc は整形されずに落ちる**。
> ⇒ **この棚の価値は「警告を 0 にすること」ではなく、まず 84 件の壊れた cref を読むこと**
> （**リネームで失われた参照の一覧＝どの島が黙って字面を失ったかの地図**）。
> ⚠️ **一括で直さない**（§5.2「一覧は欠陥の一覧ではなく*候補*の一覧」）。**CS1573/CS1591 の 326 件は
> 純粋に doc の不足**で、**急がない**。
> ⚠️ ★ **直す前に決めること**: **`GenerateDocumentationFile` を Debug にも入れるか**。
> 入れれば §0 の開始時ビルドが毎回この 476 件を吐くので、**先に減らしてからでないと
> 「Core 0 warning」という引継ぎの決まり文句が意味を失う**。**順序は「読む → 減らす → 構成を揃える」。**
>
> ## ★★ `LILYSHARP-OWN` の棚卸し（2026-08-01 に開いた・**まだ終わっていない**）
>
> §5.2／§7.6 の訂正（**LP から導出したものは字面でなくても `LILYPOND-REF`。
> `LILYSHARP-OWN` は LP に対応物が無いときだけ**）を**既存の札に当て直す**作業。
> ⚠️ **「62 件」は数え方が書かれていない**（§0 の罠）。**2026-08-01・第62セッションの実測は 67 件**:
> ```powershell
> @(Select-String -Path (Get-ChildItem -Recurse -Filter *.cs -Path LilySharp.Core) -Pattern 'LILYSHARP-OWN').Count
> ```
> **第62セッションは 1 件も足していない**（`git diff` の `+` 行で確認済）ので、
> **差は数え方か、その前のセッションの増分**。**判定を始める前にこの数で取り直すこと。**
>
> **Core の `LILYSHARP-OWN` は 62 件**。うち **18 件は近傍に LP の行番号がある**
> （機械的に数えた・下のコマンド）——**それが即「誤り」ではない**: ⒞ の多くは
> **「LP は X をやるが Lily# は意図的にやらない」と、外れた相手を引用して**書いてある。
> ⚠️ **だから一覧のまま relabel しないこと**（§5.2「一覧は欠陥の一覧ではなく*候補*の一覧」）。
>
> **1 件ずつ、次の 1 問で判定する**: **その式が計算している*量*を LP も計算しているか。**
> **しているなら ⒝（`LILYPOND-REF` ＋「なぜ字面でないか」）／していないなら ⒞。**
>
> **済**: `TupletBracketEngraver.CalculateSlope`（`LILYSHARP-OWN` → `LILYPOND-REF`。
> LP の `tuplet-bracket.cc:530-549` を*簡略化*した式で、**LP の行番号を真横に持ちながら
> 「独自」と名乗っていた**＝§5.2 が名指す形そのもの）。**残り 17 件は未判定。**
> ⚠️ ★★★ **そして「なぜ簡略なのか」を訊かれて調べたら、2 つ分かった**（`270af291`）:
> ⑴ **誰も選んでいない**——本体は**移植の規律より前**の一括 commit（`26f91d85`・2026-02-24）で
> 丸ごと入っており、「LilyPond より simpler」という文言は **2026-07-29 に隣の encompass を
> 移植したときの*後付けの記述***。**性能とは無関係。**
> ⑵ **足りないと書いた入力は、実は同じ関数の中に既にあった**——`useRealExtents` の枝が
> `NoteColumnLayout.OutwardTipDeviceY` で**列の実グラフィカル到達**を作っており、
> `MemberBeam(i)` が**覆う beam の quanted 幾何**を返している。⇒ **配管ではなく*読み方*の問題**で、
> **止めているのは対の不在だけ**（`staff.staff.tuplet-bracket-*` は平らな encompass しか押さえていない）。
> ⚠️ **⑵ は私が同じ日に書いた「字面にするには何が要るか」が外れていた**という話でもある——
> **§5.0 の「止めた側が書いた『どの行を読め』も推測で、外れていた」の再演**。
> ⇒ ★★ **⒝ の札に「字面にするには何が要るか」を書くときは、その場で*関数を読んで*から書く。**
> ```powershell
> # 近傍に LP の住所を持つ LILYSHARP-OWN を数える（候補の一覧・判定はしない）
> Get-ChildItem -Recurse -Filter *.cs -Path LilySharp.Core | ForEach-Object {
>   $L = Get-Content $_.FullName
>   for ($i=0; $i -lt $L.Count; $i++) { if ($L[$i] -match 'LILYSHARP-OWN') {
>     $ctx = $L[[Math]::Max(0,$i-6)..[Math]::Min($L.Count-1,$i+10)] -join ' '
>     if ($ctx -match '(lily|scm)/[\w./-]+\.(cc|hh|scm|ly):\d') { "$($_.Name):$($i+1)" } } } }
> ```
> ★ **先例**（§5.2 に本文あり）: 和音記号の **2.6** は `LILYSHARP-OWN` と宣言されつつ
> **LP の規則がその真横に引用されていた**——実体は 2.616256 の 0.62% 低い近似で、
> **札が「独自」だったせいで近似のまま 2 か所に増えた**。**札の誤りは値の誤りを保存する。**

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
  command 列に載せる。Lily# は `MidMeasureChangeGaps` が代役・§2B の mid-line clef 残件と同根）
  ⑷ ★ **行末の courtesy 群**（2026-08-02・第75セッションで**点が出た**）。**LP は行の両端に
  break-align 群を 1 つずつ持つ**のに、Lily# は**行頭だけ `BreakAlignSpacing` に通し、行末は定数
  3 本**（`SpacingRules.BarlineToCourtesyKey` 0.8 / `BarlineToCourtesyTime` 0.75 /
  `CourtesyKeyToTimeGap` 1.15）で綴っている。**⑵ と同じ「合成が厳密なら乖離ゼロ」ではない**——
  `courtesy.meter.barline-to-cancellation` が **−0.2**（LP は取消まで 1.00、拍子単独なら 0.75。
  **小節線からの間隔は 1 つの数ではない**＝grob ごとの `space-alist`）。⚠️ **0.8 を 1.0 にするだけでは
  駄目**：予約 `KeyCourtesySuffixWidth` が同じ定数を読むので描画と予約が一緒に動く必要がある。
  ⚠️ **出所は 1 軒**＝`SpacingRules.BarlineToCourtesyKey` の remarks（`break-alignment-interface.cc:228-243`）。
  **space-alist の値を写したのではない**——宣言は `extra-space 1.0` なのに印字は 0.750000（walk は
  group extent で回り `break-align-anchor` が後で動かす）。**「宣言値＝定数」と書けば偽の住所になる。**
  ⚠️⚠️ **0.75 は 1 冊でしか測っていない**（§7.7 の「1 冊の texture で定数化しない」に触れる・第75セッションの
  自己監査で自白）。**1.15 は 2 か所独立一致で交差検証済み**。⇒ **0.75 には texture を変えた 2 冊目**
  （行末が `|.` や複縦線／拍子が C や 3/4）**が要る。観測は `courtesy.meter.barline-to-meter` 1 点だけ。**
  モデルに列を足す日はこの 4 つを一緒に見ること（⑵ grouper・⑸ 倍率と同じ「モデル追加が先」型）。
  ★★★ ⚠️ **2026-08-10（第131セッション）＝ユーザーが目で見つけて起票。乖離は縦線の手前ではなく
  *拍子の右側*に在る。** 対 `scratch/beamskip/lp-courtesy.ly` と `courtesy.lys`（同じ紙・
  `c1 | c1 break / time 1/4 / c4 | c4 |` ＝改行位置で拍子が変わる最小の本）:

  | | LP | Lily# |
  |---|---|---|
  | 五線 | 8.5358..110.9157 | 8.5358..110.9658 |
  | 行末の縦線 | 107.921 | 108.426 |
  | courtesy の拍子 | 108.861 | 109.366 |
  | **縦線→拍子** | **0.940** | **0.940**（一致） |
  | **拍子→五線の右端** | **2.055** | **1.600** |

  ⇒ ★★ **`BarlineToCourtesyTime` 側は合っている。足りないのは「拍子の右に取る場所」で 0.455 ss。**
  Lily# はその分だけ行末群に取る幅が狭く、**手前の音楽を余計に伸ばして縦線が 0.505 右へ寄る**
  （だから縦線の位置も拍子の位置も同時にずれる——**どちらか片方を定数で直すと嘘の一致になる**）。
  ⚠️ **台帳に「courtesy 拍子の右側」を測る点は 1 つも無い**。§5.0 のとおり**点が先**。
  ⚠️ **定数で埋めないこと**（ユーザー判断 2026-08-10）。この ⑷ は⑴⑵⑶ と同じ
  「paper column モデルの欠落」なので、1 件だけ定数化すると**同じ量の 2 つ目の綴り**を作る。
  ★ 併せて**別件の起票**: `beam-auto` の 1 段目は LP と Lily# で**改行位置が違う**（縦線 3 対 5）。
  同じ段に別の音楽が載るので、**あの本で行末の x を比べてはいけない**。
  ⚠️ ~~ただし**数値の乖離は現状ゼロ**（合成が厳密なので）——着手根拠は点が出た regime だけ~~
  ★★★ **2026-08-01（第59セッション）に⑴に点が出た**＝`grace.column.approach` **+0.850449**。
  **「合成が厳密だから乖離ゼロ」は grace については偽**だった: **LP は前のばねを*縮める***
  （`spring *= 0.8`・`lily/spacing-spanner.cc:396-403`）のに、**Lily# は run の幅を前のばねの
  min に*足す***（`AdjustSpringForGraceNotes`）。**足すと引くでは、run の幅が動いても
  `前の音符 → 最初の grace` が動かない**——実際この点は列の幅を 46% 変えても 1 桁も動かなかった。
  ⇒ **⑴ は「表現できないから畳み込んだ」だけでなく「畳み込んだせいで別の機構になっている」。**
  **着手根拠はもう regime ではなく点**（同じ本の中に対照 `grace.column.approach.main-control`
  があり、そちらは exact なので**普通の音符間は無罪**と分かっている）。
- ~~**中心合わせされた 2 つの text grob**~~ — **両方とも片付いた**（和音記号 `dcbf08e9`・
  音節 `98672c3a`）。⚠️ ただし `ChordNameEngraver` の `Math.Max(2.0, …)` 幅の床は**残っている**
  （`LILYSHARP-OWN` と明示済・1 文字の "C" 1.877882 を上書きするので**実際に効く**）
- ⚠️ **`KnuthPlassBreaker` は `LpProvenanceTests` の監視範囲外**＝§5.2.1① の網の穴。
  `OverfullPenalty` の誤った `LILYPOND-REF` が何年も生き延びたのはそのため

### C. 構造の書き直し候補（第103セッションのレビューで名指し・4 点）

> ユーザー問「書き直したくなるコードはあるか」への答えを台帳化したもの。**優先順**。
> ⑵⑷は §2A に既存項があるので**参照だけ**（二重台帳を作らない）。

- ★★★ **⑴ 多声 walk の moment 順への再設計**（最大の構造負債・**未着手**）。
  現行は `MeasureCollector`: **voice 0 だけ本流にインライン・他声部は `_parallelSpans` から
  後で再構築**（`BuildExtraVoiceTracks`）。この「voice 0 の全時系列が先」という順序が
  **staff 時間順の状態共有を原理的に不可能**にしている。出た欠陥クラス:
  ⑴ **声部横断の復元♮の欠落**（collisions.ly・第103セッション②——`_measureAccidentals` は
  1 辞書なのに走査順が時間順でないので、v2 の es の後の v3 の E4 に ♮ が付かない）
  ⑵ cue region の二重 walk（第98セッション・skip リスト drift）⑶ collect 相の per-walk
  whitelist 一般の drift（正典 doc 自身が予言）。**直し方は LP と同じ「moment 順に全声部を
  1 回で歩く」**（Engraver 順序の鏡）。大手術なので**踏む本が溜まってから**——ただし
  臨時記号系の corpus 本（accidental 族は scheme が多いが plain も残る）が来るたびに
  ここに戻る。⚠️ 部分修理（臨時状態だけ staff 時間順の別 pass にする等）は
  **3 つ目の walk を増やす**ことになるので、§2A の主題（同じ量の N 個目の綴り）と
  引き換えにしないこと。
- ★★ **⑵ 残っている「同じ量の 2 つ目の綴り」**——§2A の既存 3 項を指す（詳細はそちら）:
  符尾 attachment X の黒玉固定（▶ 先頭・対 `stem.up.right-edge.{half,black}-head` 開設済み）・
  符尾長の 3 綴り（cue がどれにも属さない）・タイ列の greedy（`Ties_configuration` 丸ごと
  採点への置換）。
- ★★ **⑶ record モデルの同値性（identity の欠如）**（**未着手・設計判断が要る**）。
  音楽モデルが C# record なので **unison・同音の 2 項が「等しい」**——`IndexOf`／`Contains`／
  `Dictionary` キーが黙って衝突する。実例 = fixed 第18号（`TieItem` の unison 対が
  `ordered.IndexOf` で両方 slot 0 → DOWN 弓の 2 重描き・`ReferenceEquals` の `FindIndex` で
  回避）。stem support の `positions.IndexOf(supportPos)` も同族（今は並び順で無害と確認済）。
  **恒久解はモデル項目に識別子を持たせる**（record をやめるのではなく Id を値の一部にする等・
  等値性の意味論が変わるので**要ユーザー判断**）。それまでの規律: **モデル項目のコレクション
  検索は参照一致で書く**（値一致で書いた時点で unison バグの候補）。
- ★ **⑷ collect 相と layout 相の二重解決**——§2A の既存項
  「多声の譜が `VoiceCollector.Collect` と `NoteCollision` を 2 周する」を指す（詳細・実測
  +0.3%・畳み方 2 案はそちら）。⚠️ **着手前にコスト判断**（§2A に明記済み）。

### D. 文法の変更候補（効率の観点・**3 点とも要ユーザー判断＝勝手に実装しない**）

> ユーザー問「効率的な処理のために文法を変えるべき所はあるか」への答えの台帳化。
> 文法変更は言語設計＝ユーザーの決定事項。ここには**提案と根拠**だけを置く。

- ★★★ **⑴ オクターブアンカー（絶対指定）構文**（LP の `\fixed` 相当）。現行は相対のみで、
  **1 音の編集が同一 voice の後続全音のピッチ解決に波及**する＝増分処理（F3 増分・
  `project_lilysharp_incremental_architecture`）の依存チェーンが最悪で曲全体に伸びる。
  小節／フレーズ境界に置けるアンカー（または `\fixed` 型の囲い）があれば再解決の波及が
  区間で止まり、手書き・AI 記譜のオクターブ事故も減る（第103セッションだけで融合スパンの
  `g'4`→`g4` を 1 回踏んだ。twin 作業の頻出事故クラス＝memory
  reference_lilysharp_relative_octave_authoring）。**効率と正確性の両方に効く最有力案**。
  ⚠️ 綴りは sigil 規則（§3D・LP が記号で綴るものだけ記号）に従うこと。
- ✅ **⑵ file 既定と楽中変更の構文的区別 = 第125セッションで landed**（ユーザー判断＝
  **bare 廃止**。詳細は §1）。トップレベルの音楽は LYS0020 で拒否になり、top-level の
  clef/key/time/tempo は**無条件でファイル既定**になった（並び立つ音楽が書けないので、
  同じ綴りが位置で意味を変えようがない）。
  ⚠️ **ここに書いてあった「この walk 自体と欠陥クラスごと消える（1 パス化）」は誤りだった。**
  `IsInsideMusicContent` は Phase 7.3（中間小節調号変更）由来で、仕事は part ヘッダ /
  phrase / section 入れ子の判別＝**bare の有無と無関係に残る**。実際に消えたのは
  `topLevelMusicSeen` の bool 1 個と 4 case のガードだけ。
  ★ **この誤りの出所は「コードで確かめずに台帳の言明を引き写した」こと**——§5 の
  「corpus に訊く」と同じ穴を、台帳自身が踏んでいた。**台帳の効能書きも実測の対象。**
- ⚠️ **⑶ voice スパンの遅入り —— 提案の半分は前提が誤りだった（2026-08-09 実測で訂正）。**
  「spacer 糖衣（`s*15` 等）は検討余地」は**既に在る**: `*N` 乗数は `R1*N`（`Parser.Music.cs:335`・
  LILYPOND-REF `R<dur>*N`）・`:|*N`・`|: … :|*N` の**3 箇所で確立した綴り**で、パーサは
  **どの rest トークンにも受理**する（同 :336-337「any rest token」）。**spacer でも動く**
  ——`s1*3 |` と `s1 | s1 | s1 |` は**描画完全一致・両方無警告**
  （probe = `scratch\lpreg\mult-{probe,ctl}.lys`）。⇒ collisions.lys の v3/v4/v5 の
  パディングは今の文法のまま `s1*3` / `s1*6` / `s1*5` に畳める。
  **残るのは「スパン境界を小節グリッドから独立させる構文」だけ**で、`voice { … } { … }` の
  誤り回復（`RepeatedVoiceKeyword`・`Parser.Directives.cs:165-182`＝2 つ目の `voice` を
  1 つ目のスパンへ回収）は罠塞ぎとして正しい。⇒ **提案としては弱くなった。表現力寄りで
  優先度は最下位。**
  ★ **教訓（この項自身が例）**: 台帳の「〜が無い」は**Lily# 側の語彙を検索してから言う**
  （§1 第113 の同じ家訓の再犯）。

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
| ★ **セリフ体は TeX Gyre Schola のまま同梱する。LP の C059 には合わせない**（ユーザー判断・2026-08-02） | **量を測ったうえでの決定**。LP は `"LilyPond Serif"` を **C059** に解決し（`ly:stencil-expr` がファイルパスごと吐く）、C059 は **AGPLv3**（URW の例外は PS/PDF への埋め込み限定で**フォントプログラムの同梱は覆わない**）。**両者は advance は完全一致するが、カーンと合字が違う**: カーン値は **471 有効ペア中 438 が食い違い**、丸め後に予約幅が変わるのは **2 文字組の 11.2%（475/4225）**。合字は**両方とも合字にするがグリフ幅が違う**（`ff` 605 対 686＝5px、`ffi` 878 対 904、`fi` は一致）。**現実の文字列で 0〜4px＝0〜0.137 ss**（`Violoncello` が最大・`Allegro` +1px・`Ave verum corpus` は 0）。⇒ **0.03〜0.14 ss の恒久差**を受け入れ、**AGPL を持ち込まない**。⚠️ **帰結**: `text.width.{aa,va}` は**永久に非ゼロ**（原因は台帳に完全記述）、**今後テキスト幅の点は 1/9 の確率で非ゼロで開く**、そして**紙面そのものが LP と字送りで違う**（測定だけの話ではない）。⚠️ **測っていないのは regular/bold/bold-italic 面**（italic だけ全ペア走査した）。★ **差し替えは後からできる**——`TextFontMetrics.SerifFamily` と `Fonts/` とライセンス表記だけで、対照本 `TS1`/`TS2` が効果を即座に示す |
| **LP の「正」は 2.26.0** | 版で PUA コードポイントも Emmentaler も動く。**必ず feta 名で引く** |
| **cross-staff beam は skyline から除外**（LP の字面） | `axis-group-interface.cc:850-858` の LP 自身のコメント。Lily# の「固定 3.5 stem を残す」は発明だった |
| ★ **和音記号は LP に合わせる＝中心合わせしない**（ユーザー判断・2026-07-25 明示） | 意図的乖離かを問うたうえでの決定。`ChordName` は X-offset も self-alignment も持たない（`define-grobs.scm:837-855`）＝ink 左が列。`dcbf08e9` で移植し `staffless.line-start.chords-vs-staff` が閉じた。⚠️ **和音グリッドは別 grob（`GridChordName`）で LP も中心合わせする**が、中心を取る相手は小節の四角。Lily# に四角は無いので chords-only シートは ChordName 経路のまま＝**「グリッドも直す」で触らない** |
| ❌ **撤回（2026-07-27・ユーザー判断）: 独立 lyrics 行を「譜のような帯」として置く** — **もう決定ではない。蒸し返し禁止の対象から外れた。** | **旧決定**（2026-07-26）: 独立行は「譜に付く歌詞」ではなく**リードシートの word トラック**なので譜グループとして置く＝**9.600000 対 LP 5.500000＝+4.100000**、台帳には載せず導出形で主張。**撤回の理由**は「間違いだったから」ではなく**射程が二度狭まって残らなかった**から: ①2026-07-27 に「鎖に参加しない」部分が `lyrics.chord-row.between-systems.*` の実測で落ち、②同日 LYRR/LYRRV が **LP 側の恒等を 59 行の機械差分で確定**させ（`\lyricsto` の有無で LP は 1 行も変わらない）、**残っていた「距離」も Lily# 単独の量**だと分かった。⇒ 行は `nonstaff-relatedstaff-spacing` で自分のインクから置かれる（`dee2c045` 系）。**いまの状態**: `lyrics.row.staff-to-lyric` は**台帳点で exact**、`LyricRowIsSpacedLikeTheLyricsContextItIs` が**2 つの綴りが一致すること**（＝LP の恒等の再現）を主張する。⚠️ **帯そのものは残っている**——行は自前の小節線を持ち verse を band 内に積む（`LyricRowBaseline` は `LILYSHARP-OWN` のまま）。**消えたのは「どこに置くか」だけ。** ★ **2026-07-28 に鎖にも入った**（§1 の第20セッション）。**帯そのものはまだ残る**が、
system の最後の spaceable 譜の下に立つ行は **verse ごとに鎖の要素**で、帯の上端は解に従う。
`lyrics.row.two-verse.verse-step` は exact、LYRRV ≡ LYRV|
| ★ **タブのフレット数字を LP より大きく描くのは意図的乖離**（ユーザー判断・2026-07-24 明示） | LP のタブ数字は小さくて読みにくい。Lily# は `TabConstants.FretFontSize = 2.6`（単数字幅 1.625・高さ 1.7875）＝LP の TabNoteHead 幅 0.990155 の約 1.64 倍。和音で数字が被る問題は**じぐざぐ配置**（`SpacingRules.ApplyTabChordSpacing` ほか）で解いてある。**「LP と違う＝発明だから消す」で削らないこと。** ⚠️ 弦間隔（`TabStringSpace`）は別の話で、そちらは LP の 1.5 に揃える |

---

