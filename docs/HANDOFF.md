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
tools\Session-Check.ps1 -Build -Test -Scratch pNNN    # git・数・CI・build・full（trx 付き）を 1 コマンドで（約 15 分）
```
出た数を §1 の「開始時裏取り」と突き合わせる（HEAD・未 push・木・**full の*合計***・台帳・snapshot・`.lys`）。
**数え方の定義は RULES §6.1「引き継ぐ数の数え方」＝スクリプトが実装している。手で数え直さない**
（10 便が 10 通りに数え間違えた履歴が §6.1 に在る）。**§1 に数を書くときも同じスクリプトの出力を写す。**

⚠️ **読み方の罠（全部この機械で実測済み・出典と経緯は RULES §5.5）**:
- **`成功!`／`Passed!` の語でも終了コードでもなく*合計*を読む**——ホストが死ぬと残りが黙って走らず、
  `Passed! … Total: 1625` ＋ exit 1 も、`成功!` ＋ 合計 5307（正しくは 6688）も実際に出た。理由は trx の
  `RunInfos` に残る＝スクリプトが刷る。**flake が疑わしい 2 度目の緑には証拠が残らないので、trx は最初から。**
- **solution を建てる**（Core だけ建てても `LilySharp.Cli\bin` の Core.dll は更新されない）。**既定は Debug**＝
  lysc を測定・A/B に使う前に `dotnet build LilySharp.Cli -c Release` を明示。**生きている lysc は
  `LilySharp.Cli\bin\Debug\net10.0`**（TFM が上がると化石が並ぶ＝スクリプトが列挙する）。
- **Core の 0 警告は XML doc の健全性を含む**（`CS1573`／`CS1591` だけ `NoWarn`・csproj に数と理由）。
  `--no-incremental` は「腐り対策」ではなく **0 警告を*確かめる*ため**（無変更の増分ビルドは何もコンパイルしない）。
- **CI は `gh run list`／`gh run view` で読む**（この機械に WSL は無い・2026-09-01）。`X` は fail-fast の
  巻き添えが多い＝**完走した脚だけが証拠**（`gh run view --job <id> --log > $env:TEMP\ci.log`・`--log-failed` は途中で切れる）。
  GitHub の ubuntu 脚は 214 便のあいだ読まれず赤だったことがある。
- **開始時と終了時の両方の数を書く**（次便が引き算できるのは両端が在るときだけ）。**途中でユーザーが push したら
  `origin/master` がこの便の commit を指す**＝1 行書く（`git --no-pager log --oneline -1 origin/master`）。
- **A/B の before はその場で写す**（`audit/probe-out/pitches.csv` はどんな小さな run でも上書きされる。
  「あとで」は無い。写し損ねたら `git stash push -- <触ったファイル>` → build → 全数 → 写す → pop）。
- **このドキュメントも memory もコード内コメントも、書いた時点のスナップショット。** HEAD・テスト数・
  シンボル名・「完了」表記は実コードで再確認する。**「見つけた」と思ったら、まず §2 を grep**
  （62 便前から在った棚を「新発見」と書きかけた便がある）。

---
## 1. 現在地 ← **毎セッション書き換える**

最終更新 第357セッション（2026-09-09）＝**入り方は第298〜第354 と同じ**（ユーザーは `docs/HANDOFF.md` を読んで着手せよとだけ言い・background job・口挟み 0）。第356 ⑹ に「決定済み・未実装」は 0・⒟ の拾い箱は承認要だったので、**⒠ の並びから測れるものを 4 つ測って、2 つ直し、1 つは patch（承認待ち）、1 つは開いたまま数を書き直した**: **⑴ MIDI の途中 `partial`（第352 ⑷⒞「未測」）＝欠陥だった→直した（commit `52b93e77`）**／**⑵ `lymelmel.lys` stale（第352 ⑷⒜）＝fixture を直して対を復元（commit `8f7ebf2c`）**／**⑶ `lyrics-verses` bar 4 の 0.19（第352 ⑷⒝「未分類」）＝分類できた＝keep-inside-line rod の両端の枠（LP 移植・出力が動く＝patch `scratch/p358/keep-inside-line-frame.patch`・承認待ち）**／**⑷ §2 U8b（二声の同時 grace）＝再測定して「住所は出来たが列の合体が無い」に書き直した（§2 U8b 末尾・製品 0）**。掃き 923 冊は ⑶ の patch について **MOVED 11**（下 ⑶）・台帳 810 不動・snapshot 249 不動（patch を当てると 10 枚動く）。**骨は 5**:

★★★ **⑴ MIDI × `partial`＝空の弱起 `partial 4 |` がページでは 1 拍・MIDI では 4 拍だった**（`scratch/p358/midi/`・`run.ps1` が note-on の tick を刷る）: `mp-open`（曲頭 `partial 4 | c'4 …`）は最初の note-on が **1920 tick**、対照 `mp-open-ctl`（`partial 4 s4 |`）は **480**。途中の `c'1 | partial 4 | d'4 …` も同じ（3840 対 2400）。**ページは両綴りがバイト同一**（data-pos 伏せ）＝`MeasureBuilder.EmitEmptyMeasure` は `_timeSignature`（partial が縮めた長さ）で spacer を埋める。**根**: `MidiExporter` に `PartialDeclarationSyntax` の腕が 1 つも無く（default → `ProcessChildren` で消える）、`MeasureTicks()` は拍子の全長。**形**: `_partial`（`MeasureBuilder._partialRestore` の双子）＝music の `partial` と section header の `partial`（`_sectionHeaderPartials`＝`_sectionHeaderTimes` と同じ registry・`FirstDirectPartial`）が置き、`MeasureTicks()` が読み、**閉じる小節線で消す**（`pairsHere || timePassed`＝`RestorePartialIfPending` の双子・typed barline の空 span は閉じない）。**番人 `EmptyMeasureValidatorTests` +5**（曲頭／途中／section header の `| ` 対 `s4 |` 恒等・方向の釘＝1 拍・陽性対照＝閉じた後の `| |` は拍子）。**観測者 0**（ディスク 13825 冊で `partial N |` を書く本は私のプローブ 2 冊だけ）＝「1 つの量を 2 人が別々に答える」の食い違いで、報告された欠陥ではない。CHANGELOG 0.7.0 MIDI 節。

★★ **⑵ `lymelmel.lys`＝fixture が第216 の文法で stale だった（`sings` 無しの lyrics は独立行→`__ ~ ~` が extender に畳まれ "looong" が中央寄せ 19.68）**→ `lyrics w sings v` に直して対を復元。**再測 2.26.0**（`scratch/p358/lymelmel/run.ps1`）: "looong" のインク左 **LP 18.393（頁 X 26.929 − 8.536）／Lily# ≈18.40**（中心 22.05・extender 始端 25.90）＝c16 頭 18.39・d/e/f16 の列 20.90/23.40/25.91 不動。`audit/lp-regression/status.json` の notes に追記。

★★★★ **⑶ `lyrics-verses` bar 4 の +0.19 の正体＝keep-inside-line rod の*終点*が小節線の左端で、LP は右端**（`scratch/p358/lv/`・`settings.ly`＝LyricText／NoteHead／BarLine の X・`run.ps1`）。**LP**: bar 4 は 63.298 → 71.549＝**8.251**、"saved"（ink 6.3507・self-alignment LEFT）の右端 71.739 ＝**小節線の右端**（column＝anchor 71.739・小節線は 71.549..71.739 と列の*左*に垂れる）。**Lily#**: 63.82 → 72.26＝**8.44**＝"saved" の右端が小節線の*左*端。**歌詞を外した対照 `lv-ctl.lys` は 4 小節とも exact**（bar 4＝8.150 両側）＝ばねと ink の半分は正しく、rod の終点だけが違う。**機構**（`lily/break-alignment-interface.cc:273-274`）: 行末（`break_status_dir () == LEFT`）の break-align group は `alignment_off = -total_extent[RIGHT] - extra_right_space` で右端を列に置く（BarLine の `right-edge` は `extra-space 0.0`＝`define-grobs.scm:302`）。Lily# のばね鎖は小節線の左端で終わる（`springTargetWidth = availableWidth - totalBarlineWidth`）ので、同じ rod は spring 空間では「reach − 行末小節線の ink」。**★ 同じ本の bar 1 で行頭側も見つかった**: 2 番の先頭音節 "Twas" は最初の符頭の左 2.387 まで届き、**LP は符頭を 8.585 に置いたまま**（音節の左端 6.198 は拍子記号の下・LP の列 0 は行の左端＝`:265-266` の left-edge item）、**Lily# は 8.97**＝拍子記号の右端 6.58 ＋ 2.39（rod の起点が prefix の右端だった）。`test/lyrics` の 1 段目全体が +0.3 右に居たのも同じ。**形**（patch）: `MultiStaffLayouter` の keep-inside-line 節で右 rod は `right − lineEndBarInk`（最後の springs を持つ measure の `EndBarline` 幅）、左 rod は `left − (prefixWidth + 先頭 measure の StartBarline 幅)`・**lead sheet は左を触らない**（LILYSHARP-OWN の `gridLeftEdge` の起点はユーザー決定＝staffless の CL/CLX がその縁を exact で持つ・最初 lead sheet も引いたら `lead-sheet-lyrics`／`lytie`／`drunken-sailor` の行が −0.4 動いた→除外）。**番人 `LineEndKeepInsideLineTests` 4 本**（bar 4＝8.251±0.03・対照 8.150・差 0.101・最初の符頭 8.585）。**掃き 923 冊（`sweep358.ps1`・base `8f7ebf2c` Release `exe-base`／`exe-head3`）: MOVED 11**＝右だけなら 3（chordnames・lyrics-verses・She Bangs）、両端で **追跡 10（snapshot fixture: `showcase/08-chorale`・`test/{chordnames, chordrow-rit-second-system, lyric-break-pricing, lyrics-below-marcato, lyrics-verses, lyrics-volta, lyrics, sings-per-row, tab-lyrics-inside-strings}`）＋ユーザー 1（`She Bangs.lys`＝justified 段の 0.01〜0.02）**。焦点テスト 878 本（台帳 `LpGeometryLedgerTests` 込み）緑。**⚠️ 承認ゲート**（出力が動く・snapshot 10 枚再ベース）: **patch は当てていない**（木は HEAD・`git apply scratch/p358/keep-inside-line-frame.patch` で番人と CHANGELOG ごと入る）。⚠️ `git diff | Set-Content -NoNewline` は要素間の改行も落として patch を壊す＝`git diff --output=` で書く（`make-patch.ps1`）。

★★ **⑷ §2 U8b 再測定**（`scratch/p358/lp/{g0_2vplain,g1_2vgrace,g2_gracechord}.lys`・`pair358.ps1`・`settings.ly`＝NoteHead／Accidental の X）: 「同じ X に重なる」は消えたが**二声の grace は別々の列**（voice 1: ♯ 7.39・符頭 8.43／voice 2: ♯ 8.53・符頭 9.57／主音符 11.51）、**LP は 1 列**（♯ 7.685/8.547・符頭 9.590/10.508＝collision 0.918・主音符 13.072）。**和音 grace の対照は LP と 3 桁一致**（列の中の詰めは届いている・−0.30 は `grace.column` の既知の島）＝**欠けているのは voice を跨ぐ列の合体**。§2 U8b 末尾に書いた。射程 0・承認要・着手せず。

★ **⑸ 数**: **開始時裏取り**（`tools\Session-Check.ps1 -Build -Test -Scratch p358`）: HEAD **`a57deac1`**・**未 push 15**・`origin/master`＝`cf6edb6f`・木 clean・未追跡 0・build 0 エラー／Core 0 警告・**full 7469 / 0 / 4 / 7473**（`scratch/p358/run1.trx`＝第356 終了時の 7468/1 の赤 1（APPROXIMATIONS）を再生成した後の数と一致）・`gh run list` 緑（最新 34286219497）・台帳 810・snapshot 249・追跡 `.lys` 599・ディスク 13825。**途中**: `run2.trx` 7472 / 2 / 4 / 7478＝赤 2 は **引用の門 `CitationsThatNameNothing_DoNotGrow`**（MIDI の新しい REF が `:1670-1678 \partial` と記号名なしで書かれていた→ 2.26.0 の行 `:1697-1705 partial = context-spec-music 'Timing` に直した・⚠️ **collector／MeasureBuilder／MusicXml／SyntaxNodes の `1670-1678` は 2.24 時代の住所のまま**＝門は既存分を数えて許しているだけ・直すなら 5 か所一緒に）と **APPROXIMATIONS stale**（`LILYSHARP_UPDATE_DOCS=1` で再生成）＝commit `4320112f`。**終了時**: **commit 4 本（`52b93e77`＝製品 MIDI: Core 1＋Tests 1＋CHANGELOG／`8f7ebf2c`＝lpreg 2 ファイル／`4320112f`＝REF の記号名＋APPROXIMATIONS／この HANDOFF＋ARCHIVE）・未 push 19・木 clean（patch は scratch）・**full 7474 / 0 / 4 / 7478（`scratch/p358/run3.trx`・+5＝`EmptyMeasureValidatorTests` の弱起 5 本）**・§7.5（`-DiffBase a57deac1`）Core `+` 61／LILYPOND-REF 1／LILYSHARP-OWN 0**・台帳 810／snapshot 249／追跡 `.lys` 599 不動。§7.5 の読み直し: MIDI の REF 1（`music-functions-init.ly:1697-1705 partial`＝PartialScopeValidator と同じ引用）・OWN 0＝式は足していない（`MeasureBuilder` の規則の写し）。⚠️ **scratch は `p358`**（`p357` は第356）。⚠️ **§7 3.5**: 第355 のブロックを `Session-Check.ps1 -Archive 355` で ARCHIVE の先頭へ・第356 の経緯は下の見出しの下に残した。⚠️ ripple: 2 console が busy／standby 初回の pipeline が `Set-Location` だけで落ちた **3 回**（memory どおり・打ち直しで全部走った）。⚠️ 掃きの base exe は `git worktree add --detach scratch\p358\wt-base HEAD` から Release build・終了時に worktree は外した。

⇒ ★★★★ **⑹ 次の一手**: **⒜ push はユーザー**（第355 の 8 本＋第356 の 7 本＋この便 9 本）。**⒝ 承認待ち＝patch 2 段（第 1 便の ⑶ ＋第 4 便の退役・どちらも出力が動く）**: 合成 1 本なら `git apply scratch/p358/keep-inside-line-and-retirement.patch`（⑶ だけなら `keep-inside-line-frame.patch`・退役はその上に `lyric-edge-own-retirement.patch`）→ build → **snapshot 11 枚**（⑶ だけなら 10 枚）を LP 照合のうえ再ベース（`LILYSHARP_UPDATE_SNAPSHOTS=1`）→ full（合成なら 7514 全緑の見込み・`run7.trx` の赤は snapshot 11 ＋ census 1 だけ）→ commit。LP の数は上 ⑶・第 3 便 ⑵・番人 2 本（`LineEndKeepInsideLineTests`／`LineEdgeLyricReservationTests`）に在る。⚠️ ⑶ だけを当てる場合も `audit/magic_constants.csv` の再生成が要る（第 4 便）。**⒞ §3 に「決定済み・未実装」は 0**。**⒟ 第356 ⑹⒟ の拾い箱はそのまま**（跨ぐ音符を LP のように割る／梁を跨いで折る／双子は LYS1037 の本で LP と違う絵／段末の head の末尾 spring）。**⒠ 第355 ⑹⒞⒠⒡ の並びの残り**（F-fonts 拾い箱・`time none` の縁 ⑵⑶⑷・MIDI の途中 `partial` は閉じた・`lymelmel` は閉じた・`lyrics-verses` bar 4 と行端の OWN 2 量は patch へ・拍グリッド／ps2 の 0.017／`audit/lpreg` 取り直し／§2 U8b（列の合体・承認要）・U8／A/B/D/E／C⑴／G・段端の floor の regime）。⚠️ scratch は `p358`。**第 5 便は着手しない**: 残りは承認要（⒝・U8b）か、この便の道具立て（`p358` の exe・LP 計器）と無関係な regime（⒠）で、次のセッションでやる方が文脈の利が無い分だけ損もしない。

✅ ★★★ **第 2 便（ユーザー「このセッションでやる方が有利なら着手」）＝`partial N |` の文を読む 4 人のうち未測だった MusicXML と双子を測ったら、2 人とも 08-28 の「`| |`＝1 小節の沈黙」を知らなかった→両方直した（commit `340b3211`）**。**測定**（`scratch/p358/midi/run-xml-ly.ps1`・`gap.lys`）: 双子は `c'1 | | e'1` を `c'1 |`／`|`／`e'1 |` と写す＝LP の `|` は bar check で時間を持たず **2 小節**（ページは 3）、空の弱起 `partial 4 | c'4` は bar check failed で `c'4` が弱起に吸われる。MusicXML は空小節を書かず（gap.xml＝2 measure）、弱起版は measure 0 に `c'4` を吸い込み measure 1 が 3 拍。**形**: 双子＝`EmitMusicStream` に MIDI の walk と同じ規則（bare `|`／scope を開かない `|:` で「前の境界から時間が経っていない」なら `s<拍子>` を書く・`||`/`:|`/`|.` は飾り・section の sentinel と scope 頭は境界・chord track は除外・`_twinPartial` が pending なら `s4`）＋`TakesMeasureTime`（**時間を取らない側を列挙**＝未知の node は音楽扱いで偽の spacer を作らない）。MusicXML＝`AddSilentBar`（ProcessRest と同じ休符・partial pending なら弱起の長さ）＋`_barSeenInScope`（scope 頭の `|:` は閉じない）。⚠️ **踏んだ穴**: MusicXML の walk は `ChordPartBlockSyntax` の腕が無く default で子を歩く＝**和音行の小節線が旋律パートに届く**（`test/volta-chord-row` の `Am | |` が旋律に沈黙 2 小節を足した）→ 祖先に `ChordPartBlockSyntax` を持つ小節線は規則の外（MIDI は同じ本で不変＝`scratch/p358/midi2` 実測）。**番人 `EmptyBarExportTests` 25 本**（双子・XML とも `| |` 対 `s1 |` の恒等（曲頭・途中・弱起・閉じた弱起の後・`| |:`・3/4）・発明しない対照（`|:` の両役・先頭 `|:`・`||`・`|.`）・2 譜の measure 数）。**射程**（`sweep-ly-xml.ps1`・`| |` を書く 18 冊＝追跡 10＋ユーザー 8）: **MOVED 16 出力**（ly 11・xml 5＝`grammar-2026-06-09`・`mid-bar-break`・`section-empty-placeholder`・ユーザー 8 冊の大半＝`s1 |` の挿入）。**full 7499 / 0 / 4 / 7503（`run5.trx`・+25）**・§7.5（`-DiffBase 7b887b65`）Core `+` 141／REF 0／OWN 0＝**REF 0 は監査対象**: 読み直した結果、LP の式は 1 本も無い（規則はユーザー決定 08-28 の写し・LP の事実は「`|` は bar check」＝RULES §6 の罠17）。APPROXIMATIONS 再生成。ページ・snapshot・台帳は不変（svg を触っていない）。**終了時: commit 6 本・未 push 21・木 clean**。

✅ ★★ **第 3 便（同じ指示の 3 度目）＝小物 1 つと測定 1 つ（製品コードの変更 0・commit `88746dd1`＝コメントと docs だけ）**。**⑴ 2.24 時代の住所 5 か所**（collector／MeasureBuilder ×2／MusicXml／SyntaxNodes の `music-functions-init.ly:1670-1678`）を 2.26.0 の `:1697-1705 partial = context-spec-music 'Timing` に直した（記号名つき・引用の門 11 本緑）。**⑵ 行末／行頭で続く歌詞行の pre-port 量 2 つ（`LineEndLyricReservation`＝inkR＋0.4・`LineStartLyricFloor`＝inkL＋0.4）を LP で測った**（`scratch/p358/lehyph/{hy,wd,hy2,wd2,ctl}.lys`・`lv/run.ps1`＝system id つき LyricText／NoteHead／BarLine の X・⚠️ `LyricHyphen.after-line-breaking` は計器にしない＝`set_spacing_rods` がその property を読んで rod を足す）: **LP はどちらの縁でも何も予約しない**。行末: `bright-`（ハイフンで次段へ）と `bright`（語末）の最終小節は**同じ 18.686**（25.487 → 44.173）で音節の右端は行末小節線の右端＝keep-inside-line rod だけ／Lily# は 19.28（+0.594＝0.4 の gap ＋ 枠の 0.19）。行頭: 次段の先頭音節が "lyrically"（9.287）でも "Lyrically"（10.141）でも**最初の音符は 5.8 のまま**（音節は 1.81／1.38 から clef の下に伸びる・keep-inside rod は slack・ハイフンの after-break rod（`lyric-hyphen.cc:180-193`・0.5＋0.2＋protrusion）も効かない）／Lily# は 7.72／8.15（floor が押す）。歌詞を外した対照 `ctl` は両段とも exact。⇒ **2 量とも LP に対応物が無い LILYSHARP-OWN**＝両方の doc コメントに MEASURED を書いた（退役は出力が動く＝⑶ の patch と一緒に承認ゲート・射程は「歌詞行が幅広の音節で段を跨ぐ本」全部＝実コーパスに多いはず・未計数）。**full 7499 / 0 / 4 / 7503（`scratch/p358/run6.trx`）**・§7.5（`-DiffBase a57deac1`・便全体）Core `+` 235／REF 5（MIDI 1＋住所の付け替え 4）／OWN 2（LyricSpacing の MEASURED 文が「LILYSHARP-OWN だ」と書いた行＝式ではない）。**終了時: commit 8 本・未 push 23・木 clean**。

✅ ★★★ **第 4 便（同じ指示の 4 度目・ユーザーは `/clear` の後に「このセッションでやる方が有利なら着手」）＝第 3 便が「未計数」で置いた 2 つの LILYSHARP-OWN 歌詞量（`LineStartLyricFloor`／`LineEndLyricReservation`＋gate の `LineEndLyricMinExcess`）の退役を patch に落として射程を数えた（製品 commit 0・木は HEAD）**。**このセッションでやった理由**: 掃きの `exe-base`（`8f7ebf2c` Release）・`exe-head3`（⑶ だけ）・`sweep358.ps1`・LP 計器（`lehyph`／`lv`）が全部 `scratch/p358` に残っていて、次のセッションなら base の建て直しと LP の測り直しからになる。**形**（`scratch/p358/lyric-edge-own-retirement.patch`＝⑶ の上に当てる／`keep-inside-line-and-retirement.patch`＝HEAD に 1 回で当てる合成／作り方 `make-patch2.ps1`）: layout の行末 rod（`MultiStaffLayouter` の cross-bar rod の else 枝）と行頭 floor（`LineStartSpringForLine` の `lyricLeadingFloor` 引数ごと）を消し、gate（`SystemBreaker`）の同じ 2 価格と breaker の運び手 `MeasureSpringData.LineEndLyricMinExcess` を消す＝段の両端で音節を持つのは keep-inside-line rod だけ（LP と同じ）。`LyricLineEdge.ContinuesFromPrev` は読み手 0 になった（record の欄として残置）。**番人 `LineEdgeLyricReservationTests` 7 本**（hy／wd の段末小節 18.686±0.03・ハイフン≡語末 ±0.01・hy2／wd2／hy の次段の最初の音符 5.8±0.05・歌詞なし対照 13.378／5.8）。**照合**: 実物 hy2 は次段の最初の音符 **5.80**（LP 5.8）・段末小節 18.69（LP 18.686）。**射程（923 冊・`sweep-head4.ps1`・`exe-head4`＝⑶＋退役の Release）: base→head4 MOVED 13＝⑶ の 11 ＋退役で新たに 2（`test/rit-across-systems`＝段 2 の最初の音符 7.73→7.47／`samples/greensleeves`＝段 4・5 の最初の音符 6.60／7.09→**5.80**＝LP の継続段の値そのもの）／head3→head4 MOVED 5（新 2 ＋ ⑶ で動いた `chordrow-rit-second-system`・`lyric-break-pricing`・`She Bangs` が更に動く）**。⚠️ 2 冊とも動いたのは行頭 floor の側＝**行末 rod の退役だけで動く本は 923 冊に無い**（段末で幅広の音節が次段へ続く lehyph の形は追跡 fixture にもユーザー本にも無い＝番人が唯一の観測者）。**patch を当てた full: 7498 / 12 / 4 / 7514（`scratch/p358/run7.trx`・+11＝⑶ の 4 ＋退役の 7）＝赤 12＝snapshot 11 枚（掃きの追跡 11 冊と同じ集合）＋ `MagicConstantInventoryTests.TheCensusIsNotStale`**（⚠️ **⑶ の patch 単独でも census は古くなる**＝第 1 便は焦点 878 本しか回していなかった。`audit/magic_constants.csv` と `docs/APPROXIMATIONS.md` の再生成を合成 patch に入れた）。§7.5（patch の中身・`git diff -- LilySharp.Core` を手で数えた）: 合成 Core `+` 121／REF 3／OWN 2（⑶ の 3／1 ＋退役の 0／1＝OWN は「残るのは lead sheet の halves」の散文）・**退役だけ `+` 67／REF 0＝監査対象**: 読み直した結果、式は 1 本も足していない（消しただけ・LP の事実は「両端で何も予約しない」＝MEASURED を `LyricSpacing.ReserveLyricLine` の remarks に集約）。⚠️ 踏んだ穴: 番人の初版が次段の最初の音符を Y 帯で拾おうとして 4 本落ちた＝**Lily# の SVG は `<text>` の y と小節線 `<rect>` の y が別の枠**（段 2 の小節線 y=33.47 に対し符頭 y=21〜24）→ 文書順で 2 つ目の clef の次の glyph を拾う形に。**終了時: commit 9 本目（この HANDOFF だけ）・未 push 24・木 clean・full（HEAD・`scratch/p358/run8.trx`）**7499 / 0 / 4 / 7503**＝run6 と同数（製品は不変）・台帳 810／snapshot 249／追跡 `.lys` 599 不動**。

## 以下は第356セッションの経緯

最終更新 第356セッション（2026-09-09）＝**入り方が変わった**: ユーザーは HANDOFF を読ませて「残債はあとどのくらい」と訊き（§2 の棚卸しを返した）、次に **「小節途中の改行は、安全に実装できるなら実装して」** と指示（§2F ⒥＝第280 で「サポートしない」と決めた項。再検討条件「`time none` が彫れたとき」は第353 で成立していた）。**実装した＝§3 先頭行**。掃き 922 冊 **MOVED 1**（下 ⑷）・台帳 810 不動・snapshot 248 → **249**（新規 1・再ベース 0）。**製品は commit 済み（下 ⑸）・push はユーザー**。**同じ便に第 2 便（section 境界で割れた小節の bar check）と第 3 便（同じ小節を番号でも 1 つに）が続いた（⑸ の下）＝commit 6 本・未 push 14**。**骨は 5**:

★★★★ **⑴ 何が「安全」か＝LP を 8 冊で測ってから線を引いた**（`scratch/p357/lp/mb1-8.ly`・2.26.0・`settings.ly` 計器）: 小節途中の `\break` は**折れ目に BarLine 無し・次段は clef だけで最初の音は小節線折りと同じ 5.8・次段に BarNumber 無し**（小節線折り `mb8` は "2" を刷る）・タイとスラーは跨ぐ（mb6）・**下譜の全音符は折られ残りが空で刷られる**（mb2）・**梁は 2 片に割れる**（mb3・PROBEBEAM ×2・"forced break overridden" 警告は出ない）・`e2 break |`（読者の 198 site）は小節線折り（mb8）。**Lily# の線**: 全 voice が同じ拍で item 境界を持つ小節だけ割る。跨ぐ音符（LILYSHARP-OWN）・梁・tuplet・percent・`time none`・同じ小節の 2 本目は割らず、**break は今までどおり次の小節線に落ちて LYS1037 が voice と理由を名指す**＝黙って変わる出力を作らない。

★★★★ **⑵ 形＝小節を 2 measure に割る・表は score で 1 つ・collector は 2 回走る**。`Measure.BreaksMidBar`（head：`EndBarline None`・改行 Force・番号を進めない）／`Measure.ContinuesBar`（tail：`StartBarline None`・段頭でも番号なし）。**鍵は bar（`MeasureBuilder.LogicalMeasureIndex`＝始めた小節の数・tail は進めない）**で、`MidBarBreakTable` が `ToPhysical/ToLogical` を訳す。**1 回目**: builder が「音楽の両側を持つ mid-bar break」を `MidBarBreakRequest` として記録（`SetBreak` → `NoteMidBarCandidate` → 次の音で `RequestPendingMidBar`＝`e2 break |` は要求しない）。**settle**（`SettleMidBarBreaks`）: `MidBarBreakTable.Build` が全 voice（omitted-part harvest 込み）で item 境界・梁（`BeamDetector`）・tuplet・percent・unmetered を検査し、通った小節だけ表に、落ちた break は `MidBarBreakConflict`。**2 回目**: 全 builder が clock が offset に達した瞬間に `EmitSplitHead`（`MeasureCompleted` は鳴らさない＝臨時記号は小節線でだけ忘れる）・spacer は切る（`| |` の placeholder・`voice { }` の lead-in・行の slot）・行（`ChordNameCollector.CollectPart/CollectAlignedItems`・`LyricsCollector.CollectRow`）は bar→measure の写像で同じ小節を 2 つに書く・nested collect は `SeededMidBarBreaks` で同じ表を継ぐ。**mid-bar break を持つ本は resume 不可**（記録を ineligible・mid-resume で見つけたら `CollectResumeAbortException` → full）＝持たない本は 1 回目の空リスト検査だけ。読み手 4 人のうち動くのはページだけ（MIDI/XML は構文・双子は `\break` をそのまま＋行変数の head の後ろに `|` を書かない）。`LayoutReport` は bar で数え bar で名指す。`BarNumberEngraver.NumberMeasures` は head で進めず、`Calculate` は tail 段頭に番号を刷らない。

★★★ **⑶ 番人＝`MidBarBreakTests` 17 本**（head/tail の形・段の小節線と番号（LP の値 11.568/19.718/27.868・5.8）・`e2 break |` ≡ 小節線折り（バイト同一）・2 voice＋`| |`＋和音行＋歌詞行が同じ拍で割れる・反復 section と `pageBreak`・臨時記号は跨ぐ・全音符 refusal＋LYS1037 の位置・梁/tuplet/`time none` refusal・2 本目 refusal・percent の書かれた周は割れる・半小節（4+4 梁）は割れる＝LP 15.603/24.455/33.307・MIDI/XML 不変＋双子・増分≡full 両向き・表の写像）＋fixture `test/mid-bar-break`（chords 行・歌詞行・bass `| |`・snapshot 新規 1）。**目視 1 回**（`scratch/p357/fixture.png`）。⚠️ **踏んだ穴 3 つ**: ⒜ **percent の body 長を model measure 数で数えていた**（head+tail で 2 と読み `%%` を刷った→ `LogicalMeasureIndex` で bar を数える）／⒝ tuplet 内の `break` は**今まで黙って捨てられていた**（`ProcessTuplet` が `BreakSyntax` を歩かない）→ 歩かせて refusal＋警告（小節線折りに落ちる）／⒞ 自分のテストの relative octave（`cis' cis'` は 1 オクターブ上）。⚠️ LP 計器の教訓: **手書き probe と双子で 0.47 違った**のは pitch（`\relative c' { c' }` は c''）＝符尾向きの stem correction・Lily# は双子と 3 桁一致。

★★★ **⑷ 掃き 922 冊: MOVED 1＝ユーザーの `Disco Inferno.lys`**（`scratch/p357/sweep357.ps1`・base `19487d93` Release `exe-base`／head `exe-head`・`svg --combined`）。**動いた理由は機能そのもの**: section A が `c, c, a,, a,, break` で半小節のまま終わり（LYS2001 既出）、次 section `~A_1`（volta 1.）が同じ小節を埋める本。旧: break が合成小節の終わり（A_1 の 4 音の後）に落ち、"1." の弓が段の途中で始まる。新: **A の末尾で折れ、A_1 が "1." の弓とともに段頭に立つ（番号なし・小節線なし）＝双子が LP で組む絵**。`scratch/p357/disco-{base,head}-page1.png` に両方。**16 段・66 bar・3 頁は不動**、動くのは段 5–6 の 2 段だけ。⚠️ **承認ゲート**: ユーザーの本が動くので報告する（この便の指示「実装して」が承認と読んだ・push は別）。**他 921 冊（追跡 598 全部・ユーザー 323）は 1 バイトも動かず**＝`e2 break |` 198 site は小節線折りのまま。

★ **⑸ 数**: **開始時裏取り**（`tools\Session-Check.ps1 -Build -Test -Scratch p357`）: HEAD **`19487d93`**・**未 push 8**・`origin/master`＝`cf6edb6f`・木 clean・未追跡 0・build 0 エラー／Core 0 警告・**full 7443 / 0 / 4 / 7447**（`scratch/p357/run1.trx`＝第355 終了時と一致）・`gh run list` 緑（最新 34286219497）。**途中**: `run2.trx` 7455 / 7 / 4 / 7466＝赤 7 は新 snapshot の baseline 作成 1・§1 の前便ブロック 2 つ（→ `-Archive 354`）・resume 網 2 本（**2 回目の pass の記録を ineligible にし損ねていた**→ `FinishSplitCollect`）・`MeasureCollectorResetTests`（`_midBarBreakConflicts` が Reset を逃れていた→ readonly＋Reset で clear＋2 回目の後に戻す）・APPROXIMATIONS stale（再生成）。**終了時**: **commit 2 本（`08d4ff9a`＝製品 28 ファイル +1752/−92: Core 18（新規 `MidBarBreaks.cs`・`MidBarBreakValidator.cs`）＋Tests 4（新規 `MidBarBreakTests.cs`・fixture・snapshot）＋CHANGELOG＋GRAMMAR／GRAMMAR_FOR_LLM／SYNTAX_REFERENCE／APPROXIMATIONS・2 本目＝この HANDOFF＋ARCHIVE）・未 push 10・木 clean・full 7462 / 0 / 4 / 7466（`scratch/p357/run3.trx`・+19＝`MidBarBreakTests` 17（Theory 展開込み）＋snapshot 1＋その他）**・台帳 810 不動・snapshot 248 → **249**（新規 1・再ベース 0）・追跡 `.lys` 598 → **599**。**第 2 便の後**: commit 3 本目（製品 Core 2＋Tests 1＋CHANGELOG＋GRAMMAR_FOR_LLM）＋4 本目（この HANDOFF）・**full 7468 / 0 / 4 / 7472（`scratch/p357/run4.trx`・+6＝`SectionBoundarySplitBarTests`）**・未 push 12・木 clean。§7.5（`-DiffBase 19487d93`・第 1 便 commit 後）: Core `+` 1068・**LILYPOND-REF 3**・**LILYSHARP-OWN 1**——**1068 行に REF 3 は監査対象**（RULES §7.5）: 読み直した結果、幾何の式は 1 本も足していない。3 本の REF は「どの grob が在るか」の引用（`paper-column-engraver` の break 列・`bar-number-engraver` の BarNumber＝BarLine と共に・`accidental-engraver` の小節単位の記憶）で、数は全部 LP 実測（`scratch/p357/lp`）の側に在る。OWN 1＝`MidBarBreakTable.Build` の「跨ぐ音符は割らない」。⚠️ **scratch は `p357`**（`p356` は第355）。⚠️ **§7 3.5**: 第354 のブロックを `Session-Check.ps1 -Archive 354` で ARCHIVE の先頭へ・第355 の経緯は下の見出しの下に残した。⚠️ ripple: 2 console が busy の間の standby 自動ルーティングは 5 本中 4 本が `cd` だけで落ちた（memory に追記）。

✅ ★★★ **第 2 便（ユーザー「Disco Inferno を少し直した。絵は問題ないけど、繰り返し記号が小節の途中にある場合に LYS2006 が不正に出てしまう」）＝section 境界で割れた小節の bar check を黙らせた（commit 3 本目・§3 先頭行）**。ユーザーは A の末尾を `a,, a,, | break`（半小節を `|` で閉じる）に直し、各 ending は残り半分 `bes,,8 c, c, f,~ |` で始まる＝**A の LYS2001 ×1 と ending 4 本の LYS2006 ×4**。**根**: `MeasureValidator` は section を block ごとに検査し、隣の section を知らない。**形**: `Semantics/SectionBoundaryBars.cs`——`FormWalk.Read(form)` で再生順を展開（repeat block は body→turn の ending・回数は `:|*N`＞ending 数＞2）して隣接対を集め、**同じ part の cell** を `MeasureModel.Split` で測り、**先頭 bar は「全 predecessor の末尾が補完」・末尾 bar は「全 successor の先頭が補完」のときだけ免除**（隣が 1 つでも外れれば両方の警告は残る・repeat body と span の 2 声以降は訊かない・cell を持たない part は補完しない）。⚠️ **踏んだ穴**: `| break` の後ろに break だけの chunk が残り `isLast` がそれを指していた→「最後に鳴る bar」で判定（`TrailingMeasuresAreSilent`）。**番人 `SectionBoundarySplitBarTests` 6 本**（volta・補完しない ending 1 つで両方戻る・素の並び＋陽性対照・part-major＋`~`・`| break`・cell の無い part）。**Disco Inferno の check は bar 警告 5 → 0**（残る 1 本は作者の孤児タイ）。**診断だけ＝ページ・MIDI・XML・双子は 1 バイトも動かない**（掃きは不要・§7.5 REF 2＝volta/repeat-acknowledge の「小節の途中に立てる」引用・OWN 0）。⚠️ ページは第 2 便ではまだ 2 つの bar として数えていた（小節番号は LP より 1 多い）→ **第 3 便で合わせた（下）**。

✅ ★★★ **第 3 便（ユーザー「小節番号も LP に合わせて、その割れた小節を 1 つに数えて」）＝section 境界で割れた小節を番号でも 1 小節に数えた（commit 5 本目・§3 の行を更新）**。**形**: collector の `MarkBarsSplitBySectionBoundaries`／`SectionBoundaryContinuations`（`CollectMultiStaffPass` は `ResolveVoiceTabTies` の後・`CollectPass` は parallel span の前）が **「前 section の短い末尾＋この section の短い先頭＝その場の拍子」の先頭 measure に `Measure.ContinuesBar` を立てる**（決め手は最初の非 row voice・全 voice と row に同じ印・pickup／unmetered／placeholder／mid-bar split 済みは補完しない）。読み手は mid-bar break と同じ 2 人（`NumberMeasures` は続く measure の前で進めない・`Calculate` は段頭に番号を刷らない・`LayoutReport` は bar で数える）＝**作者の小節線は描いたまま**。**volta の 2 本目以降の ending は「前の ending の末尾」ではなく「body の末尾」に対して読む**（`Measure.ContinuedFromMeasure`＝body 末尾の index・predecessor は `_voltaBrackets`（cumulative side table＝resume でも見える）を StartMeasureIndex 順に並べ「隣接かつ番号が増える」で 1 repeat に束ね、先頭 bracket の start−1）。**⚠️ ここで 1 度間違えた**: 最初は「2 本目の ending は body の小節番号を取り戻す」と書いた（LP の `alternativeRestores` が currentBarNumber を戻すと思い込んだ）——**2.26.0 の `ly/engraver-init.ly` を読むと `alternativeRestores = (measurePosition measureLength measureStartNow lastChord)`＝位置は戻すが番号は戻さない**・`define-context-properties.scm` の `alternativeNumberingStyle`「未設定（既定）なら番号は alternative を通して続く」。**だから 2 本目以降の ending の先頭 half は短い bar でも段頭番号でもないが、番号は新しく取る**（`1 2 | 2 3 | 4 5`）＝`ContinuedFromMeasure >= 0` の measure は `NumberMeasures`／`BarCount`／`BarLabels` で bar を始める。**番人 `SectionBoundarySplitBarTests` +1**（6 measure・index 2 と 4 が ContinuesBar・4 の ContinuedFrom＝1・番号 `1 2 2 3 4 5`・段頭番号 0・report「3 systems, 5 bars／system 2: bars 2-3 (1 bar)／system 3: bars 4-5 (2 bars)」・陽性対照は 4 が続かない）。**Disco Inferno**: `lysc layout` は 67 bar → **66 bar**（A_1 の先頭 half が bar 18 を埋める・A_2／A_3／B4 の先頭 half は続く bar 番号を取る＝LP の既定と同じ）。**掃き 922 冊 head → head2（`exe-head2`・compare `-BaseTag head -HeadTag head2`）: MOVED 0**——⚠️ **stage した Disco Inferno は第 1 便の時点の写しで、ユーザーの `| break` 直しより前**（その形は mid-bar break が割るので境界の印は立たない）＝掃きは「他の 921 冊＋ユーザー本は 1 バイトも動かない」の証明にしかならない。**今の Disco Inferno は両 exe で別に彫った**（`scratch/p357/disco3/{head,head2}`・956806 → 955882 byte）: ending の段頭から番号が消え、次段は 23／32／41／45 で続く＝目視 1 回（`disco3/head2/disco-page1.png`）。**commit `2acd8b24`（製品 Core 4＋Tests 1＋CHANGELOG＋GRAMMAR_FOR_LLM＋APPROXIMATIONS 再生成）**・§7.5（`-DiffBase bbc17290`）Core `+` 215／**REF 2**（`alternativeRestores`・`alternativeNumberingStyle`）／OWN 0・**full 7468 / 1 / 4 / 7473（`scratch/p357/run5.trx`・+1＝番人・赤 1＝APPROXIMATIONS stale → 再生成して単独緑）**・台帳 810／snapshot 249 不動。

⇒ ★★★★ **⑹ 次の一手**: **⒜ push はユーザー**（第355 の 8 本＋この便 6 本）。**⒝′ 第 3 便の縁**: ending に bracket が無い（legacy `1. A`＝`[`無し）2 本目以降は predecessor を「前の measure」で読む（`VoltaBracketItem` が無いと束ねられない・**ディスク上の `.lys` 13823 冊に legacy 形の form は 0**＝第 3 便の末尾で multiline grep して数えた・直すなら resume で見える cumulative side table を 1 つ足す形）／`alternativeNumberingStyle = numbers` 相当（alternative ごとに番号を戻す表示）は LP でも非既定なので持たない。**第 4 便は着手しなかった**: ⒝′ は観測 0、⒟ は安全線の外（出力が動く＝承認要・LP 計測が主で文脈の利は無い）、⒠ はこの便の文脈と無関係。**⒝ Disco Inferno の絵はユーザーが見た（問題ない）**。**⒞ §3 に「決定済み・未実装」は 0**。**⒟ 拾い箱**: 跨ぐ音符を LP のように割る（LILYSHARP-OWN 宣言済み・`MidBarBreakTable.Build` の refusal を外し tail を空 spacer にする形・観測者はユーザー本 0）／梁を跨いで折る（LP は 2 片・Lily# の小節単位 beamer の再導出）／双子は LYS1037 の本で LP と違う絵になる（doc に明記済み・双子側で break を小節線へ動かす手も在る）／段末に小節線が無い head の末尾 spring は LP と 3 桁一致したが観測は ragged 3 冊のみ。**⒠ 第355 ⑹⒞⒠⒡ の並びはそのまま**（F-fonts 拾い箱・`time none` の縁 3 つ・`lymelmel.lys` stale／`lyrics-verses` bar 4 の 0.19／MIDI の途中 `partial`／拍グリッド／ps2 の 0.017／`audit/lpreg` 取り直し／§2 U8b・U8／A/B/D/E／C⑴／G・段端の floor の regime）。⚠️ scratch は `p357`（`p356` は第355）。

## 2. 開いている作業

### U. ユーザー報告（2026-08-29・第286 起票）← **順に着手。ユーザーが優先度を与えた**

> ⚠️ **この 3 点は「読み手が紙とプレビューで見た」もの**で、台帳の残差とは別の族。
> ★ **⑵⑶ の本はユーザーの実コーパス** `scratch\ベースタブLy\`（未追跡・300 冊級）。
> **追跡 573 冊には無い**ので、閉じるときは**射程を実コーパス側でも数えること**。

- **U1. ✅ 閉じた（第286・報告は当たっていた）＝3 小節以上の `repeat percent` は LP のスラッシュ 1 本になり、LYS2014 は退役**。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）


- **U2. ✅ 閉じた（第286）＝和音行が `rit.` を避けるのは 1 段目だけだった**（**ユーザー報告**・ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U3. ✅ 閉じた（第286）＝行頭の音符が「自分の住所」を持っていなかった**（**ユーザー報告**・ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U4. ✅ 閉じた（第286・ユーザー決定）＝タブの `as numbers` はタイも描かず、`as` 省略時は*スコアが答える***。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）


- **U5. ✅ 閉じた（第288）＝タブ譜と歌詞行が重なるのは「タブの下の弦が profile の外に居た」から** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U6. ✅ 閉じた（第288・ユーザー報告 2026-08-29）＝`@rit` は「自分の次の演奏」で終わっていた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U7. ✅ 閉じた（第293・ユーザー報告 2026-08-30）＝容器の中のリハーサル記号が黙って消えていた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U8. ▶ 容れ物は全部閉じた（第298 ＋ 第299 ＋ 第300 ＋ 第301 ＋ 第302）。残るのは*本当に grob を要る*ものだけ＝`grace { }` の本体は*歩かれない*。起票の「注釈を運ばない」より広かった**
  （**第293 起票・第298 が engine に訊き直して起票を訂正し可聴化と 2 つの carry を入れ、
  第299 が付点を、第300 が phrase 参照を、第301 がその 2 人の残りの読み手を、
  第302 が tuplet を移植した**）。

  ⚠️⚠️ ★★★ **第300 の骨＝⒜ の 4 つは「症状」で 1 箱に入っていた**。
  **第298 は「本体がそれだけなら装飾音符ごと消える」で和音・休符・tuplet・phrase 参照を 1 つにまとめ、
  第299 はその箱ごと「*ホストの*列を要る側」と書いた**——**phrase 参照は列を 1 つも要らない**。
  **grob を 1 つも名指していないから**: **和音・休符・tuplet は装飾音符の列がまだ持てない grob を要るが、
  phrase 参照は「よそに書いた音楽」の別名でしかなく、*容れ物*である**。
  ⇒ ★★★ **修理を決める軸は「どの列を要るか」ではなく「grob か容れ物か」**——
  **症状（装飾音符ごと消える）で仕分けると、修理の違うものが同じ箱に入る。**
  ⇒ ★★ **そしてこの文法の容れ物は他に 3 つあって、3 つとも phrase 参照を展開していた**
  （`tuplet { A }`・`cue { A }`・`repeat unfold 2 { A }`）。
  **`scratch/p194/four-containers.lys` は第194 がその 4 つを並べて確かめるために書いた本**で、
  **grace だけが 106 便のあいだ落としていた**（**第300 の掃きで動いたプローブ以外の唯一の本がこれ**）。

  ★★★ **`MeasureCollector.CollectGraceNotes` が読むのは「裸の `NoteSyntax`」だけ、その中の
  *音高*と*音価の値*だけ**。**本体は `ParseMusicBlock` で普通に構文解析される**ので、
  **書けるものと描かれるものが桁違いに開いている**。**第298 実測**（各綴りを対照と描き比べ、
  `data-pos` を伏せてバイト比較）:
  **⒜ 和音・休符・tuplet・phrase 参照は列を 1 本も作らない**——**本体がそれだけなら
  *装飾音符は 1 つも描かれない***（**⚠️ この 4 つのうち tuplet と phrase 参照は*容れ物*で、
  第300・第302 が閉じた。残るのは和音と休符**）／**⒝ 付点（`d'8.`・`d'8..`）は無視される**／
  **⒞ 本体の中のスラー・梁・タイは落ちる**／**⒟ `@staccato`・`@text`・`@f`・`@finger`・
  `@trill`・`@sustain`・`@rit`・`@cresc` は全部落ちる。** **LilyPond 2.26.0 はこの全部を描く。**
  ⇒ ★★★ **だから「注釈の族」ではなく*本体を歩いていない*が正しい病名**。

  ★★ **第298 が入れたもの**（`LYS4020`＝`Semantics.GraceBodyValidator`・**ink は 0 冊分も動かさない**）:
  **⑴ 落ちるもの全部が*書かれた場所で*名指される**（「`a chord` inside 'grace { }' … and this body
  holds no bare note, so **NO grace note is drawn at all**」のように、**「飾りが 1 つ減った」と
  「装飾音符ごと消えた」を言い分ける**）。**⑵ *列を要らない*注釈 2 つは運ぶようにした**——
  **`@mark`**（**LilyPond は `Mark_engraver` を SCORE context に consist する**＝
  `ly/engraver-init.ly:729 \name Score`, `:764`）と、**弦番号 `\N`**（**そもそも grob ではない**——
  **notation staff では `c'4\2` と `c'4` がバイト同一**で、**`Tunings.CalculateFret` の*入力*でしかない**）。

  ★★ **第299 が閉じたのは ⒝ 付点**（`e735fa88`・`Svg/Layout/DotColumn.cs`）。
  **`GraceNoteInfo.Dots` は `BaseDuration` の*隣*に置く**——**音価は符頭・旗・梁の本数を決めるので、
  付点を分数に畳むと `grace { d'8. }` が 16 分になって梁が 2 本になる**。
  **付点の X は定数ではなく*移植***（`DotColumn`＝**support の箱の右スカイラインを*各付点の段で*読み、
  符頭のインク右で床を張り、付点幅 1 つを足す**）。**LP 2.26.0 実測**: **`grace { e'8. }` は空きなので
  1.226600／`grace { d'8. }` は線なので付点が 1 段上がり旗に当たって 1.747300**（**差 0.520688 ＝ 旗右 − 符頭右**）。
  ⚠️ **付点は幅を 1 も予約しない**（**LP は `grace { d'8. }` と `grace { d'8 }` を同じ版面幅で彫る**）
  **が spring は読む**（**`grace { d'8. e'16 }` 2.915900 対 `grace { d'8 e'16 }` 2.448000＝差 0.467900
  ＝`0.8 × log2(3/2)`**）。⚠️ **Lily# はこの gap を両方の本で LP より 0.246 短く描く**——
  **混在 run の古い乖離**（台帳 `grace.column.*` の島）。**網は*差*を主張しているので残差を隠さない。**

  ★★ **第300 が閉じたのは ⒜ の phrase 参照**（`Semantics.GraceBodySupport.BodyElements`）。
  **`grace { G }` は `grace { G の中身 }` と*バイト同一*のページを彫る**（`octave absolute` の対で実測）。
  ⚠️ **展開は 1 綴りで、collector と validator が*両方それを読む***——**collector だけが展開すると、
  validator は参照で止まったまま「1 段下の `<c e>` について黙り」、しかも「装飾音符 2 つを彫る本」を
  *装飾音符ごと消えた*と言い続ける**（毒 `b_novalidate` が実測）。
  ★★ **枠は普通の walk と同じ 2 つのマーカーで運ぶ**（`RelativeResetMarker` / `PhraseEndMarker`）ので、
  **phrase 本体は fresh frame で評価され、出るときは*アンカー*を返す**——**main stream の `G c` と同じ。**
  ⚠️⚠️ **ただし `EnterDefaultFrame` は呼ばない**——**あれは*声部の*音価記憶も消すが、grace 本体はそれを
  1 度も読まない**（読むのは grace 自身の既定 8 分）。**消すと `grace { A }` が*装飾音符の後ろの音符*の
  音価を変える**＝**同じ音楽を inline で書いた `grace { d'16 }` には出せない副作用。**
  ⇒ **grace 自身の音価記憶のほうは境界で戻す**（`grace { c'16 G }` の G の無音価は 16 分ではなく 8 分）。
  ⚠️ **展開できない名前**（未宣言・循環）**は参照そのものが drop として残る**——
  **循環は `PhraseCycleValidator`・未宣言は `SymbolReferenceValidator` の仕事で、grace の報告はそれを消さない。**
  ⚠️ **予算は呼び手が渡す**（`Func<bool>`。collector は `ChargeExpansion`、validator は
  `DefaultExpansionBudgetCap` の局所カウンタ＝`Semantics.MeasureModel` が既にやっている「診断側の第 2 展開器」と同じ形）。
  **払えない phrase は*マーカーごと*出さない**（`ExpandVariable` と同じ「省略で釣り合わせる」）。


  ⚠️⚠️⚠️ ★★★★ **第301 が閉じたのは「その 1 つの文の*残り 2 人の読み手*」**
  （`Midi.MidiExporter.ProcessGrace`・`MusicXml.MusicXmlExporter.ProcessGraceNotes`）。
  **`GraceBodySupport` は「written once and read TWICE」と書いてあるが、`grace { }` の本体を歩く
  walk は 4 本ある**——**第300 はページと報告に「phrase 参照は容れ物だ」を教え、
  残る 2 本は `grace.Body.Items` を自分で歩いたままだった。**
  **実測 2026-08-30**（`scratch/p301/ab`・`octave absolute` の対・`phrase G { d'16 e' }`）:
  **`grace { G } c'4 c'2.` の svg は inline 綴りと*バイト同一*（`data-pos` 伏せ）、`.ly` 双子も同一、
  なのに `.mid` は*装飾音符を 1 つも書かない本と*バイト同一（91B 対 inline の 107B）、
  MusicXML は `<grace/>` が 0 個（inline は 2 個）。** ⇒ **ページに描かれている音符が、
  鳴らず、export されていなかった。**
  ★ **5 本目の `.ly` 双子 `EmitGrace` は narrowing を持たず原文を出し直すだけなので最初から正しかった**
  ＝**「読み手」に数えるのは*絞る*walk だけ。**

  ⚠️⚠️ ★★★ **同じ便で見つかった 2 つ目＝MusicXML は「装飾音符の既定音価」の*4 つ目の答え*を持っていた**。
  **`grace { c' } d'4` を `<type>quarter</type>` で書き出す**（**ページ・MIDI・双子は 8 分**）——
  **`ProcessGraceNotes` が主旋律の `_defaultDuration` を共有していた**ため。
  ⚠️ **共有は外へも漏れていた**: **追跡 fixture `test/ossia-beams.lys` は
  `d4@glissando grace { d8 } c` を 4/4 に書いており、export された小節は 3.5 拍だった**
  （`c` が `d4` の 4 分ではなく grace の 8 分を継いでいた）。**掃きが動かした唯一の追跡本がこれ。**
  ⇒ **2026-08-01 の「3 つの答えを 1 つにした」の N は 3 ではなく 4 だった**（RULES §7.6 に汎化）。

  ★★ **直し方は「読み手ごとにフレームを持つ」**——**展開は 1 綴り（`BodyElements`）のまま、
  2 つのマーカーが来たときに*その読み手が読む量だけ*を借りて返す**
  （**MIDI＝鳴らす移調・絶対オクターブ基準・grace 群の音価記憶／XML＝相対フレーム・移調・
  絶対アンカー・grace 群の音価記憶**）。**第300 ⑷ の「境界は*その読み手が読むもの*を戻す」が
  そのまま 2 人ぶん増えただけで、新しい規則は 1 つも要らなかった。**
  ⚠️ **`Count > 0` の門はページの `ExitPhraseTranspose` と同じ形**（発明ではない）。
  ⚠️ **MIDI と MusicXML の*主旋律*の phrase 展開にはどちらも予算が無い**——**別の穴で、第301 は測っただけで触っていない**（§2 F ⒩）。

  ⚠️⚠️⚠️ ★★★★ **第302 実測＝LP に「頭でない構成員」を訊いた＝*⒜ と ⒞ が 1 つの模型である*ことが LP 側からも出た**
  （`scratch/p302/lp/member`・**WSL の LP v2.27.3＝質的確認。正典 2.26.0 ではない**・
  `-dbackend=svg`・`<polygon>`＝梁／`<path>`＝符頭・休符・旗。**素の本 `c'4 c'2.` は path 5**）:

  | grace 本体 | polygon | path | LP が描くもの |
  |---|---|---|---|
  | `{ d'16 e'16 f'16 }` | 2 | 8 | 3 つを 1 本の梁 |
  | `{ d'16 r16 f'16 }` | **0** | 10 | **梁なし**＝旗つき 2 音＋休符 |
  | `{ r16 d'16 e'16 }` | **0** | 10 | **梁なし**＝旗つき 2 音＋休符 ← **外れ値** |
  | `{ d'16 e'16 r16 }` | 2 | 8 | d'–e' に梁、そのあと休符 |
  | **`{ d'16 e'16 r16 f'16 }`** | **2** | **10** | **d'–e' に梁、`f'` に*旗*、間に休符** |
  | `{ d'16 e'16 f'16 r16 }` | 2 | 9 | 3 つに梁、そのあと休符 |
  | `{ <d' f'>16 e'16 f'16 }` | 2 | 9 | **梁はそのまま**（符頭 4 つ） |
  | `{ d'16 <e' g'>16 f'16 }` | 2 | 9 | 同 |
  | `{ d'16 e'16 <f' a'>16 }` | 2 | 9 | 同 |
  | `{ <d' f'>16 }` | 0 | 8 | 単独の和音は旗（単独音と同じ） |
  | `{ d'16 e'8 f'16 }` | 3 | 8 | 部分梁（第300 の実測） |

  ⇒ ★★★★ **⑴ 和音は梁に何の影響も与えない**——**先頭・中間・末尾のどこに置いても
  polygon 2・path +4（符頭 4 つ）**。**模型に要るのは「構成員は符頭を N 個持てる」の 1 語だけで、
  梁の側は 1 行も変わらない。**
  ⇒ ★★★★ **⑵ 梁は*先頭*の「隣り合う符頭の連なり」だけを覆い、最初の休符でそこが終わる**
  ——**`{ d'16 e'16 r16 f'16 }` が決定的**: **polygon 2（d'–e' の梁）＋ path +5（符頭 3・
  休符 1・*旗 1*）**＝**1 つの grace 群が「梁つき部分列」と「旗つき単独音」を同時に持つ**。
  ⚠️⚠️⚠️ ★★★★ **この規則は 1 度書き直している。最初は「*極大*部分列に割る」と書いて
  commit し、*外れ値として括り出した 1 冊がその反証だった***（第302・**書いた 20 分後**）:
  **`{ d'16 r16 e'16 f'16 }` は e'–f' が隣り合っているのに旗**——**極大部分列の規則なら
  梁が出るはずで、出ない**。**先頭の休符も同じ理由で梁を消していた**（`{ r16 d'16 e'16 }`）。
  ⇒ ★★★ **16 冊すべてに合う規則は 1 行**: **「先頭の 2 要素が*どちらも符頭*なら梁、
  さもなくば全部旗」**（**和音は符頭として数える**。`{ <d' f'>16 e'16 f'16 }` は梁）。
  ⚠️ **測ったのは WSL の v2.27.3・4/4・16 分と 8 分の grace 本体 16 冊**——
  **「LilyPond の梁の規則」ではなく「この形の grace 本体に LP が返した答え」として読むこと。**
  ⚠️⚠️ ★★★ **それは第300 が*音価の混在*（`{ d'16 e'8 f'16 }`）で測ったのと同じ形**
  ——**⒜ の休符と ⒞ の梁は、LP の出力の側から見ても 1 つの模型変更**
  （`BeamLeftY`/`BeamRightY` が*単数*で 3 軒・`IsBeamedRun` が all-or-nothing、というのが
  綴れない相手）。**第302 ⒀ がコードから出した結論と独立に一致した。**
  ⚠️⚠️ ★★★★ **「外れ値」は外れ値ではなく*反証*だった**（第302・**5 冊追加して 2 分で片付いた**）。
  **`{ r16 d'16 e'16 }`・`{ r8 d'16 e'16 }`・`{ r16 r16 d'16 e'16 }`・`{ r16 d'16 e'16 f'16 }`・
  `{ r16 d'16 e'16 f'16 g'16 }`・`{ d'16 r16 e'16 f'16 }` はすべて旗**——
  **「先頭の 2 要素が両方とも符頭か」だけで 16 冊が説明できる。**
  ⇒ ★★★ **測った規則に「ただし 1 例だけ外れる」が付いていたら、それは規則ではなく
  *まだ反証を読んでいない*状態**（RULES §5.0 に汎化）。
  ⚠️ **正典で取り直していない**（**この機械の 2.26.0 exe は本便で 13 分ブロックした**）。
  **梁の有無という*質的*な形なので WSL で足りるが、座標を要るときは 2.26.0 を待つこと。**

  ▶ **残っているのは——⒞ の*スラーとタイ* ／ ⒟ 注釈の全族**（★★ **和音・休符・⒞ の*梁*は第308 が閉じた**）
  ▶▶ ★★★★ **そしてその 2 つは *⒝2 が閉じるまで手を出さないこと*。** **第310 が ⒝1 を入れて
  住所は実在するようになったが、描くのはまだ脇の模型**——**ここで ⒞⒟ を grace の家に彫ると
  スラーの幾何の*第 2 の綴り*ができる**（RULES §5.2.1②）。**⒝2 は ⒞⒟ を*構築により*閉じる。**

  ⚠️⚠️⚠️ ★★★★ **【第309 の骨】「装飾音符が住所を名乗れない」は*欠けている*のではなく*届いていない*。
  そして Lily# は cue と grace を LP と*逆*に持っている。**
  （**2026-08-31・第309 実測。計器は `scratch/p309/`＝`ab/` のプローブ 10 冊と `measurements.md`**）
  - ★★★ **測った対**（**両側 Release・`data-pos` 伏せ・各プローブに*その印だけが違う*対照**）:
    **`cue { }` の中では*スラーもタイも `@staccato` も描かれる*／`grace { }` の中では 3 つとも対照とバイト同一**。
    **`lysc check` は cue 側で drop を 1 行も出さず、grace 側は 3 つとも LYS4020 を出す。**
    ⚠️ **タイの対は 1 度壊した**——**対照を `{ d16 e16 }`・プローブを `{ d16~ d16 }` にしたので
    *音高の差*が「タイが描かれた」と読めた**（**第60 の罠・RULES §5.0**）。**`{ d16 d16 }` に直すと grace 側はバイト同一。**
  - ★★★ **差は 1 行だけ**: **`ProcessCueRegion` は `ProcessMusicNodeSequence(cueSites, builder)`
    ＝*普通の walker* を*普通の builder* へ通す**ので、**cue の音符は `measure.Items` の実項目になり
    実 `ItemIndex` を持つ**——**だから普通の engraver 全員が届く**。**`CollectGraceNotes` は
    音高と音価だけを脇の配列（`score.GraceNotes`）へ読む。**
  - ★★★ **LYS4020 の文面自身が既にそう言っていた**——「**a grace note *is not a measure item*,
    so there is no column for it to hang off**」。**「添字が無い」ではなく「項目でない」。**
  ⚠️⚠️ ★★★★ **正典を読んだら、Lily# は 2 つを*逆*に持っていた**（`C:\MyProj\lilypond-src` @ `v2.26.0`）:
  - **`ly/engraver-init.ly:432` に `\name CueVoice`＝*cue は独立した context*。**
  - **`grep -c "name Grace" ly/engraver-init.ly` は `0`＝*grace は context ではない*。**
  - **`ly/engraver-init.ly:368`＝`\name Voice` の中の `\consists Grace_engraver`**（**コメントは
    「Grace_engraver *sets properties*, it must come first」**）。**`lily/grace-engraver.cc` の
    `make_item|make_spanner|Grob \*` は `0` 件＝*grob を 1 つも作らない*。`process_music` は
    `consider_change_grace_settings` を呼ぶだけ**＝**grace time に入る／出るときにフォントサイズを切り替える装置**。
  - **⒞⒟ が要る engraver は全部その*同じ Voice*に `\consists` されている**:
    `Note_heads_engraver`・`Dots_engraver`・`Stem_engraver`・`Beam_engraver`・`Script_engraver`・
    `Script_column_engraver`・`Rhythmic_column_engraver`・`Slur_engraver`・`Tie_engraver`。
  ⇒ ★★★★ **だから LP は ⒞ も ⒟ も*特別なコードを 1 行も持たずに*描く**——**装飾音符は
  「grace *時間*にいる普通の Voice のイベント」で、同じ Slur/Tie/Script engraver が彫る。**
  ⇒ ★★★★ **そして Lily# は対を逆に持っている**: **LP が独立 context を与えたほう（cue）を普通に歩き、
  LP が普通の Voice に置いたほう（grace）を脇の模型へ持ち出した。** **⒞・⒟・U8b は 3 つとも
  *普通の Voice の engraver* で、grace はその手の届かない場所へ出されている**——**3 つが「同じ 1 つ」なのは
  住所を共有するからではなく、*同じ 1 つの構造から締め出されている*から。**
  ⚠️⚠️ ★★★ **⇒ `VoiceContextId.Grace` を足すのは*誤り*。** **`VoiceContextId` に `Cue` が在るのは
  `CueVoice` が LP の実在の context だから**（`MusicItem.cs` の enum・spacing が `ContextOf` で経路を分ける）。
  **grace は context ではなく*時間の領域*で、その区別は上で測ってある。**
  ★ **既に在る機構 2 つ**: **`MeasureBuilder.AddItemWithoutDuration`**（**小節時間を進めずに項目を足す。tuplet が使っている**
  ＝**「grace time は小節時間を取らない」は解決済みの機構**）／**grace のフォントは `GraceNoteItem.FontSizeStep` 族**
  （**＝LP の `general-grace-settings`＝`Grace_engraver` が*設定する*もの、そのもの**）。

  ⚠️⚠️⚠️ ★★★★ **【要ユーザー決定・第309 起票】住所を*足す*か、grace を*普通の Voice に戻す*か。**
  **⒜ 住所を足す**＝**`(itemIndex, graceColumn)` の複合鍵を作り、⒞⒟ が要る 6 型と局所辞書 4 軒と
  `VoiceItemKey`・`StaffAccidentalColumns` に通す。grace は脇の配列のまま。**
  ⇒ ⚠️ **住所の*第 2 の綴り*ができる**（RULES §5.2.1②）**うえ、LP に無い分離を模型に彫る**——
  **LP は grace を普通の Voice に置いているので、「grace 専用の住所」は移植ではなく発明。**
  **⒝ grace を普通の walker で歩く**＝**cue と同じ形にし、grace body の項目を
  `AddItemWithoutDuration` で `measure.Items` に入れ、grace time の印を付ける。**
  ⇒ **⒞・⒟・U8b は*構築により*閉じる**（**普通の engraver が届くようになるので、新しい規則は 0 本**）。
  ⇒ ⚠️ **射程**: **`score.GraceNotes`／`GraceNoteItem` の読み手は Core 18 ファイル・`GraceNoteLayout` は 7**
  ——**第298〜第308 が建てた模型**。**さらに grace の音符が `measure.Items` に現れるので列の機械が拾う。**
  ⚠️ **私は ⒝ を勧める**（**理由は正しさだけ——⒜ は LP に無い構造を彫り、住所の綴りを 2 つにする**）。
  ★ **ユーザーの原則により、実装コスト・移行コストはこの並べ方に混ぜていない。**
  ✅ **【ユーザー決定 2026-08-31・第309】＝⒝**（**普通の walker で歩く**）。**第309 は設計と測定まで。実装は未着手。**

  ⚠️⚠️ ★★★★ **【その射程は 4 冊少ない。第310 が数え直した——`grace` だけを探していたから】**
  **`acciaccatura { }` と `appoggiatura { }` も grace body で、そのうち*`grace` の語を 1 度も書かない*
  本が在る**——**追跡分で 2 冊**（`audit/lpreg/perf-grace200.lys`・`audit/lpreg/perf-slurgrace300.lys`）。
  ⚠️ **後者は第310 の掃きが出した 8 冊の 1 つだった**＝**射程の数え方が落としていた本が、実際に動いた。**
  ★★ **⒝2 が掃く母集団はこちらで数えること**:
  ```powershell
  # ディスク全部（scratch を含む）— 第310 は 2007 冊中 172 冊。第312 は 1418 冊中 58 冊
  #   ⚠️ 母集団が縮んだのは本が減ったから（scratch の掃除）。レシピは同じ、数だけ取り直す
  @(Get-ChildItem . -Recurse -Filter *.lys -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|artifacts|output)\\' -and
                   (Get-Content $_.FullName -Raw) -match '\b(grace|acciaccatura|appoggiatura)\b' }).Count
  # 追跡 .lys — 581 冊中 34 冊
  @(@(git ls-files '*.lys') |
    Where-Object { (Get-Content $_ -Raw) -match '\b(grace|acciaccatura|appoggiatura)\b' }).Count
  ```
  ★ **`grace` だけで数えると 170 / 32**（内訳＝scratch 138・audit 19・Tests 12・その他 1）。
  **第309 の 166 / 31 との差は本が増えたからではない**——**本便は `.lys` を 1 冊も足していない**
  （p310 のプローブは LilyPond の `.ly`）。**述語と仕分けの差**。⇒ **RULES §5.0 の数え直しの 8 例目。**

  ★★★ **⒝ の射程は第309 が測った**（**§2 は「snapshot が動きうる・未測定」としか書けていなかった**）:
  - **ディスク 2007 冊のうち grace を書く本は 166**（**内訳＝過去便のプローブ `scratch/` 135 ／ `audit/` 20 ／
    Tests fixtures 11**）。**⚠️ ユーザーの 326 冊は 1 冊も書かない**（第306 の測定と一致）。
  - **追跡 `.lys` 581 冊のうち grace を書く本は 31**（`audit/lpreg` 18 ／それ以外 13）。
  - ★★★ **再ベースが要る snapshot は 9 枚**（**名指し**: `showcase/02-ornaments`・`test/grace-accidental-line-start`・
    `test/grace-chord-accidental`・`test/grace-lower-staff`・`test/grace-notes`・`test/ossia-beams`・
    `test/single-staff-arpeggio-grace`・`test/tab-grace-slur`・`test/tab-grace`）。
  ⚠️ **数え方**: **snapshot は `<群>__<本>.svg` なので、拡張子を落としただけの名前で突き合わせると
  *0 枚*と出る**——**第309 は 1 度そう出して、それが計器の artefact だと気づいて `__` の後ろで取り直した**
  （**RULES §5.3「0 は『測れていない』の顔で出る」の 6 例目**）。
  ⇒ ★★ **つまり ⒝ は「承認待ちで止まる大手術」ではない**——**動く snapshot は 9 枚で、全部名前が分かっている。**

  ★★ **⒝ の実装の型**（**第309 が読んだ範囲の見立て。⒝1 は第310 が実装した——下の ✅ の
  段落が実際に建ったものを書いている。この段落は*当たった予測と外れた予測*の記録として残す**）:
  **`ProcessCueRegion` と同じ形の `ProcessGraceRegion` を書き、`_graceDepth` を立てて
  `ProcessMusicNodeSequence` を*同じ builder* へ通す**（**呼ぶ位置は今 `CollectGraceNotes` を呼んでいる 5 箇所
  ＝主音符が足される直前なので、grace の項目は自然に主音符の*手前*の添字に並ぶ**）。
  **小節時間は `MeasureBuilder` に grace 印を持たせて `AddItem` が期間を足さないようにする**
  （**`AddItemWithoutDuration` が既に在り tuplet が使っている**）。
  ⚠️⚠️ ★★★ **難所は「誰が skip するか」**: **`.Items` を読む場所は Core 75 ファイル・325 箇所**あるが、
  **⒝ の終点では大半は*何も変えなくてよい*——grace は普通の Voice のイベントなので、
  普通の項目として見えるのが正しい**。**印を要るのは「今 `GraceNoteItem` 側が担っている仕事」だけ**＝
  **描画（`SharedRenderer`）・spacing・MIDI・MusicXML・LilyPond export**（**この 5 つは今*脇の配列*を読むので、
  項目としても読むと二重になる**）。⇒ ★★★ **だから ⒝ は「項目を足す」と「脇の配列を畳む」を*同じ 1 歩*で
  やらないと木が二重状態になる**——**段階に割れないのはここ。**
  ⚠️⚠️⚠️ ★★★★ **【この 2 文はどちらも外れた。第310 が実測した】** **⑴ 「難所は誰が skip するか」＝
  触ったのは 325 のうち *11*、しかもその大半は*時計を先に直したら消えた***（`MusicItem.Duration` を
  grace time で 0 にする 1 行で、テストの赤 50 本のうち 26 本が落ちた）。**⑵ 「段階に割れない」＝割れる。**
  **⒝1 が「脇の配列を*その項目から導出する*」ので二重状態にならず、しかも*全木 5 出力バイト同一*と
  いう、⒝2 には出せない証明が付く。** ⇒ **RULES §5.0 の 7 例目**（**起票が外したのは*数*ではなく
  *難所の形*だった**）。
  ★ **網は在る**: **全木 4 出力 sweep（2007 冊）＋ grace の本 166 冊**。**取りこぼした skip は sweep の
  動いた出力として出る**（**RULES §5.4**）。

  ✅✅ ★★★★ **【⒝1 は第310 が閉じた（`0c925495`）——grace body は普通の walker が歩き、ページは 1 バイトも動かない】**
  **`MeasureCollector.ProcessGraceRegion` が `grace.Body.Items` を `GatherMusicSite` に通して
  `ProcessMusicNodeSequence` へ渡し、`MeasureBuilder.EnterGraceTime` が開いた区間の項目に
  `MusicItem.GraceTime` を押す。** **`GraceNoteItem` はその項目から*導出*される**
  （`DeriveGraceColumns`）——**構文を二度読む reduced reader は消えた。**
  ⇒ ★★★ **証明は全木掃き**: **2007 冊 × 5 出力（svg / midi / xml / ly / **check**）が
  *1 バイトも動かない*** (`scratch/p310/sweep310.ps1` / `.json`・両側 Release・base `1f6d8c71`)。
  ★★ **LYS4020 の drop 集合も不変**——**⒞⒟ の印は*住所を持てるようになっただけ*で、まだ描かれない。**

  ⚠️⚠️⚠️ ★★★★ **⒝1 の難所は「誰が skip するか」ではなく「誰が*時計*を読むか」だった。**
  **起票（この段落の上）は「`.Items` を読む 325 箇所」と書いた。実際に触ったのは 11 か所で、
  しかもその大半は*先に時計を直したら消えた***: **`MusicItem.Duration` を grace time で
  `Fraction.Zero` にする 1 行で、テストの赤 50 本のうち 26 本が落ちた**
  （**LILYPOND-REF: lily/moment.cc — LP は grace 時間を `Moment` の `grace_part_` に置いて
  主時計に見せない**）。**項目 spring・列グリッド・小節充足・梁のグルーパが全部この数で歩く。**
  ⇒ **RULES §5.0 の 7 例目**（**数ではなく*難所の形*が違った**）。

  ★★★ **⒝1 が入れた足場は 11 か所で、11 か所とも「これは足場・⒝2 が消す」と書いてある。**
  **仕事一覧は `grep -n "GraceTime" LilySharp.Core`**:
  `ProcessMusicNode` の 2 つ（歩ける node 種の門・marker の零化）／`MeasureBuilder.NarrowToGraceTime`
  （**ホワイトリスト——「捨てるもの」ではなく「残るもの」を書いた。黒リストは次に ink が
  1 種類増えた日に黙って腐る**）／`SharedRenderer.EnumerateStaffItems`／`SharedRenderer.Tab`／
  `SkylineBuilder` の 2 つ／`SpacingRules.IsMusicalColumn`／`CreateSpringsForMeasure`／
  `BeamDetector`（**跨ぐ。`IsBeamable` を false にすると*休符扱い*になり run を切る**）／
  `VoiceScan.WalkVoiceItems` と `NoteScan.FindNext`（**span 検出器**）／`VoiceCollector`／
  `ElementCoordinator` の 2 つ（slur の障害物・破断 slur の端点）／`MeasureLayouter` の 2 つ／
  `SpacingRules.NoteColumnAt`。

  ⚠️⚠️ ★★★ **⒝1 が*前から在った欠陥*を 1 つ閉じた＝`GraceNoteItem.VoiceIndex`。**
  **`MainNoteItemIndex` は*その grace を書いた voice の*項目列を数えるのに、`GraceNoteEngraver` は
  それを譜の第 1 voice に対して解決していた**——**`LayoutUtilities.VoiceItemAt` の doc が注釈の側で
  名指しているのと同じ欠陥の 3 例目**（dynamics と scripts は既に 2 段解決になっていた）。
  **見えなかったのは、grace が項目でなかったころは*どちらの voice の添字も動かなかった*から。**

  ⚠️⚠️ ★★★ **単体テスト 6812 本が全緑になった*あと*で、掃きが 8 冊を出した。**
  （`audit/lpreg/lyhygrace`・`perf-slurgrace300`・`sttremcol`・`scratch/p298/tg3`・`tg4`・
  `p308/ab/g1_2vgrace`・**ユーザーの `Real Gone.lys` と `Something That I Want.lys`**）
  **穴は 5 つ**: **多声の衝突列／tab の桁幅／梁を*切って*いた／slur の障害物を二重に読む／
  破断 slur の端点が行頭の grace を拾う。** ⇒ **231 枚の snapshot と 748 点の台帳が知らない本が
  1776 冊在る**（→ RULES §5.4）。

  ✅✅ ★★★★ **【⒝2 の本体は第313 が入れた（`0e27b056` ＋ `ccf30003`）——普通の engraver が grace 時間を描く】**
  **符頭・ledger・臨時記号・休符・符尾・旗・acciaccatura の斜線は普通の engraver が、
  grob ごとのフォントで（`GrobFontSize`）描く。** **住所は層が publish する**＝
  **`GraceNoteItem.ColumnItemIndices`（列 → その voice の項目添字）と
  `ScoreLayout.GraceColumnXs`（`(staff, voice, measure, item) → X`）**。
  **X は第 2 の計算ではない**——**`layout.X + ColumnOffsets[i]`＝`SpacingRules.GraceColumns` の
  同じ鎖。新しいのは*鍵*だけ。**
  ★ **grace 家に残したのは LP が grace のためだけに宣言しているもの**: **梁（接頭辞・thickness 0.384・
  length-fraction 0.8）とその下の符尾／主音符への slur。** ✅ **付点は第315 が帰した（下の ⒞）。**
  **普通の pass に「この列の符尾は他所が描く」と伝えるのは*普通の梁と同じ集合*
  （`BuildBeamedItemsSet`）**なので、2 軒が食い違えない。
  ★★ **acciaccatura の斜線は*項目の属性*になった**（`MusicItem.GraceSlash`）——**LP でもそれは
  Flag の属性だから**（`ly/grace-init.ly` の `Flag.stroke-style = "grace"`）。**旗を描く者が描く。**
  - ✅✅ ★★★★ **符尾は初めて LP と一致した。9 冊・全部 4 桁一致**（`scratch/p313/lp/measurements.md`）:
    **`\grace { d'8 }` `{ d'16 }` 2.80 ／ `{ d'32 }` 3.40 ／ `{ d'64 }` 4.00 ／
    `{ b'16 }`（中央線）2.70 ／ `{ d''16 }` 2.60 ／ `{ a16 }` `{ f16 }`（五線の下）2.80。**
    ⇒ ★★★ **grace の符尾＝*音価が選んだ長さ × 0.8*、短縮は積の中、中央線への延長は無し。**
    **第312 までは全部 3.5 × magstep(−3) = 2.475**（**音価を見ない・0.8 ではなく 0.7071**）
    ＝**8 分で −0.325・64 分で −1.525。** **`APPROXIMATIONS` の `UNWATCHED` 51 → 50・計 224 → 223。**
  - ⚠️⚠️ ★★★★ **表の*もう半分*が欠けていた＝`no-stem-extend`。**
    **`general-grace-settings` は `length-fraction 0.8` の隣に `(Voice Stem no-stem-extend #t)` を置き、
    `lily/stem.cc:591-593` は「符尾は中央線まで届く」規則をその property で門番している**
    （**梁側の双子 `:1233-1235` `calc_stem_info` も同じ property で 2 つの clamp を門番——
    Lily# は knee の半分だけ守っていた**）。**`StemDetails.NoStemExtend` がそれ。cue は宣言しないので
    cue の符尾は今も延びる。** ★ **決め手の対は `\grace { a16 }`（2.80 で止まる）とその full-size 対照
    （4.00 まで引かれる）。** ⚠️ **quanter にも通したが 1 冊も動かない**——**clamp が効く位置に
    grace の梁を書いた本がコーパスに無い。**
  - ⚠️⚠️ ★★★ **移す途中で出た欠陥 2 つ。どちらも「サイズは合っていて*デザイン*が違う」形**:
    **⒜ `DrawNote`/`DrawChord` の `gc.MusicFace` は符頭と臨時記号しか包んでおらず、旗と付点は
    *縮んだサイズ*で*五線のデザイン*から出ていた**（**Emmentaler は光学サイズなので 14 の輪郭と
    20 を縮めたものは別物＝`GrobFontSize` が `DesignOf` と `FontOf` を対にしている理由そのもの**）。
    **付点の*幅*も同じ形**（**2 つの付点は 1 幅ぶん離すのに、幅をフル サイズの付点で測っていた**）。
    ⚠️ **cue も同じ欠陥を持っていて、コーパスは 1 冊も観測していない**——**旗つき・付点つきの cue を
    書いた fixture が無いので snapshot は 1 枚も動かなかった。** ⇒ **観測者を足す価値がある。**
    **⒝ 休符は grace pass の `MusicFace(Emmentaler-14)` スコープの中で*フル サイズ*に描かれていた**
    （第313 前半・`0e27b056`）。
  - ✅✅ ★★★★ **【⒞ 付点は第315 が帰した。そして「故意で残した」の理由づけは*両方の家が外れていた*】**
    **`GraceNoteEngraver.Dots` は退役**（`DrawNote`/`DrawChord` の `!GraceTime` 門を外しただけ）。
    ★★★ **第313 の見立て「正しい規則を持っているのは grace 家のほう」は半分だけ当たり**——
    **grace 家は確かに `DotColumn`（移植）を呼んでいたが、その旗の支持を*退役した平らな符尾*
    （3.5 × magstep(−3) = 2.475）で測っていた**ので、**音高で変わる答えを 1 つに潰していた。**
    ★★★★ **そして `DotColumn` 自身の Y 門は*切れていた***——**`Skyline.FromBoxes` の
    `MergeSegments` が*重なる 2 箱を和集合 1 本に畳んで X は max を採る***ので、
    **必ず旗と重なる符尾の箱に旗の X が垂れていた。** **第314 の 4 冊で割れなかったのは偶然**
    （**問い合わせが両端 strict ＋ 上向き符尾の箱が符頭の行から始まる**）。
    ★ **canonical 2.26.0 に 6 冊訊いた**（`scratch/p315/gracedot-dump.ly`・**答え＝Dots 左 − NoteHead 左**）:
    **`\grace { g'8. }`（線・持ち上げ・符尾 2.80）1.226585 ／ `f'8.`（間）1.226585 ／
    `d''8.`（線・持ち上げ・符尾は*短縮されて* 2.50）1.747274 ／ `e''8.`（間・2.40）1.226585 ／
    `d''16.` 1.747274 ／ `g'16.`（短縮無しでも 16 分の旗は 0.354 深い）1.747274。**
    ⇒ ★★★★ **答えを決めているのは「線か間か」ではなく*描かれた符尾の長さ***。
    **Lily# は `g'8.` の 1 冊を 1.7473 で刷っていた（＝0.5207 のずれ）。**
    ★ **番人は `test/grace-dot-flag-column`**（**6 冊 ＋ 梁の対照 1 冊。fixture の頭に LP の 6 数**）と
    **`GraceBodyValidatorTests.AGraceDotClearsTheFlagOnlyWhenTheFlagIsOnItsRow`**
    （**ページから読む形に書き換えた＝単体呼び出しは消えた。ページの x は 2 桁なので
    ±0.01・答えの差は 0.52**）。
    ⚠️ **以下は第313 が書いた当時の観測。「フル サイズでは一致する」は今も本当だが、
    その理由づけ（旗が付点の段に届かない）は第314 が反証している。**
  - ⚠️⚠️ ★★★★ **⒞ 付点だけは grace 家に残した。故意で、理由は測ってある。**
    **付点の X は `Svg/Layout/DotColumn`＝LP の移植（support の右スカイラインを符頭のインク右で床張り）で、
    その*唯一の呼び手*が `GraceNoteEngraver.Dots`。`DrawNote`/`DrawChord` は「符頭インク右＋付点 1 つ」の
    平らな式**——**フル サイズでは一致する**（**旗が付点の段に届かない**）**が grace では一致しない**
    （**符尾が短いので、持ち上がった付点が旗に当たる**）。**実測 1.226600（空き）対 1.747300（線）。**
    **`DrawNote` に渡すと両方 1.2266 になって対が消える。**
    ⇒ ★★★ **次の一手はここ＝`DrawNote`/`DrawChord` も `DotColumn` に訊く**（**`DotColumn` の remarks は
    既に「ONE HOUSE, THREE CALLERS」と*書いている*——規則については本当で、呼び手については未だ**）。
    ★ **フル サイズの答えは変わらないはず**（**その indistinguishability は `DotColumn` の doc が測ってある**）
    **が、`AGraceDotClearsTheFlagOnlyWhenTheFlagIsOnItsRow` が 4 桁で見張っているので、動いたら分かる。**
  - ⚠️⚠️ ★★★ **⒝2 で*まだ*畳んでいないもの**: **`GraceColumnHeads` の `HeadOffsets`/`AccidentalOffsets`
    は*予約*側（`HeadInkRight`/`AccidentalInkLeft` → `SpacingRules.GraceColumns`）が読むので死んでいない。**
    ✅ **`GraceNoteEngraver.Dots` は第315 で死んだ**（**残った `StemLength` の読み手は
    `DrawGraceBeam` の quant できなかったときの fallback 1 つだけ＝観測者ゼロの近似**）。
    **`SharedRenderer.GraceNotes` は 744 → 約 460 行、`DrawGraceStemsAndBeam` は `DrawGraceBeam` になった。**
  - ⚠️⚠️ ★★★★ **X について 2 軒が今も食い違いうる唯一の場所＝*ossia の上の梁つき grace*。**
    **符頭は staff の group の中（X は縮まない・共有列の上）、梁は overlay pass のままで
    列 offset に ossia 係数を掛ける。** ⚠️ **そういう本はコーパスに 1 冊も無い**
    （`test/ossia-beams` の grace は単音）。**`DrawGraceNotes` に名指してある。**
  - ★ **動いた snapshot は 10 枚で、9 枚は第309 が名指したその 9 枚**（10 枚目は本便が足した
    `test/grace-rest`）。**どれも要素数は増減 0**（**grace font 40・score font 4・line 27 が両側同数**）。

  - ✅ ★★★ **第312 が道具立てを入れた＝`Svg/Layout/GrobFontSize.cs`**（`3ccc8a8d`）。
    **`StepOf(item, grob)` ＋ `FontOf` / `DesignOf` / `ScaleOf`。**
    **問いが *grob ごと*なのは LP がそうだから**——**cue は context の `fontSize` を
    `Font_size_engraver::acknowledge_font` が全 grob に*加算*し、grace は
    `general-grace-settings` という*grob ごとの表*（Rest の行が無い）**。
    **最初の読み手は普通の音符・和音の描画**（`note.IsCue ? … : …` 14 か所を畳んだ）。
    **インクは 1 も動いていない**（**全木 1418 冊 ×（svg・check）SAME 1418 / MOVED 0**）。
  - ⚠️⚠️ ★★★★ **⒝2 は*バイト同一で通せない*。動く理由が 4 つ在り、どれも「同じ量を 2 軒が
    違う規則で持っている」形**（**閉じるのはまさにこの 4 つ**）
    ★★ **【第313 の結果＝4 つのうち 3 つは当たり、⒞ は*外れた*。当たった/外れた記録として残す】
    ⒜ ⒝ ⒟ は畳んだ**（**⒟ は本当に「測り直しが要らない」だった＝snapshot の臨時記号 X は 1 つも動いていない**）／
    **⒞ は畳めなかった——起票は「2 軒が違う規則」と書いたが、*正しい規則を持っているのは grace 家のほう*で、
    普通の路の平らな式が LP の移植でない。向きが逆だった**（上の ⒞ を見よ）:
    **⒜ 符頭の字形——脇の模型は*どの音価でも* `NoteheadBlack`**（`graceHeadNoteValue = 4` の
    自白つき）**、普通の engraver は音価から選ぶ＝`grace { d'2 }` が動く**／
    **⒝ ledger——脇の模型はその場で引き、普通の路は*隣の列と短くし合う pre-pass*** ／
    **⒞ 付点——`GraceNoteEngraver.Dots`（flag を support に取る）対 `DrawNote` の素直な dot 列** ／
    **⒟ 臨時記号——`GraceColumnHeads.AccidentalOffsets` 対 `StaffAccidentalColumns` の詰め。**
  - ★★ ✅ **最初の設計判断は「grace 項目の X をどう層に出すか」**（**第313 が下の助言のとおりに実装した＝
    `ScoreLayout.GraceColumnXs`。予測は当たり**）——**普通の engraver は
    `ml.Items[i].X` / `GetXForTiming` からしか X を取れず、どちらも grace 列を知らない**
    （**`ml.Items` は primary voice のスロットしか無いので向かない**）。
    **`ScoreLayout.GraceNoteLayouts` に voice を持たせ、`(staff, voice, measure, itemIndex) → X`
    を引ける器を足すのが素直**（**X の*法*は `SpacingRules.GraceColumns` のままでよい＝
    spring は「残る grace 固有の 4 つ」の 1 つ**）。
  - **⒝2 が畳めるもの**: **`GraceNoteEngraver` 645 行・`SharedRenderer.GraceNotes` 744 行・
    `GraceColumnHeads` 231 行**、**そして ⒝1 の足場 11 か所。** ⇒ **⒞・⒟・U8b は*構築により*閉じる。**
  - ★★★ **`GraceColumnHeads` は自分で「A TRANSLATION, NOT A SECOND MODEL」と書いており、
    *符頭の二度*も*臨時記号の積み*も、フル サイズの和音が通るのと同じ `ChordHeadPositioning`／
    `AccidentalPlacement` を grace のフォントで呼んでいるだけ**（`:32-33`・`:82`・`:106`・`:209-210`）。
    ⇒ **符頭と臨時記号は ⒝2 でも*測り直しが要らない*。**
  - ⚠️ **残る grace 固有は 4 つだけ**: **grace run の spring（`SpacingRules.Grace`）／梁の接頭辞と quant／
    acciaccatura の斜線／主音符への anchor（grace slur）**。**⒝2 の LP campaign はこの 4 つに絞れる。**
  - ⚠️⚠️ ★★★★ **⒝2 は「フル サイズの規則 × 0.7」では*ない*。反例が 2 つ測ってある**:
    **タイの Y は grace でもフル サイズでも符頭の *1.0000* 下**（量子化されているので縮まない・第310）／
    **休符は full size で描かれる**（第308）。⇒ **engraver は*自分で掛け算せず grob にフォントを訊く*こと。**

  ⚠️⚠️ ★★★★ **【入口として名指されていた LP の 3 量は第310 が測った——そして 6 秒で済んだ】**
  （`scratch/p310/lp/`＝**LP 22 冊 ＋ `dump.py` ＋ `measurements.md`**。**WSL の 2.27.3**・`-dbackend=svg`）
  ⚠️⚠️ **「正典 exe は 13 分（第302）・17 分（第308）ブロックする」は本当だが、*訊かなければ*ブロックしない。**
  **第309 は ⒝ を見送る理由の筆頭にこの前科を挙げていた**——**WSL は 18 冊を 6 秒で返した。**
  ⇒ ★★★ **RULES §5.2 の手順どおり「量ごとに」読むこと**: **突き合わせ済みは梁 span だけ**
  （第308 が第300 の正典値と 4 桁一致を見た）。**下の 3 つは*未照合*で、⒞⒟ を彫る便は照合するか 2.26.0 で取り直す。**
  - **⑴ スラーは描かれ、しかも run を*広げる***: `{ d'16 e'16 }` の列歩 **1.4179** → `{ d'16( e'16) }` で **1.5000**。
    **フル サイズの同じ対は動かない**（`d'16( e'16)` も `d'16 e'16` も stem 間 2.5042）⇒ **grace 専用の規則ではなく、
    grace run だけが感じるほど狭い普通のロッド。** ⚠️ **下限を決めている数は未解決**——`d'( g')` は **1.6479** で、
    `Slur.minimum-length` の 1.5 では説明できない。**3 冊は規則ではない**（RULES §5.0）。
    ★ **付着点は各符頭から左右対称**（dy はどの本でも **0.8941**、dx だけが音程で動く: 0.5557 / 0.3622 / 0.6649）。
  - **⑵ タイは*何も動かさない***: `{ d'16~ d'16 }` は `{ d'16 d'16 }` と符頭・stem・梁 span まで 4 桁一致。
    **フル サイズでも同じ**（t3 対 t5）。⇒ **タイは、どちらのサイズでも、幅を 1 も要求しない。**
    ★★★ **そして tie の Y は grace でもフル サイズでも符頭の 1.0000 下**——**grace のフォントで縮まない。**
    ⚠️ **プローブは両側とも `d' d'`**（**第309 が音高違いの対で壊した罠を繰り返さないため**）。
  - **⑶ script は*grace のフォント*で描かれ、幅を予約しない**: `-.` は sc=**0.0028**（休符は full size だった・第308。
    **両方 `general-grace-settings` の名指し表から出る**）、**列原点は対照とバイト同一**。
    **`->` は 0.0713・`\fermata` は 0.4924 だけ押す**＝**script 自身の左向きのインクであって規則ではない。**
    ⚠️ **staccato の dx は 0.70 倍に近い（0.4590 対 0.6521）が dy は違う**（0.7354 対 0.9450）——
    **2 点で 2 未知数は fit であって finding ではない。コードに書かないこと。**
    ★ **線上の音符に付く script は次の*間*へ量子化される**（`b'16-.` は dy 1.5000）。

  ⚠️⚠️ ★★★★ **第308 の後半＝⒜ の休符と ⒞ の梁は*同じ 1 つ*だった、という前半の見立てが実装で当たった。**
  **休符は「1 列が符頭を 0 個持てる」だけ**（模型は和音と*同じ 1 語*）で、
  **そこから出る梁の規則を LP に 12 冊で訊いた**（`scratch/p308/lp2/measurements.md`・**座標で。第302 は polygon の数で読んでいた**）:
  - **`{ d'16 e'16 r16 f'16 }` の梁は span 1.4679・y 11.0386..11.7006 で、`{ d'16 e'16 }` 単独と*4 桁一致*。`f'` は旗。**
  - **`{ d'16 e'16 r8 f'16 }` も同じ**＝**休符の*音価*は入らない。** **`{ d'16[ e'16] f'16 }`（手書きの `[ ]`）も同じ**＝**⒞ の梁と ⒜ の休符が 1 つの規則で足りる根拠。**
  - **`{ d'16 r16 e'16 f'16 }` は梁ゼロ**——**`e' f'` は隣り合う 2 つの符頭なのに。**⇒ **「極大部分列ごとに梁」ではなく「*先頭の*連なりか、無しか」**（第302 が polygon で出した結論と一致）。
  ⇒ ★★★ **だから部分梁は*新しい梁*ではない**——**既存の quanter に*接頭辞だけ*を渡せば、LP が描く配置がそのまま出る。**
  **実装は `IsBeamedRun`（bool）を `BeamedPrefix`（**列の**個数）に変え、4 人の読み手（予約・quanter・dot 列・描画）が全員*どの列か*を訊くようにしただけ。**

  ⚠️⚠️ ★★★★ **そして*装飾の休符は full size で描かれる*。** **1 冊の中で並べて実測**
  （`scratch/p308/lp2/s2_gracerestchord`＝`\grace { r16 d'16 }`）: **休符は 0.0040、隣の符頭は 0.0028＝magstep(−3)**、
  **休符の path データは主旋律の休符と*バイト同一*。** **機構は正典ソースに在る**——
  **`general-grace-settings`（`scm/music-functions.scm:636-650`・v2.26.0）は Stem・Flag・NoteHead・TabNoteHead・Dots・Accidental・Script・Fingering・StringNumber に font-size を与え、*Rest を 1 度も名指さない*。**
  ⇒ **「装飾は小さく描く」は他の全部の grob には当たるが、休符には当たらない**——**grace のフォントで描くと LP より 1/4 狭い休符になり、列ごと狂う。**
  ⇒ **これが「休符の次の列だけ広い」理由でもある**（**LP 1.7000 対 1.4180・Lily# は 1.70 対 1.42**）。

  ⚠️⚠️ ★★★ **版の扱いがここで 1 段変わった。** **第302 は「WSL の 2.27.3 は*質的*確認まで。座標は正典 2.26.0 で取り直せ」と書いた**が、
  **第300 が正典で測った 2 冊と本便が WSL で測った同じ 2 冊は、梁 span が 1.4679 / 2.8859 で*4 桁一致*する。**
  ⇒ ★★ **「WSL は座標に使えない」ではなく「*その量について*両版が一致するかを 1 度確かめれば使える」**——
  **確かめ方は既存の正典実測と突き合わせること**（→ RULES §5.2）。
  ⚠️⚠️ **本便はそれを*確かめざるを得なかった***: **正典 exe は 17 分ブロックして 1 バイトも書かなかった**（**CPU 1.4 秒 / WS 28 MB＝第302 の「起動しきっていない」署名そのもの**）。
  ★ **原因の候補は 3 つ潰した**（→ §2 G）。

  ⚠️⚠️⚠️ ★★★★ **第308 の骨＝⒜ は*さらに 2 つだった*。和音と休符は難所が違う。**
  **第302 はこの 2 つを「難所は住所ではなく*模型*」という 1 つの箱に入れたが、
  和音は「1 列が符頭を N 個持てる」だけで*梁には 1 行も触らない***
  （**第302 自身の member 表がそう言っている——和音を先頭・中間・末尾のどこに置いても polygon 2・path +4**）。
  **休符は「1 列が符頭を *0 個* 持てる」で、その瞬間に LP の梁は*先頭の連なりだけ*を覆う**
  （同じ member 表の `{ d'16 e'16 r16 f'16 }`＝**梁 1 本と旗 1 本を 1 つの群が同時に持つ**）——
  **つまり休符は ⒞ の*部分梁グループ*の模型変更そのもので、⒜ ではない。**
  ⇒ ★★★ **この起票が*仕分けを間違えたのは 4 度目***（第298 は症状で／第299 も症状で／第302 は難所で／本便は*難所の粒度*で）。
  **毎回、実際に手を動かす便が 1 つ小さい単位を見つけている**（→ RULES §5.0）。

  ★★ **第308 が入れたもの**（`Svg/Model/GraceNoteItem.cs` の `GraceColumnInfo` / `GraceHeadInfo`・`Svg/Layout/GraceColumnHeads.cs`）:
  **`GraceNoteItem.Notes`（＝1 音の平らな列）を `Columns`（＝1 列・符頭 N 個）にしただけ**で、
  **幾何は 1 つも発明していない**——**二度の寄せは `ChordHeadPositioning`、臨時記号の積みは `AccidentalPlacement`、
  どちらも*既にフォントを取る*し*住所を取らない***（起票の予測が当たった）。
  ⚠️⚠️ ★★★★ **LP 実測がフォントの規則を 4 桁目で名指しした**（`scratch/p308/lp`・**WSL の 2.27.3。
  ただし読んだのは*両側の比*なので、正典 2.26.0 を待たずに読める量**）:
  **`grace { <c' d'>16 }` の二度の寄せは 0.8530** で、
  **14 デザインの `1.298161 × magstep(-3) − 0.065 = 0.852938` には合うが、
  20 デザインを縮めた `1.304200 × magstep(-3) − 0.065 = 0.857209` には合わない**
  （**全サイズの `<c' d'>4` は 1.2392 ＝ 1.304200 − 0.065 で桁まで一致**）。
  ⇒ ★★★ **`GraceNoteItem.ScaleFactor` の doc が何便も警告していた 0.004270 が、今度は LP の印字で裏取りされた**——
  **縮尺ではなく*デザイン*を渡す**（`ChordHeadPositioning.CalculateOffsets` の `headFont` overload）。
  ⚠️ **cue の和音は今も scale を渡している**（`SharedRenderer.Noteheads` が `EngravingDefaults.CueScale`）＝**同じ 0.0043 を抱えたまま**。
  **本便は触っていない**（cue の snapshot が動くし、cue のフォントを LP に訊く実測が別に要る）——**新規起票 ⑴ として下に立てた。**

  ⚠️⚠️⚠️ ★★★★ **⒜ を入れた瞬間に MIDI との*1 オクターブ*の食い違いが露出した——前から在って、見えなかっただけ。**
  **`MidiExporter.ProcessGrace` の和音の腕は各構成員を*直前の音に対する相対*で解いていた**のに、
  **コメントは「matches ProcessChord / CreateChordItem」と書いていた**（＝第178・第306・第307 に続く *`observed by: NOTHING` の兄弟*）。
  **実測**（`scratch/p308/ab/d_chordwide` 対 `d_mainwide`）: **`grace { <c b>16 }` は 60 と 59 を鳴らし、
  同じ本の `<c b>4` は 60 と 71 を鳴らす。ページと MusicXML はどちらも B4。**
  ⇒ ★★★ **ページが和音を 1 つも描かなかったあいだ、この食い違いは*比べようがなかった*。**
  **「4 人の読み手を毎回数えよ」がなぜ規則なのかの、いちばん短い実例**（→ RULES §5.3）。
  **直し方は 1 軒に寄せただけ**（`MidiExporter.ResolveChordMemberPitch`／`MusicXmlExporter.ResolveChordMemberPitch`——
  **XML 側は*まだ食い違っていなかった*が、grace の腕を書くのに主旋律の腕の綴りが要るので同じ形にした**）。
  （**tuplet は第302 が、phrase 参照は第300 が閉じた＝症状で作った箱はこれで空になった**）。
  ⚠️ **直し方は「装飾音符に住所を足す」ではなく*本体を普通の walker で歩く***——
  **`ProcessCueRegion` が cue 領域にしているのと同じ**（`MeasureCollector.MusicWalk.cs` の
  「A cue is a REGION, walked with the ordinary walker …」）。**LilyPond の grace も Voice context 1 つで、そこが正典。**
  ⚠️ **難所は住所**: **装飾音符は measure の item ではないので `ItemIndex` を名乗れない。**
  ★★ **射程は着手前に測ってある（2026-08-31・第306。着手はしていない）**——**ディスク 1973 冊で grace の drop を 1 行でも報告するのは 47 冊で、45 冊が第293／第298／第300／第301／第302 の*自分の A/B プローブ*。追跡本は 2 冊だけ（`audit/lpreg/grace-slash-probe.lys` と `grace-tie-probe.lys`）で、**ユーザーの 326 冊は 1 冊も当たらない**。**
  ⇒ ★★ **つまり ⒜⒞⒟ を入れてもページが動く既存の本はほぼ無い＝*snapshot の再ベースはほぼ発生せず、承認待ちで止まる可能性は低い***。**着手する便はこの数を数え直さなくてよい。**
  ★ **drop の内訳（行数）**: **⒜「bare notes only」22 ／ ⒞「no slur, beam or tie」19 ／⒟ ほか 24 ／ ⒝ tuplet bracket は 0**（**第302 が閉じたので 0 なのが正しい＝この計器が生きていることの陽性対照になっている**）。
  ⚠️ **数え方**: `lysc check` の出力に `is not engraved` を含む行を数える（`GraceBodyValidator` の 4 つの文面が全部この語を持つ）。**本の数と行数は別に数えること。**
  ⚠️⚠️⚠️ ★★★★ **ただし*それは ⒞ と ⒟ の話で、⒜ には当たらない*——第302 が着手前の測定として
  数えた**（**この項で 5 例目の「測らずに書いた見積り」で、直前の便＝第302 自身が書いた**）:
  - ★★★ **`ChordNoteInfo` は `ItemIndex` を 1 つも持たない**（住所は容れ物の `ChordItem`
    ＝`MusicItem` の側に在る）。**`RestItem` も同じ**（`BaseDuration`・`Dots`・`TimeScale`・
    `IsSpacer` だけ）。
  - ★★★ **和音の頭の置き方＝`ChordHeadPositioning.CalculateOffsets(notes, stemUp, noteValue,
    headScale)` は住所を 1 つも取らない**——**しかも既に*縮尺つきで呼ばれている***:
    `SharedRenderer.Noteheads.cs:427` が **`chord.IsCue ? EngravingDefaults.CueScale : 1.0`**
    を渡している。**cue の和音がもう同じ家を小さい縮尺で通っている。**
  - ★★★ **住所を鍵にする 2 軒**（`StaffAccidentalColumns` の
    `(measureIndex, voiceId, itemIndex, noteIndex)`・`NoteCollision`）**は、*grace という語を
    1 度も書いていない***（`grep -i grace` が両方 0 行）＝**装飾音符はそもそもその 2 軒を
    通らない。**
  - ★ **描画側の縮尺も既に在る**（`SharedRenderer.GraceNotes.cs` は `GraceNoteLayout.Scale`
    ＝`GraceNoteItem.ScaleFactor` で符頭フォントも臨時記号フォントも縮めている）。
  ⇒ ★★★★ **⒜ の難所は住所ではなく*模型*だった**: **`GraceNoteItem.Notes` は
  `ImmutableArray<GraceNoteInfo>` の*平らな列*で、「この 2 つは同時に鳴る」とも
  「この 1 つは休符だ」とも言えない**。**和音と休符が要るのはその 1 語で、住所ではない。**
  ⇒ ★★ **だから ⒜ と ⒞・⒟ は*別の難所*で、同じ箱に入れてはいけない**
  （**症状で仕分けた箱を空にしたのに、今度は*難所*で 1 つに括っていた**）。
  ⚠️ **測っていないこと**: **その 2 軒が grace を知らないのが「要らないから」なのか
  「取りこぼしているから」なのかは、この測定は答えない**（**今日 grace の和音は 1 つも
  彫られないので空虚に真**）。**⒜ に着手する便は、そこを 1 冊で測ってから決めること。**

  ⚠️⚠️⚠️ ★★★★ **第302 が閉じたのは tuplet で、閉じたのは*ページ・音・XML の 3 つ同時*だった**
  （`GraceBodySupport.Expand` の第 2 の容れ物・`GraceTupletStartMarker` / `GraceTupletEndMarker`）。
  **着手前の実測**（`scratch/p302/ab`・両側 Release・`data-pos` 伏せ）:
  **`grace { tuplet 3/2 { d'16 e' f' } } c'4 c'2.` の svg・`.mid`・MusicXML は 3 つとも
  「grace を 1 つも書かない本」と*バイト同一*で、正しかったのは narrowing を持たない
  `.ly` 双子だけ**——**第301 の phrase 参照は「4 人のうち 2 人が取り残された」形だったが、
  tuplet は*最初から誰も歩いていなかった*。**
  ★★★ **そして「鳴る長さは縮むのに描かれる音符は変わらない」は LP では*1 つの機構***:
  `LILYPOND-REF: lily/duration-scheme.cc:190-200 ly_duration_compress` ——
  **`\tuplet` は音楽を `normal/actual` で compress し、duration の compress は `factor_` を
  掛けるだけで `durlog_` と `dots_` を触らない**（**log と dots が符頭・旗・梁の本数を決め、
  factor が moment を決める**）。**LP の `\midi` 実測**（`scratch/p302/lp`・division 384・
  ⚠️ **WSL の v2.27.3。正典 2.26.0 の exe は同じ 3 冊で 13 分ブロックして 1 バイトも書かなかった**
  ——**tick は質的、正典なのは上の機構のほう**）:
  **`\grace { d'16 e' f' } c'4` は装飾音符を 0 / 21 / 43 に置き主音符を 64 で渡し、
  `\grace { \tuplet 3/2 { … } } c'4` は 0 / 14 / 29 に置き 43 で渡す＝`round(64 × 2/3)`。**
  ⇒ **ページの腕は*文書化された no-op*、MIDI と XML は `_tupletStack` に積むだけ**
  （**`FractionToTicks`・`CurrentTupletRatio` は主旋律が既に読んでいる家＝量は 1 つも足していない**）。
  ⚠️ **phrase 参照と違い枠は開かない**——**主旋律でも `tuplet 3/2 { d'16 e' f' } c'` の c は 16 分**。

  ⚠️⚠️ ★★★ **ただし drop は消えず*半分になった***。**phrase 参照は grob を 0 個持つので
  第300 は drop 行ごと消せた**が、**tuplet は*括弧と数字*を持つ**ので、
  **LYS4020 は新しい kind（`GraceDropKind.Bracket`）で残り、文面が
  「the bracket and number of a tuplet … are not engraved … although the notes it holds ARE drawn」
  になった**（**本体が結局何も彫らないときは約束のほうを引っ込める**）。
  ⇒ ★★★ **判定法（第300 ⑴ の 1 段先）**: **「grob か容れ物か」で仕分けたあと、
  *その容れ物が自分の grob を持つかを 1 行書く***——**0 個なら drop は消え、
  N 個なら drop は残って*文面が変わる*。**
  ⚠️ **MusicXML はここで 1 つだけ narrowing を決めた**: **`<time-modification>` は書き、
  `<notations><tuplet>` は書かない**——**括弧と数字はページが持てない 2 つの grob そのもので、
  XML だけが描けと言うと同じ本の LYS4020 がその片方について嘘になる**。
  **`ATupletInAGraceBody_ClaimsNoBracketThePageDoesNotDraw` が「ページが括弧を覚えた日」に赤くなる行。**

  ⚠️⚠️ ★★★ **第301 実測＝*その 3 つのうち tuplet は容れ物*で、和音・休符と同じ箱に入れたのは
  また症状での仕分けだった**（`scratch/p301/lp`・LP 2.26.0・`data-pos` 伏せ diff）:
  **`\grace { \tuplet 3/2 { d'16 e' f' } }` は `\grace { d'16 e' f' }` と*音符 3 つが座標まで
  バイト同一*で、増えるのは斜体 serif の `3` だけ**（**梁が括弧の代わりをする**）／
  **音価を 4 分にして梁を外すと、増えるのは*括弧（`<line>` 4 本）と `3`*、音符 3 つはやはり同一。**
  ⇒ ★★★ **tuplet を展開しても綴りは 1 つも増えない**（**phrase 参照と同じ**）——
  **増えるのは括弧と数字を描くときだけで、それは drop に残せる。**
  ⇒ ★★ **和音（`<path>` +1＝符頭）と休符（`<path>` +1＝休符 glyph）だけが本当に grob を要る**
  ——**そして第302 が tuplet を閉じたので、⒜ に残っているのはその 2 つだけ。**

  ⚠️⚠️ ★★★ **実コーパスの射程はタイだけ**（**第301 実測・第302 が閉じたあと取り直した**）。
  **第302 の数**（`scratch/p302/reach.ps1`・**ディスク 1761 冊**——**本便の perf 計器
  `perf-gracetuplet200.lys` は除いた。200 行の grace tuplet を 1 冊に書いた本なので、
  数えると*計器がコーパスの顔をする***）: **`grace` 語を書くのは 142 冊、
  LYS4020 は 63 行 / 47 冊**。**族別: annotation 20／beam 8／chord 8／rest 6／slur 6／tie 5／
  dynamic 4／*tuplet の括弧と数字* 4／phrase 参照 2。**
  ⇒ ★★ **tuplet の 2 行は消えず、*bracket の 4 行*になった**（**本便が閉じた 4 冊＝掃きが
  動かした 4 冊と同じ本**）——**族は残り、言っていることが変わった**（§5.0 の第302 の項）。
  ⚠️ **そのうち*追跡されている*本は依然として 2 冊だけで、2 冊ともタイ**
  （`audit/lpreg/grace-tie-probe.lys`・`grace-slash-probe.lys`）——
  **残り 45 冊は第293・第298・第300・第301・第302 が書いた scratch のプローブ。**
  **第301 の見立て（射程を持つのはタイだけ）は閉じたあとも成立している。**
  ⚠️ **`lysc check` は `LYS4020` という綴りを刷らない**（コードが出るのは `lysc svg` の診断行）——
  **最初の全数掃きはこれで 0 行を返した**（RULES §5.4 に汎化）。
  ⚠️ **本便もこの計器を*当たると分かっている 2 冊で先に鳴らして*から回した**
  （`b_tuplet.lys` と追跡の `grace-tie-probe.lys` が 1 行ずつ＝§5.4）。

  ⚠️⚠️⚠️ ★★★★ **そして*どれを取るにせよ、読み手は 4 人いる***——
  **ページに grob を足す変更は、MIDI と MusicXML の narrowing がページと食い違ったままかどうかを
  毎回数えること。** **今も食い違っている**: **和音と休符は MIDI では 2026-07-10 から鳴っていて、
  ページと XML は落とす**（`scratch/p301/sweep.json` の `i_abschordphrase.lys` がその 1 冊＝
  **MIDI だけ動き XML は動かなかった本**）。

  ⚠️⚠️ ★★★ **その難所の*形*は 2 便続けて書き直された。第300 の実測が 3 度目で、今度は場所まで数えた**
  （**第298 の一覧＝予測／第299 が「memo 4 軒ではない」まで／第300 が「ではどこか」**）:
  - ★★★ **タイ・梁・スラーは `:648` の `measure.Items[…]` からは 1 つも入らない**——
    **`ArticulationEngraver` に*すでにレイアウト済みの*配列として渡される**
    （`tieLayouts`・`beamLayouts`・`slurLayouts`）。**第298 の「`:648` から全部引く」は外れ**
    （**`:648` が読むのは `GetStaffPosition`・`GetStemUp`・`NoteheadHalfWidth`・(タブのみ)`Midi`/`StringNumber` の 4 つだけ**）。
  - ★★★ **住所を要求しているのは、そのファイルの中の*局所辞書 4 軒*で、鍵は 3 軒が `(staff, voice, measure, item)`**:
    **`tiesAtBound`（`:385`）／`BuildBeamGroupMap`（`:1947`）／`BuildBeamedStemTips`（`:2004`）**。
    **4 軒目 `slursAtMeasure`（`:408`）だけは `(staff, voice, measure)`＝*小節*までで item を要らない。**
  - ★★ **その `item` の出どころは `measure.Items` ではなく*スパンの模型*** ——
    **`TieItem.Start/EndItemIndex`・`BeamGroup.Members[i].ItemIndex`・`SlurItem.Start/EndItemIndex`**。
    ⇒ ⚠️⚠️ ★★★ **「`ItemIndex` を名乗る模型型は 10 個」は*外れで、実数は 21*** （**2026-08-31・第309 が
    着手前に数え直した＝RULES §5.0「起票の数値は着手する便が数え直す」の 6 例目**）。
    **第300 が数えたのは*⒞⒟ が要る型*で、それを「`ItemIndex` を名乗る型」と書いてしまった。**
    **落ちていた 11 型**: `ArpeggioItem`・`CrossStaffItem`・`GlissandoItem`・`GrobProperty`・
    `HairpinItem`・`LyricItem`・`OttavaBracketItem`・`PedalItem`・`PercentRepeatItem`・
    `TextSpannerItem`・`TrillSpannerItem`。**21 型とも綴りは同じ＝`measure.Items` への裸の `int`。**
    **数え方**: `grep -rn "ItemIndex" LilySharp.Core/Svg/Model/*.cs | grep -E ":[0-9]+:\s*(int|required int)\s+\w*ItemIndex"` の**ファイル数**。
    ★ **ほかの数**: **Core の非コメント `ItemIndex` は 421 箇所／うち順序・算術が 43／`Items[…]` への直接添字が 16。**
    ⇒ **⒞ が要るのは Tie と Slur と Beam、⒟ が要るのは Articulation と Dynamic と MusicMark**（**この仕分けは正しい**）。
  - ★★★ **⒞ の*梁*だけは住所を 1 つも要らない**——**grace run の梁は `BeamLayout` を通らない**。
    **`GraceNoteEngraver.IsBeamedRun` が音価だけから決め、`GraceNoteLayout.BeamLeftY/BeamRightY` で運ばれる**。
    ⇒ **本体の中の `[ ]` は「住所」の問題ではなく `GraceNoteEngraver` の*部分グループ*の問題。**
  ⚠️⚠️⚠️ ★★★★ **そして「だから梁がいちばん安い」は*外れ*——第300 が §1 にそう書いた 30 分後に、
    自分で測って自分で消した**（**この項で 4 例目の「測らずに書いた見積り」で、しかも
    *同じ便が*規則を汎化したその日に書いた**）。**LP 2.26.0 実測**（`scratch/p300/lp`）:
    **`\grace { d'16[ e'] f'16 }` は梁 polygon 2 枚が x 0.0400→**`1.5079`**、
    `\grace { d'16 e' f'16 }` は同じ 2 枚が 0.0400→**`2.9259`**、
    そして `<path>` が 9 対 8＝*3 つ目の音符の旗*が 1 つ増える。**
    ⇒ **LP の答えは「1 本の梁が 2 音を覆い、3 音目は旗」＝*1 つの grace 群が梁と旗を同時に持つ*。**
    ⚠️ **Lily# の模型はそれを綴れない**: **`BeamLeftY`/`BeamRightY` は*単数*で 3 軒に在り**
    （`GraceNoteEngraver.cs:65-66` の layout ／ `ElementCoordinator.cs:2756` の geometry tuple ／
    `SharedRenderer.GraceNotes.cs:185`）、**`IsBeamedRun`（`:303`）は run 全体の all-or-nothing。**
    ★ **既定の側は既に LP と同じ形**（**Lily# の対照も梁 polygon 2 枚・8.17→11.14＝run 全体を 1 本で覆う**）、
    **`[ ]` を書いた本と書かない本は `data-pos` 以外バイト同一＝印が完全に無視されている。**
    ⇒ ★★★ **⒞ の梁は「住所が要らない」だけで「小さい」ではない**——
    **grace 群が*梁つき部分列と旗つき単独音の並び*を持てるようにする模型変更**で、
    **層は住所の仕事と同じだけ在る**（layout・coordinator・renderer・quanter・`IsBeamedRun`）。
  ⇒ ★★★ **教訓は §2 U8 が既に 3 回教えたものと同じで、4 例目は*自分*だった**: **この項の
    「射程」も「難所」も「どれが安いか」も、*書いた便が測らずに書いて*、*次に測った便が直した*。**
    ⇒ **起票の数値は、着手前に 1 回測る。「安い」も数値。**
  ⚠️ **並行に第 2 の engraver を建てないこと**（§5.2.1②）——**`ArticulationEngraver` 自身が
  「THE SAME ENGRAVER, NOT A SECOND SPELLING」と書いている。**
  ⚠️ ★★ **同じ理由で和音・休符を `GraceNoteInfo` の中に彫らないこと**——
  **それは和音レイアウト／休符レイアウトの 2 綴り目になる。第300 が phrase 参照を、
  第302 が tuplet を取れたのは、その 2 つが*どの綴りも増やさない*容れ物だったから。**
  ★ **番人は入っている**: `GraceBodyValidatorTests.EverythingReported_IsAbsentFromThePage` が
  **「LYS4020 が鳴る」と「その綴りは対照とページ同一」を*同時に*述べる theory** なので、
  **穴を 1 つ塞いだ人はその行が赤くなって迎えられる**（**第299 の付点で実際にそう働いた**）。
  ⚠️⚠️ ★★★ **ただし第300 では 1 行も赤くならなかった＝phrase 参照の行が最初から無かった**
  ——**その theory の `Book()` は phrase を宣言できない形**で、**drop 一覧が「a phrase reference」と
  綴っている族が、番人には 1 行も無かった**。⇒ ★★ **「閉じたら赤くなる網」は*族ごとに*在るか数えること**
  （§5.4 の「毒 0 赤は網が無い報せ」の、*毒を打つ前*版）。

- **U8b. ⚠️⚠️⚠️ 起票（2026-08-31・第308）＝*二声の同時 grace* は LP では 1 本の staff column に立つのに、Lily# は同じ X に重ねて描く。和音を待たずに、今日、裸の音符 1 個で出る**

  ★★★ **出どころは §2 U8 ⒜ の宿題**（「`StaffAccidentalColumns` と `NoteCollision` が grace を知らないのは
  *要らないから*か*取りこぼしているから*かを 1 冊で測ってから決めよ」）。**答えは「取りこぼしている」。**
  ⚠️⚠️ ★★★★ **第302 のその測定は *Lily# の側だけ*を見ていた**——**`grep -i grace` が両ファイルで 0 行、という測り方**。
  **LP に訊くと 2 軒とも grace を通す**（`scratch/p308/lp`・**WSL の 2.27.3**）:
  - **`x6_gaccsec2v`＝`<< { \grace { cis''16 } … } \\ { \grace { bis'16 } … } >>`**（**二声の同時 grace・臨時記号が二度**）:
    **臨時記号は 16.2208 / 17.0831 に*積まれ*、符頭は 18.1261 / 19.0440 に*寄せられる*。**
  - **`x7_gaccchord`＝同じ 2 音を*一声の和音*で書いた本**: **臨時記号は 16.2208 / 17.0831＝*4 桁一致*。**
  ⇒ ★★★ **`StaffAccidentalColumns` の doc が XVC/XVD で書いている法則**（「staff column の臨時記号は和音の臨時記号と*まったく同じに*詰まれる」）
  **が、grace の縮尺でそのまま成り立つ。**
  ⚠️ **符頭の寄せは別の量**: **二声の衝突 0.9179 対 和音の二度 0.8529**（**本流でも 1.3042 対 1.2392**）＝**`Note_collision` と「和音の二度」は違う規則。**

  ★★ **Lily# の実測**（`scratch/p308/ab/g1_2vgrace.lys`・`g0_2vplain.lys` が対照）:
  **`voice { grace { c''16 } c''4 … } { grace { d''16 } g4 … }` は grace の符頭を*両方 x=7.39* に描く**（**y は 9.55 と 9.05＝隣り合う段**）
  ＝**二度の 2 つの符頭がほぼ完全に重なる。** ⚠️ **和音は 1 つも要らない＝第308 の変更より前から在る欠陥。**

  ⚠️⚠️ ★★★ **これは ⒜ の続きではなく ⒞⒟ と同じ*住所*の問題**: **2 軒とも
  `(measureIndex, voiceId, itemIndex, noteIndex)` / `VoiceItemKey(measure, voice, item)` を鍵にしていて、装飾音符は `itemIndex` を名乗れない**
  （`ScoreLayout.GetVoiceOffset` / `StaffAccidentalColumns.Resolve`）。
  ⇒ ★★★ **だから §2 U8 ⒜ の「難所は住所ではなく模型」は*一声の和音についてだけ*正しい**——
  **同じ ⒜ の中に、住所を要らない半分（和音の中の二度・和音の中の臨時記号列＝第308 が閉じた）と、住所を要る半分（この項）が在った。**

  ★ **射程**: **ディスク 1992 冊で `grace` と `voice` を両方書く本は 8 冊、そのうち*同時に鳴る* grace を持つ本は 0**
  （`audit/lpreg/grace-dirpoly.lys` の第 2 分岐は全部 spacer）——**自分のプローブ 2 冊を除けば実コーパスの射程は 0。**
  ⇒ **⒞⒟ と一緒に住所を作る便で閉じるのが安い。単独で追わないこと。**

  ★★ **第357 再測定（⒝2 の後・`scratch/p358/lp/{g0_2vplain,g1_2vgrace,g2_gracechord}.lys`・`pair358.ps1`・LP 2.26.0
  の `settings.ly`＝NoteHead／Accidental の X を刷る）＝住所は出来たが*閉じていない*。形が変わった**:
  「同じ X に重なる」は消え、**Lily# は二声の grace を*別々の列*に彫る**（voice 1: ♯ 7.39・符頭 8.43／voice 2: ♯ 8.53・
  符頭 9.57／主音符 11.51）。**LP は 1 本の staff column**（♯ を 7.685 / 8.547 に積み・符頭 9.590 / 10.508＝collision 0.918・
  主音符 13.072）。**voice 2 の ♯ は voice 1 の符頭のインクに重なる**（8.53 対 8.43〜9.3）。
  **対照の和音 grace `<bis cis'>16`（`g2_gracechord`）は Lily# が LP と 3 桁一致**（♯ 0.86／符頭 0.85／主音符までの
  deltas 1.90 / 2.75 / 5.31 対 LP 1.905 / 2.758 / 5.322・全体の −0.30 は台帳 `grace.column.*` の既知の島）
  ⇒ **列の中の詰め（`StaffAccidentalColumns`・二度）は届いている。欠けているのは「同じ grace 拍に立つ列を voice を跨いで
  1 つに合体させる」こと**——LP は grace の Moment（`grace_part_`）が同じ列に立つが、Lily# の grace 列は voice ごと
  （`SpacingRules.GraceColumns`／`DeriveGraceColumns`）。**射程は今も 0**（第357 は数え直していない・上の 8 冊の数のまま）。
  **直すなら列の合体＝出力が動く（承認要）**。

- **U8c. ✅ 閉じた（第316）＝反転符頭の shift も ledger も click 箱も「その符頭のフォント」から出る。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）


- **U9. ✅ 閉じた（第294・`65224424`・ユーザー決定）＝`note~@mark("X")` の印が自分の住所に `~` を → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U10. ✅ 閉じた（第295・`ca0cbf2e`・ユーザー決定）＝弓は*それを書いた文字*を名乗る** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U11. ✅ 閉じた（第297・`bb6932e3`）＝注釈が書いた弓は*その注釈の `@`* を名乗る** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U12. ✅ 閉じた（第296・ユーザー決定）＝基準は「タブか」ではなく `TabNumbersOnly`** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **U13. ✅ 閉じた（第314・ユーザー報告 2026-09-01「volta ブレースの線が太すぎるように見える」） → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

### T. タブ譜 × 実コーパス（2026-09-01・第317 起票）← **新ワークストリーム**

> ★★★ **なぜこの族が在るか**: **ユーザー実コーパス 314 冊の 93%（293 冊）がタブを書くのに、
> 追跡コーパスでタブを書く本は 9%（52/585・fixture は 32）**。**回帰網が実使用と別のものを測っている。**
> ★ **比較の基準は `scratch/ベースタブLy` の*手書き* `.ly` 286 本**（285 組が `.lys` と対）——
> **exporter を通らない独立した正解**。**LP にそれを描かせ、Lily# に `.lys` を描かせて突き合わせる。**
> ⚠️ **`.ly → .lys` の importer は要らない**（未知数が 2 つになるだけ）。第317 が測って撤回した。
> ⚠️⚠️ **他人の `.ly` を正解に使うときは、その人が定義したマクロを先に読むこと**——
> **第317 は `tnh = { \once \hide TabNoteHead }` を読まずに絵から「LP はタイ先の数字を刷らない」と
> 報告し、ユーザーに訂正された**（**LP の既定は*刷る*。Lily# が意図して刷らないだけ**）。

- **T1. ✅ 閉じた（第318・案A の忠実移植）＝タブのスラーは「普通の採点器をタブ譜の枠で走らせ、そのあと 0.35 平行移動する」の 2 段になった** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **T2. ✅ 閉じた（第328 第 8 便・ユーザー「提案通りで良い」）＝提案をコーパスへ写した**（原本は `scratch/p328/lambada/backup/`・`lysc check` 診断 0・和音の空 cell は section 頭だけ `.`・他は `| |`）。**下は経緯**: → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **T3. ✅ 閉じた（第320 ⑾）＝error 本は 14 → 1、残る 1 は T2（`Lambada Complicada`・判断待ち）** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **T4. ✅ 閉じた（第318）＝移行は意味を変えていない。むしろ*既に壊れていた本を見えるようにした*。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **T5. ✅ 閉じた（第319）＝曲の先頭の反復開始記号は grob ごと立たない。インクも*幅も*消えた。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- **T6. ▶ 起票（第318）＝移行が可視化した、それより古い `.ly` → `.lys` 変換の負債（T3 の族）**

  ★ **T4 の三者比較が分離した「移行のせいではない」側**。**3 つ数えてある**:
  **⒜ LYS2006「first measure is shorter than the meter」を出す本 28 / 112**——
  **section の先頭が半端な小節。`Air on G String` の `~C_2` は 8 分 1 つ足りない**（上記 T4）。
  **⒝ 拍子変更が `.ly` より少ない本**（例 `Butterfly` −4・`Reelin' In the Years` −5・
  `ホーリー&ブライト` −5）——**`\time` の途中変更が `.lys` に写っていない。**
  **⒞ 記号が `.ly` より少ない本**（`I Will Always Love You` は `\mark` 21 に対しほぼ 0）。
  ⚠️⚠️ **⒞ の数え方はまだ信用できない**——**第318 の指標は旧 `.lys` の `section body` を
  「記号」に数えてしまう**ので、**この数字は起票の*手がかり*であって残差ではない**。
  **閉じるときは数え直すこと**（§0 の「数を引き継ぐときは数え方も書く」）。
  ⇒ ★★ **どれも「読み手の譜面が `.ly` より情報を失っている」型**で、**T3（error 11 冊）と同じ族**。
  **直すなら `.ly` を正解として `.lys` を作り直す方向**で、**移行器の延長ではない。**
  ★ **第320 ⑾ が T3 を閉じる途中で見た同族 3 つ（未修正）**: **⒟ `<<d'1~ a1~>>`（LP の同時 2 声）が
  `.lys` で `d1~ a,1~` と*逐次*に写っている**（`Addicted To Love` L28/L32＝「Measure duration 2 exceeds」の正体）／
  **⒠ `r1 r1 r1 r1 \break` の LP 自動小節線が `.lys` に `|` として写っていない**（`Green-Tinted` L13＝
  「duration 4 exceeds」。同じ本の 5/8 warning は `\time 5/8` 欠落＝上の ⒝）／**⒡ 全数の warning は
  208 冊 / 1457 件**（`scratch/p320/check-320.csv`。⚠️ 数え方＝`lysc check` の `): warning:` 行数）。
  ★★★ **第321 が全数で数え直した**（`scratch/p321/profile321.ps1` → `profile321.csv`・**`.ly` はコメントを剥いでから**）:
  **⒢ スラー `.ly` 1099 → `.lys` 1＝138 冊が全部失っている**（`Bohemian Rhapsody` の bar 6-7 / 16-17 の弧が Lily# に無い。
  数え方＝音名・音価・弦番号の直後の `(`。`#(every-nth…)` の Scheme 括弧を数えると 1831 になる）／
  **⒣ タイ 4776 → 4768**（3 冊）／**⒤ 音価の無い音で始まる section 5 冊 17 箇所**（LP は直前の音価を引き継ぐが
  Lily# は 4 分にする＝`Billie Jean` の B1 `b, fis, a, b, r …` が 8 分から 4 分に化けた・`scratch/p321/fx/fx3c-billie-excerpt.lys`）／
  **⒥ `\break` と `break` の数が違う本 21 冊**／**⒦ `repeat percent N { 1 小節 } |`＝`}` の直後の `|` が percent の
  小節を閉じず LYS2002「Measure duration 2 exceeds」を出す綴り**（`Get Back` 全編・`fx5-percent-break.lys`）。
  **直すなら ⒢ から**（実需が最大・`.ly` を正解に `(` `)` を戻すだけ）。
  ✅ ★★★ **⒢ は第328 が閉じた（コーパス側の直し・製品コード 0）**——**計器 `scratch/p328/t6g/RestoreSlurs`**（C#・`dotnet run -c Release`）:
  `.ly`／`.lys` を音符イベント列（音名＋解決済み音価。octave 記号は無視＝`.ly` は relative・`.lys` は absolute）にして
  **LCS で揃え、`.ly` の `(` `)` を揃った `.lys` 音符の正典スロット（`核 \N @… ] ) (`・§3）へ挿入**。
  **285 対・`.ly` 1097 本（数え方＝tokenizer の post-event。Scheme の `#'(` は数えない）→ 1078 本を 126 冊に復元・skip 19**
  ＝両端が揃わない 5／**section をまたぐ 6**（例 `Bohemian Rhapsody` の B→Intro2）／**grace に触れる 7**（Lily# は grace のスラーを
  刷らない＝`lysc check` が「not engraved」と警告する。**grace のスラーが入った日に当て直す本の一覧が `detail.txt`**）／既に在った 1。
  **検証 `verify.ps1`＝`lysc check` 原本 vs patched で error 0→0・warning 887→887・新規診断 0**／insert-only（`(` `)` を抜くと原本と
  バイト同一・挿入 2156 文字＝1078×2）。**`apply.ps1` で当てた（原本は `scratch/p328/t6g/backup/` に 126 冊・patch 後に編集された本は当てない）**。
  ⚠️ **profile321 の数え方では `.lys` 1 → 1055**（`@…(`・`](` の後の `(` を数えない規則なので 25 本少なく出る。`.ly` 1099 のうち 2 は `#'(`）。
  ⚠️ **1 度目の run は grace のスラーを書いて 4 冊 14 警告を出した**（`a slur mark inside 'grace { }' is not engraved` ＋ 相手の `)` が孤児）
  ——**「新規診断 0」の門で捕まえた**（`(` `)` を足す前に `lysc check` の差を数える）。**残る unmatched 5 は `.lys` 側がその小節を
  持たないか音が違う本**（`9 to 5 (Morning Train) (Xanadu)` 46-47・`I Will Always Love You` Outro 142・`Mandy` 84・`Need You Now` 96/97）＝手で見る。
  ✅ ★★ **⒣ タイと ⒤ 音価の無い section 頭も第328 の第 2 便が同じ計器で閉じた**（計器に `~` の転写と section 頭の音価解決を足した・**当てた変更 0・手直し 2 冊**）:
  **⒣ `.ly` 4775 本のタイのうち `.lys` に無い 8 本**＝**`A Thousand Miles` L22 の 1 本はタイの*位置*が違っていた**（`.lys` `e,8.~ e,16 e,4` 対 `.ly` `e,8. e16~ e4`＝機械で足すと `8.~16~4` になるので手で移した）／
  **`I Will Always Love You` Outro の 3 本は `.lys` にその小節が無い**（⒜⒝ の族）／`アゲハ蝶` L59 の 1 本は揃わない／`test` 4 本は scratch。
  **⒤ `.lys` の section 頭で音価を書かない event 21**＝**17 は LP も 4 分**（書かなくて同じ）／**1 冊 `She Bangs` E は LP が 8 分**——原因は section 頭の `r8` が
  変換で落ちていたこと（`.ly` L80 `r fis e fis…` 対 `.lys` `fis,, e,, fis,,…`＝**⒜ LYS2006 の族の 1 冊**）→ `r8` を手で戻して**警告 2 → 0**／3 は揃わない（`Can't Get You Out Of My Head` の rest 2＝LCS 77% の本・`test`）。
  ⚠️⚠️ ★★★ **計器の罠 2 つ（どちらも「新規診断 0」の門が捕まえた）**: **⒜ `.lys` の `@accent( c)`——arg 無し注釈の直後の正典スロットに置いた `(`——を私の tokenizer は引数リストと読んだ**（Lily# のパーサは正しくスラーと読む＝`scratch/p328/t6g/probe/annot-slur.lys` → 双子 `c4-\accent ( d )`）→ 中身に空白と非空白が両方あれば slur／**⒝ その判定を blank 済み本文で走らせると `@mark("B2")` の文字列が空白になって slur に化け、3 冊に `(` を二重に足した**（error 0 → 3）→ 空白だけの中身は引数。
  ⇒ ★ **今の計器は当て済みコーパスに対して冪等**（patched 0・already 1079・unmatched 5・cross 6・grace 7）。**残る T6**: ⒜⒝⒞⒟⒠（`.ly` を正解に 1 冊ずつ）・**⒥ break 21 冊は「`.lys` の break をユーザーが意図して動かしたか」を訊いてから**（機械で足せる形だが、消した break を戻すと逆行する）→ ✅ **ユーザー決定 2026-09-08＝触らない**（`.lys` の break は意図。テストへの支障は無し＝未追跡コーパスで、T7 の段割れ比較の対から外れるだけ）・⒦。
  ✅ ★★ **⒜ の「section 頭で落ちた音」は第 3 段で計器が数えた（LCS の隙間＝直前の揃った音と section 内の最初の揃った音のあいだに `.ly` だけが持つ音）**:
  **`s` spacer と最初の揃った音より前（`.ly` の別変数＝blank／chord／click track の `b8 b8 b8. b16`）を除くと、隙間は 285 対で 2 冊だけ**——
  **`A Thousand Miles` B2 の前に `e2 fis |` 1 小節が無い**（`.ly` L72・percent の直後。**octave は手で**＝relative の `\repeat percent` 後の基準が私には確定できない→ 第 4 便で `e,2 fis, |` を入れ、✅ 第 8 便でユーザーが「B1 の同じ小節と同じ高さ」と確認）／
  **`Sugar` C の `fis\3 ais\2 dis,, cis'`（`.ly` L80）は `r1 ×4` の音価を引き継いだ*全音符 4 小節*で、`.lys` L40 は 4 分 4 つの 1 小節**＝⒠ の族（LP の自動小節線）・意図はユーザーに訊く→ ✅ 第 8 便でユーザー「全音符が正しい」＝自分で `fis,1\3 | ais,\2@fall | dis,, | cis, |` に直した（診断 0）。
  **副産物＝alignment の改善で ⒤ が 2 箇所増えた**: **`Can't Get You Out Of My Head` の section 頭 `r`（L18・L27）は LP が 8 分**→ `r8` を当てて**警告 13 → 3**（「Measure duration 2 exceeds」10 件が消えた・新規 0）。
  ⚠️ ★ **計器の教訓 3 つ目**: **`.lys` の section 頭で running duration を 4 に戻す（Lily# の読み）と、音価を書かない小節がまるごと `x/4` に化けて揃わない**——揃えるのは LP の読みで、Lily# の読み（4）は ⒤ の判定でだけ使う。
  ✅ ★★★ **⒝ ⒠ ⒦ は第331 が同じ骨の計器 `scratch/p331/t6bk/RestoreMeter`（`meter`／`bars`）で閉じた（§1 第331・コーパス側・製品 0）**——**⒦ は誤診**（`} |` は無害・k1）で、**`Get Back` 186/96 の正体は ⒝**（`.ly` に `\time` 無し＝LP 既定 4/4・変換器が終盤の 2/4 をヘッダに持ち上げた。同型 9 冊）。**⒝＝ヘッダ 9・`time` 挿入 121・37 冊＋手 4 冊**／**⒠＝`|` 1074 本（break の前 277＋run の内側 797）・206 冊**（LP の自動小節線を `.lys` に書く。`r1 r1 r1 r1 break` → `r1 | r1 | r1 | r1 | break`）。**残り: ~~⒞ 記号~~（第332 第 3 便で apply 済み）・~~⒟ 同時 2 声~~（同便）・~~⒥ break 21 冊~~（2026-09-08 触らない）・ピックアップ形の `break` 10 箇所（§1 第331 ⑷＝下の census・2026-09-08 決定済み）・`Boogie Oogie Oogie`（ユーザー編集中で当てず＝`bars` を再走）**。⚠️ **計器の罠は §1 第331 ⑸**（section 内 `time` は section 限り／`} time` は validator だけが前の body に読む／`time` は percent の `{` の外に）。
  ✅ ★★★ **⒞ は第332 第 3 便で apply 済み**（ユーザー決定 1a・`apply.ps1 -Mode marks`＝171/171・原本 `scratch/p332/t6c/backup-marks/` 171 冊・census 461 → 179 札／94 冊＝ARCHIVE 第332 ⑻）。⚠️ **この行は第351 まで「未 apply＝ユーザー判断」のまま stale だった（2026-09-08 訂正）。以下は起票時の経緯**——第332 が数え直し、計器を建てて verify まで回した: **本当に無い札 461（158 冊）**＝匿名 section 化 266＋`Body` の先頭札 173＋他。**計器 `scratch/p332/t6c/RestoreMarks`（`marks`）→ `@mark` 438 札・171 冊・門（error 0→0・新規診断 0・小節数不動）通過**。手に残す 4 群（境界近傍 134・休符 120・`'` 落ち 42・section 名違い 55）は `.lys` の section の切り方の問題＝機械が決めない。⚠️ **罠**: form が再生しない section の札は「not printed by this score」の新規警告／`r1 r1` を両側が別 section に入れると LCS が滑る（A Thousand Miles）／percent body 内の `@mark` は 1 回だけ刷られる（probe 済）。
  ✅ ★★ **⒟ は第332 第 3 便が閉じた（ユーザー決定 2a）**: `.ly` 3 冊 5 箇所（`Addicted To Love` L88／L93・`Let's Stay Together` L79／L80・`アシンメトリー` L96）を `.lys` で和音に（`<a, d>1~ | <a, d>4 …`・`<d, fis>4. <d,\2 fis\1>8`・`<b, e>@fall`）。**3 冊とも小節数が LP と同数に**（99／84／85）・警告消滅・新規 0。**LP の `<< >>`（`\\` 無し）は 1 声の同時＝Lily# では和音**。
  ⚠️ **ピックアップの census（第332 ⑻⒟）**: `.ly` の `r2. b4 |` を変換器が section 境界で割った本が同型で 8 冊超（Holiday L13-16 が典型）。`partial` は section 単位の設定（part の音楽の途中は error）なので、直し方は「前 section へ戻す」か「`partial`＋休符を消す」の 2 つ＝ユーザーに訊く（§1 第332 次の一手 ⑵）。→ ✅ **ユーザー決定 2026-09-08＝⒜ 弱起を前 section へ戻す**（`~Body_1 { … r2. b,,4 | }`＝`.ly` と同じ絵・同じ小節数。form でその section を反復しても弱起は鳴らない、を承知の上）。⚠️ **途中の `partial`（§2F ⒱・同日決定）は census には使わない**——Body_1 末尾の短い小節を合法にするだけで、`.ly` の 1 小節が Lily# の 2 小節のまま（小節 +1・小節線 +1）。根は「Lily# の section は小節線で始まる」。`time` を挟む案は拍子記号まで刷るので退けた。→ ✅ **第352 第 6 便が当てた（35 か所・22 冊・§1 第352 ⑵⒡）。⚠️ 向きは `.ly` が決めた＝35 対は「休符を*次へ*進める」B 形で、「弱起を前へ戻す」A 形は 3 対だけ**。手に残る 3 対（Butterfly・Time After Time・You're So Vain）は §1。

- **T7. ▶ 起票（第321）＝絵の突き合わせ（286 冊全数）の結果＝族の表。計器は `scratch/p321/`（§1 ⑴）**

  ★★★ **構造（段組み）**: **比較可能 283 冊＝A 完全一致 170／B-eng 小節数一致・段割れ違い・break 数同じ 69（percent あり 36）／
  B-src 7／C 小節数違い 37**（`book-categories.csv`・両側の段署名は `categories.txt`）。**B-eng の形は「LP が 4 で組む段を
  Lily# が 2+2 に割る」**（`Friend Like Me` 2,4,4 ↔ 2,2,2）＝**根は §1 ⑺＝Lily# は行 DP の最適（Δforce² 込み）を段数に
  し、LP は段数を頁の得点（`page-breaking.cc:1548-1586 finalize_spacing_result`＝Σ force² ＋ 10×Σ 頁 force²・Δ 無し）で
  選び直す（`optimal-page-breaking.cc:139-190`）。再現 `scratch/p321/fx/bis-v6-proper-rests-first.lys`**。**C は 3 形**（§1 ⑵）。
  ✅ ★★★ **第322 が段数ループを移植した**（§1 第322 ⑴。`LayoutEngine.ChooseSystemCount`・番人 `SystemCountPageScoreTests`・
  fixture `test/system-count-page-score`）。**段署名の再掃き**（`scratch/p322/sig322.ps1` → `scratch/p322/structure321.csv`・
  LP 側は第321 の `lp.out` を再利用）: **一致 356 → 388 対（＋41／−9）・全対一致 170 → 183 冊・B-eng（小節数一致・段割れ違い）69 → 65 冊
  （数え方は parse321 の "books where bars match but systems differ"）**。**−9 対＝7 冊は下の F12。**
  ⚠️⚠️ ★★★ **第324 から T7 の指標は `KindMatched` の対だけ**（parse321 の "KIND-MATCHED (the T7 metric)" 行・LP book と同じ譜構成の
  Lily# score が在る対）: **第323 の木で 203 対中 SigMatch 126・小節数一致で段割れ違い 45・小節数違い 32**（第322 の木 194 対中 124／42／28）。
  **上の 388／382 は代用対（both の Lily# score を単譜の LP book と比べた 384 対）を含む数で、もう指標にしない**（第323 ⑶′⒜）。
  ⚠️ **KindMatched の数は `.lys` の編集で動く**（第322 → 第323 で 9 対が代用から一致へ移ったのは、ユーザーが 09-02 夜に 5 冊の `.lys` を編集したから＝gate の得失ではない）。

  ★★★ **絵の族**（fixture は `scratch/p321/fx/`・LP の絵は `out/<本>/lp*.png`）:
  | 族 | 症状 | 裏取り | 射程（`profile321.csv`） | 状態 |
  |---|---|---|---|---|
  | **F3** | tab の percent 空小節に全休符 | `fx1`・`fx3h`・`Billie Jean` LP 8-10 空 | percent 106 冊（body 3 小節以上のみ） | ✅ 第321 で閉じた（`TabPercentBlankBarsTests`・`test/tab-percent-blank-bars`） |
  | **F2** | section 名の箱が行頭寄り・key があると tempo が横滑り（LP は縦積み） | `fx4`（key 有/無）・LP `Billie Jean` | `\mark` 281 冊・`\tempo` 277 冊 | ✅ **第324 が閉じた（§1 第324 ⑵）**: `CalculateXPosition` の Rehearsal 腕を SectionLabel にも通した（行頭＝`|:` が描かれていればその線・無ければ key/clef 右端）。陽性対照 `fx4-mark-tempo`（`scratch/p324/fx4-new.png`＝LP と同じ絵）。台帳は SectionLabel 対照点を RehearsalMark（MKQ 2.565）に付け替えて exact・縦 5 点は `\mark` 綴りの書（RWM/RWMN/RWMA）に付け替え（+0.75 ×2・−0.108 ×2・+0.206＝箱の描画差・OPEN）。snapshot 225 枚再ベース。横並びは `marks beside`（§3・低優先・未着手） |
  | **F11** | tab の tempo が段上の梁を貫通 | `fx3k`・`fx11-*`・**ユーザー起票（第321）** | full tab 全冊（符尾・梁が skyline に入る） | ✅ 第321 ⑼ で閉じた（`TabTempoOverBeamTests`・`test/tab-tempo-over-beam`・A/B は §1 ⑼） |
  | **F1** | 弦選択が LP と違う | `fx3e`（`a,` を 1 弦 2 → 2 弦 7） | 全固定 92／一部 86／無指定 22 | §3 既決（固有機能・比較は固定本で） |
  | F9 | フレット数字が大きい | 台帳 `slur.tab.*` | tab 281 冊 | §3 既決 |
  | **F12** | 段数ループが**LP より安く**段を併せる／割る本（`Alone Again` bar 1-8 を 1 段に併せる・`Livin' It Up` 末尾の 4,4 を 2,2,2,2 に割る・`First Love` tab／`にんじゃりばんばん`／`未来予想図Ⅱ` の 8,8 → 16） | **得点を並べた**（第322 ⑸）: `未来予想図Ⅱ` は LP 27 段 53.70 / 26 段 54.06（差 0.36）に対し Lily# 56.94 / 56.44（逆向き 0.50）＝knife-edge。`Alone Again`（再現 `scratch/p322/fx/alone-intro8.lys`・LP 4\|4\|4・Lily# 8\|4）は **⚠️ 「LP は 8 小節を置けない」が誤りだった**（第322 末尾で測り直し）: LP は `\paper { system-count = 2 }` でも `\noBreak` でも**その 8 小節を warning 無しで 1 段に置き、しかも natural 幅ぴったり（第 8 小節線 102.24 対 line-width 102.05＝力 ≈ 0）**。LP の自由 run が 2 段を inf と刷るのは LP 内部の癖（未解明・`alone-intro8-{sc2,nb45,def}-lp.*`）で、**LP が 4\|4\|4 を選ぶ本当の理由は得点＝3 段 1.27 < 2 段 1.65**（8\|4 は末尾の 4 小節段が伸びる）。⚠️⚠️ **2026-09-03 未明に測り直し（`w-*.ly`／`w-h8-min*.ly`）**: **`\noBreak` で作った natural/min 計測は LP の loose column（非 breakable な小節線）に食われて狭く出ていた＝「LP natural 102.2・1.35 倍」は撤回**。**同じ小節 `fis,,2 fis,,8 fis,, r cis,` を単独で測ると natural LP 15.84 / Lily# ideal 15.95＝一致、min LP 9.04（`system-count = 1` で 60〜120mm に押し込む・"制約を満たす改行を見つけることができません" が出た位置が min）/ Lily# 8.35（bar 1）・7.17（bar 2）＝Lily# が 8〜21% 小さい**。**得点は `DebugPageBreakingScoring` で並べた: Lily# 3 段（4\|4\|4）1.279 対 LP 1.274＝3 桁一致（採点は正しい）／Lily# 2 段（8\|4）1.208＝0.07 差で 2 段を採る・LP は 2 段を inf と刷る（理由未解明: `system-count = 2` を強制すると同じ 8\|4 を warning 無しで置く）**。⇒ **根は spring の min（圧縮の余地）**: 8 小節を 0.7 に圧縮した段を Lily# は f² 0.82 と値付け、LP の min なら余地が小さく約 1.7 になる見積り。`Livin' It Up`（Lily# 22 段 Σf² 6.97 対 LP 1.89）も同じ向き | 7 冊 9 対（−9 の全部） | ✅ **第323 が閉じた（2 commit・§1 第323）**。**根は 2 つ在った**: ⑴ **bar ごとの min**（`ccab465f`）＝note→bar が head-only で **up-stem の flag を見ない**（LP rod 2.3674 対 1.6042＝−0.86）／bar→note の rod 0.1 無し／描かれる休符が notehead 箱（+0.30）／不揃いの頭を中心で測る（−0.04）／flag 付き左音符の stem correction gate（`note-spacing.cc:264-266`）未移植（ideal +0.10）。**全部 LP の圧縮行の `minimum-distances` で測って閉じ、bar は min 9.0432・natural 15.8432＝LP と 4 桁一致**（`BarlineColumnRodTests` 8 本）。**⚠️ それでも段署名は 388 → 386 対＝min では動かない**。⑵ **gate の力**＝`CalculateLineForce` は per-measure の和で `compress_line` の線形部分（最初の spring が block した先を見ない）。**LP は候補行ごとに `Simple_spacer` で解く**（`constrained-breaking.cc:127-152 space_line`）。⇒ `MeasureSpringData.Springs`（値比較）を持たせ、fits する候補行を `SpringSolver` で解く（伸びる行で block > 0 が無ければ線形＝従来と同じ・MMR run rod／歌詞 rod の行は線形のまま）。**`alone-intro8`: 2 段 1.222 → 1.583（LP 1.648）・3 段 1.276（LP 1.274）→ 4\|4\|4**（`SystemCountPageScoreTests.AloneAgainIntro_*`）。⚠️ **「LP が 2 段を inf」は `-def.ly` 双子の `\noBreak` の副作用**（plain 双子は 1.648）＝第322 の「未解明」は計器の取り違え |
  | 撤回 F6 | 上弦の梁が UP | 10 回描いてハッシュ 1 種・`fx3f` ＋ `fx3-hand-tab.ly` で LP と一致 | — | **私の目視の誤り**（RULES §5.0 の 1x ラスタの罠の兄弟＝所見はハッシュか座標で裏取り） |
  | 撤回 F4 | volta が次段に続かない | `fx2`・`fx2b`（LP も続く） | — | `Billie Jean` の LP だけ 43-46 で切れる理由は未解明・追わない |
  | 源 | スラー消失 138 冊・音価欠落 5 冊・break 21 冊・`} \|` 綴り | T6 ⒢〜⒦ | — | T6 |

  ★★★★ **B-eng の製品側の正体＝第332 ⑼（percent 反復の空小節の幅）**: LP 側の基線は `scratch/p332/structure332.csv`（KindMatched 209 / SigMatch 140 / 段違い 50 / 小節数違い 19）、break の位置まで一致して段割れが違う 27 冊は `scratch/p332/beng332.csv`、最小再現は `scratch/p332/t7/pc2.lys`（LP 8 小節 1 段・Lily# 2＋3＋3・空小節 7.5〜7.7 対 32.5 ss）。
  ✅ ★★★★ **半分は第332 ⑽ が閉じた（構造）＝覆われた反復は spacer で書く。SigMatch 140 → 143・失った対 0**。✅ ★★★★ **残る半分＝空小節のばねは第333 が移植した（`SpacingRules.EmptyBar.cs`・§1 第333）**: LP は grob の無い列を spacing から落とし（`paper-column.cc is_used`・`system.cc used_columns_in_range`）、小節線の対を `standard_breakable_column_spacing` の**2 分岐**で値付ける——両側が breakable なら `min_dist + 1.2 × (mlen / gs) × 0.8`（stretch＝space）、片側でも forbidBreak なら `min_dist + duration_space(dt)`（既定強度）。**`s1`／`| |`／`%` 被覆小節は前者（5.51／8.07／15.75 exact）、`%%` の対の内側と 3 小節以上の slash 反復の内側は `Forbid_line_break_engraver`（鳴っている rhythmic grob が forbidBreak）で後者（6.39 exact）**。Lily# は反復の内側の小節線に `LineBreakPermission.Forbid` を書く（collector）。**gs の投票から staff の spacer を外した**（LP の skip は starter を持たない）＝percent 本の gs が LP に戻る（beam-over-stem・tab-percent-blank-bars とも LP の gs 1/8 を ALLCOL で確認）。
  ✅ ★★★ **⒜ `DoublePercentRepeat` の ink を小節線の列の skyline に入れた（第333 第 2 便）**: LP は staff-bar に break-align（`define-grobs.scm:1290-1309`）し stencil を CENTER に揃える（`percent-repeat-interface.cc:94-103`）ので、印は小節線の左端を中心に ±w/2 で列の両側に出る（`dp-settings.ly` 実測: DP の X＝BarLine の X・幅 3.757645（staff）／5.636468（tab ss 1.5）＝`PercentRepeatEngraver.Geometry` の GroupWidth と 6 桁一致）。`BoundaryColumn.DoublePercentBox`＋`MmrRodMinimumDistance(left/rightDoublePercentHalfWidth)`＋`ScoreSideTables.DoublePercentHalfWidths` memo。renderer の inline 幾何を `PercentRepeatEngraver.Geometry` に畳んで 1 軒に（描く箱＝予約する箱）・印の中心を小節線の*左端*に（前は右端＝0.19 ずれ）。**pc4r 7.57／7.38 exact・fx-bows 8.51／8.31（LP 8.32）**。
  ✅ ★★★ **⒝ 音符の後ろの skip 列＝第333 第 3 便で閉じた**（`CollectAllTimingsForMeasure`: notation staff の spacer だけの onset を落とす＝`is_used`・text row の slot／timing 付き和音記号／beat slash が anchor・「鳴っている音が跨ぐ」条件は不要になった／`SkipOpenedBarFirstSpring`: skip で開く小節の spring 0＝`standard_breakable_column_spacing` の dt 分岐／`fills_measure` は skip が続くと偽（LP の `next` は unused 列）／item 系は kept 列＋`CreateSpring(shortestPlaying:)` の fraction）。**probe `scratch/p333/ps`: ps1 `c4 s2.` 12.75・`c8 s8 c8 s8 c4 s4` 15.16・ps2 11.81・ps3 `s4 c4 d e` 12.15／`s2 c4 d` 10.35＝全部 LP と桁一致・`beam-over-stem` 15.8/11.6 → 21.26/20.66（LP 20.93/20.63）**。**射程は数えた（`scratch/p333/ZzSkipColumnCensusTests.cs.txt`）: fixture 2 冊（beam-over-stem・beam-skip）・実コーパス 1 冊 3 小節**。
  ✅ ★★★★ **⒞ 段数ループの頁見積り＝候補行の begin bucket を「その行の行頭」で値付ける（第335・§1）**: `EstimateMeasureHeights` は全候補行に*最大の* begin bucket（＝1 段目の tempo＋"Intro" 箱 7.30）を配っていたので、Le Freak（staff＋tab）は 1 頁 6 段と見積もり（実配置は 8 段）、25 段→5 頁・24 段→4 頁で「頁が減って伸びた」出口（`optimal-page-breaking.cc:183-185`）が 24 で鳴り、線の和が最安の 23 段（LP 12.705 対 Lily# 12.684）を試さなかった。LP の `begin_line_heights` は break rank ごと（`axis-group-interface.cc:417-458`）。直し＝その小節で段を開いた実配置の begin bucket、無ければ継続行の素の prefix（最小）。**Le Freak 3 対が戻り、失った対 0**。番人 `SystemCountPageScoreTests.LineStartInk_IsPricedPerCandidateLine`＋fixture `test/system-count-line-start-ink`（Le Freak 1-57 小節・2 頁の snapshot）。
  ⚠️ ★★ **Addicted To Love（4,8 → 12）は製品の欠陥ではない**: Lily# ≡ LP 双子（12 小節 1 段・幅 16.73/16.58/16.59/4.76/3.71… 対 LP 16.93/16.79/16.79/4.1/3.69）。手書き `.ly` だけが 4|8 で、**`TabNoteHead.font-size = 2` を 0 にした変種も 4|8**（`scratch/p335/handsig.ps1`・`atl-font0.ly`）＝フォントは犯人ではない。残る差は手書きの `\omit StringNumber`・tab の numbers・indent＝双子で再現できない枠（`lysc ly` は "both" score を出せない＝form 単位）。追わない。
  ✅ ★★ **双子の忠実度 2 点（第335 第 3 便・§1 ⑻）**: Staff の `\omit StringNumber`（Lily# は五線に丸数字を描かない）と、対の tab の既定 numbers（exporter は `as` の語だけ読み、対の tab を全部 `\tabFullNotation` で出していた）。⚠️ **どちらも Le Freak fixture の双子の 6|10 を動かさない**——残る差は Lily# の bar 10-11 自身の幅（+1.3 ss）＝spacing 残差の族。
  ▶ **残る小さい差**: `beam-over-stem` bar 1 の +0.33（2 声の 16 分と 8 分の対＝未診断）／text row の空 cell の slot（LP は落とす・Lily# は保つ＝`LILYSHARP-OWN` で名指し）。
  ▶ ★★★ **`Express Yourself`（LP 11 段 `8,4,4,4,12,4,4,4,12,4,4`・Lily# 14 段 `4,4,2,2,4,4,12,4,4,2,2,12,4,4`）は*圧縮行の採点ではない*（第335 第 4 便が測った・`scratch/p335/probe7.txt`・`exy-dpbs.txt`）**: 同じ枠（staff＋tab）の LP 双子も 11 段。**線の側は合っている**（Lily# の 11 段 Σf² 15.52 対 LP の 11 段の得点 14.92・8 小節行 −0.22／4 小節行 −0.20・2,2 に割ると +1.14）。**両者とも線 DP の理想は 17 段で、段数ループが下る途中で違う**: Lily# は 17（3 頁 7,7,3）→16→15→**14（2 頁 7,7・力 +0.011）で「頁が減って伸びた」出口が鳴る**／LP は 14・13・12・11 と下り続けて 11（14.92）。**LP の最終 11 段は 1 頁目に題＋8 段**（`exy-pages-when.txt`・段の pitch 17.3〜20.3 は Lily# と同程度）＝**LP の 14 段は 3 頁のまま**か**頁の力が負**（1 頁 8〜9 段に詰める）のどちらか＝**頁 DP の側**（1 次元 DP の力・1 頁目の題・LP の page-spacing）。**計器はそのまま使える**: `scratch/p335/ZzP335cProbeTests.cs.txt`＋`pages-when.ly`＋`-ddebug-page-breaking-scoring`。
  ★★★ **第 5 便が `system-count = 14` で測った（`sc14-when.ly`・`exy-sc14-when.txt`）**: **LP の 14 段は Lily# の 14 段と*同じ行割り*（`4,4,2,2,4,4,12,4,4,2,2,12,4,4`）で、頁は題＋8 段／6 段の 2 頁**（Lily# は 7,7）。**2 頁 ＜ 理想の 3 頁は両者同じなので、出口を分けたのは頁の力の*符号***: LP の 1 頁目は題 2.03 → 8 段目 139.0＋下端 ≈ 157＝最小の積みで頁いっぱい（力 ≤ 0 → 出口は鳴らず 11 まで下る）／Lily# の 8 段の積みは header 3.97＋tallness 148.4＝152.4 で 5 ss 余る（力 ＞ 0 → 7,7 を採り +0.011 で出口が鳴る）。**差 5 ss の出所は題の帯（LP は 1 段目の yoff 8.43 まで 6.4・Lily# の header 3.97）と段の tallness（LP の pitch 17.3〜20.3・Lily# 17.5〜20.0）のどちらか＝縦の台帳（`page.*`）の仕事**。**線の側でも段数ループでもない**——同じ行割りを同じ規則で採点し、頁の力が 0 をまたぐ側が違う。次は `audit/lp-geometry` の型で「題付き 1 頁目の最初の段の yoff」と「staff＋tab の段の tallness」を対で起票する（第 1 便の ⑶ で `placed sys` の数字は揃っている）。**旧 ⒝ の記述**: 音符の後ろの skip 列（`c4 s2.`）はまだ列を保つ——LP は unused 列を落として音符→小節線を `fraction × shortest_playing` の 1 本にする（`beam-over-stem` の LP 20.93 対 15.8＝この島。gs は 1/8 で両者一致）。⚠️ **`spacing-determine-loose-columns` の prune ではなく `is_used` の filter**（第332 ⑾⒞ の「LP は skip の列を prune」は住所が違った）。
  ⚠️⚠️ ★★★★ **第337: LP 側の計器 `probe-settings.ly` の all-visible BarNumber は*描かれると*LP の頁の得点を動かし段数を変えていた**（Freedom staff book 37 ↔ 39・`scratch/p337/ab/`）。**第321〜336 の LP 基線は「番号を描いた LP」**。`stencil = ##f` で全冊取り直し（`scratch/p337/structure337.csv`）: LP 側 8 対 7 冊が動き、**T7 は 209 / 149 / 41 / 19 で不動**。**頁も数え始めた**（`lysc layout` の頁行 → parse321 の `LysPages`/`LysPageSig`・PAGES 行）: **KindMatched 209 対で PagesMatch 194・段署名一致で頁数違い 5**（Sugar・The Heat Is On・Boogie Oogie Oogie・Can't Take My Eyes Off You ×2＝§1 第337 ⑸）。
  ✅ ★★★★ **第337 第 3 便が Sugar の頁差を閉じた＝full-notation tab 符尾の幾何を移植（`f4ba2b40`・§1 第337 ⑻）**: LP の 3 点（先端＝通常 stem end を tab の枠で／始点＝数字 far edge／向き＝弦で読む default-direction）を移植し、**Sugar の tab 頁 11,12,1 → 12,12＝LP**。台帳 `stem.tab-full.top-string.eighth.tip-above-middle` = −1.0（初の tab 符尾観測点・residual 0）<!-- ledger: stem.tab-full.top-string.eighth.tip-above-middle = 0 -->。snapshot 18 枚（full-notation tab 全部）。**残る純頁差 4 冊**（Boogie・The Heat Is On・Can't Take ×2＝Lily# が多く詰める側）は tab 符尾ではない＝次便が `pages-ext.ly` で読む。掃きは tab 符尾修正後に取り直していない（PagesMatch 194 は修正前の数）。
  ✅ ★★★★ **第336 が対を起票した（§1 第336・probes `titled-page.ly`／`staff-tab-page.ly`・15 点）＝5 ss は 2 つの島**: **⒜ 題の帯**（`page.titled.first-staff-refpoint` −5.632695<!-- ledger: page.titled.first-staff-refpoint = -0.001565 -->）＝LP は書名を top-aligned な paper system として鎖に入れ top-markup-spacing 4 → markup-system-spacing（床＝題の深さ 6.107＋段の上インク＋0.5）、Lily# は baseline を MarginTop に置いて baseline 以下 3.974 だけを床に足す／**⒝ ブレーカーの staff＋tab の tallness**（`page.staff-tab.compressed.staves-on-first-page` −2<!-- ledger: page.staff-tab.compressed.staves-on-first-page = 0 -->・LP 8,7 対 Lily# 7,7,1）＝Lily# 18.000 対 LP 16＝`BuildSystemDetails` の nominal 半譜（tab の refpoint）＋見積りの譜間が自然 9（LP は pure minimum translations の 8）。✅ **⒝ は第336 第 2 便が移植した（`BreakerRefpointFrame`・§1 第336 ⑻）**: 段数 5 点 exact・snapshot 1 枚（Le Freak fixture 7,4 → 8,3＝LP と同じ）・残りは foot rod +0.49 の 1 項。✅ **⒜ は第 3 便が移植した（`HeaderBand`・§1 第336 ⑼）**: 3 点とも 0.002 以内・snapshot 187 枚・**Express Yourself は 8＋3 段＝LP と同じ**。自然本の 4 点は exact。⚠️ 縦の対は*行割りを揃えてから*（最初の版は LP 8 小節/段・Lily# 7 で別の段を頁に載せていた）。
  ✅ ★★★ **C の残り 11 冊は第332 ⑺ が全部判定した（`scratch/p332/t6c/RestoreMarks -- gaps`・`detail-gaps.txt`）**: **form が section を落とす 4 冊**（Rose Garden J＋K／She Bangs D2／All Star Outro／A Thousand Miles B3）・**`.ly` の書き忘れ 1 冊**（Endless Story L76 `\time 2/4` が戻らない＝LP 95 は artifact・直すのは `.ly`）・**ピックアップ 4 冊**（Butterfly `Intro2` 0.3 小節／Hello, my friend／9 to 5 (Xanadu)／Automatic＝`partial`）・**既知 2 冊**（Arthur's Theme `| break |`／You're So Vain L25 `e1`）。**直しは全部ユーザー側**。⚠️ **計器の読み方**: 音符が全部揃って（matched＝両側の event 数）小節数が違えば、percent の回数か form の参照回数か拍子＝`plays` の 2 行を比べる。`.ly` の見積りは *変数 × score context の参照回数*（`\new` ごとに context を数え、音符を持つ変数の参照が最も多い context を採る）。
- **T8. ✅ 閉じた（第328・ユーザー報告＝Lambada の提案を見て「E2 の箱が volta ブラケットの下に居る」）＝和音行の上では volta が 1 帯高く立ち、2 番の箱がその hook の下のポケットに落ちていた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

### A. 予約と描画・複数モデルの統一（▶ と同じ族）

LP には break-align モデルが **1 本**しか無い。Lily# に**同じ量を計算する場所が 2 つ以上ある**なら
それが次の欠陥の住所（§5.2.1②）。現在わかっている残り:

- ✅ ★★★ **閉じた（第355 第 3 便・ユーザー決定 2026-09-09「広げて良い。patch をあてて」・commit `c0c29692`）＝staffless の section 名の箱（beside なら tempo まで）は行頭の spring の床になり、bar が広がる。snapshot 4 枚再ベース。以下は経緯**: **staffless（`chords` 行だけ）の section 名の箱は*置くだけ*で、行の spacing は 1 も予約しなかった**（2026-09-09・第355 第 2 便が `marks beside` の拾い箱を測っていて見つけた・計器 `scratch/p356/mk6`〜`mk9`）。
  **第251 の「箱は行の線に載る」決定（`StafflessAnchorRefpointBelowTop`）と一緒に入った予約 `BoxedLabelXWindows` → `ChordNameEngraver.Calculate(labelWindows:)` は、*記号を箱の右まで動かす*だけ**で、`SpacingRules.ApplyChordRowSpacing` は記号の幅しか値付けしない＝**小節幅は箱を知らない**。
  **実測**（4 小節 `C | G | Am | F`・bar 1 の幅は無札／`Intro`／`IntroductionLong` の 3 冊とも **13.90**・他は 10.49）: **`C` の X は 4.50 → 8.61 → 25.92**＝長い名前では *bar 3* に押し出される。**`marks beside` は窓に tempo を足すので短い札でも同じ穴が見える**（`Intro`＋`♩=117`: `C` 16.82 ＞ 小節線 13.90・tempo の ink が小節線を跨ぐ＝§1 第355 ⑹⒟）。
  ⇒ **直し方は「窓を spring にする」**: 行頭の小節の最初の列に「箱（beside なら tempo まで）の右端＋gap」を line-start の wish として足す＝`LineStartColumn.LineStartSpring` の `ownFixedFloor`（歌詞の先頭音節の reach）と同じ形・LILYSHARP-OWN（LP は札を行の線に置かない＝第251 の乖離の続き）。**出力が動く**（staffless で札を持つ全冊＝`test/lead-sheet-*` 族の snapshot・ユーザーの chords-only 本）ので**承認が要る**。窓を読む側（`ChordNameEngraver` の shift）は残してよい（spring が効けば shift は 0 になる＝差分の網になる）。
  ✅ ★★★ **第355 第 3 便が spring 化を*試作して測った*（未 commit・承認待ち・patch は `scratch/p356/label-linestart-spring.patch`＝3 ファイル 76 行・`git apply --check` 済み）**: `MusicMarkEngraver.StafflessLabelLineStartReach(score, measureIndex)`（staffless＋先頭行が chord row のとき、その小節の箱の右端＝`0.3 + 2·halfW`・beside なら tempo の ink 右まで・＋`ChordNameEngraver.SymbolGap` 0.6＝window の shift と同じ数）を `MultiStaffLayouter.LineStartSpringForLine` の `ownFixedFloor` に max で足す（frame＝prefix 右＋開き小節線を引く）。**gate と layout は同じ 1 実装を通るので段割れも整合**。**結果**: `mk6`（beside）bar 1 13.90 → 26.22 で `C` 16.82 は小節の中・tempo は小節線を跨がない／`mk6s`（stacked）18.01・`C` 8.61（今までと同じ X＝shift 0 になり列の上に立つ）／`mk9` 35.32・`C` 25.92 は bar 1 の中／`mk8`（札無し）13.90 不動。**掃き 922 冊: MOVED 8＝追跡 8・ユーザー 0**（`test/chords-attached`（2 つ目の score が staffless grid）・`test/lead-sheet-chords`・`test/lead-sheet-repeat`・`test/lead-sheet`・`test/rows-song-sheet`・`samples/amazing-grace`・`drunken-sailor`・`greensleeves`）。**snapshot が動くのは 4 枚**（`test/lead-sheet-chords`・`test/lead-sheet`・`test/lead-sheet-repeat`・`test/rows-song-sheet`）・**台帳 810 不動**（`LpGeometryLedgerTests` 緑）・対象 1303 本のうち赤はその 4 枚だけ。目視（`scratch/p356/ls-base.png`／`ls-fix.png`）: `lead-sheet` は "C" が最初の列（"Twin-kle" の上）に立ち、bar 1 が箱ぶん広がる＝意図どおり。~~**未計測**: staffless で `|:` が行頭に描かれる本~~ → ✅ 第 4 便で測って閉じた（reach の左端は `BarX`・beside の箱も `|:` の後ろ・§1 第 4 便）。**承認後に当てた（第 3 便の続き）**: patch → 4 枚再ベース（要素数は 4 枚とも出入り同数＝位置だけ）→ 番人 `StafflessLabelSpacingTests` 4 本（bar の中・窓の右・**札を隠した本と「記号の後ろの余白」が一致＝spring と shift の差分の網**・札無しの bar は不動）→ commit `c0c29692`。
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
- ✅✅ ★★★ **閉じた（2026-08-05・第97セッション）。臨時記号の列は譜のモーメントに 1 本になった。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **閉じた（2026-08-05・第97セッション）。`check_meshing_chords` は字面順になった。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **閉じた（2026-08-05・第95セッション）。符頭の X 枠は `ink` 1 本になった。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **閉じた（2026-08-05・第92セッション）。`inside_staff_skylines` は 1 本になった。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
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
- ✅✅ ★★★ **閉じた（2026-08-07・第104セッション）。付点の向きと side support は LP の 3 層になった。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **閉じた（2026-08-05・第98セッション・`58415901`）。cue region は per-voice walk でも → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **閉じた（2026-08-03・起票と同じ日）。符尾の attachment X は符頭ごとになった。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
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
- ~~**prefix 幅の第3のモデル＝`MultiStaffScore.LeadingKey`**~~ — **閉じた**（`bea0add6`）。
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
  **構造の乖離は残るが観測不能**で、点が guard になっている。<!-- ledger: system.knee-beam-notes = 0 -->
- **`BuildSystemSkylines` の全譜 union** — ⚠️ **測った。内側譜は届かない**（probe `IS3`/`IS3C`・
  §1）。「内側譜の ink が edge 譜の silhouette を突き抜ける」は**音高では起こらない**（詰め offset
  9 ss ＝ 約 2.5 オクターブ）。
- ~~**offset が minimum_translations か最終位置か**~~ — **閉じた**（`a5437c6d`＋`a0708438`）。
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
- ~~★★★ **tab の梁が量子器を通っていない**~~ — **閉じた**（第67セッション・`37c75fe2`・§1 ⑦⑧）。
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
  <!-- ledger: system.stretched-distance = -4.63e-07 -->
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
  `page.clef.first-staff-refpoint`（−8.3e-5＝**足の 0.010 が force 経由で薄まった姿**。
  ⚠️ **その −8.3e-5 は当時の値**——上の多角形 seed 以降 **−1.24e-07＝許容差以下**）。
  <!-- ledger: page.clef.first-staff-refpoint = -1.24e-07 -->
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

- ✅ ~~**crop が loose-line の伸びのぶん stale**~~ — **閉じた**（第292・`f9ab3cbc`）。**予約の消費者を 2 つに割った**: `LyricReservationBelowSystem` が 2 つの profile を返し（`LooseBlockProfiles`・最小と force 0 の rest length）、**読むのは `CreatePages` の `totalHeight` 1 行だけ**。**下スカイライン＝譜間の床は最小のまま**（LP もそう予約する・`page-layout-problem.cc:593-599`）。⚠️ **crop 自体は今も LILYSHARP-OWN, DECLARED**（`page.height` の −109.468268・APPROX 棚 221）——**閉じたのは stale のほうだけ**。⚠️ ★★ **そのとき名前が付いた地図**: **譜間の対を floor するのはスカラーの下 extent ではなく `AddLyricBand` 経由の *profile***（スカラーは silhouette を持たない system 用の fallback）——**第292 の 3 本目の毒が不発で分かった。§2 D の残りを読むときに要る。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- ~~**譜間ばねがページの鎖に無い**~~ — **移植済**（`a0708438`）。**圧縮側も台帳点あり**
  （`dc3e321f`。`page.compressed.staff-staff-inside` ほか）。~~残る名前付き乖離は
  **ossia ペアが rigid**~~ — **閉じた**（`489ac6d7`）。~~**loose line 再配分の不在**~~ — **移植済**
  （`6af5f6be`＋`3e7bd94b`）。⚠️ **譜数によらず「最後の spaceable 譜の下」の鎖は解く**
  ようになった（`3e7bd94b`）。⚠️ **グループ間歌詞も、chords 行を持つ system も
  2026-07-27 に解けるようになった**（§1・`9660e5d8`）。**ossia も 2026-07-28 に入った**
  （`489ac6d7`）。force 0 のまま残るのは
  **lyrics 行／譜間に立つ row**＝§1 の 0 番。歌詞行 1 本では **LP も動かさない**
  （`16efdf1b` で実測）ので、効くのは **同じ譜間に loose line が 2 本以上**あるときだけ、
  という当時の読みは正しかった
- ~~**圧縮 regime は未実装**~~ — ⚠️ **この記述は stale だった**（2026-07-26 に実測で確認）。
  ページは両方向に solve しており、`page.compressed.staff-staff-inside` /
  `system.compressed-distance.two-staff`（book JSK）は **exact**。
  <!-- ledger: page.compressed.staff-staff-inside = 0 -->
  <!-- ledger: system.compressed-distance.two-staff = 0 -->
  ⚠️ **圧縮強度は伸長強度と別**
  （`ideal − minimum`。staff 2 / system 4 に対し伸長は 5 / 60）なので、**片方だけ緑の移植は
  もう片方で落ちる**——`dc3e321f` が実際にそれで移植の欠陥を捕まえている
- ~~**LP の top spring はページ justify で伸びる**が Lily# は先頭 system を固定~~ —
  ⚠️ **この記述は stale だった**（2026-07-26 に実コードで確認）。`PageLayouter.cs:290-294` が
  spring 0 として top spring を鎖に積んでおり、`page.stretched.first-staff-refpoint` は
  残差 **−0.000042**（＝符頭インク族。§1 の非ゼロ表）。**乖離ではない**
  ⚠️ **その −0.000042 も当時の値**——**今は −4.46e-07＝許容差（1e-06）以下**。
  <!-- ledger: page.stretched.first-staff-refpoint = -4.46e-07 -->
- **`PageLayouter` は systemDetails の `i == 0` で `vs.SystemSystem`、配置側は `vs.TopSystem`**＝
  ブレーカーと配置で spec が食い違う（本数見積りにしか効かない）
- **`LayoutEngine` の単一ページ経路が今も自前で積む**（二重実装）。⚠️ **「force 0 なので鎖と一致する」は嘘だった**——帯の床を `SysHeight`（trailing 行の描画帯を含む）から測っていて、**行を挟む本で帯を二重計上**（第218 実測: rowgap probe 19.836 vs LP 12.000・Twinkle 23.500 vs 12.225）。**frame は `969061de` で直した**（帯の項はアンカー譜の外側線から＝PageLayouter の `HalfLast` と同型）。**二重実装そのものは残っている。**
- ✅ **歌詞帯のスカラー床は X 盲目 — 閉じた**（第221・`785ade3c`。起票 `90833c84`＋対の修理 `053e2674`）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- **Y コーパスの拡張**（`page.top-margin` / `page.bottom-margin` / `page.last-page-gap` 等）
- ✅ ★ ~~**歌詞行が譜間の「中で」LP と別の位置に立つ**~~（2026-08-14 起票・**未着手のまま 60 便**） → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

#### ★ 譜間ばね移植（`a0708438`+`dc3e321f`）で**字面から外れた 1 件と未移植 3 件**

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
- ✅✅ ★★★ **閉じた（2026-08-03）。タイの列アウトラインは移植済み＝`TieChordOutline`。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ★ **`Interval` 型は今も無い**（2026-08-03 の自己監査で名前が付いた ⒝ 債務・**残っている側**）。
  LP の `Interval`（`lily/interval.hh`）は `distance` / `widen` / `linear_combination` /
  `intersect` を持つ**一級の値**で、**タイのコードだけで 4 つ全部**を使う——
  水平距離罰（今は手で展開）・`GetAttachment` の 2 つの `widen`・`close_by` の `intersect`。
  ⚠️ ★★ **ただし起票時の論拠「器が無いから移植できない」は反証された**——**上の列アウトラインは
  `Interval` 無しで移植され、点は 9 桁で閉じた**。⇒ **残っているのは*読みやすさ*の債務であって
  忠実度の債務ではない**（着手根拠を書き換えること・§5.0「着手根拠は点」）。
- ~~★★ **`Bezier` 型が無い**~~ — ⚠️ **stale だった（2026-08-23 に裏取り）**。`Bezier`（`Bezier.cs`・
  `lily/bezier.cc` 引用付き・`CurveX`/`CurveY`＝`curve_point`）は**実在し 8 ファイルが読んでいる**
  ——**slur scorer 込み**で、**論拠に挙がっていた `SlurScoringProblem.InterpolateSlurY` は消滅済み**。
  残るのは `BezierBow.MidpointHeight` の閉じた式 `0.75 * h` **1 行だけ**（係数は厳密）＝
  **読み手が 2 つになる**という payoff が消えたので、**器の債務ではなく 1 行の判断**。
  ⚠️ **その remark 自身が「this engine has no Bezier type at all」と書き続けていた**のがこの棚の出所
  ——**同じ便で直した**（§7 の「棚と remark は同じ量の 2 綴り」）。
- **座標系の島2（device 島群）は繰延**: TieVariant / 水平 skyline の Y horizon / TabStaffGeometry /
  beam collision island。`StaffOffsetInSystemDown` の残り呼び出しは**意図的な device 境界＝消さない**。
  島1 が残した手順: ①格納を反転する前に格納値を主張するテストを書く ②生産側は全部同時に
  ③**device 島の縁では 1 回だけ反射する**（反射を島の内側へ押し込まない）。

### F. 言語・ツール側（X/Y とは独立・**一覧は伝聞。着手前に実コードで確認**）

- ✅ **F-fonts. `fonts { }` に*サイズとスタイル*を持たせる（2026-09-07・第342 起票。✅ 文法はユーザー承認済み——「その形で起票して。VS code editor の自動補完も、それに合わせて修正して」・✅ **第354 で実装＝段階 ⑴⑵⑶ 全部（§1 第354・第 1〜6 便）＝閉じた**）**
  ★★★ **入ったもの（第354）**: 文法どおり（`as` 転送・`step ±n`・`size n`・`bold`/`italic`/`regular`・順不同・次のキーで終わる）。**読み手＝`FontPlanReader.ReadAttributes`／`TextFontPlan.Binding(Step,Size,Style)`／`SizeOf`・`StepOf`・`StyleOf`（leaf→group→既定・属性ごと独立）／`ScoreTextMetrics.Size(role, default)`・`Style(role, default)`・`StepOf`**。**従う role の表＝`TextRoles.PlanReachOf`**（**25＝全 role**: title・composer・instrument・lyricText・stanza・chordName・fretFrame・tempo・mark・pedal・navigation・text・dynamics・partCombine・barNumber・tuplet・volta・ottava・bend・tabTechnique・clefOctave・tabFret・meter が Size|Style・**fingering・figuredBass は Size だけ**（Emmentaler の数字列にスタイルは無い＝`bold` は LYS8018）。第 1 便 13・第 2 便 +4・第 3 便 +3・第 4 便 +2・第 5 便 +2・第 6 便 +1）——**validator（LYS8018 警告）とページの Theory `FontAttributeTests.TheReachTable_IsWhatThePageDoes`（全 role を `step +6` で描いて表と両方向に突き合わせ）が同じ表を読む**。診断 LYS8015（家族への属性・キー前の属性）／8016（数値なし・範囲外 step ±12・size 0.5..20）／8017（step と size 両方）／8018。**双子**＝`\layout { \context { \Score \override Grob.font-size = #n / font-series / font-shape } }`・title/composer は `\markup \fontsize`・navigation は markup 内（RehearsalMark を汚さない）・`size` は warning。**LSP**＝キー後に属性・`as` 後に家族・`step`/`size` 後は空・開いた entry では属性＋キー。**TextMate**＝`as FAMILY`／属性語／数値。**残り＝無し（段階 ⑵ は第 6 便で閉じた）**。instrument・stanza・partCombine・ottava は第 2 便、tabTechnique・bend・fretFrame は第 3 便、**fingering・figuredBass は第 4 便**、**clefOctave・tabFret は第 5 便（ユーザー決定 ⒜ 2026-09-09・`TabConstants.FretEm(fonts)`＝数字とそこから測る量だけ動き弦間隔は不動・`SharedRenderer.ClefModifierEm`）**、**meter は第 6 便（ユーザー「通して」・`MeterGlyphRun.PlusEm(fonts)`＝advance も同じ step・spacing core 約 60 メソッドに fonts）**（`FingeringGlyphRun`／`FiguredBassGlyphRun` の `Step(fonts) = FontSizeStep + fonts.StepOf(role, engravingEm)` が `GlyphMetrics.AtFontSize`・`EmmentalerDesignSize`・em を一緒に連れる＝chordName の `AccidentalStep` と同じ形・§1 第354 第 4 便）で足した。⚠️ **sustain の "Ped." はグリフ列で `pedal step` に従わない**（doc に明記）。
  ★★ **なぜ**: 今の `fonts { }` は **face しか運ばない**（`TextFontPlan` が持つのはキー → face 名の連鎖 or 家族への転送と `Embed` だけ＝`Rendering/TextFontPlan.cs:81-86`）。**サイズは約 30 か所に散っている**——`EngravingDefaults` の定数・各 engraver の `*FontSize` static・描画点の裸のリテラル。**スタイルを決める「家」は 3 つしかない**（`MusicMarkEngraver.TextStyleOf`・`DynamicEngraver.LabelStyle`・`EngravingDefaults.ChordNameFontStyle`）。**doc は「スタイルとサイズは engraving の決定で `fonts` は触れない」と明言している**（`GRAMMAR.md:319`・`MusicMarkEngraver.cs:149`）。
  ⚠️⚠️ ★★★ **この便がその代償を測った**: `SharedRenderer.Marks.cs` の印の em は **`FontSize * 0.6 = 2.4`** という手書きの数で、**LilyPond の 2.771822 より 13.4% 小さかった**（§1 第342 第 2 便）。**誰の目にも触れない場所に居たから 200 便気づかれなかった。** サイズが role ごとに 1 つの家に集まっていれば、その定数は LilyPond の値の隣に在った。
  ★★★★ **決まった文法＝ブレース無し・1 エントリ ＝ キー ＋（順不同の）属性**:
  ```
  fonts {
    serif  "Charis SIL"                 % 従来と同じ字面だが、特例ではなく一般規則の 1 属性版
    chordName as sans                   % 家族への転送（`as` は Lily# の家の語）
    mark   "Charis SIL" step +1         % 順不同
    lyrics step -1                      % グループ
    tempo  italic
    title  size 3.8 bold
  }
  ```
  **規則は 1 つ**: エントリは**キー**（`AllKeySpellings()`＝家族 2・グループ 6・leaf role 25）＋属性。**属性は「引用符付きの face 名（複数で連鎖）／`as <家族>`／`step ±n`／`size n`／`bold` `italic` `regular`」の 5 種だけ**で、**エントリは次のキーで終わる**（キーも属性語も閉じた語彙・face は必ず引用符付き ⇒ 曖昧さ無し）。
  ⚠️ **後方互換は取らない**（プレリリース・`.lys` 3493 冊に `fonts` ブロックは **0 冊**）。**平たい形とブロック形の 2 つを持たない**——2 つの綴りは §5.2.1⑤ の「第 2 の綴りに移植が届かない」形そのもの。**変わる綴りは 1 つだけ＝転送** `chordName serif` → **`chordName as sans`**（裸の家族語がキーと値の両方に出るのが唯一の曖昧さの種だった）。
  ★★★ **`step` が主・`size` は逃げ道**: `step ±n` は **LilyPond の `font-size`**（magstep = 2^(n/6)）そのもので、**⑴ role の既定に対する相対なので既定が動いても意図が生き残る／⑵ `.ly` 双子に `\override Grob.font-size = #n` として書き出せる**（`LilyPondExporter.cs:694-713` が `fonts` を*意図的に*書き出していない理由＝双子の忠実度が、`step` なら消える）**／⑶ `size 2` を「2 段大きく」と誤読する罠が無い**。**`size n`（staff space 絶対）は逃げ道**で、**使うと `.ly` 双子の幾何が再現されないと doc に明記すること**。
  ★ **解決順は既存の 4 段そのまま**（leaf → group → engraving の既定）を**属性ごとに独立**に。`lyrics { step -1 }` と `stanza { bold }` は合成される。
  ★ **診断**: `LYS8015 UnknownFontAttribute`／`LYS8016 FontSizeOutOfRange`／`LYS8017 FontSizeAndStepBothGiven`。属性の重複は既存の `LYS8005` を再利用。`mark bold serif` は `serif` がキーとして読まれ **`LYS8006`**（「`as serif` のつもりですか」と言える）。
  ⚠️ **追随が要る場所**（どれも既存のテストが一致を強制している）: `Semantics/FontPlanReader.cs`（キーと値の読み）・`FontBindingValidator`・**LSP 補完** `LilySharpLanguageServer.CompletionVocabulary.cs:576/629/899` と `CompletionContext.cs:602`・**TextMate 文法** `editors/vscode/syntaxes/lilysharp.tmLanguage.json`（**`EditorColouringTests.cs:553,871` が `TextRoles.AllKeySpellings()` との一致を強制**）・doc 3 本（`GRAMMAR.md` §2.4／`SYNTAX_REFERENCE.md`／`GRAMMAR_FOR_LLM.md`）。
  ⚠️⚠️ ★★★ **重いのは文法ではなく 30 か所の付け替え**（`ScoreTextMetrics.Size(role)` / `Style(role)` 経由にする）。**段階的に**: ⑴ plan が値を運び、**既に 1 つの家を持つ role だけ従う**（mark・lyricText・chordName・title・composer・tempo・barNumber・volta・tuplet）／⑵ 描画点に裸のリテラルが残る role（movement title・`Nfr`・bend・compound meter の `+`）は次段／**⑶ 「どの role が plan に従うか」を列挙するテストを置く**——**従わない role が*黙って*無視されるのが一番悪い**。
  ⚠️ **台帳との関係**: 全 800 点は**既定サイズでの数**なので、サイズを変えた本は台帳の外に出る。**`step` ならその本の `.ly` 双子も同じ override を持つので比較可能なまま**——ここでも `step` が効く。

- ★ **⒲ 同じ lyrics track を*独立行*（fold されない words-only 行）として 2 つの別メロディで置く綴りは 2 本目が 1 本目を上書きする**（2026-09-02・第320 起票・**未実装・実需 0 冊**）。**第320 で行の `sings` は行ごとの束縛になった**（§3 先頭行）が、`MeasureCollector.CollectMultiStaff` の `staffVoices` は **track 名で辞書引き**なので、`score { staff sax  lyrics w sings a  lyrics w sings b }` のように**譜の下に fold されない行を同じ名前で 2 本**置くと、後の行の骨格が前の行の骨格を上書きする（`pendingLyricsRows` は `FirstOrDefault` で最初の spec の `Sings` を取る）。**コラール（各行がすぐ上の譜に fold される形）は 4 譜で実測済みで無事**。直すなら「行の識別を (track, 出現順) にする」＝`GetVoiceBindings` の voice 名の一意性の話で、`ChordRowSpec` の同名 2 行も同じ形。**着手前に corpus に訊くこと**（第320 時点で追跡 587 冊に「行に `sings`」は 3 冊・全部 1 本）。
- ✅ **⒵ 閉じた（ユーザー決定 2026-09-08）**——「落ちる」はクラッシュではなく splice 不採用（`splicedMeasures` 1000 → 0＝無変更の尾を歩き直す・出力は full と同一）で perf だけ、時間差は計器の分解能の下。**クラッシュでなければ追わない。** 下は経緯。 ★★★ **本文の長さが変わる編集（＝打鍵の大半）は suffix splice を丸ごと失う。根まで測った・製品コード未着手**（2026-09-03 起票＋同日に根まで。計器 `scratch/p325/P325SpliceDeltaProbe.cs.txt`＝60 小節・absolute 固定でオクターブ変数を外し、**編集の種類と位置**を振って `LastCollectResume` と毒のカウンタを読む。**各行で `incremental == full` を確かめてから数を読む**）。 → **本文は `HANDOFF-ARCHIVE.md`「閉じた §2 の本文」の同じ見出し**（2026-09-08 に落とした）
- ✅ **⒴ は第326 で入った**（§1 第326 ⑴〜⑶＝`pitch concert|written`・score header の `pitch concert`・`Semantics/ConcertPitch.cs`・番人 `ConcertPitchTests` 56 本。**綴りは第326 の後半でユーザー決定、「オクターブ移調楽器は記譜不動」は第327 でユーザー確認済み**＝§3 に行あり）。**下の 3 点の実測はそのまま当たった**——読み返すのは綴りを差し替えるときだけ。
- ★★ **⒴ (B)＝実音入力／移調パート出力（＝「コンサートピッチで書いて、パートは移調して刷る」）**（2026-09-03 起票・**第326 で実装済（上）・ユーザーが「最終的には (B) まで実装したい。安全・堅実に進めて」と明示**）。**(A) は入った**（`259922de`）＝**`InstrumentDefaults.GetTransposition` が半音移調楽器にも答える**（clarinet / clarinet-a / trumpet / trumpet-c / horn / soprano-alto-tenor-baritone-sax）。**(A) は鳴る側だけ**——**書いた音高がそのまま刷られ、動くのは MIDI と tab のフレットだけ**（`InstrumentDefaults.ConcertPitchIsNotImplemented` に「何が足りないか」を書いてある）。**(B) はその逆向き。**
  ⚠️⚠️ ★★★ **調号の機械はもう在る＝(B) に新しい調号コードは要らない**（2026-09-03 実測。**着手前にこれを疑うと 1 便無駄になる**）。**`transpose` は音高と*調号を一緒に*動かす**（`LilyPondExporter.cs:122` が自分でそう書いている・実体は `PitchTransposer.KeySignatureFifthsShift`）。**3 択の A/B/C で数えた**（`c'1` 1 音・`class="music"` のグリフ数）: **⒜ `key c major` ＝ 3**（clef ＋ 拍子 ＋ 符頭）／**⒝ `key d major` ＝ 6**（＋♯2 ＋ C に付く♮）／**⒞ `key c major` ＋ `part { transpose d }` ＝ 5＝**♯2 が `data-pos` 13（`key` トークン）に立ち、符頭は臨時記号無しの D**。⇒ **⒞ は ⒝ と同じ調号を刷っている。**
  ⚠️⚠️ ★★★ **符号は (A) の逆**——**`GetTransposition` は「書いた → 鳴る」（alto-sax は −9）**、**(B) が要るのは「実音 → 書く」（+9）**。**そのまま使い回さないこと。**
  ⚠️⚠️⚠️ ★★★★ **一番危ないのは「同じ量の綴りが 2 つ」**（RULES の族）: **(B) が裏で `transpose` を立てると、再生は preset の −9 と導出された書記側 +9 の*両方*を通る**。**ちょうど 1 度だけ相殺すること**を、**移調しないパートを対照に置いたテストで固定する**（`InstrumentTranspositionMidiTests` の `AChromaticTransposer_SoundsWhereTheInstrumentIs` が (A) 側の番人・12 行 ＋ 対照 3 行）。
  ★ **決めることが 2 つ**: **⑴ 切り替えの綴りと家**（score 級か・`transpose` の score 既定と同じ棚か＝`PartTranspose.ReadScoreDefault`。**そこは第182 `077e5c98` で「1 つの構文が 3 つの答えを返す」を閉じた場所なので、4 つ目の読者を足す形になる**）／**⑵ 既定はどちらか**（実音入力を既定にすると、**今ディスクに在る本の意味が変わる**＝**着手前に corpus に訊くこと**）。
  ★ **MusicXML は (A) の時点で正しい**（2026-09-03 実測）: `<transpose>` に **alto-sax は diatonic −5 / chromatic −9**、**tenor-sax は −1 / −2 ＋ `<octave-change>` −1**。**(B) は書記側を動かすので、この要素の意味（＝「刷られている音高から実音への距離」）が変わらないことを測り直すこと。**
  ★ **落ちたら直す文書**（**(A) のときに 1 つ stale を踏んだ**＝マニュアルの `instrument` 節が「半音移調は組み込みではない」と書いたまま残っていた）: `README.md`・`docs/GRAMMAR_FOR_LLM.md`・**`scratch/site-showcase/manual-body.html` の `instrument` 節と §11 の属性表**（**git 管理外**）。
- ★ **⒳ `repeat percent` の覆われた周に、collector の門（`_percentCoveredDepth`）がまだ通していないもの**（2026-09-02・第320 ⑽ 起票・**未実装・実需 0 冊**）。**第320 ⑽ は tie/slur の marker と script/dynamic を落とした**が、同じ再 walk が今も運ぶものが 4 つ: **figured bass・chord name・cross-staff（`CollectFiguredBass`／`CollectChordNames`／`CollectCrossStaff`）と glissando（`CreateNoteItem` の `hasGliss`）**——どれも % の下に 2 度目を刷る*はず*だが**測っていない**（測ってから同じ門に加えること＝早期 return 1 行ずつ）。**もう 1 つは本文の*末尾*の `~`／`(`**: 書かれた周の最後の音に付いた `~` は覆われた周の先頭音と対を成して tie を描く（LP は 2 周目に音が無いので "unterminated tie" 警告で描かない）。こちらは門の位置が違う（marker は書かれた周に在る）ので、直すなら detector 側で「相手が覆われた小節なら結ばない」＝`PercentRepeatItem.FirstCoveredMeasure` の 5 つ目の読者になる。⚠️ chord name は LP でも ChordNames 文脈に percent engraver が無い＝2 周目は*何も*刷らない（iterator が本文を流さないのは文脈に依らない）ので、「% の上にコード名を繰り返す」が欲しければそれは Lily# 固有の決定になる。

> ## ✅ **⒫ `|:` `:|` `[N. …]` は form 限定になった＝第305 で入った**（起票第303・決定第304・実装 2026-08-31・第305）
> **入ったもの＝診断 1 本**（`LYS1034` / `RepeatStructureScopeValidator`）: **`BarlineSyntax` の token が repeat 種、または `InlineVoltaSyntax`。それが `IsInside<FormDeclarationSyntax>()` でなければエラー。**
> **パーサの腕は 1 つも触っていない**——**その綴りは今も*読める*ので、診断がその場所を名指せる**（禁じたのは token ではなく*場所*）。
> ★ **1 つの述語で 4 つの形をまとめて取った**（phrase の中・`chords` 行・part-major の section・section-major の part ブロック）——**起票が名指した設計そのまま**。
> ⚠️ **数は第304 の 3 度目の数え直しと*ぴったり一致した***（**追跡 11 / ユーザー 115**）——**別の計器**（テキスト走査 対 パーサのノード型）**で同じ数**。
> ⚠️ **掃きは 1946 冊・両側 Release・4 出力**: **`check` が動いたのは 253 冊で、*増えた行は全部 LYS1034*・減った行は「No errors found.」**（例外 5 冊は全部こちらが意図したもの＝言い換えた診断文 3 本と、3 つ目の括弧が通って消えた LYS6008 2 本）。**SVG/MIDI/XML が動いたのは 3 つ目の括弧を使う 2 冊と `:|*N` の probe だけ。**
> ★ **賛成に回った決め手は「同じ構文に家が 2 つある」ことが生む*片方向性*だった**（§1 ⑻ 実測）:
> **music の `|:` は form の `:|` で閉じられる**が、**form の `|:` は music の `:|` では閉じられない**。
> **構造を form へ移す人は、移すのが `|:` のほうなので、必ず通らない側に先に当たる。**
> ★ **線は「演奏順序を変えるか」で引く**——**form へ**: `|:` `:|` `[N. …]` `repeat volta`／
> **music に残す**: `repeat percent`（ユーザーの **113 冊**）・`repeat unfold`・`tremolo`（＝音符の省略記法で、順序を変えない）。
> ✅ **前提条件 ⑴ ＝ 引き継ぎの記法**（下の ⒬）は **2026-08-31・第304 で入った**（`~B'` / `~B,` / `[1. B']`）。
> ✅ **前提条件 ⑵ も第304 で入った**: **宣言側に `section ~A { … }` と書くと、その section のラベル既定がひっくり返る**（§3）。**純粋な追加**であることは実測済み——**`section ~A` を書いている本はディスク 1925 冊中 0 冊で、直前まで硬いエラー**（`Expected a name, found 'Tilde'`）。★ **前提条件は 2 つとも済んだ**——**決定という意味では ⒫ の着手を止めるものは無い**（移行も ⒜ 即エラー）。
> ★ **第304 は着手前の数を 3 度間違えてから収束させた**（20 → 14 → 12 → 11。`//` コメント／1 行 form の regex／lyrics の `[1. …]`）。**第305 がコンパイラで数え直して 11 と 115 が再現した**——**計器が別**（テキスト走査 対 パーサのノード型）**なので、これは 2 つ目の観測**。
> ⚠️ **禁止の射程**: **lyrics の `[N. …]`（歌詞の番＝`LyricVoltaSyntax`）と `repeat percent` / `repeat unfold` / `tremolo` は残る**——**除外リストではなく*ノード型が別*だから**。`chords` 行の `|:` は繰り返しなので対象。**lyrics 行の `|:` は `BarlineSyntax` にならない**（`LyricMeasureGreen` の生トークン）**のでこの規則からは見えない**——**書けてしまうが、歌詞の行は何も演奏しないので順序も変えない**（実測して記録・`scratch/p305/b4_lyric_row_repeat_bar.lys`）。
> ★ **切り直した 11 冊の結果**（`scratch/p305/cmp.ps1` が base の掃きハッシュと突き合わせる）:
> | 本 | ページ | 音 | MusicXML |
> |---|---|---|---|
> | `test/lead-sheet-repeat`（snapshot） | **インク同一**（`data-pos` のみ） | 同一 | 同一 |
> | `test/rehearsal-marks-inside-containers`（snapshot） | **インク同一**（`data-pos` のみ） | 同一 | **`<ending>` が増える**（インライン綴りは 1 度も書いていなかった） |
> | `test/grandstaff-repeat`（snapshot） | **+8 要素・削除 0**＝**volta 括弧 2 本と "1." "2."**（旧綴りはこの本でどこにも描いていなかった） | **18 → 20 音**（第304 の予測どおり） | 変わる |
> | `showcase/grammar-2026-06-09` | 幽霊空小節 1 つぶん縮む | 同左 | `<ending>` が増える |
> | `showcase/grammar-tour` | 組み直し（snapshot 無し） | 同左 | — |
> | `audit/lpreg/voltasky` | **主張の量は不動**（鎖 1 が y=8.79・鎖 2 が y=6.79＝2.0 差・括弧 6 本）。X が組み直り、**最後の括弧に右キャップが出るようになった** | — | — |
> | `audit/lpreg/voltagrace{,-ctl3,-ctl4}` ＋ `audit/lp-regression/lys/repeat-volta-initial-grace` | **幽霊空小節が消える**（MusicXML の小節が 2 → 1） | 同左 | 同左 |
> | `audit/lpreg/voltagrace-ctl` | **インク同一**（`data-pos` のみ） | 同一 | 同一 |
> ⇒ ⚠️⚠️ **snapshot 3 枚は再ベースした**（**ユーザー承認を取ってから**・§5.1）。**`data-pos` は本文を書き換えれば必ず動く**ので、第304 の「インクが同一に切り直せれば再ベースは 0 件」は原理的に成り立たなかった——**保てるのはインクのほうで、2 枚は保てた**。★ **numstat がその区別を言う**: 9/9・46/46（出入り同数＝`data-pos` だけ）対 56/48（**+8 要素・削除 0**）。
> ⚠️⚠️⚠️ ★★★★ **⒫ は「構文の移動」ではない＝`|:` は書いた場所で意味が違う**（第304 実測）:
> | 書き方 | 上パート | 下パート |
> |---|---|---|
> | `up { \|: c'4 c c c \| :\| }`（music） | **8 音**（2 回） | **4 音**（1 回） |
> | `form main { \|: Main :\| }`（form） | 8 音 | **8 音**（2 回） |
> ⇒ **music の `|:` は*それを書いたパートだけ*を展開し、form の繰り返しは*スコア全体*を展開する。** **`grandstaff-repeat.lys` を切り直すと MIDI が 18 → 20 音。**
> ⚠️ **ページは前から score 級**（その fixture のコメント自身が「rh にだけ書いた繰り返しも両方の五線に出る」と書く）＝**ページと MIDI は前から食い違っていた。**
> ⚠️⚠️ ★★★ **これは決めることではなく*決定の帰結*。** **第304 はここで「意図か欠陥か」を訊きかけ、蒸し返しだと指摘されて取り下げた**（RULES §5.1 に汎化）。
> ⇒ ★★ **網は張り直した**（黙って別のテストに替えていない・§5.4）: **`grandstaff-repeat` と `grammar-tour` の「1 パートだけの繰り返し」という主張は*書けない*ので、両方のヘッダに「この本が言えなくなったこと」を名指して残した。** **`rehearsal-marks-inside-containers` も同じ**（インライン volta という容れ物が消え、生き残った容れ物は tuplet だけ）。**`VoltaBracketSkylineTests` は inline 綴りの 1 行を form 綴りに書き直した**（量は不動）。
> ★ **ユーザーのコーパスには当たらない**——**`|:` を music に書く 115 冊は全部 1 パート**。
>
> ### ⚠️⚠️⚠️ ★★★★ **⒫ が*道連れにした*3 つ**（どれも起票には無い。禁止が「唯一の家」を作った瞬間に load-bearing になったもの）
> **⑴ form は 3 つ目の括弧を言えなかった。** **music は `|: X | [1. A] :| [2. B] :| [3. C]` と書けたが、form は `:|` のあとの括弧を 1 つしか取らず、3 本目は LYS6008 で落ちて素の参照になっていた**（実測）。⇒ **これは「決定の帰結」ではなく*決定の前提の反証***（「form の中だけで書けるようにする」は form が同じことを言えることを前提にしている）。**着手前に数えた＝ユーザーの 326 冊のうち 13 冊・追跡 1 冊（`voltasky`）が 3 つ目以降を使う。** ⇒ **`ParseFormRepeatBlock` に「`:| [N. …]` をもう一度」の腕を足した**（`FormRepeatBlockGreen` は平らな子リストなので `FormWalk` は slot を歩くだけで読める＝**下流は 1 行も変えていない**）。**検算は恒等の対**: **ページはマスク後バイト同一・MIDI は 6 音で音列一致**。⚠️ **そのとき*旧綴りのほうが壊れていた*のが分かった**——**music 綴りの 3 パス目は本体を飛ばして括弧だけ鳴らしていた**（X E1 X E2 **E3**）。
> **⑵ form の `:|*N` は MIDI に届いていなかった。** **`MidiExporter.PlayRepeatBlock` は `Math.Max(2, alternatives.Count)` で、`FormWalk` が持っている play count を読んでいなかった**——**`form { |: ~X :|*3 }` は 2 回、同じ音楽を music に書くと 3 回**（実測 16 音 対 24 音）。**base でも同じなので第305 が作った欠陥ではない**が、**⒫ が「唯一の綴り」にした瞬間、これが*その量の唯一の挙動*になる**。⇒ **music 側と同じ 3 段の規則に直した**（明示 `*N` ＞ 括弧の数 ＞ 2）。**ページと `.ly` 双子は前から正しかった**＝**4 読み手のうち MIDI だけがずれていた。** ★ **射程はディスク 19 冊（ユーザー 8 冊）。**
> **⑶ MusicXML importer が*禁止した綴りを書いていた*。** **`LysWriter` は ending 付きだけを「名前つき section ＋ form」に分解し、単純な繰り返しは `BarlineBetween` が music に `|:` を書いていた**＝**import した本がコンパイルできない**。⇒ **`TryFactorPlainRepeats` を足した**（repeat 小節線ごとに section を切り、form が順序を言う。`:|:` も一方通行の `:|` も取る。閉じられていない `|:` は曲末で閉じて `report.Warn`）。**`BarlineBetween` の repeat 3 腕は到達不能になったので、消さずに理由を書いた。**
> ⇒ ★★★ **判定法として RULES §5.1 に汎化した 3 本**: **⒜ 「X を場所 Y だけに移す」決定は、*Y が X を綴れるか*を綴りごとに数えてから着手する**／**⒝ 綴りを 1 つ禁じたら、*残る綴り*の読み手を全部並べて同じ答えを出すか 1 つずつ測る**／**⒞ 禁じた綴りを*書く側*（importer・formatter・診断メッセージ）を grep する。**
> ⚠️ **嘘になった計器を 3 本直した**: `RepeatVoltaRemoved` の案内文（`[1. …] [2. …]` を宣伝していた）・`RepeatPairingValidator` の LYS4017（「この section か form のどちらでもよい」）・`ParseFormRepeatBlock` の LYS4017（「逆向きは効く」）。

> ## ✅ **⒬ section 参照の octave marks＝第304 で入った**（起票第303・実装 2026-08-31・第304）
> **入ったもの**: **`~B'` / `B,` / `[1. B']` が、その*play* の枠を 1 マーク＝1 オクターブ動かす**（phrase 参照の印と同じ綴り・同じ意味）。
> **shift は occurrence のもの**——`~B ~B'` は 1 つの section を 2 つのオクターブで鳴らし、**宣言は動かず、次の参照は part の anchor に戻る**。
> **両綴りが取り**（`~` はラベルだけを隠す）、**volta ending も取り**、**`octave absolute` でも効く**。
> ⚠️⚠️ ★★★ **起票が測っていなかった数が 1 つあり、それが設計を変えた＝*ユーザーの本の 87% が `octave absolute`***（283/326。
> **section を 2 つ以上持つ 75 冊でも 52 冊が absolute**）。**relative だけ実装していたら、この記法が存在する理由の大半の本で黙って落ちていた。**
> ★ **「新しい概念はゼロ」は正しかった**（phrase 参照は既に両モードを持っていた＝relative は `EnterDefaultFrame`、absolute は `OctaveBase +=` と twin の `\fixed`）
> **が、「最小の追加」は*読み手ごとに腕が 2 本*という意味だった。**
> ⚠️⚠️ ★★★ **そしてその「腕 2 本」が、入れた*あとに* 2 つ目の穴を出した＝`section B { P }` を `~B'` で鳴らすと absolute だけが動いた**
> （**実測: absolute G4 → G5 ／ relative は G3 のまま**）。**phrase の本体は「毎回同じ音」のために*新しい枠*を開く**（`OctaveContext.ResetToInitial`）が、
> **その枠の anchor は *section* のものでなければならない**——さもないと `~B'` は「1 オクターブ上。ただし section が参照で書かれていなければ」という意味になる。
> ⇒ **`OctaveContext.SectionOctaveOffset` を 1 本置いて `ResetToInitial` が読む**（ly と xml の phrase 腕にも同じ 1 語。midi は `_partOctaveAnchor` が既に shift 済みで最初から正しかった）。
> ★★ **判定法として残す**: **モードごとに腕を 2 本書いたら、*その量を読み直す他の場所*を数える**——**穴は「片方のモードだけが既に正しい」という顔で出る。**
> ★ **音価は新しい記法が要らない**（section の最初の音符に数字を 1 つ書けば済む——LilyPond の綴りでもある）ので、**この項では入れていない。**
> ⚠️ **まだ開いている半分＝「前の section から*引き継ぐ*」**（`~B~` のような印）。**入れるなら参照側の opt-in にすること。宣言側に置いてはいけない**——
> section の音が呼び出し元で変わる。**それはこのリセットが直した当のバグ**（`ProcessSectionPrologue` のコメントが実例を残している:
> 「the reprise `A` after `~B` (`g'1`) inherited B's whole-note and rendered its quarter-note melody as whole notes」）。
> ★ **番人は `SectionReferenceOctaveTests`**（**1 ファイル・読み手ごとに 1 メソッド・全ケース両モード**）。**毒は `scratch/p304/poison.py` の 17 本。**

> ## ✅ **⒯ 独立ヘッダは*どこに書いても*ヘッダ＝第306 で入った**（起票第305・実装 2026-08-31・第306・`427d3a38`）
> **`part` の*後ろ*に置いた `section A { key g major }` が part の本物の宣言を上書きし、双子が `\key g \major \key g \major` と*ヘッダを鳴らして*音符を落としていた**（前に置けば健全＝**違いは行の順だけ**）。
> ★ **原因は述語の食い違い 1 か所**: **`LilyPondExporter.OrderedMusic` の「単一 part の略記」の腕が `LooseSectionMusic(s).Any()` を訊いていた**——**その一覧は指示を音楽に数える**ので、ヘッダが「鳴る宣言」として*後勝ち*で登録された。**コレクタの同じ腕が訊いている `SectionHasInlineMusic`（THE one spelling）に揃えた＝直したのは 1 行。**
> ★ **form 無しの腕も一緒に閉じた**（ヘッダが `inOrder` の 2 つ目として「空の 2 度目の A」を鳴らしていた）。
> ★ **射程は実測ゼロ**: **1966 冊で SVG / MIDI / XML / `check` は 1 冊も動かず、双子が動いた 7 冊は全部 scratch のプローブ**。⚠️ **起票の proxy が名指したユーザー本 `(They Long To Be) Close To You.lys` は白**（独立ヘッダを 1 つも持っていない。**「双子 75 対ページ 83」は `\repeat percent 4` と末尾の `s1` でちょうど説明が付く**）。
> ★ **番人は `LilyPondStandaloneSectionHeaderTests`**（**対そのものを恒等式として主張する 2 行**＋内容 8 行＋**狭めた腕の陽性対照 1 本**＋**穴のピン 1 本**）。**残った穴は下の ⒰。**
>
> ## ✅ **⒰ ヘッダだけの宣言を form が鳴らすのは誤り＝第306 で拒否になった**（起票第306・ユーザー決定 2026-08-31・実装同便・`LYS1036` `20c75cb4`）
> **`section A { key g major }` だけが A の宣言で、どの part も A に音楽を与えないとき、ページはその調を arm して次の section の小節に効かせ、双子は調を 1 つも書かなかった。**⇒ ★★ **どちらの沈黙が正しいかを選ばず、綴りを拒否した**（§3 の 1 行）。
> ★ **`LYS1005` の兄弟で、その差が規則そのもの**: **`form main { ~Z }` で `section Z` がどこにも無い本は*既に*エラー**（実測）。**これは宣言は在って全部ヘッダの場合**なので、**同じ検証器・同じ walk の 1 つ先の腕**に置き、**未宣言の側は先に return する**（宣言されていない名前に「ヘッダだけ」と言うのは嘘でもある）。
> ⚠️⚠️ ★★★ **判定は「名前が」であって「この part が」ではない**——**`part fl { section A { … } }` の隣で score が `part m` だけを描く本は*正しい*（m は A の間スペーサで埋まる）**ので、「この part が宣言しているか」で訊くと誤爆する。**規則を書く前に測って**（`scratch/p306/u3`・2 小節・clean）**番人の*黙る行*として留めた**。
> ★ **述語は 1 軒に寄せた**: **`IsBareHeader` を `SectionSymbols` へ移し**（そこは既に「何が section 名を宣言するか」の家）、**`PartSectionLayoutConverter` の写しは委譲**。⚠️ **これは ⒯ が数時間前に逆向きで要求した修理と同じ**——**エクスポータが共有の問いではなく自前の問いを訊いていたことが、ヘッダに part の音楽を食わせた。**
> ★ **射程は実測ゼロ**（1970 冊で `check` が動いたのは本日書いた 3 冊のプローブだけ・出力は 4 つとも不動）。**番人は `SectionPlaysNothingValidatorTests`**（**黙る行 6 本**——そこがこの規則の壊れどころ）。
>
> ## ✅ **⒭ 閉じた＝ヘッダ位置は第305・cell の option 位置は第306**（起票第303・第305 が測り直して 3 つに分け・`LYS1035` の 2 つの位置 `14b45d4f` と `20c75cb4`）
> ✅ **ヘッダ位置**（第305）: **section の「ヘッダ位置」に書いた `clef` / `octave` はエラー**（`instrument` / `transpose` は元からエラーで、直したのは LYS0030 の文面だけ）。
> ✅ **part ブロックの option 位置**（第306・`section A { m clef bass { … } }`）: **4 つとも拒否**。
> ⚠️⚠️ ★★★ **後半は「決めることが 2 つ」と書かれていたが、*どちらも新しい決定ではなかった***——**§3 に「`transpose` / `octave` を section スコープの機能として足さない」が既に在り、その理由（*参照側*の印であって宣言側ではない）が cell にもそのまま届く**。⇒ ★★ **⑵ が No なら ⑴ も決まる。起票が「決めること」と呼んだものが、読み直すと「既に決まっていること」だった。**
> ★ **実測（第305）が 4 つを 3 対 1 に割っていたのが要点**: **`clef` / `octave` / `instrument` は黙って無視され、`transpose` だけが読まれてスコープを間違える**（cell に書いて part 全体が動く）。**番人はその非対称を保つため 4 行に分けてある。**
> ★ **書き手も見た**（第305 の規則）: **`PartSectionLayoutConverter` は cell を `BetweenBraces` で写すのでoption を*黙って落としていた***——**源で拒否したことでその経路も消えた**（逆向きは第305 の `Convert_NeverProducesABookTheCompilerRefuses` が押さえている）。
> ★ **射程は実測ゼロ**（scratch のプローブ 3 冊のみ）。**`GRAMMAR.md` に位置と「なぜ scope せず拒否したか」を書いた。**
>
> ### 以下は第303 起票の経緯（同じ棚にもう一度触るときのために残す）
> ⚠️ **この項は最初「音部記号とテンポだけが引き継ぐ理由が書かれていない」として起票したが、前提が誤っていた**（第303 §1 ⑸ の訂正）。**section 境界の現状はユーザーの判断とちょうど一致している**——**調はリセット／音部記号もリセット／テンポはリセットしない**（§3）。**ここは閉じている。**
>
> ### ⑴ 起票の主張は再現する（第305 実測・`scratch/p305/r1_section_header_clef.lys`）
> **`section A { clef bass  m { c'4 c c c | } }` の `clef bass` は 4 読み手すべてで落ちる**: ページはクレフ字母を 1 つしか描かず（`data-pos` は part ヘッダの `treble`）、**`.ly` 双子は `\clef "treble"` しか書かず**、`--pitches` も treble の anchor（C5）。**`lysc check` は「No errors found.」**
> ⇒ **傍証も現存**: **section ヘッダのレジストリは key / time / tempo / partial の 4 本だけ**（`MeasureCollector.Definitions.cs:317-324` が `_sectionHeaderKeys` / `Times` / `Tempos` / `Partials` を埋める）——**`clef` の腕は無い**。**`_sectionResetClef` は `MeasureCollector.Form.cs:355-359` の*part 既定へ戻す*係**で、ヘッダの clef を*適用する*係ではない。
>
> ### ⑵ ★★★ 起票が「確かめていない」と書いた問いには、いま答えが在る＝**言語は約束していない**
> **`GRAMMAR.md` の `SectionSetting = KeyDecl | TempoDecl | TimeDecl | PartialDecl ;`**——**clef は section setting ではない。** ⇒ **修理は実装ではなく*診断*。** ⚠️ **ただし ⑷ を読んでから決めること**（診断の対象が clef 1 つでは済まない）。
>
> ### ⑶ ★★★ 機構は起票の診断より 1 段下にある＝**ヘッダ設定にならず「孤児の音楽」になる**
> **`ParseSectionItem` に `ClefKeyword` の腕は無い**が、**`IsMusicItemStart()` が `ClefKeyword` を受ける**ので、`_ when IsMusicItemStart() => ParseMusicItem()` に落ちる。⇒ **section ヘッダの `clef` は*どの part にも属さない裸の音楽*としてツリーに入る。** **`SectionMusicNeedsPartValidator` はそれを part-major でしか報告しない**ので、**section-major では誰も何も言わない**。⇒ **「レジストリに腕が無い」ではなく「そもそもヘッダ設定として読まれていない」が根。**
>
> ### ⑷ ⚠️⚠️ ★★★ 穴は起票より広い＝**part ブロックの option という第 2 の綴りが在り、そちらは 4 つとも壊れている**
> **パーサは part ブロックに option 専用の腕を持っている**（`Parser.Sections.cs` の `IsPartOption` ＝ **transpose / octave / instrument / clef**）ので **`section A { m clef bass { … } }` も無警告で通る**。⚠️ **`GRAMMAR.md` の `PartBlock = Identifier , MusicBlock ;` には option がそもそも書かれていない。** **4 つ測った**（`scratch/p305/r4_partblock_options.lys`・4 section の同じ音楽）:
> | 綴り | 結果 |
> |---|---|
> | `m clef bass { … }` | **黙って無視** |
> | `m octave absolute { … }` | **黙って無視** |
> | `m instrument "Tuba" { … }` | **黙って無視** |
> | `m transpose d { … }` | ⚠️⚠️ **読まれるが*スコープが違う*——section A に書いたのに 4 section 全部が D5 になった**（双子は `m = \transpose c d \relative c'` を **part 全体**に掛ける） |
> ⇒ ★★★ **「1 つが何もしない」ではなく「3 つが何もせず 1 つが黙ってやりすぎる」。** **`transpose` の漏れは*出力が変わる*欠陥**なので、性質が他の 3 つと違う——**別項に切るべきかもしれない。**
>
> ### ⑸ 対照は健全（＝壊れているのは「ヘッダ位置」だけ）
> **音楽の中の `clef` は効く**: `m { clef bass c'4 c c c | }` は双子が `\clef "bass"` を書き、音高は C4、**次の section は treble に正しく戻る**（`_sectionResetClef` が仕事をしている）。`scratch/p305/r3_clef_in_music.lys`。
>
> ⇒ ⚠️⚠️ ★★★ **着手前にユーザー決定が要る＝綴りごとに「診断して拒否する」か「実装する」か。** **⒫ を 2 便止めていたのと同じ形の問い**で、しかも **`transpose` の側は出力が動く**。★ **測定は済んでいるので、次便は数え直す必要が無い**（probe は `scratch/p305/r1`〜`r4`。⚠️ `scratch/` は git 管理外）。

> ## ✅ **⒮ は ⒫ が閉じた＝「双子が music の inline volta を書き出さない」は*問いごと消えた***（第303 起票・第304 が測り直し・**2026-08-31・第305 で ⒫ が入って閉じた**）
> ⚠️ **直したのではない。その綴りが書けなくなった**——`[1. … ] :| [2. … ]` は music に無いので、双子が変換しそこねる対象が存在しない。**§2 F ⒫ の「同じ 1 つの決定の裏表」がそのとおりだったということ**（⒫ を入れたら ⒮ の仕事は 0 行になった）。
> ⚠️ **77 冊という射程も消えた**（その 77 冊は今 LYS1034 で止まり、切り直すと form 側の `\repeat volta` / `\alternative` を通る＝**双子が正しくなる経路に載る**）。★ **実測でも見えた**——`rehearsal-marks-inside-containers` を切り直したら **MusicXML が `<ending>` を*得た***。
> ★ **以下は当時の実測。同じ棚にもう一度触るときのために残す。**
> ⚠️⚠️ ★★★ **起票は「`form { ~B |: A :| }` と music の volta を*混ぜた*から余分な `\bar` が出る」と書いていたが、実測すると*混ぜていない本でも出る*。**
> **対照**（`scratch/p304/s1_both.lys` ／ `s2_noformrepeat.lys` ／ `s3_formrepeat_only.lys`・両方とも `lysc check` 無警告）:
> | 本 | `.ly` | `lysc ly` の警告 |
> |---|---|---|
> | `form main { ~B \|: A :\| }` ＋ music volta | `\repeat volta 2 { … } \alternative { … }` ＋ **余分な `\bar ":\|."`** | 片側 `:\|` の 1 本 |
> | **`form main { ~B A }`** ＋ music volta | **`\repeat volta` も `\alternative` も出ない** ＋ **`\bar ":\|."`** | **`InlineVolta not exported` ×2** ＋ 片側 `:\|` |
> | `form main { ~B \|: A :\| }`・music volta 無し | `\repeat volta 2 { … }` のみ・**余分な `\bar` 無し** | 無し |
> ⇒ ★★★ **本当の項は「余分な `\bar` を 1 本落とす」ではなく「双子が inline volta を持たない」**——**`\repeat volta` / `\alternative` を作っているのは*form の repeat ブロック*のほうで、music の `[1. … ] :\| [2. … ]` は 1 度も変換されない。**
> ★ **ページ側は起票時「未確認」だったので測った**: **3 冊とも `lysc layout` の小節数はページと一致**（混合 4 小節・form repeat のみ 2 小節）**＝ページは正しく、食い違っているのは `.ly` だけ。**
> ★★ **そして黙ってはいない**——**`InlineVolta not exported` と「片側 `:\|` は LilyPond では*描くだけ*」の 2 本が、双子が違う曲になることを*その語で*言っている**（§5.2.1 の「嘘を立てたまま残さない」は満たしている）。
> ⚠️⚠️ **射程はこれで大きく変わる**: **ユーザーの 326 冊のうち `[N. …]` を music にインラインで書く本が 77 冊**（第304 実測）——**双子に inline volta を教えるのは、その 77 冊の `.ly` が動く変更で、掃きと読み合わせを 1 便まるごと要る。**
> ⇒ ✅ **第305 で ⒫ が入り、そのとおりになった**（上の見出し）。**残っている本物の穴は 1 つだけ**——**「片側 `:|` は LilyPond では*描くだけ*で、繰り返しは鳴らない」**（`lysc ly` が警告する）。**これは form 側の綴りなので生きている。**

> ## ▶ **⒩ 展開の予算はページだけのもので、`midi`・`xml`・`ly` は読まない＝1 冊の本に「どれだけ音楽が在るか」で 4 つの出力が食い違う**（第301 起票・**実測**・✅ **【ユーザー決定 2026-09-08: ⒜ 予算は絵だけのもの】LYS1033 の文面は第352 第 4 便で入れた＝閉じた**）
> **`MeasureCollector` は展開に site 予算を持ち**（`DefaultExpansionBudgetCap = 50_000`）、
> **超えたら絵を打ち切って `LYS1033` で*そう言う***（「this score expands past the collector's
> site budget, so the picture is TRUNCATED from here on」）。**その文は*絵*についてしか言っていない**——
> **`MidiExporter` と `MusicXmlExporter` の主旋律の walk は予算を 1 度も引かない。**
> **実測 2026-08-30**（`scratch/p301/budget`・倍々の phrase DAG `P(n) = P(n-1) P(n-1)`・**原文 26 行**）:
> **`svg` は 131,072 音の本でも 1,048,576 音の本でも `data-pos` 21,350／21,349 で*平ら*（2.0→2.4 秒）**、
> **`midi` は 131,072／1,048,576 をそのまま鳴らし（0.88→1.9 秒・1.2 MB→9.4 MB）**、
> **`xml` は同じだけ書き出す（1.2→4.2 秒・24 MB→**`192 MB`**）。**
> ⇒ ★★★ **読み手は「絵は打ち切った」という警告を読んだあと、打ち切られていない 100 万音を再生し、
> 192 MB を書き出す。** ⇒ **これは第301 が `grace { }` の本体で直した族の*1 段外側*で、
> 病名も同じ「1 つの量を N 人が別々に答える」。**
> ⚠️ **重さは低い**——**実コーパス（ディスク 1754 冊）に倍々 DAG は 1 冊も無く、
> 本便の掃きでもこの差は 1 冊も出ていない。** **これは「今日の欠陥」ではなく「今日の食い違い」。**
> ⚠️⚠️ ★★★ **着手する便はまず*どちらが正しいか*を決めること。3 通り在る**:
> **⒜ 予算は絵だけのもので、export は書かれたものを全部出すのが正しい**（**なら `LYS1033` の文面が
> 「絵は」と言っているのは正しく、足りないのは*他の出力にはこの打ち切りが無い*と読み手に言うこと**）／
> **⒝ 予算は本のもので、4 つの出力が同じところで打ち切るべき**（**なら `ChargeExpansion` は
> collector の外へ出る**）／**⒞ 予算そのものが LSP の打鍵経路のためのもので、CLI の一発 export では
> 外すべき**（**なら `svg` の側が場合分けを持つ**）。
> ⚠️ **`lysc ly` の双子は数えていない**——**着手する便が 4 つ目として測ること。**
> ⇒ ✅ **ユーザー決定 2026-09-08＝⒜**。根拠: 50,000 は実コーパス最大の本（8,000 音）より上で、かつ行折り DP が確保に失敗する大きさ＝**ページの描画能力の限界であって音楽の性質ではない**。100 万音の MIDI は正しい出力で、切る理由がページの都合しか無い。**残る仕事＝LYS1033 の文面に「MIDI・MusicXML・ly はこの打ち切りを持たない」を足す**（`ExpansionBudgetValidator`）。⒝⒞ は採らない。
> ★ **計器はディスクに在る**: `scratch/p301/budget/`（`gen.py` が本を作り、`run.sh` が時間と大きさ、
> `count.sh` が出力ごとの音符数を出す。**`scratch/` は git 管理外なので、無ければ 3 本とも 20 行以下**）。

> ## ✅ **⒧ 3 小節以上の整数小節 `repeat percent` の `%`＝【ユーザー決定 2026-08-29: 診断を出す】**（第282 起票・同便で実装・`85406c45`・LYS2014）
> **`%` は「直前の 1 小節を繰り返す」記号**なので、`repeat percent 2 { A | B | C | D | }` の 4 つの `%` は**読み手に D D D D を指示する**（作者の意図は A B C D）。**音は正しく、紙だけがずれている。**
> **⒤ 今のまま／⒥ 診断／⒦ 別の記譜 の 3 択を出し、ユーザーが ⒥ を選んだ。****⒦ を採らなかったので出力は 1 バイトも動いていない**（全数掃き 899 冊 SAME 899 / MOVED 0）。
> ⚠️ **警告が安全な条件は「コレクタが小節ごとに署名するのと*ちょうど同じ*場所で鳴る」こと**——規則は `PercentRepeatShape` の一軒、**長さの測り方は 2 つのまま**で、**掃きが突き合わせる**（30 冊 94 site・census と 1 冊も違わない）。**この不変条件を壊す変更は、掃きで数を取り直すこと。**
> ★ **残っているのは ⒦ だけ**（`repeat unfold` 相当に書き下す等）。**今日それを求めている本は 1 冊も無い**ので、**起票し直す前に、まず求める本が現れたかを数えること。**


> ## ✅ **⒤ `time none`（senza misura）は描画側で効いていない＝半実装**（第280 起票・**実測**・✅ **【ユーザー決定 2026-09-08: 彫る】・第353 で実装＝§1 第353 ⑵・番人 `SenzaMisuraTests`・fixture `test/senza-misura`・LP 対 `scratch/p354/lp`**）
> **パーサも検証器も知っている**——`TimeSignatureSyntax.IsSenzaMisura` が `time none` を読み、
> `MeasureModel.Split` と `MeasureValidator` は小節長の検査を止める。**描画側がしているのは
> 拍子記号を消すことだけ**（`SharedRenderer.Prefix.DrawTimeSignature` の 1 行 `if (ts.SenzaMisura) return x;`）
> ——**`MeasureBuilder` に senza の腕が無い**ので、**自動補完は 4/4 のまま走り、小節線が引かれる**。
> ★ **実測**（`scratch/p280/senza-long.lys`・`senza-sec.lys`）: `time none` を**上位に書いても
> section の設定に書いても**、`lysc layout` は **`time 4/4 | 2 systems, 9 bars`** と答え、
> 絵には**小節線が 8 本引かれる**（拍子記号だけが消える）。
> ⇒ **無拍子の音楽は今日そもそも彫れない。**
> ⚠️ **観測者ゼロ**——**`time none` を書いた本は追跡 573 冊にも作者の 326 冊にも 0 冊**
> （だから誰も踏んでいない。**踏まれていないだけで、規則としては嘘をついている**）。
> ⇒ **着手はユーザー決定から**: 「無拍子を彫る」を決めるなら **`MeasureBuilder` に
> 「境界を作らない」腕**が要る（auto-fill を止める・小節線を引かない・小節番号をどう数えるか）。
> ★ **そして下の ⒥ より先**——**⒥ の必要は ⒤ が閉じて初めて*実在*する**。
> ⇒ ✅ **ユーザー決定 2026-09-08＝彫る**（「LP が `time none` を彫るなら Lily# も」）。**LP は `\cadenzaOn`**（NR「Unmetered music」: 区間は小節長に数えない・自動小節線／自動連桁／自動改行が止まる・`\cadenzaOff` で戻る）。**双子は既に `time none` を `\cadenzaOn` で書いている**（`LilyPondExporter.cs:2586`）ので、今日は 4 読み手のうちページだけが嘘。**移植点 4 つ**: ⑴ `MeasureBuilder` の境界を作らない腕（auto-fill 停止・小節線を引かない）／⑵ 自動連桁停止（`BeamingPattern.cs:185` は senza を既に見る＝確認）／⑶ 改行は次の `time N/M` まで無し（⒥「小節線でだけ折る」と整合）／⑷ 小節番号を進めない。**観測者 0（追跡 597・ユーザー 324 とも `time none` 0 冊）なので fixture＋LP 双子の対を先に建てる**（LP 側の計器は `scratch/p280/lp/` の型）。⚠️ 双子の `\cadenzaOff` を書いているかは着手時に確認（`TimeText` は `\cadenzaOn` しか書かない）。

> ## ✅ **⒥ 小節の途中での改行＝【ユーザー指示 2026-09-09「安全に実装できるなら実装して」・第356 で実装＝§3 先頭行・§1 第356】**（旧決定 2026-08-29「サポートしない」は ⒤ が閉じた時点で再検討条件が成立していた。**下は第280 の経緯**）
> ★ **第356 の形**: 音楽の両側を持つ小節途中の `break`（`pageBreak` も）は、その小節を**head（終止線なし・改行 Force・`Measure.BreaksMidBar`）と tail（開始線なし・小節番号なし・`Measure.ContinuesBar`）の 2 measure**に割る。割る場所は score 全体で 1 つの表 `MidBarBreakTable`（bar 単位の鍵＝`MeasureBuilder.LogicalMeasureIndex`）が決め、**collector は 2 回走る**（1 回目が要求を集め、2 回目が全 voice・行・`| |`・omitted-part harvest を同じ拍で切る）。**割れない小節は割らず、break は次の小節線に落ち、LYS1037 が理由を名指す**（他 part の音符が跨ぐ＝LILYSHARP-OWN・梁・tuplet・percent・`time none`・同じ小節の 2 本目）。読み手 4 人のうち動くのはページだけ（MIDI/XML は構文・双子は `\break` をそのまま書く）。掃き 922 冊 **MOVED 1＝ユーザーの `Disco Inferno.lys`（機能そのもの＝section 末の半小節 `break`・§1 ⑷）**・番人 `MidBarBreakTests` 17 本・fixture `test/mid-bar-break`・LP 対は `scratch/p357/lp/pair357.ps1`。⚠️ **T7 の「Lily# は行 DP の最適を段数にし LP は頁の得点で選び直す」とは独立**（forced break の話）。
> **⒜ LP は対応している**（2.27.3 実測・`scratch/p280/lp/`）: `\break` を小節の途中に書くと
> **その位置で折り、折れ目に小節線を描かない**（1 小節が 2 段にまたがる。`\bar ""` は不要だった）。
> **⒝ Lily# は黙って次の小節線へ送る**——**絵は「小節線に break を書いた本」と `data-pos` を除いて
> バイト同一**、診断ゼロ。
> **⒞ しかし求めている本が無い**。**毒**（`SetBreak` の else 腕で「小節が満杯か」を印字）**で 899 冊を掃いた**:
> **真に小節の途中に立つ `break` は追跡 573 冊で 0 件**、作者の 326 冊で 198 件・8 冊。
> ★ **その 198 件を 1 か所ずつ住所つきで読んだら全部が「短い小節の中に置かれた break」**
> （`e2 break` で `dur=1/2 meter=1` 等）で、**その短さは既に `Measure duration … is less than …`
> として診断済み**。**「満杯の小節の真ん中で折りたい」という要求は 899 冊に 1 件も無い。**
> ⚠️ **文字列の走査は嘘をつく**——`|` の無い自動補完境界を「途中」と数えて **270 件**と出た
> （真値 198）。**この種の問いは engine に訊くこと。**
> **⒟ 費用は「1 機能」ではなく前提の破壊**: 今日のエンジンは**小節をレイアウトの原子**として扱う
> （line breaker は小節を歩き・`lysc layout` は小節で報告し・パート間整列は小節で合わせ・
> 小節番号は小節を数える）。途中で折れると **1 小節が 2 段にまたがる**ので、間隔・小節番号・
> パート間整列・skyline がまとめて動く。**観測者 0 に対しては高すぎる。**
> ⇒ **Lily# の規則は「小節線は、行が折れてよい場所」**——**LP の `|` は表明で Lily# の `|` は境界**
> という既存の意図的乖離（`MeasureBuilder.HandleBarline` の remark）の、素直な延長。
> ★ **再検討の条件は 1 つだけ＝上の ⒤ が閉じたとき**。**無拍子の音楽には折る場所が存在しない**ので、
> **そのときはじめて「小節線でだけ折る」規則が*実際に*行き詰まる**。

> ## ✅ **⒦ `break` に小節線の機能を持たせる案＝【ユーザー決定 2026-08-29: 採らない】**（第280・**毒で実測**）
> 案は「`break` も小節を閉じる／ただし `| break` は小節線 1 本と読む」。**実装して 899 冊を掃いた。**
> **⒜ 版面の指示が音楽を変える**——`d'' e'' break f'' g''` を描かせると **4 小節が 5 小節になり、
> 2 段目の小節番号が 3 でなく 4 になり、折れ目に小節線が描かれる**。**LP は同じ入力で 4 小節・
> 番号 3・小節線なし**（上の ⒥ ⒜）。⇒ **「行をここで終える」と言っただけで曲の小節数が変わる**のは
> `|` が音楽の宣言・`break` が版面の指示という層の分離を壊す。
> **⒝ そして ⒤ と直接衝突する**（★ **ユーザーの指摘**）——**`time none` には小節線が 1 本も無い**ので、
> **`break` が小節線を引くなら、無拍子で折る唯一の方法が「引きたくない小節線を引くこと」**になる。
> **⒤ を将来サポートするなら、`break` は小節線であってはならない。**
> **⒞ 影響**: **追跡 573 冊は 0 冊**（毒だけが動かした本）、**作者の 326 冊は 7 冊**
> （`Real Gone`／`You Make Me Feel Brand New`／`銀河鉄道999`／`Can't Fight This Feeling`／
> `Disco Inferno`／`I Love You`／`クリスマスソング`）——**全部が「既に過少小節の警告が出ている箇所」**。
> ⇒ **得るものが無く、失うものがある。**
> ★ **「`| break` は小節線 1 本」という守りたい性質は、案を採らなければ今日すでに成立している**
> （`break` は小節線ではないので `| break` は `|` 1 本きり）。**案はその性質を危うくする側だった。**

> ## ✅ **⒱ 途中の `partial`＝【ユーザー決定 2026-09-08: 許す。パートごとに書く（`time` と同じ規則）】**（決定便で起票・**第352 第 4 便で実装＝`PartialScopeValidator` の緩和＋番人 9 本＋GRAMMAR・§1 第352 ⑵⒜**）
> **LP**: `\partial` は `Timing` context に送られる music（`ly/music-functions-init.ly:1697-1705` `context-spec-music … 'Timing`）で、途中では「現在の小節を dur 後に終える。新しい番号付き小節は作らない」（NR「Upbeats」）。**Timing は Score の別名なので時計は全譜で 1 つ＝1 パートに書けば全パートが動く**（両方に書いても同じ値を 2 度置くだけ）。
> **Lily#**: walker には途中の `partial` を読む腕が既に在り（`MeasureCollector.MusicWalk.cs:1702` → `builder.SetPartial`・`MeasureBuilder.SetPartial` は「現在の小節をその長さで閉じ、次から元の拍子」）、充足検査も小節内の `partial` を読む（`MeasureValidator.cs:343`）。**止めているのは `PartialScopeValidator` だけ**（section 直下以外を LYS error）。**共有の Timing は無く、途中の `time` はパートごとに書き直す規則**（`MeasureValidator.cs:281-286` が明記）＝**途中の `partial` も同じ規則で、書かないパートは `CrossPartMeasureValidator` の不一致になる**。
> **決定**: ⒜ 許す・パートごとに書く（validator の緩和＋番人＋GRAMMAR §8.1 の `MidMusicCommand` に規則を書く）。⒝ score 級 Timing（小節番号で束ねた timeline を全 voice の builder に配り `time` も書き一度に）は「900 冊が `time` の書き直しで困っていない」ので需要が出てから。⚠️ **T6 の census には使わない**（理由は §2 T6 の同項）。⚠️ **`PartialScopeValidator` の doc（「section にしか属さない」の理屈）と `MeasureValidator.cs:684` の案内文（「section directive として書け」）は同時に書き換える**——嘘の案内を残さない（§5.2.1）。

> ## ✅ **⒣ `removeEmpty`・`pedal` も「版面ものが part ヘッダに居る」同族＝別便で検討**（第217 起票・**ユーザー指示「別便で検討して」**・✅ **【ユーザー決定 2026-09-08: ⒝ removeEmpty だけ score 側へ・pedal は part のまま】・第352 第 5 便で実装＝`staff m as lines 1 removeEmpty all`（§1 第352 ⑵⒠）**）
> `lines` を score 側 `as lines N` へ移した決定（§3 第217）の同族が 2 つ残る: **removeEmpty**
> （LP では RemoveEmptyStaves＝context mod）と **pedal**（描画スタイル＝presentation）。
> どちらも part 持ちだと「総譜では隠す・パート譜では隠さない」等の score ごとの使い分けが綴れない。
> 移すなら **`as` 修飾の複数連結**（`staff m as lines 1 as removeEmpty …` か 1 つの `as` に列挙か）の
> 設計から——**判断だけで閉じる型ではなく設計資産が要る**（第215 骨 1 の区別）。着手はユーザー決定から。
> ⇒ ✅ **ユーザー決定 2026-09-08＝⒝ `removeEmpty` は score 項目へ・`pedal` は part のまま。綴りは `as` 1 つに列挙＝`staff m as lines 1 removeEmpty all`**（`as` の反復ではない・ossia にも許す＝`lines` と同じ・2 つの綴りは持たない RULES §5.2.1⑤）。**根拠**: LP の `\RemoveEmptyStaves` は `\with { \override VerticalAxisGroup.remove-empty = ##t }` の context mod（`ly/context-mods-init.ly:52-64`・`RemoveAllEmptyStaves` は `remove-first` を足す）で、**書ける場所は score の `\layout { \context { \Staff … } }` か譜の `\with` だけ＝音楽の属性ではない**。動作は段ごとの hara-kiri（`keepAliveInterfaces` の grob＝符頭・タブ数字・歌詞・和音名・強弱・数字付き低音・フレット図・percent・stanza が 1 つも無い段で譜が消える・休符／clef／key だけでは生きない・`remove-first` 偽なら最初の段は全譜）。典型は総譜で休む楽器を落とし、パート譜では落とさない＝`lines` を移した論法がそのまま当たる。**`pedal` は `pedalSustainStyle` という context property で `\set` により音楽にも書ける**＝家風で score ごとに変える需要が無い。**Lily# 側**: `HaraKiri.cs` が同じ規則を移植済み（値は `Staff.RemoveEmpty`）なので動くのは綴りと配線だけ（`RenderSpecParser`・`StaffRender`・LSP 補完・TextMate・GRAMMAR §3／§7・`DocKeywordListTests`）。**射程**: 追跡 `test/hara-kiri.lys`・`audit/lpreg/harakiri-percent{,-ctrl}.lys`・`audit/lp-regression/lys/hara-kiri-percent-repeat.lys`・`hairpin-spanbar.lys`／ユーザー実コーパス 0 冊。part ヘッダの `removeEmpty` は既存の unknown-property 網が拒む（`lines` と同じ・新 code 0 の見込み）。

> ## ▶ **⒨ `lysc svg --combined` は構文エラーを持つ本で例外を投げて出力を作らない**（第298 起票・**掃きの副産物**）
> **`Index was out of range. Must be non-negative and less than or equal to the size of the
> collection. (Parameter 'startIndex')`**——**エラー行を全部出したあと、既定モードなら
> 「written anyway, from the part of the file that parsed」で出す版面を、`--combined` は出さない。**
> ★ **この repo は recover を設計として持っている**（§5・第173第8便で `lysc` は best-effort に
> なった）ので、**`--combined` だけが例外で落ちるのは、そのモードが recover を通っていないということ。**
> ★ **射程（第298 実測・ディスク 1713 冊）**: **2 冊**——`scratch\p216\pins\chords-attach.lys`・
> `scratch\p216\pins\chords-row.lys`（**どちらも退役した `a:m` 記法を残した古いプローブ**）。
> ⚠️ **base と head で同じ**＝第298 の欠陥ではない。**掃きの `no-output 2` の正体はこれ。**
> ⚠️ **急ぐ理由は無い**（当たるのは壊れた本だけ）。**だが `--combined` を掃きに使うなら、
> 「no-output は本が壊れている印」だと知っていないと、レンダー失敗を数え違える**（RULES §5.0）。

> ## ✅ **⒢ ペダル・強弱 vs 歌詞の優先順位スタック＝⒜⒝ とも第220 で閉じた**（起票第215・再測第216。`abdeab0f`→`344a3a5e`→`85bbff88` の 3 commit）
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
> **残債（この島の続き）**: ⑴ ✅ **PLT＝text スタイルは同便の続きで閉じた**（`82c72f64`・語ごと 1 スパナ・
> `LyricClearance` の pedal 免除は**残す**＝LP も「ペダルは譜側・歌詞が下がる」）／
> ⑵ ✅ **dynamic の +0.024651 は同便の続きで閉じた**（音節プロファイル＝字ごと実輪郭＋LyricText 自前の
> skyline-horizontal-padding 0.1。箱 +0.0247／素輪郭 −0.0313／pad 輪郭 **−0.0003** の三角測量が機構の証明。
> 11 snapshot 再承認・射程 20 冊＝全て歌詞持ち）。⚠️ **代わりに 2 つ開いた**: ⓐ LYRBV の内側 gap が
> +0.0018→**−0.0066**（台帳 OPEN・どの項かは dynamic 点と同じ pointwise dump で切る）／
> ⓑ **打鍵 alloc**: perf-lyrplain1k 71.7→**367.9 MB**（素の輪郭化は 1235——バッチ化 508・resolved 直 merge 46/pass・
> colinear 連結 368 まで返済、連結は sweep 0/569 で無損失を機械確認。バッチ化だけなら旧箱より速い 55.2）。
> **残り 5 倍は ▶ perf 島の債**: walk 99 MB/pass・build 46×2・残 ~130 未帰属。梃子候補=行 profile の
> per-system memo／ShiftedRaised 1-alloc 化。ユーザー実本規模（〜50 音節）では +2〜3 MB/打鍵。／
> ⑶ ✅ **系をまたぐ括弧 × 増分は同便で毒→修理**（`b1607c1c` 後続 commit）: 毒は実在した——Off を消す編集で
> On 側 system の cache が死んだ括弧の ink を保持（増分ページ高 793.7 vs full 782.8）。修理は Volta と同形＝
> **検出済みスパンを両 overload の鍵に BucketSpan**（印の純関数なので再導出）。毒はテストとして常設
> （`DeletingAPedalRelease_RedrawsTheSystemsTheBracketSpanned`）・full render は 0/569 不動。

> ## ✅ **`with lyrics`/`with chords` の除去＝「score は帯の縦列」は第216 で完成した**（`b30d9bce`→`0baf2dcc` の 4 commit。**起票・決定は第215**）
> **残る作業 4 つを全部閉じた**: ⑴ bound 行は譜の直後で fold（byte 恒等を機械証明してから構文を除去）
> ⑵ グループ本体が `lyrics` 行を取る（LYS6011/6012）⑶ ハラキリはピンで固定（fold の帰結として既に正しい）
> ⑷ 無名 `chords {}` は LYS0032（ユーザー決定＝畳む）。**除去は LYS0031**・LYS6009/6010 退役。
> **chords 行は regime で家が割れる**（先頭行＝loose-chain 移植のまま／譜間＝attached engraver へ fold）
> ——理由と実測は §1 第216 の骨 2。⚠️ **第260 で「譜間」側の*置き方*は帯から run の要素に変わった**（fold そのものは不変・`AttachedChordLineInRun`）。**snapshot は chords 3 枚のみ・lyrics 全数不変・台帳不動。**

> ## ✅ **「同名のシステムフォントが同梱フェイスを隠す」は第214 で閉じた**（`05f59d45`。**起票は第213**）
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
> 「*印刷された*コピーの番号」**なので**両方向に外れていた**（`a2da9275`）:
> **phrase を 2 回鳴らす本 12 冊を理由なく除外し、`|: :|` と percent/tremolo の 22 冊を
> 比較して 2 周目まるごとを差として報告していた。**
> **今の述語は「ページが N 個の頭を彫った位置で MIDI が N 回より多く鳴っていないか」。**
> ⚠️ **見えない所は数えて印字してある**（黙って落とさない）: **grace はページ側にソース位置が無い**
> （566 冊で 1,459 位置・31 冊）／**タイの 2 つ目は XML に書かれ MIDI では併合**／
> **part-combine のユニゾンは 1 つしか彫られない**（`midiOnly` 9 冊はこれ）。
> ⚠️ **CSV は初弾も持つ**（`pitchSample` / `xmlSample`）——**報告は見出しごとに 25 行しか刷らない**ので、
> **26 位以下の族を読むには CSV のほうを見る**（第196 はこれが無くて小 listfile を何度も回した）。
> ⚠️⚠️ ★★★ **比べているのは*鳴る*音高で、書かれた音高ではない**（第197 で直した・`ddf75df7`）。
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

- ✅ **⒜ 移調 clef の実音オクターブを MusicXML が持たなかった**（2026-08-17・第195 起票／ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅ **⒝ `repeat unfold N` は「N 回鳴らす」＝各コピーは同じ音**（2026-08-17・第195 起票／ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅ **⒞ 2 つ目以降の score が MIDI・MusicXML・双子に出なかった**（2026-08-17・第195 起票／ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅ **⒟ 小節の途中の `clef` は相対枠を付け替える（ページが規則）**（2026-08-17・第195 起票／ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅ **⒠ 第195 が残した「未分類 25 冊」は第196 で分類し、決定の要らない 4 族はすべて閉じた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅ **⒡ 2 冊の fixture が、書いた人の意図しない音楽を彫っていた**（2026-08-17・第196 実測／ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅ **⒢ 素の section には part の lane が 1 つも掛かっていなかった** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- ✅ **⒣ 拍子欄を誰も描かない本が、*中途の*拍子変更の欄を予約していた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- ✅ **`font "NAME"` を指定すると、予約と描画が別の face になる**（2026-07-27 起票／ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ★★ **`lysc ly`（双子 exporter）の穴**。**塞ぐたびに LP と突き合わせられる本が増える**ので、
  忠実度作業の**測定可能面積そのもの**が懸かっている。
  ~~⑴ `voice { }`~~・~~⑵ `grandStaff` の入れ子~~・~~⑶ `ossia`／`part` 宣言なし~~・
  ~~⑷ section のヘッダ~~・~~⑸ 和音のオクターブ記号~~・~~⑹ grace のあとの音価~~ — **すべて完了**
  （第61〜63セッション。最後の 2 つは `c5fc8078`）。
  ~~⑻ `@stemUp`/`@stemDown` を落とす~~ — **完了**（2026-08-03・engine 側と同じ commit で。
  `\once \override Stem.direction`。理由は §1 ⑥）。
  ✅✅ ★★★ **この一覧に開いている項は 0 になった**（2026-08-17・第194 で実測・**⑺ は既に閉じていた**）。
  ⚠️⚠️ ★★★ **そして第197 で 1 つ増え、同じ便で閉じた**（`a7259e8c`・§2F ⒢ 末尾）:
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
  ~~⑼ 入れ子の中の phrase 参照が空になる~~ — **第194 で閉じた**（`4b58d864`）。
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
  ⇒ **`LyClefName` 1 軒に畳んで常に引用**（第176第2便 `03b78fec`）。
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
  （`715d7408`＋`edcc61a5`＋`c478cf6d`＋`757df45c`・§1）。**網は 3 ファイルの Theory** になり、
  **正典 58/0・TUTORIAL 13/0・LLM 版 22/0**。**毒は両方の新ファイルに入れて赤を見た**。
  ⚠️ ★ **`GRAMMAR.md` も第5便で入った**（`6dcc3832`）——**抽出は 2 形**（plain / `lilysharp` /
  `lys` の fence ＋ `(* Example…: … *)` の全文）。**10 例中 3 例が落ちて全部直った**
  （うち 1 つは **`ScoreDecl` の production 自身**・§1 ⑸）。
  ★ **除外集合の値段は測ってある**（`FragmentCodes` の remarks に記録）: 除外を外すと
  **LLM 版 6 本・正典 8 本・TUTORIAL 0 本**が落ち、**14 本とも正当な抜粋**。
  ⚠️⚠️ ★★★ **残っているのは `(* … *)` と箇条書きの中の「一覧」**（注釈の族の一覧など）。
  **第175 が直した `@segno` の行はそこに居た**——**Example ブロックではないので、
  新しい抽出器でも捕まらない**（第175第4便が「まさに例だった」と書いたのは誤り・第5便が訂正）。
  ★★★ **ただし第6便で*測って*ある**（`ebb7dbab`・§1 ⑹）。**4 冊が書く `@` 綴りを書かれたとおりに
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
- ✅✅ ★★★ **cue の島は第178 で閉じた（2026-08-15・実装 3 便）。隣り合う cue は 2 声部になった。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ~~**fixture が今の文法で parse しない**~~ — **第182 で閉じた**（`d49814a2`・ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ★ **音高付き休符 `a4@rest` は第179 で入った**（LP の `a4\rest`・**綴りはユーザー決定**）。
  **これで skip だった `rest-pitched-beam.ly` がコーパスに入り**、
  **`rest-avoid-note.ly` の両側置換も撤回できた**（§1）。
  ✅✅ ★★★ **残っていた穴は第194 で閉じた**（`0e8b94f5`）——**ただし起票は*小さい半分*を名指していた。**
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
- ✅✅ ★★★ **chords 行 / lyrics 行の検証は第180 で閉じた**（`290199bf`・ユーザー決定）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **名前を取る render 項で「誰も見ていないもの」は第181 で 0 になった** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **`using "file.lys"` が読めないファイルを名指す件は第182 で閉じた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **top-level でない `using` は第183 で閉じた**（`9ef5300b`・**LYS0029・ユーザー決定＝error**）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **「Guards X」と名乗る fixture の監査は第190 で閉じた（16 冊測って未測定 0）**（起票は第183）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **`octave absolute` の trailing octave 記号は第184 で閉じた——ただし起票の半分は → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **「置けないトークンが黙って消える」は第185 で閉じた——器は 3 つではなく 6 つで、 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅ **⑺ 記号の*後ろ*に書いた post-event が木では*前*に出る＝閉じた**（起票 第185 → 第186 が測り直し → **2026-08-30 に `ParsePostEvents` が「書かれた順のまま置く」ようになって解消**）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **`lyrics` のハイフンが隣の空白を落とす件は第186 で閉じた**（`3b672d88`）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **新規＝section を名指さない form が 0 バイトの絵を出していた件は第187 で閉じた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ⚠️ ★★ **`_ "text"` の空白**（第185 起票・**第187 で前提を測り直した・ユーザー決定＝後回し**・✅ **【ユーザー決定 2026-09-08: ⒜ GLUED 維持・診断文面だけ直す】**＝`_` が宣言済み section でないとき「密着させると custom text」と名指す・**第352 第 4 便で入れた＝閉じた**）。 → **本文は `HANDOFF-ARCHIVE.md`「閉じた §2 の本文」の同じ見出し**（2026-09-08 に落とした）
- ✅✅ ★★★ **repeat の無い volta ending は第188 で閉じた——「黙って消える」ではなく → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **「誰も出さない診断コード」は第187 で 7 件とも引退した**（`029986ef`＋`8f55ec08`・ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **`lysc check --pitches` が多 part の本で描画と違う音高を言う件は第182 で閉じた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **「自分の名乗る機構を 1 ピクセルも観測していない fixture」は第183 で 2 冊とも閉じた** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **score 単位の `transpose` が 3 通りの答えを返す件は第182 で閉じた**（`077e5c98`）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅✅ ★★★ **`lysc ly` が `transpose` を 3 綴りとも落としていた件は第194 で閉じた**（`087d1e53`）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
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

- **G-pdf. ⚠️⚠️ 起票（2026-09-01・第317）＝PDF の font resolver はプロセスに 1 つしかなく、2 文書が互いの face を上書きする**

  ★★★ **第317 が*競合*のほうは閉じた**（`0e45222f`＝`EmmentalerFontResolver._textFaces` は不変スナップショットを丸ごと差し替える。**素の `Dictionary` を `Clear()` して詰め直していたので、2 文書同時で `Operations that change non-concurrent collections must have exclusive access` が出ていた**）。**残るのは*設計*のほう**:
  **`PdfDocumentContext.EnsureFontResolver` はプロセスに 1 つだけ resolver を据える**（**PdfSharpCore の `GlobalFontSettings.FontResolver` は 1 回しか設定できない**）が、
  **`SetTextFonts` はその 1 つを*文書ごとに*書き換える**。⇒ **違う `fonts { }` を持つ 2 文書を同時に作ると、後から書いたほうの face で両方が埋め込まれうる。**
  ⚠️ **これは第317 が作った穴ではない**——**`EnsureFontResolver` の remark が前から「一 shot の CLI では無害・long-lived host（LSP など）では latent」と書いている**。**第317 はその文の*半分*（地図の破壊）だけを閉じ、もう半分（どの文書の地図か）は開けたまま残した。**
  ★ **閉じ方は「face を文書ごとに鍵付ける」**——`ResolveTypeface` が呼ばれた文脈から文書を引けないので、**face 名そのものに文書を混ぜる**のが素直（`LysEmbed:…#` が既に名前に情報を載せている形）。
  ⚠️ **観測者はまだ居ない**: **CLI は 1 プロセス 1 文書**で、**suite は同時に PDF を作るが `fonts { }` は同じ**。**LSP / プレビューが 2 つの本を同時に PDF にした日に出る。**

- ✅✅ ★★★★ **【閉じた・2026-09-01・第313】この hang は*起動のしかた*で、回避策は 1 行。正典 2.26.0 は 15 秒で完走する。** → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- ⚠️⚠️ ★★★ **【以下は第308 までの記録。原因は上のとおり起動方法だった】この機械の正典 LilyPond 2.26.0 は起動しきらない。第308 で容疑者を 3 つ潰したが、原因は未特定**（2026-08-31・第308。**第302 の 13 分に続く 2 例目で、今回は 17 分**）。

  ★ **署名**（`Get-Process lilypond` で読める）: **WS 28 MB のまま・CPU は 17 分で 1.4 秒**
  ＝**計算していない。何かを*待って*いる。** **出力は 1 バイトも書かれない**（`scratch/p308/canon/`）。
  ⚠️ **第302 の記録「WS 28 MB＝起動しきっていない」と*同じ数***——**持病であって、その日の事故ではない。**

  ★★ **潰した容疑者 3 つ**（ユーザー依頼で 2 つ、確認で 1 つ）:
  - **⑴ mark-of-the-web**: `C:\bin\lilypond-2.26.0` 配下 **1223 ファイル中 1203 個がブロックされていた**。
    **`Unblock-File` で 0 個にした**（**これは*やる価値があった*——他の症状には効きうる**）が、**hang は変わらず。**
  - **⑵ Defender のパス走査**: **`Add-MpPreference -ExclusionPath` を昇格して追加**（終了コード 0）。**変わらず。**
    ⚠️ **この pwsh セッションは非管理者**なので、**除外の追加は `Start-Process -Verb RunAs` 経由**（UAC が出る）。
  - **⑶ Smart App Control**: **既にオフ**（`HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy` の
    `VerifiedAndReputablePolicyState = 0`。**2026-08-29 に切った記録どおり**）＝**犯人ではない。**
  ⚠️ **Defender 自体はリアルタイム ON・`MAPSReporting = 2`・`SubmitSamplesConsent = 1`** のまま。

  ⇒ ~~★★★ **だから「正典で取り直す」は*計画に入れない*。**~~ ⚠️ **取り消し（第313）＝上の 1 行で 15 秒。**
  ★★ **第308 が示した代替（WSL の 2.27.3 で測って正典実測と 1 度突き合わせる）は*機械から消えた*が、
  規則自体は生きている**（→ RULES §5.2）——**別版で測るなら、その量について 1 度突き合わせること。**

- ✅ **第332 が直した**（4 本とも letter ではなく主題で書き直し＝**⒪＝第140「prelim の spacing は第 2 声部のタイ・スラーを見ていない」・⒪′＝第141／142 の carry＋augment memo**・`HANDOFF-ARCHIVE §1 第140／第141・第142` を名指し）。**下は経緯**: ⚠️ ★★ **コード内の `HANDOFF §1 ⒪` 参照 4 本は宛先が無い**（2026-08-31・第306 に発見・第332 で修理）。**`§1` は毎便*書き換える*節なので、そこの letter を指す参照は書いた次の便から宙に浮く**（`IncrementalCompilerTests.cs:771` `⒪′` ／ `LpGeometryProbes.cs:10403` `:10501` `:14091`）。⚠️ **`▶ ⒯` `▶ ⒭` の族とは*別の壊れ方***——**あちらは letter が*再利用*されて「今日の別の項目」に当たる**（第306 が 14 本まとめて retired と明記した）が、**こちらは当たる先が*存在しない*。** ⇒ **直すには ⒪ が何だったかを `HANDOFF-ARCHIVE.md` で特定する必要があり、本便はそこまでやっていない。**★ **原則としては同じ**: **コードから引くのは*主題*であって letter ではない**（RULES §5.1）。
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
> （`105c863e` ＋ `4663609d`。経緯は §1 第199、汎化した学びは RULES §5.1）。
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
> ⑴ **誰も選んでいない**——本体は**移植の規律より前**の一括 commit（`dc363123`・2026-02-24）で
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

> **§2G の債務は 2026-07-27 に一掃した**（`61ec3d49`／`64288a7b`／`23ecf5ba`／`de714c33`）。
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

- ✅ **`DrawingTransform.Identity` は第210 で閉じた**（`6cbe39d9`）。**`new()` が record struct の → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
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
が **1.604200 で exact**。<!-- ledger: compressed.note-to-note.quarter = 0 -->
`SeparatingPaddingTests` は LP 由来の期待値に書き直し済みで、
「`MinItemGap` を何に設定しても音符間が動かない」ことを主張するテストを追加＝**戻ってこない**。

- ✅ **歌詞の列間隔の発明 2 つ＝第222 で移植完了**（`e8e854b3` 起票 → `771dc57a` 移植）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）

- 「Lily# の最小は **0.2 広い**」→ 圧縮域では **0.2521 狭く見えた**（加線の混入）。実際は
  rod で **+0.1** ちょうど。**加線のない音高で測ること**
- 「snapshot 24 枚が動くのに台帳は 1 点も動かない」→ **鍵になる点が無かっただけ**。
  圧縮 regime の点（`compressed.note-to-note.quarter`）を開いたら正当化できた。
  ⇒ **鍵が無いのは「移植できない」ではなく「まだ測っていない」**

**残っている発明**:

- ✅ **歌詞の小節線またぎ＝barline-split モデル＝第223 で移植完了**（`3a635a6d`・ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
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
  **小節線からの間隔は 1 つの数ではない**＝grob ごとの `space-alist`）。
  ✅ ★★★ **この −0.2 は閉じた（2026-08-03・ユーザー承認）**。<!-- ledger: courtesy.meter.barline-to-cancellation = 0 -->
  <!-- ledger: courtesy.meter.barline-to-meter = 0 -->
  `SpacingRules.BarlineToCourtesyKey` は **1.0**（`define-grobs.scm:296`/`:297` は
  key-signature と key-cancellation の**両方**に `extra-space . 1.0` を宣言しているので、
  **courtesy 群が取消で開いても新調号で開いても 1 つの定数で正しい**）。
  ⚠️ **下の警告は無視ではなく*尊重*して閉じた**——「予約 `KeyCourtesySuffixWidth` が同じ定数を読む」
  はまさに**安全な理由**だった（**定数は 1 つで、描画も予約もそれを読む**ので一緒に動く）。
  ⇒ ★★ **「2 か所が同じ定数を読む」は危険の印ではなく*安全*の印**——危険なのは**2 か所が同じ量を
  別々に綴っている**とき（§2 A）。**着手前にどちらかを見分けること。**
  ⚠️ **以下の ⑷ の残りは*別の乖離*で、今も開いている**（第131 起票・点は 1 つも無い）。
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
  ✅ ★★★ **その +0.850449 は閉じた（2026-08-02・2 段の移植）**。<!-- ledger: grace.column.approach = 0 -->
  <!-- ledger: grace.column.approach.main-control = 0 -->
  `SpacingRules.SpringIntoGraceRun` が **先に縮めてから run を足す**（`Spring.Scale`＝
  `Spring::operator*=` なので **ideal を rod の下へ押し込まない**）。⚠️ **移植は*両方*のばね系に
  要った**——片方だけ直すと同じ量の 2 綴りになる。
  ⇒ ★★ **対照 `grace.column.approach.main-control` は当時も今も exact**＝**普通の音符間は無罪**で、
  **発散側だけが動いた**＝**恒等の対が「修理が形の項に効いた」ことを言っている**（§5.0）。
  ⚠️ **⑴ の*モデル*の話（独立列を持たない）は残っている**——**閉じたのは点であって列ではない。**
- ~~**中心合わせされた 2 つの text grob**~~ — **両方とも片付いた**（和音記号 `7e7fe5cb`・
  音節 `df8fb3e4`）。⚠️ ただし `ChordNameEngraver` の `Math.Max(2.0, …)` 幅の床は**残っている**
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
- ★★ **⑵ 残っている「同じ量の 2 つ目の綴り」**——§2A の既存項を指す（詳細はそちら）:
  符尾長の 3 綴り（cue がどれにも属さない）・タイ列の greedy（`Ties_configuration` 丸ごと
  採点への置換）。
  ⚠️ **~~符尾 attachment X の黒玉固定~~ はここから外した（2026-08-23 裏取り）**——**2026-08-03 に
  閉じている**（§2E）。**この行が「▶ 先頭」と書き続けていたことが、第234 の triage を
  丸ごと誤誘導した**（§1 参照）。
- ✅ **⑶ record モデルの同値性（identity の欠如）**（**2026-08-31・第307 に閉じた**。**方針と判断軸は第306**）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ★ **⑷ collect 相と layout 相の二重解決**——§2A の既存項
  「多声の譜が `VoiceCollector.Collect` と `NoteCollision` を 2 周する」を指す（詳細・実測
  +0.3%・畳み方 2 案はそちら）。⚠️ **着手前にコスト判断**（§2A に明記済み）。

### D. 文法の変更候補（効率の観点・**3 点とも要ユーザー判断＝勝手に実装しない**）

> ユーザー問「効率的な処理のために文法を変えるべき所はあるか」への答えの台帳化。
> 文法変更は言語設計＝ユーザーの決定事項。ここには**提案と根拠**だけを置く。

- ✅ **⑴ オクターブアンカー（絶対指定）構文は*既に在る*＝`octave absolute` / `octave N`**（2026-08-31・第306 に確認。**`GRAMMAR.md:75-82` と `OctaveDecl`**——**top-level・part ヘッダ・*楽中*のどこにでも書け、`octave 2` で絶対基準を貼り直せる**。**追跡 fixture でもユーザーの本でも実際に使われている**）。 → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
- ✅ **⑵ file 既定と楽中変更の構文的区別 = 第125セッションで landed**（ユーザー判断＝ → **本文は HANDOFF-ARCHIVE.md「閉じた §2 の本文」の同じ見出し**（第351 が落とした）
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
| ★★ **section 境界で割れた小節は 1 小節＝前 section の短い末尾と、form がその次に鳴らす全 section の短い先頭が足して拍子になるとき、LYS2001／LYS2006 を出さない**（2026-09-09・第356 第 2 便・**ユーザー報告「繰り返し記号が小節の途中にある場合に LYS2006 が不正に出る」**・✅ 同便実装＝`Semantics/SectionBoundaryBars.cs`） | Disco Inferno: A が半小節 `a,, a,, \| break` で終わり、volta の各 ending が残り半分 `bes,,8 c, c, f,~ \|` で始まる＝反復記号が小節の途中。LP は section を持たないので `\repeat` body から `\alternative` へ続く小節は 1 小節で bar check も通る。Lily# の section は小節線で始まるので同じ小節が 2 つの短い bar として検査に届いていた。判定は **form の再生順**（`FormWalk`・繰り返しは body→その turn の ending・回数は `:|*N`＞ending 数＞2）で隣を全部数え、**同じ part の cell** を `MeasureModel.Split` で測る。**隣の 1 つでも補完しなければ両方の警告は残る**（割れた小節の免除であって短い小節一般の免除ではない）。**第 3 便（ユーザー「小節番号も LP に合わせて、その割れた小節を 1 つに数えて」）でページも 1 小節に数えた**: 補完する先頭 measure に `ContinuesBar`（番号を進めない・段頭に番号なし・作者の小節線は描く）。volta の 2 本目以降の ending は body の末尾に対して読み（`ContinuedFromMeasure`）、短い bar でも段頭番号でもないが**番号は新しく取る**＝LP 2.26.0 `alternativeRestores` は measurePosition を戻して currentBarNumber は戻さない（`alternativeNumberingStyle` 既定「番号は alternative を通して続く」）。Disco Inferno 67 → 66 bar。 |
| ★★★ **小節途中の `break`／`pageBreak` はその小節を割って折る（LP の `\break`）。割れないときは次の小節線に落ちて LYS1037 が理由を名指す**（2026-09-09・第356・**ユーザー指示「安全に実装できるなら実装して」**・✅ 同便実装＝`MidBarBreakTable`・`Measure.BreaksMidBar/ContinuesBar`・§2F ⒥） | 旧決定「サポートしない」（第280）の再検討条件＝`time none` が彫れること、は第353 で成立。**LP 2.26.0 実測**（`scratch/p357/lp/mb1-8.ly`）: 折れ目に BarLine 無し・次段は clef だけで最初の音は小節線折りと同じ 5.8・**次段に BarNumber 無し**（小節線折りは "2" を刷る）・タイとスラーは跨ぐ・下譜の全音符は折られて残りが空で刷られる・梁は 2 片に割れる。**Lily# の「安全」の線**: 全 voice が同じ拍で item 境界を持つときだけ割る（表は score 全体で 1 つ・collector 2 回走り）。割れない小節は今までどおり小節線折り＋警告＝出力が黙って変わる形を作らない。**跨ぐ音符を割らないのは LILYSHARP-OWN**（LP は割る）・梁は LP が割るが Lily# の小節単位 beamer では再導出になるので拒否。**既存 922 冊は MOVED 1**（ユーザーの `Disco Inferno.lys`＝section 末に半小節で書かれた `break` が、次 section の音の後ではなく書かれた場所で折れる＝双子が LP で組む絵・§1 第356 ⑷。`e2 break |` 198 site は小節線折りのまま）。増分: 割る本は resume 不可（記録を ineligible に・常に full collect）＝ mid-bar break を持つ本だけが払う。 |
| ★★★ **`fonts { }` の entry はキー＋属性（引用符付き face／`as serif\|sans`／`step ±n`／`size n`／`bold` `italic` `regular`・順不同・次のキーで終わる）。転送は `chordName as sans`（裸の `chordName serif` は拒否・後方互換なし）。`step` が主・`size` は逃げ道（双子は step だけ書く）。書いたスタイルは彫版の既定を*置き換える*。**（2026-09-07 **ユーザー承認**・✅ 第354 実装＝§2F F-fonts） | 30 か所に散ったサイズ（`SharedRenderer.Marks` の印の em 2.4＝LP の 2.771822 より 13% 小さかった第342 の実測）を role ごとの 1 家に集める。`step` は LP の `font-size` そのもの（magstep 2^(n/6)）で既定が動いても意図が生き残り、双子に `\override Grob.font-size` として写せる。裸の家族語がキーと値の両方に出るのが唯一の曖昧さの種だったので `as` に。従う role は `TextRoles.PlanReachOf` の表・従わない role は LYS8018 警告（黙って無視されるのが一番悪い）。 |
| ★★★ **途中の `partial` は許す。パートごとに書く（途中の `time` と同じ規則）**（2026-09-08・**ユーザー決定**・✅ 第352 第 4 便で実装＝§2F ⒱） | LP の `\partial` は Timing（＝Score）に送られ 1 か所で全譜が動くが、Lily# は共有 Timing を持たず途中の `time` をパートごとに書き直す規則（`MeasureValidator.cs:281-286`）。walker と充足検査は既に途中の `partial` を読み、止めているのは `PartialScopeValidator` だけ。score 級 Timing（書き一度）は需要が出てから。 |
| ★★★ **T6 のピックアップ census は「弱起を前 section へ戻す」**（2026-09-08・**ユーザー決定**・コーパス側） | `.ly` の 1 小節 `r2. b4 \|` を section 境界で割った本は、`partial` をどう書いても小節が 1 つ多い（Lily# の section は小節線で始まる）。`.ly` と同じ絵になるのはこれだけ。form 反復で弱起が鳴らない代償は承知。`time` を挟む案は拍子記号まで刷るので退けた。**T6⒥ の break 21 冊は触らない**（`.lys` の break は意図）。 |
| ★★★ **`time none` は彫る**（2026-09-08・**ユーザー決定**・✅ 第353 で実装＝§2F ⒤・§1 第353） | 「LP が彫るなら Lily# も」。LP は `\cadenzaOn`（小節長に数えない・自動小節線／連桁／改行が止まる）で、双子は既に `time none` をそう書いている＝今日はページだけが嘘。移植点は `MeasureBuilder` の境界を作らない腕・自動連桁停止・改行は次の `time` まで無し・小節番号不動。観測者 0 なので fixture＋LP 双子の対から。 |
| ★★★ **`removeEmpty` は score 項目へ＝`staff m as lines 1 removeEmpty all`（`as` 1 つに列挙）。`pedal` は part のまま**（2026-09-08・**ユーザー決定**・✅ 第352 第 5 便で実装＝§2F ⒣） | LP の `\RemoveEmptyStaves` は context mod で score の `\layout`／`\with` にしか書けない＝`lines` を移した論法（総譜では隠す・パート譜では隠さない）がそのまま当たる。`pedalSustainStyle` は `\set` で音楽にも書ける property＝家風で score ごとに変える需要が無い。2 つの綴りは持たない。 |
| ★★ **展開の予算（50,000 site）は絵だけのもの。MIDI・MusicXML・ly は打ち切らない**（2026-09-08・**ユーザー決定**・残る仕事＝LYS1033 の文面・§2F ⒩） | 予算は行折り DP が死ぬ大きさに置いたページの描画能力の限界で、音楽の性質ではない。100 万音の MIDI は正しい出力。文面に「他の出力は打ち切らない」を足して食い違いを宣言済みにする。 |
| ★★ **歌詞の `~`（melisma）は Lily# の規則として残す＝LP の `\lyricsto`＋スラー／`\melisma` とは別の源**（2026-09-08・**ユーザー決定**・意図的乖離） | ページの束縛（`sings`）と melisma の LEFT 寄せは既に LP の移植。違うのは melisma の源（LP は音楽側・Lily# は歌詞側の `~`）だけ。対は双子側で `~` の音節の前に `\once \override LyricText.self-alignment-X = #LEFT` を書いて組む（§1 第351 ⒠⑴）。ページがスラーから melisma を導く案は音節の割り付けを変える言語変更なので採らない。 |
| ★ **`_ "text"` は GLUED のまま。診断文面だけ直す**（2026-09-08・**ユーザー決定**・✅ 第352 第 4 便で実装） | `_ "shown"` は section `_` への表示ラベル付き参照として生きており、空白を許すとその対が潰れる。`_` を予約語にする案は `@fig(_ 6 4)` の付け替えが要るので退けた。 |
| ★★★ **`lysc ly` は `chords` 行を `\new ChordNames \chordmode { … }` で出す＝案A。LP が自分の名前（Ignatzek）を刷り、Lily# の綴りは LP が受け付ける entry に書き直す**（2026-09-08・第350 第 5 便・**ユーザー決定「A で進めて。コードの表記は LP で通る形に書き直すように実装して」**・✅ 同便実装） | 第334 ⑵′ から並びに在った 2 択: **案A**＝`ChordStructure` → `\chordmode`（LP の和音名機構を通す・表示文字列は一般に一致しない）／**案B**＝Lily# の表示文字列を `\markup` で出し `chordNameFunction` を差し替える（字面一致・LP の機構は通らない）。**私の推薦は A**（双子は LP の解釈を照合する本で、LP 自身の名前が出るほうが差が見える）でユーザーが採った。**綴り表は `ChordQualityRegistry.LilyPondModifier`**（全 29 quality・`7sus4`→`:sus4.7`・`mmaj7`→`:m7+`・`m13`→`:m13^11`＝LP は minor 3rd では 11 を落とさない `chord-entry.scm:155-162`・`add9`→`:5.9`）。**LP 2.26.0 で全 quality を 1 冊で受け付け確認**（`scratch/p351/cm/qualities.lys`・exit 0・警告 0）。**inline `@chord` は対象外のまま**（音楽 stream に乗る印で、ChordNames stream を音価から組み直す別の読み手が要る）。 |
| ★★★ **曲頭の `\|:` は刷る**（2026-09-04・第328・**ユーザー決定**・✅ 同便実装＝`ScoreAssembler` の門を撤去・双子は `\set Score.printInitialRepeatBar = ##t` を常に書く） | 第319（T5）は LP の既定（`bar-engraver.cc:432-449`「曲頭では反復線を刷らない」）を移植していた。ユーザー「ユーザーが明示しているなら刷った方が良い」＝**Lily# の `\|:` は常に書き手が書いた印**（抑止すべき*自動*の反復線が無い）で、実コーパスはリードシート。LP 自身が `printInitialRepeatBar` でその慣習を持つので、双子に同じ語を書けば対は保たれる。台帳 `line-start.time-to-first-note.initial-repeat` は LP 5.84／Lily# 5.54＝OPEN −0.30 で開き、**同日の第 5 便で exact に閉じた**（行頭の `\|:` を break-align 表の `staff-bar` 列＝拍子の後に置き、最初の音のばねを小節線の `first-note` 1.3 で建てる。§2 T5 の ✅）。<!-- ledger: line-start.time-to-first-note.initial-repeat = 0 --> |
| ★★★ **`chords {}` に `s` は無い・小節頭の `.` は「和音の無い slot」**（2026-09-04・第328 第 7 便・**ユーザー決定**・✅ 同便実装） | ユーザーの問い「`\| \|` で空の小節が書けるのに `s` は冗長では」→ `s` が唯一担っていたのは「小節の 1 拍目に和音が無く途中から入る」`\| s C \|` で、それは小節頭の `.` を LYS2010（error）で断っていたから。**`r`＝N.C. は LP の `noChordSymbol` と同じ実需で残す**。⇒ `.` は前の和音を延ばす／小節頭では何も刷らない slot（時間は経過）＝`\| . C \|`。**LYS2010 は退役（番号は再利用しない）**・`s` は LYS1028 が置き換えの綴りを名指す。実装: `Parser.Sections.ParseChordBodyItem`（RestS を受けない）・`ChordNameCollector.ForEachSlotGroup`（HeadDot 記録を撤去）・`ChordRowGridValidator`・番人 `ChordSlotGridTests.ADotAtTheBarHead_IsTheSilentSlot`／`ChordBlockStrayTokenTests.ARetiredSpacer_*`／`ChordNamesTests.ChordRow_TheSpacerIsGone`。docs は GRAMMAR／GRAMMAR_FOR_LLM／GRAMMAR_AUDIT。lpreg `chord-names-rests.lys` は `s` の小節を `.` で写す。 |
| ★★★ **grace の手書きスラー `grace { g16( } a8)` は刷る＝appoggiatura と同じ弓**（2026-09-04・第328・**ユーザー決定**「LP では刷るんだよね。lily# でも刷るように直して」・✅ 同便実装） | LP では appoggiatura＝grace＋2 つの slur event（`ly/grace-init.ly startGraceSlur/stopGraceSlur`）なので手書きの対と同じ絵。**実装は「最後の grace 列の `(` と主音符の `)` の対だけを group の弓にする」**（`GraceNoteItem.ExplicitSlur`・walk の grace-time 門が捨てる前に読む・主音符の `)` は group が取る）。**それ以外の形（先頭の grace 列の `(`・rest の `(`・主音符より先の `)`）は今も LYS4020**＝通常の Slur engraver に grace を通す島（§2 U8 ⒝2）はそのまま。閉じない `(` は LYS4010「never closed」で絵は無し（LP の "unterminated slur" と同じ）。 |
| ★★★ **section 頭で音価を書かない音は 4 分（LP の引き継ぎは採らない）**（2026-09-04・第328・**ユーザー確認**「四分音符で良い」） | LP は直前の音価を引き継ぐが、Lily# の section は form でどの順にも参照される再利用単位なので「前の section」が一意でない。**LP 忠実以外に全音符（や引き継ぎ）にすべき理由は無い**。書き手が意図するなら書く（T6 ⒤ で `.ly` が 8 分の 3 箇所は明示した）。`Sugar` C の `fis ais dis,, cis'`（LP は `r1×4` を引き継いで全音符 4 小節・`.lys` は 4 分 4 つ）は第 8 便でユーザーが「全音符が正しい」と直した（§2 T6 ⒜）。 |
| ★★★ **和音行の記号は Score 級の mover（volta・mark・tempo）の支持に入る／Staff 級（text spanner・dynamic 等）の支持には入らない**（2026-09-04・第328・LP 実測＝`probes/volta-chord-row.ly`） | LP の `Mark_engraver`/`Volta_engraver`/`Metronome_mark_engraver` は `\Score`（`engraver-init.ly:764-768`）で、System の pass は ChordNames 行の skyline を支持に持つ（VCR: ブラケットは「Am」の上に 0.46、箱はブラケットの上に 0.46）。Staff 級の grob は Staff の axis group で先に置かれ、行はその*上*に来る（`test/chordrow-rit-second-system`＝`rit.` の上に行）。⇒ **種まき（tracker への merge）ではなく `PlaceVoltas`/`PlaceMusicMarks` だけへの「追加の支持」**（`OutsideStaffSkylines.Place(extraSupport:)`）。staffless（行が anchor）では入れない＝リードシートの「箱は行の線上」決定を守る。 |
| ★★★ **音符の後置印の語順は `核 \N @… ] ) ( [ ~`。エディタの smart key（`~ ( ) [ ] \ @`）が書く順であって、パーサは順不同のまま**（2026-09-03・第327・**ユーザー決定「良いね。これで進めて」**・✅ 同便実装 `f9a7f7c1`） | ユーザーの要件は「語順が前後しても正しく解釈するのはそのまま。なるべく同じ書式で入力できた方があとで検索しやすい」。**根拠 4 つ**: 音符から近い順＝印が及ぶ範囲の狭い順（`\N`・`@` はその音だけ／連桁は小節内／スラーは句／タイは隣の音）／同じ音符で終わる印を始まる印より先に（`d4)( e`＝時間順）／括弧は入れ子（スラー外・連桁内＝`c8([ d e f])`）／タイは結ぶ相手の直前＝最後（`c4)~ c`）。**LP 本家 regression 2237 本の集計**（`scratch/p327/lporder.ps1`）: タイ最後は一致（`(~` 5:1・`[~` 4:1・`)~` 3:0・`]~` 2:0）、`[(` 22:4 だけ本家と逆＝入れ子の読みやすさを優先。**ユーザー明示: 手元の `.lys` の語順は根拠にしない（「適当だ」）**。`@` 注釈同士の順は打った順（補完で入る）。**smart key の細則もユーザー指定**: `\` はカーソルを直後へ（数字を続ける意図）・既に `\N` なら N を選択・`@` はカーソル直後で補完再表示・数字と octave 記号は印の並びの中でも音符の上と読む。実装は `POST_EVENT_RANK` 1 か所（新しい印を足すときはそこに順位を与える）。 |
| ★★★ **実音入力の綴りは `pitch concert` / `pitch written` を `transpose` と同じ 3 つの家に置く**（2026-09-03・第326・**ユーザー決定**・✅ 同便実装＝`3423a277`＋part header の commit） | top-level＝ファイル既定（既定 `written`）／part header＝その part の上書き（own > file。「サックスは移調済みパート譜から、ピアノは実音譜から写す」を 1 冊で書くため）／score header＝その score を実音で刷る（指揮者譜）。**両方に書いたら part が勝つ**＝`transpose`・`octave`・`key`・`time` と同じ規則なので混乱しない、が採択理由。代案（`instrument alto-sax concert` の修飾語・part だけで既定無し）は提示のうえ退けた。**`instrument` を指定しない part では `pitch` は noop**（T=0）。**「楽器名は付けたまま書いた音高で鳴らす」切替は今は実装しない**（楽器名を書かなければよい）。✅ **オクターブ移調楽器（bass/piccolo/`transposition 8vb`/bass tuning）は concert でも記譜不動＝2026-09-03・第327 でユーザー確認済み**（`PartHeaderDefaults.ConcertShiftSemitones`＝`T % 12 == 0 → 0`、それ以外は T 全部＝tenor sax −14 は 14 半音動く）。**理由**: 機構だけ見れば例外だが、紙の慣習では規則そのもの——C スコアは clarinet/sax を実音で書き、piccolo・contrabass・guitar・bass はオクターブ記譜のまま置く（オクターブは読みやすさの約束で、指揮者が戻す「調」ではない）。`pitch concert` の用途は「実音の楽譜から写す」ことなので、写す元に bass がオクターブ記譜で載っている以上、そのまま写せる今の仕様が元と 1 対 1。逆にすると bass を写すたびに 1 オクターブ下げて打ち直し、加線だらけの入力になる。**帰結**: bass 主体のコーパスでは `pitch concert` は事実上 no-op で、効くのは sax/clarinet を持つ本だけ。 |
| ★★★ **section 名の箱は LP の RehearsalMark と同じ位置に立てる（拍子記号の列＝clef 右端 3.365／key があれば key 右端 6.385）。テンポはその下に縦積み。行頭配置は退役**（2026-09-02・第322・**ユーザー決定「F2 は選択肢 1 で」**・✅ 第324 が実装＝§2 T7 F2） | §2 T7 F2＝第257 から据え置きの「SectionLabel を RehearsalMark と同じ左端に立てる」問題。**LP**: 箱の左端は拍子記号の列、`♩=117` はその真下（`scratch/p321/fx/fx4-mark-tempo-lp-staff.png`）。**Lily#**: 箱は行頭（indent+0.3）、key があるとテンポが箱の右に横滑り（`-lys-staff.png`）。第220 の「マーク＋テンポの縦積みは LP 忠実のまま」と同じ方針で箱も揃える。台帳の意図的 OPEN 点（`Indent+0.3` 対 LP `0.0`・第257 第 4 幕）はこれで閉じる側へ。射程は `\mark` 281 冊・snapshot 多数（行頭の箱が右へ 3〜6 ss）。**横並びは表示オプション `marks beside` として後日**（下の行）。 |
| ★ **表示オプション `marks stacked` / `marks beside`（score レベル・既定 `stacked`＝LP・`beside`＝箱は行頭でテンポはその右）を足す。優先度は低め**（2026-09-02・第322・**ユーザー決定**・✅ **第355 で実装（2026-09-09）＝top-level 既定＋score 項目・`Semantics.MarkArrangement`・番人 `MarkArrangementTests`・§1 第355**） | ユーザーの問い「セクションマークをテンポと横並びに表示する文法は？」に私が提案し承認。置き場は `paper` / `fonts` と同じ 2 段（ファイル先頭＝既定・`score main { marks beside … }`＝その score）。**`override` にしない**（`SectionLabel.placement` は LP に無い発明で、override は `once`／section の範囲を読む構文＝score 全体の量を置くと黙って無視する形。staff-spacing 族を paper に置いた理由と同じ）／**`paper { }` にも入れない**（寸法の器・「配置アルゴリズムの切り替えは置かない」と宣言済み）／`tab bl as full` と同じ閉じた裸語彙 2 語。**F2 を LP に揃えてから足す**（逆順だと今の行頭配置が「既定の LP 逸脱」のまま残る）。触る文書: GRAMMAR・GRAMMAR_FOR_LLM・SYNTAX_REFERENCE・tmLanguage・補完・`DocKeywordListTests`・CHANGELOG。 |
| ★★★ **改行・改ページの語は LP の綴りからバックスラッシュを落としたもの＝`break` / `noBreak` / `pageBreak` / `noPageBreak`。`nobreak` は改名して退役**（2026-09-02・第320・**ユーザー決定**・✅ 同便） | 私は最初 `pagebreak` / `nopagebreak`（`nobreak` の小文字 1 語規約）で実装したが、ユーザーが「`break` / `noBreak` / `pageBreak` / `noPageBreak` の方が全体として一貫するのでは」と問い、私も賛成して改名した。**理由**: 複合語キーワードは `grandStaff` `staffGroup` `choirStaff` `condensedStaff` `combinedStaff` と LP の名前を camelCase のまま持っていて、小文字に潰していたのは `nobreak` だけ＝あれが例外側だった（`mezzosoprano` `tocoda` は LP 側が小文字）。**「LP の綴りからバックスラッシュを落とす」の 1 規則で言い切れ、`\noBreak` 専用のヒント行も要らなくなる。** 0.5.0 前で互換不要・`nobreak` を書く本は追跡 1 冊（`audit/lp-regression/lys/break.lys`）＋ scratch 16 冊で、全部書き換えた。**旧綴り `nobreak` に固有の診断は付けない**（ユーザー指示。普通の識別子＝未定義の phrase として落ちる）。**意味は LP の `pageBreak` そのもの＝page と line の両方を force**（`ly/music-functions-init.ly:1411-1418`）、`noPageBreak` は page だけ forbid（`:1255-1259`）。書ける場所は `break` と同じ（section の music・form・repeat block）。 |
| ★★★ **score 行の `sings` は*その行の*束縛＝1 つの lyrics track を複数のメロディに置ける**（2026-09-02・第320・**ユーザー決定**・✅ **同便で実装**） | ユーザー: 「歌詞を複数のメロディに対して利用できるようにして。今は L34, L36, L38 でエラーになってしまう」（`scratch/site-showcase/ode-to-joy.lys`＝四声コラールで 1 つの `verse` を `lyrics verse sings sop / alt / ten / bas` と 4 つの譜の下に置く本。**LYS7005 ×3 ＋ LYS6012 ×3**）。⇒ ★★ **これは下の 2026-08-19・第218「score の行でも `sings` を綴れる＝*同じ 1 つの track 属性*」を*置き換える***: **定義の `sings` は track の*既定*メロディ（定義ブロック同士の食い違いは LYS7005 のまま）／score 行の `sings` は*その行の置き場所*の束縛で、既定を上書きする（行同士は衝突しない・`sings` の無い行は既定を取る）**。★ **読み手は 1 軒**（`LyricBindings.TargetOfRow`＝fold・LYS6012・collector の 3 人がこれを訊く）。**`LyricsRowSpec` が `Sings` を運ぶ**。★ **LP に対応物あり**: `\lyricsto "alt"` は *placement* ごとに voice を名指す——Lily# の綴りがそれに収束した形。⚠️ **意図的に採らなかった読み**: 「定義に `sings` が無いとき、最初の行の `sings` が track の既定になる」——**行の束縛は行のもの**で、他の score の行にまで効かせると 08-19 の曖昧さが戻る。**既存 fixture・snapshot 236 枚は 1 枚も動かず**、追跡 587 冊で「行に `sings`・定義に無し」は `test/sings-chorus-row` 族の 2 冊だけ（どちらも行 1 本なので読みは同じ）。番人: `SingsLyricsTests` 4 本 ＋ snapshot `test/sings-per-row`。 |
| ★★★ **終端されない span は*エラー*にする**（2026-08-31・第306・**ユーザー決定**・✅ **同便で実装＝`LYS4018` の `Unterminated` だけ error**） | **問い**（ユーザー）: **「`@!rit` を書かない場合、自動で同じ小節の最後で終端する方が便利か」。** ⇒ ★★★ **答えは「いいえ」で、理由は 3 つ**: ⑴ **rit. は普通フレーズ末の 2〜4 小節に渡るので、小節末終端は*もっともらしく間違った長さ*を黙って描く**（第306 が 1 日かけて潰した ⒯ ⒰ と同じ形）／⑵ **警告が消えるので書き落としに気づけなくなる**／⑶ **LilyPond も `\stopTextSpan` 必須**なので明示終端は移植であって Lily# の押し付けではない。 ⇒ ★★ **代わりに直したのは*強さ*のほう**——**`GRAMMAR.md` は終端を「REQUIRED」と書いているのに診断は warning で、文法と診断が同じ文について食い違っていた**。**どちらも何も描かないので、`@!rit` を落とした本は `check` を通って rit. が消えたまま出荷される。** ★ **1 コード 2 severity で、分け方は文法のもの**: **`Unterminated` は両族（text spanner と ottava——**文法はどちらも REQUIRED と書いている**）で error、**`@!` が何も閉じない**と**開いている span の中で 2 つ目を開く**は*別の誤り*で文法が言及していないので warning のまま。 ★ **射程は実測ゼロ**（1977 冊で `check` が動いたのは 24 冊＝全部 scratch のプローブ・**24 冊とも差は `warning:` → `error:` の 1 語だけ**を行ごとに検算）。 |
| ★★★ **`form` が「宣言がヘッダだけ」の section を鳴らすのは*誤り*＝拒否する**（2026-08-31・第306・**ユーザー決定**・✅ **同便で実装＝`LYS1036`**） | **問い**（私）: **`section A { key g major }` だけが A の宣言で、どの part も A に音楽を与えないとき、その 0 小節の play は次の section の小節に自分の調を arm するか。** ⚠️ **これは「ページ対双子」ではなかった**——**ページは arm し双子は書かない**が、**A が*小節を持つ*ときは両者とも §3 の境界則どおり「A の調 → B で score の調へリセット」を出す**（`scratch/p306/b9` 実測）。**壊れているのは 0 小節のときだけで、そこでは境界そのものが発火しない**。⇒ ★★ **どちらの沈黙が正しいかを選ばず、綴りを拒否する**——**§3 の境界則は「小節を持つ section」について書かれたもので、0 小節の section はその外側**。★ **`LYS1005`（Undefined section）の兄弟**: **名前が*宣言されていない*場合は既にエラー**で、これは**宣言は在るが全部ヘッダ**の場合。⚠️ **判定は「どの part かが音楽を持つか」で訊く**——**A の音楽が*この score が描かない別の part* に在る本は正しい**（`scratch/p306/u3` 実測・2 小節・clean）。 |
| ★★★ **part ブロックの option 位置（`section A { m clef bass { … } }`）を言語は持たない＝4 つとも拒否**（2026-08-31・第306・**ユーザー確認**・✅ **同便で実装＝`LYS1035` の 2 つ目の位置**） | ⚠️⚠️ ★★★ **新しい決定ではなく、下の「`transpose` / `octave` を section スコープの機能として足さない」の*帰結*。** **その決定の理由は「`transpose` が欲しい場面は*参照側*の印であって宣言側ではない」で、cell は宣言側**——**理由がそのままこの位置にも届く**。⇒ **§2 F ⒭ は「決めることが 2 つ」と書いていたが、⑵ は既に答えが在り、⑵ が No なら ⑴ も決まる。** ★ **実測（第305）**: **`clef` / `octave` / `instrument` は黙って無視され、`transpose` だけが*読まれてスコープを間違える***（section A の cell に書いたのに part 全体が動く）。★ **`GRAMMAR.md` の `PartBlock = Identifier , MusicBlock ;` にはもともと option が書かれていない**——**直したのは実装であって文法ではない**。 |
| ★★★ **part の設定（`clef` / `octave`）を section の「ヘッダ位置」に書くのは禁止＝診断を出す**（2026-08-31・第305・**ユーザー決定**・✅ **同便で実装 `LYS1035`**） | ユーザー: 「section-major の section ヘッダに書いた clef は、あまり意味がないというか、役に立たない指示に思える。この clef は禁止して、診断メッセージを出すようにして」／「同じように、instrument も section major のヘッダに書くのはサポートしなくて良いだろう」。⇒ ★★ **足したのは規則ではなく*対称性*。** **その位置に書ける part 設定は 4 つで、`instrument` と `transpose` は既に蹴っていた**（誰も要求しないので `ReportStrayItem`＝LYS0030）。**`clef` と `octave` だけが `IsMusicItemStart` に載っている**（他の場所では本物の music item なので）**ため `ParseSectionItem` の music 腕に取られ、「どの part にも属さない裸の音楽」になって黙っていた**。⇒ **4 つのうち 2 つが喋っていたのを 4 つにした。** ⚠️ **`instrument` は作業ゼロ**（既にエラー）——**直したのは文面だけ**（LYS0030 が「正しい家」を言っていなかった）。★★ **`octave` を巻き込んだ理由は設計論ではなく実測**: **音高は 1 つも動かない**（3 section とも relative のまま G4）**のに `.ly` 双子だけ part 全体の包みが `\relative c'` → `\fixed c'` に変わる**＝**「効かない」ではなく*読み手が食い違う*。** ⚠️⚠️ **位置が規則であって語ではない**——**section の本体が*音楽そのもの*なら同じ `clef` は正しく効く**（`part m { section A { clef bass … } }` と、1 パートで裸の音楽を書く `section A { clef bass … }`。両方とも実測で効く）。**cell を持つ section だけが、置き場所を持たない。** ★ **射程はゼロ**（ディスク 1954 冊で当たるのは scratch のプローブだけ＝**追跡本 0・ユーザーの 326 冊 0**）。 |
| ★★★ **`transpose` / `octave` を section スコープの機能として*足さない***（2026-08-31・第305・**私の助言をユーザーが承認**） | **問い**（ユーザー）: 「octave と transpose は、サポートしたら便利だろうか」。⇒ **答えは「形が違う」。** ★★ **`transpose`**: 効く使い道は「最後のコーラスだけ半音上げ」で、ユーザーのコーパス（ポップス）にまさに在る。**だがその用途が欲しいのは「*同じ* section を違う高さで 2 回鳴らす」ことで、それは宣言側ではなく*参照側*の印**——**同便の §2 F ⒬ がちょうどその判断をしている**（`~B'` は occurrence のもの・宣言は動かない）。**`section D { transpose d }` は D の*全ての play* を動かす**ので欲しいものと違う。**しかも transpose は既に家が 2 つある**（part 属性・score の `ScoreTranspose`）。⇒ **やるなら別項「form 参照に移調の印」**＝⒬ と同じ設計で、**綴りの決定（半音数か・音程か・目標調か）が要る。未起票・未決定。** ★★ **`octave`**: **`absolute`/`relative` は「音楽の性質」ではなく「このテキストをどう読むか」のモード**で、section ごとに変わると同じ音符の意味が見落としやすいヘッダに依存する。**しかも collector は section 境界で relative の枠を既にリセットしている**ので、per-section octave が買えるのは「絶対モードのファイルから 1 section 貼った」だけ＝狭い。⇒ **禁止のほうが一貫する**（上の行）。 |
| ★★★★ **`|:` `:|` `[N. …]` は music に書けなくする。書けるのは `form {}` の中だけ**（2026-08-31・第304・**ユーザー決定**・✅ **2026-08-31・第305 で実装＝`LYS1034`**） | ⚠️ **実装で足りないものが 1 つ出た**（§2 F ⒫ ⑴）: **form は 3 つ目の volta 括弧を綴れなかった**ので、**`:| [3. …]` をもう一度取る腕を足した**——**「form の中だけで書けるようにする」を実行するために要る追加**であって、決定の変更ではない（**ユーザー 326 冊のうち 13 冊がこれを使う**）。★ 実測: **切り直しは 11 冊・ユーザー 115 冊、掃き 1946 冊で新しい診断はこの 1 本だけ**。／ ユーザー: 「`|:` を music の中に書けなくするのは決定だ。これを書けるのは form の中だけにする」。⇒ **第303 の「方向には合意した・決定ではない」は、これで*決定*になった**（§2 F ⒫）。★ **線は「演奏順序を変えるか」で引く**——**form へ**: `|:` `:|` `[N. …]` `repeat volta`／**music に残す**: `repeat percent`・`repeat unfold`・`tremolo`（音符の省略記法で順序を変えない）。⚠️⚠️ **射程は 2026-08-31・第304 実測**: **ユーザー 326 冊のうち 115 冊（35%）が music に `|:`/`:|`/`[N. …]` を書いており、禁止するとその 115 冊が通らなくなる**（`[N. …]` だけなら 77 冊）。**追跡側は 581 冊中 20 冊。** ✅ **移行は【ユーザー決定 2026-08-31・第304】＝⒜ 即エラー**（猶予も自動書き換えも置かない）。✅ **LP 忠実度の本は ⒝＝切り直して LP に対して測り直す**（同日・同決定。⚠️ **測り直すと数が縮んだ——LP 回帰コーパス 81 冊のうち該当は 1 冊だけ**で、残り 5 冊は `audit/lpreg/`＝LP コーパスではなく `VoltaBracketSkylineTests` の fixture）。⚠️ **私は ⒞（自動書き換えを先に作る）を勧め、数を出したうえでユーザーが ⒜ を選んだ**——**ユーザーの原則「実装コストや移行コストは選択肢の並べ方に混ぜない」と一致している**ので蒸し返さない。★ **もし後で書き換えが要るなら半分は在る**: `LysWriter.WriteVoltaSections` が volta を「名前つき section ＋ form」に分解する。 |
| ★★★ **構造だけを担う section は*宣言側*で `section ~A { … }` と書き、その section のラベル既定をひっくり返す**（2026-08-31・第304・**ユーザー案・私は賛成**・✅ **同便で実装 `73b47843`**） | ユーザー: 「section 定義側で `~A` のように書くと、ラベル表示・非表示の既定がひっくり返るのでどうだろう」。⇒ **⒫ の前提条件 ⑵**（繰り返しの縁を運ぶためだけに切った section が全部ラベルを持つ問題）。★★ **純粋な追加であることを実測した**（2026-08-31・第304）: **`section ~A` を書いている本はディスク 1925 冊中 0 冊**で、**現在は硬いエラー**（`Expected a name, found 'Tilde'`）——**既存の綴りを 1 つも読み替えない**し、**参照側の `~` 260 個は全部これまで通り「隠す」**（既定を裏返す section が 0 冊だから）。★ **賛成の理由**: **`~` の意味が両側で 1 つのまま**（「ラベルについての印」）で、**「この section は構造であってリハーサル記号ではない」は section の*性質*なので、参照ごとに繰り返すより宣言に置くのが正しい**。⚠️ **名指しておく帰結**: **`section ~A` を宣言した本では `form { ~A }` が「*表示*」を意味する行になる**——`~` を「隠す」と読む習慣とぶつかる。**既定という概念の当然の帰結として受け入れる**（代案「`section ~A` は*常に*刷らない」は、1 か所だけ見出しを出したいときに section を割ることになるので採らない）。 |
| ★★★ **セクション境界: 調はリセット／音部記号もリセットでよい／テンポはリセットしない**（2026-08-31・第303・**ユーザー決定**・**実測すると現状が既にその通り**） | ユーザーの理由が 3 つとも違う: **調**——**「音高を書くときには調が決まっているはず」**なので、section 先頭でリセットするのが*正しい*（綴りが調に依存する）。**音部記号**——**「リセットしなくても音の意味は同じ」**なので任意だが、**リセットしても悪くない**。**テンポ**——**リセットすべきではない**（曲の流れであって、section の持ち物ではない）。★ **実測（ページ＝正しい証人）**: 中途の `clef bass` は次の section で **treble を描き直す＝リセットされる**／テンポは次の section に何も刷らず MIDI も **120 / 120 / 60** のまま**戻らない＝引き継ぐ**（`scratch/p303/cleftempo/`）。⚠️⚠️ **私は §1 ⑸ で 1 度「音部記号は引き継ぐ」と書いた**——**根拠に使ったのが `lysc xml` で、同じ便で「xml は section 境界の復帰を書かない」と証明した直後だった。****壊れていると証明した計器を、その場で使い続けていた。** ⇒ **残る穴は「section ヘッダの `clef` が効かない」**だけで、§2 F ⒭ に書き直した。 |
| ★★★ **セクション境界で調と拍子は score レベルへ戻る（音部記号とテンポは戻らない）**（2026-08-31・第303・**ユーザー確認**・✅ `02613871` で 3 つの書き出しにも実装） | **ページと小節検査は前からそう振る舞っていた**（`MeasureCollector.ProcessSectionPrologue`、voice ごとの `_sectionResetTimeBeats` スナップショットに対して）。**確認したのは、そこで*同時に*起きるrelative の枠と音価のリセットを含めて「これで良い」ということ**（§1 ⑸）。⚠️ **音部記号とテンポだけが引き継ぐ理由は書かれていない**——§2 F ⒭ に起票。★ 共有の問いは `Semantics.ScoreHomeMeter` が持つ（`ScoreHomeKey` の双子）。**射程 16 冊 / 219・インク 0/81。** |
| ★★★ **volta 用に切った section でも relative の音高と音価はリセットされる。引き継ぎは*記法*で与える**（2026-08-31・第303・**ユーザー決定**・✅ **音高側は第304 で実装**） | ユーザー: 「volta を書くために導入したセクションにおいても、relative の音高と音価をリセットするのは受け入れても良い。ただし、わかりやすく簡潔な記法を導入して、音高と音価を引き継げるようにする文法があってもいい」。⇒ **リセットは据え置き**（§1 ⑸ の通り、2 オクターブ下がるのはバグではなく `InitialOctave` ＋ `LastPitchName='c'` の帰結）。✅ **記法は第304 で入った**（§2 F ⒬）: **`~B'` / `B,` / `[1. B']` が*その play* の枠を 1 オクターブずつ動かす**——**phrase 参照の印をそのまま通した**（新しい概念ゼロ・両オクターブモード・4 読み手すべて）。**音価は新しい記法が要らない**（section の最初の音符に数字を 1 つ）。⚠️ **「*前の* section から引き継ぐ」印はまだ無い**——入れるなら参照側の opt-in（§2 F ⒬ に残した）。 |
| ★★★ **rule 2（`|:` は span を閉じない）は実装しない**（2026-08-31・第303・**ユーザー決定**） | **測ってから決めた**（§1 ⑷）: 全木 218 冊のうち **89 冊が変化・幽霊小節 115 個が消える**、suite は**4 本だけ赤（全部 `EmptyMeasureValidatorTests`）・snapshot 0 枚**、**`ArmBoundaryForStructuralBarline` と `_atScopeStart` は rule 2 なら両方死ぬ**（Arm を無効化しても赤は同じ 4 本・form 経路は不動）＝**エンジンの form 経路は既に rule 2 を実装している**。⇒ **それでも入れないのは、§2 F ⒫（`|:` を form 限定にする案）が上位互換だから**——禁止すれば music 側の対の問題は*存在しなくなる*。**計測ゲートは木から落とした。** |
| ★★★ **`@!X` は全族に及ぶ＝オッターヴァは `@!ottava`（`@loco` 退役）、ペダルは `@sustain`／`@sostenuto`／`@unaCorda` … `@!X`（`@sustainOn`／`@sustainOff` 族は退役・`@treCorde` は `@!unaCorda` の糖衣として残す）**（2026-08-29・第289・**ユーザー決定**・✅ `1115f42e`＋同便） | **決め手は「その終端が*語を刷るか*」**——**ユーザーの基準**（「レンダする文字になるならその名前がわかりやすい」）**を族ごとに当てて測った**。**⒜ `loco` は刷られない**（`@loco` を書いた本の SVG に `loco` の text 要素は 0 個・変わるのは括弧の右端だけ）**うえ LP にも命令が無い**（2.26.0 の `ly/` `scm/` `lily/` で当たるのは C++ コメントの `in loco` と drum の `loconga` だけ。music glossary に*用語として*だけ載る）⇒ **退役**。**⒝ `sustainOn`／`sustainOff` の "On"／"Off" はどのスタイルでも一度も刷られない**（刷られるのは "Ped." と ✱）⇒ **方向は `!` へ**。**⒞ `treCorde` は Text スタイルが実際に刷る語**⇒ **糖衣として残す**（`@!rit` ≡ `@!textSpan` と同じ 2 綴り 1 mark）。★★★ **`@sustain` は LP から離れるのではなく*LP のモデルそのもの***——`ly/spanners-init.ly:94-101` は 6 命令を綴るが、どれも `sustainOn = #(make-span-event 'SustainEvent START)` の形で、**1 つの span event に方向を渡しているだけ**。**"On"/"Off" は表面がその方向を綴っていたもので、この言語は方向を既に `!` で綴る。** ⚠️ **`@ped` は検討して退けた**: **LP に `\ped` は無い**／**3 つとも「ペダル」なので `@ped` だけがカテゴリ名で兄弟が機構名になる**／**既定の Bracket スタイルではペダルの語が 1 つも刷られない**（実測）。**過去に `@ped(off)` が退役した理由（存在しない引数スロットに状態を置いた）は `@!ped` には当たらない**ので、退けたのは別の理由。⚠️ **オッターヴァとペダルで LP と乖離する**——**LP はどちらも閉じられなかった span を*最後まで黙って描く***（`ottava-engraver.cc:220-226`／`piano-pedal-engraver.cc:425-443`・**LP 2.26.0 で実測**）。**「描かない」は言語の答えで、両 engraver に `NOT PORTED HERE` を立てて宣言済み**（APPROX 53 → 55）。⚠️ **「開いている最中の START」の答えは族ごとに違う**: **テキストスパナは拒否／オッターヴァは octavation の変更／ペダルは踏み直し**——**類推で揃えて `audit/lpreg/ottcons.lys` に捕まった。** |
| ★★★ **終端の綴りは `@!X` に統一する＝`@!X` は `@X` が開いたものを閉じる。テキストスパナは `@textSpan("…")` / `@!textSpan`、`@rit`／`@accel`／`@rall` は START だけの糖衣で `@!rit` が閉じる**（2026-08-29・第288・**ユーザー決定＝案 B**・✅ `7b0df578` で第289 が実装） | **3 択を出した**: ⒜ `@rall` を足すだけ／⒝ LP 忠実（終端必須・閉じなければ診断して描かない）／⒞ 折衷（汎用ペアを足しつつ裸の `@rit` は 1 小節の既定を保つ）。**ユーザーは移行コストを外して ⒝ を選んだ。** ⚠️ **決め手は LP 忠実性ではなく*言語の一貫性***——**「閉じられなかった span」に Lily# は今 3 つの違う答えを持っている**（第288 実測、3 つとも `lysc check` は `No errors found`）: **`@sustainOn` は無言で何も描かず／`@ottava` は 12.01→33.64 という独自長を描き／`@rit` は 1 小節**。**⒞ は 4 つ目を足す**が、**⒝ はこの族の答えを 1 つにし、ペダルとオッターヴァを寄せる先を作る**（→ §2 の ▶「閉じられなかった span の答えを 1 つにする」・**同じくユーザー決定で優先度高**）。⚠️ **糖衣と既定長は独立**なので、**`@rit` は短いまま終端必須にできる**——⒞ はその 2 つを混ぜていた。★ **LP の裏取り**: `\rit` という命令は 2.26.0 に**存在しない**（`ly/spanners-init.ly` の原始命令は `\startTextSpan`／`\stopTextSpan` の 2 つだけ）。**長さは `\stopTextSpan` の位置そのもの**（実測: 1 小節 10.8／4 小節 46.36）で、**閉じなければ `unterminated text spanner` を出して `suicide()`＝文字ごと消える**（`lily/text-spanner-engraver.cc:121-130`）。**LP に「既定の長さ」という概念は無い。** ⚠️ **語彙を enum で持たない理由も LP に在る**——`ly/articulate.ly:565-589` は `"rall"`／`"rit."`／`"accel."`／`"poco rall."` を**文字列比較**しており、同ファイルの TODO が「Add more synonyms for accel and rall: rit ritard stringendo」と言っている＝**語彙は原理的に閉じない**。⚠️ **`@cresc` 族はこの決定の外**——**LP でも `\cresc` は Dynamic_engraver が次の強弱で自動的に閉じる**ので、**自動終端が LP 由来である唯一の族**。 |
| ★★ **`repeat percent` の body が 3 小節以上の*整数*小節のとき、LP の描き方は写さない**（2026-08-29・第282・**測って決めた**） | **LP は 2.26.0 で「裸のスラッシュ 1 本 ＋ 完全に空の小節 N−1 個」**を出す（`scratch/p282/wholebody.ly`。slash の grob は小節ぶんの広がりを持たない）。**LP 自身が範囲外だと言っている**——`RepeatSlash`／`DoubleRepeatSlash` の `description` は両方「repeating patterns *shorter than a single measure*」（`scm/define-grobs.scm`）。**写すと 32 冊 200 site が空小節になる**。⇒ **Lily# は小節ごとの `%` を保つ**（**宣言された乖離**＝`docs/APPROXIMATIONS.md` の APPROX、`PercentRepeatItem` の remark）。⚠️ **`%` が記譜として正しいかは別の問い**で、そちらは同じ日に **⒥（診断を出す）**で閉じた（次行）。 |
| ★★ **その `%` には診断を出す（LYS2014）。記譜そのものは変えない**（2026-08-29・第282・**ユーザー決定**・✅ `85406c45` で実装済み） | **`%` は「直前の 1 小節を繰り返す」記号**なので、3 小節以上の body に描く `%` は**読み手に body の*最後の*小節を N 回**と伝える（音は正しい）。**⒤ 今のまま／⒥ 診断／⒦ 別の記譜**の 3 択を出してユーザーが **⒥** を選んだ。⇒ **書き手に告げて、選ばせる**。**⒦（`repeat unfold` 相当に書き下す等）は採らない**——**今日それを求めている本が 1 冊も無く**、移行費用が要る。⚠️ **警告はコレクタが小節ごとに署名するのと同じ場所でしか鳴ってはいけない**（規則は `PercentRepeatShape` の一軒・突き合わせは全数掃き）。 |
| ★★★ **書かれた `\|` はちょうど 1 小節を閉じる＝ブロックの先頭の `\|` も空小節を 1 つ作る**（2026-08-29・第279・**ユーザー決定**・✅ 同便実装） | 旧規則（先頭の `\|` は境界を*アンカー*するだけ）は作者の本を 1 小節ずつ削っていた: `君の恋人になったら` は 4 小節 1 行で書かれているのに先頭が `\|` のブロック 2 つだけ 3 小節、`amazing-grace` の和音行は弱起用の先頭 `\|` が捨てられて全和音が 1 小節早く最後の小節が空。**ブロックの終端は今までどおり何も閉じない**（末尾 `c1 \|` を 1 小節に保つ＝追跡 497 冊・作者 98 冊がその綴り）。閉じない綴りは 3 つ: 型つきは装飾／`\|:` は前の小節を開くので scope 先頭では何も作らない／auto-fill・phrase 出口が閉じた境界に乗った `\|` は確認するだけ。**掃き 899 冊で動いた本は 5＝先頭 `\|` を書く本ちょうど 5 冊。** |
| ★★ **何も刷らない score は警告する（LYS2013）**（2026-08-29・第279・**ユーザー決定**・✅ 同便実装） | `part m { section A { } }` が白紙を刷って `lysc check` は "No errors found." だった（`staff bass` が 15 段の空譜を刷った沈黙と同族）。**LP は黙らない**——`\score { \new Staff { } }` は "skipping zero-duration score / consider adding a spacer rest" と警告し**ページを 1 枚も出さない**（2.27.3 実測）ので、**error ではなく warning**。第187 が LYS6007 で閉じた「0 バイトの絵」一族の最後の 1 形。 |
| ★★★ **小節の途中での改行はサポートしない**（2026-08-29・第280・**ユーザー決定**） | **LP は対応している**（2.27.3 実測: 書いた位置で折り、折れ目に小節線を描かない）が、**求めている本が 899 冊に 1 冊も無い**——毒で数えて真に小節途中の `break` は追跡 573 冊で **0 件**、作者の 326 冊の 198 件は**全部が「短い小節の中の break」**でその短さは既に診断済み。**費用は前提の破壊**（今日のエンジンは小節をレイアウトの原子として扱う: line breaker・`lysc layout`・パート間整列・小節番号）。**Lily# の規則は「小節線は、行が折れてよい場所」**＝`\|` が境界そのものという既存の意図的乖離の延長。⇒ **再検討の条件は `time none` が描画側で効くようになったとき 1 つだけ**（§2 F ⒤⒥）。 |
| ★★★ **`break` に小節線の機能は持たせない**（2026-08-29・第280・**ユーザー決定**） | 案を毒として実装して 899 冊を掃いた: **版面の指示が音楽を変える**——`d'' e'' break f'' g''` が **4 小節から 5 小節**になり、2 段目の小節番号が 3 でなく 4 になり、折れ目に**小節線が描かれる**（LP は同じ入力で 4 小節・番号 3・小節線なし）。**そして `time none` と直接衝突する**（★ ユーザーの指摘）——無拍子には小節線が 1 本も無いので、`break` が小節線を引くなら**折る唯一の方法が「引きたくない小節線を引くこと」**になる。影響は追跡 0 冊・作者 7 冊で、**7 冊とも既に過少小節の警告が出ている箇所**。★ **守りたかった「`\| break` は小節線 1 本」は、案を採らなければ今日すでに成立している。** |
| ★★★ **`|:` は直前の小節線と対を作る＝`… | |: …` は空小節を開く**（2026-08-28・第275・**ユーザー決定**・✅ 同便実装） | ユーザー報告: `partial.lys` の `c8 | /* HERE */ |: c'4 d e f :|` で HERE の小節が描かれず、**`|:` を `|` に書き換えると描かれる**。**先に測った**——`| |:` は `|:` と**SVG がバイト一致**（題名の文字を除く）＝2 本目の小節線は「無印」ではなく*消えていた*。`c'1 |: …` 2 小節／`c'1 | |: …` 2 小節／`c'1 | | …` 3 小節・**pickup は無関係**（`partial` 有無で同じ表）。⇒ **理由は 1 文**: **`|:` は装飾ではない**。`||` `|.` `:|` は空の span で*後ろの小節の終端*を retro-type して何も作らないが、**`|:` は何も飾らず*前の小節を開く***ので、その手前の span は持ち主が無い＝言語が既に `| |` と綴っている gap そのもの。装飾と同じ棚に置いたのが分類の誤りで、**1 つの配置の 2 つの綴りが違う答えを出していた**。⚠️ **form の小節線は対を作らない**（12 本の赤で学んだ）——form の repeat は `|:` `:|` を音楽ストリームに*合成*するので`HandleBarline` からは手書きと区別できず、直前の section は自分の最後の小節を書かれた `|` で閉じていることが多い。`form main { A |: D :|: ~D :| }` が 3 小節 → 5 小節になった。⇒ **form 側が小節線を出す直前に境界を arm する**（`MeasureBuilder.ArmBoundaryForStructuralBarline`）。⚠️ **射程＝全木 sweep 4/572**（**4 冊とも同じ綴り**: 連続 repeat の `… [2. e2 c | ] |: …`）——`grammar-2026-06-09` 12→14・`grammar-tour` 41→42・`voltasky` 8→9・`voltagrace-ctl4` は `c1 |` と `grace { f8 } |: b1` のあいだに空小節。**4 冊とも本便では直していない**（ユーザーの手元の 9 冊と同じ扱い＝古い絵に戻す綴りは冗長な `|` を落とすこと）。⚠️ **ゼロ長の span が gap になるのは元からの挙動**（`c1 | grace { f8 } | b1` も `c1 | key g major | b1` も昔から 3 小節）——本便が作ったのではなく、**`|:` だけがその規則から免除されていたのをやめた**。rerender 0/81・snapshot 0 枚 ⚠️⚠️ ★★★ **射程の訂正（2026-08-31・第303 実測）**: この行の「ユーザーの手元の 9 冊」は**隣接した `| |:` の数とぴったり一致する**（実測 9 箇所 / 9 冊）——**`| break |:` の形は数えられていない**（**78 箇所 / 66 冊**）。**合計 87 箇所 / 72 冊＝作者の蔵書の 22%**、そして **72/72 で幽霊小節が実在する**（各冊を「冗長な `|` を落とした複製」と `lysc layout` で突き合わせ、幽霊の総数 87）。**隣接 9 箇所を 1 つずつ読んだが、空小節が欲しい所は 1 つも無い。** ⇒ **決定は据え置き**（第303 でユーザーが rule 2 を退けた）。**訂正されたのは射程の数であって規則ではない**——ただしこの数は §2 F ⒫ の入力になる。 |
| ★★★ **空の小節 `| |` は診断しない。エンジンが「その拍子 1 小節ぶんの spacer」で埋める**（2026-08-28・第275・**ユーザー決定**・✅ 同便実装） | ユーザー指示「空っぽの小節線 `| |` を書くとエラーになるが、エラーにならないようにしてほしい。内部では自動で s1 を補うような動作にして」。⚠️ **診断の除去は表面で、本体は*ゼロ長*の除去**——`| |` は「item 0・duration 0」の小節として作られており、**紙面では正しく揃うのに再生では揃わなかった**（彫版は*小節*を歩き、MIDI は*長さ*を歩く）。実測: 2 譜の `c'1 | | e'1` 対 `c1 | g1 | c1` で、**上声の 3 小節目が tick 1920＝下声の 2 小節目と同時に鳴っていた**（正しくは 3840）。⇒ `MeasureBuilder.EmitEmptyMeasure` が**その時点の拍子 1 小節ぶんの spacer**（`s1`／3/4 なら `s2.`／`partial` の中なら短い小節ぶん）を入れ、`MeasureModel` も同じ長さを返し、**MidiExporter にも同じ規則を綴った**（この walk は兄弟リストしか見えずcollector の measure stream に届かないため——3 綴りが食い違わないことは `| |` 対 `s1` の恒等テストが見張る）。⚠️ **消えた網**: `MeasureValidator.ValidateEmptyPlaceholders` と、その*射程*だけを見ていた 4 つの theory（form 非依存・track cell の除外）。track を音楽と読まない保証は `CrossPartMeasureValidator` の `PartBlockSyntax` 走査と `MeasureCollector.IsInsidePartMajorTrack` に構造として残る。射程は **全木 sweep 1/572**（`test/section-empty-placeholder` のみ・空小節の幅が 6.48→6.38 と `s1` の綴りに収束）・**rerender 0/81**・**snapshot 1 枚**。GRAMMAR.md の「常に警告が出るのでソース上で必ず見える」の一節は書き換えた |
| ★★★ **staff-less リードシートは grid 行に拍子を刷る（意図的乖離）＋行の繰り返し記号は型を保つ**（2026-08-20・第226・**ユーザー決定**・✅ 同便実装） | ユーザー指示 3 連「歌詞のみ／歌詞＋コードのリードシートにセクションと拍子を表示」「歌詞とコードのリードシートに繰り返し記号も」「コードのみも同じように」。実査: **セクション枠は全変種で既に表示**・繰り返しは music 経路の行（chords）では既に出ており、**歌詞行だけが barline トークンの型を落としていた**（修理＝LyricsCollector が `ParseBarlineType` の 1 表で型を運ぶ・`|:` は「この小節を素で閉じ次を開く」＝music の HandleBarline と同意味論・`:|`×`|:` の RepeatBoth 畳みも同型）。**拍子は LP 実測と逆向きの意図的乖離**——LP は ChordNames/Lyrics だけの系に meter 幅を予約しない（staffless-system.ly CO/CO3 実測）。第226 で**台帳 2 点を退役**（`staffless.line-start.meter-identity`・`chords-vs-staff`＝decided divergence は台帳の外・歌詞行バンドの前例。LP 数値は probe ヘッダと git に残る）。実装＝`AnyStaffEngravesTime` に lead-sheet 条項（**1 述語で門・layout・renderer が自動追従**）・描画は grid 行の `SolvePrefixColumns.TimeX`（予約と同じ 1 導出）・行頭 spring は「meter で終わる staff と同じ wish」（LineStartColumn の empty-wishes 分岐に HasTime 枝）。**残**: mid-piece の `time` 変更は行 voice に change item が無く**幅もインクも出ない**（退行ではなく非表示・⑸ 小粒に起票） |
| ★★ **マーク＋テンポの縦積みは LP 忠実のまま。横並び案は「全面 LP 忠実の後」に再検討**（2026-08-20・第220・**ユーザー決定**） | ユーザー所感「Verse とテンポが近すぎる・Y を揃えて横並びが良いのでは→LP 双子と比べて」→実測: **LP 自身が縦積み**（テンポ底 2.850＝譜 2.05+padding 0.8・マーク底 6.504＝テンポ頂+0.46 の outside-staff-padding）で、**Lily# は桁まで一致済み**（2.88/6.90）。決定＝現状維持・横並びは意図的乖離になるため全面 LP 忠実の達成後に改めて検討。⚠️ 副産物: **マークの X が LP と約 2 ss 違う**（LP は箱の左端を拍子記号の位置 3.365 に・Lily# は行頭寄り 1.4＝scratch probe mark-tempo.ly 実測）——これは乖離ではなく未移植候補。⑸ の小粒に起票 |
| ★★★ **`lines` は part ヘッダから score 項目へ＝`staff m as lines N`**（2026-08-19・第217・**ユーザー起案・決定**・✅ 同便実装） | ユーザー「part {} の中に lines を置けるのはいまひとつ」＋綴りもユーザー原案どおり。線数は LP でも `StaffSymbol.line-count`＝layout 側で、part 持ちだと総譜 5 線／リード譜 1 線の併記が綴れなかった。`as` は `chords … as roman`・`tab … as numbers` と同族の「この帯をどう刷るか」で、part 名直後の裸語＝表示ラベルとの曖昧を断つ。**ossia にも許可**（ユーザー決定＝リード譜の繰り返し 2 回目のリズム等）。part ヘッダの `lines` は既存 unknown-property 網が一覧ごと自動で正しく拒む（新 code 0）・値域チェックは parser が**同文・同 code**（UnknownSymbolCase）で継承・pair テストも score 側へ移設。移行は rhythm-slashes 1 冊＝snapshot 正規化恒等を機械確認。tab は対象外（線数＝弦数）。同族の removeEmpty/pedal は **⒣（§2F）に別便起票**（ユーザー指示）。⚠️ 学び 2 敗: GRAMMAR.md の生成規則内コメントに `;` を書くと DocKeywordListTests が塊をそこで切る——注意書きをブロックに残した |
| ★★ **`with` はキーワードごと退役＝LYS0031 も除去し、補完にも出さない**（2026-08-19・第217・**ユーザー決定**・✅ 同便実装） | ユーザー指示「with は自動補完でリストされないようにして。LYS0031 のエラーコードは除去して」。移行は全書済み（追跡 569 冊＋ユーザー楽譜 9 冊＝同便前半）で網に獲物が残らないため、**LYS8007／`font` と同じ形でキーワードごと退役**——旧綴り `staff m with lyrics ja` は「表示名 "with"＋行」と読まれ fold する（`ScoreRowFoldingTests` がピン）・tab の旧綴りは generic 網（Undefined part: 'with'）。⚠️ **道連れの発見: tab の `with chords` は第216 の除去から漏れて*黙って通っていた***（`Parser.Form.cs` の分岐が残存・実測）——退役で一緒に消えた。退役番号は DiagnosticCodes 台帳・GRAMMAR.md・テスト remark の 3 箇所に同文（英語の歌詞・名前としての `with` は合法化）。**補完の `with` の出所は LSP ではなく VS Code の word-based suggestions**（LSP は `with` を一度も出していない・実査）＝`[lilysharp]` 既定で off（package.json）・tmLanguage の keyword 列からも削除。**要再配備**: deploy-extension.ps1 |
| ★★★ **`with lyrics`/`with chords` は除去し、score は「帯の縦列」に統一する**（2026-08-19・第215・**ユーザー決定**・✅ **第216 実装＝`b30d9bce`→`0baf2dcc` の 4 commit**） | ユーザー起案「縦に積むだけで意図する楽譜を書けるはず」。関連は定義（`sings`）・位置は並び順・吸着は fold（隣接する bound 行＝その譜の verse）＝**LilyPond 自身のモデルに収束**（LP に `with lyrics` は無い）。構文破壊なので**初タグ前が唯一の無料期間**にやり切った。除去は LYS0031・経緯は §2F ✅ |
| ★★★ **無名 `chords {}` は縦積みに畳む＝除去**（2026-08-19・第216・**ユーザー決定**・✅ 同便実装＝LYS0032） | 問いは「残すか畳むか」（第215 起票 ⑷）。技術評価を示して 1 問: 無名形の関連は「併記」という*推論*で、複数パート section では staff 0 固定のハードコード＝関連先がどこにも書かれていない。決定＝畳む。「名前を付けて score に置け」をエラーメッセージが綴る。既存 6 冊は命名＋行配置へ移行済み |
| ★★★ **歌詞は定義側で自分のメロディに結びつく＝`sings`**（2026-08-19・第215・**ユーザー決定 3 件**・✅ 同便実装＝`cd059d44`） | 要件はユーザー起案:「歌詞は必ず専用メロディに関連づく（歌詞メロディは省略可）・**並べた別パートのメロディには関連づかない**・同じメロディに複数の歌詞（多言語・替え歌）」。決定: ⑴ **0.3.0 の前に入れる** ⑵ 綴りは **`sings`**（`lyrics ja sings vocal { }`） ⑶ unbound トラックの `with lyrics` 添付は**即エラー**（初タグ前が唯一の無料期間。fixture 15・samples 2・docs・XML importer の出力まで全て `sings` へ）。**score の `lyrics NAME` 行は、束縛先を刷らずにそのリズムで音節を置く**（LP の `\lyricsto`＋NullVoice 相当——メロディをサブ収集し、全 item を同じ長さのスペーサーに置換した骨格が行の声部＝時刻が実在の列になる）。voice の名前一致束縛は存続・unbound 行（リードシート）は均等割りのまま。網は LYS6009/6010/7004/7005＋`SingsLyricsTests`・snapshot `test/sings-chorus-row`・変換器は sings を両方向運搬 |
| ★★ **score の行でも `sings` を綴れる**（2026-08-19・第218・**ユーザー名指し**・✅ 同便実装＝`33988510`） | `score { staff melody  lyrics verse sings melody }`——行が束縛を**表明または再表明**する。**同じ 1 つの track 属性**で、置き場所が 2 つあるだけ（読みは `LyricBindings.BindingOf` の 1 軒・LYS7004/7005 も共通・同一の再表明は無音）。従来この綴りは `sings` が次の render 項目へ落ちて Undefined part になっていた。補完は AfterLyricsRowAttachName 族 |
| ★★★ **裸の duration は直前イベント（音符・和音・スラッシュ）の反復**（2026-08-19・第215・**ユーザー決定**・✅ 同便実装＝`4ecb6676`） | LP 2.20+ の isolated duration と同義（**LILYPOND-REF: parser.yy music_embedded**・2.26.0 でバイト一致 3 種実測: 音高反復・**和音は丸ごと**・休符透過）。`bes8 8 8 8` のベース刻みが動機。**代償を承知で決定**: `c 4` の LYS0016 誤字網のうち「時間が狂う類」は bar check が受け、**「音高の書き漏らし（`4 g f e`）」は黙って反復になる**——LP が同じ綴りに払っている値段。LYS0016 は「反復先が無い」だけに残る。arpeggio は走行を**断つ**（曖昧のまま黙らせない）・`[`+整数+`.` は volta 優先（付点で走行を開くなら `[/4.` と綴る・ピンは `BareDurationTests`） |
| ★★★ **`/` はスラッシュ音符＝§3 記号規則の意識的例外**（2026-08-19・第215・**ユーザー提案・決定**・✅ 同便実装＝`4ecb6676`） | 「記号は LP が既に記号で綴っているものにだけ」に対し LP は `/` と綴らない（`\improvisationOn`）。それでも採ったのは**トークンが印字されるインクそのもの**だから（`|` が小節線を描くのと同族）。中央線固定・無音・音価どおりの符尾連桁・`lines 1` と合成で一線リズム譜。`time 4/4`・`tuplet 3/2`・`c/g` の `/` は文脈が別で不干渉。双子は `\improvisationOn`＋**clef の中央線音高**で書き、LP 側フレームだけ進めて次の実音で補正（既存の乖離フレーム機構） |
| ★★★ **0.3.0 は未署名のまま出す**（2026-08-19・第215・**ユーザー決定**） | 署名はどの経路（Azure Trusted Signing／SignPath OSS／OV 証明書）でも**ユーザーの資産＋本人確認で日〜週単位**かかり、0.3.0 がそれに吊られる。CHANGELOG の Known limitations が回避策（ブロック解除 or `dotnet lysc.dll`）を記載済み＝**その行は消さない**（第214 ⑶ の宿題 ⒝ はこれで確定）。署名を入れるなら 0.3.x で `release.yml` に配線し、そのときに行を消す。⚠️ **SAC Enforce の機械では回避策側も SmartScreen と挙動が違う**（第212 実測: ブロックは時間で解ける・判定はパスでなく内容） |
| ★★★ **移調 clef の part には MusicXML の `<transpose>` を書く**（2026-08-17・第196・**ユーザー決定**・✅ **第197 実装＝`cbc5e646`**） | MusicXML の `<pitch>` は「書かれた音高」で written→sounding は `<transpose>` が与える、という規約に従う。⚠️ **`MusicXmlReader` の注記が逆の読みを明記している**ので**同時に直す**——**片方だけだと round trip が 2 オクターブ落ちる**。**SVG も MIDI も動かない**（実測・snapshot 不動）。⚠️ **起票時の「射程 44 冊」は綴りの grep で、実測は 20 冊**（第197・§2F ⒜） |
| ★★★ **`repeat unfold N` は「N 回鳴らす」＝各コピーは同じ音**（2026-08-17・第196・**ユーザー決定**・✅ **第197 実装＝`47215106`**） | percent / tremolo に第195 が入れた「各 pass はその 1 コピー」と同じ規則へ揃える。⚠️ **起票時の「snapshot が動く」は外れ**——**566 冊に実サイトは 1 つ（`canon-in-d` の phrase 参照）で、snapshot は 1 枚も動かなかった**（第197 実測。**射程を数えるのは着手の*前*でよい**） |
| ★★★ **2 つ目以降の score は「警告 ＋ `--score` / `--all` を midi・xml・ly にも足す」**（2026-08-17・第196・**ユーザー決定**・✅ **第197 実装＝`13a674bf`**） | ページが既に `--all` / `--combined` / `--score NAME` の 3 入口を持つので、その家に揃える。**出力パスを明示したときに勝手に 2 つ目を書かない**のが警告つき既定。⚠️ **`--combined` は入れていない**——**1 枚に積むのはレイアウト、続けて収めた `.mid` は別の曲**（LP も 2 ファイルを書く・実測） |
| ★★★ **小節途中の `clef` は相対オクターブの anchor を付け替える（ページが規則）**（2026-08-17・第196・**ユーザー決定**・✅ **同じ便で実装＝`ce408263`**） | **part ヘッダの clef が既に anchor を決めている**ので、「clef という 1 語が 1 つの意味を持つ」ほうを採る。⇒ **MIDI と MusicXML が実装し、双子は補正した綴りを書く**（`transpose` の前例＝第194）。**LP 2.26.0 で裏取り済み・snapshot 不動・3 冊とも閉じた** |
| **`SystemBreaker` の再入可能化は入れない** | LP はページブレーカーが行分割を選ぶ（`optimal-page-breaking.cc:139-173`）が、入れると F3 の tier-1 skip の健全性論拠が壊れる（break 解が縦の関数になり、gate を計算するのに gate が守る結果が要る＝循環）。⚠️ **判断し直すなら順序は「①まず頻度を測る（コード変更ゼロ）→ ②有意ならオプション分離＋一致不変条件テストとセットで」**。性能が理由ではない |
| **臨時記号の糖衣 `c?` / `c!` / `c??` は入れない** | `!` は点線小節線トークン。`c?` 単独では `!` の罠への導線を作る。痛みは `@courtesy`/`@editorial` の専用エラーで解消済み |
| ★ **記号（sigil）は LP が既に記号で綴っているものにだけ使う。Lily# 固有は全部 `@name`** | 上の決定から出た一般則。今後の記号追加はこれで判断する |
| **休符の実インク化はやらない** | 実測で棄却。休符は中央線に座るので縦インクが極値にならず、LP でも 1 ビット違わない＝箱が名目なのは事実だが**不活性** |
| **単一ページは紙面サイズにしない**（意図的乖離） | Lily# は 1 ページに収まるスコアを内容サイズで出す（明示的な設計）。台帳に載せると total が ~109 になり指標が壊れるので**載せない** |
| **本数（count）の点は ss の総和に入れない** | 距離ではないから（`unit` フィールドで分離） |
| ★ **セリフ体は TeX Gyre Schola のまま同梱する。LP の C059 には合わせない**（ユーザー判断・2026-08-02） | **量を測ったうえでの決定**。LP は `"LilyPond Serif"` を **C059** に解決し（`ly:stencil-expr` がファイルパスごと吐く）、C059 は **AGPLv3**（URW の例外は PS/PDF への埋め込み限定で**フォントプログラムの同梱は覆わない**）。**両者は advance は完全一致するが、カーンと合字が違う**: カーン値は **471 有効ペア中 438 が食い違い**、丸め後に予約幅が変わるのは **2 文字組の 11.2%（475/4225）**。合字は**両方とも合字にするがグリフ幅が違う**（`ff` 605 対 686＝5px、`ffi` 878 対 904、`fi` は一致）。**現実の文字列で 0〜4px＝0〜0.137 ss**（`Violoncello` が最大・`Allegro` +1px・`Ave verum corpus` は 0）。⇒ **0.03〜0.14 ss の恒久差**を受け入れ、**AGPL を持ち込まない**。⚠️ **帰結**: `text.width.{aa,va}` は**永久に非ゼロ**（原因は台帳に完全記述）、**今後テキスト幅の点は 1/9 の確率で非ゼロで開く**、そして**紙面そのものが LP と字送りで違う**（測定だけの話ではない）。⚠️ **測っていないのは regular/bold/bold-italic 面**（italic だけ全ペア走査した）。★ **第221 追記＝垂直 ink の初 member**: **i/j の点だけ 0.135818 ss 違う**（C059 1.765633 / Schola 1.629815・**h/x/g/p は bit 同一**・fontTools bbox 実測）。歌詞床が i/j 頂点の音節で縛る本は恒久差——最初の顔は台帳 `lyrics.band-floor.staff-to-lyric` −0.092。★ **第222 追記＝列間隔に出た初 member**: **"ru" の kern が Schola +20 / C059 +18**（fontTools GPOS 実測）で、per-glyph hinting が 2/1000em を **Pango pixel 1 個（0.034143307 ss）**に増幅——台帳 `lyrics.column.word-gap.narrow` が恒久 +1px（この行の「丸め後に予約幅が変わる 11.2%」の列間隔側の最初の顔。mum/nu は kern 対を持たず exact）。★ **差し替えは後からできる**——`TextFontMetrics.SerifFamily` と `Fonts/` とライセンス表記だけで、対照本 `TS1`/`TS2` が効果を即座に示す |<!-- ledger: lyrics.band-floor.staff-to-lyric = -0.091999532 --><!-- ledger: lyrics.column.word-gap.narrow = 0.034143307 -->
| **LP の「正」は 2.26.0** | 版で PUA コードポイントも Emmentaler も動く。**必ず feta 名で引く** |
| **cross-staff beam は skyline から除外**（LP の字面） | `axis-group-interface.cc:850-858` の LP 自身のコメント。Lily# の「固定 3.5 stem を残す」は発明だった |
| ★ **和音記号は LP に合わせる＝中心合わせしない**（ユーザー判断・2026-07-25 明示） | 意図的乖離かを問うたうえでの決定。`ChordName` は X-offset も self-alignment も持たない（`define-grobs.scm:837-855`）＝ink 左が列。`7e7fe5cb` で移植し `staffless.line-start.chords-vs-staff` が閉じた。⚠️ **和音グリッドは別 grob（`GridChordName`）で LP も中心合わせする**が、中心を取る相手は小節の四角。Lily# に四角は無いので chords-only シートは ChordName 経路のまま＝**「グリッドも直す」で触らない** |
| ❌ **撤回（2026-07-27・ユーザー判断）: 独立 lyrics 行を「譜のような帯」として置く** — **もう決定ではない。蒸し返し禁止の対象から外れた。** | **旧決定**（2026-07-26）: 独立行は「譜に付く歌詞」ではなく**リードシートの word トラック**なので譜グループとして置く＝**9.600000 対 LP 5.500000＝+4.100000**、台帳には載せず導出形で主張。**撤回の理由**は「間違いだったから」ではなく**射程が二度狭まって残らなかった**から: ①2026-07-27 に「鎖に参加しない」部分が `lyrics.chord-row.between-systems.*` の実測で落ち、②同日 LYRR/LYRRV が **LP 側の恒等を 59 行の機械差分で確定**させ（`\lyricsto` の有無で LP は 1 行も変わらない）、**残っていた「距離」も Lily# 単独の量**だと分かった。⇒ 行は `nonstaff-relatedstaff-spacing` で自分のインクから置かれる（`dee2c045` 系）。**いまの状態**: `lyrics.row.staff-to-lyric` は**台帳点で exact**、`LyricRowIsSpacedLikeTheLyricsContextItIs` が**2 つの綴りが一致すること**（＝LP の恒等の再現）を主張する。⚠️ **帯そのものは残っている**——行は自前の小節線を持ち verse を band 内に積む（`LyricRowBaseline` は `LILYSHARP-OWN` のまま）。**消えたのは「どこに置くか」だけ。** ★ **2026-07-28 に鎖にも入った**（§1 の第20セッション）。**帯そのものはまだ残る**が、
system の最後の spaceable 譜の下に立つ行は **verse ごとに鎖の要素**で、帯の上端は解に従う。
`lyrics.row.two-verse.verse-step` は exact、LYRRV ≡ LYRV|<!-- ledger: lyrics.row.staff-to-lyric = 0 --><!-- ledger: lyrics.row.two-verse.verse-step = 0 -->
| ★ **タブの*和音*のタイは LP の広げ方に譲る**（ユーザー判断・2026-08-16 明示） | **LP をタブ側で直接測ってから決めた**: `<c' e' g'>2~ <c' e' g'>4` の TabStaff で LP 2.26.0 は **dir = −1, +1, +1**（TabNoteHead・staffpos 1/3/5＝**一番下の弦は数字の下・上 2 本は上**。双子に `Tie.direction` を吐かせた実測）。⇒ 旧「タイは stem と反対側に固定」という Lily# 独自規則を**和音では通さない**。★★ **実装は規則を書き直していない**——`TieFormattingProblem` の中に既に移植済みだった `set_ties_config_standard_directions` を static に出し、**タブが*自分の* staff position** （`TabStaffGeometry.StaffPositionOfString` ＝ LP の `tablature-position-on-lines` ＝ `StringCount+1−2·string`）で呼ぶ。★★★ **単音は数学的に不変**: 旧規則は `string > (N+1)/2`（符尾と反対）、新しい位置の符号は `string < (N+1)/2` で正、列が 1 本なら `sign(position)` と `neutral-direction`＝UP なので**全チューニングの全弦で答えが一致する**（中央弦も両方 UP）。⇒ **`LILYSHARP-OWN` が 1 件 `LILYPOND-REF` になった。** 観測は `TabChordTieTests` |
| ★ **タブのタイは自分の数字の縁から出る**（第180・LP に対応物なし） | Lily# の数字はジグザグで 2 列に分かれるので、**タイは自分の列の数字の縁から縁へ**引く（`軸 + dx ± 数字幅/2`）。⚠️ **LP には問えない**——**LP のタブ数字は 3 つとも同じ x**（実測 8.82 / 12.951）なので、選ぶべき第 2 の x が存在しない。⚠️ **帰結として単音のタイが短くなる**（`test/tab-tie` で 1.29 → 0.89）。LP は**中心から中心へ引いて `whiteout` で抜く**（頭間 2.787 に対しタイ 2.467＝88%）が、**その whiteout は Lily# には移植できない**（§2 の ✅ ⒳＝LP の数字 1.180 は弦間 1.5 に収まるが Lily# の 2.166 は収まらず、隣の弦の線を消す／占有子は色を使うのでダークモードで穴になる）。⇒ **タイが短いのは大きい数字の帰結として受け入れる。「短いから戻す」で数字の縁を捨てないこと** |
| ★ **占有（不透明な箱）ではなく除去（インクを切る）で重なりを解く**（第180 で再確認・元は `digitGaps` の実装時） | **理由は 2 つあり、どちらも実測**: ⑴ **箱は色を使う**——ページを反転してテーマを当てる viewer（VS Code のダークモード）では箱が黒くなり、**数字の周りが黒・背景がグレー**で数字が穴に座る（ユーザー・2026-08-16 明示）。⑵ **箱は数字と同じ高さ**＝Lily# では **2.166 対 弦間 1.5** なので**隣の弦の線を両側 0.333 ずつ消す**——**数字を大きくできなくしていた天井そのもの**。⇒ `digitGaps` は**色を 1 つも使わず自分の線の中だけで完結する**。⚠️ **LP の `TabNoteHead (whiteout . #t)` を「LP がやっているから」で移植しないこと**——**LP の数字は 1.180 高で 1.5 に収まる**（2.26.0 実測）ので LP では安全なだけ。**重なりを消すなら、覆うのではなく切る** |
| ★ **タブの弦は小節の中で継承する。明示 `\N` も継承する**（ユーザー判断・2026-08-16 明示） | **LP と違うことを測ってから決めた**: `c( g'\2) g g4` をベースで書くと Lily# は g を 3 つとも 2 弦 5 フレットに置き、**LP は無印の 2 つを 1 弦開放へ戻す**（2.26.0 実測・双子）。**1 つの音高は 1 小節のあいだ同じ押さえ方に見えるほうが読める**、が決定の理由。⚠️ **この resolver はもともと LP と別の模型**（`Tunings.CalculateFret` ＝左手の位置を追う LILYSHARP-OWN）なので、乖離はその延長。**「LP と違う＝バグ」で消さないこと**（理由は `TabResolver.ResolveTabStrings` の remarks にも書いてある） |
| ★ **タブのフレット数字を LP より大きく描くのは意図的乖離**（ユーザー判断・2026-07-24 明示） | LP のタブ数字は小さくて読みにくい。Lily# は `TabConstants.FretFontSize = 2.6`（単数字幅 1.625・高さ 1.7875）＝LP の TabNoteHead 幅 0.990155 の約 1.64 倍。和音で数字が被る問題は**じぐざぐ配置**（`SpacingRules.ApplyTabChordSpacing` ほか）で解いてある。**「LP と違う＝発明だから消す」で削らないこと。** ⚠️ 弦間隔（`TabStringSpace`）は別の話で、そちらは LP の 1.5 に揃える |

---

