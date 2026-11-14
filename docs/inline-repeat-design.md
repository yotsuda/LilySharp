# 反復とナビ記号を section の音楽にも書く（設計・段 0〜8 は実装済み）

2026-11-14・第1243。オーナーの提案から始まった設計。**第1243〜第1249 で段 0〜8 を実装した**（§4 の段取り・経過は §7。LYS1034 は第1249 で phrase だけになった）。
これは次の 2 つの決定を取り下げる変更で、取り下げそのものがオーナーの判断（同日の会話）。

- 2026-08-31・第304「`|:` `:|` `[N. …]` は music に書けない。書けるのは `form {}` だけ」（LYS1034）
- 2026-10-04・第793「`segno` `coda` `fine` `to coda` `dc` `ds` も form だけ」（同じ LYS1034）

## 1. なぜ変えるか

紙の譜面は、反復もナビ記号も小節の流れの中に書いてある。今の文法でそれを写すには、
書き手が譜面を section に切り直さなければならない。

- 1 小節の volta のために section を 1 つ作る。
- 切った section では relative の音高と音価がリセットされるので、`B'` のような印や数字で補う。
- OMR（Calamo）と `lysc import`（`LysWriter.WriteVoltaSections`）も、同じ理由で反復を section に分解して書き出している。

コード譜を紙から手で写すときに一番響く。頭から順に打てない。

## 2. 取り下げる決定が守っていたもの

当時の根拠（HANDOFF-ARCHIVE「閉じた §3 の根拠」・GRAMMAR §8.1 の注）は、記号の置き場所ではなく
**読み手が割れていたこと**だった。この設計はそこを守る。

| 当時の欠陥 | この設計での扱い |
|---|---|
| music の `|:` を MIDI はそのパートだけ展開し、ページは score の反復として描いた（絵と音が違う） | 反復は score のもの（§3.2）。演奏順の読み手は 1 つ（§4） |
| music の `segno` は描かれるだけで、form の `ds` の行き先にならなかった | ナビ記号も同じ 1 本の列で読む（§3.6） |
| `| |:`・`| break |:` が空小節を作った（第303 の実測: 72 冊・87 箇所） | 規則は変えない＝空小節のまま（§3.4・オーナー決定 2026-11-14） |
| 対の判定が書かれた文面ではできなかった | 文面では判定しない。組み立てた列で判定する（§3.3）。ページの collector は今もこの形で判定している（`RepeatPairingValidator` の注・`UnpairedRepeatWarnings`） |

## 3. 規則

### 3.1 書ける場所

section の中の音楽（パートの本体と `chords` の行）に、`|:` `:|` `:|:` `:|*N` `[N. … ]` と
`segno` `coda` `fine` `to coda` `dc` `ds`（`al fine`・`al coda`）を書ける。form にも今までどおり書ける。

- **phrase の中は書けないまま**（LYS1034＝第1249 からこの 1 つだけを言う）。phrase は使い回す部品で、置かれる場所ごとに反復の意味が変わる。
- `lyrics` の行は今までどおり対象外（`verse N { }` は別の構文）。
- section の無いファイル: パートの音楽は section の中にしか書けないので（`part mel { c'4 … }` はエラー）、section の無い形は
  **平らに書いた chords トラックだけ**（`chords X { C | G7 | }`・by-part でないファイル）。そこにも同じ記号を書ける（オーナーの指摘 2026-11-14 で Q1 は閉じた）。

### 3.2 反復は score のもの

記号は「section の頭から数えて何本目の小節線か」に付く。section のどのパートに書いても全パートに効く。

- 書かなかったパートは従う（コードの行にだけ書けば旋律も反復する）。
- 複数のパートが書いたら合わせて読む。**`|:` と素の `|` は正しい書き方**（診断なし）。**同じ小節線に違う反復記号（`|:` と `:|`・`:|*2` と `:|*3`）はエラー**
  （LYS1070・`RepeatSignAgreementValidator`・オーナー決定 2026-11-14）。quick fix が 3 つ付く（`Editing.RepeatSignFixes`: 相手の記号をここに書く／この記号を相手に書く／ここを素の `|` にする。相手の小節線が何行も離れていても探さずに済む）。ending とナビ記号も同じ規則（第1251・同じ LYS1070）: 同じ小節線で開く 2 つのパートの ending は番号も長さも同じ、同じ小節線に書く 2 つのパートのナビ記号は同じ並び。書かないパートは従う。quick fix は小節線の分だけ。
- 小節線でない所（小節の途中）に書いたらエラー。

### 3.3 対は form に組み立ててから見る（オーナー決定 2026-11-14）

score が演奏する form の**刷る順**に section を並べ、form 自身が書いた記号も同じ所に入れて、
**1 本の記号の列**にする。規則はその列の上で見る。section の中で対が閉じている必要はない。

```
section A { chords c { C | F |: G | C | } }
section B { chords c { Am | Dm :| G7 | C | } }
form { A B }          -- G C Am Dm を 2 回
form { B A }          -- エラー: A の |: を閉じる :| がない（B の :| は曲頭からの反復）
```

列の上の規則は form が今持っているものと同じ。

- `|:` は後ろの `:|` で閉じる。閉じなければエラー（今の LYS4017）。
- `:|` だけなら、前の反復の終わり（なければ曲頭）から繰り返す（今の form の読み）。
- 開いている反復の中でもう一度 `|:` が開いたらエラー（入れ子は書けない）。
  form の `|: A :|` が、中に反復を持つ A を囲む形もこれに当たる。
- ending の番号の規則（LYS1041〜1043）は列の上で見る。
- form の無いファイルは宣言順の列で見る。

検査は form ごと。同じ section を 2 つの form が使い、片方でだけ崩れるなら、
エラーは section の記号の位置に出し、どの form で崩れるかを文に書く。

### 3.4 `|:` と直前の小節線（オーナー決定 2026-11-14: 今の規則のまま）

**`C | |: G` は空小節を作る。`C | | G` が作るのだから。** `|:` も書かれた小節線の 1 本で、直前の `|` とのあいだは空の小節
（2026-08-28 の決定そのまま・`MeasureBuilder.HandleBarline`）。`| break |:` も同じ。例外は今までどおり 1 つだけ:
scope（section の音楽・行）の**頭**の `|:` は何も閉じない（`|: c d e f :|` は 1 小節）。
紙から写すときは小節線 1 本につき記号 1 つ（`f |: g`）と打てば空小節はできない。

