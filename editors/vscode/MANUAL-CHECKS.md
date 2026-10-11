# Lily# VS Code 拡張 — 手動確認リスト（2026-10-10 起票）

機械の網（拡張の 211 本・本体の 11,773 本）が見られないもの＝**実機の VS Code でしか確かめられない挙動**だけを集めた。
出典は `docs/HANDOFF.md` §1.0 ⒜「VS Code の手動確認が未」・`editors/vscode/CHANGELOG.md` の 0.11.0 と Unreleased・
`docs/HANDOFF.md` §2 R13 ⒝・OMR の提案 B1〜B6（`..\LilySharp-Omr\docs\repro\lilysharp-proposal-2026-10-04.md`）。
**確かめたら `[ ]` を `[x]` にし、違ったものは HANDOFF §1.0 ⒜ に 1 行で起票する**（番号で指せる）。

> ⚠️ 2026-10-10 時点で入っている拡張（`yotsuda.lilysharp-0.7.0` のフォルダ名のまま・中身は 17:42 に組み直したもの）には
> 同日 18:05 以降の commit（`time 4/4 break`・PDF の font・condensed の臨時記号・検査の模型）が入っていない。
> **先に §0 で組み直してから始める。**

## 0. 準備

- [ ] 拡張を組み直して入れる（`editors/vscode/DEPLOY.md` Local Testing の手順。`npm run vscode:prepublish` → `server` と `out\extension.js` を
      `%USERPROFILE%\.vscode\extensions\yotsuda.lilysharp-*` へ）。VS Code を **Reload Window**。
- [ ] 出力チャネル「Lily#」に `Language server version: …` の行が出て、その番号が組み直した `lysc --version` と同じ（`Failed to get server version` が出ない）。
- [ ] 設定の既定: `lilysharp.typingAids.enabled` 入・`lilysharp.audition.enabled` 入・`lilysharp.chordShapes.includeStretch` 切・
      `lilysharp.preview.refreshDelay` 100・`lilysharp.preview.highlightTodos` 入・`lilysharp.omr.verifyOnSave` 切。
- 題材は `site/videos/SHOTLIST.md` の 4 行の本（clips 1〜5 用）と、そこに載っている各 clip の本。

## 1. HANDOFF §1.0 ⒜ が「未」と名指すもの

### 1.1 ステップ Ctrl+Shift+↑ / ↓（`lilysharp.stepUp` / `stepDown`）

キーは **Ctrl+Shift+↑↓ の 1 組だけ**（オクターブ記号もコード図の形も同じキー）。HANDOFF の「Ctrl+Alt+↑↓」は Lily# の割り当てではない
（Ctrl+Alt+↑↓ は Windows のカーソル追加＝`auditionCore.ts` の註）＝起票が古い。

> ⚠️ **音はプレビューの webview が鳴らす**（`stepAudition.ts` の頭の註・2026-09-28 のユーザー決定）＝**その本のプレビューが開いていないと、ステップも自動の試聴も黙って何も鳴らさない**（ステップはプレビューを開かない）。Alt+P / Alt+M だけは 1 回目でプレビューを開き、2 回目で鳴る。確かめる前に `Ctrl+K V` でプレビューを横に開く。音が出ないときの切り分け: ⑴ プレビューが開いているか ⑵ Alt+P で鳴るか（鳴らなければ webview の合成器＝`AudioContext` の側・第901 からは SoundFont シンセ＝出力チャネル「Lily# Extension」の `Preview synth …` の行を見る） ⑶ 出力チャネル「Lily#」に `step:` の行が出ていないか（サーバーが Fallback を返した） ⑷ `lilysharp.audition.enabled` が切られていないか。サーバーの `lilysharp/step` は SHOTLIST の 4 行の本で `pitches=[72]` を返す（第888 が使い捨ての試験で確認）。

- [ ] **オクターブ記号**（SHOTLIST clip 3 の手順）: `c4` の上で ↑ 2 回 → `c''4`、↓ 1 回 → `c'4`。各回その音が鳴る。
- [ ] 和音の中の `e` で ↑ → `<c e' g>`（動くのはその音だけ・**鳴るのは和音全体**＝c e' g の 3 音・2026-10-10 のユーザー決定）。`>` の直後で ↑ → `<c e' g>'4`（和音全体が動いて鳴る）。カレットを `c` `e` `g` と渡っても和音は 1 回しか鳴らない。
- [ ] プレビューを開いた直後（まだクリックしていない）に ↑ を押すと、ステータスバーに *Lily#: click the preview once to enable its sound* が出る。プレビューを一度クリックすると、以後は鳴る。プレビューを別の列へ動かした後（webview が作り直される）も同じ案内が出て、クリックで戻る。
- [ ] **コード図の形**（clip 8 の手順）: `c'1@chord(Cm7)` の `Cm7` の上で ↑ → `@chord(Cm7 x35343)`、図が出て鳴る、
      ステータスバーに `Cm7: shape 1 of …`。↑ を続けると形と図が変わる。↓ で戻り、最初の形の上でさらに ↓ → 形が消えて図も消える。
