# 名前の無いブロックとスコアの中の上書き — 設計

**状態**: **方針はユーザー決定**（2026-10-09・第870）。**§0 1〜6 は実装済み**（第870: form と score は `d26115786`、chords・lyrics・part はその次の commit）。残りは §6 と、§3 の `staff bass` の読み（下）。
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

- **出力名**: 名前の無いスコアは `<入力名>`、`score NAME` は `<入力名>-NAME`。`"basename"` の文字列は
  今どおりファイル名を丸ごと指定する（名前と両方書けば文字列が勝つ）。
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