- この設計の最初の版は「`|:` は直前の `|` と重なる」（第303 が見送った rule 2）と書いていた。決定を断らずに覆す内容で、誤りだった。実装していない。
- **コードの行は音楽に合わせた**（`Semantics.ChordRowBarLines.Closes`＝行の小節線が何を閉じるかの唯一の規則・8 つの読み手が読む）。
  それまで行の読み手は 2 通りに割れていた（`C | |: G` を 2 小節と読むものが 5 つ、3 小節と読むものが 3 つ。後者は行の頭の `|:` にも空小節を数えた）。

### 3.5 ending

`[1. … ]` は小節線の位置から始まり、`]`（または `-]`）か `:|` で終わる。
最後の ending は `]` で閉じる。閉じる `]` は後ろの section にあってもよい（§3.3 の列で見る）。
`@voltaBracket(…)` は form と同じ。

### 3.6 ナビ記号

`segno` `coda` は次の小節の頭に、`fine` `to coda` `dc` `ds` は前の小節の終わりに付く。
小節の途中はエラー。道順は §3.3 の列の上で今の `FormRoute` の規則どおりに読む
（`ds` は最後の `segno` の後へ・`al fine` は `fine` で終わる・戻った区間の反復は 1 回）。

### 3.7 section を切らないので引き継ぐもの

反復や ending の境目で relative の音高・音価・調・拍子・音部記号は動かない（section の境目ではないから）。
ラベルも増えない。

## 4. 作り方

**演奏順の読み手を 1 つにする。** form の項目（section 1 つ）を単位にしている今の読みを、
「section の play の中の小節の範囲」を単位にする読みに替える。新しい読み手（仮に `Semantics.RepeatStream`）が
§3.3 の列を作り、構造（反復・ending・道順）と診断を返す。各出力はそれを読む。

読み手ごとの距離。**段 0 で測った**（2026-11-14・探り譜 7 冊と表は Lab `sessions/p1243/stage0.md`）。
LYS1034 のエラーが出ていても出力は書かれるので、今の答えがそのまま読める。

| 読み手 | 今の答え（実測） | 要ること |
|---|---|---|
| ページ（`MeasureCollector`） | **§3.2・§3.3 をもう満たす。** 片方のパートにだけ書いた反復・ending が全段に出る。`|:` を A に、`:|` を B に書いても対になる。コードの行に書いた反復は譜表に出る。ナビ記号 4 種も描く | パートどうしが食い違うときの診断と、コードの行の ending の括弧（§7） |
| MIDI（`MidiExporter.ProcessSequence`／`ProcessRepeatSpan`） | **書いたパートだけ**反復する（2 パートで 25 音・正しくは 27）。section をまたぐ対は反復しない。コードの行の反復は誰も反復しない。ナビ記号はたどらない | 一番遠い。この歩きは 1 つの section の 1 パートしか見えない（同ファイル :2971-2985 の注）。score の小節の列から道順を取り、section の小節の範囲を全パートで鳴らす形にする |
| MusicXML（`MusicXmlExporter`） | 書いたパートにだけ `<repeat>`。`<ending>`・回数・ナビ記号は出ない。コードの行の反復は出ない | 全パートの同じ小節に barline／ending／回数／記号を書く |
| `.ly`（`LilyPondExporter.EmitInlineRepeat`） | 書いたパートにだけ `\repeat volta`・`\alternative`。ナビ記号は `\segnoMark`・`\jump`・`\codaMark` で出る | 書かなかったパートにも同じ小節で入れる（`\unfoldRepeats` と MIDI のため。絵は今も score 全体に出る） |
| タイの持ち越し・小節の補完（`PlayedOrder.Expand`・`SectionBoundaryBars`） | **測った（第1245・§7）。** タイは 5 つの読み手とも直した（MIDI 第1245・ページと検査 第1246・MusicXML と `.ly` 第1247）。`:|` が割る小節も 5 つとも 1 小節と読む（第1247・`SignCutBars`） | score 全体の記号の列（`RepeatStream`）から「小節の次に鳴る小節」を引く |
| 歌詞の `verse N` | 未測定 | §6 Q3 |
| プレビューの増分（`MeasureCollector.Resume`） | **測った（第1245）。** 記号を 1 つのパートに打つ・消す・1 文字ずつ打つ 10 通りで、増分の絵は全部描き直した絵と同じ（網 `InlineRepeatIncrementalTests`） | なし |
| 読み込み（`LysWriter`）・OMR・Dettato | section に分解して書く | §6 Q2 |

オーナーの本にも前例がある: LYS1034 より前、ABC・Automatic・Beat It の 3 冊は `|:` を 1 つの section に、
`] :|` を後ろの section に書いていた（`MidiExporter.cs` :2975-2978 の注）。§3.3 はその綴りを戻す。

### 段取り

LYS1034 は**最後に**外す。エラーの本も今は描けて鳴るので、各段の網はエラーつきの本の出力で書ける。

0. ✅ 済み（上の表）。
1. `RepeatStream`（✅ 列と演奏順・第1246＝§7「段 1」。form だけの本は答えが変わらない）と診断（✅ 第1248＝§7「診断」・パートどうしの ending とナビ記号の食い違いは残り）。
2. ✅ ページ（行の ending の括弧）。3. ✅ MIDI（§7）。4. ✅ タイと小節の補完（§7「MusicXML・`.ly` のタイ」「記号が割る小節」）。5. ✅ MusicXML（小節線・ending・回数・行の記号・音楽のナビ記号）。6. ✅ `.ly`（全声部に書く。LilyPond は通る・絵の突き合わせは未）。7. ✅ 歌詞（規則 3 の再生と 4 種目の警告は第1253）。
8. ✅ LYS1034 を phrase だけに狭める（第1249＝§7「段 8」）。GRAMMAR・GRAMMAR_FOR_LLM・SYNTAX_REFERENCE・TUTORIAL・LEARN_FOR_LLM・site の手引き・補完・CHANGELOG 2 本。
9. 読み込みと OMR の書き出し（Q2 の答えしだい）。

## 5. 残る懸念

