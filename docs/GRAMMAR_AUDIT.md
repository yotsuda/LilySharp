# Lily# 文法監査 (grammar audit)

2026-08-21 起票。対象は `.lys` の**言語仕様そのもの**——`docs/GRAMMAR.md` 全 1191 行、
`GRAMMAR_FOR_LLM.md`、`SYNTAX_REFERENCE.md`、`GRAMMAR_STATUS.md`、`grob-override-scope-design.md`
と、`Parser/` `Syntax/` `Semantics/` `Svg/Model/` の対応箇所。

⚠️⚠️ **この監査はビルドせずに書かれた。** 起票セッションの環境に `dotnet` が無く、
**一度もコンパイルも実行もしていない。** 全ての指摘は原文読解で、RULES §5 の「推論せず測る」を
満たしていない。`測` と印した項目は**実測してから動くこと**。

> ✅ **2026-08-22・第228 が `測` 印を全部走らせた**（ビルドできる環境で・**結果は §10**）。
> **§1.1（`$` の唯一の機能）と §3.1（費用＝テスト 1 本）は実測で*確認***、
> **§8.1 の「変拍子が無料で正しくなる」は実測で*一部反証***（5/8 と 8/8 だけ・**7/8 は落ちる**）。
> `測` 印は残してあるが、**各項の直後に実測結果を差し込んである**。

分類: **決定済み**=判断は出ているが実装が追っていない／**欠落**=文法に表現手段が無い／
**曖昧**=同じ綴りが位置や隣接で意味を変える／**不整合**=同じ語彙が 2 箇所に書かれて食い違う／
**設計**=欠陥ではない。動いているが**製品の狙いと合っていない**もの（§8）。

---

## 0. なぜ起票したか

C# スクリプトを `.lys` に埋め込む案を検討し、**取りやめた**（2026-08-21・ユーザー決定）。
理由は §6 に置く。その結果、「いずれスクリプトで書ける」前提で空けてあった要求が
**すべて文法側の宿題に移った**ので、現状の文法がそれを受けられる形かを確認した。

結論: **予約語を増やさない設計（`@name` と `override Grob.property`）が拡張余地の全てで、
形としては正しい。** 足りないのは形ではなく中身と、下の曖昧さ。

⚠️ **§8 だけ出自が違う。** 同日に製品の狙い（平易な文法・リアルタイムプレビュー・
最初の対象はアマチュア・最も強い用途はリードシート）が明言されたので、
**欠陥ではないが狙いと合っていない**ものを分けて起票した。§1〜§4 とは判断者が違う。

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

> ✅ **実測した（2026-08-22・第228）。読解は当たっていたが、*壊れ方*が想定より悪い。**
> `phrase sn { c4 d e f | }` を `$sn` と裸 `sn` の 2 綴りで `lysc ly` に通した:
>
> | 綴り | 出力 | 診断 |
> |---|---|---|
> | `$sn` | `elative c' { c4 d e f }` ＝**フレーズに届く** | 無し |
> | 裸 `sn` | `m = \drummode { … }` ＋ `
ew DrumStaff { \m }` ＝**staff ごとドラム譜に化け、フレーズ本体は消える** | **エラーではなく** LYS2006（小節が短い）だけ |
>
> ⇒ ★ **失敗は静かで、しかも局所ではない**——「フレーズが読めない」ではなく
> **treble の旋律 staff が丸ごと DrumStaff に置き換わる**。`$` を外すなら
> **宣言側の診断は「あれば安い」ではなく*必須***（無いと動く本が黙って別の絵になる）。
> **宣言側は今なにも言わない**——`phrase q` / `phrase sn` / `phrase bd` は **`lysc check` 緑**。
> 一方 `phrase es` / `phrase c` は**既に宣言でエラー**
> （`Expected a name, found 'es', a reserved word — pick another name`）
> ＝**「音高名は定義上届かない」という読解は正しい**（`$` はそこでは何も区別していない）。
> **横取りされる名前は 63 個**（canonical 31 ＋ alias 31 ＋ `q`。`DrumNameRegistry` を*実行して*数えた）。
> **コーパスの `phrase` 名 156 種に衝突は 0**・**`$name` 参照は全 572 冊で 5 箇所 4 冊だけ**
> （`Fixtures/showcase/grammar-tour.lys` の `$theme`、`audit/lilypond-ref/cases/` の `$top`／`$bottom` 3 冊）
> ＝**書き換えの対象はこの 4 冊。**

