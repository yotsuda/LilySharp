# 値 site 監査 (value-expression site audit)

2026-08-15 起票（第165）。**第166 で軸A を数え切り**（§1）、**第166〜第168 で返済が続いている**。
**文法に「式」を導入する日に触ることになる site の全数調査**。
動機は [[GRAMMAR_STATUS.md]] の long tail を「壁」でなく「逃がし口」にする設計検討で、
そこでの結論は **「式の層を先に通す」のはスクリプトを一行も書かなくても黒字**、というもの。
本書はその工事の**対象範囲**を、推測でなく現物から数えたもの。

⚠️ ★★ **本書はもう「数えただけ」の文書ではない**（第165 の但し書きはそう書いていた）。
**第166 が `override`／part property を、第167 が小数リテラルと `tempo` を返済した**ので、
**表の一部は「当時の姿」**になっている。⇒ **打ち消し線と「第16N で〜」の注記が入っている行は
歴史**、入っていない行が現状。**数（§1.1 の 68／§1.2 の 17・20）は数えた便の基準値**で、
**返済ぶんは引かれていない**——⚠️ **引き継ぐなら数え直し、§0 の作法で「数え方」も書くこと。**

**閉じたもの（現状の索引）**: `override`／part property の文字列パイプライン（第166・`LysValue`）／
小数リテラル（第167・`DecimalLiteral`）／`tempo` の値 run（第167・`TempoValue`）／
**`@name.suffix` の点落ち**（第168・§3.3。**本体は round-trip ではなく位置ずれ**）／
**`(` の曖昧性＝スラー飲み込み**（第168・§9.4）／
**引数ノードそのもの**（第169・`MarkArgument`＋`AnnotationValues`・§9.5）。
**残る決定待ちは `drummap` だけ**——**`@name(引数)` は第168 でユーザーが決めた（§9）**。
⇒ ★ **`@name(引数)` の島は §9 が入口**（実測・制約・残作業）。§3.3 はその内部表現の側。

---

## 0. 数え方（再現手順）

対象は `LilySharp.Core`。**単位は「呼び出し site」**（grammar 上の「位置」ではない——
分岐で 2 度書かれた同じ位置は 2 と数える）。3 つの軸を別々に数えた。**軸が違うので足し合わせないこと。**

⚠️ **第165 が書いた `rg` 版の手順は、この環境では走らない**——`rg` は PATH に無い
（第166 実測）。以下は素の PowerShell で、第165 の数を再現することを確認したもの。

```powershell
cd C:\MyProj\LilySharp
# 軸A(Expect 型): パーサがリテラルトークンを「要求」している site → 19
@(Get-ChildItem LilySharp.Core\Parser -Filter *.cs |
  Select-String -Pattern 'Expect\(SyntaxKind\.(IntegerLiteral|StringLiteral)').Count
# 軸B/C: トークン文字列を値へ変換している site → 77
@(Get-ChildItem LilySharp.Core -Recurse -Filter *.cs |
  Select-String -Pattern 'int\.TryParse|int\.Parse|double\.TryParse|double\.Parse').Count
# 軸A(走査型)の母数: Parser の Advance() → 182 マッチ / 181 行
@(Get-ChildItem LilySharp.Core\Parser -Filter *.cs |
  Select-String -Pattern 'Advance\(\)' -AllMatches | ForEach-Object { $_.Matches }).Count
```

⚠️ ★ **`Advance()` は 182 マッチだが 181 行**——`Parser.Music.cs:796` が 1 行に 2 呼び出し。
**ripgrep の `-c` は行を数え、`Select-String -AllMatches` は呼び出しを数える。**
本書の走査型 49 は**呼び出し**（当該行は 2 つとも値 site ではないので差は出ない）。

### 0.1 ⚠️ 除外の見直し（第166）

第165 は「.lys 文法の外」として 11 ファイル 38 件を除外していたが、**4 件は誤除外だった**
（いずれも .lys に書かれた値を読んでいる。§7 に経緯）:

| ファイル | 第165 の扱い | 第166 の判定 |
|---|---|---|
| `Parser\Lexer.cs:243` | 除外 | **文法内** — トレモロ `:8` の数を字句解析が parse（§3.3 と同じ値の 1 回目） |
| `Svg\Layout\StaffSpacingParameters.cs:284` | 除外 | **文法内** — **`override` の値**を直に `double.TryParse`（§2 の 5 番目の読み手） |
| `Rendering\SharedRenderer.Overlays.cs:124` | 除外 | **文法内** — `@bend.N` の半音数（§7 ③ の再 parse） |
| `Svg\Model\FiguredBassItem.cs:144` | 除外 | **文法内** — `@fig(3 5)` の数字（⚠️ 第166 訂正: 綴りは `@fig.6` ではない。§3.3） |

正しく除外のまま: `MusicXmlReader`(25) / `MusicXmlExporter`(2) / `LilyPondExporter`(3) /
`MidiExporter`(1) / `EmmentalerFaces`(1) / `SvgSystemFragmentCache`(1) / `Score.cs`(1)
＝ **34 件**。（`Score.cs` は Lily# 自身が符号化した文字列の復号＝§7 ③ の同族だが、
ユーザーが書いた値ではないので除外のまま。）

⇒ 変換 site の実測 **77 件中、文法内は 43 件**（第165 の「39」は誤除外 4 件ぶん少ない）。

---

## 1. 分類と数（実測・第166 で数え切り）

### 1.1 軸A ＝ **68**（第165 の「19」は `Expect` 型だけの床だった）

第165 の 19 は `Expect(IntegerLiteral|StringLiteral)` にしか当たらず、
**`Advance()` で値を受ける site が 49 ある**。合計 **68**。内訳は値の性格で 4 クラス:

| クラス | 意味 | 数 | Expect / 走査 |
|---|---|---|---|
| **A1** | **数値だけ**が来る（式が素直に入る） | **24** | 17 / 7 |
| **A2** | **文字列だけ**が来る | **12** | 2 / 10 |
| **A3** | ★★★ **多相**（数・識別子・文字列のいずれも来る）＝**文字列で保持される** | **16** | 0 / 16 |
| **A4** | **値が綴りか反復に埋まっている**（式が届かない） | **16** | 0 / 16 |

