# 学習セットと Pages の引き継ぎ

> レッスン（`lesson-…`）とサイト（gh Pages）の**現在地と残り**だけを置く。HANDOFF.md §1.0 の「学習セット」の行が
> ここを指す（次の一手の一覧は §1.0 にあり、ここはその中身）。設計・決定の経緯・過不足の台帳は
> **`docs/learning-set-design.md`**（§6 台帳・§7 計画・§8 製品側の気づき）。**閉じたら消す。**
> 第902（2026-10-12）末に、HANDOFF.md から分けて起こした。

## 1. 現在地

- **レッスン 12 本**（`LilySharp.Lsp/Lessons/en/lesson-01-completion.lys` 〜 `lesson-12-chart.lys`）。
  トップレベルで Ctrl+Space → `lesson-01`、`lesson` と打つと全部。網は `LilySharp.Tests/Lsp/LessonTests.cs`
  （本数・番号・コンパイル・各レッスンの手順を実際の補完経路で踏むテスト）。
  - 12 本目「A jazz chart」＝Petaluma、一線譜のスラッシュ、コード行（ユーザー要望・`3191eb6b2`）。
  - Lesson 1 の STEP 0 は「プレビューは横に自動で開く。出ないときは Ctrl+K V」（`810d02578`）。
- **Pages**: 計画（`learning-set-design.md` §7）の**段 1〜4 はすべて済**（段 2 のスクリーンショットは動画の撮影に回した）。
  - 段 3: `site/build-manual.ps1` がレッスンの `.lys` から `learn.html` と `lesson-NN-….html` を生成
    （導入・STEP・TRY はレッスンのコメントから。「In the manual」の行き先は `$moreOf`、文字は節の見出しから
    読むので、消えた節はビルドが止まる）。絵は `site/build-site.ps1` の 2b が `lysc svg --todo` で
    `site/learn/` に描く（赤丸つき・警告は課題なので許し、エラーで止める）。
  - 段 1: §6.5 の A1〜A11、`#drums`、スマホ幅（375px で全ページはみ出し 0）、P8（例 17 本と index の抜粋を
    相対記法に）、P12（`8e716785f`）。
  - `site/index.html` は `site/build-preview.ps1` の生成物（git に入らない）。手で直す元は `build-preview.ps1`。
  - **段 2 は済**（2026-10-12）: 入口は `start.html`（下の「上部ナビとページ構成」）、tagline の最初のリンクは
    「Get started →」。各ページの節番号は `build-manual.ps1` の
    `Add-SectionNumbers` が数える（本文に番号を書かない。節を動かせば振り直る）。図の alt は例の先頭コメントの
    1 文（`Get-AltText`）。OGP・Twitter card・favicon は `site/site-common.ps1`（index と読み物で共有。favicon は
    拡張の `icon.png` を `build-site.ps1` が `_site/favicon.png` に写す）。目次は `<details>`：幅 1000px 以上は
    横の目次として開き、それ未満は閉じて「Contents」1 行（`Add-FoldingToc`）。
  - **段 4 の一部は済**（2026-10-12）: manual に「Key, meter, tempo」の組（`#key` `#meter` `#tempo` `#clefs`
    `#dynamics`）と `#using` を足し（§6.1 P3）、`#phrases` を厚く（複数パート・入れ子・同じ音＝パートの `octave` は
    効かない・名前の制約・ドラムの定番・エディタ）。例 6 本はすべて完全な文書で、ビルドがコンパイルする（警告ゼロ）。
    描かれ方は `lysc png` で目で確かめた。レッスン 3・4・5 の「In the manual」に `#meter` `#key` `#dynamics`。
    ⚠️ 書いていて分かった事実: **MIDI は強弱記号の段階だけを鳴らし、ヘアピンは鳴らさない**（`MidiExporter` は
    レベルの無い DynamicSyntax を飛ばす）＝ページにそう書いた。**音楽の中の `key` が全パートに効くか**は文書に
    無いので、ページは「変わるパートごとに書く」とした（どちらでも正しい書き方）。
  - 文書の細かい残り（済・2026-10-12）: `GRAMMAR_FOR_LLM.md` に「Music font」の節、`editors/vscode/CHANGELOG.md` の
    レッスンの項を直下に揃えた（Twelve）。
  - **Editor commands ページ**（2026-10-12）: `commands.html` ← `site/commands-body.html`。メニューから起動できる
    コマンドを 1 つずつ、例つきで説明。`editor.html` §07 の表は索引として残し、そこから張る。コマンドを足したら、
    `package.json` の `menus` と `editor-body.html` §07 の表とこのページの 3 か所を同じ便で直す。
  - **上部ナビとページ構成（済・2026-10-12・オーナー決定「ナビはあなたの提案通りに」）**: 全ページ（index も）に
    **Home · Get started · Learn · Reference · Editor · About**。定義は `site/site-common.ps1` の `$SiteNav` 1 か所
    （index の `build-preview.ps1` と読み物の `build-manual.ps1` が共有。`.topbar` の CSS は index の基本スタイルへ
    移した）。ナビに無いページは親を太字にする（lesson → Learn、ref-*・chords → Reference、commands → Editor）。
    - **Get started** ＝ `start.html` ← `start-body.html`（インストール・最初の 1 曲・レッスン・次に読むもの）。
      インストールの正本はここ（§6.3 の重複）。index の Install は 2 行＋リンク、`editor.html#install` は鍵の表だけ。
    - **Reference** ＝ `grammar.html` が入口（README が指す URL を残す）。**本文の元は `manual-body.html` 1 本のまま**、
      `build-manual.ps1` が目次の組で 4 ページに割る（Orientation・Writing music・Key, meter, tempo → `ref-music`、
      Structure → `ref-structure`、Output → `ref-output`、Reference → `ref-lookup`。`#read` は入口に）。
      どの本文で `grammar.html#ID` と書いても、書き出すときに ID の載るページへ張り替える（`Update-RefLinks`）。
      外から来た `grammar.html#ID` は入口のスクリプトが移った先へ飛ばす。**組を足したら `$refGroupPage` に 1 行**
      （無いとビルドが止まる）。
    - **About** ＝ `about.html` ← `about-body.html`（Lineage・Licence・Speed・Architecture を index から移した。
      図は README の mermaid を `Get-ReadmeMermaid` が持ってくる）。index には非提携の 1 行と About へのリンク。
    - 重複（§6.3）: インストール＝start に 1 か所、「3 つの考え方」＝`grammar.html#read`（index の Grammar から重複
      2 項を消してリンク）、コードの綴り＝index は Sketch カードだけ（Grammar の項を消した）。Ctrl+Shift の形は
      `chords#editor` が `editor#step` を指すだけなので残した。
    - `build-site.ps1` の検査に**アンカー**を足した: `page.html#id` の id がそのページに在ること（割ったページへの
      張り替え誤り・章の改名を落とす）。
  - **動画のプレースホルダ（2026-10-12・オーナー「効果的な部分があれば撮影する」）**: 既存 9 本に 6 本を足した
    （`site/videos/SHOTLIST.md` の 10〜15。台本とコンパイル済みのスニペットつき）。**優先は 11（最初の 1 曲・音あり）、
    12（レッスンの始め方）、13（Guess Section Key → Respell）**。撮れたら `site/videos/NAME.mp4` に置き、その figure の
    `data-pending` を消す（Learn の 12 だけは `build-manual.ps1` の中）。
  - **欠陥（直した・2026-10-12・オーナー承認）: 下の段の幅の広いメモが系の上へ出た**。原因は**大譜表が段の間に
    引く小節線（span bar）と系頭の縦線**：描画（`SharedRenderer` の memo 配置）はそれをインクとして避けたが、
    レイアウトの予約（`TodoMemoBand`）は各段のスカイラインしか見ず知らない＝1 小節より広いメモは段の間で必ず
    小節線に当たり、置き場が無く系の上へ登った。直し: その線を `InkRecorder` に記録しない（`InkRecorder.Unrecorded`、
    メモの引出線と同じ扱い）。網 `TodoStaffMarginTests.AMemoWiderThanABar_…`（grandStaff 版は直す前に赤、
    plain 版は対照）。メモの文字が細い小節線をまたぐのは許した（読める）。
    ⚠️ 切り分けで分かった事実: 予約（TodoBands）は preview でも `--todo` でも効く。最初の見込み「右端の寄せの
    食い違い」は外れ（中ほどでも起きた）。