> ### ⚠️⚠️ 第228 第3便が**実装を通しでやって、緑にできずに戻した**（2026-08-22）
> **`scratch/p228/dollar/dollar-removal.patch`（40 ファイル・207+/161−）が動く実装**。
> **設計上の穴は全部埋まった**が、**無関係な既存欠陥 1 つが出口を塞いでいる**。次便はこの patch を
> 当てるところから始められる（`git apply`）。**残っているのは docs 7 本と、下の ⑷ の判断だけ。**
>
> **⑴ 「`$` が区別しているものは無い」は 3 回反証された。** 監査は読解で「技術的必然は無い」と
> 断じたが、実測は**3 つの族**を出した——**⒜ ドラム語彙＋`q`**（監査自身の ⚠️測。静かに壊れる）・
> **⒝ clef 語 `bass`/`treble`/`alto`/`tenor`**（**監査は見落とし**。宣言は通り、裸参照は LYS0030）・
> **⒞ 強弱記号 `p`/`f`/`mf`…**（**宣言側で既に「予約語」として拒まれていた**——つまり
> `phrase p` は元から不正で、**テストは `$` とエラー回復の陰でだけ通っていた**）。
>
> **⑵ ⒝ は「拒む」ではなく「届かせる」で閉じた**（実装済み・patch 内）。`ParserTests` が
> **「`bass` パートは宣言・参照・section 化・構造化できる」を意図的に pin している**ので、
> `$` を外した副作用でその保証の phrase 半分を黙って失うのは誤り。**music stream で裸の clef 語を
> 参照として読む**（`clef bass`/`tuning bass` は自分のキーワード経由なので曖昧さは無い）。
> ⚠️ **⒜ は同じ手が使えない**——`q` とドラム名は*本物の music item* なので、宣言側で拒むしかない
> ＝**LYS1030（新設・patch 内）**。⒞ は既存の「予約語」診断がそのまま効く。
>
> **⑶ 監査のインベントリは code 側も test 側も過小だった。** code は 5 者ではなく **8 者**
> （＋`SyntaxKind.Dollar`・`DrumNameRegistry` の doc・`Parser.Directives` の hint）。
> test は「`ParserTests.cs:1560` の 1 本」ではなく **30 ファイル・101 行**（テストは `.lys` を
> C# 文字列で組み立てる）。**docs 7 本は当たっていた**——しかも **docs の例はテストされている**
> （`GRAMMAR.md`／`GRAMMAR_FOR_LLM.md`／`SYNTAX_REFERENCE.md`／`TUTORIAL.md` の 4 本が赤くなった）。
>
> **⑷ ★★★ 出口を塞いでいるもの＝`IncrementalParseTests.WithChange_RandomizedEdits_MatchFullParse`。**
> このテストは `Source` を種にした**決定論的 fuzz** なので、`Source` から `$` を 1 文字消すと
> **乱数の当たり先が全部ずれる**。ずれた先で**全パースと増分パースの木が食い違う**（子 9 対 8）。
> ⚠️ **これは本便が作った欠陥ではない**——**Core の変更を stash して戻した build でも同じ赤**＝
> **既存欠陥**。再現文書は `scratch/p228/dollar/fuzz-diverge.txt`（`/*` `*/` が不均衡な fuzz 文書
> ＝テスト自身のコメントが「増分レキサの最悪ケース」と名指す regime）。
> ⇒ **次便の判断はここだけ**: ⒳ その増分レキサの欠陥を先に閉じる（別島・§5.1「1 島 1 関心」）か、
> ⒴ fuzz の当たり先を動かさない形で `Source` を書き換えるか。**⒴ を選ぶなら「欠陥を隠した」に
> ならないよう、⑷ の起票を残したまま**にすること。**本便は勝手に決めずに戻した。**
>
> ✅ **第229（2026-08-22）が ⒳ で閉じて、§1.1 全体を完了した。** 欠陥の正体はレキサではなく
> **`IncrementalReuseMap.HasDiagnosticIn` の strict 重なり判定**——`Expect` は「見つかった
> トークン＝次アイテムの先頭」の span で報告するので、**産み手のアイテムの span に*接するだけ*で
> 重ならない診断**があり、産み手が再利用されると診断だけが消えた（2026-08-16 の zero-width 修理の
> 幅 2 版）。接触も数える 1 本の arm に直し（`b957ae5b`）、**先に建てた Probe `reuse` 計器で
> before/after 同値**（perf 3 冊・toggle/type-in とも不動）を確認してから patch を適用した。

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

### 2.1 `paper { }` / `layout { }` が存在しない ★ → ✅ **第232 が実装（2026-08-23）**

用紙サイズ・余白・五線サイズ・段間隔・システム間隔を **`.lys` から一切指定できない。**
`PageLayouter.cs:347` は `_options.PageWidth` を読み、これは API 側にしか無い。
マニフェストの層も無い（`UsingExpander.cs` はあるがプロジェクトファイルの概念は無い）。

**フォントだけ指定できて用紙が指定できない**のは `fonts { }` を作った動機と整合しない。
`fonts { }` の 4 段フォールバック（role → group → generic → bundled・狭い方が勝つ）が
そのまま雛形になる。出版社のハウススタイルという用途では**ここが最大の欠落**。

> ✅ **第232 が実装（2026-08-23・ユーザー判断 4 つ→ GRAMMAR §2.5）**。判断＝
> ⑴ **狙い＝ハウススタイル（§2.1 単独・A/B は需要が現れた日に）** ⑵ **単位接尾辞を導入**
> （`210mm`/`29.7cm`/`8.5in`・**数字に糊付け**＝1 量。裸の数値は ss）⑶ **語彙＝寸法系全部**
> （用紙・余白・indent・raggedRight・spacing 群。**アルゴリズム切替は載せない**）
> ⑷ **段間隔の分担＝paper に寄せる**（§2.2 の欄）。
> 実装の骨: 語彙は LP の `\paper` 変数の camelCase（scalar 10・flag 1・spacing spec 13×4）、
> 変換は **mm→ss ×72.27/127 を 6 桁丸め＝既定値の計算と同一**なので**既定を書いた本は既定と
> byte 一致**（PaperBlockTests が pin）。reader は `PaperPlanReader`（ONE HOME・collector と
> `PaperValidator` の 2 呼び手＝fonts と同じ契約）、Score.Paper → `new LayoutEngine(score.Paper)`
> ×6 site。診断は LYS9001〜9006。**露出しなかったもの**: `StaffHeight`（単位の枠そのもの・
> LP も staff size は `\paper` 外）・`SystemSpacing`（不活性と実測済みの札あり）・breaking 切替。
> **exporter は未輸出を warning で名指す**（drummap 型の穴＝paper は Lily# の絵を動かすので
> fonts の「書かない」判断とは逆向き。追跡本に paper を書く本は 0 なので今日盲目の probe は無い）。

> **第231 が決定材料を測った（2026-08-23）。判断は未着手＝何を載せるかは狙いの持ち主。**
>
> **候補台帳＝`LayoutOptions`**（internal・`Svg/Layout/LayoutOptions.cs`。単位は ss・
> LP 既定値と 6 桁一致の注記つき）: PageWidth／PageHeight・Margin×4・StaffHeight・
> Indent／ShortIndent・RaggedRight・SpacingIncrement・VerticalSpacing（system-system 等）・
> StaffSpacing・UseOptimalPageBreaking／PageBreaking。**CLI（lysc）はどれも露出していない**
> （フラグ全数: all/combined/crop/debug/help/no-embed-font/output/pitches/relative/scale/
> score/verbose/version——page 系ゼロ）＝今日これを動かす手段はテストの internal 構築だけ。
> **テスト・ツール側の需要実測**（非既定で組んだ site 数）: PageHeight 7・PageWidth 6・
> VerticalSpacing 3・StaffSpacing 3・UseOptimalPageBreaking 3・SpacingIncrement 2・
> Margin 系 6・Indent 2・SystemSpacing 1。
>
> **雛形**: `fonts { }`（GRAMMAR の `FontDecl`）。`paper { }` は PaperDecl →
> LayoutOptions overlay の 1 対 1 写像で、役割×グループの構造が無いぶんフォールバック段は
> 要らない＝fonts より簡単。**設計判断が要る点は 3 つ**:
> ⑴ **語彙の範囲**（用紙・余白だけか、indent／ragged／spacing まで載せるか）。
> ⑵ **単位の綴り**（LP は `210\mm`。Lily# に単位接尾辞の前例が無い——mm 固定か、ss か、
> `a4` のような紙名か）。⑶ **§2.2 との分担**——段間隔は LP では **system-system が
> `\paper` 変数・staff-staff が StaffGrouper grob** という分担。LP に合わせるなら
> `paper { }` と override の両方に 1 つずつ載る。全部 `paper { }` に寄せる案は
> 「1 構文で完結」だが LP の慣習から外れる。⚠️ HANDOFF §2F の恒久注意
> （page 系を override に載せない）はどちらの案でも守られる。

