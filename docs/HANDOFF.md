# Lily# 開発ハンドオフ（常設・単一）

> **このファイルが唯一の引継ぎ先。新しい `handoff-*.md` を作らないこと。**
> 引継ぎは §1「現在地」を**書き換えて**行う（追記しない）。恒久的な知識は §4 の表に従って
> それぞれの置き場所へ出す。ここに溜め込むと、以前と同じように 16 個に分裂する。

最終更新: 2026-07-24 / master ＝ **`ae53b356` 以降＝起票→修正→時値幅と 3 連続で commit＋docs・未 push**。⚠️ hash は自己参照——**HEAD は §0 で裏取り**。**このセッション（続き×2）＝break-align 描画 walk の 2 regime を対起票→修正、さらに残った −0.0953 の正体（4/4=C グリフの幅 2 モデル）まで閉じた**：①custom key の欠落 key 列（KCC −2.745→…・key 幅モデル `SpacingRules.KeySignatureInkWidth` 1 本化）②ossia key を系共有列へ（OKN/OKNF −3.365→**0 exact**・座標系ルールで ossia 調号 advance のスケール混在も修正）③**4/4・2/2 は C/¢ グリフ＝LILC 経路**（`timesig.C44/C22` ink **1.700000** を bake・`GetTimeSigWidth` に LP の style 分岐を字面移植・KCS/KCC/KC2 全て **0 exact**）。③で **4/4 第1システムの音符が +0.095264567 右へ＝snapshot 184 枚＋programmatic 2 枚再ベース**（機械照合: 全枚 行数不変・Y 系属性不変・viewBox 高さ不変＝純 X・改行移動ゼロ・PNG 目視済）。**3245 passed / 0 failed / 3 skipped**・Core 0 warn/err・**LP 忠実度 52/68 exact・total |residual| = 0.006267 ss**＝今日開いた 5 点は全て exact で閉じ、total はセッション開始時 baseline（slur/tie 残等）に復帰。直近 commit `c71a8218`（単音/グレース臨時記号 DRAW を position_apes へ一元化＋position 側も `CalculateSinglePosition`→`CalculatePositions` 1 本化）→ `d7240c27`（break-align 列 walk を `SolveColumns` 1 本に統一）→ `8ef0044e`（percussion defect-3＋clef ink を `ClefBBox` に統一）→ `43a73cea`（key→time +0.4 統一）→ `8e5a315d`（TSA probe）→ `824798c4`（break-align 列エンジン）→ `5f9ee531`（clef 実 ink・defect-3 pitched）。**前セッション（`c71a8218`）で島2 の残＝単音/グレース臨時記号の第2描画モデルを閉じた**：draw を reserve と同じ `AccidentalPlacement.CalculateSinglePosition` 経路（＋`DrawAccidentalAtInkLeft`）に通し、固定 gap の `DrawAccidental` を**削除**（dead）。台帳2点 `accidental.single-natural-to-notehead` **+0.0177→0**・`accidental.single-flat-to-notehead` **+0.12→0**（両予測が桁一致）。**さらに字面統一**: `CalculateSinglePosition` の単一 ape アルゴリズムを**削除**し、`CalculatePositions`（position_apes）の**1 要素呼び出し**へ畳んだ——LP は単音でも汎用 position_apes を 1 要素リストで回すだけで単音専用関数は無い。これで**単音も和音も同一の position エンジン 1 本**（break-align/`ClefBBox` と同型の重複解消・出力不変で実証）。⚠️ **flat の 0.12 は handoff 想定外の第2欠陥**——「sharp/flat は一致」は reserve だけで、**draw は flat の bbox.Left −0.12 overhang を誤計上して 0.12 外していた**（穴を開けたら別欠陥＝§5.2.1④）。**3234 passed / 0 failed / 3 skipped**、Core 0 warn/err。**LP 忠実度 47/63 exact・total |residual| = 0.006267 ss**（両点 0 で total 不変）。**snapshot 再ベース 11 枚**（flat +0.12・natural −0.0177 の純 X・**sharp 不変**・PNG 目視で重なりなし）。
その前のセッションで **defect-3（行頭 clef の幅を GClefWidth 固定で reserve）を全 clef で LP 実 ink へ字面移植して閉じた**。`GlyphMetrics.LineStartClefWidth(ClefType)` が **clef 別のステンシル右端**（LP `last_ext[RIGHT]`＝`g->extent(g,X)[RIGHT]`）を返す：**G=`ClefG.Right`2.565／F=`ClefF.Right`2.6834／C=`ClefC.Right`2.720**（percussion のみ実 metric 無しで G 近似）。`SpacingRules.MaxClefWidth(score)` が **系内最広 clef** を `CalculatePrefixWidth`＋`FirstNoteSpring`（同一幅で clef-only を cancel）＋`DrawClef` の flow-anchor に配線。**grandStaff は 1 本の clef break-align 列を共有**（`break-alignment-interface.cc:141-142,242` 実読で裏取り：グループ extent＝全譜 clef の union＝max、次列は max+gap）＝広い bass F が treble 譜の meter も支配し、両 meter が縦に揃ったまま +0.12 右へ（`timesig-grandstaff`: 両 4.88→5.00・両 note 8.35→8.47・整列維持を実測）。**変更 8 製品ファイル**（`GlyphMetrics.cs`・`SpacingRules.cs`・`LayoutEngine.cs`・`MultiStaffLayouter.cs`・`SystemBreaker.cs`・`IncrementalCompiler.cs`・`SharedRenderer.cs`・`SharedRenderer.Prefix.cs`）＋データ 1（`lp-geometry.json`）＋テスト注釈 2。
**台帳**：`line-start.clef-to-time.{treble,bass}` **両方 exact**（treble −0.001→0・bass −0.1194→0）。**GClefWidth（advance 2.564）を prefix ink に使う経路は消滅**——全 clef が自分のステンシル ink を読む＝advance-vs-ink 乖離ゼロ。**3226 passed / 0 failed / 3 skipped**、Core 0 warn / 0 err。**LP 忠実度 42/58 exact・total |residual| = 0.006267 ss**。**snapshot 再ベース 182 枚**（非 treble 64＝bass/alto/C/grandStaff/mixed-tab は +0.12・treble 116＝G の advance→ink で +0.001＝2 桁で多くは丸め消滅・programmatic 2）、**要素数・viewBox 全て不変の純 X 再配置**（Y-reflow/OOB なし・機械照合）。⚠️ churn 回避で treble を advance に残す前案は**ユーザー指示で撤回**——「byte churn は非 faithful を残す理由にならない」。

---

## 0. セッション開始時にやること（**必ず裏取り**）

```powershell
cd C:\MyProj\LilySharp
git log --oneline -8
git rev-list --count origin/master..master     # 未 push 数
git status --short
dotnet build LilySharp.Core\LilySharp.Core.csproj --no-incremental -v q
dotnet test  LilySharp.Tests\LilySharp.Tests.csproj -v q 2>&1 | Select-String 'Passed!|Failed!'
```

⚠️ **このドキュメントも memory もコード内コメントも、書いた時点のスナップショット。**
HEAD・テスト数・シンボル名・「完了」表記は開始時に実コードで再確認する。
過去の引継ぎでは stale な記述を毎セッション複数踏んでいる（§5.2）。

---

## 1. 現在地 ← **毎セッション書き換える**

### ▶ 次のセッションの最初の一手 ＝ **beam の page/system 種（TSU/TSD 相当を先に対で起票）**——`AugmentSkylinesForPaging` の固定 3.5 が残る最後の既知の未測定領域（§1 の beam 残メモ参照）。次点（byte-hack 監査 2026-07-24 の残り 2 件）: ① **widest-key の対象譜集合の不一致**＝予約 `WidestActiveKey` は tab/ossia **込み**の全譜・描画の max ループは tab/text/ossia を **skip**。移調 tab パート＋concert 譜のような「tab が最広 key を持つ」regime で予約＞描画になり得る（fixture 皆無・未測定）。LP の key 群は「調号を**彫る**譜」＝tab 除外が字面——**probe を対で起票してから**両者を 1 helper に統一。② break-align 描画 walk の純構造化（sharedKeyX/sharedTimeX の手組み max ループ→`SolvePrefixColumns` 消費へ。値は一致済＝出力不変・ただし予約側は score モデル＋measure 走査、描画側は `ResolveKeySignature`＋`GetSystemStartKeyChange` と **key 解決経路が別**——この解決経路の統一が本丸で、片方だけ挿げ替えると多分岐で壊れる。急がず focused session で）

**sub-question ①（X 軸）は完全決着**、**島2（単音/グレース臨時記号）も決着**（courtesy paren は本セッションで**字面移植して完了**＝島2 完了）。次はどれも「点を対で起票→測って初めて見える」未測定領域（§3A の穴）：

- ✅ **単音/グレースの臨時記号 DRAW を skyline 経路に一元化（`c71a8218`・済）**：和音は `position_apes`、単音/グレースは固定 `AccidentalNoteGap 0.35` だった第2モデルを、`CalculateSinglePosition`（reserve と同一）＋`DrawAccidentalAtInkLeft` に一元化。**♮ +0.0177／flat +0.12** を閉じ、`DrawAccidental` 削除。sharp は不変。
- **break-align 描画 walk（第3の複製）**：`SharedRenderer` の line-start 描画（`sharedTimeX`＋`prefixEndX` を `GetSpacing` 直呼び）は reserve/boundary の `SolveColumns` を通していない。draw=reserve は値一致済だが engine 未経由。描画ループが delicate なので別途。
- **beam の page/system スカイライン種は未着手**（`AugmentSkylinesForPaging` 固定 3.5）。TSU/TSD 相当の beam 台帳点が無いので**先に対で起票**。
- ✅ **courtesy paren スカイライン＝`parenthesize` を字面移植して完了（本セッション・下記ブロック）**。当初「単音は box と恒等・列 regime は書けない＝打ち切り」と判定したが、**ユーザー指示で覆った：「LP のロジックの完全模倣が目標。字面移植できる部分は出力が同じでもそうする」**。leftparen/rightparen の実 outline を bake・box `Merge` を paren 合成へ置換・flat 0.375 を runtime `!parenthesized` 分岐へ。出力 byte 不変・LP dump ピンのテスト 4 本。和音 courtesy を実装した暁には**追加作業ゼロで**列 nest も LP 一致になる。
- **clef の LILC-vs-skyline sliver（Y 4 点）** は SKPath 不可＝下記「保留になった一手」（LP instrument が先）。

**手順は 5 回連続で成立した型をそのまま**（点を対で起票→予測を why に先書き→種/移植→対の食い違いが第2欠陥を出す）。§1 末尾の 4 箇条を読むこと。

#### ✅ このセッション（2026-07-24 前半）＝ **courtesy paren を実測→`parenthesize` 字面移植（出力不変）＋break-align 描画 walk の乖離 2 regime を特定**

##### ★★ 教訓: **「観測可能な差が無い」は字面移植をしない理由にならない**（ユーザー指示・byte churn 教訓の裏面）
実測で「単音は box と恒等・列 regime は Lily# で書けない」を確認し**一旦打ち切りと判定したが、ユーザーが覆した**：Lily# の目標は出力の類似でなく **LP 内部ロジックの完全模倣**であり、**字面移植が可能な部分は出力が同じでも字面移植する**。「効くが LP でない」を残さないの適用対象は**観測不能な内部モデルにも及ぶ**（§5.2 の box パッキングが台帳の点を開くまで見えなかったのと同じ構図——今は差が出なくても、和音 courtesy 実装など将来の regime 拡張で box は再び欠陥になる）。

##### ✅ 実施（5 ファイル・出力 byte 不変・snapshot 0 枚・3238 緑）
| ファイル | 内容 |
|---|---|
| `Extract-EmmentalerSkylines.py` | **RAW outline のみを bake する形へ**：flat/flatflat の 0.375 焼き込みを撤去（runtime 分岐が LP の位置）。`accidentals.leftparen/rightparen` の生 outline skyline を追加 bake＋`AccidentalParenSkylinePair(bool)` accessor |
| `GlyphSkylinesGenerated.cs` | 再生成（flat 23→22・doubleFlat 44→43 RIGHT buildings＝fatten 除去のみ・paren 2 glyph 追加。値は LP dump と一致——例: leftparen y=1.046 で x=−0.2215 ↔ LP −0.221499…） |
| `AccidentalPlacement.cs` | `GlyphSkylinePair` を字面移植形へ：courtesy は **paren 実 outline を LILC 端へ padding 0 で合成**（`accidental.cc:33-43` parenthesize・`Raise`=X 平行移動）、非 courtesy の flat/doubleFlat だけ **0.375 fatten**（`accidental.cc:65-82` の `!parenthesized` guard）。`MergeParen` helper 新設・`GlyphSkylinePair` を internal 化（テスト用） |
| `accidental-skyline.ly` | **AccidentalCautionary の skyline/gap スコアを追記**（PFLAT/PSHARP/PNAT＋PFLAT1/PSHARP1/PNAT1・期待値コメント付き）＝合成の照合を再実行可能な形で commit |
| `AccidentalPlacementTests.cs` | **LP dump ピンの 4 テスト**：sharp belly 1.7 / y=1.2 で 0.864（自 outline が透ける）・flat 1.4＋y=1.5 で ~0.10134（**fatten 非適用**）・bare flat y=1.5 で 0.3（fatten 維持）・natural 1.2666＋y=1.3 で ~0.16581。**旧 box に一時戻して 3 本落ちる・BareFlat のみ通ることを実証** |

- **LP の括弧表現を確定**: cautionary は **`AccidentalCautionary` という別 grob**（`accidental-engraver.cc:294` `make_item ("AccidentalCautionary", …)`・`define-grobs.scm:57` に `parenthesized . #t`・`extra-spacing-width (-0.2 . 0.0)`）。stencil は `Accidental_interface::print` → `parenthesize`（`accidental.cc:33-43`）が `accidentals.leftparen/rightparen` を **padding 0 の `add_at_edge`** で左右に付加＝**括弧は Accidental の stencil 内**（paren 専用 grob は無い）。skyline はその stencil 全体の実 outline（`accidental.cc:58` `skylines_from_stencil`）。flat の 0.375 膨らませは **parenthesized のときスキップ**（`accidental.cc:65-67`）。**前セッションで `bes'?` 等の ext dump が出なかった理由＝`Accidental` を override していた**（cautionary は別 grob なので当たらない）。
- **実測**（scratch .ly＝`AccidentalCautionary.after-line-breaking` override で x/ext と horizontal-skylines を dump。`bes'?1`/`fis'?1`/`c'?1`/`<fis'? ais'>1`/`<bes'? des''>1`・detached `cmd /c … < NUL`）: 単音 courtesy の paren ink-right→符頭 left は **sharp/flat/natural とも 0.350000**（= right-padding 0.15 + padding 0.20・非 courtesy と同値）。Lily# 側も snapshot（`courtesy-accidentals` の natural）で ACC→HEAD anchor 1.62 ＝ LP 1.6166 の F2 丸めに一致。**box 壁 ≡ paren belly の最右点**（add_at_edge padding 0 ⇒ box 右端 = paren bbox 右端 = belly の x）なので、**符頭が belly に正対する 1 要素問題では box と実 outline は恒等**＝単音では原理的に差が出ない。
- **差が出る唯一の regime ＝ 列 packing**（隣の臨時記号が belly 以外の Y で nest する場合）。LP 実測: `<fis'? ais'>`（courtesy 下・通常上の三度）＝ ACC anchor 間 **1.885746**（通常同士 1.284000）／`<bes'? des''>` ＝ **1.596091**（通常同士 0.964561）。実 paren の Y 域は **±1.052**（sharp bbox ±1.5 より狭い）・曲線 profile。box paren なら sharp 側は ~2.4（**+0.51 級**の差）になる計算。**だが Lily# は和音内 courtesy を未対応**（`MeasureCollector.ItemFactory.cs:286,324` が chord note を常に `IsCourtesy: false` で作る）＝ この regime は .lys で書けない＝台帳点を起票できない → 一旦「打ち切り」と判定。**ユーザー指示で覆り、上記のとおり字面移植を実施**（列 nest は和音 courtesy 実装時に追加作業ゼロで LP 一致・その日が来たら上の LP 実測値 1.885746/1.596091 で点を対で起票）。
- **break-align 描画 walk（第3の複製）のスコープ調査**（次手の下ごしらえ・コード変更なし）: 単純に `SolveColumns` へ差し替えるだけでは**出力不変にならない**。予約と描画が**既に**食い違う（＝統一すると出力が動く）regime を source で 2 つ特定:
  1. **custom key（非伝統的調号）の行頭**: 予約側 `WidestActiveKeySharps`（`SpacingRules.cs:220-242`）と `KeySignatureChangeItem` 走査は **`.Sharps` しか見ない**が、custom key は `new KeySignature(0, custom)`（`MeasureCollector.Form.cs:385`）で Sharps=0 のまま＝**予約は key 列ゼロ・描画は `keyed = Custom≠null` で描く**（`SharedRenderer.cs:270,491`・幅は `KeySignatureInkWidth` の per-glyph 和）。行頭 custom key で予約と描画が別物の疑い（**source 読みのみ・未実測・fixture 有無も未確認——測ってから**）。
  2. **ossia の clef 無し key**: 描画は drawClef=false でも `GetSpacing(Clef,Key)` 0.82 を systemStartX に足す（`SharedRenderer.cs:492-494`）＝存在しない clef からの gap。LP なら LeftEdge→KeySignature の space-alist。engine（`SolveColumns` は幅 0 item を skip）へ通すと 0.80 になり **0.02 動く**。
  どちらも**先に LP で測って点を対で起票してから**統一する（型どおり）→ **同セッション後半で起票完了（次ブロック）**。

#### ✅ このセッション（2026-07-24 続き）＝ **break-align 描画 walk の 2 regime を対で起票（4 点・ハーネスのみ・出力不変）**

| 点 | LP | Lily# 残差 | 中身 |
|---|---|---|---|
| `line-start.time-to-first-note.standard-key`（KCS） | **3.700000**（TIME ink右 9.235 + **semi-shrink-space 2.0**＝`TimeSignature.space-alist (first-note . (semi-shrink-space . 2.0))`） | **−0.095264567** | **control が開けた瞬間から非ゼロ**＝keyed+metered 行頭の time→第1音は誰も測っていなかった regime（§5.2.1④）。分解: −0.1（Lily# の gap 1.9 vs LP 2.0。1.9 は full-measure と同値＝共有 spring 疑い・未単離）＋0.004735433（Pango 時値幅差＝time-change-to-notehead 閉鎖時の既知量） |
| `line-start.time-to-first-note.custom-key`（KCC） | **3.700000**（`\set Staff.keyAlterations` で同じ 2 sharps＝**KCS と 15 桁一致**。LP の key モデルは keyAlterations 1 本） | **−2.745264567** | **対の差 = −2.650000000 exact**（= Clef→Key 0.82 + key ink 2.2 + Key→Time 1.15 − Clef→Time 1.52）＝**欠落 key 列を 15 桁で単離**。実害: 描画 meter 7.54 が第1音 8.49 に**重なる**（custom key `key custom fis cis` で再現・fixture 皆無だった） |
| `line-start.ossia-key-alignment.{sharps,flats}`（OKN/OKNF） | **0**（metric-free・TSA と同型） | **−3.365000**（両方同値＝予測が桁一致・pair 一致が content 非依存の検算） | ⚠️ **予測「LeftEdge→key 0.8」は LP dump が棄却**——真相は **ossia の key も系全体の KeySignature break-align 列に入る**（`break-alignment-interface.cc:141-142`。OKEY x ≡ 主譜 KEY x = 4.185。NR 慣行でも `\magnifyStaff` でも同じ＝OKM スコアで裏取り）。Lily# は存在しない clef からの 0.82 を systemStartX に足して **0.82** に描く |

- **probe**: `barline-spacing.ly` に KCS/KCC/OKN/OKNF＋**OKM**（`\magnifyStaff` は space-alist もスケールする＝`music-functions-init.ly:1106-1116`。ossia の scaled key→time 1.15×magstep(−3) が共有 TIME 列を 7.198 に引く——モデル比較用・台帳点ではない）。ハーネス: `TimeSignatureToFirstNotehead`／`OssiaKeyAlignmentOffset`（ossia glyph は **fontSize で識別**・recorder が group transform を解決済みなので X は絶対座標）。
- **Lily# fixture**: custom key は `key custom fis cis`（構文は既存・**corpus に fixture ゼロだった**）。ossia は ossia-beams 形（measure 1 fragment・LP twin は全長小staff＝R1 が鳴る documented 非対称・量は行頭なので無関係）。
- ⚠️ **`\set Staff.keyAlterations` は単独で印字される**（`key-engraver.cc:148-151` lastKeyAlterations≠keyAlterations で create_key）。
- **教訓（対の設計）**: KCS/KCC は「同じ 2 sharps を二通りに書く」対＝LP 側が**恒等**になる対。Lily# 側の差だけが欠陥を運ぶので、**対の差が定数 2.65 を 15 桁で切り出した**。control 自身の −0.0953 は「開けるまで見えなかった」持参金。

#### ✅ このセッション（2026-07-24 続き×4・最新）＝ **byte-hack 監査（ユーザー問い掛け）＝細工ゼロを確認・`keyed` 述語の二重定義を 1 件検出して修正**

- **監査結果: フィッティングや byte 細工は無し**——今日の等価性は全て同一算術（標準 key の 本数×advance ≡ ink・max 式 ≡ engine TimeX・DrawClef 戻り値 ≡ sharedKeyX）で、byte 不変は強制でなく**検証結果**。
- **検出・修正**: `keyed` 述語が描画 `Sharps!=0 || Custom!=null` / 予約 `ink>0` の二重定義で、**`key custom`（音名なし）＝Custom="" という到達可能な縮退入力**で予約≠描画（描画だけ key 列 +1.97）。両所を `KeySignatureInkWidth > 0`（予約と同じ述語＝`SolveColumns` の幅 0 skip と同型）へ統一。corpus 出力不変（3245 緑・snapshot 0 枚）。
- **残り 2 件は ▶ の次点に起票**（widest-key の譜集合不一致＝probe から／walk の純構造化＝key 解決経路の統一が本丸）。

#### ✅ このセッション（2026-07-24 続き×3）＝ **4/4・2/2 の「幅 2 モデル」を閉じた（KCS/KCC/KC2 全て 0 exact・snapshot 184+2 再ベース）**

- **正体**: LP の既定 style は `make-c-time-signature-markup`（`time-signature-settings.scm:954-964,981-982`）＝**ちょうど 2/2 と 4/4 だけ `timesig.C22/C44` グリフ（LILC ink 経路）、他の分数は `\number` markup（Pango 経路）**。Lily# は C を**描いて**いたのに予約は digit Pango 幅 1.604735433。C44/C22 の LILC ink は **1.700000**（bake して確認＝KCS 実測 TIME ext と一致）。
- **fix**: `Extract-EmmentalerMetrics.py` に `TimeSigCommon/TimeSigCutCommon` bbox を追加→再生成。`GetTimeSigWidth` に **LP の style 分岐を字面移植**（(4,4)/(2,2)→C ink・他→Pango digit 不変＝`barline.next.time-change-to-notehead` は exact のまま）。`DrawTimeSignature` の発明 `return x+2.0` も同じ幅へ。**対 KC2**（2/2）を起票＝LP 実測 **3.700000＝KCS と一致**（C22 ink も 1.7＝対の検算）→ Lily# も 3 点全部 **0 exact**。
- **snapshot 184＋programmatic 2 再ベース**（4/4/2/2 第1システムの音符・小節線が +0.095264567 右へ）。**機械照合**: 全枚で行数不変・y/y1/y2/cy/height 属性完全不変・viewBox 高さ不変＝**純 X・改行移動ゼロ**。PNG 目視（keysig-treble）健全。`TimeSignaturePangoWidthTests` の「4/4=digit」ピンは前提が LP と食い違っていたので LP モデルに書き換え（digit 経路のピンは維持）。
- ★ **教訓: 残差の why が 2 回外れて、3 回目は「描かれている物を見る」で当たった**——(1)「spring 1.9 vs 2.0」は `FirstNoteSpring` 実読で棄却、(2)「digit の ext vs advance」は **PNG に C グリフが写っていた**ことで棄却。数字だけ睨むより **何が描かれているかを見る**（§5.3 の親戚）。「LP の時値幅は markup 経路」という記憶/REF は **digit の話**で、C グリフには適用されない——**同じ grob に経路が 2 本**あるのが LP の字面。

#### ✅ このセッション（2026-07-24 続き×2）＝ **起票した 2 regime をそのまま修正（KCC −2.745→−0.0953／OKN・OKNF −3.365→0 exact・既存 corpus byte 不変）**

| 変更 | 内容 |
|---|---|
| `SpacingRules.KeySignatureInkWidth(KeySignature)` **新設＝key 幅の単一モデル** | custom は per-glyph advance 和・標準は本数×advance。**予約と描画が同じ関数を読む**（旧 `SharedRenderer.Prefix` の同名は削除・描画側は委譲）。LP は key モデルが keyAlterations 1 本＝予約が描画そのもの、の構造写し |
| `SolvePrefixColumns`/`CalculatePrefixWidth`/`FirstNoteSpring` | 引数を **(本数, sharps?) → key ink 幅 (double)** へ。標準 key は同一算術＝byte 不変 |
| `WidestActiveKey`（旧 `WidestActiveKeySharps` 置換） | **KeySignature の全 record を返す**・widest は ink 幅比較（group extent は ink の union＝`break-alignment-interface.cc:141-142`）。**mid-piece の key change も `kc.NewKey` を丸ごと**（旧は `.Sharps` に落として Custom を捨てていた） |
| `MultiStaffScore.LeadingKey`（旧 `LeadingKeySharps` 置換） | all-tab は CMajor。呼び出し 4 系統（LayoutEngine/SystemBreaker/IncrementalCompiler/MultiStaffLayouter）を KeySignature 駆動へ |
| `SharedRenderer` **sharedKeyX** | key 列を **系共有の 1 式**（widest-clef 右＋Clef→Key）に。clef を描く譜では旧 walk と**恒等**（DrawClef の戻り＝共有 clef 右）・**ossia（clef 無し）だけが動く**＝OKN/OKNF が 0 に |
| `DrawKeySignature` に **scale 引数** | ⚠️ **ユーザーの座標系ルールが捕まえた単位混在**：ossia 調号の**グリフ advance が非スケール（実 1.1ss）のまま scaled frame に置かれていた**。列 X＝非スケール共有・stencil 内部＝譜のスケール frame、の区別を明示（LP は NR 慣行でも stencil をスケール＝OKN dump OKEY ink 1.5558≈2.2×magstep(−3)。**残る sub-0.1% は LP の design-size フォント metric＝clef sliver 族・未起票**） |
| fixture 2 枚新設＋snapshot | `test/custom-key`（重なり解消の固定）・`test/ossia-key`（共有列＋scaled advance の固定）。**PNG 目視済**・SvgSnapshotTests 登録 |