- **section が単独では読めなくなる。** `|:` で終わる section は、form を見ないと反復の範囲が分からない。紙の譜面と同じなので受け入れる。
- **綴りが 2 つになる。** form と section のどちらにも書ける。読み手が 1 つなら意味は割れないが、書き出し側（Q2）は 1 つに決める。
- **同じ section を 2 回演奏する form**（`form { A A }`）で A が `|:` だけを持つと入れ子のエラーになる。エラーの文で form を名指す。

## 6. 未決（オーナーに訊く）

- ~~**Q1** section の無いファイル~~ — 閉じた（§3.1。残る形は平らな chords トラックだけで、同じ記号を読む・網 `AFlatChordsTrackWritesEndingsToo`）。
- ~~**Q2** 読み込み・OMR・Dettato の書き出しの綴り~~ — **オーナー決定 2026-11-14**: 反復記号は**なるべく form 側**に書き出す（今の形のまま）。
  volta の ending の小節が短い場合は section 側に書いてもよい（1 小節の ending のために section を切らない）。境目の小節数は書き出す側で決める。
  本体が出てから、Calamo と Dettato の REQUESTS に知らせる。
- **Q3** 歌詞の `verse N`（**オーナー決定 2026-11-14: 下の 5 規則でよい**。歌い分けは実装済み・警告と quick fix は未実装）。オーナーの方向（2026-11-14）:「規則を増やした方が分かりやすくなるかもしれない。
  たとえば section の中で繰り返しがあれば、必ず対応する `verse 1 {}` を書かないといけない」。最初の案（その小節が歌われる N 回目＝verse N）は、
  中に反復を持つ section を form がもう一度演奏すると行と番が対応しなくなるので取り下げた。
  1. **数える軸は 1 つ**: section の中に反復があれば `verse N` はその反復の N 回目。なければ form がその section を演奏する N 回目（今のまま）。
  2. **反復のある section で verse を書くなら全部の回を書く**（1〜K をちょうど 1 回ずつ・`verse 1-2 { }` 可）。足りない・余る・重なるはエラー。`verse` の無い言葉は毎回同じ（今のまま）。
  3. **`verse N` に言葉を書けるのは N 回目に歌われる小節だけ**: 反復の外は `verse 1` だけ・1 番括弧は `verse 1`・2 番括弧は `verse 2`。ほかはエラー。
  4. **反復のある section を form が何度演奏しても言葉は毎回同じ**。別の言葉にしたいなら section を切って form の反復で書く（エラーの文で案内）。
  5. **`ds` で戻った区間は最後の回の言葉**（戻ったあとは反復を最後の 1 回だけ演奏する）。
  - 1 つの section に回数の違う反復が 2 つあるときは多いほうに合わせ、少ないほうの小節は余った verse を空にする（規則 3 のとおり）。
  - **規則 2・3 に反する書き方は、再生を止めない**（オーナー指示 2026-11-14:「直し方が分からなくて再生できない状態になるのはもっとまずい。quick fix を提供できれば良い」）。
    エラーではなく警告にし、どれにも「そのときどう歌うか」と quick fix を付ける。
    | 書き方 | そのとき | quick fix |
    |---|---|---|
    | 回が足りない（2 回繰り返すのに `verse 2` が無い） | 足りない回は最後に書いた verse を歌う | 「`verse 2` を足す」＝その section の小節線だけを並べた空の verse を挿入（「`verse 1` の言葉を写す」も） |
    | 回が余る（2 回なのに `verse 3`） | 余った verse は刷るが歌わない | 「`verse 3` を消す」／「反復を 3 回にする（`:|*3`）」 |
    | 同じ回を 2 つの verse が書く | 先に書いたほうを歌う | 「番号を振り直す」 |
    | N 回目に歌われない小節に `verse N` の言葉（反復の外の `verse 2`・1 番括弧の `verse 2`） | その言葉は刷るが歌わない | 「この小節の言葉を、歌われる verse へ移す」／「この小節を空にする」 |
    規則 4・5 は読み方の決まりで、反する書き方が無いので診断も無い。
  - 実測（2026-11-14・Lab `p1243/probe/p-lyr-inline`・`q-lyr-form`）: 絵はどちらの綴りでも同じ（1. と 2. が重なる）。
    歌は、form の反復なら 2 回目に verse 2、section の中の反復だと 2 回とも verse 1 だった。
  - **実装（第1243）**: 音楽に記号を書いた本は、展開したあとの音に言葉を置く（`MidiExporter.SingLyrics` を `PlayedTimeline.Unfold` の後に回す）＝反復の N 回目に verse N。
    足りない回は最後に書いた verse（`lastVerseGoesOn`・規則 5 の `ds` の戻りも同じ腕）。反復を持つ section は、どの印刷にも verse を全部重ねる
    （`LyricsCollector.SectionRepeatsItself`・規則 4）。網 `AVerseIsThePassOfTheRepeat`。
  - 「足りない回は最後の verse」は form だけで反復を書いた本にも入れた（オーナー決定 2026-11-14）。それまでは verse の無い回に何も歌わなかった。
    帰結: `verse 1,3 { … }` で 2 回目を飛ばすと、2 回目も同じ言葉を歌う。黙らせたい回は空の verse（`verse 2 { | }`）を書く。
  - **警告 LYS4080 と quick fix（第1243）**: 下の表の上 3 行を実装（`VerseRepeatValidator`・`Editing.VerseRepeatFixes`・網 `VerseRepeatValidatorTests`）。
    回数は section の音楽が求める最大（`:|*N`・ending の最大の番号・どちらも無ければ 2）。「反復を 3 回にする」の fix は入れていない（記号が複数のパートにあると全部を直す必要があるため）。
  - **規則 3 の再生と、表の 4 行目の警告（第1253）**: 回は 1 つの表から読む＝`RepeatStream.RouteByPass`（`Route` を継ぐ前の区間に「何回目か」を付けたもの。反復の本体は 1..K・ending はその番号・反復の外は 1・`|:` の無い `:|` が戻した区間は 2・ジャンプで戻った区間は最後の回）。
    - 再生: `PlayedTimeline.Unfold` がその表を演奏の tick に並べて返し、`SingLyrics` は反復を持つ section の verse を「発音の回がその verse の回か」で置く（`PassAt`）。
      第1252 の実測（Lab `p1252/probe-verse-endings.md`）: それまでは verse N を「その音の N 回目の発音」に置き、足りなければ最後の発音へ丸めていたので、1 回しか鳴らない括弧の音に全部の verse が乗り、先に書いたほうが歌われた。
    - 「足りない回は最後の verse」は、**その歌詞の section の verse がどれも名指していない回**だけ（それまでは音ごとに見ていた）。帰結: 反復を持つ section で `verse 2` の小節を空けると、2 回目のその小節は黙る（verse 1 の言葉は歌わない）＝**オーナー決定 2026-11-15「2 回目は黙るので良い」**。form だけで反復を書いた本は今までどおり音ごと。
    - 警告: `VerseRepeatValidator` の `NotSungHere`。小節の回は列（`RepeatStream.PassesOf`）の、その section を最初に演奏する form の最初の印刷から読む。歌詞の小節は section の小節と順に対応させる（ページの置き方と同じ）。警告は言葉の上に出る。
      quick fix は「その小節で歌われる verse の、同じ小節が空ならそこへ移す」と「この小節を空にする」。
    - 網: `InlineRepeatMidiTests.AVerseSingsInTheBarsItsPassPlays`・`VerseRepeatValidatorTests` の 3 本。反復記号が小節を割る本（弱起が反復の中・`g a b :| g4 |`）でも歌詞の小節と列の小節は合う（Lab `p1253/cut-bar*.lys`＝正しい本は警告 0・反復の後の小節に書いた `verse 2` は 2 件）。

