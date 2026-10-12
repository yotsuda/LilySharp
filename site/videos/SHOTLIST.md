# サイトのデモ動画 — 撮影リスト

各ページには、下の動画ごとに隠れたプレースホルダが置いてある:

```html
<figure class="demo-video" data-pending>
  <video src="videos/NAME.mp4" …></video>
  <figcaption>…</figcaption>
</figure>
```

`data-pending` の付いた figure は表示されず（`display:none`）、`build-site.ps1` もそのファイルを
探さない。**動画を公開するには:** ここに `site/videos/NAME.mp4` として保存し、ページの元
（`site/*-body.html`。Learn ページだけは `site/build-manual.ps1`）にあるその figure から
`data-pending` を消す。以後はファイルが無いとビルドが止まり、あれば `_site/videos/` に写される。
このファイル自体は公開されない（ページが参照するファイルだけが出る）。

音のある動画は `controls`（自動再生しない。ブラウザは音のある自動再生を許さない）、音の無い動画は
`muted loop playsinline autoplay`。各プレースホルダの上の HTML コメントにも、その撮り方を書いてある。

## 収録の仕様

- MP4・H.264（横に `.webm` を置くのは可。その場合は `<source>` を 2 つ目に足す）、およそ 1280×720、
  30 fps、**5 MB 以下**。1 本 5〜20 秒。前後の無駄な時間は切る。
- VS Code はプレビューを右に（`Ctrl+K V`）、テーマは明るいもの（ページは印刷した紙面を明るく見せる）、
  ソースが動画の大きさで読めるように拡大（Ctrl+= を 1〜2 回、または `"editor.fontSize": 18`）。
  ミニマップとサイドバーは隠す。
- キー操作が主の動画では打鍵を画面に出す（例: VS Code の *Screencast Mode* —
  *Developer: Toggle Screencast Mode*）。
- 下のスニペットはすべて `lysc check` で確かめてある（1〜9 は Lily# 0.9.0 ＋ 未リリース分、10〜15 は
  0.11.0 ＋ 未リリース分）。「警告あり」と書いた動画は、その警告を見せるのが目的。
- メニュー名・メッセージ・画面の文字は英語のまま書く（拡張の表示が英語のため）。

## 動画の一覧

| # | ファイル | ページ — 節 | 音 | 長さ |
|---|---|---|---|---|
| 1 | `editor-octave-mark.mp4` | editor — Typing aids | なし | 約 10 秒 |
| 2 | `editor-duration-overwrite.mp4` | editor — Typing aids | なし | 約 10 秒 |
| 3 | `editor-step-octave.mp4` | editor — Stepping | **あり** | 約 15 秒 |
| 4 | `editor-audition.mp4` | editor — Audition | **あり** | 約 10 秒 |
| 5 | `editor-preview.mp4` | editor — The preview | なし | 約 15 秒 |
| 6 | `editor-quickfix.mp4` | editor — Completion, hover, fixes | なし | 約 10 秒 |
| 7 | `editor-split-sections.mp4` | editor — Commands | なし | 約 20 秒 |
| 8 | `chords-shape-step.mp4` | chords — Let the editor write the shapes | **あり** | 約 15 秒 |
| 9 | `ai-transform.mp4` | editor — AI in the editor | なし | 約 20 秒 |
| 10 | `start-install.mp4` | Get started — Install | なし | 約 20 秒 |
| 11 | `start-first-score.mp4` | Get started — A first score | **あり** | 約 25 秒 |
| 12 | `learn-lesson.mp4` | Learn — レッスンの一覧 | なし | 約 20 秒 |
| 13 | `commands-guess-key.mp4` | Editor commands — Guess Section Key | なし | 約 20 秒 |
| 14 | `commands-extract-phrase.mp4` | Editor commands — Extract Phrase | なし | 約 15 秒 |
| 15 | `about-speed.mp4` | About — Why the preview keeps up | なし | 約 15 秒 |

**一部だけ撮るなら 11・12・13 を先に**（2026-10-12 追加）: 11 と 12 は初めての人が最初に見るページ
（Get started・Learn）、13 は静止画では説明できないコマンド。10 と 15 はあれば良い程度。

1〜5 は次のファイルから始める:

```
part m { clef treble }
section A { m { c4 cis4. d8 e4 | c4 d4 e4 f4 | c4 e g <c e g>4 | g2 g | } }
form { ~A }
score { staff m }
```

### 1. `editor-octave-mark` — オクターブ記号は音名の後ろに入る
1. 1 小節目の `cis4.` の `c` と `is` の間をクリック。
2. `'` を 2 回打つ: `cis''4.` — 記号は `cis` の後ろ、`4.` の前に入る。キャレットは動かず、
   プレビューの音が上がる。
