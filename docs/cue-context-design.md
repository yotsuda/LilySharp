# `cue { … }` — 設計

**状態**: **形は決定**（`@cue` を廃し `cue { … }` にする・ユーザー判断）。
**細目 4 点が未決**（§8）。**実装は未着手。**（2026-08-02・第72セッション）
**根拠**: `audit/lp-geometry/probes/cue-span.ly`（この文書の「MEASURED」は全部そこ）。

---

## 0. 決定

**`@cue`（音符単位の注釈）を廃止し、`cue { … }`（範囲）にする。**

```
m { c4 d cue { e4 f } g4 | }
```
↓ `lysc ly`
```lilypond
m = \fixed c' { c'4 d' \new CueVoice { e'4 f' } g'4 | }
```

---

## 1. なぜ「範囲」でなければならないか

### 1.1 LilyPond の cue は音符の属性ではない

`ly/engraver-init.ly` の `CueVoice` は**コンテキスト**で、大きさは**コンテキストプロパティ**から来る：

```lilypond
\context{ \Voice
  \name CueVoice
  \alias Voice
  fontSize = #-4
  \override NoteHead.ignore-ambitus = ##t
  \override Stem.length-fraction = #(magstep -4)
  \override Beam.length-fraction = #(magstep -4)
  \override Beam.beam-thickness = #0.35
  \override StemTremolo.beam-thickness = #0.35 }
```

音符に付く情報は 1 つもない。MEASURED: cue の符頭も臨時記号も `font-size = -4.0` を報告し、
**grob ごとの表を持たない**（grace は `scm/music-functions.scm` の `general-grace-settings` で
NoteHead −3・Accidental −4 という表を持つ。cue は持たない）。

⇒ LilyPond における cue の定義は「**この範囲は小さい活字の声部である**」。
「この音符は cue である」という概念は LilyPond に存在しない。

### 1.2 境界は観測できる ＝ 推測してはいけない

MEASURED（`cue-span.ly` の B 群）——**4 つのうち 3 つは音符単位の印からは見えない**：

| 事象 | 境界を跨ぐか | 実測 |
|---|---|---|
| **連桁** | ✗ | `c''8 d'' \new CueVoice { e''8 f'' }` は Beam **2 本**。同じ 4 つを 1 声部で書くと **1 本** |
| **タイ** | ✗ | `warning: unterminated tie`、Tie grob 無し |
| **スラー** | ✗ | `warning: cannot end slur` / `unterminated slur`、Slur grob 無し |
| **臨時記号の状態** | ○ 共有 | `cis''2 \new CueVoice { cis''2 }` は Accidental **1 つだけ**（`Accidental_engraver` は Staff 側） |

音符単位の印では、連桁・タイ・スラーが「囲いを跨ぐつもりだったのか」を Lily# が推測するしかなく、
その推測が LilyPond と一致する保証がない。範囲なら書いた人が決める。

### 1.3 記録されていた反対理由は成り立たない

引継ぎには「`\new CueVoice` は並行 voice になるので**符尾方向と衝突回避が変わる**＝双子が別の音楽に
なりうる」とあった。MEASURED（A 群）——**変わらない**：

```
A-HIGH  g''4 g'' \new CueVoice { g''4 g'' }   stem dir = -1 -1 -1 -1
A-CTL   g''4 g'' g''4 g''      （対照）        stem dir = -1 -1 -1 -1
A-LOW   d'4 d'  \new CueVoice { d'4 d' }      stem dir = +1 +1 +1 +1  ← cue 側も音高由来
```

インラインの `\new CueVoice` は**逐次**であって並行ではない。

### 1.4 これで初めて双子が作れる

`cue { e4 f }` → `\new CueVoice { e'4 f' }` の 1 対 1。境界推測コードが無い＝境界推測バグも無い。
現状は cue の台帳点 **0**・probe **0**、`lysc ly` は `@cue` を落とす（`warning: articulation @cue
not mapped, dropped`）ので、cue について何も測れていない。

---

## 2. なぜ `cue` であって `cuevoice` でないか

1. **LilyPond 自身が機能名として "cue" を使う。** ユーザー向け命令は全部 `cue*`
   （`cueDuring` / `cueDuringWithClef` / `cueClef` / `cueClefUnset` / `transposedCueDuring`）。
   `CueVoice` は内部のコンテキストクラス名で、その description 自身が
   *"Usually left to be created implicitly"* と書いている。
2. **Lily# は既に「LP のコンテキスト名から構造サフィックスを落とす」規則で動いている。**
   `staff`←`Staff` ／ `tab`←`Tab**Staff**` ／ `chords`←`Chord**Names**` ／ `voice`←`Voice`。
   `cue`←`Cue**Voice**` はこの列にそのまま並ぶ。
3. **`cuevoice` は誤解を招く。** Lily# の `voice { } { }` は**並行**する声部。`cuevoice` と書くと
   「周囲と並行に鳴るもの」と読まれるが、実際は逐次（§1.3）。名前が意味を裏切る。
4. **粒度が揃う。** `staff` / `tab` / `voice` / `grace` / `tuplet` / `chords` / `lyrics` は
   すべて 1 語の概念語。

⚠️ 弱いところ: Lily# には `staffgroup` / `grandstaff` / `choirstaff` という複合語キーワードもある。
ただしこれらは **LP 側の名前自体が概念語**（`StaffGroup` に落とすべきサフィックスが無い）なので、
規則の反例ではなく適用対象外と考える。

---

## 3. 構文

```
cue-expression := "cue" music-block
```