## 7. 経過

### MIDI（第1243・2026-11-14・`fd6c5d0b0`）

作り: 音楽に記号を書いた本は、**刷った順に 1 回だけ歩き**、記号（form のものも音楽のものも）を時刻つきで控え、
書き出しの最後に小節線の時刻で展開する（`Midi/PlayedTimeline.cs`・`MidiExporter.PlayFormAsPrinted`・`ProcessSequence` の控え）。
道順は `FormRoute`、回数と ending は `RepeatPasses` をそのまま使う。網は `InlineRepeatMidiTests`（16 本・毒で 27 赤）。

- 全パートが従う（コードの行に書いた記号にも）。section をまたぐ対、form の `:|` が音楽の `|:` を閉じる形、`ds`／`dc`／`al fine`／`al coda` を読む。
- ✅ **読み手は 1 つ（第1255・オーナー指示 2026-11-15「あるべき妥当な形に」）。** どの本も刷った順に 1 回歩いて列で展開する。
  form 専用の歩き（`PlayRepeatBlock`・`PlayRepeatRun`・`RepeatFromTheBeginning`・音楽の `ProcessRepeatSpan`）は外した（`MidiExporter.cs` −387 行）。
  - 外す前の実測（Lab `sessions/p1255`・`midi-sweep.ps1`＝1041 冊・1268 本の .mid を古い歩きと突き合わせ）: 1265 本がバイト一致・違う 3 本は 1 冊（ABC）で、古い歩きが書いていた冗長な拍子イベント 1 個だけ。
    テンポ・拍子・調・タイは探り譜でも同じだった（古い歩きも section の頭で戻していた＝第1243 の「差」の記述は、form の反復では起きない）。
  - **2 つが違った点と、決めた規則＝「戻った小節は、そこに刷られているとおりに鳴る」**:
    ⑴ form の `|: … :|` の中で変えた強弱（`|: c1 | d1@ff :|`）は、古い歩きだけ 2 回目の頭へ持ち越していた（2 回目の c が ff）。同じ歩きの `dc`／`ds`／片側の `:|` と、音楽に書いた反復は頭の強さに戻していた＝古い歩きの中でも食い違っていた。戻す側に統一（網 `AReplayedBarSoundsAsItIsPrinted`）。LilyPond の MIDI は時間順に持ち越す＝LILYSHARP-OWN。
    ⑵ 反復記号をまたいで踏んだままのペダル（LYS4023 が「そこで切る」と言う形）は、古い歩きは次に踏むまで踏みっぱなしだった。今は戻った位置の状態（離す）。
  - 途中で見つけて直した欠陥: 列の最後の区間が、曲の終わりの tick に在るペダルの離しを落としていた（音楽に記号を書いた本で、踏んだまま終わる曲のペダルが .mid に残る）。網 `APedalThePieceEndsUnderIsLifted_AfterARepeatToo`。
  - ⚠️ **決まっていない規則が 1 つ（オーナーに訊く）**: `[1. B] :| [2. C] [3. D]`＝3 番括弧の前に `:|` が無い形。form に書くと「反復が開いていない ending」＝2 回＋D を 1 回（LYS6008・ページ・双子・MusicXML もその読み）、音楽に書くと 3 回繰り返す（`MidiRepeatTests.InlineVoltas_CountInferredFromHighestNumber`）。
    どちらも今までどおりに残した（`RepeatStream.Elements` が「form が書いた ending か」で分ける）。オーナーのコーパスの 11 冊が form でこの形を書いていて、元の .ly は `\repeat volta 3` ＋ alternative 3 つ（＝3 回）。
    3 回に統一するなら form の読み手（ページ・双子・MusicXML・LYS6008）も一緒に変える。網 `AFormsThirdEndingWithNoRepeatSignBeforeIt_IsPlayedOnce`。
- 残り: 反復の中に書いたナビ記号はたどらない（form と同じ）／1 つのパートの 2 つの声部が同じ記号を書くと 2 つと数える／
  歌詞は刷った位置の言葉をそのまま繰り返す（Q3）。

### 途中で見つけたもの

- ✅ **コードの行が `[1. …]` とナビ記号を読む**（同便）。行の ending は入れ子にしない＝`[1.`（中身の無い `InlineVolta`）と `]`（新しい `VoltaEnd`）を
  行の項目の間に平らに置く。行の読み手は 54 ファイルあり、入れ子にすると知らない読み手が ending の中のコードを黙って落とすため。
  MIDI は読む（網 `AChordRowWritesEndingsAndJumps`）。ページも行の ending の括弧を描く（`ChordNameCollector.RowEndings`・網 `ThePageBracketsARowsEndings`・譜表の上でも行だけの譜でも）。`-]` と `@voltaBracket(…)` は行ではまだ読まない。
