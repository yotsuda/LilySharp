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
# ⚠️ project 単体ではなく **solution** を建てる（約 4 秒）。**Core だけ建てても
#    `LilySharp.Cli\bin` の Core.dll は更新されない**ので、そのあと `lysc` を打つと
#    旧 Core を抱く（2026-08-17 実測・RULES §5.5。`--no-incremental` でも防げない）。
# ⚠️ ★★★ **この solution build の既定は Debug**（2026-08-20・第225 実測）——
#    **`LilySharp.Cli\bin\Release` の lysc.exe はこれでは一度も更新されない**。
#    lysc を測定・A/B に使う前に `dotnet build LilySharp.Cli -c Release` を明示（RULES §5.5）。
# ⚠️ その `--no-incremental` はここでは「増分の腐り対策」ではなく **0 警告を*確かめる*ため**——
#    **無変更の増分ビルドは何もコンパイルしないので警告 0 を*自明に*報告する。**
dotnet build LilySharp.slnx --no-incremental -v q 2>&1 |
  Select-String 'エラー|error|LilySharp\.Core.*warning'   # Core は 0 警告が期待値
#    （solution 全体には Tests の analyzer 警告が 22 行在る。Core の行だけ見ること）
#    ★★★ **この「0 警告」は 2026-08-18・第199 から XML doc の健全性も含む**——
#    `GenerateDocumentationFile` が Release 条件を外れて既定になったので、**壊れた cref・
#    間違った宣言に付いた doc・閉じていない XML は、この行が赤くなる**（毒で実測済み）。
#    ⚠️ **`CS1573`（param の書き漏らし）と `CS1591`（public に doc 無し）だけは `NoWarn`**
#    （2026-08-18 時点で 178 ＋ 6。**csproj のコメントに数と理由が書いてある**）。
#    ⚠️ ★★★ **第197 はこの棚を「新発見」として §1 に書きかけた**——**§2 を読む前に数えたため。**
#    **「見つけた」と思ったら、まず §2 を grep すること**（その棚は 62 便前から在った）。
# ⚠️ 成否行はロケール依存。ja-JP の機械では Passed! は 1 度も出ない（RULES §5.5）
dotnet test  LilySharp.Tests\LilySharp.Tests.csproj -v q 2>&1 | Select-String '成功!|失敗!|Passed!|Failed!'
# ⚠️⚠️ ★★★ **この緑は「この機械の Windows での緑」でしかない**（2026-08-19・第212セッション）。
#    **GitHub の門は 214 便のあいだ 1 度も読まれておらず、実際には赤だった**——
#    **最後に push した木で ubuntu Release は 5331 合格 / 59 失敗**。**必ず両方読むこと**:
gh run list --limit 5
#    ⚠️ **`X` が並んでいても中身を見るまで理由は分からない**——**fail-fast が他脚を*キャンセル*
#    するので、`X` の多くは「失敗」ではなく「巻き添え」**。**完走した脚だけが証拠**:
#      gh run view <runId>                          # ✓/X と job ID
#      gh run view --job <jobId> --log > $env:TEMP\ci.log   # `--log-failed` は途中で切れる
#      Select-String -Path $env:TEMP\ci.log -Pattern '\sFailed\s+LilySharp\.'
#    ⚠️ ★★★ **CI を待たなくてよい**（2026-08-19・第213 が建てた）。**ubuntu 脚はこの機械で
#    30 秒で回る**——**手順と罠は `scratch/linux-repro/README.md`**（`scratch/` は git 管理外なので
#    無ければ建て直す。中身は 10 行）:
#      wsl -d Ubuntu-24.04 -e bash -lc 'export PATH=$HOME/.dotnet:$PATH; cd ~/lilysharp && \
#        git fetch win && git reset --hard win/master && dotnet build -c Release -v q && \
#        dotnet test LilySharp.Tests/LilySharp.Tests.csproj -c Release --no-build -v q \
#          --logger "trx;LogFileName=$HOME/runs/lin.trx"'
#    ⚠️ **その Linux も「GitHub の ubuntu」ではない**——**この機械の WSL は LilyPond の
#    ビルド依存でシステムにも TeX Gyre を持つ**（第214 が同名シャドウを閉じて以来この差は
#    赤を出していないが、フォント起因の赤が出たらまずこの差を疑う）。
#    ⚠️ **WSL の LilyPond は v2.27.2 系**（~/lilypond）。**正典は 2.26.0**（RULES §5.2）——
#    質的確認には使えるが、台帳の点数をこの exe で取り直さないこと。
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

⚠️ ★★ **5 例目＝「未追跡 0」**（2026-08-16・第183セッション）。何便も
`git status --porcelain | Where-Object { $_ -like '??*' }` で数えていたが、
**`-like` の `?` は PowerShell のワイルドカード**なので**この式は全行に一致する**
——数えていたのは未追跡ではなく**作業ツリーの項目数**だった。
**木が clean な開始時は両者とも 0 なので、答えは何便も正しく、理由だけが間違っていた**
（この便の骨がまさにその形＝**正しい数を誤った理由で出す計器**）。**正しくはこう**:
```powershell
$st = git status --porcelain
"未追跡 $(@($st | Where-Object { $_.StartsWith('??') }).Count) / 作業ツリー項目 $(@($st).Count)"
```

