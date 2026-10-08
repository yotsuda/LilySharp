# 音楽フォント（SMuFL）— 設計

**状態**: **文法は決定**（2026-10-08・第857・ユーザー承認）。**実装は未着手**。
段取りは §6（土台 3〜4 便 → 取り込み → 出力 → 仕上げ＝計 7〜10 便）。
**根拠**: ユーザー決定（第850〜851・第857）と、第857 の棚卸し（§3 の表・file:line は 2026-10-08 時点）。

---

## 0. 決定

1. **寸法まで SMuFL から取る**（第850・ユーザー利便性）。描画だけの差し替えではない。
2. **内部の共通語を SMuFL にする**。グリフは SMuFL の名前で呼び、寸法は SMuFL の metadata の形で持つ。
   **Emmentaler も SMuFL の形に包み、すべての音楽フォントを同じ経路で扱う**（第857 ユーザー）。
3. ⚠️ **ただし SMuFL に無くて Emmentaler に在るパラメータは失わない**（第857 ユーザー）。
   包んだ metadata に **LP 固有の拡張**として持たせる（§3）。
4. **Emmentaler の出力はバイト不変**が条件（`music` を書かない譜＝書いた譜と同一）。
5. 書き方は `fonts { music "…" }`（§1・第857 で `paper { musicFont }` 案から変更）。

## 1. 文法

```
fonts { music "Bravura" }

fonts petaluma { music "Petaluma" "Bravura" }      // 名前つきブロック・フォールバック
score parts { fonts petaluma  staff melody }
```

- **`fonts` ブロックのキー `music`**。LP も同じ alist に持つ（`ly/paper-defaults-init.ly:169`
  `property-defaults.fonts.music = "emmentaler"`）。単位のある量（`paper`）でも描き方の切り替え
  （`layout`）でもない。名前つきブロック・score からの参照・上書き・`embedded` は既存の形のまま効く。
- **グリフで描くもの全部**（符頭・音部記号・臨時記号・休符・旗・拍子の数字・指番号・強弱・Ped.・brace…）
  が music フォント。**テキストで描く記譜**（tab の数字・`treble_8` の 8・拍子の `+`）は従来どおり
  `notation` グループ＝`music` とは別物（GRAMMAR に明記する）。
- **寸法の優先順**: `layout { }`／`--set` に書いた値 ＞ フォントの `engravingDefaults` ＞ LP の既定。
  単位の換算（SMuFL は staff space、`Stem.thickness` 等は線の太さの倍数）は内部で行う。
