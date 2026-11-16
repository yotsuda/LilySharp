# 行だけの score の一線譜 — 設計

オーナー決定 2026-11-15（HANDOFF §3 の最上段）: 譜表の無い score（`chords` と `lyrics` の行だけ）には一線譜を自動で描く。
⑴ 線の上には何も描かない（線と小節線だけ）⑵ 歌詞の行だけの score にも描く ⑶ 描かない書き方は用意しない
⑷ 置き場所＝行は書いた順のまま、一線譜は最初の歌詞の行のすぐ上・歌詞の行が無ければ最下段。

## 背景（測ったこと）

- chords だけの譜は文字と小節線だけで、拍と小節の土台が無く読みづらい（オーナー）。
- 全部が空の小節（`| | | |`）の段は小節線も描かれず真っ白になる（Lab `sessions/p1279/row/mylife.png`）。
- 手で書いた同じ形＝中身の無いパート `part line { }` と `score { chords prog  staff line as lines 1 }` は、
  望む絵になる（`sessions/p1279/row/l1.png`: 空の段にも線と小節線、1. 2. の括弧は線に乗る、拍子は線の頭）。
  ⇒ **自動の一線譜は「中身の無いパートを `staff … as lines 1` で置く」のと同じ絵**を目標にする。
- 行が譜表の上か下かは今も書いた順（`chords prog  staff m  lyrics en` はコードが上・歌詞が下、
  `staff m  lyrics en  chords prog` は両方下で歌詞→コード）。⑷ はこの規則に乗る。

## 形

- **差し込む所は 1 つ**: `RenderSpecParser.Parse` が項目を並べ終えた所（`FoldAdjacentRows` の前）。
  条件＝項目に譜表の類が 1 つも無く、`ChordRowSpec` か `LyricsRowSpec` が 1 つ以上。
  最初の `LyricsRowSpec` の前、無ければ最後に `SingleStaffSpec(new StaffSpec(Treble, AutoStaffPart, Lines: 1, NameSuppressed: true))`。
  `FindFirst`・`FindByName`・`PlayedSpec` など spec を読む道は全部 `Parse` を通るので、ページ・MusicXML・MIDI に届く。
- **名前**: `RenderSpecParser.AutoStaffPart = "(staff)"`＝括弧はパート名に書けない（`UnnamedScoreLabel` と同じ手）。
  宣言は作らない。構文木にも入れない＝ソースの位置は動かず、構文を読む検証（未宣言のパート LYS1007 など）は見ない。
- **小節**: 中身の無い譜表は、行の小節の骨組み（`GiveBlankStavesTheRowsBars`＝長さ・弱起・小節線・ラベル）を写して
  もらう。今は chords の行だけを写し元にしている＝**歌詞の行も写し元にする**（歌詞だけの score のため）。
- **見た目**: `as lines 1` の今の規則のまま＝音部記号と調号なし、拍子は描く（`SpacingRules.EngravesClef`）。
  `MultiStaffScore.IsLeadSheet` は偽になる（本物の譜表がある score と同じ組み方）。

## 特別に扱う所

1. **`.ly`**: `\score` の譜表は構文から書いている（`EmitScore` → `RenderRows`）ので自動の譜表は届かない。
   行だけの score が今書いている拍の骨組み（`EmitLeadSheetTiming`＝`\new Devnull` の skip）を、
   一線譜の位置に `\new RhythmicStaff`（LILYPOND-REF: ly/engraver-init.ly＝Clef と Key の engraver を外した一線譜）として書く。
   `EmitStaff` は使わない（宣言の無い名前は別のパートの音楽を拾う）。
2. **`SingleEngravedPart`** は自動の譜表を数えない＝裸の section の音楽の持ち主にしない（MIDI・MusicXML）。
   ページ側も、自動の譜表が裸の section の音楽を拾わないようにする（`MeasureCollector.Form.cs` の「どのパートにも
   当たらない声部が拾う」道）。