- ✅ **行の小節線の規則を 1 つにした**（§3.4・`ChordRowBarLines`・網 `ARowsBarLineBeforeARepeatSignIsAnEmptyBar`／`ARowOpeningWithARepeatSignCountsTheBarsItWrites`）。
- ✅ **form の無い by-part の本で、2 つ目のパートが 1 つ目の後に鳴る**（`part mel { section A { … } } part bas { section A { … } }`・form なし。
  Lab `p1243/probe/h-plain.lys`: bas の最初の音が 8 拍目）。反復とは無関係の既存の欠陥だった。第1250 で直した: form の無い本の MIDI は、
  section を名前の宣言順に 1 回ずつ、全パートの写しを一緒に鳴らす（`MidiExporter.ProcessNode` の section の腕＝`PlaySectionByName`・網 `FormlessMidiTests`）。

### 段 4: 反復をまたぐタイと、反復記号が割る小節（第1245・2026-11-14）

実測の表と探り譜は Lab `sessions/p1245/stage4.md`。form の綴りの本と並べて比べた。

- **規則は form のものをそのまま使う**（§3.3 の列の上で読む。新しい決めごとは無い）: タイは「次に鳴る音」へ運ぶ（オーナー決定 2026-09-28）。
  弧は刷った次の音が次に鳴る音でもあるときだけ描き、ほかの行き先の頭には短い弧（repeat tie）、弧の出ない音には掛かったままのタイを描く。
- ✅ **MIDI のタイ**（`PlayedTimeline.Unfold`・網 `ATieIsCarriedToTheNotePlayedNext` 6 通り・毒 4 本とも赤）。それまではどの音もジャンプで切っていたので、
  本体から 2 番括弧へ入るタイは打ち直し、1 番括弧から頭へ戻るタイは消えていた。直し方: ジャンプで切った音と、ジャンプの位置で終わる `~` つきの音（`MidiNote.TiedOut`）は、
  着地点で始まる同じ高さの音へつなぐ。刷った順で前の小節からつながっていた音（`[1. d1~ :| [2. d1 ]` の 2 番括弧の d）は、着地点から打つ。
- ✅ **頭の `|:` と宣言した弱起**（`MeasureValidator`・網 `ARepeatSignAtTheHeadKeepsTheDeclaredPickup`）: 音楽の頭の `|:` のあとの小節が、section の頭に宣言した `partial` を忘れて LYS2006 を出していた。
- **残り 1: ページ・検査・MusicXML・`.ly` のタイ。** 本体から 2 番括弧へ入るタイは、2 番括弧の頭に短い弧が出ない。1 番括弧から頭へ戻るタイは何も描かれず、
  LYS4007 が 2 番括弧の音（次に鳴らない音）に出る。4 つの読み手は行き先を section の play の単位で持っている（`SectionPlayGraph`＝play の最初の音の印・`PlayedOrder`）ので、
  section の中の小節線に立つ記号が見えない。記号は別のパートが書いていることもあるので、声部の小節だけからは作れない。
  作り: score の記号の列（段 1 の `RepeatStream`）から「小節 m の次に鳴る小節」の表を作り、`TieDetector`・`SectionTieCarry`・`TieTargetScanner` と 2 つの書き出しがそれを引く。
  ⚠️ タイの絵が別のパートの記号に依るようになるので、増分（`SectionTieCarry` は「声部だけの関数」と書いてある）の網を先に書く。
- **残り 2: `:|` が割る小節**（`|: g4 | c d e f | g a b :| g4 | c1 |`＝弱起を含む反復。紙の譜面ではごく普通の形）。form の綴り（`form { |: P A :| Z }`）は 1 小節として読む（`SectionBoundaryBars`）:
  警告なし・MIDI にすき間なし。section の中に書くと 3 拍と 1 拍の 2 小節になり、LYS2001 が出て、MIDI は 3 拍の小節を 4 拍に埋める。
  同じ規則（短い小節を、次に鳴る小節の頭がちょうど補うなら 1 小節）を上の表で引けば入る。検査・MIDI・ページの小節番号・MusicXML・`.ly` を一度に。
- 小節の途中の `[1.`（`g a [1. b c :| [2. b g ]`）は §3.5 でエラーにする形。診断はまだ無い（段 1）。
- タイが戻った先の音と違う高さのとき（`|: c1 | d1~ :| d1`＝1 回目は c へ戻る）: form の綴りは LYS4007 を出し、音楽に書くと出ない。残り 1 と一緒に揃う。

### 段 1: score の記号の列と、ページ・検査のタイ（第1246・2026-11-15）

- **`Semantics.RepeatStream`**: form の刷る順に section の play を並べ（1 play は section の小節数＝`SectionBarCounts` の semantic の数）、form の記号と音楽の記号を小節線の位置（その前に刷った小節の数）に置く。
  `Played` が演奏順の区間、`Next(bar)` がその小節の次に鳴る小節。展開は `RepeatStream.Route` の 1 つ（`PlayedTimeline` から移した）で、MIDI は同じものを tick の上で使う。form の無いファイルは宣言順。
- **`Semantics.SectionSigns`**: 1 つの section の声部が書いた記号を「section の頭から何小節のあとか」で読む（`.ly` の書き出しの読みを移して共有した）。
  ナビ記号は「前に閉じた小節の数、その小節にもう音があれば +1」（記号も文字も同じ式）。
- 網 `RepeatStreamTests`: form だけの本は、全 net book の全 form で `PlayedOrder.Expand` と同じ演奏順（入れ子の block は除く＝`PlayedOrder` が読まない）。
- **ページと検査のタイ**（下の「残り 1」の 4 つの読み手のうち 2 つ）: コレクタが声部を仕上げるとき、各小節線の記号をその後ろの小節の最初の音に刻印する
  （`MusicItem.ScoreSignsBefore`／`ScoreSignsAfter`・`MeasureCollector.StampTheScoresSigns`・綴りは `RepeatStream.Encode`）。
  `SectionPlayGraph` は刻印があれば、刻印の小節と play の頭で区切った区間を `Route` で展開する。`TieDetector`・`SectionTieCarry`・`TieTargetScanner` は今までどおりこの表を引くので 3 つとも揃う。
  刻印するのは「音楽に記号を書いた本」の「タイを書いた声部」だけ（刻印は内容なので、記号の前後の同じ小節がレイアウトを共有できなくなる＝`SystemLayoutCacheTests`）。ほかの本と声部は form の刻印と `PlayedOrder` のまま。