⚠️ **判定は機械ではない。** 次のものは「値 site」から**外した**——数え直す人が
別の線を引けるように、外した根拠を書いておく:
- **閉じた語彙**（clef 名・旋法・drum 名・強弱名・`up`/`down`・repeat 種別・`as roman|both|names`）＝§3.4。
- **名前・参照**（part 名・変数参照・section ラベル・`lyrics sop` の voice 束縛名）——
  そこに入るのは値ではなく**参照**。
- **誤り回復の読み飛ばし**と**構造の約物**（`(` `)` `[` `]` `|` `*` `+` `.`）。
  ⚠️ ただし `Parser.Sections.cs:257`（`SkipStrayChordToken`）は**幅を保つために token を残す**
  回復で、値ではないが**消してはならない**（remark に実害の記録あり）。

#### A1（24）— 数値だけ
`Expect` 17: time の 4（`Parser.Declarations.cs:282,290,293,297`）／`repeat <type> N`（`Directives:61`）／
tuplet の 2（`Directives:409,411`）／form の 4（`Form:176,184,282,306`）／`:|*N`（`Music:552`）／
override 値の回復腕（`Music:591`＝**位置としては A3**）／volta の 2（`Music:731,739`）／
lyrics・chords 行番号の 2（`Sections:350,354`）。
走査 7: 音価の数（`Music:226`）・付点（`:231`）／`R1*N` の小節数（`:345`）／度数（`:526`）／
`partial` の数（`Declarations:351`）・付点（`:363`）／フレーズ音程 `'(3)`（`:400`）。

#### A2（12）— 文字列だけ
`Expect` 2: `using "f.lys"`（`Directives:475`）／`_"text"`（`Form:166`）。
走査 10: part 表示名（`Declarations:35`）／instrument の表示ラベル（`:181`）／
`title`・`composer`（`:211`）／`font "NAME"`（`:243`）／section 参照のラベル（`Form:64`）／
構造 section のラベル（`:199`）／score の出力名（`:381`）／staff render のラベル（`:527`）／
歌詞音節（`Sections:472` と接着ループ `:507`）。

#### A3（16）— ★★★ 多相＝文字列で保持される（**工事の本体**）
| site | 綴り |
|---|---|
| `Parser.Music.cs:587,588,589,638` | **`override` の値**（int / identifier / string / 負数の 4 腕） |
| `Parser.Declarations.cs:156,169,170,174` | **part property の値**（値＋ハイフン継ぎ＋オクターブ記号の run） |
| `Parser.Sections.cs:582` | `ParsePartOption` の値（`octave` / `transpose` / `instrument` / `clef`） |
| `Parser.Form.cs:773` | MIDI part render の `instrument:N` / `octave:N`（**第167: 読み手ゼロ＝対象外**） |
| ~~`Parser.Declarations.cs:315,329`~~ | ~~`tempo` の値 run~~ **第167第2便で `TempoValue` に**（§5 ⑹） |
| `Parser.Directives.cs:112` | ⚠️ **`drummap { … }` の本体を丸ごと生トークンで保持**（§7 ④） |
| `Parser.Music.cs:875` | `@name(引数)` の引数 run（token を無差別に収集） |
| `Parser.Declarations.cs:227,255` | metadata / font の**回復** run（誤り形の値を保持） |

#### A4（16）— 値が綴りか反復に埋まっている（**式の層では閉じない**）
- **反復で数を表す**（8）: オクターブ記号 `'` / `,` の収集——`Music:200,435,499,539` ／
  `Declarations:198`（top-level transpose）・`:393`（フレーズ）／音高トークン自身 `Music:194`・
  `Declarations:195`。⇒ **`c''` の「2」は token の個数**。式を入れるには**綴りごと変える**。
- **トレモロ `:8`**（4）: `Music:241,253,264,437`（音符・和音反復・drum・和音）。
- **弦番号 `\4`**（1）: `Music:921`。
- **mark 名の分節に数**（1）: `Form:155`（`ExpectMarkName` が `IntegerLiteral` を受ける）。⚠️ **第166 訂正: 例に挙げていた `@fig.6` は廃止済みの綴りで、今はパースしない**——図付き低音は `@fig(3 5)`＝A3 の引数 run で書き、パーサが内部 mark 名 `fig.3.5` に正規化する。⚠️⚠️ **第167 で「別途要確認」を確認した: 用途は無い。** `ExpectMarkName` の**呼び手は 1 つだけ**（`Parser.Form.cs:66` の form 直下の `@`＝`@ds.al.fine` の島）で、**図付き低音は音符付き（`Music.cs`）＝別の島**。⇒ `IntegerLiteral`/`RestS`/`PitchF` の 3 腕は**観測者ゼロ**（コーパス 80＋フィクスチャ 209 で 1 件も届かない）。**消さずに「unobserved」と注記した**——「到達不能」は主張であって、それを測っている機械が無い。同じ行に載っていた `LILYPOND-REF: figured-bass-engraver.cc` は**島違いの引用なので消した**。
- **和音品質 `maj7`**（2）: `Sections:301,305`。

### 1.2 軸B / 軸C（第165 の数・不変）

| クラス | 意味 | 数 | 内訳 |
|---|---|---|---|
| **B** | 構文アクセサが値へ**変換** | **17** | `Syntax\SyntaxNodes*.cs`（Declarations 7・`SyntaxNodes.cs` 6・Expressions 2・FormRender 1・Attachments 1） |
| **C** | 下流がアクセサを通さず**自前で再解釈** | **20** | Collector 8 / `GrobProperty` 2 / Semantics 8 / `DrumNameRegistry` 2 |

⚠️ **C が B より多いことが本監査の主眼**。値の解釈が構文層に閉じておらず、
**同じ値を 2 か所以上が別々に読んでいる**（第164 の「カットには表が 2 つある」と同型の形）。

⚠️ ★★ **この 17/20 は第165 に数えた数で、第166・第167 の返済ぶんが引かれていない**
（`GrobProperty` の 2・`CollectTempo` の 1 は少なくとも消えている）。
**⇒ 数え直すまで「今の C は 20」と書かないこと**——数え直す人は §0 の手順で、
**「数え方も書く」**（§0 の罠）。**ここに残しているのは第165 の基準値**という意味だけ。

