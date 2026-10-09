# 名前の無いブロックとスコアの中の上書き — 設計

**状態**: **方針はユーザー決定**（2026-10-09・第870）。**§0 1〜6 は実装済み**（第870: form と score は `d26115786`、chords・lyrics・part はその次の commit）。**§7（score の名前は識別子 1 つ）も実装済み**（第871）。残りは §6 と、§3 の `staff bass` の読み（下）。
**根拠**: ユーザーとの会話（第870）。数は 2026-10-09 の作業ツリー（§4）。

---

## 0. 決定

1. **`chords`・`lyrics`・`form`・`score`・`part` を名前なしで書ける**。名前の無いブロックは
   「ファイルの既定」＝1 ファイルに 1 つ。`fonts`／`paper`／`layout` が既にこの約束（GRAMMAR §2.4）。
2. **`score` の識別子は、スコア自身の名前**になる（今は form への参照）。form はスコアの中で選ぶ。
3. **スコアの中で `form`・`fonts`・`paper`・`layout` を上書きできる**（§2）。
4. **`chords`・`lyrics` の中身はスコアの中に書けない**（ユーザー「長いし」）。名前の無いトップレベルのブロックで足りる。
5. **名前の無い `part` は、part が 1 つだけのファイルに限る**。
6. **鳴らす form は、選んでいるスコアの form**（ユーザー「自明」）。プレビューは選択中のスコア、
   CLI の `lysc midi` は名前の無いスコア（`--score NAME` で選ぶ）。
7. **後方互換は取らない**（ユーザー「累計 DL 8・半分は私」）。既存の本は一括で書き換える（§4）。
8. **既定のスコア（score を書かないファイル）は今は入れない**（ユーザー「あとで入れることもできる」）。§6。

## 1. 例

```
part {
  clef treble
  section A { c'4 d e f | g2 g | }
}

chords {
  section A { Csus2 . Csus4 Csus4 | Cmaj7 }
}

form { A }

score {                      // → song.svg
  chords
  staff
}

score another {              // → song-another.svg
  form  { A A }
  fonts { music "Petaluma" }
  chords as roman
  staff
}
```

## 2. スコアの中の 4 つの項目

`form`・`fonts`・`paper`・`layout` は同じ形で書く:

| 書き方 | 意味 |
|---|---|
| （書かない） | 名前の無いトップレベルのブロック（ファイルの既定） |
| `fonts house` | 名前つきブロックの参照（今と同じ） |
| `fonts house { … }` | 参照して、その場で一部を上書き（今と同じ） |
| `fonts { … }` | **新規**: ファイルの既定に、その場で上書き |

- `form` は上書きに意味が無いので、`form NAME`（参照）と `form { … }`（その場で書く）の 2 つだけ。
  ⚠️ `form A`（波括弧なし）は**参照**であって節の並びではない。節を書くなら `form { A }`。
  参照先が form でなく section の名前なら、その旨を名指すエラーにする。
- `fonts { … }` の解決は「組み込みの既定 ＋ 名前の無いブロック ＋ その場の上書き」。
  `fonts NAME { … }` は今どおり「組み込み ＋ NAME ＋ 上書き」で、名前の無いブロックを経由しない（GRAMMAR §2.4）。
- 同じ項目を 1 つのスコアに 2 回書くのは、今の参照と同じく警告（後が勝つ）。

## 3. 名前の約束

- **出力名**: 名前の無いスコアは `<入力名>`、`score NAME` は `<入力名>-NAME`。文字列の名前（`score "x"`）は
  第871 でやめた（§7）。
  今 `"main"` を特別扱いしている所（`RenderSpecParser.cs:53`・`ScoreForms.cs:52`・
  `MeasureCollector.Definitions.cs:435`）は、「名前の無いもの」を特別扱いする形に置き換える。
- **名前の無い同種のブロックが 2 つ**: エラー（`fonts` の 2 つ目の名前なしは今は警告＝合わせるかは実装時に決める）。
- **スコアが form を選ばず、名前の無い form も無い**: 最初に宣言した form を鳴らす（`ScoreForms.Primary`）。form が 1 つも無ければ section を宣言順に鳴らす（今のまま）。
- **名前の無い part**:
  - part が 2 つ以上あるファイルでは、どれかが名前なしならエラー。「最初の part にも名前を」と案内する。
  - スコアの中の `staff`・`tab` を裸で書くと、名前の無い part を指す（内部名 `part`＝`SyntaxFacts.UnnamedPartName`。chords・lyrics も同じ形で `chords`・`lyrics`）。
    ⚠️ **未実装**: `staff bass` は今も「bass という part」と読む（名前の無い part を低音部記号で、の読みは入れていない）。裸の `staff` の直後の単語は常に名前。
  - 歌詞の `sings` の既定も、その part。
  - section の中に直接音楽を書く形（`section A { c'4 d e f | }`）は、名前の無い part の音楽と読む。
  - part の宣言（`part { clef treble … }`）を書かずに、section の中の音楽だけでもよい。
- **名前の無い chords・lyrics**: スコアの中の `chords`・`lyrics` を裸で書くと指す。
  `chords as roman` の `as` は今どおり。

## 4. 移行

後方互換は取らない（§0 7）ので、既存の本を一括で書き換える。2026-10-09 の数:

| | 本の数 |
|---|---|
| 追跡下の `.lys` | 666 |
| Lab の `corpora/` の `.lys` | 511 |
| `form main` を含む | 1,129 |
| `score main` を含む | 1,126 |
| `main` 以外の名前の `score` を含む | 6 |
| `score main` を含むテストの `.cs` | 536 |

