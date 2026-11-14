# 反復とナビ記号を section の音楽にも書く（設計・未実装）

2026-11-14・第1243。オーナーの提案から始まった設計。**まだ 1 行も実装していない。**
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
| `| |:`・`| break |:` が空小節を作った（第303 の実測: 72 冊・87 箇所） | `|:` は直前の `|` と重なる（§3.4） |
| 対の判定が書かれた文面ではできなかった | 文面では判定しない。組み立てた列で判定する（§3.3）。ページの collector は今もこの形で判定している（`RepeatPairingValidator` の注・`UnpairedRepeatWarnings`） |

## 3. 規則

### 3.1 書ける場所

section の中の音楽（パートの本体と `chords` の行）に、`|:` `:|` `:|:` `:|*N` `[N. … ]` と
`segno` `coda` `fine` `to coda` `dc` `ds`（`al fine`・`al coda`）を書ける。form にも今までどおり書ける。

- **phrase の中は書けないまま**（LYS1034 が残る）。phrase は使い回す部品で、置かれる場所ごとに反復の意味が変わる。
- `lyrics` の行は今までどおり対象外（`verse N { }` は別の構文）。
- section の無いファイル: パートの音楽は section の中にしか書けないので（`part mel { c'4 … }` はエラー）、section の無い形は
  **平らに書いた chords トラックだけ**（`chords X { C | G7 | }`・by-part でないファイル）。そこにも同じ記号を書ける（オーナーの指摘 2026-11-14 で Q1 は閉じた）。

### 3.2 反復は score のもの

記号は「section の頭から数えて何本目の小節線か」に付く。section のどのパートに書いても全パートに効く。

- 書かなかったパートは従う（コードの行にだけ書けば旋律も反復する）。
- 複数のパートが書いたら合わせて読む。同じ小節線に違う記号が立ったらエラー。
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

### 3.4 `|:` と直前の小節線

`|:` は小節線の一種で、直前の `|` と重なる（`C | |: F` の小節は 2 つ。空小節はできない）。
`| break |:` も同じ。`:| |:` は `:|:` と同じ。空小節が欲しいときは `| | |:` と書く。
第303 が測って見送った「rule 2」に当たる（当時の実測で表現力は減らない）。

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
| ページ（`MeasureCollector`） | **§3.2・§3.3 をもう満たす。** 片方のパートにだけ書いた反復・ending が全段に出る。`|:` を A に、`:|` を B に書いても対になる。コードの行に書いた反復は譜表に出る。ナビ記号 4 種も描く | §3.4（`| |:` は今は空小節を 1 つ作る）と、パートどうしが食い違うときの診断 |
| MIDI（`MidiExporter.ProcessSequence`／`ProcessRepeatSpan`） | **書いたパートだけ**反復する（2 パートで 25 音・正しくは 27）。section をまたぐ対は反復しない。コードの行の反復は誰も反復しない。ナビ記号はたどらない | 一番遠い。この歩きは 1 つの section の 1 パートしか見えない（同ファイル :2971-2985 の注）。score の小節の列から道順を取り、section の小節の範囲を全パートで鳴らす形にする |
| MusicXML（`MusicXmlExporter`） | 書いたパートにだけ `<repeat>`。`<ending>`・回数・ナビ記号は出ない。コードの行の反復は出ない | 全パートの同じ小節に barline／ending／回数／記号を書く |
| `.ly`（`LilyPondExporter.EmitInlineRepeat`） | 書いたパートにだけ `\repeat volta`・`\alternative`。ナビ記号は `\segnoMark`・`\jump`・`\codaMark` で出る | 書かなかったパートにも同じ小節で入れる（`\unfoldRepeats` と MIDI のため。絵は今も score 全体に出る） |
| タイの持ち越し・小節の補完（`PlayedOrder.Expand`・`SectionBoundaryBars`） | 未測定 | play の印を小節の範囲に細かくする |
| 歌詞の `verse N` | 未測定 | §6 Q3 |
| プレビューの増分（`MeasureCollector.Resume`） | 未測定 | 記号 1 つの打鍵が全パートの小節線を動かす |
| 読み込み（`LysWriter`）・OMR・Dettato | section に分解して書く | §6 Q2 |

オーナーの本にも前例がある: LYS1034 より前、ABC・Automatic・Beat It の 3 冊は `|:` を 1 つの section に、
`] :|` を後ろの section に書いていた（`MidiExporter.cs` :2975-2978 の注）。§3.3 はその綴りを戻す。

### 段取り

LYS1034 は**最後に**外す。エラーの本も今は描けて鳴るので、各段の網はエラーつきの本の出力で書ける。

0. ✅ 済み（上の表）。
1. `RepeatStream` と診断（§3.2〜§3.6）。form の反復も同じ列に入れ、今の form の本で答えが変わらないことを見る。
2. ページ。3. ✅ MIDI（§7）。4. タイと小節の補完。5. MusicXML。6. `.ly`（LP の絵と突き合わせる）。7. 歌詞。
8. LYS1034 を phrase だけに狭める。GRAMMAR・TUTORIAL・LEARN_FOR_LLM・COMPOSE_FOR_LLM・補完・CHANGELOG。
9. 読み込みと OMR の書き出し（Q2 の答えしだい）。