⚠️ ★ **B(17)＋C(20)＝37 で、文法内 43 に 6 足りない**（第165 は 39 と書いて 2 足りなかった）。
差の内訳は **パーサ内 2 ＋ 第166 の誤除外 4**:
- **パーサ内の検証専用変換 2 件**——`Parser.Music.cs:532`（度数が 1 以上か）と
  `Parser.Declarations.cs:403`（フレーズ音程が 1 以上か）。**数に戻して診断を出し、
  数は捨ててトークンを積む**＝同じ文字を下流がもう一度 parse する。**A にも B にも C にも入らない。**
- **§0.1 の誤除外 4 件**（うち `StaffSpacingParameters` は C、他 3 つは C 相当）。

---

## 2. ★★★ 最大の発見: `override` と part property は値が**文字列のまま全工程を横断する**

`OverrideDeclarationSyntax.ValueToken`（`Syntax\SyntaxNodes.Overrides.cs:41`）は**生トークン**で、
値は型を持たないまま `Dictionary<string, Dictionary<string,string>>` に**文字列として**積まれ、
**使う場所で初めて数に戻される**。

| site | 綴り |
|---|---|
| `Svg\Model\GrobProperty.cs:218` | `GetDouble` → `double.TryParse(value, NumberStyles.Any, InvariantCulture, …)` |
| `Svg\Model\GrobProperty.cs:233` | `GetInt` → `int.TryParse(value, …)` |
| `Svg\Collector\RenderSpecParser.cs:412` | part property `lines` → `int.TryParse(GetPartProperty(…))` |
| `Svg\Collector\MeasureCollector.cs:2684` | part property `octave` → `int.TryParse(valueToken.Text)` |
| ★ `Svg\Layout\StaffSpacingParameters.cs:284` | **第166 追加** — `double.TryParse(ovr.Value, …)` を **`GrobProperty` を通さずに**直に呼ぶ（`StaffGrouper` の spacing）。**プロパティ名も自前で `Split('.', 2)` している。** |

⇒ **式は `Dictionary<string,string>` を通れない。**
ここは「読み取り site を 1 個直す」話ではなく、**値のパイプラインが文字列型であること自体**が
枠の問題。**式の層を入れるなら、最大かつ最優先の工事はここ。**
関連: [[grob-override-scope-design.md]]（スコープの設計。値の型には触れていない）。

⚠️ ★ **part property には読み手が 2 つあり、答が違う**（第166・§7 ①）——
`RenderSpecParser.GetPartProperty`（`:604-631`）は**値トークンを全部連結**するのに対し、
`PropertyAssignmentSyntax.ValueText`（`SyntaxNodes.Declarations.cs:57`）は**先頭 1 個**しか返す。
`instrument bass-guitar` で前者は `bass-guitar`・後者は `bass`。
**`ValueText` はリポジトリ全体で消費者ゼロ**（実測）なので**今日は無害**だが、
**型付きの値にする便で最初に消す/直すもの**。

---

## 3. 構成別 site 一覧

### 3.1 数値が入る（式にして意味がある）

| 構成 | パーサ（軸A） | 変換（軸B/C） |
|---|---|---|
| `time N/D`（加算拍子 `3+2/8` 含む） | `Parser.Declarations.cs:282,290,293,297` | `SyntaxNodes.Declarations.cs:121,150` |
| `tempo … N` / `4. = N` / `swing N` | `Parser.Declarations.cs:315,329`（走査・A3） | `SyntaxNodes.Declarations.cs:231,255,281` |
| `tuplet N/D` | `Parser.Directives.cs:409,411` | `SyntaxNodes.Expressions.cs:232,237` |
| `repeat <type> N` | `Parser.Directives.cs:61` | `SyntaxNodes.cs:786`・`MeasureCollector.cs:4876` |
| `:\|*N`（小節線の反復数） | `Parser.Music.cs:552` | — |
| volta `[1-2]`（インライン） | `Parser.Music.cs:731,739` | `SyntaxNodes.cs:891,892` |
| form の alternative / `*N` | `Parser.Form.cs:176,184,306,282` | `SyntaxNodes.FormRender.cs:224` |
| lyrics/chords 行ヘッダの番号 | `Parser.Sections.cs:350,354` | `LyricsCollector.cs:281` |
| MMR の小節数 | `Parser.Music.cs:345` | `SyntaxNodes.cs:393` |
| 音価と付点 | `Parser.Music.cs:226,231` | `SyntaxNodes.cs:210`（⚠️ §4） |
| `partial` の音価 | `Parser.Declarations.cs:351,363` | — |
| 弦番号 `\4` | `Parser.Music.cs:921` | `SyntaxNodes.Attachments.cs:124` |
| 度数和音の度数 | `Parser.Music.cs:526` | `SyntaxNodes.cs:452` |
| フレーズ参照の音程 `Melody'(3)` | `Parser.Declarations.cs:400` | `SyntaxNodes.Declarations.cs:574` |
| part property `octave` / `lines` | `Parser.Declarations.cs:156`・`Sections.cs:582`・`Form.cs:773` | `MeasureCollector.cs:2684`・`RenderSpecParser.cs:412` |
| `override` の数値 | `Parser.Music.cs:587-589,638` | `GrobProperty.cs:218,233`・**`StaffSpacingParameters.cs:284`** |
| メタデータの整数 | `Parser.Declarations.cs:227`（回復 run） | `SyntaxNodes.Declarations.cs:402` |
| `drummap` の `position N` 等 | `Parser.Directives.cs:112`（本体丸ごと生） | `DrumNameRegistry.cs`（2 件） |
| 図付き低音 `@fig(3 5)` | `Parser.Music.cs:875`（引数 run・A3） | `FiguredBassItem.cs:144`（内部 mark 名 `fig.3.5` を読む） |

### 3.2 文字列が入る

