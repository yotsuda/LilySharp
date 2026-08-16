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
# ⚠️ 成否行はロケール依存。ja-JP の機械では Passed! は 1 度も出ない（RULES §5.5）
dotnet test  LilySharp.Tests\LilySharp.Tests.csproj -v q 2>&1 | Select-String '成功!|失敗!|Passed!|Failed!'
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
⚠️ **終了時に数えると差が出る**（終了時は編集済みファイルが並ぶ）。**両方書くこと。**

---

## 1. 現在地 ← **毎セッション書き換える**

最終更新 第185セッション＝**「報告する」と「保つ」は別の修理で、片方だけ在るまま
一族が 6 つの器と 4 つの診断に散らばっていた便**（**実装 3 便**）。
ユーザーの目的は **0.3.0 を出すこと**で、優先は「**文法を完全にしてから出す**」。
開始時の裏取りは**全項目を走らせた**——**台帳 516 点・ss 非ゼロ 98・総和 3.609965521・
count 点 106/うち非ゼロ 2**・HEAD `d9fa7b51`（実装の最後 `774d1ae5`）・
**未 push 11・未追跡 0・作業ツリー項目 0**・suite **5126 passed / 0 failed / 4 skipped**
＝**引継ぎの数は全部合っていた**（**16 回連続**）。
終了時 **実装の最後は `6baa4ff9`**（HEAD `90470c67` は文書と網のみ）・**未 push は
`git rev-list --count origin/master..master` が言う**・**未追跡 0・作業ツリー項目 0**・
suite **5176 passed / 0 failed / 4 skipped**（+50＝新しい網ちょうど）・
**台帳 516 点で不動**（ss 非ゼロ 98・総和 3.609965521・count 106/2）・
**snapshot 1 枚**（`test/volta-labels`——**インクはバイト同一で、動いたのは data-pos 12 個・全部 +2**）。
**A/B は 2 回**（**worktree ではなく `git stash` の base ビルド対 HEAD ビルド**・
コーパス 81 ＋ fixture 219 ＝ **300 冊**・**PID ごとの作業場**）：
**第1便＝絵 0/300・data-pos 1/300**（volta-labels のみ）、**第2便＝絵 0/300・data-pos 0/300**、
**ユーザー自身の `scratch\ベースタブLy` 313 冊も両便でインク不動**。
★ **ユーザー決定 3 件**（LYS0030 は *error*／射程は *沈黙 4 綴り＋既存報告の幅*／
form の `|` は *不活性に保存*・`||` は *彫る*）。
★★ **環境＝pwsh の MCP コンソールは 1 本（`Sage`）で通せた**——**長い sweep の最中は
Read/Grep ツールだけで待つ**規律が今回も効いた（第183 と同じ）。

> ## ★★★ この便の骨＝**器を塞ぐと、その沈黙に寄りかかっていた網が名乗り出る**
> §2F は「`ParseList`／`ParseMusicBlock` の裸 `Advance()`」を 1 項目として起票していた。
> **実測すると器は 6 つ**（section／form／score／music block／**トップレベル**／part ヘッダ）で、
> **そのうち 4 つは*報告していた*のに幅を落としていた**（LYS0016 2 文字・LYS0021 4 文字・
> LYS0025 14 文字・LYS0009 1 文字）。**沈黙の 4 綴りは「絵が対照とバイト同一・data-pos 込み」で決着**
> ——`"oops"` を 7 文字入れた 4 冊が、**その行を消した本と 1 バイトも違わなかった**。
> ⇒ ★★★ **収穫の大半は「直した」ではなく「直したら誰が困ったか」だった。6 件が名乗り出た**:
> **⑴ `DecimalLiteralTests` は落ちることを*契約として* pin していた**——しかもその注記が
> **「もし誰かが error recovery にトークンを保たせたら、この対は一緒に動く」と予告していた**。
> **⑵ `DurationAdjacencyTests` の陽性対照の名前が `AStrayTokenWithNoRule_IsStillDropped`。**
> **⑶ `ParserTests` は「`\tabStaff` / `\tuning` は*正真正銘の* Lily# の綴り」と書いていた**
> ——**変更前のビルドで既に "Undefined variable or phrase: 'tabStaff'"**、ツリーに 0 冊。
> **1 つのコードの*不在*しか assert していないので緑だった**（受理は一度も見ていない）。
> **⑷ `IncrementalParsingTests` は `$relative c' { … }` に「エラー無し」を主張**——
> **入れ子の `{` が黙って消えていたから通っていた。**
> **⑸ `DocExamplesParseTests` は断片を丸ごと音楽として包んでいた**——`title` 行も外側の `{` も
> **parser が削っていたから「コンパイルする」と言えていた**（割り目は `ReportTopLevelMusic` が
> ファイルに 1 回だけ鳴る位置＝**推測ではなく計器**）。
> **⑹ `AnnotationRoundTripTests` はラチェットとして正しく鳴った**（「もう round trip する。
> 既知の壊れた本から外せ」）。**この 1 本だけが設計どおりの働き方をしていた。**
> ⇒ ★★ **予測は外れた**（「新しく報告する本は 0 冊」→ **実測 3 冊**、全部ユーザーの実ファイル）。
> **外れが収穫**——⒜ `9 to 5` は**閉じ括弧が 2 つ余分** ⒝ `Billie Jean` は **`b8,`**
> （オクターブ記号を音価の後ろに書いた＝`,` が落ちて**1 オクターブ高く鳴っていた**）
> ⒞ `A Thousand Miles` は**存在し得ない section `a2` を form が指していた**
> （**宣言側は元から拒否**していて、**参照側だけが黙っていた**）。
> ⇒ ★★★ **そして実測が 2 度、誤った修正を止めた**（RULES §5.0 に汎化）:
> **⒜ 「`||` を保存しても何も描かない」と書いて設問に出したが、逆だった**——`||` は
> **正しく複縦線を描き**（`blogger.lys` で要素 1 個増＝`x=59.51`/`60.00` の対）、
> **黙って通すつもりだった平の `|` のほうが空小節を挿し込んでいた**（2→3 小節）。
> **⒝ `@` を form の項として production に足しかけたが、`@` はどの綴りも受理されない**
> （`@segno`→LYS1022・`@mark`→未知の注釈・`@mark("X")`→`(` が置けない）。
> **どちらも「書く前に 1 冊測る」で止まった。**

- **⑴ ★★★ 第1便＝section／form／score が置けないものを言うようになり、*保つ*ようになった**
  （`44e751c8`・**LYS0030・ユーザー決定＝error**）。上の骨。**着手条件は先に数え切った**——
  **この 3 つの item 一覧を読む消費者は、宣言ノードを名指す 35 ファイルすべてで種別による
  肯定選択**（あるいは `is not SyntaxTokenNode` の明示除外）で、**3 つの器は元からトークンの子を
  持っている**（keyword・名前・波括弧）ので、迷子トークン 1 つは**既に飛ばしているものと構造的に
  区別が付かない**。**家は `ReportStrayItem` 1 軒**（`SkipStrayChordToken` と同じ形）。
  ★★ **form の縦線は 2 つの別物だった**（ユーザー決定）: **平の `|` は生のトークンで不活性に保存**
  （section は元々隣接するので何も頼んでいない。**節点にすると `A | B` が 3 小節になる**
  ——A の音楽が既に `|` で閉じるので「空小節は `| |` の対」という言語自身の規則が効く）、
  **それ以外の縦線は `BarlineSyntax`**（`||` は複縦線を描く。第174 の `:|` の腕の続き）。
  ⚠️ **`volta-labels` は round-trip の既知一覧から外れた**（「`[1. …]` の前の `|` が*保存されていない*」
  と登録されていた）。**残る 4 冊は別の島**（注釈とスラー/タイの順序）。
