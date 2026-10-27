# Lily# VS Code 拡張 — 手動確認リスト（2026-10-10 起票）

機械の網（拡張の 211 本・本体の 11,773 本）が見られないもの＝**実機の VS Code でしか確かめられない挙動**だけを集めた。
出典は edocs/HANDOFF.mde §1.0 ⒜「VS Code の手動確認が未」・eeditors/vscode/CHANGELOG.mde の 0.11.0 と Unreleased・
edocs/HANDOFF.mde §2 R13 ⒝・OMR の提案 B1〜B6（e..\LilySharp-Omr\docs\repro\lilysharp-proposal-2026-10-04.mde）。
**確かめたら e[ ]e を e[x]e にし、違ったものは HANDOFF §1.0 ⒜ に 1 行で起票する**（番号で指せる）。

> ⚠️ 2026-10-10 時点で入っている拡張（eyotsuda.lilysharp-0.7.0e のフォルダ名のまま・中身は 17:42 に組み直したもの）には
> 同日 18:05 以降の commit（etime 4/4 breake・PDF の font・condensed の臨時記号・検査の模型）が入っていない。
> **先に §0 で組み直してから始める。**

## 0. 準備

- [x] 拡張を組み直して入れる（eeditors/vscode/DEPLOY.mde Local Testing の手順。enpm run vscode:prepublishe → eservere と eout\extension.jse を
      e%USERPROFILE%\.vscode\extensions\yotsuda.lilysharp-*e へ）。VS Code を **Reload Window**。
- [x] 出力チャネル「Lily#」に eLanguage server version: …e の行が出て、その番号が組み直した elysc --versione と同じ（eFailed to get server versione が出ない）。
- [x] 設定の既定: elilysharp.typingAids.enablede 入・elilysharp.audition.enablede 入・elilysharp.chordShapes.includeStretche 切・
      elilysharp.preview.refreshDelaye 100・elilysharp.preview.highlightTodose 入・elilysharp.omr.verifyOnSavee 切。
- 題材は esite/videos/SHOTLIST.mde の 4 行の本（clips 1〜5 用）と、そこに載っている各 clip の本。

## 1. HANDOFF §1.0 ⒜ が「未」と名指すもの

### 1.1 ステップ Ctrl+Shift+↑ / ↓（調の音・elilysharp.stepSemitoneUpe / eDowne）と Ctrl+Shift+PgUp / PgDn（オクターブ・elilysharp.stepUpe / estepDowne）

**2026-10-15 ユーザー決定でキーが 2 組になった**: Ctrl+Shift+↑↓＝半音、Ctrl+Shift+PgUp/PgDn＝オクターブ記号（それまで Ctrl+Shift+↑↓ がオクターブ）。**2026-10-16 のユーザー決定で ↑↓ は半音ではなく調の音へ**（調号の音階・短調は導音も通る＝同日のユーザー決定）。
コード図の形はどちらの組でも動く。Ctrl+Alt+↑↓ は Lily# の割り当てではない（Windows のカーソル追加＝eauditionCore.tse の註）。
**下の項目の「↑ / ↓」は、オクターブ記号の項目では PgUp / PgDn と読む。**

