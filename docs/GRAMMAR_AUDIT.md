# Lily# 文法監査 (grammar audit)

2026-08-21 起票。対象は `.lys` の**言語仕様そのもの**——`docs/GRAMMAR.md` 全 1191 行、
`GRAMMAR_FOR_LLM.md`、`SYNTAX_REFERENCE.md`、`GRAMMAR_STATUS.md`、`grob-override-scope-design.md`
と、`Parser/` `Syntax/` `Semantics/` `Svg/Model/` の対応箇所。

⚠️⚠️ **この監査はビルドせずに書かれた。** 起票セッションの環境に `dotnet` が無く、
**一度もコンパイルも実行もしていない。** 全ての指摘は原文読解で、RULES §5 の「推論せず測る」を
満たしていない。`測` と印した項目は**実測してから動くこと**。

分類: **決定済み**=判断は出ているが実装が追っていない／**欠落**=文法に表現手段が無い／
**曖昧**=同じ綴りが位置や隣接で意味を変える／**不整合**=同じ語彙が 2 箇所に書かれて食い違う。

---

## 0. なぜ起票したか

C# スクリプトを `.lys` に埋め込む案を検討し、**取りやめた**（2026-08-21・ユーザー決定）。
理由は §6 に置く。その結果、「いずれスクリプトで書ける」前提で空けてあった要求が
**すべて文法側の宿題に移った**ので、現状の文法がそれを受けられる形かを確認した。

結論: **予約語を増やさない設計（`@name` と `override Grob.property`）が拡張余地の全てで、
形としては正しい。** 足りないのは形ではなく中身と、下の曖昧さ。

---

## 1. 決定済み・未実装

### 1.1 `$` フレーズ参照の廃止 ★

ユーザー決定は出ている（記号がユーザーに分かりにくく導入障壁になる）。**実装が追っていない。**
現状 5 者が食い違う:

| 場所 | 状態 |
|---|---|
| `Lexer.cs:197` | `'$'` を `SyntaxKind.Dollar` として字句化 |
| `Parser.cs:417,482` / `Parser.Music.cs:131` | `Dollar` を `ParseVariableReference()` へ |
| `Parser.Declarations.cs:583` | `Expect(SyntaxKind.Dollar)` |
| `SyntaxNodes.Declarations.cs:613-616` | 「optional な `$`」として構文木にモデル化 |
| `ParserTests.cs:1560` | `ParseDollarVariableReference` が `$theme` の成功をピン留め |
| `Parser.Declarations.cs:622` のコメント | 「the `$` sigil is gone」 |
| docs 7 本 | `$` を教えている（`GRAMMAR_FOR_LLM.md` = canonical spec 含む） |

**技術的必然は無い**（読解で確定）。`Lexer.cs:481-530` の `ScanPitchOrRestOrIdentifier` は
文脈を見ずに音高と識別子を分け、`SyntaxFacts.cs:108-111` の `IsPartNameKind` は
`Identifier` と `bass/treble/alto/tenor` のみ——**`PitchA`〜`PitchG` も `RestR/S` も入っていない**。
`PhraseDecl` は `ExpectPartName()`（`Parser.Form.cs:496`）を通るので、**`phrase es { }` や
`phrase c { }` は宣言時点で既に書けない。** ゆえにフレーズ名は定義上すべて `Identifier` に
字句化され、音楽ストリームの `Identifier` は既に `ParseBareVariableReference()` に流れる。
**`$` が区別しているものは無い。**

⚠️測 **唯一の例外**: `Parser.Music.cs:167-170` が `Identifier` から `q`（和音反復）と
**ドラム語彙**（`DrumNameRegistry`）を横取りする。しかも**打楽器パートかを見ていない**ので、
`phrase sn { }` は全パートでドラム音符として読まれ、`$sn` が唯一の到達手段のはず。
**`$` に残る唯一の機能がこれ**——外す前に必ず測ること。
対処は宣言側で 1 診断（`phrase` 名が `q` かドラム名なら拒否）。参照ごとに記号を書かせるより安い。

**移行診断は入れない**（2026-08-21・ユーザー決定）。未リリースにつき移行対象が存在しない。
`Diagnostic.cs:186-190` の LYS8007 の前例と同じ理由——「a migration path for a spelling that
could not have reached anyone, since Lily# has never been released」。`LYS0031`・`LYS0013` も同様に廃番済み。
⇒ `Lexer.cs` から `'$'` ごと落として、`$theme` は素の `LYS0030` に落とすのが正解。