### 2.2 override の語彙が狭い ★

C# 埋め込みを捨てた以上、**これが唯一の拡張経路**。構文の形（`Grob.property = value`、
LilyPond と同じ語彙、予約語にしない）は正しいので、問題は設計ではなく中身。
現状の実態は §4.1 に。

> **第231 が決定材料を測った（2026-08-23）。判断は未着手＝何を足すかは狙いの持ち主。**
>
> **現状の器**: 語彙 4 行（`NoteHead`／`Stem` × `transparent`／`color`）。scope 機構
> （staff/voice タグ・`once`／`revert`・replay する timeline＝`GrobPropertyResolver`）は
> 実装済みで語彙から独立——**行を 1 つ足せば scope 一式が付いてくる**。値は typed
> （`LysValue`・小数/負数/文字列/識別子）。
>
> **発見＝§4 の形の 3 例目（今回は文法の門も閉じている）**:
> `StaffSpacingParameters.ApplyOverrides`（`LayoutEngine.cs:94` から生きている）は
> `StaffGrouper.staff-staff-spacing.*`／`staffgroup-staff-spacing.*` の **8 綴り**
> （basic-distance／minimum-distance／padding／stretchability）を完全実装している。だが
> ⑴ whitelist に行が無く（書けても LYS1029 error）、⑵ **文法が dotted sub-property を
> 綴れない**（`ParseGrobPropertyName`（`Parser.Music.cs:812`）は hyphen 結合のみ・
> 2 つ目の `.` は `Expect(Equals)` で壊れる）。到達者はテストだけ
> （`AlignmentDistanceTests` が `GrobOverride` を直接構築）。⚠️ 適用は **score-wide 一発**
> （`ApplyOverrides` は measure／staff scope を見ない）＝載せるなら positional 意味論なしを
> 明記するか、位置を配管するかの判断が要る。
>
> **需要の実測 3 面**:
> 1. **lp-regression category=override 89 冊**（README「Lily# が同じ override を持てば
>    個別判断で拾ってよい」の母集団）: **54 冊は `\override` を 1 つも書かない**
>    （`\set`／`\with` で除外＝§2.2 の射程外）・**override のみの 28 冊は全部一点物**
>    （首位でも `Score.BarLine.hair-thickness` 7 回・1 冊）・mixed 7 冊。
>    ⇒ **綴りを 1 つ実装して戻る本は約 1 冊——コーパス再開は語彙拡張の動機にならない。**
>    StaffGrouper を書く唯一の本（page-spacing-staff-group-nested.ly）も `\with` で
>    塞がったまま。
> 2. **追跡 .lys 572 冊**: override を書くのは complex-once.lys 1 冊（transparent 対）＝
>    どんな拡張も既存コーパスの出力・exit code を動かさない。
> 3. **LP 語彙との距離**: `\hideNotes`（ly/property-init.ly）は **6 grob**
>    （Dots・NoteHead＋no-ledgers・Stem・Accidental・Rest・TabNoteHead）に transparent を
>    張る。Lily# は「inked な 2 grob」だけ＝**休符・付点・臨時記号・加線は隠せない**。
>
> **候補群と値札**:
> - **A. StaffGrouper spacing 8 綴り** — reader 費用 0（実装済み）。要るのは文法
>   （dotted sub-property・`ParseGrobPropertyName` の拡張）＋ 8 行＋ pin／completion／docs。
>   ⚠️ 段間隔は §2.1 `paper { }` の候補でもある——**§2.1 と対で決める**（§4.2／§4.3 の
>   「対で決める」と同族。片方だけ決めると同じ量の家が 2 つできる）。
> - **B. transparent／color の面を広げる**（Dots・Accidental・Rest・加線・Beam…）—
>   per grob で draw site に reader 1 つ＋行 1 つ（resolver が届いていない site は配管も）。
>   `\hideNotes` 対応が完成する。
> - **C. 幾何ノブ**（Stem.length・Beam.thickness・StaffSymbol.staff-space・font-size…）—
>   per 綴りで本実装（幾何配管）。需要は一点物（89 冊表の長い尾）。force-hshift と同じく
>   「本実装の便」の族。
> - **D. `\set`／`\with` は override 構文の外**＝§2.2 の射程外（89 冊のうち 54 はこちら。
>   別の起票が要る）。
>
> **ユーザーに要る判断**: ⑴ 狙いはどれか（LP idiom 完成＝B／ハウススタイル＝A＋§2.1／
> コーパス再開＝測定上動機薄）。⑵ A を採るなら dotted sub-property が GRAMMAR に入る。
> ⑶ §2.1 との分担（段間隔をどちらの構文に置くか）。
>
> ✅ **判断済み（2026-08-23・ユーザー）＝狙いはハウススタイル・§2.1 単独を実装し、
> 段間隔は paper { } に寄せる。** 決め手＝⑴ 能力差は今日ゼロ（どちらの綴りも終着は同じ
> `StaffSpacingParameters`・score-wide）⑵ override 側は「綴りが位置依存に見えて効かない」
> 傷（scope 一式が付いてくるのに reader は見ない）か位置配管の費用を先に払うことになる
> ⑶ paper は綴り＝意味が最初から一致し dotted sub-property の文法拡張も不要。
> **A（StaffGrouper 8 綴り）・B（\hideNotes 完成）・C（幾何ノブ）はどれも見送り＝需要が
> 現れた日に**（A は「paper＝既定・override＝グループ個別上書き」として後から足せる——
> 退路は塞がっていない）。**D（`\set`/`\with`）は引き続き別の起票。**
> §2.2 の器と scope 機構はそのまま（語彙 4 行のまま）。**第232 が §2.1 側を実装**（§2.1 の欄）。

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