- 書き換えは `form main {` → `form {`、`score main` → `score`。`score X`（X は `main` 以外）は
  `score X { form X … }` に書き換える（今は X が form の名前なので）。
- ⚠️ ユーザーのコーパス（Lab の `corpora/`）を書き換えるかは、ユーザーに訊く。改行は CRLF のまま保つ（Lab notes §1）。
- 古い形への診断は 1 つだけ置く。名前つきのスコアが form を選ばず、同じ名前の form がある場合に、
  「スコアの名前は form を選ばない。`form NAME` を書け」と名指す。黙って意味を変えないため。
- 出力名は変わらない（`score main` も `score` も `<入力名>`）＝掃きは「動いた svg 0」で書き換えの正しさを確かめられる。

## 5. 段取り

1. **文法と構文木**: 5 つの宣言（chords・lyrics・form・score・part）の名前を省ける。スコアの中の `form { }`・`fonts { }`・
   `paper { }`・`layout { }`。GRAMMAR・SYNTAX_REFERENCE を同じ commit で。
2. **意味**: スコアの名前と form の切り離し・「名前の無いもの」の解決・名前の無い part の制約・診断。
3. **出力と再生**: 出力名・`lysc midi --score`・プレビューの選択中スコアの form で再生。
4. **エディタ**: 補完・テンプレート（Ctrl+Space のテンプレートは名前なしの形に）・quick fix（§4 の診断）。
5. **移行**: 追跡下の本とテストを一括で書き換える。掃きで「動いた svg 0」。`site/` の例も。

## 6. 入れないもの（今は）

- **既定のスコア**: part が 1 つでスコアを書かないファイルを、「名前の無い part の譜表 1 本 ＋
  名前の無い chords・lyrics」の 1 枚と見なす。最小のファイルが `section A { c'4 d e f | }` の 1 行になる。
  楽譜をファイルの一番外に書くのを禁じている今の方針（「a file declares parts, sections and scores」）を
  半分開けることになるので、別に判断する（ユーザー「あとで入れることもできる」）。
- **スコアの中の `chords { }`・`lyrics { }`**（§0 4）。

## 7. score の名前は識別子 1 つ（第871・ユーザー決定）

**決定**（2026-10-09・第871）: `score name { }`（識別子）に一本化する。`score "name" { }` も `score name "alias" { }` もやめる。
`main` は特別扱いしない。名前の無い score は VS Code のプレビューで **`(Default)`** と表示する。

**理由**:
- 他の宣言（part・section・form・chords・lyrics・fonts・paper・layout）はどれも名前を識別子で書く。
- Lily# で `"…"` は紙に刷る文字（`staff flute "Piccolo"`・`title`）。score の名前は `--score` と選択欄の呼び名＝識別子の側。
- 識別子は Unicode の文字を取るので `score イントロだけ` と書ける。
- 文字列は名前と二重の役（出力の接尾辞・選択欄の表示）を持ち、名前の無い `score "both"` は内部で `main` と答えて、同じファイルの `score { }` と `--score main` で区別できなかった。
  ⚠️ 第871 の最初の説明で「文字列はファイル名を丸ごと決めるので本どうしで `both.svg` がぶつかる」と書いたのは誤り。実装（`RenderSpec.ResolveOutputStem`）は 2026-09-26 から `<入力>-<文字列>` で、ぶつからない（GRAMMAR・SYNTAX_REFERENCE の「names the file outright」が古かった）。

**形**:

| | |
|---|---|
| 出力名 | 名前の無い score は `<入力>`、`score NAME` は `<入力>-NAME`（今までと同じ） |
| 選択欄 | ラベルは名前、名前の無い score は `(Default)`（括弧は名前に使えないので取り違えない）。値は出力名（名前の無い score は空） |
| 何も選んでいないとき | **名前の無い score**、無ければ最初の score（`RenderSpecParser.ChooseIndex`）。今までは最初の score だった＝名前の無い score を 2 つ目以降に書いた本は既定が出なかった |
| `--score` | 名前で選ぶ。名前の無い score は `--score` を省くと書かれる（CLI は省けば全部書く） |
| `score "tab"` | **LYS0038**（parser の error）。直す語を名指す（`write 'score tab'`・空白やハイフンは camelCase＝`SyntaxFacts.ScoreNameFor`）。回復として文字列の中身を名前と読む＝古い本は error 1 つで、プレビューは意図した score を出し続ける。quick fix あり |
| `score tab "both"` | 同じく LYS0038（「名前は 1 つ。引用を消せ」）。名前は `tab` |
| 名前の無い score が 2 つ | LYS6001（「既定は 1 つ。名前を付けよ」） |

**移行**（Lab `sessions/p871/migrate.ps1`）: `score "S"` → `score S'`（S' は S が名前ならそのまま、でなければ camelCase）／`score N "S"` → `score S'`（S が名前でなければ `score N`）／`.lys` で S が本の名前と同じで名前の無い score がまだ無ければ `score`（`<stem>-<stem>.svg` という名前の付け方は、文字列がファイル名を丸ごと決めていた頃の名残）。追跡下 224 冊・Lab `corpora/` 143 冊・C#／文書 172 ファイル。出力名が変わったのは追跡下 204（うち名前なしへ 187）・Lab 27（ユーザーのベースタブ本では `She Bangs` の `-tab-unfold` → `-unfold` だけ）。

**確かめたこと**: ⑴ 旧 lysc × 新ソース 対 新 lysc × 新ソース＝1199 枚・差 0（コードは描画を変えない）。⑵ 旧 lysc × 旧ソース 対 旧 lysc × 新ソース（追跡下・出力名は対応表で読み替え）＝687 枚・差 0（移行は意味を変えない）。どちらも data-pos は伏せた。