- [ ] **開放弦の輪**: 図の開放弦（形の `0`）は **塗らない輪**（○）、弾かない弦（`x`）は ×、押さえる弦は黒丸。`chord(C x32010)` で確かめる。
- [ ] `lilysharp.chordShapes.includeStretch` を入にして ↑ を続けると、切のときには出なかった広い形が候補に増える。
- [ ] `layout { chordDiagrams all }` の本では、形を書いていないコード名にも既定の図が出る。その上で ↓ → 図が消える。
- [ ] 形がコード名と合わない（`@chord(C x35343)`）と LYS1039 の警告。

### 1.2 入力補助（smartType: `'` `,` `.` `\` と数字）

- [ ] **オクターブ記号が音名の直後に入る**（clip 1）: `cis4.` の `c` と `is` の間で `'` 2 回 → `cis''4.`、カレットは動かず、プレビューの音が上がる。
      `,` 1 回で `'` が 1 つ消える。`e4` の後ろで `'` → `e'4`。
- [ ] **数字が音価を上書きする**（clip 2）: `d4` の直後で `8` → `d8`（`d48` ではない）。`f` と `4` の間で `1` → `f1`。`cis4.` の後で `8` → `cis8.`（付点は残る）。
- [ ] `\` を音符の上で打つとタブの弦番号の枠が音符の後ろに開く（`c\3`）。`c\3 d` で `(` `[` を打つと弦番号を飛び越えて正しい音に付く。
- [ ] `lilysharp.typingAids.enabled` を切ると、どのキーも素の文字になる。
- [ ] 補完のポップアップが開いている間は `'` `,` が補完の操作を邪魔しない（`!suggestWidgetVisible`）。

### 1.3 Split Sections to Match a Part（clip 7）と Regroup

- [ ] clip 7 の本で `A` の警告をホバー → `Ctrl+.` → *Split section A in the other parts to match vn1…*。
      確認の UI に計画（vn2 の A を 2 小節で A, B に割る・form は A → A B）が出て、**Apply** で vn2 が `section A` `section B` になり、form が `A B`、警告が消える。
- [ ] 同じ確認で **Cancel** → 何も変わらない。
- [ ] `Lily#: Regroup (by part ⇄ by section)` を往復して本が同じ絵に戻る（プレビューが動かない）。

### 1.4 SHOTLIST の 9 本（ユーザーが録る）

- [ ] clip 4 audition: 矢印キーで音符に乗るたびに 1 度鳴る・打った `a` が鳴る（Alt+P 長押し・Alt+M で小節）。
- [ ] clip 5 preview: 打鍵ごとに譜が伸びる・カレットの音が光る・選択範囲が光る・プレビューのクリックでエディターが飛ぶ。
- [ ] clip 6 quick fix: *Add 1 bar line to bass (|)* で警告が消え、埋まった小節が描かれる。
- [ ] clip 9 AI transform（Copilot のサインインが要る）: 候補が並んで描かれ、After/Before が切り替わり、Accept で本が変わり Ctrl+Z で戻る。
- [ ] 録った mp4 を `site/videos/NAME.mp4` に置き、ページの `data-pending` を外す（SHOTLIST の手順）。

## 2. 0.11.0 で入ったもの（`editors/vscode/CHANGELOG.md` 0.11.0）

### 2.1 `@todo` と元の画像（OMR の本が無くても見られる分）

- [ ] `c4@todo` / `c4@todo("F♯ or F♮?")` が Problems に LYS4026 で出る。プレビューでは**符頭が人の手で描いたような赤い輪で囲まれ**（塗りではない・線は太め・始点を少し越えて閉じる・2026-10-10 のユーザー決定）、memo のあるものは**段の上の余白に小さめの赤い文字**（箱なし）で memo が出て、**音符の x から余白の広い側へ一定量ずらした所**に置かれ、**文字には下線**、輪から下線の中央へ引き出し線（輪・下線・線は同じ太さ 0.2）。`.down` の memo は文字の上の中央につなぐ（線が文字を横切らないため）。`.down` で memo が段の下。**和音は全体を 1 つの輪**、休符も輪、`R1*N` は棒全体を輪。**プレビューでは memo の置き場が段組みに予約され、memo が頁の持つ余白に収まらないときだけ次の段と頁が下がる**（`Untitled-2.lys` は 1 空間も増えない・1 段に 6 本書いた `one.lys` は段が下がる＝第899〜900・2026-10-10 のユーザー決定「他の要素を動かしても良い・整った形に」・2026-10-11 の報告「余白が無駄に広い」）。**書き出し（PDF / PNG / SVG）の段組みは memo が無いときと同じ**（下の行）。その帯の中で **memo は他のインク（コードネーム・強弱・加線の音符・先に置いた memo）と重ならない所へ逃げる**＝音符の反対側が空いていればそちらへ回り（ページの余白も使う）、両側ふさがっていれば上（`.down` は下）へ登る（第896・ユーザー要望「重なると読めない」「音符の近くの余白に」）。**同じ段の memo は短いものから順に、全部の左右の組み合わせを試して一番安い並び**（登りが少なく・引き出し線が他の memo の文字を通らず・箱が他の線の上に乗らない）**で描く**（第898〜899）＝隣り合う 2 音に memo を書いても（`c''4@todo("first memo here") d''4@todo("second memo here")`）、1 段に 6 本書いても（Lab `sessions/p896/probe/one.lys`）、どの引き出し線も他の memo の文字を通らない。**カレットをその音符に置くと輪はハイライト色の線になり、中は透過のまま**（塗られたら第892 の再発）。`lilysharp.preview.highlightTodos` を切ると輪・吹き出しの箱・線が消える（memo の文字は黒で残る）。
- [ ] 同じ本を PDF / PNG / SVG に書き出すと、輪も吹き出しも無く、memo のぶんの余白も無い（書き出しは memo が無いときと同じ段組み）。
- [ ] クイックフィックス *Resolve this TODO* / *Resolve all N TODOs* で `@todo` が消える（メモごと）。
- [ ] `Lily#: Show Original Scan` は `x.omr.json` の無い本では出ない（コマンドパレットの when）。
- [ ] 音符の後ろで `@` → 補完から `todo` を選ぶ → `@todo("")` が入り、カレットが `""` の間に来る（`text`・`mark` と同じ・2026-10-10 のユーザー要望）。そのまま Tab／Esc で抜けても `@todo("")` は警告 LYS4026 を出し、エラーにはならない。
- [ ] `c4@todo("comment")` の `)` の後ろで `.` を打つ → 補完に `down`（先頭）と `up` が出て、選ぶと `@todo("comment").down` になる。`)` の直後で Ctrl+Space でも `.down`／`.up` が出る。`@text("dolce").` は `up` が先頭、`@fermata.` も `.` で開く。**付点の `c4.` では何も開かない**（`.` は配置のときだけ答える・2026-10-11 のユーザー要望）。⚠️ Reload Window が要る（トリガー文字はサーバの起動時に決まる）。

### 2.2 コード図まわり

- [ ] `layout { chordDiagrams … { … } }` の表の中で補完が、本に出てくるコード名のうち表に無いもの → 調のコード → `section` → section 名の順に出る。
- [ ] 表の中の `section NAME`・調弦の語・形が色づき、表の `}` の後ろの `layout` の鍵も色が続く。
- [ ] 同じ part を capo の違う 2 つの score が弾く本で `chord(…)` をホバー → score ごとに 1 行、capo の下で鳴る音名が書かれる。

### 2.3 プレビューの Export と再生

- [ ] picker を `score practice`（form が section B から始まる）にして Export → MIDI が B から始まる（`lysc midi --score practice` と同じ）。
- [ ] Export の PDF / PNG が選んだ score の絵（score の名前を変えた直後でも、名無しの絵にならない）。SVG にフォントが入っている。
- [ ] MIDI / LilyPond の Export の警告がエディターの通知に出る。
- [ ] 1 part だけの score を選んで Play → その score の part と form だけが鳴る。
- [ ] form が 6 回弾く section の中で音を打つ → プレビューの 6 回全部が描き直る。

#### 2.3.1 SoundFont の音源（第901・`docs/playback-design.md`）

- [ ] プレビューを開くと出力チャネル「Lily# Extension」に `Preview synth loading: …` の行が段階ごとに出る（processor を足す→synth を作る→音源を fetch）。数秒で `Preview synth ready: 38.1 MB in … ms: …MuseScore_General.sf3` が出る（AudioContext は最初のクリックまで suspended だが、worklet は suspended でも答える＝第901 実機 2 回目で確認。出なければプレビューを 1 回クリック）。`failed` なら理由がその行にある。読み込み中に ▶ を押すと「Loading the sound font…」と出て、間に合えば音源で、8 秒で来なければ従来の音で鳴る。
- [ ] ▶ で楽器の音（ピアノ・ギター等）が鳴る。`part vn { midiInstrument "violin" }` と `part kit { clef percussion }` の本で、vn はバイオリン・kit はドラムの音。
- [ ] 密な本（和音の多いピアノ譜・4 part 以上）で、途切れ・歪みが無い。⏹ を押すと直ちに止まり、後から音が漏れない。▶ を押し直す（seek）と頭から正しく鳴る。
- [ ] プレビューのタブを隠して（別のタブを前に出して）も再生が続く。
- [ ] Alt+P を長押し → その part の楽器で鳴り続け、離すと止まる。Ctrl+Shift+↑ の後の音も同じ楽器。
- [ ] 設定 `lilysharp.playback.soundFont` に別の .sf2/.sf3 の path を入れる → 開いているプレビューの音が替わる（Reload 不要）。存在しない path → 出力チャネルに `does not exist` の行、音は同梱のまま。空に戻す → 同梱に戻る。
- [ ] `media\soundfont` を空にして（実験）Reload → 出力チャネルに `Preview synth none`、▶ は従来のオシレーターの音で鳴る。
- [ ] part の `{ }` の中で Ctrl+Space → `midiInstrument` を選ぶ → `midiInstrument ""` が入ってカーソルがクオートの中、そのまま 128 の楽器名（GM 順・家族名つき）が出る。`electric gui` まで打って `electric guitar (clean)` を選ぶと `"electric guitar (clean)"` になる（`"electric electric …` にならない）。`midiInstrument ` と手で打って Ctrl+Space → クオート付きの名前が入る。

### 2.4 エディターの小さな直し

- [ ] part の下に書いた section header に `key` / `partial` / `time` / `tempo` を打つ → 音を触らなくてもプレビューが新しい header になる。
- [ ] `key` を途中まで打ってもプレビューが止まらない。段の調号をクリックすると、編集の後でも `key` を書いた場所へ飛ぶ。
- [ ] `key custom fis cis` の本でアウトラインとパンくずが出る。
- [ ] 文字列: `"say \"hi\""` の `\"` が文字列の色のまま・`@"…"` は逐語・`"\^"` は LYS0036 の印。
- [ ] section の音楽に `segno` を書くと LYS1034 のエラー。音楽の補完に `segno` `coda` `fine` `dc` `ds` が出ない。

## 3. Unreleased（今の拡張で見られる）

### 3.1 名前の無いブロック・score の名前

- [ ] `score { … }` と `score tab { … }` の本: picker に **(Default)** と **tab**。何も選んでいないとき (Default) が描かれる（tab が先に書いてあっても）。
- [ ] `score "tab"` → LYS0038、クイックフィックスで `score tab` になる。`score tab "both"` → 文字列を消すフィックス。
- [ ] section があって `score` の無い本 → LYS6014、クイックフィックスが score（chords の行・part ごとの staff・lyrics）を末尾に足し、本の改行に合う。
- [ ] `form` / `score` のスニペットが名前の無いブロックを書く。score の中で `form |` と打つと form の一覧が補完に出る。
- [ ] Export のファイル名: 名無しの score は `<本>.svg`、`score tab` は `<本>-tab.svg`（Explorer の batch export も同じ）。
- [ ] `part bass { }` と `staff bass click` → part bass の譜で click は MIDI だけ、LYS1046 の警告が綴りを案内する。
- [ ] `section A2 "A'" { }` を top level に書くと `form { A2 }` が A' を印刷。part の中の section にラベル → LYS0039。2 つの宣言でラベルが違う → LYS1045。

### 3.2 音楽フォント（SMuFL）

- [ ] `fonts { music "Bravura" }` → プレビューが Bravura で描く（音部記号の形で分かる）。`"Petaluma"`・`"Leland"` も。
- [ ] `fonts {` の中で `music` が補完に出て、値に引用符つきで `Emmentaler` `Bravura` `Leland` `Petaluma` が出る。`music` の鍵が色づく。
- [ ] `music "NoSuchFont"` → LYS8019 の警告に探した場所が全部並ぶ。絵は Emmentaler のまま。
- [ ] `music "Petaluma" "Bravura"` の鎖で、Petaluma に無いグリフが Bravura から描かれ、警告が 1 グリフ 1 回。
- [ ] Petaluma の本: 題名・コード名・リハーサル記号・`@text`・テンポ・強弱の言葉・楽器名が Petaluma Script、歌詞は彫刻の書体のまま。
- [ ] Petaluma の `form { Introduction "Intro" }` の枠が文字にぴったり（WOFF2 を組み直したので **カーニングが効く**＝以前は枠からはみ出した）。
- [ ] `form { Introduction "ほえほえ" }` の枠がかなの高さで、細い帯にならない。
- [ ] Petaluma の `time 4/4` で分子と分母が重ならない。
- [ ] `lilysharp.fontPaths` に自前のフォルダを書くと、そこの SMuFL フォントが名前で見つかり、**設定を変えた瞬間に開いているプレビューが描き直る**。
      `LILYSHARP_FONT_PATH` でも同じ（VS Code を環境変数つきで起動）。

### 3.3 Markdown の fence

- [ ] Markdown の ```` ```lily# ```` に score が 2 つある本を丸ごと貼る → 既定の score が描かれる（拒まない）。score の無い fence は LYS6014 で拒まれる。

## 4. §2 R13 ⒝ — 差分頁の配信（実機未確認・第404）

- [ ] 出力チャネル「Lily#」を開いたまま、1 頁の本で音を 1 つ打つ → `pages kept` の行（頁が丸ごと届き直していない）。
- [ ] 複数頁の本（10 頁級）の途中の頁で音を 1 つ打つ → 編集した頁だけ `changed`、後ろの頁は `shifted`（`requestFull` が出ない）。
      `PREVIEW update` の ms を見る（shifted 9 頁 × 1000 要素の DOM 書き換えの実測＝HANDOFF が求めている数字）。
- [ ] 頁の高さが動く本（auto-size の fixture の形）では `requestFull` で全頁が届き直す＝その頻度（HANDOFF R13 ⒝′ の判断材料）。
- [ ] 小節が増える編集で、後続の頁が小節番号の重ね描きだけの違いで丸ごと届くか（⒝ 群単位の item の値付け）。

## 5. OMR 連携 B1〜B6（`LilySharp-Omr` が要る・HANDOFF §1.0「B1〜B6 は VS Code で未確認」）

- [ ] **B1** `Lily#: Import from Image/PDF…`: コマンド・Explorer の右クリック（png/jpg/tif/pdf）・エディターへのドラッグ＆ドロップ・画像の貼り付けの 4 経路で
      `lilysharp.omr.path` の OMR が走り、進み具合が通知に出て、終わると `.lys` とプレビューと scan が開く。OMR が無いと設定を案内する。
- [ ] **B2** `x.omr.json` のある本で `#キー` 付きの `@todo` に、候補（`fis'8` / `f'8` …）がクイックフィックスとして出て、選ぶと音が置き換わり `@todo` が外れる。候補の無い todo は *Resolve* だけ。
- [ ] **B3** `Lily#: Show Original Scan`: 頁画像の上に todo の枠が赤、小節の枠がクリックできる。枠のクリック → `.lys` の `@todo` / 小節の行。
      `.lys` のカレット → 対応する枠が光ってスクロール（編集で行がずれても追う）。プレビューのクリックでも同じ。
- [ ] **B4** *Scroll with the preview*（既定で入）: scan とプレビューが小節ごとに一緒にスクロール。`@todo` のある小節がプレビューで赤い破線の枠。
      `paper { breaksOnly }` の本で段組みが元と揃う。
- [ ] **B5** `lilysharp.omr.verifyOnSave` 入 → 保存で `omr verify` が走り、scan とクイックフィックスが更新される（**OMR 側の `verify` 実装待ち**。無ければ呼ばれないことを確かめる）。
- [ ] **B6** `@todo` を含む範囲を選んで Ctrl+I → プロンプトに todo のメモ・確信度・候補が入っている（Output / デバッグで見る）。直した印は `@todo` が外れ、直さない印は残る。

## 6. 結果の書き方

- 通ったら `[x]`。違ったら **番号・手順・見えたもの・期待したもの**を HANDOFF §1.0 ⒜ に 1 行で起票する（例: `1.1 開放弦の輪＝塗られている`）。
- 録画は SHOTLIST の仕様（1280×720・5 MB 以下・5〜20 s）。
