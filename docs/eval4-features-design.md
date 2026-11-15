# 評価 4 の「欠けている機能」— 設計

オーナー決定 2026-11-15（第1264）: 案 10 件（Lab `sessions/p1227/proposals-eval4.md`）のうち
**作る**＝6・7・8-1・2・4、**後で**＝3・1・10、**見送り**＝5・8-2〜4・9。着手の順は
6 → 7 → 8-1 → 2 → 4（6・7・8-1・2 は済み）。この文書は機能ごとの設計と、作った後の実際の形。

## 6. 符頭の形の区間 `c'8@notehead(cross) d' e' f'@!notehead`（第1269 で実装）

- **背景**: 語りの区間（× の符頭）に `@notehead(cross)` を音ごとに書いていた（評価の合唱曲で 56 回）。
- **綴り**: 最初の音に `@notehead(STYLE)`、最後の音に引数なしの `@!notehead`。STYLE は今ある 6 語
  （`x`・`cross`・`diamond`・`triangle`・`slash`・`xcircle`）。終わりに形は書かない（`@!` は引数を取らない＝パーサーの規則）。
- **規則は印の区間（`@staccato … @!staccato`・2026-11-12）と同じ 1 つ**＝`Semantics.ArticulationRuns` が構文の上で読む:
  終わりは、自分の音楽（1 パートの 1 section・1 phrase・`voice { } { }` の 1 本）の中で、まだ閉じていない
  **いちばん近い** `@notehead(STYLE)` を閉じる／開きの無い終わりはエラー LYS4018／休符・装飾音・別の声部・
  区間の中で呼んだ phrase の音には掛からない（phrase の呼び出しは警告）／終わりの無い `@notehead(…)` は今までどおり 1 音。
- **重なり**: 区間の中の音が自分の `@notehead(…)` を持てばそれが勝つ。`@dead` も勝つ（× のまま）。
  2 つの区間の中の音は内側の形。印の区間と符頭の区間は名前で見分けるので、入れ子も交差も自由。
- **読み手**（どれも「その音に書いてあったのと同じ」に読む）:
  ページ＝`MeasureCollector.ExtractNoteheadStyle`（`ArticulationRuns.CoveringNotehead`）／
  MusicXML＝`NoteheadFromMarks(WithCovering(…))`（単音・和音・`<< >>` の中の音）／
  `.ly`＝`SplitAttachments(WithCovering(…))` が音ごとに `\once \override NoteHead.style`／
  `lysc review`＝`CompositionChecks.UnpitchedHeads`（× の区間は和声の音に数えない）／
  エディタ＝`@!` の後の補完に `notehead`（`MusicMarkItem.IsSpanEndName`）。
- **音は変わらない**（MIDI は形のない譜と同じ）。
- **一緒に直したもの**: `lysc ly` が区間の終わり（`@!staccato` も）を "dropped (out of scope)" と報告していた／
  `<< c@notehead(cross) e g >>` の中の音の形をページが描いていなかった（MusicXML は書いていた）。
- ⚠️ **作っていないもの＝歌い手**: 案の文面は「歌い手はこの区間を歌わない（語り）」。今は × の符頭の音も、
  言葉があれば歌い手が音高どおりに歌う（1 音の `@notehead(cross)` も同じ＝区間の規則ではなく符頭の規則）。
  入れるなら `MidiExporter.SingLyrics`（× の音の言葉を声にしない）と `SungVoices.Read`（× の音で melisma を切る）の
  2 か所で、耳で確かめるのはオーナー。ステップのキーは形（引数）を回すだけで、終わりは動かない。

## 7. `condensedStaff` / `combinedStaff` の名前（第1270 で実装）

- **背景**: 何パートかを 1 段にまとめた譜表には名前を書く場所がなかった（`condensedStaff "Tpts 1-2" { }` は構文エラー）＝
  ビッグバンドの総譜の段に楽器名が出ない。
- **綴り**: キーワード（と音部記号の語）の後、`{` の前に文字列を 1 つか 2 つ。
  `condensedStaff "Tpts 1-2" "Tpts" { tp1 tp2 }`・`combinedStaff bass "Vc. / Cb." { vc cb }`。
  1 つ目が最初の段の名前、2 つ目が 2 段目からの短い名前＝`part vn1 "Violin I" "Vn. I"` と同じ並び。
  順は `staff bass x "Name"` と同じく音部記号が先（音部記号の語は、後ろに `{` か文字列が来るときだけ音部記号）。
- **書かないときは今のまま無名**（オーナー決定 2026-11-15）。パートの名前を "/" でつなぐ既定は作らない。
  2 段以上の総譜でほかの譜表が既定の名前（パート名）を出すときも、まとめた譜表は書いた名前しか出さない。