**LP 忠実度 49/67 exact・total 9.577→0.196796 ss**。既存 snapshot 0 枚変化（標準 key は同一算術・ossia+key の既存 fixture は無かった）。

##### ★★ 教訓: **PNG 目視が 2 つの別欠陥を追加で出した**（§5.3・7 回目）
①**Lily# は 4/4 を C グリフで描いている**のに予約幅は digit 経路の Pango 幅——control −0.0953 の正体仮説が「spring 1.9 vs 2.0」（誤・`FirstNoteSpring` は (2.0,1.0) で LP 一致）から「**C の LILC ink 1.7 vs digit Pango 1.6047 の幅 2 モデル**」へ鋭利化（▶ 参照）。②視覚 fixture が**既定の相対オクターブで `cis'`/`e'` が跳ね上がる**罠（`'` は相対では octave 跳躍）——`octave absolute` を明示して解消。probe 側は Preamble が絶対指定済みで無事。ついで **twin の音列不一致**（Lily# `c'`=C♮ は D major で cancellation ♮ を印字・LP twin は `cis''`）も目視が出し、fixture/probe の音列を `cis'`・`ees` に統一（測定量は第1音なので台帳値は不変）。

#### ✅ 前セッション（2026-07-24）＝ **break-align 列エンジンを純関数で移植し、移調パートの meter 整列を LP 実測で閉じた（`824798c4` ほか）**

ユーザーの問い「break-align エンジンを移植するとアーキ上の不利（1-pass 不可・red-green 破綻・perf）が出るか」に**実コードで判定＝3 つとも出ない**：① `calc_positioning_done`（`break-alignment-interface.cc:152-283`）は**反復なし前向き計算**（extent＋space-alist を1回積む・ノード非依存）で、Lily# の系全体 spring solve（`MultiStaffLayouter.LayoutMeasures`）の**前にスカラで出る今の場所にそのまま入る**＝1-pass 維持。② memo は**系単位**（`SystemLayoutCache`）でキーが**全譜 clef＋`PerStaffKeySignature` を既に畳む**（`MeasureContentKey:161-162,276`）＝cross-staff 依存は memo 単位内で完結・幾何はキーに載せない。③ O(譜×前置≈4)＝無視。**移植したのはアルゴリズム（純関数）で LP の grob/callback 機構ではない**（Lily# に grob 無し・持込は逆に有害）。

| ファイル | 内容 |
|---|---|
| `BreakAlignSpacing.cs` | **`SolvePrefixColumns` 新設**＝`calc_positioning_done` の前向き walk を純関数移植。各列 X＝前列 group-right＋space-alist（`CalculateDistance`）。返り値＝`PrefixColumns(ClefX,KeyX,TimeX,Right,…)`。`CalculatePrefixWidth` は `.Right` に委譲＝**完全等価**（既存 19 テスト緑で証明） |
| `SpacingRules.cs` | **`WidestActiveKeySharps(score,startMeasureIndex)` 抽出**（layout の widest-key 走査を helper 化・layout も呼ぶ＝単一ソース） |
| `SharedRenderer.cs` | **共有 time 列 `sharedTimeX`**＝**全譜の per-staff meter X の max**。同 key の譜は max==各で byte 不変、key が譜ごとに違う時だけ最広に整列。meter 描画を per-staff flow から `sharedTimeX` へ |
| `SharedRenderer.Prefix.cs` | `+0.4` を `KeySigTrailingGap` 定数＋`KeySignatureDrawnWidth(key)` に抽出（draw と共有幅計算で同式） |
| snapshot 3 枚 | `transpose-multistaff`・`key-per-staff`・`part-header-key-per-part`＝**全て譜ごとに key が違う**。他 194 枚 byte 不変 |

##### ★★ 教訓: **LP を実際にレンダしたら「Verified against LilyPond」コメントが誤りだった**
`transpose-multistaff`（上 D-dur/下 C-dur）の現状は上 meter x8.05・下 meter x5.0＝**ズレ**（fixture コメントは「LP 照合済」と主張）。LP を実レンダ（`lilypond.exe --svg`）して測ると**両 meter が x16.19 で整列**（下は clef 密着でなく最広 key の後ろ）＝Lily# が誤り。修正後 Lily# も両 x8.05 で整列＝LP と一致。**主張でなく実測（§5.3）**——`--svg` 出力の glyph translate を直読みで足りる粗さの照合。

##### ✅ 台帳点も起票した（metric-free）＝ `line-start.time-signature-cross-staff-alignment`（TSA）
移調 grand staff（上 D-dur/下 C-dur）の meter X の**譜間 spread**（max−min）。LP を実レンダ（barline-spacing.ly の TSA・両 TIME grob **x=7.6534 で EQUAL**・CLEF 0.8・**共有 KEY 列 4.3034**＝空の下 key も同列・HEAD 11.3534）＝**spread 0**。`RenderedGeometry.TimeSignatureAlignmentSpread` が Lily# の per-staff meter X の max−min を返す＝**同一レンダ内の 2 anchor 差＝ink 幅非依存の metric-free** probe（LP=0 は整列そのもの・距離でない）。修正前 ~3.05 → 後 **0 exact**。**LP 忠実度 43/59 exact・0.006267 ss**。

##### ✅ `+0.4`（KeySigTrailingGap）の draw-vs-reserve 分裂も閉じた＝ `line-start.clef-to-time.keyed`（DCTK）
`DrawKeySignature` が key-ink + **0.4** を返して meter がそこから流れる一方、`BreakAlignSpacing` は key-ink だけで note 列を予約＝**§5.2.1② の別式**。LP を実レンダ（DCTK＝単一譜 D-dur・CLEF 0.8・KEY 4.185(ext 2.2)・TIME 7.535＝clef→time **6.735**）で key→time は **ink 右端 +1.15・pad 無し**と確定。`sharedTimeX` を `KeySignatureInkWidth`（ink のみ＝予約と同一）へ、`DrawKeySignature` の死んでいた +0.4 return も削除＝**draw=reserve 一元化**。**+0.4 → 0 exact**（Lily# の key ink 幅は LP の 2.2 と既に一致＝0.4 が分裂の全て）。**snapshot 24 枚**（key+time を持つ譜）＝**meter が 0.4 左へ寄るのみ**（keysig-treble は time 7.94→7.54 の 1 行だけ・first note 不動・要素数不変）。TSA の絶対も LP 一致に。**LP 忠実度 44/60 exact・0.006267 ss**。

##### ⚠️ 正直な残り（**このセッションで一部解消・未コミット**）
- ~~移植は列エンジンの line-start 部分のみ~~ ← **不正確だった**。mid-line 境界（clef/key/time 変更＋bar）は `BoundaryColumn.cs` が既に `calc_positioning_done` を忠実移植済み・`barline.next.*` 台帳で測定済み。実態は「LP は 1 関数を Lily# が 2 コピー（line-start=`SolvePrefixColumns`／mid-line=`BoundaryColumn`）」だった。**このセッションで 1 本の `BreakAlignSpacing.SolveColumns`（LP `calc_positioning_done` の単一前向き walk）に統一**し両者が呼ぶ形へ＝LP の「エンジン 1・順序ベクタ」構造に一致。**出力不変**（3232 緑・snapshot 0 枚・2 ファイルのみ）。⚠️ 落とし穴: 距離は LP と同じく左 grob の**厳密な ink 幅**（`extents[l][RIGHT]`）で測る——`prevRight-prevLeft` の再構成は丸め境界で line-start note を 0.01 動かす（keysig-change で検出）。
- **残る第3の複製**: `SharedRenderer` の line-start **描画** walk（`sharedTimeX` 算出＋`prefixEndX` 累算・`GetSpacing` 直呼び）は draw 側で、reserve 側（`SolveColumns`）と `GetSpacing` 値を共有して一致済みだが、engine を通していない。描画ループが delicate（ossia/tab/source-scope/cross-staff max）なので別途。
- **custos** は順序ベクタの end-of-line 側に載る**未実装フィーチャ**（グリフも配置も無し・台帳点ゼロ）。engine 統一とは独立。

##### ✅ このセッション（2026-07-24 最新）＝ **percussion clef を閉じて defect-3 を全 clef 族で完了（+0.565→0 exact・`8ef0044e`・9 ファイル＋本ファイル）**
LP 実レンダ（`\clef percussion \time 4/4` を自分で `--svg` 実行）で **CLEF 原点 x=0.13・ext (0.67 . 2.0)・TIME x=3.65・HEAD x=7.35** を確認（handoff の測定値と桁一致）＝pitched clef（ext-left=0・原点=ink-left=0.8）と違い、**percussion は glyph 原点が ink-left より 0.67 左**。単純な幅置換では済まず **2 量**を通した：

| ファイル | 内容 |
|---|---|
| `Extract-EmmentalerMetrics.py`／`GlyphMetricsGenerated.cs` | **`ClefPercussion` BBox を font の LILC 表から抽出**（`clefs.percussion`＝`(0.67,-1.0,2.0,1.0)`・treble control の grob-extent が LILC bbox と一致することで LP 値=font 値を裏取り）。差分は ClefPercussion 追加のみ＝font drift なし |
| `GlyphMetrics.cs` | **clef metric を単一 `ClefBBox(clef)` map に統一**＝`LineStartClefWidth`＝`Right-Left`（ink 幅）・`ClefInkLeft`＝`Left`（原点→ink-left）を同じ BBox から導出。percussion 特別扱いも「ink-left=原点」隠れ仮定も廃止＝**LP と同じく全 clef を stencil extent で一様に扱う**（pitched は Left=0 なので出力不変＝3232 緑・snapshot 差分不変で実証）。単位/方向の差ではない——同じ ss・同じ +X・同一 font の per-glyph ink offset |
| `SharedRenderer.Prefix.cs` | `DrawClef` の glyph 描画を **`x+ClefGlyphXOffset − ClefInkLeft(clef)`** へ＝percussion 原点を 0.13 に落とし ink-left を共有 0.8 列に載せる（pitched は no-op） |
| `barline-spacing.ly`／`LpGeometryProbes.cs`／`RenderedGeometry.cs`／`lp-geometry.json` | **DCP probe（`clef percussion \time 4/4`）** 起票＝`line-start.clef-to-time.percussion` 3.52。`IsClef` に percussion 追加。`ClefToTimeSignatureOnFirstSystem` を流用（`StaffRefpoints` 非依存なので drum の可変線数でも OK） |
| snapshot `drum-groove` | 再ベース 1 枚＝時値列 −1.235・clef glyph −0.67 の純 X 再配置（Y/要素数/viewBox 不変・**PNG 目視で clef 重なりなし**） |

★ **教訓（型が 6 回目に成立）**: 起票時の予測「+0.565」が**測定で桁まで的中**（Lily# 4.085＝treble 値・G 幅 fallback＋origin 未補正の合成）。fix 後 **3.520000 exact**。⚠️ **BBox は font の LILC 表から抽出**（`Extract-EmmentalerMetrics.py` に `clefs.percussion` を足して再生成＝CI が font drift を検出できる faithful な経路）——LP 実レンダの grob-extent と 15 桁一致で二重裏取り。**mid-line clef change（`DrawClefChange`・percussion_change glyph）は同型の origin ズレを持つ可能性があるが台帳点が無いので未着手**（drum-groove は行頭 clef のみで未露出）。

#### ✅ このセッション（2026-07-24）＝ **defect-3 を全 clef で実 ink へ字面移植して閉じた（treble/bass 両 → 0 exact・`362ffda0`）**

| ファイル | 内容 |
|---|---|
| `GlyphMetrics.cs` | **`LineStartClefWidth(ClefType)` 新設**＝**全 clef が自分のステンシル右端**（LP `last_ext[RIGHT]`）。**G=`ClefG.Right`2.565／F=`ClefF.Right`2.6834／C=`ClefC.Right`2.720**（percussion のみ metric 無し＝G 近似・要 probe・tab は上流除外） |
| `SpacingRules.cs` | **`MaxClefWidth(score)` 新設**＝非 tab/非 text/非 ossia 譜の最広 clef（fallback も `LineStartClefWidth(Treble)`）。`FirstNoteSpring` に `clefWidth` 引数（`CalculatePrefixWidth` と**同一幅**で clef-only を cancel）。int-only `CalculatePrefixWidth`（treble 既定）も `LineStartClefWidth(Treble)` 経由＝**GClefWidth を prefix ink に使う経路は消滅** |
| `LayoutEngine.cs`／`MultiStaffLayouter.cs`／`SystemBreaker.cs`／`IncrementalCompiler.cs` | prefix 幅 4 経路を `MaxClefWidth(score)` 配線。`MultiStaffLayouter` は `FirstNoteSpring` にも同 `maxClefWidth` |
| `SharedRenderer.cs`／`SharedRenderer.Prefix.cs` | `DrawClef` に `clefColumnWidth` を渡し、戻り値（key/time の flow-anchor）を**系内共有列**へ。glyph は自分の ink で描き、次項目だけ共有幅から。**grandStaff の両 meter が整列維持**（`timesig-grandstaff`: 両 4.88→5.00・両 note 8.35→8.47） |
| `lp-geometry.json` | `line-start.clef-to-time.{treble,bass}` **両方 → 0 exact**（treble −0.001→0・bass −0.1194→0・why を CLOSED へ） |
| snapshot 182 枚 | 非 treble 64（+0.12）＋treble 116（G advance→ink で +0.001・多くは 2 桁で丸め消滅）＋programmatic 2。純 X 再配置（要素数/viewBox 不変・OOB/Y-reflow なし・機械照合） |

##### ★ 教訓: **grandStaff の縦整列は「幅の広い方が列を支配」＝1 列共有（LP 実ソースで裏取り）**
per-staff で clef 別幅を描くと bass 譜だけ meter が右へ動き **treble と縦がずれる**（visible regression）。LP は Clef break-align 列を**系全体で共有**する——`break-alignment-interface.cc:141-142` の `g->extent(g,X)` は**グループ（＝全譜の clef）の union**、`:242` の次列 offset が `extents[clefIdx][RIGHT]+gap`＝max 起点。`MaxClefWidth`（系内最広）を予約・描画の両方に通すと両 meter が 5.00 で揃ったまま右へ。**片側だけ動かす naive fix は `timesig-grandstaff` が捕まえる**。⚠️ ただし `MaxClefWidth` は loop の**字面転写でなく結果の再実装**（Lily# に break-align エンジンは無い）。grand-staff cross-staff の**台帳点は未起票**（DCT/DCB は単一譜）＝ソース確認止まり。

##### ★ 教訓: **byte churn は非 faithful を残す理由にならない**（前案の撤回）
当初 G だけ advance(2.564) のまま残し「treble snapshot を churn させない」を理由にした——**ユーザーが却下**：LP は全 clef で stencil ink（`last_ext[RIGHT]`）を読むので G も `ClefG.Right`2.565 が字面。churn 回避のための非 faithful は **§5.2 の焼き込みと同型の病**。全 clef を ink に統一＝DCT も 0 exact・treble 116 枚が +0.001 再配置（sub-visual・2 桁で大半消滅・OOB/Y なし）。**「効くが LP でない」を snapshot 都合で温存しない**。

#### ✅ 前セッション（2026-07-24）＝ **行頭 prefix を LP へ字面移植し sub-question ① を決着（+2.264 → 0 exact・2 段 port・`9d5c2bd6`）**

| ファイル | 内容 |
|---|---|
| `BreakAlignSpacing.cs` | `FirstNoteSpring` に clef 幅を配線し clef case を **`max(0, 5.0−clefWidth)`**（`staff-spacing.cc:183-187` minimum-fixed-space の字面＝`last_ext[LEFT] + max(length, 5.0)`）。`CalculatePrefixWidth` が LeftEdge→Clef（`ClefGlyphXOffset`）を prefix 先頭に確保 |
| `EngravingDefaults.cs` | `ClefGlyphXOffset` **0.3（LILYSHARP-OWN）→ 0.8**（`define-grobs.scm:2091` `LeftEdge.space-alist (clef . extra-space 0.8)`・LeftEdge extent 0）。**draw / skyline / prefix の 3 読者が同一定数を共有** |
| `SpacingRules.cs` | `FirstNoteSpring` が `GClefWidth` を配線（prefix と cancel）。⚠️ この GClefWidth 固定が defect-3 |
| `SharedRenderer.Prefix.cs` | `DrawClef` の戻り値を発明の `0.3+3.0` → **実 clef ink 右端 `ClefGlyphXOffset + GClefWidth`** |
| `SharedRenderer.cs` | 行頭 prefix 描画に **clef→key(0.82)/clef→time(1.52)/key→time(1.15)** の break-align gap を `BreakAlignSpacing.GetSpacing` から挿入（空 key は gap なし）。meter/key が LP 位置へ（4/4 の C: 3.30→4.88=LP・note 不動） |
| `lp-geometry.json` | `line-start.clef-to-first-note.{treble,bass}` 2 点起票 → +2.264 → −0.30 → **0**。slur/tie 4 点を新残差（+0.0005）へ更新 |
| `barline-spacing.ly` / `LpGeometryProbes.cs` / `RenderedGeometry.cs` | LSCT/LSCB probe（**LP＝omit-time 単一 system／Lily#＝interior system** の documented 非対称・等価を実測確認）＋ `ClefToFirstNoteOnSystem` |
| snapshot 196 | clef 0.30→0.80・note +0.8。要素数/viewBox 幅/OOB 全不変、5 枚のみ健全 Y-reflow |

##### ★★ 教訓: **ペアが「予測外れ」で真因を出した（機構は当初診断が正しかった）**
起票の予測は「clef 幅の二重計上 ⇒ treble/bass で residual が**異なる**」。実測は**両方 +2.264 で一致**＝予測外れ。だが真因（spring が clef 右端から 5.0 を足す＝二重計上）は当初診断どおりで、**測定の挙動**を読み違えていた：prefix が固定 GClefWidth を使い（defect-3）、かつ clef 描画位置も幅連動するので clef-anchor→note が幅非依存になっていた。**計装（一時 `[PREFIX]` dump）で `note = prefixWidth + 5.0` を実測して確定**（疑った `Math.Max(5.0, s0.min)` は s0.min=0.2 で不発）。§5.3「推測でなく計装で実測」。

##### ★ 教訓: **発明定数は LP 実ソースの値へ（焼き込みでなく）**
`ClefGlyphXOffset 0.3` は「LILYSHARP-OWN・LP に無い」とコメントされていたが、実は LP の `LeftEdge.space-alist (clef . extra-space 0.8)` そのものだった。0.5 や 0.8 を焼くのでなく **LP の grob 既定値を引いて 0.8 に**。§5.2「未実装でなく、書いてあるが LP でない」の X 版。draw/skyline/prefix が同じ定数を読むよう一元化（§5.2.1⑤「読み手が居なかった」）。

##### ★ 教訓: **2 段 port は 1 段目が「行き過ぎ」て 2 段目の strand を出す**
spring-frame だけ入れると ledger は +2.264 → **−0.30**（0 を跨いで行き過ぎ）。その −0.30 が left-edge→clef の欠落（defect-1）で、ClefGlyphXOffset を LP の 0.8 にして閉じた。slur/tie も 1 段目で符号反転（−0.0077→+0.0042）、2 段目で +0.0005 に収束。**行き過ぎは「別 strand が残っている」の徴候**。

##### ★ 教訓: **spacing を直すと描画の別式バグが露呈する（§5.2.1②）**
ClefGlyphXOffset を 0.8 にした瞬間、`DrawClef` の戻り値が発明の `0.3+3.0`（リテラル 0.3＝旧 ClefGlyphXOffset）だったため meter/key が clef と一緒に動かず、以前 0.435 あった clef→meter 隙間が **−0.065（微小重なり）** に悪化——ユーザーが「C は以前から左寄り」と指摘。真因は**prefix 描画が break-align gap を一切足さず item を密着**（spacing は正しく積む）＝予約と描画が別式。描画を `BreakAlignSpacing.GetSpacing` の gap で組み直し **draw=spacing** に一元化。⚠️ **note は spring 由来なので不動**——動くのは meter/key glyph だけ（snapshot は全て純水平・要素数/viewBox/OOB/高さ全不変）。**「発明値 `0.3+3.0` があった」は §5.2 の「未実装でなく、書いてあるが LP でない」の描画版。**

#### ✅ このセッション（2026-07-24）＝ **タイの横アタッチを LP の Y 依存モデルで字面移植（−0.045 → −0.0019・`4c8cb54a`・9 ファイル）**

| ファイル | 内容 |
|---|---|
| `TieFormattingProblem.cs` | **`GetAttachment` 新設**＝端点 Y で edge/center を選ぶ（`set_column_chord_outline`+`get_attachment`+`widen(-x_gap)` の字面）。width/height を**候補ごと**に再計算（LP の provisional→tune→final 順）。`Solve` の固定 width 廃止 |
| `ElementCoordinator.cs` | タイに **inner-edge と head-centre の両アンカーを渡す**（dots は centre で無視・tab は edge 固定の no-op） |
| `lp-geometry.json` | `system.tie-{under,over}-notes` −0.045314478 → **−0.001882534**（why 追記）。TID/TIU は不変 |
| snapshot 5 件 | ties-slurs / multivoice-chord-tie / tab-chord-tie / multi-line-spanners / showcase__02-ornaments。譜面タイが中心アタッチへ拡幅（**X 不変・tab tie 不変**・showcase は譜間タイで Y +0.1 reflow） |
| `TieFormattingProblemTests.cs` | ctor に centre 2 引数（合成 fixture は center==edge） |

##### ★★ 教訓: **測定は「横アタッチ幅」と言ったが、真相は「Y 依存アタッチ」だった**
起票時測定（TSID span 7.319 vs 5.206）は「タイが頭から ~1ss/side 内側」と読め、**素朴な「常に中心」**を試すと TSID は −0.0019 に改善したが **TID が +0.0014→+0.0736 に悪化**（対の食い違い＝§1 の型）。LP 実測（4 音価+pitch sweep）で、アタッチは**端点が符頭箱をクリアするか**で edge↔center 切替＝**|delta|=0.5 の階段**と判明。§5.3「逆算でなく実測」。

##### ★ 教訓: **`-dir/2` は整数除算＝中点**
`set_column_chord_outline` の updowndir 箱は `x[-dir]=linear_combination(-dir/2)`。`dir` は ±1 Direction ＝**整数除算で 0**＝`linear_combination(0)`＝区間の**中点**（¾点でない）。ソース読みで ¾点と誤読し測定と食い違ったのはこれ。字面移植は**整数型を確認**する。

#### ✅ このセッション（2026-07-24）＝ **ページ跨ぎタイ TSID/TSIU を起票→種→cross-system 衝突修正で −0.9176 → −0.045（`2ca9cf69`・6 ファイル）**

| ファイル | 内容 |
|---|---|
| `page-vertical.ly` | **TSID/TSIU book**（SSD/SSU 4 補正を踏襲・`e,`=E2 / `f''''`=F7 の**鏡像対**・notes-above-floor 設計）。LP gap **13.512560327518213**（対は 15 桁一致） |
| `LpGeometryProbes.cs` | **TSID/TSIU twin**（16 小節 4 system・`StaffGapAt(1)`）＋登録 |
| `lp-geometry.json` | **`system.tie-{under,over}-notes` 2 点**。起票 −0.917560328（予測が桁まで的中）→ 種 **−0.045314478**（対一致） |
| `LayoutEngine.cs` | **タイ種を `AugmentSkylinesForPaging` に配線**（スラー種ブロックの真下・`AddTiesToSkyline`／`SeedBowInk` 共有・`ties` 引数＋`prelimTies`） |
| `ElementCoordinator.cs` | **cross-system 衝突修正**（下記 ★）＝`LayoutTies` が `existingTies` を **tie-column**（同一 voice・同一 start chord＝measure+item）で絞る＝LP `tie-column.cc:81-93` の字面 |
| `showcase__02-ornaments.svg` | **再ベース +0.69**（唯一の譜間タイ fixture＝`d,2~ d4` が種で新規予約＝予約漏れ修正。全 Y 剛体シフト・X 不変・path 形状不変・ページ拡大でviewBox外なし） |

##### ★★ 教訓: **「タイは衝突回避を持たない」の予測が外れ、その外れが真因を指した**（§1 の「予測が外れたときこそ2つ目」の実例）
種を入れた瞬間**対が食い違った**（TSID −0.9176 のまま／TSIU +0.2047）。`LayoutEngine` の種ブロックに一時 TIEDBG を挿して実データを見ると、interior system の下タイの制御点が**音符中心で鏡映**＝勝者候補の向きが up に化けていた（`TieItem.CurveUp` は down のまま・`SeedBowInk` は geometry で向き判定するので **down タイが up skyline に誤送→予約ゼロ**）。真因は **`TieFormattingProblem.ScoreTieTieCollision`**：`LayoutTies` が `existingTies` に全 system のタイを累積して渡し、改行後は各 system の小節が local X を共有するので interior 下タイが**上の同位置タイと衝突判定**され monotonicity で向きが反転。**スラーの cross-system 衝突と完全に同型**。修正＝`LayoutTies` が `existingTies` を **tie-column**（同一 voice・同一 start chord＝measure+item・自分の broken segment は除外）で絞る＝LP `tie-column.cc:81-93`（`calc_positioning_done`→`problem.from_ties(ties)` を **Tie_column ごとに1つ**）の字面移植。以後 TSID==TSIU が **13.467245850** で桁一致（tie-column 版でも corpus 出力は完全同一・3222 緑・snapshot 不動）。