- 声部の小節が列と揃わないとき（play の小節数が違う・section が違う）は刻印しない＝今までの読み。1 つの Measure が複数小節を持つ形（`R1*N` など）があるかは未確認。
- 確かめ: 探り譜 t-b は form の綴り t-e と同じ絵（2 番括弧の頭に短い弧）。t-c は掛かったままのタイと頭の短い弧で、LYS4007 は出ない。t-a は form の綴りと同じ LYS4007。
  網 `InlineRepeatTieTests`（別のパートが書いた記号・増分 3 通りを含む）。毒 6 本とも赤（Lab `p1246/poisons.ps1`）。
- 打鍵の門: 「音楽に記号があるか」は green を歩き、top-level の宣言ごとに答えを覚える（`RepeatStream.InTheMusic`・`KeystrokeStaysRedFreeTests`）。
  記号とタイのある本は、木ごとに 1 回列を作る（全声部の `MeasureModel.Split` と木の索引）。その値段は未測定。
- **残り**: ~~MusicXML と `.ly` のタイ~~（第1247 で済み・下）／`:|` が割る小節（上の残り 2・`Next` を引く）／診断（小節の途中の記号・入れ子・ending の食い違い）／
  MusicXML の `_musicNavMarks` と MIDI の `WritesRepeatsInTheMusic` は同じ読みの別の綴りのまま（`SectionSigns`・`InTheMusic` に寄せられる）。

### MusicXML・`.ly` のタイ（第1247・2026-11-15）

段 4「残り 1」の残っていた 2 つの読み手。どちらも**音楽に記号を書いた本だけ**列（`RepeatStream.Next`）を引き、ほかの本は form の読み（`PlayedOrder`）のまま。
探り譜と LilyPond の絵は Lab `sessions/p1247/probe`（form の綴りの双子 `t-e`・`u-g`・`u-i-twin` と同じ出力になることを確かめた）。

- **MusicXML**（`MusicXmlExporter.CarryTiesAlongTheColumn`）: 小節を書き終えるたびに、開いているタイの音を控える（`_openAtBarEnd`）。書き終わったあと、
  「次に鳴る小節が刷った次の小節だけ」でない小節と、パートのブロックの最後の小節について、次に鳴る小節それぞれの最初の音に stop を書く。
  刷った次の小節が次に鳴らないとき（`[1. d1~ :| [2. d1 ]`）は、流れが付けた stop を外す。stop の付かない start は外し、弧の出ない start には `let-ring` を足す（綴りは `FinishCarriedTies` と同じ）。
  行き先は、タイの音と同じ声部の、その小節の最初の音。パートの小節の数が列と同じときだけこの読みにする（違えば form の読み）。
- **`.ly`**（`LilyPondExporter.RepeatTieBars`／`PlantTheRepeatTies`）: パートの声部がタイで終える小節（`TiedBars`＝小節線の前の `~`）から、刷った次の小節以外の「次に鳴る小節」を求め、
  その小節の頭に `RepeatTieMarker` を置く（次の音が `\repeatTie` を取る）。ending の直前の印はその ending の中身の先頭に入れる（`EmitInlineRepeat`・`:|` の先の ending を探す読みも印を透かす）。
  別の声部の ending を広げる前に置くので、広げた ending にも一緒に入る。
- 網: `InlineRepeatTieMusicXmlTests` 9 本・`InlineRepeatLilyPondTests.ATieOverARepeatTheMusicWrites_…`。毒 10 本とも赤（Lab `p1247/poisons.ps1`）。
  掃き 1041 冊（xml 1268 件・ly 1268 件）は HEAD と差 0。LilyPond 2.26.0 は探り譜 10 冊を通る。
- 残り: 1 番括弧から頭へ戻るタイ（弧の出ないタイ）は、`.ly` では `~` のままなので LilyPond が `unterminated tie` と警告する（form の綴りも同じ・ページは短い弧を描く）。
  2 声部の小節どうしのタイは網がない（声部の番号で行き先を選ぶ）。フレーズの中で終わるタイは `.ly` では見ない（form の読みの `EndsWithTie` と同じ）。

### 記号が割る小節（第1247・2026-11-15）

段 4「残り 2」。`|: g4 | c d e f | g a b :| g4 | c1 |`（弱起を含む反復）の `g a b` と `g4` を 1 小節と読む。探り譜は Lab `sessions/p1247/probe/m-a-pickup`（form の綴りの双子 `m-c-formtwin` と同じ答えになることを確かめた）。

- **規則は `SectionBoundaryBars` のものを列の上で読む**（新しい決めごとは無い・`Semantics.SignCutBars`）: 短い小節は、**閉じる小節線に score の記号が立ち**、次に鳴る小節（`RepeatStream.Next`）が
  どれもちょうど残りを持っていれば「前半」。小節は、開く小節線に記号が立ち、前に鳴る小節がどれもちょうどその分を残していれば「後半」。
  記号のない素の小節線の前後は今までどおり短い小節（LYS2001・MIDI は埋める）。
- **score が決める**: その 2 小節を両方書いている音楽の声部が全部そう書いているときだけ（埋め草の声部とコードの行は数えない）。長さは `MeasureModel.Split`。
  宣言した弱起（`partial`）は後半にはなるが前半にはならない。曲の最初の小節は後半にならない。
- 後半が「続き」になるのは、前に鳴る小節が全部前半のとき（`ContinuedFrom`）: 刷った前の小節、または 2 つ目以降の ending なら本体の最後の小節（`g a | [1. b c :| [2. b g ] |`）。
  反復が 4 拍の小節へ戻る形（`|: c d e f | g a b :| g4 |`）は、1 回目に 3 拍の小節ができるので前半にならず、今までどおり警告する。
- 読み手: 検査（`MeasureValidator`＝どの form のどの play でも半分である小節には LYS2001・LYS2006 を出さない）／MIDI（`ProcessSequence`＝前半を埋めず、後半の小節は前半の分だけ進んだところから数える）／
  ページ（`MeasureCollector.SignCutContinuations`＝`Measure.ContinuesBar`・小節番号は 1 つ）／MusicXML（`NumberTheBarsASignCuts`＝後半は前半の番号で `implicit`）／
  `.ly`（記号を別の声部から受け取る声部が前半の後ろに書いた素の `|` を書かない＝LilyPond の bar check が通る）。
