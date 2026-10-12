# 次のセッションへの引き継ぎ（第902 → 第903）

> 2026-10-12、第902 の終わりに書いた。HANDOFF.md §1（第902 の記録）とは別に、**残っている課題だけ**を
> 一か所に集めた。着手したら該当の行を消すか、HANDOFF.md へ移す。用が済んだらこのファイルごと消してよい。

## 0. 今の状態

- 作業ブランチは master。**未 push が 210 コミット**（push はユーザーが行う）。
- 全体テストは **12125 合格 / 0 失敗 / 2 スキップ**（`dotnet test LilySharp.Tests -c Debug --nologo`）。
- 拡張は `tools\Deploy-Lsp.ps1` でインストール先に入れ済み（最後は `84baa2fab`）。
- 未追跡のまま残しているもの: `editors/vscode/media/soundfont/`（MuseScore_General.sf3、§3 ①）。

## 1. ユーザーの判断待ち

| # | 何を決めるか | 置き場所 |
|---|---|---|
| ① | 同梱する音源（MuseScore_General か FluidR3Mono か）と配り方（VSIX 同梱かダウンロードか）。決まれば `media/soundfont/` を commit するか捨てる。 | `docs/playback-design.md` §4 |
| ② | Pages の上部ナビの項目。案は「Home · Get started · Learn · Reference · Editor · About」。今は仮に先頭に Learn を足しただけ。 | `docs/learning-set-design.md` §7 |
| ③ | 2 言語の歌詞（`lyrics en` / `lyrics fr`）の行頭にも `1.` `2.` が付く。言語違いの 2 行に番号は不自然か。 | 同 §8 |
| ④ | ホバーの説明が薄い（`@mf`、`clef`、`instrument` の語には何も出ない）。「その語が何をするか」まで厚くするか。 | 同 §8 |
| ⑤ | 下の声部の `@todo` のメモを自動で譜表の下に置くか（第902 で質問、未回答）。 | — |

## 2. Pages（サイト）の残り

計画は `docs/learning-set-design.md` §6.5（再監査）と §7（4 段の計画）。段 1 と段 3 は第902 で済んだ。

- **段 2 — 入口（Get started）**
  - トップページの Install を手順に（入れる → `.lys` を開くか New Score → プレビューは自動で横に出る →
    `lesson` と打って Ctrl+Space → ▶）。プレビューが自動で開くようになったので、Ctrl+K V の段は不要になった。
  - トップページの最初のリンクを「Get started →」に。スクリーンショット 1 枚。
  - 全ページに**生成する**上部ナビ（トップページにも。②の決定待ち）。manual の章番号（01〜25 の手打ち）を生成に。
  - OGP と favicon。図の alt が全部「engraved example」なので中身を書く。
  - スマホで manual の 25 項の目次が本文の前に数画面続く（§6.5 C の残り）。
- **段 4 — Reference と About**
  - `grammar.html` を 4 つに分ける（`ref-music` / `ref-structure` / `ref-output` / `ref-lookup`。`grammar.html` の
    URL は入口として残す。README がこの URL を指している）。
  - 章の無い基本（調と臨時記号、拍子、テンポ、音部記号、`partial` / `time none`、強弱、`using`）を足す（§6.1 P3）。
  - `#phrases` を厚く（レッスン 9〜12 の土台）。
  - トップページの Lineage / Licence / Architecture / Speed を About へ。重複（§6.3）を 1 か所に。
- **細かい残り**
  - `docs/GRAMMAR_FOR_LLM.md`（拡張の AI に同梱される）に music フォント（`fonts { music "…" }`、
    Bravura / Petaluma / Leland、`--set music=`）の節が無い。
  - `editors/vscode/CHANGELOG.md` が古い（「Ten lessons」のまま）。リポジトリ直下の CHANGELOG.md が正。
    拡張側を同期するか、拡張側から直下を指すだけにするか。

## 3. 製品側の残り