- [x] **調の音**: ekey f major a'4e の eae で Ctrl+Shift+↑ → ebes'4e、もう 1 回 → ec''4e（記号が付いて音高が保たれる）。各回その音が鳴る。
- [x] ekey g major e'4e で ↑ → efis'4e。調号の無い本で ec'e ↑ → ed'e、ecis'e ↑ → ed'e、↓ → ec'e。ekey a minor g'4e ↑ → egis'4e（導音）、もう 1 回 → ea'4e。
- [x] **和音の転回**（2026-10-16 のユーザー決定）: e<c e g>e の e>e の直後で Ctrl+Shift+↑ → いちばん低い c だけが 1 オクターブ上がる（プレビューで e g c の形・e と g の高さは変わらない）。↓ → いちばん高い音が 1 オクターブ下がる。同じ位置で Ctrl+Shift+PgUp → e<c e g>'e（全体が上がる）。
- [x] relative の本で ef4 b4e の ebe で ↑ → ef4 c'4e（プレビューで c が f の上にある）。
- [x] **テンポ**（2026-10-16 のユーザー決定）: etempo "Allegro" 4 = 120e の上（etempoe から e120e までのどこでも）で Ctrl+Shift+↑ → e121e、Ctrl+Shift+PgUp → e131e、PgDn → e121e。プレビューのテンポ記号の数字も変わる。何も鳴らない。etempo swinge の上ではステータスバーに *no metronome number*。
- [x] **調**（2026-10-16 のユーザー決定）: ekey c majore の上で Ctrl+Shift+↑ → ekey g majore、もう 1 回 → ekey d majore、↓ 2 回で戻り、さらに ↓ → ekey f majore。プレビューの調号が変わる。音符は書き換わらない。ekey a minore ↑ → ekey e minore。
- [x] **同主調・平行調**: ekey c majore の上で Ctrl+Shift+PgUp → ekey c minore（同主調）、もう 1 回 → ekey c majore。PgDn → ekey a minore（平行調・調号は変わらない）、もう 1 回 → ekey c majore。タブは動かない。ekey d doriane の上ではステータスバーに *major and minor only*。
- [ ] **音部記号**（2026-10-16 のユーザー要望・2026-10-24 にユーザー決定で組み直し「↑↓ はひとつずつ・PgUp/PgDn はグループをジャンプ」「先頭は treble・Ctrl+Space の一覧と同じ順」）: 音楽中の `clef treble` の上で Ctrl+Shift+↓ → `clef treble_8` → `clef alto` → `clef tenor` → `clef bass`（`clef ` の後の Ctrl+Space の一覧と同じ順）、もう 1 回は動かずステータスバーに *already the last*。↑ で戻り、`treble` で *already the first*。プレビューの音部記号が変わり、音符は書き換わらない。PgDn → `treble` → `alto` → `bass`（族の代表）、PgUp で戻る。part の見出しの `clef`（サンプルの `part lh { clef bass octave 3 }`）では 11 個全部: `treble` `treble^8` `treble_8` `soprano` `mezzosoprano` `alto` `tenor` `baritone` `bass` `bass_8` `percussion`。見出しの `clef bass` で PgDn → `percussion`。score の `condensedStaff alto { … }` の `alto` の上で ↓ → `tenor`、↑ → `treble_8`（音楽中と同じ 5 つ・ユーザー要望 2026-10-24）。`combinedStaff` と `staff bass gt` の `bass` でも同じ。
- [x] **強弱**（2026-10-16 のユーザー決定）: ec'4@pe の epe の上で Ctrl+Shift+↑ → e@mpe → e@mfe、↓ で戻る。e@decresce の上で ↑ → e@cresce、もう 1 回は動かない、↓ → e@decresce。プレビューの記号が変わる。
- [x] **アーティキュレーション**（2026-10-16 のユーザー要望）: ec'4@downBowe の edownBowe の上で Ctrl+Shift+↑ → e@upBowe、もう 1 回は動かない（回らない＝ユーザー決定）、↓ → e@downBowe。ステータスバーは組の一覧だけ（eupBow / downBowe＝上が先・2026-10-17 のユーザー要望「大きい順」・現在の名前は繰り返さない＝ユーザー決定）。e@pullOffe ↑ → e@hammerOne。e@staccatoe ↑ → e@portatoe → e@tenutoe（止まる）、↓ で戻って e@staccatissimoe。e@fermatae ↑ → e@longFermatae。e@staccato.upe で ↑ → e@portato.upe（e.upe は残る）。PgUp / PgDn でも同じ。プレビューの記号が変わる。e@tape の上で ↑ → 音符の方が動く（ec'4@tape → ed'4@tape＝組の無い記号と e@sfze のような段の無い強弱は、書かれた音符を動かす・2026-10-17 のユーザー要望）。e<c' e' g'>4@chorde の echorde の上で ↑ → e<c'' e' g'>4@chorde（転回・e>e の上と同じ）、PgUp → e<c' e' g'>'4@chorde。ec'4@stemDowne の estemDowne の上で ↑ → e@stemUpe（プレビューの符尾が上向きになる）、もう 1 回は動かない（ステータスバー estemUp / stemDowne）。
- [x] **プレビューの記号のクリック**（2026-10-17 のユーザー報告）: プレビューのフェルマータをクリック → カレットが e@fermatae の e@e の**直後**に来る（前は e@e の直前＝音符の側で、そこで Ctrl+Shift+↑ を押すと音高が動いた）。そのまま Ctrl+Shift+↑ → e@longFermatae、音符は変わらない。強弱の emfe・e@chord(Cm7)e のクリックも同じ。音符・小節・題名のクリックは今まで通り。
- [x] **拍子**: etime 3/4e の上で ↑ → etime 4/4e。etime 6/8e で PgUp → e3/4e、PgDn → e2/4e。etime 2/2e で PgUp → e4/4e。小節線が組み直される。
- [x] **octave N**: epart lh { clef bass octave 3 }e の e3e の上で ↑ → eoctave 4e。その part の音がプレビューで 1 オクターブ上に、再生でも上で鳴る。
- [ ] **octave relative / absolute**（2026-10-17 のユーザー要望・決定「音高も一緒に直す」）: 先頭の eoctave relativee の上で Ctrl+Shift+↑ → *Convert Octaves to Absolute* が走る（情報メッセージ *octaves converted to absolute (N note(s) re-marked)*・音符の e'e/e,e が付け直されてプレビューの音は動かない・Ctrl+Z 1 回で戻る）、もう 1 回は *the octaves are already absolute*、↓ → *Convert Octaves to Relative*＝先頭の行は消えずに eoctave relativee になる（ユーザー報告 2026-10-17「行ごと消えた」の修正）。音楽の中の eoctave absolutee でも同じ（その行は消え、先頭に eoctave relativee が立つ）。epart lh { octave absolute }e は言語のエラー（モードは先頭か音楽の中）＝キーは変換せずステータスバーがそう言う。PgUp / PgDn でも同じ。
- [ ] **music フォント**（2026-10-24 のユーザー要望）: `fonts { music "Emmentaler" }` の `music` の上で Ctrl+Shift+↓ → `music "Bravura"`（ステータスバー *music "Bravura": 2 of 4 (Emmentaler, Bravura, Leland, Petaluma)*・プレビューの記号が Bravura になる）、もう 1 回 → `"Leland"` → `"Petaluma"`（止まる・*already the last*）、↑ で戻る（一覧の下へ ↓・ユーザー決定 2026-10-24）。名前の上でも同じ。PgUp / PgDn でも同じ。`lilysharp.fontPaths` に SMuFL フォントのフォルダーを足すと、その名前が一覧の後ろに加わる。`fonts { bend "TeX Gyre Heros" }` の上で ↓ → 補完の一覧で次の書体（ステータスバー *bend "…": N of M*）・プレビューのベンドの文字が変わる。
- [ ] **staff の組**（2026-10-24 のユーザー要望）: `score { grandStaff { staff rh staff lh } }` の `grandStaff` の上で Ctrl+Shift+↓ → `staffGroup`（プレビューの括弧が角括弧になる）→ `choirStaff`（小節線が譜ごとに切れる）→ `condensedStaff { rh lh }`（中の `staff` が消えて 1 段になる・改行は残る）→ `combinedStaff { rh lh }`（2 つの part が一致するところで 1 つにまとまる・a2 / Solo）、もう 1 回は動かない。part が 3 つの組では `condensedStaff` で止まり、ステータスバーに *combinedStaff takes exactly two parts*（ユーザーの問い 2026-10-24・part が 2 つのときだけ列に入れる）。↑ で `choirStaff { staff rh staff lh }` に戻る（`grandStaff` で *already the first*）。PgDn / PgUp は族を行き来する（ユーザー決定 2026-10-24）: `staffGroup` / `choirStaff` で PgDn → `condensedStaff { rh lh }`、`combinedStaff` で PgUp → `grandStaff { staff rh staff lh }`、`grandStaff` で PgUp・`condensedStaff` で PgDn は動かない（*first family* / *last family*）。`condensedStaff bass { rh lh }` で ↑ → `staff bass rh staff bass lh`。`choirStaff { staff treble melody staff treble p2 }` で ↓ → `condensedStaff treble { melody p2 }`（共通の音部記号は語の後ろへ 1 つ・ユーザー報告 2026-10-24）→ `combinedStaff treble { … }`。音部記号が違う組（`staff bass rh staff lh`）で ↓ → 動かず *the staves have different clefs*。`{ }` の中の `staff` の上では組の語は変わらない（*holds staves, not a tab*）。
- [ ] **condensedStaff の補完**（2026-10-24 のユーザー要望）: `score { ` で `cond` と打って補完から `condensedStaff` を選ぶ → `condensedStaff ` が入り、すぐ候補が開く（`{ }`・`treble`・`treble_8`・`alto`・`tenor`・`bass`）。`treble` を選ぶ → `condensedStaff treble {` / 字下げした `▮` の行 / `}` の 3 行になり（ユーザー要望 2026-10-24）、中で part 名の候補が開く。`{ }` を選ぶ → 同じ形で音部記号なし。中身が書いてある `condensedStaff ▮ { melody p2 }` の `▮` で Ctrl+Space → `{ }` は出ず、`treble` を選ぶと語だけが入る（`condensedStaff treble { melody p2 }`・空の `{ }` が増えない＝ユーザー報告 2026-10-24）。`combinedStaff` と、`grandStaff { }` の中でも同じ。中身の書いてある `▮ { staff treble melody … }` の `▮` で補完から `grandStaff`（`staffGroup`・`choirStaff`）を選ぶ → 語だけが入り、空の `{ }` が増えない（ユーザー報告 2026-10-24）。同じ位置で `condensedStaff` を選ぶ → `condensedStaff treble {` / `melody` / `p2` / `}`（行の `staff treble` が消え、共通の音部記号が語の後ろに 1 つ・Ctrl+Z 1 回で戻る・ユーザー要望 2026-10-24）。音部記号が違う行（`staff bass p2`）があると語だけが入る。
- [ ] **小節線の種類**（2026-10-24 のユーザー要望）: `c'1 | d'1` の `|` の上で Ctrl+Shift+↓ → `||`（プレビューが複縦線になる）→ `|.`（終止線）→ `!`（点線）、もう 1 回は動かない、↑ で戻る。`|:` `:|` の上では何も変わらない。`c'1|d'1` のように音符に接したカーソルでは音符の方が動く。`form { A | B }` の `|` では form の並び（ユーザー決定 2026-10-24）: ↓ → `||` → `|:`（*close it with :| in the form*・`:|` が無ければ下線）→ `:|` → `:|:` → `|.` → `!`、↑ で戻り、`|` に戻すと *a plain divider - a form draws no bar line here*。`form { ` の Ctrl+Space でも section 名のすぐ後に同じ順（`||` `|:` `:|` `:|*3` `:|:` `|.` `!`・`:|*3` は `:|` の直後）。`:|*3` を別の記号にすると `*3` も消える。`:|*3` の `3` の上で ↑ → `:|*4`（*plays it 4 times*）、↓ で減り `:|*2` で止まる（ユーザー要望 2026-10-24）・ステータスバーに *:|*3 closes the repeat - it plays 3 times*。`|: A | B :|` の中でも同じ。音楽の中の `!` で ↓ は動かない。
- [ ] **選んだ長さ・選んだ和音名**（2026-10-24 のユーザー要望「全部入れて」）: `c'4` の `4` だけを選んで Ctrl+Shift+↑ → `c'2`（選択が `2` に残り、続けて ↑ → `c'1`）、↓ → `c'8` → `c'16`、`4.` は付点が残る。選ばずにカーソルだけなら従来どおり音高が動く。chords の `C` を選んで ↓ → `Cm` → `C7` …（`F/A` は `/A` が残る）、`@chord(Cm7)` の `Cm7` を選んでも同じ。選ばずにカーソルだけなら従来どおりダイアグラムの形が動く。
- [ ] **form の section・sings の part**（2026-10-24 のユーザー要望「全部入れて」）: `form { A B }` の `B` の上で ↓ → 宣言順で次の section（プレビューの曲の並びが変わる）、↑ で戻る、`~B` は `~` が残る。`lyrics v sings sop { … }` の `sop` で ↓ → 次の part。
- [ ] **音楽の中の数**（2026-10-24 のユーザー要望「全部入れて」）: `tuplet 3/2 { … }` の `3/2` で ↑ → `4/3` → `5/4`（括弧の数字が変わる）、↓ で戻り `2/3` で止まる。`repeat tremolo 4 { c'16 }` の `4` で ↑ → `5`。`R1*4` の `4` で ↑ → `R1*5`（休みの小節が増える）、`1` で止まる。`partial 4` の `4` で ↑ → `partial 2`（弱起が長くなる）、`4.` は ↓ → `8.`。
- [ ] **値の語**（2026-10-24 のユーザー要望「全部入れて」）: `part sax { instrument alto-sax }` の `alto-sax` で ↓ ↑ → 補完の一覧の隣のプリセット、その音で 1 音鳴る（手で打ち直しても鳴る）。弦のあるプリセット（`instrument ukulele`・`guitar`・`bass`・`violin`・`banjo` …）は、補完で選んでも ↑↓ でも打ち直しても `tuning` と同じく開放弦が弦の順にポロロンと鳴る（ukulele は G4 C4 E4 A4・ユーザー要望 2026-10-24）。`piano-right` は C4 E4 G4 B4、`piano-left` は C2 G2 B2 E3 の Cmaj7 が同時に鳴る（ユーザー要望 2026-10-24「単音はさみしい」）。`flute` などは 1 音のまま。`transposition 8va` → `8vb`、`pedal`、part の `pitch written` ⇄ `concert`、`score full pitch transposed` ⇄ `concert`。`layout { accidentals default }` → `modern` …、`barNumbers lines` → … → `every 4`（数の上で ↑ → `every 5`）、`paper { size a4 }` → 次の用紙、`tab gt as numbers` ⇄ `full`、`chords c as roman` ⇄ `names`、`fonts { chord as serif }` ⇄ `sans`。いずれも端で止まり、一覧に無い語の上では何もしない。
- [ ] **as の選択子**（2026-10-24 のユーザー要望）: `staff melody as lines 1` の `lines` の上で Ctrl+Shift+↓ → `as removeEmpty true` → `as pitch transposed`（止まる）、↑ で戻る。`removeEmpty` の値の上で ↓ → `true` → `all` → `false`、`pitch` の値で `transposed` ⇄ `concert`。`as lines 1 removeEmpty true` の `lines` で ↓ → `removeEmpty` は書いてあるので飛ばして `pitch transposed`。ossia の行では `pitch` に行かない。
- [ ] **as lines N**（2026-10-24 のユーザー要望）: `score { staff melody as lines 1 }` の `1` の上で Ctrl+Shift+↑ → `as lines 2`（プレビューの譜が 2 線になる）→ … → `5`（止まる・*already the most*）、↓ で戻り `1` で止まる。PgUp / PgDn でも同じ。
- [ ] **score の part 名**（2026-10-24 のユーザー要望）: `part melody { }` `part bass { }` のある本の `score { staff melody }` の `melody` の上で Ctrl+Shift+↓ → `staff bass`（ステータスバー *bass: part 2 of N*・プレビューの段が bass の音になる）、↑ で戻る、先頭で ↑ は動かない（*already the first*）。section の中だけで書いた part（`drums { … }`）も一覧に入る。`tab` の part、`condensedStaff { … }` の中の名前でも同じ。
- [ ] **staff / tab**（2026-10-24 のユーザー要望）: `score { staff gt }` の `staff` の上で Ctrl+Shift+↑ → `tab gt`（プレビューが tab 譜になる）、もう 1 回 → `staff gt`。`staff bass gt` → `tab gt`（音部記号の語は落ちる）、`tab bass4 gt` → `staff gt`。PgUp / PgDn でも同じ。
- [ ] **% の小節数**（2026-10-24 のユーザー要望）: `c'4 d' e' f' | g'1 | a'1 | % |` の `%` の上で Ctrl+Shift+↑ → `%2`（ステータスバー *%2: repeats the 2 bars before it*・プレビューが 2 小節の反復になる）→ `%3`、↓ で `%2` → `%`、もう 1 回は動かない（*already one bar*）。PgUp / PgDn でも同じ。
- [ ] **fonts の size / step**（2026-10-24 のユーザー要望）: `fonts { lyrics "TeX Gyre Schola" size 2.2 }` の `2.2`（または `size`）の上で Ctrl+Shift+↑ → `size 2.3`（ステータスバー *size 2.3: staff spaces*・プレビューの歌詞が大きくなる）、↓ で戻る、PgUp → `3.2`。`20` で ↑ は動かない（*already the largest*）。`lyrics step +1` の上で ↑ → `step +2`、↓ ↓ → `step 0` → `step -1`。
- [ ] **midiInstrument**（2026-10-24 のユーザー要望）: プレビューを開いて `part m { midiInstrument "acoustic grand" }` の上で Ctrl+Shift+↓ → `"bright acoustic"`、その音色で 1 音鳴る（ステータスバー *program 2 of 128*）。ギター系（`"acoustic guitar (steel)"` …）・ベース系（`"slap bass 1"` …・一弦ずつ）・`"violin"` `"viola"` `"cello"` `"contrabass"` `"banjo"` は開放弦がポロロンと鳴る（ユーザー要望 2026-10-24）、`"synth bass 1"` は 1 音、↑ で戻る（一覧の上が program 1＝↓ で下へ・ユーザー決定）。PgDn → `"celesta"`（8 つ先の族）、PgUp で戻る。名前を手で `"viola"` に打ち直す・補完で選ぶ → 打ち終えて少しすると viola の音が鳴る（`"viol"` の途中では鳴らない）。カーソルが行を通るだけでは鳴らない。
- [ ] **tuning**（2026-10-24 のユーザー要望）: プレビューを開いて `part b { tuning bass }` の `bass` の上で Ctrl+Shift+↓ → `bassDropD`、開放弦 D1 A1 D2 G2 が弦の順に**一弦ずつ**鳴る（前の弦は次の弦が鳴ると止まり、和音にならない＝ユーザー要望 2026-10-24「低い音の和音は濁る」・`instrument bass` も同じ・MIDI 出力でも SoundFont でも発振器でも）。弦の間はギターやウクレレと同じ 90 ms（200 ms は遅いとユーザー判断・同日）。ギターやウクレレは鳴り重なっていく。`tuning ukulele` では 4 弦の高い G4 が先で G4 C4 E4 A4（3 弦の C4 より高い音から＝ユーザー報告 2026-10-24）、banjo は 5 弦の高い g が先。**プレビューを別のタブの裏に隠した状態でも**補完で `ukulele` を選ぶと 4 本が 90 ms おきに鳴り、1 本の後に残りが和音でまとめて鳴らない（ユーザー報告 2026-10-24・SoundFont・発振器・MIDI 出力の 3 通りで）（ステータスバー *tuning bassDropD: D1 A1 D2 G2*）・tab の数字が付け直される。↑ → `bass` → `guitarAsus4`（一覧の下へ ↓・ユーザー決定 2026-10-24）。PgDn → `violin`（族の先頭）、`bassDropD` で PgUp → `bass`、もう一度 → `guitar`。`bass4` の上で ↓ → `bassDropD`。手で `cello` と打つ → 少しして C2 G2 D3 A3 が鳴る。カーソルが通るだけでは鳴らない。`part b { tuning ` の直後で Ctrl+Space → 調弦の一覧（guitar から族ごと・各行に開放弦・`bass4` は *the same strings as bass*）、選ぶと入って開放弦が鳴る。プロパティの一覧から `tuning` を選ぶとすぐこの一覧が開く。
- [ ] **キャレットのステータス行**（2026-10-24 のユーザー要望・ホバーは画面を隠すのでステータスバーへ）: プレビューを開かずに、`clef bass` の `bass` にキャレットを置く → 少しして左下に *clef bass: middle line D3 · a clef moves no note - octave or instrument sets the register*。`tuning ukulele` → *4 G4 · 3 C4 · 2 E4 · 1 A4 - re-entrant: string 4 sounds above string 3*。`key d minor`・`octave 3`・`transposition 8vb`・`midiInstrument "…"`・`instrument bass`・`grandStaff`・form の `:|*3`（*plays 3 times*）・`paper { size a4 }` も 1 行ずつ。選択中は消える。音符の上では *bar 2 · beat 2 + 1/2 · B4*（score に 2 パート以上ならパート名が先頭）、`instrument clarinet` のパートでは *D4 sounds C4*、`instrument bass` では *E2 sounds E1*、和音は全部の音、partial の音は *bar 0 · beat 4*（ユーザー要望 2026-10-24）。休符の上では消える。`form { A B A }` の 2 つ目の `A` → *section A: 2 bars · the 2nd of 2 times this form names it*。`time 6/8` → *2 beats of a dotted quarter (6 eighths a bar)*、`tempo 4 = 90` → *a quarter lasts 0.67 s*、`tempo "Allegro"` だけ → *printed only*、`@mf` → *moderately loud · plays at velocity …*、`@staccato` → *short, detached*、`@rit` … `@!rit`（ユーザー要望 2026-10-24）。キャレットを動かし続けても、ちらつかない。
- [ ] **AI 候補の試聴**（2026-10-25 のユーザー要望）: プレビューを開き、一度クリックして音を有効にしてから、数小節を選んで Ctrl+I → 候補パネルの **▶ Play**（または Space）→ プレビューの音源で候補（After）の曲が頭から鳴る。Tab で Before に切り替えて ▶ Play → 元の曲が鳴る。■ Stop で止まる。Accept・Ask Again…・Reject でも止まる。プレビューの楽譜の音符は光らず、**候補パネルの楽譜の鳴っている音符がオレンジに光る**（鳴っている側の After／Before・新しい音符が画面の外ならスクロール・止めると消える＝ユーザー要望 2026-10-25）。SoundFont・MIDI 出力のどちらでも。プレビューを閉じて ▶ Play → パネルに *Open the preview of this file to hear it*。score を選んでいるプレビューでは、その score の形で鳴る。
- [ ] **Ctrl+I の作業中の表示**（2026-10-24 のユーザー報告「指示できたのか分からない」、2026-10-25 に通知へ）: 数小節を選んで Ctrl+I → 指示を入力して Enter → すぐ右下に通知が**1 つだけ**出て（2 つ続けて出ていた＝ユーザー報告 2026-10-25）、題は *Lily#: Ask AI via <モデル>*、文は段階が先で依頼文が後ろ（*reading the score… — “…”* → *generating…* → *validating the candidate…* → *rendering the candidate…*・60 字を超える依頼は「…」で切る）、候補パネルが開くときに閉じる（パネルの上に通知が残らない）。通知の Cancel で中断でき、何も変わらない。エディターには塗りも砂時計も出ない（AI の変更は範囲の外に及ぶこともあるので塗りはやめた＝ユーザー決定）。Ask Again… で直しを頼むと、また通知が 1 つ出て、新しい候補は**同じペイン**に出る（別のペインが開いていた＝ユーザー報告 2026-10-25）。何も選ばずに**空行**で Ctrl+I → 入力欄の題が *Lily# — Ask AI*、文は *What should the AI change?*（短く・ユーザー要望 2026-10-25）、通知の題は *Lily#: Ask AI (whole score) via <モデル>*（全体が対象・ユーザー要望 2026-10-24）、候補は全体の書き直しとして出る。音符の行で押せばその行、選択があれば選択（これまでどおり）。
- [ ] **instrument の補完の説明**（2026-10-24 のユーザー要望）: `part b { instrument ` の直後で Ctrl+Space → 行の右には楽器の名前だけ（`alto-sax` なら *Alto Saxophone*・譜表の左に出る名前＝ユーザー要望 2026-10-24）。もう一度 Ctrl+Space で開く詳細欄には、その名前の下に、決める設定が 1 行ずつ、clef → midiInstrument → octave → tuning → transposition の順（*clef bass* / *midiInstrument "electric bass (finger)"* / *octave 3 — a bare c is written C3* / *tuning bass — E1 A1 D2 G2* / *transposition 8vb — written C3 sounds C2*・中黒で連結せず改行・どのプリセットにもある行が上、少ないものが下で、候補を ↓ で替えても同じ行に同じ項目＝ユーザー要望 2026-10-24）。選ぶ前に違いが分かる。
- [ ] **instrument のホバー**（2026-10-24 のユーザー要望）: `part b { instrument bass }` の `bass`（または `instrument`）にマウスを載せる → **Instrument preset bass** の下に `clef bass`・`midiInstrument "electric bass (finger)"`・`octave 3`（a bare `c` is written C3）・`tuning bass` E1 A1 D2 G2・*`transposition 8vb` — sounds 12 semitones lower: written C3 sounds C2* の 5 行（この順・補完とステータスバーも同じ順＝ユーザー要望 2026-10-24）、どの行も頭が揃う（3 行目だけ左に寄っていた＝ユーザー報告 2026-10-24）。譜表の左の名前（Bass）は出ない。`clef treble` を書き足すと `clef bass` の行に取り消し線と *overridden: the part sets `clef treble` itself*（`tuning bass5` を書き足せば tuning の行が同じく。上書きされたと分かる表現＝ユーザー要望 2026-10-24）。見出しは **Instrument preset bass** まで太字で、preset と bass の間は普通の空白 1 つ（ユーザー要望 2026-10-24）。`instrument clarinet` は *written C4 sounds B♭3*。
- [x] **transpose**: epart cl { transpose d }e の上で ↑ → etranspose eese（ステータスバー *up 3 semitones*）。ekey e majore の本で etranspose ce ↑ → edese（F 長調になる・ecise ではない）。PgUp → eees'e。
- [x] **shift**（2026-10-17 のユーザー要望＝曲全体の半音シフト）: ekey c majore の本の先頭に eshift 0e と書き、その上で Ctrl+Shift+↑ → eshift +1e（ステータスバー *shift +1: key c major -> des major*）。プレビューで全パートの音符と調号が D♭ 長調に、コード行の名前も D♭・A♭7 に、再生も半音上に。↓ 2 回 → eshift -1e（B 長調）。etranspose de を持つパートも一緒に動く。コード図は書いた形ではなく新しいコードの既定の形。ドラムは動かない。PgUp / PgDn でも同じ 1 半音。
- [x] 音符の上でない所で Ctrl+Shift+PgUp / PgDn → エディタのタブが左右に動く（VS Code 既定の Move Editor Left / Right in Group）。音符の上ではタブは動かず、オクターブが動く。