⚠️ ★★ **7 例目＝「追跡コーパス 567 冊」の*数え方がどこにも書かれていなかった***
（2026-08-19・第211セッション）。**答えは 40 便連続で正しかったが、数え方が無いので
裏取りのたびに推測することになる**——**実際この便は最初 `audit` 配下だけを数えて 341 を出した。**
**正しくはこう**（**`.lys` は `audit` の外にも在る＝`LilySharp.Tests\Fixtures` など**）:
```powershell
"追跡コーパス $(@(git ls-files '*.lys').Count) 冊"   # 567（audit 配下だけなら 341・別の数）
```
⚠️ **ディスク上の全 `.lys` はさらに別の数**（未追跡の `scratch\` を含むので **1026 冊**級。
**§2 の ▶ perf の射程はそちらで数えてある**）。**どの母集団かを毎回書くこと。**

⚠️ **終了時に数えると差が出る**（終了時は編集済みファイルが並ぶ）。**両方書くこと。**

⚠️ ★★ **6 例目＝「A/B の before を、B を測ったあとに保存した」**（2026-08-17・第196セッション。
**同じ便で 2 度踏んだ**）。`audit/LilySharp.Probe` は**どんな小さな run でも
`audit/probe-out/pitches.csv` を上書きする**ので、**566 冊の結果を「あとで写す」ことはできない**
——**間に 3 冊の run を挟んだ時点で before は 3 行の CSV になっている。**
**1 度目は `before 0 / after 47` という*ありえない数*で気づいたが、2 度目は
「動いた本」の列が全部空という*もっともらしい*出方をした。**
⇒ ★★★ **全数を測ったら*その場で*写す**。**「あとで」は無い**:
```powershell
dotnet run --project audit/LilySharp.Probe -c Release -- pitches audit\probe-out\all566.txt
Copy-Item audit\probe-out\pitches.csv scratch\pNNN\<label>.csv -Force   # ← 同じ行で
```
⚠️ **写し損ねたら推論で埋めない**。**`git stash push -- <触ったファイル>` → build → 全数 → 写す →
`git stash pop` → build** で取り直せる（**約 15 秒**）。**第196 はこれで 2 度とも取り直した。**

⚠️ ★ **未 push は「開始時」だけ書いても裏取りにならない**（2026-08-17・第193セッション）。
**第192 は開始時の 9 しか書いておらず、終了時の数がどこにも無い**ので、
**第193 の開始時 4 が「間で push された」のか「数え違い」なのか区別できなかった**
（＝**その項だけ「引継ぎの数が合っていた」の連番に入れられない**）。
**開始時と終了時の両方を書くこと**——**次便が引き算できるのは、両端が在るときだけ。**
⚠️⚠️ ★★★ **両端だけでも足りないことがある＝*セッションの途中でユーザーが push する***
（2026-08-18・第201セッション。**開始 61・第1便で 62・そこで push・終了時 5**）。
**両端だけ見ると「61 → 5」で、便数とも commit 数とも合わない数になる。**
⇒ **`origin/master` が*この便の commit*を指していたら、間で push が起きている**——
**それを 1 行書く**（第201 の §1 がその形）。**確かめ方**:
```powershell
git --no-pager log --oneline -1 origin/master   # 自分が今日作った commit なら、間で push された
```

---
## 1. 現在地 ← **毎セッション書き換える**
最終更新 第226セッション＝**残債筆頭 ⒥「スパン予約の rod 化」を §5.0-3 の移植で閉じた**（対は前便 `558920fd` が開いた・本便は移植そのものから・`e3a190dc`）——**中間列を跨ぐ音節間予約は BumpSpanMin をやめて真の range rod**：`ReserveLyricLine` が rod を集め、`ReserveLyricWidthByColumn` が**小節ローカルに `SpringSolver.ApplyRods`**（lyric-hyphen.cc:163-179 set_spacing_rods → simple-spacer.cc:90-128 add_rod）。**予告されていた本丸「門×レイアウトの設計」は消滅した**——rod を小節自身の chain に焼き込むと ApplyRods の効果はスパンされた spring に局所なので **system レベルで与えるのと同値**、そして**門の Σ MinDistance がそのまま per-measure solve になる**（Σ(ideal+f·inv)＝rod の距離ちょうど）＝ **1 リスト（ApplySharedColumnReservations）の内側に置いただけで両消費者が正しくなる**。鍵は 1 つも広がらない（in-measure＝小節局所・SpringReusable の目録非接触）。**隣接（1 spring）は bump のまま**（max(ideal,need) で同値・全 lyrics.column.* exact 点は不動で確認）・**leading/trailing halves も bump のまま**（未測定 regime・line-start 置換が interior-spring 落着に依存＝ReserveLyricLine の remark）。**予測 3 本とも最終桁まで着地**：melisma-span.width +2.796089998→**0.000000000**（両 ink ちょうど 0.450000 離れる）・first-gap −1.095871799→**0.000000000**（=width/3＝等 spring 均等分配を ApplyRods の blocking force が実装）・bound-voice.skip-gap +1.507844999→**+0.036600000**（span-bump 項 1.471245 が消え、union-edge sliver（⒦ 族）だけが桁まで残った＝帰属替えして記録）。no-bind 2 点 0 不動。**sweep A/B 5/569・全冊同族で帰属済み**（lyrics-verses／amazing-grace／greensleeves／perf-lyrmel1k＝単声 melisma スパン・lead-sheet＝行歌詞が chord-only union 列を跨ぐ）。snapshot **220 中 2 枚だけ動き承認**（test/lead-sheet・test/lyrics-verses・承認前の音節重なり目視 0＝第223 骨 2 の作法・commit に台帳キーを名指し）。台帳 562 点のまま **3 点更新（2 点 exact 化・skip-gap 0.0366）・ss 非ゼロ 115→113／総和 9.423182122→4.059975326**（引き算 5.363206796 と桁一致で検算）・count 点 107／非ゼロ 2。
> ⚠️ **環境の罠 2 つ（本便が踏んで名指した）**：⑴ **台帳 json を `json.dump` で書き戻さない**——数値表記が全域で変わり（0.900000→0.9・1e-6→1e-06）**1091 行のノイズ diff** になる。**テキストの外科置換で当該エントリだけ**（この便は 3 エントリ＝diff 6 行）。⑵ before 画像の取り直しは `git stash push -- <file>` → build → render → pop → build（§0 6例目の 15 秒手順）。**PNG ハッシュで before/after 全冊が違うことを見てから読む**（前便骨 3「同一と出た A/B は何の証拠でもない」の画像版）。

**ユーザー起票（本便・目視）**：**lead-sheet の音節×縦線の重なりは before/after 同一＝この移植の退行ではない**。LP は音節と縦線のあいだに何も予約しない設計（§2H・extra-spacing-height）だが、**この本（行歌詞のリードシート）で LP が同じ絵を描くかは対が無い＝未測定**。→ 小粒残債に「lead-sheet 音節×縦線の対」を起票（修理より点が先）。

**続き便（同セッション・ユーザー指示 4 連）＝リードシートのセクション・拍子・繰り返し記号**（`7dc67cc7`・§3 に決定を記録）。**実査が先**: セクション枠は全変種で既に出ていた・繰り返しも music 経路の行（chords）では既に出ていた——**本当に無かったのは ⑴ 拍子（全変種）⑵ 歌詞行の barline 型（`|:` 等が素の `|` に落ちる）**、そして目視で 2 つの繰り返し欠陥が出た。⑴ **拍子＝意図的乖離**（LP は staffless 系に meter 幅を予約しない＝CO/CO3 実測を**ユーザー決定で覆す**）: `AnyStaffEngravesTime` に lead-sheet 条項＝**1 述語で門・layout 予約・renderer が同時に追従**・描画は grid 行の `SolvePrefixColumns.TimeX`（予約と同じ 1 導出）・行頭 spring は「meter で終わる staff の wish」。**台帳 2 点退役**（meter-identity・chords-vs-staff＝decided divergence は台帳の外・562→560 点）。⑵ 歌詞行の型＝`ClosingBarToken`→`ParseBarlineType`（music と 1 表・`|:` の意味論も HandleBarline の鏡・RepeatBoth 畳みも同型）。⑶ **繰り返し開始が太すぎる（ユーザー目視）＝LP 実測で欠陥確定**: LP 2.26.0 の `.|:` は**前小節の thin を持たない**（lprep.ly＝thin4+thick2 ちょうど）のに Lily# は Single を密着で印字→**同一 system 内の repeat-start に Single が譲る**（`EndBarYieldsToRepeatStart`・描画 3 site。改行では印字継続＝LP の end-of-line piece "|" そのもの）。⑷ **`:|:` は改行で `:|`／`|:` に割れるべき（ユーザー指摘）＝割れていなかった**（丸ごと行末・次行頭は無）→ LP の break piece を実装（`EndBarWithBreakPieces`/`StartBarWithBreakPieces`＝end ":|."・begin ".|:"・scm/bar-line.scm）。**sweep A/B 16/569 全帰属**（meter 族 7＝lead-sheet 4 変種・rows-song-sheet・lytie・drunken-sailor／repeat-ink 族 9＝grammar 3・repeat-volta・volta-labels・lyrics-volta・nav-below-clears-lyrics・voltagrace-ctl4・voltasky）・snapshot **10/220 再承認**（同じ帰属・目視で重なり 0）。**mid-piece の `time` 変更は行 voice に change item が無く幅もインクも出ない**＝退行ではなく非表示（小粒起票）。

**続き便 3（同・ユーザー指示 2 件）＝grid の行頭 bar と縦線クリアランス**（`8c13f304`）。⑴ **「拍子の直前にも barline」**＝grid は**全行を bar で開く**（staff の左端 system bar の grid 版・renderer の grid 分岐に 1 描画）——継続行は LP の keep-inside が ink を余白線に置くので新しい bar と重なる→ **grid の keep-inside 左 rod に LeftEdge gap 0.8 を加算**（1 行目の bar→拍子と同じリズム・`gridLeftEdge`・spring が既に超える列では不活性）。⑵ **「まだ重なっている」＝中間の縦線×前後の音節**——続く行は halves（bar クリアランス）を落として cross-bar rod に任せる設計（**譜の下の歌詞では LP 忠実**＝bar は歌詞 spacing に関与しない）だが、**grid の縦線は歌詞帯を貫く**ので衝突に見える→ **lead-sheet 経路（ApplyLeadSheetLyricSpacing）だけ halves を落とさない**（`keepEdgeHalves`・rod と halves は両方 minima＝max で共存）。実測: 最タイト対 'what'|'you' で 1.8／1.4 ss・全縦線 ≥ MinItemGap。sweep 3 段で 7→5→4 冊・全部 staff-less シート・snapshot 5 枚再承認・両 OS 5711/0/4。

**続き便 4（同・ユーザー指摘）＝stanza「1.」のドットが行頭 bar に重なる**（`6db864eb`）。StanzaNumberEngraver は全ラベルを `measures[0].X − 4.0` に吊るしており、grid が bar＋拍子 prefix を得た結果 **1 行目だけラベルが右へ滑ってドットが bar（x=0）に乗った**（継続行は −4.0 のまま＝列も割れた）。**lead sheet ではアンカーを行頭（`system.Indent − 4.0`）に**＝全行のラベルが同一列・bar の左 ~2 ss に着地。staff 譜は従来アンカーのまま。lead-sheet フラグは annotation context に載せた（その Score member は flat モデルで答えられない・両 builder が `MultiStaffScore.IsLeadSheet` を読む）。sweep 1/569＝drunken-sailor（コーパス唯一の stanza 付き staff-less シート）・snapshot 不動・両 OS 5711/0/4。

**続き便 5（委任「有利なら着手」→着手＝⒦ 歌詞 sliver 族を閉じた）**（`3650fd19`）。**着手判断の根拠**＝probe の per-voice LP 実測（LBIP 0.6521／LBI 0.6887）・当該ファイル・測定管路の全部がこの便で温かい＝新便の §0＋読み込みを丸ごと節約。**監査が疑いどおりの fork を出した**: engraver の edge walk は**各 staff の primary 小節だけ**（bound 声部の音節を primary の中心に描く）・予約は**全声 union 表**（primary の音節に外声の太頭を課金）——**2 綴り・別々に voice-blind・どちらも LP ではない**（LyricText の X parent は associated voice の音符頭＝per (voice, moment)・1 列 1 値の表では書けない）。**修理＝1 provider**（`SpacingRules.OwnVoiceAlignmentEdgeAt` → `LyricSpacing.OwnVoiceEdgeProvider`）を全消費者が読む: 予約・ink reach・line edges（layout も門も＝1 リスト）・描画 X（`LyricEngraver.ParentAlignmentEdge` が先に取る）。union/placeholder は行と未解決 voice の fallback として台帳が pin する regime にだけ残る。extent 式は single-item core に括り出し（list 形と per-syllable walk の 1 綴り）。**予測 3 本とも桁まで着地**: primary-control +0.0366→**0**（予約側）・bound-voice.skip-gap +0.0366→**0**（描画側）・no-bind.skip-gap +0.1098→**+0.0732**＝**最太頭課金の項だけが残った**（これは note-spacing モデルの問い＝lyric edge 族ではない・多声自然間隔の regime で対を開いてから）。sweep 1/569＝named-voice-lyrics（唯一の bound-voice 本）・snapshot 1/220 承認（重なり目視 0・verse 2 が自声の頭に整列）・両 OS 5711/0/4・台帳 ss 非ゼロ 113→**111**／総和 4.059975326→**3.950175326**（引き算 3×0.0366 と桁一致）。

**続き便 6（ユーザー所感「小さい数字のために perf コストを払いたくない」→実測で答えた）**（`ce997d0b`）。同日 A/B（alloc probe・⒦ 前 vs 後）: **打鍵は全冊 ±0**（lyrplain1k 55.1／lyrmel1k 37.7／lyrhyph1k 50.5／plain1k 45.3）だが **full render が全冊 +2 MB 級（+0.3%）・歌詞 0 の対照 plain1k まで +1.8**——**犯人は per-voice walk ではなく provider の構築**（クロージャ＋staff→voices 表を小節ごとに 3 呼び出し元が構築・うち 1 つは歌詞ガードの外）。**修理＝score ごとに 1 provider（ConditionalWeakTable・表は eager 構築で escaping closure が読み取り専用を共有）＋ ink-walk site は sung 本だけ要求**。着地: **plain1k full 694.2 で厳密一致・sung 本 +0.1〜0.2 MB（0.03%＝同日自前ノイズ規模）・打鍵全冊不変・sweep 0/569＝出力バイト同一**。⇒ ⒦ の忠実度は**対照 0 コスト**で立った。

**続き便 7（委任「有利なら着手」→着手＝最太頭課金 +0.0732 の対の起票・移植はしない）**（`6c8d6820`・engine 非接触）。着手根拠＝観測者は 1 時間前に帰属し直した点・probe 書式と LP 管路と 0.0732 の算術が全部この文脈に温存。**probe `multi-voice-head-spacing.ly`**（MVH＝LBI の音楽から歌詞を抜いた本・MVQ＝全 4 分の対照・双子は `lysc ly`）**・予測を先に書き全的中**: LP MVH の 3 gap 完全等値 3.002244999134613・**MVQ ≡ MVH 桁一致**（相手が読まない変数だけを変えた強い恒等形）・Lily# wide-head-gap **+0.073200000＝頭幅差 (1.3774−1.3042) ちょうど**・quarter-gap／control-gap 0。**容疑者 1 名を生きた毒で棄却**: `ApplyCrossVoiceColumnSpacing` を止めると**10 網が赤**（毒は届いている）のに**この点は不動**＝cross-voice 床は機構ではない（骨⑸ の作法＝動く証明つきの否定）。次の容疑（点の why に記録）: per-staff の NoteSpacing wish 管路（wish が union 列の ink を課金する疑い・LP は各声自身の note column から）／shortest-playing モデル。台帳 **560→563 点・ss 総和 3.950→4.023**（+0.0732＝歌詞本経由でしか測れていなかった量の可視化・no-bind.skip-gap は lyric 側の証人として相互参照）。**移植は専用便**——単声本（note-to-note 全点）を動かしてはいけない境界を quarter-gap/control-gap が対の内側から pin している。

★ **開始時裏取り**: HEAD `9a543ceb`・未 push 0・未追跡 0/木 0・Windows suite **5713/0/4**・CI＝**前便閉幕 `9a543ceb` の 32363814421 は開始時 in_progress→本便中に success を読んだ**・台帳 562 点・追跡コーパス 569 冊・ss 非ゼロ 115／総和 9.42318212203487・Core 0 警告＝**前便の閉幕数と全一致**。
⒥ 便の終了時: suite **Windows 5713/0/4・Linux（WSL）5713/0/4＝両 OS 完全緑**（**新網 0 は手抜きではない＝観測者は既存の対 4 点そのもの**——rod を外せば width/first-gap が赤くなる）・`e3a190dc` の CI **32365572494 success を本便で読んだ**。
セッション終了時: 未追跡 0（scratch/p226 は git 外）・commit 11 本（`e3a190dc` ⒥ 移植 → `7dc67cc7` リードシート機能 → `6449a827` handoff → `8c13f304` 行頭 bar＋クリアランス → `ec79dc69` handoff → `6db864eb` stanza → `da61e52f` handoff → `3650fd19` ⒦ per-voice edge → `59927dd4` handoff → `ce997d0b` provider メモ化 → 閉幕 handoff）・suite **Windows 5711/0/4・Linux（WSL）5711/0/4＝両 OS 完全緑**（総数 5715＝−2 は退役台帳点）・snapshot 220 中 **計 18 枚動き承認**（⒥ 便 2＋機能便 10＋行頭 bar 便 5＋⒦ 便 1・全部帰属付き）・台帳 **560 点・ss 非ゼロ 111／総和 3.950175326・count 点 107／非ゼロ 2**・追跡コーパス 569 冊・Core 0 警告。**CI は `e3a190dc`〜`59927dd4` の 9 本全部の success を本便で読んだ**（32365572494・32368962646・32369111798・32370832384・32370848636・32371432832・32371474926・32373838106・32373919294）。`ce997d0b`（メモ化）と閉幕 handoff の CI は次便が読む。

★ **この便の値段**:

| 便 | 何が動いたか | 射程 |
|---|---|---|
| ① ⒥ の移植（`e3a190dc`） | ReserveLyricLine の音節間スパン（b>a+1）を rod 化・ReserveLyricWidthByColumn が小節ローカルに ApplyRods・BumpSpanMin/ReserveLyricLine の remark 更新・台帳 3 点更新・snapshot 2 枚承認 | 予測 3 本最終桁着地・sweep 5/569 全帰属・両 OS 完全緑・門の価格付けは 1 リストの内側で自動決着 |
| ② リードシート機能＋repeat ink（`7dc67cc7`・ユーザー指示） | 拍子＝AnyStaffEngravesTime の lead-sheet 条項（意図的乖離・§3）＋grid 行描画＋行頭 wish・歌詞行の barline 型・repeat-start の Single 譲り（LP 実測）・RepeatBoth の break piece 分割 | sweep 16/569 全帰属・snapshot 10 枚承認・台帳 2 点退役（562→560）・両 OS 完全緑 |

- **⑸ ★★★ 次に触るなら＝残債**: ⒞ CoPlaceToCodaWithLabels 鏡像／▶ perf（歌詞打鍵の章はほぼ完了＝55.1 vs 非歌詞 45.3。残り ~10 MB は hyphen／apply／非歌詞 L5/L9 等の小粒）／⒡ 配管 6 site／⒣ removeEmpty/pedal の score 移行検討（別便＝ユーザー指示）／✅ **⒦ 歌詞 sliver 族は第226 続き便 5 で閉じた**（`3650fd19`）／**⒧ 多声列の最太頭課金 +0.0732＝対は開いた**（続き便 7・`6c8d6820`＝`multi-voice.natural.wide-head-gap` +0.0732・quarter/control 0。**次の専用便は機構の名指しから**——cross-voice 床は生きた毒で棄却済み・次の容疑は per-staff NoteSpacing wish 管路か shortest-playing モデル・単声本を動かさない境界は対の内側の 2 control が pin）／小粒: twin の歌詞行・`lines` twin 未輸出・マークの X・chord-row の上帯スカラー・**lead-sheet 音節×縦線の対（新規 2026-08-20・ユーザー目視起票。before/after 同一＝既存 regime。LP twin で点を開くのが先）**・**lead-sheet の mid-piece `time` 変更の表示（新規 2026-08-20＝§3 の決定の残。行 voice に change item が無く幅もインクも出ない——表示するなら row 経路に change item を通すところから）**。Marketplace は PAT 待ちのまま（第220 ①）。

> ## ★★★ 骨 1＝**「予告された難所」は、共有点の内側に実装を置いたら消えた**
> 前便は「門は per-spring min しか読まないので gate 側の価格付けが本丸＝専用便の設計問題」と
> 書いた（cross-bar rod＝第223 が実際そうだったから）。だが in-measure の rod は**小節自身の
> chain に焼き込める**：ApplyRods の効果はスパンされた spring に局所＝system で与えるのと同値、
> 門の Σmin は**焼き込んだ瞬間に per-measure solve そのもの**になり、設計すべき「門側の価格付け」
> は存在しなかった。cross-bar が専用設計を要したのは cross-measure＝鍵の圏を跨ぐから。
> ⇒ **難所の見積もりは前例の「形」ではなく「量の局所性（どの鍵の圏に収まるか）」で立てる**——
> 同じ rod でも、小節局所なら共有 1 リストの内側で終わる。

> ## ★★ 骨 2＝**ユーザーの目は A/B の第 3 の計器——「重なっている」には before/after の 2 枚で答える**
> lead-sheet の音節×縦線の指摘に、同じ本の before/after を並べたら**両方同じ絵**＝この便の
> 退行ではなく既存の未測定 regime だと 1 枚で切れた。⇒ **目視指摘の triage は「直す」でも
> 「否定する」でもなく、まず変更前の絵を同じ手順で出す**（stash 15 秒）。両側で同じなら
> §5.0 のとおり対の起票が先で、修理はその点が導く。**同じ目が続き便で本物の欠陥を 2 つ出した**
> （repeat-start の太さ＝LP 実測で確定・`:|:` の break 割れ＝コード読みで確定）——
> 目視指摘は「絵の好み」ではなく**計器の 1 系統**として毎回 LP かコードで裏取りする。

> ## ★★ 骨 3＝**「X を表示して」の半分は既に表示されていた——機能要望も実査が先**
> 「セクションと拍子を表示・繰り返しも」の 3 指示に対し、実査するとセクション枠は全変種で、
> 繰り返しも music 経路の行では既に出ていた。**本当に無かったのは拍子と「歌詞行だけが
> barline 型を落とす」欠陥**で、後者は要望の言葉には無かった（probe を変種ごとに 1 冊
> 描いたから出た）。⇒ **要望を仕様に写す前に、現状を変種の直積で 1 枚ずつ描く**——
> 作るものが半分になり、隠れていた欠陥（真の作業）が名指しで出る。

> ## ★ 骨 4＝**decided divergence は台帳から*退役*させ、決定は §3 に置く**（歌詞行バンドの前例の 2 例目）
> 拍子の表示は LP 実測（CO/CO3 恒等）を**ユーザー決定で覆す**ので、その恒等を pin していた
> 2 点は残しても永遠に赤いだけ——**残差に「決定」という why を書くのではなく、点ごと退役**
> （probe 側に退役の理由と LP 数値の在処をコメントで残す）。台帳の ss 総和は
> 「LP に向かう残債」の計器であり、**向かわないと決めた量を混ぜると計器が壊れる**。

---

## 以下は第225セッションの経緯


最終更新 第225セッション＝**残債の小粒筆頭「多声小節の byItem 予約写像」を §5.0 の型どおり専用便で閉じた**——**対の起票**（probe `lyric-bound-voice-mapping.ly`・3 冊・`04b40fb1`＝engine 非接触）→ **移植**（byItem 分岐退役・`c3b0b32f`）→ **対の食い違いが第2・第3の欠陥を名指した**。対の設計＝**LP 恒等対**：1 小節・primary 4 分×4（列 0..3）＋bound 声部 2 分+4 分×2（item 0 1 2＝列 0 2 3）・同語反復で顔が相殺（音節 ink 中心の step）。**LP は「rod は音節間・どの列が運んでいても」なので LBI（bound）と LBIP（primary）の全 binding gap が 1 数 D＝ink+0.45＝10.010125984251968**——2.26.0 実測で桁まで的中。**Lily# は LBI を両向きに fork**（skip-gap +10.0467＝両予約が 1 列左に堆積＝2D+0.0366／step-gap −7.0079＝必要な対に予約ゼロ＝自然 3.002245 のまま **ink 9.56 が 6.56 重なる**＝named-voice-lyrics の 'deep'-on-'slow' の実寸版）。**移植＝`ApplyLyricSpacing` と `InkReachPerColumn` の byItem 分岐を退役し、全予約を TIMING 列へ**（描画・cross-bar rod edge と同じ 1 写像・3 消費者）。**step-gap 0.000000000・重なり解消**。**skip-gap は +1.507844999 に着地（予測 +0.0366 は外れ＝収穫）**：残差が**名前付き 2 項に桁まで分解**＝⑴ **+1.471245＝スパン予約の bump モデル**——`BumpSpanMin` は have を spring **min** で数えるが ragged の行は **ideal** で立つので、**中間列を跨ぐ予約は非最終 spring の Σ(ideal−min) だけ過開**（LP の spanning rod は max(natural, need)。修理は cross-bar rod と同じ形＝ApplyRods の真 rod 化。**ただしこの regime は単声の melisma スパン全部を含み未測定→対から**）⑵ **+0.0366＝union edge sliver**（下記）。**sweep A/B 3/569・全冊同族で帰属済み**：named-voice-lyrics（snapshot 220 中この 1 枚だけ承認・sung 小節が LP 向きに 3.45 締・**承認前チェックの音節重なり目視 0**＝第223 骨 2 の作法）＋ **lymelmel／perf-lyrmel1k＝行歌詞（IsLyricsRow）も同じ欠陥を共有していた**——**行の Timing は均等スロット・ItemIndex はスロット番号**（`LyricsCollector.PlaceRun`）なので旧予約は 'ho'（slot 3・描画は t=3/4＝col6）を col3 に置き、**16 分群を 2.96 ss 無駄に開いていた**。予約＝描画列は行でも一致した。台帳 **553→558 点**（+5）：step-gap／no-bind.step-gap＝exact・**開いたままの 3 点が第2欠陥の観測者**＝`primary-control` **+0.0366**（**予約の alignment-edge 表（`ParentAlignmentEdgesPerColumn`）は全声 union・描画と LP は自声の頭**——LP 実測: LBIP の音節は 4 分の 0.6521・LBI/LBIC は自声 2 分の 0.6887 に中心）・`no-bind.skip-gap` **+0.1098＝0.0732+0.0366**（**Lily# の自然 spring は列の最太頭を課金・LP の ideal は外声の太頭に盲目**＝実測 LP 全 gap 3.002245 等値）・skip-gap **+1.5078**（↑の 2 項和）。**ss 総和 3.877→5.531 は悪化ではなく「測っていなかった量の可視化」**（README の前例と同じ）——+1.654 は全部この便が開いた名前付き残差。
> ⚠️ **環境の罠 2 つを踏んで名指した（RULES §5.5 に収載済み）**：⑴ **`LilySharp.slnx` の既定ビルドは Debug**——§0 の solution build は `LilySharp.Cli\bin\Release` を**一度も**更新せず、`lysc.exe` は任意に stale（**A/B に使う前に `dotnet build LilySharp.Cli -c Release` を明示**。この便の CLI A/B は「同一バイナリ同士の比較」で 2 度「差なし」と嘘をつき、**sweep（dotnet run が Core を自前ビルド）だけが真実を言い続けた**）⑵ **pwsh コンソールで repo DLL を `Assembly.LoadFrom` するとロックが残り、以後のビルドが静かに stale コピーを残す**（コンソール再起動＋bin 削除で解放）。

**続き便（同セッション・委任「有利なら着手」→着手＝範囲は ⒥ の対起票まで・移植はしない）**＝**melisma スパンの対を開いた**（probe `lyric-melisma-span.ly`・LMS/LMN・`558920fd`＝engine 非接触）。着手の理由＝span-bump の算術（ideal 3.002245／min 1.604200＝頭 1.3042+0.3）はこの便の実測で手元にあり、probe 骨格・測定管路も温かい。**移植を断った理由＝in-measure の range rod は「門×レイアウト」の設計問題**（門は per-spring min しか読まない・第223 の cross-bar rod が専用便を要したのと同型。ただし in-measure 量は**小節局所**なので cross-measure 鍵は不要＝cross-bar より易しいはず）。**予測は probe ヘッダに先に書き、LP 実測で全部最終桁まで着地**：LP width＝max(natural, need)＝**12.294350393700784**（dump で両 ink がちょうど **0.450000** 離れる）・**first-gap＝width/3 を最終桁まで**（等 spring への均等分配＝**range rod の分配則そのものを実測**・移植の設計入力）・Lily# width **+2.796089998269224＝2×(ideal−min) ちょうど**（**need 算術に sliver ゼロ**＝melisma 左揃え・ink・0.45 とも両エンジン同一→**この点は純粋な bump モデル**）・first-gap **−1.095872**＝中間 spring は自然のまま（no-bind と同桁＝「歌詞が居ないかのよう」）・LMN 両点 **0.000000000**。台帳 **558→562 点**（+4）。**⒥ は「対はもう開いた」状態**——次の専用便は移植そのものから始められる。

★ **開始時裏取り**: HEAD `118bf6f5`・未 push 0・未追跡 0/木 0・Windows suite **5704/0/4**・CI＝**前便閉幕 `118bf6f5` の 32357553370 success を本便で読んだ**・台帳 553 点・追跡コーパス 569 冊・ss 非ゼロ 110／総和 3.876975326・Core 0 警告＝**前便の閉幕数と全一致**。
終了時 **未追跡 0・木 0**（probe-out は git 外）・commit 5 本（`04b40fb1` 対 → `c3b0b32f` 移植 → `d50fd55d` handoff → `558920fd` melisma 対 → 閉幕 handoff）・suite **Windows 5713/0/4・Linux（この機械の WSL）5713/0/4＝両 OS 完全緑**（+9＝新台帳点 5+4 ちょうど）・snapshot **220 枚中 1 枚だけ動き承認**（named-voice-lyrics）・台帳 **562 点・ss 非ゼロ 115／総和 9.42318212203487**（**前便比 +5.55 は悪化ではなく可視化**——全部この便が開いた名前付き残差：byItem 族 1.654（skip-gap 1.508＋sliver 0.0366＋0.1098）＋melisma スパン族 3.892（width 2.796＋first-gap 1.096）。closed 側は step-gap 等 4 点が exact）・追跡コーパス 569 冊・Core 0 警告。**push 済み 4 commit の CI success を全部同便で読んだ**（`04b40fb1`＝32359470723・`c3b0b32f`＝32361420921・`d50fd55d`＝32362016256・`558920fd`＝32363279717）。閉幕 handoff 自身の CI は次便が読む（docs のみ・毎便同じ形）。

★ **この便の値段**:

| 便 | 何が動いたか | 射程 |
|---|---|---|
| ① 対の起票（`04b40fb1`） | probe `lyric-bound-voice-mapping.ly`（LBI/LBIP/LBIC・予測を先に書き 2.26.0 で実測）＋`LpGeometryProbes` 3 冊 5 点＋`RenderedGeometry.SyllableInkCentre(i)` | LP 恒等対が的中（LBI==LBIP＝D 1 数）・byItem の両バレルを数値化（+10.05／−7.01）・engine 非接触 |
| ② 移植（`c3b0b32f`） | `ApplyLyricSpacing`／`InkReachPerColumn` の byItem 分岐退役＝全予約 TIMING 列（描画・rod edge と 1 写像） | step-gap **0.000000000**・重なり解消・**sweep 3/569 全帰属**（行歌詞も同欠陥だった）・snapshot 1/220・両 OS 完全緑・残 2 項を台帳に名前付きで記録 |
| ③ melisma スパンの対（`558920fd`） | probe `lyric-melisma-span.ly`（LMS/LMN・予測を probe ヘッダに先書き）＋4 点。移植はせず（門×レイアウトの設計＝専用便へ） | 予測全的中：LP width＝need（ink 間ちょうど 0.45）・first-gap＝width/3 最終桁・Lily# **+2.796089998269224＝2×(ideal−min) ちょうど**（need sliver ゼロ＝純粋な bump モデル）・LMN 2 点 exact |

- **⑸ ★★★ 次に触るなら＝残債**: ⒞ CoPlaceToCodaWithLabels 鏡像／▶ perf（歌詞打鍵の章はほぼ完了＝55.1 vs 非歌詞 45.3。残り ~10 MB は hyphen／apply／非歌詞 L5/L9 等の小粒）／⒡ 配管 6 site／⒣ removeEmpty/pedal の score 移行検討（別便＝ユーザー指示）／**新規 2 つ（この便が開いた・どちらも観測者が台帳に居る）**: **⒥ スパン予約の rod 化**（BumpSpanMin の min-have→per-measure solve の真 range rod。**対はもう開いた＝`lyrics.melisma-span.*` 4 点（`558920fd`）——次の専用便は移植そのものから始められる**。着地予測: melisma width +2.796090→0・first-gap −1.095872→0・bound-voice skip-gap 1.5078→0.0366（sliver のみ残る）。設計入力を対が実測済み: **LP の range rod は等 spring に均等分配＝first-gap は width/3 を最終桁まで**。門は per-spring min しか読まないので gate 側の価格付けが本丸——ただし in-measure 量は小節局所＝cross-measure 鍵不要で cross-bar（第223）より易しいはず。隣接対＝全 lyrics.column.* exact 点は max(ideal,need) で両モデル同値＝**動いてはいけない**）・**⒦ 歌詞 sliver 族＝多声列の voice-blind 表**（予約 edge 表の union vs 自声の頭 +0.0366・自然 spring の最太頭課金 +0.0732。`primary-control`/`no-bind.skip-gap` が観測者。**per-voice の alignment extent は 1 列 1 値の表では表現できない**——LyricEngraver 側の per-bar edge cache と MultiStaffLayouter 側で別の measure 集合から同じ関数を呼んでいる疑いも同便で見ること）／小粒: twin の歌詞行・`lines` twin 未輸出・マークの X・chord-row の上帯スカラー。Marketplace は PAT 待ちのまま（第220 ①）。

> ## ★★★ 骨 1＝**LP 恒等対は「相手の不変量」を恒等に使うと最強になる**
> LBI と LBIP は音楽も歌詞の載る声部も違うのに、LP では全 binding gap が同じ 1 数 D になる
> ——「rod は音節間であり、どの列が運んでいるかを LP は読まない」という**性質そのもの**を
> 恒等にしたから。おかげで Lily# の fork（+10.05／−7.01）が**そのまま欠陥の量**になり、
> 帰属は測る前に書けた。⇒ **恒等対を設計するときは「同じ音楽を二通りに書く」だけでなく、
> 「相手が読まない変数だけを変える」を探す**（§5.0 の対の設計の 3 つ目の形）。

> ## ★★ 骨 2＝**予測が外れた 1 本（移植後 +0.0366 のはず→実際 +1.5078）が第3の欠陥を掘った**
> 差 1.471245 は spring col0→col1 の ideal−min（3.075445−1.604200）に桁まで一致し、
> **BumpSpanMin が have を min で数える一方、ragged の行は ideal で立つ**ことを名指した。
> 隣接対（スパン 1 spring）では max(ideal, need) と等価なので**単声の隣接歌詞では永久に見えない**
> ——中間列を跨いだ瞬間だけ課金される。⇒ **「span を跨ぐ min の総和」を根拠にする式は、
> 描画が ideal で立つ regime で必ず過開する**。melisma スパンが同族（未測定・対から）。

> ## ★★ 骨 3＝**A/B の前に「両側のバイナリが違う」ことを確かめる——同一と出た A/B は何の証拠でもない**
> CLI での before/after 比較が 2 度「バイト同一」と出たが、**同一だったのはバイナリのほう**
> （slnx 既定 Debug で Release の lysc が stale＋LoadFrom ロックでコピーが静かに落ちる）。
> sweep（dotnet run が Core を project 参照で自前ビルド）だけが一貫して「3 冊動いた」と言い、
> 最後に DLL ハッシュを取って嘘が割れた。⇒ **A/B が「差なし」と言ったら、まず両側の
> DLL ハッシュを比べる**（§5.5 の旧 Core 罠の一般形＝「同じ答えを 2 度出す計器は、
> 測っていない計器かもしれない」）。

> ## ★ 骨 4＝**control は 0 でなくても仕事をする——非 0 の control は残差算術の名前付き項になる**
> primary-control（+0.0366）と no-bind.skip-gap（+0.1098＝0.0732+0.0366）は exact に
> ならなかったが、その値が skip-gap の残差 1.5078＝1.4712+0.0366 を桁まで分解する部品に
> なった。⇒ **「control が緑になるまで対を作り直す」のではなく、非 0 の control には
> 名前を付けて台帳に置く**——次の残差がその項で割れる。

> ## ★ 骨 5＝**1 つの量に消費者が 3 軒揃った瞬間、残っていた第 4 の綴りが sweep に出た**
> 予約を TIMING 写像に揃えたら、sweep が行歌詞の 2 冊を出した——**行の ItemIndex は
> スロット番号で、音符列とは何の関係も無かった**（描画だけが正しい列に居た）。
> §5.2.1②「同じ量を計算する場所が 2 つ」は**写像にも当てはまる**：写像を統一する便は、
> 統一そのものが残りの別綴りを掃き出す計器になる。

---

## 2. 開いている作業

> # ▶ **打鍵のアロケーション＝「置いて捨てる」「同じ物を 2 度作る」の返済。地図は全部埋まった**
> （2026-08-17・第190 起票／**第191 が 6 段・第192 が 4 段・第193 が 1 段返済**＝
> **`perf-fingstack1k` 3304.5 → 165.0 MB（−95.0%）・`perf-plain1k` 95.9 → 45.2 MB（−52.9%）**。
> **次の一手と現在の地図は §1 ⑷ の表**）
>
> ★★★ **⚠️ ユーザー決定（2026-08-17）＝優先度を上げる。** 起票時は「射程が実ファイル 0 冊なので
> いま着手する理由は無い」と §2G に置いたが、**ユーザーがこの項の優先度を上げた**ので §2 の頭へ移した。
> **測定は変わっていない。変わったのは判断で、それはユーザーのもの**（§5.0「測定と判断を混ぜない」）。
> ⚠️ ★★ **その決定は articulation の島についてのもので、その島は第191 で閉じた。**
> **第192 が返した 3 段は articulation と無関係**（梁の群・梁の検出・content key）で、
> **どれも本の形によらず届く**——**だから下の ⑷「射程 0 冊」は第191 までの話**。
>
> ⚠️⚠️ **この項の数字は net10 の Release 実測**。**他の ▶ 項の ms と並べてはいけない**
> （RULES §5.5＝▶ の打鍵の順位は net9 の実測なので、順位を根拠に決める前に取り直せ）。
>
> **⑴ 残っている島は 3 つ**（MB は `perf-plain1k` の 1 打鍵 45.2。**内訳は §1 ⑷ の表**——
> **端から端まで region で読んであり、未帰属はゼロ**）:
> **⒜ `CollectMeasures` 2.68 MB＝再開機構そのもの**（歩きではない。生の `ProcessMusicNode` は
> **9 回**）。⚠️⚠️ **首位の 1.11 は「節の site を約 8000 件リストに materialise してから 9 件だけ歩く」**で、
> **prefix を*索引で*当て suffix を継ぐという再開の設計がそのリストを要求している**。
> **streaming にするなら checkpoint の番地の付け方を変えることになる＝増分機構で最も安全性が要る所。
> 専用の便と falsifier から。**／
> **⒝ 注釈の 2 パス（`L5-prelim` 3.28 ＋ `L9-annotations` 2.73＝6.01 MB）**
> ——⚠️ **ここは「もう無駄ではない」側**で、**返ってくる配置そのもの**。
> **scratch バッファで削れるが、それは「答えを作る費用」を削る話**なので、
> **始める前に何を主張できるかを決めること**（§5.0）。／
> **⒞ `2a-contentKeys` 1.10 MB＝まだ二分していない**（第193 が首位の 0.93 を落とした残り。
> **次に触るなら region を 1 段深く置くところから**）。
> ✅ **`BoundaryClefAllowance` 0.93 MB は第193 で閉じた**（`ee3f8896`）——**列を建てずに厳密な 0 を
> 返すようにした**。⚠️ **「2 人目の呼び手 `CreateSpringsForMeasure`」も同時に直っている**
> （直しは共有関数の中）が、**そもそも打鍵ではその 2 軒は 0 MB だった**（`2b-springs` の memo）。
> ⚠️ **「`3-layout` の未帰属 約 2 MB」という島は*存在しない*。** 第192 が引き算で出して
> **同じ便のうちに region で測って消した**——梁の検出を `L2-firstSystem` と二重に数えていた分だった。
> **引き算の残りを島として引き継がないこと。**
>
> **⑵ もう見なくてよい所**（測って閉じた・**もう一度開けないこと**）:
> **`L1-break` の DP 19.3 MB（40%）＝本質的**（列を切る案・帯状化の案とも実測で退行・ARCHIVE §1 第191）／
> **`4-render` 8.9 MB は床に近い**（構造上の下限 約 7.5＝文書 3.76 ＋ string 化 3.76）／
> **`CreatePages` 1.0**・**`AugmentSkylinesWithScripts`**・**`LayoutSystems`／`looseChain`／
> `LayoutAllSpanners` は 0.0〜0.4 MB**・**prelim の beams/ties/slurs は memo が効いている**。
>
> **⑶ 着手前に読む警告**（どれも生きている）:
> ⚠️⚠️ ★★★ **「置くのをやめる」形の一手は丸めが動く**——**`Distance` を*置いた 2 本*ではなく
> *profile 2 本＋相対オフセット*で計算すれば置く費用は消える**が、**平行移動が比較の中に入るので
> 第191 第4便と同じ「順序の決定」**（ARCHIVE §1 第191 の骨 ⑹）。**着手するならユーザー決定から。**
> ★ **`VerticalSkyline.MergeInternal` は今も merge ごとに全長の合成リストを作り、全体を sort する**
> ＝**1 つの system の script 数について二次のまま**。**器が全 merge なので射程は広く、危険も広い。**
> ⚠️⚠️ **`List.Sort` は不安定**なので、**同じ `Start` を持つ building の並び順に現在の出力が依存している。**
> **アルゴリズムを触る前にそこを測ること。**
> ⚠️⚠️⚠️ **`PagingAugmentProgram` の class remark は「バッチ化するな」と言っている**
> （第141 で 4,878 ULP ずれた）。**それは merge の*再結合*についての禁止**で、
> **第191 が外した「merge と merge のあいだの複製」は含まない**——**札が禁じている操作と
> 自分の操作が同じかを 1 行で書いてから進むこと**（RULES §5.1）。
> ⚠️ **`AugmentSkylinesWithScripts` に memo は入れていない**（遅延で足りたため）。
> **歌詞つきで script の多い本が出たら memo が次の一手**——⚠️ **`_pagingAugments` は system index
> だけで引く 1 枠なので、共用すると枠を奪い合って毎回 miss する。別の辞書が要る**
> （第192 第2便が同じ罠を*リストで*避けた形が前例）。
>
> **⑷ 射程＝articulation の島についての恒久の制約**（第190 実測・**第191 までの 4 段に掛かる**）:
> ディスク上の **1026 冊**を collect して数えると **961 冊が articulation ゼロ・58 冊が 1〜49 個・
> 50〜499 個は 0 冊・500 個以上は 7 冊で全部 `audit/lpreg` の合成 perf 本**
> （**`scratch\ベースタブLy` のユーザー自身の 313 冊は 0**・`Fixtures` 219 冊の最悪が 25）。
> ⇒ ⚠️⚠️ ★★★ **だから articulation の段について「実ファイルが速くなった」とは書けない**
> ——書けるのは「**script あたりの定数が N 倍下がった**」まで（§5.0「何を主張できるかを測る前に決める」）。
> ⚠️ **第191 第5便（ページ経路）・第192 の 3 段はこの制約の外**＝**実ファイルに届く。**
>
> ★★ **計器は repo に在る**＝**`audit/LilySharp.Probe`**（第190 で solution に入れた・**6 番目の project**。
> **`alloc` は第191 が足した**）:
> ```
> dotnet run --project audit/LilySharp.Probe -c Release -- census [top-n]
> dotnet run --project audit/LilySharp.Probe -c Release -- sweep <label> [listfile]
> dotnet run --project audit/LilySharp.Probe -c Release -- cmp <a> <b> [book...]
> dotnet run --project audit/LilySharp.Probe -c Release -- alloc <book>... [control]
> ```
> **`alloc`**＝**フルレンダ 1 回と打鍵 1 回のアロケーション**（`audit/lpreg` の本を名前で。
> **どの編集アンカーを引いたかを印字する**——別のアンカーの 2 回は比較できない）。
> ⚠️ **打鍵の*時間*は入れない**（`EditKeystrokeBench` が持つ＝同じ量の 2 軒目を作らない）。
> ⚠️ **対照の本を同じ run に必ず入れる**。**ただし「その変更がその本に届き得るか」を先に言う**
> ——**届き得る本は対照ではない**（§1 の骨 ⑶）。
> **`census`**＝全 `.lys` を collect して本ごとに数える（約 17 秒。**モデルから数える**——
> `@staccato` を grep すると*綴り*を数えることになり、10 回参照される phrase は engine には
> 10 回効いて grep には 1 回しか見えない）。
> **`sweep` / `cmp`**＝毒→全数 SVG ハッシュ→差分（fixture 219 冊で 1 サイクル約 20 秒。
> `listfile` に `git ls-files "*.lys"` を渡せば 566 冊へ広がる）。出力は
> `audit/probe-out/`（git 管理外＝1 回の測定であってリポジトリの状態ではない）。
> ⚠️ **`cmp` は共通集合が空なら exit 1 で「1 冊も比べていない」と言う**（「0 冊動いた」と
> 区別が付かないので）。**毒が 0 冊なら「fixture が盲目」ではなく「毒が engine に届いていない」。**
> ⚠️⚠️ ★★★ **`sweep` は 1 冊を `SvgGenerator.Generate` で*フルレンダ*する**——**増分の経路
> （`IncrementalCompiler`）を 1 度も通らない**。**だから増分側だけの量**——`MeasureContentKey`・
> 各 memo の鍵・resume の窓——**については、掃きの 0 冊は何も言っていない**（第192 実測：
> content key の折る数を変える毒で **0/566**）。**その族の計器は `IncrementalCompilerTests` の
> incremental==full の一族**（鍵を盲にする毒で 22 本赤）。
> ⇒ ★★ **0 冊を証拠として引く前に、毒を 1 つ当てて「届く」ことを見せる**（第192 の骨 ⑴）。
> ⚠️ **`alloc` の床は本によって決定的でない**（`perf-v2bow1k` は run 間で約 4% 揺れ、
> `perf-plain1k` は厳密）。**詳細と読み方は `Alloc` の remarks。**
> ⚠️ **歌詞本の打鍵は第221 で +10%**（perf-lyrplain1k 368.0→405.2 MB・full +21%。plain1k/fingstack は桁まで不動）＝
> **帯 profile の毎打鍵構築**（`LyricReservationBelowSystem`）。**memo するなら「その鍵は歌詞の内容を
> 含むか」を先に確かめる**——含まない鍵に畳むと sweep に見えない増分穴（第192 の族）。
> ★ **第222 の列移植で一部返った**: perf-lyrplain1k **打鍵 405.2→372.5 MB（−8%）**・full 741.9・
> plain1k 対照 694.8/45.3＝桁不動（列予約の縮小と LINE 単位の grouping の正味）。
> ★★ **第224 が rod 後の +64.2 を帰属して返した**（詳細は §1・`a7e6c0d0`）: 打鍵 436.7 の地図＝
> ann.lyrics 222.0（verse skylines 110.6＋chain 走査 ~107）・L3.lyricBand 165.2・非歌詞 ~20。
> **帰属は 100% 本の形**（rod で sung 行 5→約 6 小節・形強制実験で 0.01 MB 一致）、正体は
> **List の倍化 1024 段跨ぎ**——`VerticalSkyline.ReserveForBatch`（数えてから 1 回確保）で退役し
> **打鍵 347.3 MB＝rod 前 372.5 をも下回る**（sweep 0/569・対照 plain1k 45.3 桁不動）。
> ⚠️ **他の batch 地点で ±1000 建物級のバッチを見たら同じ段を疑う**（remark に実測ごと記載）。
> ⇒ ★★★ **帯（`1b42fd74`）・verse skylines（`2f02a5cd`）・chain prefix（`d0a1e489`）が第224 で
> 全部 memo に載り、歌詞打鍵の章は閉幕**＝perf-lyrplain1k 打鍵 **436.7 → 55.1 MB（非歌詞対照 45.3
> の +22%）**。残り ~10 MB は hyphen engraver／apply・solve／非歌詞 L5/L9 の小粒のみ。
> ⚠️ chain memo の 2 境界（詳細は `LyricChainMemo` remark と §1 第4便）: **anchor テーブル系スカラーは
> 値に入れない**（live で ResolveAnchor）・**store は per-pass**（seed がパスの anchor profile を読む
> ——共有第1稿は incremental==full 網が数分で赤にした）。
> ★ **memo 族の道具箱（この島で確立・他所でも使える）**: MeasureLayout 参照同一鍵（FingScriptMemo
> 条項）／hit を主張する net／X-only 値なら store 共有・そうでなければ per-pass／cache 境界の入力目録は
> 既存 memo の監査を兼ねる（font 番人 `5af72975` はその獲物）。
> ⚠️ **font plan はどの content/global 鍵にも入っていない**——session の番人（`IncrementalCompiler.Compile`
> 冒頭・pin FontEditIncrementalTests）が変化で cache を全部落とすので、**新しい memo は fonts を
> この番人の下で圏内と数えてよい**（第224 続き便が実顔で欠陥を実測してから建てた番人）。
> ⚠️ **ビルド費用は測ってある**: solution の full が **3.81〜4.01 秒**（5 project のとき 3.63〜4.03）・
> **noop は 0.90 秒で不変**＝実質ゼロ。**打鍵の*時間*はここに入れていない**——
> `EditKeystrokeBench` / `PreviewUpdateBench` が既に持っており、**同じ量の 3 軒目**を作らないため。

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

- ★★ **`condensedStaff` の 2 パートは、モデルの上で見分けられない**（2026-08-17・第193セッション・
  **測って名指しただけ・未修理**・**着手前にユーザー判断は不要だが、モデル変更なので専用の便**）。
  - **現状**: `condensedStaff` は**パートごとに 1 binding を出しつつ譜は 1 枚**なので
    （`RenderSpec.GetVoiceBindings` の `SharesStaffWithPrevious` →
    `MeasureCollector:_currentStaffIndex = sharesStaff ? … - 1 : …++`）、
    **どのパートも `(StaffIndex, VoiceIndex) = (同じ, 0)`**。
    ⇒ **第193 が入れた `TupletBracketItem.AddressedTo` は、この 2 つを切れない**——
    **各パートの梁検出が相方の tuplet bracket を受け取る。**
  - **実測**（566 冊・guard の計数）: **通した 2882 本・落とした 2 本**で、
    **2 本とも `audit/lpreg/pctend-probe.lys`**。⚠️ **捕まえたのは `BuildTupletSpans` の
    範囲外 guard であって scoping ではない**——**相方の小節が `R1` 1 個だったから範囲外になっただけ。**
    **小節が長い condensed パートなら*範囲内*で黙って衝突する。**
  - **射程**: ⚠️ **今のコーパスで観測できる本は 0 冊**（`pctend-probe` は guard に救われている）。
    **だから掃きは計器にならない**——**着手するなら「範囲内で衝突する condensed 本」を
    1 冊書いて、判別する綴りを機械に探させるところから**（第193 は 9 形中 1 形だった）。
  - **終点の形**: **bracket に「どのストリームか」の判別子**——
    ⑴ `VoiceIndex` を condensed パートにも配る（**声部の意味が 2 つになる**ので要注意）／
    ⑵ 別の `StreamIndex` を足す／⑶ scoping の鍵を「collect した binding の番号」にする。
    ⚠️ **どれもモデル変更で、双子 exporter・MusicXML・台帳の点が読む可能性がある。**
  - ⚠️⚠️ **同じ軸を他の量も踏んでいないか一緒に見ること**——**`_currentStaffIndex` で切っている
    per-staff の表は tuplet だけではない**（dynamics・articulation・cross-staff…）。
    ★ **軸そのものは痩せていない**: **`condensedStaff`／`combinedStaff` を書く本は 29 冊**
    （実測・`git ls-files "*.lys"` を `combinedStaff|condensedStaff` で grep。ほぼ `audit/lpreg`
    の part-combine 群）。**tuplet が乗っているのがその中の 1 冊だっただけ。**
    ⇒ ★★ **だから「condensed は珍しいから後回し」は成り立たない**——
    **珍しいのは*その譜に tuplet を書いた本*で、他の per-staff の量は 29 冊ぜんぶを通っている。**
    ⚠️ **起票時に「1 冊しか無い」と書きかけて、数えたら 29 だった**（§0 ★「数を引き継ぐときは
    数え方も書く」の当日版）。

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
- **`LayoutEngine` の単一ページ経路が今も自前で積む**（二重実装）。⚠️ **「force 0 なので鎖と一致する」は嘘だった**——帯の床を `SysHeight`（trailing 行の描画帯を含む）から測っていて、**行を挟む本で帯を二重計上**（第218 実測: rowgap probe 19.836 vs LP 12.000・Twinkle 23.500 vs 12.225）。**frame は `b232d979` で直した**（帯の項はアンカー譜の外側線から＝PageLayouter の `HalfLast` と同型）。**二重実装そのものは残っている。**
- ✅ **歌詞帯のスカラー床は X 盲目 — 閉じた**（第221・`ef838197`。起票 `c207b25c`＋対の修理 `5c3df019`）。
  帯の最小 profile が paging skyline の要素になり（`LyricReservationBelowSystem` が profile を返し
  `PagingAugmentProgram.Builder.AddLyricBand` が最終 family として merge・鍵は building の数値＝augment memo と両立）、
  床は X で読む。スカラー床は CreatePages / PageLayouter の両経路から消え、extents は profile の最深点。
  台帳 `lyrics.band-floor.*`（fork の 2.272129 を LP 実測・ink-past-band **0.000000000 ちょうど**）・
  射程 12/569 冊（全部歌詞本・LP 側へ縮む向き）・snapshot 1 枚（余白のみ）。
  **露出して残った島**: row/sings の音節 X ドリフト・j-dot（→§1 第221 ⓐⓑ）・**上側の chord-row 帯は
  今もスカラー**（`EstimateAboveStaffExtents` の bandUp・同じ形の島の上側・対から起票）。
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

> ## ▶ **⒣ `removeEmpty`・`pedal` も「版面ものが part ヘッダに居る」同族＝別便で検討**（第217 起票・**ユーザー指示「別便で検討して」**）
> `lines` を score 側 `as lines N` へ移した決定（§3 第217）の同族が 2 つ残る: **removeEmpty**
> （LP では RemoveEmptyStaves＝context mod）と **pedal**（描画スタイル＝presentation）。
> どちらも part 持ちだと「総譜では隠す・パート譜では隠さない」等の score ごとの使い分けが綴れない。
> 移すなら **`as` 修飾の複数連結**（`staff m as lines 1 as removeEmpty …` か 1 つの `as` に列挙か）の
> 設計から——**判断だけで閉じる型ではなく設計資産が要る**（第215 骨 1 の区別）。着手はユーザー決定から。

> ## ✅ **⒢ ペダル・強弱 vs 歌詞の優先順位スタック＝⒜⒝ とも第220 で閉じた**（起票第215・再測第216。`2afd1b50`→`14b2ffcd`→`69ba0d20` の 3 commit）
> **台帳に 4 点起票してから移植した**（audit/lp-geometry `lyrics.{pedal-bracket,pedal-text,dynamic}.staff-to-lyric`＋対照。
> LP は **2.26.0 のローカル実機**で取り直し済み＝第216 の宿題どおり。probe は `probes/pedal-lyric-stack.ly`）:
> **⒝ `@f`**＝`-1` family（最下譜・単譜の歌詞塊）が**システムシルエット**を読んでいて、per-staff Down に
> 既在だった dynamics が見えなかった → **anchor 譜自身の Down を読む形に**（`LyricEngraver.LastSpaceableStaffOf`
> ＝系ごと・hara-kiri 対応。譜なしシートはシルエットに fallback）。−1.668349 → **+0.024651**。
> **⒜ 括弧**＝`PedalEngraver` が score 全体で 1 本の Y（systems[0] の底）を使い、どのスカイラインにも
> ペダル ink が無かった → **族ごと・系ごとに 1 本**（LP の SustainPedalLineSpanner＝padding 1.2、pointwise、
> フックは全高・線中央は半線幅）を **skyline 構築時に解いて seed**（`SolveAndSeed`）、解は
> `StaffSkylineSet.PedalLines` で draw に渡る（1 計算 2 読者）。PLB −1.800155 → **−0.000155**（対照と同値の書体スライバ）。
> LP 実測 5.295 ＝ 支え 3.045 + 1.2 + フック 1.05 と桁一致。snapshot 4 枚だけ動き再承認（pedal-below-lyrics は
> 「別譜のペダルは落ちない」を保ったまま自譜に寄った）。射程: sweep 569 冊中、⒝ で 2 冊（歌詞床が締まる向き）＋⒜ で snapshot 4 冊のみ。
> **残債（この島の続き）**: ⑴ ✅ **PLT＝text スタイルは同便の続きで閉じた**（`3e21dfa9`・語ごと 1 スパナ・
> `LyricClearance` の pedal 免除は**残す**＝LP も「ペダルは譜側・歌詞が下がる」）／
> ⑵ ✅ **dynamic の +0.024651 は同便の続きで閉じた**（音節プロファイル＝字ごと実輪郭＋LyricText 自前の
> skyline-horizontal-padding 0.1。箱 +0.0247／素輪郭 −0.0313／pad 輪郭 **−0.0003** の三角測量が機構の証明。
> 11 snapshot 再承認・射程 20 冊＝全て歌詞持ち）。⚠️ **代わりに 2 つ開いた**: ⓐ LYRBV の内側 gap が
> +0.0018→**−0.0066**（台帳 OPEN・どの項かは dynamic 点と同じ pointwise dump で切る）／
> ⓑ **打鍵 alloc**: perf-lyrplain1k 71.7→**367.9 MB**（素の輪郭化は 1235——バッチ化 508・resolved 直 merge 46/pass・
> colinear 連結 368 まで返済、連結は sweep 0/569 で無損失を機械確認。バッチ化だけなら旧箱より速い 55.2）。
> **残り 5 倍は ▶ perf 島の債**: walk 99 MB/pass・build 46×2・残 ~130 未帰属。梃子候補=行 profile の
> per-system memo／ShiftedRaised 1-alloc 化。ユーザー実本規模（〜50 音節）では +2〜3 MB/打鍵。／
> ⑶ ✅ **系をまたぐ括弧 × 増分は同便で毒→修理**（`620cc81c` 後続 commit）: 毒は実在した——Off を消す編集で
> On 側 system の cache が死んだ括弧の ink を保持（増分ページ高 793.7 vs full 782.8）。修理は Volta と同形＝
> **検出済みスパンを両 overload の鍵に BucketSpan**（印の純関数なので再導出）。毒はテストとして常設
> （`DeletingAPedalRelease_RedrawsTheSystemsTheBracketSpanned`）・full render は 0/569 不動。

> ## ✅ **`with lyrics`/`with chords` の除去＝「score は帯の縦列」は第216 で完成した**（`6d6d1b92`→`5d7a8ef1` の 4 commit。**起票・決定は第215**）
> **残る作業 4 つを全部閉じた**: ⑴ bound 行は譜の直後で fold（byte 恒等を機械証明してから構文を除去）
> ⑵ グループ本体が `lyrics` 行を取る（LYS6011/6012）⑶ ハラキリはピンで固定（fold の帰結として既に正しい）
> ⑷ 無名 `chords {}` は LYS0032（ユーザー決定＝畳む）。**除去は LYS0031**・LYS6009/6010 退役。
> **chords 行は regime で家が割れる**（先頭行＝loose-chain 移植のまま／譜間＝attached engraver へ fold）
> ——理由と実測は §1 第216 の骨 2。**snapshot は chords 3 枚のみ・lyrics 全数不変・台帳不動。**

> ## ✅ **「同名のシステムフォントが同梱フェイスを隠す」は第214 で閉じた**（`220e03bb`。**起票は第213**）
> **`BuildFontNameCompletions` が同梱同名のインストール行を畳む**——問い口は
> **`TextFontMetrics.IsBundledFamilyName`＝`TryBundledFamily` の公開ドア**（「is this face
> available?」の 4 人目の読み手も同じ家を読む）。**網は合成リスト**
> （`FontNameCompletionTests.BuildHelper_ExcludesInstalledFamiliesTheBundleShadows`）**なので
> TeX Gyre の無い機械でも赤が見える**。**この機械の WSL は 5593/2 → 5596/0＝完全緑。**
> ⚠️ **「システム側を別の段に見せる」案は採らなかった**（第214 の自律判断・§1 ⑵）——
> **エンジンは同名を常に同梱で解決する（`BundledPathForName` が先）ので、システム行は
> *選べても使われない***。**ユーザーが自分の TeX Gyre を指したくなったら、それは同梱解決
> そのものの仕様変更であって、補完の段の話ではない。**

> ## ✅ **CI の ubuntu 脚は第213 で閉じた**（`7e00f580`。**起票は第212**）
> **ubuntu Release は `5531 / 53` → `5593 / 2`**（**残る 2 件は上の ▶＝この機械固有**）。
> **Windows は `5595 / 0`。総数はどちらも 5599。** **0.3.0 の門はもう赤で止めない。**
> ⚠️ **CI 自身が緑を出すのは push のあと**——**`gh run list` を読むのは §1 ⑸ ⒥ のあと。**
>
> ### 原因（**測定。推測ではない**）
> **`SKPaint.GetTextPath` が 2 つの機械で同じ関数ではない。** TextSize 1000・upem 1000 の
> 同梱 bold serif で、**Windows は設計自身の整数**（`"3"` → top `-708`・bottom `14`）、
> **Linux（SkiaSharp の FreeType 経路）は同じ輪郭を 1/512 の格子へ**
> （`-708.0078125` / `13.916015625`）。**Emmentaler も同じ**（`U+E0A4` の top が
> `-782` 対 `-781.982421875`）。
> ⚠️ **hinting のノブは無関係**——`NoHinting`／`IsLinearText`／`SubpixelText` の 4 組合せを
> 両 OS で測って、**Linux はどれでも Linux の数**。
> ⇒ **1 グリフ 1e-5 em。font-size を掛けて縦に積むと 1e-4 staff space** になり、
> **丸めの境界に載った台帳点と snapshot だけが赤くなる**（**53 件のうち 51 件がこれ**）。
> ★★★ **算術は `barnumber.*.staff-to-baseline` で閉じてある**: overshoot が
> **Windows `0.024445976200310277`**（台帳の記録 `0.024446` そのもの）／
> **Linux `0.024299327626563994`**、`3.05 + overshoot` が **3.074445976 ／ 3.074299328**
> ——**後者は Linux の suite が刷った値と桁まで一致。差 0.000146648 ＝ 観測された drift。**
> ⚠️ **HarfBuzz は白だった**（第212 が並べた 2 つの容疑者のうち片方）:
> **`Advance` は両 OS でビット一致**、**`hb_font_get_glyph_extents` は同一の整数**を返し、
> **その整数は Windows の Skia の値そのもの。**
>
> ### 閉じ方（**ユーザー決定＝⒝ 両 OS で合わせる**）
> **輪郭の生産者を 2 つとも `hb_font_draw_glyph` に替えた**（`HarfBuzzOutline.cs`）
> ——**テキストの `OutlinePath` と音楽の `MusicGlyphPath`。**
> **命令列は両 OS で SHA-256 一致**で、**その値は Windows が既に返していた数**
> ＝**Linux を Windows に合わせたのであって、両方を第三の数へ動かしていない**
> （**台帳 529 点・snapshot 218 枚とも不動**がその観測）。
> ⚠️ **新規パッケージは 0**。**`HarfBuzzSharp` 8.3.1 に draw の API は無い**が、
> **同梱の native lib が `hb_draw_*` を全部 export している**ので生 P/Invoke で届く。
> ⚠️ **`⒜ Windows 限定` と `⒞ 許容差を上げる` は採らなかった**——**⒜ は Linux で彫版の
> 幾何を誰も見なくなる／⒞ は原因が測れた時点で「何 ulp までを同値とみなすか」を
> 書く必要が消えた**（**動かすべきは許容差ではなく生産者だった**）。
>
> ### ★★ 道連れで消えたもの（**探していない**）
> **`GetTextPath` は GPOS 抜きの素の advance でグリフを並べていた**のに、
> **`Advance` は 2026-08-02 から kerning を数えている**——
> **予約*幅*と予約*輪郭*が「2 文字目がどこから始まるか」で食い違っていた**
> （§7.7 の「同じ量の 2 つ目の綴り」）。**pen は shaper のものになった。**
>
> ### ★★★ 網（**214 便のあいだ無かった観測点**）
> **`TextFontMetricsTests` に 11 本**——**「同梱フェイスの ink は*整数の font unit*」**。
> **数ではなく*整数性*を主張する**のは、**それが両 OS の一致そのもの**だから
> （**どのフェイスかは別の問いなので、ピン留めは 1 対だけ**）。
> ⚠️ **11 本とも「旧 Core ＋ Linux」で赤になることを確かめてある**（WSL で実際に戻して実行）。
> ⚠️ **Windows では旧 Core でも緑**——**これは*Linux の*観測者**で、
> **§0 の Windows だけでは永遠に沈黙する。** **だから §0 に Linux の行が要る。**
>
> ### perf（§7.9）
> **計算は変えたので測った**（`alloc`・3 回・最小採用）:
> **`perf-fingstack1k` 2440.4 → 2412.1 MB（全体）・165.0 → 161.9（1 打鍵）＝改善**、
> **`perf-plain1k` は 694.0 / 45.2 で厳密に不動＝真の対照**（fingering が無くテキスト輪郭が建たない）。
> ## ✅ **部品ヘッダの値検証は第210 で閉じた**（`b6482657` ＋ `2b66808b`。**ユーザー決定＝5 つとも error**）
> **起票（第209）は「2 つの値が検証されていない」だったが、着手時に switch を数えたら 5 つだった**
> （`removeEmpty`・`lines`・`octave`・`transpose`・`transposition`。**枝が在るのは
> `clef`/`tuning`/`pedal`/`instrument` の 4 つだけ**）。**2 つだけ塞ぐと報告は移動する。**
> ⇒ **汎化して §5 へ**: **「報告された欠陥の数は、その族の大きさではない。族は*検査する側*を数えて出す。」**
> ⇒ **残った網は `EveryPartProperty_RefusesAValueItCannotRead`**——**公開語彙の上に書いてあるので、
> 検査の無い property を後から足すと赤で着く。**
> ⚠️ **「大小文字を無視するのは `removeEmpty` ただ 1 つ」の項も同時に閉じた**（同じ根＝誰も検査していない）。
> **`transposition 8VB` が拒まれる理由がレクサーだった件は、レクサーを大小無視にして*検証側*へ移した**
> ——**受理する集合は 1 語も変わらず、診断が 3 行から 1 行になった。**

> ## ✅ **LSP の値補完の綴りは第211 で閉じた**（`792c5f57` ＋ `89f69a4c`。**起票は第210・⑵ も同じ便で閉じた**）
> ★★★ **起票は 2 件だったが、族は 3 件だった**——**着手前に「Core の語彙を自前で綴っている補完」を
> *数え直した*ら、⑶ `GetPartPropertyCompletions` が出た**（第210 は値の補完だけ数えて
> **名前の補完を数えていなかった**）。**そしてその 3 件目だけが*今まさに壊れていた***:
> **9 property のうち 6 つしか出さず**（`transposition`・`lines`・`pedal` が欠落＝**エディタが
> 言語の property 3 つの存在を否定していた**）、**`octave` の Detail が `absolute | relative` と
> 書いていた**——**第210 が部品ヘッダで*エラーにした*ちょうどその 2 語**（実測: `octave relative` は error）。
> ⇒ ★★★ **第209→第210→第211 で「色づけ／検証／補完」と 3 便続けて同じ族の別の顔が出た。**
> **§5.0 罠22 の「族は*検査する側*を数えて出す」を、今回は*提案する側*を数えて適用した。**
> ⚠️ **起票の「閉じ方は 1 つ」は実行できなかった**——**`LilySharp.Lsp` は別アセンブリで
> `RemoveEmptyValueVocabulary` は `internal`**（`InternalsVisibleTo` は Tests/Benchmarks/Probe のみ）。
> **起票は読みだけで書かれていたのでそこが見えていなかった。**
> ⇒ **`LanguageVocabulary`（public）を Core に置いて全部そこから作らせた。**
> **`SyntaxFacts.ClefNameVocabulary` は 11 語を `IsClefKeyword` で*濾して* 5 語を出す**ので、
> **音楽側は「パーサが拒む語」を名乗れない**。**パーサのエラー文言と `GRAMMAR.md` の
> `ClefName` もそこを読む＝5 語の綴りが 4 つから 1 つに。**
> ⚠️ **`tuning`/`pedal` の値補完文脈は足していない**（起票の指示どおり混ぜていない）。


> ## ★★ **計器が在る（2026-08-17・第195）＝`audit/LilySharp.Probe -- pitches`。この節の項は、まずこれに訊く**
> ```
> dotnet run --project audit/LilySharp.Probe -c Release -- pitches [listfile] [only]
> ```
> **1 冊を複数の出力に訊いて食い違いを印字する**——**ページ（collector の item）と MIDI を
> *ソース位置で*突き合わせ**（`MidiNote.SourcePos` と `MusicItem.SourcePosition`）、
> **MusicXML と MIDI を*鍵の多重集合*で**（XML にソース位置は無いので、
> **MIDI が「文書が書いていないコピー」を鳴らさない本＝566 中 532 冊**に限る）。
> ⚠️⚠️ ★★★ **ページ側に `check --pitches` を使わないこと**——**音高付き休符は音符と同じ家で
> 解決されるので trace には音符として並び**、**報告で作った計器は第194 の ⑶ を「MATCH」と言う。**
> ⚠️⚠️ ★★★ **射程の述語は第196 で直した。それ以前の CSV の `xmlComparable` は信用しない**
> ——**旧述語は `SourceOrdinal == 0` が全部**で、**その量は「鳴った回数」ではなく
> 「*印刷された*コピーの番号」**なので**両方向に外れていた**（`508e6364`）:
> **phrase を 2 回鳴らす本 12 冊を理由なく除外し、`|: :|` と percent/tremolo の 22 冊を
> 比較して 2 周目まるごとを差として報告していた。**
> **今の述語は「ページが N 個の頭を彫った位置で MIDI が N 回より多く鳴っていないか」。**
> ⚠️ **見えない所は数えて印字してある**（黙って落とさない）: **grace はページ側にソース位置が無い**
> （566 冊で 1,459 位置・31 冊）／**タイの 2 つ目は XML に書かれ MIDI では併合**／
> **part-combine のユニゾンは 1 つしか彫られない**（`midiOnly` 9 冊はこれ）。
> ⚠️ **CSV は初弾も持つ**（`pitchSample` / `xmlSample`）——**報告は見出しごとに 25 行しか刷らない**ので、
> **26 位以下の族を読むには CSV のほうを見る**（第196 はこれが無くて小 listfile を何度も回した）。
> ⚠️⚠️ ★★★ **比べているのは*鳴る*音高で、書かれた音高ではない**（第197 で直した・`0e685032`）。
> **移調 part は「印刷する音高」と「鳴る音高」が違うためにある**ので、
> **ページから C4・MIDI から 48 を読んで差と数えていた計器は、46 冊のうち 21 冊を
> *欠陥でないもの*で埋めていた**（`test/treble8` は 24 位置ぜんぶ）。**今は両側を sounding に揃える**——
> **⒜ MusicXML 側は文書自身の `<transpose>`**（規約の式。**`<clef-octave-change>` は足さない＝記譜だから**）／
> **⒝ ページ側は part ヘッダ**（**その要素が無いので**）**をソース span で帰属**
> （part block・part 宣言・**それと phrase 本体**——**phrase はどの part の外にも書かれる**）。
> ⚠️⚠️ ★★★ **⒝ は*循環*で、注記にそう書いてある**——**MIDI と同じ読みなので、
> その読みが壊れたら両側が一緒に動いて緑になる。**残るのは他の全部（綴り・開いたオクターブ・落とした音）。
> ⚠️ **`Staff.Transposition` から読んではいけない**——**TAB 譜しか埋めない**
> （`CreateTab` は取り `Create` は取らない）。**通常譜は 0 を返し、`staff m` と `tab m` を
> 両方持つ本は最後に歩いた譜で答えが変わる**（第197 が 30 分そこで外した）。
> ⚠️ **双子（LP）はまだこの計器に入っていない**——**1 冊ずつなら
> `scratch\p195\Compare-Pitches.ps1`**（`lysc ly` → LP → NoteHead dump → ページと多重集合で比較）。
> **全数に広げるなら LP を 566 回まわす便が要る。**
> ★ **現状**（第197 終了時）:
> **soundingRests 0 冊・midiOnly 9 冊・silentHeads 35 冊・pitchDiffers 5 冊・xmlDiffers 3 冊
> ＝合併 5 冊**（**第197 の頭は 46 冊**）。
> ⚠️ **この数は引継ぎに書いてあるだけ**——`audit/probe-out/` も `scratch/` も**git 管理外**なので、
> **CSV へのパスを根拠として引かないこと**（第196 は 1 度そう書いて、翌行で古くなった）。
> **取り直しは 8 秒**: `dotnet run --project audit/LilySharp.Probe -c Release -- pitches audit\probe-out\all566.txt`
> （**listfile は `git ls-files "*.lys"` で作り直す**）。
> ⇒ ★★★ **残る 5 冊の行き先**（**移調 clef の島は ⒜ で閉じた**）:
> **⒡ の 2 冊**（`section-meter-resets-to-global`・`fermata-b-obs-probe`＝**意図しない上昇＋MIDI の天井**）／
> **⒢ の 2 冊**（`bend`・`dead-note`＝**素の section と part の register・要決定**）／
> **`tab-below-range` 1 冊**（**MIDI の*床*。天井と同じ外部制約**）。
> ⇒ ★★★ **§2F に「決定済み・未実装」の項は 1 つも残っていない。**
> ⚠️ **第198 が足した ⒣ は第202 が閉じた**（`36c2e6f2`。**先に LP を測って台帳点を作る**を
> そのとおりの順で踏んだ——**測ったら起票の 2 regime のうち片方は*届かない*と分かり、
> 移植の形も「幅 0」から「欄ごと無い」へ変わった**）。**射程 0 冊は変わらない。**
> **この計器（pitches）はそもそも拍子を見ない**ので、
> **⒣ も、第198 が直したタブの拍子も、この 5 冊の数には 1 度も現れていない。**

- ✅ **⒜ 移調 clef の実音オクターブを MusicXML が持たなかった**（2026-08-17・第195 起票／
  **第196 決定／第197 実装＝`465fe58f`**）。**`<transpose>` は clef のオクターブ込みの*全部*の距離**
  ——**`<pitch>` は書かれた音高で、`<clef-octave-change>` は*記譜*（どこに描くか）**なので、
  **文書を鳴らす読み手には `<transpose>` しか材料が無い**。**出版される guitar も両方書く。**
  - ⚠️⚠️ ★★★ **importer は「同時に直す companion」ではなく*それ自体が欠陥*だった**——
    **他人の書いた guitar は前から `clef treble_8 transposition 8vb` に読まれて 2 オクターブ落ちていた。**
    **clef の*語*が担う分を引く**（`MusicXmlReader.TranspositionBeyondClef`）。**片方だけ動かすと必ずずれる。**
  - ⚠️ ★★★ **falsifier は「Lily# が書いていない文書」でなければならない**——
    **export が `<transpose>` を書かないあいだ、round trip の網はこの欠陥の上で永久に緑**
    （`PublishedTransposingPart_DoesNotCountItsOctaveTwice`）。
  - ★★★ **射程は 44 冊ではなく 20 冊だった**（第197 実測）。**44 は「本文に `treble_8` 等が在るか」の
    grep**で、**46 冊の suspect のうち 21 冊は欠陥ではなく計器の誤り**だった（§1 の骨）。
    ⇒ **数え直しは 8 秒**（下の計器）。**綴りの grep を族の数として引き継がないこと**（§0 ★ の族）。
  - **SVG も MIDI も動いていない**（snapshot 217 枚で不動）。
- ✅ **⒝ `repeat unfold N` は「N 回鳴らす」＝各コピーは同じ音**（2026-08-17・第195 起票／
  **第196 決定／第197 実装＝`85f52dcf`**）。**各 pass は repeat が開いた枠へ戻る**
  （**既定音価も同じ枠に乗る**）。**ページ・MIDI・MusicXML の 3 つが実装し、双子は
  `\repeat unfold N { … }` をそのまま書いて LP の同じ規則へ渡す。**
  - ⚠️⚠️ ★★★ **「snapshot が動く唯一の項」という札は間違っていた**——**射程は 0 冊。**
    **566 冊に実サイトは `samples/canon-in-d.lys` の `repeat unfold 13 { ground }` 1 つだけ**
    （**他の 5 件は全部コメント**）で、**本体が phrase 参照＝自分の枠で開くので昇らない。**
    **snapshot は 1 枚も動かず、承認は要らなかった**（§1 の骨 4）。
  - ⚠️ **裏返し＝この規則を観測している本が 1 冊も無かった。** 5332 本の網が緑のまま、
    **ページも MIDI も 4 コピーを 1 オクターブずつ上げていた**（SVG 符頭 y = 28.7/21.7/14.7/7.7）。
    **`UnfoldRepeatFrameTests` が唯一の観測者。**
- ✅ **⒞ 2 つ目以降の score が MIDI・MusicXML・双子に出なかった**（2026-08-17・第195 起票／
  **第196 決定／第197 実装＝`ab755a71`**）。**`--score NAME` / `--all` を 3 つに足し、
  選ばなかった form を警告で名指す**（`lysc midi test/multi-movement.lys` は 40 音を書いて黙っていた）。
  - **`--all` は 1 score 1 ファイル**——**命名は `svg --all` と同じ規則**（`RenderSpec.ResolveOutputStem`）。
  - ⚠️ **`--combined` は入れていない**——**1 枚に積むのは*レイアウト*で、3 楽章を続けて収めた `.mid` は
    *別の曲***。**LP も `\midi { }` 付きの `\score` 2 つに対して `ts.mid` と `ts-1.mid` を書く**（2.26.0 実測）。
  - ★★ **既定の読み（`main`・無ければ最初）が 3 軒に綴られ、2 軒目が既に外れていた**
    ——**MIDI と MusicXML は ordinal、双子は大小文字無視**。**`form Main` の本では双子だけ別の楽章を描く。**
    **`Semantics/ScoreForms` の 1 軒にした**（**その綴りはコーパスに 0 冊＝だから居られた**）。
- ✅ **⒟ 小節の途中の `clef` は相対枠を付け替える（ページが規則）**（2026-08-17・第195 起票／
  **第196 で決定 ＋ 実装 ＝ `65e54d69`**）。**3 冊とも閉じた**（`clef-change`・`mmr-clef-change-bound`・
  `cue-clef-manually`）。**MIDI と MusicXML が実装し、双子は補正した綴りを書く**
  （`c,` → `c,,,`）——**LP 2.26.0 に通して 13 音を裏取り済み。snapshot は 1 枚も動いていない。**
  ⚠️ **cue の clef は*両端*で無条件に付け替える**（ページがそうしている）。**残すのは以下の経緯だけ**:
  - **実測**（`test/clef-change`＝`g4 a clef bass c,4 d | e4 f g a |`）:
    **ページ C3 D3 E3 F3 G3 A3** ／ **MIDI 72 74 76 77 79 81（C5 D5 E5 F5 G5 A5）** ／
    **MusicXML 同上** ／ **双子は `\clef "bass" c,4` と*そのまま*書く**＝
    **LP の相対鎖は clef を見ないので C5**。⇒ **1 対 3 で、少数派がページ。**
  - **家は 1 行**: `MeasureCollector.MusicWalk` の `ClefDeclarationSyntax` の枝
    （`_octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(...)`）。
    **その注記は「*変わらない* clef は枠を付け替えてはいけない」と書いており、
    裏返せば「変わる clef は付け替える」が意図**と読める。
    ⚠️ **しかし `LILYPOND-REF` はその行の*上*にある「不変 clef は grob を作らない」の住所で、
    付け替えそのものには REF も OWN も無い**（LP に対応物が無い＝本来 `LILYSHARP-OWN` の側）。
  - ⇒ ★★★ **決定は「ページの付け替えが規則か」**。**規則なら 3 出力がそれを実装する**
    （双子は補正した綴りを書くしかない）。**規則でないならページを直す＝ snapshot が動く。**
    ⚠️ **どちらの向きでも先に文書に無いことを確かめてある**（`GRAMMAR.md`／`SYNTAX_REFERENCE.md`／
    `GRAMMAR_FOR_LLM.md` を `clef` × `octave|anchor|relative` で grep して **0 件**）。
  - **射程**: **`pitchDiffers` の中の 3 冊**（`test/clef-change`・`test/mmr-clef-change-bound`・
    `audit/lp-regression/lys/cue-clef-manually`）。★ **第196 が全数の述語で数え直して 3 冊で確定**
    （第195 は目視だった）。**3 冊とも `xmlDiffers` は 0＝MusicXML は MIDI と一致していて、
    少数派はページだけ**という形も変わっていない。
- ✅ **⒠ 第195 が残した「未分類 25 冊」は第196 で分類し、決定の要らない 4 族はすべて閉じた**
  （2026-08-17・第196）。**分類の結果と行き先だけ残す**（経緯は §1）:
  - **和音のタイが MIDI で併合されない 8 冊** → ✅ `9ecb1fb3`。**残差が MusicXML 側の
    第2の欠陥を出した**（不一致メンバーに「始まっていない tie-stop」）→ ✅ `22c85c19`。
  - **phrase の自動移調が MusicXML に効かない 2 冊** → ✅ `44a6480b`。
    **調べたら島はもっと大きかった**——**attributes が 1 度しか出ず、しかもファイル中*最後の*値**。
  - **section 境界の相対枠を MIDI だけ持ち越す 2 冊** → ✅ `d3dbc742`（**既定音価も同じ lane に乗っていた**）。
  - **MIDI の天井 127 で黙って潰す 2 冊** → ✅ `90077268`（**警告を出すようにした**。
    ⚠️ **差そのものは消えない**——**MIDI が 128 鍵しか持たないという外部制約**で、
    **ページと MusicXML は書かれたオクターブを保つ**のが正しい）。
  - **小節途中の `clef` 3 冊** → **上の ⒟ のまま**（要ユーザー決定）。
- ✅ **⒡ 2 冊の fixture が、書いた人の意図しない音楽を彫っていた**（2026-08-17・第196 実測／
  **第197 でユーザー承認 ＋ 修正＝`52c04380`**）。**各 section の最初の音にだけ `'` を残した。**
  ⚠️ **動いた snapshot は 1 枚**（`test/section-meter-resets-to-global`）——
  **「2 冊動く」は外れで、`audit/lpreg/fermata-b-obs-probe` は baseline を持っていない。**
  ⇒ ★★★ **直したら、天井が隠していた本物の欠陥が出た**（下の ⒢）——
  **9 件の pitchDiffers は全部「page D10 対 midi 127」で、127 の列は何とも食い違えない。**
  **まともな音高にした瞬間に、MIDI が section B を 2 オクターブ上で開いているのが見えた。**
  ⇒ ★★ **これは §1 の骨「片側を規則どおりにすると、もう片側の綴りが露出する」の*計器版***
  ——**露出させたのは engine の修理ではなく*本の*修理だった。**
  **以下は経緯として残す**（2026-08-17・第196 実測）:
  - **`audit/lpreg/fermata-b-obs-probe`** は **`a''4@accent a''4@accent@fermata …` と 4 回書いている**が、
    **相対モードの `''` は*最も近い* a から数える**ので **A5 A7 A9 A11 と 2 オクターブずつ昇る。**
    **本の名前が言っているのは「同じ音に 4 種のフェルマータ」**で、**ページはそう描いていない。**
  - **`test/section-meter-resets-to-global`** も同じ形（**全音符に `'` が付いていて octave 12 まで昇る**）。
    **本の主張は拍子についてで、音高は付き合いで書かれたもの。**
  - ⇒ ★★★ **これは §5.0 の「fixture の冒頭の散文は主張である」の*裏***——
    **散文が言っていない部分（ここでは音高）は、誰も見ていないので何年でも間違っていられる。**
    **第196 の MIDI 天井の警告はこの 2 冊を*名指しで*鳴らすので、次に `lysc midi` を打った人には見える。**