- **⑵ ★★★ 第2便＝*報告していた* 4 つも幅を保つようになり、器が 2 つ増えた**（`6baa4ff9`）。
  **music block とトップレベル**に LYS0030、**LYS0016・LYS0021・LYS0025・LYS0009** は
  トークンを返すように。**元から正しかった LYS0023（裸の `.`）と LYS0027（`\N`）を対照として
  同じ網に並べた**＝一族が 1 つで答えも 1 つ（トークンを返す）。
  ★ **backslash は 3 経路を 1 つの形にした**——`\` を報告して**残し**、後ろの語は呼び手のループが
  次の回に読む。**副産物で `\nobreak` が直った**（旧「構造コマンド」枝がキーワードごと捨てていたが、
  `nobreak` は次の dispatch が読める実在の綴り）。**代償は `\new` に 2 本目の診断が付くこと**
  （"Undefined variable or phrase: 'new'"・真ではある）。
  ★ **`IsMusicItemStart` にナビ記号が無かった**——`ParseMusicItem` は昔から扱い `GRAMMAR.md` も
  「section の音楽でも form でも同じ裸のトークン」と書いているのに、**トップレベルの `segno` は
  「音楽」ですらなく黙って落ちていた**。`IsTopLevelMusicStart` の注記が「両者は歩調を合わせよ」と
  書いていた当の穴。
  ⚠️⚠️ ★★★ **`IncrementalReuseMap.HasDiagnosticIn` は零幅の診断を境界で見落とす**
  （`Start < end && End > start` はどちらも偽になる）。**見落とされた item は再利用され、
  その診断が黙って消える**。**ガードと同じだけ古い**が、**迷子トークンが member になって
  境界が動いたので fuzz が踏んだ**（診断 17 対 16・欠けたのは EOF の
  "Expected integer measure-count after '*'"）。**単独では入れられない**（無いと差分試験が落ちる）。
- **⑶ ★★★ 第3便＝予約語の一覧を 3 冊とも実測に合わせ、初めて観測者を付けた**（`90470c67`・
  **コードは 1 行も触っていない**）。**発端は 1 行**——`GRAMMAR.md` の `StructureDecl` が
  **まだ `'structure'`** と書いていた（**production は Example でも fence でもないので
  `DocExamplesParseTests` が読めない**＝この形の 4 例目）。
  ⇒ **全語を `Lexer.GetKeywordKind` に突き合わせた**（判定は 1 つ＝「part を名乗れるか」）:
  **予約語と書いてあって*自由*だったのは `structure` `use` `let` `include` `chordnames` `tabStaff`**、
  **逆にどの一覧にも無い予約語が 16 語**。⚠️ **`f` は入れない**——**裸の a-g は keyword 表の前に
  PITCH として掃かれ、`@name` 経路だけがそこへ届く**（その但し書きは元から正しかった）。
  ★ **form の項も 1 綴りずつ `check` に通して production を書き直した**——`String` 単独は項ではない
  （直前の参照の表示ラベル）・repeat block と縦線と `break`/`nobreak` が抜けていた・
  **`_"text"` は文字列が*密着*でないと通らない**（`_ "text"` は section 参照になる・**実測**）。
  ★ **`DocKeywordListTests` は両方向**（一覧の語が Identifier に落ちない／`*Keyword` 種別すべてに
  綴りが在る）。**16 語を見つけたのは後者**。**抽出が空でも緑にならない**ように
  「40 語以上取れたこと」を先に assert してある（§5.4 の空集合の罠）。
- **⑷ ★ 毒は 11 方向、全部赤**。第1便＝section 10 本・form 8 本・score 5 本・`|` を節点にする 1 本・
  `||` を不活性にする 1 本。第2便＝music block 4 本・トップレベル 4 本・LYS0016 の return 2 本・
  `\` を再び落とす 2 本・ナビ記号を外す 2 本・零幅の腕（差分試験）。第3便＝**文書側に毒**
  （`chordnames` を戻す／`form` を外す）で両方向とも赤。
  ⚠️ **文書の毒は逆向きの編集で戻した**（`git checkout` はその便の未 commit の仕事を捨てる・§5.4）。
- **⑸ ★ §7.5 の監査**＝**Core +274 行・REF 0 / OWN 0・数値定数 0**（**正しい**）。
  **分類は ⒟**——**既存の家（`ReportUnclaimedDot` の「返す」形）を 6 か所へ指し直しただけ**。
  **LP に対応物が無い**: LP の parser は yacc で、置けないトークンは硬い構文エラー。
  `form`/`score` の本体は Lily# 固有で、**LP には増分再パースが無いので再利用マップも無い**。
- **⑹ ★ perf（RULES §7 ⑼）＝計算を足していない**。6 か所の報告が**既に消費したトークンを
  捨てずに返す**だけ・`ParseLilypondBackslashCommand` は**短くなった**（再 dispatch を削除）・
  `IsMusicItemStart` は jump table にラベル 6 個・`HasDiagnosticIn` は**増分経路だけ**で
  診断×item あたり比較 1 つ増（しかも**間違う寸前だった item を余計に再パースする側**にしか倒れない）。
  **新しい走査も確保も memo も無し。打鍵経路の `HasUsings` は不動。**
- **⑺ ★★★ §2F に残っているのは 8 つ**——**⑴ 一覧に構文を与えるか（決定であって作業ではない）
  ⑵ 繰り返し縦線の ⑹（MIDI の片側 `:|`）⑶ `lyrics` の voice 束縛（要決定）
  ⑷ `lysc ly` が transpose を落とす件（exporter の島・それだけで 1 セッションの形）
  ⑸ 「Guards X」と名乗る fixture の残り 13 冊 ⑹ 入れ子の中の phrase 参照が双子で空になる件
  ⑺ 新規＝`_ "text"` の空白（`_"text"` だけが通る。規則の形が決定）
  ⑻ 新規＝LYS1015 が宣言されているだけで誰も出さない**。
  **「`ParseList`／`ParseMusicBlock` の裸 `Advance()`」はこの便で閉じた**（器は 6 つだった）。
  ⇒ ★★★ **「0.3.0 の前でなければ不可能」な項目は 0 のまま。**
  ⚠️ ★★ **round trip は今日から parser の不変条件に近い**——**ディスクの本で破れるのは 4 冊だけ**で、
  **その 4 冊は 1 つの島**（注釈とスラー/タイの順序）。**次に「黙って消える」を疑うなら、
  まず `SyntaxTree.Parse(src).GetRoot().ToFullString() == src` を合成した本に通すこと。**

---

## 以下は第184セッションの経緯

最終更新 第184セッション＝**引継ぎが「実測」の札をつけて残した断定が、
計器を通してしか測られていなかったので、半分が嘘だった便**（**実装 3 便**）。
ユーザーの目的は **0.3.0 を出すこと**で、優先は「**文法を完全にしてから出す**」。
開始時の裏取りは**全項目を走らせた**——**台帳 516 点・ss 非ゼロ 98・総和 3.609965521・
count 点 106/うち非ゼロ 2**・HEAD `c4f7d610`（実装の最後 `8aa76b5a`）・
**未 push 6・未追跡 0・作業ツリー項目 0**・suite **5073 passed / 0 failed / 4 skipped**
＝**引継ぎの数は全部合っていた**（**15 回連続**）。
終了時 **実装の最後は `774d1ae5`**・**未 push は
`git rev-list --count origin/master..master` が言う**・**未追跡 0**・
suite **5126 passed / 0 failed / 4 skipped**（+53＝新しい網ちょうど）・
**台帳 516 点で不動**（ss 非ゼロ 98・総和 3.609965521・count 106/2）・
**コーパス 300 冊を 3 チャンネルで 3 回**（**絵・双子・報告**を同じ run で・
**worktree の base ビルド対 HEAD ビルドの真の A/B**・**陽性対照つき**）——
**第1便 絵 0/報告 0・第2便 絵 0/双子 0/報告 0・第3便 絵 0/報告 0/双子ちょうど 2**
（**その 2 冊は sweep の前に名指してある**）・
**snapshot 0 枚**（**絵を 1 ピクセルも動かしていない便**）。
★ **ユーザー決定 1 件**（`octave absolute` の phrase 参照の trailing 記号は *動かす*）。
⚠️ ★★ **環境＝pwsh の MCP コンソールは 2 本になった**（`Thyme` ＋ `Sage`）。
**第2便まで 1 本で通せた**——**長い sweep の最中は Read/Grep ツールだけで待った**。
**第3便で `TaskStop` した sweep の子プロセスが生き残っていて**、Thyme が Busy のまま
2 本目が開いた（**`Start-Process` のリダイレクト出力は終了までフラッシュされないので
「出力が空＝死んだ」は嘘**）。**次の人は畳んでよい。**

> ## ★★★ この便の骨＝**「実測した」の中身が計器なら、それは*計器*についての実測**
> 第183 は §2F と `GRAMMAR.md` の両方に、**「measured」の札つきで**こう書いた——
> 「`octave absolute` では和音 `<c e g>'` も phrase 参照 `theme'` も記号が落ちて
> C4 E4 G4 のまま・診断ゼロ」。**その測定は `lysc check --pitches` だった。**
> ⇒ ★★★ **絵はずっと正しかった。** `octave absolute` の `<c e g>'` は
> **符頭 y 13.85/12.85/11.85**（五線 12.35…16.35）＝**C5 E5 G5**、`,` は **C3 E3 G3**、
> **しかも相対の双子と SVG バイト同一**。**3 綴りが 3 つの別の絵を描いているのに、
> 報告だけが 3 つとも C4 E4 G4 と言っていた。**
> ⇒ ★★ **その場で気づける形があった**——**報告が*書けない答え*を返していた**:
> 分散和音 `<< c e g >>'` の報告は **C4 E5 G5**＝**どの綴りでも作れない和音**
> （root だけが壊れた経路を通っていた）。**「変だが有り得なくはない」より
> 「有り得ない」のほうが安い証拠。**
> ⇒ ★★★ **欠陥の形**: **`ResolveAbsolutePitch` は「解決しながら」trace を書く**のに、
> **absolute だけ*その戻り値に後から* `ShiftOctave` していた**。**絵は動き、記録は動かない。**
> 相対は同じ shift を**解決の前に anchor へ足す**ので最初から正しかった。
> ⇒ ★★★ **原則**（RULES §5.3 に汎化）: **報告を通してしか測っていない「engine に
> ついての主張」は、engine ではなく*報告*についての主張。絵を読むのはハッシュ 1 つ。**
> **この計器が嘘をついたのは 3 便連続で 3 度目**（第182＝多 part・第183＝`using` の幅・
> この便＝群の octave 記号）。**RULES §5.3 判定法⑶ が全セッションに「合成した本は
> 着手前にこれを 1 回通せ」と命じている計器**が、これで 3 回とも直った。
> ⇒ ★★ **起票の残り半分は本物だった**（phrase 参照は絵も動かない）＝**掃きの値打ちは
> 当否とは独立**。**半分嘘の起票を「全部嘘」とも「全部本当」とも読まずに、綴りごとに開く。**