`SyntaxKind.StringLiteral` は **28 か所 / 11 ファイル**。文法の受け口は:
`title`/`composer`（`SyntaxNodes.Declarations.cs:383`）・`part X "表示名"`（`:517`）・
`font "NAME"`（`:433`）・`using "f.lys"`（`Parser.Directives.cs:475`）・
`_"text"`（`Parser.Form.cs:166`）・`tempo "Allegro"`（`:201`）・staff ラベル（`Parser.Form.cs` の 7 件中）。
⇒ パーサ側の受け口は §1.1 の **A2 12 件**。

### 3.3 ⚠️ 名前の中に数が埋まっている（式の層では届かない別項）

**値が識別子の一部**になっているので、式を通すには**綴りごと変える**必要がある。
**第165 は 3 系統と書いたが、実数は 6 系統**（＋反復で数を表す 8 site＝§1.1 A4）。

⚠️⚠️ ★★★ **第167 訂正: この表の「綴り」列は 3 行が*内部の mark 名*で、綴りではなかった。**
**書き方は全部 `@name(引数)` の 1 つ**で、パーサがそれを `name.引数.引数` に正規化する
（実測 2026-08-15: `@finger(3)`→`finger.3` ／ `@bend(5)`→`bend.5` ／ `@bend(half)`→`bend.half`
／ `@fig(6 s)`→`fig.6.s`。**点つきの `@fig.6` は LYS0016 で落ち figure は出ない**）。
⇒ ★ **「名前の中に数が埋まっている」のは*内部表現*の話**であって、**文法の側では
`@name(引数)` の引数 run（A3・`Music:875`）1 件に集約される**。第165・166 が 6 系統と
数えたうちの 3 系統は、**同じ 1 つの綴りを 3 回数えていた**。
⚠️ **コーパス実測（80 冊＋フィクスチャ 209 枚）: 書かれているのは `@finger(` 28 件と
`@fig(` 5 件だけ。`@bend` は 0 件**（＝§7 ③ の 3 ホップ配管に届く本は 1 冊も無い）。

| 内部 mark 名 | 綴り（実測） | パーサ | 読み手 |
|---|---|---|---|
| `finger.3` | **`@finger(3)`** | `Music.cs:875` | `MeasureCollector.cs:4780` — `name.AsSpan(7)` |
| `bend.half` / `bend.N` | **`@bend(half)` / `@bend(5)`** | `Music.cs:875` | `MeasureCollector.Annotations.cs:503-507` — `markName[5..]`（**さらに §7 ③**） |
| `fig.3.5` | **`@fig(3 5)`** | `Music.cs:875` | `FiguredBassItem.cs:144` |
| — | トレモロ `:8` | `Music.cs:241,253,264,437` | `Lexer.cs:243`（分類のため）＋`MeasureCollector.ItemFactory.cs:216` — `text[1..]` |
| — | 弦番号 `\4` | `Music.cs:921` | `SyntaxNodes.Attachments.cs:124` |
| — | 和音品質 `maj7` / `sus4` | `Sections.cs:301,305` | 和音名の解釈側 |

⇒ **式の層と直交するのは下 3 行だけ。上 3 行は ▶ ⒯⑴ ⒟（`@name(引数)` の式の文法）に
そのまま乗る**（HANDOFF ▶ ⒯⑷ はこのぶん軽くなる）。

~~⚠️⚠️ ★★★ **その調査中に踏んだ別の欠陥（第167・未修正・起票）: 音符付きの
`@name.suffix` は点を黙って落とす。**~~ — **第168第1便で閉じた**（`9f9a7e14`）。
⚠️⚠️ ★★★ **ただし起票の記述は 2 か所とも外れていた（第168 実測・訂正）**:

- **例が弱かった。** `@feather.up` は**未知名なので警告は出る**（`Unknown annotation '@feather'`）。
  **診断がゼロなのは正しい綴りのほう**——**`@staccato.up` は警告 1 つ出ない**まま
  `@staccatoup` になっていた。**コーパス＋フィクスチャに 34 site**（`@marcato.up`・
  `@sfz.up`・`@fermata.down` …）。
- **`c4@bend.half` は `@bendhalf` にならない。** `.half` は配置語ではないので**点の後が
  そのまま音楽に落ち**、`Undefined variable or phrase: 'half'` で**はっきり落ちる**
  （`@feather.left`・`@notehead.x` も同型。`@finger.3`・`@fig.6` は LYS0016）。
  ⇒ **点を食うのは `.up` / `.down` の配置修飾子の枝だけ。**

⇒ ★★★ **本体は round-trip ではなく*位置*だった。** ノードの位置は**手前の green 幅の
累計**なので、**食われた点 1 個ぶん、後続のノードが全部 1 字ずつ手前を指す**
（実測: `c4@staccato.up d4 …` で `root.FullWidth 35` 対 `src.Length 36`・`d4` の位置が
空白を指す。対照＝点なしの同じ小節はぴったり）。**着地先は SVG の `data-pos`・LSP の
ジャンプ先・`PartSectionLayoutConverter`（.lys を木から書き戻す編集器）。**
⇒ 修復は `ArticulationGreen` / `DynamicGreen` に**点のスロット**を足すだけ（`ForcedAbove` は
スロット 3 へ）。**拒否した修飾子もノードに載せる**（`@staccato.up.down` の 2 つ目・
hairpin の `@cresc.up`）——*消費して捨てる*のが欠陥そのものだったので。
⚠️ ★★ **snapshot 5 枚 70 行が動いたが、全部 `data-pos` の値**（数字を伏せると新旧の
行集合が完全一致＝幾何はゼロ）。**新しい値は音符に着地し、古い値は空白・小節線・
トークンの途中を指していた。**

★★ **穴そのものを測った（第168・「穴も測る」の型）**: **コーパス 80＋フィクスチャ 219 の
計 299 冊を木から綴り直すと、round-trip が壊れていたのは 15 冊**。
**内訳は 3 島**——⑴ **この点落ち 10 冊**（本便で閉じた） ⑵ **注釈とスラー/タイの順序が
入れ替わる 4 冊**（`empty-chord` / `script-stack-order1` / `slur-vertical-skylines` /
`feature-tour`＝マークが、書かれた位置の `(` や `)~` より**前**に出し直される）
⑶ **`volta-labels` が `[1. …]` ラベルの手前の `|` を落とす** 1 冊。
⇒ **⑵⑶ は別の島・未修正**。網 `AnnotationRoundTripTests.EveryBook_SpellsItselfBackOutOfItsTree`
が**その 5 冊を名指しで持つラチェット**なので、**数は黙って増えないし、島には住所がある**。