⚠️ **字面移植の訂正**（§5.2 の「効くが LP でない」）：最初 `TieSpansOverlap`（スラーの `SlurSpansOverlap` を写した time-overlap）で対処したが、それは**スラーの機構**（`auxiliary_acknowledge_extra_object`＝time-overlap）であって**タイの機構ではない**。タイは `Tie_column` で列ごとに problem を作る（time-overlap でなく列メンバーシップ）＝span-overlap は単一声部では一致するが**別声部の同時タイで乖離する proxy**。ユーザーの「字面移植できたか」で気付き、`tie-column.cc` を実読して**列アンカー（voice+start chord）に直した**。スラーで一度踏んだ罠をタイでまた踏み、同じ問いで捕まった。

##### ★ 教訓: **対が食い違うまで欠陥は見えない**（3 度目の X 版）
片側（TSIU）だけ動いていたら「効いた」と誤読して出荷していた。TSID が動かなかったことだけが衝突バグを出した。§5.2.1④「穴を開けるまで分からない」。

##### ★ 教訓: **OPEN 残差の原因は逆算でなく実測**（§5.3）
−0.045 を「slur と同じ 0.14ss 漏れ」で片付けかけたが、`BezierBow.Height` の微分で**タイ arc 感度は 0.019**（slur 0.076）と分かり 0.14ss では 0.003 にしかならない＝**説明にならない**。after-line-breaking で LP の Tie.control-points を dump して初めて **span 2.1ss 差**が正体と判明。予測（arc 漏れ）が外れたのでもう一段掘った。

⚠️ **島2 の残（focused session で拾う候補）**:
- ✅ **単音/グレースの臨時記号 DRAW の第2配置モデルは閉じた（`c71a8218`・済）**。単音（`Noteheads.cs`）・グレース（`GraceNotes.cs`）を固定 `AccidentalNoteGap 0.35` の `DrawAccidental` から、reserve と同一の `CalculateSinglePosition`（scale 引数追加・grace/cue 対応）＋`DrawAccidentalAtInkLeft` に一元化し、`DrawAccidental` を**削除**。台帳 `accidental.single-natural-to-notehead` +0.0177→0。⚠️ **「sharp/flat は一致」は誤りだった**——reserve は一致だが **draw は flat の bbox.Left −0.12 overhang を誤計上して 0.12 外していた**（`accidental.single-flat-to-notehead` +0.12→0）。sharp（Left 0）は不変。**「バグでなく掃除」の想定に反し、掃除が第2欠陥（flat 0.12）を露出**＝§5.2.1④ がまた成立。
- ✅ **courtesy paren は `parenthesize` 字面移植で完了（本セッション・§1 の最新ブロック参照）**——実 leftparen/rightparen outline を bake して合成・flat 0.375 は runtime `!parenthesized` 分岐へ・出力 byte 不変。
- ★ **LP 語彙へのリネームをユーザーに依頼**（構造は既に LP に揃った＝`position_apes` の字面移植）。`AccidentalPlacement` の packer 内部を per-ape 命名へ：`Ape`/`PositionApes`/`SetApeSkylines`/`BuildHeadsSkyline` 等。**今回はリネームでなく構造ごと入れ替えた**ので、残るのは命名だけ。§5.1 の「MSVS でユーザーが F2」手順で。
- **clef の LILC-vs-skyline sliver（Y 4 点）** は依然 SKPath だけでは閉じない（下記「保留になった一手」）。⚠️ **島2 と混同しない**——accidental は実効 scale が **1.0**（生 outline/250 が LP と 15 桁一致）だが、clef は `get_unscaled_indexed_char_dimensions × magnification` の 0.27% がある。別問題。

★ **今回の教訓: 単位のミスマッチが最後の欠陥を出す**。position_apes を入れて 4 点 exact になった後、**snapshot ではなく PNG 目視でユーザーが courtesy×barline の重なりを見つけた**。原因は `SpacingRules` の単音経路が「glyph 幅 + gap」で予約していたこと（描画は `position_apes` の 0.35 gap ＋ courtesy paren）。**予約と描画が別式**＝§5.2.1② の X 版。直したら **♮ の `barline.next.key-change-to-notehead` −0.017672 も一緒に閉じた**（単音の左予約が取りこぼしていた同じ項）。§5.2.1④「穴を開けるまで何が溜まっているか分からない」がまた成立。

⚠️ **beam の残（前セッション・次点の候補）**:
- **page/system スカイラインの beam 種は未着手**: 譜間（`BuildStaffSkylines`）だけが drawn beam を種にする。`BuildSkylines`（system/page）は固定 3.5 のまま（page 用 beam 台帳点＝TSU/TSD 相当が無いので触らない）。
- **cross-staff / kneed beam** は forced-shorten の対象外（`ComputeBeamShorten` は knee で 0＝LP `calc_stem_shorten` line 1068 と一致）＝固定 3.5 のまま。

#### ★★ 教訓: **base_lengths_ 仮説は測定で否定された——正体は beam 'shorten**
前 handoff は「score_stem_lengths の base_lengths_[i]/stem_ypositions_[i] を絶対フレームで再構成」と診断したが、これは**誤り**だった。
`inspect-quants` で −6.5/−6.19 を強制して full score card を吐かせると、**limit_penalty は UNSHIFTED beam_y に対して発火**（shortest_y_=−6.74・base_lengths_=0）。
シフトは**すべて ideal 側**にあり、その正体は **forced-direction stem shortening**（Roush & Gourlay）＝**beam の `shorten` プロパティ**:
- `Beam::calc_stem_shorten`（`beam.cc:1059-1090`）＝`beamed-stem-shorten[beam_count-1]`（`define-grobs.scm:493`＝`(1.0 0.5 0.25)`）× `forced_stem_count/normal_stem_count`。knee は 0。
- 「forced」＝頭が中線から外れ（`|chord_start_y|>0.1`）かつ direction ≠ default-direction（`beam.cc:1277-1293`）。default-direction は**音符位置ごと**（中線下→up・上→down・中線上→0＝除外）。
- `shorten` は **ideal のみ**から引く（`stem.cc:1245` `ideal_y -= shorten`）——**shortest_y_ は引かない**。BMD は 4 stem 全 forced で shorten=1.0、ideal を −7.52→−6.52 に寄せ、shortest_y_ −6.74 に引かれ −6.81 に量子化。
- 移植＝`BeamScoringProblem.ComputeBeamShorten` ＋ `CalculateBeamedStemInfo` の `idealY -= beamShorten`。**定数 fitting なし**（`shorten` は grob プロパティの字面移植）。
- ⚠️ **LP 検証で clef を必ず合わせる**: `instrument bass` は Lily# では**楽器名**（ト音記号のまま）だが、この fixture は**ヘ音**で組まれる。treble の LP と比べて「不一致」と誤読した（実際は bass で 0.5/0/0 一致）。

★ **twin の壁は測定で解けた**: Lily# は単音 `@stemdown` で beam 群を下向き強制**できない**ので BMD/BMU は `voice { … }` で強制。LP の量子化は単音 `\stemDown`・`\voiceTwo`・`\voiceOne` で **14 桁一致**。

★ **スラー・タイの縦は完了**（`d11ede43`／`c182d4d0`→`b0fe9c42`）。タイ残 +0.001391 は追わない
（インク taper 近似＋ratio 0.333。スラーの −0.000076 Pango と同類）。

⚠️ **LP の実測は scheme dump が速い**: `\once \override <Grob>.after-line-breaking = #(lambda (g) (format (current-error-port) "…~s…" (ly:grob-property g 'positions/'control-points)) '())`
で staff 相対（中線=0・up+）に吐ける。タイ arc 高さ・beam positions ともこれで LP と桁照合（SVG 精密測定＝§5.3 禁止 を回避）。

⚠️ **スラーの残・その1: ページ（system 間）スラー/タイは未種**。`AugmentSkylinesForPaging` にはまだ
入れていない（TSU/TSD に相当する **SSU/SSD の対を先に起票**してから。台帳点の無い snapshot を動かさないため）。

⚠️ **スラーの残・その2: `move_away_from_staffline` は未移植**（`slur-scoring.cc:640-658`）。端点が
五線の線上（±0.2 以内）に落ちると LP は `0.15ss` 外へ弾く。SD/SU では発火しない＝**別の名前付き点**。
⚠️ **端点も grace/ossia head はスケールする**——今回は full-size head half（0.545）で全スラーを弾いた（grace は僅かに深い・目視健全）。

⚠️ **スラーの残・その1: ページ（system 間）スラーは未種**。`AugmentSkylinesForPaging` にはまだ
スラーを入れていない（TSU/TSD に相当する **SSU/SSD の対を先に起票**してから。台帳点の無い
snapshot を動かさないため）。タプレットが TU/TD の後に TSU/TSD で page を閉じたのと同じ順序。

⚠️ **スラーの残・その2: `move_away_from_staffline` は未移植**（`slur-scoring.cc:640-658`）。端点が
五線の線上（±0.2 以内）に落ちると LP は `1.5·staff_space·dir/10 = 0.15ss` 外へ弾く。SD/SU では
発火しない（端点は五線の遥か下）ので今回の残差には無関係＝**別の名前付き点**。開くなら端点が
線に載る fixture を対で。⚠️ **端点も grace/ossia head はスケールする**——今回は full-size head
half（0.545）で全スラーを弾いたので、grace スラーは head スケール分だけ僅かに深い（目視では健全）。

### ▶ 保留になった一手 ＝ **clef の LILC-vs-skyline sliver（Y 4 点）は SKPath だけでは閉じない**

⚠️ **`f37d2af1` の「SKPath で輪郭が取れる＝手が届く」は半分だけ正しかった**（2026-07-23 に実測）。
LP の clef skyline (−2.540 . 4.776) は `add_named_glyph_segments`（`stencil-integral.cc:535`）が
**輪郭を `scale = LILC_bbox_X / outline_bbox_X` で縮めて** `add_outline_to_skyline` で作る。
Skia は輪郭 **Y**（635/1194 font-units）を LP と桁一致で返すが、**scale が合わない**:
`LILC_X/outline_X = 2.565/643 = 0.003989` に対し LP の実効 scale は **0.004**（＝ 635×0.004=2.540）。
差 0.27% は **LP 内部の `get_unscaled_indexed_char_dimensions`（grob extent 2.565 とは別の LILC 計測）×
magnification 0.5690551 の相互作用**由来で、Skia からは出せない。**フォントは byte 一致（同一 SHA256）**
なので font 差ではない。閉じるには **LP を instrument して
`get_glyph_outline_bbox` / `get_unscaled_indexed_char_dimensions` / magnification を dump** して
実効 scale を突き止めるのが先（payoff は 4 点 ×0.0001〜0.0008 ss）。**SKPath 直読みで定数を合わせるのは
§5.2 違反（fitting）。** ⚠️ この 4 点はまだ台帳に「clef sliver」として残っている（§3A）。

#### ✅ このセッション（2026-07-24 最新）＝ **島2（実 glyph 水平スカイライン基盤）を建てて臨時記号スタッキングを閉じた（4 点 exact ＋ ♮ 1 点）**

| commit | 内容 |
|---|---|
| `e08f5e12` | **臨時記号を実 glyph 水平スカイラインで nest**（`position_apes` 字面移植）。①実 outline スカイラインを bake（`Extract-EmmentalerSkylines.py`→`GlyphSkylinesGenerated.cs`＝`add_outline_to_skyline` の再現。CCW 分類・cubic を `max(2,len/0.2)` で平坦化・**scale=1.0 で LP dump と 15 桁一致**）。②`HorizontalSkyline` に contour 構築＋`Raise`/`Shift`/`Scale`/`MaxHeight`＋horizon-padded `Distance`（LP `padded()`）。③`AccidentalPlacement` の box `QueryXInRange` を `position_apes` へ（**高い臨時記号を先に置く**＝三度が C 字に nest。単音も同経路＝note gap 0.35）。④`SpacingRules` の単音予約を `CalculateSinglePosition` の実左端へ（courtesy paren×barline 重なりを解消＝ユーザーが PNG 目視で発見）。⑤`0.375` は LP の位置へ（`accidental.cc:76` flat の右スカイライン膨らませ）、box/2-rect `AccidentalGlyphSkyline` 削除。**4 点 exact（sharp 1.284000 / flat 0.964561）＋ `barline.next.key-change-to-notehead` −0.017672→exact**。snapshot 18 件再ベース |
| `aa09f78e` | **chord accidental の列スタッキングを台帳に開いた**（`chord.accidental.{sharp,flat}-column-gap-{below,above}`・4 点・vertical mirror・ハーネスのみ・**出力不変**）。probe CSB/CSA/CFB/CFA（三度クラスタ＝符頭非反転で列が clean、trailing a/b/c'' で cancellation natural を排除）＋ `RenderedGeometry.ChordAccidentalColumnGap` ＋ 台帳 4 点。**mirror 一致**（below=above）＝方向非依存が確認できたので数値は信頼できる |
| `452830b1` | §1 書き換え（この分。▶ を島2 の実スカイライン建設へ repoint） |

**開いた時の測定**（`aa09f78e`。この 4 点は `e08f5e12` で閉じた＝上の行）: Lily# は列を **box（glyph 幅 + padding 0.2）** で積んでいた（sharps 1.300 / flats 1.120）が、LP は **実 glyph 水平スカイライン**で nest（sharps **1.284000** / flats **0.964561**）＝当時 残差 **+0.016 / +0.155**。**予測を先に書き、sharp の +0.016 は測定前に桁一致**（同じ和音を二度＝second で書くと LP も 1.300＝nest 無しになることから導出）。
開いた時点: テスト 3223 / LP 忠実度 33/50 exact・total 0.364863 ss（現在は 38/50・0.004313 ss＝§1 冒頭）。

##### ★★ 教訓: **実装があっても、台帳の点が無ければ LP との差は見えない**
`AccidentalPlacement.cs` は skyline collision・stagger・ape 列を持つ立派な実装だが、**字面移植ではなく再解釈**で、box パッキングという構造欠陥を抱えていた。点を開くまで +0.016/+0.155 は誰にも見えていなかった（§5.2.1④ の X 版）。
⚠️ **発明の `0.375` flat-merge は不発だった**（box より緩い枝で `maxRight<xRight` guard が非発火）＝**定数を疑う前に「その枝が実際に発火しているか」を測る**。予測（0.375 が原因）は方向だけ当たり magnitude が外れ、その外れが真因（box モデル）を指した——§1 の「予測が外れたときこそ2つ目」の X 版。

#### ✅ 前セッション（2026-07-24）＝ **beam を LP の forced-shorten で閉じた（+0.69 → 0）**

| commit | 内容 |
|---|---|
| （**未コミット・要 add**） | **forced-direction stem shortening を移植**（beam の `shorten` プロパティ）。`BeamScoringProblem.ComputeBeamShorten`＝`Beam::calc_stem_shorten`（`beam.cc:1059-1090`）の字面移植＝`beamed-stem-shorten[beam_count-1]`（`(1.0 0.5 0.25)`）× `forced_stem_count/normal_stem_count`（forced＝頭が中線外＋dir≠default-direction、`beam.cc:1277-1293`）。`CalculateBeamedStemInfo` が `idealY -= beamShorten`（`stem.cc:1245`＝**ideal のみ**、shortest_y_ は不変）。tab は string 位置に音高 default-direction が無いので除外（`LILYSHARP-OWN`）。**`staff.staff.beam-{under,over}-notes` +0.69 → 0**（BMD=BMU exact）。**snapshot 7 件再ベース**（全て forced beam の短縮・**LP と個別照合済**: tab 0.5/0/0・drums 1.0・script 0.75・swing 0.75・showcase 0.25・全一致） |

**テスト 3219 passed / 0 failed / 3 skipped**（基準 3218）。Core 0 warn / 0 err。
**LP 忠実度 33/46 exact, total |residual| = 0.021985 ss over 42 distances ＋ counts 4/4**
（beam 2 点 0.69 → 0＝exact。合計 1.401985 → 0.021985）。

##### ★★ 教訓: **base_lengths_ 仮説は否定・正体は beam 'shorten（§1 冒頭に詳細）**
前 handoff の「score_stem_lengths の base_lengths_ を絶対フレームで再構成」診断は誤りだった。full score card 測定で
limit_penalty は UNSHIFTED beam_y に発火（base_lengths_=0）と判明、シフトは ideal 側の forced-shorten だった。
⚠️ **LP 照合で clef 取り違え**（`instrument bass`＝楽器名でト音のまま vs Lily# ヘ音）を一度踏んだ——**必ず同一 clef で比較**。

#### ✅ 前セッション（2026-07-24 後半）＝ **beam を対で開き、種を入れた（+0.95→+0.69）**

| commit | 内容 |
|---|---|
| `1a002706`（**未 push**） | **beam の台帳点を対で起票**（BMD/BMU）。`SkylineBuilder.AddNoteBoxToSkylines` の**固定 3.5 符尾**予約を確定＝**残差 +0.950000000**。コード変更ゼロ |
| `0138e5c0`（**未 push**） | **beam の種を staff スカイラインへ**（`AddBeamsToSkyline`＝`OuterEdgeStaffSpaceAtX`／`BeamedItemsToSuppress`／`StaffBeamLayouts`）。**+0.95 → +0.69**。視覚 fixture `test/beam-under-staves` 新設。残 +0.69＝描画 beam 自体（→上記で forced-shorten と判明・閉じた） |

##### ★★ 教訓: **種が「描画は正しい」という前提を否定した（第2欠陥＝描画 beam）**
前 handoff は「描画は quanter で正しい・予約だけ古い」と書いた。種を入れて予約が描画に一致した瞬間、**残差は 0 でなく +0.69**——
つまり**描画の beam 自体が LP より 0.69 低い**（Lily# 中心 −7.5ss・符尾 3.0／LP −6.81ss・符尾 2.31）。
固定 3.5 の過剰予約がこの描画欠陥を**隠していた**。§5.2.1④「穴を開けるまで何が溜まっているか分からない」の実例。
⚠️ **「描画は正しい」を実測せず信じない**——予約が描画より外側にある間は描画欠陥は見えない。

##### ★ 教訓: **「twin の壁」は起票前に測って否定できる**
前セッションの「`\voiceTwo` で量子化が変わり得る」警告を実測で否定（単音 `\stemDown`・`\voiceTwo`・`\voiceOne` とも ∓6.81 で 14 桁一致）。
beam 数を 2/3/4 に変えても平坦 beam の量子・gap は不変（BMD=BMU の一致がその検算）。

#### ✅ このセッション（2026-07-24 前半）＝ **スラー 0.13 を閉じ、タイを開いて種を入れて閉じた（縦の bow は完了）**

#### ✅ このセッション（2026-07-24 前半）＝ **スラー 0.13 を閉じ、タイを開いて種を入れて閉じた（縦の bow は完了）**

| commit | 内容 |
|---|---|
| `d11ede43`（**未 push**） | **スラーの端点アタッチを LP 化＋インクを tapering に**。SD/SU **−0.130000 → 0.000000**（両 exact・対の一致）。**発明を 2 つ落とした**: ① 端点＝note 中心+0.9（`slurOffset 0.6`＋候補 `offset 0.3`）を LP の **head 端+0.5ss=1.045** へ（`slur-scoring.cc:556-557,727`）。② `AddSlursToSkyline` の平坦インク 0.1 を LP の bezier sandwich（内側制御点を 0.5·curvethick 外へ＋round pen）へ＝峰で **0.085**・端で pen のみ（`lookup.cc:395-415,484-515`）。net 0.145−0.015=0.13。**ratio 0.25／height-limit 2.0 は既に桁一致**＝arc は無関係（旧 handoff の 3 候補を実測で否定）。**snapshot 12 件再ベース**（描画スラー端点が head で 0.145 深く・beam tip で 0.3 浅く、両方 LP 方向。PNG 目視で衝突なし） |
| `c182d4d0`（**未 push**） | **タイの台帳点を対で起票**（TID/TIU、`staff.staff.tie-{under,over}-notes`）。スラーの隣の grob＝LP の Tie も vertical-skylines/no outside-staff-priority で staff skyline に入るが Lily# は未種。**予測を先に書き桁まで的中**: Lily# は note 床 9.095 に座り LP は 9.655901 に垂れる＝**−0.560901**（TID/TIU 同値）。タイは平坦なので e/a'（−11/+11）まで出して床9を超えさせた。コード変更ゼロ（probe+twin+台帳） |
| `93ae87d1`（**未 push**） | **タイの bow を staff skyline に種**（`AddTiesToSkyline`＝スラーと `SeedBowInk` を共有＝描画と予約が1モデル）。**−0.560901 → +0.020776**（対の一致・**snapshot 0 件**＝譜間タイ fixture が無い＝スラーと同じ穴）。残 **+0.020776 は第2欠陥**＝描画タイが LP より 0.0208 深い |
| `b0fe9c42`（**未 push**） | **タイの arc 高さを attachment 幅で測る**（LP `attachment_x_.widen(-x_gap)`）。**+0.020776 → +0.001391**（対の一致）。LP の control-points を scheme dump で照合＝**端点は既に一致**（中心−0.5=−6.0）で、差は arc 高さのみ＝`Solve` が height を **note 間の生幅**（2·XGap=0.4 広い）で計算していた。inset して解消。残 +0.001391 は インク taper 近似＝**追わない**。**snapshot 10 件再ベース**（描画 arc 0.026 浅く・LP 方向・viewBox 縮み外描画なし・PNG 健全） |

**テスト 3202 passed / 0 failed / 3 skipped。** Core 0 warn / 0 err。
**LP 忠実度 31/44 exact, total |residual| = 0.021985 ss over 40 distances ＋ counts 4/4**
（スラー 2 点 → 0、タイ 2 点 → +0.001391。タイ全体で 1.141004 → 0.021985）。

##### ★ 教訓（3 度確認された）: **種を入れると狙っていなかった第2欠陥が出る**
スラーの 0.13 は「単一定数」に見えて **端点 +0.145 ＋ インク −0.015** の合成だった（§5.2 の縦版）。
**タイも種を入れた瞬間 +0.02 が出て、それは arc 高さ**（幅の取り違え）だった——**予約でもインクでもなく描画自身**。
§5.2.1④「穴を開けるまで分からない」が 7 回目。⚠️ `StemThickness = 0.13` とスラー残差の一致は**偶然**。
⚠️ **タイの arc は attachment 幅**（端点を XGap で inset した後）で測る。生の note 間幅は 2·XGap 広く、arc が高くなる。

#### ✅ 前セッション（2026-07-23 後半）＝ **スラー点を対で開いた＋clef sliver を実測で保留した**

| commit | 内容 |
|---|---|
| `87bcde22`（**未 push**） | **スラーの最初の台帳点を対で起票**（SD/SU、`staff.staff.slur-{under,over}-notes`）。出力不変（probe 2 book＋台帳 2 点＋.lys twin のみ）。**予測 −0.512596 が桁まで的中**・SD/SU 同値。欠陥確定＝スラーは skyline に未予約 |
| `f093583e`（**未 push**） | **スラーを staff スカイラインに種として入れた**（`AddSlursToSkyline`＝bezier を LP と同じく平坦化＋bow 半幅で外へ／`StaffSlurLayouts`＝offset 0 の 1 譜 system で `LayoutSlurs` を丸ごと再利用）。SD/SU **−0.512596 → −0.130000**（両同値）。**committed snapshot は 0 件動かず**（譜間スラーの fixture が無い＝穴）＝corpus では出力不変。視覚 fixture `test/slur-under-whole-notes` を新設（PNG 目視）。`ClefToString` を internal 化 |

clef sliver は **SKPath 直読みでは閉じないと実測で判明**（上の「保留になった一手」）＝コード変更ゼロ・investigation の test/probe は削除済み。


**タプレット括弧は 4 点とも閉じた**（残差は名前付きの 1 種類だけ）。X 軸も「定数 1 つで閉じる」ネタは
尽きている。**残っている最大のリスクは、測られていない領域そのもの**——§3A の穴は
**スラー/タイ・ビーム・臨時記号配置**で、台帳の点が **1 つも無い**。

⚠️ **今セッションの実績が根拠**: 点を開いた瞬間、**狙っていた欠陥と一緒に狙っていなかった欠陥が落ちた**。
タプレットでは 4 つ（予約漏れ・幻の符尾・prelim の声部スコープ漏れ・probe が clef を測っていた）。
**穴を開けるまで、そこに何が溜まっているかは分からない**（§5.2.1④）。

**手順は 4 回連続で成立した型をそのまま使う**:

1. **点を起票する**（コード変更ゼロ・出力不変）。`residual: null` ＋ **予測を先に `why` へ書く**。
2. ⚠️ **必ず対で作る。** P/Q・TU/TD・TSU/TSD の 3 組とも、**値ではなく「対が食い違ったこと」**が
   欠陥を出した。片側だけなら予測が当たって欠陥ごと出荷していた（今回まさにそうなりかけた）。
3. ⚠️ **床に座らせない。** 譜間は basic-distance **9**、system 間は **12**。それを超えないと点は
   両側 exact になって**何も測らない**。probe TU が最初 `d'` で 9.000 を返した件を読むこと。
4. ⚠️ **probe が何を測っているか確かめてから信じる。** TSU は最初 clef を測っていた（下記）。

**その後の候補**: 水平スカイライン（X の ♮ 1 点 −0.017672 ＋ 臨時記号配置の族が閉じる。§2④'）と、
下の**描画フォントの乖離**（ユーザー判断待ち）。

#### ✅ 完了: フォント metric を実行時に測るようにした（`f37d2af1`）