- 網: `SignCutBarsTests` 7 本・`InlineRepeatCutBarTests` 6 本。毒 11 本とも赤（Lab `p1247/poisons2.ps1` と `.ly` の 1 本）。掃き 1041 冊（svg・xml・midi・ly 各 1268 件）は HEAD と差 0。
- 残り: ナビ記号（`fine`・`ds`）が割る小節も同じ規則で読む（記号の種類を選んでいない）が、網は反復記号と ending だけ。曲が割れた小節の前半で終わる形（`… g a b fine`）は、今までの「最後の小節が弱起を補う」の読みのまま。
  1 つのパートだけを描く譜でも score 全体の声部で決める。ページの絵は LilyPond と突き合わせていない。

### 診断（第1248・2026-11-15）

§3.3・§3.5・§3.6 の規則を、音楽に記号を書いた本について列の上で見る（`Semantics.MusicRepeatValidator`・網 `MusicRepeatValidatorTests`・探り譜は Lab `sessions/p1248/probe/d-*`）。
直す前に出ていたのは、閉じない `|:`（LYS4017）だけだった。

| 形 | 診断 | 出す場所 |
|---|---|---|
| 小節の途中の ending（`g a [1. b c :|`） | LYS1071（エラー・新設） | `[1.` |
| 前後に音のあるナビ記号（`d2 segno e2`） | LYS1071 | 記号 |
| 開いている反復の中の `|:`（form の `|:` の中も・後ろの section でも） | LYS1072（エラー・新設） | 内側の `|:` |
| ending の番号の抜け・重なり | LYS1043（form と同じ規則） | 抜けの次の ending／重ねた ending |
| ending のある反復の `:|*N` | LYS1042 | その `:|*N` |
| 本体のない反復（`|: [1. …`） | LYS1041 | `|:` |
| 反復のない ending | LYS6008（警告） | `[1.` |

- 小節の端のナビ記号は小節線に立つ（`c1 segno | d1`・`g1 ds | c1` は診断なし）。小節を割る ending は小節線を書けば 1 小節と読む（`g a | [1. b c :| [2. b g ] |`＝`SignCutBars`）。
- 列の診断は form ごとに見て、**音楽が書いた記号の位置**に出す（form だけが書いた記号は `FormDeclarationValidator` のまま）。同じ場所の同じ診断は 1 回。
- 毒 10 本とも赤（Lab `p1248/poisons.ps1`）。一緒に直した: `.ly` で、小節線なしにタイで終わる section の最後の小節を見落としていた（閉じ括弧のトークンを音と数えていた・第1247 の `EndsTied`）。
- ~~パートどうしで ending の番号やナビ記号が食い違うときの診断~~（第1251 で LYS1070 に入れた・§3.2）。残り: コードの行の小節の途中の記号は見ていない。

### 段 8: LYS1034 を phrase だけに（第1249・2026-11-15）

- **検査**（`RepeatStructureScopeValidator`）: 反復の小節線・ending・ナビ記号を、phrase の中に書いたときだけ LYS1034。定数は `RepeatStructureInPhrase`。
  文は「phrase が演奏される場所（section の音楽）か form に書け」と言う。section の音楽・コードの行・form はどれも書ける。
- **見つけて直した穴＝LYS4025（行き先のないジャンプ）**: この検査は form の歩きだけを読んでいた。LYS1034 が外れると、form の `ds` が音楽の `segno` を「無い」と言い、
  音楽の `ds` は行き先が無くても黙っていた。音楽に記号を書いた本は列で読む（`RepeatStream.JumpFaults`＝`Played` を作るのと同じ `FormRoute` の歩きに報告させる）。
  警告は音楽の記号の位置にも出て、2 つのパートが同じ記号を書いても、2 つの form が同じ section を使っても 1 回。音楽に記号を書かない本は今までの読みのまま。
- **補完**: section の音楽（ドラムも）の一覧の最後に `|:` `:|` `:|*3` `:|:` `[1. :|` `[2. ]` `[3. ]` とナビ記号 10 語（`RepeatSignItems`）。phrase の中では出さない（`WithRepeatSigns`）。
  `|` や `:` を打ってから選ぶと、打った分を置き換える（form の一覧と同じ規則・`BarSignRunBefore`）。
- **`repeat volta` のエラー文**: 小節のまわりに `|: … :|` を書く形を先に言い、form の形を後に言う。
- **文書**: GRAMMAR §8.1「Repeat signs in the music」（規則 7 つ）・§7.1・§6 の注、GRAMMAR_FOR_LLM「Repeats in the music」、SYNTAX_REFERENCE「Repeats in the music」と Barlines の表・Navigation Marks、
  TUTORIAL、LEARN_FOR_LLM、site `manual-body.html`、CHANGELOG 2 本、MANUAL-CHECKS。COMPOSE_FOR_LLM は古い規則を書いていなかったので触っていない。
- 網: `RepeatStructureScopeValidatorTests`（phrase の 4 形・書ける場所 11 形）・`CompletionAuditTests` 3 本・`FormCompletionTests`（サーバー経由・phrase の中）・`FormJumpTargetValidatorTests` 11 本。
  毒 9 本とも赤（Lab `p1249/poisons.ps1`）。
- 残り: step キー（`NoteStepper`）は音楽の小節線を `|` `||` `|.` `!` のまま回す（反復記号は回らない）／~~`|: d1 [1. e1 :|` に LYS1071 と一緒に LYS1041 も出る~~（第1250 で LYS1071 だけに）／
  fixture と audit の本は form の綴りのまま（2026-08-31 に切り直した形・戻していない）／Calamo と Dettato への知らせ（Q2）は release のときに。

## 8. エディタ: 記号を全パートに書く（オーナー決定 2026-11-14・実装 `Editing.RepeatSignSpreader`＝小節線は第1243・ending とナビ記号は第1254・実機は未確認）

1 つのパートに書けば全パートに効くが、書いていないパートは読んだときに反復が見えない。
エディタが、ほかのパートの同じ小節線を実際に書き換えられるようにする。

- **打鍵には連動しない**（入力の途中でほかのパートが動くと、元に戻す操作と入力の流れを壊す）。
- **下線を出さない**（文法として正しい書き方なので、診断にしない）。
- **カーソルがどこにあっても実行できるメニュー**にする（小節線は 1 文字で、その上にカーソルが無いと出ない電球は使いにくい）。
  コマンドパレットと右クリックのメニュー。ファイル全体をそろえ、「何か所に書いたか・どこは書けなかったか」を返す。