3. **MusicXML**: 自動のパートは `<staff-lines>1</staff-lines>`（`WriteStaffLines` が既に書く）・名前は印刷しない
   （`(staff)` を `<part-name>` に漏らさない）・コード（`<harmony>`）はこのパートの小節に載る。
   小節を足さない道（form の無い本・section の無い本）では今の書き方（行を自分のパートにする）を保つ＝コードを落とさない。
4. **MIDI**: 何もしない（音符が無いのでトラックもチャンネルも作られない）。バイト同じを網とスイープで確かめる。

## 作ってみて決めたこと（第1281）

- **自動の譜表は音楽を歩かない**: `MeasureCollector.CollectMultiStaffPass` は自動の譜表に空の声部を与え、
  `GiveBlankStavesTheRowsBars` が行の小節（chords の行、無ければ歌詞の行）を写す。写すときは行の拍子と調の
  変更（`TimeSignatureChangeItem`・`KeySignatureChangeItem`）をそのまま残す（`SpacerSkeleton(keepSignatures)`）。
  歩かせると ⑴ form の無い本で行の終わりの括弧を音楽のものとして読み（2 つめの `2.` が 1 小節前に出た）、
  ⑵ 見出しの `partial` を落とした。
- **form の無い本の声部の歩きは、上の段の `chords`／`lyrics` の中へ入らない**（`s_formlessMusicSiteRule`）。
  自動の譜表だけでなく、手で書いた `part l { }`＋`staff l as lines 1` も同じ括弧を重ねていた。
- **ページは手で書いた `as lines 1` と同じ規則で描く**＝網 `AutoRowStaffTests.ThePage_IsAPartWithNoMusic_PlacedAsLines1`
  （描くものと高さが同じ。X だけ違う＝自動の譜表の小節は行が間隔を決める `RowsSpaceTheGrid`）。
  行の頭のラベルは 1 本線の譜表の規則＝譜表の左端に中央を置く（LilyPond の実測・第879）。
- **MusicXML は自動の譜表を書かず、行を自分のパートにする今の形のまま**、そのパートに `<staff-lines>1</staff-lines>` と
  印刷しない名前（`ROW (chords)`・`print-object="no"`）を与える（`MusicXmlExporter._rowsStaffLines`）。
  そのパートが拍子の変更・反復記号・`<harmony>` を前から持っている。chords の行が 2 本なら 1 本線のパートが 2 つ。
- **台帳**: 譜表の無い形を測っていた 2 点（`staffless.chords-lyrics.refpoint-step`・`barnumber.rows-only.row-to-ink-bottom`）は
  その形が描かれなくなったので外した。`tempo.staffless.grid.over-chord.row-ink-to-baseline` は 0 に閉じた（コードの行が
  譜表の上に立ち、目印との間は outside-staff-padding 0.46＝LilyPond と同じ）。
- **オーナーの答え（2026-11-15）**: ⑴ 区切りのラベルは譜表の規則のまま＝余地があればコードの名前と同じ高さに並ぶ
  （2026-10-04 の「譜表の無い譜面ではラベルは行の上の段」を一線譜では上書き・網 `RowsOnlySectionLabelTests`）。
  ⑵ 歌詞と一線の間は「自然に見える間隔に。あとで見て評価する」⇒ 下の第1282。

## 第1282 で決めたこと

- **歌詞は線の 3.5 譜間下**（LILYSHARP-OWN・`ScoreAssembler.RowStaffPaper`）: LilyPond の Lyrics の
  `nonstaff-relatedstaff-spacing` basic-distance 5.5 は譜表の基準点（中央の線）から測る＝5 線の譜なら下の線から 3.5 だが、
  1 本線ではその線から 5.5 で離れて見えた。basic-distance から 5 線の半分（2）を引く。padding（小節線の端から 0.5）は
  そのまま。⚠️ 前は段で間が違った: 次の段に押された段は最小の 4.4、最後の段は 5.5（LilyPond も最小を取る＝
  page-layout-problem.cc:593-599）。3.5 は最小より近いので、どの段も 3.5 にそろう（Lab `p1282/gap/gap.png`）。
  当てるのは**音符の無い譜表だけの score**（自動の譜表と、音楽の無いパートの手書きの `as lines 1`）。音符のある
  一線譜は符尾が線の下へ伸びるので LilyPond のまま。紙に `nonStaffRelatedStaffSpacing` を書いた本は書いた値のまま。
  ⚠️ 紙（`score.Paper`）に入れたので、紙を読まない網の道具 `RenderedGeometry.Render`（既定の紙）には出ない＝
  網は `SvgGenerator.Generate` で測る（`AutoRowStaffTests.TheWords_Hang3Point5BelowTheLine_OnEverySystem`）。
