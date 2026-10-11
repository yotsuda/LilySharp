# 学習セット（`lesson-…`）と Pages の整合

> 2026-10-11（第902）起票。**ユーザー決定**: 「ユーザーを増やすため、template-twinkle と同じ仕組みで
> 段階的に Lily# を学べるセットを作る」→ 計画 10 本を承認（「あなたの提案通りに進めて」）。
> 追加の指示 3 つ: ⑴ **最初の段階で VS Code の補完入力を紹介する**（補完があれば自力で機能を発見できる）／
> ⑵ **説明を gh Pages と整合させる。Pages の過不足は記録して、あとで Pages を直しやすくする**／
> ⑶ **gh Pages の構成も大きく改善したい**（html のサイズが不揃い・分割と章立てが良くない）／
> ⑷ **レッスンは多言語化を視野に入れる**（英語版の完成度が上がったらローカライズ）。

## 1. 仕組み

- **補完 item**: トップレベルの Ctrl+Space に `lesson-NN-slug` を出す。実体は `template-twinkle` と同じ
  `ScoreTemplateItem`（no-op の edit ＋ `lilysharp.applyScoreTemplate`＝**ファイル全体を置き換える・中身があれば訊く**）。
  拡張側の変更は要らない（本文は引数で渡る）。
- **出し方**（`LessonItems`）: 打ちかけの語が**空なら lesson-01 だけ**（一覧はもう長い・入口は 1 本でよい）、
  `lesson`／`learn` に向かう語なら**全部**、それ以外の語なら出さない。client は語の 1 文字目で 1 回だけ訊き、
  その後は手元で絞るので、`l` で 10 本が来て `ly`（lyrics）・`la`（layout）で自然に消える。
- **本文はファイル**: `LilySharp.Lsp/Lessons/<言語>/lesson-NN-slug.lys` を EmbeddedResource（csproj の
  `Lessons\**\*.lys`）。順序はファイル名。1 行目 `// Lesson N of 10 — Title` が item の detail になる。
- **言語**: LSP `initialize` の `locale`（VS Code は表示言語を送る）→ `LessonsFor(locale)`＝英語の全セットを土台に、
  `pt-br` → `pt` の順で訳を探し、**訳の無いレッスンは英語のまま出す**（欠けて見えない）。
  **ラベル（slug）は全言語で同じ英語**＝レッスンの身元で、本文の「NEXT: `lesson-02-notes`」もこれで引く。
- **網**: `LessonTests`＝全言語の全レッスンが**エラーも警告も無く**コンパイルされ描ける／訳はすべて英語に同名がある／
  Lesson 1 が「Ctrl+Space でこれが出る」と書いた項目が実際の補完経路で出る（補完の仕様が変わったらレッスンの記述が
  古くなったことを赤で知る）。

## 2. レッスン一覧（英語版）

