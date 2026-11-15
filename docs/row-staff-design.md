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

## 作らないもの

- 線の上のスラッシュ・休符（⑴）。描かない設定（⑶）。
- 譜表のある score の行の扱いは変えない。
