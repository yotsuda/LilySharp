# 値 site 監査 (value-expression site audit)

2026-08-15 起票（第165）。**2026-08-15 第166 で軸A を数え切った**（§1）。
**文法に「式」を導入する日に触ることになる site の全数調査**。
動機は [[GRAMMAR_STATUS.md]] の long tail を「壁」でなく「逃がし口」にする設計検討で、
そこでの結論は **「式の層を先に通す」のはスクリプトを一行も書かなくても黒字**、というもの。
本書はその工事の**対象範囲**を、推測でなく現物から数えたもの。

⚠️ **この監査は「site を数えた」だけで、直していない。** 数だけを引き継ぐと嘘になるので、
**数え方（§0）を必ず一緒に読むこと**。

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
| `Svg\Model\FiguredBassItem.cs:144` | 除外 | **文法内** — `@fig.6` の数字（§3.3 の 4 系統目） |

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
| `Parser.Form.cs:773` | MIDI part render の `instrument:N` / `octave:N` |
| `Parser.Declarations.cs:315,329` | `tempo` の値 run（marking / bpm / 拍単位 / 付点 / `=` / swing 語） |
| `Parser.Directives.cs:112` | ⚠️ **`drummap { … }` の本体を丸ごと生トークンで保持**（§7 ④） |
| `Parser.Music.cs:875` | `@name(引数)` の引数 run（token を無差別に収集） |
| `Parser.Declarations.cs:227,255` | metadata / font の**回復** run（誤り形の値を保持） |

#### A4（16）— 値が綴りか反復に埋まっている（**式の層では閉じない**）
- **反復で数を表す**（8）: オクターブ記号 `'` / `,` の収集——`Music:200,435,499,539` ／
  `Declarations:198`（top-level transpose）・`:393`（フレーズ）／音高トークン自身 `Music:194`・
  `Declarations:195`。⇒ **`c''` の「2」は token の個数**。式を入れるには**綴りごと変える**。
- **トレモロ `:8`**（4）: `Music:241,253,264,437`（音符・和音反復・drum・和音）。
- **弦番号 `\4`**（1）: `Music:921`。
- **mark 名の分節に数**（1）: `Form:155`（`ExpectMarkName` が `IntegerLiteral` を受ける＝`@fig.6`）。
- **和音品質 `maj7`**（2）: `Sections:301,305`。

### 1.2 軸B / 軸C（第165 の数・不変）

| クラス | 意味 | 数 | 内訳 |
|---|---|---|---|
| **B** | 構文アクセサが値へ**変換** | **17** | `Syntax\SyntaxNodes*.cs`（Declarations 7・`SyntaxNodes.cs` 6・Expressions 2・FormRender 1・Attachments 1） |
| **C** | 下流がアクセサを通さず**自前で再解釈** | **20** | Collector 8 / `GrobProperty` 2 / Semantics 8 / `DrumNameRegistry` 2 |

⚠️ **C が B より多いことが本監査の主眼**。値の解釈が構文層に閉じておらず、
**同じ値を 2 か所以上が別々に読んでいる**（第164 の「カットには表が 2 つある」と同型の形）。

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
| 図付き低音 `@fig.6` | `Parser.Form.cs:155` | `FiguredBassItem.cs:144` |

### 3.2 文字列が入る

`SyntaxKind.StringLiteral` は **28 か所 / 11 ファイル**。文法の受け口は:
`title`/`composer`（`SyntaxNodes.Declarations.cs:383`）・`part X "表示名"`（`:517`）・
`font "NAME"`（`:433`）・`using "f.lys"`（`Parser.Directives.cs:475`）・
`_"text"`（`Parser.Form.cs:166`）・`tempo "Allegro"`（`:201`）・staff ラベル（`Parser.Form.cs` の 7 件中）。
⇒ パーサ側の受け口は §1.1 の **A2 12 件**。

### 3.3 ⚠️ 名前の中に数が埋まっている（式の層では届かない別項）