| # | ラベル | 新しく出るもの | 曲（すべて PD） |
|---|---|---|---|
| 1 | `lesson-01-completion` | **STEP 0＝プレビューを開く**（Ctrl+K → V・エディタ右上のアイコン・▶）→ 補完ツアー: トップレベル（`key`→`g`→`major`）・part の中（`instrument`→`cello`＝clef が自動でヘ音記号になる・part に clef は書かない）・`@todo` を 1 つ消す（赤い丸が消える＝以後の課題の印の扱いを覚える）・`@` の一覧・休符の小節を `Cmaj7`→`<c e g b>`（`C` は音名 c と紛れるので避けた・G major でも出る）・form を `form { |: ~A :| }` に書き換え・squiggle にマウスを乗せる | Mary Had a Little Lamb |
| 2 | `lesson-02-notes` | 音名・音価・`\|`・相対オクターブ `'` `,`・ファイルの 4 要素・**Ctrl+Shift+Up/Down**（`g2` を Down で `g,2`、後ろの音まで戻る）・**数字キー**（`d4` の d の前で `2`＝音符のどこで打っても音価に入る。2026-10-11 ユーザー指示「キーボードが上手な人ほど永久に気づかない」） | Ode to Joy（Beethoven） |
| 3 | `lesson-03-rhythm` | 付点・16 分・3/4・`partial`・（TRY で休符・タイ・数字キー） | Happy Birthday to You（Hill） |
| 4 | `lesson-04-keys` | `is`/`es`・「鳴る音を書く・調は印刷だけ」・`key` と旋法・**`score low transpose bes,`＝同じ曲を別の調で印刷**（2026-10-11 ユーザー指摘「レッスンに transpose がない」） | Minuet in G（Petzold・右手） |
| 5 | `lesson-05-expression` | 強弱・ヘアピン・スラー・`@tenuto` `@fermata`・`@rit`…`@!rit` | Silent Night（Gruber・6/8） |
| 6 | `lesson-06-form` | section・form・`\|: :\|`・1 番 2 番括弧・`~`・**同じ名前＝同じ時間** | Jingle Bells（Pierpont・サビ） |
| 7 | `lesson-07-lyrics` | `lyrics … sings`・ハイフン・`\|`・2 言語の 2 行 | Frère Jacques（英・仏） |
| 8 | `lesson-08-chords` | `chords` 行・リードシート・（TRY で Ctrl+Shift+↑ のダイアグラム・`chordDiagrams all`） | Evening Walk（オリジナル） |
| 9 | `lesson-09-piano` | 大譜表・`octave 3`・`phrase`・（TRY で和音・3 連符・2 声） | Minuet in G（両手・左手は簡略） |
| 10 | `lesson-10-band` | **移調楽器**（`pitch concert`＋`instrument trumpet`＝書く文字は実音、トランペットの段は全体の楽譜でも `score trumpet` でも奏者の調＝ニ長調で印刷、鳴る音はハ長調のまま。2026-10-11 ユーザー指摘で全体の楽譜の `score pitch concert` を外した）・`tab`/`tuning bass`・`clef percussion` とドラム・score を 3 つ（`midiInstrument` は TRY の補完だけ） | Saturday（オリジナル） |

曲は**音を確実に書けるもの**に絞った（初案の Oh! Susanna・Scarborough Fair・Brahms・Clementine・Saints は、
旋律や歌詞の割り付けを確かめられないので差し替えた）。Lesson 4 と 9 は同じ曲＝「4 で書いた曲に 9 で左手を足す」。

## 3. 書き方の規約（Pages と揃える）

- ★★★ **文章は可能な限り少なく**（2026-10-11 ユーザー指示「文字が多いと、ユーザーは読むことを諦めてしまう」）。
  型: 1 行目の題 → 2 行目 `// Preview: Ctrl+K V` → 新しいことを **2〜4 行**（例を 1 つずつ）→ 楽譜（新しい構文の行だけ
  行末に短い註）→ `// TRY:` **1〜3 項目を 1〜2 行で** → `// NEXT — type `lesson` on a new line and pick …`。
  説明は書かず、触らせて見せる（hover・squiggle・▶ が説明を担う）。
- ★★ **Lesson 1 は最初にプレビューを開かせる**（同日ユーザー指示「ないと、ユーザーは楽譜を一切閲覧できない」）。
  全レッスンの 2 行目にも開き方を置く（途中から始めた人のため）。
- ★★ **音符を指す課題は `@todo("…")` をその音符に付ける**（同日ユーザー提案「at the end of line 1 は五線の 1 行目か
  ソースの行か分からない。`@todo("here")` とか書いておくのはどうだろう」）。プレビューが音符を赤い手書きの丸で囲み、
  メモを譜面の上に出す＝**どこを触るかを楽譜の上で示せる**。メモは 28 字以内の命令形（`make it d4 d`・
  `delete the comma`）。TRY 行は「赤い丸の指示どおりにして、`@todo(…)` を消す」だけ。音符の外の課題（key・form・
  歌詞・コード行）は `@todo` を付けられないのでコメントのまま。網は LYS4026 だけを許す。
