# 引継ぎ — `feature/transpose-and-timesig-change`(① transpose / ② 小節途中の拍子変更表示)

文法レビュー(リリース硬化)で見つかった2つの**未実装機能**を実装するブランチ。散文は日本語、パス/識別子は原文。

## 0. 一行サマリ
- **② 小節途中の拍子変更の表示**: 足場 commit 済み(`cc2e7ed`)だが **未完(描画されない)**。次の一手は「mid-music `time` が collector の新ケースに流れない」原因特定。
- **① transpose(移調)**: **未着手**。`transpose` は part-option としてパースされるだけで意味解析ゼロ → 相対音程移調を新規実装する。
- ブランチは master(`7971e3f`)から分岐。`cc2e7ed` は full suite 1462緑(回帰なし)。

## 1. 背景(なぜこの2つか)
リリース前の文法表現力レビュー(`grammar-support-matrix.md` は manual beam 等で stale、実機は `grammar-tour.lys` が全機能描画する)で、実測した残ギャップ:
- **transpose**: `transpose:` は part-option パースのみ。Bind/Expand/Collector のどこにも移調処理が無く、書いても**無言で何もしない**(誤った=移調されない楽譜が黙って出る)。
- **小節途中の拍子変更**: clef/key は変更時に `ClefChangeItem`/`KeySignatureChangeItem` を生成し描画するが、**time は change-item が無い**。`time 3/4` を小節途中に書くと、拍数には(部分的に)効くが**拍子記号が描かれない**。
- (参考)pickup `partial 4` は**動作する**(reference 未記載だが実装あり)。`@`-annotation は text-registry 解決で additive=破壊的変更不要。**単字 dynamic 名(`p`/`f` 等)は識別子に使えない**(lexer が Dynamic 化)。relative octave のみ。

## 2. ② 小節途中の拍子変更表示 — 現状と次の一手

### 済(`cc2e7ed`)
- `LilySharp.Core/Svg/Model/MusicItem.cs`: `TimeSignatureChangeItem`(zero-duration、Clef/Key 雛形)。
- `MeasureCollector.cs`: `MeasureBuilder._timeSignature` を可変化、`AddItem` が `TimeSignatureChangeItem` で measure 長を re-arm(後続小節の自動完了長を更新、duration 0)。music-stream switch(`:~1058`)に `case TimeSignatureSyntax` を追加し item 発行。top-level switch(`:~1308`)を `IsInsideMusicContent` でガード(初期 time のみ global 設定)。
- `SharedRenderer.cs`: dispatch switch に `case TimeSignatureChangeItem`、`EnumerateStaffItems` の column-path に hang-left の X 減算、`DrawTimeSignatureChange`(`DrawTimeSignature` を full-size 呼び+`gc.Source`)。

### 既知の不具合(必ず読む)
`c4 d e f | time 3/4 g4 a b |`(`C:\temp\hard\g_midchange.lys`、相対モード)で **3/4 が描かれない**。SVG 実測:`time 3/4` の source 位置(data-pos 173)に出るグリフは **U+E0EA = notehead**(拍子記号でない)。つまり:
- 私の `case TimeSignatureSyntax`(MeasureCollector 1034 のスイッチ)に **mid-music `time` が流れていない**疑い。section 内パート楽譜の収集は 1034 とは**別経路**の可能性大。
- その位置に**音符が出ている**=`time 3/4` が音符として解釈されている経路がある?(要確認)。

### 次の一手(調査順)
1. **section-part 楽譜を収集するメソッドを特定**。1034 のスイッチが入っている method(`foreach (var node in ...)`)が section 内パート music を本当に処理しているか。`BuildPartMeasures`(Binder/Expander 系、`:~351`)→ どの collector メソッドへ流すかを追う。`time` の `TimeSignatureSyntax` がそこへ来るか確認。
2. 来ていなければ、その経路に同じ `case TimeSignatureSyntax`(item 発行 + builder.AddItem)を足す。
3. **単一譜は item-slot パス**(`useColumnTiming=false`)。`MeasureLayouter.LayoutItems` が `TimeSignatureChangeItem` に**幅/スロットを確保**するか確認(clef/key は `SpacingRules.GetClefChangeWidth`/`GetKeySignatureChangeWidth`)。無ければ `GetTimeSignatureChangeWidth`(= `GlyphMetrics.GetTimeSigWidth`)を足し、item-slot 経路でも X が付くように。EnumerateStaffItems の column-path 減算は既に追加済みだが、**単一譜は item-slot 経路**なので別途要対応。
4. system 跨ぎ: 拍子記号は変更点に1回だけ(clef/key のように毎 system 反復しない)。`DrawSystem` の prefix time sig は first system のみ。変更が system 頭に来る場合は item が measure 頭に出るので OK のはず。要検証。