### 3.4 閉じた語彙（式にしなくてよい）

clef 名・旋法・instrument preset・accidental style・drum 名・強弱名・`up`/`down`・
repeat 種別・`as roman|both|names`。**列挙であって値ではない。**

---

## 4. ⚠️ 触ってはいけない側

**`DurationSyntax.Value`（`Syntax\SyntaxNodes.cs:210`）は全音符が通る。**
式にすると打鍵ごとに全音符分の評価が走る——**式の層で唯一、性能が設計を決める site**。
さらに同 site のコメントが実害を記録している:

> 壊れた入力（例 `partial .`）で throw すると render / diagnostics pass ごと道連れになり、
> Problems パネルが空になった。

⇒ **literal のまま据え置くのが正解。** 動かすなら単独便で、[[HANDOFF.md]] の打鍵 regime ⑶ の
床を先に測ってから。パーサ側の対応 site は `Parser.Music.cs:226,231`（A1）。

---

## 5. 次の一手（この監査が名指すもの）

1. ~~★★★ `override` / part property の文字列パイプラインを型付きの値にする~~ —
   **第166第2便で返済**（`0b846ea6`）。`LysValue`（判別共用体 record・
   `LilySharp.Core\Syntax\LysValue.cs`）を入れ、**値はコレクト時に 1 度だけ型が付く**。
   - `GrobOverride.Value` が `string` → `LysValue`・resolver の辞書も
     `Dictionary<string,Dictionary<string,LysValue>>`。
   - **5 つの読み手が全部「読む」側になった**——`GetDouble`/`GetInt`/`GetString`/`GetBool` は
     新しい `GetValue` の上の 4 行になり、**`StaffSpacingParameters:284` の迂回路も
     `ovr.Value.AsDouble` へ**。
   - part property は `PropertyAssignmentSyntax.Value`（型付き）と `.ValueText`（全連結）に
     一本化。`RenderSpecParser.GetPartProperty` はその読み手になり、
     **`lines` と `octave` は数として読む**。
   - **出力は 1 バイトも動いていない**（snapshot 無変更・台帳 514 点で不動・
     suite 4569 → 4575＝新しい網 6 本ぶんだけ）。
   ⚠️ ~~**残りの A3 7 件**（…）は**手つかず**＝次便の範囲~~ — **第167 で 2 件動いた**（下の 6）。
   ⚠️ ~~★ **`LysValue.Real` は文法では書けない**~~ — **第167第1便で書けるようになった**（§7 ⑤）。
2. ~~軸A の残りを数え切る~~ — **第166 で完了**（§1.1・**19 → 68**）。
3. ~~**§3.3 の 6 系統は別項**として起票~~ — **第167 実測で 3 系統**（§3.3 冒頭の訂正。
   `@finger.N`/`@bend.N`/`@fig.N` は**内部 mark 名**で、綴りは `@name(引数)`＝A3 の run そのもの）。
   **残る別項は トレモロ `:8`・弦番号 `\4`・和音品質 `maj7` の 3 つ**＋
   **§1.1 A4 の「反復で数」8 site**。
4. **§4 は据え置き。**
5. **§7 の 4 件**は型付きの値が入れば自然に消えるもの——**先に消さないこと**（消しても
   配管が文字列のままなら別の形で戻る）。
6. ★★ **第167 の後に残る A3（＝この監査が次に名指すもの）**:
   - ~~`tempo` の値 run~~ — **第167第2便で返済**（`e6e2bfb0`）。`TempoValue` が
     **1 パスで 5 つの読み**を答える。⚠️ **6 つの状態機械があった**（引継ぎは 3 と書いていた）
     ——`Marking`/`SwingSubdivision`/`Bpm`/`BeatUnit`/`BeatDots` に加えて
     **`MeasureCollector.CollectTempo` の正規表現の歩き**が 6 つ目。
     ⚠️ **2 つの読みは食い違っていた**（`tempo "x" = 90` の beat unit）＝**振る舞いが 1 つ変わった**。
   - ~~MIDI part render の `instrument:N`/`octave:N`~~ — **対象から外す**（第167 実測）。
     **読み手がリポジトリに 1 人もいない**うえ **`MidiFile` は ProgramChange を書かない**＝
     **着地先そのものが無い**。⇒ **⒠ と同じ分類。**
   - ~~**`drummap` 本体**（§7 ④）と **`@name(引数)` run** の 2 つが残り、**どちらもユーザー決定**~~
     — **`@name(引数)` は第168 で決まった（§9）**＝**値の列＋閉じた語彙**。
     **残る決定待ちは `drummap` だけ**（言語を育てる話・無断で足さないこと）。
   - **metadata / font の回復 run** は**やらない**（型を付けても読む者が居ない・診断が動く危険）。
   ⇒ ★ **`@name(引数)` を実装する便の入口は §9**。§3.3 の 3 系統をまとめて抱えるのは変わらないが、
   **島の 2 つの欠陥（点落ち・`(` の飲み込み）は第168 が先に閉じた**ので、
   **残るのは引数ノードそのもの**（§9.5）。

---

## 6. 副産物（第165 の監査中に見つかった stale）

[[GRAMMAR_STATUS.md]] の Known gaps は **「Multi-file projects (`include` across files) 未実装」**
と書いているが、**`using "file.lys"` は実在する**——`Parser\UsingExpander.cs`
（深さ優先・フルパスで重複排除・循環は停止・読めないファイルは inert）。
**この記述は stale。** 文法ドキュメントを整理する便で落とすこと。

---

## 7. 第166 で見つかった「文字列で運ぶ」実例（すべて実測）

**§2 の主張が抽象論でないことの現物。** どれも**今日は壊れていない**——起票ではなく、
型付きの値を入れる便が**同時に消せるもの**の台帳。