- 1 レッスン＝**そのまま鳴る完全な 1 ファイル**。インデントは 2 空白・タブ無し・LF。
- **相対オクターブで書く**（Pages の推奨・TUTORIAL・ギャラリーと同じ）。
- ファイルの形: **パートが 1 つなら by part**（`part melody { clef treble  section A { … } }`・2026-10-11 ユーザー指示。弱起は part の外に `section A { partial 4 }`）、複数パート（Lesson 9・10）は by section（`section A { rh { … } lh { … } }`）・
  `form { ~A }`・`score { staff melody }`。コード行は譜の上（`chords prog  staff melody`）、歌詞は下。
- 用語は Pages のもの: Lily#（LilySharp と書かない）・part／section／form／score・row・chord row・lyrics track が
  part を **sings**・pickup・the preview。音名はオランダ式（`fis` `bes`）。

## 4. 多言語化の手順（英語版が固まってから）

1. `LilySharp.Lsp/Lessons/<言語>/` に**英語と同じファイル名**で置く（例 `Lessons/ja/lesson-01-completion.lys`）。
   一部だけでもよい（残りは英語で出る）。
2. 訳すのは**註だけ**。音楽・識別子（`melody` `A`）・キー名（Ctrl+Space）・補完で選ぶ語（`key` `g` `major`）は
   そのまま。曲名は 1 行目の title だけ訳してもよい（`title "…"` は譜面に出るので原題を推奨）。
3. `LessonTests` が全言語を自動で拾う（コンパイル・同名の英語版があること）。
4. ⚠️ 未対応: item の detail の「REPLACES the whole file (it asks first)」と、拡張の確認ダイアログの文言は英語のまま
   （拡張の UI 文言はまだどれも英語＝拡張全体の l10n と一緒に）。

## 5. Pages の監査（2026-10-11・第902・読むだけの監査）

**ビルド**: `site/build-site.ps1` が ① `build-examples.ps1`（本文中の例は**このスクリプトの here-string が正本**で
`examples/*.lys` は生成物）② ギャラリー 7 曲を `lysc check`/`svg` ③ `build-manual.ps1`（`manual-body.html`→
`grammar.html`・`chords-body.html`→`chords.html`・`editor-body.html`→`editor.html`）と `build-preview.ps1`
（**`index.html` の本文はこのスクリプトの here-string**）④ 検査（参照切れ・版番号・`{{…}}` 残り・`score` と `form` を
含む `<pre>` は全部コンパイル）⑤ `_site/` へ。`.github/workflows/pages.yml` が master への push で回す。

**大きさ**（行）: `manual-body.html` 1188（全散文の約 6 割・25 章が 1 頁）／`editor-body.html` 419／
`chords-body.html` 290（**一番形の良い頁**＝手順で読める）／index の template 約 260。
章の大小も偏る: `#chordrows` 約 115 行・`#parts` 約 95・`#paper` 約 70 に対し `#phrases`・`#override`・`#tab` は数行、
index の「03 Speed」は見出しだけ。

## 6. Pages の過不足の台帳 ← **Pages を直す便はここから**

レッスンを書くたびに、Pages に無い／違う／重複しているものをここへ足す。**閉じたら消す。**

### 6.1 無いもの

- P1 **初心者の入口が無い**: 「最初の 1 曲」の頁が無い。`grammar#glance` は 2 章目でいきなり fonts／paper／layout 入りの
  骨格を見せる。`docs/TUTORIAL.md` はサイトに載っていない。
- P2 （一部済 2026-10-11: editor 頁の鍵の表と `#language` に Ctrl+Space。残りは learn-01＝Lesson 1 の手順そのもの）**Ctrl+Space という語がサイトのどこにも無かった**。補完は `editor#language` の 1 項目と `grammar#chordrows` の
  「What completion offers」（コードだけ）のみ。`@` の一覧は typing aids の表で触れるだけ。⇒ Lesson 1 の内容が丸ごと無い。