> ⚠️ **音はプレビューの webview が鳴らす**（estepAudition.tse の頭の註・2026-09-28 のユーザー決定）＝**その本のプレビューが開いていないと、ステップも自動の試聴も黙って何も鳴らさない**（ステップはプレビューを開かない）。Alt+P / Alt+M だけは 1 回目でプレビューを開き、2 回目で鳴る。確かめる前に eCtrl+K Ve でプレビューを横に開く。音が出ないときの切り分け: ⑴ プレビューが開いているか ⑵ Alt+P で鳴るか（鳴らなければ webview の合成器＝eAudioContexte の側・第901 からは SoundFont シンセ＝出力チャネル「Lily# Extension」の ePreview synth …e の行を見る） ⑶ 出力チャネル「Lily#」に estep:e の行が出ていないか（サーバーが Fallback を返した） ⑷ elilysharp.audition.enablede が切られていないか。サーバーの elilysharp/stepe は SHOTLIST の 4 行の本で epitches=[72]e を返す（第888 が使い捨ての試験で確認）。

- [x] **オクターブ記号**（SHOTLIST clip 3 の手順）: ec4e の上で ↑ 2 回 → ec''4e、↓ 1 回 → ec'4e。各回その音が鳴る。
- [x] 和音の中の eee で ↑ → e<c e' g>e（動くのはその音だけ・**鳴るのは和音全体**＝c e' g の 3 音・2026-10-10 のユーザー決定）。e>e の直後で ↑ → e<c e' g>'4e（和音全体が動いて鳴る）。カレットを ece eee ege と渡っても和音は 1 回しか鳴らない。
- [x] プレビューを開いた直後（まだクリックしていない）に ↑ を押すと、ステータスバーに *Lily#: click the preview once to enable its sound* が出る。プレビューを一度クリックすると、以後は鳴る。プレビューを別の列へ動かした後（webview が作り直される）も同じ案内が出て、クリックで戻る。
- [x] **コード図の形**（clip 8 の手順）: ec'1@chord(Cm7)e の eCm7e の上で ↑ → e@chord(Cm7 x35343)e、図が出て鳴る、
      ステータスバーに eCm7: shape 1 of …e。↑ を続けると形と図が変わる。↓ で戻り、最初の形の上でさらに ↓ → 形が消えて図も消える。
- [x] **開放弦の輪**: 図の開放弦（形の e0e）は **塗らない輪**（○）、弾かない弦（exe）は ×、押さえる弦は黒丸。echord(C x32010)e で確かめる。
- [x] elilysharp.chordShapes.includeStretche を入にして ↑ を続けると、切のときには出なかった広い形が候補に増える。
- [x] elayout { chordDiagrams all }e の本では、形を書いていないコード名にも既定の図が出る。その上で ↓ → 図が消える。
- [x] 形がコード名と合わない（e@chord(C x35343)e）と LYS1039 の警告。

### 1.2 入力補助（smartType: e'e e,e e.e e\e と数字）

- [x] **オクターブ記号が音名の直後に入る**（clip 1）: ecis4.e の ece と eise の間で e'e 2 回 → ecis''4.e、カレットは動かず、プレビューの音が上がる。
      e,e 1 回で e'e が 1 つ消える。ee4e の後ろで e'e → ee'4e。