**タプレット 4 点は +0.0000208 で閉じた**（残るは Pango 量子化の半分）。
**LP 忠実度 total 2.214721 → 0.023937 ss** ＝ タプレットを測り始める前の 0.023853 に戻った。

決定と実施内容:

| | |
|---|---|
| 既定テキストフォント | **TeX Gyre Schola**（＋ sans は **TeX Gyre Heros**）を同梱 |
| 選定理由 | `"LilyPond Serif"` は C059 を第一優先。**C059 と Schola は全 advance とインクが一致**＝同一設計。ただし **C059 は AGPL v3**、Schola は **LPPL 1.3c**（GUST） |
| 測定 | `SKPaint.GetTextPath(...).Bounds`（**`MeasureText` はフォント単位で量子化されるので不可**） |
| 適用 | レイアウトの 37 箇所＋3 backend すべてを同じファイルから |

✅ **旧メトリック表（`Serif/SansTextMetrics`）と同梱 Liberation は削除済み**。
`THIRD-PARTY-NOTICES.md` / `README.md` は TeX Gyre の帰属に差し替え済み。
⚠️ **`FontEmbedInfo.LibreMarkers` の `"Liberation"` は残す**——あれは**利用者が
`font "X" embedded` と書いたときの許可リスト**で、システム導入フォントの話。同梱物とは無関係。

⚠️ **改行が動いた**: `test/lyric-break-pricing` 2→3 システム、`test/lyrics-after-rest-bar` 1→2。
歌詞が 9% 広くなった直接の結果で方向は正しいが、**目に見える製品挙動の変化**。

★ **予約と描画が一度も同じフォントでなかった**: PNG は CSS generic をシステム解決（実測で
`"serif"` → **Segoe UI ＝ sans**）、SVG は閲覧者任せ、PDF だけが Liberation。
レイアウトは Times 幅を予約していた。**タプレット括弧が「どこにも予約されていなかった」のと同型**で、
**こちらも台帳の点が 1 つも無かった**。

#### ★★ 元の判断メモ（2026-07-22）

> ⚠️ **以下は着手前の状態を現在形で書いたもの。** `Serif/SansTextMetrics` も同梱 Liberation も
> 既に削除済みで、38 箇所は全て `TextFontMetrics` に移っている。**判断の根拠として残す。**

⚠️ **過去の記述「C# 側に OTF パーサは無い」は誤りだった。** 裏取り済み:

| 事実 | 出典 |
|---|---|
| `SkiaSharp` は **`LilySharp.Core` の依存** | `LilySharp.Core.csproj:31-35` |
| `SKTypeface.FromFile` / `FromFamilyName` / `MeasureText` を**本番で使用中** | `PngDrawingContext.cs:284-315`、`FontEmbedInfo.cs:71,137` |
| `.lys` は**テキストフォントを指定できる** | `font "NAME"` ヘッダ → `MultiStaffScore.TextFont` |

つまり**フォントは利用者が選べる**ので、**定数を焼き込む設計は原理的に誤り**——`font "..."` を
書かれた瞬間に外れる。LP と同じく**実効フォントに実行時に問い合わせて式へ入れる**のが正しい。

**本当に無いのはレイアウト層の配線**。今のレイアウトは `SerifTextMetrics` ＝
**Times New Roman の advance 幅を手打ちした表**（`SerifTextMetrics.cs:47-55`）を使っており、
**高さを一切持たない**。しかも**描画側は別のフォントで描いている**ので、
これは強弱記号で記録済みの「確保するのは片方のインク、描くのは別フォント」と同じ病気。

⚠️ **規模はタプレット数字 1 件ではない。** `Serif/SansTextMetrics` の呼び出しは
**10 ファイル 38 箇所**（`MusicMarkEngraver` 15・`OutsideStaffStacker` 6・`LayoutEngine` 4 …）で、
**幅の予約も全部この表**。1 箇所直せば数字の高さも既存の幅も同時に正される。

⚠️ **決めるべきは 1 つ: 既定の決定性。** 実インストールのフォントを測ると、
**同じ `.lys` がマシンによって違う組版になる**。これは `f37d2af1` に入ったプローブ修正（serif 固定）と**同型**
（fontconfig 任せの serif を測っていた）。LP は自分のテキストフォントを同梱して既定を固定している。
Lily# は Emmentaler は同梱しているがテキストフォントは同梱していない。
→ **測るのは動的に、既定は決定的に**（＝既定用のテキストフォントを 1 つ同梱する）が素直。

#### ✅ Phase 0 は実施済み — **設計は成立する**（2026-07-22・コード変更ゼロ）

予測を先に書いてから測った。LP 側: `text-font-size 11pt × 2^(−2/6)` = **8.730706 pt**、
1 ss = 5 pt なので 1.255434 ss = 6.277170 pt ⇒ **予測 ink/em = 0.718976**。

| 経路 | 結果 |
|---|---|
| `SKPaint.MeasureText(s, ref SKRect)` | **0.718750**（TextSize を 10^6 にしても不変＝**量子化**。粒度 0.027 ss で**追っている残差より粗い**） |
| **`SKPaint.GetTextPath(...).Bounds`（アウトライン）** | **0.71900006** ⇒ **1.255476 ss** |

**LP 実測 1.255434 ss との差は 0.000042 ss。** ⇒ 残差 −0.547717 は **−0.000021 程度まで**閉じる見込みで、
残りは `DynamicText` の −0.000076 と同じ **Pango の量子化**と整合する。
⚠️ **使うのは `MeasureText` ではなく輪郭**（LP も FreeType のアウトラインを見ている）。

⚠️⚠️ **そして名前引きは使えない。** このマシンで `SKTypeface.FromFamilyName` は
**`"LilyPond Serif"` も `"C059"` も `"TeX Gyre Schola"` も、そして `"serif"` さえ Segoe UI** に解決した
（＝**sans**）。LP は自分の `share/lilypond/2.26.0/fonts/otf` を私的 fontconfig で登録しているので
システムには居ない。**つまり Lily# の PNG は今この瞬間「serif」を Segoe UI で描いている。**

⇒ **同梱してファイルから `SKTypeface.FromFile` で読むのは必須**（決定性のためだけでなく、
そもそも名前が解決しないため）。LP 同梱の候補は `C059-*.otf` と `texgyreschola-*.otf`（どちらも
数字の輪郭高さは同一 0.71900006 なので、この点だけでは選べない。幅や他グリフで決めること）。

#### ⚠️ このセッションが作った/露わにした複製 2 件（着手時に畳む候補）

- **`EnrichExtentsWithAnnotationProtrusions` は今やタプレット括弧の 2 つ目のモデル**（`LayoutEngine.cs:872-880`
  の手書き封筒 `hi−1.6 / lo+0.7`）。スカイライン経路が入ったので、**同じ grob を別式で持つ状態**＝
  §5.2.1② そのもの。空スカイラインの fallback でしか効かないので優先度は低いが、名指しておく
- **prelim パスが 4 つの引き当て表を自前で作っている**（`LayoutEngine.cs` の `prelimVoicesByStaff` ほか）。
  最終パスの同名テーブルと**同じ構築を 2 回**している。`staffYByIndex` だけは読み元の system 配列が
  違う（prelim/最終）ので、統合するなら等価性を確かめてから

---

### ✅ このセッション（2026-07-23）でやったこと — **タプレット括弧・実フォント計測・TimeSignature**

⚠️ **ユーザーがこのセッションの細かいコミットを squash で意味単位にまとめた**（2026-07-23）。
下表は **squash 後の実在ハッシュ**。作業中の細かいハッシュ（`caa0f239` `b36db266` 等）は
**全部 dangling** になった——本ファイルの他所や過去記述に残る旧ハッシュは
`git merge-base --is-ancestor <sha> master` で到達性を確認すること（§1 末尾の手順）。

| commit | 内容 |
|---|---|
| `eb8315f8`（origin） | **タプレット括弧を譜間とページ両方のスカイラインへ＋幻の符尾**。起票→予測→実装を対（TU/TD・TSU/TSD）で。**欠陥 4 件**を検出（①予約漏れ ②幻の符尾＝`highestPos` を符尾先端の集約に作り替え ③prelim 注釈パスの声部/譜スコープ漏れ＝間隔だけ壊れ snapshot に出ない ④probe が clef を測っていた）。台帳 4 点 `−1.7/−1.3 → −0.547717` |
| `f7d2983c`（origin） | **強弱の縦インクを実インクへ**（前セッション分。dynamic は fetaText＝アウトライン） |
| `f37d2af1`（origin） | **実フォント計測**。TeX Gyre Schola/Heros 同梱、`TextFontMetrics`（輪郭で測る）、レイアウト 37 箇所＋3 backend を実フォントへ。タプレット 4 点 `−0.547717 → +0.0000208`。**予約と描画が一度も同じフォントでなかった**（PNG=Segoe UI）。CJK 幅の回帰修正・旧 Times 表と Liberation の削除も同梱 |
| `033c0df8`（**未 push**） | **TimeSignature 幅を Pango 量子化へ**（LP 定数から導出：`72×72.27/(1200×5×25.4)`）。`barline.next.time-change-to-notehead −0.004735 → +4.3e-7`。**exact 28→29 / total 0.023937→0.019202 ss**。**snapshot 188 件再ベース**（y 不動・最大 0.03 ss） |
| `11d5bcba`（**未 push**） | §1 書き換え（この分） |

**テスト 3197 passed / 0 failed / 3 skipped。** Core 0 warn / 0 err。
**LP 忠実度 29/40 exact, total |residual| = 0.019202 ss over 36 distances ＋ counts 4/4。**

#### ★ TimeSignature の −0.004735 は Pango 量子化だった（DynamicText の −0.000076 と同一機構）

LP の default 拍子は `\number`（fetaText）＝**テキスト経路**で、幅は device pixel に hint される。
量子 `q = 0.034143 ss` は **`INCH_TO_BP / (PANGO_RESOLUTION × output_scale)`** から**字面導出**
（`pango-font.cc:109-112`）。全 10 桁で LP を 1e-15 再現。⚠️ **同じ Pango 量子化でも
DynamicText の高さ側は輪郭全体の量子化が要るので未着手のまま**（幅は 1 回の snap で済む）。

⚠️ **未測定の本物の乖離を記録した**: Lily# は `fattened` 桁 advance を使うが LP は ASCII 経路。
桁 4 は両者 1.600 で同じだが桁 1 は fattened 1.292 vs ASCII 1.268。1/4 拍子等の点が無いので
**推測で直さず点を先に作る対象**（`GetTimeSigDigitWidth` の remark と台帳 why に記録）。

#### ★ 4 点とも、起票時の予測も実装後の予測も桁まで的中した

| 点 | 起票 | 実装後 |
|---|---|---|
| `staff.staff.tuplet-bracket-{up,down}` | −1.700225（2 点同値） | **+0.0000208**（4 点すべて同値） |
| `system.tuplet-bracket-{up,down}` | −1.295225（2 点同値） | **+0.0000208**（同上） |

⚠️ **途中段階の −0.547717 ×4 は `f37d2af1` のフォント配線で消えた。** 残るのは Pango 量子化の半分。

#### ★★ 教訓: **欠陥を出したのは値ではなく「対が食い違ったこと」**（3 度連続）

| 対 | 何が出たか |
|---|---|
| P/Q | 幻の符尾（`89aaa29f`） |
| TU/TD | 予約漏れ＋幻の符尾（LP 側が同値になるまで pitch を直した） |
| **TSU/TSD（LP 側）** | **probe が clef を測っていた**——小節丸ごとのタプレットだと括弧が clef の直後から始まり、相手 system の最深インクが五線 2.05 ではなく **clef 3.540** になる。差 1.490 がモデルに合わなかった。各小節の頭に素の全音符を置いて解決 |
| **TSU/TSD（Lily# 側）** | **prelim パスが声部を見ていなかった**——シード後 TSU だけ予測どおり動き、TSD が動かなかった |

⚠️ **TSD が動かなかったことが無ければ、TSU は予測に一致して点は閉じ、欠陥ごと出荷していた。**

#### ★★ 教訓: **プローブの描画オプションが答えを決めることがある**（`b69c73e6`）

`ly/paper-defaults-init.ly:170-173` は `fonts.serif` を **SVG backend のときだけ**素の `"serif"`
（＝fontconfig 任せ）に落とす。測定スクリプトは**実ページが要るので `-dbackend=svg`** を使っている。
結果、**binding するインクにテキストが入る量だけ**が、LP 本来のフォントでない・かつ**測定マシン依存**の
値で台帳に入っていた。タプレット 4 点が **0.027492** ずつ小さかった。

⚠️ **対が一致していても捕まらなかった**——両側とも同じだけ間違っていたから。
**対の食い違いは「片側だけの欠陥」しか捕まえない。** 参照そのものが偏っているときは効かない。

切り分けは実測: 両 backend で全書籍を回すと、**binding がグリフか五線の 8 冊（N J S L T P D Q）は
完全に同一**（12.000000 / 12.254816 / 12.255229 / 11.716033 / 11.716074 / 12.018019 /
9.595000 / 10.783076 / 9.595000）で、**タプレット 4 冊だけが毎回 0.027492 違う**。
book D の強弱が無事なのは **DynamicText が fetaText＝Emmentaler** でテキストフォントではないから
（`26afa9fe` が逆向きに使った区別と同じ）。

→ `probeTag` に `property-defaults.fonts.serif = "LilyPond Serif"` を入れた（**個々の book ではなく
タグに**。次にテキストを含む book を足す人が同じ罠を継がないように）。
**数字の半分の高さは 0.627717**（旧記録 0.600225 は誤り）。

#### ★ prelim（間隔決定）パスは最終パスと**別の geometry を見ていた**

`LayoutEngine` の prelim 注釈パスは `VoicesByStaff` / `MeasuresByStaff` / `StaffYByIndex` /
`StaffByIndex` を**渡していなかった**ので、`TupletBracketEngraver` が
**主譜の主声部**にフォールバックしていた。声部 2 のタプレットは**声部 1 の音符**から、
下の譜のタプレットは**上の譜の音符**から位置決めされていた。
⚠️ **最終パスは正しく描いていたので、snapshot には一切現れない**——間隔だけが間違っていた。
**オラクルが構造的に見られない種類の欠陥**で、台帳の対だけが捕まえられる。

#### ★ snapshot 14 件は**全部が剛体の平行移動＋ページ枠**

12 件は `<svg>` の高さと viewBox **だけ**が変わりグリフは 1 つも動かず、
`figbass-chordname-lower-staff` は 67 個の y が**全て −1.74**、`ossia-beams` は 73 個の y ＋
ossia の transform ＋ スラー 2 本が**全て −0.04**。要素数・行数は 14 件とも一致。
ページ枠が動いたのは**単一ページ経路が内容サイズでページを作る**から＝間違っていたのは測定の方。
⚠️ **縮んだ側があるので全 28 ファイル（前後）で「viewBox 外に描画が無いか」を検査した**
（`fc0feb20` の教訓）。**ゼロ**。

---

### 同日の前半（2026-07-22）— 譜間側の記録

#### ★ 起票時の予測も、実装後の予測も、2 点とも桁まで的中した

| 段階 | 予測 | 実測 |
|---|---|---|
| 起票（コード変更ゼロ） | −1.700225 | **−1.700225000**（TU/TD 同値） |
| シード＋符尾ガード後 | −0.547717 | **−0.547717000**（TU/TD 同値） |

**TU と TD が全段階で同じ値を返すこと自体が検証**（P/Q と同じ性質）。2 冊は音高が 0.5 違うだけなので、
引き算で残るものは括弧のスタックだけ。⚠️ **片方だけ作っていたら符号の誤りを見逃せた。**

#### 1 つの数字に**符号の逆な欠陥が 2 つ**入っていた

| | 直した内容 | 寄与 |
|---|---|---|
| ① 予約漏れ | `SkylineBuilder` に **`tuplet` という語が 1 つも無かった**。譜間もページも括弧を種にしておらず、**下の譜の括弧が上の譜の五線を突き抜けて描かれていた**。LP の TupletBracket は `outside-staff-priority` を**持たない**＝VerticalAxisGroup の内部 grob で、clef と同じく staff のスカイラインに入る | **+1.18** |
| ② 幻の符尾 | `CalculateSlope` が `DefaultStemLength` を音価によらず足していた（`89aaa29f`・`26afa9fe` と同型の 3 つ目）。LILYPOND-REF `lily/stem.cc Stem::is_normal_stem` | **−2.955** |

#### ★ ② が 2 度の掃除を生き延びたのは「門を足すだけでは済まなかった」から

`highestPos`/`lowestPos` は **staff position** を集約してから一律に符尾を足していたので、
**音価を見る場所がそもそも無かった**。**列ごとの encompass point**（符尾があれば先端、無ければ符頭の
LILC インク）の集約に作り替えた。⚠️ **これは整理のための書き換えではない**——音価が混ざった瞬間に
**「五線上で最も高い音」と「括弧が避けるべき音」が別物になる**。
LILYPOND-REF `lily/tuplet-bracket.cc calc_position_and_height`。

#### ★ `0.13 → 0.16` は新しい定数ではない — **読み手が居なかった**

`EngravingDefaults.TupletBracketThickness` は **LP の `1.6 × line-thickness` を LILYPOND-REF 付きで
最初から持っていた**のに、`SharedRenderer.Overlays.cs` がその**隣で生の `0.13` を書いていた**。
**§5.2 の「未実装ではなく、書いてあるが呼ばれていない」の実例。** 描画と予約は 1 つの定数から。

#### ★ 残るのは **TupletNumber のテキストインク** 1 件（−0.547717）

`lily/tuplet-number.cc:342` は knee でない限り数字の `Y-offset` に**括弧の positions の中点**を返し、
`:227-228` が stencil を**中心合わせ**する。だから数字は括弧線をまたぎ、**自分の高さの半分＝0.627717**
だけ外へ出る（括弧自身の 0.08 より遠い）。これは LP の**斜体 font-size −2・通常テキストフォント**の
metric ＝ TimeSignature の −0.004735 と同じ経路で、Lily# に測る手段が無い。

⚠️ **一度台帳に「padding 1.1 ＋ 括弧の半分の太さ 0.08 ＋ 数字の食み出し 0.547717」と書いたが、
それは合計から逆算した推論で誤り。** 合計は同じでも分解が違う。訂正済み。
**§5.3「要約された数字から逆算しない」の縦版。**

#### ★ snapshot 9 件は**全行が線幅だけ** — そして**それがフィクスチャの穴の証拠**

56 行が動き、**56 行すべてが `stroke-width 0.130 → 0.160`**。`stroke-width` をマスクすると
**差分 0 行**、要素数・行数は 9 件とも完全一致、**Y は 1 箇所も動いていない**。

⚠️ **これは「無害の証拠」ではない。** 括弧の位置が 1 つも動かなかったのは、
**既存フィクスチャに全音符タプレットが 1 つも無く**、どれも `min(tipY, −staff-padding)` の床が
勝っていたから＝**コーパスは今回の修正の本体を見ていない**。`fc0feb20` の教訓どおり
**`test/tuplet-bracket-whole-notes` を新設**した（PNG 目視済み）。
2 小節に分けてあるのは、1 小節だと**2 つの数字が同じ X で重なる**のをフィクスチャが承認してしまうため。

#### ⚠️ 測定の罠（probe のヘッダにも書いた）

**`after-line-breaking` のコールバックで system 相対座標を読むと、その照会自体が縦の整列を
早期に確定させて答えを動かす。** 同じ音楽が譜間 **18.000000** を報告し（実際は 9.000000）、
その 9 ss を説明する grob を探して時間を溶かした。**数値は無摂動の描画出力から取ること。**

---

### 前セッション（2026-07-22）— **強弱記号の縦インクを閉じた**（`26afa9fe`）

分類 → 承認 → 実行の順で snapshot 16 件を再ベース済み（承認取得済み・**未 push**）。

| commit | 内容 |
|---|---|
| `26afa9fe` | **強弱の縦インクをフォントから読む＋幻の符尾＋符頭インク＋五線 frame**。`+1.866924 → −0.000076`。**snapshot 16 件再ベース**（分類・承認済） |
| `773db20f` | §1 書き換え＋**恒久ルール 2 件**（実測に基づく修正の禁止・リネームはユーザーが MSVS で） |

`staff.staff.dynamic-under-whole-note` は **+1.866924 → −0.000076**。
**着手前に書いた予測 −0.000076 と桁まで一致。**
LP 忠実度 **1.890701 → 0.023853 ss**（28/36 exact。exact 数は予測どおり不変＝
−7.6e-5 は tolerance 1e-6 より大きい）。テスト **3167 passed / 0 failed / 3 skipped**。

#### 1 つの数字に**欠陥が 3 つ**入っていた（符号が違うので同時にしか直せない）

| | 直した内容 | 寄与 |
|---|---|---|
| ① 幻の符尾 | `DynamicEngraver.GetLowestExtent` が**音価を見ずに** `noteY − DefaultStemLength` を返していた。`GetNoteValueFromFraction(...) >= 2` で門を付けた（LILYPOND-REF: `lily/stem.cc Stem::is_normal_stem`） | **−3.000000** |
| ② 符頭の名目箱 | この engraver だけ `NoteheadHalfHeight` 0.5 のまま。スカイラインは `22120764` で LILC の 0.545 に移っていた。`GetNoteheadBBox` へ | **+0.045000** |
| ③ 強弱の縦インク | `0.6 + 1.2 + 0.3 = 2.1` ＝ グリフを 1.5 の高さと見なしていた。LP は **3.188076** | **+1.088076** |

#### ★ ③ の答え: **LILC ではなく「アウトライン」**（`ec7a2254` と逆で、理由は同じ）

`DynamicText` は**グリフ引きではなくテキスト**（`define-grobs.scm:1438` `font-encoding
fetaText` ＋ `:1445` `ly:text-interface::print`）。だから
`Modified_font_metric::text_stencil`（`modified-font-metric.cc:125-143`）→ Pango →
FreeType アウトライン、という経路で測られ、**LILC を読む
`get_indexed_char_dimensions`（`open-type-font.cc:372-409`）は通らない**。
両者は丸め誤差の域ではなく違う: **LILC `f` = (−0.5834 . 2.0066) / アウトライン
= (−0.692 . 1.896)**。⚠️ **`ec7a2254` が「LILC が正」と決めたのは grob 経路の話**で、
**テキスト経路では逆になる。どちらの経路かを先に確かめること。**

裏取り（導出ではなく照合）は 3 組: `\p` → (−0.584004 . 1.168008)、
`\mp` → (−0.584004 . 1.196016) ＝ **アウトラインの p と m の和集合で、LILC からは到達不能**。
グリフ名は `dynamics.*` ではなく**素の ASCII 名 `f m n p r s z`**（fetaText 符号化）。

残る **−0.000076 は Pango の量子化**（LP のスカイラインは 2.588076、アウトラインは 2.588000）。
clef の LILC-vs-skyline sliver と同型の**名前付き残差**。埋めない。

#### ★ 4 つ目の欠陥が同じ式にあった — **打ち消し合っていた**

probe D では音符が binding するので**この数字には出ない**が、同時に直した:

| | Lily# 旧 | LP |
|---|---|---|
| 五線の寄与 | 線の**中心** 2.0 | `dim.set_minimum_height(staff_extents[dir])`＝**インク 2.05**（`side-position-interface.cc:323-330`） |
| `staff-padding` 0.1 | padding に**加算**していた | **refpoint の床**（`:433-453`）。加算ではない |

`2.0 + 0.1 + 0.6 + 名目 descent 0.64 = 3.34` と `2.05 + 0.6 + 実インク 0.692002 = 3.342002`。
**LP 実測 1.342 に固定していたテストが 2 桁では区別できず、何年も通っていた。**
`DynamicPlacementTests` は **3 桁**に上げてある（再ピン止めではなく、式が合ったので締めた）。
⚠️ **これが「実測に合わせて定数を選ぶ」の代表的な失敗形。§5.2 の枠に追記した。**

#### snapshot 16 件の分類（**X は 1 件も動いていない・要素数も全件一致＝改行不変**）

`page 高さ` は最大 +1.09。Y の移動量は全部この 5 つの和で説明できる:

| Δ | 正体 |
|---|---|
| **+0.696** | `f` を含む語のインク上端 1.2 → 1.896 |
| **+0.045** | 符頭インク 0.5 → 0.545（②。上向き符尾の音符に付いた強弱だけ） |
| **−0.050** | 五線の寄与 2.0+0.1 → 2.05（④。五線が binding する強弱だけ） |
| **−0.004 〜 −0.132** | 先頭が `p`(1.168)/`m`(1.196)/`s`(1.168)/`z`(1.068) の語＝**1.2 より低いので上がる** |
| 系の伸び | 上向き強弱が高く届くようになった分だけ system/page が伸び、その下が一律に動く |

代表例（`test/scripts-dynamics`、page 不変・動いたのは強弱 5 個だけ）:
`sfz fp sf` が **+0.74**（音符 binding＝0.696+0.045）、`rfz fz` が **+0.65**（五線 binding＝0.696−0.05）。

⚠️ **`test/dynamics` と `test/above-dynamics` は PNG で目視済み**（`--crop --scale 4.0`）。
衝突なし・上向きの積み上げも健全。

#### ⚠️ この変更で**見えるようになった**既存の乖離（未着手・別件）

**Lily# は強弱を serif の太字イタリックで描いている**（`SharedRenderer.Overlays.cs:65`
`gc.DrawText(..., "serif", ...)`）が、**LP は Emmentaler の fetaText グリフ**。
つまり**確保する幅・高さは LP のグリフ、描くのは別のフォント**という状態になった。
serif の `f` は上 ~1.5 なので、LP なら音符の 0.6 下に来るインク上端が **~1.0 下**になる
＝ **0.4 ss ほど余白が増えて見える**。⚠️ **これはこの変更が作った乖離ではなく、
この変更が可視化した乖離**（旧コードは名目 1.2 が偶然 serif に近かった）。
**直すなら描画側を feta グリフへ**——グリフは ASCII コードポイント（`f` = U+0066）に
入っているので、Emmentaler で `f` と描けばそのまま出る。X も同時に閉じられる
（`DynamicHalfWidth = 0.75` / `dynamicWidth = 1.3` も名目のまま）。**判断待ち。**