> ✅ **実測した（2026-08-22・第228）——毒で測って、数まで当たっていた。**
> `ParseStaffRender` の `if (Check(StringLiteral) || IsPartNameKind(Peek(0)?.Kind))` から
> **裸識別子の枝を落とした build** で suite 全数:
> **合格 5718 / 失敗 1 / スキップ 4**、赤いのは
> **`ScoreRowFoldingTests.TheRetiredWithSpelling_ReadsAsADisplayNameAndARow` 1 本だけ**
> ＝**この監査が名指したその 1 本。snapshot 222 枚は全部緑**
> ＝「コーパスは 1 バイトも動かない」が **grep の 0 件ではなく*出力*で**示された。
> ⇒ **§9-5 の費用見積りは確定。着手可。**

> ✅ **第229 が完了（2026-08-23・ユーザー承認）。** `ParseStaffRender` から裸識別子の枝を
> 落とし（`DisplayName = String` のみ）、pin は
> `TheRetiredWithSpelling_ReadsAsAPartRefAndARow` に改名——`staff vocal with lyrics ja` は
> 「staff＋未宣言 part `with` への参照（**診断が名指す**・以前は黙ってラベルに食われた）＋
> fold する lyrics 行」として読む。docs 3 本の「語順の回避策」段落は削除
> （位置が意味を変えない、が新しい仕様文）。赤は毒実測どおり名指しの 1 本だけ・snapshot 222 不動。

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

> ✅ **第230 が実装（2026-08-23）＝LYS1031（warning・exit 0）。** 発火条件は
> 「**繰り返しの参照先が小節線の向こうに在る**」——直前の*書かれた*イベント
> （音符・和音・スラッシュ・ドラム・`q`・先行 bare duration）との間に小節線が立つ形。
> 小節内の走行（`bes8 8 8 8`）は無音・跨いだ最初の 1 個が鳴り、以後の連鎖は
> その bare が anchor になるので**跨ぎ 1 回につき 1 本**。休符は resolver と同じく透過
> （`| r4 4` も鳴る＝脱字はどこでも起きる）。判定は `BareDurations` の**同じ 1 回の walk** に
> 載せた（§5.2.1②＝validator は自分で数え直さない）。
> **コーパス実測 572 冊で 0 件**（textual grep 2 形＋ `lysc check` 全冊の 3 経路で確認）＝
> 追跡本の出力・exit code は 1 つも動かない。pin は `BareDurationTests` 5 本。

---

## 4. 語彙と実装の不整合

> CLAUDE.md「**同じ量を計算する場所が 2 つ以上あったら、そこが次の欠陥の住所**」がそのまま出た。
> 対応語彙が **whitelist**（`SupportedGrobOverrides`）と **reader 群** の 2 箇所に書かれ、
> 片方だけ更新された。**両方向に食い違っている。**

### 4.1 実態

| プロパティ | reader | whitelist | 結果（起票時） |
|---|---|---|---|
| `NoteHead.color` / `Stem.color` | **生きている** | **無い** | 描画される。**ただし LYS1029 error + exit 1** |
| `NoteHead.transparent` / `Stem.transparent` | 生きている | ある | 正常 |
| `NoteColumn.force-hshift` | **無効化** | ある | 通るが何も起きない |

✅ **第229 後の実態**: color は「ある・正常」、force-hshift は「無い・LYS1029」＝両方向一致。

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

> ✅ **第229 が完了（2026-08-23・ユーザー承認済み＝§4.3 と対で「対で閉じる」を選択）。**
> color 2 行を追加し、pin（validator／completion）と docs 5 本を同便で更新。
> **コーパスの color override 使用は 0 冊**＝実本の出力・exit code は 1 つも動かない。

### 4.3 `force-hshift` — 逆向きの同じ欠陥

whitelist にあり検証を通るのに、`ElementCoordinator.cs:49` の `ForceHshiftEnabled = false` で捨てられる
（初回リリース向け・resolver は温存）。**4 本の doc が「黙って無視することはない」と断言している
当の silent no-op がこれ**で、`GRAMMAR.md` の Example 自身が 2 行書いている。

⚠️ **4.2 と 4.3 は対で決めること。片方だけ直すと欠陥が生き残る。**

> ✅ **第229 が完了（2026-08-23・ユーザー決定）。** 方向は「有効化」ではなく **whitelist から
> 外して正直な LYS1029 に**——`ForceHshiftEnabled = false` の理由がコード内に明記されている
> （現実装は値が正規化で消え、列全体に当たる＝per-voice shift ができない）ので、
> 「有効化」は 2 行仕事ではなく本実装の便。**whitelist＝実装済みの一覧という自らの原則に
> 両方向で一致させた**：row は本実装が載る commit で flag と同時に戻す（ElementCoordinator の
> コメントと `SupportedGrobOverrides` の不在コメントが互いを名指す）。
> **コーパスの force-hshift 使用は 0 冊**・docs の例は 4 本とも書き換え（例文はテストされている）。

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

## 5. 完了（`ffc8f7f` で push 済み・doc のみ）

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
  （`<c e g2>` vs `<c e g 2>`、tempo 実行列の読み方——「なぜそう決めたか」まで書いてある。
  ⚠️ ここに挙げていた `'(3)` vs ` (` は **2026-08-28 に廃止**——**明文化されていることと
  読みやすいことは別**で、ユーザーが後者で判断した。この項が褒めているのは前者のまま）
- **`fonts { }` の 4 段フォールバック**は素直で拡張しやすい（§2.1 の雛形）
- **音楽と提示の分離** — `lines` を part から score へ移した判断（2026-08-19）は正しい
- **単一パス・前方参照なし**は AI 生成と相性が良く、原則として一貫している

---

## 8. 製品の狙いから出たもの（欠陥ではない）

> §1〜§4 は欠陥。**ここは壊れていない。** 直さなくても何も落ちないが、
> 直さないと狙いが達成されない類のもの。判断者が違う。
>
> ⚠️ **§8.1 は 2026-08-21 に全部決まった。** 綴り・`.` の意味・細分・小節境界まで確定し、
> 費用も測ってあるので、**残っているのは実装だけ**（ビルドできる環境が要る）。
> 決定の根拠と落選案は各項に残してある——**蒸し返さないため**。

### 8.1 コード記号の入力が、言語で最も平易でない部分になっている

**前提**（ユーザー明言・2026-08-21）:

