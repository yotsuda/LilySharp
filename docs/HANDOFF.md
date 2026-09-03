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
# ⚠️⚠️ ★★★ **`成功!` の語ではなく*合計*を読む**（2026-08-30・第302 実測）——同じ木・同じ
#    コマンドで 1 度だけ `合格: 5304 … 合計: 5307` と「成功!」を刷った（正しい合計は 6688）。
#    **赤は 0 のまま 1378 ケースが黙って走らなかった。** 引き継いだ合計と突き合わせること（RULES §5.5）。
# ⚠️⚠️⚠️ ★★★★ **`--logger trx` を*最初から*付ける**（第316 が申し送り・**第317 の 1 発目がそれで捕まった**）——
#    **落ちるのが「赤いテスト」ではなく*ホストの死*のことがある**。そのとき合計は黙って足りなくなり、
#    **緑になった 2 度目には証拠が残らない**。**trx には残る**（`ResultSummary/RunInfos` に理由と全スタック）:
#      [xml]$x = Get-Content scratch\pNNN\run1.trx -Raw; $x.TestRun.ResultSummary.RunInfos.RunInfo.Text
#    ⚠️ **logger は無いディレクトリを自分で作る**ので、パスが無いことは失敗の原因ではない（第317 実測）。
dotnet test  LilySharp.Tests\LilySharp.Tests.csproj -v q `
  --logger "trx;LogFileName=$PWD\scratch\pNNN\run1.trx" 2>&1 |
  Select-String '成功!|失敗!|Passed!|Failed!'
# ⚠️⚠️ ★★★ **`exit 1` なのに `Passed!` と刷ることがある**（第317 実測＝`Passed! … Total: 1625` で
#    終了コード 1）。**語も終了コードも別々では足りない。合計を引き継ぎと突き合わせること。**
# ⚠️⚠️ ★★★ **この緑は「この機械の Windows での緑」でしかない**（2026-08-19・第212セッション）。
#    **GitHub の門は 214 便のあいだ 1 度も読まれておらず、実際には赤だった**——
#    **最後に push した木で ubuntu Release は 5331 合格 / 59 失敗**。**必ず両方読むこと**:
gh run list --limit 5
#    ⚠️ **`X` が並んでいても中身を見るまで理由は分からない**——**fail-fast が他脚を*キャンセル*
#    するので、`X` の多くは「失敗」ではなく「巻き添え」**。**完走した脚だけが証拠**:
#      gh run view <runId>                          # ✓/X と job ID
#      gh run view --job <jobId> --log > $env:TEMP\ci.log   # `--log-failed` は途中で切れる
#      Select-String -Path $env:TEMP\ci.log -Pattern '\sFailed\s+LilySharp\.'
#    ⚠️⚠️⚠️ ★★★★ **この WSL 脚は 2026-09-01 現在この機械で回らない**（第313 実測）——
#    **`wsl -l -v` が "no installed distributions"。`Ubuntu-24.04` は消えている。**
#    **⇒ ubuntu の緑は `gh run list` / `gh run view` で*読む*しかない。下の 5 行は距離が戻ったときのため。**
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

⚠️ ★★ **8 例目＝「exact 490」の*数え方がどこにも書かれていなかった***
（2026-08-25・第247セッション）。**答えは何便も正しかったが、素朴に `residual -eq 0` で
数えると 462 になる**——**台帳自身が `"tolerance": 1e-6` を宣言していて、`exact` は
その許容の中に居る点のこと**（丸めだけ残った点も exact に数える）。**正しくはこう**:
```powershell
"exact $(@($e | Where-Object { [math]::Abs($_.Value.residual) -le 1e-6 }).Count)"
```
⚠️ **`-eq 0` の 462 と `<= 1e-6` の 490 の差 28 は「丸めだけ残った点」の数**で、
**どちらの数にも意味はあるが、§1 が引き継いでいるのは後者。混ぜないこと。**
⚠️⚠️ ★★★ **`-lt` ではなく `-le`**（2026-08-29・第283 が裏取りで踏んだ）。**この段落は
何便も `-lt` と書いていて、その式は 581 を返す——§1 が引き継いでいる 582 ではない。**
**差の 1 個は `lyrics.row.between-staves.two-verse.hara-kiri.last-system` の残差 `-1e-6`**
＝**宣言された許容の*ちょうど境界*に乗った点**で、**「許容の中に居る」の素直な読みは境界を含む。**
⇒ ★★ **この節が 9 例ぶん「数え方を書け」と言っているのに、書いてあった数え方のほうが
§1 の数と食い違っていた**——**レシピを置くだけでは足りない。置いたレシピが、引き継がれている
数を*実際に再現するか*を 1 度打って確かめること。** ★ **第283 の踏み方も同じ形**
（素朴に `-eq 0` で数えて「exact 550」を出し、首をかしげて §0 を読んだ＝`-eq 0` の 3 例目）。

⚠️ ★★ **7 例目＝「追跡コーパス 567 冊」の*数え方がどこにも書かれていなかった***
（2026-08-19・第211セッション）。**答えは 40 便連続で正しかったが、数え方が無いので
裏取りのたびに推測することになる**——**実際この便は最初 `audit` 配下だけを数えて 341 を出した。**
**正しくはこう**（**`.lys` は `audit` の外にも在る＝`LilySharp.Tests\Fixtures` など**）:
```powershell
"追跡コーパス $(@(git ls-files '*.lys').Count) 冊"   # 567（audit 配下だけなら 341・別の数）
```
⚠️ ★ **9 例目＝「snapshot 222」の*数え方がどこにも書かれていなかった***
（2026-08-26・第256セッション）。**答えは何便も正しかったが、素朴に `*.snap` や
`*.verified.*` で数えると 0 になる**——**この木の snapshot は `.svg` で、置き場所が名前**。
**正しくはこう**:
```powershell
"snapshot $(@(git ls-files 'LilySharp.Tests/Snapshots/*').Count) 枚"   # 222
```
⚠️ ★★ **10 例目＝「台帳の `OPEN:` 0」の*数え方がどこにも書かれていなかった***
（2026-08-30・第294セッション）。**答えは何便も 0 で正しかったが、素朴に json を grep すると 12 になる**
——**`OPEN:` は `why` の*先頭に置く接頭辞*で、台帳自身の説明文（L10）と、本文の途中で
「deliberately OPEN:」のように*語として*使っている点 11 個が全部当たる**。**正しくはこう**:
```powershell
$e = (Get-Content audit\lp-geometry\lp-geometry.json -Raw | ConvertFrom-Json).entries.PSObject.Properties
"OPEN: $(@($e | Where-Object { $_.Value.why -like 'OPEN:*' }).Count)"   # 0（素朴な grep なら 12）
```
⇒ ★★ **接頭辞で意味が決まる札は、grep ではなく*その場所*で数える**——**「先頭に在るか」と
「含まれるか」は別の述語で、後者は必ず多めに出る**（§5.5 の `-in` が大小を無視して 18 冊と答えた
のと同じ向きの壊れ方＝**増やす側に外れるので、数がもっともらしいと気づけない**）。

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

最終更新 第328セッション＝**入り方は第298〜第326 と同じ**（ユーザーは `docs/HANDOFF.md` を読んで着手せよとだけ言い、便の途中の口挟みは 0・background job）。⇒ ★★★★ **本便は第327 の次の一手 ⑶＝「smartTyping.ts に自動テストが無い（切り出せば node で回せる）」を閉じた**（拡張コード 1 commit `d054e34c`・Core 0 変更）、**そのあと第327 の次の一手 ⑷ の先頭＝§2 T6 ⒢（スラー復元）をコーパス側で閉じた**（製品コード 0・コーパスは git 管理外）。**骨は 6 つ**:

⚠️⚠️ ★★★ **⑴ `smartTyping.ts` を 2 つに割った**——**`editors/vscode/src/smartTypingCore.ts`（`vscode` import 0）＝本文の読み手（`musicEvents`／`noteSlots`／`postEventRun`／`POST_EVENT_RANK`）と規則 1〜29 の planner 全部**。**planner は適用せず*計画*を返す**（`FixPlan`＝現本文の offset の edits ＋ 仕上がりの caret ＋ log 用 `what`。`TypePlan` は従来どおり）。**`smartTyping.ts` は VS Code 側だけ**（change-event 経路・intercept 経路・caret の窓・snippet 1 回で edits と caret を同時に置く `carryOut`）。**snippet の算術 `composeFix`・change-event 経路が打鍵を取り除く `afterKeystrokeEdit`・intercept 経路の 3 結末 `typedKeyOutcome` も core に出した**ので、**2 経路を文字列の上で再生できる**。規則の一覧（1〜29）は core の頭へ移した。
★★★ **⑵ 番人＝`editors/vscode/test/smartTypingCore.test.ts` ＋ `test/simulate.ts`（68 本・`npm test`・0.2 秒）**——**`tsconfig.test.json` が core と test を `out-test/` に建て（git 管理外・`.vscodeignore` で VSIX 外）node の test runner で回す**。**caret は `‸`**（`|` は小節線なので印に使えない・選択は `‸«3»`）。**規則ごとに doc の例をそのまま 1 ケースに**（`c1.‸`+`6`→`c16.‸`・`c8(~‸ d`+`[`→`c8([‸~ d]` など）。★★ **TypePlan の鍵（`' , . \ 0-9 @`）は毎ケース intercept 経路と change-event 経路の*両方*を再生して一致を assert**＝「打鍵なしの本文で決める」枠組みの性質そのもの。**core が `vscode` を import しないこともテスト**（それが node で回る唯一の前提）。**simulate の auto-close は VS Code の既定（`autoCloseBefore`＝`;:.,=}])> \n\t`＝空白・行末・閉じ括弧の前だけ `()` になる）。**
★ **⑶ 挙動の差は機構 1 つだけ**——**和音の wrap（規則 1・`‸c4`+`<`→`<c‸>4`）は `editor.edit` → 選択移動の 2 段だったのを、他の caret 付き plan と同じ snippet 1 回にした**。他は edits が同一（`applyFix` 経路の promote/demote/auto-open は caret 無しのまま）。
★ **⑷ CI に `extension-tests` job を足した**（`.github/workflows/ci.yml`・ubuntu・node 22・`npm ci` → `check-types` → `npm test`）＝⚠️ **未検証（push 後に `gh run list` で読む・第313 ⑶）**。
★ **⑸ 罠**: **⒜ 最初の赤 2 本は両方とも*私の期待値*の読み違い**（`<<c e>‸>4` の Delete は 2 つ目の `>` を消すので caret は `>` の*後*／VS Code は `"` の前で `(` を auto-close しない）＝**コード側の赤 0**／**⒝ `node --test` はパイプに繋ぐと TAP を刷る**（合計は `# tests 68`・`# pass` の行を読む。`ℹ` で grep しても出ない）／**⒞ ripple の Caribbean／Adriatic は第327 の `>>` 継続プロンプトのまま busy 表示**（Ctrl+C で prompt は戻ったが ripple の boundary は戻らず＝第324 ⒜ と同じ。Norwegian を使った）。
★★★ **⑹ T6 ⒢＝`.ly` のスラーを `.lys` へ戻した**（詳細は §2 T6 ⒢ の ✅）——**285 対・1097 本中 1078 本を 126 冊に復元・skip 19（両端不一致 5／section 跨ぎ 6／grace 7／既在 1）**。**`lysc check` は error 0→0・warning 887→887・新規診断 0・insert-only**。**計器は `scratch/p328/t6g/`**（`RestoreSlurs/`＝C#・`verify.ps1`・`apply.ps1`・`backup/`＝原本 126 冊・`report.csv`・`detail.txt`）。★ **1 度目は grace のスラーを書いて 14 警告**→ 書かずに数える側へ（Lily# が grace のスラーを刷る日に `detail.txt` の 7 本を当て直す）。★ **設計の要点**: 音価は両側とも解決済みで比べる（`.lys` は section 頭で 4 分に戻す＝T6 ⒤ の Lily# の読み）／`.ly` の post-event は `\markup { }` の block まで読む（Butterfly 86/88 の `^\markup { x1 }(` を 1 度取りこぼした）／`c4 (` の離れたスラーも読む。**第 2 便（ユーザー「この便が有利なら着手」）で同じ計器を ⒣ タイ・⒤ section 頭の音価に延ばした**＝機械で当てる物は 0・手直し 2 冊（`She Bangs` E の落ちた `r8`・`A Thousand Miles` L22 のタイの位置）・**計器の罠 2 つは §2 T6 ⒣⒤ の ✅ に**（`@accent( c)` を引数と読む／blank 済み文字列が空白に化ける＝どちらも「新規診断 0」の門が捕まえた）。**第 3 便（同じ指示）で ⒜＝section 頭で落ちた音を計器に数えさせた**（§2 T6 ⒜ の ✅）＝隙間は 2 冊だけ・手直し待ち（`A Thousand Miles` B2 の前の 1 小節・`Sugar` C の全音符 4 小節＝**どちらも第 8 便で片付いた**）・副産物で `Can't Get You Out Of My Head` の `r8` 2 箇所を当てて警告 13 → 3。**T7 は新しい流れなので着手していない。**
★★★★ **第 4 便＝ユーザーが 9 点の判断を返した便（対話）**——**製品コード 1 commit に 3 つの決定**（§3 に 4 行）: **⑺ grace の手書きスラー**（`grace { g16( } a8)`＝appoggiatura の弓・`GraceNoteItem.ExplicitSlur`・番人 `GraceExplicitSlurTests` 5 本＋fixture `test/grace-explicit-slur`）→ **コーパスの 7 本も当てた**（`t6g/backup3/`）／**⑻ 曲頭の `|:` を刷る**（T5 の決定反転・双子に `printInitialRepeatBar`・台帳 IR 取り直し＝OPEN −0.30）／**⑼ 和音行の上の volta と ending の箱＝§2 T8**（ユーザーが提案の絵で見つけた欠陥・LP 実測 3 点・移植 3 点・**1 度目の種まきは Staff 級 mover まで持ち上げて退行＝支持は grob の階層ごと**）。**手直し**: `A Thousand Miles` B2 の前に `e,2 fis, |`（octave は第 8 便でユーザー確認済＝B1 の同じ小節と同じ高さ）。**Lambada は提案を `scratch/p328/lambada/` に建てた**（T2 ✅ 提案・36 cell の読みが C の和音とバス音の一致で裏付く）。⚠️ **T6 ⒥ break は「放置」（ユーザー）＝閉じた**。**push はユーザーが行う**（`38cb4dad` の説明は返答に）。
★★★★ **第 5 便＝ユーザー報告「C セクション先頭の `|:` と最初の音符の x 距離が近すぎる」（同じ Lambada の絵）**——**根は第 4 便が開けた台帳の OPEN −0.30 そのもの**（§2 T5 の ✅ に詳細）。**⑽ 行頭の `|:` は break-align 表の列ではなく、pen が prefix から 1.15（LP に無い数・`LineStartBarClearance`）ずらして描き、最初の音のばねは拍子の `first-note` 2.0 のまま、小節線の幅 1.84 をその前に差し込んでいた**＝`|:` は 0.15 右・音は小節線から 0.86（LP 1.30）。**LP は begin-of-line の順で `staff-bar` を拍子の*後*に置く**（`define-grobs.scm:668-683`・中間の小節線は拍子の*前*＝`BoundaryColumn` の順）。**移植**: `SolvePrefixColumns(…, staffBarWidth)` → `PrefixColumns.BarX/BarWidth/BarGap`（**`Right` は小節線を含めない**＝measure frame が `StartBarline` 幅を spring 0 の前に差し込むので、列はばねで払い pen は `Right + BarGap` に描く）／`LineStartColumn.LineStartSpring(…, measureStartBarWidth)`＝小節線を extremal grob にして wish（`first-note` semi-shrink 1.3）・小節線の箱を min_dist に・**光学補正（staff-spacing.cc:206＝既存の `BarlineToNextNotesCorrection`）を行頭でも**・返すばねは measure frame（差し込まれる幅を引く）／pen は **`MultiStaffLayouter.LineStartBarGap`（staff・tab・span bar・mark anchor の 4 読み手が 1 導出）**・`DrawnLineStartBarline`（`:|:` の begin-of-line 片の判定を layout と pen で 1 つに）／**行だけの譜で `|:` が prefix 無しに立つときは LeftEdge の 0.0 に置き、grid の細い system bar はその下に描かない**（LILYSHARP-OWN・LP に対応物無し）。**LP 実測** `probes/initial-repeat-bar.ly` IR: TIME 4.885+1.7・BAR 7.585+1.84・HEAD 10.725 → **Lily# 7.58／10.73（2 小節目以降の線も LP と一致 16.785／25.685）**。**台帳**: IR exact・新点 `line-start.time-to-repeat-bar`（LP 2.70・exact・読み手 `TimeSignatureToLineStartBarline`＝太線を読む）。**番人** `LineStartColumnTests` +3（列・ばね・継続段は clef 0.7／key 1.1・prefix 無し 0.0）。⚠️ **踏んだ罠**: 単体テストの小節線の箱を `StaffBottom..StaffTop` の帯で作ると音符（譜の下）と向き合わず min_dist が拍子のまま＝`PrefatoryBox` の neighbour 伸ばしを通すこと／引用の門は 1 行ごとに名前を要る（`space-alist`・`extra-space` は 2 節で名前にならない＝`break-align-orders`・`extra-spacing-width` を同じ行に）。
★★ **第 6 便＝T2 の対話**（ユーザーが提案 `.lys` の行頭 `|` を「冗長」と消した→ 和音が 1 小節ずれる＝行頭の `|` は「1 小節目に和音が無い」の cell だった）: **提案は section 頭の空 cell だけ `s`・他は `| |` に書き直した**（`lysc check` 0・絵は第 4 便と同じ）。**その途中で製品の欠陥 1 つ**: **`MeasureValidator` が part-major の `chords` track の section を inline music として歩き、`s`／`r` の slot を 4 分休符に値付けて 2/4 の行の各小節に LYS2001／2006 を出していた**（`| |` の空 cell は 0 なので鳴らず、`s`／`r` を書いた瞬間に出る）→ `ValidateNode` で `ChordPartBlockSyntax` を歩かない（和音行は拍の格子＝短くも長くもならない・格子の診断 LYS2009／2010 は collector 側）。番人 `MeasureValidatorSectionTests.ChordTrackSections_AreNotBarChecked`（対照＝同じ綴りを part に書けば警告が正しく出る）。⚠️ **ユーザーの問い「`chords {}` で `s`・`r` を受け入れる必要はあるか」は返答で意見を述べた（`r`＝N.C. は LP の noChordSymbol と同じ実需・`s` は `| |` と小節頭の slot 以外で冗長）**→ ★★★ **第 7 便＝ユーザー決定「`s` を除去して、小節頭の `.` を空 slot として認めて」を実装（§3 の行）**: パーサは `RestS` を chords で受けず LYS1028 が `'| . C |'`／`'| |'`／`r` を名指す・collector の HeadDot 記録と `ChordRowGridValidator` の LYS2010 腕を撤去（`ChordRowGridWarning` から `HeadDot` を落とした）・`Diagnostic.cs` は LYS2010 を退役コメントに（LYS2014 の型）・番人 3 本・lpreg `chord-names-rests.lys` と台帳 probe ROWMX/ROWME の `s` を `.` に（幾何は同じ＝台帳不動を確認）・docs 3 本・CHANGELOG 2 本（Breaking changes）・Lambada 提案の `s` も `.` に。**コーパス 286 冊＋fixture＋lpreg を `lysc check` で走らせ、旧 `s` を含む本は 0**（下の数を見よ）。★ **第 8 便＝ユーザー「提案通りで良い」→ Lambada の提案をコーパスへ写した**（T2 ✅・原本は `scratch/p328/lambada/backup/`・error 本は 0 に）。

★ **開始時裏取り**: HEAD `54d41a62`・**未 push 34**（`origin/master` は `407b4f77`＝第322。第327 §1 の「30」は HANDOFF commit の前に数えた数＝`d8e71787`・`e986242e`・`5c9e4099`・`54d41a62` の 4 本で 34）・木 clean・未追跡 0・`LilySharp.Cli\bin\Debug` は net10.0 のみ・Core 0 エラー 0 警告・**Windows Debug 7065 / 0 / 4 / 7069（`scratch/p328/run1.trx`・2 分 23 秒・第327 と一致）**・追跡 `.lys` 594・snapshot 244。
終了時: **commit 7 本**（`d054e34c`＝拡張の分割＋番人＋CI job／`da5a3fce`＝HANDOFF＋ARCHIVE／`681a0629`＝T6 ⒢ の HANDOFF／`bb752a49`＝T6 ⒣⒤ の HANDOFF／`3fea11a5`＝T6 ⒜ の HANDOFF／**`4a7f9506`＝第 4 便の製品 commit（grace の弓・曲頭 `|:`・和音行の volta＝38 ファイル）**／その HANDOFF）・**未 push 41**・作業ツリー clean・未追跡 0（`scratch/p328` は管理外＝`run1..5.trx`・`commit-msg-*.txt`・`t6g/`・`volta/`・`grace/`・`lambada/`）・**ユーザー実コーパス 126 冊が動いた（スラーの `(` `)` だけ・backup 有）＋第 2 便の手直し 2 冊（`She Bangs`・`A Thousand Miles`＝backup 無し）＋第 3 便の `Can't Get You Out Of My Head`（`r8` ×2・`t6g/backup2/`）＋第 4 便＝grace のスラー 7 本を 4 冊に（`t6g/backup3/`）・`A Thousand Miles` B2 の前に `e,2 fis, |`（手・backup 無し）**・Core 0 エラー 0 警告・**Windows Debug 7076 / 0 / 4 / 7080（`scratch/p328/run5.trx`・2 分 36 秒・第327 比 +11＝GraceExplicitSlur 6 ＋ InitialRepeatBar 1 ＋ snapshot 2 ＋ 台帳 3 − 1）**・**`npm test` 68 / 0**・追跡 `.lys` 596（fixture 2 冊＝`grace-explicit-slur`・`volta-chord-row`）・**snapshot 246（新 2・動いた 8＝`initial-repeat-bar`・`grandstaff-repeat`・0.02 の 5 枚・`showcase/04-advanced`）**・**台帳 762 点（exact 594 / `OPEN:` 1＝IR の −0.30）**・`docs/APPROXIMATIONS.md`／`audit/magic_constants.csv` 再生成・**LSP／拡張は `tools/Deploy-Lsp.ps1` で配備**（第 1 便の `d054e34c` と第 4 便の後）。
**第 5〜7 便の終了時**: **commit 5 本**（**`9272c7e1`＝製品＝`|:` の列・23 ファイル**・HANDOFF `3a8320dd`・**`696d87ef`＝下向き符尾の台帳点＝tests／probe／台帳／HANDOFF のみ**・**`594998fa`＝第 6 便＝`MeasureValidator` の和音 track 除外＋番人＋CHANGELOG 2 本＋HANDOFF**・**第 7 便＝`s` の除去と小節頭の `.`（§3・製品＋番人＋docs＋CHANGELOG＋HANDOFF）**）・**未 push 46**・作業ツリー clean・未追跡 0・Core 0 エラー 0 警告・**Windows Debug 7084 / 0 / 4 / 7088（`scratch/p328/run11.trx`・第 4 便比 +8＝`LineStartColumnTests` 3 ＋ 台帳 2 ＋ validator 1 ＋ 第 7 便 2）**・**コーパス 286 冊＋fixture＋lpreg の `lysc check` で旧 `s` を含む本 0**・追跡 `.lys` 596 不動・**snapshot 246（動いた 4＝`initial-repeat-bar`・`grandstaff-repeat`・`volta-chord-row`・`showcase/04-advanced`＝全部行頭に `|:` を持つ）**・**台帳 764 点（`residual -eq 0` の点 565・`OPEN:` 0＝第 4 便の「exact 594」とは数え方が違うので並べない）**・`APPROXIMATIONS.md`（OWN 121）／`magic_constants.csv` 再生成・**拡張は `Deploy-Lsp.ps1` で配備し直した（Reload Window）**。

⇒ ★★★★ **次の一手**: ⚠️ ★★ **⑴ push 後に `gh run list`**（4 脚＋本便の `extension-tests`。WSL は無い＝第313 ⑶）。✅ **⑵ T6 ⒢ は本便で閉じた**（上 ⑹）——**ユーザーが patched 126 冊を目で確かめる番**（`git diff --no-index scratch/p328/t6g/backup/<本>.lys "scratch/ベースタブLy/<本>.lys"`・戻すなら backup を Copy-Item）。✅ **⒤⒣ も第 2 便で、⒜ の数え上げは第 3 便で閉じた**。**次は T6 の残り**＝**手直し**（`A Thousand Miles` B2 の前の `e,2 fis, |` は入れた＝✅ octave はユーザー確認済／`Sugar` C の全音符 4 小節＝✅ ユーザー「全音符が正しい」と自分で直した（`lysc check` 診断 0）／`I Will Always Love You` Outro）・⒝⒞⒟⒠ は `.ly` を正解に 1 冊ずつ／**⒥ break は「放置」（ユーザー決定・第 4 便）＝閉じた**／⒦ `} |` 綴り。**Lambada（T2）は提案をユーザーが見て良ければコーパスへ写す。**★★ **⑶ T7 の C 37 冊を 1 冊ずつ**（指標は KindMatched の対で）／**⑷ 以下は第327 の並び**: T7／`audit/lpreg` 取り直し／§2 U8c・U8b・U8／§2 A/B/D/E／§2 C⑴／§2 G／低優先 `marks stacked`/`beside`／「なぜ `perf-v2bow1k` だけ落ちるか」。★ **smart typing の残る小穴（第327 ⑶）**: `c4(` の直後に `)` → `c4)(`（無意味・未起票）／`@`・`(` の change-event 経路の一瞬の caret 往復（設計どおり）／`package.json` の `"key": "\\"` が JIS の 2 つの `\` のどちらに解決するかは未確認。⚠️ **承認待ち**: §2 T2 の 1 件＋第323 ⑶ の「戻すか」（§2 T5 は第 4・5 便で閉じた＝⏸ は履歴）。⚠️ **§2 F の開いている項目は第320 の ⒲ 1 件**。⚠️ **リリース 0.6.0**: 版番号 bump と tag は未着手。✅ **第 5 便の副産物（下向き符尾の光学補正を行頭でも）は同便末尾で測った**＝`initial-repeat-bar.ly` に ID（`a''4 b'' c''' d'''`）・台帳 `line-start.time-to-first-note.initial-repeat.down-stem`（LP 5.982857＝IR ＋ 0.142857・**exact**）。<!-- ledger: line-start.time-to-first-note.initial-repeat.down-stem = 0 -->

## 以下は第327セッションの経緯

最終更新 第327セッション＝**入り方が第298〜第326 と違う**（ユーザーが VS Code の補完・smart typing を*対話で*直させた便。口挟みは 10 回超・**便の頭で Claude Code の左キー誤爆でセッションが background 化し、復帰後は直前の指示が文脈から消えていた**——ユーザーが指示を言い直した）。開始時の作業ツリーは**前の便（第326 の続き・HANDOFF 未記載）が残した 13 ファイル未コミット**＝`pitch`／`repeat` の値補完・`[` が次の音符で閉じる・`\N` を壁と読まない直し。⇒ ★★★★ **本便は smart typing の語順を 1 つに定め（ユーザー決定・§3 に行を足した）、tab 弦番号の smart key `\` と `@` の置き場所を足し、`\` 直後の補完を弦番号だけにした。製品コード 1 commit（`f9a7f7c1`・15 ファイル・前便ぶん込み）**。**骨は 5 つ**:

⚠️⚠️⚠️ ★★★★ **⑴ 語順＝`核 \N @… ] ) ( [ ~`（ユーザー決定）**——**根拠は「音符から近い順＝印が及ぶ範囲の狭い順」「同じ音符で終わる印を始まる印より先に」「括弧は入れ子（スラー外・連桁内）」「タイは結ぶ相手の直前＝最後」**。**LP 本家 regression 2237 本の集計（`scratch/p327/lporder.ps1`）はタイ最後（`(~` 5:1・`[~` 4:1・`)~` 3:0）と一致、`[(` 22:4 だけ本家と逆＝入れ子の読みやすさを優先した**。⚠️ **ユーザー明示「手元の `.lys` の語順は根拠にしない（適当だ）」「パーサはどの順でも読めるまま。変えるのは補完入力の語順だけ」**＝Core 変更 0。**実装は `editors/vscode/src/smartTyping.ts` の `POST_EVENT_RANK`／`postEventRun`／`postEventSlot` 1 か所**で、`~ ( ) [ ] \ @` の 7 つが全部これを読む（既存の印が順不同でも並べ替えず、新しい印を「同順位以下の後」に入れるだけ）。`docs/GRAMMAR_FOR_LLM.md` に house style 1 項。
★★★ **⑵ `\`（tab 弦番号）の smart key**——音符の上で `\` → 核の直後に `\` を入れ**カーソルは `\` の直後へ**（ユーザー指定・数字を続けて打つ意図。他の印は stayPut）／既に `\N` があれば**N を選択状態にして挿入しない**（ユーザー指定・`\`＋数字で弦を変える）／`\` の直後に数字が無い音符で数字を打つとその数字は `\` の後ろ（rule 25・カーソル不動）。**休符は素通し**。**和音は便の末尾でメンバー対応にした**（ユーザー「有利なら着手」で着手・rule 29＝`chordMemberAtCaret`: `<c| e>4`＋`\` → `<c\| e>4`、`@` も同じ。和音の両端と度数メンバーは素通し。parser はメンバー側 `<c\3 e\2>` と和音レベル `<c e>4\5\4` の両方を読む＝`TabStringNumberEntryTests`）。`package.json` に `"key": "\\"` を足した（**JIS の 2 つの `\` キーのどちらに束縛が解決するかは未確認**・予備経路は change-event）。
★★★ **⑶ `@` も同じ表**（ユーザー「位置が不定だ」）——`\N` と既存 `@…` の後・印の前・カーソルは `@` 直後・`editor.action.triggerSuggest` を打ち直す（LSP の trigger は旧位置で発火済みのため）。**キー束縛は付けない**（JIS は独立キー・US は Shift+2＝配列非依存に束縛できず、`shift+2` を束縛すると JIS の `"` を奪う）。
⚠️⚠️ ★★★ **⑷ 「音符の上」の範囲を印の並び全体に広げた**——`musicEvents` の event.end は `@` と `\N` までで、`a8[|` の `(` や `c8\8(|[` の `4` を「音符の外」と読んで素通ししていた（ユーザー報告 2 件）。`postEventEnd`＝並びの末尾まで／数字と octave 記号は `noteAtCaret(…, throughMarks)`（**dot は核だけ**＝`@text("x")` の後の `.` は `.up`）。**「注釈の引数の中」の判定は `(` の直前 1 文字でなく名前を遡って `@` に当たるか**（`c8\8(` の `8` を名前と読んで 'member' にしていた＝ユーザー報告 3 件目）。
★★ **⑸ LSP**: `AfterBackslash` が LP の強弱名（ppp…cresc・dim＝全部 LYS の LilypondBackslashCommand）を出していた → `GetStringNumberCompletions`＝1..`Tunings.GetStringCount` の最大（6）。番人 `AfterBackslash_OffersOnlyTabStringNumbers`（各 `c4\N` が guitar tab で通る）。**前便ぶん**: `pitch`／`repeat` が値リストを再表示（`SyntaxFacts.RepeatKindVocabulary` 新設＝パーサの文言・reference・tmLanguage 番人が読む）。

★ **開始時裏取り**: HEAD `65f15f37`・未 push 29・**作業ツリー 13 ファイル M（前便の未コミット）**・未追跡 0。
終了時: **commit 1 本（`f9a7f7c1`）＋この HANDOFF**・**未 push 30**・作業ツリー clean・未追跡 0（`scratch/p327` は管理外＝`completion1..3.trx`・`lporder.ps1`・`move-handoff-block.ps1`・`commit-msg-1.txt`）・Core 0 エラー 0 警告・**補完系フィルタ（Completion|EditorColouring|DocKeywordList）488 / 0 / 0 / 488（`scratch/p327/completion3.trx`）**。**Windows Debug full run 7065 / 0 / 4 / 7069（`scratch/p327/full1.trx`・終了コード 0・6 分 25 秒）＝第326 の 7041 比 +28＝本便 2 行（backslash theory）＋前便 26（pitch/repeat の theory 行込み）と一致**（ユーザー「この便が有利なら着手」で HANDOFF commit 後に回した）。**LSP／拡張は Deploy-Lsp.ps1 で配備済み**（`yotsuda.lilysharp-0.5.0-win32-x64`・118 ファイル byte 一致・`out/`・`package.json` 込み＝ユーザーはその場で Reload Window して検証）。追跡 `.lys` 594 不動・snapshot 244 不動・台帳 759 点不動。★ **踏んだ罠**: **⒜ ripple の pwsh に `"…$(C '\\[1-9]\(')…"` の入れ子引用を送ると `>>` 継続プロンプトで永久 busy**（2 console が便の終わりまで busy 表示＝集計はスクリプトファイルに書いて `pwsh -File`）／**⒝ `Enum.GetValues<TuningType>()` の `TuningType` は `LilySharp.Core.Syntax`（`Tunings` は `.Tablature`）**／**⒞ 番人の part 名に `g` は使えない**（音名は予約語＝`gtr`）。

⇒ ★★★★ **次の一手**: ⚠️ ★★ **⑴ push 後に `gh run list` で ubuntu 脚を読む**（Core を触っている・WSL は無い＝第313 ⑶。Windows の full run は本便で緑）。✅ **⑵ 第326 ⑶「オクターブ移調楽器は `pitch concert` でも記譜不動」はユーザー確認済み**（本便の末尾・§3 の行を書き換えた。機構は変えていない＝`ConcertShiftSemitones` そのまま）。★ **⑶ smart typing の小さな穴（未起票・ユーザー報告待ち）**: ✅ 和音のメンバーへの `\`／`@` は便の末尾で入れた（⑵）／`c4(` の直後に `)` を打つと `c4)(`（旧は `c4()`・どちらも無意味）／`@` と `(` の change-event 経路は一瞬カーソルが前後して見える（設計どおり）／**smartTyping.ts に自動テストが無い**（planner は純関数だが `vscode` を import する同一ファイルに居る＝切り出せば node で回せる。着手するなら次便）。**⑷ 以下は第326 の並び**: T6＝スラー復元 138 冊／T7 の C 37 冊／`audit/lpreg` 取り直し／§2 U8c・U8b・U8／§2 A/B/D/E／§2 C⑴／§2 G／低優先 `marks stacked`/`beside`／「なぜ `perf-v2bow1k` だけ落ちるか」。⚠️ **承認待ち**: §2 T2 の 1 件＋§2 T5 の ⏸ 1 件＋第323 ⑶ の「戻すか」（第326 ⑶ は本便で確認済み）。⚠️ **§2 F の開いている項目は第320 の ⒲ 1 件**。⚠️ **リリース 0.6.0**: CHANGELOG（拡張側）に本便 5 項を足した・版番号 bump と tag は未着手。

## 2. 開いている作業

### U. ユーザー報告（2026-08-29・第286 起票）← **順に着手。ユーザーが優先度を与えた**

> ⚠️ **この 3 点は「読み手が紙とプレビューで見た」もの**で、台帳の残差とは別の族。
> ★ **⑵⑶ の本はユーザーの実コーパス** `scratch\ベースタブLy\`（未追跡・300 冊級）。
> **追跡 573 冊には無い**ので、閉じるときは**射程を実コーパス側でも数えること**。

- **U1. ✅ 閉じた（第286・報告は当たっていた）＝3 小節以上の `repeat percent` は LP のスラッシュ 1 本になり、LYS2014 は退役**。
  **報告**: 「LP は 3 小節以上を repeat percent で繰り返した場合、後続の最初の小節のみにスラッシュを
  書いて、次の二つの小節は空欄で残していた**と思う**。LP の双子をよく見て。」⇒ **そのとおりだった。**
  ★★★ **裏取りは LP のソースで割れた**——**`lily/percent-repeat-iterator.cc:86-99` の分岐は
  `== mlen` と `== mlen*2` の 2 つだけで、`else` は 1 本**。**その else は body の*全長*を持つ
  `RepeatSlashEvent` を 1 個出すだけ**なので、**反復は「先頭にスラッシュ 1 本＋残りは空小節」**になる。
  **`scm/music-functions.scm:377-389 calc-repeat-slash-count`** が本数を決め（**音価が揃っていれば
  `max(log-2,1)`＝全音符なら 1 本／揃っていなければ 0**）、**`lily/slash-repeat-engraver.cc:57-65`** が
  **0 なら `DoubleRepeatSlash`・それ以外なら `RepeatSlash`**。**絵でも確かめた**（2.26.0）:
  `scratch/p282/wholebody3.png`（3 小節 body＝スラッシュ 1 本＋空 2）・`wholebody8.png`（8 小節 body＝
  スラッシュ 1 本＋空 7）・`scratch/p286/lp/ragged.png`（3/4 で `e4. e8 e4. e8`＝**2 本スラッシュ**）。
  ⚠️⚠️ ★★★ **第282〜285 が「LP 自身の分岐」と書いていた根拠は grob の *description* だった**
  （`RepeatSlash`／`DoubleRepeatSlash` が自分を "repeating patterns shorter than a single measure" と
  説明している）。**description は用法の要約であって分岐ではない**——**iterator にその条件は無い。**
  ⇒ ★★ **「LP はこうしている」を書くときは、*description* ではなく*分岐の在るファイル*を読むこと。**
  **`WholeMeasureRun` と `Ragged` は Lily# が発明した細分**で、**LYS2014 はその発明の自白だった。**
  ★ **射程は実測**: **SVG 全数掃き 1519 冊（base/head 同時刻の 2 パス）＝SAME 1484 / MOVED 35**
  （**ユーザー実コーパス 32 ＋ scratch 3・追跡 573 冊は 0 冊＝snapshot 不動**）。
  **`lysc check` は LYS2014 の 96 行が消えただけ・終了コードの変化 0。**
  ⚠️ **ユーザーの実譜面は本当に動く**（例: `Walk.lys` は percent 記号 364 → 298・ページ高 8450 → 6760）。
  ⚠️ ★★ **掃きの 2 パスの間にユーザーが `Walk.lys` を編集した**（mtime 11:45:31）ので、
  **1 回目の対では Walk.lys に「LYS2002 が 4 行消えて 5 行増えた」という*偽の*差が出た**。
  **同じ exe で同じファイルを続けて叩いたら base ≡ u1**。⇒ ★★★ **ユーザーの生きたコーパスを母集団に
  使うときは、base と head を*続けて*取ること**（§0 の「A/B の before はその場で写す」の掃き版）。
  ★ **残った小物 1 つ＝スラッシュの小節内 X が LP と少し違う**（3 小節 body で LP は小節線から 2.09、
  Lily# は 1.09）。**構造ではなく spacing の話**で、**第282 が移植した拍スラッシュと同じ配置規則**を
  共有している。**触るなら拍スラッシュ側の番人と一緒に。**


- **U2. ✅ 閉じた（第286）＝和音行が `rit.` を避けるのは 1 段目だけだった**（**ユーザー報告**・
  `scratch\ベースタブLy\Untitled-6.lys` の五線 3 行目）。
  ★★★ **これは 2026-08-28 の修理の*残り半分***——**その便は「`@rit` は外側インクで、
  上に立つ行はそれを避けねばならない」と決めて `MultiStaffLayouter.BuildAllStaffSkylines` で
  `sky.Up` に合流させた**が、**行が 2 段目以降に立つときは*そこを通らない***。
  **後続段の行は loose-line の鎖（`LayoutEngine.Rows.LeadingLinesOfSystem`）が置き**、
  **その閉じの距離は五線の*内側*シルエットで測っていた**——**外側インクはそこに居ない。**
  ⇒ **同じ本が 1 段目では避け、3 段目では突き抜ける**。**読み手はそれを見て 2 度目の報告をした。**
  ★ **実測**（`scratch/p286/u2/twosec.lys`）: **和音行は五線上 1.50 → 3.40**、`rit.` は 1.01 で
  **隙間が 1 段目と同じ 2.39 になった**。**mark も避ける**（mark は 2.66）。
  ⚠️ ★★ **切り分けの決め手は計器 1 本**——**`LeadingLinesOfSystem` が sys=1 でしか走らない**ことを
  stderr に出して初めて「1 段目と 2 段目は*別の経路*」と言えた。**それまでの仮説（節の再生・volta・
  歌詞）は全部外れ**（`twice.lys` は再生でも再現せず、`volta.lys` は 3 段でも再現しない）。
  ★ **射程は 1 冊**: **SVG 全数掃き 1519 冊（同時刻 2 パス）＝SAME 1518 / MOVED 1**——
  **動いたのは報告された `Untitled-6.lys` だけ**。**`lysc check` はバイト同一。台帳不動。**
  ⚠️ **追跡コーパスは 0 冊＝また盲**だったので**番人を置いた**（`test/chordrow-rit-second-system`）。
  **毒（合流を止める）で 223 枚中この 1 枚だけが赤。**
  ⚠️⚠️ ★★★ **✅ 第291 で閉じた（点 `6e4f8e69` ＋ 移植 `2ce6f992`・ユーザーが選んだ項）＝残っていた大きい問い「閉じの距離は*内側*でよいのか」**。**第286 が入れたのは「text spanner だけ外側から足す」という狭い直し**で、**LP は loose line を
  VerticalAxisGroup の*スカイライン*（＝置かれた外側 grob 込み）に対して配る**
  （`axis-group-interface.cc:860-985` ＝ `min_offsets` の walk が読む `vertical-skylines`・`align-interface.cc:207`）。
  ⇒ **`LeadingLinesOfSystem` は部屋の per-staff スカイライン（`SystemPlacements.StaffSkylines`）を読むようになった**——**譜間の端（`ComputeBetweenStavesEnd`）が前から読んでいたのと同じ物**で、**text spanner の特別扱いはその中へ退役した。**
  ★ **点は `lyrics.chord-row.between-systems.dynamic.*`**（本 DUR/DUN・プローブ `dynamic-under-row.ly`）: **−3.188075804 → −0.000075804**、**対照は +2.5e-08 で不動**。**射程は 1591 冊 SAME 1589 / MOVED 2＝本便のプローブだけ**（**ユーザー実コーパス 0 冊**）。
  ⚠️ **残っているのはプレリミナリ・パスの側だけ**（`ClosingProfileWithoutTheRoom`）——**部屋がまだ無い段では外側の層が取れない**。**ページを少し小さく見積もるだけで本パスが直す。観測者ゼロ。**

- **U3. ✅ 閉じた（第286）＝行頭の音符が「自分の住所」を持っていなかった**（**ユーザー報告**・
  `scratch\ベースタブLy\Walk.lys` の **L15・`ees,1`**）。**Core が犯人で、エディタは無罪。**
  ★★★ **原因は 1 つのプロパティ**——**`GreenNode.LeadingTrivia` は `virtual => null` で、
  override しているのは*トークンだけ***。**合成ノード（音符・和音・repeat）は、先頭トークンが
  改行とインデントを抱えていても「前方トリビア 0」と答える**ので、**`SyntaxNode.Span` が
  それを素直に足していた**＝**行を開くノードは自分の前の空白まで自分だと言っていた。**
  ⚠️ **行内の空白は*前の*トークンの後続トリビアになる**ので、**食い違うのは改行の後だけ**
  ——**だから「行の最初の音符」だけが外れ、他の 536 個は何便も正しかった。**
  ★ **直しは 2 段で、両方に生きた毒がある**: **⒜ collector が `.Position`（トリビア込み）ではなく
  `.SourceStart`（＝`Span.Start`）を読む**／**⒝ `Span` が先頭・末尾の*終端*まで降りて
  トリビア幅を取る**（`GreenNode.GetLeadingTriviaWidth` / `GetTrailingTriviaWidth`）。
  ⚠️⚠️ **⒜ だけでは 1 mm も動かない**——**実測した**（プローブの `data-pos` が 74 のまま）。
  **合成ノードでは `Span.Start == Position` だったから**で、**そこで初めて ⒝ に行き着いた。**
  ★★ **⒜ の置換はコンパイラに検証させた**——**`.Position` → `.SourceStart` を collector 全体に
  当てて建て、`SourceStart` を持たない受け手 6 件を*コンパイラが弾いた***
  （`GreenSite`・タプル・`PitchTraceEntry`）。**そのうち 3 件は増分 resume の番地で、
  `Position`（FullSpan.Start）が正しい側**——remark にそう書いてある。
  ★ **射程と、それが*住所だけ*であることの証明**:
  **SVG 全数掃き 1519 冊＝SAME 922 / MOVED 597**、⇒ **動いた 597 冊の SVG から `data-pos` の値を
  伏せて突き合わせると、597 冊すべてが一致・それ以外で違う本は 0 冊**（`scratch/p286` の突き合わせ）。
  **`lysc check` は 135 冊 466 行が動き、そのすべてが (行,桁) だけの違い**——
  **文言・件数・終了コードの変化は 0**。**抜き取ると桁は例外なく「空白 → 最初の実文字」**
  （例: `beam-multiplicity-over-rests.lys(17,1)` → `(17,5)` で `c16` の頭）。
  **snapshot は 93 枚が再生成され、456 行すべてが `data-pos` の値だけの差**。**台帳不動。**
  ⚠️ **番人は `LineLeadingSourcePositionTests`**（行頭 2 音＋行内 2 音＋診断の桁）。
  **毒（`Span` を旧式に戻す）で 5 本中 3 本が赤、行内の 2 本は緑**＝**対照が効いている。**

- **U4. ✅ 閉じた（第286・ユーザー決定）＝タブの `as numbers` はタイも描かず、`as` 省略時は*スコアが答える***。
  **決定**: 「`as numbers` は数字だけ、タイ先の数字も描かない。`as full` は拍子記号もタイも休符も全部描く。
  ただしタイ先の数字だけは描かない。」＋「`as` を書かない場合は、**同じ part を staff で描画していれば
  numbers**、していなければ **full**」。
  ⚠️⚠️ ★★★ **起票の前に私が事実を 2 つ間違え、ユーザーはその誤った説明の上で決定した**——
  **⒜「`as numbers` / `as full` という文法は無い」**（**両方とも既にあった**。`TabRenderVocabularyValidator`
  の語彙が `{numbers, full}`）／**⒝「`as full` は符尾・連桁の新規実装が要る」**（**符尾は既にあった**。
  連桁だけが無い）。**GRAMMAR.md の生成規則だけを読み、fixture もパーサも語彙も grep しなかった。**
  ⇒ ★★★ **「その綴りは無い」は*生成規則を読んだだけ*では言えない。** **言う前に、語彙・パーサ・
  fixture の 3 つを grep すること**（`test/tab-as-numbers.lys` は**ファイル名で**在ることを叫んでいた）。
  ★ **誤りに気づいた時点で前提を訂正してユーザーに再提示し、そのうえで「そのまま進めて」を得た。**
  ★ **実際に足りなかったのは 2 点だけ**: **⒜ タイが両モードで描かれていた**（`SharedRenderer.Tab` の
  注記が「Ties still print」と*書いていた*——**LP に訊いていない乖離**）／**⒝ 既定が文脈を見ていない。**
  **「タイ先の数字を full でも描かない」は既に両モードでそうなっていた**（LP の full は*出す*ので、
  ここは既に意図的な乖離＝`tab-note-head::handle-ties` の片翼だけ移植した形）。
  ★★ **LP の実測**（`scratch/p286/lp/`）: **既定 TabStaff＝数字だけ**（拍子も調号も無し）／
  **`\tabFullNotation`＝拍子は出るが調号は出ない**——**`ly/engraver-init.ly:1214` が
  `\remove Key_engraver` を、隣で `Accidental_engraver` も外している**（**stencil ではなく engraver ごと**
  なので `\tabFullNotation` では戻らない＝**タブに調号という概念が無い**）。**タイは
  `:1271-1276` で `Tie/RepeatTie/LaissezVibrerTie.stencil = ##f`、その 2 行下で
  **スラーは stencil を保って動くだけ**（`slur::move-closer-to-tab-note-heads`）
  ——**だからタイだけを止め、スラーは触っていない**（`tab-grace-slur` の曲線 4 本が不動で裏取り）。
  ★ **抑止は `ElementCoordinator.LayoutTies` の 1 か所**（描画側ではなく**レイアウト側**——
  **刷らない弓がスカイラインに部屋を予約してはいけない**）。**既定は `RenderSpecParser.StaffRenderedParts`**
  （ループの*前*に 1 度作る＝`tab` を `staff` より上に書いても同じ答えになる）。
  ★ **射程**: **SVG 全数掃き 1519 冊（同時刻 2 パス）＝SAME 1266 / MOVED 253**
  （**ユーザー実コーパス 232 ＋ 追跡 11 ＋ scratch 10**）。**`lysc check` はバイト同一。台帳不動。**
  ⚠️ ★★ **fixture の 7 冊には `as full` を*明示*した**——**その本の主題（連桁・付点・tuplet 番号・
  タブのタイ・スクリプトが符尾を避けること）が numbers では消える**ので、黙っていると
  **通り続けながら何も測らなくなる**。**残り 10 冊は再生成し、主題が生き残っていることを 1 冊ずつ機械で確認**
  （bend の曲線 40 本・% 記号の点 4 組・× 6 個・技法文字 16 個などが不動）。
  ⚠️⚠️ ★★★ **`audit/lpreg` の LP 照合プローブが 2 冊巻き込まれた**——**`tabdot.lys` は双子が
  `\tabFullNotation` なのに Lily# 側が numbers に落ちる**ので `as full` を明示（**黙っていたら
  付点が消えて CLAIM を測らなくなる**）。**逆に `tabtie-probe.lys` は双子が*素の* `\new TabStaff`**
  なので、**今回の変更で初めて双子と絵が一致した**（**それまで Lily# だけが弓を引いていた**）。
  ⇒ ★★ **LP 照合プローブは「双子がどのモードか」を本文に書くこと。**
  ★ **番人 2 つ**: `test/tab-tie-default-numbers`（素の staff+tab＋タイ＝弓は 1 本・タブは 0 本）と
  `TabRenderVocabularyTests.WithNoClause_TheScoreChoosesTheStyle`（既定を明示綴りとの*等値*で述べる）。
  **毒の効き方が違う**: **タイ抑止を止めると 1 枚だけ赤／既定を full 固定に戻すと 11 枚＋規則テストが赤。**
  ⚠️⚠️ ★★★ **✅ 残債は無かった＝「`as full` の連桁が未実装」は誤り**（2026-08-30・第294 が着手前に
  測って撤回。**§5.0「引き継がれた処方箋は、着手する前にその*診断*を 1 回測る」**）。
  **タブの連桁は主・副・切れ端まで在り、LP の量子器を通っている**——**`TabBeamQuant.Compute` が
  `BeamScoringProblem` に*弦の線*を stem position として渡し、TabStaff の 2 つの override
  （`Beam.beam-thickness 0.32`・`Beam.length-fraction 0.62`）で解く**。**実測**（`scratch/p294/b16`）:
  **2/4 に 16 分 8 個 ＋ 8 分と 16 分の混在で、タブ側に polygon 8 本＝主梁 4・副梁 4（切れ端 2 本を含む）**。
  **`staff` を外した*タブだけ*の本でも同じ**（**梁は音符側の連桁検出に依存するが、その検出は
  譜の有無に依存しない**）。**符尾も旗も点も休符も拍子も同様に在る**（`SharedRenderer.Tab.cs:474-482`）。
  ⚠️ ★★★ **反証は起票の 12 行上に最初から在った**——**上の「fixture の 7 冊には `as full` を明示した」が
  「その本の主題（**連桁**・付点・…）が numbers では消える」と書いている**。**`test/tab-beam-slope`・
  `test/tab-beam-script`・`test/tab-staccato-beam-side` は全部タブの梁の本**で、
  **`0238f5b1`「Slope the tab beam along the fret contour」まで在る。**
  ⇒ ★★ **同じ節の中で 1 つの量について 2 つのことを書いていたら、grep ではなく*その節を通しで*読む。**
  **⒝ の訂正（「符尾は既にあった。連桁だけが無い」）は、訂正した側もまだ半分外していた。**


- **U5. ✅ 閉じた（第288）＝タブ譜と歌詞行が重なるのは「タブの下の弦が profile の外に居た」から**
  （**ユーザー報告 2026-08-29**・`scratch\ベースタブLy\Untitled-6.lys`。**その本のスコアは
  `chords / tab / lyrics` で、タブが melody の唯一の譜**）。
  ★★★ **原因は 1 つの量が 3 か所で綴られていたこと**（§5.2.1②）——**「この譜は何ユニット高いか」**。
  **⒜ `SkylineBuilder.SeedStaffSymbol` は*どの譜も*スコアの公称の半分（±2.050000 のインク）で
  五線を撒いていた**——**その remark 自身が "this builder has ONE staff height, not one per staff"
  と書いていた**。**6 弦タブの下端の弦は refpoint の 3.800000 下**なので、**タブは自分の一番下の弦を
  1.750000 だけ自分の profile の*内側*に持っていた**。**⒝ `LyricEngraver.ResolveAnchor` は
  アンカー譜の*上端*から refpoint へ降りる段を同じ公称 2.000000 で踏んでいた**
  （`anchorOffset`＝`staffBottom + BasicDistanceBelowBottomLine - RelatedStaffBasicDistance`）。
  ⚠️ **⒞ 同じ量の 3 つ目の綴りは*既に正しかった***——**system の silhouette は
  `StaffSpan` が置かれた `StaffLayout.Height` を読んでいて 7.500000 を知っている**
  （`page.tab-only.first-staff-refpoint` がそれで閉じた）。**その entry が
  「DO NOT CLOSE IT BY SPECIAL-CASING TAB — 同じ公称 4.000000 を SkylineBuilder も仮定している」と
  名指ししていた棚が、これ**。<!-- ledger: page.tab-only.first-staff-refpoint = 0 -->
  ★★★ **なぜ「真ん中の段だけ」だったか＝インクが床を隠していたから。** **段 1 と段 3 は
  範囲外の音でフレット番号が五線の*下*にぶら下がり**、**その番号が profile の底を弦より下へ押し下げる**
  ので、**間違った床は一度も binding にならなかった**。**段 2 はフレットが全部弦の内側**なので、
  **床がそのまま出た**。⇒ ★★ **「一部の段だけ壊れる」は「その段だけ、間違った既定値が露出している」
  の顔をしていることがある。**
  ★ **LP の実測**（新しい対 `audit/lp-geometry/probes/tab-lyric-row.ly`・**grob の dump**）:
  **TBL1（タブ＋歌詞）6.120115 ＝ 五線のインク 3.800000 ＋ 音節の ascender 1.820098 ＋ padding 0.500000**、
  **TBL2（対照＝通常譜）5.500001 ＝ `nonstaff-relatedstaff-spacing` の basic-distance そのもの**。
  **Lily# は 4.370000（＝2.050000 ＋ 1.820000 ＋ 0.500000）で、今 6.120000。**
  ⚠️⚠️ ★★★ **SVG から測ってはいけない**（`audit/lp-geometry/README.md`）——**私は最初 LP の SVG から
  6.0147 を読み**、**それは LP の*描いた baseline* であって VerticalAxisGroup の refpoint ではなかった**。
  **正しい 6.120115 は grob の dump から。0.105 ずれる。**
  ★★ **第287 の第一仮説（`LayoutEngine.Rows.cs:250` の `halfStaff`）は今も外れたまま**で、
  **その便が書いた「次の一手は `LyricEngraver.ResolveAnchor`」は当たっていた**——**⒝ がまさにそこ。**
  ★ **毒は 2 本とも生きていて、効き方が違う**: **⒜ を戻すと 3 枚が赤**
  （`test/tab-lyrics-inside-strings`・`test/tab-below-range`・台帳 `lyrics.tab.staff-to-lyric`）／
  **⒝ を戻すと 2 枚**（歌詞のある本と台帳だけ。`tab-below-range` は歌詞が無いので動かない）。
  ⚠️ ★★★ **対照が*別の欠陥*を 1 つ釣った**（**✅ 第291 で閉じた。以下は当時の観測と、閉じた理由**）——**TBL2（通常譜）は LP 5.500001 に対し Lily# 4.369960**
  （**残差 −1.130041486**）。**base の exe でも head の exe でもバイト同一**なので**第288 の代償ではなかった**。
  **LP の spec は floor ではなく*バネ***（`(basic-distance . 5.5) (padding . 0.5) (stretchability . 1)`・
  `ly/engraver-init.ly:649-652`・**`minimum-distance` は無い**）で、**Lily# はそのバネを*最小*に置いていた**。
  ⇒ ★★★ **真因は「部屋」だった**（第291・§1 参照）——**`BuildLooseChainEnds` はページ最後の塊を
  `anchor - MarginBottom` で閉じており、それは*このページの crop*。**Lily# は単一ページを内容に合わせて
  裁つ**（既定の分岐・意図的な乖離）**ので、裁つ元になっている量が*この塊の予約（＝alignment 最小）*そのもの**
  ——**部屋を予約から読み戻していたので輪が閉じていた**。**実測: 部屋 4.407001213 対 鎖の最小の和
  4.369959965 ＋ 0.037041248 ＝ 4.407001213（9 桁一致）。最小の和ちょうどに解かれた鎖は、バネが何であれ床から離れられない。**
  ⚠️ **`...last-system` の 2 点が exact だったのは「最後の段」だからではなく、その本の crop がたまたま
  鎖の理想より広かったから**（ROWF は部屋 11.5 対 理想 5.5＋2.8＋1.0）——**両点の `why` が書いていた
  「room is unbounded」は Lily# についての未実測の主張**で、第291 が訂正した。
  ⚠️ **`5.5` を minimum-distance として入れて閉じてはいけない**——**LP に無く、`lyrics.band-floor` が
  「5.5 は縮む」ことを実測で持っている。**<!-- ledger: lyrics.tab.staff-to-lyric = -0.000155000061 --><!-- ledger: lyrics.tab.control.staff-to-lyric = 0 -->

- **U6. ✅ 閉じた（第288・ユーザー報告 2026-08-29）＝`@rit` は「自分の次の演奏」で終わっていた**
  （`scratch\ベースタブLy\Untitled-6.lys`・**報告は「A の rit は 6 小節あるのに A2 の rit は 1 小節しかない」**）。
  ★★★ **1 小節のほうが正しい。** **`DetectTextSpanners` は「同じ譜の次の rit/accel」で span を閉じる**が、
  **`musicMarks` は*演奏された*曲のマーク**なので、**form が繰り返す section は 1 演奏につき 1 個ずつ
  同じマークを積む**——**同じ `SourcePosition`・違う小節**。**その 2 個目が 1 個目の終端として拾われ**、
  **1 本目が「間に挟まった全部」を貫いていた**（この本では **section B 丸ごと**）。
  ⚠️ **`m != mark` では見えない**——**2 つの演奏は 2 つの `MusicMarkItem`**。**同一性は
  `SourcePosition`**（**マークは全部 syntax の `SourceStart` で作られる**ので、**位置が一致するのは
  「1 つの書かれたマーク」のときだけ**）。
  ★ **実測**（最小対 `scratch/p289`）: **1 回演奏 `r1` は 11.29→21.40 の 1 小節**／
  **2 回演奏 `r2` は 11.15→55.67 と 58.62→67.54 だったのが、11.15→20.07 と 58.62→67.54 で*等長***。
  **報告本は 3 段にまたがる 3 片 → 12.02→35.87 と 8.80→33.50 の各 1 小節。**
  ★ **射程**: **全数掃き 1567 冊＝SAME 1557 / MOVED 10**、**`lysc check` は 1567 冊バイト同一**。
  **動いた 10 冊のうちユーザー実コーパスは 1 冊＝報告された本だけ。**
  ⚠️⚠️ ★★★ **`scratch/p286/u2/twice.lys` が 10 冊に入っている**——**第286 が「同じ section を 2 回演奏」を
  測るために*自分で建てた*プローブ**で、**その中で 1 本目が 1 段まるごと・2 本目が 1 小節だった**のに、
  **建てた便はそれを見ていない**。⇒ ★★ **プローブは「見に行った量」しか見せない。**
  ⚠️ **remark が書いていて*コードがしていなかった*ことを 2 つ直した**:
  **⒜ tempo 変更は span を終わらせない**（**探索リストは rit/accel だけ**——**bar 1 の rit と bar 5 の
  `tempo 160` で実測: 4 小節ではなく 1 小節＝fallback**）／**⒝ `LILYPOND-REF` は engraver に付いていたが
  この規則自体は LP に無い**（**LP は `\startTextSpan`〜`\stopTextSpan` で、Lily# には終端の綴りも
  長さの引数も無い**——**語彙・パーサ・fixture の 3 つで確認**）。
  ⚠️ ★★ **単体テストが `sourcePosition: 0` を 2 つ並べていた**——**parse された本では起こり得ない状態**で、
  **しかもこの直しが弾かねばならない形そのもの**。**data を実在の 2 位置に直し、規則と対照の
  テストを 2 本足した。**
  ⇒ ✅ **この問いは閉じている＝終端の綴りは `@!rit` で入った**（**2026-08-31・第306 に*ユーザーの指摘で*気づいた。起票時 2026-08-29 の記述が古いまま残っていた**）。**実測で裏取り**: **`@!rit` / `@!accel` / `@!rall` / `@!textSpan` が在り、`GRAMMAR.md` は「an end is REQUIRED」と書いている**。**閉じない `@rit` は*無言ではなく警告*で、その文面が `@!rit` を名指す**（`scratch/p306/rit1_bare.lys` 実測: 「a text spanner is never closed … write '@!rit'」）。
  ⚠️⚠️ ★★★ **教訓は §5.2 の「引き継ぎは書いた時点のスナップショット」の 1 例**——**この 1 行は「要ユーザー判断」の札を付けたまま何便も残り、第306 が「あなたが判断すべきことは何か」を*一覧にして初めて*ユーザーが「もう実装した」と言った。**⇒ ★★ **未決定の札は、貼った便の次に一度は*実コードで*確かめること**（→ RULES §5.3）。
  ⚠️ **以下は起票当時（2026-08-29）の実測で、`@!` が入る前の姿**——**`@rit` の側は上のとおり警告になったが、`@sustainOn` と `@ottava` を閉じなかったときの振る舞いが揃っているかは*確かめていない*。同じ棚に触るならそこから**:
  ⚠️⚠️ ★★★ **「閉じられなかった span」に言語は*3 つの違う答え*を
  持っている**（2026-08-29 実測・`scratch/p289/pair1`・`pair2`・`r1`。**どれも `lysc check` は
  `No errors found`**）:
  **⒜ `@sustainOn` を閉じないと *何も描かれない***（**"Ped." の文字ごと消える**——**しかも無言**）／
  **⒝ `@ottava` を閉じると*描かれ*、12.01→33.64 まで伸びる**（**五線の右端は 60.37 なので
  「最後まで」でもない・独自の長さ**）／**⒞ `@rit` を閉じないと 1 小節**。
  ⇒ ★★★ **1 つの問いに 3 つの答えがあり、どれも LP のものではなく、どれも読み手に告げられていない。**
  **これは §5.2.1② が言語のレベルで起きている形。**
  ⇒ ★★ **だから「B か C か」は LP 忠実かどうかの話ではない**——**C は 4 つ目の答えを足す**が、
  **B はこの族に答えを 1 つにし、⒜⒝ を後から寄せる先を作る。**
  ★ **糖衣と既定長は分離できる**——**`@rit` を短く保ったまま終端を必須にできる**（**`@rit` ≡
  `@startTextSpan("rit.")` の START だけの糖衣**）。**C はこの 2 つを混ぜている。**
  ⚠️ **B の代償は「終端を書き忘れると印が丸ごと消える」**。**LP はそこを warning で補っている**が、
  **Lily# は LYS 診断で*どの位置か*まで言える**ので、**LP より良い形にできる**（⒜ が今まさに
  「無言で消える」をやっていて、それが最悪だという証拠になっている）。
  ⚠️⚠️ ★★★ **兄弟も同じ欠陥だった＝`HairpinEngraver` の `nextMark`**（`fd46cdda` の直後に実証して
  `4db35c23` で閉じた）。**この項は最初「同じ形をしているが実証していない・`@cresc` のプローブが
  どれも描かなかったので、engraver ではなく私のプローブが外れたほうが確からしい」と書いていた。**
  **プローブが外れていたのは当たり**で、**外れ方が記録に値する**——**Lily# の `@cresc` は
  「cresc.」という*文字*ではなく*ウェッジ*を描く**ので、**SVG を "cresc" で grep すると
  「何も無い」と出る**（**LP は `\cresc`＝テキストスパナと `\<`＝ウェッジを分けているが、
  Lily# はどちらも `@cresc`**）。⇒ ★★★ **「描かれていない」と読んだら、まず*同じ本をその印だけ
  抜いて*描き、差分を取ること**（`c5` − `c6` でウェッジ 2 本が残差として出た）。
  ★ **実測**（`scratch/p289`・**ウェッジは `stroke-linecap="round"` の 2 本**）: **1 回演奏 `c7` は
  幅 7.58／2 回演奏 `c8` は 17.18 と 7.58 → 直して 7.58 と 7.58。**
  ⚠️ **コーパスでは出ようがなかった**——**同じ演奏の中に強弱が 1 つでもあると、繰り返しが届く前に
  ウェッジが閉じる**。**だから fixture は「強弱を 1 つも置かない」と冒頭に書いてある。**
  ★ **射程**: **掃き 1572 冊＝SAME 1569 / MOVED 3**（**新しい fixture と本便のプローブ 2 冊だけ・
  ユーザー実コーパス 0 冊**）・**`lysc check` は 1572 冊バイト同一。**
  ⚠️ **隣の `nextDynamic` は直していないし実証もしていない**——**section の中で cresc より*前*に
  書かれた強弱は、その*次の複製*では cresc より後に来る**ので終端になり得るが、**`SourcePosition`
  では見分けられない**（**強弱と cresc は別の書かれたマークなので「同じ源」関係が無い**）。
  **見分けるには item が持っていない「何回目の演奏か」の番号が要る。**

- **U7. ✅ 閉じた（第293・ユーザー報告 2026-08-30）＝容器の中のリハーサル記号が黙って消えていた**
  （**ユーザー指示は「残債の返済をいったんやめて、私に見えている問題を直して」**——**どれが見えているかは
  名指されなかったので、最後に触られた本を掃いて探した**。**当たったのは
  `scratch\ベースタブLy\ポリリズム.lys`＝A・B・C・D と書いてあって A しか出ない**）。
  ★★★ **原因は「印」ではなく「歩き」**——**リハーサルマークは item が `ProcessMusicNode` の
  *statement アーム*で作られる唯一のマーク族**で、**そのアームは「音符の*兄弟*として walk が
  配った印」しか見ない**。**降りるのは `MusicSitesLazy` だけ**なので、**自分の walk を持つ容器
  （`volta.Items`・`repeat.Body.Items`・`cue.Body.Items`・`tuplet.Body.Items`）の中では
  site にならず、アームが 1 度も走らない**。**直しは `CollectArticulations` に 1 本アームを足すこと**
  ——**`@rit`・`@text`・`@sustain`・compound `@ottava` は前からそこで作られている。**
  ⚠️ **de-dupe（`MusicMarkExistsAt`）も一緒に動かすこと**——**1 つの part を staff と tab の
  2 段に描く本は音符を段ごとに歩く**ので、**guard 無しだと全文字が 2 度出る**（§1 ⑶）。
  ★ **射程は実測**: **1603 冊 SAME 1554 / MOVED 49＝ユーザー実コーパス 45 冊・120 文字 GAINED /
  0 LOST**（**gained は全部リハーサルのラベル**）。**`lysc check` はバイト同一・台帳不動。**
  ⚠️ **番人は 3 つ**（`AMarkInsideAContainerThatOwnsItsWalk_IsCollectedOnceAndPrinted` 5 ケース ＋
  `AMarkIsPrintedOnce_WhenOnePartIsScoredOntoAStaffAndATab` ＋ snapshot
  `test/rehearsal-marks-inside-containers`）。**毒 2 本の効き方が違う**（§1 ⑸）。
  ⇒ ★★★ **✅ 2 つの問いはユーザーが答え、同じ便のうちに入った**（`b702e47f`・§1 ⑺〜⑽）:
  **⒜ ✅「1 度だけにする」** ⇒ **コードは既にそうだったので*変えずに留めた***
  （`AMarkInsideAnUnfoldedRepeat_IsPrintedOnce_NotOncePerUnfolding` が規則と乖離を述べる）。
  **LP は unfold を*展開*するので engraver が見る頃には N 個の書かれた印になり N 度刷る**が、
  **Lily# は「書かれたもの」を数える**——**staff ＋ tab で全文字が 2 度出るのを止めているのと同じ規則**。
  **射程は実コーパス 0 冊**なので、**これは修理ではなく*規則の選択*で、だからユーザーのもの。**
  **⒝ ✅「同じ扱いにする」** ⇒ **`LYS4019`＝書かれたのに刷られないリハーサル記号を、その位置で言う**
  （`RehearsalMarkEngravedValidator`）。**インクは 1617 冊 1 枚も動かない。**
  ⚠️ **これは*計器を先に作って*決めた**——**箱の `data-pos` は印の `SourceStart` そのもの**なので
  **書かれた `@mark` を 1 つずつ絵の上で引ける**。**263 冊 2244 個中 2242 個が描かれている**
  （**残る 2 個は `@mark` が構文として通らない本で、既に 1 冊 3 行のエラーを出している**）。
  ⚠️⚠️ ★★★ **その計器が 1 度嘘をついた**——**`note~@mark("X")` では印のノードの span が `@` ではなく
  `~` から始まる**ので、**1 度目の集合比較は「37 冊 58 個が消えている」と出た**（**うち 56 個がこの形**）。
  **そのまま読んで着手していたら、正しく描けている本に 1 便を使っていた**（§1 ⑻）。
  ★ **LYS4019 が今なお捕まえる形は 3 つ**: **どの form も演奏しない `section`**／
  **どの score も描かない `part`**／**装飾音符**。**ユーザーの 326 冊は 1 冊も警告を得ない。**
  ⚠️ **問うのは*最初の score* 1 つだけ**（`SemanticValidation.TryCollect`）。
  **後続の score だけが名指す part を持つ本は「描かれない」と言われる**——**ディスク上 0 冊**。
  **出たら「score ごとに問う」が直しで、答えを弱めるほうではない。**

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

- **U8c. ✅ 閉じた（第316）＝反転符頭の shift も ledger も click 箱も「その符頭のフォント」から出る。
  ⚠️ そして*起票は狭かった*——scale を渡していたのは cue だけではなく、grace も第313 以来ずっとだった**
  （**第308 起票・第316 が測って閉じた**）

  ⚠️⚠️ ★★★★ **起票の「cue はまだ scale を渡している」は当たっていたが、**
  **`SharedRenderer.DrawChord` の `headScale` は `chord.IsCue ? CueScale : 1.0` ではなく
  `GrobFontSize.ScaleOf(chord, NoteHead)` だった**——**つまり grace も同じ経路を通っていた。**
  ⇒ ★★★ **⒝2 が grace の和音を普通の renderer に渡した日（第313）から、grace の二度は
  *予約 0.852938 対 描画 0.857209* で割れていた**——**`GraceColumnHeads` は
  「THE one spelling: 予約も skyline も renderer もここに訊くので、ある幅で予約して別の幅で描くことはできない」
  と*自分で書いている*のに、renderer だけがそこを通らなくなっていた。**
  ⇒ ★★ **起票の文面を grep して直したら片肺で終わる。*どの式が実際に呼ばれているか*を読むこと。**

  ★★★★ **正典 2.26.0 が 3 つの regime を 1 冊で答えた**（`scratch/p316/sec-dump.ly`＝grob dump・
  **答え＝第 2 符頭の左 − 第 1 符頭の左**）:
  **`<c' d'>4` 1.239200（＝Design20 1.304200 − 0.065）／`\grace { <c' d'>16 }` **0.852939**
  （＝**Design14** 1.298161 × magstep(−3) − 0.065）／`\new CueVoice { <c' d'>4 }` **0.750349**
  （＝**Design13** 1.294282 × magstep(−4) − 0.065）。**
  **20 を縮めた読みは 0.857209 と 0.756597＝+0.004270 と +0.006248。Lily# はこの 2 つを刷っていた。**
  ★ **幅も裏取りになっている**: **grace の符頭の実測幅 0.917939 は 1.298161 × 0.707107**——
  **20 を縮めたら 0.922207 で 4 桁で外れる。** ★ **対照 `\grace { <c' e'>16 }` は両読みとも 0**
  ——**二度を書かない本はこの差を 1 冊も観測できない。**

  ★★ **直したのは「箱をフォントに訊く」1 語**（`SharedRenderer.Noteheads.HeadFontOf`）。
  **反転 shift・ledger の幅・click 箱・符尾なしトレモロの中心・glissando の境界・tie 列の offset・
  cue 列の臨時記号の詰め**が同じ font から出るようになり、**`CalculateOffsets` の
  *scale を取る overload は消えた***（**残った 1 本は `DesignMetrics?` を取り、null＝20**）。
  ⚠️ **`GrobFontSize.ScaleOf` の doc が「click target の箱は本物の scaling」と*書いていた*のを訂正した**
  ——**click 箱は「glyph が*埋める*箱」でなければならず、glyph は `DesignOf` の輪郭で描かれる。**
  **cue の箱は 0.006248 広く、そして 0.021696 *低く*刷られていた**（**Design13 の黒符頭は 20 より
  背が高い＝1.124440 対 1.090000。幅と高さで符号が逆**）。

  ⚠️⚠️ ★★★ **ページに出るのは ledger だけ、そして単体テストでしか見えない量が半分ある。**
  **射程**（`scratch/p316/sweep316.ps1`・両側 Release・base は `git stash`）: **`grace|acciaccatura|appoggiatura|cue`
  を書く本 78 冊＝全数で SAME 60 / MOVED 18**・**それ以外 1420 冊から 20 冊に 1 冊の 71 冊＝SAME 71 / MOVED 0**。
  ⇒ **動いた 18 冊の差は合計 70 行、その 70 行が*全部* `<line>`＝ledger**（**x1/x2 が ±0.01＝丸め 1 単位・
  全部「短くなった」＝Design13/14 の符頭が 20 より狭いから。符頭・臨時記号・符尾・旗は 1 行も動かない**）。
  **snapshot 3 枚**（`test/cue-accidentals`・`test/grace-lower-staff`・`test/grace-notes`）・
  **ユーザー実コーパス 1 冊**（`9 to 5 (Morning Train).lys`）。
  ⚠️⚠️ ★★★ **二度そのものは 1 冊も動かない**——**SVG の x は 2 桁で、0.0043 / 0.0062 は丸めの下**。
  ⇒ **番人は単体テスト**（`NoteCollisionTests.ChordHeadPositioning_{FullSize,Grace,Cue}Second_*`）で、
  **LP の 3 数を 4 桁で述べたうえに「20 を縮めた読みとの差」を 0.004270 / 0.006248 で*同時に*述べる**
  ＝**毒を式として持っている**（旧読みならその行が必ず赤）。
  ⚠️ **click 箱は `_interactive` のときだけ出る**（`SvgDrawingContext:176-201`）——**静的 SVG も snapshot も
  掃きもこの半分を 1 バイトも見ない。観測者はプレビューだけ。**

  ⇒ ★★★ **残した島 3 つ（測って、範囲外と決めた）**:
  **⑴ `ItemSkylineFactory` / `SpacingRules` / `SkylineBuilder` は cue の列を*丸ごとフル サイズ*で読む**
  （`GetNoteheadBBox(noteValue)`＝20 の箱そのもの）——**0.006 ではなく縮尺 1 つぶんの島。grace は
  `GraceTime` の門で通らないので cue だけの話**／**⑵ `ElementCoordinator.BuildTieColumn` は offsets だけ
  cue のフォント・隣の `headBBox` は 20 のまま＝枠が半分**（コードに名指した）／
  **⑶ `DrawChord` の click 箱の*幅*は advance・`DrawNote` は ink**——**第95 が単音だけ移した残りで、
  フル サイズでも 0.024 違う。本便が変えたのは*どのフォントか*であって*どの箱か*ではない。**


- **U9. ✅ 閉じた（第294・`65224424`・ユーザー決定）＝`note~@mark("X")` の印が自分の住所に `~` を
  含んでいた件は §2F ⑺ の島そのもので、その島ごと閉じた**（**第293 起票・第294 が原因を測り直して合流**）。
  **`data-pos` は書かれた `@` を指す**（番人 `PostEventOrderTests.ARehearsalLetter_ReportsTheOffsetOfTheAtSignThatWroteIt`・
  **記号を前に置かない対照つき**）。**インクは 1630 冊 1 点も動かない。**
  ⚠️⚠️ ★★★ **起票の「`GreenNode` の span の取り方の問題」「第286 U3 と同じ族」は*どちらも外れ*で、
  U3 の直しは 1 ミリも効かない**（2026-08-30 実測）。**`GreenNode` は正しい**——**犯人は
  `ParsePostEvents` の並べ替え**: **記号（`~ ( ) [ ]`）の*後ろ*に書いた post-event（`@…` か弦番号）は
  音符の上へ持ち上げられ、記号は `_pendingPostEventMarkers` から*その後ろ*へ再生される**ので、
  **木の順序が原文と逆になる**。**位置は緑の幅の累積なので、入れ替わった 2 ノードが両方とも嘘の住所を持つ。**
  ⇒ ★★ **判定法はこの 2 つの区別**: **U3 は「幅は正しいが `Span` が trivia を食う」**（`GreenNode` の話）／
  **こちらは「`Span` の取り方は正しいが、そのノードが木の別の場所に居る」**（parser の話）。
  **症状はどちらも「data-pos が 1 手前」で、同じ顔をしている。**
  ★ **住所は §2F ⑺**（**着手条件・消費者の数え上げ・要決定の中身は全部そちらに在る**）。
  **ここに 2 つ目の綴りを置かない**（§5.2.1②）。
  ★ **射程（2026-08-30 に数え直し。ディスク上 1629 冊）**: **62 冊が該当・sum と span で同一集合**——
  **ユーザーの 326 冊のうち 40 冊 63 か所**（**`~@mark` 53・`~@trill` 1・`)@fall` 3・
  スラー閉じのあとの弦番号 `)\4` `)\2` `)\3` が各 2**）／**追跡 11 冊**（既知一覧）／
  **scratch のプローブ 11 冊**。**インクは動かない。**

- **U10. ✅ 閉じた（第295・`ca0cbf2e`・ユーザー決定）＝弓は*それを書いた文字*を名乗る**
  （**第294 起票・ユーザーの問い「`~` をクリックしたらタイがハイライトされるべきじゃないの？」から出た**）。
  **決定**: **スラーは `(` が primary（クリックの跳び先）・`)` が `data-alt` の alias**／
  **記号の上にカーソルを置いたときは*弓だけ*が光る**（音符は暗くなる＝Core だけの変更で済む側）。
  ★ **タイは `~` の 1 文字なので自明**。**射程は「弓を書く本のほぼ全部」だがインクは 1 点も動かない**
  ——**増えるのは属性だけ**（**1645 冊掃きで svg MOVED 387・`data-pos` を伏せると全冊バイト同一・
  追加された属性は 21,581 個・midi/xml/ly は MOVED 0・`check` の差 0**）。
  ⚠️⚠️ ★★★ **起票が書いていた「決められない理由」は事実として古かった**——
  **「エディタは 1 要素 1 `data-pos` を仮定している」は barline の便から成り立っていない**
  （`IDrawingContext.Source(int, aliases)` が `data-alt` を吐き、`extension.ts:2184` が
  `[data-pos="p"], [data-alt~="p"]` で引く）。⇒ **起票が「これが決まらないと進めない」と書いた
  条件も、処方箋と同じく着手前に 1 回測る**（§5.0 に汎化）。
  ★ **入ったもの**: **`MarkerFlags` が marker ノードの `SourceStart` を運ぶ**／
  **`MusicItem` の 3 offset を `WithBowSources` の 1 軒が打つ**／**`TieItem.SourcePosition` と
  `SlurItem.Start`/`EndSourcePosition`**／**`DrawTies`/`DrawSlurs` が `Source` スコープで包む**。
  ⚠️⚠️ ★★★ **キャッシュされた弓は「それを*計算した*編集の住所」を持つ**ので、
  **`SharedRenderer.ResolveDataPos` に入れるまで終わっていない**（`SystemLayoutCache` の注記が
  名指していた第190 の骨。外すと `ChainedEditsOnABowedTwoVoiceScore_AlwaysMatchFull` を含む 5 本が赤）。
  ⚠️ **`MusicItem` に足した offset は `MeasureContentKey` の除外表と `CollectTailShifter` の
  両方に足す**——**drift net `ShifterInventory_CoversEveryPositionField` が 3 つとも先に名指した。**
  ★ **番人は `BowSourcePositionTests`**（**タイ 3 綴り・スラー 2 綴り・「弓の無い本は 1 本も住所を持たない」
  対照・staff+tab で 1 本の弓が 2 経路 1 住所**）。**毒 5 本の効き方が全部違う**（§1 ⑺）。

- **U11. ✅ 閉じた（第297・`bb6932e3`）＝注釈が書いた弓は*その注釈の `@`* を名乗る**
  （**第295 が U10 を閉じる過程で数えて起票**）。**snapshot の弓 56 本が 56 本とも住所を持つ。**
  ★ **第 3 の弓の族**——**タイは `~`・スラーは `( )` だが、l.v./repeat tie を書くのは*注釈***なので、
  **U10 の規則「弓はそれを*書いた文字*を名乗る」がそのまま `@` に着地する。**
  ⚠️⚠️ ★★★ **起票の「要決定 1 問」は*既に決まっていた***——**U10 でユーザーが答えた同じ問い**。
  **起票がそれを未決だと思った理由は、`TieVariantLayout.SourcePosition` が既に在って*音符*の住所を
  持っていたこと**で、**それは「規則の別の読み」ではなく*たまたま転がっていたフィールド***。
  ⇒ ★★★ **だから工事は「drawer を包む」ではなく「注釈の住所をそこまで運ぶ」で、包むのは最後の 1 行。**
  ⇒ ★★ **起票が「これが決まらないと進めない」と書いた条件は着手前に 1 回測る**（§5.0——**U10 でも
  同じ形を踏んでおり、これが 2 例目**。**1 便ぶんの往復がこれで消えた**）。
  ★ **入ったもの**: **`MusicItem` に `LaissezVibrerSourcePosition`／`RepeatTieSourcePosition`**（**base に
  置く理由は弓 3 本と同じ＝suffix splice が 1 つの `with` で re-home する**）／**`ChordNoteInfo` にも同じ 2 つ**／
  **`MeasureCollector.NamedArticulationSourceOf` の一軒が `@` トークンから読む**／**`SemiTiesOf` が
  音高トークンでなくそれを運び、*和音レベルがメンバーに勝つ*（方向の先取権と同じ）**／
  **`DrawTieVariants` が `Source` スコープを開く（何も書いていない弓はスコープを開かない）。**
  ⚠️⚠️ ★★★ **キャッシュされた半弓は「それを*計算した*編集の住所」を持つ——そしてまた機械が先に鳴った**
  （`SystemLayoutCacheTests.MultiStaff_ReusesSystems_AndStaysByteIdentical` が `2836` 対 `2835` で赤）。
  **U10 ⑶ と同じ形。** ⇒ **直しは `SharedRenderer.ResolveSemiTies`**——⚠️ **`ResolveBows` では*ない***:
  **1 つの和音が住所の違う半弓を複数持ちうる**（メンバー毎の注釈）ので、**note locator が表せる
  「item 1 つ → 住所 1 つ」ではメンバーが 1 つに潰れる**。**`Calculate` が出す順序で対応づける。**
  ★ **番人は `SemiTieSourcePositionTests`**（**note 3 綴り・和音レベル・メンバーレベル・先取権・
  「注釈が無ければ住所を持つ path は 1 本も無い」対照＝7 ケース**）。**毒 5 本の効き方**:
  **⒜ スコープを外す → 7／⒝ note 腕で音符の住所を打つ → 4（和音の腕は緑＝*この 1 本が
  「注釈であって音符ではない」を測定にしている*）／⒞ 先取権を逆に → 1／⒟ `ResolveSemiTies` を
  外す → 1（キャッシュ網の*バイト同一性*・L264）／⒠ content key の除外を外す → 1（*同じ網の
  別の主張*＝`cache.Count` 9→10・L262＝memo churn）。**
  ⚠️ **§5.2.1③ への答え**: **名指せる台帳キーは無く、無いことが正しい**（**台帳の点は属性を観測できない**）。
  **代わりに U10 と同じマスク証明＝`data-pos` を伏せると `test__lv-meterchange.svg` は base とバイト同一**。
  **加わったのは `data-pos="64"` 1 属性で、64 は `@`・音符 `c'` は 62**（**fixture で実測**）。

- **U12. ✅ 閉じた（第296・ユーザー決定）＝基準は「タブか」ではなく `TabNumbersOnly`**
  （**第296 が「五線とコード名と rit が重なる」を閉じる過程で*自分で作った*決定。起票も決着も第296**）。
  ★ **経緯**: **LP の `TabStaff` の stencil 表（`ly/engraver-init.ly:1277-1285`）をそのまま移すと、
  blank は*文脈プロパティ*なので五線が隣に在るかを見ない**——**タブ*しか*無い score が `@text`・
  dynamics・`rit.` を 1 度も描かなくなる**（**前は 1 度描いていた**）。**実測
  `scratch/ベースタブLy/奏（かなで）.lys`**: **`--score both` は `@text("人差し指で")` を 2 回 → 1 回
  （直した欠陥）／`--score tab` は 1 回 → 0 回（値段）**。**タブを読む奏者への運指の指示が消える。**
  ⇒ ★★★ **ユーザーの決定＝保つ。そして基準はユーザーが名指した**——
  **「staff がないというのは、つまり `as full` のタブには `@text` を描画するという解釈で良いのかな」**。
  ⇒ ★★★ **その基準は既にコードに在った**。**`RenderSpecParser.StaffRenderedParts` の `as` 既定規則
  （2026-08-29・同じユーザーの決定）が「五線と並ぶタブは数字だけでよい。上の五線が拍子も休符も
  付点も符尾もタイも運んでいるから。単独のタブは全部自分で運ばなければならない」と書いている**——
  **`TabNumbersOnly` は最初から「この part は五線にも出ているか」を意味していて、`as` で上書きできる。**
  ⇒ **注釈の族はその*同じ表*の続きなので、`TabStaffStencils.Blanks` を `IsTab` から
  `TabNumbersOnly` に変えるだけで済んだ**（**私が起票時に書いた「part が五線にも出ているかで gate」は
  この既存規則の再発明だった**）。
  ★ **帰結**: **`staff m`＋`tab m`（既定 numbers）は blank ＝二重描画が消える／`tab m` 単独
  （既定 full）は全部描く。** ⚠️ **二重に出る組み合わせが 1 つだけ残る＝`staff m` の隣に*明示的な*
  `tab m as full`**——**書き手が「完全なタブ」を求めた以上それは書き手の選択**で、欠陥ではない。
  ★ **番人**: `TabStaffStencilTests.AFullTabKeepsTheMarkupItHasToCarry`（**負けたほうの答えを
  固定していた網を*書き換えた*のがこの決定の記録**）＋ `AnOrdinaryScriptIsBlankedOnANumbersOnlyTabAndKeptOnAFullOne`。
  ★ **射程（2026-08-30・ディスク 1645 冊）**: **タブを持つ本 341・タブと五線の両方 320・
  blank される族を書く本は 2 冊**（**どちらもユーザーの `ベースタブLy`**）。

- **U13. ✅ 閉じた（第314・ユーザー報告 2026-09-01「volta ブレースの線が太すぎるように見える」）
  ＝太さは LP と同一。ユーザー決定で*そのまま*。**
  ★ **実測**（`scratch/p314/`・git 管理外）: **Lily# の SVG は `stroke-width="0.160"`、LP 2.26.0 の
  双子は `0.1600`**（`volta.lys` / `volta.ly`＝`\repeat volta 2 { c1 } \alternative { … }`）。
  **＝どちらも `VoltaBracket (thickness . 1.6)` × line-thickness 0.1。**
  ★ **タブの上でも同じ**（`voltatab.lys` / `voltatab.ly`）: **両者とも五線 0.100・volta 0.160**——
  **LP は `TabStaff`（staff-space 1.5）でも線の太さと edge-height 2.0 を*絶対*で持つ**ので、
  **タブだけの本でも括弧は notation の 1.6 × line-thickness のまま。**
  ★ **ラスタでも突き合わせた**（目視は比でしか答えられないので、**同じ絵の五線 1 本を対照に取る**）:
  **LP 331 dpi の頁 PNG で volta 4.00 px 対 五線 2.51 px ＝ 1.59 倍**／
  **Lily# `png --crop --scale 8.0` で 13 px 対 8 px ＝ 1.63 倍**（**1 ss = 80 px**）。
  ⚠️ ★★ **crop した絵で測ってはいけない**——**LP の `-dcrop=#t` は volta の線を `y=0` で裁つので
  被覆が 3.54 px と出る**（**紙の端に立つ物を測るときは crop しない絵で**）。
  ⚠️ **被覆（1 − 明度）の積分で測ること。暗い画素を*数える*とアンチエイリアスの分だけ太い側に丸まる**
  （計器は `scratch/p314/measure.ps1`・10 行）。
  ⇒ ★★★ **なぜ今報告されたか＝4 日前に本当に太くなったから**。**`51c32b3b`（2026-08-28）が
  「素の 0.13」を LP の宣言 1.6 に直した**（`VoltaBracketEngraver.LineThickness` の remark がその経緯）。
  **読み手の目は差を正しく見ている。動いたのは Lily# で、動いた先が LP。**
  ⇒ **ユーザー決定（2026-09-01）＝LP どおり 0.16 のまま。意図的乖離は作らない。**
  ★ **同じ本で対照になる数**: **リハーサル記号の枠は `0.100`**（LP の `\box` thickness 1）——
  **volta が枠より 1.6 倍太いのは LP 自身の対比。**
  ⇒ ★★ **汎化＝「太い/細い」の目視は 2 つの数で答える**: **その線の ss と、*同じ絵の*五線 1 本の ss。**
  **比が LP の宣言と合っているかは、それだけで決まる**（memory「1x ラスタ」の太さ版）。


> # ✅ **`@!X` を実装した＝テキストスパナの終端必須化**（第289 で閉じた・`7b0df578`）
> （2026-08-29・第288 起票／**第289 実装。設計は §3 の行、実装の骨は §1 ⑵〜⑺**）
>
> **入ったもの**: **`@!X` は `@X` が開いたものを閉じる**／**primitive は `@textSpan("…")`／`@!textSpan`**、
> **`@rit`・`@accel`・`@rall` は START だけの糖衣**（**印字文字列は `MusicMarkItem.TextSpanSugarText`
> の 1 表**）／**閉じられなかった span は LYS4018 を出して*何も描かない***（**線も語も**）。
> **`MusicMarkType` は `Rit`／`Accel` を退役し `TextSpanStart`／`TextSpanStop` を得た**（**型は語を持たない**）。
> **`MusicMarkItem.VoiceIndex` が生まれ**、**対応付けは (譜, 声部) ごと**（`ly/engraver-init.ly:375`）。
>
> **退役した 3 つ**（**どれも「対にする相手が無い」ことの帰結だった**）: **1 小節フォールバック**／
> **「次の rit/accel が閉じる」探索**／**第288 が足した「自分の次の演奏では閉じない」ガード**。
> **演奏順に対にすると 3 つとも要らない**——**演奏 1 の STOP は演奏 2 の START より先に来る。**
>
> ★ **射程は実測**（1582 冊・base/head を*本ごとに続けて*・並列版 `sweep289p.ps1`）:
> **SAME 1554 / MOVED 28・レンダー失敗 0**。**28 はテキストスパナを書く本ちょうど**
> （**追跡 fixture 4＝本便で移行・scratch 23・ユーザー実コーパス 1**）。**エラーが増えた本は 0。**
> ⚠️ **ユーザーの `Untitled-6.lys` は `@rit` が閉じていないので、いま何も描かない**（LYS4018 が 2 行）。
> **その本には触れていない**——**どこで終わるかはその人の音楽**（起票時の指示どおり）。

> # ✅ **閉じられなかった span の答えを 1 つにした＝ペダルとオッターヴァを寄せた**（第289 で閉じた・`1115f42e`）
> （2026-08-29・第288 起票／**第289 実装。ユーザー決定＝`@!` で一貫させる**）
>
> **入ったもの**: **`@!ottava` が正典**（**`@!quindicesima` も同じ mark＝`\ottava #0` が octavation を何であれ取り消すのと同じ**）／**`@loco` は退役**（**専用の診断は付けない＝ユーザー指示。普通の unknown annotation になる**）／**閉じられないオッターヴァは LYS4018 を出して*何も描かない***／**ペダルは*インクを動かさず*診断だけ足した**。**診断は 1 本の validator・1 本のコード**（`SpanPairingValidator` が `SpanKind` × `SpanPairingFault`）。
>
> ⚠️⚠️ ★★★ **この項が書いていた LP の説明は偽だった**（§1 ⑴）。**「LP は閉じられなかった span に答えを 1 つしか持たない＝warning ＋ `suicide()`」はテキストスパナの engraver 1 つからの一般化**で、**オッターヴァもペダルも warning を出さず*最後まで描く***（`ottava-engraver.cc:220-226`／`piano-pedal-engraver.cc:425-443`・**LP 2.26.0 で実測**）。⇒ **「描かない」は言語の答えで、両方の engraver に `NOT PORTED HERE` を立てて宣言した**（**APPROX 53 → 55**）。
>
> ⚠️ ★★★ **`@loco` の「レンダする文字」も無かった**——**`@loco` を書いた本の SVG に `loco` の text 要素は 0 個**（**変わるのは括弧の右端だけ**）。**LP にも `loco` というコマンドは無い**（**music glossary に用語として載るだけ**）。
>
> ⚠️ ★★★ **「開いている最中の START」の答えは族ごとに違う**（§1 ⑶）: **テキストスパナは*拒否***（LP の "already have a text spanner"）／**オッターヴァは*状態変更*で 2 本目の括弧を開く**（LP の `process_music` はどの ottava イベントでも開いている span を閉じる）／**ペダルは*踏み直し***。**類推で揃えようとして `audit/lpreg/ottcons.lys` に捕まった。**
>
> ★ **射程は実測**（1586 冊・base/head を本ごとに続けて）: **SAME 1581 / MOVED 5・レンダー失敗 0**。**動いたのは移行した 2 冊（`showcase/03-piano` は不動・`test/multi-staff-ottava` と `audit/lpreg/ottcons`）と scratch 3 冊で、ユーザー実コーパスは 0 冊。**
>
> ⚠️ **残った小物 2 つ**: **⒜ ペダルの対応付けに譜／声部フィルタが無い**（**別の譜の `@sustainOff` が閉じる**——**直すとインクが動く**。**実測でその配置に届く本は 0 冊**なので、**直す便は本を書くところから**）／**⒝ ✅ 綴りも寄せた**（**同じ便の続きで**）: **`@sustain` … `@!sustain`／`@sostenuto` … `@!sostenuto`／`@unaCorda` … `@!unaCorda`**、**`@treCorde` はその糖衣として残した**（**Text スタイルが実際に刷る語だから**）。**`@sustainOn`／`@sustainOff` 族は退役。**

> # ✅ **テキストスパナの左端も書き手のものになった＝両端が同じ 1 つの綴りになった**（第290 で閉じた・点 `b6c33de3`／直し `b8fde5b4`）
> （2026-08-29・第289 起票／**第290 実装。第289 が書いた順「LP で測って点を置く → 直す → 全数掃き」をそのまま踏んだ**）
>
> **入ったもの**: **`StartItemIndex` は `Math.Max(start.Mark.AnchorItemIndex, 0)`**（**終端が既に使っていた同じ clamp**）
> ／**`startX` が `BoundPadding` を払う**（**同じメソッドの他の 3 分岐は前から払っていた**）。
>
> ⚠️ ★★ **欠陥は 1 つではなく 2 つで、対照が無ければ 1 つしか見えなかった**。**⒜ 列**（`StartItemIndex: 0` が定数）と
> **⒝ 左の padding**（`BoundPadding` を右端でしか払っていなかった）。**対照の本 TXH は「Lily# の定数が既に指している所」で開く**ので、
> **⒜ を持たず ⒝ だけを見る**——**⒜ だけ直していたら TXH は −0.25 に残り、主役もそこへ着地していた。**
> ⇒ ★★★ **「対照は動かないことを確かめる網」ではなく、*主役が隠している第 2 の量*を出す装置**（第288 ⑺ と同じ形が、今度は同じ便のうちに出た）。
>
> ★ **台帳の点は 2 つとも exact で閉じた**（`textspanner.x.label-to-notehead` −6.254489998 → 0／`...control...` −0.250000000 → 0）。
> <!-- ledger: textspanner.x.label-to-notehead = 0 --><!-- ledger: textspanner.x.control.label-to-notehead = 0 -->
> **LP 側は `audit/lp-geometry/probes/textspanner-left-bound.ly`（TXO／TXH）で実測**——**両方 +0.250000000 で、
> 「LP は書かれた音符に束ねる」は*算術を経ずに*読める**（**対がそれ自身の反証装置**）。
> ★ **読みは pen 対 pen**（ラベルの原点どうし）——**破線にしなかったのは、その 2 辺が 0.05 ずれた定義になるから**
> （`ottava.x.line-start-to-notehead` が抱えている harness 項）。<!-- ledger: ottava.x.line-start-to-notehead = 2.71317795 -->
>
> ★ **射程は実測**（1588 冊・base/head を本ごとに続けて・`sweep289p.ps1`）: **SAME 1581 / MOVED 7・`check` 差 0・レンダー失敗 0**。
> **7 は追跡 fixture 4 ＋ 第289 の scratch 3**。**ユーザー実コーパスは 0 冊**（**その 1 本の `@rit` は閉じていないので何も描かない**）。
> ⚠️ ★★ **`showcase/01-expressions` ではリハーサルマーク "B" が 1.68 下がった**——**その箱は x 58.47..60.75 で、
> 旧 `rit.` は 59.31 から始まっていた**。**outside-staff の段が、そこに居るべきでなかったラベルからマークを持ち上げていた**。
> **62.29 になって重なりが消えた。** ⇒ **`startX` は水平重なり判定の*入力*なので、これは第 2 の変更ではなく帰結。**
>
> ⚠️⚠️ ★★★ **第289 のこの項の射程見積りは低かった**——**「小節頭以外に `@rit` を書く追跡本は `showcase/01-expressions` の 1 冊」と
> 書いたが、`test/multi-staff-text-spanners` は両譜の 2 小節目の*2 番目の音符*に `@accel` を書いている**。**正しくは 2 冊。**
> ⇒ ★★★ **族の 1 綴りで grep して族の射程を答えた**（`@rit` だけを見て `@accel`／`@rall`／`@textSpan` を見ていない）。
> **糖衣が 4 つある族の在庫は、4 つとも掃くまで在庫ではない。**
>
> ⚠️ **名指して直していない 2 つ**（**どちらも APPROX 棚に載せた・55 → 57**）: **⒜ 継続片はいまも小節の原点を取る**
> （**LP の `left-broken` は attach-dir を RIGHT に反転する**）／**⒝ 右端は束ねた列の*左端*を取るが、LP は*中央*を取る**
> （`bound-details.right` が attach-dir を宣言しない＝CENTER。**同じ dump で 31.309760708228 対 head 30.657660708228＝約 0.65**）。
> **左を直したことが右を読めるようにした。** **どちらも台帳の点がゼロなので、閉じる便は点を置くところから。**

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
> ✅ **`BoundaryClefAllowance` 0.93 MB は第193 で閉じた**（`52161baf`）——**列を建てずに厳密な 0 を
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
> ★★ **第224 が rod 後の +64.2 を帰属して返した**（詳細は §1・`1cc7a383`）: 打鍵 436.7 の地図＝
> ann.lyrics 222.0（verse skylines 110.6＋chain 走査 ~107）・L3.lyricBand 165.2・非歌詞 ~20。
> **帰属は 100% 本の形**（rod で sung 行 5→約 6 小節・形強制実験で 0.01 MB 一致）、正体は
> **List の倍化 1024 段跨ぎ**——`VerticalSkyline.ReserveForBatch`（数えてから 1 回確保）で退役し
> **打鍵 347.3 MB＝rod 前 372.5 をも下回る**（sweep 0/569・対照 plain1k 45.3 桁不動）。
> ⚠️ **他の batch 地点で ±1000 建物級のバッチを見たら同じ段を疑う**（remark に実測ごと記載）。
> ⇒ ★★★ **帯（`b4e04839`）・verse skylines（`4eb18a7f`）・chain prefix（`985b924f`）が第224 で
> 全部 memo に載り、歌詞打鍵の章は閉幕**＝perf-lyrplain1k 打鍵 **436.7 → 55.1 MB（非歌詞対照 45.3
> の +22%）**。残り ~10 MB は hyphen engraver／apply・solve／非歌詞 L5/L9 の小粒のみ。
> ⚠️ chain memo の 2 境界（詳細は `LyricChainMemo` remark と §1 第4便）: **anchor テーブル系スカラーは
> 値に入れない**（live で ResolveAnchor）・**store は per-pass**（seed がパスの anchor profile を読む
> ——共有第1稿は incremental==full 網が数分で赤にした）。
> ★ **memo 族の道具箱（この島で確立・他所でも使える）**: MeasureLayout 参照同一鍵（FingScriptMemo
> 条項）／hit を主張する net／X-only 値なら store 共有・そうでなければ per-pass／cache 境界の入力目録は
> 既存 memo の監査を兼ねる（font 番人 `10a225a1` はその獲物）。
> ⚠️ **font plan はどの content/global 鍵にも入っていない**——session の番人（`IncrementalCompiler.Compile`
> 冒頭・pin FontEditIncrementalTests）が変化で cache を全部落とすので、**新しい memo は fonts を
> この番人の下で圏内と数えてよい**（第224 続き便が実顔で欠陥を実測してから建てた番人）。
> ⚠️ **ビルド費用は測ってある**: solution の full が **3.81〜4.01 秒**（5 project のとき 3.63〜4.03）・
> **noop は 0.90 秒で不変**＝実質ゼロ。**打鍵の*時間*はここに入れていない**——
> `EditKeystrokeBench` / `PreviewUpdateBench` が既に持っており、**同じ量の 3 軒目**を作らないため。

> ⚠️ **`keep-inside-line` は入った**（`115133b4`・`b3ee5e25`）。全列・左右両方の rod が
> `SpringSolver.ApplyRods`（＝`Simple_spacer::add_rod` の移植）へ流れている。
>
> rod の入力は**列の ink 全体**＝テキスト（**音節は中心合わせなので左右へ半幅ずつ／和音記号は
> `7e7fe5cb` 以降 ink 左が列なので右へ全幅・左へゼロ**）＋**音楽の ink**
> （`SpacingRules.MusicalInkOverhangsPerColumn`。符頭は列から右へ全幅、臨時記号は左へ届く。
> どちらも esw 抜きの素の extent＝`col->extent` が取るもの）。⚠️ 一時期テキストだけだった
> （`b3ee5e25`）のを `df7fff40` で報告し、追い移植済み。**出力は動かない**が、それは
> 「満たされているから」であって「生成していないから」ではない——区別は
> `KeepInsideLineOverhangs_IncludeTheMusicalInkNotJustTheCentredText` が入力側で主張している。
>
> ⚠️ **`audit/{property,grob}_coverage.csv` は生成物で、いま大きく stale。**
> `pwsh audit\scripts\Build-GrobCoverage.ps1` を走らせると（**約 6.5 分**）
> `keep-inside-line` は `"0","Absent"` → `"4","Used"` に正しく反転するが、**同時に無関係な
> drift が 371 行**出る（Absent 329→280 / Used 124→168 ＝何セッション分もの溜まり）。
> **手編集しないこと**。再生成は**単独の commit** にする。

### T. タブ譜 × 実コーパス（2026-09-01・第317 起票）← **新ワークストリーム**

> ★★★ **なぜこの族が在るか**: **ユーザー実コーパス 314 冊の 93%（293 冊）がタブを書くのに、
> 追跡コーパスでタブを書く本は 9%（52/585・fixture は 32）**。**回帰網が実使用と別のものを測っている。**
> ★ **比較の基準は `scratch/ベースタブLy` の*手書き* `.ly` 286 本**（285 組が `.lys` と対）——
> **exporter を通らない独立した正解**。**LP にそれを描かせ、Lily# に `.lys` を描かせて突き合わせる。**
> ⚠️ **`.ly → .lys` の importer は要らない**（未知数が 2 つになるだけ）。第317 が測って撤回した。
> ⚠️⚠️ **他人の `.ly` を正解に使うときは、その人が定義したマクロを先に読むこと**——
> **第317 は `tnh = { \once \hide TabNoteHead }` を読まずに絵から「LP はタイ先の数字を刷らない」と
> 報告し、ユーザーに訂正された**（**LP の既定は*刷る*。Lily# が意図して刷らないだけ**）。

- **T1. ✅ 閉じた（第318・案A の忠実移植）＝タブのスラーは「普通の採点器をタブ譜の枠で走らせ、そのあと 0.35 平行移動する」の 2 段になった**

  ★ **報告**（2026-09-01・ユーザー）: 「タブ譜のタイとスラーが、特に良くないように思える。」
  ⇒ **測った結果、タイは既に正しく、問題はスラーだけ**（対は `scratch/p317/tab/ts.lys` ＋ `ts.ly`）。

  ★★★★ **LP のタブのスラーは 2 段構え**:
  1. **普通のスラー採点器が TabStaff の音符（フレット数字）に対して走る。** ⚠️ **符尾は長さ 0**——
     `ly/engraver-init.ly:1248-1256` が `Stem.details` の `lengths`/`beamed-lengths` などを全部 0 にし、
     **コメントに「`slur::calc-control-points` への影響を最小化するため」と明記**している。
     だから curve は数字のすぐ上に短く出る。
  2. **制御点 4 点すべての Y を `− staff-space × direction × 0.35` ずらす**
     （`ly/engraver-init.ly:1275` → `scm/tablature.scm:144-157`）。
     **TabStaff の staff-space は 1.5（Lily# は `EngravingDefaults.TabStringSpace` が同じ 1.5 を持つ）
     なので 0.525 スペース**、常に**数字側へ**（`direction` が上向き +1 のとき下へ）。
     ⚠️ **`control-points` は端点を含む 4 点**なので、**曲線全体の平行移動**であって形は変わらない。

  ⚠️⚠️⚠️ **Lily# は ⑴ を持っていなかった。** **第318 まで `BuildTabSlurLayout` は採点器を通らなかった**:
  ```csharp
  double clearance = 0.36 * TabConstants.FretFontSize + 0.1;
  double peakY = Math.Min(topDigitY - clearance, Math.Min(startY, endY) - 0.4);
  double controlY = (8 * peakY - startY - endY) / 6;   // 対称三次で頂点を peakY に合わせる
  var tabSlur = new SlurItem(…, curveUp: true, …);      // 向きは常に上
  ```
  **数字の上に必ずアーチを架け、向きを固定している**（LP は記譜側と同じ `direction` を使う）。
  ⇒ ★★★ **だから `move-closer` だけを移植しても意味がない**——**発明したアーチに LP の定数 0.35 を
  掛けても LP の曲線にはならず、たまたま近づくだけ。** **これは「発明を 2 つ目の発明で覆う」形**
  （§5 の[LP実ソース模倣]）。**ユーザーに 2 案を示し、案A（忠実移植）が選ばれた。**

  ★★★★ **⑴ 段目は「同じ採点器・違う*譜*」だった**（第318 が閉じた）。**縮尺ではない**——
  **LP は `staff_space_` を 6 か所で掛けている**: **base attachment の持ち上げ `dir·0.5·ss`
  （`slur-scoring.cc:557`）・格子の刻み `ss/2`（`:798-801`）・minimum-length（`:729`）・
  height-limit（`:714`）・符尾 extent の widen（`:747`）・五線からの逃げ 2 か所**
  （`:650`/`:655` と `slur-configuration.cc:61`/`:69`）。**それ以外（`slur-details` の重み・
  音符列の extent・線の太さ）は絶対量でそのまま通る。**
  ⚠️⚠️ **4 本線のタブの*線*は奇数位置**（3, 1, −1, −3）。**5 線の述語を使い回すと「弦を隙間、
  隙間を弦」と答える**ので `EngravingDefaults.OnStaffLine(pos, lineCount)` を足した
  （**5 では既存と全位置で一致することをテストにした＝五線側が no-op である証明**）。
  ★ **⑵ 段目は `SlurLayout` を作り直す 4 行**（`BowLayout` の Y は page Y-up＝LP の枠なので、
  `scm/tablature.scm:155-156` の引き算をそのまま綴れる）。
  ★ **向きは記譜側から取らず、タブの*弦*から取る**（`lily/slur.cc:60-68 calc_direction`＋
  `TabStaffGeometry.StringStemUp`）——**低い弦の run は記譜が上向きに描く所でタブは下向きに描く。**
  ★ **休符も点も tuplet 番号も script も入らない**（LP が同じブロックで stencil を消している）ので
  **extra-encompass の集合は空**。**grace のフレット数字だけは障害物に入る**（`AddTabGraceObstacles`）。

  ★★ **正典の 4 桁**（`audit/lp-geometry/probes/tab-slur.ly`・タブ譜自身の spaces）:
  **上向き `P0 1.043326 / C1 2.074551`・下向きはその厳密な負**。**台帳 5 点 `slur.tab.*` が持っている。**
  ⚠️⚠️ ★★★ **残差は全部「Lily# のフレット数字が大きい」（§3 で既決）1 つに帰着する**——
  **`string-to-attachment` の +0.328674 は `0.722000 − 0.393326` に 9 桁一致**（数字の半分の高さの差）、
  **`attachment-to-control` の +0.073124 は LP 自身の高さの式に Lily# の span を入れて出る 0.072720**
  （残り 0.0004 は indent の頭打ち）。**その span も `slur.tab.span` として測ってある。**
  <!-- ledger: slur.tab.span = 0.655402835 --><!-- ledger: slur.tab.up.string-to-attachment = 0.328673993 --><!-- ledger: slur.tab.up.attachment-to-control = 0.073123652 -->
  ⇒ ★★ **対を「生の高さ」と「立ち上がり」に割らなければ、間違った曲線が既知のフォント差に隠れられた。**

  ★ **射程**（`--combined` の全数掃き＝`tab` を書き `(` を含む 515 冊・**SAME 493 / MOVED 22**）:
  **ユーザー実コーパス 7 冊**（`Endless Story` / `Sweet Child Of Mine` ×2 / `wrongfret` /
  `君の恋人になったら` / `奏（かなで）` / `夕暮れ沿い`）・**追跡 2 冊**（`test/tab-grace-slur` と
  本便が足した `test/tab-slur-pinned`）・残りは scratch と backup の複製。**対照＝非 tab 27 冊は全部 SAME。**
  ⚠️⚠️⚠️ ★★★★ **掃きは `--combined` で取ること。`lysc svg` は*最初の* score block しか描かない**——
  **ユーザー実コーパスはたいてい `staff` の score を先に置くので、既定の掃きはタブの絵を 1 枚も見ない**
  （**タブが既定なのは 314 冊中 1 冊**）。**第318 の第1稿は 759 冊を 54 分叩いて MOVED 10 という
  *もっともらしい小さな数*を出した。** ⚠️ **これは `RULES.md:1111-1117` に第298 が既に書いていた**
  ——**掃きを組む前に `RULES.md` で「掃き」を引くこと**（第318 が同じ段落の下に足した）。

  ★ **同じ棚で一緒に片付くもの 2 つは、どちらも着手する物が無かった**（第318 が着手前に測って撤回）:
  **⒜ `PhrasingSlur.stencil = ##f`（`engraver-init.ly:1276`）は空振り**——**Lily# に phrasing slur という
  grob は無い**（`(`/`)` 1 種類だけ。`TabStaffStencils` の remarks が「Tie/RepeatTie/LaissezVibrerTie/
  PhrasingSlur の 4 行は移植していない＝これは製品の決定」と*既に書いていた*）／
  **⒝ `\hideSplitTiedTabNotes` は U4 で既に決着**——**LP の既定は改行先のタイ先を括弧付きで刷るが、
  U4（ユーザー決定）で Lily# はタイ先の数字をどのモードでも刷らない**ので、
  **`\hideSplitTiedTabNotes` を当てた状態そのもの。**
  ⇒ ⏸ **残るのは読み手の判断 1 つ＝「行頭の括弧付きだけは出すか」**。**欠陥ではないので勝手にやらない。**

- **T2. ✅ 閉じた（第328 第 8 便・ユーザー「提案通りで良い」）＝提案をコーパスへ写した**（原本は `scratch/p328/lambada/backup/`・`lysc check` 診断 0・和音の空 cell は section 頭だけ `.`・他は `| |`）。**下は経緯**:
  ⏸ 判断待ち（第317）＝`Lambada Complicada.lys` は移行できない。`.ly` が無く、和音トラックの割り方が決まらない

  ★ **形**: **`|:` 2 つ・`:|` 2 つ・`[1. …]` と `[2. …]`（`[` 2 個に対し `]` 1 個＝閉じていない）**。
  **旋律 `part melody` を 5 つに割る必要があるが、この本は `chords prog { section A { … } }` という
  *並行トラック*を持ち、そちらも同一に割らないと大半の小節から和音が消える。**
  ⚠️ **数が合わない**: **旋律は書かれた小節 36（17 ＋ 8 ＋ 6 ＋ 2 ＋ 3）に対し、和音のセルは 33。**
  **私の数え方が違うのか、和音が終止部を覆っていないのか判別できない。**
  ⇒ ★★★ **推測で割ると譜面が静かに壊れるので止めた。** **ユーザーに訊くこと**: 元の `.ly` は在るか、
  無ければ小節を一緒に数える。**残り 11 冊の error 本のうち、判断が要るのはこの 1 冊だけ。**
  ★★ **第328（ユーザー「ly 双子を作って検討して」）＝提案を `scratch/p328/lambada/Lambada Complicada.lys` に建てた（コーパスには当てていない）**:
  **36 小節＝A 17／B 8／C 6／E1 2／E2 3**、form `A |: B :| |: C [1. E1 ] :| [2. E2 ] |.`、和音は `cis:m` → `C#m` 等の綴り直し。
  **「33 対 36」の正体は和音行の*行頭の `|`***——「書かれた `|` は 1 小節を閉じる」規則（§3）で行頭の `|` は**空 cell（前の和音が続く）**なので cell は 36 で小節と一致し、
  **C の 6 cell `C#m F#m B E A D#7` が旋律のバス音 `cis fis b e a dis` と揃う**（33 と読むと C の和音がずれる）。`lysc check` は診断 0・Lily# の絵と LP 双子（和音行は双子に出ない）の段組み・記号は一致（`lambada-lys2.png`／`lambada-lp.png`）。
  **残る問い＝E2 の和音 `| G#m G | F#`（1 cell 目は E1 の G#m の続き）でよいか**。ユーザーが見て良ければ提案をコーパスへ写す。

- **T3. ✅ 閉じた（第320 ⑾）＝error 本は 14 → 1、残る 1 は T2（`Lambada Complicada`・判断待ち）**
  **計器は `scratch/p320/check-corpus.ps1` → `check-320.csv`**（317 冊・`check-final.csv` との差分を刷る）。
  **⚠️ CSV の 14 冊のうち 4 冊（`A Thousand Miles`・`ABC`・`Automatic`・`Beat It`）は第318 末の
  binary（`scratch/p319/exe-base`）で既に 0 だった**＝第317 の CSV より後に製品側で消えていた。
  **本便が直したのは 9 冊で、全部 `.lys` の綴り**（コーパスは git 管理外・`.ly` を正解に読んだ）:
  **⒜ `staff X with lyrics verse`（旧綴り）→ `staff X` ＋ 行 `lyrics verse sings X`**（`tab-low`・
  `ぐるぐるワンダーランド`・`がくふ`＝grandStaff 3 譜・`雪やこんこ`）／**⒝ form の `|: |:`（開きが 2 つ）→ 1 つ、
  `]:|` → `:|`**（`Addicted To Love`・`青い珊瑚礁`）／**⒞ 移行器が section 境界で*和音を割った*
  `… break <e` ┃ `gis1>4. …` → `<e gis>4.`（`Green-Tinted Sixties Mind`・3 か所。**T4 の「音符を落とした本 0」は
  数え方が音符単位なので、割れた和音は数えられない＝T4 の網の穴**）／**⒟ `b8,` → `b,8`**（`Billie Jean` 2 か所・
  `.ly` は相対で `b8`）／**⒠ chords 行の小文字ルート `c | f | g | c` → `C | F | G | C`**（`雪やこんこ`。
  `.ly` に和音は無い＝誰かが後から足した行。内容は残し綴りだけ直した）／**⒡ `staff bass` → `staff bassline`**
  （`Your Smiling Face`）。⚠️ **「和音の綴り 26」は `Lambada` の 29 の見間違い**——T2 の本で、T3 の族では無かった。
  **残る warning は 208 冊 / 1457 件＝T6 の族**（本便では数えただけ）。

- **T4. ✅ 閉じた（第318）＝移行は意味を変えていない。むしろ*既に壊れていた本を見えるようにした*。**

  ★★ **「数冊を目で見る」ではなく、まず全数で数えた**（絵は 4 点しか見られず、見た本についてしか
  言えない）。**計器は `scratch/p318/screen-migration.ps1` と `t4/barcount.ps1`。**
  ⚠️⚠️ ★★★ **三者比較でなければ問いに答えない。** **第1稿は「手書き `.ly`」対「移行後 `.lys`」を
  数えて拍子欠落 13 冊・記号欠落 60 冊超を出した——が、それは*移行のせい*だとは言っていない。**
  **測るべきは `移行後 − 移行前(backup)`**（T4 の問い）と **`移行前 − .ly`**（別の問い＝T3 の族）の
  2 つで、混ぜると**私が何便も前にやった `.ly` → `.lys` 変換の傷が、第317 の移行の傷に見える。**

  ★★★★ **移行が動かした量（112 冊全数）**: **音符を落とした本 0 冊**（42 冊で*増えた*＝
  `CarryDurations` が切れ目で音価を明示した分）・**`volta` を落とした本 0 冊**・
  **拍子の数が動いた本 1 冊＝`ABC` の +5**（**第317 が手で戻した意図的な修復**）。
  ⇒ **移行は音符も反復も拍子も落としていない。**

  ★★★ **小節数を LP と突き合わせた（20 冊・LP に手書き `.ly` を描かせて BarNumber を数える）**:
  **18 冊が厳密一致**。**残り 2 冊は `Butterfly` 107/108 と `Hello` 104/105 で、どちらも +1**——
  ⚠️ **そして*移行前は*さらに遠かった**（**`Butterfly` 109 / `Hello` 106**）。
  ⇒ **移行は両方を LP に 1 小節*近づけた*。残っている 1 はそれより古い負債。**
  ⚠️ **LP 側の計器の罠**: **素の `BarNumber` は行頭にしか grob を残さない**ので
  `Air on G String` は 12 個しか返し、最大値 17 は「最後の行頭」であって最後の小節ではない。
  **`break-visibility = #all-visible` にして初めて全小節に 1 個立つ**（第318 が 1 度踏んだ。
  **数は*少なめ*に外れるので、もっともらしく見える**）。

  ★★ **絵の 4 点は `Air on G String` で確認**（`scratch/p318/t4/airpng.png` と `air_lys.png`）:
  **小節番号（行頭 1,3,5,8,11,14,17・総数 20）・記号の位置（A/B/C が 1/8/14）・volta 括弧の位置**
  が一致。**反復記号だけ 1 か所違い、それは移行とは無関係の Lily# 側の逸脱で T5 に起票した。**

  ⚠️⚠️ ★★★ **28 冊が出す LYS2006「first measure is shorter than the meter」は、移行の傷ではなく
  *移行が見えるようにした古い傷*。** **移行前は 0 冊、移行後は 12/12 冊**なので一見すると回帰だが、
  **`Air on G String` の `~C_2` を backup で読むと同じ 7/8 が*移行前から在った***——
  **手書き `.ly` は `a'8\2 g \2a\2 a, d2\3`（8 分 4 つ＋2 分＝4/4）なのに `.lys` は 8 分 3 つ＋2 分。**
  ⇒ **短い小節を作ったのは移行ではなく、その前の `.ly` → `.lys` 変換**（**`\2a\2` ＝ 弦番号を音符の
  *前*に書く綴りで 1 音落としたと見られる**）。**移行はそれを section の*先頭*小節にしたので、
  section 頭を検査する LYS2006 が初めて届いた。** ⇒ ★★★ **これは回帰ではなく診断の獲得。**
  **28 冊ぶんの既存の破損が可視化された** ⇒ **T3 の族として T6 に起票。**

- **T5. ✅ 閉じた（第319）＝曲の先頭の反復開始記号は grob ごと立たない。インクも*幅も*消えた。**

  ★★★★ **規則は LP のソースが自分で書いている**（LILYPOND-REF: `lily/bar-engraver.cc:432-449
  Bar_engraver::pre_process_music`。**`pre_process_music` の直前のコメントが
  "At the start of a piece, we don't print any repeat bars" そのもの**）:
  **`repeatCommands` を読む loop——`Repeat_acknowledge_engraver` が積んだ `start-repeat` を
  `startRepeatBarType` に変える所——は `first_time_` が立っている間まるごと飛ばされる。**
  **`first_time_` は `!(Timing.init_mom < Timing.now_mom)`**（`:414-417
  Bar_engraver::initialize`）＝**「Timing 文脈がまだ最初の moment に居るか」**。
  ⚠️ **第318 が引いた `:304-310` は*種類*を選ぶ jenga tower のほうで、規則はそこには無い。**
  ★ **絵でも確かめてある**（`scratch/p318/t4/startrepeat.ly`。同じ `\repeat volta 2` が
  曲頭なら刷らず、1 小節置けば `‖:` を刷る＝位置だけで変わる）。

  ⚠️⚠️⚠️ ★★★★ **これは*描かない*ではなく*作らない*。そして Lily# は幅を払っていた。**
  **台帳 2 点で測った**（`line-start.time-to-first-note.{initial-repeat,no-initial-repeat}`・
  プローブ `audit/lp-geometry/probes/initial-repeat-bar.ly` の IR / IN）:
  **移植前、`|:` で開く本は拍子→最初の符頭が 5.540000（LP は 3.700000）、
  同じ音楽から `|:` を取った対照は 3.700000 で厳密一致**——**⇒ 開き記号は
  *1.840000 ss の幅も予約していた*。** **今はどちらも exact。**
  ⇒ ★★★ **だから直す場所は renderer ではなく*モデル*だった**——**`Measure.StartBarline` は
  spacing/layout 15 箇所が読む**ので、**描画だけ飛ばすと 1.84 が隙間として残る。**
  ★★ **対で足したから読めた**（§ README「点を足すときは両側を足す」）: **片方だけなら
  「3.7 になった」としか言えず、それは metered line start の値と区別が付かない。**

  ★★★ **住所は `ScoreAssembler`＝Score / MultiStaffScore の 2 つの constructor を呼ぶ唯一の場所。**
  **`RepeatStart` を作る所は 4 つある**（`MeasureBuilder` の pending start・rows-only の form walk・
  `ChordNameCollector`・`LyricsCollector`）ので、**そこに置くと規則が 4 つになる**（§5.2.1②）。
  **番人 `InitialRepeatBarTests` はその 4 本の道を 1 つずつ通す**——⚠️ **各テストが*陽性対照*を
  自分で持つ**（**同じ本の 2 小節あとの `|:` は残ること**。無いと「そもそも collect されなかった」
  本と区別が付かない）。**毒（`ScoreAssembler` を stash）で 6 本 ＋ snapshot 2 枚が赤。**
  ★ **snapshot は `test/initial-repeat-bar`**（**規則の両側を 1 枚に：曲頭の `|:` は何も描かず、
  2 小節あとの同じ `|:` は描く**）＋ **`test/grandstaff-repeat` が動いた**（開き記号 10 要素が消え、
  他は全部 X が詰まっただけ）。**双子でも確かめた**（`lysc ly` → LP 2.26.0 で
  `scratch/p319/irb_lp.png`＝**小節線が 1 本ずつ一致**）。

  ★★★ **射程（全数掃き `scratch/p319/sweep319.ps1` / `.csv`・379 冊・約 90 分）**:
  **母集団は「`|:` を含む本」339 冊＝上位集合**（第318 の教訓＝多め側に外す）＋
  **対照 40 冊（`|:` を持たない本）**。**出力は 5 つ全部**（`svg --combined` / midi / xml / ly / check）:
  **svg MOVED 38 / SAME 340 / no-output 1**・**midi・xml・ly・check は 379 冊すべて SAME**
  ⇒ **動いたのはページだけ、というのは主張ではなく実測**。**対照群の MOVED は 0。**
  ★ **内訳**: **ユーザー実コーパス `scratch\ベースタブLy` の 10 冊**（`Air on G String` /
  `Air on G String楽譜ママ` / `Always There` / `Birthday` / `Carnival` /
  `Eine kleine Nachtmusik…` / `Freedom` / `SOMEDAY` / `ホーリー&ブライト` / `目を閉じておいでよ`）・
  **その `scratch/p317/tab/backup-…` の複製 10 冊**・**追跡コーパス 2 冊**（`test/grandstaff-repeat` と
  本便が足した `test/initial-repeat-bar`）・**lp-regression / lpreg 5 冊**・残りは scratch。
  ⚠️ **`no-output 1` は `scratch\dogfood\grammar-demo2.lys` で base も head も同じ**（§2 ⒨ の
  `--combined` の既知の脆さ。本便の欠陥ではない）。
  ⚠️⚠️ ★★★ **第318 の「追跡コーパス 6 冊 ⇒ snapshot 6 枚」は間違いだった**——
  **6 冊のうち 5 冊（`voltagrace{,-ctl,-ctl3}` / `voltasky` / `repeat-volta-initial-grace`）は
  `audit/lpreg` と `audit/lp-regression/lys` の*回帰コーパス*で snapshot を持たない**。
  **実際に動いた既存 snapshot は `test/grandstaff-repeat` の 1 枚。**
  ⇒ ★★ **「追跡コーパスに N 冊」と「snapshot が N 枚」は別の数。冊数から枚数を推定しないこと。**

  ⚠️⚠️⚠️ ★★★★ **境界を 1 つ測った＝*冒頭の grace*。LP はそこで開き記号を*刷る*。**
  **`\grace f'8 \repeat volta 2 { b'1 }` は `‖:` を刷り、`\repeat volta 2 { \grace f'8 b'1 }` は刷らない**
  （`scratch/p319/gracerepeat.ly` / `.png`・2.26.0 実測）——**grace が `now_mom` を `init_mom` の
  先へ進めるので `first_time_` が偽になる**。⇒ **「measure 0 なら落とす」は LP の規則の*近似*。**
  ★★ **しかし Lily# にその綴りは無い**（実測）: **music の `|:` は LYS1034（error）**なので
  **`|:` は form にしか書けず、form の `|:` の前に音楽は置けない**。**唯一の候補＝`|:` の前に
  「grace しか持たない section」を置く形も、その grace が*自分の小節*を取るので `|:` は measure 1 に
  乗り、開き記号は今も刷られる**（`scratch/p319/graceopen3.lys`・**base と head で SVG がバイト同一**）。
  ⇒ **穴は無い。ただし「measure 0」は LP の `first_time_` と*たまたま*一致しているだけで、
  同じ言葉ではない——先頭に音楽を置ける綴りが増えたらここを読み直すこと。**
  ★ **副産物**: **その `graceopen3` は Lily# が 2 小節、LP が 1 小節**（grace 専用 section が空小節を
  取る）。**base ≡ head なので本便の物ではない**が、**`audit/lp-regression` の
  `repeat-volta-initial-grace`（state=open）と同じ族**。
  ⚠️ **その 5 冊の lpreg 本は LP に*近づいた*が、`audit/lp-regression/status.json` は更新していない**
  ——**あの台帳は lpreg のハーネスで取り直すもの**（`audit/lpreg/REGENERATE.md`）。**次便の一手。**

  ✅ ★★★ **第328 でユーザーが決めた＝刷る**（§3 の行）。門は撤去・双子は `printInitialRepeatBar = ##t`・番人 `InitialRepeatBarTests` は反転・snapshot 2 枚（`initial-repeat-bar`・`grandstaff-repeat`）再ベース・台帳 IR は LP 5.84 に取り直して OPEN −0.30。
  ✅ ★★★ **その −0.30 は同日の第 5 便で閉じた（exact）**——ユーザー報告「C セクション先頭の `|:` と最初の音符の x 距離が近すぎる」の根。**行頭の `|:` は break-align 表の列ではなく、renderer が prefix から 1.15（LP に無い数）ずらして描き、最初の音のばねは拍子の `first-note` 2.0 のままで小節線の幅 1.84 をその前に差し込んでいた**＝`|:` は 0.15 右・音符は小節線から 0.86（LP 1.30）。**LP は begin-of-line の順で `staff-bar` を拍子の*後*に置き**（define-grobs.scm:668-683）、拍子→小節線は `TimeSignature (staff-bar . 1.0)`（clef 0.7・key 1.1・LeftEdge 0.0）、小節線→音は `BarLine (first-note . semi-shrink 1.3)`＋光学補正。**移植**: `PrefixColumns.BarX/BarWidth/BarGap`（`SolvePrefixColumns(…, staffBarWidth)`）・`LineStartColumn.LineStartSpring` は小節線を extremal grob として wish を建て、小節線の箱を min_dist に入れ、**measure frame が差し込む `StartBarline` 幅を引いて返す**（`measureStartBarWidth`）・pen は `MultiStaffLayouter.LineStartBarGap`（staff・tab・span bar・mark の anchor の 4 読み手が 1 導出）・`LineStartBarClearance` 定数は削除。**台帳**: IR 0.0・新点 `line-start.time-to-repeat-bar`（LP 2.70・0.0）。番人 `LineStartColumnTests` 3 本。<!-- ledger: line-start.time-to-first-note.initial-repeat = 0 --><!-- ledger: line-start.time-to-repeat-bar = 0 -->**下の ⏸ は履歴として残す**:
  ⏸ ★★ **読み手の判断が 1 つ残る（勝手にやらない）**: **LP 自身が
  `\set Score.printInitialRepeatBar = ##t` という逃げ道を持ち、`Documentation/en/notation/
  repeats.itely:160-172` は「ジャズのリードシートでは伝統的に*刷る*」と書いている。**
  **ユーザーの実コーパスはまさにリードシート/タブ譜**なので、**「曲頭の `|:` を出したい」なら
  それは欠陥ではなく設定**。**本便は LP の既定に揃えただけで、記法は 1 つも増やしていない**
  （[文法を育てない]）。

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
  ⇒ ★ **今の計器は当て済みコーパスに対して冪等**（patched 0・already 1079・unmatched 5・cross 6・grace 7）。**残る T6**: ⒜⒝⒞⒟⒠（`.ly` を正解に 1 冊ずつ）・**⒥ break 21 冊は「`.lys` の break をユーザーが意図して動かしたか」を訊いてから**（機械で足せる形だが、消した break を戻すと逆行する）・⒦。
  ✅ ★★ **⒜ の「section 頭で落ちた音」は第 3 段で計器が数えた（LCS の隙間＝直前の揃った音と section 内の最初の揃った音のあいだに `.ly` だけが持つ音）**:
  **`s` spacer と最初の揃った音より前（`.ly` の別変数＝blank／chord／click track の `b8 b8 b8. b16`）を除くと、隙間は 285 対で 2 冊だけ**——
  **`A Thousand Miles` B2 の前に `e2 fis |` 1 小節が無い**（`.ly` L72・percent の直後。**octave は手で**＝relative の `\repeat percent` 後の基準が私には確定できない→ 第 4 便で `e,2 fis, |` を入れ、✅ 第 8 便でユーザーが「B1 の同じ小節と同じ高さ」と確認）／
  **`Sugar` C の `fis\3 ais\2 dis,, cis'`（`.ly` L80）は `r1 ×4` の音価を引き継いだ*全音符 4 小節*で、`.lys` L40 は 4 分 4 つの 1 小節**＝⒠ の族（LP の自動小節線）・意図はユーザーに訊く→ ✅ 第 8 便でユーザー「全音符が正しい」＝自分で `fis,1\3 | ais,\2@fall | dis,, | cis, |` に直した（診断 0）。
  **副産物＝alignment の改善で ⒤ が 2 箇所増えた**: **`Can't Get You Out Of My Head` の section 頭 `r`（L18・L27）は LP が 8 分**→ `r8` を当てて**警告 13 → 3**（「Measure duration 2 exceeds」10 件が消えた・新規 0）。
  ⚠️ ★ **計器の教訓 3 つ目**: **`.lys` の section 頭で running duration を 4 に戻す（Lily# の読み）と、音価を書かない小節がまるごと `x/4` に化けて揃わない**——揃えるのは LP の読みで、Lily# の読み（4）は ⒤ の判定でだけ使う。

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

- **T8. ✅ 閉じた（第328・ユーザー報告＝Lambada の提案を見て「E2 の箱が volta ブラケットの下に居る」）＝和音行の上では volta が 1 帯高く立ち、2 番の箱がその hook の下のポケットに落ちていた**
  ★ **再現**（`scratch/p328/volta/`）: v1（和音行なし）正常／**v2・v2b（和音行あり）で再現**／v3（反復 2 つ・行なし）正常／v4（Lambada から行を外す）正常＝**引き金は和音行**。cell 数を揃えても再現（v2b）＝入力の不整合ではない。
  ★★★ **計器（stderr に一時出力）で割れた**: 見積り段階の `voltaBrackets` は空（`volta=False`）＝持ち上げは stacker だけ／**volta の anchor は「系の上端＋3.13」で、行が先頭だと系の上端＝行の帯の上端**＝譜から見て 1 帯高い／**行の記号は支持から除外（`IsChordRow`）**＝ブラケットは何にも当たらず 3.13 のまま／**E2 の見積り（譜＋padding）は hook の先端より低く、forbidden 区間 (0.74, 6.0) に 0 が入らないのでポケットに留まる**。E1 は「Am」の ceiling で見積りが上がっていたので hook に当たって持ち上がった＝**偶然**。
  ★★★ **LP 実測**（`probes/volta-chord-row.ly`・page-post-process の dump＝`mark-chord-row.ly` の型）: **VCR（行あり）: ブラケットの線の下端＝「Am」の ink 上端＋2.152405**（0.46＋0.5＋番号の ink＝番号が Am の上で binding）／**E1・E2 の箱の下端＝線の上端＋0.460000**／**VCN（行なし）: 箱 0.460000・線は床 5.0614**。⇒ LP は行の記号を Score 級 mover の支持に持つ（§3 の行）。
  ★★ **移植 3 点**: **⒜ `VoltaBracketEngraver`: 床を系の上端でなく top spaceable staff から吊る**（`YOffsetYUp + StaffOffsetInSystemUp`）／**⒝ `OutsideStaffStacker.ChordRowSupport`: 行の記号を `PlaceVoltas`・`PlaceMusicMarks` に*追加の支持*として渡す**（`Place(extraSupport:)`・staffless では渡さない）／**⒞ ブラケットの skyline の上端を `anchor0 + 0.1` → `+ half`（0.08）**＝箱が線の上に 0.48 でなく 0.46＝両本 exact。
  ⚠️⚠️ ★★★ **1 度目は行の記号を tracker に*種まき*して退行を出した**——`test/chordrow-rit-second-system` の `rit.`（Staff 級の text spanner）が行の上に登った。**LP では Staff の axis group が先に閉じ、行はその上に置かれる**ので Staff 級 mover は行を見ない。⇒ **同じ ink でも「誰の支持か」で答えが違う**＝支持は grob の階層（Score/Staff）ごと。snapshot の赤 1 枚がそれを言った。
  ★ **台帳 3 点**: `page.volta.chord-row.symbol-to-line`（−0.009944943＝番号 glyph の face・`NumberFontSize` の島）・`mark.second-ending.chord-row.line-to-box-bottom`（exact）・`mark.second-ending.line-to-box-bottom`（VCN・exact）。<!-- ledger: page.volta.chord-row.symbol-to-line = -0.009944943 --><!-- ledger: mark.second-ending.chord-row.line-to-box-bottom = 0 --><!-- ledger: mark.second-ending.line-to-box-bottom = 0 -->**snapshot**: 新 `test/volta-chord-row`＋⒞ で 5 枚が 0.02 動いた（`volta-labels`・`repeat-volta`・`tocoda-volta-clearance`・`rehearsal-marks-inside-containers`・`grammar-test`）。⚠️ **bar number は Score 級だが今回は触っていない**（`barnumber-chord-row` の台帳が別に在る・行の上の bar number が記号に当たる本は未測）。

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
- ✅✅ ★★★ **閉じた（2026-08-03・起票と同じ日）。符尾の attachment X は符頭ごとになった。**
  <!-- ledger: stem.up.right-edge.half-head = 0 -->
  <!-- ledger: stem.up.right-edge.black-head = 0 -->
  起票時の姿は「`LayoutUtilities.StemAttachX` が `NoteheadBlackStemAttachment.X` を**符頭によらず**
  返す／LP は**符頭ごとの ink 右端 − thickness/2**（黒玉 1.304200 − 0.065 ／ 半玉 1.377400 − 0.065）
  ⇒ **半音符の上向き符尾が 0.073200 左**」。**予測を先に書いてから測り、9 桁で着地した**
  （`stem.up.right-edge.half-head` −0.073200000・対照 `black-head` 0）。
  **修理は house に `noteValue` を足して `GlyphMetrics.GetNoteheadStemAttachment` に訊かせただけ**
  ——`scm/define-grobs.scm:2608` が宣言する callback（LP は**頭に訊く**＝`note-head.cc:201-213`）の
  移植で、**1 つの数が形ごとの問いの代わりをしていた**のが正体。観測者 `StyledHeadStemAttachmentTests`。
  ⚠️ **対は捨てていない**——**両側 exact の恒等対**になったので、**符尾の x を動かす何かが入った瞬間に
  第2の計器へ変わる**（§5.0）。⚠️ **全音符だけは意図的に未閉**（LP は不可視符尾を中心に置く
  ＝`stem.cc:1063-1064`・**描画側が全部 `noteValue >= 2` で門番するので点も観測者も無い**）。
  ★★ **残った教訓**（起票時の読みがそのまま正しかった）: **これは「綴りが 2 つ」ではなく
  「house が 1 つ足りない」型**——`MetronomeMarkGeometry.StemAttachment` が同じ知識を拍単位で
  選び分けていたので、**engine は答えを持っていて 1 か所だけが訊いていなかった**。
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

- ✅ ~~**crop が loose-line の伸びのぶん stale**~~ — **閉じた**（第292・`f9ab3cbc`）。**予約の消費者を 2 つに割った**: `LyricReservationBelowSystem` が 2 つの profile を返し（`LooseBlockProfiles`・最小と force 0 の rest length）、**読むのは `CreatePages` の `totalHeight` 1 行だけ**。**下スカイライン＝譜間の床は最小のまま**（LP もそう予約する・`page-layout-problem.cc:593-599`）。⚠️ **crop 自体は今も LILYSHARP-OWN, DECLARED**（`page.height` の −109.468268・APPROX 棚 221）——**閉じたのは stale のほうだけ**。⚠️ ★★ **そのとき名前が付いた地図**: **譜間の対を floor するのはスカラーの下 extent ではなく `AddLyricBand` 経由の *profile***（スカラーは silhouette を持たない system 用の fallback）——**第292 の 3 本目の毒が不発で分かった。§2 D の残りを読むときに要る。**

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
- ✅ **歌詞帯のスカラー床は X 盲目 — 閉じた**（第221・`785ade3c`。起票 `90833c84`＋対の修理 `053e2674`）。
  帯の最小 profile が paging skyline の要素になり（`LyricReservationBelowSystem` が profile を返し
  `PagingAugmentProgram.Builder.AddLyricBand` が最終 family として merge・鍵は building の数値＝augment memo と両立）、
  床は X で読む。スカラー床は CreatePages / PageLayouter の両経路から消え、extents は profile の最深点。
  台帳 `lyrics.band-floor.*`（fork の 2.272129 を LP 実測・ink-past-band **0.000000000 ちょうど**）・
  射程 12/569 冊（全部歌詞本・LP 側へ縮む向き）・snapshot 1 枚（余白のみ）。
  **露出して残った島**: row/sings の音節 X ドリフト・j-dot（→§1 第221 ⓐⓑ）。
  ✅ **上側の帯も閉じた**（第273・`d2c97b40` 起票＋`d2c97b40` 移植）——⚠️ **これは "chord-row 帯" ではなかった**: `EstimateAboveStaffExtents` の `bandUp` に渡るのは `inlineChordNames` だけで、**和音*行*は届く前に濾されていた**（だから和音行の書 4 冊が全部盲目だった）。
  歌詞帯と違って**移植ではなく retire**——X-aware silhouette が実位置で予約し annotation extents が
  実インクで値付けし済みの上に敷かれた**第 3 の課金**で、外すと `page.inline-chord.gap-first` が
  +0.451116000 → +0.001116000（残りは silhouette 腕の平坦 1.9＝§1 の一覧）。sweep 0/572・rerender 0/81。
  <!-- ledger: page.inline-chord.gap-first = 0.001116 -->
- **Y コーパスの拡張**（`page.top-margin` / `page.bottom-margin` / `page.last-page-gap` 等）
- ✅ ★ ~~**歌詞行が譜間の「中で」LP と別の位置に立つ**~~（2026-08-14 起票・**未着手のまま 60 便**）
  — **第274 が観測者を立てて閉じた**（起票 probe `grandstaff-lyric-row.ly`・書 GSL/GSN・台帳 6 点・
  **コード変更ゼロ**）。**この項が主張していた乖離は今日のエンジンには無い**:

  | | 上譜中心→歌詞ベースライン | 歌詞→下譜中心 | 譜間合計 |
  |---|---|---|---|
  | LilyPond 2.26.0（第274 実測） | 5.021223442736809 | 6.046346450541339 | 11.067569893278148 |
  | Lily#（同） | 5.021122673 | 6.046291551 | 11.067414224 |
  | 残差 | **−0.000100770** | **−0.000054899** | **−0.000155669**（＝上 2 つの和・全桁） |

  <!-- ledger: lyrics.grand-staff.staff-to-lyric = -0.000100770 -->
  <!-- ledger: lyrics.grand-staff.lyric-to-staff = -0.000054899 -->
  <!-- ledger: lyrics.grand-staff.staff-staff-inside = -0.000155669 -->
  <!-- ledger: lyrics.grand-staff.control.staff-staff-inside = 0 -->

  **⑴ 第 1 歩の −0.000100770 は `staff-to-lyric` 一族の面項**——
  `lyrics.row.between-staves.verse-hole.control.staff-to-lyric` の **−0.000100769 と 9 桁一致**で、
  一族 12 点が **−0.0001006 ±3e-7** に並ぶ。
  <!-- ledger: lyrics.row.between-staves.verse-hole.control.staff-to-lyric = -0.000100769 -->**⑵ 部屋は 2 歩の和ちょうど**＝
  **この読みには機構の項が 1 つも入っていない**（brace も through-bar-line も乗らない）。
  **⑶ 対照 GSN（行を消しただけ）は両 engine とも 9.000000000 exact**＝読みそのものの陽性対照。
  ⚠️ **`nonstaff-relatedstaff-spacing`（上）/ `nonstaff-unrelatedstaff-spacing`（下）を疑えという
  当時の読みは、量の出所として外れている**——LP の 5.021223 は spec 数ではなく **skyline の歩き**
  （padding 0.5 を staff1 の down 全幅 3.55 に足すと 5.870 で、**LP は歌詞の X における最深インク**を
  読んでいる）。**0 に「直す」と面を fit したことになる。**

  ⚠️⚠️ **これは 2026-08-14 の測定の*反証*ではない。反証はできない**——
  **当時の再現 `grandStaff { staff upper with lyrics words … }` は今日 LYS6011**
  （群の中の行は staff 項の**あいだ**に置く `lyrics NAME sings PART`・GRAMMAR `StaffGroupBody`）で、
  **probe も残っていなかったので誰も読み直せない**。しかも **LP 側の 6.739 / 4.500 / 11.239 は
  この本のどの綴りからも出ない**——既定 paper／house font pin ＋ exporter の `\mark`／`\addlyrics`／
  **staff2 を群の外に出した形**の 4 通りで**全桁同一**（`scratch/p274/gs-variants.ly`）。
  ⇒ **当時の数は「再現不能」として記録する**（§5.0「why と数が矛盾している点は対が壊れている」の、
  *対がもう存在しない*版）。★★ **教訓は棚の側にある**: **この項は 60 便のあいだ「未着手」の札で
  §2 に立ち、そのあいだ誰の観測者にもなっていなかった**——**未着手の起票は、測るまでは
  「開いている欠陥」ではなく「開いているかどうか未知の主張」。**

  ★ **外した方向予測とその収穫**（§5.0「予測が外れたときこそ収穫」）: 起票時に
  「どちらかが割れるなら*閉じ*の歩きのほう」と書いた——唯一の先例 `lyrics.row-between.lyric-to-staff`
  （書 IOA）が **+0.082995881 を丸ごと閉じの歩きに載せている**から。**外れた**（大きいのは第 1 歩）。
  **理由が言明になる**: IOA の閉じを縛っているのは **staff2 の `^"Text"`**（Lily# が約 0.35 高く立てる
  TextScript の島）で、**この本の staff2 は bass 記号しか持たない**。⇒ ★★
  **残差は「歩きの位置」ではなく「その歩きを*縛っているインク*」に属する。同じ形を閉じる 2 つの
  閉じは同じ量ではない。**
  <!-- ledger: lyrics.row-between.lyric-to-staff = 0.082995881 -->

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
- ✅✅ ★★★ **閉じた（2026-08-03）。タイの列アウトラインは移植済み＝`TieChordOutline`。**
  <!-- ledger: tie.width.seconds.upper = -1e-09 -->
  <!-- ledger: tie.width.seconds.lower = 0 -->
  <!-- ledger: tie.width.clears-head = 0 -->
  `set_column_chord_outline`（`tie-formatting-problem.cc:96-287`）＝各符頭・付点（LEFT のみ）・
  **符尾**・旗（LEFT のみ）・臨時記号（RIGHT のみ）・同じ列の他の符頭を持つ列ごとの skyline に、
  後退箱（`:243-258`）・`close_by` の intersect（`:565-579`）・符尾の引き戻し（`:583-609`）・
  **列レベルの outer-tie-length-symmetry 項（`:890-908`）**まで入っている。
  ⚠️ **falsifier は生きて緑**＝`tie.width.clears-head` と `tie.width.seconds.lower` は 9 桁 EXACT。
  ★★★ **この項の値打ちは「2 段で閉じ、2 段目はタイの修理ではなかった」ところ**（`why` に全文）:
  アウトライン移植で **+0.888699999 → −0.073200001** まで来て、**残りは名前の付いた 1 量**だったので
  **調整せずに開けたまま置いた**——そして**上の符尾 attachment を閉じた瞬間に、タイのコードを
  1 行も触らずに −1e-09 へ落ちた**（タイの右端は符尾の引き戻しが持っているため）。
  ⇒ ★★ **教訓**: **残差を「名前の付いた 1 量」まで分解できたら、そこで止めて別の点に回す。**
  **分解できていれば、その量は別の場所で閉じたときに*ひとりでに*返ってくる。**
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

- ★ **⒲ 同じ lyrics track を*独立行*（fold されない words-only 行）として 2 つの別メロディで置く綴りは 2 本目が 1 本目を上書きする**（2026-09-02・第320 起票・**未実装・実需 0 冊**）。**第320 で行の `sings` は行ごとの束縛になった**（§3 先頭行）が、`MeasureCollector.CollectMultiStaff` の `staffVoices` は **track 名で辞書引き**なので、`score { staff sax  lyrics w sings a  lyrics w sings b }` のように**譜の下に fold されない行を同じ名前で 2 本**置くと、後の行の骨格が前の行の骨格を上書きする（`pendingLyricsRows` は `FirstOrDefault` で最初の spec の `Sings` を取る）。**コラール（各行がすぐ上の譜に fold される形）は 4 譜で実測済みで無事**。直すなら「行の識別を (track, 出現順) にする」＝`GetVoiceBindings` の voice 名の一意性の話で、`ChordRowSpec` の同名 2 行も同じ形。**着手前に corpus に訊くこと**（第320 時点で追跡 587 冊に「行に `sings`」は 3 冊・全部 1 本）。
- ★★★ **⒵ 本文の長さが変わる編集（＝打鍵の大半）は suffix splice を丸ごと失う。根まで測った・要ユーザー判断で製品コード未着手**（2026-09-03 起票＋同日に根まで。計器 `scratch/p325/P325SpliceDeltaProbe.cs.txt`＝60 小節・absolute 固定でオクターブ変数を外し、**編集の種類と位置**を振って `LastCollectResume` と毒のカウンタを読む。**各行で `incremental == full` を確かめてから数を読む**）。
  | 編集 | Δ | 1 小節目 | 15 | 30 | 45 | springMemo |
  |---|---|---|---|---|---|---|
  | `e'`→`g'`（音名） | 0 | **splice 59** | 45 | 30 | 15 | 57/3 |
  | **空白 1 つ挿入（内容不変）** | +1 | **0** | 0 | 0 | 0 | **0/0** |
  | `e'`→`ees'` | +2 | **0** | — | 0 | — | 57/3 |
  ⚠️ **空白の行が一番効く**——**`springMemo 0/0` は「内容不変なので vector を丸ごと参照で再利用した」の意**（`IncrementalCompiler.LastSpringMemo` の doc）。**コンパイラは何も変わっていないと分かっているのに、collect は 59 小節を歩き直している。**
  ★★★ **根は 1 行**＝`CollectResumePlanner.GreenPrefixAgrees` の **`aBelow != bBelow` の腕**（`if (aBelow != bBelow || …) return false;`）。**毒で最初の不一致を吐かせた**: `below: Note old[69,73) new[69,74) limit=73 aBelow=True bBelow=False`／`below: PitchE old[69,70) new[69,72) limit=70`。⇒ **編集を担うノードが baseline では limit のちょうど上で終わり、new では 1〜2 バイト先で終わる**。**`limit` は `ComputeWindow` の共通接頭辞長＝*最初に食い違うバイト*なので、この非対称は長さの変わる編集で*必ず*起きる**（偶発ではない）。
  ⚠️⚠️ **効き方が「候補 1 つ」ではなく「collect 全体」なのが要点**: **`ParseAgreementsHold` は判定を probe に*メモ化*する**ので、1 度 false になると**その collect のどの候補も L318 で降りる**。**毒のカウンタがそれを示す**——**アドレス照合は正常で `TrySpliceSuffix` は毎境界で*呼ばれている***（1 小節目・Δ=+1 で **attempts=120**）が、**59 回が L318**（parse 合意）**・60 回が L248**（記録側の尾が境界小節を書き換えた）で降りる。**Δ=0 では attempts=3 で 1 回成功。**
  ⚠️⚠️ ★★★ **以下 ⒜〜⒞ と根の診断は*合成した 60 小節 1 冊*の上の話。射程は下の ⒟ で撤回・縮小した。読む順を間違えないこと。**
  ⚠️ **既存コメントと実測が合っていない**: `MeasureCollector.Resume.cs:256-268` は「Δ≠0 では挿入点を*またいだ*小節が降りるだけで、**その先の候補は splice を続ける**」と書くが、**1 小節目を編集して 59 小節の尾が残っていても 0**。**またぎの門（L274）は 1 回しか発火していない**ので、コメントが名指している門は犯人ではない。
  ⚠️ **`ParseAgreementsHold` の doc は「典型的な正直な失敗＝小節線を消して 2 小節が融合した場合。そういう編集はどのみち下流の状態を変えるので、全 splice を断っても実質何も失わない」と書いている**が、**空白 1 つの行がそれを否定する**（下流の状態は 1 ビットも変わっていない）。  ✅✅ ★★★★ **2026-09-03 に「緩めたら何が手に入るか」も測った。答えは*二重に否*で、⒵ は格下げ。** **実験は自己検証型**（`aBelow != bBelow` の腕を「降りずに*再帰する*」に毒し、各ラウンドで `incremental == full` を主張。**壊れれば「門は必要」が答え・壊れなければ取り分が出る**）。
  **⒜ 取り分は 100%**: 毒版は 8 行とも尾を丸ごと splice（**0 → 59/45/30/15＝利用可能な尾と完全一致**）、**しかも単発の打鍵では出力はバイト一致のまま**。
  ⚠️⚠️⚠️ ★★★★ **⒝ しかし不健全＝網が捕らえた**: **`IncrementalCompilerTests.ChainedKeystrokes_KeepDataPosEqualToFull_WhenSystemsAreCarriedOver` が赤**（87 緑 / 1 赤）。**落ちたのは `data-pos` が古いこと＝full が `700`・incremental が `715`**。⇒ **単発では 8 行とも正しく、*連鎖した打鍵*で初めて壊れる**——**私の探針では原理的に捕まらない種類**で、**捕らえたのは既存の網**（`CollectResumePlanner` の doc が「網が guard の集合を正直に保つ」と書いているのは、まさにこれのこと）。**⇒ 素朴な緩和は既に反証済み。** 直すなら「採用した尾の burned position を*連鎖をまたいで*貼り直す」が要る。
  ⚠️⚠️ **⒞ そして速くもならない**: **`perf-plain1k`（1000 小節）に Δ=+1 の打鍵**（`EditKeystrokeBench` では測れない——**あの 3 冊の編集は全部同じ長さの音高交換で Δ=0**）、Release・3 走の最小: **現行 floor 61.5 ms（splice 合計 0）対 毒版 77.1 ms（splice 合計 3992）**。**median は逆**（123.4 対 97.0）。⇒ **2 つの統計が符号で食い違う＝差は計器の分解能の下**。**理由は読める**: **`ParseAgreementsHold` の合意歩き自体が高い**（同ファイルの doc が「2 本の合意歩きが perf 本では plan の費用の 94〜99%」と実測を書いている）。**現行は編集ノードで即座に降りるので安い。緩めると接頭辞の木を全部歩く**——**splice の節約がその歩きを払って終わる。**
  ⚠️⚠️⚠️ ★★★★ **⒟ そして射程そのものが間違っていた＝「Δ≠0 なら必ず」は撤回する**（同日・実本で測り直して判明）。**上の ⒜〜⒞ と根の診断は*合成した 60 小節 1 冊*の上での話**で、**実本 3 冊に同じ形の Δ=+1 打鍵**（`find` を `replace + " "` に置換＝内容変更は同じで長さだけ +1）**を当てると、落ちるのは 1 冊だけ**（新しい session を毎回建てて*単発*の編集を測る・3 走とも同じ数）:
  | 本 | Δ=0 の splicedMeasures | Δ=+1 の splicedMeasures |
  |---|---|---|
  | `perf-plain1k` | 499 | **499（落ちない）** |
  | `perf-fingbeam1k` | 500 | **500（落ちない）** |
  | `perf-v2bow1k` | 1000 | **0（落ちる）** |
  ⇒ ★★★ **`GreenPrefixAgrees` の腕が発火するのは「長さが変わったから」ではなく、*最初に食い違うバイトがノードの終端に落ちたとき*らしい**——**合成本の 2 例はどちらもそうだった**（空白挿入＝音符の末尾／`e'`→`ees'` は共通接頭辞が `e` を含むので P が PitchE の終端 70 に落ちる＝実測ログの `PitchE old[69,70) new[69,72) limit=70`）。**`g8`→`a8␠` は先頭の文字から違うので P がノードの*境界*に落ち、発火しない。** ⚠️ **ただし `perf-v2bow1k` はこの説では説明できない**（同じ形の編集で落ちる）ので、**その 1 冊の理由は未測定**。
  ⚠️⚠️ ★ **これは「corpus に訊く」の再犯**（§5 の家訓）——**合成 1 冊・4 位置・2 種類でよく揃った数が出たので、射程を測らずに「必ず」と書いた。** **実本に当てたら 3 冊中 2 冊で起きなかった。**
  ⇒ ★★★★ **⒵ の結論**: **⒜ 素朴な緩和は不健全**（連鎖打鍵の `data-pos`・網が捕らえた）／**⒝ 時間では見えない**（floor と median が符号で食い違う）／**⒞ しかも射程は「全部の打鍵」ではなく*一部の本の一部の編集位置***。⇒ **⒵ は閉じてよい項目に近い。** **もし追うなら順番は「まず `perf-v2bow1k` が落ちる理由を測る」**（2 声・prefix resume も `adopted 0` で効いていない本＝他の 2 冊と機構レベルで違う）——**そこに何か在るとすれば ⒵ ではなくその違いのほう。**
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

> ## ▶ **⒩ 展開の予算はページだけのもので、`midi`・`xml`・`ly` は読まない＝1 冊の本に「どれだけ音楽が在るか」で 4 つの出力が食い違う**（第301 起票・**実測**）
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
> ★ **計器はディスクに在る**: `scratch/p301/budget/`（`gen.py` が本を作り、`run.sh` が時間と大きさ、
> `count.sh` が出力ごとの音符数を出す。**`scratch/` は git 管理外なので、無ければ 3 本とも 20 行以下**）。

> ## ✅ **⒧ 3 小節以上の整数小節 `repeat percent` の `%`＝【ユーザー決定 2026-08-29: 診断を出す】**（第282 起票・同便で実装・`85406c45`・LYS2014）
> **`%` は「直前の 1 小節を繰り返す」記号**なので、`repeat percent 2 { A | B | C | D | }` の 4 つの `%` は**読み手に D D D D を指示する**（作者の意図は A B C D）。**音は正しく、紙だけがずれている。**
> **⒤ 今のまま／⒥ 診断／⒦ 別の記譜 の 3 択を出し、ユーザーが ⒥ を選んだ。****⒦ を採らなかったので出力は 1 バイトも動いていない**（全数掃き 899 冊 SAME 899 / MOVED 0）。
> ⚠️ **警告が安全な条件は「コレクタが小節ごとに署名するのと*ちょうど同じ*場所で鳴る」こと**——規則は `PercentRepeatShape` の一軒、**長さの測り方は 2 つのまま**で、**掃きが突き合わせる**（30 冊 94 site・census と 1 冊も違わない）。**この不変条件を壊す変更は、掃きで数を取り直すこと。**
> ★ **残っているのは ⒦ だけ**（`repeat unfold` 相当に書き下す等）。**今日それを求めている本は 1 冊も無い**ので、**起票し直す前に、まず求める本が現れたかを数えること。**


> ## ▶ **⒤ `time none`（senza misura）は描画側で効いていない＝半実装**（第280 起票・**実測**）
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

> ## ▶ **⒥ 小節の途中での改行＝【ユーザー決定 2026-08-29: サポートしない】**（第280・**測って決めた**）
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

> ## ▶ **⒣ `removeEmpty`・`pedal` も「版面ものが part ヘッダに居る」同族＝別便で検討**（第217 起票・**ユーザー指示「別便で検討して」**）
> `lines` を score 側 `as lines N` へ移した決定（§3 第217）の同族が 2 つ残る: **removeEmpty**
> （LP では RemoveEmptyStaves＝context mod）と **pedal**（描画スタイル＝presentation）。
> どちらも part 持ちだと「総譜では隠す・パート譜では隠さない」等の score ごとの使い分けが綴れない。
> 移すなら **`as` 修飾の複数連結**（`staff m as lines 1 as removeEmpty …` か 1 つの `as` に列挙か）の
> 設計から——**判断だけで閉じる型ではなく設計資産が要る**（第215 骨 1 の区別）。着手はユーザー決定から。

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

- ✅ **⒜ 移調 clef の実音オクターブを MusicXML が持たなかった**（2026-08-17・第195 起票／
  **第196 決定／第197 実装＝`cbc5e646`**）。**`<transpose>` は clef のオクターブ込みの*全部*の距離**
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
  **第196 決定／第197 実装＝`47215106`**）。**各 pass は repeat が開いた枠へ戻る**
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
  **第196 決定／第197 実装＝`13a674bf`**）。**`--score NAME` / `--all` を 3 つに足し、
  選ばなかった form を警告で名指す**（`lysc midi test/multi-movement.lys` は 40 音を書いて黙っていた）。
  - **`--all` は 1 score 1 ファイル**——**命名は `svg --all` と同じ規則**（`RenderSpec.ResolveOutputStem`）。
  - ⚠️ **`--combined` は入れていない**——**1 枚に積むのは*レイアウト*で、3 楽章を続けて収めた `.mid` は
    *別の曲***。**LP も `\midi { }` 付きの `\score` 2 つに対して `ts.mid` と `ts-1.mid` を書く**（2.26.0 実測）。
  - ★★ **既定の読み（`main`・無ければ最初）が 3 軒に綴られ、2 軒目が既に外れていた**
    ——**MIDI と MusicXML は ordinal、双子は大小文字無視**。**`form Main` の本では双子だけ別の楽章を描く。**
    **`Semantics/ScoreForms` の 1 軒にした**（**その綴りはコーパスに 0 冊＝だから居られた**）。
- ✅ **⒟ 小節の途中の `clef` は相対枠を付け替える（ページが規則）**（2026-08-17・第195 起票／
  **第196 で決定 ＋ 実装 ＝ `ce408263`**）。**3 冊とも閉じた**（`clef-change`・`mmr-clef-change-bound`・
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
  - **和音のタイが MIDI で併合されない 8 冊** → ✅ `1c5d9c48`。**残差が MusicXML 側の
    第2の欠陥を出した**（不一致メンバーに「始まっていない tie-stop」）→ ✅ `a33e1602`。
  - **phrase の自動移調が MusicXML に効かない 2 冊** → ✅ `d51a63e2`。
    **調べたら島はもっと大きかった**——**attributes が 1 度しか出ず、しかもファイル中*最後の*値**。
  - **section 境界の相対枠を MIDI だけ持ち越す 2 冊** → ✅ `c690d5cd`（**既定音価も同じ lane に乗っていた**）。
  - **MIDI の天井 127 で黙って潰す 2 冊** → ✅ `eebd477d`（**警告を出すようにした**。
    ⚠️ **差そのものは消えない**——**MIDI が 128 鍵しか持たないという外部制約**で、
    **ページと MusicXML は書かれたオクターブを保つ**のが正しい）。
  - **小節途中の `clef` 3 冊** → **上の ⒟ のまま**（要ユーザー決定）。
- ✅ **⒡ 2 冊の fixture が、書いた人の意図しない音楽を彫っていた**（2026-08-17・第196 実測／
  **第197 でユーザー承認 ＋ 修正＝`6c38b5a5`**）。**各 section の最初の音にだけ `'` を残した。**
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
  （2026-08-17・第197 で起票・実測・修理＝`890b2fa2`（MIDI ＋ 計器）・`122c7c0b`（MusicXML）・
  `a7259e8c`（双子））。**素の section の持ち主は `score` が決める**——
  **`score main { staff bl }` はこの音楽が part bl のものだと言っている唯一の文**で、
  **ページは前からそう読んでいた**（`RenderSpecParser` → `GetPartDefaults`）。
  **MIDI は `score` を 1 行も読まず、MusicXML は "Part 1" という*誰も宣言していない名前*で出していた。**
  ⇒ ★★ **道具は 1 軒**＝`RenderSpec.EngravedPartNames`（**staff ではなく*相異なる part*を数える**
  ——`staff bl  tab bl` は 1 つの part を 2 譜に描いているので**1**）。
  ⚠️ **score が 2 つ以上の part を名指したら今のまま**（**ページは同じ音楽を 2 つの register で描くので、
  1 本の流れはそのどちらでもない**）。**射程 0 冊なので、これは修理ではなく*定義*。**
  ⚠️ ★★★ **双子は「戻す」のではなく「差を書く」側だった**（4 軒目・`a7259e8c`）——
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
    **⒡ を直したら 2 つ目の症状が出た**（`6c38b5a5`）: **section 境界の枠の戻しも効かない。**
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
  - ⚠️⚠️ ★★★ **計器も同じ穴を持っていた**（`c7ebdd9b`）——**MIDI を直した瞬間に `test/bend` が
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
    <!-- ledger: mid-piece.tab-numbers.meter-identity = 0 -->
    <!-- ledger: mid-piece.tab-numbers.change-bar-vs-plain-bar = 0 -->
    <!-- ledger: mid-measure.tab-numbers.meter-identity = 0 -->
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
  - ✅ **閉じた側 ⑴ ＝文法**（`ea6a71eb`）。**`font` は役どころごとに face を束ねられるようになった**
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
    **`d4fdfd33` 漏れ止め／`8313bc33` face 鍵／`2ed165a5`＋`350f349a` 配管／`a7b28e58` 網と文書**）。
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
  - ✅ **SVG の同梱 sans のずれ＝第215 が閉じた**（`46b595b0`・ユーザー委任「見えている
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
- ✅✅ ★★★ **cue の島は第178 で閉じた（2026-08-15・実装 3 便）。隣り合う cue は 2 声部になった。**
  **診断（`e368f995`）と spacing（`f045872e`）が同じ刻印 `MusicItem.BeginsCueRegion` を読む。**
  ★ **刻印は「領域番号」ではなく「領域の縁」**——番号は collect resume の suffix splice が
  **別の walk で採番された tail** を貼り付けるので破れる。縁の印は*自分の領域の中での位置*だけの
  関数なので貼り付けても正しく、position 非依存なので `MeasureContentKey` も畳み続ける。
  **識別子が要る読み手は既に歩いている所で導出する**（スラーは走査中に数える／タイの結び先は
  次の音符なので「結び先が領域を開くか」で足りる／spacing は「領域が対を跨いで手前へ届くか」だけ）。
  ★ **spacing 側は先に測ってから移植した**（`voice-boundary-spacing.ly` §F・**同じ 4 音で
  `\new CueVoice` が 1 個か 2 個かだけが違う対**）: 境界の歩幅は **2.898044999134611（素の ideal）
  対 2.513393907138011（精錬済み）**。台帳 `cue.column.region-edge` −0.384653432 → **0（exact）**、
  対照 `cue.column.region-edge-control` は **−0.000002340 のまま不動**。
  <!-- ledger: cue.column.region-edge = 0 -->
  <!-- ledger: cue.column.region-edge-control = -2.34e-06 -->
  ⚠️ ★★ **「閉じたら −0.000002340 に着地するはず」と書いた予測は外れて 0 になった**——
  **その丸めは head-width 項に乗って来ていた**ので、**項ごと消えた**（外れ方が機構の裏取り）。
  ⚠️ **残る近似は「別々の声部で cue 領域が*同時に*生きている」形だけ**＝
  **右列に領域を*開かない* cue item が居るとき**は精錬を残す（移植前の挙動）。
  **観測者 0・書ける本も 0**（1 小節に cue 領域を 2 つ書く本がディスクに無い）。
- ✅✅ ~~**fixture が今の文法で parse しない**~~ — **第182 で閉じた**（`d49814a2`・
  **ユーザー決定＝直す**）。`test/multi-movement` は**3 section / 3 form / 3 score** へ、
  `showcase/grammar-2026-06-09` は**期限切れの主張 3 つを訂正**して現行文法へ。
  ★ **副産物**: multi-movement が**ツリーで唯一の「form が 2 つ以上ある本」**になった
  （実測：それまで 0 冊）＝`FormDeclarationValidator` が存在理由にしている配置に観測者がついた。
  ⚠️ **どちらも snapshot リストに無い**（実測）ので再ベースは起きていない。
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
- ✅✅ ★★★ **chords 行 / lyrics 行の検証は第180 で閉じた**（`290199bf`・ユーザー決定）。
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
  （`9bf7914a`＋`53686d66`）。**`staff|tab … with chords C` / `with lyrics L`** は
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
  （`4bc125e0`＋`a0809fc9`・**LYS0028・ユーザー決定＝warning**）。
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
- ✅✅ ★★★ **top-level でない `using` は第183 で閉じた**（`9ef5300b`・**LYS0029・ユーザー決定＝error**）。
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
  *嘘*で、その嘘は計器から来ていた**（`3ffcba9e`＋第2便・**ユーザー決定＝動かす**）。
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
  ⚠️ **`'(N)`（フレーズ参照の音程）は 2026-08-28・第278 で廃止された**（ユーザー決定）——この行が測っていた「absolute でも効く」はもう検証できない。**オクターブ印 `'` / `,` は残っている**ので、**その部分だけが今も生きた記述**。
- ✅✅ ★★★ **「置けないトークンが黙って消える」は第185 で閉じた——器は 3 つではなく 6 つで、
  そのうち 4 つは*報告していた***（`b78964f5`＋`8f0bb220`・**LYS0030・ユーザー決定＝error**）。
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
- ✅ **⑺ 記号の*後ろ*に書いた post-event が木では*前*に出る＝閉じた**（起票 第185 → 第186 が測り直し → **2026-08-30 に `ParsePostEvents` が「書かれた順のまま置く」ようになって解消**）。
  ⚠️⚠️ ★★★ **この見出しは 2026-08-31・第306 まで「木の形は決定待ち」の札を付けたまま残っていた**——**閉じたのはその 1 日前**。**第306 がユーザーに「判断すべきことは何か」を一覧で出したとき、この項を*札のまま*数えて渡してしまった**（→ RULES §5.3「未決定の札は実コードで確かめる」）。
  ★ **閉じた証拠は番人 2 本の*空の*既知一覧**: `AnnotationRoundTripTests` の
  `EveryBook_SpellsItselfBackOutOfItsTree` と `EveryNodeInEveryBook_StandsWhereItSaysItStands`——**どちらも「2026-08-30 以降 空。名前が現れたら*新しい欠陥*」と書いてある**。**11 冊は 11 個の本の欠陥ではなく 1 個のパーサ挙動だった。**
  ⚠️ **round trip はいま parser の*無条件の*不変条件**（空のラチェットもラチェット）。**以下は起票当時の経緯で、同じ棚に触るときの資料。**
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
  ✅ **実害はもう止まっている**（第186 第3便 `343d755d`・**ユーザー決定＝⒝ を先に**）——
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
  ★★ **2026-08-30・第294 追記＝⑴ 検出器の残り半分を足した ⑵ 射程を数え直した ⑶ §2 U9 を合流させた。**
  ★ **⑴ `AnnotationRoundTripTests.EveryNodeInEveryBook_StandsWhereItSaysItStands`**（`12599c12`）
  ——**RULES §5.0 が「2 つで 1 組」と書いていた*各ノード*の側**で、**コーパス規模では 1 本も無かった**
  （在ったのは inline 2 本と狭い 3 ファイルだけ）。**2 つの半分を持つ**: **⒜ `FullSpan`（`Position` と
  `ToFullString`）⒝ `Span`（trivia を食っていないか＝*空白でもコメントでも始まらず終わらない*）**。
  ⚠️ **⒝ は「幅から導かない」ことが値打ち**——**ノード自身のテキストを切り出す書き方は同じ
  `GetLeadingTriviaWidth` から来るので、U3 の誤答と一緒にずれて一致してしまう。**
  ★ **毒**（戻した）: **`SyntaxNode.Span` を U3 の綴り（`Green.LeadingTriviaWidth`）に戻すと
  ⒝ が 569 冊で赤・`EveryBook_SpellsItselfBackOutOfItsTree` は緑**。**⒜ の陽性対照は既知一覧そのもの。**
  ⚠️ **予測を 2 つ外して、どちらも初回の run が訂正した**（**書く前に測っていれば要らなかった**）:
  **⒳「2 つの一覧は違うはず」→ 同じ 11 冊**（**この島では `A+M == M+A` が成り立たないので、
  和の検査も必ず落ちる**）／**⒴「`Span` 側は 0 のはず」→ 同じ 11 冊**（**再生された記号は音符の
  後続空白の*後ろ*に落ちるので、`(` と綴るノードが `" "` を含む span を名乗る**）。
  ⇒ ★★ **収穫は ⒝ のメッセージのほう**——**冊名ではなく*ノードと誤った住所*を出す**ので、
  **読み手がプレビューをクリックして失う量そのものを名指す。**
  ★ **射程は `CorpusBooks()` の 1 軒に寄せた**（2 つの掃きが各自ディレクトリ一覧を持っていた＝
  §5.2.1② の 2 つ目の綴り）。**床は 566 → 580**（**引き算の実測: 追跡 580 冊に対し射程 580＝差 0**）。
  ★ **⑵ 数え直し（ディスク上 1629 冊・2026-08-16 は 1021 冊で 55）**: **62 冊・sum と span で同一集合**
  ——**ユーザーの 326 冊のうち 40 冊 63 か所**（**`~@mark` 53・`~@trill` 1・`)@fall` 3・
  `)\4` `)\2` `)\3` 各 2**）／**追跡 11 冊**／**scratch のプローブ 11 冊**（第186・283・293・294）。
  ⚠️ **ユーザー側の 40 冊は 2026-08-16 から動いていない**——**この島は増えても減ってもいない。**
  ⇒ ✅✅✅ ★★★ **閉じた（2026-08-30・第294・`65224424`・ユーザー決定＝「木を原文の順に忠実にする。
  実装コストや移行コストは考慮すべきではない」）。** **`ParsePostEvents` が記号を queue に入れず
  post-event の一覧へそのまま足す**——**木の順序＝原文の順序**になり、**`EveryBook_SpellsItselfBackOutOfItsTree`
  と `EveryNodeInEveryBook_StandsWhereItSaysItStands` の既知一覧は 11 冊 → 0 冊**。
  **round trip は parser の*無条件の*不変条件になった。**
  ★★★ **直しは 1 行で、そのあとにコーパスが 2 つの帰結を出した**（**どちらも自分では思いつかず、
  掃きが名指した**）:
  **⑵ 記号は 2 か所に立ちうる**（**別の post-event より*前*に書けば host の子・*最後*に書けば次の項**）。
  **上の歩きは何も要らない**——**`MusicSitesLazy` は音符の*中*へ降りるので、子の記号は兄弟と同じ位置に並ぶ**。
  **容器の body は違う**——**tuplet／cue／repeat／inline ending は `Body.Items`＝*直下の子*を歩く**ので、
  **音符の中の記号は一覧に無い**。**実測 `audit/lpreg/tupnumss.lys`＝`tuplet 3/2 { e8(@accent e8 e8) }` が
  スラー開きを落とし、「`)` に対する `(` が無い」と警告し始めた**。⇒ ★★ **第293 のリハーサル記号の
  発見と同じ容器・同じ直下歩き・落ちるものだけが違う鏡像**。**直しは lookahead を host 自身の
  post-event から seed する 1 か所**（`FoldOwnMarkers`）。
  **⑶ 型フィルタは物が落ちる場所**——**`RestSyntax`／`ArpeggioSyntax`／`ChordSyntax` の `Articulations` は
  注釈系の種別しか名指していなかった**ので、**緑の木は*どの accessor も渡さない*ノードを抱えた**。
  **実測 `audit/lp-regression/lys/empty-chord.lys`＝`<>)@text("sul D")` が MusicXML と LP 双子の
  両方からスラー閉じを無言で落とした**（**絵は正しいまま**）。⇒ ★ **3 つのフィルタが記号の種別を名指すようにした**
  （**`PitchSyntax.Articulations` が「弦番号を数か月呑んでいた」と書いている警告の隣**）。
  ⚠️⚠️ ★★★ **2026-08-16 の census の「代償」は請求書より大きかった**（§5.0「処方箋の否定形と肯定形を
  分けて読む」）: **⒜ 生存 12 経路のうち `TieTargetScanner`・`SlurPairingScanner` は*収集済み item* を
  読むので下流＝無関係**（動いたのは lookahead 2 か所だけ）／**⒝ 死んでいた 6 経路は目を覚まし、
  しかも*一致した*——MIDI と MusicXML は 1630 冊バイト同一**／**⒞ `smartTyping.ts` は 1 行も要らない**
  （**両方の綴りが忠実になったので、打鍵補助がどちらに置こうと木は原文を写す**）。
  ★★ **全数 A/B は 4 出力で取った**（`scratch/p294/sweep4.ps1`・**SVG だけの掃きは ⑶ に構造的に盲目**
  ——**あの欠陥は exporter の中だけに居た**）: **1630 冊で svg MOVED 57／midi 0／xml 0／ly 62・
  `lysc check` の差 0・レンダー失敗 0**。★★★ **動いた 2 列は*確かめた*、仮定しなかった**
  （`scratch/p294/verify-moved.ps1`）: **svg 57 は `data-pos` *だけ*が違う**（伏せるとバイト同一＝
  **1630 冊のインクが 1 点も動かない**）／**ly 62 は*並べ替えだけ***（**同じ文字・increase も loss も 0**
  ＝`)` が双子から消える形を捕まえる検査）。
  ★ **番人は「2 つの綴りは同じ音楽」の恒等で書いた**（`PostEventOrderTests.TheTwoOrdersOfOnePostEventRun_AreTheSameMusic`・
  6 対）——**どちらを parser が好むかに依存しない形**。**毒 2 本の効き方が違う**:
  **own-marker の seed を外すと*tuplet の対だけ*赤**（**上の歩きの対は緑のまま＝⑵ の構造の主張が実測になった**）／
  **`ChordSyntax.Articulations` から記号を外すと*空和音の対だけ*赤**。
  ⚠️⚠️ ★★★ **そしてその 2 本目の毒が、私の書いた網の嘘を暴いた**——**1 度目は*緑*で返った**。
  **`MusicXmlExporter.Export` は文書*モデル*を返すので `.ToString()` は型名**で、
  **同じ定数どうしを比べていた**（6587 本が通る、落ちようのない比較）。**`ToXml()` で直列化して初めて赤。**
  ⇒ **RULES §5.4 の「検査器は落ちることを先に証明する」が、そのまま効いた。**
- ✅✅ ★★★ **`lyrics` のハイフンが隣の空白を落とす件は第186 で閉じた**（`3b672d88`）。
  **`lyrics L { la -- la }` が木で `la-- la`＝幅 24 → 22**。**第183〈`using`〉・
  第185〈迷子トークン〉と同じ一族の 7 つ目の器**で、**起票すらされていなかった**——
  **`EveryBook_SpellsItselfBackOutOfItsTree` の射程外**（`audit\lpreg` 257 ＋ `samples` 6）に
  居たから。**射程は 300 → 566 冊＝git 管理下の全数**（`036d4bb1` ＋ `9d552948`。
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
  （`a14e3b6c`・**LYS6007・ユーザー決定＝LYS6002 と同じ error**）。**⑻ の前提を測っている途中で出た。**
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
  「4 出力のうち 2 つだけが消していた」だった**（`bad2d130`＋`dbed5899`＋`6ab664ef`・
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
- ✅✅ ★★★ **「誰も出さない診断コード」は第187 で 7 件とも引退した**（`029986ef`＋`8f55ec08`・
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
  （`ffff6b0a`）。**RenderSpec 無しの素の collect** が section 内の 2 つめの part block に
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
  （`0cfcf48b`＋`69bfd957`・**どちらも LP 照合 → ユーザー承認 → 再ベース**）。
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
- ✅✅ ★★★ **score 単位の `transpose` が 3 通りの答えを返す件は第182 で閉じた**（`077e5c98`）。
  **原因は 1 行**——`PartTranspose.ReadScoreDefault` が「part の中でない `transpose`」を
  ツリー全体から拾うので、**score の transpose を*ファイルの既定*として数えていた**。
  ⇒ **宣言した score は 2 回**（既定＋自分の `RenderSpec.ScoreTranspose`）＝長3度、
  **他の score は 1 回**（頼んでいないのに D 長調で彫られる）。**両方とも同じ 1 行**なので
  **ガード 1 つ**（render 宣言の中の transpose はその score のもの）で消えた。
  ★ **3 綴り（part ヘッダ・top-level・score 単位）が `d`＝長2度で一致した。**
  ⚠️ **毒（ガードを外す）で赤になるのは新しい 4 本だけ・既存 5061 本は 1 本も落ちない**
  ＝**この欠陥が生き延びていた理由そのもの**。
- ✅✅ ★★★ **`lysc ly` が `transpose` を 3 綴りとも落としていた件は第194 で閉じた**（`087d1e53`）。
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

- **G-pdf. ⚠️⚠️ 起票（2026-09-01・第317）＝PDF の font resolver はプロセスに 1 つしかなく、2 文書が互いの face を上書きする**

  ★★★ **第317 が*競合*のほうは閉じた**（`0e45222f`＝`EmmentalerFontResolver._textFaces` は不変スナップショットを丸ごと差し替える。**素の `Dictionary` を `Clear()` して詰め直していたので、2 文書同時で `Operations that change non-concurrent collections must have exclusive access` が出ていた**）。**残るのは*設計*のほう**:
  **`PdfDocumentContext.EnsureFontResolver` はプロセスに 1 つだけ resolver を据える**（**PdfSharpCore の `GlobalFontSettings.FontResolver` は 1 回しか設定できない**）が、
  **`SetTextFonts` はその 1 つを*文書ごとに*書き換える**。⇒ **違う `fonts { }` を持つ 2 文書を同時に作ると、後から書いたほうの face で両方が埋め込まれうる。**
  ⚠️ **これは第317 が作った穴ではない**——**`EnsureFontResolver` の remark が前から「一 shot の CLI では無害・long-lived host（LSP など）では latent」と書いている**。**第317 はその文の*半分*（地図の破壊）だけを閉じ、もう半分（どの文書の地図か）は開けたまま残した。**
  ★ **閉じ方は「face を文書ごとに鍵付ける」**——`ResolveTypeface` が呼ばれた文脈から文書を引けないので、**face 名そのものに文書を混ぜる**のが素直（`LysEmbed:…#` が既に名前に情報を載せている形）。
  ⚠️ **観測者はまだ居ない**: **CLI は 1 プロセス 1 文書**で、**suite は同時に PDF を作るが `fonts { }` は同じ**。**LSP / プレビューが 2 つの本を同時に PDF にした日に出る。**

- ✅✅ ★★★★ **【閉じた・2026-09-01・第313】この hang は*起動のしかた*で、回避策は 1 行。正典 2.26.0 は 15 秒で完走する。**
  **`cmd.exe /d /s /c "<lilypond …> < NUL > log 2>&1"`＝*デタッチして stdin を NUL から与える*。**
  ```powershell
  cmd.exe /d /s /c "C:\bin\lilypond-2.26.0\bin\lilypond.exe -dbackend=svg -dno-point-and-click -o out in.ly < NUL > lp.log 2>&1"
  ```
  ⚠️ **これは*新発見ではない*——`memory/reference_lilypond_mcp_console_hang.md` に 80 日前から書いてあった**
  （**lilypond.exe を MCP コンソールの*子*として起動すると Guile 初期化でデッドロックする。CPU を数秒使って止まり、
  WS 28 MB のまま**＝**下に書いてある「署名」そのもの**）。**第302・第308・第309 の 3 便が、この署名を読みながら
  memory を引かなかった。** ⇒ ★★★ **汎化＝「この機械のツールが動かない」は*環境の記憶*に当たること**（RULES §5.3）。
  ★ **第313 実測**: **14 冊 1 launch で 15 秒／さらに 6 冊足して 3 秒**（`scratch/p313/lp/g1..g3.ly`）。
  ⚠️⚠️ ★★★ **だから「正典で取り直す」は*計画に入れてよい*。** **第309 が ⒝2 を見送る理由の筆頭に挙げていた前科は、無い。**

  ⚠️⚠️⚠️ ★★★★ **そして代替のほうが消えた＝*この機械に WSL はもう無い***（2026-09-01・第313 実測）。
  **`wsl -l -v` は "no installed distributions"**——**`Ubuntu-24.04` は §0 の ubuntu Release 脚でもあり、
  第308・第310 が LilyPond を測った場所でもある。** ⇒ **§0 の WSL 行は今この機械では回らない**（→ §0）。
  **LilyPond は上の正典 1 本だけになったが、それは*台帳が要求している版そのもの*なので、質はむしろ上がった**（RULES §5.2）。

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

- ⚠️ ★★ **コード内の `HANDOFF §1 ⒪` 参照 4 本は宛先が無い**（2026-08-31・第306 に発見・**未修理**）。**`§1` は毎便*書き換える*節なので、そこの letter を指す参照は書いた次の便から宙に浮く**（`IncrementalCompilerTests.cs:771` `⒪′` ／ `LpGeometryProbes.cs:10403` `:10501` `:14091`）。⚠️ **`▶ ⒯` `▶ ⒭` の族とは*別の壊れ方***——**あちらは letter が*再利用*されて「今日の別の項目」に当たる**（第306 が 14 本まとめて retired と明記した）が、**こちらは当たる先が*存在しない*。** ⇒ **直すには ⒪ が何だったかを `HANDOFF-ARCHIVE.md` で特定する必要があり、本便はそこまでやっていない。**★ **原則としては同じ**: **コードから引くのは*主題*であって letter ではない**（RULES §5.1）。
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
が **1.604200 で exact**。<!-- ledger: compressed.note-to-note.quarter = 0 -->
`SeparatingPaddingTests` は LP 由来の期待値に書き直し済みで、
「`MinItemGap` を何に設定しても音符間が動かない」ことを主張するテストを追加＝**戻ってこない**。

- ✅ **歌詞の列間隔の発明 2 つ＝第222 で移植完了**（`e8e854b3` 起票 → `771dc57a` 移植）。
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

- ✅ **歌詞の小節線またぎ＝barline-split モデル＝第223 で移植完了**（`3a635a6d`・
  `lyrics.column.word-gap.cross-barline` **+0.540000000 → 0.000000000 ちょうど**）。
  <!-- ledger: lyrics.column.word-gap.cross-barline = 0 -->
  **LP は音節と小節線のあいだに何も予約しない**（extra-spacing-height (0.2 . -0.2)）——
  継続する歌詞線は halves（0.4＋bar ink＋0.4）を落とし、`CrossBarLyricRodDistance`＝
  inkR＋0.45/0.1＋inkL−bar ink を rods リスト（ApplyRods）へ。**門は各小節の
  `LyricBarPricing` 半分を持ち、KP の cumPairMin が break 時に結合**（結合済み excess は
  4 鍵量で memo の 3 鍵窓に入らない——§1 第223 骨 1）。**halves の残り＝行の最初/最後の
  音節 vs 小節線・折りで千切れた対の行頭床/行末予約は LILYSHARP-OWN のまま**
  （未測定 regime＝LP の折り行末 hyphen 予約を含む。測るなら `\break` 対から）。
  ✅ **在庫だった「多声小節の byItem 予約写像」は第225 で閉じた**（対 `679dec4c`・移植
  `6c00d15a`＝byItem 分岐退役・全予約 TIMING 列。行歌詞の均等スロット ItemIndex も同欠陥
  だった＝sweep 3/569）。✅ **⒥ スパン予約の rod 化は第226 で閉じた**（対 `76a49f34`・移植 `ce72c1ac`＝
  中間列を跨ぐ音節間予約を ReserveLyricLine → ApplyRods の真 range rod に。width/first-gap
  とも exact 着地・隣接 bump と halves は据え置き）。**残りは §1 ⒦**＝歌詞 sliver 族
  （voice-blind な列表 +0.0366／+0.0732・観測者は lyrics.column.bound-voice.primary-control /
  no-bind.skip-gap / bound-voice.skip-gap）。
  <!-- ledger: lyrics.column.bound-voice.primary-control = 0 -->
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
- ✅ **⑶ record モデルの同値性（identity の欠如）**（**2026-08-31・第307 に閉じた**。**方針と判断軸は第306**）。
  音楽モデルが C# record なので **unison・同音の 2 項が「等しい」**——`IndexOf`／`Contains`／
  `Dictionary` キーが黙って衝突する。実例 = fixed 第18号（`TieItem` の unison 対が
  `ordered.IndexOf` で両方 slot 0 → DOWN 弓の 2 重描き・`ReferenceEquals` の `FindIndex` で
  回避）。それまでの規律: **モデル項目のコレクション検索は参照一致で書く**。

  ⚠️⚠️ ★★★ **ユーザー方針（2026-08-31）＝*一律に書き直さない*。型ごとに record か class かを判断する。** **「Id を値の一部にする」案は*採らない***——**`with` 式が Id ごと複製するので「同じ Id を持つ 2 項目」を作れてしまい、塞ぎたい穴が復活する**（実測: `with` は Svg/ 196・Rendering/ 25 箇所）。

  ⇒ ★★★ **型ごとの判断軸＝「実体（identity）か、値（value）か」。3 つの問いで決まる**:
  **⒜ 内容が同じ 2 つが*同時に存在しうる*か**（unison のタイ 2 本・同じ休符 2 つ）→ Yes なら実体。
  **⒝ コレクションの中で「この 1 つ」を指す必要があるか**（`IndexOf`/`Contains`/辞書キー/削除）→ Yes なら実体。
  **⒞ 小さくて複製が自然か**（拍子・調号・座標・拍）→ Yes なら値＝`record struct` のまま。

  ⚠️⚠️ ★★★ **第307 の結論＝「record か class か」は*偽の二択*だった。第3 の道が両方に勝つ。**
  **`record` のまま `Equals(T?)` と `GetHashCode()` を自分で書くと、コンパイラは合成をやめて
  そちらを使う**——**`with` も分解も `ToString` も残り、`==`・`Equals(object)`・`IndexOf`・
  `Contains`・`Dictionary`・`HashSet` が全部そこを通る**（`scratch/p307/eqprobe` で実測）。
  ⇒ ★★ **これで「争点は Staff / Measure / Voice」が消える**——**争点は「実体だが `with` の
  集中先でもある」ことだったので、`with` を失わない道が在れば対立自体が無い。**
  ⚠️ **`class` 化のほうは `with` を消すので、Staff 38 / Measure 15 / Voice 9 / StaffGroup 7 …
  100 行超を手書きのコピーコンストラクタに置き換えることになる**（**ユーザー承認: 第307**）。
  ★★★ **`MusicItem` 族は*抽象基底 1 か所*で足りる**——**派生 record の合成 `Equals` は
  `base.Equals(...)` から始まるので identity がそのまま伝播する**（実測）。**7 型で宣言は 1 つ。**

  ⚠️⚠️ ★★★ **⒜ は「ありうる」ではなく*実際に出る*——574 冊で実測した**（第307・
  `scratch/p307/twinscan.txt`。**collect 相の反射走査 ＋ tie/slur/beam/gliss の検出器**）:

  | 型 | 内容同一の双子の対 | 冊 | 出どころ |
  |---|---:|---:|---|
  | `MusicItem` 族 | **396** | 4 | **`repeat tremolo` が書かれた 1 群を同一内容の `NoteItem` 多数に展開する**（`wntacc[cdef].lys`） |
  | `VoltaBracketItem` | 4 | 2 | `grandstaff-repeat` ほか |
  | `TieItem` | 2 | 1 | **`chord-X-align-on-main-noteheads`＝第18号と同じ形が今も corpus に居る** |
  | `Measure` | 1 | 1 | `grammar-tour` |
  | ほか 20 型 | 0 | — | 走査は届いている（型名は twinscan.txt の見出し行） |

  ⚠️⚠️ ★★★ **第306 の「値同値性に依存する箇所は*ゼロ*」は*外れ*だった＝3 型ある。**
  **静的な数え方が `Distinct()` と `IndexOf` を探していたので、*まるごと 1 個を比べる*綴りに
  盲目だった**——**`SequenceEqual` と `==` はモデル項目そのものを値で比べる。**
  ⇒ **見つけ方は毒**（第307）: **39 型全部に identity を入れて全テストを回すと 5 本赤くなり、
  1 本は行番号の artefact、残り 4 本が 3 型を名指した**:
  - **`GrobOverride` / `GrobRevert`**——`IncrementalCompiler.cs:392-394` の `overridesUnchanged`。
    **コード内コメント自身が「the override/revert collections themselves are compared by value
    below」と書いている**（⚠️ **反証は最初から手元にあった**の再演）。
  - **`BeamRestStem`**——`BeamDetector.cs:350` の `a.RestStems.SequenceEqual(b.RestStems)`（memo の健全性）。
  ⇒ ★★★ **汎化＝この木で値同値性が担っている仕事は 1 種類しかない: 「いま計算し直したものは、
  しまってあるものと*同じ値*か」＝キャッシュの妥当性判定。** **それ以外の用途はゼロ**（第306 の
  `Distinct()` 20 箇所は全部*射影されたスカラー*、という調べ自体は正しかった）。

  ⚠️⚠️ ★★★ **第306 の「`ReferenceEquals` 30 箇所は回避であって活用ではない」は*数も読みも*外れ**
  （第307 実測）。**数え方が書かれていなかったので再現できない**——**下のレシピは今日 66 を返し、
  そこから*本便が足した 25 行*を引いた 41 が「第306 が数えられたはずの数」**（**Svg だけなら 53 − 25 ＝ 28**）:
  ```powershell
  # 註釈行と `ReferenceEqualityComparer` を除いた「実際に呼んでいる行」
  @(Select-String -Path (Get-ChildItem LilySharp.Core -Recurse -Filter *.cs |
      Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) -Pattern 'ReferenceEquals' |
    Where-Object { $_.Line -notmatch 'ReferenceEqualityComparer' -and $_.Line -notmatch '^\s*//' }).Count
  ```
  ⚠️ **読みのほうがもっと外れている**: **28 行の大半は双子への防御ではなく、memo の
  「同じ*物*ならどう見ても不変」という*安い早道***（`AboveStackMemo` `BelowStackMemo`
  `FingScriptMemo` `LyricChainMemo` `VerseSkylineMemo` `SystemLayoutCache` `VerticalSkyline`
  `IncrementalCompiler:494`）。**双子への防御は少数**——`ElementCoordinator:2055`（第18号の修理）・
  `MeterStencil:116/143`・`SpacingRules.MidMeasureChanges:445/572/601`・
  `SharedRenderer.Prefix:313/335`・`TabResolver:203`・`MusicXmlExporter:1275/1964`・
  `LayoutEngine.Finishing:309`。⇒ ★★ **同じ綴りが 2 つの別々の仕事に使われているので、
  数を 1 つにまとめた瞬間に読みが壊れる**（§5.3 の「数を引き継ぐときは数え方も書く」の
  *意味*版——**数え方だけでなく「何を数えたか」も書く**）。
  ⚠️ **本便の commit メッセージ `478d73b6` はこの「30」をそのまま引いてしまった**（訂正はここ）。

  ✅ **実装（第307）**: **実体 31 型に identity・値 8 型はそのまま**。**宣言の編集は 25 か所**
  （`MusicItem` 基底 1 ＋ 単発の印 18 ＋ 容れもの 6）。**規則の家は
  `LilySharp.Core/Svg/Model/ModelIdentity.cs`**（**実体側の `GetHashCode` は全部
  `ModelIdentity.HashOf(this)` を通るので、規則に grep で当たる住所が 1 つできる**）。

  | 側 | 型 |
  |---|---|
  | **実体 31**（1 つの*出来事*） | `MusicItem` 族 7（`NoteItem` `RestItem` `ChordItem` `ClefChangeItem` `KeySignatureChangeItem` `TimeSignatureChangeItem`）／単発の印 18（`Articulation` `ChordName` `CrossStaff` `CustomText` `Dynamic` `FiguredBass` `GraceNote` `Hairpin` `Lyric` `MusicMark` `OttavaBracket` `PercentRepeat` `Slur` `TextSpanner` `Tie` `TrillSpanner` `TupletBracket` `VoltaBracket`）／容れもの 6（`Measure` `Voice` `Staff` `StaffGroup` `Score` `MultiStaffScore`） |
  | **値 8**（1 つの*記述*） | `GrobOverride` `GrobRevert` `BeamRestStem`（**実測で赤くなった 3 型**）／`BeamGroup` `BeamMember` `BeamLayout`（同じ memo の族）／`VoiceColumn` `VoiceEntry`（列の導出） |
  | **`record struct` 11** | ⒞ のまま。**class 化は性能の後退で、しかも参照同値がそもそも無いので unison 問題は class 化では直らない**（必要なら添字か Id で指す別の設計） |

  ★★ **札は `ModelEqualityKindTests` が保持する**（**新しいモデル型は*どちらかの札を貼るまで*
  赤い**・**実体が値に戻っても・値が実体になっても赤い**＝両向き）。⚠️ **札は*振る舞い*で読む**
  ——**`MusicItem` 族は基底に override が在るので、宣言の形で読むと派生 6 型を「値」と誤判定する。**
  **`GetUninitializedObject` で全フィールド既定の 2 個を作って `Equals` に訊く。**
  ★ **3 本とも毒で赤を見てから置いた**（実体の override を剥がす／値に identity を足す／
  札の無い型を足す）。
  ⚠️ **性能の前提は 2 つの kind で*逆*になる**（2026-08-31・第306 実測）:
  **参照型 `record` は `class` と同じもの**なので、**違うのは合成される `Equals`/`GetHashCode` が
  *全フィールド*を比較・ハッシュすること**だけ——**`Contains`/`IndexOf`/`Dictionary` が呼ばれる
  場所では record のほうが*遅い*。** ⇒ **今回の 31 型は*速くなる側***（参照比較はポインタ 1 回）。
  **`record struct` は本当に有利**（ヒープ割り当てが無い）＝**温存が既定**（RULES §5.6）。
  ⚠️ **第306 の計器 `scratch/p306/modelscan.py` は `abstract record` を数えていなかった**
  ——**正規表現が `public` の直後に `readonly?`/`sealed?` しか許しておらず、`public abstract
  record MusicItem` を落としていた**。**「全 50 型」は 51 型で、落ちていた 1 つが*項目階層の
  基底*＝この節で一番効く型だった**（→ RULES §5.3）。
- ★ **⑷ collect 相と layout 相の二重解決**——§2A の既存項
  「多声の譜が `VoiceCollector.Collect` と `NoteCollision` を 2 周する」を指す（詳細・実測
  +0.3%・畳み方 2 案はそちら）。⚠️ **着手前にコスト判断**（§2A に明記済み）。

### D. 文法の変更候補（効率の観点・**3 点とも要ユーザー判断＝勝手に実装しない**）

> ユーザー問「効率的な処理のために文法を変えるべき所はあるか」への答えの台帳化。
> 文法変更は言語設計＝ユーザーの決定事項。ここには**提案と根拠**だけを置く。

- ✅ **⑴ オクターブアンカー（絶対指定）構文は*既に在る*＝`octave absolute` / `octave N`**（2026-08-31・第306 に確認。**`GRAMMAR.md:75-82` と `OctaveDecl`**——**top-level・part ヘッダ・*楽中*のどこにでも書け、`octave 2` で絶対基準を貼り直せる**。**追跡 fixture でもユーザーの本でも実際に使われている**）。
  ⚠️⚠️ ★★★ **提案文は「現行は相対のみ」と書いていて、それが誤り**——**この節の ⑶ が付けている家訓「台帳の『〜が無い』は Lily# 側の語彙を*検索してから*言う」に、同じ節の ⑴ が違反していた**（第113・第306 で 3 例目）。
  ✅✅ ★★★ **残っていた増分の問いは 2026-09-03 に測って閉じた＝止まっている。⑴ は丸ごと閉じる。** **`octave absolute` は再解決の波及を*2 層とも*止める**（計器 `scratch/p325/P325OctaveAnchorResumeProbe.cs.txt`＝60 小節 1 パート 1 section の本を **アンカー行 1 行だけ違えて** 対にし、同じ位置の音を 4 通りに編集して `IncrementalCompiler.LastCollectResume` と `LastSpringMemo` を読む。**各ラウンドで `incremental == full` を確かめてから数を読む**）:
  | 編集 | Δ | relative の splice | absolute の splice | relative の memo | absolute の memo |
  |---|---|---|---|---|---|
  | **`e'`→`e,`（オクターブ・長さ不変）** | 0 | **0** | **29** | **29/31** | **57/3** |
  | `e'`→`g'`（音名） | 0 | 29 | 29 | 57/3 | 57/3 |
  | `e'`→`ees'`（臨時記号） | +2 | 0 | 0 | 57/3 | 57/3 |
  | `e'`→`e''`（オクターブ・1 文字増） | +1 | 0 | 0 | 29/31 | 57/3 |
  **機構は 1 行**＝`MeasureCollector.Resume.cs:523` の `if (OctaveCheckpoint.Capture(_octave) != ck.Octave) return false;`。**`OctaveCheckpoint` は `CurrentOctave` と `LastPitchName`（相対チェーンの走行状態）を含み、`OctaveContext.Resolve` は*両モードとも*毎音それを書く**が、**absolute はそれを*読まない*（`OctaveBase + octaveOffset`）ので、編集の次の音で走行状態が再収束する**＝suffix splice が通る。**relative は再収束しないので尾を丸ごと歩き直す。**
  ⚠️ **「relative は必ず波及する」は言い過ぎ**——**音名編集の行がそれを否定する**（`e'`→`g'` は次の `f'` の解決オクターブを変えないので relative でも 29 splice）。**波及するのは*解決オクターブが実際に動いたとき*だけ。**
  ⚠️⚠️ ★★★ **ただし直交する 2 つ目の減点が在り、そちらは未解明＝新規の問い**: **本文の長さが変わる編集は*両モードとも* suffix splice を失う**（`e'`→`ees'` は **Δ=+2・memo は 3 小節しか動いていない**のに splice 0）。**`Resume.cs:256-268` のコメントは「Δ≠0 では*またいだ*小節だけが降り、その先の候補は splice を続ける」と書いているが、実測はその先も 0**。⇒ **候補がそもそも立っていないのか、別の門で降りているのかは*測っていない*。** ★★ **これはオクターブの話ではないので ⑴ とは別項**——そして**打鍵は普通*長さを変える***ので、実用上はこちらの方が効く。**着手するなら `CollectResumePlanner` の `SuffixCandidates` の組み立てを毒で数えるところから。**
  ⚠️ **以下は起票当時の提案文で、前提が誤っている。資料として残す。**
  ~~現行は相対のみで、~~
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
| ★★★ **曲頭の `\|:` は刷る**（2026-09-04・第328・**ユーザー決定**・✅ 同便実装＝`ScoreAssembler` の門を撤去・双子は `\set Score.printInitialRepeatBar = ##t` を常に書く） | 第319（T5）は LP の既定（`bar-engraver.cc:432-449`「曲頭では反復線を刷らない」）を移植していた。ユーザー「ユーザーが明示しているなら刷った方が良い」＝**Lily# の `\|:` は常に書き手が書いた印**（抑止すべき*自動*の反復線が無い）で、実コーパスはリードシート。LP 自身が `printInitialRepeatBar` でその慣習を持つので、双子に同じ語を書けば対は保たれる。台帳 `line-start.time-to-first-note.initial-repeat` は LP 5.84／Lily# 5.54＝OPEN −0.30 で開き、**同日の第 5 便で exact に閉じた**（行頭の `\|:` を break-align 表の `staff-bar` 列＝拍子の後に置き、最初の音のばねを小節線の `first-note` 1.3 で建てる。§2 T5 の ✅）。<!-- ledger: line-start.time-to-first-note.initial-repeat = 0 --> |
| ★★★ **`chords {}` に `s` は無い・小節頭の `.` は「和音の無い slot」**（2026-09-04・第328 第 7 便・**ユーザー決定**・✅ 同便実装） | ユーザーの問い「`\| \|` で空の小節が書けるのに `s` は冗長では」→ `s` が唯一担っていたのは「小節の 1 拍目に和音が無く途中から入る」`\| s C \|` で、それは小節頭の `.` を LYS2010（error）で断っていたから。**`r`＝N.C. は LP の `noChordSymbol` と同じ実需で残す**。⇒ `.` は前の和音を延ばす／小節頭では何も刷らない slot（時間は経過）＝`\| . C \|`。**LYS2010 は退役（番号は再利用しない）**・`s` は LYS1028 が置き換えの綴りを名指す。実装: `Parser.Sections.ParseChordBodyItem`（RestS を受けない）・`ChordNameCollector.ForEachSlotGroup`（HeadDot 記録を撤去）・`ChordRowGridValidator`・番人 `ChordSlotGridTests.ADotAtTheBarHead_IsTheSilentSlot`／`ChordBlockStrayTokenTests.ARetiredSpacer_*`／`ChordNamesTests.ChordRow_TheSpacerIsGone`。docs は GRAMMAR／GRAMMAR_FOR_LLM／GRAMMAR_AUDIT。lpreg `chord-names-rests.lys` は `s` の小節を `.` で写す。 |
| ★★★ **grace の手書きスラー `grace { g16( } a8)` は刷る＝appoggiatura と同じ弓**（2026-09-04・第328・**ユーザー決定**「LP では刷るんだよね。lily# でも刷るように直して」・✅ 同便実装） | LP では appoggiatura＝grace＋2 つの slur event（`ly/grace-init.ly startGraceSlur/stopGraceSlur`）なので手書きの対と同じ絵。**実装は「最後の grace 列の `(` と主音符の `)` の対だけを group の弓にする」**（`GraceNoteItem.ExplicitSlur`・walk の grace-time 門が捨てる前に読む・主音符の `)` は group が取る）。**それ以外の形（先頭の grace 列の `(`・rest の `(`・主音符より先の `)`）は今も LYS4020**＝通常の Slur engraver に grace を通す島（§2 U8 ⒝2）はそのまま。閉じない `(` は LYS4010「never closed」で絵は無し（LP の "unterminated slur" と同じ）。 |
| ★★★ **section 頭で音価を書かない音は 4 分（LP の引き継ぎは採らない）**（2026-09-04・第328・**ユーザー確認**「四分音符で良い」） | LP は直前の音価を引き継ぐが、Lily# の section は form でどの順にも参照される再利用単位なので「前の section」が一意でない。**LP 忠実以外に全音符（や引き継ぎ）にすべき理由は無い**。書き手が意図するなら書く（T6 ⒤ で `.ly` が 8 分の 3 箇所は明示した）。`Sugar` C の `fis ais dis,, cis'`（LP は `r1×4` を引き継いで全音符 4 小節・`.lys` は 4 分 4 つ）は第 8 便でユーザーが「全音符が正しい」と直した（§2 T6 ⒜）。 |
| ★★★ **和音行の記号は Score 級の mover（volta・mark・tempo）の支持に入る／Staff 級（text spanner・dynamic 等）の支持には入らない**（2026-09-04・第328・LP 実測＝`probes/volta-chord-row.ly`） | LP の `Mark_engraver`/`Volta_engraver`/`Metronome_mark_engraver` は `\Score`（`engraver-init.ly:764-768`）で、System の pass は ChordNames 行の skyline を支持に持つ（VCR: ブラケットは「Am」の上に 0.46、箱はブラケットの上に 0.46）。Staff 級の grob は Staff の axis group で先に置かれ、行はその*上*に来る（`test/chordrow-rit-second-system`＝`rit.` の上に行）。⇒ **種まき（tracker への merge）ではなく `PlaceVoltas`/`PlaceMusicMarks` だけへの「追加の支持」**（`OutsideStaffSkylines.Place(extraSupport:)`）。staffless（行が anchor）では入れない＝リードシートの「箱は行の線上」決定を守る。 |
| ★★★ **音符の後置印の語順は `核 \N @… ] ) ( [ ~`。エディタの smart key（`~ ( ) [ ] \ @`）が書く順であって、パーサは順不同のまま**（2026-09-03・第327・**ユーザー決定「良いね。これで進めて」**・✅ 同便実装 `f9a7f7c1`） | ユーザーの要件は「語順が前後しても正しく解釈するのはそのまま。なるべく同じ書式で入力できた方があとで検索しやすい」。**根拠 4 つ**: 音符から近い順＝印が及ぶ範囲の狭い順（`\N`・`@` はその音だけ／連桁は小節内／スラーは句／タイは隣の音）／同じ音符で終わる印を始まる印より先に（`d4)( e`＝時間順）／括弧は入れ子（スラー外・連桁内＝`c8([ d e f])`）／タイは結ぶ相手の直前＝最後（`c4)~ c`）。**LP 本家 regression 2237 本の集計**（`scratch/p327/lporder.ps1`）: タイ最後は一致（`(~` 5:1・`[~` 4:1・`)~` 3:0・`]~` 2:0）、`[(` 22:4 だけ本家と逆＝入れ子の読みやすさを優先。**ユーザー明示: 手元の `.lys` の語順は根拠にしない（「適当だ」）**。`@` 注釈同士の順は打った順（補完で入る）。**smart key の細則もユーザー指定**: `\` はカーソルを直後へ（数字を続ける意図）・既に `\N` なら N を選択・`@` はカーソル直後で補完再表示・数字と octave 記号は印の並びの中でも音符の上と読む。実装は `POST_EVENT_RANK` 1 か所（新しい印を足すときはそこに順位を与える）。 |
| ★★★ **実音入力の綴りは `pitch concert` / `pitch written` を `transpose` と同じ 3 つの家に置く**（2026-09-03・第326・**ユーザー決定**・✅ 同便実装＝`3423a277`＋part header の commit） | top-level＝ファイル既定（既定 `written`）／part header＝その part の上書き（own > file。「サックスは移調済みパート譜から、ピアノは実音譜から写す」を 1 冊で書くため）／score header＝その score を実音で刷る（指揮者譜）。**両方に書いたら part が勝つ**＝`transpose`・`octave`・`key`・`time` と同じ規則なので混乱しない、が採択理由。代案（`instrument alto-sax concert` の修飾語・part だけで既定無し）は提示のうえ退けた。**`instrument` を指定しない part では `pitch` は noop**（T=0）。**「楽器名は付けたまま書いた音高で鳴らす」切替は今は実装しない**（楽器名を書かなければよい）。✅ **オクターブ移調楽器（bass/piccolo/`transposition 8vb`/bass tuning）は concert でも記譜不動＝2026-09-03・第327 でユーザー確認済み**（`PartHeaderDefaults.ConcertShiftSemitones`＝`T % 12 == 0 → 0`、それ以外は T 全部＝tenor sax −14 は 14 半音動く）。**理由**: 機構だけ見れば例外だが、紙の慣習では規則そのもの——C スコアは clarinet/sax を実音で書き、piccolo・contrabass・guitar・bass はオクターブ記譜のまま置く（オクターブは読みやすさの約束で、指揮者が戻す「調」ではない）。`pitch concert` の用途は「実音の楽譜から写す」ことなので、写す元に bass がオクターブ記譜で載っている以上、そのまま写せる今の仕様が元と 1 対 1。逆にすると bass を写すたびに 1 オクターブ下げて打ち直し、加線だらけの入力になる。**帰結**: bass 主体のコーパスでは `pitch concert` は事実上 no-op で、効くのは sax/clarinet を持つ本だけ。 |
| ★★★ **section 名の箱は LP の RehearsalMark と同じ位置に立てる（拍子記号の列＝clef 右端 3.365／key があれば key 右端 6.385）。テンポはその下に縦積み。行頭配置は退役**（2026-09-02・第322・**ユーザー決定「F2 は選択肢 1 で」**・✅ 第324 が実装＝§2 T7 F2） | §2 T7 F2＝第257 から据え置きの「SectionLabel を RehearsalMark と同じ左端に立てる」問題。**LP**: 箱の左端は拍子記号の列、`♩=117` はその真下（`scratch/p321/fx/fx4-mark-tempo-lp-staff.png`）。**Lily#**: 箱は行頭（indent+0.3）、key があるとテンポが箱の右に横滑り（`-lys-staff.png`）。第220 の「マーク＋テンポの縦積みは LP 忠実のまま」と同じ方針で箱も揃える。台帳の意図的 OPEN 点（`Indent+0.3` 対 LP `0.0`・第257 第 4 幕）はこれで閉じる側へ。射程は `\mark` 281 冊・snapshot 多数（行頭の箱が右へ 3〜6 ss）。**横並びは表示オプション `marks beside` として後日**（下の行）。 |
| ★ **表示オプション `marks stacked` / `marks beside`（score レベル・既定 `stacked`＝LP・`beside`＝箱は行頭でテンポはその右）を足す。優先度は低め**（2026-09-02・第322・**ユーザー決定**・▶ 未実装） | ユーザーの問い「セクションマークをテンポと横並びに表示する文法は？」に私が提案し承認。置き場は `paper` / `fonts` と同じ 2 段（ファイル先頭＝既定・`score main { marks beside … }`＝その score）。**`override` にしない**（`SectionLabel.placement` は LP に無い発明で、override は `once`／section の範囲を読む構文＝score 全体の量を置くと黙って無視する形。staff-spacing 族を paper に置いた理由と同じ）／**`paper { }` にも入れない**（寸法の器・「配置アルゴリズムの切り替えは置かない」と宣言済み）／`tab bl as full` と同じ閉じた裸語彙 2 語。**F2 を LP に揃えてから足す**（逆順だと今の行頭配置が「既定の LP 逸脱」のまま残る）。触る文書: GRAMMAR・GRAMMAR_FOR_LLM・SYNTAX_REFERENCE・tmLanguage・補完・`DocKeywordListTests`・CHANGELOG。 |
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