- **`on` を書かない歌詞の行は、音楽の無いパートを歌わない**（`RenderSpecParser.SungStaffAbove`）: すぐ上の譜表を歌う
  規則のせいで、`part l { }`＋`staff l as lines 1` の下の歌詞の行はその譜表に畳まれ、音符が無いので全部の音節が
  黙って消えていた（警告も無し）。音楽の無いパート（宣言に section も音楽のブロックも無い）なら、行は等分の行のまま。
  書いた `on l` は今までどおり結ぶ。あわせて `MultiStaffScore.RowsSpaceTheGrid` も音符の無い譜表を自動の譜表と
  同じに数える（行が小節の幅を決める）。網は `ThePage_IsAPartWithNoMusic_PlacedAsLines1` の 7 例目。
- **`IsLeadSheet` の格子の道は消さない**: ページでは通らない（全網で true になったら投げる毒＝SVG の網は 0 本）が、
  MusicXML の書き出しは自動の譜表を外した仕様で score を組むので true になる（網 13 本）。ページ側の分岐
  （`SharedRenderer` の格子・`MultiStaffLayouter`／`SpacingRules` の lead sheet の枝）は、片付けるなら MusicXML の読み手と
  分けてから。

## 調を文字で（オーナー要望 2026-11-16・第1285）

- **score の譜表（行は除く）が全部 1〜4 線**（自動の一線譜・`as lines 1`〜`4`）で、タブ譜・打楽器の譜が無ければ、
  最初の小節と転調の小節の頭に **「Key: D」**（短調は「Key: Bm」・♯♭は「F#」「Bb」の ASCII）を出す
  （`Svg/Collector/KeyNameMarks.cs`・目印の種類 `MusicMarkType.KeyName`・LILYSHARP-OWN）。5 線以上の譜表が 1 本でもあれば出さない。
  `key custom`／`key none`、`key` を書いていない本も出さない。
- 名前は書いた `key` の構文から（主音の音名と長調／短調）。最初の調は「0 小節目から始まる section の見出しの `key`、無ければ
  section の外の最初の `key`」。転調は譜表に転調の記号があればそこ（元の調への戻りは元の調の名前）、無ければ（自動の一線譜は
  行の骨組みを写すので転調を持たない）section の開始小節ごとに見出しの `key` か元の調を並べ、前と違う所。
- 見た目: 直立・テキストの大きさ・テンポと同じ横位置に左揃え。**積むのは最後**（`OutsideStaffStacker.PlaceMusicMarks`）＝
  テンポと区切りのラベルは LilyPond の位置のまま（台帳 `tempo.staffless.over-chord.*` が見張る）で、文字はその一番外側に乗る。
  縦は文字のインクで箱を作り、その中心に描く（`KeyNameInk`・`KeyNameBaselineBelowCentre`）。
- 同時に直したこと: **一線譜が途中の転調の記号（♮と♭）を描いていた**（最初の調号は描かないのに）＝`DrawStaffItems` が
  線の数と `Blanked` も見る。2〜4 線の譜は今までどおり記号も描く（LilyPond と同じ・線から外れて見えるので文字が要る）。
- `.ly`・MusicXML には出さない（あちらは本物の調を持つ）。

## 作らないもの

- 線の上のスラッシュ・休符（⑴）。描かない設定（⑶）。
- 譜表のある score の行の扱いは変えない。
