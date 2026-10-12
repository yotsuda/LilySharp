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
- **Pages**: 計画（`learning-set-design.md` §7）の**段 1（誤りの修正）と段 3（Learn ページ）は済**。
  - 段 3: `site/build-manual.ps1` がレッスンの `.lys` から `learn.html` と `lesson-NN-….html` を生成
    （導入・STEP・TRY はレッスンのコメントから。「In the manual」の行き先は `$moreOf`、文字は節の見出しから
    読むので、消えた節はビルドが止まる）。絵は `site/build-site.ps1` の 2b が `lysc svg --todo` で
    `site/learn/` に描く（赤丸つき・警告は課題なので許し、エラーで止める）。
  - 段 1: §6.5 の A1〜A11、`#drums`、スマホ幅（375px で全ページはみ出し 0）、P8（例 17 本と index の抜粋を
    相対記法に）、P12（`8e716785f`）。
  - `site/index.html` は `site/build-preview.ps1` の生成物（git に入らない）。手で直す元は `build-preview.ps1`。

## 2. ユーザーの判断待ち

| # | 何を決めるか | 置き場所 |
|---|---|---|
| ① | 上部ナビの項目。案は「Home · Get started · Learn · Reference · Editor · About」。今は仮に先頭に Learn を足しただけ。 | §7 |
| ② | 2 言語の歌詞（`lyrics en` / `lyrics fr`）の行頭にも `1.` `2.` が付く。言語違いの 2 行に番号は不自然か（Lesson 7 で気付いた製品側の問い）。 | §8 |
| ③ | ホバーの説明が薄い（`@mf`、`clef`、`instrument` の語には何も出ない）。厚くしたらレッスンで案内し直す。 | §8 |

## 3. 残り

- **ユーザーの実機確認**: New Score → `lesson` → lesson-01 から 12 まで。感想を受けて手直し。
- **段 2 — 入口（Get started）**
  - トップページの Install を手順に（入れる → `.lys` を開くか New Score → プレビューは自動で横に出る →
    `lesson` と打って Ctrl+Space → ▶）。プレビューが自動で開くようになったので、Ctrl+K V の段は要らない。
  - トップページの最初のリンクを「Get started →」に。スクリーンショット 1 枚。
  - 全ページに**生成する**上部ナビ（トップページにも。①の決定待ち）。manual の章番号（01〜25 の手打ち）を生成に。
  - OGP と favicon。図の alt が全部「engraved example」なので中身を書く。
  - スマホで manual の 25 項の目次が本文の前に数画面続く（§6.5 C の残り）。
- **段 4 — Reference と About**
  - `grammar.html` を 4 つに分ける（`ref-music` / `ref-structure` / `ref-output` / `ref-lookup`。`grammar.html` の
    URL は入口として残す。README がこの URL を指している）。
  - 章の無い基本（調と臨時記号、拍子、テンポ、音部記号、`partial` / `time none`、強弱、`using`）を足す（§6.1 P3）。
  - `#phrases` を厚く（レッスン 9〜12 の土台）。
  - トップページの Lineage / Licence / Architecture / Speed を About へ。重複（§6.3）を 1 か所に。
- **日本語訳**（`Lessons/ja/`・`learning-set-design.md` §4 の手順・英語版が固まってから）。Learn ページを日本語でも
  出すなら、`build-manual.ps1` に言語の引数を足す（今は `Lessons/en` だけを読む）。
- **文書の細かい残り**
  - `docs/GRAMMAR_FOR_LLM.md`（拡張の AI に同梱）に music フォント（`fonts { music "…" }`、Bravura / Petaluma /
    Leland、`--set music=`）の節が無い。
  - `editors/vscode/CHANGELOG.md` が古い（「Ten lessons」のまま）。直下の CHANGELOG.md が正。同期するか、
    拡張側から直下を指すだけにするか。
  - §8 の「大譜表の下の段のメモが上の段の上に出る」は、メモを自分の譜表の余白に立てる直し（`9cc4410e8`）で
    直っている見込み。確かめて §8 から消す。

## 4. 作業の注意

- **サイトの全体ビルド**: `& site\build-site.ps1`（約 60 秒。Release の `lysc` が要る:
  `dotnet build LilySharp.Cli -c Release`。古い `lysc` だと新しい文法のレッスンがエラーになる）。
- ビルドは `site/examples/*.lys` を書き直す。改行だけの差分なので `git checkout -- site/examples` で戻してよい
  （例の中身を変えたときだけ commit）。
- `build-site.ps1` の検査: ページ内の完全な例（score と form を持つ `<pre>`）は警告ゼロでコンパイルされること。
  レッスンの全文は `<pre data-lesson>` で検査から外し、2b でエラーだけを見る。ページ間のリンク先が
  ビルドで書くページであること。
- PowerShell の変数名は大文字小文字を区別しない（`$countWord` と `$CountWord` は同じ変数。第902 で一度踏んだ）。