#### ✅ ここで「残した同型の欠陥」と書いた `TupletBracketEngraver` は `caa0f239` で閉じた

**幻の符尾は 3 箇所とも閉じた**（`89aaa29f` `26afa9fe` `caa0f239`）。予告どおり門の追加では済まず、
**位置の集約 → encompass point の集約**への作り替えになった。§1 冒頭の記録を読むこと。

---

### ✅ 決定: **`SystemBreaker` の再入可能化は入れない**（2026-07-22・蒸し返さないこと）

ページブレーカーの LP 乖離は **5 点とも閉じた**（`113fdeda` で 3 点／`850c7d98` で orphan
penalty／`fc0feb20` で overfull の却下）。台帳の `page.tight.*` は 2 点とも exact。

**残るのは構造差だけで、それは入れないと決めた。**
LP は `Optimal_page_breaking::solve` が**システム数を理想値から下へ掃引**し、各 count の全
line-division 構成でページ配置を解いて **demerit の argmin** を取る
（LILYPOND-REF: `lily/optimal-page-breaking.cc:139-173`、設計は `page-breaking.cc:75-101` に明記）。
つまり **LP ではページブレーカーが行分割を選ぶ**。実測（40 小節・6 システム、単位 ss）:

| 紙面 | LP | Lily# |
|---|---|---|
| 〜75 | 2 ページ 5+1（6 システム） | 2 ページ 5+1（〜76） |
| 76・77 | **1 ページ 5 システム**＝行分割を組み直した | （到達不能） |
| 78〜 | 1 ページ 6 システム | 1 ページ 6 システム（77〜） |

**見送った理由は性能ではなく F3 の健全性論拠が壊れること。**
`IncrementalCompiler.cs:37-42` は tier-1 skip を
「break 解は **per-measure spring ベクタ＋行頭 prefix 幅＋紙面の幅** の純関数」と根拠づけている。
**全部が横方向**。ページが行分割を選ぶと break 解は**縦の関数**にもなり
（紙面高さ・余白・スカイライン・hara-kiri 可視性・縦 spacing spec）、
それらは **break の下流で計算される**＝gate を計算するのに gate が守る結果が要る＝**循環**。
実害: 今日は「行分割を変えないと証明できる」編集（縦 extent だけ動かす＝高い音符・臨時記号・
強弱・スラー）が、**行分割を変えうる**ようになる。

⚠️ **緑ノードは無傷**（幾何を持たない設計は変わらない）。壊れるのは**レイアウト層の gate**。混同しない。

性能面: 候補ごとに system 境界が動くので `SystemLayoutCache` がほぼ全滅＝実質「数回のフルレイアウト」。
**安い部分は既にある**（`KnuthPlassBreaker.FindBreaksByLineCount` ＝ LP の `Constrained_breaking`
と同じ `dp[j,k]`。今は `looseness` 専用）ので、高いのは常に下流の再実行。

**判断し直すときの順序**（この順でないと始めないこと）:
1. **先に頻度を測る**（コード変更ゼロ）。既存 fixture ＋ showcase を LP に通し、
   **LP が選んだシステム数が Lily# と食い違う譜面を数える**。ゼロに近ければ議論は終わり
2. 有意なら**オプション分離**（CLI/PDF は LP 忠実・プレビューは 1-pass）。
   ⚠️ 二重実装＝§5.2.1② の罠そのものなので、**「両者が一致する」不変条件テストとセットでのみ**

**島1（`StaffLayout.Y` の Y-up 格納）も `ff64f38e` で完遂した。** §3B ① を参照。

---

### 同日の前セッション（2026-07-22）— **島1 とページブレーカーを閉じ、強弱に点を届かせた**

⚠️ **docs コミット（`51f229db` `26e44606` `453776a0` `dc28098c` `5f82aaf5` `ba728f31`
`3371d4e8`）は §1 の書き換えと訂正。下表は実質のあるものだけを挙げる。**

| commit | 内容 |
|---|---|
| `ff64f38e` | **`StaffLayout.Y` / `StaffGroupLayout.Y` / `BraceTop`/`BraceBottom` を Y-up 格納へ**（島1 の atomic flip）。3 ディスパッチャ＋7 積み上げ地点＋15 消費側。**byte 不変・snapshot 0 件**。フレームを直接固定する単体テスト 6 件を新設 |
| `920cf4dc` | **ページブレーカーの点を 4 つ起票**。A4 では捕まらないと判明し、**紙面を縮めた probe T** を新設。出力不変 |
| `113fdeda` | **ブレーカーの LP 乖離 3 点を字面移植**。`page.tight.page-count` −1 → 0。**snapshot 1 件再ベース**（分類済） |
| `850c7d98` | **orphan penalty を LP の条件へ**（第4の乖離）。`page.tight.systems-on-first-page` −1 → 0。**snapshot 0 件** |
| `fc0feb20` | **overfull を却下せずクランプ**（LP 準拠）。`test/tab-percent-repeat` が**紙面外描画から 2 ページへ**。**snapshot 1 件再ベース**（分類・承認済） |
| `19e06be1` | **DynamicEngraver に届く点を起票**（probe D、第2声部で符尾方向を強制）。**符号が逆の欠陥 2 つ**を検出。出力不変 |
| `7817c543` | **LP の dynamic 実測**（`glyph-skyline.ly` に book 追加）。descent 2 つの答えは「どちらでもない」と確定。出力不変 |

**テスト 3155 → 3167 passed、0 failed / 3 skipped。** Core 0 warn / 0 err。
**LP 忠実度 28/36 exact, total |residual| = 1.890701 ss（32 distances）＋ counts 4/4。**
⚠️ **ss 合計が 0.023777 → 1.890701 に増えたのは悪化ではない**——probe D が
**誰も測っていなかった 1.87 ss の乖離**を可視化した分。§5.2.1④ と同じ形（点集合が違う）。

#### ★ 教訓: **予測が外れたときこそ、その差が 2 つ目の欠陥**

probe D の予測 +2.955 に対し実測 +1.866924。**符号は的中して magnitude が外れた**とき、
差の 1.088076 は誤差ではなく**符号が逆の別の欠陥**だった。
⚠️ **もし予測を書かずに測っていたら、幻の符尾を直して residual −1.088 を見て
「絶対値が減った＝改善」と読んでいた。** 予測は当てるためではなく、
**外れ方から2つ目を見つけるため**にある。

#### ★ 教訓: **「到達しない」と書いた見立ては、フィクスチャで裏取りする**

`fc0feb20` の欠陥を最初に見つけたとき「小さい紙面を所有するフィクスチャでしか到達しない」と
**書いて残した**。直してみたら `test/tab-percent-repeat` が実際に踏んでおり、
**楽譜の半分以上が紙面外に描かれたまま出荷されていた**（viewBox 169.01 に対し Y=325.58）。
⚠️ **snapshot は「前回の自分」との比較なので、紙面外への描画を何度でも承認できる。**
到達性を主張するなら、コーパスを実際に走らせて数えること。

#### ★ 教訓: **`LILYPOND-REF` は「値の出典」であって「式の出典」とは限らない**

第4の乖離は算術ではなかった。**orphan penalty (100000) を LP に無い条件で課していた**——
Lily# は「最終ページが 1 システム」に課しており、それは**LP 自身の答え（5+1）そのものの形**。
force² の demerit が約 0.001 なので、この 1 つが全ページ分割を単独で決めていた。

LP の実際の規則は **markup 段落の泣き別れ**（`page-spacing.cc:375-383`、
`last_markup_line_`/`first_markup_line_` は markup の Prob 由来で、音楽システムの
`Line_details` は false 固定＝**音楽では絶対に発火しない**）。
旧コードは `page-breaking.cc:269` を引いていたが、そこは**値を読む行**で適用箇所ではない。
⚠️ **§5.2.1① の「REF があっても式が一致しているとは限らない」の実例。REF を見たら、
その行が『値』なのか『式』なのかを確かめる。**

#### ★ 教訓: **A4 では見えない量がある——regime を開くのはコーパスの仕事**

ページあたり本数の点を A4（book J）に足したら**最初から exact だった**。原因を測ったら、
**A4 では本数を容量が決めていない**——第1システムを 4 オクターブ（8 ledger 段）高くしても
LP は 13 のまま。ブレーカーは各候補ページが解く **force** から選んでおり、rod が天井に
当たっているのではない。**紙面を 70 ss に縮めて初めて force が効き**、LP 5+1 vs Lily# 4+2 が出た。
⚠️ **点が exact でも「その量が正しい」とは限らない。その regime に入っていないだけのことがある。**

#### ★ 教訓: **二重実装は、片方だけ直ると生き残る**

`cfdf85b4` が鎖から廃止した `Stretchability/60` は**ブレーカーに生き残っていた**——しかも
LP のブレーカーは stretchability を使わない（`inverse_hooke_ = full_height() + space`）。
§5.2.1② が言う「複製は移植が半分しか当たらない場所」の実例が、まさにその警告の対象で起きていた。

#### ★ 教訓: **byte 不変はこの種の移行の証拠にならない**（島1）

島1 の**成果は seam に出た**: `FindStaffYInSystem` が `system.Y - offsetDown` から
**`system.Y + staff.Y`**（LP 自身がやる素の和）になり、system 原点と staff 原点の間から
反射が消えた。`LayoutUtilities` では **`StaffOffsetInSystemUp` が primitive**（`staff.Y` を
そのまま返す）で `Down` がその否定＝**否定が「もう格納の姿ではない側」へ移った**。

その移行の検証について——生産側と消費側が**一緒に符号を反転すると打ち消し合う**ので、snapshot オラクルは
「緑だがフレームが逆」を素通しする。だから `StaffLayoutFrameTests` は**格納値そのもの**を
主張する — `staves[0].Y == 0` / `staves[i].Y < 0` かつ上の譜の下端をクリア /
`BraceTop > BraceBottom` / `TotalHeight > 0` / `Height >= 0`（長さは正のまま）/
2 つの accessor が厳密な反射で `system.Y + staff.Y` に合成される。
**3 つの overload（素・hara-kiri・skyline）は独立に積むので別々に固定した。**
⚠️ **「修正前なら落ちる」は実証済み**（`LilySharp.Core` だけ stash して 6/6 fail）。
§5.4 の原則をこの形で満たすこと——**先に測れないなら、格納値を主張するテストで代替する。**

#### ★ device 島の縁では「1 回だけ反射する」

`staffYByIndex`（`LayoutEngine`）と `staffYBySystem`（`LayoutEngine` / `OutsideStaffStacker`）は
下流の注釈エングレーバが**下向き offset を期待している**テーブルなので、島の縁で `-st.Y` と
**1 回だけ**反射させ、flip を下流へ伝播させなかった。`StaffOffsetInSystemDown` を残したのと
同じ理屈（§3B）。**反射を内側へ押し込まない。**

⚠️ **セッション中にユーザーが `fix/note-bang-diagnostic` を master に取り込んだ**
（`a994f418` `1ff10e45` `7d5255cb`）。**テスト数の基準が 3142 → 3155 に動いた**ので、
開始時に読んだ数と突き合わせるときは注意。予告どおり**1 ファイルも衝突しなかった**。

---

### 前セッション（2026-07-22）でやったこと — **譜間を測れるようにして、2 つ閉じた**

| commit | 内容 |
|---|---|
| `b3cfb119` | **2 段譜の台帳点を 2 つ起票**（probe P/Q）。出力不変。**予測を先に書いた** |
| `89aaa29f` | **幻の符尾を止めた**。`+1.450000 → −0.050000`。snapshot 3 件 |
| `854a0e95` | **五線をインクで種にした**（±2.0 → `staffHeight/2 + StaffLineThickness/2`）。`−0.050000 → 0` ×2。snapshot 14 件 |
| `28048fd8` | §1 書き換え |
| `7f2f8ff8` | **`StaffOffsetInSystemDown` 10 箇所を全数調査**。本物の移行は 2 箇所だけと判明し、それを実施（byte 不変・snapshot 0 件）。残り 8 箇所は**意図的な device 境界**＝`Down` は消さないのが正解、と shim の remark に記録 |
| `500627c9` | **決定 2 件を記録**（糖衣 `c?`/`c??` 不採用＋記号ルール、島1 の再スコープ）。docs のみ |

**台帳 22/29 → 24/31 exact、total |residual| = 0.023777 ss（不変）。** 新しい 2 点が両方 exact。

#### ★ 教訓: **対で足したから 2 つ目の欠陥が出た**

P（上の譜から下向きに出っ張る）と Q（下の譜から上向きに出っ張る）は**同じ算術の鏡像**なので
LP はどちらも `9.595000` を返し、**残差も同じ値になるはずだった**。ならなかった
（−0.050000 と **+1.450000**）。その差が**幻の符尾**:

- `SkylineBuilder.AddNoteBoxToSkylines` が**全ての符頭に** 3.5 の符尾を生やしていた。
  **コメントは「全音符には符尾が無い」と書いてあるのに、コードに音価の判定が無かった。**
- 描画側は `noteValue >= 2` で正しく分岐している（`SharedRenderer.Noteheads.cs`）。
  だから**全音符は符尾なしで描かれ、符尾があるものとして間隔が取られていた**。
- LILYPOND-REF: `lily/stem.cc` `Stem::is_normal_stem`（duration-log >= 1 のみ）。

⚠️ **片側だけ足していたら、−0.050000 が「予測どおり」に見えて終わっていた。**
README §「片側しか測っていない点は…」の縦版。**両側を足す。**

#### ★ 2 段譜の点は「素の 2 段譜」では作れない（設計の肝・再掲用）

LP は隣接する譜を `max(skyline距離 + padding, minimum, basic)` に置く
（`align-interface.cc:228-238`、StaffGrouper の 9 / 7 / 1）。**両側とも五線なら
2.05 + 2.05 + 1 = 5.1 で basic 9 が勝ち、五線の extent は出力に一切現れない。**
だから probe は **binding する X で片側だけが突出物**である必要がある
（P = 高音部譜の `d` が中央線の 6 下＋符頭 0.545 ↔ 低音部譜は自分の中央線）。
`staff-refpoint-extent` は system 内の全 staff refpoint の区間（`lily/system.cc:705-717`）
なので、**2 段 1 system なら `StaffGap()` がそのまま譜間距離**。実装追加は不要だった。

#### 参考: 縦の残差 4 点（合計 0.001365 ss）は原因を測って確定済み

`probes/glyph-skyline.ly` で grob に stencil と skyline を同時に聞いた結果:

| grob | `ext`（stencil） | `vertical-skylines` | Lily# |
|---|---|---|---|
| Clef G | (−2.550 . 4.800) | **(−2.540 . 4.776)** | bbox の −2.550 を使用＝**0.010 深い** |
| NoteHead | (−0.545 . 0.545) | (−0.545 . 0.545) | 一致（`22120764` が桁まで閉じた理由） |
| StaffSymbol | (−2.05 . 2.05) | (−2.05 . 2.05) | ✅ 一致（`854a0e95` で線のインクへ） |

**LP はグリフの skyline を stencil の描画プリミティブから作る**（`stencil-integral.cc`）ので、
輪郭が bbox の角から離れるグリフ（clef）だけ skyline が浅くなる。符頭は輪郭が bbox に接する
ので一致する。⚠️ **閉じるには輪郭ベースの skyline 生成が要る**
（⚠️ **「C# 側に OTF パーサは無い」と長く書かれてきたが、これは誤り**。`SkiaSharp` は
`LilySharp.Core.csproj:31-35` の依存で、`PngDrawingContext` が `SKTypeface.FromFile` /
`FromFamilyName` / `MeasureText` を**本番で使っている**。typeface のロードもグリフ輪郭も取れる。
実際に無いのは**レイアウト層がフォントに触る配線**の方——下記のフォント metric の項を読むこと）
（`ec7a2254` が LILC を選んだのと同じ制約）。**実測値を定数で埋めない**こと。

#### ✅ `StaffSymbol` の ±2.05 は閉じた（`854a0e95`）— **保留した判断が正しかった**

前セッションで一度直して**出荷せず戻した**のは「14 fixture が動くのに台帳点が 1 つも動かない」
＝改善かどうかを誰も判定できないため。**先に点を作る**という判断が正しく、作ってみたら
**2 つ目の欠陥（幻の符尾）まで出た**（§1 冒頭）。前セッションの実測「14 件・構造保存・
Y は −0.05〜+0.15」は今回の実測（14 件・X 完全不動・Y +0.03〜+0.15 と −0.05 が 1 件）と一致。

定数ではなく**導出形**で入れた: `_staffHeight / 2 + StaffLineThickness / 2`（§5.2.1⑤）。
`SeedStaffSymbol`（譜間用）と `SeedSystemStaffSymbol`（ページ用）の**両方**。

#### ✅ clef をスカイラインに入れた（`90efec02`）— **本体は padding 4 倍の方だった**

LP の clef は staff の VerticalAxisGroup スカイラインに入る**内部 grob**（`axis-group-interface.cc:914-940`）で、
素のスコアでは**上下ともインクの極値**（refpoint から下 3.550・上 3.800）。Lily# は種にしていなかった。

⚠️ **ただし clef 単独では直らず、一度差し戻した**（`c83a0551`）。狙った 2 点は閉じるのに
`system.natural-distance` が **+1.110000** に後退したため。**真因は別の欠陥**だった:

**`LayoutEngine` の単一ページ経路が system 間 padding に `SystemSpacing * 0.5 = 4` を使っていた。
LP は 1**（`paper-defaults-init.ly:62-65`・`page-layout-problem.cc:625-632`）。**4 倍。**
スカイラインが薄いうちは ink 項が basic-distance 12 に届かず**見えなかった**。clef が入って
ink が 9.110000（clef の下 5.550 と次 system の小節番号 3.560 が同じ左端で向き合う）に達した
瞬間に `9.11 + 4` が binding して露見した（LP なら `9.11 + 1` で効かない）。
同経路を `PageLayouter` の鎖と同じ `max(basic, max(minimum, ink + padding))` に揃えて解決。

⚠️ **教訓: 「新しい種を入れたら縦が広がった」を種のせいにしない。** 種は既存の欠陥を
**可視化しただけ**だった。LP が同じ音楽で動かないなら、動いた側が間違っている。

#### ✅ 決着: 休符の実インク化は**やらなくてよい**（測って否定した）

`22120764` の続きとして「休符も名目 1.0 → `GetRestBBox`」を予定していたが、**実測で棄却**。
128分休符（グリフ下端は中央線の 3.05 下）を敷き詰めた probe と、素の音符の probe が
**LP で 1 ビット違わぬ同じ値**（11.716074 / 12.255229）を返した。休符は中央線に座るので
**縦インクが極値になることが無く**、スペーシングに効かない。箱が名目なのは事実だが**不活性**。

---

### ⚠️ このセッションで踏んだ測定の罠（同じ手を繰り返さないこと）

- **要約された数字から逆算しない。** 「残差 0.044994＝五線の線幅の半分」と一度結論したが、
  これは**丸めた gap から算術した私の誤り**。probe は元から**全 system の生データ**を出しており
  （要約しているのは測定スクリプトの方）、直読みすれば 3.550000000 だった。
- **stencil の extent と skyline は別物。** LP の spring の床が使うのは **skyline**
  （3.540〜3.545）で、stencil の extent（3.550）ではない。
  この2つを突き合わせて「未知の 0.005」を作り出していた。**どちらの量かを必ず言うこと。**
- **probe が何を測っているか確かめてから信じる。** 休符 probe は休符を測っておらず、
  中央線上の音符 probe は**下向き符尾**が clef を隠していた。`a`（中央線の1段下）で初めて
  clef が単離できた。**両方とも罠として probe のヘッダに書いてある。**

---

**HEAD は docs コミット・origin より 8 ahead で未 push**（`git rev-list --count origin/master..master`
で裏取り＝開始時 3 ＋ 当セッションの `87bcde22`/`5ecabdff`/`f093583e`＋docs 2 件、
及びユーザーが並行投入した `b9837fcf`「editor: ')' 打鍵でスラー終端移動」）。旧記述の「100 ahead」は
`git rev-list` と食い違う stale 値だったので撤去。push はユーザー判断・コミットは可。
⚠️ **push は明示的に「まだしないで」と言われている**（2026-07-22）。解除まで push しないこと。
⚠️ **未 push には大規模 snapshot 再ベースが多数**（フォント移行 **194 件**・TimeSignature **188 件**
・フォント差し替え 186/192・タプレット 9/14 件・強弱 16 件ほか）**が含まれる**（`87bcde22`・`f093583e`
は出力不変＝新規 snapshot 1 件のみ追加）。
別ブランチ `fix/vscode-extension`（`7291531a` から）と `fix/note-bang-diagnostic` は
**どちらも master に取り込み済み**（ユーザー）。**走っている別ブランチはもう無い。**
**テスト 0 failed / 3200 passed / 3 skipped。** Core build 0 warn / 0 err。
**LP 忠実度 29/42 exact, total |residual| = 0.279202 ss over 38 distances ＋ counts 4/4**
（**2.26.0 基準**。X 22点＋Y 7点＋譜間 **5点**＋**スラー譜間 2点**＋**system 間タプレット 2点**＋
**ページ本数 4点**。スラー 2 点は **−0.130000**＝峰高/端点の定数 1 つ・§1 冒頭）。
⚠️ **本数の点は距離ではないので ss の総和に入れない**（`unit` フィールドで分離。`page.height` を
台帳から落としたのと同じ理由——1 system を 0.019202 ss に足すと指標が意味を失う）。
**タプレット 4 点は全て +0.0000208**（`caa0f239` `075277ff` `b36db266`。Pango 量子化の半分）。
**`barline.next.time-change-to-notehead` は +4.3e-7（`033c0df8`。Pango 量子化を字面移植して閉じた）。**
⚠️ **途中 2.214721 まで増えたのは悪化ではなく、誰も測っていなかった乖離の可視化**だった（§5.2.1④）。
起票 −1.700225 ×2・−1.295225 ×2 → 予約と符尾で −0.547717 ×4 → 実フォント計測で **+0.0000208 ×4**。
**譜間の残り 3 点は 2 点 exact**（`854a0e95`）**＋ −0.000076**（`26afa9fe`。Pango 量子化）。
**縦 7 点の合計は 0.001365**（4 点が上記の skyline sliver、3 点は exact）。
X 3 点は `e38a76bf` から不変。
**作業ツリーはクリーン**（未追跡の旧 `HANDOFF-*.md` 14個 ＋ `demo-lp-compat-features.lys`
＋ `audit/scripts/__pycache__/` を除く。§8・§7-8）。

⚠️ **2026-07-22 に履歴が書き換えられた（コミット日時の変更）。** メッセージは不変だが **SHA は全部変わった**。
本ファイル・`COORDINATE_AUDIT.md`・`audit/lp-geometry/README.md` の参照は張り替え済み（28件、到達性を検証）。
**同じことが起きたら、subject で新旧を突き合わせて張り替えること** — `git log -1 --format=%s <旧SHA>`
は書き換え後も dangling オブジェクトとして引けるので、そこから `git log --format='%h %s' master` を検索する。
⚠️ `git cat-file -t` では判定できない（旧コミットは gc されるまで残る）。**`git merge-base --is-ancestor` を使う。**

### ✅ LP の「正」は **2.26.0** に確定（2026-07-22・版分岐は解決済み）

src・binary とも 2.26.0 で揃った。**もう版の混成は無い。**

- `C:\MyProj\lilypond-src` は **`v2.26.0`（`3596756be0`）を detached HEAD で checkout**。
  旧 HEAD `bc68038f76`（2.25.35 devel）はその直接の祖先で 128 commit 前だった
- 実測 binary は **`C:\bin\lilypond-2.26.0\bin\lilypond.exe`**（`audit/lp-geometry/*.ps1` の既定値も更新済）
- ⚠️ **PATH 上の `python` は今も 2.24.4 同梱のもの**で pip も fontTools も無い。
  `audit/scripts/*.py` は **`py -3.13`** で起動すること
- ⚠️ `C:\bin\lilypond-2.24.4` は残っている。**比較目的以外で使わない**

`ly/paper-defaults-init.ly` と `scm/define-grobs.scm` の 2.25.35→2.26.0 差分は**著作権年のみ**なので、
旧記述の「src 2.25.35（正）」列の値がそのまま 2.26.0 の値。**2.24.4 との差は下の §2⑧ の表に集約した。**

### ⚠️ 2.26.0 は Emmentaler を作り直している（**このセッションの最大の発見**）

**① PUA コードポイントは版をまたいで安定しない。** 2.26.0 はグリフを 34 個挿入したため、
Lily# がハードコードしていた 115 定数のうち **73 個の割り当てがずれた**
（`U+E085` は `clefs.G` でなく `clefs.varC`、`U+E0EA` は `noteheads.s2` でなく `flags.stackedu7`）。
LP は常に feta **名**で引く（`lily/clef.cc:29-52`、`lily/note-head.cc`）。
→ `EmmentalerGlyphs.Generated.cs` を **名前引きで生成**するようにした（`a2ceb2f0`）。
**名前がフォントに無ければ生成器が exit 1 で落ちる**のが唯一のガード。実行時にフォントを読む配線が無いので
テストでは固定できていない。**生成器を CI で回して差分ゼロを assert するのが残件。**

**② LILC テーブルが zlib 圧縮になった**（114580 → 6175 バイト。otf が半分になる主因）。
`lily/open-type-font.cc:78-123` が透過的に inflate し、非圧縮ならそのまま読む。
⚠️ **`Extract-EmmentalerMetrics.py` が inflate しないと LILC を 0 件と判定してアウトライン
fallback に落ちる** — それは `ec7a2254` が「非 LP 方式」として捨てた経路。対応済み。

**③ LILC の bbox が design 単位で小数3桁に丸められた**（`noteheads.s2` 6.52106 → 6.521）。
共有 628 グリフ中 566 個が最大 7e-5 ss 動く。**台帳 22 点中 10 点の LP 実測値がこれで動いた。**