**① ~~`PropertyAssignmentSyntax.ValueText` は死んでいて、しかも答が違う~~** — **第2便で閉じた**。
発見時（`SyntaxNodes.Declarations.cs:57`）: 消費者は**リポジトリ全体でゼロ**（実測）、
生きている読み手 `RenderSpecParser.GetPartProperty` は**全トークン連結**、`ValueText` は
**先頭 1 個**（`instrument bass-guitar` で `bass-guitar` 対 `bass`）。
⇒ **連結を `ValueText` に一本化し、`GetPartProperty` がそれを読む形にした**（網
`LysValueTests.APartPropertysValueIsTheWholeRunOfTokens`）。

**② パーサが診断のためだけに値を数に戻し、その数を捨てる**（2 件）——
`Parser.Music.cs:532`（度数 < 1 を弾く）・`Parser.Declarations.cs:403`（フレーズ音程 < 1 を弾く）。
下流は同じ文字をもう一度 parse する。**型付きの値なら 1 回で済む。**

**③ ★★ 型のある int を、わざわざ文字列に戻して再 parse している**（`@bend.N`）——
`markName[5..]` を `int` へ（`MeasureCollector.Annotations.cs:507`）→
`BendSemitones`（**int で保持**）→ `$"bendUp:{…}"` で**文字列 sentinel に再符号化**
（`ArticulationEngraver.cs:684`）→ `int.TryParse(a.Glyph.AsSpan(7))` で**再び int**
（`SharedRenderer.Overlays.cs:124`）。**3 ホップ、2 回の parse。**
⚠️ **これは第165 が「文法の外」として除外した側にあった**——除外の線が
「ファイルの置き場所」で引かれていたため。**同族**: `KeySignature.Custom` の
`"s:a;s:a"`（`Score.cs:69-81`）——ただしこちらは **record struct の値等値**という
明示の理由がコメントにあり、ユーザーの書いた値でもない。

**④ `drummap { … }` の本体は丸ごと生トークン**（`Parser.Directives.cs:106-115`）。
`position 6` の `6` を含め、**部分言語がまるごと未解釈のまま**赤ノード側に渡る。
**式を入れる日の飛び地**——A3 の中で唯一「値の位置」ですらなく「本文」。

**⑤ ~~★★★ 文法は小数を書けない~~ — 第167第1便で閉じた**（`52ec1f0f`）。
発見時（第166第2便）: `ScanNumber` は**数字だけ**を食べて `.` を見ず、
`override Stem.length = 3.5` は **`3` が値になり `.5` は黙って落ちた**。
`double.TryParse` を 5 か所が持っていたのに、**文法はそこへ届く値を作れなかった**
（`LysValue.Real` は C# の呼び手のためだけに在った）。
⇒ **lexer に `DecimalLiteral` が入り、`Real` は .lys で書ける値になった**
（**書けないのは `Bool` だけ**）。**衝突は測って 0**——コーパス 80 冊＋フィクスチャ 209 枚で
本文の `数.数` は `chordnames.lys:21` の `g2:m7.5-` ただ 1 つ、**それも `m7` が識別子として
一括で食われるので `ScanNumber` に届かない**（網 `DecimalLiteralTests`）。
⚠️ **釘付けの網は宣言どおり落ちた**——`LysValueTests.ARealIsNotWritableInTheGrammar` が
4579 中 1 本だけ落ち、`ARealIsWritableInTheGrammar` になった。**「落ちたら報せ」は機能する。**
⚠️ ★ **1 つだけ引き継ぐ**: `IncrementalLexer.Guard = 2` は `ScanNumber` の先読み
（`.` ＋数字）で**ちょうど埋まっている**。縮める人は `IncrementalParseTests` の
`e1.5` 行で落ちる。

**⑥ 第2便の 1 つの取り決め**: **`Str` は数ではない**。文字列パイプラインは引用符を外して
から積んでいたので `= "10"` が `GetDouble` に 10 と答えていた——**型が無いことの漏れ**で
あって誰かが書いた規則ではない。**本番で文字列として読む property は `color` 1 つだけ**
なのでコーパスは動かない（網 `LysValue_AQuotedValueIsAStringAndAStringIsNotANumber`）。

---

⚠️ **第1便はコードを 1 行も変えていない**（数えただけ）。**第2便が §5 ⑴ を返済した**
（`0b846ea6`）——**出力は不動**（snapshot 無変更・台帳 514 点／ss 非ゼロ 97／
総和 3.609963181 で不動）、suite **4569 → 4575**（+6＝新しい網ちょうど）。

---

## 8. 第167 で見つかった「同じ run を N 回歩く」実例（tempo・すべて実測）

**§7 が「値を文字列で運ぶ」の台帳なら、こちらは「同じ値を何度も読み直す」の台帳。**
`tempo` の値 run 1 本を、**6 つの状態機械が別々に歩いていた**（引継ぎは 3 と書いていた）:

| 読み | 住所（当時の**宣言行**。⚠️ 引継ぎの `:239/:265/:290` は doc コメント側の行） |
|---|---|
| `Marking` | `SyntaxNodes.Declarations.cs:237` |
| `SwingSubdivision` | `:263` |
| `Bpm` | `:288` |
| **`BeatUnit`** | `:310`（引継ぎが数えていなかった） |
| **`BeatDots`** | `:334`（同上） |
| **`MeasureCollector.CollectTempo`** | **6 つ目**——`=` から点を遡り、直前トークンを正規表現で照合 |

⇒ `TempoValue`（`Syntax\TempoValue.cs`）が**1 パスで 5 つとも**答え、プロパティは 1 行ずつになった。

★★★ **そして 2 つは答が違った**＝**同じ量の綴りが 2 つある島は、いつか必ず食い違う**（§5.2.1②）:
`tempo 8 = 120` のあと `tempo "x" = 90` を書くと、**collector 側は単位 8 を残し**
（`=` の直前に「数トークン」が要る歩きなので `"x"` には何も一致しない）、
**node 側の規則は「単位のない `=` は四分」**。⇒ node 側に揃えた（**振る舞い変更 1 件・網つき**）。

