# 引き継ぎ (a): 多譜表 ottava の完遂 ― above-staff スタッカーの per-staff 化

**前提: `docs/DEV_BUGFIX_WORKFLOW.md` を先に読むこと**(ripple shell / `Write` でファイル化 / master 直 /
勝手にブランチ作らない / LILYPOND-REF / **単一譜 byte-identical 不変条件** / `Co-Authored-By: Claude <current-model>`)。
本書はその流儀の上で「多譜表スパナ #6」の**残り 1 種 = ottava(8va/8vb/15ma/15mb)**を仕上げるための手順。

---

## 0. どこまで終わっているか(このセッションの成果 = 未 push の連続コミット)

#6「多譜表スパナ」は **hairpin と text spanner を完了済み**。関連コミット(古い順):

| commit | 内容 |
|---|---|
| `refactor(marks): carry StaffIndex on note-attached music marks` | `MusicMarkItem` に `StaffIndex`(収集器 `_currentStaffIndex` から)。**出力不変の基盤**。 |
| `refactor(stacking): stack below-staff elements per staff, not per system` | **`OutsideStaffStacker.StackBelowStaff` を per-(system,staff) 化**。`DynamicLayout`/`HairpinLayout`/`TextSpannerLayout`/`ArticulationLayout` に `StaffIndex` を追加。**この手本をそのまま above 側に適用するのが本タスク**。 |
| `fix(hairpin): support hairpins per staff on a grand staff` | hairpin 完了。fixture `test/multi-staff-hairpins`。 |
| `fix(text-spanner): support rit./accel. spanners per staff on a grand staff` | text spanner 完了。fixture `test/multi-staff-text-spanners`。 |

**ottava だけ未完**。検出(cross-staff ペアリング防止)と engraver Y offset までは実装したが、
**描画で `StackAboveStaff` が per-system のまま**のため revert した(理由は §2)。

---

## 1. 症状(具体 repro)

`Write` で下記を作り `dotnet run --project LilySharp.Cli -- svg scratch\msott.lys C:\temp\msott.svg`:

```
octave absolute
part top { clef treble }
part low { clef bass }
section S {
  top { c'''4@ottava c'''4 c'''4 c'''4@loco | }
  low { c,4@ottava c,4 c,4 c,4@loco | }
}
structure { S }
score "msott" { staff top staff low }
```

現状(ottava の per-staff 対応なし): 2 段目(bass)の `8va` が **上段の上**に積まれる。
`DrawOttavaBrackets`(`SharedRenderer.cs`)に一時 `Console.Error.WriteLine` を入れて実測した値:

```
[DBG ott] text=8va StaffIndex=0 IsAbove=True b.Y=-7.3 sysY=12.0 absY=4.7   ← 上段の上(正)
[DBG ott] text=8va StaffIndex=1 IsAbove=True b.Y=-9.9 sysY=12.0 absY=2.1   ← 上段のさらに上(誤; 本来 ~15=下段の上)
```

engraver が計算した `b.Y`(= `AboveStaffY + staffOffset`)は正しかったが、**`StackAboveStaff` が
両段の 8va を per-system の up-skyline から一括再積層**し、staff1 の 8va を staff0 の 8va の上へ押し上げた。

---

## 2. 真因

- `OutsideStaffStacker.StackBelowStaff` は commit `refactor(stacking)…` で **per-(system,staff) トラッカー**化済み
  (各譜表の底 `StaffBottom + staffOffset` から個別にスタック)。単一譜(offset 0)は旧挙動を完全再現 = byte-identical。
- ところが **`StackAboveStaff`(同ファイル)は per-system のまま**。全 above-staff グロブ(trill/bar number/
  tuplet bracket/**ottava**/text script/volta/rehearsal mark)を **1 システムにつき 1 本の occupancy** に対して
  積層する。しかもその occupancy は **per-system の up-skyline**(音符/符幹/ビームの輪郭)から seed される。
- したがって staff2 の 8va も staff1 の up-skyline を基準に積まれ、上段の上に出る。

**これが下段より厄介な理由**: 下段は定数 `StaffBottom` を seed にしていたので `+ staffOffset` で済んだ。
上段は **実際の音符スカイライン**(`systemSkylines` = per-system の `(up, down)`)が seed。per-staff にするには
**per-staff の up-skyline** が要る。

---

## 3. やること(推奨順・各段 byte-identical を刻む)

### 手順 A: ottava 側の StaffIndex 導通を復元(engraver レベル。revert 済みなので再実装)

下記は既に一度書いて revert したもの。**hairpin/text-spanner とまったく同じ型**。

1. `LilySharp.Core/Svg/Model/OttavaBracketItem.cs` ― record に末尾 `int StaffIndex = 0` を追加。
2. `LilySharp.Core/Svg/Layout/OttavaBracketEngraver.cs`:
   - **`OttavaBracketLayout`** record に末尾 `int StaffIndex = 0` を追加。
   - **`DetectOttavaBrackets`** ― 終端(loco/次 ottava)探索を `ottavaMarks[i+1]`(staff 無視)から
     **同一 `StaffIndex` の次マーク**へ変更(下記)。生成する `OttavaBracketItem` に `StaffIndex: mark.StaffIndex`。
     ```csharp
     MusicMarkItem? terminator = null;
     for (int j = i + 1; j < ottavaMarks.Count; j++)
         if (ottavaMarks[j].Mark.StaffIndex == mark.StaffIndex) { terminator = ottavaMarks[j].Mark; break; }
     // endMeasure = terminator != null ? terminator.MeasureIndex - 1 (clamp) : mark.MeasureIndex + 1;
     ```
   - **`Calculate`** ― 引数 `Dictionary<int,double>? staffYByIndex = null` を追加。各 bracket で
     `double staffOffset = staffYByIndex != null && staffYByIndex.TryGetValue(bracket.StaffIndex, out var so) ? so : 0;`、
     `double y = (isAbove ? AboveStaffY : BelowStaffY) + staffOffset;`。生成する `OttavaBracketLayout` に `StaffIndex: bracket.StaffIndex`。