- 製品の価値は「**LP の文法を平易にしたこと**」と「**リアルタイムプレビュー**」。忠実度はその下（`RULES.md` §5.6）
- 最初の対象は**アマチュアの作曲家・演奏家**。出版社は将来
- 最も強い用途は**リードシート**——「高品質なリードシートを簡単に書ける体験は他で得にくい」

**現状**（`Parser.Sections.cs:324`・`GRAMMAR.md` §5 の `ChordEntry`）は **LilyPond の書式そのまま**:

| 音楽家が書く | Lily# が要求する |
|---|---|
| `Am` | `a:m` |
| `G7` | `g:7` |
| **`Gm7b5`** | **`g:m7.5-`** |
| `C/G` | `c/g` |

しかも音楽家が自然に書く `@chord(C)` / `@chord(Dm)` は **`LYS1008 = UnknownAnnotation`** に落ちる
（専用の案内すら無い）。

⚠️ **問題は「LP 互換であること」ではなく位置。** **最も強い用途の中心**で、
**LP 経験が最も薄い層**（ジャズ／ポピュラーでリードシートを書く人）が最初に触る場所に、
**LP の中でも最も批判される記法**が置かれている。`m7.5-` がそれ。

⚠️ `GRAMMAR.md` の設計原則 5 は「**practical conventions** を継承する（Scheme の複雑さは継承しない）」。
**コード入力は LP の practical でない側**なので、原則の趣旨からは外れている。

#### ⚠️ 決定的な証拠 — 作者自身がその綴りで書いている

`samples/canon-in-d.lys:20`（**コメント**）:

```
// The immortal ground: D  A  | Bm  F#m | G  D  | G  A
```

**人間に向けて書くときは、この綴りで書いている。** 大文字の根音・`#` の臨時記号・裸の quality
（`Bm`）・**小節相対の配置（音価が 1 つも無い）**。本体では同じ進行を別の綴りに翻訳している。
⇒ **言語が表現できない記法を、そのファイル自身のコメントが使っている。**
案 1（小節相対）まで同時に裏付けている。

#### 文法上の余地

**大文字 `A`–`G` は空いている。** Lily# の音高は小文字なので（`Lexer.cs:499` の `first is >= 'a' and <= 'g'`）、
`Am` は `PitchA` ではなく **`Identifier` に字句化される**。chords ブロックで曖昧さなくコード記号に解釈できる。
⚠️ **`R`（全休符）との衝突だけ未確認。**

#### ⚠️ ただし音価と衝突する（大文字化が持ち込む唯一の問題）

有効な音価 `1 2 4 8 16 32 64 128` と、数字で始まる quality `6 7 9 11 13 2 4 5 69` の重なりは:

| 綴り | 判定 |
|---|---|
| `G7` `C9` `Am6` `F13` `C69` | **曖昧なし**（7・9・6・13 は音価に無い） |
| **`C2` `C4`** | **決まらない** — sus2/sus4 か、二分/四分か |

**重なるのは `2` と `4` だけ。** ただし**最も使う音価 2 つと、最も使う拡張 2 つ**なので、狭いが致命的。

#### 綴り — 臨時記号と変化音（ユーザー決定・2026-08-21）

**シャープ `#` / フラット `b` / 変化音 `+` `-`。**

| | 記号 | 空き状況 |
|---|---|---|
| 根音の臨時記号 | `#` `b` | **`#` は完全に空き** — レキサに処理が無く、`GRAMMAR.md` の句読点一覧にも無い。コーパスでは**コメント内にしか現れない** |
| 和音内の変化音 | `+` `-` | `+` は `SyntaxKind.Plus`（`Lexer.cs:192`）で使用中——後述 |

⚠️ **これは趣味ではなく帰結。** 根音が `#`/`b` を取った以上、変化音は別の記号でなければ
`Bb5` が決まらない（B♭ の power chord か、B の ♭5 か）。集合を分けることで曖昧さが原理的に消える:

| 書く | 意味 |
|---|---|
| **`Bb5`** | **B♭5 で確定** |
| **`B-5`** | **B の ♭5 で確定** |
| `Bb-5` | B♭ の ♭5 |

⚠️ **「5 度は +/-」では穴が残る。規則は「変化音はすべて +/-」。**
`b9` を残すと `Bb9` が再び決まらない（B♭9 か B の ♭9 か）。`#11`・`b13` も同じ。
⇒ `C7-9` `Ab7+11` `C7-13`。これで **`b` と `#` は根音とベース音にしか現れない**。

⚠️ **`+` は chords 内で既に使われている。** `Parser.Sections.cs:351` の `/+`
（LilyPond の `CHORD_BASS`・`c/+g`）。位置で区別はつく（`/` の直後か quality 内か）が、
1 つのトークン内で `+` が 2 義になる。⇒ **`/+` は落とすことを勧める**——
「転回を示さずベース音を足す」という LP の細部で、リードシートでは使わない。
（`time 3+2/8` の加算拍子・`Parser.Declarations.cs:480` は文脈が別なので無関係。）

**まとまった形:**

```
ChordSymbol = Root [ '#' | 'b' ] [ Quality ] [ '/' Root [ '#' | 'b' ] ]
Root        = 'A' .. 'G'
Quality     = { 'm' | 'maj' | 'dim' | 'aug' | 'sus' | 'add' | 数字
              | ('+'|'-') 数字 | '+' | '-' }
```

`C` `Am` `G7` `F#m` `Gm7-5` `C7+5` `C+` `Ab7+11` `Bb13` `Csus4` `C/G` `C/Bb`

#### 案

**案 1（推奨）: 小節相対にして、音価を書かない**

実際のリードシート（フェイクブック・iReal Pro・Nashville ナンバー・ChordPro）は
**音価を書かず、小節線と位置で表す**。

```
chords prog { | Am | F G | C . . G7 | Dm7 . G7 . | }
```

1 小節に 1 つ＝その小節を占める／2 つ＝等分／`.`＝前のコードを 1 拍伸ばす。
**数字が音価として現れないので衝突が消える。** `.` は chords ブロックで未使用
（`ChordsBlock = 'chords' Identifier '{' { ChordEntry | Barline } '}'`）。

⇒ 現状の `c2 g:7 | a:m f |` は running duration に依存し、**小節ではなく直前の音価**で決まる。
書き手が数える必要がある。小節相対はそこも外す。**今より平易になる方向の変更。**

