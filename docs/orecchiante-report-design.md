# 直した小節を Orecchiante に報告する（Report to Orecchiante・設計メモ）

Orecchiante の依頼 23（`C:\MyProj\Orecchiante\docs\repro\lilysharp-requests-2026-11-23-report-bars.md`）。
オーナーの希望 2026-11-23「Lily#+Calamo のように、小節ごとに正解を直してレポートする機能。ベースにもあれば正解データ作成が楽」。
第1372 で実装した。Calamo の「Report Misread to OMR」（`omrSeedCore.ts`）の双子。

## 1. 誰に出すか

- **オーナーだけ**。`settings.json` に手で `lilysharp.recordings.seedFolder` を書いたときだけ出る。
  設定もコマンドも `package.json` に載せない（`orecchianteSeed.test.ts` が見る・`RELEASING.md` の注）。
- 隣のファイル（`<stem>.orecchiante.json`）があり、その小節が `section` を持つ `.lys` だけ。

## 2. 入口（Ctrl+. のコードアクション）

拡張が自分で登録するコードアクション（`orecchianteSeed.ts`）。コマンドパレットには出ない。

| キャレット | 出るもの |
|---|---|
| section の中の `bass`・`kit`・和音の行の小節 | `Report Bar k of A (bass) to Orecchiante…` と `Report Section A (bass) as Checked to Orecchiante…` |
| 1 行の `phrase` の定義で、ドラムの型（`phrase beatA { drums { hatsA backA kickA } }`）が使うもの | `Report Phrase hatsA to Orecchiante…` |
| 1 行の `steps` の型（`phrase beatA { steps { hh "x... x..." … } }`・`steps 4. { … }`）そのもの（Orecchiante 0.18.0・依頼 26） | `Report Phrase beatA to Orecchiante…` |

- `Section … as Checked`: その section のその行の小節を、直したものも直していないものも全部「正解」として 1 つのフォルダに入れる（依頼の「あると速いもの」）。
- `Phrase`: 型を直した報告。その型を使う kit の小節（型の名前の小節と、その後の `%`）を全部並べる。どれが本当に型どおりかはオーナーが聴いて決める＝小節や section の報告とは別の種類（`kind`）。
- 一言は入力欄で訊く（空でよい）。Esc で何も書かない。

## 3. 小節の数え方

サーバの `lilysharp/voiceBars`（`SameBarFinder.AllBars`）が、section ごとの声部（part の音楽・和音の行）の小節を文字の範囲で返す。
数え方は LYS1070 と同じ一本（`VoiceBarLines`）。拡張は section の名前と section の中の何小節目か（`barOfSection`）で隣のファイルの `bars` を引く:

- `bars`（演奏した小節の番号）＝隣のファイルで `section` と `barOfSection` が同じ小節の `bar` 全部。
- `barWhenRead`＝その 1 回目の `bass`（文字列）・`drums`（太鼓ごとの行）・`chords` の `symbol` の列。

`.lys` の section を改名・結合すると名前が合わなくなる（`bars` が空）。section の小節の数が隣のファイルと違えば `linedUp: false` と入力欄の題で言う。
隣のファイルは読んだときのまま（SIDECAR.md）なので、名前で引くのは「Orecchiante が書いた section のまま直している」間だけ正しい。

## 4. 書くもの

フォルダ `<seedFolder>/<日時>-<曲>-bar<N>-<part>`（section は `…-<section>-<part>`、phrase は `…-<phrase>-kit`。N は 1 回目の演奏の `bar`）に:

- `.lys` の写し（報告した時点のエディタの中身）・隣のファイルの写し・`seed.json`。

`seed.json`（`OrecchianteSeed`・`seed: 1`）:

| 項目 | 中身 |
|---|---|
| `kind` | `bar`・`section`・`phrase` |
| `part` | `bass`・`kit`・`chords` |
| `section` | bar・section の報告の section（phrase は null） |
| `phrase` | phrase の報告: `{ name, body, grooves }`（今の中身と、それを使う型。`steps` の型は `body` が `steps { … }` の全文で `grooves` は `[自分]`） |
| `linedUp` | 報告した section の小節の数が隣のファイルと同じ |
| `items` | 小節ごとに `{ section, barOfSection, bars, bar, groove?, lines?, barWhenRead }`。`bar` は `.lys` の今の文字（小節線を除く）。kit の小節が型（`beatA`・その後の `%`）なら `groove` とその phrase の中身 `lines`。`steps` の型なら `lines` は太鼓ごとの行 `{ "hh": "x... …", "sn": …, "bd": … }`＝隣のファイルの `drums` と同じ形（同じ太鼓の 2 行目は `"hh 2"`）。小節にじかに書いた `steps { … }` は型ではない＝`bar` の文字だけ |
| `note` | 一言 |
| `lysWas`・`lysCopy`・`sideFileWas`・`sideFileCopy` | 元の場所（読み直さない）と写しの名前 |
| `recording` | 隣のファイルの `audio` を、その場所から解いたパス（写さない） |

## 5. 制限

- phrase は Orecchiante が書く 1 行の形だけを読む（`phrase X { … }` が 1 行）。`steps` の型は、中身が `太鼓 "行"` の並びだけのもの（`orecchianteSeedCore.ts` `stepsOf`）。型かどうかは形で決め、kit の小節から呼ばれているかは見ない（呼ばれていない型の Report Phrase は「nothing to report」）。
- section の外の和音の行（section の無い譜）は出さない（Orecchiante は bass・kit を書く譜を section で書く）。
- 実機の網: Lab `sessions/p1372/vscode-e2e`（Orecchiante 0.14.0 が書いた `x186.lys` で 16 項目・設定なしで 0 件）。