- [x] **数字が音価を上書きする**（clip 2）: ed4e の直後で e8e → ed8e（ed48e ではない）。efe と e4e の間で e1e → ef1e。ecis4.e の後で e8e → ecis8.e（付点は残る）。
- [x] e\e を音符の上で打つとタブの弦番号の枠が音符の後ろに開く（ec\3e）。ec\3 de で e(e e[e を打つと弦番号を飛び越えて正しい音に付く。
- [x] elilysharp.typingAids.enablede を切ると、どのキーも素の文字になる。
- [x] 補完のポップアップが開いている間は e'e e,e が補完の操作を邪魔しない（e!suggestWidgetVisiblee）。
- [ ] **e%e の後の空白で e| e が入る**（第912・2026-10-18 のユーザー決定「正規の綴りは省略しない、でも省略しても良い」）: esection A { c d e f | %e の後ろで空白 → ec d e f | % | e でカレットは e| e の後ろ。続けて e%e 空白 e%e 空白 → e% | % | % | e。e%2e も同じ。直後に自分で e|e を打っても二重にならず、Ctrl+Z 1 回で e| e だけ戻る。e| c4 %e の後ろの空白では何も入らない。小節線を省いた e| % % % |e を書いてもプレビューは e| % | % | % |e と同じ絵で、Problems に何も出ない。

### 1.3 Split Sections to Match a Part（clip 7）と Regroup

- [x] clip 7 の本で eAe の警告をホバー → eCtrl+.e → *Split section A in the other parts to match vn1…*。
      確認の UI に計画（vn2 の A を 2 小節で A, B に割る・form は A → A B）が出て、**Apply** で vn2 が esection Ae esection Be になり、form が eA Be、警告が消える。
- [x] 同じ確認で **Cancel** → 何も変わらない。
- [x] eLily#: Regroup (by part ⇄ by section)e を往復して本が同じ絵に戻る（プレビューが動かない）。

### 1.4 SHOTLIST の 9 本（ユーザーが録る）

- [x] clip 4 audition: 矢印キーで音符に乗るたびに 1 度鳴る・打った eae が鳴る（Alt+P 長押し・Alt+M で小節）。
- [x] clip 5 preview: 打鍵ごとに譜が伸びる・カレットの音が光る・選択範囲が光る・プレビューのクリックでエディターが飛ぶ。
- [x] clip 6 quick fix: *Add 1 bar line to bass (|)* で警告が消え、埋まった小節が描かれる。
- [x] clip 9 AI transform（Copilot のサインインが要る）: 候補が並んで描かれ、After/Before が切り替わり、Accept で本が変わり Ctrl+Z で戻る。
- [x] 録った mp4 を esite/videos/NAME.mp4e に置き、ページの edata-pendinge を外す（SHOTLIST の手順）。

## 2. 0.11.0 で入ったもの（eeditors/vscode/CHANGELOG.mde 0.11.0）

### 2.1 e@todoe と元の画像（OMR の本が無くても見られる分）

- [x] ec4@todoe / ec4@todo("F♯ or F♮?")e が Problems に LYS4026 で出る。プレビューでは**符頭が人の手で描いたような赤い輪で囲まれ**（塗りではない・線は太め・始点を少し越えて閉じる・2026-10-10 のユーザー決定）、memo のあるものは**段の上の余白に小さめの赤い文字**（箱なし）で memo が出て、**音符の x から余白の広い側へ一定量ずらした所**に置かれ、**文字には下線**、輪から下線の中央へ引き出し線（輪・下線・線は同じ太さ 0.2）。e.downe の memo は文字の上の中央につなぐ（線が文字を横切らないため）。e.downe で memo が段の下。**和音は全体を 1 つの輪**、休符も輪、eR1*Ne は棒全体を輪。**プレビューでは memo の置き場が段組みに予約され、memo が頁の持つ余白に収まらないときだけ次の段と頁が下がる**（eUntitled-2.lyse は 1 空間も増えない・1 段に 6 本書いた eone.lyse は段が下がる＝第899〜900・2026-10-10 のユーザー決定「他の要素を動かしても良い・整った形に」・2026-10-11 の報告「余白が無駄に広い」）。**書き出し（PDF / PNG / SVG）の段組みは memo が無いときと同じ**（下の行）。その帯の中で **memo は他のインク（コードネーム・強弱・加線の音符・先に置いた memo）と重ならない所へ逃げる**＝音符の反対側が空いていればそちらへ回り（ページの余白も使う）、両側ふさがっていれば上（e.downe は下）へ登る（第896・ユーザー要望「重なると読めない」「音符の近くの余白に」）。**同じ段の memo は短いものから順に、全部の左右の組み合わせを試して一番安い並び**（登りが少なく・引き出し線が他の memo の文字を通らず・箱が他の線の上に乗らない）**で描く**（第898〜899）＝隣り合う 2 音に memo を書いても（ec''4@todo("first memo here") d''4@todo("second memo here")e）、1 段に 6 本書いても（Lab esessions/p896/probe/one.lyse）、どの引き出し線も他の memo の文字を通らない。**カレットをその音符に置くと輪はハイライト色の線になり、中は透過のまま**（塗られたら第892 の再発）。elilysharp.preview.highlightTodose を切ると輪・吹き出しの箱・線が消える（memo の文字は黒で残る）。
- [ ] **下の声部の memo は自動で段の下**（2026-10-17 のユーザー決定）: em { voice { c''4@todo("upper") d''4 e''4 f''4 } { c'4@todo("lower") d'4 e'4 f'4 } | }e で *upper* は段の上・*lower* は e.downe を書かなくても段の下。ec'4@todo("lower").upe なら上に戻る。ecombinedStaff { vone vtwo }e の vtwo（リズムが違い声部が分かれるとき）も同じ。
- [x] 同じ本を PDF / PNG / SVG に書き出すと、輪も吹き出しも無く、memo のぶんの余白も無い（書き出しは memo が無いときと同じ段組み）。
- [x] クイックフィックス *Resolve this TODO* / *Resolve all N TODOs* で e@todoe が消える（メモごと）。
- [x] eLily#: Show Original Scane は ex.omr.jsone の無い本では出ない（コマンドパレットの when）。
- [x] 音符の後ろで e@e → 補完から etodoe を選ぶ → e@todo("")e が入り、カレットが e""e の間に来る（etexte・emarke と同じ・2026-10-10 のユーザー要望）。そのまま Tab／Esc で抜けても e@todo("")e は情報 LYS4026（第903 から警告ではない）を出し、エラーにはならない。
- [x] ec4@todo("comment")e の e)e の後ろで e.e を打つ → 補完に edowne（先頭）と eupe が出て、選ぶと e@todo("comment").downe になる。e)e の直後で Ctrl+Space でも e.downe／e.upe が出る。e@text("dolce").e は eupe が先頭、e@fermata.e も e.e で開く。**付点の ec4.e では何も開かない**（e.e は配置のときだけ答える・2026-10-11 のユーザー要望）。⚠️ Reload Window が要る（トリガー文字はサーバの起動時に決まる）。

### 2.2 コード図まわり

- [x] elayout { chordDiagrams … { … } }e の表の中で補完が、本に出てくるコード名のうち表に無いもの → 調のコード → esectione → section 名の順に出る。
- [x] 表の中の esection NAMEe・調弦の語・形が色づき、表の e}e の後ろの elayoute の鍵も色が続く。
- [x] 同じ part を capo の違う 2 つの score が弾く本で echord(…)e をホバー → score ごとに 1 行、capo の下で鳴る音名が書かれる。

### 2.3 プレビューの Export と再生

- [x] picker を escore practicee（form が section B から始まる）にして Export → MIDI が B から始まる（elysc midi --score practicee と同じ）。
- [x] Export の PDF / PNG が選んだ score の絵（score の名前を変えた直後でも、名無しの絵にならない）。SVG にフォントが入っている。
- [x] MIDI / LilyPond の Export の警告がエディターの通知に出る。
- [x] 1 part だけの score を選んで Play → その score の part と form だけが鳴る。
- [x] form が 6 回弾く section の中で音を打つ → プレビューの 6 回全部が描き直る。

#### 2.3.1 SoundFont の音源（第901・edocs/playback-design.mde）

- [x] プレビューを開くと出力チャネル「Lily# Extension」に ePreview synth loading: …e の行が段階ごとに出る（processor を足す→synth を作る→音源を fetch）。すぐに ePreview synth ready: 約 3 MB in … ms: …gm.dlse が出る（音源は同梱しない＝既定は OS の General MIDI バンク・2026-10-12 オーナー決定。開発機で emedia/soundfont/e に音源を置いているとそちらが先に鳴るので、確かめるときは一時的に退ける）（AudioContext は最初のクリックまで suspended だが、worklet は suspended でも答える＝第901 実機 2 回目で確認。出なければプレビューを 1 回クリック）。efailede なら理由がその行にある。読み込み中に ▶ を押すと「Loading the sound font…」と出て、間に合えば音源で、8 秒で来なければ従来の音で鳴る。
- [x] **Choose Sound Font… の履歴**（2026-10-12）: Browse で 2 つの音源を順に選ぶ → もう一度開くと eDefaulte の下に 2 つ（新しい順・使用中に e(current)e）。片方のファイルを別の場所へ移してから開くと、その 1 つだけ出ない（戻すとまた出る）。🗑 を押すとその行が消え、ファイルは残る。eDefaulte を選ぶと既定の音に戻る。
- [x] ▶ で楽器の音（ピアノ・ギター等）が鳴る。epart vn { midiInstrument "violin" }e と epart kit { clef percussion }e の本で、vn はバイオリン・kit はドラムの音。
- [x] 密な本（和音の多いピアノ譜・4 part 以上）で、途切れ・歪みが無い。⏹ を押すと直ちに止まり、後から音が漏れない。▶ を押し直す（seek）と頭から正しく鳴る。
- [x] プレビューのタブを隠して（別のタブを前に出して）も再生が続く。
- [x] Alt+P を長押し → その part の楽器で鳴り続け、離すと止まる。Ctrl+Shift+↑ の後の音も同じ楽器。
- [x] 設定 UI の eLily# › Playback: Sound Fonte の説明の「choose a file…」リンク（またはパレットの **Lily#: Choose Sound Font…**）→ 「Bundled」「Windows: gm.dls」「Browse for a file…」の 3 択。gm.dls を選ぶ → 設定に path が入り、開いているプレビューの音が（貧弱に）替わる。Browse → .sf2/.sf3/.dls に絞ったダイアログ。Bundled → 設定が空に戻り、同梱の音に戻る。
- [x] 設定 elilysharp.playback.soundFonte に別の .sf2/.sf3 の path を入れる → 開いているプレビューの音が替わる（Reload 不要）。存在しない path → 出力チャネルに edoes not existe の行、音は同梱のまま。空に戻す → 同梱に戻る。
- [x] emedia\soundfonte を空にして（実験）Reload → 出力チャネルに ePreview synth nonee、▶ は従来のオシレーターの音で鳴る。
- [x] part の e{ }e の中で Ctrl+Space → emidiInstrumente を選ぶ → emidiInstrument ""e が入ってカーソルがクオートの中、そのまま 128 の楽器名（GM 順・家族名つき）が出る。eelectric guie まで打って eelectric guitar (clean)e を選ぶと e"electric guitar (clean)"e になる（e"electric electric …e にならない）。emidiInstrument e と手で打って Ctrl+Space → クオート付きの名前が入る。

### 2.4 エディターの小さな直し

- [x] part の下に書いた section header に ekeye / epartiale / etimee / etempoe を打つ → 音を触らなくてもプレビューが新しい header になる。
- [x] ekeye を途中まで打ってもプレビューが止まらない。段の調号をクリックすると、編集の後でも ekeye を書いた場所へ飛ぶ。
- [x] ekey custom fis cise の本でアウトラインとパンくずが出る。
- [x] 文字列: e"say \"hi\""e の e\"e が文字列の色のまま・e@"…"e は逐語・e"\^"e は LYS0036 の印。
- [x] section の音楽に esegnoe を書くと LYS1034 のエラー。音楽の補完に esegnoe ecodae efinee edce edse が出ない。

## 3. Unreleased（今の拡張で見られる）

### 3.1 名前の無いブロック・score の名前

- [x] escore { … }e と escore tab { … }e の本: picker に **(Default)** と **tab**。何も選んでいないとき (Default) が描かれる（tab が先に書いてあっても）。
- [x] escore "tab"e → LYS0038、クイックフィックスで escore tabe になる。escore tab "both"e → 文字列を消すフィックス。
- [x] section があって escoree の無い本 → LYS6014、クイックフィックスが score（chords の行・part ごとの staff・lyrics）を末尾に足し、本の改行に合う。
- [x] eforme / escoree のスニペットが名前の無いブロックを書く。score の中で eform |e と打つと form の一覧が補完に出る。
- [x] Export のファイル名: 名無しの score は e<本>.svge、escore tabe は e<本>-tab.svge（Explorer の batch export も同じ）。
- [x] epart bass { }e と estaff bass clicke → part bass の譜で click は MIDI だけ、LYS1046 の警告が綴りを案内する。
- [x] esection A2 "A'" { }e を top level に書くと eform { A2 }e が A' を印刷。part の中の section にラベル → LYS0039。2 つの宣言でラベルが違う → LYS1045。

### 3.2 音楽フォント（SMuFL）

- [x] efonts { music "Bravura" }e → プレビューが Bravura で描く（音部記号の形で分かる）。e"Petaluma"e・e"Leland"e も。
- [x] efonts {e の中で emusice が補完に出て、値に引用符つきで eEmmentalere eBravurae eLelande ePetalumae が出る。emusice の鍵が色づく。
- [x] emusic "NoSuchFont"e → LYS8019 の警告に探した場所が全部並ぶ。絵は Emmentaler のまま。
- [x] emusic "Petaluma" "Bravura"e の鎖で、Petaluma に無いグリフが Bravura から描かれ、警告が 1 グリフ 1 回。
- [x] Petaluma の本: 題名・コード名・リハーサル記号・e@texte・テンポ・強弱の言葉・楽器名が Petaluma Script、歌詞は彫刻の書体のまま。
- [x] Petaluma の eform { Introduction "Intro" }e の枠が文字にぴったり（WOFF2 を組み直したので **カーニングが効く**＝以前は枠からはみ出した）。
- [x] eform { Introduction "ほえほえ" }e の枠がかなの高さで、細い帯にならない。
- [x] Petaluma の etime 4/4e で分子と分母が重ならない。
- [x] elilysharp.fontPathse に自前のフォルダを書くと、そこの SMuFL フォントが名前で見つかり、**設定を変えた瞬間に開いているプレビューが描き直る**。
      eLILYSHARP_FONT_PATHe でも同じ（VS Code を環境変数つきで起動）。

### 3.3 Markdown の fence

- [x] Markdown の eeee eeelily# eeee に score が 2 つある本を丸ごと貼る → 既定の score が描かれる（拒まない）。score の無い fence は LYS6014 で拒まれる。

### 3.4 小節のクリック（第905・隔離した VS Code で CDP の実入力では確認済み）

- [x] プレビューで小節の音符の無い所（五線の上）をクリック → エディターのカレットがその段のその小節の最初の音符へ飛ぶ。歌詞・コード名の行の上なら一番近い段の小節。
      マウスを載せた小節が薄い青になり、カーソルが指になる。音符のクリックは今まで通りその音符へ、ドラッグは今まで通りスクロール。
- [x] 再生中に小節をクリック → その小節から鳴る（同じ section を 2 度書いた form なら、クリックした方の回から）。
- [x] Show Original Scan とプレビューを左右に並べる → プレビューの小節に載せるとスキャンの同じ小節（同じパート）が光る・スキャンの小節に載せるとプレビューの同じ小節が光る。どちらからマウスを外しても、相手の光が消える。
- [x] 行の末尾で ec d e re と打って空白 → ec d e r | e になる。続けて自分で e| e を打っても二重にならない。Ctrl+Z 1 回で自動の e| e だけ消える。足りない・溢れる小節、行の途中、3 連符の入った小節では入らない。
- [x] evoice { ‸ }e の中で 1 小節分打って空白 → e} { e で次の声部へ。2 つ目の声部が 1 つ目と同じ長さになって空白 → e} e で閉じる（e}e が二重にならない）。小節の途中で始まる voice、連符・装飾音の e{ }e の中では何もしない。
- [x] エディタでカレットを動かす → プレビューでカレットのある段・小節が青く光ったまま（スキャン画面の点灯と同じ）。小節の中で打ち込んでも、上に行を足しても、点灯が正しい小節について来る。小節と小節の間の空白や曲の外では消える。
- [x] .lys を閉じてプレビューだけ残す → プレビューの音符や小節をクリック → .lys がプレビューの左に（普通のタブで）開き直り、カレットがクリックした所に来る。
- [x] Show Original Scan の小節: マウスを載せた小節と caret の小節が**五線の内側だけ**同じ青で塗られ、枠線は無い。プレビューのホバーと同じ色・同じ範囲。五線の外（box の上下）のクリックでもその小節へ飛ぶ。