### 残る残差は **22点中3点 0.022412 ss**

| 残差 | 点数 | 原因 |
|---|---|---|
| −0.017672 | 1 | **水平スカイライン項**（未移植）。`barline.next.key-change-to-notehead`（♮）。LP は臨時記号の右スカイラインを符頭のスカイラインと測る（`accidental-placement.cc:412`）が Lily# は box で測る |
| −0.004735 | 1 | **TimeSignature grob 幅** 1.600000 / LP 1.604735。原因特定済＝LP は markup を組むので**テキストレイアウト経路**が幅を決める（下の ⚠️）。もう OPEN ではない |
| +0.000005449 | 1 | `barline.next.down-stems-after-clef`。**2.26.0 のフォントで新規発生・`OPEN:`**。LP の光学補正が 0.189365 → 0.189360 と動いたのに Lily# は 0.189365449 のまま。丸めのスケール内だが、どのメトリクスが `/7` を通って 5.4e-6 を運ぶかは**未特定** |

⚠️ **♯ の 2 点（`barline.next.accidental-to-notehead` / `midmeasure.key-cancel.key-to-next-note`）が
exact 化したのは「直った」からではない。** 2.26.0 で `accidentals.sharp` の右端が 1.100000
ちょうどになり、スカイライン項（−0.000010）が**見えなくなった**だけ。水平スカイラインは今も未移植で、
**それを測っている点は ♮ の 1 点だけになった**。台帳の `why` にも同じことを書いてある。
（2 点が同時に、どちらも 0.000010 で閉じたことは「同一原因」という以前の読みの裏取りにはなった。）

**X 軸の「定数を1つ直せば閉じる」ネタは尽きた。** 残る3点のうち2点は**Lily# に無いパイプライン**が
要り（水平スカイライン1点／テキストレイアウト1点）、1点は `OPEN:`（5.4e-6）。
§2⑧ の余白4定数は `e38a76bf` で、Y 台帳の起票は `0c0d8f38` で、
`page.first-staff-refpoint` の −3.000000 は `b94487ad`＋`1dfb62d7` で閉じた。
（**伸長 regime の点も済み**——`6ffbe7bd` で起票、`cfdf85b4` で鎖として移植。現在
`page.stretched.first-staff-refpoint` −0.000042 / `system.stretched-distance` −0.000414。
**次の一手は §1 の冒頭を見ること。**）

⚠️ **TimeSignature 幅 −0.004735 は「定数を直す」問題ではない**（2026-07-22 に実測で確定）。
`ly:time-signature::print` は **markup を組んで `grob-interpret-markup` に渡す**
（`scm/time-signature.scm:18-29`）ので、幅は**テキストレイアウト経路**が決める。同一 grob 上で:

| 経路 | 桁4 |
|---|---|
| `ly:font-get-glyph "fattened.four"`（音楽フォント＝このコーパスが使う経路） | **1.600000** |
| `(markup #:number "4")`（print が実際に通る経路） | **1.604735** |

**Lily# の 1.600000 は誤りではなく、LP 自身の音楽フォント経路の値と一致している。**
10桁すべてについて棄却済み: LILC bbox / 生 outline / advance / plain 族(U+0030-0039) /
fattened 族(U+E0B4-U+E0BF) / フォントビルド差（LP 同梱と repo は別サイズだが桁 metric は同一）。
markup 値は 0,2,8 / 3,5,7 / 6,9 が同値になるが、**フォント内のどの metric もその group を作らない**。
2桁文字列も1桁幅の和にならない（±0.102430）。**閉じるにはテキスト経路の metric が要る。**
⚠️ 以前ここに書いた「advance のまま＝§2④ の取りこぼし」という見立ては**誤りなので採用しない**。

### 直近セッション（2026-07-22 最終）でやったこと — **先頭 system・鎖・プレビューのフォント**

| commit | 内容 |
|---|---|
| `6ffbe7bd` | **伸長 regime を起票**（probe W ＝ V と同じ音楽を 150 小節）。着手前に予測を書いてから測定: `page.stretched.first-staff-refpoint` **−0.025482000（予測どおり）** / `system.stretched-distance` **+0.089206333**（予測は向きのみ的中） |
| `cfdf85b4` | **ページを LP の spring 鎖として解く**（`Page_layout_problem` ＋ `Simple_spacer` の字面移植）。**0.114688 → 0.004090**。転記誤り2件を同時に廃止（下記）。**snapshot 2 件再ベース** |
| `a7b96569` | 生 dump を直読みして**残差の原因の記述を訂正**（コード変更なし）。要約からの逆算が誤りだった |
| `22120764` | **縦スカイラインを符頭の実インク（LILC ±0.545）へ** — `ec7a2254` の縦版。**0.004090 → 0**、**縦は 5/5 exact**。**snapshot 80 件再ベース**（全件で行数・グリフ数一致＝構造保存、Y は −0.05〜+1.80） |
| `769529ed` | **clef がスカイラインに無いことを測れる probe S を起票**（出力不変）。予測を先に書いて桁まで的中。休符案は実測で棄却（上記） |
| `058ab13a` | `VerticalSkyline.Distance` の契約を単体テストで固定（**スカイライン算術は無罪**と確定） |
| `0b30f53d` | **座標系監査**を `COORDINATE_AUDIT.md` §2.1 へ。`staffMiddleY` が 9 ファイルで逆向きの 2 意味だった |
| `a8c75679` | **19 シンボルを `Down` 付きへ改名**（ユーザーが MSVS で実施）。純リネーム |
| `39da7084` | **Y-up shim を開設**（`StaffOffsetInSystemUp`）＋ `OutsideStaffStacker` を移設。**snapshot 0 件** |
| `511ab68c` | **`SkylineBuilder` を Y-up へ**（`ToSystemUp` が単なる加算に）。**snapshot 0 件** |
| `90efec02` | **clef を種に＋単一ページ経路の padding を LP の 1 へ**。0.162412 → **0.023777 ss**。**snapshot 82 件再ベース**（構造保存・縦のみ最大 −6.88） |
| `b94487ad` | **先頭 system の配置を refpoint フレームへ**（§2⑧ の順序①）。`CalculateFirstStaffRefpoint` を新設し、`CalculateFirstSystemY` を **halfStaff 変換の唯一の seam** に。3 呼び出し元とも spec を渡す。**出力不変・snapshot 0 件** |
| `1dfb62d7` | **先頭 system を top-system spring に載せた**（順序②）。`max(basic-distance, ink + padding)`。**`page.first-staff-refpoint` −3.000000 → 0**、22/25 exact・0.022412 ss。**snapshot 2 件再ベース**（`programmatic/hara-kiri{,-paged}`、全て +3.00） |
| `99baed0f` | **`Deploy-Lsp.ps1` が `media/` を配っていなかった**のを修正。プレビューの符頭が三角になっていた原因（下記） |

⚠️ **「全 snapshot が動く」という §2⑧ の予測は外れた。** `max()` は**五線上のインクが薄いときだけ**効くので、
**190 件の `.lys` snapshot は 1 バイトも動かず**、動いたのは programmatic 2 件だけだった。
これは「無害の証拠」ではなく**フィクスチャの穴**（先頭 system が疎なフィクスチャが1つも無い）。
台帳の点が LP に対して固定しているので優先度は低いが、**視覚フィクスチャは未追加**。

### ⚠️ プレビューの符頭が三角になるのはフォントの配り忘れ（`99baed0f` で解決）

`tools/Deploy-Lsp.ps1` は `server/` `syntaxes/` `out/` `package.json` だけを配り、
**`media/`（webview 自身の Emmentaler）を配っていなかった**。`070f1e21` で 2.26.0 に差し替えた後、
サーバは新コードポイントを出すのにプレビューは **2.24.4 の woff2 を掴んだまま**になり、
**115 定数中 73 個の割り当てズレ**がそのまま別グリフとして描かれていた（符頭が三角）。
**レイアウトのバグではない。** 実測: repo 52116 バイト `13D75317…` / 配布済み 50248 バイト `D207674F…`。
⚠️ 同じ症状を見たら**まずインストール済み拡張の `media/fonts/*.woff2` をハッシュで照合する**こと。
CLI の PNG は `LilySharp.Core\Fonts` を埋め込むので**正常に見える＝切り分けに使える**。

### その前のセッション（2026-07-22）でやったこと — **2.26.0 への移行**

| commit | 内容 |
|---|---|
| `a2ceb2f0` | **グリフ表を feta 名引きに**（`EmmentalerGlyphs.Generated.cs` ＋ `Extract-EmmentalerGlyphs.py`）。**出力不変・snapshot 0 件**。副産物で既存の誤りを 5 件発見して修正（下記） |
| `070f1e21` | **フォントと台帳を 2.26.0 へ**。otf 2 個＋派生 woff/woff2 4 個を差し替え、生成器2本を zlib＋名前引きに、弓記号の方向ペアを移植、`lp-geometry.json` を 2.26.0 実測へ、テスト7ファイルの旧 codepoint 直書き 31 箇所を定数参照へ。**snapshot 186 件再ベース** |
| `7291531a` | **3 ページのフィクスチャ `test/multi-page-vertical`**。§2⑧ が警告していた「複数ページを踏む fixture がゼロ」を埋めた |
| `e38a76bf` | **紙面 5 定数を LP の単位で読み直した**（§2⑧ 完了）。**snapshot 192 件再ベース** |
| `0c0d8f38` | **台帳を Y に開いた**（3 点）。`RenderedGeometry` を全ページ対応に。出力不変 |

**`a2ceb2f0` で見つかった既存バグ 5 件**（現行フォントに対しても誤っていた）:

| 定数 | 実際に描かれていたもの | 修正後 |
|---|---|---|
| `Fermata{Short,Long}{Above,Below}` | **Henze フェルマータ** | 通常の `scripts.u/dshortfermata`・`u/dlongfermata` |
| `ArticThumb` | **`scripts.snappizzicato`** | `scripts.thumb` |

LP は両者を別の articulation として持つ（`scm/script.scm:356` shortfermata ↔ `:183`
henzeshortfermata、`:220` ↔ `:174`）。**どのフィクスチャも踏んでいないので snapshot は動かない**
＝コーパスはこの修正を見ていない。articulation を次に触るとき fixture を足すこと。

**弓記号は本物の LP 挙動変更**: 2.24.4 は `("downbow" . "downbow")` と上下で同一グリフだったが、
2.26.0 は `(ddownbow . udownbow)` / `(dupbow . uupbow)` に分割（`scm/script.scm:88`・`:453`）。
旧グリフはフォントから消えている。`ArticulationItem` / `ArticulationEngraver` を方向選択に移植した。

⚠️ **snapshot 186 件の再ベースは全行分類してから承認を取った**: 3277 行がコードポイントのみ、
26 行がコードポイント＋座標、108 行が座標のみ、**座標の最大変化は 0.01＝`F2` 丸め 1 単位**。
36 種のコードポイント移動のうち **35 種が両フォントで同一 feta 名**、残る 1 種が上の弓記号の分割。
**記号の同一性は変わっておらず、レイアウトも丸め以上には動いていない。**

**判明した2つの訂正**（どちらも過去の記述が誤り）:

1. **`11.528` は圧縮ではなく基準点の取り違えだった。** `staff-refpoint-extent` は system ごとに
   違う（小節番号を頭上に持つ system は原点がその分上に伸びる）ので、**system 原点間**で
   測ると間隔が一様でも値がばらつく。同じダンプが原点間で 11.528583 / 12.000000、
   **staff refpoint 間では 12.000000 / 12.000000**。LP はそこで圧縮していなかった。
   → **縦は staff refpoint 間で測ること**（§5.3 に追記済）。
2. **`ragged-last-bottom` は「伸ばさない」ではない。** `page-breaking.cc:570-573` は最終ページに
   **直前ページと同じ force** を渡す（`fixed_force_solution(last_page_force)`）。
   `last_page_force` の初期値 0（`:643`）なので**単ページ書籍だけ**自然長。
   Lily# は無条件に force 0 を入れていた＝**最終ページだけ違って見える**というユーザー報告の
   直接原因。`c353bc85` は「1ページに何本入るか」を直したが「最終ページをどう詰めるか」は
   残っていた。

⚠️ **snapshot が 1 件も動かないのは、複数ページを踏む committed フィクスチャが1つも無いから。**
「変更が無害」の証拠ではなく**フィクスチャの穴**。

### その前のセッション（2026-07-22 前半）でやったこと

**§2④' と §2⑥ を完了。§2⑤ は既に完遂済みと判明。** どちらも予測が全点的中した。

| commit | 内容 |
|---|---|
| `94e8996c` | **§2④' 実装**。`AccidentalNoteGap` 0.2 → **0.35**（LP の `padding` 0.2 ＋ `right-padding` 0.15）。**3点とも予測が桁まで的中**。snapshot 22件（全て `test/*`、showcase はゼロ） |
| `8bc90025` | §2⑤ は `098f5279`＋`8448749a`＋`94656b84` で完遂済みと判明。元 handoff を回収して §2⑤⑥⑦ に整理 |
| `a64ffc16` | **§2⑥ 実装**。`fills_measure` を LP の musicality 判定へ。**変更前に点を足して予測 −1.000000 を記録 → 実測 −1.000000000 → 修正後 0**。snapshot 2件 |

**0.338987 → 0.022361 ss、17/21 → 18/22 exact。** ④' の exact 数は予測どおり動かない
（−0.000010 は tolerance 1e-6 より大きい）。⑥ で足した点が exact で着地して +1。

参考: この前の 2026-07-21 セッションは §2①③④ と §2② の掃除を入れた
（`ec10fd4b` 計測ハーネスの stderr 混入修正 / `f4b94f64` 行中変更 item の LP モデル導出 /
`4eb8cf16` §2① / `ec7a2254` §2④ LILC / `d056b5e5` §2③ 境界列 / `2745d603` §2② 掃除）。
**台帳の値自体は全部正しく、壊れていたのは再現手段のほうだった。**

### 進行中で中断しているものは無い

**X 軸は行中・行頭・臨時記号とも完結。**

⚠️ **`total |residual|` の履歴は点集合が違う。同じ集合の中でだけ比較すること。**
15点 4.592405 → 19点 11.435647（`5c4126d6` が行中4点を追加＝それまで測っていなかった発散の可視化）
→ 21点 4.747978（`4eb8cf16`＋MKA 2点）→ 4.738987（`ec7a2254`）→ 0.338987（`d056b5e5`）
→ 21点 0.022361（`94e8996c`）→ 22点 **0.022412**（`070f1e21`）。

⚠️ **`070f1e21` で比較の基準そのものが 2.24.4 → 2.26.0 に変わった。**
点集合は同じ 22 点だが LP 側の実測値が 10 点動いているので、**0.022361 と 0.022412 は
厳密には別物差**（悪化ではない。exact は 18 → 19 に増えている）。
**この行より上の日付が古い記述に出てくる LP 実測値は全部 2.24.4 のもの**なので、
数値を引き写すときは版を確認すること。

---

## 2. 短期ロードマップ（次の数セッション）

優先順。**①②は COORDINATE_AUDIT §4.7 の残り**で、ユーザー合意済みの順序。

### ① ✅ 完了（`4eb8cf16`）— 行中（mid-measure）の変更 item を LP の専用列として価格付け

> **2026-07-21 に再スコープ → 実装。** 旧①は「3つの extent ヘルパの frame ＋ 定数2つ」だった。
> **着手前に測った結果、それでは 4 点は 0 にならないと分かった**（§5.3「変更する前に測る」が
> 効いた例）。導出済みモデルと実測は `COORDINATE_AUDIT.md` **§4.7.2**。
>
> **結果**: 行中4点 6.843242 → 0.000453 ss（`clef→次の音符`は**厳密一致**）。
> 残差は全て `GlyphMetricsGenerated.cs` の4桁丸め（符頭 1.3040 vs LP ink 1.304212 等）。
> **残った未了**: 分配が正しいのは force 0 のみ。LP は2本の spring を独立に伸ばし、
> key/time の右側は**伸びない**（`shrink-space`/`semi-shrink-space`）。これは本物の
> 第2列が要るので③と同じ仕事。`SpacingRules.MidMeasureChangeGaps` に記録してある。
>
> 以下はモデルの記録（③も `d056b5e5` で完了済み）。

**LP は行中の clef/key 変更に non-musical 列を1本立て、左右を別の式で価格付けする**:

| | 式 | 出典 |
|---|---|---|
| 列原点 | 変更グリフの **ink 左端** | 実測 |
| 左 gap | `max(ideal − 列幅, (ideal + min_dist)/2)` ＝実際は常に床側 | `note-spacing.cc:105-107` |
| 右 gap | `ink幅 + space-alist 距離` | `staff-spacing.cc:147-198` |
| 　clef | `next-note` **1.0** | `define-grobs.scm:924` |
| 　key | `next-note` が**無い**ので `first-note` **2.5**（shrink） | `define-grobs.scm:1947` |
| 　time | 同じく `first-note` **2.0**（semi-shrink） | `define-grobs.scm:3948` |
| `min_dist` の左 esw | Clef=既定 0.1 / Key=**0.0** / Time=**0.0** | `define-grobs.scm:1936` / `:3933` |

**MC/MK の左右4点すべて 6 桁一致でモデル確定済み。**

Lily# 側は列が無く、`ChangeItemPrefixWidth`（= W + 2×0.5）を**1本の spring に丸ごと加算**し、
描画側が `列X − (W + 0.5 + 次の臨時記号)` で**ぶら下げる**。実測分解:
`head2→head3 = (1.304 + CalculateLeftExtent(clef) 1.505 + 0.4) + 3.010 + 0.3 = 6.519000`（実測一致）。

→ **frame だけ直すと `+1.119 → +0.612` に減るが、左右の分配は変わらず逆符号のまま。**

**同時に直したもの**: 変更 clef の幅 `FClefAdvance × 0.75 = 2.010` → LP の `clefs.F_change`
ink **2.146680**（現在は `ec7a2254` で生成器から LILC 由来）。

### ② ✅ 完了 — extent ヘルパの中心基準と、①③が殺したシンボルの掃除

**出力は完全に中立**（snapshot 0件、LP 忠実度 17/21・0.338987 のまま）。削除したもの:

| シンボル | 経緯 |
|---|---|
| `SpacingRules.ChangeItemPrefixWidth` | ③が最後の呼び出し元を外した |
| `SharedRenderer.FollowingAccidentalLeftExtent` | ①で臨時記号が rod 経由になった |
| `SpacingRules.CalculateRightExtent` | 元からの②。テストを `CalculateNoteheadRightExtent`（左端基準）へ寄せた |
| `GlyphMetrics.ClefChangePadding` | 上の掃除で参照ゼロに。**そもそも LP の量ではなかった**（0.5 は `right-edge`） |

`CalculateLeftExtent` / `CalculateNoteheadRightExtent` の変更 item 分岐は
**左端基準（0 / 全幅）**へ。

⚠️ **副産物で本物のバグが1つ出た**: `MeasureLayouter.ItemStartingAt` が zero-duration の
変更 item を返しており、**音符列同士の rod が変更グリフから測られていた**。
音符を返すよう直したところ、分岐が到達不能になり、かつ出力が中立に戻った
（つまり従来は rod が binding していなかっただけで、式は間違っていた）。

⚠️ **未検証で残したもの**: `GetItemToBarlineSpace` / `GetBarlineToItemMinimum` の
変更 item エントリ（`1.0 / 1.0 / 0.75`）。「中心基準だから」という根拠は消えたが、
**LP と照合し直していない**。到達経路は「変更 item が小節の最後の timing を共有する」
という **LP に存在しない構図**だけで、fixture も踏まない。

### ③ ✅ 完了（`d056b5e5`）— 行頭 key/time の境界列

モデルは `COORDINATE_AUDIT.md` §4.7.3。**着手前の予測が4点とも桁まで的中**した:

| 台帳キー | 実装前 | 予測 | 実測 |
|---|---|---|---|
| `barline.next.key-change-glyph` | −0.500000 | 0 | **0** |
| `barline.next.time-change-glyph` | −0.250000 | 0 | **0** |
| `barline.next.key-change-to-notehead` | −2.234272 | −0.034272 | **−0.034272** |
| `barline.next.time-change-to-notehead` | −1.454735 | −0.004735 | **−0.004735** |

**4.439007 → 0.039007 ss、exact 15/21 → 17/21。** snapshot 14件。

**未モデル化（意図的）**: LP は key 変更を `KeyCancellation` と `KeySignature` の**2 grob**に分け
間に 0.5 を置くが、Lily# は1つの `KeySignatureChangeItem` に畳んでいる。コーパスは踏まない
（probe K は 0→3 個で cancellation が出ない）。`BoundaryChangePrefix` に記録。

⚠️ **`BoundaryColumn.cs`（clef を bar line の前に置く既存の型）とは別物**。今回入れたのは
`SpacingRules.BoundaryChangePrefix` ＋ `BarlineToFirstColumnSpring` の `last_grob` 切替。
両者の統合は未着手。

### ④' ✅ 完了（`94e8996c`）— 臨時記号 → 符頭の距離

`GlyphMetrics.AccidentalNoteGap` は LP の `padding` 0.2 **だけ**で、`right-padding` 0.15 が
抜けていた（`accidental-placement.cc:397` / `:400`、適用は `:412-416`）。**0.35 に。**
式・摂動法の裏取り・グリフ別のスカイライン項は**その定数の `<remarks>` に全部書いてある**。

| 台帳キー | 実装前 | 予測 | 実測 |
|---|---|---|---|
| `barline.next.accidental-to-notehead`（♯） | −0.149990 | −0.000010 | **+0.000010** |
| `midmeasure.key-cancel.key-to-next-note`（♯） | −0.149990 | −0.000010 | **+0.000010** |
| `barline.next.key-change-to-notehead`（♮） | −0.034272 | −0.017606 | **−0.017606** |

**0.338987 → 0.022361 ss。** 絶対値は3点とも桁まで的中（符号は逆＝0.35 が LP の 0.349990 を
わずかに超える分）。exact 数は予測どおり不変。snapshot 22件。

**残った未了 = スカイライン項そのもの**（意図的に未移植）。LP は `:412` で臨時記号の右
**スカイライン**を符頭のスカイラインと測るので、縦に細いグリフだけ box より外に出る
（♮ +0.017606 / ♯♯ +0.047704 / ♯ −0.000010 / ♭ −0.000004 / ♭♭ −0.001996）。
移植には**水平スカイラインの基盤**が要る（§3B②の島に接続）。

### ④ ✅ 完了（`ec7a2254`）— グリフメトリクスを LILC 由来に

**LP はグリフ bbox を、フォント埋込の `LILC` テーブルから読む**
（`lily/open-type-font.cc:288` `load_scheme_table("LILC")` ＋ `:389-407`。生アウトラインは fallback）。
`GlyphMetricsGenerated.cs` はアウトライン（`BoundsPen`）から取っていた＝**非 LP 方式**で、
これが台帳に残っていた 1e-4〜1e-3 級の残差の**唯一の原因**だった。

入れたもの: 生成器を LILC 優先に／出力を **6桁**に／`ApplyLeftHeadWidth` と
`GetKeySignatureAccidentalWidth` を **advance → ink extent** に／変更 clef も生成器から。

**7点が 0 に**（`barline.prev.whole-note` `.half-note` `barline.next.down-stems-after-clef`
`midmeasure.clef.prev-note-to-clef` `midmeasure.key.prev-note-to-key`
`midmeasure.key.key-to-next-note` `midmeasure.key-cancel.prev-note-to-key`）。
**8/21 → 15/21 exact。** snapshot 184件・承認のうえ再ベース。

⚠️ **踏んだ罠（再発しやすい）**: 生成 bbox から派生する定数を `static readonly` にすると、
**partial クラス間の静的初期化順序は C# で未定義**なので既定値の `BBox`（=0）を読む。
変更グリフの幅が全部 0 になり clef が自分の gap から消えた。**プロパティにすること。**

⚠️ `down-stems-after-clef` が閉じたのは**予測外**。残差 +0.00002 は「符尾の符頭接続オフセット差」
と帰属されていたが、実際は符頭メトリクスだった。**帰属は閉じてみるまで確定しない**例。

### ⑤ ✅ 完了 — MMR run のグルーピング

**「LP は clef があっても run を保つが Lily# は弾く」は既に解消済み。** 3層とも入っている:

| 層 | commit |
|---|---|
| run グルーピング（`IsBreakAlignedChange` に `ClefChangeItem`） | `098f5279` |
| 描画（clef を bar line の**前**へ＝`BoundaryColumn` / `BoundaryClefAllowance`） | `8448749a` |
| 視覚フィクスチャ `test/mmr-clef-change-bound` | `94656b84` |

2026-07-22 に LP 2.24.4 と PNG を並べて再確認済み（単一 "5" ＝ longa 4 ＋ whole 1、bass clef は
bar line の前）。旧 handoff が「描画位置の方針をユーザーに仰げ」としていた論点は**測定で解決済み**:
clef は列の**原点**を左へ動かすだけで bar line は動かさない（bar line 間は clef の有無によらず
14.133856）。だから rod には足さず、`BoundaryClefAllowance` として**前の小節の閉じ側**に付ける。

**元資料 `handoff-2026-07-21-mmr-runs.md` は本項をもって回収完了**（§8）。残っていた未解決は
下の ⑥⑦ に移した。

### ⑥ ✅ 完了（`a64ffc16`）— `fills_measure` の述語を LP に合わせた

`spacing-spanner.cc:446-472` は**列の musicality しか見ない**のに、Lily# は2箇所とも
`NoteItem or ChordItem` に絞っていた＝**全休符の小節に `full-measure-extra-space` 1.0 が
付かなかった**。2箇所を同時に `SpacingRules.IsMusicalColumn` へ。

⚠️ **ここで推論が外れた。** 「musical＝duration を持つ」ではない。`Paper_column::is_musical` は
`shortest-starter-duration` を読む＝**engrave された grob が決める**ので、**skip は非 musical**。
摂動実測（`full-measure-extra-space` → 0、LP 2.24.4）:

| 列 | 差 | |
|---|---|---|
| `r1` | **−1.000000** | 効く |
| `R1` | **−1.000000** | 効く |
| `s1` | 0 | **効かない** |
| 四分音符×4 | 0 | 効かない |

**コーパスは構造的に盲目だった**: スコア F は閉じ側（`barline.prev.whole-rest`）しか測っておらず、
fmes が乗るのは開き側。`barline.next.whole-rest` を足し、**`residual: null` ＋ 予測 −1.000000 を
先に書いてから**実装 → 変更前 −1.000000000 → 変更後 **0**（LP 1.900000 = Lily# 1.900000000）。

**副産物**: `SpacingInvariantTests.FullMeasureRest_CompactsOnCombinedPath` の
`< noteWidth / 2` は**LP 自身が満たしていない**主張だった（LP の `R1` 小節 7.890000 :
四分音符4つ 13.525735 ＝ 0.583）。`* 0.6` に緩め、**移植定数ではなく粗い gate** だと明記。

**残った未了**: `R1` は幅を MMR rod 経由で決めるので、この spring は rod が binding しない
場面でしか効かない。LP の `max()` と全ケースで一致するかは未測定。

### ⑧ Y 軸（ページ縦方向）— 着手済み。**残りは「Lily# は圧縮できない」**

**ユーザー報告「複数ページで最終ページだけ改行幅が狭い」を実測 → 原因はページブレーカーだった。**

LP 2.24.4 実測（`ragged-bottom` ＋ `systems-per-page` を絞り、伸長も圧縮も起きない条件）:
**自然 system 間距離 = 12.000 ちょうど。Lily# の最終ページも 12.000 で厳密一致。**
つまり**最終ページが正しく、それ以外のページが伸びすぎていた**。

`c353bc85` で `tallness_` / `spring_length` / `refpoint_extent_` を字面移植（§2⑥ と同じく
「rod と spring の二重計上」だった）。A4・30 反復のプローブで:

| | 変更前 | 変更後 | LP |
|---|---|---|---|
| ページ1 のシステム数 | 11 | **13** | 14 |
| ページ1 の gap | 14.55 | **12.12** | 11.528 |
| 最終ページの gap | 12.00 | 12.00 | 12.000 |

ページ間の見た目の差 **2.55 ss → 0.12 ss**。

#### ✅ 最終ページの force 継承は完了（`4ac3df8e`）

`page-breaking.cc:570-573` — `ragged-last-bottom` は「伸ばさない」ではなく、最終ページに
**直前ページと同じ force** を渡す。移植して予測どおり **12.000000 → 12.450000**（page 1 と一致）。
単ページ書籍は `lastPageForce` 初期値 0 のまま＝ 12.000000 で不変。両方テストで固定済み。

#### ✅ 紙面・余白の 5 定数は完了（`e38a76bf`）

**原因は単位の取り違えだった。** LP の point は `lily/include/dimensions.hh:27` の
`INCH_TO_PT = 72.270`＝**TeX point**（1/72.27"）で、PostScript の big point（1/72"）ではない
（`:31` に `INCH_TO_BP = 72` が別にある）。既定 staff は 20pt なので
**1 staff space = 5pt = 127/72.27 mm = 1.757299 mm**。168.4 は同じ 5pt を PostScript point で
換算した値だった。全定数は `<mm> × 72.27/127` で導ける（下表の「LP 2.26.0」列と 6 桁一致）。

**実測した影響**（推測でなく）: 192 snapshot 全てで **system 数もグリフ数も不変＝改行は動かなかった**。
グリフの最大移動は **0.70 ss** で、これは上余白の増分 +0.690551 そのもの。
つまり内容が下へ 0.69・右へ 0.036 ずれ、五線が 0.38 伸び、紙面が 0.61 高くなっただけ。
**着手前の予測は 2 つとも外れた**（「横が広がって行あたり小節が増える」→ 0.38 では届かない、
「帯が狭まって fixture が 4 ページになる」→ 3 ページのまま 13+13+8）。

⚠️ **`HaraKiriVisualTests.PagedRendering` が本当に壊れた。** `PageHeight=30` だけ固定して
余白は製品既定を継承していたため、帯が 20 → 18.62 になり分割されなくなった。
**ページブレーカーのテストは自分が割るページを所有すべき**なので、上下余白を明示させて直した
（再ピン止めではない）。横は幅に依存しないので製品既定のままにしてある。

#### ✅ Y の台帳を開いた（`0c0d8f38`）— 6 桁で 3 点

LP 2.26.0 の `page-vertical.ly` **book L**（短いスコア＋既定用紙＝そのページが最終ページで、
gap は伸長後でなく **spring の自然長**）に対して:

| 台帳キー | Lily# | LP | 残差 |
|---|---|---|---|
| `page.width` | 119.501575 | 119.501575 | **0** |
| `system.natural-distance` | 12.000000 | 12.000000 | **0** |
| `page.first-staff-refpoint` | 8.690551 | 11.690551 | **−3.000000** → **✅ 0**（`1dfb62d7`） |

⚠️ **gap が exact なのは形式ではなく実結果。** 旧記述は SVG 2 桁読みで「一致」としていたが、
それでは 12.00 と 12.004 を区別できない。6 桁で 12.000000 だった。

#### ✅ 先頭 system の −3.000000 は閉じた（`b94487ad` → `1dfb62d7`）— **順序が本体**

LP はインクが小さいとき先頭 system をインクで測らない: `top-system-spacing` は spring で、
インクはその**床**でしかなく（`page-layout-problem.cc:625-633` ＋ `spring.cc:156-159`）、
force 0 の spring は `max(min_distance, ideal_distance)`（`spring.cc:219-237`）。
つまり距離は **`max(basic-distance, ink + padding)`**。`TopSystem.BasicDistance` の 6 は
元から正しく、**読み手が居なかっただけ**だった。

**ただし字面移植の前にアンカー統一が要る**（2026-07-22 に②だけ入れて差し戻した実測）:

| | Lily# | LP |
|---|---|---|
| `firstY` / `SystemLayout.Y` が指すもの | **五線の最上線**（system 原点） | — |
| `top-system-spacing` が狙うもの | — | **staff refpoint＝中央線** |

**ずれは `halfStaff` = 2 ss ちょうど。単位も向きも同じで、アンカーだけが違う。**
①を飛ばして②だけ入れると *最上線* が LP の *refpoint* 値に着地し、残差は
**−3.000000 → +2.000000** で閉じない。実際にそうなることを実測して差し戻してある。

入れたもの: `LayoutUtilities.CalculateFirstStaffRefpoint`（refpoint フレームで距離を出す。
**header は アンカーでなく床に入る** — `page-layout-problem.cc:441-444`＋`:471-473`）と、
`CalculateFirstSystemY`（= refpoint − halfStaff。**halfStaff 変換はここ 1 箇所だけ**）。
呼び出しは `LayoutEngine`（先頭 seed と単一ページ経路）と `PageLayouter` の 3 箇所。

⚠️ Lily# の `upExtent` は**五線最上線より上のインク**（probe V では 0）で、LP の
`up_skyline.distance()` は **refpoint からのインク**（同 3.8）。**この 2 つを取り違えると
また閉じない。** 変換は `CalculateFirstStaffRefpoint` の中で `upExtent + halfStaff` として
1 箇所に閉じてある。

⚠️ **未了**: LP の top spring は**ページ justify で伸びる**（`set_default_stretch_strength
= ideal_distance`、`spring.cc:213-216`）が、Lily# は先頭 system を固定して system 間 spring だけ
伸ばす。force 0 では見えないので台帳は緑。**伸長 regime に着手するとき最初に触る乖離。**

#### ⚠️ 意図的な乖離: 単一ページは紙面サイズにならない

`page.height` を台帳に入れようとして落とした。**Lily# は 1 ページに収まるスコアを内容サイズの
ページで出し、溢れて初めて紙面に切り替える**（`LayoutEngine.cs:606-611`、明示的な設計）。
LP は常に紙面に組む。この probe では **−109.468268**。実在し理解もできているが閉じる予定が無く、
台帳に載せると `total |residual|` が ~109.5 になり指標が壊れるのでここに書く。
**ページ分割される経路は紙面をそのまま使う**（`test/multi-page-vertical` は 3 × 169.009370）。

#### ✅ 伸長 regime も鎖として移植した（`6ffbe7bd` → `cfdf85b4`）

**LP はページごとに `[top spring][system pair ×N][last-bottom spring]` を1本の鎖にして
`Simple_spacer` で `page_height_` に対して解く**（`page-layout-problem.cc:406-545`・`:780-804`）。
だから**ページ上の全 spring が同じ force を持つ**＝ top も bottom も一緒に伸びる。
Lily# は鎖を持たず、先頭を固定し、足に spring を置かず、**最終 system のインクが下余白に
届くまで**system 間だけ伸ばしていた。solver は既存の `SpringSolver`（`Simple_spacer` 移植済）で足りた。

**同時に廃止した転記誤り2件**:

| 誤り | LP の実際 |
|---|---|
| `TopSystem.Stretchability = 0` | `paper-defaults-init.ly:78-80` に **stretchability キーは無い**＝`set_default_strength` で inverse stretch は **ideal（6）**（`spring.cc:213-216`）。「0」を「未指定」と読むようにした |
| `InverseHooke = max(0.1, Stretchability / 60)` | **`/60` も下限 0.1 も LP に無い。** stretchability をそのまま使う |

⚠️ **compress strength は spec の minimum-distance から取る**（`ensure_min_distance` は
最小値を上げるが**強度を張り直さない** — `spring.cc:156-159`）。上げた後の min を渡すと
全 blocking force が静かに変わる。

✅ **PageBreaker は 4 点とも閉じた**（`113fdeda`: `inverse_hooke_` ／ `min_whitespace_at_top/bottom_of_page`
／ 死んでいる `bottom_padding_`。`850c7d98`: orphan penalty を markup 段落の規則へ）。top spring と last-bottom spring は、ブレーカーには
**バネとしてではなく「最小空白の予約」として入る**のが LP の設計。

⚠️ **旧記述「`systemDetails[0]` が `vs.SystemSystem`、配置側は `vs.TopSystem` ＝食い違い」は誤診だった。**
LP は**先頭を含む全 line に system-system-spacing を与える**（`constrained-breaking.cc:548-555`）。
top-system-spacing がブレーカーに届く経路は `min_whitespace_at_top_of_page` **だけ**。
`i == 0` 分岐は元から正しい。訂正はコード側（`PageBreaker._vs` の remark）にも書いた。

⚠️ **残っているのは構造差（LP はページが行分割を選ぶ）と、単一ページ・フォールバック**。§1 冒頭を見ること。

⚠️ **`LayoutEngine` の単一ページ経路は今も自前で積んでいる**（force 0 なので鎖と一致するが**二重実装**）。

#### 参考: 旧記述の紙面比較表

`Measure-LilyPondPageGeometry.ps1`（LP **2.26.0** 実測、A4、markup 無し、単位 ss）:

| | Lily# 旧 | **Lily# 現在＝LP 2.26.0** | 参考: 2.24.4 |
|---|---|---|---|
| `PageHeight` | 168.4 | **169.009370** | 169.009370 |
| `MarginTop` | 5 | **5.690551**（10 mm） | 2.845276（5 mm） |
| `MarginBottom` | 5 | **5.690551**（10 mm） | 3.414331（6 mm） |
| 縦の使用可能帯 | 158.4 | **157.628268** | 162.749764 |
| `MarginLeft` / `MarginRight` | 8.5 / 8.5 | **8.535827**（15 mm） | 5.690551（10 mm） |
| `ContentWidth` | 102.05 | **102.429921** | 108.120472 |
| `PageWidth` | 119.05 | **119.501575** | 119.501575 |
| system 間の自然距離 | 12.000000 | **12.000000**（一致） | 12.000000 |

**この表は `e38a76bf` で解消済み**（Lily# 列＝LP 列）。2.24.4 列は過去の記述との突き合わせ用。

（旧記述の「LP 5.68 / Lily# 8.93」はプローブの `section Main` マーク由来の交絡で、
markup 無しで測り直すと LP の先頭インク上端は **3.824272**。）

（この4定数は `e38a76bf` で完了。上の ✅ を参照。）

#### 圧縮は**まだ入れない**（旧記述の訂正は維持）

PASS 2 を `SpringSolver` による両方向 solve に置き換えて測ったところ、**プローブも snapshot も
1 バイトも動かなかった**（§3.4 に従い revert 済み）。理由は Lily# の使用可能帯が狭く、
ブレーカーが伸ばす側の本数しか選ばないため。**上の余白を LP に寄せると圧縮側に入る見込み**
なので、順序は「余白 → 圧縮」。置換箇所は `PageLayouter` PASS 2 → `SpringSolver.SolveForWidth`
＋ `GetPositions`、PASS 1 で `gapMinimum[]` を拾う必要あり。
**入れるときは圧縮が実際に走るケース（`SystemsPerPage` 強制など）を同時に用意すること。**

✅ **複数ページ fixture は入った**（`7291531a`）。`test/multi-page-vertical` は **3 ページ**で、
ページ1（埋まったページ）・中間ページ（ヘッダ無し）・**最終ページ（前ページの force を継承）**の
3 ケースを分離する。2 ページだと最終ページ規則と単ページ規則が区別できないので 3 が最小。
全音符なのは、ページ分割が価格付けるのは **system** であり、1 小節 1 グリフなら同じ 37 system を
88 KB で買えるから（四分音符だと 210 KB でツリー最大を超える）。最初の system だけ小節番号が
無いので `staff-refpoint-extent` が system ごとに違う＝ §5.3 の取り違えを検出できる。

**これで余白 4 定数を動かす準備は整った。**

**Y コーパスの起こし方（未着手）**: `audit/lp-geometry` の台帳は X 専用のまま。LP 側の
プローブと測定スクリプトは `d57defcd` で入ったので、あとは `lp-geometry.json` に点を足すだけ。
候補は `page.top-margin` / `page.bottom-margin` / `page.height` / `system.natural-distance`
（現状 exact）/ `page.last-page-gap`。⚠️ 版差のある量には印を付けること（§1）。

#### ✅ 解決: `VerticalSpacingParameters.TopSystem.BasicDistance` は **6 で正しい**

2.26.0 実測で **最初の staff refpoint が `top-margin + 6.000000` ちょうど**に来ることを確認した
（`Measure-LilyPondPageGeometry.ps1`、book N）。**「1 は `last-bottom-spacing` からの転記ミス」と
書いたコメントのほうが誤り**（1 は 2.24.4 の値）。潜在バグではない。
**`1dfb62d7` で読み手が付いた**ので、もう死んでいない（`CalculateFirstStaffRefpoint` が読む）。
⚠️ ただし `PageLayouter` は **systemDetails の `i == 0` でまだ `vs.SystemSystem` を使う**（`:101-104`）。
配置側は `vs.TopSystem` なので**ブレーカーと配置で spec が食い違っている**。
ページあたりの本数見積りにしか効かないが、**伸長 regime に着手するとき最初に確認すること。**

### ⑦ MMR まわりの小さい未解決（記録のみ）

- **`GlyphMetrics.RestMaximaWidth = 1.8` が手動側**。フォントメトリクスなので生成器が `rests.M3` を
  出すようになったら `GlyphMetricsGenerated.cs` へ（`GlyphMetrics.cs:89-96` に記録済）
- ⚠️ **`SystemBreaker` は 1 回しか呼ばれない**（＝レイアウトは 1-pass）。LP はページブレーカーから
  システム数を指定して行分割を**やり直させる**（`optimal-page-breaking.cc:139-173`＋
  `page-breaking.cc:75-101`）。**再入可能化は検討のうえ見送りと決定済み**——§1 の決定記録を読むこと。
  蒸し返す前に「頻度の測定」が先
- **`SystemBreaker.BreakIntoSystemsGreedy` は MMR run 非対応**。ただし
  `LayoutOptions.UseOptimalLineBreaking` が既定 `true`（`LayoutOptions.cs:100`）なので**既定出力に
  影響しない**。かつ greedy は LP のアルゴリズムではない（LP = `constrained-breaking.cc` = optimal）
  ので**忠実度は上がらない**。優先度低

---

## 3. 長期ロードマップ

### A. LP 忠実度を測定可能にし、単調に上げる ★中心

**現状 38/52 exact, total |residual| = 0.019730 ss over 48 distances ＋ counts 4/4**
（`audit/lp-geometry/`・**LP 2.26.0 基準**。beam 2 点は `1a002706`/`0138e5c0` で開き＋種（各 +0.95→+0.69）、
前セッションで **forced-direction stem shortening（beam `shorten`）を移植して各 +0.69 → 0**（exact）＝§1 冒頭。
合計は 0.021985 → 1.921985 → 1.401985 → **0.021985**（スラー/タイを閉じた地点に戻った）。この合計を過去値と直接比べない——点集合が違う）。
**X 22 点は 19 exact / 0.022412、Y 7 点は 3 exact / 0.001365、譜間 5 点は 2 exact、
system 間タプレット 2 点、ページ本数 4 点は全て exact**
（`920cf4dc` で起票、`113fdeda`＋`850c7d98` で閉じた）。
**タプレットの 4 点は全て +0.0000208**（`b36db266` で実フォント計測により閉じた。残るは Pango 量子化）、
**譜間の残り 1 点は probe D の −0.000076**（`26afa9fe`。Pango の量子化）。
**5 点とも Lily# に無いテキスト metric が要る＝閉じる予定は無い名前付き残差**
（`0c0d8f38` で Y に開き、`1dfb62d7` で自然長、`cfdf85b4` で伸長、`22120764` でインク、
`90efec02` で clef、`b3cfb119`＋`854a0e95` で譜間を、`26afa9fe` で強弱を、
`caa0f239`＋`075277ff` でタプレットを閉じた）。
**Y の残り 4 点は同一原因**（clef の LILC bbox 3.550 vs LP の skyline 3.540〜3.545）。
X 3 点のうち 2 点は Lily# に無いパイプライン（水平スカイライン／テキストレイアウト）が要る。

⚠️ **ss 合計を過去の値と直接比べないこと**（点集合が違う）。タプレットは 2.214721 まで増えてから
**0.023937 に戻った**——増加は悪化ではなく**誰も測っていなかった乖離の可視化**だった（§5.2.1④）。

これがこのプロジェクトの品質指標。snapshot は「前回の自分」との比較なので、一度承認した誤りは
永久に緑のまま。台帳は **LP との距離**を数値で持ち、増減どちらでもテストが落ちる。

- 短期: ✅ X 軸（§2 ①③④④'）は完結。**定数1つで閉じる残差はもう無い**
- 中期: **コーパスを縦（Y）にも広げる** — 現在は bar line 周りの X のみ。
  ページ縦は LP 側のプローブと測定スクリプトが `d57defcd` で入った（§2⑧）ので、
  次は `lp-geometry.json` に点を足す番。ほかに譜間距離・スラー/タイ・ビーム・臨時記号配置
- 原則: **snapshot を再ベースするたびに、LP 照合済みの点が増えているべき**

⚠️ **既知の穴**: 以下2つの LP 検証は**数値がコメントに残っているだけで、プローブが未 commit**
（scratchpad に置いたまま消える）。コーパスの「再実行可能」原則から外れているので、
次に触るとき `audit/lp-geometry/probes/` に移すこと。
- **stretch strength 0.45 の検証**（同じ音楽を 120mm / 180mm で justify し、force を解いて
  独立な spring で交差検証）→ 数値は
  `SpacingInvariantTests.BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance` に
- **符尾 Y extent のダンプ**（光学補正の 2×2 の裏取り）→ 数値は
  `SpacingRules.BarlineToNextNotesCorrection` の remarks に

✅ **幻の符尾は 3 箇所とも閉じた**（`89aaa29f` `SkylineBuilder` / `26afa9fe` `DynamicEngraver` /
`caa0f239` `TupletBracketEngraver`）。3 例とも「先に点を作る → 予測を書く → 実装」で桁まで一致した。
**この型は 3 回とも成立している。**

✅ **タプレット括弧は 2 つのスカイラインとも閉じた**（`caa0f239` 譜間 / `075277ff` ページ）。

✅ **臨時記号スタッキングの 4 点は `e08f5e12` で閉じた**（`chord.accidental.{sharp,flat}-column-gap-{below,above}` exact＝sharp 1.284000 / flat 0.964561。`aa09f78e` で対で開き、mirror 一致）。
スラー/タイ/ビームも対で開き済みで閉じた——スラー `87bcde22`＋`f093583e`→`d11ede43`、タイ `c182d4d0`→`b0fe9c42`、
ビーム `1a002706`／`0138e5c0`→`81d5740d`（+0.95→+0.69→0 exact・§1 冒頭）。
閉じ方＝**実 glyph 水平スカイラインを bake→`position_apes` 字面移植**（島2＝実スカイライン基盤・§1 ▶）。
おまけに **♮ の `barline.next.key-change-to-notehead` −0.017672 も閉じた**（単音の左予約が同じスカイライン項を取りこぼしていた）。
✅ **ページ跨ぎスラーの対 SSD/SSU は閉じた**（`0385b1fb` 起票→`3ac143e7` 種＋衝突修正。`system.slur-{under,over}-notes` −1.122500648479451 → **−0.007708667**・両側 13.114791981 で 15 桁一致）。残 −0.0077 は **OPEN（原因未特定・受容せず）**＝arc の音符間隔追従と推測だが未実測（§1 ▶）。**残る真の未測領域はページ跨ぎタイ（未起票・§1 ▶）だけ。**
⚠️ タプレットで点を開いたら**欠陥が 4 つ**出た教訓は健在——**新しい点は必ず対で作ること。** スラーはさらに span 依存なので probe に 4 補正＋interior gap が要り、種が cross-system 衝突ドリフトを露呈した（§1 ▶ の教訓）。

### B. 座標系の LP 統一を完了させる（COORDINATE_AUDIT §4.6）

起票時の実バグ8件は全て対処済み。残るのは「数値は正だが frame 忠実性が未完」の3系統:

| | 内容 | 状況 |
|---|---|---|
| ① | 譜間/system 縦積みの Y-down 残存（**島1**） | ✅ **完了**（`ff64f38e`。下記） |
| ② | device 島群（**島2**） | ⏸ 繰延。TieVariant / 水平 skyline の Y horizon / TabStaffGeometry / beam collision island |
| ③ | non-musical PaperColumn の欠落 | 🔄 §2 ③ |

#### ✅ 島1 は閉じた（`ff64f38e`）— **byte 不変・snapshot 0 件**

`StaffLayout.Y` / `StaffGroupLayout.Y` / `GrandStaffLayout.BraceTop`・`BraceBottom` が
Y-up 格納（先頭 staff が 0・下ほど負）。**LP の `Align_interface` は
`where += stacking_dir * dy`（`align-interface.cc:274`）で `stacking_dir = DOWN = -1`＝
元から負に歩いて `translates` をそのまま格納する**ので、これは近似ではなく字面移植。
3 ディスパッチャ・7 積み上げ地点・15 消費側。`Height` は長さなので正のまま。

**副産物**: `FindStaffYInSystem` が `system.Y + staff.Y` になり、`StaffOffsetInSystemUp` が
primitive・`Down` がその否定（＝反射が device 島の縁だけに残った）。詳細は §1 の教訓 2 件。

⚠️ **`StaffOffsetInSystemDown` の残り 8 呼び出しは移行していない**（意図的な device 境界＝島2。
`Down` は消さない）。理由は `LayoutUtilities.StaffOffsetInSystemDown` の remark にある。

**X（③）と Y（①）は独立に進められる。** 島1 は boundary-shim で byte 不変移行できると実証された。

★ **島1 が残した再利用可能な型**（島2 に着手するときの手順）:
1. **格納を反転する前に、格納値を主張するテストを書く**（オラクルが打ち消しを見逃すため）
2. **生産側を全部同時に**（半端＝全崩れ）。消費側は grep で網羅してから
3. **device 島の縁では 1 回だけ反射する**。反射を島の内側へ押し込まない

### C. 未移植 LP 計算の取り込み

tuplet on-line / volta shorten / hairpin niente / ledger / brace / 開 chord / Ignatzek。
出典 `HANDOFF-lp-calc-incorporation.md`（§8）。**未検証の一覧なので、着手前に実コードで裏取り。**

### D. 言語・ツール側（X/Y 座標系とは独立）

いずれも**この一覧は伝聞。着手前に実コードで確認すること。**

- MusicXML インポート — ほぼ完遂、実ファイル検証が残
- AI 協調編集 M1–5（Ctrl+I / 譜面選択 / 補完 / BYO-key）— 実機 E2E 未検証
- 文法改善 5 件 — **完了。糖衣は入れないと決定した**（2026-07-22、下記）。0.3.0 リリースは GO 待ち
- **`note!` 密着の診断 — ✅ master に取り込み済み**（`a994f418` `1ff10e45` `7d5255cb`。
  LYS4009 ＋ 点線小節線のフィクスチャ）。ユーザーがマージ済み・ブランチは残っていない
- **`override` の消費側は 4 つだけ**（`NoteHead.transparent` / `<grob>.color` / `NoteColumn.force-hshift`）。
  **文法側は元から開いている**（grob 名・プロパティ名とも任意の識別子・ハイフン連結可・許可リスト無し）
  ので、増やす作業は配線だけ。⚠️ ただし**値に小数リテラルが書けない**（整数/識別子/文字列/負整数のみ）。
  §5.3 の摂動法を Lily# 側でも使うなら `StaffGrouper.staff-staff-spacing` 等の配線＋小数が要る。
  ⚠️ **page 系（`paper-height` / `top-system-spacing` / `systems-per-page`）を `override` に載せないこと**——
  LP ではそれらは `\paper` 変数であって grob プロパティではない。**コーパスは
  `RenderedGeometry.Render(source, LayoutOptions)` というハーネス引数で解決した**（2026-07-22 決定）