- **⑴ ★★★ 第1便＝`check --pitches` が absolute の群 octave 記号を、描かれている所と違う
  音名で報告していたのを直した**（`d3cbeced`）。上の骨。**家は 1 軒・site は 3 つ**
  （`ItemFactory` の和音 root と和音メンバー・`MusicWalk` の分散和音 root）で、
  **`ShiftOctave` を消し、`CalculateStaffPosition(pitch, groupOctaves)` として
  解決の*前*に畳んだ**。★ **畳みが厳密な理由は構造**（統計ではない）: **`DiatonicShift` も
  `PitchTransposer` も octave 同変**（前者は index に 7N を足すだけで step も alter も動かず、
  後者は target に 12N を足すだけで綴りが動かない）。
  ⇒ **A/B 300 冊＝絵 0/300・報告 0/300**（**`octave absolute` を書く本は 103 冊あるが、
  群記号を書く本は 0 冊**）。**陽性対照は同じ run の中**——**新 binary は plain と `'` を
  絵でも報告でも区別し、旧 binary は絵で区別して報告で区別しない**＝**直した欠陥そのものを、
  0 を出したその計器が実演した。**
  ★ **毒は site ごと**: 和音 root 5 本赤・和音メンバー 5 本・分散和音 root 4 本。
  **新しい網 12 本は先に書いて 10 本が赤**で、**緑だった 2 本は対照**（無印の和音と相対モード
  ＝どちらも壊れていなかった側）。
  ⚠️ **`GRAMMAR.md` の Chord 段落は「measured」と書いた嘘を抱えていたので、
  *何をどう測ったか*ごと書き直した**（絵の y 座標と、なぜ前の測定が計器の話だったか）。
- **⑵ ★★★ 第2便＝phrase 参照の trailing 記号が absolute でも効くようになった**
  （`e4218079`・**ユーザー決定＝動かす**）。**こちらは絵も動かないので本物**。
  ★ **規則は 1 つ、歩き手は 4 つ**——**「参照の記号は body を読む*枠*を動かす。相対では
  走っている枠、absolute では *anchor*」**: collector の `OctaveBase`（`EnterPhraseTranspose`
  が push/pop）・MIDI の `_partAbsoluteBase`・MusicXML の `_octaveAnchor`・双子の**入れ子 `\fixed`**。
  **4 つとも要る**——1 つでも残すと出力どうしが食い違う。
  ⚠️⚠️ ★★★ **黙っていたのは 3 つで、双子だけは言っていた**（"the body is exported UNSHIFTED"）。
  **その警告は*挙動*については正しく、*規則*については間違っていた**——
  **`GRAMMAR.md` の PhraseRef も `GRAMMAR_FOR_LLM.md` も「記号は効く」と教えており、
  反対していたのは実装とコメント 1 つ**（`EnterDefaultFrame` が不在を*設計*と宣言していた）。
  **ツリー 0 冊がこの綴りを書くので、両者が突き合わされたことが一度も無かった。**
  ★ **LP に訊いた**（双子の綴りから推論しない・RULES §5.0）: **`\fixed c''` と `\relative c''` は
  LP 2.26.0 で SVG バイト同一**、plain/`'`/`,` の 3 つは互いに別。
  ★ **入れ子が効く理由**＝**`\fixed` は入れ子にすると*シフト*になる**ので、二重参照が
  collector の stack と同じに合成される。
  ⇒ **A/B 300 冊＝絵 0/300・双子 0/300・報告 0/300。毒は歩き手ごとに 6/5/5/3 本赤。**
  ⚠️ **`'(N)` は元から absolute でも効いていた**（`theme'(3)` → E4 F4 G4 A4・両モード一致）
  ＝**落ちていたのは接尾辞ではなくオクターブの移動だけ**、という第183 の観察はこちらは正しかった。
- **⑶ ⚠️⚠️ ★★★ 自分のハーネスが 2 回続けて嘘をついた**（RULES §5.4 に汎化）。
  第2便の A/B は最初 **「絵が動いた本 27/300」**、やり直して **「35/300」**——
  **重なり 0**。⇒ ★★★ **同じ 2 つの binary を測った 2 回が一致しないなら、どちらも測定ではない。**
  **27 と 35 のどちらが本物かを考える必要は無く、重なり 0 で決着。**
  **原因は共有の作業場**——`Start-Process` で投げた 1 回目を「出力が空だから死んだ」と読んで
  console でもう 1 回走らせたが、**`Start-Process` のリダイレクト出力は終了までフラッシュされない**
  ので生きていて、**両方が `$env:TEMP\p184ab2\a.svg` / `b.svg` に書いていた**＝互いの
  ファイルをハッシュし合っていた。⇒ **告発された本を 1 冊だけ単独で描き直したら全部バイト同一**、
  **単独で走らせ直した 3 回目が 0/300**。**予防は 1 行**（`"...-$PID"`）。
  ⚠️ **「ジョブが死んだ」を出力の空さで判定しない。プロセスを見る。**