- **文字列が 1 つ**なら 2 段目からは名前なし（1 つしか書いていないパートと同じ）。`""` は名前なし。
- **読み手**: 構文＝`CondensedStaffRenderSyntax.NameToken`／`ShortNameToken`（combined も同じ 1 つの読み
  `SharedStaffNames`）／ページ＝`RenderSpecParser.ParseCondensedStaff`／`ParseCombinedStaff` が spec の
  `InstrumentName`・`ShortInstrumentName` に入れ、`RenderSpec.OneStaff` が `Staff` に渡す（最初の段の字下げも
  ほかの譜表の名前と同じ計算に入る）／`.ly`＝`\new Staff \with { instrumentName = … shortInstrumentName = … }`。
- **エディタ**: `condensedStaff ▮` の補完に名前の項目（`"name" { }`）。ステップのキーで group に変えると、
  group は名前を持てないので名前は消え、ステータスバーがそう言う（condensed ⇄ combined は名前を保つ）。
- **音は変わらない**（MIDI は名前のない譜と同じ）。
- ⚠️ **作っていないもの＝MusicXML**: まとめた譜表のパートは MusicXML では今も別々の `<part>` で、譜表の名前を
  載せる場所がない（`<part-group>` の `<group-name>` が候補だが、読み手は part-group を staffGroup として読み戻す）。

## 8-1. `:|*4` に "4×" を刷る（form は 2026-11-13 に済み・音楽の中は第1271）

- **案を書いた時点で半分は在った**: form の `|: A :|*4` は 2026-11-13（評価 4 の浄書の便）から閉じる小節の上に "4×" を刷り、
  MusicXML は `<repeat direction="backward" times="4"/>` を書く（`MusicMarkType.RepeatCount`・`MeasureCollector.AddRepeatCountMark`・
  網 `GrammarEvaluation4Tests.ARepeatPlayedMoreThanTwice_PrintsItsCountOverTheClosingBar`）。字は "4x" ではなく "4×"。
- **残っていたもの＝音楽に書いた反復**（2026-11-14〜15 の「反復記号を音楽にも書く」より前に form の分だけ入った）:
  `section A { m { |: c'1 | d'1 :|*4 } }` は 4 回鳴り MusicXML も `times="4"` だが、ページは何も言わなかった。
- **作った形**: 音楽の歩き（`MeasureCollector.MusicWalk` の小節線）が `:|*N`（N > 2）で同じ印を足す
  （`AddRepeatCountMarkOfTheMusic`）。置き場所は form の印と同じ（閉じた小節の上・右端が小節線）。
- **スコアの印は 1 小節に 1 つ**（`HasScoreMarkAt`）: 反復は score のものなので、何パートが書いても 1 回。描かないパートが
  書いた分は harvest（`HarvestOmittedStructure`・`IsStructuralMark` に `RepeatCount`）が描くパートの譜へ運ぶ。
- **コードの行も同じ**: `chords` の行にだけ書いた `:|*N` の回数とナビ記号（`fine`・`segno` …）は、反復し跳ぶのにページに出て
  いなかった。行の歩き（`ChordNameCollector.CollectPart`）が `RowScoreMarks` に出し、collector がパートの印の無い小節にだけ足す
  （行の ending の括弧 `RowEndings` と同じ形）。置く小節はパートの規則と同じ（記号は開く小節・文字は閉じた小節）。
- **一緒に直したもの**: 2 つのパートが同じ小節線に書いたナビ記号（`fine`・`segno` …＝「Write Repeat Signs in Every Part」が
  書く形）が 2 つ重ねて刷られていた。同じ規則（1 小節に 1 つ）で 1 回になった。
- LilyPond は `\repeat volta 4` に何も刷らない＝LILYSHARP-OWN（双子の絵に回数は無い）。音は変わらない。

## 2. `transposition` に音名＋`range`（第1272 で実装）

- **背景**: `transposition` は `8va`／`8vb`／`15ma`／`15mb` だけ＝オクターブしか言えず、preset の移調を**置き換える**。
  preset にない移調楽器（B♭ のバスクラ＝長 9 度・アルトフルート・コールアングレ）は書けなかった
  （評価 4 の書き手は `instrument clarinet` のまま 1 オクターブ上の音を聞いた）。音域の検査（LYS1052）も preset の表だけ。

### `transposition PITCH`

- **綴り**: 今の 4 語に加えて**音名**＝文字 a–g・`is`／`isis`／`es`／`eses`・オクターブの印（`transpose` の的と同じ読み）。
- **意味**: 「**書かれた中央のド（`c'`）で鳴る音**」＝LilyPond の `\transposition bes,` と同じ。音名は絶対の読み
  （裸の `c` は C3・`c'` が中央のド＝2026-10-28 からの音楽の中の読みと同じ）。
  `bes`＝B♭ 管（−2）・`bes,`＝バスクラ・テナーサックス（−14）・`a`＝A 管（−3）・`g`＝アルトフルート（−5）・
  `f`＝ホルン・コールアングレ（−7）・`ees`＝アルトサックス（−9）・`ees'`＝E♭ クラ（+3）・`c'`＝移調なし
  （preset の移調を消す語が今まで無かった）・`c`＝`8vb`・`c''`＝`8va`。