**値が識別子の一部**になっているので、式を通すには**綴りごと変える**必要がある。
**第165 は 3 系統と書いたが、実数は 6 系統**（＋反復で数を表す 8 site＝§1.1 A4）。

| 綴り | パーサ | 読み手 |
|---|---|---|
| `@finger.3` | `Music.cs:905` | `MeasureCollector.cs:4780` — `name.AsSpan(7)` |
| `@bend.half` / `.full` / `.N`（半音数） | `Music.cs:905` | `MeasureCollector.Annotations.cs:503-507` — `markName[5..]`（**さらに §7 ③**） |
| トレモロ `:8` | `Music.cs:241,253,264,437` | `Lexer.cs:243`（分類のため）＋`MeasureCollector.ItemFactory.cs:216` — `text[1..]` |
| ★ `@fig.6` / `@fig.6.s`（図付き低音） | `Form.cs:155` | `FiguredBassItem.cs:144` |
| ★ 弦番号 `\4` | `Music.cs:921` | `SyntaxNodes.Attachments.cs:124` |
| ★ 和音品質 `maj7` / `sus4` | `Sections.cs:301,305` | 和音名の解釈側 |

⇒ **式の層と直交する。起票するなら別項**（HANDOFF ▶ ⒯⑷）。

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
   ⚠️ **残りの A3 7 件**（`tempo` run・`drummap` 本体・`@name(引数)` run・
   metadata/font の回復 run・MIDI part render）は**手つかず**＝次便の範囲。
   ⚠️ ★ **`LysValue.Real` は文法では書けない**（§7 ⑤）。
2. ~~軸A の残りを数え切る~~ — **第166 で完了**（§1.1・**19 → 68**）。
3. **§3.3 の 6 系統は別項**として起票（式の層では閉じない）。**§1.1 A4 の「反復で数」8 site も同じ項**。
4. **§4 は据え置き。**
5. **§7 の 4 件**は型付きの値が入れば自然に消えるもの——**先に消さないこと**（消しても
   配管が文字列のままなら別の形で戻る）。

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

**⑤ ★★★ 文法は小数を書けない**（第2便で判明）。**字句解析に小数が無い**——
`ScanNumber`（`Parser\Lexer.cs:336-362`）は**数字だけ**を食べ、`.` を見ない。
⇒ `override Stem.length = 3.5` は **`3` が値になり `.5` は落ちる**。
**コーパスで小数の override を書いている本は 0 冊**（実測）。`double.TryParse` を
5 か所が持っていたのに、**文法はそこへ届く値を作れなかった**。
⚠️ `LysValue.Real` は **C# の呼び手のためだけに存在する**（譜間 spacing と音符衝突のテスト）。
⚠️ ★ **小数リテラルを足すのは*文法*の変更**＝単独便の話で、しかも §4 の性能の但し書きが
そのまま当たる。**ここで勝手に足さないこと**（網
`LysValueTests.ARealIsNotWritableInTheGrammar` が現状を釘付けにしてある——
**この網が落ちたら、それは字句解析が育ったという報せ**）。

**⑥ 第2便の 1 つの取り決め**: **`Str` は数ではない**。文字列パイプラインは引用符を外して
から積んでいたので `= "10"` が `GetDouble` に 10 と答えていた——**型が無いことの漏れ**で
あって誰かが書いた規則ではない。**本番で文字列として読む property は `color` 1 つだけ**
なのでコーパスは動かない（網 `LysValue_AQuotedValueIsAStringAndAStringIsNotANumber`）。

---

⚠️ **第1便はコードを 1 行も変えていない**（数えただけ）。**第2便が §5 ⑴ を返済した**
（`0b846ea6`）——**出力は不動**（snapshot 無変更・台帳 514 点／ss 非ゼロ 97／
総和 3.609963181 で不動）、suite **4569 → 4575**（+6＝新しい網ちょうど）。