3. `,` を 1 回: `'` が 1 つ打ち消される（`cis'4.`）。
4. 1 小節目の末尾 `e4` の後ろにキャレットを置き `'` を打つ: `e'4`。

### 2. `editor-duration-overwrite` — 数字は音価を置き換える
1. 2 小節目の `d4` の直後にキャレット。`8` を打つ → `d8`（`d48` にはならない）。
2. `e4` の後ろで `2` → `e2`。
3. `f` と `4` の間で `1` → `f1`。小節が詰め直される（小節が長すぎる警告が出るが構わない。後で戻してよい）。
4. 任意: `cis4.` の後ろで `8` → `cis8.`（付点は残る）。

### 3. `editor-step-octave` — Ctrl+Shift+Up/Down（音あり）
1. 3 小節目頭の `c4` にキャレット。`Ctrl+Shift+Up` を 2 回（`c''4`）、`Ctrl+Shift+Down` を 1 回
   （`c'4`）— そのたびに鳴る。
2. `<c e g>` の中の `e` にキャレット: `Ctrl+Shift+Up` — その構成音だけが動く（`<c e' g>`）。
3. `>` の直後にキャレット: `Ctrl+Shift+Up` — 和音全体が動き（`<c e' g>'4`）、鳴る。

### 4. `editor-audition` — 動かすと・打つと音が鳴る（音あり）
1. 4 小節目（`g2 g`）の頭にキャレット。矢印キーで 3〜4 小節目を右へ — 着いた音が 1 回ずつ鳴る。
2. 4 小節目の末尾で ` a4 |` と打つ — `a` を打った時点で鳴る。

### 5. `editor-preview` — プレビューが文字に付いてくる
1. 音楽の末尾で ` a4 b c' d' |` と打つ — 1 打ごとに楽譜が伸びる。
2. 1 小節目に沿ってキャレットを動かす — キャレットの音が楽譜で光る。
3. 2 小節目を選択 — その小節の音がすべて光る。
4. プレビューの音符をクリック — エディタがそこへ飛ぶ。

### 6. `editor-quickfix` — 電球からのクイックフィックス（警告あり）
```
part melody { clef treble }
part bass { clef bass octave 3 }
section A {
  melody { c4 d e f | g1 | }
  bass { c1 | }
}
form { A }
score { staff melody  staff bass }
```
1. `bass` の波線にマウスを乗せる — *Section 'A' is not the same length everywhere it is written:
   2 bar(s) in part 'melody'; 1 bar(s) in part 'bass'…*
2. `Ctrl+.` → *Add 1 bar line to bass (|)*。警告が消え、プレビューに埋まった小節が出る。

（オーナーの案は `@upbow` → `@upBow` だった。大文字小文字違いの名前にはクイックフィックスが無く、
警告が *Names are case-sensitive: write '@upBow'* と言うだけなので、この動画は節の長さの修正を使う。）

### 7. `editor-split-sections` — Split Sections to Match a Part（警告あり）
```
part vn1 {
  clef treble
  section A { c'4 d e f | g1 | }
  section B { a4 b c d | e1 | }
}
part vn2 {
  clef treble
  section A { e'4 f g a | b1 | c4 d e f | g1 | }
}
form { A }
score { staff vn1  staff vn2 }
```
1. `A` の警告にマウスを乗せる（*4 bar(s) in part 'vn2'; 2 bar(s) in part 'vn1'*）。
2. `Ctrl+.` → *Split section A in the other parts to match vn1 (…)…*。
3. 確認ダイアログで止める（計画: vn2 の A を 2 小節目の後で分割 → A, B。form main: A → A B）。
   **Apply** をクリック。
4. vn2 が `section A` と `section B` を書き、form が `A B` になり、警告が消える。

### 8. `chords-shape-step` — コードの形を順に送る（音あり）
```
part m { clef treble }
section A { m { c'1@chord(Cm7) | f1@chord(F7) | } }
form { ~A }
score { staff m }
```
1. `Cm7` の中にキャレット。`Ctrl+Shift+Up`: `@chord(Cm7 x35343)` — ダイアグラムが出て和音が鳴る。
   ステータスバーに `Cm7: shape 1 of …`。
2. `Ctrl+Shift+Up` をさらに 3 回 — 形とダイアグラムが変わり、そのたびに鳴る。
3. `Ctrl+Shift+Down` で `x35343` まで戻し、もう 1 回: 形が消え、ダイアグラムも消える。
4. 任意: `F7` にマウスを乗せる — *Ctrl+Shift+↑ adds a chord diagram (guitar: …)*。