## 2. ユーザーの判断待ち

| # | 何を決めるか | 置き場所 |
|---|---|---|
| ② | 2 言語の歌詞（`lyrics en` / `lyrics fr`）の行頭にも `1.` `2.` が付く。言語違いの 2 行に番号は不自然か（Lesson 7 で気付いた製品側の問い）。 | §8 |
| ③ | ホバーの説明が薄い（`@mf`、`clef`、`instrument` の語には何も出ない）。厚くしたらレッスンで案内し直す。 | §8 |

## 3. 残り

- **ユーザーの実機確認**: New Score → `lesson` → lesson-01 から 12 まで。感想を受けて手直し。
- **動画の撮影**（ユーザー）: `site/videos/SHOTLIST.md` の 1〜15。優先は 11・12・13。
- **見た目の確認**: 新しいナビ・Reference の入口とカード・Get started・About を、ブラウザで（375px 幅も）。
  ビルドの検査（コンパイル・リンク・アンカー）は通っているが、目では見ておらず、375px のはみ出しも測っていない
  （段 1 は CDP の端末エミュレーションで測った＝Lab の道具）。
- **日本語訳**（`Lessons/ja/`・`learning-set-design.md` §4 の手順・英語版が固まってから）。Learn ページを日本語でも
  出すなら、`build-manual.ps1` に言語の引数を足す（今は `Lessons/en` だけを読む）。

## 4. 作業の注意

- **サイトの全体ビルド**: `& site\build-site.ps1`（約 60 秒。Release の `lysc` が要る:
  `dotnet build LilySharp.Cli -c Release`。古い `lysc` だと新しい文法のレッスンがエラーになる）。
- ビルドは `site/examples/*.lys` を書き直す。改行だけの差分なので `git checkout -- site/examples` で戻してよい
  （例の中身を変えたときだけ commit）。
- `build-site.ps1` の検査: ページ内の完全な例（score と form を持つ `<pre>`）は警告ゼロでコンパイルされること。
  レッスンの全文は `<pre data-lesson>` で検査から外し、2b でエラーだけを見る。ページ間のリンク先が
  ビルドで書くページであること。
- PowerShell の変数名は大文字小文字を区別しない（`$countWord` と `$CountWord` は同じ変数。第902 で一度踏んだ）。