### 検証レシピ
- Lily#: `dotnet run --project LilySharp.Cli -- png C:\temp\hard\g_midchange.lys C:\temp\out.png`(svg も)。SVG で `data-pos="<offset>"` のグリフ codepoint を確認(U+E0EA=notehead、時間記号は数字/common 系)。
- LP 突合: `lily/time-signature-engraver.cc`(変更で TimeSignature grob 生成)。`time 3/4`/`6/8`/`2/2` を変更点に置いた最小 .ly を `lilypond --png -dcrop=#t` で。
- 全 snapshot は mid-music time を含まないので、完成後は回帰テスト用に `Fixtures/test/timesig-change.lys` を追加し snapshot 化推奨。

## 3. ① transpose(移調)— 未着手・設計

### 現状
- `transpose` は SyntaxKind.TransposeKeyword(`Lexer.cs:443`)、part-option として `ParsePartOption`(`Parser.cs:1590`、`transpose: <value>` 形)でパースされるのみ。**意味解析が一切無い**(Bind/Expand/Collector に transpose 処理ゼロ)。

### 設計(LP 忠実=diatonic 音程移調)
- 構文: part-option `transpose: <pitch>`(1値)。「書いた c が <pitch> の高さで鳴る」= **c→<pitch> の音程**を全ピッチに適用、と解釈(LP `\transpose from to` の from=c 固定版)。`from to` 2値が要るなら part-option パーサ(1値前提)の拡張が必要。
- 適用箇所: part の music を構築する所(`BuildPartMeasures` `:~351` 付近)で、part に transpose があれば全 `NoteItem`/`ChordItem` のピッチと**調号**を移調。
- アルゴリズム(diatonic): from→to の (diatonic step 数, semitone 数) を求め、各音符: 新音名 = 音名 + diatonicStep(7 で wrap、オクターブ調整)、新臨時記号 = **元の semitone 関係を保つ**よう綴り直す(単純な semitone シフトでなく音名ベース)。`NoteItem` は `StaffPosition`(half-spaces)+ `Accidental`(string)で持つ(`MusicItem.cs:48-`)ので、StaffPosition と Accidental の両方を再計算。
- エッジ: ダブル臨時(isis/eses)、多シャープ/フラット調、enharmonic、オクターブ wrap、調号移調(KeySignature の sharps 数)。テスト必須。
- LP 参照: `scm/music-functions.scm`(transpose)、`lily/pitch.cc`(Pitch::transpose, 音程演算)。

### 検証
- `transpose: d` で `c d e f` → `d e fis g`(全音上)を期待。`C:\temp\hard\g_transpose.lys` を相対モードに直して(part 名は `p` 不可=DynamicP 衝突、`melody` 等)。LP `\transpose c d { c d e f }` と突合。

## 4. 環境・規約(厳守)
- シェルは ripple `execute_command`(shell=pwsh)。PowerShell/Bash ツール禁止。引用符の入れ子は継続プロンプト(`>>`)で固まるので避ける(単引用符 or 文字列連結)。
- **Lily# は relative octave のみ**。probe は相対で書く(`'`/`,` は最近接からの追加移動)。
- **part 名に単字 dynamic(p/f/mf 等)を使わない**(lexer が DynamicP 等に解釈し parse 崩壊)。
- LP クローン `C:\MyProj\lilypond-src`、`lilypond` は PATH(v2.24.4)。LP crop: `cd C:\temp; cmd.exe /d /s /c "lilypond --png -dresolution=300 -dcrop=#t -o out in.ly < NUL > out.log 2>&1"`。
- ビルド `dotnet build LilySharp.Core` / テスト `dotnet test LilySharp.Tests`(snapshot のみ `--filter "FullyQualifiedName~SvgSnapshotTests"`、計43)。snapshot fixture は `LilySharp.Tests/Fixtures/{test,showcase}/`(samples/ から分離済み)。再ベースラインは `LILYSHARP_UPDATE_SNAPSHOTS=1`(**LP 突合で正当性確認後のみ**)。
- LSP デプロイ `.\deploy-extension.ps1`(version 自動 bump、空行ノイズは LSP version 行だけ checkout→再編集で除去してから commit)。
- commit 英語 + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`。ship(tag/publish/告知)は全緑+明示 GO。
- probe 群: `C:\temp\hard\*.lys`(g_midchange/g_pickup/g_transpose 等)。

## 5. やってはいけない / 教訓
- byte 一致に拘らない(ユーザー判断)。検証は **LP 突合 + snapshot 再ベースライン**。無検証の再ベースライン禁止。
- 急いで2機能を詰めない。移調は1符号ミスで黙って誤楽譜=リリース硬化の趣旨に反する。
- 「単一 flip 一括反転」(旧 Stage4)は破綻済み・再提案しない([[project_lilysharp_stage4_yup]])。
