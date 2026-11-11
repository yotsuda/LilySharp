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

## 3. 変更箇所

- **Collector**（`Svg/Collector/MeasureCollector.cs` 6169 付近）: `DynamicItem.IsAbove` の既定を「歌詞のある譜表なら上」にする（`ForcedAbove` が書かれていればそれ）。`HairpinItem` に `IsAbove` を足し、同じ規則で決める。どの譜表が D1 に当たるかは score の行の並び（RenderSpec）から一度だけ求めて渡す。
- **HairpinEngraver**: いま `SpannerOffsetY(dir: -1.0, …)` と `WedgeSkylines` で下側だけを計算している。`dir` をヘアピンの向きから取り、支えの輪郭（`DynamicEngraver.SpanSupportSkylines`）を上側で取る。下の譜表との間の span bar を避ける処理（`SpanBarBelowOnSystem`）は、上に置くときは上の譜表との間で判定する。
- **DynamicAlignEngraver**: 305 行で `d.IsAbove` の強弱記号を整列から外している。上に置いた強弱記号と上のヘアピンを、同じ向き同士で 1 本にそろえる（LilyPond の DynamicLineSpanner は向きごとに 1 本）。
- **DynamicEngraver**: 上側の配置はすでにある（`IsAbove`、`dir = +1`）。ヘアピンとそろえたときの基準線が上側でも正しいかを照合する。
- **MultiStaffLayouter.BuildAllStaffSkylines**: ヘアピンの場所取りを、上に置くものは上側の輪郭に入れる（段の間隔がここで決まる）。
- **Parser**（`Parser/Parser.Music.cs` 1542 付近、`GreenNodes.cs` 673 付近）: D2 のとおりヘアピンの `.up` / `.down` を受け付ける。
- **LilyPondExporter**: D1 に当たる譜表に `\dynamicUp` を書く。明示の `.up` / `.down` は今までどおり `^` / `_`。
- **MusicXmlExporter**: 上に置いた強弱記号とウェッジを `placement="above"` で書く（ウェッジは今まで書いていない可能性がある。§4 で確認）。
- **プレビューの増分描画**: フルの描画と同じ経路を通ることを、既存の増分＝フルの網で確かめる。
- **ドキュメント**: `SYNTAX_REFERENCE.md`（ヘアピンは常に下、の記述）、`GRAMMAR.md`、`GRAMMAR_FOR_LLM.md`、CHANGELOG（`[engraving]` の印。OMR の学習データに効く）。

## 4. 照合と門

- LilyPond 2.26.0 の `\dynamicUp` の双子（`lysc ly --pin-fonts`）と、上に置いたヘアピン・強弱記号の位置を svg の座標で比べる。少なくとも §6 の譜、クレッシェンドが段をまたぐ譜、合唱（choirStaff の 4 声部）、強弱記号だけの譜。
- 全 svg の掃きで、動く svg が「歌詞のある譜表に強弱記号かヘアピンがある本」だけであることを確かめ、枚数を報告する。
- 毒: 既定の向きを戻す、ヘアピンの `dir` を固定に戻す、整列から上側を外す、の各々で網が赤くなること。
- full テスト・`npm test`・RULES §7 の終了時の門。

## 5. 作業順

1. 調べ（§3 の各所の現状、ヘアピンの `.up` を拒む LYS 番号、MusicXML のウェッジの placement、LilyPond の上側の計算の出典）を確定し、この文書の §3 を正確な行番号で更新する。
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