- [ ] **カーソルの小節の青（第973・ユーザー報告）**: evoice {e・etuplet 3/2 {e・ebreake で始まる小節の行で、カーソルを evoicee の上（音符より前・字下げの中）に置く → プレビューのその小節が青くなる（前は最初の音符まで動かす必要があった）。前の小節の中や、小節の無い行（ep1 {e だけの行）では、次の行の小節は光らない。Fix OMR Marks のパネルで evoice {e の小節の行をクリックしても同じ。

### 3.5 MIDI 出力（第911・edocs/playback-design.mde §6。Windows は網が実機の GS 音源を開いて鳴らして閉じた＝winmm は通る。**mac／Linux の CoreMIDI／ALSA は未実走**）

- [ ] パレット **Lily#: Choose MIDI Output…** → 「The preview's own synth」の下にこの機械のポート（eMicrosoft GS Wavetable Synthe・eZOOM UAC-2 MIDIe …）が並ぶ（.lys を 1 つも開いていなくてもサーバーが立って並ぶ）。GS を選ぶ → 設定 elilysharp.playback.midiOutpute に名前が入り、ステータスバーに ethe preview plays through …e。設定 UI の説明の「choose a port…」リンクでも同じ picker。
- [ ] ▶ → Windows の GS 音源で鳴り、プレビューの音源は鳴らない。追従の点灯が音に合う（ずれが分からない程度）。⏹ で直ちに止まり、音が残らない。再生中に音符をクリック → そこから鳴る。Alt+M → その小節だけ。▶ を押し直す → 頭から。
- [ ] epart vn { midiInstrument "violin" }e と epart kit { clef percussion }e の本 → vn はバイオリン・kit はドラム（ch 10）の音。四分音（ecihe）の本 → ベンドで鳴って次の音は戻る。
- [ ] Alt+P 長押し → 鳴り続けて離すと止まる（GS から・part の楽器）。Ctrl+Shift+↑ の後の音・カレットの着地・打鍵の音も同じ経路。
- [ ] loopMIDI のポートを選び、DAW（または VST ホスト）のトラックをそのポート・その part のチャンネル（1 から score の順・ドラムは 10）で受ける → DAW の音源で鳴る。▶ の頭でプログラムチェンジが届く（GM 準拠の音源なら楽器が替わる）。
- [ ] 設定を手で存在しない名前にして ▶ → ステータスバーに eMIDI output "…" is not there; this machine has: …e が 1 回、プレビューの音源で鳴る（2 回目からは出力チャネル「Lily# Extension」だけ）。
- [ ] 再生中に Choose MIDI Output… で別のポート（または The preview's own synth）に替える → 止まる。次の ▶ は新しい方で鳴る。
- [ ] 再生中に Reload Window → GS／DAW に音が残らない。
- [ ] 設定を空に戻す → プレビューの音源に戻る。
- [ ] **MIDI 出力で鳴っている音符が 1 つずつ光る**（ユーザー報告 2026-10-25「8 分音符 8 個の小節で 1〜2 個しか光らない」）: MIDI 出力を選んで 8 分音符の続く曲を ▶ → 音符が音に合わせて 1 つずつ光る（1 秒おきに飛ばない）。止めると出力チャネル「Lily# Extension」に *PREVIEW follow-along: N ticks, longest gap … ms* が 1 行＝longest gap が 50 ms 前後（1000 ms 前後なら拡張側の合図が届いていない）。

### 3.6 0.14.0 の後半（2026-10-25〜26・この節は全部未確認）

- [ ] **拡張の名前**（ユーザー決定 2026-10-26）: 拡張機能ビュー・Marketplace の表示が **Lily# Studio**。コマンドは *Lily#: …* のまま、言語モードの表示は *Lily#* のまま、設定は `lilysharp.*` のまま。0.13 から自動で 0.14.0 に上がる（別の拡張にならない）。
- [ ] **Ask AI の入口**（2026-10-25）: プレビューのヘッダの AI ボタンは **✨** だけ、マウスを載せると *Ask AI (Ctrl+I)*、小節を選ぶと *Ask AI, the selected bars as a hint (Ctrl+I)*。右クリックは、楽譜の外で *Ask AI…*、選択の上で *Ask AI, the Selection as a Hint…*、音符で *…This Note…*、小節で *…This Bar…*。入力欄は何も選ばずに *What should the AI change?*、選んで *3 bars selected — a hint for the AI. What should it change?*。
- [ ] **候補パネルの見え方**（2026-10-25）: AI が変えた音符だけが**緑**（範囲の中の変えていない音符は黒のまま＝ユーザー報告「範囲の中が全部オレンジ」）。*Text after Accept*（旧 Replacement text）を開くと、書いた語が緑・消した語が赤の取り消し線（OMR の修正パネルと同じ）。
- [ ] **無題のファイルを保存してから Accept**（ユーザー報告 2026-10-25）: 無題の .lys で Ctrl+I → 候補が出たら、元のファイルを保存（名前を付けて）→ Accept → 保存したファイルに書き込まれる（*the document was closed* にならない）。▶ Play も鳴る。書けなかったとき（ファイルを閉じた等）は、パネルの下に理由が出てボタンが押せる状態に戻る（灰色のまま残らない）。
- [ ] **Range**（ユーザー要望 2026-10-25）: ヘッダのアイコン（上下の矢印と 8 分音符）を押す → ボタンが沈み、楽譜の右上に表。各パートの ▼ 最低音・▲ 最高音（書いた綴り・`bar N`）・**Tessitura**（G4–E5 のような音名と、半音ごとの棒＝範囲の中は濃く下線・外は淡い）。▲ を押す → その音符が光って画面に入り、エディタのカーソルもそこへ。同じ高さが何か所かあれば *1/2*、押すごとに次へ。棒にマウスを載せると *E4: 12% of the time*。編集すると表も変わる。もう一度押すと閉じてボタンが戻る。
- [ ] **楽器の音域の警告**（ユーザー要望 2026-10-25）: `part fl { instrument flute }` に `b,,4`（B3）→ Problems に LYS1052 *this note sounds B3, below the range of the Flute (C4-D7 …)*。`instrument clarinet` で書いた E3 は出ず、書いた D3 は出る（鳴る高さで判定）。`instrument contrabass5` で書いた C2（鳴るのは C1）は出ない、`contrabass` では出る。`instrument` の無いパートには出ない。
- [ ] **選んだ小節の再生**（ユーザー要望 2026-10-25）: 小節を Shift+クリックで 2〜3 小節選んで ▶ → その小節だけ、全パートが鳴る。1 小節目の途中の音符から 4 小節目の途中の音符まで選んでも、**水色の小節の頭から終わりまで**鳴る（ユーザー報告「3 拍が範囲に入らない」）。▶ を押しても水色の選択は消えない（ユーザー報告）。
- [ ] **練習メニュー**（ユーザー要望 2026-10-25）: ⏹ の右のメトロノームと ▾ のボタン → ドロップダウン。**Tempo** のスライダー（50〜150%・動かす間は数字だけ、離すと変わる・ボタンに *80%* が出る・**Reset** で 100%）、**Loop**（On／Off）、**Metronome**（On／Off・拍ごとにウッドブロック、小節の頭が強い）、**Parts**（パート名のボタンが縦に並ぶ・押し込みで鳴る・外すと灰色で鳴らない・コード段も 1 つのパート）。再生中に変えると、その場所から新しい設定で続く。何か 1 つでも楽譜どおりでなければボタンが沈む。外をクリックか Esc で閉じる。
- [ ] **ループ**（ユーザー報告 2026-10-25〜26）: Loop を On にして ▶ → 選んだ小節（無ければ曲全体）を回り続ける。楽譜の反復は取らない（書いた順に 1 回ずつ）。MIDI 出力でも 1 周の頭と終わりが欠けない（3 拍飛ばない）。出力チャネルに *PREVIEW loop: N rounds of … s, … notes*（*old build* と出たら Reload Window）。`samples/morning-light.lys`（コード段あり）で全部の音が同時に鳴らない・1 周は最後のフェルマータまで。弱起（`partial 4`）の曲でメトロノームの強拍が小節線に来る。同じ高さの音が続く所で、Tempo を 120%・150% にしても音が欠けない。
- [ ] **演奏時間**（ユーザー要望 2026-10-25）: ヘッダに *3:42* のような長さ（反復とテンポの変化込み）、編集すると更新、右に余計な隙間が無い。
- [ ] **ヘッダの並び**（ユーザー要望 2026-10-25）: Export・✨・Range は同じ大きさのアイコン、中身が中央。プレビューを狭くするとボタンは縮まず、まとまり（Score と Export／✨ と Range／再生／ページ）ごとに次の段へ。段の端に区切り線が残らない。
- [ ] **ツールチップ**（ユーザー報告 2026-10-25「小さくて読みづらい」）: プレビューのボタン・Range の棒・候補パネルのボタンにマウスを載せる → 0.5 秒後に VS Code のホバーと同じ大きさの字で説明が出る（ブラウザの小さい黄色い札は出ない）。楽譜の上では出ない。
- [ ] **C1**（第977・補完の続き）: part の中で `transpose` を補完（Tab／Enter）→ `transpose ` の後ろで補完窓が開き、`des`（up a minor second）・`b,`（down a minor second）・`d`・`bes,` … `c'`・`c,` が近い順に並ぶ。選ぶと書かれ、譜が移る。トップレベルの `transpose` と `score x transpose` も同じ。part の中で `transposition` を補完 → `8va` `8vb` `15ma` `15mb` が開く。`shift ` の後ろでは何も出ない（従来どおり）。
- [ ] **C2**（第977・AI 候補のパネル）: 数頁ある楽譜で Transform Selection with AI → 候補のパネルで 2 頁目あたりまでスクロール → **Before**／**After**（と Tab）を押しても表示はそのまま（1 頁目の先頭に戻らない）。Ctrl+ホイールで拡大・縮小（ポインタの下の小節が動かない・Before に切り替えても同じ倍率）、戻すと開いたときの幅に。楽譜をドラッグするとスクロール（手のカーソル）、離しても音は鳴らない。ドラッグせずに音符をクリック → そこから鳴る・小節の空いた所をクリック → その小節が選ばれる。 ドラッグの後、音符に紫（選択色）の四角が残らない。**Text after Accept**（旧 Replacement text）を開く → ファイルの全文（選んだ範囲の外も）・最初の変更が欄の中央に来る（長い候補で）→ Accept／Ask Again…／Play／Stop／Reject は画面内のまま、テキストは欄の中で右にスクロールバー・ホイールで最後の行まで読める（楽譜は動かない）。
- [ ] **C3**（第978・scan のヘッダ）: Show Original Scan の scan を Ctrl+ホイールで 200% 以上にして横・縦にスクロール（ドラッグでも）→ 頭のバー（ファイル名・Scroll with the preview・Marks below・Fix N marks with AI…）は左端から右端まで崩れずに上に残り、右に楽譜が透けない。ペインを狭めて頭が折り返しても 1 頁目の上端が頭に隠れない。プレビューとの連動（Scroll with the preview）と Fit Width／Reset Zoom は従来どおり。
- [ ] **C4**（第978・小節の Play from Here・⚠️ package.json が変わった＝Reload Window を 2 回）: プレビューで小節の音符の無い所（音符の間・休符の横）を右クリック → **Play from Here** が出る → その小節の頭から鳴る（頭が休符なら次に鳴る音から）。音符の上では従来どおりその音符から。楽譜の外（余白・題名）では出ない。再生中の右クリックの Stop Playback も従来どおり。
- [ ] **C5**（第980・カウントイン）: プレビューの練習メニュー（⏹ の横のメトロノーム）→ **Count-in** を On（練習ボタンが押し込まれる）→ ▶ → 木の音が 1 小節（頭が強い）鳴ってから曲が始まる。弱起の曲では最後のカウントの上で弱起が鳴る。小節の途中の音符で Play from Here → 「1、2」と鳴ってその拍から入る。小節を選んで ▶・ループ On でも 1 周目の前だけカウント。再生中にテンポを動かしても数え直さず、同じ所から続く。MIDI 出力でも同じ。Off で従来どおり。
- [ ] **C6**（第982・Add Chord Track…・⚠️ package.json が変わった＝Reload Window を 2 回）: ピアノの曲（例 samples/nocturne.lys）を開き、プレビューの Score メニューで score を選ぶ → コマンドパレット **Lily#: Add Chord Track…** → メニューは 1 つだけ（題にその score 名・Plain／Standard／Rich の順・初回は Standard、次からは前回の選択にカーソル・第986）→ 選んだ score のいちばん上の譜表の上に `chords harmony` が譜表の前の独立した行で入り、拍の変わり目に和音が並ぶ。メロディの譜表だけの score を選んで もう一度 → `harmony2` が別にでき、その score にだけ載る（メロディから付けた和音）。Rose Garden-3 なら Standard で `Dm | G7 | C`、Rich で `Dm7 | G7 | C`、Plain で `Dm | G | C`。Ctrl+Z で 1 回で戻る。
- [ ] **C7**（第987・AI 候補のボタンが戻る）: Ctrl+I で AI に頼み、候補パネルが出たら Ask Again で別の頼みを送り、AI が考えている間に言語サーバーを止める（タスクマネージャで lilysharp-lsp の dotnet を終了＝VS Code が自動で立て直す）→ パネルは前の候補に戻り、足元に「Asking again failed (…) — this candidate is as it was.」、3 つのボタンが押せる。Accept → 書き込まれる。同じく Accept を押した瞬間にサーバーを止める → 「Not written (…) — Accept to try again.」が出てボタンが戻り、もう一度 Accept で書ける。
- [ ] **C8**（第989・Compose with AI・⚠️ package.json が変わった＝Reload Window を 2 回）: コマンドパレット **Lily#: Compose with AI…**（.lys の右クリックにも）→ 「弦楽四重奏、ニ短調、3 分くらい、ゆっくり－速く－ゆっくり」など → AI が考えている間は何も開かない（	itle "New Piece" だけのファイルも出ない）→ 曲が来たら新しい無題のエディタで開き、先頭のコメントに「Composed with AI (…), 日付」「Asked: 頼み」「The AI: 一言」が入る（候補パネルは出ない・第990）。`title` は曲に合った固有の題（`New Piece` などではない・第991）。「エレキギターとドラムの曲」→ ベースも入り、テンポは最後まで 1 つ。「ギターとベースの曲」→ ドラムも入る。「ジャズのリードシート」→ 旋律 1 段の上に chords の行、セブンスと ii–V、スウィング（第992）。「ジャズの曲」（リードシートと言わない）→ ピアノかギター・ベース・ドラム＋旋律楽器のコンボ（1 段にならない・第993）。各パートに `instrument`（violin／viola／cello）、`staffGroup` で括られ、Play で弦の音がする。全部が全音符・全パート同じリズムにはならない。Lily# Extension のログに `attempt N/3 turned down — it compiles, but as music: …` が出ていれば、点検が 1 回差し戻している。開いていた score は変わらない。
- [ ] **C9**（第996・見落とした小節線）: `Downloads\tabi.lys` の L45（32 小節目＝OMR が小節線を見落として 2 小節が 1 行）→ 電球に **Write the missing bar line** → 選ぶと `dis8` の後ろに ` |` が入り、赤い枠と MissingBarLine の警告が消える（Ctrl+Z で戻す）。戻して L45 を右クリック → **Edit Bars Beside the Scan** → Scan も Now も 2 小節とも見える（前は 1 つ目だけ）・行の説明の後ろに **write the missing bar line** → 押すと欄に ` |` 入りの行が入り、Draft の絵と門が付いてくる → Accept で書かれる。パネルのどの絵も Ctrl+ホイールでポインタの下を中心に拡大・縮小、ドラッグでスクロール（手のカーソル）、ドラッグの後でクリックは起きない（音は鳴らない）。拍の途中で切れる小節（音が切れ目をまたぐ）ではどちらも出ない。
- [ ] **C10**（第997・⚠️ package.json が変わった＝Reload Window を 2 回）: OMR の .lys のプレビューで小節を右クリック → **Fix OMR Marks with AI…** が無い（Edit Bars Beside the Scan はある）。Show Original Scan の小節を右クリック → こちらも Fix OMR Marks with AI… は無い（第998）。scan の頭のボタンが **Edit N Bars Beside the Scan…**（1 つなら Bar）・スライダーを動かすと N が変わる → 押すと表示中の印の小節が Beside the Scan のパネルに並ぶ（AI は呼ばれない・行の Ask AI で頼める）。印が 0 ならボタンは押せない。
- [ ] **C11**（第998・AI の返答待ち）: Edit Bars Beside the Scan の行で Ask AI → 返答が来るまで、行の説明の後ろと Draft の欄に回る輪と点滅する「asking the AI…」が出続ける → 答えが来ると消える。cancel でも消える。順番待ちの行は「waiting for the AI…」（動かない）。
- [ ] **C12**（第999・行がずれても正しい小節を切り抜く）: OMR の .lys を開き（元画像は開かない）、30 小節目あたりより上に空行を 2 行足す → 下の小節で Edit Bars Beside the Scan → Scan の絵がその行の小節（前は 2 小節前の絵）。上の行を 1 行消しても同じ。保存して VS Code を閉じ、開き直してから同じことをしても正しい。元画像を開いてクリック・キャレットの連動も、足した行の下で合う。
- [ ] **C13**（第1000・AI と自分の編集のトグル）: Beside the Scan の行で Ask AI → 待っている間に下のテキストを編集（Draft は「your edit」）→ AI の答えが来る → 行の説明に「Your edit. **use the AI's**」→ 押すと Draft が AI の案（「AI proposal」）・説明の後ろに **back to my edit** → 押すと自分の編集が戻る（絵と門も）→ もう一度 use the AI's → AI の案、と何度でも行き来できる。AI の案を表示中に打ち直すと新しい編集になり、back to my edit は消える。AI に頼んでいない行でも undo my edit ⇄ back to my edit。
- [ ] **C14**（第1001・欄の補完）: Beside the Scan の行の下のテキストをクリックして開き、音符の後ろで `@` → キャレットの下に候補（エディタと同じ並び）→ `st` と打つと絞られる → ↑↓ で選び Enter（または Tab）→ `@staccato` が入り、欄は閉じない・絵と門がついてくる。Escape は一覧だけ閉じる（もう一度 Escape で欄が元に戻る）。Ctrl+Space で開く（VS Code に取られて開かなければ報告）。候補をクリックしても入る。一覧が無いときの Enter は今までどおり欄を閉じる。調の中の和音名など、ファイルの文脈が要る候補も出る。
- [ ] **C15**（第1002・Select Chord・⚠️ package.json が変わった＝Reload Window を 2 回）: プレビューで和音の音符を右クリック → **Select Chord**（単音の音符・小節の空いた所では出ない）→ エディタのキャレットが `<c e g>|4` の `>` の直後に来て、プレビューで和音の音が全部光る。そのまま Ctrl+Shift+Up で和音全体がオクターブ上がる（`>'`）。キャレットを `|<` に置いても、`4` や `@fermata` の上に置いても全体が光る。和音の中の音の上では、その音だけ光る（今までどおり）。`<< … >>` や `\<` では出ない。
- [ ] **C16**（第1003・和音名と構成音）: `<c' f' g'>1@chord(C)` と書く → `f'` に青い波線（情報・問題パネルでは情報）「F is not a tone of C: the chord's notes spell Csus4 …」→ 電球 **Name the chord Csus4** → `@chord(Csus4)` になり波線が消える。`<c' e' e'>8@chord(C)`・`<c' g'>1@chord(C)`（3 度なし）・`e'4@chord(C)`（単音）では何も出ない。
- [ ] **C17**（第1004・AI に小節の読み方を渡す）: OMR の .lys で 2 声・タイ・スラー・3 連符・臨時記号のある小節を Fix OMR Marks with AI → 出力チャネル「Lily# AI」で system に「HOW TO READ THE BAR」の 6 項がある。前より、和音と順に鳴る音・タイの続き・声部の振り分け・連符が合う案が増えたか（採用率の感触でよい）。
- [ ] **C18**（第1005・ズームしても小節が枠に残る）: Beside the Scan の Scan の絵を Ctrl+ホイールでずっと縮める → 6 倍で止まり、対象の小節は枠の中に丸ごと見えたまま・ドラッグしても小節が枠から出ない → 拡大し直すと小節に戻る。拡大中のドラッグは小節の外へは行かない。Now・Draft の絵も同じ。編集で Draft の絵が描き直されたら、その新しい切り抜きが基準になる。
- [ ] **C19**（第1006・編集から頼む／2 つのラベル）: Beside the Scan の行で下のテキストを編集 → Draft の絵の上が **your edit**（太字）/ **AI proposal**（AI の案があるとき・無ければ **the bar as it stands**）→ 行の説明に undo my edit・use the AI's・back to my edit のリンクは無い。編集したまま Ask AI → 出力チャネル「Lily# AI」の依頼で `<bar>` が自分の編集・`<reader>` が元の読み → 答えが来ると Draft が AI の案（AI proposal が太字）、your edit を押すと自分の編集に戻る・もう一度 AI proposal で AI の案。AI が「unchanged」と答えたら自分の編集のまま。
- [ ] **C20**（第1007・AI の返事は「AI:」）: AI の返事が来た行で、行の見出しに **AI:** で始まる返事の文。your edit を選んで（または返事を待つ間に編集して）も「Your edit. **AI:** …」と返事が見える。
- [ ] **C21**（第1008・3 つのラベル／見えている行を渡す）: AI の案が来た行を編集 → Draft の上が「original / **your edit** / AI proposal」→ original を押すと元の小節（Accept は @todo を外すだけ）・AI proposal で AI の案・your edit で自分の編集に戻る（何度でも）。AI proposal を表示したまま Ask AI → 出力チャネルの依頼に「as an EARLIER ANSWER of yours proposed it」と前の案・original を表示したまま Ask AI → `<bar>` は元の読み（`<reader>` は無い）・your edit なら自分の編集。
- [ ] **C22**（第1011・プレビューの右クリックのグループ・⚠️ package.json が変わった＝Reload Window を 2 回）: プレビューで和音の音符を右クリック → 区切り線は 2 本＝「Ask AI… / Select Chord / Select This Bar（/ Clear Selection / Edit Bars Beside the Scan）」「Play from Here（/ Stop Playback）」「Fit Width / Reset Zoom」の 3 群。元画像の右クリックも Edit Bars Beside the Scan と Fit Width・Reset Zoom の 2 群。

## 4. §2 R13 ⒝ — 差分頁の配信（実機未確認・第404）

- [x] 出力チャネル「Lily#」を開いたまま、1 頁の本で音を 1 つ打つ → epages kepte の行（頁が丸ごと届き直していない）。
- [x] 複数頁の本（10 頁級）の途中の頁で音を 1 つ打つ → 編集した頁だけ echangede、後ろの頁は eshiftede（erequestFulle が出ない）。
      ePREVIEW updatee の ms を見る（shifted 9 頁 × 1000 要素の DOM 書き換えの実測＝HANDOFF が求めている数字）。
- [x] 頁の高さが動く本（auto-size の fixture の形）では erequestFulle で全頁が届き直す＝その頻度（HANDOFF R13 ⒝′ の判断材料）。
- [x] 小節が増える編集で、後続の頁が小節番号の重ね描きだけの違いで丸ごと届くか（⒝ 群単位の item の値付け）。

## 5. OMR 連携 B1〜B6（eLilySharp-Omre が要る・HANDOFF §1.0「B1〜B6 は VS Code で未確認」）

- [x] **B1** eLily#: Convert Image/PDF to Lily# (.lys)…e: コマンド・Explorer の右クリック（png/jpg/tif/pdf）・エディターへのドラッグ＆ドロップ・画像の貼り付けの 4 経路で
      elilysharp.omr.pathe の OMR が走り、進み具合が通知に出て、終わると e.lyse とプレビューと scan が開く。OMR が無いと Scan Import 拡張の導入を案内する（第903）。
- [x] **B2** ex.omr.jsone のある本で e#キーe 付きの e@todoe に、候補（efis'8e / ef'8e …）がクイックフィックスとして出て、選ぶと音が置き換わり e@todoe が外れる。候補の無い todo は *Resolve* だけ。
- [x] **B3** eLily#: Show Original Scane: 頁画像の上に todo の枠が**赤い破線**（プレビューの小節の枠と同じ赤・同じ破線・2026-10-17 のユーザー要望）、小節の枠がクリックできる。小節のクリック → e.lyse のその小節の行。
      赤枠の内側にマウスを載せても赤くならず、下の小節が他と同じ青に染まる。赤枠の内側のクリックもその小節へ（前は e@todoe へ飛び、赤い半透明で塗られた）。枠の tooltip にメモと確信度。
      頭の 1 行（読んだ頁の数・*Scroll with the preview*・*Marks below*）は頁をスクロールしても上に残る（2026-10-17 のユーザー要望）。その下に頁が潜り、帯の背景は透けない。プレビューの小節に飛んだとき（B4）小節は帯の下ではなく帯のすぐ下に来る。
      **頁のドラッグでスクロール**（2026-10-17 のユーザー要望・プレビューと同じ）: 頁の上で手のカーソル、左ボタンで掴んで動かすと頁が付いてくる（握った手）。小節・赤い印の上では指のまま、クリックは今まで通り飛ぶ。ドラッグの終わりのクリックは飛ばない。帯の上・スライダー・スクロールバーからは掴めない。画像の「ゴースト」が付いて来ない（ブラウザの画像ドラッグが起きない）。Scroll with the preview 入ならドラッグ中もプレビューが付いて来る。
      **Ctrl+ホイールで拡縮**（2026-10-17 のユーザー要望・プレビューと同じ 25〜400%・10% 刻み）: マウスの下の点が動かない・右下に e150%e のような表示が一瞬出る・頁の幅がペインを超えたら横スクロールできる（帯はそのまま）・小節の青い塗りと赤い破線も一緒に拡大される・ドラッグ・クリック・ホバーは拡大後も同じ小節に効く・タブを閉じて開き直しても倍率が残る。
      **右クリック**（同日のユーザー要望）: Cut/Copy/Paste は出ず、*Fit Width* と *Reset Zoom (1:1)* の 2 つだけ。Fit Width → 100%（頁がペインの幅）。Reset Zoom (1:1) → 頁の実寸（読んだ画像の 1 px が画面の 1 px・右下の表示は例えば e310%e・400% で頭打ち）、頁の左上が帯の下に来る。パレットでも scan がアクティブなら 2 つが出る。プレビューの右クリックは今まで通り（Play from Here・Fit Width・Reset Zoom (100%)）。
      e.lyse のカレット → 対応する枠が光ってスクロール（編集で行がずれても追う）。プレビューのクリックでも同じ。
- [x] **B4** *Scroll with the preview*（既定で入）: scan とプレビューが小節ごとに一緒にスクロール。e@todoe のある小節がプレビューで赤い破線の枠＝**scan の箱と同じパート小節だけ**（2026-10-17 のユーザー要望・前は段の全パートを囲んだ）: パートの印はそのパートの五線の小節・長さの印（memo に evoices …e・side file に epartse）は拍が合わないパート全部・符尾／連符の印（epart: nulle）は全パート。**2 パート以上を覆う印は scan でもパートごとの枠**（同日のユーザー報告「大きさが揃わない」・reader の箱が 2 パートを一つで囲んでいても、Lily# は ebarse のパート小節ごとに描く）＝両側とも同じ数・同じ大きさの枠。スライダーの e(N / M)e は枠の数ではなく印の数。**プレビューの枠の高さは五線ちょうどではなく上下に 2 間ずつ広い**（scan の箱と同じ・同日のユーザー要望）＝五線の高さの 2 倍。青い塗り（ホバー・caret）は今まで通り五線の内側だけ。手で書いた e@todoe（scan が知らない key）はそれが書かれた五線の小節。同じ section を 2 回弾く本では両方の写しに枠。
      **一番上**（2026-10-17 のユーザー要望）: プレビューを一番上まで戻す → scan も一番上（頁の頭が見える）。scan を一番上まで戻す → プレビューも一番上（曲名が見える）。前はどちらも相手の最初の小節を上端に置き、題名が隠れた。
      **見切れの一致**（同日のユーザー要望）: 片方で上端の小節が半分隠れる位置に止める → 相手でも同じ小節が上端でほぼ半分隠れる（前は相手の小節が上端の少し下に来た）。上端の小節の上端が丁度見えている → 相手も丁度。scan の「上端」は帯の下端。
      epaper { breaksOnly }e の本で段組みが元と揃う。
- [x] **B5** elilysharp.omr.verifyOnSavee 入 → 保存で eomr verifye が走り、scan とクイックフィックスが更新される（**OMR 側の everifye 実装待ち**。無ければ呼ばれないことを確かめる）。
- [x] **B6** e@todoe を含む範囲を選んで Ctrl+I → プロンプトに todo のメモ・確信度・候補が入っている（Output / デバッグで見る）。直した印は e@todoe が外れ、直さない印は残る。
- [x] **B7**（第903・OMR 拡張の受け口）: eLILYSHARP_OMR_PATHe を外し、OMR 側の VSIX（e..\LilySharp-Omr\out\extension\lilysharp-omr-win32-x64-0.3.0.vsixe）を
      *Install from VSIX…* で入れる → Reload なしで Explorer の画像の右クリックに *Convert Image/PDF to Lily# (.lys)… (Beta)* が出る・Output に eOMR reader (extension): …\bin\omr.exee・
      取り込み直後に照合の e@todoe が付く（e--verifye）・初回だけ Beta の一言。拡張を外す → 右クリックの項目は **残る**（Windows・e.lyse の無いワークスペースでも・2026-10-17 のユーザー決定「OMR 未インストールでもメニューを出す」）、右クリックでもパレットでも、ファイル選択ダイアログより**先に** *Install Scan Import* の案内（同日のユーザー決定・前はファイルを選んだ後で、右クリックの項目は OMR が入っているときだけだった）。
- [x] **B7′ 古い reader**（2026-10-17）: eLILYSHARP_OMR_PATHe に e--version --jsone が eOMR_MIN_VERSIONe（eomrCore.tse）より小さい版を答える reader を指して読む → ファイルを選んだ後に警告 *Scan Import 0.2.0 is older than this Lily# is written for (0.3.0 or newer). Update it?*（ボタン *Update Scan Import*）。断る → その reader で読み込みが続く。押す → Marketplace の最新が入り、読み込みは行わない（もう一度実行）。保存時の verify は警告しない。
- [x] **B8**（第903・confidence）: OMR の新しい VSIX で取り込む → Problems で OMR の印の一部が **Warning**（confidence 0.6 未満）、残りと手で書いた e@todoe は **Info**。F8 が Warning の印から回る。
      Show Original Scan: 箱が塗りなしの赤い破線・低いものほど太く濃い・上に *Marks below* のスライダーと e(N / M)e・動かすと高いものが消える・e.lyse に無い印（quiet）は細く薄い（破線は同じ）。取り込みの通知に e(+N quiet, in the scan view)e。
- [ ] **B9**（第972・edocs/omr-ai-fix-design.mde M1・Copilot のモデルが要る）: ex.omr.jsone のある本でパレット **Lily#: Fix OMR Marks with AI…**（e.lyse の右クリックにも・scan の無い本ではパレットだけ）→ QuickPick に印のある小節が**確信度の低い順**（確信度の無い印が先頭）・各行に memo と行番号・**scan view のスライダーより下の印にチェック**（scan view が無ければ 0.8 未満）・題名に eN bars · about X se（初回は e(2–4 s a bar)e・2 回目からは実測の平均）・チェックを変えると数字が変わる。確定 → 通知の進捗 ek / n bars, p proposals — bar N (part)e（キャンセル可）・対象の行が薄く塗られる → パネル **Lily# — OMR fixes** に 1 小節 1 行: **Scan**（元画像のその小節の切り抜き・30% の余白）・**Now**（今の譜のその小節）・**Proposal**（提案の譜・下に文字）・**Why · gates**（理由・ecompile ✓ length ✓ verify —e）・**Accept / Reject / Open in editor**。
      通らなかった小節は *no proposal* と理由（黙って消えない）・模型が enulle を返した小節も同じ。各小節の見出しに **Take** のチェックボックス（門が全部 ✓ で confidence ≥ 0.5 の行は最初から入り、入った小節は 2 行とも**薄い青**）。何度入れ外ししても何も書かれない・頭の eN of M checkede と下の *Accept N checked* の数が追う・頭の **All** のチェックボックス（全部入りで ✓・一部で「－」・押すと全部入れる／全部入りなら全部外す）。**Accept N checked** → チェックした小節だけが 1 つの編集で書き換わり e@todoe が消え、パネルが閉じる・**Ctrl+Z 1 回で全部戻る**。**Cancel** → 何も書かずに閉じる（第973 のユーザー要望で行ごとの Accept／Reject はチェックボックスに替えた）・Problems の LYS2001/2002 がその小節から消える。出力チャネル **Lily# AI** に 1 小節 1 行（秒・proposal／no proposal・門）。
- [ ] **B9′**: 範囲を選んで実行 → その行の印だけ全部チェック。実行中に e.lyse を編集 → 止まり、済んだ分だけパネルに出る（通知 *the file changed…*）。行（チェックボックスを含む）のクリック → エディターがその小節の行へ移り、カーソルがその小節の頭に置かれ（選択はしない）、行が薄い青（パネルのフォーカスはそのまま・プレビューも追う）・パネルの今の行に青い枠。絵の下の 2 行は差分＝Now では消える部分が赤の取り消し線（e@todoe も・e@todoe は鍵とメモを省いて e@todoe とだけ）、Proposal では入る部分が緑。**絵の音符も同じ色**＝Now の絵で消える音符・休符・連符の括弧が赤、Proposal の絵で入るものが緑（位置の付いていない e{e e}e などは塗られない）。提案の無い小節（*unchanged*・*no proposal*・*no answer*・門を通らなかった提案）もチェックでき **Take (remove @todo)**（既定は外れ）→ Accept で音はそのまま e@todoe だけ消える。Accept の前に別の行を足して対象の行を動かす（パネルは開いたまま） → 動いた行を追って書く・対象の行自体を書き換える → その行は *skipped* と名指し。elilysharp.ai.omrFixBatche を 2 にして 5 小節 → 2 小節ごとに *Continue with the next…*（断ると止まる）。
- [ ] **B9″**: **Lily#: Select AI Model…** の 1 つ目 *Ask AI… (Ctrl+I) and Fix OMR Marks with AI…* → 選ぶと elilysharp.ai.editModele に入り、次の実行の出力チャネルの 1 行目 evia <vendor/family>e が変わる。設定 UI の AI グループに elilysharp.ai.editModele（omrFixBatch・omrFixImages は settings.json だけ）。
- [ ] **B10**（第973・design M2・画像）: 既定（elilysharp.ai.omrFixImagese 入）で B9 を回す → 出力チャネルの 1 行目に escan shown to the modele・各小節の行に escan 460×160e のような大きさ（30% の余白つきの切り抜き）。画像を受けない模型（例: 古い GPT 系）を eeditModele に選ぶ → 最初の小節で escan not taken by the model (sent as text)e と e<model> takes no image: …e が 1 回・以後の小節は esent as texte・提案は出る。設定を切る → 1 行目 escan off (lilysharp.ai.omrFixImages)e・小節の行に scan の語が無い。**門（design §5）**: 同じ本（OMR の edata/evale の既知の誤りの小節）を入・切で 2 回回し、集計行 eN bars · P proposals · scan on|offe と Apply 後の etaken so fare を比べて採用率を 1 行で HANDOFF §1.0 ⒜ に書く。
- [ ] **B10′**（クイックフィックス）: e@todoe の診断の上で eCtrl+.e → *Resolve this TODO (remove @todo)* の下に **Fix this bar with AI…** → 選ぶと QuickPick なしでその 1 小節だけ走り、パネルに 1 行。手で書いた e@todoe（scan の無い本）でも出て、Scan の列は *no scan*。
- [ ] **B11**（第973・design M3・照合の門）: eLILYSHARP_OMR_PATHe を 2026-10-23 以降の OmrProto（e--version --jsone に efeatures: ["verify-bars"]e）に向け、elilysharp.ai.omrFixVerifye 入（既定）で B9 を回す → 各小節の行の門が ecompile ✓ · length ✓ · verify ✓|✗e、パネルの *Why · gates* にも everify ✓|✗e（旧 reader・設定切・side file 無しでは everify —e と出力チャネルに理由 1 行 egate verify: …e）。提案が scan と食い違う小節は everify ✗e で **Accept all に入らない**・理由に ethe scan says: bar N: …e。1 小節あたり 2〜3 秒増える（lysc が全文を描く）。実行中・実行後に ex.omr.jsone の更新時刻が変わらない（e--no-writee）。ereadingse のある本（OMR の e--no-fixe の出力）では、プロンプト（出力チャネルには出ない＝デバッグで見る）に eas written -> as it might bee の行。集計行に everify N ✓ M ✗e。
- [ ] **B12**（第975・印を残して採る）: パネルの行の頭、Take の右の **keep @todo** を入れる → Take も入り、右に入力欄（「what is still wrong (optional)」）が出てカーソルがそこへ。何か打って Accept → その小節が提案どおりに書き換わり、**元の印に近い音符**に e@todo(<元のキー> "<打った言葉>")e が付く（空なら e@todo(<キー>)e・手書きの印なら e@todo("…")e・e.upe／e.downe は元のまま）。Take を外すと keep も外れて欄が消える・All で外しても同じ。提案の無い行（Take (remove @todo)）で keep を入れると、音符はそのままで印の言葉だけが替わる。Ctrl+Z 1 回で全部戻る。プレビューに新しい memo が出る。
- [ ] **B13**（第975・提案をその場で直す）: パネルの右の列（Proposal）の下の文字にマウスを載せると点線の枠 → クリックで入力欄に変わる。打って 0.4 秒止まると、右の絵が描き直され（緑＝新しく入る音）、左の Now の赤も付け直され、門が ecompile ✓|✗ · length ✓|✗ · verify …e（照合のある reader なら数秒後に verify が付く）、理由が「Your edit…」と **use the model's**。打った時点で Take が入る・Take の語が「Take」になる。Enter で閉じる・Escape で開いたときの文字に戻る・**use the model's** でモデルの答え（無ければ元の行）に戻り赤緑も戻る。提案の無い行でも同じに書ける（自分で直す）。Accept で編集した行が書かれる（keep @todo と併用可）。入力欄が開いている間は提案の絵の緑が消え、カーソルのある音符（選択なら選択した範囲の音符）が青く光り、カーソルを動かすと付いてくる。閉じると青が消えて緑が戻る。和音の中の音も光る（edata-alte の位置）。和音では、構成音の文字の上ならその 1 音だけ・e<e／e>e／e>e の後ろ（音価・e@cresce など）の上なら全部の音が光る。提案の絵の音符（休符・小節線も）にマウスを載せると指の形 → クリックで入力欄が開き、その音符の文字（和音なら構成音）の前にカーソル、その音が青。提案の無い行（no answer／unchanged／did not pass）でも、右の列に今の小節（印を除いた形）が描かれ、理由の後ろに「— click a note or the text to edit the bar yourself」。編集すると Take が入り、Accept で書かれる。
- [ ] **B14**（第975・AI を通さず小節を開く）: e.lyse で小節の行にカーソル（または数行を選択）→ 右クリック *Edit Bars Beside the Scan*（scan のある e.lyse）／コマンドパレット *Lily#: Edit Bars Beside the Scan* → パネル「Lily# — Bars beside the scan」、列は **Now · Scan · Edit**、行ごとに今の小節・scan の切り抜き（side file があれば）・同じ小節（編集用）。理由の行は「Click a note or the text under the picture to edit this bar.」、所要時間は出ない。最初はどれもチェックなし・編集すると入る・**undo my edit** で戻る。Accept で編集した小節だけが書かれる（1 回の Ctrl+Z）。小節の無い行だけを選ぶと「no bar on the selected lines」、31 小節以上は先頭の 30。side file が無い e.lyse でも開ける（Scan は no scan）。e@todoe の無い小節の行は Take の右が **add @todo**（入力欄は「what to check (optional)」）→ 入れて Accept で最初の音符に e@todo("…")e が付く。e@todoe を含む小節の行は **keep @todo**（既定は外れていて、Take だけなら印が消える）。
- [ ] **B15**（第975・右クリックから）: プレビューの小節（音符でも小節の空いた所でも）を右クリック → メニューの先頭に **Fix OMR Marks with AI…** と **Edit Bars Beside the Scan**（Play from Here・Fit Width などはその下）。小節の外（余白・題名）ではこの 2 つは出ない。Show Original Scan の小節（枠の中）を右クリックでも同じ 2 つ。Edit Bars → その 1 小節がパネルに開く（行の小節番号が右クリックした小節）。Fix OMR → その小節に e@todoe があれば QuickPick なしでその小節だけ AI へ／無ければ「that bar carries no @todo mark」＋ **Edit Bars Beside the Scan** ボタン（押すとその小節が開く）。小節の最初が行頭の字下げの後でも、前の行の小節にならない。パネルは、プレビューから開いても Show Original Scan から開いても **Show Original Scan と同じ列**（その e.lyse の scan を開いていないときは前面の列の隣）。
- [ ] **B17**（第975・行ごとの Accept／Discard・⚠️ B9〜B16 の「Take」「Accept N checked」は今は無い＝行の **Accept**／**Discard** と下の **Accept All (N)**／**Discard All** で読み替える）: 行の頭に **Accept**（と **Discard**）。Accept＝その小節だけが今すぐ e.lyse に書かれ（Ctrl+Z 1 回でその 1 小節が戻る）、行がパネルから消える・ステータスバーに「wrote bar N — Ctrl+Z undoes it」。Discard＝書かずに行が消える（AI 待ちならそれも取り消し）。印の無い小節で編集も add @todo も無い行は Accept が押せない・提案の無い印付きの行は「Accept (remove @todo)」。**準備のできた行**（自分の編集で compile ✓ length ✓、または AI の案で門が全部 ✓）は Accept が強調され、下の **Accept All (N)** の N に数えられる→押すとそれらだけが 1 回の編集で書かれ、残りは残る。**Discard All** で閉じる（それまでに書いた分は残る）。最後の行が消えるとパネルは自動で閉じる。1 行 Accept した後でも Edit Bars Beside the Scan で同じパネルに行が足せる（「Open a New Panel?」は出ない・足した行の Now は書いた後の譜面）。
- [ ] **B16**（第975・AI に行ごとに聞く・裏で答える）: (1) Edit Bars Beside the Scan で開いたパネルを残したまま、別の小節で Edit Bars Beside the Scan → **行が下に足される**（それまでの行の編集・チェックはそのまま・同じ小節なら足さずにその行へスクロール）。(2) 行の **Ask AI** → その行だけ「— asking the AI… cancel」（2 つ目以降は「waiting for the AI…」）・ステータスバーに Lily# AI。待つ間も他の行を編集・チェックできる。答えが来るとその行だけが入れ替わる（絵・門・理由・確信度・所要時間・通れば Take が入る）・3 列目の見出しが Edit → Proposal。(3) 待っている行を自分で編集していたら、答えが来ても**自分の編集が残る**（理由の行に「use the AI's」＝AI の案に戻す、AI の案が無ければ「undo my edit」）。編集してから Ask AI すると、その編集が「人の途中の書き直し」として AI に渡る（add/keep @todo の欄の言葉も人の言葉として渡る）。(4) **cancel** でその行が元に戻る。(5) **Fix OMR Marks with AI** は QuickPick の後すぐパネルが開き、行は「waiting for the AI…」で始まって答えの順に埋まる（通知の進捗は出ない・エディターの行の灰色の lock も無い）。同じファイルのパネルが開いていれば行が足され、既にある小節の行はそのまま聞き直す。25 小節を超えると最初に「All N / The first 25」を聞く。(6) パネルを閉じると AI も止まる。別のファイル、または開いた後に書き換わったファイルでパネルを開き直すときは、チェックや編集があれば「Open a New Panel?」と聞く。
- [ ] **B18**（第975・名前を 1 つに・⚠️ B9〜B16 の「OMR fixes」「Bars beside the scan」「Now (OMR)」「Proposal」「Edit」の列は今は無い＝**Bar fixes** と **Now · Scan · Draft** で読み替える）: Fix OMR Marks with AI と Edit Bars Beside the Scan のどちらで開いても、タブは **Lily# — Bar fixes**、頭は **Bar fixes — <ファイル名>**、列は **Now · Scan · Draft**（AI に聞いても列名は変わらない）。各行の Draft の絵の上に小さな字で出どころ: AI の行は待つ間 *waiting for the AI…*／*asking the AI…* → 答えが来て *AI proposal*（提案が無ければ *no AI proposal — the bar as it stands*）。提案の文字を打ち換える → *AI proposal, edited*。AI に聞いていない行は *the bar as it stands* → 打ち換えると *your edit*。*undo my edit*／*use the AI's* で元の字に戻る。
- [ ] **B19**（第975・字の差分）: Draft の `a16` を `a,16` に打ち換える → 絵の下の Draft の字は `,` だけが緑、Now の字の `a16` には取り消し線が付かない。`fis4` → `f4` なら Now の `is` だけが赤の取り消し線。`b4` → `b'8` なら Now は `4` だけ赤、Draft は `'8` だけ緑。`a8` → `g8` のように音名が違うと音符まるごと（従来どおり）。絵の音符は丸ごと赤／緑のまま。
- [ ] **B20**（第975・右クリックの出し分け・manifest が変わった＝Reload Window を 2 回）: (1) scan の無い `.lys`（`x.omr.json` が横に無い）のプレビューで小節を右クリック → **Fix OMR Marks with AI…** と **Edit Bars Beside the Scan** が出ない。エディターの右クリックにも出ない。scan のある `.lys` では従来どおり出る。(2) プレビューの音符を右クリック → 先頭に **Transform Selection with AI…** → エディターでその音符が選ばれ、Ctrl+I と同じ入力欄。小節の空いた所 → その小節の音符全部が選ばれる。Shift+クリックで範囲を選んでから右クリック → その範囲。余白・題名の上では出ない。
- [ ] **B21**（第977・読みの進捗）: 数頁の PDF（表紙のあるもの）を Convert Image/PDF to Lily# で読む → 通知の文が毎秒進む時計つきで *opening the pages… 0:03* → *finding the staves on N pages… 2:10*（Scan Import 0.4.0 ではここが長く、棒は 0% のまま＝OMR に `stage` を依頼済み・入れば *finding the staves: page 2 of N… (about 0:56 to go for the staves)* と棒が進む）→ *reading page 2 of N… (about 0:28 to go for the pages)*（目安は 2 頁目の後から）→ *checking the reading against the pages…* → 楽譜が開く。Cancel で止まる。`lilysharp.omr.verifyOnSave` を on にして保存 → ステータスバーが *checking x.lys against the scan… 0:02* と秒を数える。

- [ ] **B10″**（scan view のボタン）: Show Original Scan の頭のバー、スライダーの右に **Fix N marks with AI…**（N＝スライダーが見せる印のうち e.lyse にあるもの・quiet は数えない・0 なら押せない）。スライダーを動かすと N が変わる・押すと QuickPick がその N 個にチェックされて開く（scan をアクティブにしたままでも e.lyse が対象）。印を Accept で消す → scan の枠が消えて N が減る。

## 6. 結果の書き方

- 通ったら e[x]e。違ったら **番号・手順・見えたもの・期待したもの**を HANDOFF §1.0 ⒜ に 1 行で起票する（例: e1.1 開放弦の輪＝塗られているe）。
- 録画は SHOTLIST の仕様（1280×720・5 MB 以下・5〜20 s）。