⚠️ ★ **消した正規表現の半分は最初から死んでいた**——`^([0-9]+)(\.*)$` の第2グループは
**トークン内部の点**を拾う気だったが、**点は必ず別トークン**なので常に空。
**「そのコードが実際に何を見ているか」は、書いてある意図とは別に確かめること。**

---

## 9. ⒟ `@name(引数)` の島（第168 実測・**設計はユーザー決定済み**）

**▶ ⒯⑴ ⒟ の本丸。** 第168 が島を全数で測り、その上でユーザーが 2 つ決めた。
**着手する便は、この §9 を読んでから設計を書き足すこと**（決まっているのは方針で、
引数ノードの形はまだ書かれていない）。

### 9.1 実在する引数は 5 種だけ（コーパス 80＋フィクスチャ 219 の全数）

| 種類 | 綴り | 件数（`@name(` の全出現） |
|---|---|---|
| 文字列 | `@text("dolce")` `@mark("A")` | text 59 ／ mark 2 |
| 整数 | `@finger(3)` `@fig(6)` `@bend(5)` | finger 39 ／ fig 13 |
| 整数の列 | `@fig(3 5)` `@fig(6 4)` | （上に含む） |
| 識別子 | `@notehead(triangle)` `@feather(right)` `@ottava(bassa)` | notehead 11 ／ feather 2 ／ ottava 2 |
| **部分言語** | `@chord(c:m7)` `@chord(a:m)` | chord 16 |

⚠️ **`@p( d e f)` は引数ではない**（`@p` は強弱で別経路・`(` はスラー開き）。
⚠️ ★★★ **算術・変数参照を要求している消費者は 1 つも無い**——**今日の site は全部「値」で足りる。**
⇒ **ユーザー決定 ⑴＝「値の列」**（`LysValue` をそのまま引数へ）。**本物の式は入れない。**

### 9.2 ★★★ ただし「値に落とすと壊れる引数」がある（第168 実測・**設計の制約**）

| 綴り | 今日の内部名 | 値に落とすと |
|---|---|---|
| `@frame(032010)` | `frame.032010` | **`Int(32010)`＝先頭の 0 が消える**。フレット図は**位置文字列**なので 6 弦が 5 弦に化ける |
| `@finger(03)` | `finger.03` | `Int(3)`（同型・今日は実害なし） |
| `@chord(c:m7)` | `chord.c.:.m7` | `c` は **`PitchC` トークン**＝引数が音楽トークンを借りた**部分言語** |
| `@text("dolce")` | `text."dolce"` | `Str(dolce)`＝**きれいに落ちる** |
| `@fig(3 5)` | `fig.3.5` | `Int(3) Int(5)`＝**きれいに落ちる** |

⇒ ★★ **引数ノードは「値」と「書かれた字」の両方を持つこと。**
**位置文字列（frame）と部分言語（chord・figures）は字が意味**なので、
`LysValue` 1 本にすると**静かに壊れる**。⚠️ **`@frame(` を書く本は今 0 冊**なので、
**壊しても網は鳴らない**——これは「測ってから設計する」が効いた例そのもの。

### 9.3 値が文字列で 3 往復する（`@chord` が現物）

```
トークン列 [c][:][m7]
  → MusicMarkSyntax.MarkName が '.' で連結        → "chord.c.:.m7"
  → ChordNameItem.ParseChordName が Split('.')+Join("") → "c:m7"
  → ChordStructure.TryParseChordEntry がもう一度パース
```
`ChordNameItem.cs:161` の remark が **「Parts were joined with dots by MusicMarkSyntax.MarkName;
rejoin to the written text」**と、**自分で連結を取り消していると書いている**。
⚠️ **`MarkName` の読み手は 9 ファイル・30+ site**（`MeasureCollector` 系・`LilyPondExporter`・
`MusicXmlExporter`・`AnnotationNameValidator`）で、**どれも `StartsWith` / スライス / 再パース**。
⚠️ ★★ **`AnnotationNameValidator.IsKnownCompoundName` は他の 9 つの再実装**
（`text.` `feather.*` `finger.N` `notehead.*` `frame.*` `pluck.*` `arpeggio.bracket` `bend.*`
＋ figures ＋ chord を*もう一度*綴っている）＝**§8 の「同じ run を N 回歩く」の 2 例目**。
**引数に型が付く便は、この 10 個目を消せるかを同時に見ること。**

⇒ **第169 で 7 家族ぶん畳んだ＝§9.5.2**（`AnnotationValues` が 1 コピー）。
**この節の「30+ site」「9 ファイル」は第168 の数**で、**現在は 13 site・5 ファイル**
（数え方も §9.5.2）。**`@chord` の 3 往復はまだ現物**——`Arguments[0].Text` が既に
`"c:m7"` を持っているのに、`ChordNameItem` は今も点つき文字列を受け取っている。

### 9.4 `(` の曖昧性は第168第2便で閉じた（`b3e74a34`）

`IsArticulationName()` が**任意の識別子を通す**ので、`(` は**どんな名前の後でも**引数として
読まれていた ⇒ **`c4@staccato (d4 e4 f4)` がスラー群を丸ごと飲み**、音符 3 つが小節から消えて
診断は `Unknown annotation '@staccato(d 4 e 4 f 4)'` だけだった。
⚠️ **コーパスに踏んでいる本は 0 冊**＝**live な欠陥ではなく罠**（ごく普通の音楽を書くと落ちる）。

⇒ **ユーザー決定 ⑵＝閉じた語彙**（`SyntaxFacts.ArgumentTakingAnnotations`）。
★ **クラスは 2 つではなく 3 つ**にした:
| 名前 | `(` の読み | 理由 |
|---|---|---|
| 引数を取る名前（fig/chord/finger/bend/notehead/frame/text/mark/feather/pluck/arpeggio/ottava/quindicesima/ds/dc/to） | 引数 | 語彙 |
| **引数を取らないと分かっている名前**（articulation・plain feature・裸マーク） | **スラー** | これが修復 |
| **未知の名前** | 引数 | **誤字を 1 つの間違いのままにする**——`@notehed(x)` の `(` をスラーに渡すと *`Undefined variable or phrase: 'x'`* が増えたうえ、**点つき名前から計算される "did you mean '@notehead(x)'?" が消える** |