- **⑷ ★★★ 第3便＝双子が `part X { octave N }` を読まないので、2 冊が別の音楽として
  LP と突き合わされていた**（`774d1ae5`）。`test/octave-base.lys` は `octave 3` を宣言し
  **本文自身が「bare c は C3」と書いている**のに、**ページ・報告・MusicXML は C3、双子だけ C4**。
  `test/figbass-below-script.lys` も同じ（ページ A3 G3 C3・双子は 1 オクターブ上）。
  ⚠️⚠️ ★★★ **ゲート一覧のどれにも当たらない**（parse 不能／voice{}／grandStaff／ossia／
  part 宣言なし／bar check／instrument／タブ）＝**除外されずに比較されていた**
  ＝§2F の `lysc ly` transpose 落ちと同じ形。
  ⇒ ★★★ **私はこれを一度「既知」として棄却しかけた**——**`AbsoluteBaseOctave` の remark が
  "an existing gate, not a new one" と書いていた**ので、第2便では**その定数から引く**だけにした。
  **ゲートではなかった。** **RULES §5.0「検査器が告発したら告発された側を開く」は、
  *告発先が自分自身について間違っている*場合も込み**——**5 例目。**
  ★ **家は 1 つ**: 定数を **part ごとの値**にし（collector と同じ `InstrumentDefaults.AbsoluteBaseOctave`）、
  **4 つの読み手を一緒に動かした**（wrapper・印つき参照の入れ子 `\fixed`・度数和音の綴りの両半分）。
  **双子は音高を*字面で*書く**ので、**wrapper だけが音を決める**＝ばらすと「正しく見えて違う」になる。
  ★ **LP に訊いた**: 両方の本で**新しい双子 ≡ LP 既定の absolute（bare c = C3）の対照とバイト同一**、
  **旧双子は別の絵**（`18F0B777F972A9` 対 `AF6643653AE4E4` ／ `0769511AC8971F` 対 `F8176D42EC39BC`）。
  ⇒ **sweep の前に「双子が動くのはこの 2 冊」と名指して、そのとおりに出た**（絵 0・報告 0・双子 2）。
  ⚠️⚠️ ★★★ **この便で唯一「観測者が無い」と書いた行がある**——`CarryFrameInto` が絶対アンカーを
  入れ子へ渡す 1 行は、**毒を入れても suite が緑**。**網の穴ではなく代数**:
  度数和音は **anchor = base + rootOffset**、**written = octave − base** で**打ち消す**。
  打ち消さない唯一の使い道（印つき phrase 参照の入れ子 `\fixed`）は**到達不能**——
  **入れ子の exporter は `_phrases` を持たないので参照そのものを空にする**（下の ⑸）。
  ⇒ **コメントに「correct-by-construction・観測者なし」と明記して残した**（§7.6 ⒞）。
- **⑸ ⚠️ ★★ 第3便が*直さずに起票*した 1 件＝入れ子の中の phrase 参照が双子で空になる**
  （§2F・**実測つき**）。**`tuplet 3/2 { theme }` は `\tuplet 3/2 { }`** になるのに、
  **同じ本の同じ `theme` を裸で書いた側は正しく展開する**。**家は `CarryFrameInto` が
  `_phrases` を渡していないこと**（tuplet / grace / cue / repeat の 4 経路が同じ）。
  ⚠️ **警告は出るが理由が嘘**——「referenced but not declared」だが**宣言はされている**。
  ⇒ **ツリー 0/300 冊**（`lysc ly` を全数に通して当該警告 0 件）＝**falsifier が無い**ので
  §5.4 の順序どおり起票。**直すなら嘘の警告文を先に直すこと。**
- **⑹ ★ §7.5 の監査**＝**第1便 Core +19 行・第2便 +60 行・第3便 +59 行**、
  **REF 0 / OWN 1**（OWN は第3便の観測者なしの 1 行・§7.6 ⒞ の 3 点つき）・
  **数値定数は全部「既定値」**（`groupOctaves = 0`・`octaveOffset = 0`・`_absoluteBaseOctave = 4`
  ＝どれも「動かさない／part が何も言わないとき」）。
  **第1便と第2便は分類 ⒟**——**既存の家を指し直した／飛ばしていたモードへ広げた**。
  **第3便も ⒟**——**定数を part ごとの値にしただけで、その値は collector が既に持っている家
  （`InstrumentDefaults.AbsoluteBaseOctave`）から引いた。**
  **LP に対応物が無い**: phrase 参照は Lily# 独自で、群記号の全体伝播も
  「A deliberate Lily# divergence from LilyPond's per-member relative chain」と正典が書いている。
  `\fixed` は**双子が既に使っている綴り**（`EmitPartVariable` が同じ値から引く）。
- **⑺ ★ perf（RULES §7 ⑼）＝計算を*減らした*便**。第1便は **`ShiftOctave` のレコード複製
  （メンバーごと）を消して、既に評価している式の中の整数加算 1 つにした**——
  **collector は `PublishDiagnostics` 経由で打鍵ごとに走る**ので、これは打鍵経路が安くなった側。
  第2便は **phrase 参照 1 つあたり整数加算 2 つと push/pop 1 組**で、**その経路は既に
  3 つの値を save/restore している**。第3便は **part あたり読み 1 回・入れ子あたり代入 1 回**で、
  **そもそも export 経路＝打鍵経路ではない**。**新しい走査も確保も memo も無し。**
- **⑻ ★★★ §2F に残っているのは 7 つ**——**⑴ 一覧に構文を与えるか（決定であって作業ではない。
  下位項目＝phrase 参照を `$name` と教えるか裸で教えるかの文書どうしの食い違い）
  ⑵ 繰り返し縦線の ⑹ ⑶ `lyrics` の voice 束縛（要決定）⑷ `lysc ly` が transpose を落とす件
  （exporter の島・それだけで 1 セッションの形）⑸ `ParseList`／`ParseMusicBlock` の裸 `Advance()`
  ⑹ 「Guards X」と名乗る fixture の残り 13 冊 ⑺ 新規＝入れ子の中の phrase 参照が双子で空になる件**。
  **「`octave absolute` の trailing octave 記号」はこの便で閉じた**（半分は欠陥ですらなかった）、
  **「双子が `part { octave N }` を読まない」も閉じた**（起票すらされていなかった＝
  コードの remark が「ゲート」と自称していたので誰も数えていなかった）。
  ⇒ ★★★ **「0.3.0 の前でなければ不可能」な項目は 0 のまま。**
  ⚠️ ★★ **測定可能面積は 2 冊増えた**——**`octave-base` と `figbass-below-script` は
  今日から LP と同じ音楽で比べられる**（それまでは 1 オクターブ違う本を比べていた）。

---

## 以下は第183セッションの経緯

