# 歌詞のある譜表では強弱記号とヘアピンを譜表の上に置く — 設計

オーナー決定 2026-11-11「A で進めて」「1 で進めて」。この文書は着手前の設計で、作業順は §5。

## 1. 背景

- **報告**: `Downloads/Zundamon-to-Metan-no-Jikoshoukai.lys` の ChorusFinal、めたんの譜表の `d4@ff~`（58 小節）の `ff` が、すぐ下の歌詞「ろ し く ー」の「ー」の真上に来て、歌詞の段に食い込んで見える。
- **測定**: 同じ形の小さな譜（§6）で、`ff` の下端から歌詞の上端まで Lily# は 0.5 譜間、LilyPond 2.26.0 はほぼ 0。`ff` が他の `ff` より 0.67 譜間低いのは、前から続くクレッシェンドの終わりに高さをそろえるため（DynamicLineSpanner）で、LilyPond も同じ。**Lily# の配置の欠陥ではない**。
- **慣習**: 声楽の譜面では、強弱記号を譜表の上に書く（Gould, *Behind Bars*、声楽の章）。LilyPond の声楽用テンプレートは `\dynamicUp` で、強弱記号とヘアピンをまとめて上に置く。`\dynamicUp` を入れた同じ譜（§6）では、クレッシェンドと 2 つの `ff` が一本にそろって譜表の上に出て、歌詞と離れる。
- **今の Lily#**: 強弱記号は `@ff.up` で上に置けるが、**ヘアピンは常に下**（`SYNTAX_REFERENCE.md` の Niente の節の末尾。`@cresc.up` はエラー）。強弱記号だけ上に移すと、下のヘアピンが上の `ff` に向かう形になって切れる。

この変更は LILYSHARP-OWN（既定の向きを LilyPond の `\dynamicUp` の慣習に寄せる）。上に置いたときの位置そのものは LilyPond の上側の計算を移植し、`\dynamicUp` の双子と照合する。

## 2. 決めたこと（オーナー判断・2026-11-11 に D1〜D4 とも推奨どおりで確定）

| # | 問い | 推奨 |
|---|---|---|
| D1 | どの譜表を「歌詞のある譜表」とするか | score の中で、歌詞の行がその譜表の**直下**に付く譜表（`staff sop  lyrics w`）。`lyrics w on alt` のように別のパートの旋律に合わせた行や、譜表から離れた行は数えない。choirStaff の中の各声部も同じ規則 |
| D2 | ヘアピンに `.up` / `.down` を許すか | **許す**（`@cresc.down`）。既定が上になった譜表で下へ戻す手段が要る。今のエラー（LYS 番号は §4 で確認）は外す。LilyPond の `_\<` / `^\<` に当たる |
| D3 | 強弱記号だけ `.down` と書いたとき、そこへ向かうヘアピンは | 書いた記号は書いたとおり、書いていない記号は譜表の既定。ヘアピンが勝手に追随はしない（LilyPond も `_\ff` はその記号だけを動かす） |
| D4 | 歌詞のない譜表 | 今までどおり下（変えない） |

## 3. 変更箇所（第1224 の調べ。行番号は HEAD `294e0b1d4`）

### 3.1 向きを決めて通す（作業順 2）

- **Parser**: `Parser/Parser.Music.cs:1538-1563`。`@cresc.up` を拒むのは 1549-1558 で、番号は汎用の **LYS0002**（`DiagnosticCodes.ExpectedToken`、`Syntax/Diagnostic.cs:254`）。拒んだ `.up` は `DynamicGreen` の 4・5 番目の枠に残している（`GreenNodes.cs:670-684`）。拒むのをやめて 1561 と同じ `new DynamicGreen(at, name, dot, dir)` にすれば、`DynamicSyntax.ForcedAbove`（`SyntaxNodes.Attachments.cs:89`）がそのまま読める。`.subito` はヘアピンには付かない（1525）ので枠の読み分けは変わらない。`@!cresc`（止め）に向きは要らない。
- **D1 の判定**: 「直下に付く歌詞の行」は、すでに `StaffSpec.WithLyrics`（`Svg/Collector/RenderSpec.cs:52`）として譜表に畳まれている。書くのは `RenderSpecParser.WithFoldedVerse`（`:493`）だけで、呼ぶのは score 直下の `FoldAdjacentRows`（`:389`）と group の中の `ParseGrandStaff`（`:769`）。畳まれないもの（`on` で別のパートに合わせた行、tab の下の行、condensed・combined の下の行、譜表から離れた行）は D1 でも数えないので、**「`WithLyrics` が空でない譜表」が D1 そのもの**。新しい判定は作らない。
- **Collector**: 強弱記号は `MeasureCollector.CollectDynamics`（`MeasureCollector.cs:6131`）の 6167-6172（`IsAbove = ForcedAbove == true`）。ヘアピンの始まりはここでは `HairpinItem` ではなく **`MusicMarkItem`**（6184-6190）で、向きの欄がない。`MusicMarkItem` に向きを足し、`HairpinEngraver.DetectHairpins`（`HairpinEngraver.cs:911`、`new HairpinItem` は 1022 と 1101）で `HairpinItem.IsAbove` に写す。譜表が D1 かどうかは、複数譜の入口（`MeasureCollector.cs:1687` の `GetVoiceBindings` の `withLyrics`、譜表番号は 1700）と 1 パートの入口（`attachedLyricParts`、`:935`・`:2700`）で分かる。`CombinedStaffAddressing.cs:189` は `DynamicItem` を作り直すので向きも写す。
- **`@text`**（自由な文字、`MeasureCollector.Annotations.cs:1167`）は変えない。LilyPond の `\dynamicUp` が動かすのは DynamicText と DynamicLineSpanner だけで、TextScript は動かない（`ly/property-init.ly:338-341`）。