- P3 **章の無い基本**: 調号と臨時記号・拍子・テンポ（`#glance` の註と parts の表だけ）・音部記号（表の 1 行）・
  弱起 `partial`（`#sections` の pre の註だけ。曲中の `| partial 2. r2. |` と `time none` は皆無）・強弱とヘアピン
  （`#anno` に混在）・`using "other.lys"`（LilyPond 対照表だけ）。
- P7 レッスン（`lesson-…`）の案内: editor 頁 `#language` は済（2026-10-11）。`start` 頁は再構成で。
- P13 **「まずプレビューを開く」が入口に無い**。Ctrl+K V は「Ctrl+K を離してから V」の和音キーで、初心者は押し方を知らない。
  エディタ右上のアイコンでも開ける。start 頁の 1 段目に置く（Lesson 1 の STEP 0 と同じ文）。
- P14 **文章量**: レッスンは「文章は可能な限り少なく」がユーザーの指示（§3）。learn 頁も同じ型（2〜4 行＋例＋TRY）で書き、
  詳しい説明は Reference に置く。今の manual は 1 章が長く、learn に転用するなら大きく削る。

### 6.2 違うもの・食い違い

- P8 **オクターブの方針が自己矛盾**: `grammar#pitch` は「相対で書け、絶対は生成器や AI 向け」と言うのに、本文の例 23 のうち
  18 が `octave absolute` で始まる（ギャラリー・TUTORIAL・editor の例は相対）。
- P11 TUTORIAL は `part melody`（括弧なし）、サイトは常に `part melody { clef treble }`。（どちらも正しい＝誤りではなく揃え方の問題。再構成で TUTORIAL を吸わせるときに揃える）
- P12 綴りの混在: harmonise／colour の隣にコマンド名 auto-harmonize。

### 6.3 重複（1 か所に寄せる候補）

- コードの綴り規則（`+`/`-`、`#`/`b` は使わない）が 4 か所: index の Sketch カード・index の Grammar・`grammar#chordrows`・`chords#names`。
- 「3 つの考え方」（音楽は part の中・順序が意味を持つ・反復は form）: index の Grammar・`grammar#read`・Air in D のカード。
- Ctrl+Shift+↑↓ でコードの形: index の柱 05・`chords#editor`・`editor#step`。
- インストール: index と `editor#install`。LilyPond との違い: index の Lineage と `grammar#lilypond`。`lysc` の使い方:
  `editor#cli` と TUTORIAL の「Output Formats」。

### 6.4 分け方・ナビ

- コード行は manual、ダイアグラムは chords.html（`grammar#layout` の chordDiagrams 系の行は chords へ飛ばすだけ）。
  tab（`grammar#tab`）が `chord(…)`（`chords#notes`）から遠い。`repeat unfold/percent/tremolo` が `#groups` にあり反復記号は `#forms`。
  CLI と Markdown が「Editor and AI」頁。Lineage・Licence・Architecture が宣伝頁。
- index に上部ナビが無く、読み物 3 頁のナビは index を載せない（ブランド名だけが home）。manual の目次が上部ナビを繰り返す。
  index の眉の番号が「01 Engraving / Lineage / 02 Grammar」と混在。manual の 01〜25 は手打ち（章を動かすと壊れる）。
- 確かめた頁間アンカーはすべて解決する。`README.md` が `grammar.html` を指す＝URL を残すか転送する。

## 7. Pages の再構成案（ユーザー判断待ち）

上部ナビを**全頁で生成**（index も）: **Home · Get started · Learn · Reference · Chords · Editor · About**。章番号は生成。
大きさは chords.html を 1.0 として:

1. `index.html` Home（約 0.7）: ヒーロー・3 段のインストール＋「Get started →」・6 本の柱・ギャラリー・短い AI。
   Lineage／Licence／Speed／Architecture は About へ。Grammar 節は抜粋＋Learn への導線に縮める。