- 逆向き（全パートから消して 1 か所に残す）も同じ場所に置く。
- 書き換えられない場所: 小節線が phrase の本体にある・`R1*4` や `| % % % |` の途中・その小節まで書いていないパート。そこは残して伝える。
- 反復の小節線（`|:` `:|` `:|:`・回数つき）は第1243。**ending とナビ記号は第1254**:
  - ending は同じ小節を囲む: 開く小節線の後ろに `[N.`、閉じる小節線の前に `]`（その線が `:|` なら `]` は書かない＝`:|` が閉じる）。開く線と閉じる線が、そのパート自身の小節線で、同じブロックに在るときだけ書く。section の頭で開く ending は書かない（後ろに書く線が無い）＝伝える。
  - ナビ記号は同じ小節線に: 文字（`fine` `to coda` `dc` `ds` …）はその小節を閉じる線の前、記号（`segno` `coda`）はその小節が始まる線の後ろ（section の最初の小節なら頭）。
  - 1 つの小節線に書くものは 1 つの編集にまとめる（線・その前・その後ろ）＝エディタの適用順に依らない。
  - 逆向きは括弧と記号だけを消す（ending の中の音は残る）。食い違っているもの（LYS1070）はどちら向きでも触らない。
  - ending とナビ記号の LYS1070 にも quick fix（`Editing.RepeatSignFixes`）: 相手の番号をここに／この番号を相手に／ここの括弧を外す（長さが違う ending は括弧を外すだけ）。記号は相手のをここに／これを相手に／ここのを消す。
  - ⚠️ 途中で見つけた欠陥（第1254 で直した）: `SectionSigns` が section の頭の記号（`section A { segno c1 | … }`）を 1 小節遅れに置いていた（ブロックの見出しのトークンを「記号の前に在る音」と読んだ）＝列の上で `ds` が 2 小節目に戻っていた。MIDI は tick の列なので正しかった。網 `ASignThatOpensTheSectionStandsOnItsFirstBarLine`。
- 同じ道具で「同じ小節を別のパートで開く」（`Editing.SameBarFinder`・Ctrl+Alt+PgDn/PgUp）も入れた。小節線の数え方は 3 つとも `Semantics.VoiceBarLines`。
- 「同じ小節線に違う記号」の診断（§3.2・これはエラー）と同じ計算（section の何本目の小節線がソースのどこか）を使うので、一緒に作る。

### MusicXML（第1243・2026-11-14）

- 音楽に書いた `|:` `:|` `:|:`・`:|*N`・`[N. … ]` を、**全パートの同じ小節**に書く（`SpreadRepeatSignsToEveryPart`＝書き終わったあとに小節番号で広げる・ending は `InlineVoltaSyntax` の腕）。
  網 `InlineRepeatMusicXmlTests`。それまでは書いたパートにだけ `<repeat>` が出て、`<ending>` と回数は出なかった。
- **コードの行に書いた記号**（第1244・`8f33cddd8`）: 行の `|:` `:|` `:|:`・`:|*N`・`[N. … ]` を、行の和音が入るパートの小節（リードシートなら行自身のパート）に書く（`WriteRowSigns`）。
  そのあと上の広げ方で全パートに届く。
- **音楽に書いたナビ記号**（第1244・同じ commit）: 全パートの同じ小節に `<direction>` と `<sound>` を書く（`WriteTheMusicsNavigationMarks`）。`segno`・`coda` は後ろの小節の頭、ほかは前の小節の終わり。
  掃き 1041 冊 1268 件で動いたのは 4 冊 8 件（音楽に `segno`／`coda` を書いている本）。
- 残り: `lysc import` の往復は確かめていない。小節の途中に書いたナビ記号は、`segno`・`coda` なら次の小節、ほかはその小節に付く（§3.6 はエラーにすると言うが、診断はまだない）。

### `.ly`（第1244・2026-11-14・`c9ff89900`）

- 1 つの声部に書いた `|:` `:|` `:|:`・`:|*N`・`[N. … ]` を、**全部のパートとコードのトラック**の同じ小節線に書く（`LilyPondExporter.RepeatSigns.cs`・`WriteTheScoresRepeatSigns`）。
  LilyPond は 1 段の `\repeat volta` を全段に刷るので絵は前から揃っていたが、声部はそれぞれ別の式で、`\repeat` の無い声部は `\unfoldRepeats` と MIDI でまっすぐ鳴っていた。
- 置き方: 記号は「section の頭から数えて何小節のあとか」で置く（`VoiceBarLines`＝LYS1070 と同じ数え方）。パートが書いた小節線・足りない小節の埋め草・section を書かないパートの埋め草・コードのトラックの小節に置ける。
  `|:` は素の `|` と入れ替え、`:|` と `:|:` は `|` の後ろに足す（空の小節を消さないため）。ending はその声部の小節を包む（`SpreadEndingMarker`＝元のノードのまま持つ）。
- **全部置けるときだけ書く**（`CarriesTheScoresSigns`）: 記号の小節線が phrase や `R1*4` の中にあるパートは、どの記号も受け取らず、警告を出す。`:|` だけが入ると「曲頭からの反復」になるため（探り譜 `t-phrase` で実際にそうなった）。
- コードの行が自分で書いた ending（平らな `[1.` と `]`）も同じ経路で `\alternative` になる（それまで `.ly` は行の ending を落としていた）。
- 一緒に直した既存の欠陥: `[2. g f e d ] | c1 |` の `]` の後の `|` が空小節（`s1 |`）になっていた。最後の ending の小節はその `|` が閉じる。
- 確かめ: 網 `InlineRepeatLilyPondTests` 10 本（毒 6 本とも赤）。探り譜 12 冊（Lab `p1244/probe`）を LilyPond 2.26.0 に通して警告 0（そのままと `\unfoldRepeats` の両方）。掃き 1041 冊 1268 件は差 0。
- 残り: LilyPond の絵と Lily# の絵の突き合わせはしていない。ナビ記号は書いた声部にだけ出る（`\segnoMark`・`\jump`・`\codaMark` は Score のものなので絵は出るが、未確認）。
  form の ending（`[1. A]`）の中の section に、別の声部の ending が掛かる形は未確認。