### 3.2 上側の配置（作業順 3）

- **HairpinEngraver.LayoutPiece**（`:332`）: 高さは `:558` の `SpannerOffsetY(dir: -1.0, …)`。`DynamicEngraver.SpannerOffsetY`（`DynamicEngraver.cs:504`）と支えの `SpanSupportSkylines`（`:693`）、`WedgeSkylines`（`HairpinEngraver.cs:615`）はどれも上下対称に書かれているので、`dir` をヘアピンの向きから渡せば足りる。段の終わりで span bar を避ける `SpanBarBelowOnSystem`（`:483`・`:730`）は、LilyPond では DynamicLineSpanner の向きの側の隣の譜表を見る（`lily/hairpin.cc:66-78`）ので、上向きは上の譜表との間で判定する。`HairpinLayout`（`:42`）に向きを足す。
- **DynamicAlignEngraver**: `BuildLines`（`:267`）は譜表ごとに 1 本で、`:305` で上の強弱記号を外している。`AlignLines` の高さは `:231` の `dir: -1.0`。譜表と向きの組ごとに線を作る。
- **OutsideStaffStacker**: ここが一番大きい。下の積み（`StackBelowStaffCore`、`:206`）はヘアピンと整列の組を全部扱う（`PlaceHairpin` `:628`、組は `:504` から）。上の積み（`StackAboveStaffCore`、`:1731`）にあるのは強弱記号 1 個ずつの `PlaceAboveDynamics`（`:2552`）だけで、ヘアピンも組も知らない。上向きのヘアピンと組を下の積みから外し（`:322-325`・`:449-495`、ottava の下の支え `:2757`）、上の 250 の番で 1 つの物として置く。両方の memo（`BelowStackMemo.cs`、`AboveStackMemo.cs`）は入力を鍵にしているので、上の memo の鍵にヘアピンを入れる。
- **DynamicEngraver**: 強弱記号 1 個の上側（`PointwiseBaselineY`、`:541`）はすでにある。上で同じ列に重なる記号を 2.0 ずつ積む `StackStep`（`:381-387`）は下側では第851 で外したもので、上側に残っている。既定が上になると普通に通る道になるので、双子と照合する。
- **LilyPond の出典**: 上下の計算は 1 つ（`lily/side-position-interface.cc:188-455` aligned_side、`dir` を掛けるだけ）。DynamicLineSpanner は `direction DOWN`・`padding 0.6`・`staff-padding 0.1`・`minimum-space 1.2`・`outside-staff-priority 250`（`scm/define-grobs.scm:1401-1416`）、DynamicText は向きによらず線の 0.6 下（`:1450`）、Hairpin は線の中央（`:1787`）。`\dynamicUp` は `ly/property-init.ly:338-341`。向きの違う記号が来たら線を切る規則は `lily/dynamic-align-engraver.cc:119-160`。

### 3.3 段の間隔（作業順 4）

- **MultiStaffLayouter**: 譜表ごとのヘアピンは `StaffHairpinLayouts`（`MultiStaffLayouter.cs:4886`）で作り、`:4235` で `SkylineBuilder.AddHairpinsToSkyline`（`SkylineBuilder.cs:2447`）に渡す。入れるのは下の輪郭だけ。上向きは上の輪郭へ。
- **SkylineBuilder.AddDynamicsToSkyline**（`:2830`）: 上の強弱記号の場所取りは幅 1.3・高さ 0.5 の箱（`:2892-2905`）で、下側のような字形の輪郭ではない。`@f.up` がまれだった間はこれで足りたが、既定が上になると段の間隔を決める道になる。下側と同じ輪郭の計算にする。
- **改ページの高さ**（`LayoutEngine.PagingSkylines.cs:609-632`）はヘアピンの中心の上下に同じ幅を取るので、向きによらない。変更なし。
- **歌詞と span bar**（`LyricSpacing.cs:353-356`）は強弱記号の上下をすでに読み分けている。変更なし。
- **描画**（`SharedRenderer.Overlays.cs:1118` `DrawHairpins`）は配置の Y を描くだけで、向きを見ない。変更なし。

### 3.4 書き出し（作業順 5）

