# 評価 4 の「欠けている機能」— 設計

オーナー決定 2026-11-15（第1264）: 案 10 件（Lab `sessions/p1227/proposals-eval4.md`）のうち
**作る**＝6・7・8-1・2・4、**後で**＝3・1・10、**見送り**＝5・8-2〜4・9。着手の順は
6 → 7 → 8-1 → 2 → 4。この文書は機能ごとの設計と、作った後の実際の形。

## 6. 符頭の形の区間 `c'8@notehead(cross) d' e' f'@!notehead`（第1269 で実装）

- **背景**: 語りの区間（× の符頭）に `@notehead(cross)` を音ごとに書いていた（評価の合唱曲で 56 回）。
- **綴り**: 最初の音に `@notehead(STYLE)`、最後の音に引数なしの `@!notehead`。STYLE は今ある 6 語
  （`x`・`cross`・`diamond`・`triangle`・`slash`・`xcircle`）。終わりに形は書かない（`@!` は引数を取らない＝パーサーの規則）。
- **規則は印の区間（`@staccato … @!staccato`・2026-11-12）と同じ 1 つ**＝`Semantics.ArticulationRuns` が構文の上で読む:
  終わりは、自分の音楽（1 パートの 1 section・1 phrase・`voice { } { }` の 1 本）の中で、まだ閉じていない
  **いちばん近い** `@notehead(STYLE)` を閉じる／開きの無い終わりはエラー LYS4018／休符・装飾音・別の声部・
  区間の中で呼んだ phrase の音には掛からない（phrase の呼び出しは警告）／終わりの無い `@notehead(…)` は今までどおり 1 音。
- **重なり**: 区間の中の音が自分の `@notehead(…)` を持てばそれが勝つ。`@dead` も勝つ（× のまま）。
  2 つの区間の中の音は内側の形。印の区間と符頭の区間は名前で見分けるので、入れ子も交差も自由。
- **読み手**（どれも「その音に書いてあったのと同じ」に読む）:
  ページ＝`MeasureCollector.ExtractNoteheadStyle`（`ArticulationRuns.CoveringNotehead`）／
  MusicXML＝`NoteheadFromMarks(WithCovering(…))`（単音・和音・`<< >>` の中の音）／
  `.ly`＝`SplitAttachments(WithCovering(…))` が音ごとに `\once \override NoteHead.style`／
  `lysc review`＝`CompositionChecks.UnpitchedHeads`（× の区間は和声の音に数えない）／
  エディタ＝`@!` の後の補完に `notehead`（`MusicMarkItem.IsSpanEndName`）。
- **音は変わらない**（MIDI は形のない譜と同じ）。
- **一緒に直したもの**: `lysc ly` が区間の終わり（`@!staccato` も）を "dropped (out of scope)" と報告していた／
  `<< c@notehead(cross) e g >>` の中の音の形をページが描いていなかった（MusicXML は書いていた）。
- ⚠️ **作っていないもの＝歌い手**: 案の文面は「歌い手はこの区間を歌わない（語り）」。今は × の符頭の音も、
  言葉があれば歌い手が音高どおりに歌う（1 音の `@notehead(cross)` も同じ＝区間の規則ではなく符頭の規則）。
  入れるなら `MidiExporter.SingLyrics`（× の音の言葉を声にしない）と `SungVoices.Read`（× の音で melisma を切る）の
  2 か所で、耳で確かめるのはオーナー。ステップのキーは形（引数）を回すだけで、終わりは動かない。

## 7. `condensedStaff` / `combinedStaff` の名前（未着手）

## 8-1. `:|*4` に "4x" を刷る（未着手）

## 2. `transposition` に音名＋`range`（未着手）

## 4. `key none`（未着手）