3. `LilySharp.Core/Svg/Layout/LayoutEngine.cs` ― `OttavaBracketEngraver.Calculate(ottavaItems, systems, ml)` を
   `…, staffYByIndex)` に(`staffYByIndex` は同メソッド内で既に構築済み。hairpin/text の呼び出しが手本)。

**この手順 A 単独ではまだ StackAboveStaff が上書きする**が、単一譜は byte-identical のはず(`staffOffset=0`)。
ここで一度 `dotnet test` 緑を確認してもよい(commit はしない — B とセットで意味を持つ)。

### 手順 B(核心): `StackAboveStaff` を per-(system,staff) 化

`OutsideStaffStacker.cs` の `StackAboveStaff`(§13.6 のとおり above-staff グロブの**最終 Y はここで決まる**)。

1. **per-staff up-skyline を用意する**。現状の seed 源は `systemSkylines`(per-system の `(up,down)`)。
   - 探すべき所: `MultiStaffLayouter.BuildAllStaffSkylines`(既に **per-(system×staff) の `(Up,Down)` を作っている** ―
     `skylineBuilder.BuildStaffSkylines(staff, …)` を staff ごとに呼ぶ)。**この per-staff skyline を StackAboveStaff まで
     配管**すれば、staff2 の 8va を staff2 の音符スカイラインから積める。**まずこの配管経路(誰が per-system に畳んで
     いるか)を grep で確定すること** ― `systemSkylines` の生成箇所と、`BuildAllStaffSkylines` の戻り値の使われ方。
2. **トラッカーを per-(system,staff) 化**。`StackBelowStaff` の `Track(sys, staff)` ローカル関数がそのまま手本
   (dict キー `(int Sys, int Staff)`、seed を per-staff skyline に)。各 above グロブ(ottava/volta/mark/…)は
   自分の `StaffIndex` でトラッカーを引く。**above グロブの多くは現状 `StaffIndex` を持たない**(volta/mark 等)ので、
   `StaffIndex` を持たない型は **staff 0 扱い**にフォールバックしてよい(単一譜=byte-identical を最優先)。
   ottava だけ手順 A で `StaffIndex` を持つ。
3. **段階的に**: 最初は「ottava だけ per-staff、他 above グロブは staff0 固定」で通す。全 above を per-staff にするのは
   スコープ拡大なので**別コミット**に切る(volta/rehearsal-mark を多譜表対応するかは別途方針確認)。

### 手順 C: 検証と fixture

- repro(§1)を再レンダし、DBG で staff1 8va の `absY` が **下段(bass ~22-26)の上 = ~15 付近**になることを確認。
- `test/multi-staff-ottava` fixture を追加(8va を上段、8vb を下段にして above/below 両方を突く案が良い)。
  `SvgSnapshotTests.cs` の一覧に登録 → `LILYSHARP_UPDATE_SNAPSHOTS=1` で生成。
- **単一譜 byte-identical**: `git status --short LilySharp.Tests/Snapshots/` に既存 svg の変更が出ないこと(新 fixture のみ ??)。
- 全 `dotnet test` 緑。

---

## 4. 落とし穴(このセッションで踏んだ)

- **prelim/main の 2 パス**: `LayoutEngine.CalculateAnnotationLayouts` は多譜表で **prelim(staffYByIndex=null)と
  main(populated)の 2 回**呼ばれる。**描画に効くのは main の結果**(hairpin で確認済み)。engraver に DBG を入れると
  両方出るので混乱するが、`Draw*` 側(レンダラ)に DBG を入れれば**最終値だけ**見える。ottava も同様に **`DrawOttavaBrackets`
  に DBG** を入れて確定させること。
- **probe スクリプトの誤検出**: text spanner のとき probe が別グリフを拾って「19.4」と誤報した。**最終判断は必ず
  `Draw*` の `absY` DBG で**(SVG 座標の目視 probe は補助)。
- **8vb(below)の扱い**: below の ottava は本来 `StackBelowStaff` 側で扱うべきだが、現状 **全 ottava が `StackAboveStaff` に
  渡っている**(`LayoutEngine` の `StackAboveStaff(… ottavaLayouts …)`)。8vb を下段スタッカー(既に per-staff)へ回せば
  手順 B なしで 8vb だけ先に正せる可能性がある ― **調査の価値あり**(8vb だけ先行修正 → 8va は手順 B)。

---

## 5. 完了の定義

- 多譜表で 8va/8vb が**各譜表の上/下**に正しく出る(repro の staff1 8va が下段の上)。
- 単一譜 byte-identical。fixture 追加。全テスト緑。
- コミットは「症状 → 真因(StackAboveStaff が per-system)→ LILYPOND-REF(axis-group-interface.cc)→ 実装 → 検証」。
  hairpin/text-spanner コミットのメッセージが雛形。