最終更新 第183セッション＝**「自分は X を測る／X を教える」と名乗っている計器・fixture・教材が、
X をしていなかったのを 4 件見つけた便**（**実装 5 便**）。
★ **4 件目は、自分がこの便の 1 時間前に書いた断定を検算して出た**（⑸）。
ユーザーの目的は **0.3.0 を出すこと**で、優先は「**文法を完全にしてから出す**」。
開始時の裏取りは**全項目を走らせた**——**台帳 516 点・ss 非ゼロ 98・総和 3.609965521・
count 点 106/うち非ゼロ 2**・HEAD `9d1ad561`（実装の最後 `d921add1`）・
**未 push 39・未追跡 0**・suite **5062 passed / 0 failed / 4 skipped**
＝**引継ぎの数は全部合っていた**（**14 回連続**）。
終了時 **実装の最後は `8aa76b5a`**・**未 push は
`git rev-list --count origin/master..master` が言う**・**未追跡 0**・
suite **5073 passed / 0 failed / 4 skipped**（+11＝新しい網ちょうど）・
**台帳 516 点で不動**（ss 非ゼロ 98・総和 3.609965521・count 106/2）・
**コーパス 0/81**（第1便で取得。**残る 4 便は構造で言える**——**第3〜第5便は
`git diff -- LilySharp.Core` が空**〈第5便は加えて、触った本の data-pos を伏せた SVG が
前後で同一 `2A59D6B01CC3`〉、**第2便が触った `ResolvedPitches.ForFile` の生産側の
呼び手は `Program.cs` の `--pitches` 1 つだけ**〈実測・描画経路は 1 か所も読まない〉。
**ハッシュではなく構成**）・
**snapshot 2 枚**（`test/grandstaff-high-bass`・`test/accidental-octave-straddle`
——**どちらも LP 照合 → ユーザー承認 → 再ベース**）。
★ **ユーザー決定 4 件**（LYS0029 は *error*／範囲は *`using` だけ*／
`grandstaff-high-bass` は *C5-E5-G5 へ*／`accidental-octave-straddle` は *`<eis eis' cis cis'>` へ*）。
★★ **環境＝pwsh の MCP コンソールは 1 本のまま**（`Thyme`）。**第180・181・182 は 3 便続けて
2 本になっていた**——**長い sweep の最中に読み取りを打たない**だけで止まる。次の人も 1 本で通せる。

> ## ★★★ この便の骨＝**「報告している綴りが 1 つある」は「その綴りは正しい」ではない**
> §2F ⑹ は「`using` を section／score の中に書くと**診断ゼロで黙って消える**」と書き、
> 「**part ヘッダの位置は報告するので、黙るのは section と score の本体だけ**」と付けていた。
> **診断についてはそのとおり。だが*位置*については 5 綴り全部が壊れていた。**
> ⇒ ★★★ **沈黙は小さいほうの半分だった**——**トークンを裸の `Advance()` で消費すると
> 木からその*幅*が消え、red の位置は green 幅の累積なので、以降の全位置が左へ滑る。**
> **実測**（`section A { using "n.lys"  m { c4 d e f | } }`）: **木が 16 文字短く綴り返し**・
> **SVG の `data-pos` が全部 16 手前**（`52,55,57,59,61` ＝**その行を消した本の offset そのもの**・
> 真の音符は `68,71,73,75,77`）・**`check --pitches` が using 行の文字を音楽として報告**
> （`g`→C4・`n`→D4・`lys`→E4・`s`→F4）。**RULES §5.3 判定法⑶ が全セッションに
> 「合成した本は着手前にこれを 1 回通せ」と命じている計器**が、それを言っていた。
> ⇒ ★★ **報告していた part ヘッダも幅は落としていた**（LYS0025 を出しながら 14 文字）。
> **報告することと保つことは別の修理で、片方だけ在った。**
> ⇒ ★★★ **同じ形が fixture 側で 2 件**（⑶⑷）——**どちらも「自分が守ると名乗る機構を
> 毒しても絵が 1 ピクセルも動かない」**。**snapshot は再ベースできるので、この状態は
> 網が 1 本も赤くならないまま何年でも続く。**

- **⑴ ★★★ 第1便＝top-level でない `using` が報告され、以降の位置を滑らせなくなった**
  （`9fb978e2`・**LYS0029・ユーザー決定＝error**）。上の骨。**綴りは 5 つ**——
  **section 本体 16・score 本体 14・form 本体 14・part ヘッダ 14・音楽の中 14**（失う幅）で、
  **top-level だけが round-trip する**。**家は 1 軒**（`Parser.ParseMisplacedUsing`）＝
  報告してから**その正体である directive として parse する**ので幅が残る。
  ★ **error にした理由は測ってある**: **LYS0028 が warning なのは沈黙が*設計として宣言*され
  網で pin されていたから**（`UsingExpander` の doc ＋ `MissingFile_IsSkipped`）。
  **置き場所の誤りにはその契約が無い**——**どんなファイルシステムの状態でも意味を持ち得ない。**
  ⚠️ **`HasUsings` は root の子だけを見る綴りのまま**（打鍵経路・第182 ⑺）。
  **変わったのはそれを守っていた 2 本の網の主張のほう**——「directive は root の子より深くならない」は
  もう偽なので、**`EveryUsingHasUsingsSkips_IsReported`**（root-children の問いが取りこぼすものは
  全部 error になっている）へ書き直した。
- **⑵ ★ 第2便＝前便が残した警告と、同じファイルで stale になった理由**（`f4d5bd03`）。
  **`ResolvedPitches.cs` の CS8625**（`specs.Count > 0 ? specs : [null]` は条件式の自然型を
  非空の腕から取るので `[null]` を `RenderSpec` に対して変換する）＝**§6 が期待する 0 warn へ戻した**。
  ★ **同じファイルの注記が、自分のセッション内で閉じた欠陥を「未解決」と書いていた**
  ——「score 単位 transpose は疑いの下にあるので、正しい文言をまだ書けない」は
  **`de4acbd0` が 4 便あとに終わらせている**。**実測してから書き直した**: 1 つの form に
  2 つの score・片方に `transpose d` で**別々の絵**（135,854 対 135,903 バイト）を描くのに、
  **報告は C4 の 1 つだけで警告ゼロ**＝**状況は今や*書ける*。開いているのは文言のほう。**
- **⑶ ★★★ 第3便＝`test/grandstaff-high-bass` が、自分の名乗る seed を初めて読んだ**
  （`28b24b33`・**ユーザー承認済み**）。§2F は「本文の C4-E4-G4 に対し実際は C3-E3-G3」と
  起票していた。⇒ ★★★ **主張は二重に外れていた**——**本文が名乗る C4-E4-G4 でも
  譜間は床（5.000）に座ったまま**で何も測らない。**実測**（上の譜の最下線→下の譜の最上線）:
  **C3-E3-G3 5.000／C4-E4-G4 5.000／C5-E5-G5 8.090**、**LP 2.26.0 は 5.000／5.000／8.095**
  （0.005 は SVG の 2 桁丸め）。**両 engine が床でも開いた値でも一致＝Lily# 側に欠陥は無い。**
  ★★ **観測者を毒で数えた**: `SeedStaffSymbol` を止めると**論理網 4・snapshot 13・台帳点 約40**が赤——
  **この本はその*どれにも居ない***。`<c'' e'' g''>` にして初めて動く。
  ★ **論理の網を新設**（`TheHighBassFixture_LiftsTheStavesOffTheirFloor`）＝**この本が初めて持つ
  snapshot 以外の観測者**。**対で書いた**（1 オクターブ下と 2 オクターブ下は*同じ*床でなければならない
  ＝regime の主張。無いと「譜間が広い」はどんな本でも通る）。