- **LilyPondExporter**: `EmitDynamic`（`LilyPondExporter.Music.cs:1834-1852`）は `ForcedAbove` を `^` / `_` にして `\<` / `\>` の前にも付けるので、Parser が受け付ければヘアピンの明示の向きはそのまま出る。`\dynamicUp` を書く場所は `\new Staff` を書く 3 か所（`LilyPondExporter.Score.cs:364`・`:524`・`:651-701`）。`\dynamicUp` は Voice に効く上書きなので、声部が複数ある譜表では `\new Staff \with { \dynamicUp }` の形で譜表に掛ける（作業順 5 で LilyPond に通して確かめる）。
- **MusicXmlExporter**: ウェッジは `placement="below"` を固定で書いている（始まり `MusicXmlExporter.cs:5377`、止め `:1260`・`:5391`・`:5405`・`:5443`・`:5762`）。強弱記号は `:5714` で `ForcedAbove == true` のときだけ `above`。どちらも譜表の既定を知らない。読み込み側（`MusicXmlImport/MusicXmlReader.cs:1335-1354`）はウェッジの placement を読まない。

### 3.5 その他

- **プレビューの増分描画**: フルの描画と同じ経路を通ることを、既存の増分＝フルの網で確かめる。歌詞の行を足す・消すと、その譜表の強弱記号の向きが変わる（譜表の中身は変わらないのに絵が変わる）ので、その編集を網に入れる。
- **ドキュメント**: `SYNTAX_REFERENCE.md:475`（強弱記号は既定で下）と `:529`（ヘアピンは常に下・接尾辞はエラー）、`GRAMMAR_FOR_LLM.md:468`・`:589`、`GRAMMAR.md` の Dynamic の注、CHANGELOG（`[engraving]` の印。OMR の学習データに効く）。

### 3.6 調べで分かった、D3 と LilyPond の違い（オーナーに確認する）

D3 は「強弱記号だけ `.down` と書いても、ヘアピンは追随しない。LilyPond も同じ」としたが、LilyPond は場合による（`dynamic-align-engraver.cc:123-159`）。

- ヘアピンが**先に走っていて**、その終わりに `_\ff` が来る: 線が切れて `ff` だけが下に行く。ヘアピンは上のまま。D3 と同じ。
- **同じ音に** `_\ff\<` と書く: `ff` が線の向きを下に変え、向きを書いていないヘアピンは同じ線に乗るので、**ヘアピンも下に行く**。D3 と違う。

D3 のとおりに作ると、後者（`c@ff.down@cresc`）で Lily# はヘアピンを上、双子は下に描く。書き出しでそのヘアピンに `^\<` を付ければ双子は Lily# と同じ絵になる。作業順 3 まではどちらでも進められるので、D3 のままで進め、作業順 3 の照合のときにこの形の絵を並べてオーナーに見せる。

## 4. 照合と門

- LilyPond 2.26.0 の `\dynamicUp` の双子（`lysc ly --pin-fonts`）と、上に置いたヘアピン・強弱記号の位置を svg の座標で比べる。少なくとも §6 の譜、クレッシェンドが段をまたぐ譜、合唱（choirStaff の 4 声部）、強弱記号だけの譜。
- 全 svg の掃きで、動く svg が「歌詞のある譜表に強弱記号かヘアピンがある本」だけであることを確かめ、枚数を報告する。
- 毒: 既定の向きを戻す、ヘアピンの `dir` を固定に戻す、整列から上側を外す、の各々で網が赤くなること。
- full テスト・`npm test`・RULES §7 の終了時の門。

## 5. 作業順

1. **済（第1224）** 調べ（§3 の各所の現状、ヘアピンの `.up` を拒む LYS 番号、MusicXML のウェッジの placement、LilyPond の上側の計算の出典）を確定し、この文書の §3 を正確な行番号で更新する。LilyPond の目標の svg は Lab `sessions/p1224/dynamics-up/rup.svg`（下向きの対照は `r.svg`）。
2. Collector と Parser（D1・D2）。描画はまだ下のまま、向きの情報だけを通す。網: 向きの判定。
3. HairpinEngraver と DynamicAlignEngraver の上側。§6 の譜で LilyPond の双子と照合。
4. BuildAllStaffSkylines の場所取り。段の間隔を照合。
5. LilyPondExporter（`\dynamicUp`）と MusicXML。
6. 全 svg の掃き、ドキュメント、CHANGELOG。

## 6. 今回の測定の譜（Lab `sessions/p1223/dynamics-up/`: `r.lys`、`r.ly`、`\dynamicUp` を足した `rup.ly`、LP の `rup.cropped.png`）

```
time 4/4
octave absolute
part m { clef treble }
section A {
  m { b'8@cresc cis'' d'' e'' d'' a'4. | d''8 b' b'4 d''4@ff~ d''4 | g'4@ff a' b' r4 | }
}
lyrics w { ず ん だ も ん と | ろ し く ー | な の よ | }
form { A }
score { staff m  lyrics w }
```

`lysc ly --pin-fonts` で書き出した `.ly` の `\new Staff { … }` に `\dynamicUp` を足したものが、LilyPond の目標の形（ヘアピンと 2 つの `ff` が譜表の上で一本にそろう）。