### 9. `ai-transform` — Transform Selection with AI
VS Code で GitHub Copilot（または別のモデル提供元）にサインインしている必要がある。
```
part m { clef treble }
section A { m { e'4 d c d | e e e2 | d4 d e d | c1 | } }
form { ~A }
score { staff m }
```
1. 1〜2 小節目を選択。`Ctrl+I`、*add a harmony line a third below* と打って Enter。
2. 元の楽譜の横に彫られた候補が出るのを待つ。After/Before を 1 回切り替える。
3. Accept — ファイルが変わる。`Ctrl+Z` 1 回で戻る（任意）。
待ち時間が長ければ切る。

### 10. `start-install` — 拡張機能ビューから楽譜まで
Lily# がまだ入っていない VS Code（または新しいプロファイル: *Profiles: Create Profile…*）。
ページの手順（初めて VS Code を使う人向け）どおりに、キーではなく**マウスで**操作する。
1. 左端の **Extensions** アイコン（四角が 4 つ、1 つ外れかけ）をクリック、`Lily#` と打ち、*yotsuda* のものの
   **Install** をクリック。
2. メニューの **File → New File…** → **Lily# Score (.lys)**: テンプレートが `Untitled` を選択した状態で開き、
   横に彫られた楽譜がひとりでに出る。

### 11. `start-first-score` — 最初の 1 曲（音あり）
*New Score* から始める（下のテンプレートが書かれる）。
```
title "Untitled"

tempo 100
time 4/4
key c major

part melody {
  clef treble
  section A { c'4 d e f | g2 g | }
}

form { A }

score {
  staff melody
}
```
1. `Untitled` の上にタイトルを打つ — 楽譜のタイトルが打つそばから変わる。
2. `section A` の閉じ `}` の前に `e4 d c2 |` と打つ — 小節が 1 音ずつ伸び、音ごとに鳴る。
3. プレビュー上部の **▶** を押す — 演奏され、音符が順に光る。
4. `e4 d c2 c4 |`（1 拍多い）と打つ — 波線が出る。マウスを乗せてメッセージを見せる
   （*Measure duration 5/4 exceeds time signature 4/4*）。元に戻す。

### 12. `learn-lesson` — 補完の一覧からレッスンを始める
1. 空の `.lys`（*New Score* のあと全選択して削除）。`lesson` と打ち `Ctrl+Space`: 12 本の一覧。
   `lesson-02-notes` を選ぶ（置き換えの確認に答える）。
2. プレビューが赤い丸 2 つ付きで開く。1 つ目の指示どおりにする — `d4@todo("caret before d, type 2")`
   の `d` の前にキャレットを置き `2` を打つ — そして `@todo(…)` を消す: 丸が消える。

### 13. `commands-guess-key` — Guess Section Key から Respell まで
調がわからないまま耳コピした旋律: ほとんど ♭ で書き、2 音だけ `fis` と書いてしまった。
```
time 3/4
part melody { clef treble }
section A { melody { ges'4 aes bes | ces2 bes4 | aes4 fis f | fis2. | } }
form { A }
score { staff melody }
```
1. section A にキャレット、右クリック → *Lily#: Guess Section Key…*。一覧の先頭が `key ges major`
   （`best guess`）、続いて `fis minor`、`ces major`（実測のスコア 0.85、0.57、0.56）。
2. `key ges major` を選ぶ: `section A { key ges major melody { … } }` — プレビューに ♭ 6 つの調号が出る。
3. 続いて綴り直しの一覧: `fis → ges · 2 notes`（チェック済み）。Enter: プレビューの ♯ 2 つが G♭ になる
   （`aes4 ges f | ges2. |`）。

### 14. `commands-extract-phrase` — Extract Phrase from Section
```
part melody { clef treble }
section A { melody { c'4 d e f | g1 | } }
form { A }
score { staff melody }
```
1. 1 小節目の中にキャレット。右クリック → *Lily#: Extract Phrase from Section*、`theme` と打って Enter。
2. ファイルが `phrase theme { c'4 d e f | g1 | }` と `section A { melody { theme } }` になる。
   プレビューは変わらない。

### 15. `about-speed` — 触った段だけが組み直される
`site/petite-valse.lys`（何段にもわたる大譜表）を開き、プレビューを横に。
1. プレビューを曲の中ほどまでスクロールする。そこの小節で一定の速さで打つ — 音をいくつか、強弱記号 —
   ページ全体を見せたまま: 編集中の段だけが打つたびに詰め直され、ほかの段は動かない。