- **⑷ ★★★ 第4便＝同型の 2 例目、しかも嘘の出所が論理網のコメントだった**
  （`23d7b600`・**ユーザー承認済み**）。**第182 が起票した「同型を疑う」の全数掃き**——
  **fixture 219 冊のうち音名を名乗るのは 21 冊、候補 9・偽陽性 7**（section ラベル `G1`/`A2`・
  ベースの調弦 `E1-A1-D2-G2`・`c,`＝C3 という文法の説明）**・本物 2 冊**（⑶ とこれ）。
  ⚠️⚠️ ★★★ **その「偽陽性 7」は*推論*だった**——**全文を読んだのは 2 冊だけ**で、残りは
  「ラベルに見える」で棄却したまま**この §1 に断定で書いた**。**⑸ で 7 冊とも開いた**：
  **音名の主張としては 7 冊とも確かに偽陽性**だったが、**うち 1 冊（`grammar-tour`）は
  *別の* 嘘を 4 つ持っていた**。**数は合っていて、根拠が無かった。**
  `accidental-octave-straddle` は **floored modulo `((p%7)+7)%7`** のための本なのに
  **position 3,10,1,8＝全部非負**を書いていた（truncating と一致する領域）＝
  **`%` を裸に戻しても SVG がバイト同一**。⇒ **3/-4 と 1/-6 に直した。**
  ⇒ ★★★ **その綴りは論理網 `AccidentalOctaveAlignmentTests` のコメントから来ていた**——
  **網は正しい position（-4,3,-6,1）を使いながら、「それは `<eis' eis'' cis' cis''>` である」と
  書いていた**（実際は 3,10,1,8。`eis'` は E♯5 で E♯4 ではない）。**両方を同じ commit で直した。**
  ⚠️ **LP はこの欠陥を*見られない***: LP は音高から働くので**旧綴りでも新綴りでも同じ 2 列**
  （x 7.885 / 9.169・Lily# は 7.89 / 9.17）。**LP 一致はこの本の値打ちではない。零をまたぐことが値打ち。**
- **⑸ ★★★ 第5便＝自分が 1 時間前に書いた「偽陽性 7」が*推論*だったので検算したら、
  文法ツアーが 6 つの偽を教えていた**（`grammar-tour` ＋ `GRAMMAR.md`）。
  ⇒ ★★★ **着手の動機がそのまま骨**——⑷ の掃きで告発された 9 冊のうち**私が全文を読んだのは 2 冊**で、
  残る 7 冊は「section ラベルに見える」と*見た目で*棄却し、**その結果を恒久の記録に断定で書いた**。
  **RULES §5.0 は「検査器が告発したら告発された側を開く。3 回中 3 回、悪かったのは検査器だった」**
  と書いている。**開いたら、そのうち 1 冊が本物だった。**
  ★ **`showcase/grammar-tour`（言語の「文法ツアー」＝教材）の 6 主張が偽**（全部 `check` で実測・
  **どれもこの本自身のコードが数行下で反証している**）: **⑴ `G1: ヘッダ属性はすべてコロン形式`**
  →`clef: treble` は error「the ':' form has been removed」・本文の part は裸
  **⑵⑶ `structure` / `render`**→どちらも error（今は `form` / `score`。本自身がそう書いている）
  **⑷ 無名 `lyrics { … }` の例**→error「an unnamed 'lyrics { ... }' block has no way to be attached」
  **⑸ 指していたパス `samples/test/lyrics.lys`**→存在しない（実物は `LilySharp.Tests/Fixtures/test/lyrics.lys`）
  **⑹ 「グランドスタッフでの歌詞は現状未対応」**→**対応している**（実測：`grandStaff { staff rh with
  lyrics w  staff lh }` で 4 音節すべて描かれ、`with lyrics` を外すと 0）。
  ⚠️ **旧綴りの掃きは全数**：`structure`/`render`/コロン形式を書くコメントは**ツリー 563 冊でこの 1 冊だけ**
  （他 3 件は英語の "structure" ＝偽陽性）。**第182 ⑷ が `grammar-2026-06-09` で直した一族の再発で、
  そのとき隣の本は見られていなかった。**
  ★ **出力不変を測った**: **data-pos を伏せた SVG が前後で同一**（`2A59D6B01CC3`）・警告 3 件で不変・
  **この本は snapshot リストに居ない**。⚠️ **生の data-pos は動く**——**コメント行を足せば以降の
  offset は当然動く**（この便の骨の*正しい*側の顔）。
- **⑹ ⚠️ ★★★ この便が*直さずに起票*した 2 件**——**⒜ `ParseList` の裸 `Advance()`
  （`Parser.Declarations.cs:146`）と `ParseMusicBlock` の同じ行は、`using` に限らず
  *認識できない項を全部*落とす**（ユーザー決定＝この便は `using` だけ）。**実測**：
  section 本体の stray identifier は**4 文字失い、`m { … }` を `m …` に潰す**——**診断ゼロ**。
  ⚠️ **一族の網 `AnnotationRoundTripTests.EveryBook_SpellsItselfBackOutOfItsTree` は在る**
  （既知の壊れた本 5 冊つき）が、**ディスクの本しか見られない**——**`using` を書く本は 0 冊**
  （実測：563 冊中 4 冊が `using` を含み、**4 冊ともコメント**）。**着手するなら
  section/form/score の item を読む消費者が全部「種別で肯定選択」しているかを先に数え切ること**
  （第181 ⑶ の教訓）＝**呼び手は 3 つだけなので島は小さい。**
  **⒝ `octave absolute` の本では trailing の octave 記号 `'` `,` が診断ゼロで死んでいる**——
  **和音 `<c e g>'` も phrase 参照 `theme'` も、相対では C5 へ動くのに absolute では無印と同じ C4**。
  ⇒ ★★★ **観測者は正典の 1 文だけ**: **`GRAMMAR.md` の Chord が「In 'octave absolute' mode …
  the trailing marks *still shift the whole group*」と明言している**＝**文書が実装を反証している。**
  ⇒ ★★ **同じ位置の `(N)` は absolute でも効く**（`theme'(3)` → E4 F4 G4 A4）
  ＝**接尾辞ではなくオクターブの移動だけが落ちている。**
  ⚠️ **ツリー 563 冊中 0 冊がその綴りを書く**（`octave absolute` は 319 冊）＝**falsifier になる本が無い**
  ので §5.4 の順序どおり**直さず起票**。**規則の形が決定。**
  ⚠️⚠️ ★★★ **↑ この段落の「和音も」は*嘘*だった（第184 が反証・§1 の骨）。** 逐語で残すが、
  **読むときは第184 ⑴ と一緒に読むこと**——**和音と分散和音は absolute でも最初から群ごと動いていて、
  C4 と言っていたのは `check --pitches` のほうだった**（絵は C5 E5 G5 を描いていた）。
  **本物だったのは phrase 参照の側だけ**で、そちらは第184 ⑵ が 4 出力とも塞いだ。
- **⑺ ★ 毒は 5 方向**（§5.4）。**⑴＝報告だけ落とす→5 本赤**（綴り 1 つずつ・幅の網は緑＝独立）／
  **section の腕を外す→3 本赤**。⚠️⚠️ ★★★ **その 3 本目は主張ではなく*陽性対照*が赤にしていた**
  ——**欠陥を戻したうえで `Assert.NotEmpty(missed)` を外すと、`EveryUsingHasUsingsSkips_IsReported`
  は 5 本とも緑になる**（数える集合が空になるので「その全部が報告されている」が真）。
  **§5.4 の空集合の罠の 2 例目**（1 例目は第182 ⑼）＝**1 行が唯一の観測者。**
  **⑶＝和音を下げる→赤／`SeedStaffSymbol` を止める→赤**（後者が本命）。
  **⑷＝fixture を旧綴りに戻す→赤／`%` を裸に戻す→論理網が赤・旧綴りの本は不動**。