2. `start.html` Get started（約 0.4）: インストール（index と `editor#install` を統合）・プレビュー（Ctrl+K V）・最初の 1 曲・
   「`lesson` と打って Ctrl+Space」。
3. `learn-01.html`〜`learn-10.html`（各 0.5〜0.8・前後リンク）＝**拡張のレッスンと 1 対 1**。移す元: 01←`editor#language`
   `#typing`（要約）`#step` `#audition`・`grammar#chordrows` の補完 h3／02←`#pitch` `#duration` `#glance`（簡約）／03←
   `#duration` `#events` の休符 `#connect` のタイ `#sections` の partial `#bars`／04←調と臨時記号（新規）・`#transposing` の score の `transpose`／05←`#anno` を
   割る `#connect`／06←`#sections` `#forms`＋P4／07←`#lyrics`／08←`#chordrows` の前半・slash notes・chords.html への入口／
   09←`#scores` の grouping・`#groups` `#phrases`／10←`#tab` `#parts`（instrument・midiInstrument）・`#transposing`（移調楽器と `pitch concert`）・ドラム（新規・P5）・複数 score。
4. Reference（`grammar.html` を割る。URL は入口として残すか転送）: `ref-music.html`（約 1.5）・`ref-structure.html`（約 1.5）・
   `ref-output.html`（約 1.2）・`ref-lookup.html`（約 0.8＝予約語・診断・LilyPond から・Rough edges）。
5. `chords.html` はそのまま（`#check` を末尾へ）。`editor.html`（約 1.2・Ctrl+Space を正面に）。`ai.html`（約 0.5）。
   `about.html`（約 0.6・Lineage／Licence／Architecture／Speed／CLI）。
6. ビルド: index を `build-manual` の頁一覧に入れるか上部ナビを共通化・learn の前後ナビを生成・TUTORIAL の内容を
   start／learn-02 へ吸わせて漂流を止める（TUTORIAL は Pages へのリンクに縮める）。

## 8. 書いている途中の気づき（製品側・Pages とは別）

- 歌詞の行を 2 本重ねると、2 言語（`lyrics en`／`lyrics fr`）でも行頭に `1.` `2.` が付く（Lesson 7 の絵）。
  言語違いの 2 行に番号は不自然かもしれない＝**ユーザー判断**（行に番号を付けない書き方が要るか）。
- **hover が薄い**（2026-10-11 ユーザー指摘「hover any word は何がおこるかわからないし、実際何もおきない」）:
  `LanguageReference.Hover` は音符（`**Note**: e / Octave offset / Duration`）・休符・小節線などの構文の名前を返すだけで、
  `@mf` などの印・`clef`／`instrument`／`key` の語・part の名前には何も返さない。⇒ Lesson 1 は hover の案内をやめ、
  確実に動く「squiggle にマウスを乗せる」を具体的な手順で書いた。**hover を「その語が何をするか」まで厚くする**のは
  製品側の改善候補（ユーザー判断）。厚くしたらレッスンで案内し直す。
- **`@todo` のメモで見つけた製品の欠陥（直した）**: メモのある譜面で拍子が数字（3/4・6/8）だと、プレビューが
  `the music font has no box for timeSig3` で落ちて何も描かなかった（`InkRecorder.PutGlyph` が字形の design box を
  読み、拍子の数字には outline box しか無い）。4/4 は C の字形なので通っていた。outline box で測るよう直した
  （`TodoCalloutTests.AMemo_InAScoreWithANumericMeter_StillRenders`）。
- 大譜表の**下の段**の音符に付けたメモは、上の段の上へ出て引き出し線が上の段を横切る（Lesson 9）。読めるが、
  段の下に置けるならその方が近い＝メモ配置の改善候補（ユーザー判断）。
- ドラムの 2 声で、**下の声のスネア（`sn4`）の符幹が上を向く**（`voice { hh8 … } { bd4 r8 bd sn4 bd | }`・2026-10-11 に文書の例を描いて気づいた）。
  下の声は符幹が下のはず＝製品側の確認候補（未調査）。