- Dead-code 監査 — アナライザ検出分は完了、手動分が残
- `LILYPOND-REF` 行番号の一括再採番（cosmetic・繰延）
- `IDrawingContext.cs:37-39` の remark が装飾前後2フレームを記述していない（§4.4）
- **対応の取れないスラーが無警告で消える** — `SlurDetector.cs:49-56` は開きスタックが
  空のまま `)` を読むと黙って捨て、開いたまま終わった `(` も捨てる。`(e c4 d)` のように
  `(` の前に音符が無い形（`MusicWalk.cs:103-108` の通り `(` は直前の音符に結び付く）は
  スラーが1本消えるのに診断が出ない。タイの LYS4007 に相当する警告を足す。
  LilyPond も unterminated slur を警告する。**未着手・別ブランチでやる**（2026-07-22 記録）
- **`editors/vscode/src/smartBrackets.ts` を `smartTyping.ts` に改名** — 角括弧
  （`<`/`>` の和音・アルペジオ）だけでなくスラー `(`/`)` とオクターブ記号 `'`/`,` も
  扱うようになり、名前が実態から離れた。`registerSmartBrackets` も同様。変更は
  `extension.ts` の import 1 箇所。**未着手・競合を避けて後で**（2026-07-22 記録）

#### ✅ 決定: 臨時記号の糖衣 `c?` / `c??` は**入れない**（2026-07-22・蒸し返さないこと）

⚠️ 旧記述は「糖衣 `c?` / `c!` 未実装」で、**却下済みと未着手を同じ「未実装」に潰していた**。
`c!` は検討して**撤回した**もの（`3e4188b`）で、未着手ではない。

| | 状態 |
|---|---|
| `c!`（LP の強制臨時記号） | **却下**。`!` は点線小節線トークン（`Lexer.cs:192`、LP `\bar "!"`）。密着判定は空白に意味を持たせ、`c4! d` と詰めた既存の点線小節線の意味を黙って変える |
| `c?`（LP の cautionary） | **不採用**。痛み自体は既に解消済み（`Parser.cs:70-72` が `@courtesy`/`@editorial` を案内する専用エラーを出す）。残りはキーストローク節約だけ |
| `c??`（`@editorial` の糖衣） | **不採用**。LP に無い記号の発明 |

`c?` を落とした決め手は**単独では `!` の罠を悪化させること**: LP の `?`/`!` は対なので、
片方だけ通すと「LP の書き方が効く」と学習させ、`c!` を試す導線をこちらから作る。そして
`c!` は今**黙って小節を割り**、LYS2006（「弱起なら partial を宣言しろ」）という見当違いの
助言しか返さない。さらに和音では LP に無い配置設計が要る（LP は `<cis? e g>` の**音符単位**で、
和音全体に付ける `<c e g>?` は存在しない）。

**→ 代わりに `note!` 密着の診断をやる**（上記・別ブランチ）。静かな誤動作を名指しの説明に
変える方が価値が高く、言語の表面積を増やさない。

★ **ここから出た線引き（今後の記号追加はこれで判断する）**:
> **記号（sigil）は LilyPond が既に記号で綴っているものにだけ使う。
> Lily# 固有のものは全部 `@name` で書く。**

この一本で、`?` を入れない理由・`!` を点線小節線のまま残す理由・将来「括弧なし強制」を
足すなら `@force`（記号ではない）にする理由が、すべて同じ根拠で説明できる。
なお**「強制」自体は既に可能**——`@courtesy` は規則上出ない臨時記号を強制的に出したうえで
括弧を付ける（`MeasureCollector.ItemFactory.cs:73-79`）。無いのは「括弧なしの強制」だけ。

### E. 保守性の負債（このセッションで見つけたもの）

- `DrawingTransform.Identity` は `new()` なので **`ScaleX/ScaleY = 0`**、
  `Identity.IsIdentity` 自体が false。record struct はプライマリコンストラクタの既定値を
  適用しない。出荷 3 backend は無害だが、**記録用コンテキストの作者を2人捕まえている**。
  `Identity => new(0,0,1,1)` に直す価値あり（未実施・要判断）
- `SharedRendererBeamTests` と `LpFidelity/RecordingDocumentContext` に記録用コンテキストが
  **2実装ある**。統合は既存の通っているテストに触るので要判断

---

## 4. 知識の置き場所 ← **増殖防止の核心**

**引継ぎ文書が増えたのは、寿命の違うものを1つに混ぜたから。** 種類ごとに置き場所を決める。

| 知識の種類 | 置き場所 | 例 |
|---|---|---|
| **LP の幾何の実測値** | `audit/lp-geometry/`（プローブ＋台帳） | bar line → 符頭 = 0.900000 |
| **LP の式・定数の出典** | **コード内 `// LILYPOND-REF:`** | `staff-spacing.cc:213` の 0.3 |
| **LP の挙動で驚いたこと** | コード内コメント（数値つき）＋ user memory | 光学補正は clef でなく符尾 |
| **座標系の現状と残作業** | `docs/COORDINATE_AUDIT.md` | §4.5 の対処状況表 |
| **アーキテクチャの意図** | `docs/*.md`（既存の該当ファイル） | `SKYLINE_ARCHITECTURE.md` |
| **不変条件** | **テスト**（`SpacingInvariantTests` 等） | 両 spring 系の一致 |
| **現在地・次の一手・ロードマップ** | **このファイル §1–§3** | |
| **ユーザーの好み・作業規律** | user memory | 「done は push 済みで」 |

**ここに書かない**: LP の式の導出、実測値の生データ、アーキテクチャ解説、コードの説明。
それらは上表の置き場所へ。このファイルには**ポインタだけ**置く。

> 判断に迷ったら: 「これは**次のセッションだけ**必要か、**ずっと**必要か？」
> ずっと必要ならこのファイル以外へ。

---

## 5. 恒久ルール（滅多に変わらない）

### 5.1 ワークフロー規律

- **master 直コミット。ブランチを勝手に作らない**（作成・削除は GO 待ち）
- **1 島 / 1 関心 = 1 commit**。ただし**依存があるなら同時投入**し、message に
  「単独では入れられない」と書く（frame と定数のように、片方だけだと壊れるケース）
- **巨大ファイルを分割しない**
- **Co-Authored を付けない**。message に「何を・なぜ・**検証結果の数値**」＋
  **未完・残差・意図的に触らなかった点**を明記
- コミットは**関係ファイルのみ明示 `git add`**（無関係の `.py` / handoff を混ぜない）
- **push はユーザー。「done」は push 済みでのみ主張。ship = 全緑 ＋ 明示承認**
- **出力を変える変更はユーザー承認前に出荷しない。** snapshot 再ベースも
  **LP 照合 → 承認 → 実行**
- **シェルは pwsh MCP / ripple（bash 禁止）**。ファイル書き込みは Write ツール
  （`Set-Content` 直書き禁止。**PowerShell に heredoc は無い** — commit message は
  ファイルに書いて `git commit -F`）
- **「未使用に見える」≠「消してよい」。** 削除前に `.cs` 以外も横断 grep →`<see cref>` 確認
  → 削除後にヘルパが孤立しないか再 grep →**ユーザー承認**

#### ★ シンボルのリネームは**ユーザーが MSVS で行う**（Claude は自分で改名しない）

クラス・メソッド・プロパティ・フィールドなど**名前を変えたくなったら、ユーザーに依頼する**。
ユーザーが Visual Studio のリファクタ機能（F2）で実施する。**Claude が自分で置換しない**
（grep 不可視の消費者 — `<see cref>`・XML doc・テスト・文字列 — を取りこぼすため）。

**依頼するときに伝える 4 点**:

| | |
|---|---|
| ファイルパス | `C:\MyProj\LilySharp\...\Foo.cs` |
| 行番号 | `123` |
| 現在の名前 | `OldName` |
| 新しい名前 | `NewName` |

⚠️ **オカレンスは 1 箇所だけ伝えればよい。** そこから全オカレンスが一括で改名される。
複数箇所を列挙する必要はない。複数シンボルを頼むときは上の 4 点を 1 行ずつ並べる。

⚠️ **MSVS のリファクタは「コメント内のシンボル名」を取りこぼすことがある。**
改名後に Claude 側で `grep -r '<旧名>'` を掛け、コメント・XML doc（`<see cref>` は追随するが
`<c>Foo</c>` や地の文は追随しない）・`.md`・`.ly` プローブのヘッダ・
コミットメッセージ用のメモに残った旧名を拾って直すこと。**改名依頼とセットで必ず行う。**

⚠️ 新規シンボルの追加・不要になったシンボルの削除は対象外（通常どおり Claude が行う。
削除は上の「未使用に見える ≠ 消してよい」に従う）。ローカル変数も対象外。

### 5.2 LP 移植の原則

> ## ★★ 実測値に基づいてコードを直すことは**禁止**（ユーザー指示・2026-07-22 再確認）
>
> **コードに入るのは LP の `lily/*.cc` / `scm/*.scm` の式だけ。**
> LilyPond の出力から読んだ数値でコードを書いてはならない。可能な限り**字面通りに移植**する。
>
> 実測の役割は 2 つだけ:
> 1. **欠陥を見つけるヒント**（どこがズレているかを知る）
> 2. **移植の照合**（LP の式から導いた値が実測と一致するかを確かめる）
>
> ⚠️ **「実測に合う定数を選ぶ」は 1 も 2 も満たさない。** 合ってしまうので気づきにくい。
> ⚠️ **判定法**: その値を **LP のどの関数のどの行から導いたか**をコメントに書けるか。
> 書けないなら実測を貼っただけ。`LILYSHARP-OWN:` を付けて独自と明示するか、式を探しに戻る。
> ⚠️ **偶然一致に注意**: 2026-07-22 の強弱記号では、誤った frame（五線の中心 2.0 ＋
> staff-padding 0.1）と誤った定数（名目 descent 0.64）が**打ち消し合って** LP 実測の
> 1.342 を 0.002 以内で再現していた。実測に合わせて選んだ定数は、こういう形で
> **2 つの欠陥を固定する**。
>
> ★ **これは何度も繰り返されている指摘。破ったら差し戻し。**

- レイアウト/描画は `C:\MyProj\lilypond-src` の `lily/*.cc` を**符号一致で字面移植**。
  関数名・変数名・符号・丸めまで揃える。**独自の近似・辻褄合わせを入れない**
- **移植したら必ず `// LILYPOND-REF: lily/xxx.cc:行` を付ける**（定数1つ、式1つでも）
- **座標系が揃っていなくて字面移植が難しいときは、勝手に変換して押し込まず報告する**
- **既存の移植を先に探す。**「未実装」でなく「書いてあるが呼ばれていない/引数が違う/
  frame が違う」ことが本当に多い
- **分岐は全部書く**（`space-alist` の型を無視して値だけ使う類の手抜きをしない）
- ⚠️ **doc / コメント / 過去の自分の結論を疑う。ただし「疑った結果」も裏取りする。**
  `LILYPOND-REF` が付いていても式が一致しているとは限らない
- ⚠️ **同名プロパティが grob ごとに別の値**を持つ（`stem-spacing-correction` は
  StaffSpacing 0.4 / NoteSpacing 0.5）。**単位も別**（staff-spacing.cc は staff-space、
  note-spacing.cc は staff position。どちらも /7 するので2倍ずれる）

### 5.2.1 発明を機械に見つけさせる（3 つの仕組み）

**このセッションで見つけた欠陥は 1 件の例外もなく「Lily# が発明した箇所」だった**
（padding 4 倍・`/60`・名目 1.0 の箱・鎖の欠落・clef の欠落）。
「LP の式を正しく移植したが合わなかった」は**ゼロ**。だから方針は正しい。
問題は**発明が何年も気づかれない**ことで、散文の警告では防げなかった。以下は機械が落とす形。

**① 出典の無い定数はテストが落とす** — `LpProvenanceTests`（`UnsourcedBaseline`）。
`EngravingDefaults` / `LayoutOptions` / `VerticalSpacingParameters` の数値定数は
**`LILYPOND-REF:`（LP の式の出典）か `LILYSHARP-OWN:`（Lily# 独自である理由）を必ず持つ**。
台帳と同じラチェットで、**下げるのは可・上げるのは不可**。
⚠️ **初期値 13 は実在の負債**。`NoteheadHeight` `NoteheadHalfHeight` `RestHeight` `RestWidth`
——**今回噛まれた名目の箱がそのまま並んでいる**。1 つ潰すたびに baseline を下げること。
⚠️ **`LILYPOND-REF` が付いていても式が一致しているとは限らない**（§5.2）。
このテストは「出典を書け」までしか強制できない。**REF の隣の式が別物だった実例が
`LayoutEngine` の padding 4 倍**（`page-layout-problem.cc:1070-1127` の真下にあった）。

**② 二重実装は「一致する」不変条件テストで縛る** — spring 2 系統＋改行 gate は既に §5.4 で
縛ってある。**縦も同じ扱いにする**。⚠️ **padding 4 倍のバグは「複製された側」（単一ページ経路）に
住んでいた。** 重複は美観ではなく**移植が半分しか当たらない場所**。新しい経路を足すときは
「既存の経路と同じ答えを出す」テストを同時に足すか、**そもそも足さず統合する**。

**③ 再ベースは台帳点とセットにする** — snapshot を再ベースするコミットは、
**その差分を正当化する台帳キーを message に名指しする**。名指せないなら、
**先に台帳点を作る**（`StaffSymbol` の ±2.05 を保留したのがこの適用例。§1）。
⚠️ 190 snapshot に対し台帳は 29 点。**snapshot は「前回の自分」との比較**なので、
再ベースのたびに誤りを承認する機会が生まれる。網は見た目より薄い。

**⑤ 実測値をコードに書かない。書くのは LP の式。** ★このセッションで指摘された癖
実測は**不具合を見つけるヒントと、移植の照合**にだけ使う。**コードに入るのは LP の
`lily/*.cc` / `scm/*.scm` の式**であって、LilyPond の出力から読んだ数値ではない。
- ⚠️ **判定法**: その定数を**LP のどの行から導いたか**を書けるか。書けないなら、それは
  実測を貼っただけ。`LILYSHARP-OWN:` を付けて**独自と明示する**か、式を探しに戻る。
- ⚠️ **導出形で書く**。`2.05` ではなく `staffHeight/2 + StaffLineThickness/2`。
  数値が同じでも、**前者は測定の写しで、後者は式**。フォントや設定が変われば差が出る。
- ⚠️ **既にある独自定数を別ファイルにコピーしない。** 実例: clef のスカイラインを入れたとき
  `ClefGlyphXOffset = 0.3` を `SkylineBuilder` に**書き足した**。0.3 は LP の量ではなく
  `DrawClef` の独自値で、**発明の家が 1 つから 2 つに増えた**。`EngravingDefaults` に
  1 つだけ置き `LILYSHARP-OWN:` を付けて解消済み。**`SystemSpacing * 0.5` が何年も
  生き延びたのと同じ形**。

**④ コーパスの穴を数える** — 台帳が薄いのは~~多段譜~~・~~ページ跨ぎ~~・**伸長**
（多段譜は `b3cfb119`、ページ本数は `920cf4dc` で入った）。
⚠️ **2026-07-22 に「点が exact でも regime に入っていないだけ」という新しい形が出た**:
ページ本数の点を A4 に足したら最初から exact で、測ってみると**A4 では本数を容量が決めていない**
（第1システムを 4 オクターブ上げても LP は 13 のまま）。**紙面を縮めて初めて binding した。**
**exact は「正しい」ではなく「その regime では動かない」かもしれない。**過去の最大級の欠陥 2 つ（padding 4 倍・clef 欠落）は
**長期間コーパスから見えず**、clef が padding を偶然あぶり出して初めて露見した。
**新しい点を足すときは、既存の点が測っていない regime を優先する。**
⚠️ **2026-07-22 に同じことがもう一度起きた**: 多段譜の regime を開いた瞬間、
狙っていた欠陥（五線 0.05）と**一緒に、狙っていなかった欠陥（幻の符尾 1.45）が落ちた**。
**穴を開けると、そこに何が溜まっていたかは開けるまで分からない。**

### 5.3 測定の原則

- **推論せず測る。** 実測 → 予測との照合 → 一致しなければ**まず自分の当てはめを検算**
- **摂動法が強力**: `\override` で esw / padding を振り、係数1で追随するか不変かを見る。
  **全部ゼロにして残った定数**がハードコード値
- **測定 regime を混ぜない。** ragged-right（force 0）では spring の床、圧縮時は rod が
  binding する。**どちらで測ったか必ず記録する**
- ★ **ダンプは「1 レコード 1 行」にする。** 行を分けると**落ちる**（LilyPond の stdout に
  診断が割り込む）。2026-07-22 に「2 ページなのに PAGE 1 の行しか出ない」形で落ち、
  そこから誤った結論を出した。**内訳は 1 回の format にまとめ、合計が既知の総数と
  一致するかを必ず突き合わせる**（本数の和 ≠ システム総数なら、その読みは捨てる）
- **配置は「両側」を測る。** ある grob の位置は前後2つの間隙で決まる。
  さらに**同じ box の左右が同じ基準点か**を確かめる
- ★ **縦は staff refpoint 間で測る。system 原点間で測ると嘘の値が出る。**
  `staff-refpoint-extent` は system ごとに違う（小節番号を頭上に持つ system は原点が
  その分だけ上に伸びる）ので、**間隔が一様でも原点間距離はばらつく**。同じ LP ダンプが
  原点間で 11.528583 / 12.000000、staff refpoint 間で 12.000000 / 12.000000。
  前者が「LP は圧縮している」という誤った結論として引き継がれていた（§2⑧）
- ★ **残差の符号で原因を切り分ける。** あるグリフの**左右の残差が逆符号**なら
  **frame（基準点）の誤り**、**同符号**なら**定数の誤り**。定数が違えば両側とも同じ向きに
  ずれるが、基準点がずれていると片側が広がった分だけ反対側が狭まるため。
  行中 clef/key 変更でこれを使って診断した（`midmeasure.*` の4点。§2①）
- ★ **変更する前に測る。** 変更後に測ると「LP に近づいたか」を判定できない。
  着手前にコーパスへ点を足しておけば、**反証可能な予測**（この4点が揃って 0 に向かうはず）
  になり、外れたときに診断が違うと分かる
- **「悪化した」＝「変更が間違い」ではない。** 間違った定数が別の欠陥を隠している構図は実在する
- ⚠️ **SVG から精密測定をしない。** 座標は `F2`（`SvgGenerator.cs:229`）で2桁に丸められる。
  6桁の LP 値と比べるなら `LpFidelity/RecordingDocumentContext` を使う
- ⚠️ **紛らわしい数値に飛びつかない。** 6桁一致しないなら別物と疑う
  （残差 0.189365 を「bar line 幅 0.19」と誤認した実例あり）

### 5.4 テストの原則

- **実装の定数を実装自身と比べるテストは何も守っていない。**
  LP 由来の期待値を書き、なぜその値かを `LILYPOND-REF` で示す
- **テストが LP と食い違ったら、テストを実測に合わせる**（再ピン止めしない）
- **追加したテストが「修正前なら落ちる」ことを実証する**
- **1点狙い撃ちにせず掃く**（掃引テストは改行位置が動いても空振りしない）
- 増分再利用（F3）: **小節幅に影響する新要素は `MeasureContentKey` に必ず畳み込む**。
  「隣の小節の内容で決まる」量は intrinsic hash から復元できないので**明示的に**足す
- spring は 2 系統＋改行 gate の 3 箇所を**必ず一致**させる
  （`MeasureLayouter.CreateTimingSprings` / `SpacingRules.CreateSpringsForMeasure` /
  `SystemBreaker`）

### 5.5 環境の落とし穴

- **dotnet の増分ビルドが腐る** → 前後比較では `--no-incremental` でビルドして
  `dotnet run --no-build`。なお `dotnet test` は `--no-incremental` を受け付けない
- **LilyPond は Guile デッドロックする** → `cmd /c "... < NUL"` でデタッチ必須。
  終了コード 1 でもダンプは出ている
- **コンソールの文字化けに騙されない。** ファイル実体は正しいことが多い。Read で確認してから判断
- **fixture**: Lily# の `octave absolute` は LP より一段高い（**LP `c'` ↔ Lily# `c`**）。
  既定は相対オクターブ。mid-music の key/time/clef 変更はバックスラッシュ無し。
  空小節は `| |` ペア。part 名に予約語を避ける（`p` は dynamic）

---

## 6. コマンド集

```powershell
# ビルド（--no-incremental 必須）
dotnet build LilySharp.Core\LilySharp.Core.csproj --no-incremental -v m   # 0 warn/err 期待
dotnet build LilySharp.Tests\LilySharp.Tests.csproj --no-incremental -v q

# 全テスト
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build -v q 2>&1 | Select-String 'Passed!|Failed!|\[FAIL\]'

# LP 忠実度スコア
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build `
  --filter 'FullyQualifiedName~Corpus_ReportsTotalDivergence' --logger 'console;verbosity=detailed'

# LP 実測（プローブを LilyPond に通す。既定 exe は 2.26.0）
pwsh audit\lp-geometry\Measure-LilyPondGeometry.ps1        # X（小節線まわり）
pwsh audit\lp-geometry\Measure-LilyPondPageGeometry.ps1    # Y（ページ縦）

# フォント由来の生成物（フォント更新後は必ず両方。py -3.13 必須 — PATH の python は
# LilyPond 2.24.4 同梱で fontTools も pip も無い）
py -3.13 audit\scripts\Extract-EmmentalerGlyphs.py     # → Svg\EmmentalerGlyphs.Generated.cs
py -3.13 audit\scripts\Extract-EmmentalerMetrics.py    # → Svg\Layout\GlyphMetricsGenerated.cs
# どちらも「feta 名がフォントに無ければ exit 1」。差分が出たらフォントが変わった証拠

# snapshot 再ベース（LP 照合＋ユーザー承認の後のみ・フィルタを掛けない）
$env:LILYSHARP_UPDATE_SNAPSHOTS = "1"
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build -v q
Remove-Item Env:\LILYSHARP_UPDATE_SNAPSHOTS
"ENV NOW = [$($env:LILYSHARP_UPDATE_SNAPSHOTS)]"    # ← 空であることを必ず目視
# → env を消して再実行し全緑を確認 ＋ git status で「動いたのは意図した snapshot だけ」を確認

# 目視用 PNG / SVG
dotnet run --project LilySharp.Cli -- png --crop --scale 4.0 "NAME.lys" "out.png"
```

- fixtures = `LilySharp.Tests\Fixtures\{test,showcase}\*.lys`、
  snapshot は `Snapshots\<dir>__<name>.svg`
- snapshot テストは `SvgSnapshotTests.TestSamples()` の**明示 `yield return` リスト**
  （`.lys` を置くだけでは走らない）

---

## 7. セッション終了時チェックリスト

1. [ ] 全緑を確認（`Passed!` の数を §1 に書く）
2. [ ] **§1「現在地」を書き換える**（追記しない）— HEAD / ahead 数 / テスト数 / やったこと
3. [ ] ロードマップ（§2/§3）が動いたなら更新する。**完了した項目は消す**
4. [ ] このセッションで得た**恒久的な知識**を §4 の表に従って置き場所へ出す
      （LP 実測値 → `audit/lp-geometry/`、LP の式 → コード内 REF、座標系の状態 →
      `COORDINATE_AUDIT.md`）
5. [ ] **新しい `handoff-*.md` を作っていないことを確認**
6. [ ] **snapshot を再ベースしたなら、それを正当化する台帳キーを message に名指したか**（§5.2.1③）
7. [ ] **定数を足したなら `LILYPOND-REF:` か `LILYSHARP-OWN:` を付けたか**
      （`LpProvenanceTests` が落ちる。baseline を上げて通すのは禁止・§5.2.1①）
8. [ ] `git status` で意図しないファイルが混ざっていないか確認
      （特に `audit/scripts/__pycache__/` — 生成器を走らせると必ず出る。commit しない）

---

## 8. 旧 handoff ファイルの棚卸し

root に **14 個の未追跡 `HANDOFF-*.md` / `handoff-*.md` / `REVIEW-HANDOFF.md`** が残っている
（`handoff-2026-07-21-mmr-runs.md` は回収完了ののち削除済み）。
各ファイルが「原則・手順」を丸ごと重複コピーしており、これが増殖の主因だった。
**本ファイルがそれらを置き換える。**

⚠️ ただし中身には未回収の知識が残っている可能性がある。**一括削除しないこと。**
以下は着手時に参照する価値がある順:

| ファイル | 参照価値 |
|---|---|
| `handoff-2026-07-21-x-frame-unification.md` | ✅ 内容は完了・`COORDINATE_AUDIT.md` §4.7 と本ファイルに吸収済。**光学補正を clef のせいとする誤記あり**（訂正済） |
| `handoff-2026-07-21-boundary-column.md` | §2③ 着手時に。LP 事実の記録として有用 |
| `HANDOFF-stage4-vertical-yup.md` | **島1 は `ff64f38e` で完了したので、この用途は消えた。** 残る価値は §3B ②（島2 = device 島群）の記述だけ。⚠️ 島1 に関する記述は全部 stale。**次に島2 へ着手する人が回収して削除する候補**（削除はユーザー承認） |
| `HANDOFF-lp-calc-incorporation.md` | §3C 着手時に |
| `HANDOFF-dead-code-audit.md` | §3D 着手時に |
| `HANDOFF-2026-07-20-*.md`（5本） | 過去セッションの記録。LP 事実は概ね吸収済 |
| `HANDOFF-beam-quanter-unification.md` / `HANDOFF-coord-frame-unification.md` / `HANDOFF-layout-x-unification.md` | 完了済み作業の記録 |
| `REVIEW-HANDOFF.md` | 規律は §5.1 に吸収済 |

**回収方針**: 各テーマに着手するとき、そのファイルを読んで**必要な知識を §4 の置き場所へ移し、
読み終えたファイルを削除する**（削除は都度ユーザー承認）。一気にやらない。

なお `AI_POSITIONING_HANDOFF.md` と `docs/arpeggio-rework-handoff.md` は**追跡済み**で
別系統。本ファイルの対象外。