- **⑺′ ★★ 便の外で 1 つ掃いた＝「Guards X」と名乗る fixture 21 冊の体系的な検算**（コード変更ゼロ）。
  **今日見つけた 2 冊は音名の掃きで*偶然*出たもの**で、**「この本は X を守る」を体系的に問うたことは
  一度も無かった**。⇒ **21 冊のうち 8 冊を確かめ、8 冊とも正直だった**（`<X>Item.StaffIndex` の
  7 冊は毒 1 つで 7 冊とも動く）。**新しい嘘つきは 0。**
  ⚠️⚠️ ★★★ **`fingering-*` 2 冊は「不動」だが容疑者ではない**——**3 つの site を毒して
  3 つとも 0 冊**＝**毒のほうが死んでいた**。**動く本を 1 冊も見せられない毒は、結論に使えない。**
  **この便で陽性対照が結論を救ったのは 3 度目**（⑺ の空集合・⑸ の「偽陽性」・これ）。
  ⇒ **手順は RULES §5.4 に、残り 13 冊の一覧は §2F に置いた。**
- **⑻ ★ §7.5 の監査**＝**第1便 Core +99 行・REF 0 / OWN 0・数値定数 0**（**正しい**——
  `using` は Lily# 独自で、**LP の `\include` は字句のテキスト取り込みで、ファイルが無ければ止まる**
  ＝対応物が無い。第182 ⑽ と同じ理由）。**第2便は annotation のみ・第3〜5便は Core を 1 行も触っていない。**
- **⑼ ★ perf（RULES §7 ⑼）＝計算を足していない**。第1便は**トークン種別の switch の腕を 5 本**
  （ツリーのどの本も書かない綴り）で、**新しい走査も確保も memo も無く、打鍵経路の `HasUsings` は不動**。
  第2〜5便は annotation と fixture と文書のみ。⚠️ **第182 ⑽ が「便ごとに『計算を足したか』を問え」と
  書いたので、この便は毎回問うた。**
- **⑽ ★★★ §2F に残っているのは 4 つ＋2 新規**——**⑴ 一覧に構文を与えるか（決定であって作業ではない。
  ★ この便で*文書どうしの食い違い*が下位項目として付いた——phrase 参照を `GRAMMAR.md` は裸、
  `SYNTAX_REFERENCE.md`／`GRAMMAR_FOR_LLM.md` は `$name` と教える。両方通るので網は何も言わない）
  ⑵ 繰り返し縦線の ⑹ ⑶ `lyrics` の voice 束縛（要決定）⑷ `lysc ly` が transpose を落とす件
  （exporter の島・それだけで 1 セッションの形）**、＋**⑸ 新規＝`ParseList`／`ParseMusicBlock` の
  裸 `Advance()` ⑹ 新規＝`octave absolute` の trailing octave 記号**（どちらも上の ⑹）。
  **「section／score の中の `using`」「`grandstaff-high-bass`」「`accidental-octave-straddle`」
  「grammar-tour の期限切れの主張」はこの便で閉じた。**
  ⇒ ★★★ **「0.3.0 の前でなければ不可能」な項目は 0 のまま。**

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
  ⚠️ ★ **第182 追記＝*存在しない* face 名も黙る**（実測：`font "NoSuchFontFace"` は
  `No errors found.`）。**名前を取る構文の棚卸しで残った 2 つのうちの 1 つ**
  （もう 1 つは `instrument "NAME"` だが、**あれは表示テキストなので黙って正しい**）。
  ⇒ **この項を直す便は、予約と描画の一致だけでなく「その名前が実在するか」も一緒に決める。**
- ★★ **`lysc ly`（双子 exporter）の穴**。**塞ぐたびに LP と突き合わせられる本が増える**ので、
  忠実度作業の**測定可能面積そのもの**が懸かっている。
  ~~⑴ `voice { }`~~・~~⑵ `grandStaff` の入れ子~~・~~⑶ `ossia`／`part` 宣言なし~~・
  ~~⑷ section のヘッダ~~・~~⑸ 和音のオクターブ記号~~・~~⑹ grace のあとの音価~~ — **すべて完了**
  （第61〜63セッション。最後の 2 つは `275c12ee`）。
  ~~⑻ `@stemUp`/`@stemDown` を落とす~~ — **完了**（2026-08-03・engine 側と同じ commit で。
  `\once \override Stem.direction`。理由は §1 ⑥）。
  **残っているのは 2 つ**:
  ⑺ **度数和音が `<>` になる**（`<1 3 5>`・`<d 3 5 7,>`）。**今は警告を出すだけ**で、
     **解決は独立した移植**——`MeasureCollector.ItemFactory` が root ＋ 調に対して解決している
     側から**字面で写せる**。**閉じれば `chord-octave-marks` の bar check が消える。**
  ⑼ ⚠️ ★★ **新規＝入れ子の中の phrase 参照が空になる**（第184 実測・**未修理**）。
     **`tuplet 3/2 { theme }` は双子で `\tuplet 3/2 { }`**——**同じ本の、同じ `theme` を
     裸で書いた側は正しく展開する**（実測：`c4 d e f | \tuplet 3/2 { } r2 |`）。
     **家は `CarryFrameInto`**＝**`_phrases` と `_activePhrases` を入れ子の exporter へ渡していない**
     （6 つの構築 site が `_octaveAbsolute` と `_anchorOctave` だけを写す）。**tuplet / grace /
     cue / repeat の 4 つが同じ経路。**
     ⚠️ **警告は出るが、*理由が嘘***: **「phrase 'theme' is referenced but not declared」**
     ——**宣言はされている**。見えていないのは入れ子の exporter の側。
     ⇒ ★★ **ツリー 0/300 冊がこの綴りを書く**（実測：`lysc ly` を 300 冊に通して当該警告 0 件）
     ＝**falsifier になる本が無い**ので **§5.4 の順序どおり直さず起票**。
     **着手するなら ⑴ 受理集合を網に ⑵ 嘘の警告文を先に直す ⑶ そのあとで `_phrases` を渡す。**
     ⚠️ **1 行で直りそうに見えるが、直した瞬間に「入れ子の中の参照」が初めて双子に出る**
     ので、**双子 199 本の before/after 全数比較が要る**（この島の他の項と同じ）。
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
  **`rest-avoid-note.ly` の両側置換も撤回できた**（§1）。**残っている穴は 1 つだけ**:
  ⚠️ **MusicXML が高さを落とす**——音高付き休符は `<rest/>` になる（音価は正しい）。
  **LP の持ち方は「音高は*位置*」なので、書き出すなら `display-step` / `display-octave`**
  （`RestItem.StaffPosition` ＋ clef から導ける）。**測る本はまだ 0 冊。**
- MusicXML インポート — ほぼ完遂、**実ファイル検証が残**
- AI 協調編集 M1–5 — **実機 E2E 未検証**
- 文法改善 5 件は完了。**0.3.0 リリースは GO 待ち**
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
- ⚠️ ★ **新規＝「Guards X」と名乗る fixture 21 冊のうち、確かめたのは 8 冊**（第183・**残り 13 冊は未測定**）。
  **手順は RULES §5.4**（X を 1 行毒して 1 回ビルドし、**名乗る本だけ**を描いてハッシュ比較）。
  ★ **済み 8 冊＝全部「ちゃんと観測している」**: **`<X>Item.StaffIndex` routing を名乗る 7 冊**
  （arpeggio / articulations / dynamics / figbass-chordname / grace / trillspan / tuplet の各 `-lower-staff`）は
  **`MeasureCollector.cs:1583` の `_currentStaffIndex` を 0 に固定する毒 1 つで 7 冊とも動いた**
  ＋ **`grandstaff-high-bass`**（この便で観測者にした）。
  ⚠️⚠️ **`fingering-lower-staff` と `fingering-articulation` は*未測定*で、容疑者ではない**——
  **運指は `NoteItem` に乗るので上の毒が届かず、専用の毒を 3 site 試して 3 つとも 0 冊だった**
  （`FingeringEngraver.Calculate` の `staffIndex` は beam tip の索きにしか効かない／
  `LayoutEngine.FingeringStaffScores` はこれらの本では通らない＝`splittable` が偽で
  `wholeIslands` の枝へ行くと見える）。⇒ **続きは「運指がどの譜に置かれるかを決める行」を
  先に特定すること**。**動く本を 1 冊見せるまで、どの結論も出せない**（§5.4）。
  ⚠️ **残る 11 冊は別の機構を名乗る**（clef・調号の取り消し・多声部の符尾・volta ラベル・
  `SkylineBuilder.AddDynamicsToSkyline`・`ArticulationEngraver` の per-note sort ほか）
  ＝**上の毒が届かないので「不動」は何も言っていない。1 機構 1 毒で続ける。**
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
- ⚠️ ★ **新規＝`_ "text"` は空白があると通らない**（第185 実測・**未修理**）。
  **`_"text"` は受理され、`_ "text"` は「Undefined section: '_'」になる**——`_` が section 参照に
  読まれる。`GRAMMAR.md` の production は `'_' , String` と書いていた（EBNF は空白を語らない）ので、
  **実測どおり「GLUED」と注記だけ入れて挙動は変えていない**。
  ⇒ ★ **規則の形が決定**（空白を許すのか、密着を要求すると明言するのか）。