- `music-block` は `grace { … }` / `tuplet 3/2 { … }` が使うのと同じ `MusicBlockSyntax`。
- 置ける位置は `GraceExpressionSyntax` と同じ（音楽の中に逐次で）。
- 構文ノード `CueExpressionSyntax`（keyword + Body）は `GraceExpressionSyntax` の写しでよい。
- 新キーワード `cue` = `SyntaxKind.CueKeyword`。`Parser.Music.cs` の
  「音楽項目の開始トークン」表に 1 行足す。

---

## 4. 中に書けるもの

**許す** — MEASURED（C 群）で LilyPond が警告なしに組めたもの：

| 中身 | 実測 |
|---|---|
| 音符・和音・休符 | `C-REST` 休符 1 つ |
| 小節線を跨ぐ（`cue { e2 \| f2 }`） | `C-BAR` 両方 −4、間に BarLine、抜けた次の音は原寸 |
| 連桁（囲いの中で閉じるもの） | `B-BMIN` Beam 1 本 |
| `tuplet 3/2 { … }` | `C-TUP` 3 つとも −4 |
| `grace { … }` | `C-GRACE` **font-size −7.0**（context の −4 と grace の −3 が**合成される**） |
| 音符修飾 `@staccato` 等 | `C-SCRIPT` Script grob あり |
| `cue { } cue { }` の連続 | `C-TWO` 両方 −4、統合は不要 |

**跨がせない** — §1.2 のとおり LilyPond が形にしないので、Lily# は**診断で先に落とす**：

- タイ／スラーが囲いの内外を跨ぐ → 既存 `LYS4010 UnpairedSlur` と同族。**新コード LYS4012**
  （`SpanCrossesCueBoundary`）を立て、「囲いの中で閉じるか、囲いの外に出すか」を促す。
- 連桁は Lily# が自動で決めるので「跨ごうとする」入力が存在しない。
  **実装側の仕事**として「自動連桁が `cue` 境界を跨がない」ことを保証する（診断は不要）。

**禁じる（保守的に。開けるのは後からでも安い）**：

- `cue { }` の入れ子 → **LYS4013** `NestedCueBlock`
- `cue { }` の中の `voice { } { }` → **LYS4014** `VoiceInsideCue`
  ⚠️ LilyPond 側では書けるが意味が二重になる。**未リリースなので、必要になってから開ける方が安い。**
  → §8 の未決事項。
- 空の `cue { }` → 既存の空ブロック診断に乗せる（`LYS4011 LoneVoiceBlock` の隣）

---

## 5. `@cue` の撤去

- `Semantics/AnnotationNameValidator.cs` の `ExtraPlainNames` から `"cue"` を外す。
  → `@cue` は既存の「未知の注釈」診断に落ちる。
- **撤去専用のエラーは足さない**（未リリース方針：撤去に専用エラーを足さない・LYS コードは
  退役させて再利用しない）。
- 書き換える fixture は 2 本だけ：
  - `LilySharp.Tests/Fixtures/test/cue-accidentals.lys`（snapshot `test__cue-accidentals.svg` あり）
  - `LilySharp.Tests/Fixtures/test/cue-notes.lys` — ⚠️ **現在の文法で既に壊れている**
    （`chords` が予約語になったため `LYS0002`）。snapshot が無いのでテストは緑のまま。どのみち直す。

---

## 6. exporter

`CueExpressionSyntax` → `\new CueVoice { … }`。

これで `lysc ly` が cue の双子を出せるようになり、**台帳に cue の点を開けるようになる**
（それが本来の目的）。

---

## 7. モデル / レンダラ / 出力への影響

**この変更では出力を変えない。** collector は cue 範囲を持ち、範囲内の item に「cue である」印を
付けるだけ。レンダラは現在の per-note scale 経路（`EngravingDefaults.CueScale = 0.66`）を
そのまま使う。⇒ **snapshot は不変のはず**（`test__cue-accidentals.svg` が動いたら、文法変更が
意図せず幾何に触れている＝バグ）。

**次段（別 commit・要承認）**: `0.66` → 13 デザインの移植。ここは描画が動くので
**台帳点を先に開く**。開くべき点（数値は cue-span.ly の A 群に測定済み）：

```
cue.head.width          LP 0.815348908  （原寸 1.304200・比 0.625172）
cue.accidental.width    LP 0.692956577  （原寸 1.100000・比 0.629961 = magstep(-4) ちょうど）
```

⚠️ **1 つのスカラーでは両方を出せない**：臨時記号の比だけが `magstep(-4)`、符頭の比は違う。
13 デザインの符頭は 20 デザインの縮小ではないから。`CueScale = 0.66` は符頭に対して **5.6%**
大きく（自身のコメントが言う「magstep(-4) に対して 4.8%」ではない）、そもそもスカラーでは直らない。
道具は grace の臨時記号で既に揃っている（`GlyphMetrics.AtFontSize` /
`IDrawingContext.MusicFace` / `AccidentalSkylinePair(kind, 13)`）。

---

## 8. まだ決めていないこと

1. **`cue { }` の中に `voice { } { }` を許すか。** §4 では保守的に禁止した。
2. **`\cueClef` 相当をいつ足すか。** cue は普通「引用元の楽器の clef」を伴う。
   構文の余地は空けてある（`cue treble8 { … }` のように第 1 引数を足せる形）。**今は足さない。**
3. **MusicXML の `<cue/>` は音符単位。** 連続する cue 音符を範囲にまとめる処理が要る。
   ⚠️ ただし**現在の import は cue 音符をそのまま捨てている**
   （`MusicXmlImport/MusicXmlReader.cs` `ReadNote` が `"cue note dropped."`）ので、
   この変更で壊れるものは何も無い。
4. **grace × cue の合成デザイン。** MEASURED で font-size **−7.0** になることは分かったが、
   それが Emmentaler のどのデザインを選ぶかは**未測定**。§7 の移植で触るときに測る。