- **線数の少ない譜表の点検の続き**。Lily# は 2 線・4 線譜を五線の枠の上で描く（2 線は ±2、4 線は −2…4。
  `EngravingDefaults.StaffLinePositions`）。一方 LilyPond の 2 線は ±1、4 線は ±1・±3。この食い違いで、
  第902 に 3 件見つかって直した（反復記号のドット `89d0e7c99`、スラッシュの加線 同、アクセント `84baa2fab`）。
  まだ LilyPond の対称な線を前提にしていそうな箇所:
  - `SharedRenderer.Prefix.cs:589` — `staffBottom = staffY - (staffLines - 1)`（音部記号まわり）。
  - `EngravingDefaults.OnStaffLine(int, int)` — 対称な線の判定。タブ譜用だが、記譜の譜表から呼ばれていないか。
  - スタッカートなど quantize する記号、強弱、休符の上下の記号も、2 線・4 線で一度描いて確かめる。
  - 手順: `as lines 1..5` の 5 段を並べた譜面を `lysc png --scale 3 --crop` で描いて目で見る（第902 の
    アクセントの確認と同じ）。
- **レッスンの実機での確認**（ユーザー）。New Score → `lesson` → lesson-01 から 12 まで。感想を受けて手直し。
- **レッスンの日本語訳**（`LilySharp.Lsp/Lessons/ja/`）。訳すと Learn ページも訳される仕組みは段 3 で入った
  （今は英語だけを生成している。日本語のページを出すなら `build-manual.ps1` に言語の引数を足す）。
- 第901 の残り（HANDOFF.md §1 第901 の「判定」）: 音源の同梱の決定（§1 ①）→ 次段 B＝サーバーから MIDI 出力
  （`docs/playback-design.md` §6）。

## 4. 第902 で入ったもの（次の作業の前提として）

- **プレビュー**: `.lys` を開くと横に自動で開く（`lilysharp.preview.openOnOpen`、既定 on）。1 枚が前面の
  `.lys` に追従し、題名バーの錠でロック。ロック中に別のファイルへ移ると新しいグループに並ぶ。`.lys` は
  プレビューのペインでは開かず、プレビューの無い左のペイン（無ければ新しく左に作る）へ移る。判断は
  `editors/vscode/src/previewCore.ts`（node テスト）。
- **`lysc svg|png|pdf --todo`**: `@todo` の赤丸とメモを描く。サイトのレッスンの絵はこれで描いている。
- **レッスン 12「A jazz chart」**: Petaluma、一線譜のスラッシュ、コード行。全レッスンが「of 12」。
- **Learn ページ**: `site/build-manual.ps1` がレッスンの `.lys` から `learn.html` と `lesson-NN-….html` を生成。
  絵は `site/build-site.ps1` の 2b が `site/learn/` に描く。リンク先の節が消えるとビルドが止まる。
- **SVG のフォント指定**: 代替フォントの字（Leland に無いスラッシュなど）と、同じフォントの別サイズ
  （装飾音の Emmentaler-13 など）は `style="font-family: …"` で書く（属性は `.music` の CSS に負ける）。

## 5. 道具（作り直し方）

第902 の一時ファイルはジョブの tmp にあり、セッションが終わると消える。必要なら作り直す。

- **隔離した VS Code を CDP で操作する**（プレビューの実機確認）。
  - `Code.exe --remote-debugging-port=9333 --user-data-dir=<tmp> --extensions-dir=<tmp>` で、ユーザーの窓に
    触らずに立てる。インストール済みの拡張を `--extensions-dir` に複製し、`out/` と `package.json` だけ差し替える。
  - ⚠️ 更新待ちの VS Code が `vscode-updating` mutex を握っていると、2 つ目が 30 秒待って終わる。
    `Code.exe` と版フォルダを複製し、複製側の `resources/app/product.json` の `win32MutexName` を変えて起動する。
  - 開くファイルは `Code.exe --user-data-dir=<同じ> -r <file>` で同じ窓へ送る。タブは `.tab` の `aria-label`、
    プレビューの中身は webview の iframe ターゲットの `document.querySelector('iframe').contentDocument`。
- **スマホ幅の確認**: Edge をヘッドレスで `--remote-debugging-port` 付きで立て、CDP の
  `Emulation.setDeviceMetricsOverride { width: 375, mobile: true }` で読み込み、`scrollWidth` を測る。
  ⚠️ `--window-size=375` だけでは最小幅（約 500px）で描いたものが切り取られるだけで、測定にならない。
- **サイトの全体ビルド**: `& site\build-site.ps1`（約 60 秒、Release の `lysc` が要る:
  `dotnet build LilySharp.Cli -c Release`）。ビルドは `site/examples/*.lys` を書き直すが、改行だけの差分なので
  `git checkout -- site/examples` で戻してよい（中身を変えたときだけ commit）。