- **優先は今のまま**: 書いた `transposition` ＞ preset ＞ tuning。置き換えであって足し算ではない。
- **読み手は 1 つ**: `InstrumentDefaults.ParseTranspositionSemitones` が値の全文（`bes,`）を読む。
  `PartHeaderDefaults.TranspositionSemitones` から先は半音の数しか見ていないので、MIDI・tab のフレット・
  `pitch concert` の入力・`score … pitch transposed`／`concert` の調号・MusicXML の `<transpose>`・`.ly` は付いてくる。
  文字を自分で読んでいた 2 か所（`.ly` の `PartTransposition`・`RenderSpecParser.ResolvePartTransposition`）と
  検査（`SymbolCaseValidator`）・ステータスバーの 1 行（`NoteStepper.TranspositionSays`）を同じ読みに揃える。
- **大文字は拒む**（`Bes`）＝part header のほかの語と同じ Ordinal の規則。異名同音は区別しない（`ais`＝`bes`）。

### `range LOW HIGH`

- **綴り**: part header に音名 2 つ。`part bcl { instrument clarinet  transposition bes,  range d, bes'' }`。
- **意味**: その part の**鳴る音**の下限と上限（絶対の音名）。preset の音域を置き換える。preset の無い part にも書ける
  （`part kids { range c' g' }`＝子どもの歌を C4–G4 に収める検査）。
  ⚠️ **「書かれた音」ではなく「鳴る音」にした**（Claude の判断）: preset の表と LYS1052 の文面が「as it sounds」で、
  案の例 `range d, bes''` もバスクラの鳴る音域（D2–B♭5）。
- **誤り**: 音名が 2 つでない・音名でない・下限が上限より高い＝エラー（LYS1010・ほかの part property の値と同じ）。
- **読み手**: `PartHeaderDefaults.Range`（書いた range ＞ preset の表）を LYS1052（`InstrumentRangeValidator`）と
  コードの音の床（`ShapeChords.CloseFloorOf`）が読む。書いた range は tuning では動かさない（書いた人の数字）。
- **preset の音域は `transposition` で動かさない**（Claude の判断）: `instrument clarinet transposition bes,` で `range` を
  書かなければ、低い音に今までどおり LYS1052 が出る＝その文面に `range` を 1 言足す。動かす案（preset の「書かれた音域」を保つ）は
  `instrument piano-left transposition 8vb` のような「同じ楽器を 1 オクターブ下で」の本の警告を変えるので取らない。
- 譜面は変えない。音は `chord(…)` の項目の密集の床だけが動く（preset の音域と同じ扱い）。MusicXML・`.ly` には書かない。

### 作らないもの（残り）

- MusicXML の読み込みはオクターブでない `<transpose>` を今も「書かれた音高で読み込む」と警告する。
  `transposition` が音名を取るようになったので書けるはず＝別便。
- 補完の `transposition ▮` は 4 語のまま（説明文だけ直す）。

## 4. `key none`（第1272 で実装）

- **背景**: 無調の曲でも `score clarinet pitch transposed` は「C major → D major」として ♯ 2 つを刷った。
  調を書かない本は C major として移調される（`pitch concert` の規則）ので、調号を消す綴りが要る。
- **綴り**: `key none`。`key` が書ける所ならどこでも（ファイルの頭・section の頭・part header・音楽の中）。`time none` と対。
- **意味**: 調号を刷らない。**移調譜でも刷らない**（`score … pitch transposed`・`transpose`・`shift`）。臨時記号は音ごと
  （空の調号に対する普通の規則）。音は変わらない。
- **器は既に在った**: 文書に載っていない `key custom`（調号を音名で並べる綴り）を**音名なし**で書くと、まさにこの絵になる
  （Lab `sessions/p1272/probe4/out/cus-player.png`: B♭ クラの移調譜で調号なし・D G♯ C F♯。`key c major` と無記は ♯ 2 つ）。
  「custom の調は移調で綴り直さない・主音を持たない」が `MeasureCollector` の規則で、MusicXML は `<key />`、`.ly` は
  `\set Staff.keyAlterations = #`()`。⇒ **`key none` は「音名のない custom」の名前**＝`KeySignatureSyntax.IsNone`、
  `IsCustom` は none も含む（主音のない調を訊く 22 ファイルの読み手は 1 つの問いのまま）。
- **足すもの**: パーサー（`none` の語・後ろに音名を取らない）／`lysc review`（下）／ステータスバーとステップのキーの文面／
  補完（`key ▮` に `none`）・色付け・文書 3 冊。
- **`lysc review`**: 調を書かない本と custom の本には今も調の検査（`key.signature`・`key.outside`・`form.final-bass`）が出ない
  ＝「調を聞かない」は既に成り立つ。`key none` の本は見出しに「no key」と言い、「Heard in: …」の行（聞こえる調の推定）を出さない。
- **MusicXML の読み込み**: 調号も `<key-step>` も無い `<key>` は `key none` で戻す（今は何で戻るかを作る前に測る）。
- ⚠️ Claude の判断: `none` の後ろに音名や mode を書いたらエラー／part header の `key none` はその part だけ（ほかの part は自分の調）。