線引き（今後も同じ）: **LilyPond の綴りを書いた人への案内は残す**（`LYS0006` `repeat volta`、
`LYS0008` `<< \\ >>`、`LYS4009` `cis!`、`LYS1008` `@chord(C)`、`\` 付きコマンド）。
**旧 Lily# 綴りへの移行案内は足さない。**

### 1.2 `LYS1012` の名前が化石

`BareReferenceRequiresDollar`（`Diagnostic.cs:583`）。現在の発火条件は
**音名の綴り間違いヒント**（`eb` → `ees`、`Parser.Declarations.cs:634`）でドルと無関係。
⚠️ リネームは RULES に従いユーザーが MSVS で行う。`ParserTests.cs:1320` が参照。

---

## 2. 文法に無いもの（欠落）

### 2.1 `paper { }` / `layout { }` が存在しない ★

用紙サイズ・余白・五線サイズ・段間隔・システム間隔を **`.lys` から一切指定できない。**
`PageLayouter.cs:347` は `_options.PageWidth` を読み、これは API 側にしか無い。
マニフェストの層も無い（`UsingExpander.cs` はあるがプロジェクトファイルの概念は無い）。

**フォントだけ指定できて用紙が指定できない**のは `fonts { }` を作った動機と整合しない。
`fonts { }` の 4 段フォールバック（role → group → generic → bundled・狭い方が勝つ）が
そのまま雛形になる。出版社のハウススタイルという用途では**ここが最大の欠落**。

### 2.2 override の語彙が狭い ★

C# 埋め込みを捨てた以上、**これが唯一の拡張経路**。構文の形（`Grob.property = value`、
LilyPond と同じ語彙、予約語にしない）は正しいので、問題は設計ではなく中身。
現状の実態は §4.1 に。

---

## 3. 曖昧さ（原則 7「曖昧さのない文法」への抵触）

### 3.1 スコア項目の裸識別子が位置依存

```
ScoreItem   = … | PartRef              (* 裸の part 名 = MIDI 専用 *)
StaffRender = 'staff' [ClefName] PartRef [DisplayName] …
DisplayName = String | Identifier
```

`staff flute piccolo` の `piccolo` が**表示名か MIDI 専用パートかは直前に何があったかで決まる。**
`GRAMMAR.md` §7 自身が「MIDI 専用パートは staff より前か、括弧グループの後に書け」と回避策を書いている。

⚠️ **パーサの正当化が誤っていた**（2026-08-21 に訂正）。`Parser.Form.cs:583` の
`ParseStaffRender` には「following render items always begin with a keyword, so a trailing
identifier is unambiguous」と書かれていたが、**`ScoreItem` には裸の `PartRef`（MIDI 専用パート）
があり、これはキーワードで始まらない。** `staff flute click` は `click` を flute の表示名として
食い、クリックトラックが黙って鳴らなくなる。`GRAMMAR.md` §7 は衝突と回避策を明記しており、
**仕様書がパーサのコメントを反証していた**。コメントは実態に書き換え済み。

⇒ **`DisplayName` から `Identifier` を落として引用符必須にすれば消える**（`staff X "Piccolo"`）。
`part X "Violin I"` は既に文字列形式なので統一にもなる。

**費用を測った**（2026-08-21・`samples/` `audit/` `Fixtures/` の `.lys` 571 本）:

| | 使用数 |
|---|---|
| 裸形式 `staff X piccolo` | **0**（コーパスに 1 件も無い） |
| 引用符形式 `staff X "Piccolo"` | **0**（表示名を使うテストは C# 側で組み立てている） |

裸形式を留めている唯一のものは `ScoreRowFoldingTests.TheRetiredWithSpelling_ReadsAsADisplayNameAndARow`——
**廃止済みの `with` 節が「"with" と名付けられた staff ＋ row」に優雅に劣化する**という挙動のピン留めで、
誰かが求めた機能ではない。しかも `with` は未リリースのまま廃止された綴りなので、**§1.1 の
「未リリースにつき移行対象なし」の原則がそのまま当てはまる**——ここが構文エラーになって困る人はいない。

⇒ **費用はテスト 1 本の書き換えだけ。** コーパスは 1 バイトも動かない。⚠️測（未実測）

### 3.2 `[` の三重定義

インライン volta（`[1. …`）・手動ビーム（`[ … ]`）・そして bare duration の導入で
**`[4. 8]` が volta 4 と読まれる**ため、書き手が `[/4. 8]` / `[bes4. 8]` と回避する必要がある
（`GRAMMAR.md` §8.2）。bare duration が持ち込んだ衝突。**回避を書き手に負わせている。**

### 3.3 bare duration の静かな誤読

`4 g f e`（`a4 g f e` の打ち間違い）が**黙って通る**。承知の上の決定として HANDOFF §3 に記録済み
（LilyPond が同じ綴りに払っている代償）。ただし**言語で唯一の静かな誤読経路**であり、
AI 生成を主用途に置く言語で 1 文字の脱落が別の曲になる。

⇒ 診断で個別に受けるのが妥当（「小節頭の裸の数字は直前の音の繰り返しです。`a4` のつもりなら
音名を書いてください」）。**バージョン表記の代替はこれ**（§6.1 参照）。

---

## 4. 語彙と実装の不整合

> CLAUDE.md「**同じ量を計算する場所が 2 つ以上あったら、そこが次の欠陥の住所**」がそのまま出た。
> 対応語彙が **whitelist**（`SupportedGrobOverrides`）と **reader 群** の 2 箇所に書かれ、
> 片方だけ更新された。**両方向に食い違っている。**

### 4.1 実態

| プロパティ | reader | whitelist | 結果 |
|---|---|---|---|
| `NoteHead.color` / `Stem.color` | **生きている** | **無い** | 描画される。**ただし LYS1029 error + exit 1** |
| `NoteHead.transparent` / `Stem.transparent` | 生きている | ある | 正常 |
| `NoteColumn.force-hshift` | **無効化** | ある | 通るが何も起きない |

### 4.2 色 — 動く機能を「非対応」と言っている ★

`SharedRenderer.Noteheads.ResolveColor`（`:919`・呼び出しは `:548 :582 :712 :833`）が
`ColorParser` 経由で色名と `#rgb`/`#rrggbb` を解釈して符頭と符尾に適用する。**機能は生きている。**

ところが `color` が `SupportedGrobOverrides`（`GrobProperty.cs:91-98`）に無いため、
`OverrideVocabularyValidator.cs:77` が **LYS1029 を error として出し exit 1**。
それでも**譜面は色付きで出る**——LYS1029 は best-effort 対象で、エラーでも出力が書かれる
（`CliBestEffortOutputTests.cs:146`）。
⇒ **正しく色が付いた楽譜が、その色を「このバージョンでは非対応」と言われながら出力される。**

**なぜ見落とされたか**が明確に残っている。検証器自身の MEASURED 注記が試した綴りを列挙している——
`Wibble.wobble`・`Stem.wibble`・`Stem.direction`・`Stem.length`・`Beam.thickness`・`stem.direction`——
**`color` が入っていない。** 色を一度も書かないまま「3 対だけが 1 バイトでも動かした」と結論した。
`SupportedGrobOverrides` は自分の注記で「プロパティを足すときは reader と行を同じコミットで」と
求めているが、**ここは reader だけが先に来た。**

⇒ 修正は **whitelist に 2 行**（reader は既にある）。**出力が変わる**ので RULES に従いユーザー承認が要る。
`OverrideVocabularyValidatorTests.cs` が `Spellings` をピン留めしている。

### 4.3 `force-hshift` — 逆向きの同じ欠陥

whitelist にあり検証を通るのに、`ElementCoordinator.cs:49` の `ForceHshiftEnabled = false` で捨てられる
（初回リリース向け・resolver は温存）。**4 本の doc が「黙って無視することはない」と断言している
当の silent no-op がこれ**で、`GRAMMAR.md` の Example 自身が 2 行書いている。

⚠️ **4.2 と 4.3 は対で決めること。片方だけ直すと欠陥が生き残る。**

### 4.4 `LYS0032` のコメントが自分から廃番審査を招いている

`Diagnostic.cs:532-539`。「Removed before the first tag」と**歴史で自己正当化**しているため、
未リリース基準で洗うと廃番候補に見える。**実際は残すべき**——(1) `chords` は生きたキーワードなので
`chords {` は必ず何かを報告する必要がある、(2) `voice { }` が正当に無名を取るので
**`chords { }` は歴史と無関係に新規ユーザーがやる間違い**。
⇒ 現在形の根拠に書き直す。雛形は `LYS0019`（`Diagnostic.cs:227`）——
「まだ構文として通り、しかも違う音楽になって黙る」と現在形で自分を正当化している。

**2026-08-21 実施済み。** 要約を現在形の 1 文にし、`<remarks>` に (1) `chords` が生きた
キーワードである以上その位置は必ず答えを要すること、(2) `voice { }` との類推で新規ユーザーが
やる間違いであること、の 2 つを根拠として置いた。歴史は歴史として残し、
「なぜこの框が廃番審査を招いたか」も併記した。

---

## 5. 完了（`8c54084` で push 済み・doc のみ）

- `grob-override-scope-design.md` — §4 の実態に全面書き換え。旧版は `NoteHead.color`/`Stem.color` を
  「消費する」としつつ他を落としており、**それを「消費しない」と直した第一版はさらに誤りだった**
  （reader を `Svg/Renderer/` に探して空振りした。実体は `Rendering/`）
- `GRAMMAR_STATUS.md` — 既知のギャップに色の件
- `GRAMMAR.md`・`GRAMMAR_FOR_LLM.md`・`SYNTAX_REFERENCE.md` — `force-hshift` が silent no-op である旨
- `GRAMMAR.md` §11 — 診断表は標本であって全体でない旨（実際は `Diagnostic.cs` に 131 コード）
- `DrumNameRegistry.cs` — `$` 前提の化石注釈を現状と §1.1 の衝突に置換

**同 2026-08-21・後続コミット**（コメントのみ・挙動不変）:

- `Diagnostic.cs` — `LYS0032` の根拠を現在形に（§4.4）
- `Parser.Form.cs` — `ParseStaffRender` の誤った正当化を訂正し、§3.1 の実測を併記

---

## 6. 取り下げた指摘 ← **蒸し返さない**

### 6.1 「言語バージョン表記が無い」

起票時に★で挙げたが**取り下げ**（2026-08-21）。比較対象を誤っていた——
汎用プログラミング言語はファイル内バージョン表記を持たないのが普通で、持つ場合も
プロジェクト側にある（Rust の edition = `Cargo.toml`、C# の `LangVersion` = `.csproj`）。
`GRAMMAR.md:20` が `\version` を拒否し `LYS0013` が廃番済みなのは**意図的な判断**。

さらにこの状況では**悪い手段**でもある: (1) `convert-ly` 相当の移行ツールが無く、
**移行ツールの無いバージョン表記はただの飾り**、(2) `.csproj` に当たる層が無いので
ファイルに書くしかなく、それは必要性ではなく消去法。

⇒ 指していたリスクの実体は **§3.3（bare duration の静かな誤読）** であり、そちらで受ける。

### 6.2 C# スクリプトの `.lys` 埋め込み

**採らない**（2026-08-21・ユーザー決定）。理由:
(1) 設計原則 1「単一パス」2「暗黙より明示」3「局所性」7「曖昧さのない文法」を**全部壊す**、
(2) **`.lys` が実行可能コードになる**——VS Code 拡張は受け取ったファイルを開いて自動描画する構成で、
LilyPond ですら `-dsafe` を用意しつつ完全でない、
(3) そもそも**フックする面が無い**——効くプロパティが 2 つの状態でスクリプトを載せても空回りする。

⇒ 拡張点は**ドキュメントの中ではなくホスト側 API**に置く。ホストは最初から C# なので埋め込む必要がない。
`.lys` は宣言的なデータのまま保つ。

---

## 7. 変えなくてよいもの（記録）

公平を期すために残す。

- **予約語を増やさない設計。** `@name` はテキストから解決され予約語にならない（`tr` が識別子として生きる）。
  `override` のプロパティ名も同様。**スクリプトを捨てた今、この 2 つが拡張余地の全て**で、形として正しい
- **曖昧さの解決規則が全て明文化され、実測日付つきで裏取りされている。** 隣接規則
  （`<c e g2>` vs `<c e g 2>`、`'(3)` vs ` (`）、tempo 実行列の読み方——「なぜそう決めたか」まで書いてある
- **`fonts { }` の 4 段フォールバック**は素直で拡張しやすい（§2.1 の雛形）
- **音楽と提示の分離** — `lines` を part から score へ移した判断（2026-08-19）は正しい
- **単一パス・前方参照なし**は AI 生成と相性が良く、原則として一貫している

---

## 8. 順序

1. **§4.2 + §4.3 を対で決める** — 出力が変わるのでユーザー承認が要る。whitelist 2 行で終わる
2. **§1.1 `$` 廃止一式** — コード・テスト・docs 7 本を 1 コミットに。⚠️ 先に `phrase sn` を測る
3. **§2.2 override の語彙を広げる** — 拡張経路の本体
4. **§2.1 `paper { }`**
5. §3.1 `DisplayName`（**費用測定済み＝テスト 1 本**）、§3.3 bare duration の診断、§1.2 リネーム
   （§4.4 は完了）

**1 と 2 はビルドできる環境が要る。** 3〜5 は設計判断が先。