⚠️ `ottava`/`quindicesima`/`ds`/`dc` が語彙に居るのは**裸マークでもある**から
（規則だけだとその引数形が消える）。

### 9.5 引数ノード（第169 で入った）

~~1. 引数ノードを入れる~~ — **入った**（`MarkArgument` ＋ `MusicMarkSyntax.Name` /
`HasArgumentList` / `Arguments` / `ForcedAbove`）。**§9.2 の制約どおり値と字の両方を持つ。**

★★ **引数の切り方は「トークン」ではなく「隣接した run」**。これが `MarkName` との
決定的な違いで、**そこが §9.3 の 3 往復を消す仕掛け**:

| 綴り | `MarkName`（トークン毎に `.`） | `Arguments`（run 毎） |
|---|---|---|
| `@fig(3 5)` | `fig.3.5` | `Int(3)` `Int(5)` ＝ 2 |
| `@chord(c:m7)` | `chord.c.:.m7` → split → join → `"c:m7"` | **`Text = "c:m7"` が最初から**・`Value` は null |
| `@frame(032010)` | `frame.032010` | `Text = "032010"`／`Value = Int(32010)`（**先頭 0 は Text 側にだけ残る**） |
| `@text("dolce")` | `text."dolce"` | `Text = "\"dolce\""`／`Value = Str(dolce)` |

⚠️ **区切りは空白と `,` だけ**。**コーパス 80＋フィクスチャ 219 の全数で、空白を含む引数は
図付き低音（`@fig(5 3)` / `@fig(6 4)` / `@fig(6 s)`）だけ**＝それ以外は全部 1 run。

### 9.5.1 ★★★ 実測で分類が 1 つ変わった＝**`@fig` は値の家族ではない**

第168 の「消す順は値として読める家族から（text/**fig**/finger/bend/notehead/pluck）」の
**fig は誤り**。**実測**（網 `AFigureWrittenWithoutASpace_IsOneRunButTwoDottedParts`）:

- `@fig(#6)` → run は **`"#6"` の 1 つ**／`MarkName` は **`fig.#.6` の 2 部**
- `@fig(6s)` → run は **`"6s"` の 1 つ**／`MarkName` は **`fig.6.s` の 2 部**

**`#` と suffix は空白なしで数に貼りつく**ので、**run の切れ目と点の切れ目が一致しない**。
`FiguredBassItem.ParseFigures` は点の切れ目に依存している（`#` は `pendingAlteration`、
1 文字は `currentAlteration` の suffix）ので、**引数へ移すには run の中を自分でパースし直す
＝受理される綴りが変わりうる**。⇒ ★★ **`@fig` は `@chord` / `@frame` と同じ「部分言語」の組**。
**無断で綴りを変えない**（§5 の家訓）＝**移すなら小さなパーサを書く便を別に立てること。**

### 9.5.2 移した家族と、残っている `MarkName` 読み手（第169 実測）

**移した 7 家族**——読み手は全部 `Semantics.AnnotationValues` の 1 コピーになった:
`@finger(N)`／`@pluck(p|i|m|a)`／`@bend(half|full|N)`／`@notehead(style)`／`@text("…")`／
`@feather(right|left|accel|rit)`／`@arpeggio(bracket)`。

★ **§9.3 の「10 個目」＝`AnnotationNameValidator.IsKnownCompoundName` は
この 7 家族ぶん畳めた**（⑶ の答）。**同時に `string` overload を消した**——
**呼び手が 1 つも無かった**（監査の「他の 9 つの再実装」は、*呼ばれてすらいない*コピーを
1 つ抱えていた）。**残る腕は frame / fig / chord ＋ `ParseMarkName` の点つき名前だけ。**

⚠️ ★★ **振る舞いが 1 つ変わった（宣言済・網つき）**: `@finger("3")` は
**collector が拒否し LilyPond exporter は受理して `-3` を書いていた**
（＝**Lily# が描かないものを双子が言う**）。**exporter 側の comment 自身が
「the SAME set … reads」と書いていた**ので、**collector の規則に揃えて comment を真にした**。
**コーパス実害 0**（引数が引用符つきの本は 1 冊も無い）。第167 ⑸（tempo）と同じ型。

**`MarkName` の code read は 18 → 13・7 ファイル → 5**（実測・数え方は
`Select-String '\.MarkName'` から `///` `//` `*` 始まりの行を除く）。**残りの内訳**:

| 残っている読み | site | 何の家族か |
|---|---|---|
| `frame.` の slice | 3（MusicXmlExporter 2・collector 1） | **位置文字列**（§9.2） |
| `FiguredBassItem.ParseFigures` | 1 | **部分言語**（§9.5.1） |
| `== "chord"` ／ `ParseChordName` | 2 | **部分言語**（§9.3） |
| `MusicMarkItem.ParseMarkName` 系／`ProcessDirectionName`／`mark.` ラベル | 6 | **点つきの「名前」であって引数ではない**（`@ds.al.fine`）。ただし **`@mark("A")` のラベルはここに混ざっている値** |
| `WarnUnknown` の診断文 | 1 | 表示用（最後まで残る） |

### 9.5.3 残っている作業（次便へ）

1. **`@mark("A")` のラベル**——**点つき名前の家族と同じ `ParseMarkName` に絡んでいる**ので、
   移すならそこを割ってから。**値としては `@text` と同型**（文字列 1 個）。
2. **`@frame`** — 字が意味。**引数ノードは既にそれを保持している**（`Text`）ので、
   移すのは機械的。⚠️ **書く本は 0 冊なので網が唯一の番人**（§9.2）。
3. **`@fig` と `@chord`** — §9.5.1 の部分言語。**小さなパーサを書く便**。
   `@chord` は `Arguments[0].Text` が既に `"c:m7"` なので**3 往復のうち 2 つは無料で消える**。
4. **`MarkName` 自体をいつ消すか**——上の 3 つが済むと残るのは点つき名前の家族だけなので、
   **そのとき `MarkName` は「点つき*名前*を綴る」1 用途に狭まる**。名前もそう変えること。