**案 2: quality を裸にして `:` を音価に回す** — `Am` `Gm7b5` `C/G` `Am:2` `F:4.`
quality に `:` は出ないので完全に曖昧さ無し。骨格を保ったまま `:` の役割を入れ替えるだけ。
⚠️ music では `:` が tremolo（`c4:8`）なので、**同じ記号が文脈で別物**になる。原則 3「局所性」から減点。

**案 3: 大文字化をやめる**（現状維持）。衝突は大文字化が持ち込むので、やめれば消える。
代償は**リードシートの入口が `g:m7.5-` のまま**であること。

#### 推奨

**案 1 単独。案 2 とは併用しない。**

1. 案 1 だけで用途の大半が閉じる。音価を明示したい場面は稀で、稀な場面は `.` で足りる
2. **併用すると綴りが 2 つになる**——`$` を廃止した理由（§1.1）がそのまま戻る
3. 案 1 は**覚えることが減る**方向。案 2 は記号の意味を 1 つ増やす

⇒ **「大文字コード ＋ 小節相対配置」を 1 セットで採る**か、**現状維持**かの二択。
**中間（大文字にして音価も書けるようにする）が最も筋が悪い。**

なお採る場合、`a:m` は**追加ではなく置き換え**（未リリースなので移行診断は不要・§1.1 と同じ理屈）。

#### `.` の意味（**決定済み**・2026-08-21・ユーザー承認）

**`.` は「1 拍ぶん、直前のコードを伸ばす」。拍は LP の beat structure が定める。**

実装の入口は既にある——`BeamingPattern.Options.For(timeSig)`（`BeamingPattern.cs:177`）が
`BeatBase` と `BeatStructure` を返す。

```
1 小節のスロット数 = BeatStructure.Length
スロット i の長さ  = BeatBase * BeatStructure[i]
```

| 拍子 | structure | スロット | 1 スロット |
|---|---|---|---|
| 4/4 | [1,1,1,1] | 4 | 四分 |
| 3/4 | [1,1,1] | 3 | 四分 |
| **6/8** | **[3,3]** | **2** | **付点四分** |
| 9/8 | [3,3,3] | 3 | 付点四分 |
| 12/8 | [3,3,3,3] | 4 | 付点四分 |
| 5/8 | 表で上書き | 2 | 不均等（3+2） |

**採った理由**（蒸し返さないために残す）:

1. ★ **コードのスロットと梁のグループが同一の格子になる。** `AutoBeamCheck` が旋律の梁を
   切っているのと同じ beat 格子を読む。別の「拍」概念を持ち込めば、**同じ量が 2 箇所にある状態**
   ——CLAUDE.md が「次の欠陥の住所」と呼ぶもの——を自分で作ることになる。
   五線の梁が 2 つに割れている小節で、コード行が 6 スロットに割れていたら同じ譜面の中の矛盾
2. **規則を発明しない。** `BeamingPattern.cs:166` に
   `LILYPOND-REF: scm/time-signature-settings.scm:367-381 calc-simple-fraction-structure`
   付きで既に在る（「numerator が 3 より大きく 3 で割り切れるならグループは 3、でなければ 1」）。
   §5.2 に完全に沿う。落選案は独自規則になり `LILYSHARP-OWN:` が要った
3. **変拍子が無料で正しくなる。** 5/8 は LP の表が不均等グループを持っているのでそのまま出る

**落選**（再検討不要）:

- **分母の単位を 1 スロット**（4/4→4・**6/8→6**）——6/8 は 2 つに感じる拍子。**音楽的に誤り**
- **書かれたスロット数で小節を等分**——拍子非依存で表も要らないが、4/4 に 3 スロットを書くと
  1 スロット＝4/3 拍という無意味な分割が通る。6/8 が 2 で正しくなるのも**規則ではなく偶然**
- **上＋表現可能性の検証**——穴は塞がるが**規則を発明する**ことになる

#### 付随して決めたこと

**① 細分を許す。** 書かれたスロット数が拍数の**整数倍 k** なら、**各拍を k 等分**する。
4/4 に 8 スロットなら八分刻み。倍数でなければ診断（既存の小節長チェック LYS2001 と同じ形）。

⚠️ **不均等拍子では細分が拍ごとに違う長さになる。** 5/8 [3,2] で k=2 なら、
第 1 拍は 3/16 が 2 つ、第 2 拍は 1/8 が 2 つ。**定義としては一意だが直感に反する。**
実装時に実際の 5/8 のコード譜で確かめること。⚠️測

> ⚠️ **実測した（2026-08-22・第228）——「変拍子が無料で正しくなる」は*一般には成り立たない*。**
> `BeamingPattern.Options.For` を全拍子で**実行して**拍構造を印字した:
>
> | 拍子 | beatBase | 構造 | 出所 |
> |---|---|---|---|
> | 6/8 | 1/8 | `[3,3]` | 既定規則（numerator が 3 で割り切れる） |
> | **5/8** | 1/8 | **`[3,2]`** | **表の上書き**（不均等・監査の主張どおり） |
> | **8/8** | 1/8 | **`[3,3,2]`** | **表の上書き**（不均等） |
> | 4/8 | 1/8 | `[2,2]` | 表の上書き |
> | **7/8** | 1/8 | **`[1,1,1,1,1,1,1]`** | **表に無い**＝既定規則に落ちる |
> | 5/4・7/4 | 1/4 | `[1,1,1,1,1]`・`[1,1,1,1,1,1,1]` | 表に無い |
>
> ⇒ ★★ **不均等グループを持つのは LP の表が上書きしている 3 拍子（4/8・5/8・8/8）だけ。**
> **7/8 は 7 スロット**になる——5/8 の次に多い変拍子で、しかも実譜はほぼ [3,2,2]／[2,2,3] に感じる。
> §8.1 の採用理由 3「**変拍子が無料で正しくなる**」は**5/8 と 8/8 では真、7/8 では偽**。
> **これは設計を否定しない**（LP の拍を採るという規則自体は一貫している＝`LILYSHARP-OWN` を
> 増やさない）が、**「無料で正しい」と書いた行は「LP の表にある拍子だけ無料」に直すべき**。
> 7/8 のコード譜を書く人には `[1]×7` が出る、と**先に言うのが仕様**。

**② `.` は小節をまたがない。** 小節ごとに独立。小節頭の `.` は伸ばす対象が無いので診断
（`| C | . |` ではなく `| C | C |` と書く）。タイは音符の概念であってコード記号には要らない。