- ✅ **⒢ 素の section には part の lane が 1 つも掛かっていなかった**
  （2026-08-17・第197 で起票・実測・修理＝`03d3d414`（MIDI ＋ 計器）・`08283389`（MusicXML）・
  `409cf3bb`（双子））。**素の section の持ち主は `score` が決める**——
  **`score main { staff bl }` はこの音楽が part bl のものだと言っている唯一の文**で、
  **ページは前からそう読んでいた**（`RenderSpecParser` → `GetPartDefaults`）。
  **MIDI は `score` を 1 行も読まず、MusicXML は "Part 1" という*誰も宣言していない名前*で出していた。**
  ⇒ ★★ **道具は 1 軒**＝`RenderSpec.EngravedPartNames`（**staff ではなく*相異なる part*を数える**
  ——`staff bl  tab bl` は 1 つの part を 2 譜に描いているので**1**）。
  ⚠️ **score が 2 つ以上の part を名指したら今のまま**（**ページは同じ音楽を 2 つの register で描くので、
  1 本の流れはそのどちらでもない**）。**射程 0 冊なので、これは修理ではなく*定義*。**
  ⚠️ ★★★ **双子は「戻す」のではなく「差を書く」側だった**（4 軒目・`409cf3bb`）——
  **LP の `\relative` 鎖は section 境界を知らない**ので、**次の音に marks を足して埋める**
  （`EmitClef` が小節途中の clef でやっているのと同じ形）。
  **LP 2.26.0 で両向き裏取り済み**: `test/custom-text` はページが C4 D4 E4 F4 G4 G4 / C4 B3 A3 G3 C4、
  **直したあとの双子を LP に通すと 0 2 4 5 7 7 / 0 −1 −3 −5 0（中央ハからの半音）＝一致**、
  **直す前は 0 2 4 5 7 7 / 12 11 9 7 12＝境界の先が 1 オクターブ上。**
  **動いた双子は 20 冊中 2 冊**（`custom-text`・`section-meter-resets-to-global`。各 1 文字）。
  ⚠️⚠️ **5348 本の網と 217 枚の snapshot が全部緑のままだった**——**双子は LP しか読まない**ので、
  **双子が別の曲であることは「LP を回す便」以外からは見えない。**
  **以下は経緯として残す**（2026-08-17・第197 実測）:
  - **実測**（`part bl { clef bass tuning bass }` ＋ **素の** `section A { c4 … }` ＋
    `score main { staff bl  tab bl }`）: **ページ C3・MIDI C4。** **落とし方は 2 冊**
    ——`test/bend`（6 音）と `test/dead-note`（12 音＝**全部**）。
    ★ **最小対**（`scratch` に置いた 4 冊で分離した）: **`clef bass` を書くと出る／
    `tuning bass` だけなら出ない**（**anchor の話であって移調の話ではない**）。
  - **原因**: **`score main { staff bl }` は「この音楽は part bl のもの」と言っているが、
    MIDI は `score` を 1 度も読まない**（`MidiExporter` に `RenderSpec` は 1 行も無い）。
    **part ヘッダを armするのは `PartBlockSyntax` を見たときと、section が part 宣言の*中*に
    書かれているとき（part-major）だけ**——**素の section はそのどちらでもない。**
    **ページは逆に score の staff spec から part を引く**（`GetPartDefaults`）。
  - ⚠️⚠️ ★★★ **anchor だけの話ではない——part の lane が*丸ごと*掛からない。**
    **⒡ を直したら 2 つ目の症状が出た**（`52c04380`）: **section 境界の枠の戻しも効かない。**
    **最小対**（`scratch\p197\sec2.lys` / `sec3.lys`・**この対で分離した**）:
    ```
    section A { c'4 d e f | }  section B { g'4 f e d | }   form main { A B }   ← 素
    section A { melody { c'4 d e f | } }  …                                    ← part ブロックつき
    ```
    **素のほう**＝**ページと MusicXML は C5 D5 E5 F5 / G4 F4 E4 D4**（section 境界で part の
    anchor へ戻す）**が MIDI は 2 つ目の `g'` を G6（91）で鳴らす**——**双子も `g'` をそのまま
    書くので LP も G6**。**part ブロックをかぶせると差は 0。**
  - ⚠️ **MusicXML も同じ穴を持つ**（別の半分）——**section 境界の戻しは効くが part の anchor は
    効かない**（`test/bend` は `xmlDiffers 0`＝**XML は MIDI と一致していてページと違う**）。
  - ★★★ **射程を数えた**（2026-08-17・第197。**綴りの grep＝下の数え方ごと残す**）:
    **part を宣言して part ブロックも part-major も持たない本＝ 23 冊 / 566**、
    **うち part が既定でない（`clef`/`instrument`/`tuning`/`octave`/`transposition` を書く）本＝ 19 冊。**
    ★★★ ⚠️ **そして「素の section を score が*複数の* part に割り当てる本」は 0 冊**
    ——**起票時に「要ユーザー決定」と書いた曖昧さは、コーパスに 1 例も無い。**
    ```powershell
    # 素の section の本（近似）: part を宣言し、`name {` ブロックが 1 つも無く、part-major でもない
    $t = [IO.File]::ReadAllText($book)
    $parts = [regex]::Matches($t,'(?m)^\s*part\s+([A-Za-z_]\w*)') | ForEach-Object { $_.Groups[1].Value }
    $partMajor = [regex]::IsMatch($t,'(?s)part\s+\w+\s*\{[^}]*section')
    $hasBlock  = $parts | Where-Object { [regex]::IsMatch($t,"(?m)^\s*$_\s*\{") }
    ```
  - ⇒ ★★ **決めた形**: **score が名指す part が 1 つなら素の section はその part のもの／
    2 つ以上なら今のまま**（**射程 0 冊なので、後者は修理ではなく*定義*。網が固定している**）。
  - ✅ **見積り 1 便は当たった**（3 commit・**Core 3 ファイル＋計器＋網 12 本**）。
    ★ **`MidiExporter` の part-major の枝と新しい bare の枝は同じ 6 つを arm するので
    `PlayInPart` の 1 軒にした**——**part-major 側は既に「sounding shift も arm すること」と
    注記で*説明*していた＝2 軒目が要るという兆候はそこに書いてあった。**
  - ⚠️⚠️ ★★★ **計器も同じ穴を持っていた**（`613b861d`）——**MIDI を直した瞬間に `test/bend` が
    *逆向きに* 6 件出した**（**ページの C4 対、正しく C3 を鳴らす MIDI**）。
    **probe の帰属も span なので、素の section はどの span にも入らない。**
    ⇒ ★★ **「直したら計器が反対を向いた」は、計器が同じ前提を共有している合図。**
  - ⚠️ **NullReferenceException を 4 冊で出した**（同じ便のうちに直した）——
    **`spec?.EngravedPartNames ?? default` の `default` は*未初期化の* `ImmutableArray` で、
    `.Length` が投げる**。**`score` ブロックの無い本がその普通の経路。**
    ⇒ ★ **全数を回す計器が「読めなかった 4 冊」と言ったから見えた**（黙って 0 にしていたら通っていた）。

- ✅ **⒣ 拍子欄を誰も描かない本が、*中途の*拍子変更の欄を予約していた**
  （2026-08-18・第198 起票／**同日・第202 が閉じた**＝`66b0b639` 起票・`36c2e6f2` 移植・
  `ebba7523` コーパスの観測者）。**行頭側は第198 が閉じてあり**
  （`SpacingRules.AnyStaffEngravesTime` が門）、**残っていた中途側を閉じた。**
  - ★★★ **起票の 2 つの regime のうち、実在したのは片方だけ**。**起票は
    「`tab … as numbers` だけの本と、譜が 1 つも無い本（コード/歌詞のみ）」と書いていたが、
    後者はレンダして測ると*すでに厳密に不動*だった**——**2/4・8/16・16/32 でジオメトリが同一。**
    **`chords` は自分の `time` を取らない**ので、**中途の変更は score が描かない part にしか
    住めず、列の一覧に入らない**。⇒ ★★ **「直っていない」のではなく「届かない」**（別の文）。
    ⚠️ **その 0 は毒で裏を取ってある**——**同じ本を `staff melody` で描くと 2/4 と 16/32 で動く。**
  - ★★★ **LP を先に測ったら、移植の形が変わった**（§5.2.1③ の順序）。**LP は素の TabStaff の
    TimeSignature を*外していない*——`\override TimeSignature.stencil = ##f` で*空白にしている***
    （`ly/engraver-init.ly:1214-1220`）。**grob は非音楽列に*在って* X extent が空**で、
    **extent を読む 2 つの walk がそれを飛ばす**:
    `lily/break-alignment-interface.cc:144-156 calc_positioning_done`（offset も幅も付けない）／
    `lily/spacing-interface.cc:217-220 extremal_break_aligned_grob`（`space-alist` を読まれる
    `last_grob` にもならない）。⇒ ★★★ **欄は「幅 0 で在る」のではなく「無い」**。
    **バイトで裏取り**: **`\set Timing.measureLength = #1/2` で同じ小節格子を作った本と
    `\time 2/4` の本が SVG バイト同一**（`29AD0B45…`・8394 バイト）。
    ⇒ **だから「幅 0 を返す」実装は誤り**（`(first-note . (semi-shrink-space . 2.0))` を払い続ける）。
  - ★★★ **移植は LP と同じ機構**: **`TimeSignatureChangeItem.Blanked`（`##f` の移植）／
    `SpacingRules.ChangeItemHasInk`（2 つの skip の移植）／`MeterStencil.Blank`
    （collect 相の最後に `AnyStaffEngravesTime` が false なら旗を立てる）。**
    ⚠️ **`IsChangeItem` は*わざと*変えていない**——**空白の拍子も*非音楽列の*item ではある**ので、
    **`MeasureLayouter.ItemStartingAt` は今までどおり飛ばさねばならない**（さもないと
    零長 grob がその瞬間の音符としてスカイラインへ渡る）。
  - ★★ **旗を*item*に載せた理由**: **問いは score 単位**（paper column は全譜を束ねる）**で、
    譜は collect が声部を建てるまで存在しない**ので、**walk の内側では誰も答えられない。**
    **代替は 9 つの spacing 入口と 6 つの呼び手に bool を通すこと**で、
    **どれか 1 つを忘れられる形**（§5.2.1②）。
    ⚠️ **pass は*参照同一性*で memo する**——**`RenderSpec.ToStaffGroups` は
    notation 譜と tab 譜に*同じ* `Voice` を渡す**ので、譜ごとに書き換えると模型が倍になり
    `ReferenceEquals` が壊れる。
  - ★★★ **点は 2 つ要った。identity だけでは足りない**（§5.0 の「対称な誤りは identity から
    消える」）: **2 walk を移植した時点で identity 2 本は exact になり、
    `change-bar-vs-plain-bar` だけが +0.150000 で残った**——
    **`BarlineToFirstColumnSpring` の min_dist walk が `ClefChangeItem` *だけ*を飛ばしていた**
    （**key/time があると必ず別の枝へ行くので、それまでは exact だった**）。
    **空白の拍子はその枝へ行かないので、音符として測られていた。**
  - **台帳 3 点**（521 点・全部 exact）: `mid-piece.tab-numbers.meter-identity` ＋
    `mid-piece.tab-numbers.change-bar-vs-plain-bar` ＋ `mid-measure.tab-numbers.meter-identity`。
    **プローブは `audit/lp-geometry/probes/tab-numbers-meter.ly`**（**TN/TW/TL/MN/MW ＋
    対照 FN/FW**——**`\tabFullNotation` の 2 本が差を出すことで「掃きが届く」を示す**）。
  - ★★ **`+1.229159055` は幅の欠陥ではなかった**——**LP が拍子を*描く*ときに払う幅と 9 桁一致**
    （FN/FW の bar が 41.342172 対 42.571331）。**Lily# の幅の模型は正しく、
    欄が在ることだけが誤りだった。**／**`+3.454735433` は第198 が行頭で測った 3.4548 と同じ数**
    ＝**1 つの量が 2 か所で予約されていて、198 が片方を閉じていた。**
  - ★ **射程 0 冊は変わらない**（数え方は下の第198 の記録のまま）。**snapshot は 1 枚も再ベースせず、
    観測者として 1 冊足した**（`test/tab-numbers-meter-change`・217 → 218 枚）。
    **その本は毒 2 つで「自分が名乗る機構を観測している」ことを確かめてある**（§5.0）。
    ⚠️ **中途の変更はコーパス本に入れていない**——**中途の拍子変更はどちらかの拍子と必ず食い違う**
    （Lily# は LYS2001・LP は bar check 失敗）ので、**規則を観測するために恒久の警告を
    コーパスへ足すことになる**。**その半分の観測者は台帳点と単体網。**
  - ⚠️ **第198 の起票（射程の数え方・母集団の作り方）は下に逐語で残す**——
    **「0 を信じる前に 0 でない母集団を出す」の手順はこの項が閉じても使える**:
    **母集団A＝全部 `as numbers` のタブのみ 5 冊**／**母集団B＝譜が 1 つも無い本 28 冊**／
    **コーパス全体で中途 time を持つ本 18 冊**（**これが出ないなら計器が届いていない**）。
    ⚠️ **「中途の time」は*コメントを剥いでから* `section|phrase` の初出より後ろに `time N/M` が
    在るかで数える**——**`section { … }` の中だけ見ると `test/timesig-change` を取り落とす**
    （あの本は変更を `phrase` に置いている）し、**剥がないと 2 冊を偽陽性で拾う**
    （`lead-sheet-repeat` と `samples/drunken-sailor` は*コメントの中の* "section" に釣られる）。

- ✅ **`font "NAME"` を指定すると、予約と描画が別の face になる**（2026-07-27 起票／
  **2026-08-18・第200 が文法側を／第201 が測る側を閉じた**）。
  ★★★ **2026-08-18・第205 で*描画バックエンド 2 軒*も閉じた**（下の ⑹）。**この項は全部閉じた。**
  - ✅ **閉じた側 ⑴ ＝文法**（`b68eaefc`）。**`font` は役どころごとに face を束ねられるようになった**
    ——`font { serif "Georgia"  lyricText "A" "B"  chordName serif  title "C" }`。
    **`IDrawingContext.DrawText` は family ではなく `TextRole` を取る**ので、
    **face を知るのは `TextFontPlan.Resolve` 1 軒だけ**（葉→群→総称 family→同梱。
    **狭い綴りが順序によらず勝つ**）。**`TextFontDrawingContext` は役目を失って削除。**
  - ✅ **閉じた側 ⑵ ＝未知の face 名**（ユーザー決定 2026-08-18＝**warning に揃える**）。
    ★★ **起票時の「黙る」は*片方の綴りだけ*だった**——**`FontEmbedWarningValidator` は
    `DiagnosticCodes.FontNotFound` を前から持っており、`embedded` を書いたときだけ鳴っていた**
    （**同じ事実を同じコードが見つけて、片方の綴りでだけ報告していた**）。**門を外した。**
    ⚠️ **error にしなかった根拠＝フォントの有無は*機械の性質*でソースの性質ではない**
    （**licence の検査は `embedded` に残す**——**埋め込まないなら破れる licence が無い**）。
  - ✅ **閉じた側 ⑶ ＝ PDF の resolver が「設定 face を 1 つ」しか持てなかった**。
    **役どころごとに別 face を束ねられる以上、集合が要る**。**同時に、非埋め込みの
    stand-in が*常に serif* だった欠陥も直った**——**和音記号を absent な face に束ねると
    Heros で予約して Schola で描いていた。**
  - ⚠️ **⑷ 記譜テキストを射程から外した**（ユーザー決定）。**`font "NAME"` は
    `treble_8` の «8»・複合拍子の «+»・タブのフレット番号に届かない**（**起票時は届いていた**）。
    **`notation` か葉を名指したときだけ従う。**
  - ✅ **閉じた側 ⑸ ＝「名指し face は*描かれる*が*測られない*」**（2026-08-18・第201。
    **`9830873f` 漏れ止め／`8313bc33` face 鍵／`2ed165a5`＋`350f349a` 配管／`a7b28e58` 網と文書**）。
    ★★★ **ユーザー決定は*上書きされた***——**取ってあった ⒟「埋め込むときだけ実測」ではなく
    「解決できたら常に実測。`embedded` は PDF 埋め込みだけ」**（ユーザー決定 2026-08-18・第201）。
    **⒟ の前提「そこだけは決定的」が半分しか正しくなかったため**——**layout は 1 本**
    （`new LayoutEngine().Layout(multiScore)` を PDF も SVG も通る）**なので、`embedded` は
    PDF の自己整合を買うだけで機械間の決定性は買わない**。**§5.0「設問の前提を先に測る」。**
    ⇒ ★★★ **そして LP が名指し face を測っている**（`lily/font-select.cc:193-217 select_font`
    が `font-name` の文字列を `PangoFontDescription` にして `find_pango_font` へ渡す）
    ——**測らないほうが Lily#-own の逸脱だった。**
    ⇒ **軒は `ScoreTextMetrics`**（役どころ→face を 1 軒で解決・`(role, style)` で memo）。
    **`TextFontMetrics` は `TextFace` 鍵**（`Name` が null＝同梱）。**解決順は「同梱ファイル→機械」**
    ——**それも LP 自身の順序**（`lily/font-config.cc:43-78 make_font_config` が datadir の
    `00-lilypond-fonts.conf` を fontconfig の既定 conf より前に積む）。
    ⚠️ **機械に無い face は*throw*する**（`CanMeasure` に訊いてから使う）＝`LILYSHARP-OWN`。
    **Skia は未知の family に既定 face を返すので、黙って別の face を測るのが唯一の代替**だった。
  - ⚠️⚠️ ★★★ **代償を書いておく＝名指しした本は*機械依存*になる**（face の無い機械では同梱へ
    落ちて版面が変わる）。**LP と同程度の露出で、`FontNotFound` の warning が唯一の可視化。**
    **名指ししない本は無関係**（同梱ファイルは必ず在る）。
  - ★★★ **ずれの大きさは測ってある**（2026-08-18・`scratch\p200\facegap`。
    **同梱 Schola の予約 対 システム face の実描画・title サイズ 2.2 ss**）:
    **`Allegro moderato` は Georgia −0.573／Courier New +3.608／Verdana +1.543／
    Times −1.937／Arial −0.762**、**`rit.` は Courier New で +2.140＝ +68%**。
    ⇒ **丸めではなく「別の face を測っている」大きさ。**
    ⚠️ **`MeasureText` は shaping を通していない**ので**対の差は含まない**が、
    **face の差が桁で勝っている。**
  - ✅ **閉じた側 ⑹ ＝描画側の shaping 2 軒**（2026-08-18・第205。
    **`cefdc68a` PNG／`753d67fe` PDF／`5a7833c` §7.5 の監査**）。
    ★★★ **第201 の見立て「ずれるのは shaping ぶんで face ぶんではない」は*半分外れていた***
    ——**PDF には face ぶんのずれが在った**（下の ⚠️）。
    - **⒜ PNG**: **描画 typeface を Skia に family 名で引き直していた**ので予約が開いた
      ファイルと同じとは限らず、そのため名指し face は**必ず unshaped**で描かれていた。
      ⇒ **`ScoreTextMetrics.Face` ＝予約自身の walk から face を取り、`TextFontMetrics.Typeface(face)`
      の*その*ファイルで描いて `ShapeRun(text, size, face)` で置く。**
      **`FirstAvailable`（chain の 2 つ目の walk）は削除**——**§5.2.1⑤「同じ量の 2 つ目の綴り」**。
      ⚠️ **`TextRole.SystemBrace` だけ明示分岐**（Emmentaler のファイル名で、
      `TextFontMetrics` は開けない。通すと同梱 serif に落ちて serif の「{」を黙って描く）。
    - **⒝ PDF**: 同じ修理 ＋ **`embedded` があれば本物の program が在るので shaped 経路へ。**
      ⚠️⚠️ ★★★ **探しに行って*記録に無い*ずれが 1 つ出た**（第203 と同じ形）:
      **`TextFontMetrics` は名前を*その名前の*同梱ファイルに解決する**（`TeX Gyre Schola` は
      どの family に束ねても serif ファイル）が、**PDF の resolver は「束ねられた family の
      stand-in」を出していた**。⇒ **`font { sans "TeX Gyre Schola" }` は Schola で予約して
      Heros で描いていた**＝**shaping ぶんではなく face ぶん。**
      ⇒ **規則は `TextFontMetrics.TryBundledFamily` の 1 軒**（`TextFace` の 2 つの family 欄の
      *どちら*に答えているかを doc に書いた。両側が食い違っていたのはまさにそこ）。
    - ⚠️ ★★★ **観測者は 3 本。1 本目は足りなかった**——**cross-family の probe を
      「family を*束ね*から取る」毒で汚したら*緑のまま*だった**（**配置は `ShapeRun` から来て、
      それは描かれる face が何であれ*測った* face を渡される**）。⇒ **ページの `/BaseFont` も
      読ませた**。**毒の下で `["HUTCCG+TeX#20Gyre#20Heros"]` と証拠付きで落ちる。**
    - ⚠️ **stand-in の枝は `LILYSHARP-OWN`**（LP は `select_font` が必ず*ファイル*を得るので
      この設問が存在しない。**licence の決定が組版の帰結を生む**）。**観測者は無い**
      ——**第三者 face のインストール有無に依るから**。**`embedded` の枝も同じ理由で網に入らない。**
    - ★ **射程は 0 冊**（追跡 567 冊に `font` 宣言は 1 冊も無い。**毒で計器の到達を確認済み**。
      **ディスク 1119 冊の 10 冊は前の便の scratch probe**）。**snapshot も無力**
      （**218 枚は全部 SVG で PNG も PDF も 1 枚も無い**——**それがこれの生き延びた理由**）。
    - ★★ **便外で 1 度だけ実測した**（網に入らない枝の代わり）:
      **`scratch/p205/named-face.lys`**（Georgia の title）で **PNG の title インク 1027 → 1029 px
      ＝ +0.1 ss・左端 1 px 外・音楽は不動**（**356 インク行のうち動いた 51 行は全部 title 帯**）／
      **`scratch/p205/embed.lys`**（`font "Georgia" embedded`）で **PDF の配置 8 → 30 件
      ＝ 1 文字列 1 配置がクラスタごとになった**・**`/BaseFont` は `PBZCOJ+Georgia`。**
  - ⚠️⚠️ **SVG は原理的にここから外れる**——**SVG は face 名を属性で書くだけで、
    実際にどの face が出るかは*読み手の*機械が決める。**（予約が名指し face を測るように
    なったので、**読み手がその face を持っていれば SVG は今のほうが正しい**。）
  - ✅ **SVG の同梱 sans のずれ＝第215 が閉じた**（`f1676e5e`・ユーザー委任「見えている
    リリースブロッカーを直す」が「出力についての別の決定」を与えた）:
    **`FamilyAttributeFor` が root の serif と同じ形 `"TeX Gyre Heros, sans-serif"` を出す**
    （名指し face が先・generic はフォールバック）。**動いた snapshot は和音記号を持つ 11 枚・
    全差分が family 属性のみ**（両綴りを正規化して diff の残りが空になることを機械で確認。
    幾何は SVG 出力段でしか読まれない属性なので原理的にも動けない）。
  - ⚠️ **双子（`lysc ly`）は `font` を書かない。決定であって穴ではない**
    （`LilyPondExporter.EmitHeader` の注記）——**LP に対応物はある**（`make-pango-font-tree`）が、
    **書くと LP 自身の幅が動いて双子が対照でなくなる**。**Lily# 側は版面が動かないのだから、
    比較の中にだけ差が生まれる。**
  - ⚠️⚠️ ★★★ **射程は今も 0 冊**（`^\s*font\s+"` ＝**宣言の綴り**で数えた）。
    **この行は最初「2 冊」と書いて commit した**——**`font\s+` で数えた数**で、
    **2 件とも*コメント***（`above-dynamics.lys:8` と `flag-stem-begin-position.lys:10`）だった。
    **§5.0「grep の的の数を族の数として引き継がない」を、その規則を引用した当日に踏んだ**ので
    **数え方ごと残す**。⇒ ★★ **だから第200 の 4 つの挙動変更も第201 の実測も、snapshot を
    1 枚も動かしていない**（**LP 回帰 81 冊も 0 / 81**）。
  - ⚠️⚠️ ★★★ **そして「束ねれば版面が動く」は役どころによる**（2026-08-18・第201 実測・
    geometry-only 比較）: **`lyricText` を束ねると動く／`title`・`chordName`・`barNumber` は
    動かない。** **タイトルは中央揃えで幅を誰も予約しない／短い和音記号は `SymbolWidth` の
    2.0 の床の下／その本は小節番号を刷らない。**
    ⇒ ★★ **「実測するようにした」が*ページで*観測できるのは、幅が spacing に入る役どころだけ。**
    **これは読んでは分からず、端から端までの網に毒を当てて初めて出た**（§5.4）。
  - ⚠️ ★★ **配管の残り 6 site を名前で残す**（**測るのは同梱 face のまま**）:
    **`TabStaffGeometry` 3**（`TabFret` は*記譜*なので広い束縛が届かない側＋
    `FretDigitHeight` が型初期化の `static readonly`）／
    **`FetaTextRun` 3**（feta run の中の*非 feta 文字*の fallback。消費者 3 軒が
    それぞれ static の包みを持つ）。
  - ✅ **予約と描画の食い違い＝第201 が 3 site を記録し、第203 が閉じた**（`17669dcd`）。
    ★★★ **記録の 3 つは族の*小さい半分*だった**——**族を数え直したら、2 site は
    *サイズ*も食い違っていた**（`MarkXExtent` が **2.2 Bold**／`LayoutEngine` の mark box が
    **2.4 Bold**・**描画は 2.8 BoldItalic**）。**どちらも同じ switch の*隣のケース*の数**
    （2.2＝boxed SectionLabel・2.4＝boxed Rehearsal）で、**どちらも予約が足りない向き**
    ——**「D.S. al Coda」で −4.233770079 と −2.868037795 ss。**
    **記録済みのスタイル差（0.034〜0.171）より 1〜2 桁大きい。**
    ⇒ ★★ **移植の形は「1 軒」**: `MusicMarkEngraver.PlainTextFontSize`／`TextStyleOf`
    （plain-text mark）と `DynamicEngraver.LabelStyle`（強弱ラベル）を**描画側も予約側も読む**。
    **`OutsideStaffStacker` だけは前から正しく、注記もそう書いてあった。**
    ⚠️ ★★★ **swing の `"="` は 0.000000000**——**Schola は `=` の advance が Bold と Regular で
    同じ**。**対は間違っていたが、見せるものを持っていなかった。**
    ⚠️ ★★★ **一番大きい 2 つの修理は 567 冊で 1 冊も動かさない**。**そして bare な 0 にしていない**
    ——**`MarkXExtent` の枝は 100 ss の毒でも 0 冊**（**plain-text mark と、その 2 つの
    重なり検査が比べる相手＝行内コード記号／歌詞とを対にした本がコーパスに無い**）／
    **`LayoutEngine` の box は同じ毒で 3 冊動く＝そちらは到達し、余裕があっただけ。**
    ⚠️ **観測者は `MarkReserveVersusDrawTests` 30 本**（**コーパスが届かないので
    `MarkXExtent` を internal にして*呼ぶ*）。**毒 3 つで赤の集合が 3 つに分かれる。**
    ⇒ ★★★ **そして §7.5 が 1 件出した＝`PlainTextFontSize` の 2.8 は LP の数ではない**
    （`6f8631f6`・注記のみ）。**LP の `JumpScript` は `font-size` を宣言しない**
    （`scm/define-grobs.scm:1898-1926`）**ので paper の 2.2**（＝`TextScriptFontSize` と
    同じ住所 `scm/paper.scm:69-77`）。**0.7 は staff サイズからの当てずっぽうで、
    `ChordNameFontSize` と `CombineTextFontSize` が 2 度とも直された形と同じ。**
    **直していない**（**予約と描画を揃える島と、箱そのものを直す島は別**）。**`LILYSHARP-OWN` で名前を付けた。**
    ⚠️ ★★ **もう 1 つ名前だけ付けて直していない**: **LP の `JumpScript` は
    `font-shape italic` で `font-series` を宣言しない＝*italic であって bold ではない***のに、
    **Lily# は BoldItalic で描く。**
- ★★ **`lysc ly`（双子 exporter）の穴**。**塞ぐたびに LP と突き合わせられる本が増える**ので、
  忠実度作業の**測定可能面積そのもの**が懸かっている。
  ~~⑴ `voice { }`~~・~~⑵ `grandStaff` の入れ子~~・~~⑶ `ossia`／`part` 宣言なし~~・
  ~~⑷ section のヘッダ~~・~~⑸ 和音のオクターブ記号~~・~~⑹ grace のあとの音価~~ — **すべて完了**
  （第61〜63セッション。最後の 2 つは `275c12ee`）。
  ~~⑻ `@stemUp`/`@stemDown` を落とす~~ — **完了**（2026-08-03・engine 側と同じ commit で。
  `\once \override Stem.direction`。理由は §1 ⑥）。
  ✅✅ ★★★ **この一覧に開いている項は 0 になった**（2026-08-17・第194 で実測・**⑺ は既に閉じていた**）。
  ⚠️⚠️ ★★★ **そして第197 で 1 つ増え、同じ便で閉じた**（`409cf3bb`・§2F ⒢ 末尾）:
     **section 境界で Lily# は枠を part の anchor へ戻すのに、双子は差を書いていなかった**
     ——**LP は境界の先を 1 オクターブ上で読む**（`test/custom-text` を LP 2.26.0 で実測）。
     ⇒ ★★★ **「一覧が 0 になった」は*その日の*棚卸しの結果であって、穴が出尽くしたという意味ではない。**
     **この穴は「落とす」でも「別のものを書く」でもなく*3 つ目の形＝同じ綴りが別の意味になる***
     ——**双子は `g'4` と書き、それは正しい綴りだが、境界の先では別の音を指す。**
     ⚠️ **見つけ方も残す**: **engine 側で同じ規則を直したとき、双子にも site があるかを必ず訊く**
     （第197 は collector・MIDI・MusicXML の 3 軒を直した*あとで* 4 軒目に気づいた）。
  ~~⑺ 度数和音が `<>` になる~~ — **既に閉じている**（**第194 が測って発見**：
     `<1 3 5>` → `<c e g>`・`<d 3 5 7,>` → `<d f a c,>`）。**§2F の「残っているのは 2 つ」は
     stale だった**——**この見出しが「一覧は伝聞。着手前に実コードで確認」と言っているとおり。**
  ~~⑼ 入れ子の中の phrase 参照が空になる~~ — **第194 で閉じた**（`e38770ea`）。
     **`CarryFrameInto` が `_phrases`/`_activePhrases` を渡すようになり、6 つの入れ子 site 全部**
     （phrase 参照・tuplet・voice span・grace・cue・repeat）**が同じ 1 軒を読む。**
     ⚠️⚠️ ★★★ **起票の「ツリー 0/300 冊」は 566 冊では 1 冊**——
     **`samples/canon-in-d.lys`**（`repeat unfold 13 { ground }`）で、
     **その双子は `\repeat unfold 13 {  }`＝ページ 53 小節・双子 1 小節**。
     **`samples` は第186 が射程を広げるまで誰も掃いていなかったディレクトリ。**
     ⇒ ★★ **「0 冊だから falsifier が無い」は*射程が 0 冊*だっただけ**（§1 の骨 ⑵）。
     ⇒ ★ **網は綴りではなく不変条件で書いた**——
     `TheNotDeclaredWarning_NeverNamesAPhraseTheBookDeclares` が 566 冊すべてに当たる。
  ⚠️ ★★★ **穴は「落とす」だけではない——「別のものを書く」形もある**（第176 で 1 つ出た）。
  **`\clef` は clef 名を*文字列*で取る**（`make-clef-set` がオクターブ記号をその文字列から
  切り出す）のに、**双子は 4 か所とも裸で書いていた**。**`treble_8` だけが壊れる**——
  **LP の reader が先に切って `_8` が指番号になる**（裸 5643 バイト＋"Unattached FingeringEvent"／
  引用 6442 が本物／素の treble 5161）。**6 冊が別の clef の双子を持っていた。**
  ⇒ **`LyClefName` 1 軒に畳んで常に引用**（第176第2便 `784d0369`）。
  ⚠️ ★★ **教訓＝「4 つのうち 3 つは正しく見える」形の穴を、正しく見える例で網にしない**。
  `treble`/`bass`/`alto`/`tenor` は英字だけなので裸でも lex する＝**双子テスト 12 本が
  裸を主張していた**。**網は `treble_8` を名指すこと。**
  ⚠️ ★★ **穴の値段を初めて全数で測った**（2026-08-15・第176。**双子 299 冊を LP に通した**——
  fixtures ＋ LP 回帰コーパス・`-dno-print-pages`・約 70 秒）。**LP が何か言った本の内訳**:
  **bar check failed 17 冊**（罠17＝測定から除く既知の仕分け）・
  **skipping zero-duration score 7 冊**・discarding/conflict event 5 冊・
  タブの弦/フレット 1 冊・rest collision 1 冊。**Unattached FingeringEvent は 0**（第2便の直しが 299 冊で保った）。
  ⇒ ★★★ **`zero-duration` の 7 冊が「測れる面積」の実損**——**LP がその score を丸ごと飛ばす**ので、
  **その本では何ひとつ突き合わせられない**。**内訳は 2 冊が既知の parse しない fixture**
  （`multi-movement`・`grammar-2026-06-09`＝§2F の別項）で、**残る 5 冊は chords 行／lyrics 行だけの本**
  （`lead-sheet` 系 4 冊と `rows-song-sheet`）。
  ⚠️ **これは黙った穴ではない**——exporter は
  **`chord row 'prog' is not exported — the twin has no chord row`** と**ちゃんと警告している**
  （下の規則は守られている）。**新しい欠陥ではなく、既知の穴の*値段*が 5 冊と分かったということ。**
  ⇒ ★ **chords/lyrics 行を双子に出せると、譜を持たない本 5 冊が測定面積に入る。**
  ⚠️ §2F 下段の「chords 行 / lyrics 行が `PartReferenceFinder` に無い」と**同じ島**（別の顔）。
  ⚠️ **「exporter が黙って空を返す」欠陥はこれで 6 度目**（第55・56・61・62・63）。
  ⇒ ★ **落とすなら必ず `Warnings` に出す**。**`<>` や空の part 変数を黙って書かない。**
  ⇒ ★ **塞いだら双子 199 本の before/after を全数比較する**（第62セッション ② の手順。
     1 回目で本物の退行を捕まえている）
- ~~★★ **仕様書の網は `GRAMMAR_FOR_LLM.md` しか読んでいない**~~ — **第175 で閉じた**
  （`7a240afd`＋`24767164`＋`fcdddb85`＋`b9e8cbe8`・§1）。**網は 3 ファイルの Theory** になり、
  **正典 58/0・TUTORIAL 13/0・LLM 版 22/0**。**毒は両方の新ファイルに入れて赤を見た**。
  ⚠️ ★ **`GRAMMAR.md` も第5便で入った**（`72cf5daf`）——**抽出は 2 形**（plain / `lilysharp` /
  `lys` の fence ＋ `(* Example…: … *)` の全文）。**10 例中 3 例が落ちて全部直った**
  （うち 1 つは **`ScoreDecl` の production 自身**・§1 ⑸）。
  ★ **除外集合の値段は測ってある**（`FragmentCodes` の remarks に記録）: 除外を外すと
  **LLM 版 6 本・正典 8 本・TUTORIAL 0 本**が落ち、**14 本とも正当な抜粋**。
  ⚠️⚠️ ★★★ **残っているのは `(* … *)` と箇条書きの中の「一覧」**（注釈の族の一覧など）。
  **第175 が直した `@segno` の行はそこに居た**——**Example ブロックではないので、
  新しい抽出器でも捕まらない**（第175第4便が「まさに例だった」と書いたのは誤り・第5便が訂正）。
  ★★★ **ただし第6便で*測って*ある**（`e6f3e57f`・§1 ⑹）。**4 冊が書く `@` 綴りを書かれたとおりに
  抜いて全部 `check` に通す**——**94 綴り・却下 13**で、**13 は全部メタ構文か「無効と書いてある行」**。
  ⇒ **いま一覧に潜んでいる欠陥は 0。** 再測はこの 1 手（プローブは `c4<綴り> d e f |`）:
  ```powershell
  $rx='@[A-Za-z][A-Za-z0-9_]*(\([^)]*\))?(\.[A-Za-z]+)*'
  # docs\{GRAMMAR,SYNTAX_REFERENCE,GRAMMAR_FOR_LLM,TUTORIAL}.md から Matches で集めて重複除去し、
  # 各綴りを part/section に入れて lysc check（No errors found 以外を数える）
  ```
  ⚠️ **網にはしていない**——**主張された綴りと*反例*を機械が見分けられない**
  （文書は散文でしか区別していない）。**正規表現で分けるのは §5.2.1⑦ の「推測する checker」。**
  ⚠️⚠️ ★★★ **そして例の網には*重さ*の穴がある**（2026-08-16・第179 実測）。
  **`DocExamplesParseTests` は*エラー*でしか落ちない**ので、**例に書かれた注釈の綴り間違いは
  素通りする**——`@resty` を入れても緑（未知の注釈は**警告**）。**エラー級の毒に替えると赤**。
  ⇒ **つまり「正典の例は全部コンパイルする」は「正典の例が全部正しい」ではない。**
  **`@` の綴りについては上のカタログ実測が唯一の観測者**（第175第6便・94 綴り）。
  ⇒ ★ **塞ぐなら「例は警告も 0 であること」に締める**——**ただし抜粋の除外集合が
  警告を出す形かどうかを先に測ること**（`FragmentCodes` は*エラー*コードの集合）。
  ⇒ ★★ **やるなら「一覧に構文を与える」＝設計判断**（例：各項を `(* Example: … *)` に割る／
  反例に印を付ける）。**その判断が要るので、この項は作業ではなく決定として残っている。**
  ⚠️⚠️ ★★★ **一覧のほかに、*production* も網の外**（第176 実測）。
  **`GRAMMAR.md` の `Cue` は `'cue' , MusicBlock ;` と書いていたが実物は `[ ClefName ]` を取る**
  ——**production は*例*ではないので抽出器が読まない**。**第175第5便の `ScoreDecl` と同じ型で、
  これで 2 例目。** ⇒ ★ **当座の手当ては「production を足したら例も足す」**（第176 はそうした・
  毒で両方読まれることを確認）。**機械化するなら production 自体を実行可能にする＝これも設計判断。**
  ★ **正典に節ごと無い機能もあった**（`cue` は `SYNTAX_REFERENCE.md` に 1 行も無かった）——
  **「例が全部通る」は「書いてある」を意味しない。**
  ⚠️⚠️ ★★★ **3 例目＝*文書どうしの食い違い*は、例が全部通るので構造的に見えない**
  （2026-08-16・第183 実測）。**phrase の参照を `GRAMMAR.md` の production は*裸の Identifier*と書き、
  `SYNTAX_REFERENCE.md` と `GRAMMAR_FOR_LLM.md` は `$name` と教える**——
  **実測（8 綴り × 両オクターブモード）で両者は完全に同じ**なので、**`DocExamplesParseTests` は
  どちらの例も緑にする**。⇒ **production は「parser が受理するもの」に直した**
  （`[ '$' ] , Identifier , …`）が、**どちらを*教える*かは決定**＝この項に属する。
  ⇒ ★★ **判定法**: **同じ構文を 2 冊以上が書いているなら、綴りを grep で突き合わせる。**
  **「全部コンパイルする」網は、2 つの正しい綴りが 2 冊で食い違っていても何も言わない。**
- ★★ **繰り返し縦線の島＝第174 で 4 便入れた。残っているのは 2 つだけ**（2026-08-15）。
  **ユーザー決定**（第174・全部実装済みか下に明記）: **⑴ 判定は score 展開後にしかできない**
  （section 単体では form が前に `|:` を置くか分からない）**⑵ 対応しない `|:` はエラー**
  **⑶ 片側の `:|` は「曲の先頭から繰り返す」**（＝原理的に未対応になり得ないので `:|` は
  エラーにならない）。**⑷ 縦線は score の物で、他 part へ必ず伝播する**——**実測でページは
  既にそうなっていた**（`SynchronizeBarlines` が「score-level Timing semantics」と自称。
  `melody` にだけ `|: … :|` を書くと **bass の譜にも反復ドットが出る**・伝播した縦線は
  `data-pos="0"`）。**MIDI だけが part ごとに読む＝これが 5 つ目の食い違い。**
  ~~⑴ 仕様書は対の形しか定義していない~~・~~⑵ 3 出力が違うことを言う~~・
  ~~⑶ 判定は score 展開後でしかできない~~・~~⑷ 展開の歩きが 4 本~~ — **第174 で全部片付いた**（§1）。
  ⚠️ **「4 本の展開の歩き」は言い方が不正確だった**——**展開するのは MIDI だけ**で、
  ページ・双子・MusicXML は**構造を記録する**。⚠️ **site 数 12/18/1 も再現しない**
  （`RepeatStart|RepeatEnd` で数えると 5/11/1）＝**数え方を書いていない数**（§0 ★）。
  **残っているのは 1 つだけ**（⑸ は起票した当日に自分で倒した。下）:
  ~~⑸ 双子が「二重の `|:`」を入れ子にする~~ — ⚠️⚠️ ★★★ **欠陥ではない。第174 の最後に
     LP 2.26.0 で実測して撤回した。** `form { |: A … }` の section A 自身も `|:` で始まる綴り
     （`Addicted To Love`・`青い珊瑚礁`）で**双子は `\repeat volta 2` を 2 重に出す**が、
     **LP はそれを 1 重と*バイト同一*に組む**（`\repeat volta 2 { \repeat volta 2 { X } }` と
     `\repeat volta 2 { X }` の SVG ハッシュが一致・**内側の span が外側の本体とちょうど同じ**
     なので縦線が重なる）。**MIDI も同一。**⇒ **冗長なだけで、LP のどの出力も動かない。**
     ⚠️ ★★★ **起票時に「LP は 4 回鳴らす」と書いたのは*構造からの推論*で、`実測` の札まで
     貼っていた**——**双子が出す綴りを測っただけ**だった。§5.0「確認済と書いてあっても、
     その確認が何を見たかまで書いていなければ再確認する」の**自分版**。
     ⇒ ★★ **双子の綴りを見て LP の答えを推論しない。LP に訊くのは 1 コマンド。**
  ⑹ ★ **section 音楽中の片側 `:|` を MIDI が鳴らさない**（**133 冊中 0 冊**が書く）。
     ⚠️ **第174 第4便の第1版がここを実装して倒れた**——**622 冊で 1 冊のはずが 4 冊動いた**。
     **ABC／Automatic／Beat It は `|:` を或る section に `] :|` を別の section に書いていて**
     （**展開後は正しく対**）、**`ProcessSequence` は 1 section しか見えない**ので片側と読み、
     **曲を丸ごと繰り返した**。⇒ ★★★ **MIDI に片側性は判定できない。**
     **鳴らすなら MIDI が collector の平らな列を読む形にすること。**
     ⚠️ **値段の見積りに双子を使えない**——**LP の MIDI は `\repeat volta` を展開しない**
     （第174 実測・RULES §6）。**この項の効果を測れるのは Lily# の MIDI だけ。**
- ✅✅ ★★★ **cue の島は第178 で閉じた（2026-08-15・実装 3 便）。隣り合う cue は 2 声部になった。**
  **診断（`3312b4f8`）と spacing（`e84513e7`）が同じ刻印 `MusicItem.BeginsCueRegion` を読む。**
  ★ **刻印は「領域番号」ではなく「領域の縁」**——番号は collect resume の suffix splice が
  **別の walk で採番された tail** を貼り付けるので破れる。縁の印は*自分の領域の中での位置*だけの
  関数なので貼り付けても正しく、position 非依存なので `MeasureContentKey` も畳み続ける。
  **識別子が要る読み手は既に歩いている所で導出する**（スラーは走査中に数える／タイの結び先は
  次の音符なので「結び先が領域を開くか」で足りる／spacing は「領域が対を跨いで手前へ届くか」だけ）。
  ★ **spacing 側は先に測ってから移植した**（`voice-boundary-spacing.ly` §F・**同じ 4 音で
  `\new CueVoice` が 1 個か 2 個かだけが違う対**）: 境界の歩幅は **2.898044999134611（素の ideal）
  対 2.513393907138011（精錬済み）**。台帳 `cue.column.region-edge` −0.384653432 → **0（exact）**、
  対照 `cue.column.region-edge-control` は **−0.000002340 のまま不動**。
  ⚠️ ★★ **「閉じたら −0.000002340 に着地するはず」と書いた予測は外れて 0 になった**——
  **その丸めは head-width 項に乗って来ていた**ので、**項ごと消えた**（外れ方が機構の裏取り）。
  ⚠️ **残る近似は「別々の声部で cue 領域が*同時に*生きている」形だけ**＝
  **右列に領域を*開かない* cue item が居るとき**は精錬を残す（移植前の挙動）。
  **観測者 0・書ける本も 0**（1 小節に cue 領域を 2 つ書く本がディスクに無い）。
- ✅✅ ~~**fixture が今の文法で parse しない**~~ — **第182 で閉じた**（`5d492d5f`・
  **ユーザー決定＝直す**）。`test/multi-movement` は**3 section / 3 form / 3 score** へ、
  `showcase/grammar-2026-06-09` は**期限切れの主張 3 つを訂正**して現行文法へ。
  ★ **副産物**: multi-movement が**ツリーで唯一の「form が 2 つ以上ある本」**になった
  （実測：それまで 0 冊）＝`FormDeclarationValidator` が存在理由にしている配置に観測者がついた。
  ⚠️ **どちらも snapshot リストに無い**（実測）ので再ベースは起きていない。
- ★ **音高付き休符 `a4@rest` は第179 で入った**（LP の `a4\rest`・**綴りはユーザー決定**）。
  **これで skip だった `rest-pitched-beam.ly` がコーパスに入り**、
  **`rest-avoid-note.ly` の両側置換も撤回できた**（§1）。
  ✅✅ ★★★ **残っていた穴は第194 で閉じた**（`733305a8`）——**ただし起票は*小さい半分*を名指していた。**
  ⚠️⚠️ **起票は「MusicXML が高さを落とす＝`<rest/>` になる（音価は正しい）」だったが、実測は
  `<note><pitch><step>A</step>…`＝*音符*だった**（`<rest/>` ですらない）。
  **MIDI も鳴らしていた**（`a'4@rest c'4 r4 g'4@rest` で noteOn 3・対照は 1）。
  ⇒ ★★★ **起票どおりに直しに行くと `<rest>` に `display-step` を足す仕事**になり、
  **その要素は出ていないので 1 行も効かない**（§1 の骨 ⑷）。
  ⇒ **原因は「読み手が*存在しない*」**——**両 exporter は collector の item ではなく*構文*を歩く**ので
  それぞれ綴りの読み手が要るのに、どちらも持っていなかった。
  **今は `Semantics.PitchedRest` 1 軒**（collector と双子もそこを読む）。
  ★ **正しさはコードが自分で書いていた**——`CreatePitchedRestItem` の注記が
  **「must not sound in MIDI」**と最初から言っており、**それを見ている網が 0 本だった。**
  ⚠️ **`@rest` を書く本はツリーに 3 冊**（`rest-avoid-note`・`rest-pitched-beam`・`restavoid`）
  ＝**SVG 掃きの陽性対照はこの 3 冊**（毒で 3/566 が動く）。
- MusicXML インポート — ほぼ完遂、**実ファイル検証が残**
- AI 協調編集 M1–5 — **実機 E2E 未検証**
- 文法改善 5 件は完了。**0.3.0 は出荷済み**（第219・タグ `v0.3.0`）
- ★ **`override` の消費語彙は 3 対**（2026-08-15 に engine 内の resolver 参照を全数抽出して確定）＝
  `NoteHead.transparent`・`Stem.transparent`（`SharedRenderer.Noteheads`・計 4 site）と
  `NoteColumn.force-hshift`（`ElementCoordinator`）。**「4 つ」は stale**。
  ~~文法側は元から開いている~~ — **LYS1029 で閉じた**（`a0126cd4`・`SupportedGrobOverrides` が唯一の家。
  未対応の綴りは「not supported in this version」でエラー・実装を増やすと診断が 1 つ消える）。
  ⚠️ ~~**値に小数リテラルが書けない**~~ — **書ける**（第167 で `DecimalLiteral`。実測：`= 5.5` / `= -3.5` /
  `= "red"` / `= true` すべて通る）。
  ⚠️ **page 系（`paper-height`/`top-system-spacing`/`systems-per-page`）を `override` に載せない**——
  LP ではそれらは `\paper` 変数であって grob プロパティではない（コーパスはハーネス引数で解決済み）
- ✅✅ ★★★ **chords 行 / lyrics 行の検証は第180 で閉じた**（`00de89bb`・ユーザー決定）。
  `chords NAME` / `lyrics NAME` が**存在しないトラックを名指せなくなった**（それまでは
  「No errors found」のまま行を 1 つも描かなかった）。
  ⚠️⚠️ ★★★ **引継ぎの処方箋「`PartReferenceFinder` に足す」をそのまま書くと、リポジトリの
  リードシートが全部落ちる**——**`chords NAME` は part ではなく*名前付き* `chords NAME { … }`
  ブロック（`ChordPartBlockSyntax`）が宣言するトラック**を指す。**参照の半分と宣言の半分は
  1 つの claim**（§5.0）。**名前空間は畳まなかった**（畳むと `staff prog` が通る＝LYS1007 が
  防いでいる空の譜）。★ **全数で測った**：ツリーの全 `.lys` **897 冊**で**診断が変わった本 0 冊**。
  ⚠️ ★★ **`AllPartNameTokens`（改名）には足していない**——**LSP が既に chord/lyrics の
  トラック名を*より完全に*解決している**（宣言・行・`with` 節）ので、こちらが caret を奪うと
  答えが小さくなる（実際に LSP の網 3 本を赤にしてから撤回した）。
- ✅✅ ★★★ **名前を取る render 項で「誰も見ていないもの」は第181 で 0 になった**
  （`18cbddd5`＋`91257b41`）。**`staff|tab … with chords C` / `with lyrics L`** は
  `Tracks`（トラックの名前空間）へ、**`condensedStaff { … }` / `combinedStaff { … }` の
  裸のメンバー**は `ReferenceTokens`（part の名前空間）へ入った。
  ★ **数え切った一覧**（parser を読んで確定・推論ではない）: staff・ossia・tab・
  grandStaff の中の staff・condensedStaff・combinedStaff・midi part・`chords` 行・
  `lyrics` 行・`with chords`・`with lyrics`・`score <form>`（LYS1018）。
  ⚠️ **`condensedStaff`/`combinedStaff` は `AllPartNameTokens` にも入れた**——
  **第180 が行の族でやって撤回したのと*逆*の判断**で、理由は測ってある:
  **LSP はこの 2 つを 1 つも解決していない**（`grep Condensed|Combined` は補完のコメント 1 件）。
  **行の族のほうは LSP のほうが完全なので、今も入れない。**
  ⚠️ **同じ島の別の顔がまだ残っている**——**exporter が chords/lyrics 行を双子に出せない**
  （穴の値段は**譜を持たない本 5 冊**・第176 実測）。**塞ぐなら双子 199 本の before/after
  全数比較**が要る（§2F 上段）＝**それだけで 1 セッションの形。**
  ⚠️⚠️ ★★★ **その一覧は *render 項* の一覧だった、というのが第182 の骨**——
  **枠の外に `using` が居て、しかも一番大きく落としていた**（下）。
- ✅✅ ★★★ **`using "file.lys"` が読めないファイルを名指す件は第182 で閉じた**
  （`b675b23b`＋`49aadc2c`・**LYS0028・ユーザー決定＝warning**）。
  **実測**（RULES §5.0 のバイト法）: `using "metaa.lys"` は **`No errors found.`** と言い、
  **data-pos を伏せた SVG が using 行を削除した本と文字単位で一致**した
  ＝**綴り間違いの取り込みは、行を書かないのと厳密に等価**だった。
  ⚠️ **半分は「うるさく」失敗していた**——消えた名前をスコアが参照すれば
  `Undefined section` / `Undefined part` は出る。**ただし正しい行を指し、1 行目には何も言わない。**
  ★ **warning にした理由は測ってある**: **沈黙は設計として宣言され網で pin されていた**
  （`UsingExpander` の doc「a missing using never aborts the render」＋`MissingFile_IsSkipped`）。
  **warning はその契約を動かさずに沈黙だけ消す。**
  ★ **同じ commit 群で LSP の食い違いも閉じた**——**Problems パネルは*展開前*の木で
  validator を回していた**（プレビュー・export・playback は展開後）ので、
  **`using` で分割した曲は、それを描いている当の server から「その part は無い」と言われていた。**
- ✅✅ ★★★ **top-level でない `using` は第183 で閉じた**（`9fb978e2`・**LYS0029・ユーザー決定＝error**）。
  **§2F の起票は「診断ゼロで黙って消える／part ヘッダの位置は報告するので黙るのは section と score だけ」**
  だった。⇒ ★★★ **診断についてはそのとおりだが、*位置*は 5 綴り全部が壊れていた。**
  **トークンを裸の `Advance()` で消費すると木からその*幅*が消え、red の位置は green 幅の累積なので
  以降の全位置が左へ滑る。** **実測**（`section A { using "n.lys"  m { c4 d e f | } }`）:
  **木が 16 文字短く綴り返し**・**SVG の `data-pos` が `52,55,57,59,61`＝その行を消した本の offset そのもの**
  （真の音符は `68,71,73,75,77`）・**`check --pitches` が using 行の文字を音楽として報告**
  （`g`→C4・`n`→D4・`lys`→E4・`s`→F4）。**失う幅**＝section 本体 16・score 本体 14・form 本体 14・
  **part ヘッダ 14（LYS0025 を出しながら）**・音楽の中 14。**top-level だけが round-trip する。**
  ⇒ ★★ **報告することと保つことは別の修理**——**「N 綴りのうち 1 つは報告している」は
  「その 1 つは正しい」ではない**（RULES §5.0 の「1 つだけが壊れる形」の裏返し）。
  ★ **error にした理由**: **LYS0028 が warning なのは沈黙が*設計として宣言*され網で pin されていたから**。
  **置き場所の誤りにはその契約が無い**——どんなファイルシステムの状態でも意味を持ち得ない。
  ⚠️ **`HasUsings` は root の子だけを見る綴りのまま**（打鍵経路）。**directive は root の子より深くならない**
  はもう偽なので、**それを守っていた網を `EveryUsingHasUsingsSkips_IsReported`
  （root-children の問いが取りこぼすものは全部 error になっている）へ書き直した。**
- ✅✅ ★★★ **「Guards X」と名乗る fixture の監査は第190 で閉じた（16 冊測って未測定 0）**（起票は第183）。
  ⚠️ ★ **まず数え方**（§0）: 「21 冊」は再現しない。**`Fixtures\**\*.lys` で `guard(s)` を
  語境界で grep すると 27 冊**（**3 冊は「✔ VERIFIED TO GUARD」を自分で名乗る既済**、
  第183 が 8 冊、**第190 が 16 冊**＝**27/27 が測定済み**）。
  **手順は RULES §5.4**（X を 1 行毒して 1 回ビルドし、**全 fixture** を描いてハッシュ比較）。
  ⚠️⚠️ ★★ **読む数は 2 つ**——**名乗る本が動いたか**と**何冊でも動いたか**。
  **0 冊なら「その fixture は盲目」ではなく「毒が外れた」**（この便で 3 回起きた）。
  ★ **道具は `scratch\p190-incbench`**（⚠️ git 管理外）: `-- sweep <label> [listfile]` が
  全 fixture 219 冊（listfile を渡せば git 管理下 566 冊）を**プロセス内で**描いて
  ハッシュ CSV に出し、`-- cmp <a> <b> <claimant...>` が差分と名乗り本の判定を印字する。
  **1 サイクル（毒→ビルド→掃き→比較→revert）が約 20 秒**。
  ⚠️ **`cmp` は共通集合が空なら「0 冊動いた」ではなく「1 冊も比べていない」と言う**
  ——**掃きの命名を変えて base を取り直さなかったとき、静かに「0 動いた」と印字した**ので締めた。
  ★ **第190 の 10 冊＝全部「観測している」**（毒／動いた冊数）:
  **`alto-tenor-clefs`・`clef-positions`**（`basePosition` の alto/tenor case を落とす・5 冊）／
  **`keysig-cancel-naturals`**（`NaturalKernPadding` の 2 辺を入れ替える・2 冊）／
  **`mixed-meters`・`multivoice-voice2-tuplet`**（`AutoBeamCheck.EndsBeam` の
  beamExceptions 参照を飛ばす・34 冊）／**`voice-dynamics-multistaff`**
  （`AddDynamicsToSkyline` を即 return・4 冊）／**`script-stacking`**（`OrderByScriptPriority` の
  `ThenBy` から優先度を落とす・**1 冊＝唯一の観測者**）／**`volta-labels`**
  （`alt.DisplayLabel` を落とす・**1 冊＝唯一の観測者**）／
  **`fingering-lower-staff`・`fingering-articulation`**（`FingeringEngraver.Calculate` を空に・5 冊）。
  ⇒ ★★★ **収穫は `fingering-articulation` の札が*向きごと*嘘だったこと。**
  札は「articulation を運指の外へ押す LayoutEngine の後処理を守る」と書いていたが、
  **コードは逆向き**——**運指は自分の音符の script が*全部*置かれたあとに flush され**
  （`FlushFingerings` の最後の `int.MaxValue` 呼び）、**その flush が*数字*を script の外へ持ち上げる**。
  **札が名指す 2 site を毒しても 219 冊で 0 冊**、**数字の clearance（`move := 0`）を毒すと
  ちょうどこの 1 冊**。⇒ **札は本文に残したまま、訂正をファイル末尾に置いた**（下の ⚠️）。
  ⚠️⚠️ ★★★ **札はファイルの*末尾*に書くこと。** **音楽より上のコメントは以降の source offset を
  全部動かす**ので、**10 冊の header に 1 行入れた時点で snapshot が 10 枚赤になった**
  （実測）。**末尾なら 1 つも動かない**（同じ掃きで suite 緑）。
  ⇒ ★ **`fingering-articulation` の header の嘘を消すには snapshot 1 枚の再ベースが要る＝ユーザー決定**。
  ⚠️ ★★ **早い flush（script より先に運指を column へ入れる枝）は死んでいない、fixture が居ないだけ**
  ——**219 冊で 0・566 冊で 5 冊**（`audit/lpreg/{obs-probe,perf-fingstack1k,scriptstack1,slurscript-obs}`・
  `audit/lp-regression/lys/script-stack-order1`）。**「fixture で 0」を「死んでいる」と読みかけて、
  射程を広げて訂正した**（§5.4）。
  ⚠️⚠️ ★★ **別の所見＝`Staff.IsMultiVoice` は生きた消費者 3 つ（`TupletForceStemUp` ×2・
  `forceStemUp`）を持ちながら、`false` に固定しても*566 冊で 0 冊*動かない**。
  **`Score.IsMultiVoice` のほうは本番の読み手が 0**（`MultiVoiceRenderingTests` の 2 本だけ）。
  ⇒ **`multivoice-voice2-tuplet` の「梁」の半分は観測済みだが、「括弧の向き」の半分は
  この入力では説明できない**。**次に触るなら、括弧を下に置いている行を先に特定すること。**
  ✅✅ **残る 6 冊（`voice { } { }` 族）も同じ便で閉じた**＝`two-voice-polyphony`・`voice-tuplet`・
  `voice-dynamics`・`voice-dynamics-mid`・`voice-grandstaff`・`voice-mixed`。
  **`VoiceDefaults.GetDefaultStemUp` を全声部 null にすると 31 冊が動き、6 冊とも入る。**
  ⇒ ★★★ **この島の骨＝多声部の符尾方向は 2 軒ある**（§2A の顔）。
  **⒜ `GetDefaultStemUp`**＝**collector が `MeasureCollector.cs:2061` で item へ*書き戻す***（`StemUpOverride`）／
  **⒝ `GetDefaultStemUpAt`**＝**span で narrow して*生で読む*約 10 の消費者**
  （renderer・BeamDetector・Skyline・Dynamic・Trill・ElementCoordinator・Articulation…）。
  ⚠️⚠️ **⒝ だけ毒すと 7 冊動くが、この 6 冊は 1 冊も動かない**——**書き戻しの方から描いているから**。
  ⇒ ★★ **「毒が届かない」は「その fixture が盲目」ではなく「軒を間違えた」ことがある。**
  **同じ量に軒が 2 つあるなら、毒は*規則*に当てる（両軒の上流）。**
  ✅ **`voice-dynamics-mid` の「小節オフセット」も分離した**——`MeasureCollector` の
  **`_metadataMeasureOffset` を 0 に固定**（＝起票が名指す退行そのもの。sub-voice の
  per-note metadata が measure 0 へ落ちる）と **3 冊**が動き、この本が入る
  （他は `above-dynamics`・`voice-dynamics-multistaff`）。**27 冊とも主張が実測で裏付いた。**
  ⚠️⚠️ ★★★ **計器が 1 度嘘をついた——原因は改行**。掃きが `.lys` を*生読み*していたので、
  **CRLF で書き戻した 10 冊が無関係な毒で「動いた」ことになった**（`SvgSnapshotTests:721` は
  **わざと LF 正規化して読む**——data-pos をプラットフォーム非依存にするため。同じことを掃きも
  しなければならない）。**`.gitattributes` は `*.lys text eol=lf`**。
  ⇒ ★★ **`.lys` を書くツールは必ず LF で書き、読むときは `.Replace("\r\n","\n")`。**
  **suite は緑のままだったので、赤で気づく道は無かった**——**気づいたのは、動くはずのない
  単声部の本が「動いた」と出たから**（＝掃きの結果を*説明できるか*で読む）。
  ★ **第183 の済み 8 冊**（据え置き）: **`<X>Item.StaffIndex` routing を名乗る 7 冊**
  （arpeggio / articulations / dynamics / figbass-chordname / grace / trillspan / tuplet の各 `-lower-staff`）は
  **`MeasureCollector.cs:1583` の `_currentStaffIndex` を 0 に固定する毒 1 つで 7 冊とも動いた**
  ＋ **`grandstaff-high-bass`**（第183 で観測者にした）。
  ⚠️⚠️ **運指 2 冊の*譜ルーティング*の主張は第190 でも未測定のまま**——第183 が専用の毒を
  3 site 試して 3 つとも 0 冊（`FingeringEngraver.Calculate` の `staffIndex` は beam tip の
  索きにしか効かない／`LayoutEngine.FingeringStaffScores` はこれらの本では通らない＝
  `splittable` が偽で `wholeIslands` の枝へ行く）。**第190 が効かせた毒は運指を丸ごと消す形**なので、
  **譜どうしを区別できない**。⇒ **続きは「運指がどの譜に置かれるかを決める行」を先に特定すること**。
  **動く本を 1 冊見せるまで、どの結論も出せない**（§5.4）。
- ✅✅ ★★★ **`octave absolute` の trailing octave 記号は第184 で閉じた——ただし起票の半分は
  *嘘*で、その嘘は計器から来ていた**（`d3cbeced`＋第2便・**ユーザー決定＝動かす**）。
  **第183 の起票は「和音の `<c e g>'` も phrase 参照の `theme'` も診断ゼロで死んでいる（実測）」**
  で、**`GRAMMAR.md` にもその文言を「measured」の札つきで入れていた。**
  ⇒ ★★★ **和音は最初から壊れていなかった。壊れていたのは `check --pitches` のほう。**
  **絵から音名を読むと** `octave absolute` の `<c e g>'` は**符頭 y 13.85/12.85/11.85**
  （五線 12.35…16.35）＝**C5 E5 G5**、`,` は **C3 E3 G3**、**しかも相対の双子と SVG バイト同一**。
  **報告だけが 3 綴りとも C4 E4 G4 と言っていた**（分散和音では **C4 E5 G5** ＝*書けない和音*。
  root だけが壊れた経路を通っていた）。⇒ **原因は 1 軒 3 site**——`ResolveAbsolutePitch` が
  **解決しながら trace を書く**のに、absolute だけ**その戻り値に後から `ShiftOctave` していた**。
  相対は同じ shift を**解決の前に anchor へ足す**ので最初から正しかった。
  ⇒ ★★★ **教訓＝報告を通してしか測っていない「engine についての主張」は、報告についての主張。**
  **絵を読むのはハッシュ 1 つ。**（RULES §5.0 に汎化済み。**この計器が嘘をついたのは 3 便連続で
  3 度目**——第182 が多 part、第183 が using の幅、これが群の octave 記号。）
  ⇒ ★★ **phrase 参照のほうは*本物*だった**（絵も動かない）ので**動かす方へ実装**。
  **4 出力すべてに同じ穴**があり、**4 つとも同じ形で塞いだ**——「参照の記号は body を読む*枠*を
  動かす。相対では走っている枠、absolute では **anchor**（collector の `OctaveBase`・MIDI の
  `_partAbsoluteBase`・MusicXML の `_octaveAnchor`・双子の入れ子 `\fixed`）」。
  ⚠️⚠️ ★★★ **黙っていたのは 3 つで、双子だけは言っていた**（"the body is exported UNSHIFTED"）。
  **その警告は*挙動*については正しく、*規則*については間違っていた**——
  **`GRAMMAR.md` の PhraseRef と `GRAMMAR_FOR_LLM.md` はどちらも「記号は効く」と教えており、
  実装とコメント 1 つ（`EnterDefaultFrame` が不在を*設計*と宣言）だけが反対だった。**
  ★ **LP に訊いて決着**（双子の綴りから推論しない・RULES §5.0）: `\fixed c''` と
  `\relative c''` は **LP 2.26.0 で SVG バイト同一**、plain/`'`/`,` の 3 つは互いに別。
  ⚠️ **`'(N)` は元から absolute でも効いていた**（`theme'(3)` → E4 F4 G4 A4・両モード一致）。
- ✅✅ ★★★ **「置けないトークンが黙って消える」は第185 で閉じた——器は 3 つではなく 6 つで、
  そのうち 4 つは*報告していた***（`44e751c8`＋`6baa4ff9`・**LYS0030・ユーザー決定＝error**）。
  §2F の起票は「`ParseList`／`ParseMusicBlock`・呼び手は 3 つだけ＝島は小さい」だった。
  ⇒ ★★★ **器は section／form／score／music block／*トップレベル*／part ヘッダの 6 つ**、
  **沈黙は 4 つで、残る 4 診断は声を出しながら幅を落としていた**
  （LYS0016 2 文字・LYS0021 4 文字・LYS0025 14 文字・LYS0009 1 文字。
  **元から正しかったのは LYS0023 と LYS0027 だけ**）。
  **沈黙側の決着は等式**——`"oops"` を 7 文字入れた 4 冊が、**その行を削除した本と
  SVG バイト同一・data-pos 込み**で、`lysc check` は 4 冊とも `No errors found.`。
  ★ **着手条件は数え切ってから触った**（起票が要求していたとおり）: **宣言ノードを名指す
  35 ファイルすべてが種別で肯定選択**、しかも**3 つの器は元からトークンの子を持つ**。
  ★ **form の縦線は仕分けた**（ユーザー決定）: **平の `|` は不活性なトークン**
  （`BarlineSyntax` にすると `A | B` が 3 小節になる＝「空小節は `| |` の対」が効く）、
  **`||` などは彫る**（`blogger.lys` で複縦線が 1 個増えた）。
  ⚠️ **全数は 897 冊中 3 冊が新しく報告**（`scratch\p18*` のプローブを除いた数え方）——
  **全部ユーザー自身のファイルで、fixture・コーパス・showcase は 0 冊**。
  **⒜ 閉じ括弧 2 つ余分 ⒝ `b8,`（`,` が落ちて 1 オクターブ高く鳴っていた）
  ⒞ 存在し得ない section を form が参照**（宣言側は元から拒否・参照側だけ沈黙）。
- ⚠️ ★★★ **⑺ 記号の*後ろ*に書いた post-event が木では*前*に出る＝実害は第186 で止めたが、
  木の形は決定待ち**（起票 第185 → **第186 が 4 断定とも測り直した**。既知一覧 11 冊）。
  ⚠️ **起票の「最後の島・4 冊・data-pos は滑らない」は 3 つとも外れていた**（§1 の骨）:
  **⒜ 最後ではない**（`lyrics` のハイフンが網の射程外に居た。第186 で閉じた）
  **⒝ 4 冊ではない**——**木の 1021 冊で 55 冊**、**うち約 40 冊は `scratch\ベースタブLy` の
  ユーザー自身の曲**。**綴りは集中している**: **`c,,1~@mark("A")` が 34 冊**・
  `f,)\3`（スラー閉じのあとの弦番号）ほかが 18 冊。**git 管理下 563 冊では 11 冊。**
  **⒞ 「位置は滑らない」は半分嘘**——**後続は滑らないが、並べ替わった 2 ノード自身が嘘の位置を持つ**
  （`g4(@cresc` の `At` トークンが「`@`」と綴りながら 36 を名乗り、原文の 36 は `(`。
  真の `@` は 37）。**⒟ 「幅は落ちない」だけが正しかった**（**両綴りの SVG はバイト同一**
  ＝順不同の意味論は守られている）。
  ★ **家は `_pendingPostEventMarkers`**（`ParseMusicItem` 冒頭が「`ParsePostEvents` が順序を崩して
  消費したスラー/タイ/梁の印をここで再生する」と自称＝**再生の位置が post-event の後ろになる**）。
  ✅ **実害はもう止まっている**（第186 第3便 `56180f1e`・**ユーザー決定＝⒝ を先に**）——
  `PartSectionLayoutConverter` が **`Verbatim(source, node)`＝原文の切片**から運ぶようになった。
  **それまでは上書き保存するエディタコマンドが `c,,1~@mark("A") c,,` を
  `c,,1@mark("A") ~c,,` に書き換えていた**（`Top Gun Anthem` で実測）。
  ⚠️ **残っているのは「木を faithful にするか」＝設計決定**（作業ではない）。**着手条件は数え切ってある**
  （第186・**推論ではなく実測**）:
  **本番の消費者 17 か所・生存 12・生存はすべて SEQUENCE 読み**で、**順序が効くのは設計**
  （`MusicWalk.PeekMarkers` は音符の*後ろ*の連なりを掃く／`TieTargetScanner` は*直後*の項に結ぶ／
  `SlurPairingScanner` は項順のスタック）。
  ⚠️⚠️ ★★★ **`note.Articulations` から記号を読む 6 か所は*死んでいる***
  （`MusicXmlExporter` :1429/:1852/:1931/:2077・`MidiExporter` :1237・`LilyPondExporter` :1968-70）
  ——**1021 冊で 0 冊**がそこに記号を置く（**陽性対照は同じ歩き**: seq の Slur 26,399・Tie 7,862・
  BeamMarker 319・articulation 110,282）。**faithful にすると*この 6 経路が目を覚ます*。**
  ⚠️ **`editors/vscode/src/smartTyping.ts` に同じ順序契約の 2 つ目の実装がある**（TypeScript・
  `onInsertSlurOpen` ほか）。**歩調を合わせないと打鍵補助と木が食い違う。**
  ⇒ ★ **閉じれば `EveryBook_SpellsItselfBackOutOfItsTree` の既知一覧が空になり、
  round trip が parser の無条件の不変条件になる**——**この一族の検出器そのもの**（RULES §5.0）。
- ✅✅ ★★★ **`lyrics` のハイフンが隣の空白を落とす件は第186 で閉じた**（`afb6b5be`）。
  **`lyrics L { la -- la }` が木で `la-- la`＝幅 24 → 22**。**第183〈`using`〉・
  第185〈迷子トークン〉と同じ一族の 7 つ目の器**で、**起票すらされていなかった**——
  **`EveryBook_SpellsItselfBackOutOfItsTree` の射程外**（`audit\lpreg` 257 ＋ `samples` 6）に
  居たから。**射程は 300 → 566 冊＝git 管理下の全数**（`ca7060b8` ＋ `3b379a48`。
  床の assert も 250 → 566）。
  ★ **家は `ParseLyricSyllable` の 2 枝**——**どちらも 2 トークンから 1 つを組み直し、対の*外側*の
  trivia しか持たない**ので、**あいだの空白はどちらにも入らない**。**規則は「密着だけ貼る」。**
  ★ **値段**: 音符は 49 に立つのに **data-pos 47**。**A/B 563 冊＝絵 0・data-pos 3**（＝該当本ちょうど）。
  **絵が動かない理由は構造**: `LyricSyllableReader.Classify` が `la-`+`-` と `la`+`--` を
  **同じ音節の同じ Hyphen connector に畳む**。
  ⚠️ **綴りは 2 つ残した**（`la--la` → `la-`+`-` ／ `la -- la` → `la`+`--`）。
  **統一は「木は原文に忠実」という設計と衝突しない**——**畳むのは `Classify` 1 軒**なので
  2 つ目のモデルではない。**統一したいなら別の決定。**
- ✅✅ ★★★ **新規＝section を名指さない form が 0 バイトの絵を出していた件は第187 で閉じた**
  （`8c9d4dbe`・**LYS6007・ユーザー決定＝LYS6002 と同じ error**）。**⑻ の前提を測っている途中で出た。**
  **家は `SvgDocumentContext.Assemble` の `_pages.Count == 0` の裸の `return ""`**——
  `lysc svg` は **「Created: … Font embedded: Yes」と言って exit 0 で 0 バイトを書き**、
  `lysc check` は `No errors found` だった。**LYS6002 の doc は「空のページはレイアウト障害に
  見えるので出荷せず報告する」と既に書いている**＝**規則は在って、器 2 つのうち 1 つにしか
  当たっていなかった**（§5.2.1②）。**結果は blank page より悪く、page が無い。**
  ★ **射程は推測せず引き算した**——**`GRAMMAR.md` §StructureItem の項を全部列挙して
  engraver に通した**（単独＋実参照との組）: **46 形・0 バイトは 16 形・うち 15 形が LYS6007**。
  ⚠️ **「4 綴り」と書きかけたのを総当たりが 15 形に直した。**
  ✅ **16 形目（repeat の無い volta ending）も第188 で閉じたので、この族に開いている形は 0**
  ——**ただし塞ぎ方は LYS6007 とは別**（あちらは報告、こちらは**4 出力のうち 2 つを残りに
  合わせて彫らせた**＋LYS6008 の警告）。**下の volta の項に住所がある。**
  ★ **「section を名指すか」は `SectionReferenceFinder` に訊く**（**3 綴りを既に知っている家**）。
  ⚠️ ★★ **毒がテストの弱さを暴いた**——**plain `SectionReferenceSyntax` だけ数える毒に替えたら、
  volta の「緑であるべき」case が*緑のまま*だった**（その body に plain 参照も入っていたから）。
  **綴りを分離する body は `|: [1. A] :| [2. B]`。**
  ⚠️⚠️ ★★★ **全数 A/B が 1 回目に嘘をついた**（第184 の罠の再演）——
  **1025 冊で「114 冊が新しく報告」と出たが、掃きと*私のリビルド*が `LilySharp.Cli\bin` を
  共有していた**。**PID ごとにバイナリを写して測り直すと 1 冊**（`scratch\p185\name.lys`＝
  第185 の壊れたプローブで、**その form は本当に section を名指していない＝新しい診断は真**）。
  **fixture・コーパス・showcase・ユーザーの実ファイルは 0 冊。**
- ⚠️ ★★ **`_ "text"` の空白**（第185 起票・**第187 で前提を測り直した・ユーザー決定＝後回し**）。
  **`_"text"` は受理され、`_ "text"` は「Undefined section: '_'」になる。**
  ⚠️⚠️ ★★★ **起票の「空白を許すのか」は、選べる枝ではなかった**——**判定は parser ではなく
  `Lexer.cs:295` の 1 文字先読み**（`Current=='_' && Peek()=='"'`）で、**`_` は今も合法な section 名**
  （実測：`section _ { … }` は `No errors found`）。⇒ **`_ "shown"` は*その section への表示ラベル付き
  参照*として生きており、`_"shown"` の custom text とは*別の意味*で、両方通る**
  （実測：前者は 136051 バイトを描き、後者は描かない）。**空白を許すとこの対が潰れる。**
  ⇒ ★ **残る枝は 2 つ**: **⒜ GLUED を維持して診断だけ直す**（`_` が宣言済み section でないとき
  「密着させると custom text」と名指す）／**⒝ `_` を予約語にする**（`section _` は実測 0/1025 冊だが、
  **単独の `_` は `@fig(_ 6 4)` が使っている**ので `ParseFigures` の付け替えが要る・`figbass-empty.lys`）。
  ⚠️ **`~A "lab"` も `A "shown"` も空白で通る**＝**空白が意味を変えるのは `_` だけ**（実測）。
  ⚠️ ★ **2026-08-17・第194 で前提だけ再測**（**修理はしていない**）: **記述どおり生きている**
  ——`form main { _ "shown" }` は **`Undefined section: '_'`**、密着の `_"shown"` はそのエラーを出さない
  （**section を 1 つも名指さない form なので LYS6007 になる**＝別の器）。**項は動いていない。**
- ✅✅ ★★★ **repeat の無い volta ending は第188 で閉じた——「黙って消える」ではなく
  「4 出力のうち 2 つだけが消していた」だった**（`4dd1aedb`＋`bc168607`＋`8aca063c`・
  **LYS6008・ユーザー決定＝彫る＋警告**）。§2F の起票は「黙って消える／規則の形が決定」だった。
  ⇒ ★★★ **⑴ 「消える」は半分だった。** **MusicXML と双子は最初から平の参照として鳴らしており**
  （`MusicXmlExporter:340`・`LilyPondExporter:959`——**双子はその規則をコメントに書いていた**）、
  **ページと MIDI にだけ腕が無かった**。⇒ **同じ本が、どの出力に訊くかで別の曲だった。**
  ⇒ ★★★ **⑵ 規則は決定ではなく LP のものだった**（**訊くのは 1 コマンド**）:
  `\alternative { \volta 1 { … } }` を `\repeat volta` 無しで書くと **LP 2.26.0 は平の音楽と
  SVG バイト同一**（**括弧も番号も描かず・警告も出さない**）。`\repeat` を戻した対照だけ動く。
  ⚠️ **番号を書かない `\alternative { { … } }` は LP が警告する**が、それは**番号が無いこと**への
  警告で repeat の話ではない。**そちらだけ測ると設問ごと逆の答えが出る。**
  ⇒ ★★★ **⑶ 起票が書いた述語は弱かった。** 「規則は『その form に repeat block が 1 つも無い』
  でなければならない」——**`|: A :| B [1. B]` は repeat block を持ちながら同じく落ちていた**
  （`|: A :| B B` とインクも MIDI もバイト同一・実測）。**正しい述語はエングレーバ自身の条件＝
  「repeat block の中に居ない `FormAlternative`」**で、正規の `[2. C]` は
  `ParseFormRepeatBlock` の finalAlternative スロットに入るので**構文上 block の子**（実測）。
  ★ **述語は 1 か所だけに綴った**——validator が collector と MIDI と同じ問いを訊く。
  `TheWarnedEndingsAreExactlyTheOnesTheEngraverDoesNotBracket` が
  **1 冊（`|: A [1. A] :| [2. B] [3. B]`）で「彫る集合」と「警告する集合」が*互いに素かつ網羅***
  であることを主張する＝**2 つ目の綴りが生えたらここが鳴る。**
  ⚠️ ★★ **毒が 1 つ緑で返り、それが収穫だった**——`!IsInsideRepeatBlock` を外しても
  **FormVolta 一族の網は 1 本も赤くならない**のに、**ガードは効いている**
  （`|: A [1. A] :| [2. B]` のインク `86F66A44` → `63559F68`＝両 ending が二度彫られる）。
  **その穴のために網を 1 本足した**（`AnEndingInsideARepeatBlock_…`＝section の並びを数える。
  括弧の一覧では見えない）。**§5.4「どの case が赤くなったかで読む」の実例。**
  ★ **第187 が置いたピンが設計どおり鳴った**——`AVoltaEndingNoRepeatOpens_…AndStillEngravesNothing`
  は「閉じたらこの網は変わる。だから注記ではなく網にした」と自分で書いており、
  **suite で赤くなった 1 本がそれだった。**
- ✅✅ ★★★ **「誰も出さない診断コード」は第187 で 7 件とも引退した**（`8b365092`＋`688763c6`・
  **ユーザー決定＝7 件とも削除**）。§2F は **LYS1015 1 件**として起票していた。
  ⇒ ★★★ **「1 件」は的を絞った grep の数で、族の数ではなかった**——**実測は 96 宣言・89 に呼び手・
  7 件が誰にも名指されない**（`LYS0001` `LYS0005` `LYS1002` `LYS1003` `LYS1006` `LYS1015` `LYS2003`。
  **リテラル参照も 0**）。**LYS1006 が一番よく効く例**——**phrase は検査されている**が
  `SymbolReferenceValidator` が `UndefinedVariable` の本文（"Undefined variable or phrase"）へ
  畳んでいるので、専用コードは一度も鳴らない。
  ★ **見えなかった理由は構造**: **既存の網は全部「1 つのコード」を見る**ので、
  **誰も名指さないコードは、誰の網にも名前が出ない**。**集合を見るものが無かった。**
  ⇒ **`DiagnosticCodeTests.CodesThatNothingEmits_DoNotGrow`**（**名前の一覧・今は空**・
  ソースを読む＝`const string` はコンパイラが畳むので反射では追えない・
  **コメント内の言及は数えない**＝`StripComment`）。
  ⚠️ **副産物が本体より面白かった**——**`DocExamplesParseTests.FragmentCodes`
  （断片が出してよい診断の閉じた集合）に `UndefinedPhrase` が居て、*何も除外していなかった***。
  **死んだ除外は、無い被覆に見える。**
- ✅✅ ★★★ **`lysc check --pitches` が多 part の本で描画と違う音高を言う件は第182 で閉じた**
  （`ad358143`）。**RenderSpec 無しの素の collect** が section 内の 2 つめの part block に
  1 つめの相対鎖を引き継がせていた。**今は `Semantics.ResolvedPitches.ForFile` が
  全 score を*書かれた位置*で畳んで答える。**
  ★ **決着は絵で付けた**——**part block の順を入れ替えると報告は C6→C4 と動くのに SVG は
  文字単位で同一**（＝順序に依存しているのは報告のほう）。**値も絵で**：和音は五線の内側・
  加線ゼロ＝**C3-E3-G3**。
  ⚠️ **全数 97/300 冊で報告が変わった**（32 冊は**旧コードでは 1 音も出ず error だけだった**・
  9 冊は**繰り返しの畳み込みで短くなった**＝痩せではない）。
  ⚠️ **残っている射程の穴**: **どの score も描かない part は報告に出ない**
  （validator は見る＝5 拍の小節は警告する・実測）。**全*宣言* part を自分の clef から
  解決するのは collector の中の別の島。**
- ✅✅ ★★★ **「自分の名乗る機構を 1 ピクセルも観測していない fixture」は第183 で 2 冊とも閉じた**
  （`28b24b33`＋`23d7b600`・**どちらも LP 照合 → ユーザー承認 → 再ベース**）。
  **⑴ `test/grandstaff-high-bass`**：本文は「C4-E4-G4・加線を何本も引いて上の譜の音域に届く」と
  名乗り、音楽は C3-E3-G3 だった。⇒ ★★★ **主張は二重に外れていた**——**名乗りの C4-E4-G4 でも
  譜間は床（5.000）に座ったまま**。**実測**（上の譜の最下線→下の譜の最上線）:
  **C3 5.000／C4 5.000／C5-E5-G5 8.090**、**LP 2.26.0 は 5.000／5.000／8.095**（0.005 は SVG の 2 桁丸め）
  ＝**両 engine が床でも開いた値でも一致＝Lily# 側に欠陥は無い。壊れていたのは主張だけ。**
  **⑵ `test/accidental-octave-straddle`**：floored modulo `((p%7)+7)%7` のための本なのに
  **position 3,10,1,8＝全部非負**（truncating と一致する領域）を書いていた＝**`%` を裸に戻しても
  SVG がバイト同一**。⇒ **3/-4・1/-6 へ。★★★ 嘘の出所は論理網 `AccidentalOctaveAlignmentTests` の
  コメント**で、**正しい position を使いながら「それは `<eis' eis'' cis' cis''>` である」と書いていた**
  （実際は 3,10,1,8）。**fixture はそこから写されていた。両方を同じ commit で直した。**
  ⚠️⚠️ ★★★ **判定法は毒**（§5.4）: **その本が名乗る機構を止めて、その本の絵が動くか。**
  動かなければ観測していない。**`SeedStaffSymbol` を止めると論理網 4・snapshot 13・台帳点 約40 が赤で、
  この 2 冊はどちらにも居なかった。**
  ⚠️ **snapshot は再ベースできるので、この状態は網が 1 本も赤くならないまま何年でも続く**
  ——**2 冊とも論理の網を新設した**（`TheHighBassFixture_LiftsTheStavesOffTheirFloor`・
  `TheStraddleFixture_StillStraddlesTheMiddleLine`。どちらも fixture をディスクから読む）。
  ★ **「同型を疑う」は全数で掃いた**（第182 の宿題）: **fixture 219 冊のうち音名を名乗るのは 21 冊、
  候補 9・偽陽性 7**（section ラベル `G1`/`A2`・ベースの調弦 `E1-A1-D2-G2`・`c,`＝C3 という文法の説明）
  **・本物 2 冊＝上の 2 冊**。⚠️ **掃きは checker にしていない**——**名乗った音名と section ラベルを
  機械が見分けられない**（§5.2.1⑦）。**再掃きはコメント行から `[A-G](#|b)?[0-8]` を集めて
  `check --pitches` の解決集合と突き合わせ、出た候補を*人が読む*。**
- ✅✅ ★★★ **score 単位の `transpose` が 3 通りの答えを返す件は第182 で閉じた**（`de4acbd0`）。
  **原因は 1 行**——`PartTranspose.ReadScoreDefault` が「part の中でない `transpose`」を
  ツリー全体から拾うので、**score の transpose を*ファイルの既定*として数えていた**。
  ⇒ **宣言した score は 2 回**（既定＋自分の `RenderSpec.ScoreTranspose`）＝長3度、
  **他の score は 1 回**（頼んでいないのに D 長調で彫られる）。**両方とも同じ 1 行**なので
  **ガード 1 つ**（render 宣言の中の transpose はその score のもの）で消えた。
  ★ **3 綴り（part ヘッダ・top-level・score 単位）が `d`＝長2度で一致した。**
  ⚠️ **毒（ガードを外す）で赤になるのは新しい 4 本だけ・既存 5061 本は 1 本も落ちない**
  ＝**この欠陥が生き延びていた理由そのもの**。
- ✅✅ ★★★ **`lysc ly` が `transpose` を 3 綴りとも落としていた件は第194 で閉じた**（`c24a2334`）。
  **part 変数を `\transpose c <target>` で包む**——**`\relative`/`\fixed` の*外側***
  （LP は書かれた音高の相対を解いてから移す＝collector と同じ順）。
  ⇒ ★★★ **起票の「綴りの設計判断が要る」は誤りで、決定ではなく LP のものだった**
  （第188 の骨の 2 例目・**訊くのは 1 コマンド**）。**実測（LP 2.26.0）**:
  `\transpose c d` で包むと **LP はページが解決する 10 音をそのまま読み**、
  **KeySignature の `alteration-alist` が `()` → `((0 . 1/2) (3 . 1/2))`＝C major → D major**。
  **fixture 自身が「Verified against LilyPond \transpose c d」と書いていた**（散文なので測って裏を取った）。
  ⚠️ **「fixture 3 冊」は 4 冊**（`transpose` / `-down` / `-multistaff` / `-score`）。
  **双子 A/B は 566 冊中ちょうどその 4 冊**が動き、他は 1 冊も動かない。
  ⚠️ **`\drummode` に巻いても LP の SVG はバイト同一**（測定済み）＝**drum の特例は要らない。**
  ⚠️ **残っている射程の穴は移植の穴ではない**——**双子が書くのは*最初の score* だけ**なので、
  **2 つ目の score が別の transpose を宣言する本は双子に一度も現れていない**
  （**exporter が 1 score しか出さないことの帰結**。**そういう本がツリーに在るかは未測定**）。
- ★★ **⑷ 新規＝`lyrics NAME` の NAME が voice を名指し損ねると黙って第1声部へ付け替わる**
  （第181 実測・**要決定なので実装していない**）。`lyrics allt`（`alt` の打ち間違い）で
  **音節の x が `part@24.9 → 17.3`・`deep@27.9 → 24.9`** と動き、**診断は 0**。
  絵は出るので**空のスコアや消える行とは違う**＝**0.3.0 の門ではない**。
  ⚠️⚠️ **「voice を名指せ」に締めてはいけない**——**ツリーの名前付き `lyrics` ブロック 40 個のうち
  `voice NAME {` と一致するのは 1 個だけ**（もう 1 個は `voice` キーワード無しで書かれている）。
  **リードシートでは voice に対応しないのが普通**で、**第1声部への fallback は設計**
  （`test/named-voice-lyrics.lys` の冒頭が明記）。
  ⇒ ★ **成立しうるのは「*名前付き voice を持つ part の中で*、どの voice も名指さない
  `lyrics`」だけ**——**実の本 0 冊**。**規則の形（error か warning か）が決定。**
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
> ✅ **una corda の積み順**（2026-08-18・第206 で閉じた・`e6eeb280`）。**推測は逆端だった**——
> **LP は una corda を最も譜に近く置く**（`audit/lp-geometry/probes/pedal-three.ly`・
> 五線下端から **una corda 2.777500 / sostenuto 4.738700 / sustain 7.181300**）。
> ⚠️ **計器が知らない数を再現したのが裏取り**: sustain−sostenuto が **2.442600** で、
> **第204 がペアだけから測った 2.443** と一致した。
> ⚠️ **「Ped.」はテキストではなくグリフ**（`lily/sustain-pedal.cc`）なので、
> **`<tspan>` を数える計器は 2 段を 3 段と報告しかける**——単独ペダル 3 スコアでラベルを同定した。
> **残っているのは段の*間隔***: **LP は 1.961 → 2.443（各段の実インク）・Lily# は一律 2.46**。
> **順序だけ移植した。台帳点は無い**（§1 ⑻ ⒝）。

> ## ✅ XML doc の警告（2026-08-11・第135 起票／**2026-08-18・第199 で閉じた**）
>
> **283 件のうち欠陥カテゴリを全部 0 にし**（CS1574 26／CS0419 27／CS1570 18／CS1734 11／
> CS1587 6／CS1572 5／CS1571 4）、**`GenerateDocumentationFile` の Release 条件を外した**
> （`729c5ec9` ＋ `bb0811ab`。経緯は §1 第199、汎化した学びは RULES §5.1）。
> **残る 184 は CS1573 178 ＋ CS1591 6＝*不足*で、csproj の `NoWarn` に入っている。**
> ⚠️ ★★ **`NoWarn` から外すなら先に測り直すこと。欠陥を黙らせるために足さないこと**
> （**理由と数は `LilySharp.Core.csproj` のコメントに書いてある**）。
> ⚠️ **書き足すなら doc そのもの**——**184 件は「public に doc が無い」「`<param>` を
> 一部しか書いていない」で、直すたびに減る。ラチェットではないので誰も落とさない。**
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

- ✅ **`DrawingTransform.Identity` は第210 で閉じた**（`6cbe39d9`）。**`new()` が record struct の
  パラメタ無しコンストラクタ＝主コンストラクタの既定値を適用しない**ので `ScaleX/ScaleY = 0` だった。
  ⇒ ★★ **起票が「記録用コンテキストの作者を 2 人捕まえた」と書いていたのが要点で、
  その 2 人は回避策として `new(0,0,1,1)` を手書きしていた**＝**2 つ目・3 つ目の綴り**。
  **property を直して 2 軒ともそれを読むようにした**ので、**記録用コンテキストが 2 実装ある件
  （下の項）はまだ 2 実装だが、*単位変換の綴りは 1 つ*になった。**
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

- ✅ **歌詞の列間隔の発明 2 つ＝第222 で移植完了**（`b5a973ee` 起票 → `c7beded3` 移植）。
  音節は **描画サイズ（`LyricTextFontSize` 2.469417）の advance** で測り（LP の LyricText の
  X extent は Pango LOGICAL rect＝1200dpi 量子化つき advance・pango-font.cc:351-362）、対の間隔は
  **語間 0.45（LyricSpace）／語内ハイフン 0.1（LyricHyphen）を `ConnectorType` が選ぶ**。
  予約は**歌詞 LINE ごと**（`GroupByLine`/`ReserveLyricLine`＝LP の Hyphen_engraver が
  context ごとに 1 本なのの鏡）。台帳 `lyrics.column.*` 6 点: word/hyphen/no-bind/control
  **exact**・narrow **+0.034143307＝Pango pixel 1 個ちょうど**（顔の kern データ差:
  Schola "ru" +20 vs C059 +18・§3 の 2026-08-02 計量双子族の**水平初 member**）。
  **残っていた 1 点「小節線またぎ」も第223 で閉じた（下の ✅）＝lyrics.column 全 6 点が
  exact か named**。
  ✅ **row vs sings の音節 X ドリフト（第221 ⓐ）はこの移植で解消**——音節数＝音符数の本では
  両綴りの音符格子・音節 X とも一致（第222 実測・F2 同一。旧ドリフトは列あたり ≈0.587）。
  音節数≠音符数の小節は**文法上別音楽**（row は均等割り・LILYSHARP-OWN）のまま＝欠陥ではない。

⚠️ このとき **§2H の旧記述は 2 つとも外れていた**ので、同じ推論を繰り返さないこと:

- 「Lily# の最小は **0.2 広い**」→ 圧縮域では **0.2521 狭く見えた**（加線の混入）。実際は
  rod で **+0.1** ちょうど。**加線のない音高で測ること**
- 「snapshot 24 枚が動くのに台帳は 1 点も動かない」→ **鍵になる点が無かっただけ**。
  圧縮 regime の点（`compressed.note-to-note.quarter`）を開いたら正当化できた。
  ⇒ **鍵が無いのは「移植できない」ではなく「まだ測っていない」**

**残っている発明**:

- ✅ **歌詞の小節線またぎ＝barline-split モデル＝第223 で移植完了**（`8cdeca0e`・
  `lyrics.column.word-gap.cross-barline` **+0.540000000 → 0.000000000 ちょうど**）。
  **LP は音節と小節線のあいだに何も予約しない**（extra-spacing-height (0.2 . -0.2)）——
  継続する歌詞線は halves（0.4＋bar ink＋0.4）を落とし、`CrossBarLyricRodDistance`＝
  inkR＋0.45/0.1＋inkL−bar ink を rods リスト（ApplyRods）へ。**門は各小節の
  `LyricBarPricing` 半分を持ち、KP の cumPairMin が break 時に結合**（結合済み excess は
  4 鍵量で memo の 3 鍵窓に入らない——§1 第223 骨 1）。**halves の残り＝行の最初/最後の
  音節 vs 小節線・折りで千切れた対の行頭床/行末予約は LILYSHARP-OWN のまま**
  （未測定 regime＝LP の折り行末 hyphen 予約を含む。測るなら `\break` 対から）。
  ✅ **在庫だった「多声小節の byItem 予約写像」は第225 で閉じた**（対 `04b40fb1`・移植
  `c3b0b32f`＝byItem 分岐退役・全予約 TIMING 列。行歌詞の均等スロット ItemIndex も同欠陥
  だった＝sweep 3/569）。✅ **⒥ スパン予約の rod 化は第226 で閉じた**（対 `558920fd`・移植 `e3a190dc`＝
  中間列を跨ぐ音節間予約を ReserveLyricLine → ApplyRods の真 range rod に。width/first-gap
  とも exact 着地・隣接 bump と halves は据え置き）。**残りは §1 ⒦**＝歌詞 sliver 族
  （voice-blind な列表 +0.0366／+0.0732・観測者は lyrics.column.bound-voice.primary-control /
  no-bind.skip-gap / bound-voice.skip-gap）。
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
  （probe = `audit\lpreg\mult-{probe,ctl}.lys`）。⇒ collisions.lys の v3/v4/v5 の
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
| ★★★ **staff-less リードシートは grid 行に拍子を刷る（意図的乖離）＋行の繰り返し記号は型を保つ**（2026-08-20・第226・**ユーザー決定**・✅ 同便実装） | ユーザー指示 3 連「歌詞のみ／歌詞＋コードのリードシートにセクションと拍子を表示」「歌詞とコードのリードシートに繰り返し記号も」「コードのみも同じように」。実査: **セクション枠は全変種で既に表示**・繰り返しは music 経路の行（chords）では既に出ており、**歌詞行だけが barline トークンの型を落としていた**（修理＝LyricsCollector が `ParseBarlineType` の 1 表で型を運ぶ・`|:` は「この小節を素で閉じ次を開く」＝music の HandleBarline と同意味論・`:|`×`|:` の RepeatBoth 畳みも同型）。**拍子は LP 実測と逆向きの意図的乖離**——LP は ChordNames/Lyrics だけの系に meter 幅を予約しない（staffless-system.ly CO/CO3 実測）。第226 で**台帳 2 点を退役**（`staffless.line-start.meter-identity`・`chords-vs-staff`＝decided divergence は台帳の外・歌詞行バンドの前例。LP 数値は probe ヘッダと git に残る）。実装＝`AnyStaffEngravesTime` に lead-sheet 条項（**1 述語で門・layout・renderer が自動追従**）・描画は grid 行の `SolvePrefixColumns.TimeX`（予約と同じ 1 導出）・行頭 spring は「meter で終わる staff と同じ wish」（LineStartColumn の empty-wishes 分岐に HasTime 枝）。**残**: mid-piece の `time` 変更は行 voice に change item が無く**幅もインクも出ない**（退行ではなく非表示・⑸ 小粒に起票） |
| ★★ **マーク＋テンポの縦積みは LP 忠実のまま。横並び案は「全面 LP 忠実の後」に再検討**（2026-08-20・第220・**ユーザー決定**） | ユーザー所感「Verse とテンポが近すぎる・Y を揃えて横並びが良いのでは→LP 双子と比べて」→実測: **LP 自身が縦積み**（テンポ底 2.850＝譜 2.05+padding 0.8・マーク底 6.504＝テンポ頂+0.46 の outside-staff-padding）で、**Lily# は桁まで一致済み**（2.88/6.90）。決定＝現状維持・横並びは意図的乖離になるため全面 LP 忠実の達成後に改めて検討。⚠️ 副産物: **マークの X が LP と約 2 ss 違う**（LP は箱の左端を拍子記号の位置 3.365 に・Lily# は行頭寄り 1.4＝scratch probe mark-tempo.ly 実測）——これは乖離ではなく未移植候補。⑸ の小粒に起票 |
| ★★★ **`lines` は part ヘッダから score 項目へ＝`staff m as lines N`**（2026-08-19・第217・**ユーザー起案・決定**・✅ 同便実装） | ユーザー「part {} の中に lines を置けるのはいまひとつ」＋綴りもユーザー原案どおり。線数は LP でも `StaffSymbol.line-count`＝layout 側で、part 持ちだと総譜 5 線／リード譜 1 線の併記が綴れなかった。`as` は `chords … as roman`・`tab … as numbers` と同族の「この帯をどう刷るか」で、part 名直後の裸語＝表示ラベルとの曖昧を断つ。**ossia にも許可**（ユーザー決定＝リード譜の繰り返し 2 回目のリズム等）。part ヘッダの `lines` は既存 unknown-property 網が一覧ごと自動で正しく拒む（新 code 0）・値域チェックは parser が**同文・同 code**（UnknownSymbolCase）で継承・pair テストも score 側へ移設。移行は rhythm-slashes 1 冊＝snapshot 正規化恒等を機械確認。tab は対象外（線数＝弦数）。同族の removeEmpty/pedal は **⒣（§2F）に別便起票**（ユーザー指示）。⚠️ 学び 2 敗: GRAMMAR.md の生成規則内コメントに `;` を書くと DocKeywordListTests が塊をそこで切る——注意書きをブロックに残した |
| ★★ **`with` はキーワードごと退役＝LYS0031 も除去し、補完にも出さない**（2026-08-19・第217・**ユーザー決定**・✅ 同便実装） | ユーザー指示「with は自動補完でリストされないようにして。LYS0031 のエラーコードは除去して」。移行は全書済み（追跡 569 冊＋ユーザー楽譜 9 冊＝同便前半）で網に獲物が残らないため、**LYS8007／`font` と同じ形でキーワードごと退役**——旧綴り `staff m with lyrics ja` は「表示名 "with"＋行」と読まれ fold する（`ScoreRowFoldingTests` がピン）・tab の旧綴りは generic 網（Undefined part: 'with'）。⚠️ **道連れの発見: tab の `with chords` は第216 の除去から漏れて*黙って通っていた***（`Parser.Form.cs` の分岐が残存・実測）——退役で一緒に消えた。退役番号は DiagnosticCodes 台帳・GRAMMAR.md・テスト remark の 3 箇所に同文（英語の歌詞・名前としての `with` は合法化）。**補完の `with` の出所は LSP ではなく VS Code の word-based suggestions**（LSP は `with` を一度も出していない・実査）＝`[lilysharp]` 既定で off（package.json）・tmLanguage の keyword 列からも削除。**要再配備**: deploy-extension.ps1 |
| ★★★ **`with lyrics`/`with chords` は除去し、score は「帯の縦列」に統一する**（2026-08-19・第215・**ユーザー決定**・✅ **第216 実装＝`6d6d1b92`→`5d7a8ef1` の 4 commit**） | ユーザー起案「縦に積むだけで意図する楽譜を書けるはず」。関連は定義（`sings`）・位置は並び順・吸着は fold（隣接する bound 行＝その譜の verse）＝**LilyPond 自身のモデルに収束**（LP に `with lyrics` は無い）。構文破壊なので**初タグ前が唯一の無料期間**にやり切った。除去は LYS0031・経緯は §2F ✅ |
| ★★★ **無名 `chords {}` は縦積みに畳む＝除去**（2026-08-19・第216・**ユーザー決定**・✅ 同便実装＝LYS0032） | 問いは「残すか畳むか」（第215 起票 ⑷）。技術評価を示して 1 問: 無名形の関連は「併記」という*推論*で、複数パート section では staff 0 固定のハードコード＝関連先がどこにも書かれていない。決定＝畳む。「名前を付けて score に置け」をエラーメッセージが綴る。既存 6 冊は命名＋行配置へ移行済み |
| ★★★ **歌詞は定義側で自分のメロディに結びつく＝`sings`**（2026-08-19・第215・**ユーザー決定 3 件**・✅ 同便実装＝`5fc6f788`） | 要件はユーザー起案:「歌詞は必ず専用メロディに関連づく（歌詞メロディは省略可）・**並べた別パートのメロディには関連づかない**・同じメロディに複数の歌詞（多言語・替え歌）」。決定: ⑴ **0.3.0 の前に入れる** ⑵ 綴りは **`sings`**（`lyrics ja sings vocal { }`） ⑶ unbound トラックの `with lyrics` 添付は**即エラー**（初タグ前が唯一の無料期間。fixture 15・samples 2・docs・XML importer の出力まで全て `sings` へ）。**score の `lyrics NAME` 行は、束縛先を刷らずにそのリズムで音節を置く**（LP の `\lyricsto`＋NullVoice 相当——メロディをサブ収集し、全 item を同じ長さのスペーサーに置換した骨格が行の声部＝時刻が実在の列になる）。voice の名前一致束縛は存続・unbound 行（リードシート）は均等割りのまま。網は LYS6009/6010/7004/7005＋`SingsLyricsTests`・snapshot `test/sings-chorus-row`・変換器は sings を両方向運搬 |
| ★★ **score の行でも `sings` を綴れる**（2026-08-19・第218・**ユーザー名指し**・✅ 同便実装＝`98bbfddd`） | `score { staff melody  lyrics verse sings melody }`——行が束縛を**表明または再表明**する。**同じ 1 つの track 属性**で、置き場所が 2 つあるだけ（読みは `LyricBindings.BindingOf` の 1 軒・LYS7004/7005 も共通・同一の再表明は無音）。従来この綴りは `sings` が次の render 項目へ落ちて Undefined part になっていた。補完は AfterLyricsRowAttachName 族 |
| ★★★ **裸の duration は直前イベント（音符・和音・スラッシュ）の反復**（2026-08-19・第215・**ユーザー決定**・✅ 同便実装＝`c6b9fb32`） | LP 2.20+ の isolated duration と同義（**LILYPOND-REF: parser.yy music_embedded**・2.26.0 でバイト一致 3 種実測: 音高反復・**和音は丸ごと**・休符透過）。`bes8 8 8 8` のベース刻みが動機。**代償を承知で決定**: `c 4` の LYS0016 誤字網のうち「時間が狂う類」は bar check が受け、**「音高の書き漏らし（`4 g f e`）」は黙って反復になる**——LP が同じ綴りに払っている値段。LYS0016 は「反復先が無い」だけに残る。arpeggio は走行を**断つ**（曖昧のまま黙らせない）・`[`+整数+`.` は volta 優先（付点で走行を開くなら `[/4.` と綴る・ピンは `BareDurationTests`） |
| ★★★ **`/` はスラッシュ音符＝§3 記号規則の意識的例外**（2026-08-19・第215・**ユーザー提案・決定**・✅ 同便実装＝`c6b9fb32`） | 「記号は LP が既に記号で綴っているものにだけ」に対し LP は `/` と綴らない（`\improvisationOn`）。それでも採ったのは**トークンが印字されるインクそのもの**だから（`|` が小節線を描くのと同族）。中央線固定・無音・音価どおりの符尾連桁・`lines 1` と合成で一線リズム譜。`time 4/4`・`tuplet 3/2`・`c/g` の `/` は文脈が別で不干渉。双子は `\improvisationOn`＋**clef の中央線音高**で書き、LP 側フレームだけ進めて次の実音で補正（既存の乖離フレーム機構） |
| ★★★ **0.3.0 は未署名のまま出す**（2026-08-19・第215・**ユーザー決定**） | 署名はどの経路（Azure Trusted Signing／SignPath OSS／OV 証明書）でも**ユーザーの資産＋本人確認で日〜週単位**かかり、0.3.0 がそれに吊られる。CHANGELOG の Known limitations が回避策（ブロック解除 or `dotnet lysc.dll`）を記載済み＝**その行は消さない**（第214 ⑶ の宿題 ⒝ はこれで確定）。署名を入れるなら 0.3.x で `release.yml` に配線し、そのときに行を消す。⚠️ **SAC Enforce の機械では回避策側も SmartScreen と挙動が違う**（第212 実測: ブロックは時間で解ける・判定はパスでなく内容） |
| ★★★ **移調 clef の part には MusicXML の `<transpose>` を書く**（2026-08-17・第196・**ユーザー決定**・✅ **第197 実装＝`465fe58f`**） | MusicXML の `<pitch>` は「書かれた音高」で written→sounding は `<transpose>` が与える、という規約に従う。⚠️ **`MusicXmlReader` の注記が逆の読みを明記している**ので**同時に直す**——**片方だけだと round trip が 2 オクターブ落ちる**。**SVG も MIDI も動かない**（実測・snapshot 不動）。⚠️ **起票時の「射程 44 冊」は綴りの grep で、実測は 20 冊**（第197・§2F ⒜） |
| ★★★ **`repeat unfold N` は「N 回鳴らす」＝各コピーは同じ音**（2026-08-17・第196・**ユーザー決定**・✅ **第197 実装＝`85f52dcf`**） | percent / tremolo に第195 が入れた「各 pass はその 1 コピー」と同じ規則へ揃える。⚠️ **起票時の「snapshot が動く」は外れ**——**566 冊に実サイトは 1 つ（`canon-in-d` の phrase 参照）で、snapshot は 1 枚も動かなかった**（第197 実測。**射程を数えるのは着手の*前*でよい**） |
| ★★★ **2 つ目以降の score は「警告 ＋ `--score` / `--all` を midi・xml・ly にも足す」**（2026-08-17・第196・**ユーザー決定**・✅ **第197 実装＝`ab755a71`**） | ページが既に `--all` / `--combined` / `--score NAME` の 3 入口を持つので、その家に揃える。**出力パスを明示したときに勝手に 2 つ目を書かない**のが警告つき既定。⚠️ **`--combined` は入れていない**——**1 枚に積むのはレイアウト、続けて収めた `.mid` は別の曲**（LP も 2 ファイルを書く・実測） |
| ★★★ **小節途中の `clef` は相対オクターブの anchor を付け替える（ページが規則）**（2026-08-17・第196・**ユーザー決定**・✅ **同じ便で実装＝`65e54d69`**） | **part ヘッダの clef が既に anchor を決めている**ので、「clef という 1 語が 1 つの意味を持つ」ほうを採る。⇒ **MIDI と MusicXML が実装し、双子は補正した綴りを書く**（`transpose` の前例＝第194）。**LP 2.26.0 で裏取り済み・snapshot 不動・3 冊とも閉じた** |
| **`SystemBreaker` の再入可能化は入れない** | LP はページブレーカーが行分割を選ぶ（`optimal-page-breaking.cc:139-173`）が、入れると F3 の tier-1 skip の健全性論拠が壊れる（break 解が縦の関数になり、gate を計算するのに gate が守る結果が要る＝循環）。⚠️ **判断し直すなら順序は「①まず頻度を測る（コード変更ゼロ）→ ②有意ならオプション分離＋一致不変条件テストとセットで」**。性能が理由ではない |
| **臨時記号の糖衣 `c?` / `c!` / `c??` は入れない** | `!` は点線小節線トークン。`c?` 単独では `!` の罠への導線を作る。痛みは `@courtesy`/`@editorial` の専用エラーで解消済み |
| ★ **記号（sigil）は LP が既に記号で綴っているものにだけ使う。Lily# 固有は全部 `@name`** | 上の決定から出た一般則。今後の記号追加はこれで判断する |
| **休符の実インク化はやらない** | 実測で棄却。休符は中央線に座るので縦インクが極値にならず、LP でも 1 ビット違わない＝箱が名目なのは事実だが**不活性** |
| **単一ページは紙面サイズにしない**（意図的乖離） | Lily# は 1 ページに収まるスコアを内容サイズで出す（明示的な設計）。台帳に載せると total が ~109 になり指標が壊れるので**載せない** |
| **本数（count）の点は ss の総和に入れない** | 距離ではないから（`unit` フィールドで分離） |
| ★ **セリフ体は TeX Gyre Schola のまま同梱する。LP の C059 には合わせない**（ユーザー判断・2026-08-02） | **量を測ったうえでの決定**。LP は `"LilyPond Serif"` を **C059** に解決し（`ly:stencil-expr` がファイルパスごと吐く）、C059 は **AGPLv3**（URW の例外は PS/PDF への埋め込み限定で**フォントプログラムの同梱は覆わない**）。**両者は advance は完全一致するが、カーンと合字が違う**: カーン値は **471 有効ペア中 438 が食い違い**、丸め後に予約幅が変わるのは **2 文字組の 11.2%（475/4225）**。合字は**両方とも合字にするがグリフ幅が違う**（`ff` 605 対 686＝5px、`ffi` 878 対 904、`fi` は一致）。**現実の文字列で 0〜4px＝0〜0.137 ss**（`Violoncello` が最大・`Allegro` +1px・`Ave verum corpus` は 0）。⇒ **0.03〜0.14 ss の恒久差**を受け入れ、**AGPL を持ち込まない**。⚠️ **帰結**: `text.width.{aa,va}` は**永久に非ゼロ**（原因は台帳に完全記述）、**今後テキスト幅の点は 1/9 の確率で非ゼロで開く**、そして**紙面そのものが LP と字送りで違う**（測定だけの話ではない）。⚠️ **測っていないのは regular/bold/bold-italic 面**（italic だけ全ペア走査した）。★ **第221 追記＝垂直 ink の初 member**: **i/j の点だけ 0.135818 ss 違う**（C059 1.765633 / Schola 1.629815・**h/x/g/p は bit 同一**・fontTools bbox 実測）。歌詞床が i/j 頂点の音節で縛る本は恒久差——最初の顔は台帳 `lyrics.band-floor.staff-to-lyric` −0.092。★ **第222 追記＝列間隔に出た初 member**: **"ru" の kern が Schola +20 / C059 +18**（fontTools GPOS 実測）で、per-glyph hinting が 2/1000em を **Pango pixel 1 個（0.034143307 ss）**に増幅——台帳 `lyrics.column.word-gap.narrow` が恒久 +1px（この行の「丸め後に予約幅が変わる 11.2%」の列間隔側の最初の顔。mum/nu は kern 対を持たず exact）。★ **差し替えは後からできる**——`TextFontMetrics.SerifFamily` と `Fonts/` とライセンス表記だけで、対照本 `TS1`/`TS2` が効果を即座に示す |
| **LP の「正」は 2.26.0** | 版で PUA コードポイントも Emmentaler も動く。**必ず feta 名で引く** |
| **cross-staff beam は skyline から除外**（LP の字面） | `axis-group-interface.cc:850-858` の LP 自身のコメント。Lily# の「固定 3.5 stem を残す」は発明だった |
| ★ **和音記号は LP に合わせる＝中心合わせしない**（ユーザー判断・2026-07-25 明示） | 意図的乖離かを問うたうえでの決定。`ChordName` は X-offset も self-alignment も持たない（`define-grobs.scm:837-855`）＝ink 左が列。`dcbf08e9` で移植し `staffless.line-start.chords-vs-staff` が閉じた。⚠️ **和音グリッドは別 grob（`GridChordName`）で LP も中心合わせする**が、中心を取る相手は小節の四角。Lily# に四角は無いので chords-only シートは ChordName 経路のまま＝**「グリッドも直す」で触らない** |
| ❌ **撤回（2026-07-27・ユーザー判断）: 独立 lyrics 行を「譜のような帯」として置く** — **もう決定ではない。蒸し返し禁止の対象から外れた。** | **旧決定**（2026-07-26）: 独立行は「譜に付く歌詞」ではなく**リードシートの word トラック**なので譜グループとして置く＝**9.600000 対 LP 5.500000＝+4.100000**、台帳には載せず導出形で主張。**撤回の理由**は「間違いだったから」ではなく**射程が二度狭まって残らなかった**から: ①2026-07-27 に「鎖に参加しない」部分が `lyrics.chord-row.between-systems.*` の実測で落ち、②同日 LYRR/LYRRV が **LP 側の恒等を 59 行の機械差分で確定**させ（`\lyricsto` の有無で LP は 1 行も変わらない）、**残っていた「距離」も Lily# 単独の量**だと分かった。⇒ 行は `nonstaff-relatedstaff-spacing` で自分のインクから置かれる（`dee2c045` 系）。**いまの状態**: `lyrics.row.staff-to-lyric` は**台帳点で exact**、`LyricRowIsSpacedLikeTheLyricsContextItIs` が**2 つの綴りが一致すること**（＝LP の恒等の再現）を主張する。⚠️ **帯そのものは残っている**——行は自前の小節線を持ち verse を band 内に積む（`LyricRowBaseline` は `LILYSHARP-OWN` のまま）。**消えたのは「どこに置くか」だけ。** ★ **2026-07-28 に鎖にも入った**（§1 の第20セッション）。**帯そのものはまだ残る**が、
system の最後の spaceable 譜の下に立つ行は **verse ごとに鎖の要素**で、帯の上端は解に従う。
`lyrics.row.two-verse.verse-step` は exact、LYRRV ≡ LYRV|
| ★ **タブの*和音*のタイは LP の広げ方に譲る**（ユーザー判断・2026-08-16 明示） | **LP をタブ側で直接測ってから決めた**: `<c' e' g'>2~ <c' e' g'>4` の TabStaff で LP 2.26.0 は **dir = −1, +1, +1**（TabNoteHead・staffpos 1/3/5＝**一番下の弦は数字の下・上 2 本は上**。双子に `Tie.direction` を吐かせた実測）。⇒ 旧「タイは stem と反対側に固定」という Lily# 独自規則を**和音では通さない**。★★ **実装は規則を書き直していない**——`TieFormattingProblem` の中に既に移植済みだった `set_ties_config_standard_directions` を static に出し、**タブが*自分の* staff position** （`TabStaffGeometry.StaffPositionOfString` ＝ LP の `tablature-position-on-lines` ＝ `StringCount+1−2·string`）で呼ぶ。★★★ **単音は数学的に不変**: 旧規則は `string > (N+1)/2`（符尾と反対）、新しい位置の符号は `string < (N+1)/2` で正、列が 1 本なら `sign(position)` と `neutral-direction`＝UP なので**全チューニングの全弦で答えが一致する**（中央弦も両方 UP）。⇒ **`LILYSHARP-OWN` が 1 件 `LILYPOND-REF` になった。** 観測は `TabChordTieTests` |
| ★ **タブのタイは自分の数字の縁から出る**（第180・LP に対応物なし） | Lily# の数字はジグザグで 2 列に分かれるので、**タイは自分の列の数字の縁から縁へ**引く（`軸 + dx ± 数字幅/2`）。⚠️ **LP には問えない**——**LP のタブ数字は 3 つとも同じ x**（実測 8.82 / 12.951）なので、選ぶべき第 2 の x が存在しない。⚠️ **帰結として単音のタイが短くなる**（`test/tab-tie` で 1.29 → 0.89）。LP は**中心から中心へ引いて `whiteout` で抜く**（頭間 2.787 に対しタイ 2.467＝88%）が、**その whiteout は Lily# には移植できない**（§2 の ✅ ⒳＝LP の数字 1.180 は弦間 1.5 に収まるが Lily# の 2.166 は収まらず、隣の弦の線を消す／占有子は色を使うのでダークモードで穴になる）。⇒ **タイが短いのは大きい数字の帰結として受け入れる。「短いから戻す」で数字の縁を捨てないこと** |
| ★ **占有（不透明な箱）ではなく除去（インクを切る）で重なりを解く**（第180 で再確認・元は `digitGaps` の実装時） | **理由は 2 つあり、どちらも実測**: ⑴ **箱は色を使う**——ページを反転してテーマを当てる viewer（VS Code のダークモード）では箱が黒くなり、**数字の周りが黒・背景がグレー**で数字が穴に座る（ユーザー・2026-08-16 明示）。⑵ **箱は数字と同じ高さ**＝Lily# では **2.166 対 弦間 1.5** なので**隣の弦の線を両側 0.333 ずつ消す**——**数字を大きくできなくしていた天井そのもの**。⇒ `digitGaps` は**色を 1 つも使わず自分の線の中だけで完結する**。⚠️ **LP の `TabNoteHead (whiteout . #t)` を「LP がやっているから」で移植しないこと**——**LP の数字は 1.180 高で 1.5 に収まる**（2.26.0 実測）ので LP では安全なだけ。**重なりを消すなら、覆うのではなく切る** |
| ★ **タブの弦は小節の中で継承する。明示 `\N` も継承する**（ユーザー判断・2026-08-16 明示） | **LP と違うことを測ってから決めた**: `c( g'\2) g g4` をベースで書くと Lily# は g を 3 つとも 2 弦 5 フレットに置き、**LP は無印の 2 つを 1 弦開放へ戻す**（2.26.0 実測・双子）。**1 つの音高は 1 小節のあいだ同じ押さえ方に見えるほうが読める**、が決定の理由。⚠️ **この resolver はもともと LP と別の模型**（`Tunings.CalculateFret` ＝左手の位置を追う LILYSHARP-OWN）なので、乖離はその延長。**「LP と違う＝バグ」で消さないこと**（理由は `TabResolver.ResolveTabStrings` の remarks にも書いてある） |
| ★ **タブのフレット数字を LP より大きく描くのは意図的乖離**（ユーザー判断・2026-07-24 明示） | LP のタブ数字は小さくて読みにくい。Lily# は `TabConstants.FretFontSize = 2.6`（単数字幅 1.625・高さ 1.7875）＝LP の TabNoteHead 幅 0.990155 の約 1.64 倍。和音で数字が被る問題は**じぐざぐ配置**（`SpacingRules.ApplyTabChordSpacing` ほか）で解いてある。**「LP と違う＝発明だから消す」で削らないこと。** ⚠️ 弦間隔（`TabStringSpace`）は別の話で、そちらは LP の 1.5 に揃える |

---