- **既定は `music "Emmentaler"`**（大小文字を問わない＝LP の `"emmentaler"` も通る）。
- **名前の解決順**（試して無ければ次・全部外れたら試した場所を全部並べる）:
  ⑴ 同梱のフォント ⑵ SMuFL 仕様の metadata の置き場
  （[SMuFL 1.4](https://www.w3.org/2021/03/smufl14/specification/font-metadata-locations.html):
  `%COMMONPROGRAMFILES%\SMuFL\Fonts\<名前>\<名前>.json`・ユーザー単位は `%LOCALAPPDATA%\…`、
  macOS `/Library/Application Support/SMuFL/Fonts/…`・`~/Library/…`、Linux `$XDG_DATA_DIRS`・`$XDG_DATA_HOME`
  の `SMuFL/Fonts/…`）＋インストール済みの OTF。配布物の名前 `<名前>_metadata.json`（Bravura の形）も試す。
  ⑶ 見つからない・metadata が無い → **警告**して Emmentaler（入っているかは機械の性質＝テキストの face と同じ理屈）。
- **複数の名前はグリフ単位のフォールバック**。`engravingDefaults` は先頭のフォントだけ。
  どこにも無いグリフは Emmentaler で描き、グリフ名ごとに 1 回警告。
- **拒否**: `music` に `step`／`size`／`bold`／`italic`／`as`（LYS8015 の族）。大きさは `layout { NoteHead.scale }` 等。
  役割ごとの music フォントは作らない。
- **LP 双子**: LP 2.26 は SMuFL を読めない＝Emmentaler で書き出して警告（`size` と同じ扱い）。
- **CLI**: `--set music=Bravura`（OMR が同じ譜を複数のフォントで描くための口）。

## 2. 内部の形

- **`MusicFont`** ＝ SMuFL の font-specific metadata と同じ形の表
  （`glyphBBoxes`・`glyphAdvanceWidths`・`glyphsWithAnchors`・`glyphsWithAlternates`・`ligatures`・`sets`・
  `optionalGlyphs`・`engravingDefaults`）＋ **§3 の LP 拡張**。グリフは SMuFL 名で引く。
- **Emmentaler は生成器で包む**: 今の 3 本の生成器（`audit/scripts/Extract-Emmentaler{Metrics,Skylines,Glyphs}.py`）
  の出力を、feta 名 → SMuFL 名の対応表（§4）を通して同じ形に並べ替える。値は 1 ビットも変えない。
- **他のフォントは実行時に読む**: metadata の JSON と OTF（輪郭は HarfBuzz・今の
  `TextOutlineSkylines.PlaceMusicGlyph` と同じ経路）。輪郭由来の skyline は LP の平坦化の手順をそのまま回す。
- **拡張が無いフォントでの代わり**は §3 の右端の列。代わりの経路は Emmentaler では通らない（＝バイト不変を壊さない）。

## 3. SMuFL に無い・意味が違うパラメータ（失わないもの）

分類: **E**＝SMuFL に欄が無い（LP 拡張に持つ）／**S~**＝欄は在るが意味・単位・精度が違う（両方持つ）。

| # | パラメータ | 今の住所 | 分類 | 包んだ形での持ち方 | Bravura 等での代わり |
|---|---|---|---|---|---|
| 1 | **LILC の箱**（LP が配置に使う設計の箱。輪郭の箱と 0.001〜0.448 ss 違う・ヘ音記号の下 0.448） | `GlyphMetricsGenerated.cs`・`GlyphMetrics.cs` | S~ | `glyphBBoxes`（＝輪郭の箱）とは**別欄**に LILC の箱 | `glyphBBoxes` |
| 2 | **符尾の付け点**（上・下・頭の形ごと。下は上の鏡ではない＝s2triangle） | `GlyphMetrics.cs:939-1006` | S~ | `stemUpSE`／`stemDownNW` に入れ、デザインごとの値を保つ | metadata の anchor |
| 3 | **ledger-shortening-range**（臨時記号ごとの加線の短縮） | `GlyphMetrics.cs:687-701`（mf から手写し） | E | 拡張欄 | `cutOut*` から導く（要設計）か短縮しない |
| 4 | **光学サイズ 8 本**（11〜26・比で最寄りを選び magstep で拡縮。全寸法・skyline・輪郭がデザインごと） | `EmmentalerDesignSize.cs`・`GrobFontSize.cs` | E | metadata を**デザインごとに 8 枚**＋選び方の表 | 1 枚を magstep で拡縮 |
| 5 | **design_size**（LILY 表・11.22〜25.20） | 生成器 `EMP:495-505` | E | 拡張欄 | 不要 |
| 6 | LILY 大域表（staff_space・linethickness・blot_diameter 等） | 生成器 | E | 拡張欄（今は staff_space と design_size だけ読む） | 不要 |
| 7 | **GPOS のペアカーニング**（強弱の文字・数字。Pango の画素丸めの内側） | `GlyphSkylinesGenerated.cs`・`FetaTextRun.cs:87-112` | E | 拡張欄 | OTF に GPOS が在れば読む・無ければ 0 |
| 8 | **数字の 3 つの切り方**（拍子＝素・指番号＝fattened＋.alt・通奏低音＝fixedwidth＋.alt） | `FingeringGlyphRun.cs`・`FiguredBassGlyphRun.cs`・`MeterGlyphRun.cs` | E/S~ | `timeSig0-9`／`fingering0-9`／`figbass0-9` に割り当て、.alt は `glyphsWithAlternates` | SMuFL の 3 つの範囲 |
| 9 | **テキスト経路で測るグリフ**（強弱・数字を ASCII で Pango に通す＝輪郭で測り画素で丸める） | `EmmentalerGlyphs.cs:38-43` | E | 拡張欄に「この字はテキスト経路」 | グリフ経路 |
| 10 | **brace の段**（576 字・高さと幅の表は LP を回して採った） | `BraceLadderGenerated.cs`・`SharedRenderer.Connectors.cs:1060-1071` | E | 拡張欄に段の表 | `brace` を拡縮（`braceSmall` 等の代替も） |
| 11 | トリルの波（LILC の幅＝繰り返しの歩幅・輪郭＝最初の 1 個の長さ） | `TrillWaveOutline.cs` | S~ | #1 の 2 つの箱で足りる | `wiggleTrill`＋`repeatOffset` |
| 12 | Ped. の貼り合わせ（LILC の箱どうし） | 生成器 `EMP:247-257` | S~ | #1 で足りる | 輪郭の箱 |
| 13 | 旗の原点の約束（符尾の右端＋blot/2） | `LayoutUtilities.cs:224,241` | S~ | 旗の anchor に換算して入れる | `stemUpNW`／`stemDownSW` |
| 14 | 拍子の数字の縦の枠（`digitHalfHeight = 1.0`） | `SharedRenderer.Prefix.cs:573` | S~ | 拡張欄（または anchor） | SMuFL の約束（要確認） |
| 15 | grace の斜線（グリフでなく feta-flags.mf の定数で線を引く） | `SharedRenderer.GraceNotes.cs:524-545` | E | 拡張欄に線の定数 | `graceNoteSlashStem*` |
| 16 | 手書きの寸法（maxima 1.8・breve 2.296・breve の送り＝全音符×1.30・32 分以下の旗は 16 分の箱・portato の箱・fermata の箱の流用・付点の半径 0.225・bracket 0.45＝feta の thick_sharp） | `GlyphMetrics.cs:156,901,1026`・`EngravingDefaults.cs:733,968,176`・`ArticulationEngraver.cs:2153-2159` | E | 包んだ metadata に**値として**入れる（コードから定数を消す） | metadata の箱 |
| 17 | Emmentaler の形に合わせた LP の定数（フラットの skyline を幅の 0.375 で太らせる・和音名の 0.094725／0.3／0.6・打楽器記号は C 記号の skyline を借りる） | `AccidentalPlacement.cs:387-390`・`ChordNameGlyphRun.cs:282,636`・`SkylineBuilder.cs:627,648` | E | 拡張欄（フォントの性質として） | 輪郭から（要設計） |

**SMuFL 側で読むだけ・拡張の要らないもの**: 輪郭の箱・hmtx の送り・付点の箱・C／cut-C・加線つき休符・
実行時の輪郭 skyline（`TextOutlineSkylines`）。**エンジンの側に残すもの**（フォントの性質ではない）:
script の優先度と padding（scm/script.scm）・Pango の画素の量子。

⚠️ **線の太さは今フォントから読んでいない**（`EngravingStyle.cs:57-141`＝LP の paper と grob の既定）。
包んだ Emmentaler の `engravingDefaults` には**今の LP の値をそのまま**入れる（バイト不変）。
LP は太さを line-thickness の倍数で持ち、line-thickness は譜の大きさで変わる（paper.scm:52-66）＝
`engravingDefaults` の staff space の値との換算はこの依存を保って行う。`legerLineExtension` は LP では
符頭の幅に比例（length-fraction 0.25）＝SMuFL の絶対値と意味が違う。

## 4. グリフ名の対応

- feta 名 → SMuFL 名の表を 1 枚（約 130 字）。**曖昧なものは描いて目で決める**: スラッシュの符頭 3 種・
  `s2xcircle`・diamond／triangle の系列・`flat.slash`／`flatflat.slash`・Stein の 1/4 音シャープ 2 種・
  `scripts.stopped`・staccatissimo（楔か）・ペダルの踵とつま先・上下で同じ字の script（SMuFL は Above／Below の対）・
  portato（上下の意味が逆＝`ArticulationItem.cs:152-158`）。
- **SMuFL に無い Emmentaler の字**（fattened／fixedwidth の数字・`brace0..575`・`clefs.percussion_change`・
  `clefs.tab_change`・`clefs.tab`）は SMuFL の optional glyph の約束（文体の代替は `.salt01`、それ以外は
  フォント固有の名前）で名前を与える。
- feta 名は LP の語彙でもある（`\musicglyph`・双子の書き出し）＝対応表は両方向に引ける形にする。

## 5. 出力

- SVG・PDF・PNG とも今は「コードポイントで字を描く」（`IDrawingContext.DrawGlyph`）。SVG は WOFF2 を埋め込み、
  PDF は OTF を埋め込む。他のフォントも同じ経路に乗せる（OFL のフォントは埋め込める）。
- `boxes.json` の `kind` は変えない。トップに使ったフォントの名前を足す（OMR が学習データを分けるため）。
- `-n, --no-embed-font` の説明は「Emmentaler」を「音楽フォント」に直す。

## 6. 段取り

1. **土台（3〜4 便）**: `MusicFont` の型と SMuFL 名の表 → 生成器が包んだ Emmentaler を出す → 読み手を
   1 族ずつ `MusicFont` に移す（符頭 → 臨時記号 → … → brace）。**門は毎回: 全 svg の掃き（`p723/svg2/sweep-all.ps1`）で
   動いた svg 0・snapshot 不変・full 緑**。手書きの定数（§3 #16・#17）もここで metadata へ移す。
   - ✅ **第 1 歩（第857）**: `Svg/MusicGlyph.cs`（150 行・SMuFL のコードポイントは glyphnames.json と 0 件の食い違い・
     `feta.` 4 行・未確認 24 行）と `Svg/Layout/MusicFont.cs`（`MusicFont`／`MusicFontDesign`／`EmmentalerMusicFont`）。
     網 `MusicFontTests`（生成された寸法が全デザインで何かのグリフから届く＝失わない規則をリフレクションで）。読み手はまだ 0。
   - **生成器の表に寸法の無いグリフ**（32 分以下の旗・1/4 音の記号・短い／長いフェルマータ・portato・踵とつま先・stopped・
     thumb・maxima・打楽器と tab の変更記号・拍子の数字の箱）は、読み手が手の規則を持つ＝その族を移すときに §3 #16 として拾う。
     生成器を回すには fontTools が要る（この機械には無い）。
   - 次: 読み手を族ごとに移す。最初は描画の口（`IDrawingContext.DrawGlyph(char)` の呼び手が `MusicGlyph` で頼む形）。
2. **取り込み**: metadata の JSON と OTF を読む・§1 の解決順・文法（GRAMMAR §2.4・SYNTAX_REFERENCE・LSP の補完・
   TextMate）・診断・`--set music=`。**同梱するか（§7 C）はこの段の前に決める。**
3. **出力**: 埋め込み・`boxes.json`・双子の警告。
4. **仕上げ**: Bravura・Leland・Petaluma で本を描いて目で見る・§3 の代わりの経路の値付け。

## 7. 未決（ユーザー判断）

- **A. パス指定**（`music "Petaluma" file "fonts/Petaluma.otf"`）: 推奨は「出力の段で入れる」。
- **B. `engravingDefaults` を無視するスイッチ**: 推奨は「作らない」（LP の太さは `layout` に書けば足りる）。
- **C. 同梱**（Bravura・Leland・Petaluma はどれも OFL）: 文法は変わらない＝取り込みの段までに決める。