⚠️ 実譜には「前の小節と同じ」を表す `%` / `/` の記号があるが、**これは `.` とは別の機能**。
今回のスコープ外——必要になったら独立した記号として起票する。

#### 費用（**実測済み** 2026-08-21）

**外部ユーザーのファイルはゼロ**（未リリース）。書き換えが要るのは**このリポジトリのコーパスだけ**:

| | |
|---|---|
| `chords` ブロックを持つ `.lys` | **15 / 571**（2.6%） |
| 実際に使われている quality | **9 種** — `m`(28) `maj7`(19) `7`(12) `m7`(3) `m7.5-`(2) `sus4` `sus2` `m6` `m13` |

**変化音構文を使っているのは `m7.5-` の 2 箇所だけ。** 残りは `a:m`→`Am`、`c:maj7`→`Cmaj7`、
`g:7`→`G7` の機械的置換。⇒ **費用は小さい。**

#### ⚠️ 検証手段が既にある（この変更の一番良い性質）

**入力の綴りが変わっても、音楽は変わらない。** よって**描画された SVG は byte 一致するはず**。
⇒ **220 枚の SVG snapshot が、そのまま書き換えの正しさの証明になる**——
**snapshot が 1 枚でも動いたら、書き換えが間違っている。**

snapshot の再ベースは**不要**であり、**してはならない**。再ベースは
「出力を変える変更」の手順（LP 照合 → 承認 → 実行）を踏むものであって、赤を消す手段ではない
（`RULES.md` §5.1・1083 行）。この変更は定義上**出力を変えない**ので、赤が出たら
書き換えが誤っているという意味しかない。⇒ **`RULES.md`:474「snapshot は再ベースできるので
網ではない——承認は観測者ではない」がそのまま当てはまる場面。**

> ⚠️ **上の「byte 一致」は 1 語だけ狭かった（第230 実測）**——SVG は **data-pos（ソース位置）を
> 属性に持つ**ので、書き換えでオフセットがずれた分だけ snapshot は**幾何同一のまま byte が動く**。
> 検証は「**data-pos を剥がして byte 一致**」で行った：snapshot 10 冊（git の旧版との対比でも確認）
> ＋ 非 snapshot 8 冊（HEAD worktree の旧ビルド×旧綴り vs 新ビルド×新綴りの直接レンダ）＝**全 18 冊一致**。

> ✅ **第230 が実装（2026-08-23）＝案 1「大文字コード＋小節相対」一式。**
> - **綴り**：entry は印字形そのもの（`ChordEntry = Root [#|b] [Quality] [/Root [#|b]]`）。
>   実装は**トークン run**（`Am`=1 Identifier・`F#m`=Identifier+BadToken`#`+Identifier）を
>   `ChordEntrySyntax.SymbolText` で結合し、**`ChordStructure.TryParseChordEntry` の 1 文法**へ
>   （@chord と chords{} は従来どおり 1 書式）。`#` は chords 本体でも黙許（@chord/@fig 引数と同じ
>   Parser 序文の領域追跡）。registry は `m7.5-`/`m7b5` → **`m7-5`**・`+`＝aug を追加。
>   **`/+`（added bass）は廃止**（'+' が quality に入ったため。表示は元々同一・BassIsAdded は
>   XML importer 用にモデルへ残る）。旧綴りは LYS1028 が **run 単位で 1 回**名指す。
> - **`.` と格子**：`ChordExtendSyntax`（新 kind）＋ `ChordRhythm.SlotDurations`＝
>   `BeamingPattern.Options` の拍構造（梁と同じ 1 格子）。1 slot＝小節・拍数と同数＝拍・
>   整数倍 k＝細分・約数＝拍の束。**外れは LYS2009（warning・等分 fallback）**、
>   **小節頭の `.` は LYS2010（error・時間は経過）**——判定は描画と同じ walk が記録し
>   `ChordRowGridValidator` が発話（BeamPairing の型）。旧 `ChordRhythm` の 4/4 専用表
>   （3 個→4 4 2 等）は**廃止**＝3/5/6/7 個は今は診断つき等分。
> - **費用実測**：書き換え＝corpus 15 冊＋inline @chord 3 冊＋テスト 24 file＋probe 7 site・
>   docs 4 本・completion（挿入＝symbol そのもの・`:` 品質補完は廃止）・harmonizer（symbol を生成）・
>   XML importer の @chord 出力。**幾何は上記 18 冊全て同一・suite 全緑**（snapshot 10 枚は
>   data-pos のみで再ベース）。全 572 冊 `lysc check` で新診断 0 件。

---

## 9. 順序

1. ✅ **§4.2 + §4.3 — 第229 が完了（2026-08-23・ユーザー承認）**。color 2 行を whitelist へ、
   force-hshift は whitelist から外して正直な LYS1029 に（本実装が載る便で row と flag を同時に戻す）。
   コーパス使用は両者 0 冊＝実本の出力不変
2. ✅ **§1.1 `$` 廃止一式 — 第229 が完了（2026-08-22）**。出口を塞いでいた増分パースの
   診断落ち（`IncrementalReuseMap` の接触判定）を先に閉じ（`b957ae5b`・再利用率の計器つき）、
   第228 の patch を適用、docs 7 本と tmLanguage の `$` 規則も掃いた。
   書き換え 4 冊は sweep A/B で**幾何不動**（grammar-tour は data-pos のみ＝コメント追加ぶん）
3. ✅ **§2.2 override の語彙 — 判断済み（2026-08-23・ユーザー）＝A/B/C とも見送り・
   段間隔は paper { } に寄せる**（§2.2 の欄。語彙拡張は需要が現れた日に・D は別の起票）
4. ✅ **§2.1 `paper { }` — 第232 が実装（2026-08-23・ユーザー判断 4 つ）**＝GRAMMAR §2.5。
   語彙・単位・分担の決定と実装の骨は §2.1 の欄
5. ✅ **§3.1 `DisplayName` — 第229 が完了（2026-08-23・ユーザー承認）**＝引用符必須・裸は常に
   MIDI 専用 part 参照。✅ **§3.3 bare duration の診断は第230 が実装（LYS1031・2026-08-23）**。
   残り＝§1.2 リネーム（ユーザーが MSVS で。§4.4 は完了）

✅ **§8.1 コード記号の書式 — 第230 が実装（2026-08-23・ユーザーが順位を名指し）**
（大文字根音・`#`/`b`・変化音 `+`/`-`・小節相対・`.` = LP の拍。詳細と実測は §8.1 の完了欄）。
起票時の残メモ:

- 費用は測定済み（`chords` を持つ `.lys` は **15 / 571**・quality は **9 種**）
- **220 枚の snapshot が正しさの証明になる**（出力は変わらないはずなので、動いたら誤り）
- ✅ **実装前に測る 2 点は第228 が測った（2026-08-22）**:
  - **5/8 の細分** → `[3,2]` で出る（**が 7/8 は `[1]×7`＝「変拍子が無料」は 3 拍子だけ**。§8.1 の該当欄）
  - **`R` と大文字根音** → **衝突は実在するが 1 冊 1 綴り**。`chords` 行の中に
    **`R1` が現に書かれている**（`audit/lp-regression/lys/chord-names-rests.lys`＝
    `r`／`s`／`R` が N.C. を刷る規則の本・観測者は `ChordNamesTests.ChordRow_RestsPrintNC_SkipsDoNot`）。
    **`R` は A〜G に無いので字面の衝突は無い**が、**大文字＝根音という規則を入れるなら
    `R`／`r`／`s` を chords 行の休符として*明示的に*除外する必要がある**（今は「大文字が来ない」ことに
    寄りかかっている）。**コーパスの chords 行に立つ大文字はこの `R1` だけ**（全 572 冊・
    `section A` のような keyword 先導は曖昧にならないので数から外した）。
  - ⚠️ **ついでに再検算した**: `chords` を持つ本 **15 / 572**（監査の 15/571 と同数）・
    quality **9 種**（監査どおり）・**`#` は本当に空き＝コメントと文字列リテラルを除くと全 572 冊で 0 件**
    （最初に粗く数えて 79 冊と出したのは `title "Lily# …"` 等の**文字列の中**。監査が正しい）

⚠️ **§8 の他の項が増えたら、それはこの順序の外。** 欠陥ではなく**狙いに関わる判断**なので、
上の 1〜5 と競合させず、**狙いを決めた人が決める**。決まれば実装は §2・§3 と同じ棚に並ぶ。

**1 と 2 はビルドできる環境が要る。** 3〜5 は設計判断が先。

> ✅ **第228（2026-08-22）が「ビルドできる環境」で `測` 印を全部潰した。**
> **2 の唯一の技術的門（`phrase sn`）は開いた**——読解どおり `$` が唯一の到達手段で、
> **外すなら宣言側の診断が必須**（無いと staff ごと DrumStaff に化ける・§1.1 の実測欄）。
> **書き換え対象は corpus 4 冊・5 箇所**、docs 7 本、コード 5 者、テスト 1 本。
> **1 はユーザー承認だけが門**（技術的な門は無い）。**5 の §3.1 も費用が出力で確定**。
> ⇒ **次便が最初にやる測定はもう無い。** 残っているのは実装と承認。

---

## 10. 実測の再現手順（第228・2026-08-22）

**この監査は原文読解で書かれたので、数を引き継ぐ人は数え方が要る**（HANDOFF §0 の 7 例目）。
道具は `scratch/p228/gate1/`（**git 管理外**——無ければ下から建て直す。10 分）。

| 測ったこと | 道具 | 答え |
|---|---|---|
| `$sn` と裸 `sn` の差 | `lysc ly scratch/p228/gate1/{dollar,bare}-sn.lys` → `.ly` を読む | `elative` vs `\drummode`＋`
ew DrumStaff` |
| 宣言側が何を拒むか | `lysc check scratch/p228/gate1/decl-{es,c,q,sn,bd}.lys` | `es`・`c` は赤／`q`・`sn`・`bd` は緑 |
| 横取りされる名前の数 | `DrumNameRegistry.CanonicalEntries`／`AliasEntries` を**実行して**数える | 31 + 31 + `q` = **63** |
| corpus の `phrase` 名と `$` 参照 | `git ls-files '*.lys'` を正規表現で走査 | 156 種・衝突 0／`$` は **5 箇所 4 冊** |
| §3.1 の費用 | `ParseStaffRender` の裸識別子の枝を**毒で落として** suite 全数 | **5718/1/4**・赤は名指しの 1 本・snapshot 222 緑 |
| 拍構造 | `BeamingPattern.Options.For` を全拍子で**実行**（internal なのでリフレクション） | 5/8 `[3,2]`・8/8 `[3,3,2]`・**7/8 `[1]×7`** |
| chords 行の大文字 | `chords NAME { … }` の本体を全 572 冊から切り出して集計 | **`R1` 1 冊だけ**（`section A` は keyword 先導＝除外） |
| `#` の空き | `//` コメント**と文字列リテラル**を落として `#` を数える | **0 件 / 572 冊** |

⚠️ **`#` は 2 度数えた。** 1 度目は `//` だけ落として **79 冊**と出た——中身は全部
`title "Lily# 機能ツアー"` のような**文字列の中の製品名**だった。
**句読点の空きを数えるときは、コメントと文字列リテラルの両方を落とす。**
（この監査の `#` の主張は正しく、**間違っていたのは検算のほう**だった。）

**第231（2026-08-23・§2.1／§2.2 の決定材料）の数え方**:

| 測ったこと | 道具 | 答え |
|---|---|---|
| override 89 冊の内訳 | status.json の category=override を列挙し、各 `.ly`（lilypond-src/input/regression）を `\override Grob(.sub)*.prop` 形・`\set`・`\with` の 3 正規表現で走査 | override 無し 54／override のみ 28（全部一点物）／mixed 7・綴り首位 7 回 |
| 追跡 .lys の override 使用 | `git ls-files '*.lys'` 全冊を行頭の `once?` ＋ `override`／`revert` で走査 | **1 冊**（complex-once.lys・transparent 対） |
| StaffGrouper reader の到達性 | `ApplyOverrides` の呼び手を grep（`LayoutEngine.cs:94` のみ）＋ `ParseGrobPropertyName` を読む（dot は 1 段） | 文法から到達不能・テストのみ（`AlignmentDistanceTests`） |
| `\hideNotes` の LP 語彙 | `lilypond-src/ly/property-init.ly` の定義を読む | 6 grob ＋ no-ledgers |
| LayoutOptions のテスト需要 | `Prop = ` の綴りを Tests／Cli／audit で grep して集計 | PageHeight 7・PageWidth 6・Vertical/StaffSpacing 各 3 … |
| lysc の露出フラグ | `"--` を LilySharp.Cli から grep | 13 本・page 系 0 |