- ⚠️ ★ **新規＝LYS1015（`MultipleFormDeclarations`）は宣言されているだけで誰も出さない**
  （第185 実測・grep 1 件＝宣言そのもの）。`GRAMMAR.md` の表が「top-level `structure` は 1 つまで」と
  この番号の規則を説明していたので**表のほうを実装に合わせた**（実際は LYS1016 未命名・
  LYS1017 名前重複で、**form は複数あってよい**）。**定数は消していない**——
  「未使用に見える ≠ 消してよい」（§5.1）で、横断 grep とユーザー承認が要る。
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
- ⚠️ ★★★ **新規＝`lysc ly` が `transpose` を 3 綴りとも黙って落とす**（第182 実測・**未修理**）。
  **transpose を書く fixture 3 冊すべて**で **双子に `\transpose` が 1 つも無く**、
  **調も*書かれたほう***（`transpose-down.lys` は自分のヘッダに「G major → F major」と
  書いてあるのに双子は `\key g \major`）。**警告も出ない。**
  ⇒ ★★★ **移調する本の LP 突き合わせは、ずっと別の音楽を比べていた**
  ——第179 の「では*この本*は LP の本と同じ音楽か」の exporter 版で、
  **「exporter が黙って落とす」はこれで 7 度目**（第55・56・61・62・63・第181 の和音/歌詞行）。
  ⇒ **着手するなら §2F 上段の exporter 島と同じ手順**（**塞いだら双子 199 本の
  before/after 全数比較**）＝**それだけで 1 セッションの形**。
  ⚠️ **綴りの設計判断が要る**: `\transpose c <target>` で包むか、鳴る音高を直接書くか。
  **包むなら LP が調も動かすので Lily# と一致する**（part・score・top-level の 3 綴りを
  それぞれ part の音楽／score／全 part のどこに落とすかを決めること）。
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
| ★ **タブの*和音*のタイは LP の広げ方に譲る**（ユーザー判断・2026-08-16 明示） | **LP をタブ側で直接測ってから決めた**: `<c' e' g'>2~ <c' e' g'>4` の TabStaff で LP 2.26.0 は **dir = −1, +1, +1**（TabNoteHead・staffpos 1/3/5＝**一番下の弦は数字の下・上 2 本は上**。双子に `Tie.direction` を吐かせた実測）。⇒ 旧「タイは stem と反対側に固定」という Lily# 独自規則を**和音では通さない**。★★ **実装は規則を書き直していない**——`TieFormattingProblem` の中に既に移植済みだった `set_ties_config_standard_directions` を static に出し、**タブが*自分の* staff position** （`TabStaffGeometry.StaffPositionOfString` ＝ LP の `tablature-position-on-lines` ＝ `StringCount+1−2·string`）で呼ぶ。★★★ **単音は数学的に不変**: 旧規則は `string > (N+1)/2`（符尾と反対）、新しい位置の符号は `string < (N+1)/2` で正、列が 1 本なら `sign(position)` と `neutral-direction`＝UP なので**全チューニングの全弦で答えが一致する**（中央弦も両方 UP）。⇒ **`LILYSHARP-OWN` が 1 件 `LILYPOND-REF` になった。** 観測は `TabChordTieTests` |
| ★ **タブのタイは自分の数字の縁から出る**（第180・LP に対応物なし） | Lily# の数字はジグザグで 2 列に分かれるので、**タイは自分の列の数字の縁から縁へ**引く（`軸 + dx ± 数字幅/2`）。⚠️ **LP には問えない**——**LP のタブ数字は 3 つとも同じ x**（実測 8.82 / 12.951）なので、選ぶべき第 2 の x が存在しない。⚠️ **帰結として単音のタイが短くなる**（`test/tab-tie` で 1.29 → 0.89）。LP は**中心から中心へ引いて `whiteout` で抜く**（頭間 2.787 に対しタイ 2.467＝88%）が、**その whiteout は Lily# には移植できない**（§2 の ✅ ⒳＝LP の数字 1.180 は弦間 1.5 に収まるが Lily# の 2.166 は収まらず、隣の弦の線を消す／占有子は色を使うのでダークモードで穴になる）。⇒ **タイが短いのは大きい数字の帰結として受け入れる。「短いから戻す」で数字の縁を捨てないこと** |
| ★ **占有（不透明な箱）ではなく除去（インクを切る）で重なりを解く**（第180 で再確認・元は `digitGaps` の実装時） | **理由は 2 つあり、どちらも実測**: ⑴ **箱は色を使う**——ページを反転してテーマを当てる viewer（VS Code のダークモード）では箱が黒くなり、**数字の周りが黒・背景がグレー**で数字が穴に座る（ユーザー・2026-08-16 明示）。⑵ **箱は数字と同じ高さ**＝Lily# では **2.166 対 弦間 1.5** なので**隣の弦の線を両側 0.333 ずつ消す**——**数字を大きくできなくしていた天井そのもの**。⇒ `digitGaps` は**色を 1 つも使わず自分の線の中だけで完結する**。⚠️ **LP の `TabNoteHead (whiteout . #t)` を「LP がやっているから」で移植しないこと**——**LP の数字は 1.180 高で 1.5 に収まる**（2.26.0 実測）ので LP では安全なだけ。**重なりを消すなら、覆うのではなく切る** |
| ★ **タブの弦は小節の中で継承する。明示 `\N` も継承する**（ユーザー判断・2026-08-16 明示） | **LP と違うことを測ってから決めた**: `c( g'\2) g g4` をベースで書くと Lily# は g を 3 つとも 2 弦 5 フレットに置き、**LP は無印の 2 つを 1 弦開放へ戻す**（2.26.0 実測・双子）。**1 つの音高は 1 小節のあいだ同じ押さえ方に見えるほうが読める**、が決定の理由。⚠️ **この resolver はもともと LP と別の模型**（`Tunings.CalculateFret` ＝左手の位置を追う LILYSHARP-OWN）なので、乖離はその延長。**「LP と違う＝バグ」で消さないこと**（理由は `TabResolver.ResolveTabStrings` の remarks にも書いてある） |
| ★ **タブのフレット数字を LP より大きく描くのは意図的乖離**（ユーザー判断・2026-07-24 明示） | LP のタブ数字は小さくて読みにくい。Lily# は `TabConstants.FretFontSize = 2.6`（単数字幅 1.625・高さ 1.7875）＝LP の TabNoteHead 幅 0.990155 の約 1.64 倍。和音で数字が被る問題は**じぐざぐ配置**（`SpacingRules.ApplyTabChordSpacing` ほか）で解いてある。**「LP と違う＝発明だから消す」で削らないこと。** ⚠️ 弦間隔（`TabStringSpace`）は別の話で、そちらは LP の 1.5 に揃える |

---