## 5. 残る懸念

- **section が単独では読めなくなる。** `|:` で終わる section は、form を見ないと反復の範囲が分からない。紙の譜面と同じなので受け入れる。
- **綴りが 2 つになる。** form と section のどちらにも書ける。読み手が 1 つなら意味は割れないが、書き出し側（Q2）は 1 つに決める。
- **同じ section を 2 回演奏する form**（`form { A A }`）で A が `|:` だけを持つと入れ子のエラーになる。エラーの文で form を名指す。

## 6. 未決（オーナーに訊く）

- ~~**Q1** section の無いファイル~~ — 閉じた（§3.1。残る形は平らな chords トラックだけで、同じ記号を読む・網 `AFlatChordsTrackWritesEndingsToo`）。
- **Q2** `lysc import`・OMR・Dettato の書き出しを section 内の綴りに替えるか。勧め: 替える。ただし本体が出てから別の段で。
- **Q3** section の一部だけが反復されるとき、歌詞の `verse N` をどう置くか。
  勧め: 絵は今のまま（段を重ねる）。歌うときは反復の中だけ「N 回目＝verse N」、外は verse 1。

## 7. 経過

### MIDI（第1243・2026-11-14・`fd6c5d0b0`）

作り: 音楽に記号を書いた本は、**刷った順に 1 回だけ歩き**、記号（form のものも音楽のものも）を時刻つきで控え、
書き出しの最後に小節線の時刻で展開する（`Midi/PlayedTimeline.cs`・`MidiExporter.PlayFormAsPrinted`・`ProcessSequence` の控え）。
道順は `FormRoute`、回数と ending は `RepeatPasses` をそのまま使う。網は `InlineRepeatMidiTests`（16 本・毒で 27 赤）。

- 全パートが従う（コードの行に書いた記号にも）。section をまたぐ対、form の `:|` が音楽の `|:` を閉じる形、`ds`／`dc`／`al fine`／`al coda` を読む。
- ⚠️ **読み手が 2 つある。** 音楽に記号を 1 つも書かない本は今までどおり `PlayForm` の歩きが鳴らす（既存の本の音は 1 つも動かしていない）。
  同じ form を 2 通りに読むので、**`PlayForm` の歩きを `PlayedTimeline` に寄せて 1 つにする**のが残り（下の差を決めてから）。
- 2 つの読み手の差（わかっている分）: ジャンプをまたぐタイは、新しい方はジャンプで切る（古い方は次に鳴る音へ運ぶ）。
  テンポ・拍子・調・ペダルは、新しい方は「刷った位置で効いていたもの」に戻す（古い方は鳴った順に引き継ぐ）。
- 残り: 反復の中に書いたナビ記号はたどらない（form と同じ）／1 つのパートの 2 つの声部が同じ記号を書くと 2 つと数える／
  歌詞は刷った位置の言葉をそのまま繰り返す（Q3）／`| |:` の空小節は MIDI もページと同じく残っている（§3.4 は段 2）。

### 途中で見つけたもの

- ✅ **コードの行が `[1. …]` とナビ記号を読む**（同便）。行の ending は入れ子にしない＝`[1.`（中身の無い `InlineVolta`）と `]`（新しい `VoltaEnd`）を
  行の項目の間に平らに置く。行の読み手は 54 ファイルあり、入れ子にすると知らない読み手が ending の中のコードを黙って落とすため。
  MIDI は読む（網 `AChordRowWritesEndingsAndJumps`）。⚠️ **ページはまだ行の ending の括弧を描かない**（コードと反復の小節線は出る・Lab `p1243/probe/i-chordvolta.lys`）。`-]` は行ではまだ読まない。
- **行の頭の `|:` が空小節を 1 つ数える**（`chords p { section A { |: C | G :| } }` は 3 小節・Lab `p1243/probe/j-rowlead.lys`）。
  数える側（`ChordNameCollector.CountBars`）は「どの小節線も 1 小節を閉じる」、鳴らす側（`MidiExporter.PlayChordRow`）は「`|:` は待っている小節だけを閉じる」で食い違っている。§3.4 と同じ直し。
- **§3.4 の住所**（`|:` が空小節を作る規則の写し）: `MeasureBuilder.HandleBarline`（`_atScopeStart`）・`MidiExporter.ProcessSequence`（`pairsHere`）・
  `MusicXmlExporter`（:3029 `pairsHere`）・`LilyPondExporter.Music`（:332 `pairsHere`）・`Semantics.MeasureModel`（:256）・小節を数える `WalkBars`／`WalkBarsRed`・行の `CountBars`／`ProcessRun`。全部を 1 便で替える。
- **form の無い by-part の本で、2 つ目のパートが 1 つ目の後に鳴る**（`part mel { section A { … } } part bas { section A { … } }`・form なし。
  Lab `p1243/probe/h-plain.lys`: bas の最初の音が 8 拍目）。反復とは無関係の既存の欠陥。`form { A }` を書けば揃う。未着手。
