# 多声 walk の moment 順への再設計 — 設計（docs/moment-walk-design.md）

起票: HANDOFF §2 C ⑴（第103・2026-08-07）「多声 walk の moment 順への再設計＝最大の構造負債」。
**ユーザー決定（2026-10-23・第968〜969）: 案 A（moment 順の walk）を終点に、案 B（五線時間順の merge 層）を
その段 0 として、5 段に門を置いて進める。** 第969 が設計と判断材料を書き（Lab `sessions/p969/` → ここへ）、
同日に段 1 を閉じた。各段の経緯は HANDOFF §1.1 の各便と、閉じたら ARCHIVE。

## 0. 判断材料（第969・2026-10-23 の裏取り）

- 起票が挙げた欠陥クラス 3 つのうち、**⑴ 声部横断の復元♮の欠落は第915（2026-10-20）が
  `StaffAccidentals`（五線ごとの後処理・時間順・足すだけ）で閉じ、⑵ cue の二重 walk は第98 で閉じた**。
  残るのは **⑶「per-walk whitelist の drift」という一般論＝今日、踏む本は 0 冊**。
- 現行の walk は **`MeasureCollector` 9 ファイル 17,306 行・インスタンス フィールド 172**（§7 の表）。
  **増分コンパイル（`MeasureCollector.Resume.cs`・`CollectResumePlanner`・`CollectWalkProbe`＝小節境界の
  checkpoint と suffix splice）が「声部ごとの逐次 walk」を前提**に建っている。
- それでも A を終点にする理由（ユーザーの問い「最終的に優れた実装を得たい。A と B はどちらが良いか」への答え）:
  ⑴ LP の形そのもの＝五線の状態が timestep の終わりに 1 回書かれるので、声部の数と書く順序に依存する
  欠陥のクラスが構造的に消える／⑵ B（後処理）では届かない規則がある＝`StaffAccidentals` は既定の臨時記号
  様式だけで動き `modern` 等では何もしない（先頭の判定）、voice 2 の中の clef・key 変更が五線に効く等は
  「walk の途中で五線状態が要る」ので後処理では書けない／⑶ 後処理を重ねるほど「同じ時間順を各自で
  組み立てる」綴りが増え、§2 A の主題（同じ量の N 綴り）に戻る。
- B が A の段 0 である理由: A のスケジューラが毎 moment に作る「この五線のこの時刻に居る (voice, item) の
  列」は B の `StaffTimeline` と同じデータ。先に建てて後処理 3 つに読ませれば五線時間順の意味論が網で
  固定され、A で walk を差し替えるときの byte 同一の門がそのまま使える。

## 1. 現行の構造（実コードで裏取り・2026-10-23）

- **voice 0 は本流にインライン**（`ProcessMusicNode` → `MeasureBuilder`）。`<< >>`／`voice { } { }` は
  `_parallelSpans`（`MeasureCollector.cs:262`）に (node・開始小節・offset・octave frame・既定音価・付点・
  `MetadataState` の snapshot) を記録し、本流は voice 0 の branch だけを歩く。
- **`BuildExtraVoiceTracks`**（`MeasureCollector.cs:2771-2858`）が span ごとに voice t を**別の walk**で歩く:
  `CursorScope` で cursor を差し替え、octave／duration／dots／meta を **span が開いた時点に復元**してから
  `CollectMeasuresFromNode`。設計意図は「各声部は span が開いた枠から独立に評価＝順序非依存・どの声部も
  次を引きずらない」（同所の註）。
- **五線レベルの状態は walk ごと**: 臨時記号記憶 `_measureAccidentals`、clef／key／meter の `_meta`。
  五線の時間順で決まる量は**後処理で足す**:
  - `StaffAccidentals.Apply`（第915・`Svg/Collector/StaffAccidentals.cs`）: 全声部の head を小節ごと・onset 順に
    並べ、別の声部が先に示した変化量との差に臨時記号を**足す**（取り去らない）。既定の様式だけ。
  - `StaffAccidentalColumns.Resolve`（列の詰め・collect 時）／`ElementCoordinator.CalculateVoiceOffsets` と
    `NoteCollision`（layout 時）＝§2 A「多声の譜が 2 周する」の 2 つ。
- **増分コンパイル**: checkpoint は小節境界で `WalkCarriesNothing()`（`Resume.cs:66-72`＝`_pendingGrace`・
  `_cueDepth`・`VoiceScope == null` …）のときだけ・`_parallelSpans` の数と各項の照合（`Resume.cs:795-799`）・
  suffix splice は本流の記録を再利用。**`_parallelSpans` の読み手は Core 8 ファイル**（collector 4・増分 2・
  `LilyPondExporter.Music`・`MidiExporter`・`VoiceColumn`）。

## 2. LP の形（引用）

- `lily/global-context.cc:137-213 Global_context::iterate`: moment `w` を 1 つずつ進め `iter->process (w)`
  → 全 iterator が同じ moment を処理 → `OneTimeStep` → finalization。
- `lily/simultaneous-music-iterator.cc:97-115 Simultaneous_music_iterator::process`: 子（各 Voice の
  Sequential_iterator）のうち pending moment が `until` のものだけを同じ `until` まで進める。
- engraver は文脈ごと（`Accidental_engraver`・`Key_engraver`・`Clef_engraver` は Staff、`Timing_translator` は
  Score＝`ly/engraver-init.ly`）に **timestep の終わりに状態を書く**＝同時の声部は互いを見ない
  （`StaffAccidentals` もそう実装している）。

## 3. 終点の形（案 A）

- 各 voice branch を iterator（`IEnumerator<WalkEvent>` か明示スタック）にし、スケジューラが最小の pending
  moment を持つ iterator だけを進める。**声部固有の状態（§7 の V）は iterator へ**、**五線の状態（S）は
  timestep 単位で共有して更新**、**part の form 走査（P）は五線の全声部を駆動する 1 本**、**収集全体の
  定義・設定・出力（W）はそのまま**。`_parallelSpans`（X）は退役。
- **門（全段共通）**: snapshot 249 枚・掃き 1,015 冊 1,248 枚が byte 同一／full 緑／増分の
  `incremental == full`（`IncrementalCompilerTests`・`CollectEditResumeTests`）／exporter の掃き（ly・xml・midi）。
- **回数で見た値段**: 1 回の collect の walk 回数は今「1 ＋ span の数」、後は 1。増分の再利用率は段 4 の設計で決まる。

## 4. 段取り（各段に門・段 4 の前にもう一度ユーザー判断）

| 段 | 内容 | 門 | 状態 |
|---|---|---|---|
| ① | 状態フィールド 172 の分類表（V／S／P／W／R／X） | 計器だけ・コード変更 0 | ✅ 第969（§7） |
| ② | **B＝`StaffTimeline`** を建て、`StaffAccidentals`・`StaffAccidentalColumns`・`NoteCollision` の入力がそれを読む | 出力不変（掃き・snapshot・full） | |
| ③ | voice 0 だけを iterator 化（多声は従来経路のまま） | byte 同一 | |
| ④ | 増分コンパイルの設計（checkpoint＝全 iterator の状態の組） | 設計書 → **ユーザー判断** | |
| ⑤ | 多声をスケジューラへ・`BuildExtraVoiceTracks` を退役・exporter と `_parallelSpans` の読み手を移す | byte 同一・増分 `== full`・exporter の掃き | |

## 5. 案 B・案 C（判断材料として残す）

- **B**: 後処理 3 つが各自で「五線の全声部の item を時間順に並べる」のを `StaffTimeline`（五線ごと・小節ごと・
  onset 順の (voice, item, grace rank) 列）1 本にする。walk と増分は触らない。1 便・出力不変。
  弱点: moment 順の walk ではない＝「walk の途中で五線状態が要る」規則には届かない。→ **A の段 ② として採用**。
- **C**: 待つ。踏む本が 0 冊。→ 採らない（ユーザー決定）。

## 6. 設計上の観察（段 ③〜⑤ で効く）

- **出力の side table（W）は小節番号か source position を鍵に append-only**＝声部を interleave しても
  壊れない。**例外＝walk 順に依存するもの**: `_keyByMeasureLog`（replay は last-wins＝「後の声部の
  transposed な再記録が勝つ」）・`_sectionStartLog`（first-wins）・`_musicMarks` の重複判定・`_nextBeamId`
  （番号の払い出し順）。段 ⑤ で順序を固定する（timestep 内は voice 番号順）。
- **`MetadataState` は 3 つの層が 1 つの箱に入っている**＝header／paper／fonts／layout（W）・Time*（Score の
  clock＝W）・Key*／Clef（Staff＝S）。段 ③ で割る。
- **`CollectionCursor`** も 2 層＝`StaffIndex`（S）と `VoiceIndex`／`VoiceScope`／`MetadataMeasureOffset`（V）。
  `MetadataMeasureOffset` は「sub-voice を 0 始まりの builder で歩く」ための補正なので、スケジューラが
  実小節で歩けば **消える**（§7 の V の中で唯一、移すのではなく無くなる座標）。
- **`WalkCarriesNothing`**（checkpoint の資格）は V の pending 系だけを見ている＝段 ④ で「全 iterator が
  carry なし」に読み替えればそのまま使える。

## 7. 状態フィールドの分類表（段 ①・第969・計器 Lab `sessions/p969/fields.ps1` → `fields.txt`＝172 本・註つき）

分類: **V**＝声部の cursor（iterator へ移る）／**S**＝五線の状態（timestep の終わりに書く・共有）／**P**＝part の
form 走査（全声部で 1 本）／**W**＝収集全体（定義・設定・出力の side table・警告）／**R**＝増分（record／resume・
段 ④ で設計し直す）／**X**＝退役。

**V（45）**: `_octave`（OctaveContext）・`_defaultDuration`・`_defaultDots`・`_cursor` の `VoiceIndex`／`VoiceScope`／
`MetadataMeasureOffset`（後者は消える・§6）・`_pendingGrace`・`_pendingGraceAt`・`_pendingLeadingGrace`・`_graceDepth`・
`_cueDepth`・`_cueRegionPending`・`_percentCoveredDepth`・`_openPhrases`・`_phraseCallSites`・`_phraseTransposeSaves`・
`_phraseAnchorSaves`・`_phraseAbsoluteBaseSaves`・`_tremoloRepeatCount`・`_tremoloPairShape`・`_tremoloPairFirst`・
`_pendingEmptyChordSlurStart`／`End`／`StartSource`／`EndSource`・`_spreadNote`・`_sectionResetKeySharps`／`Custom`・
`_sectionResetClef`・`_sectionResetOverrides`・`_sectionActiveGrobProps`・`_ambientTonicStep`／`Alter`／`Valid`・
`_openingKeyOverride`・`_pendingInlineVoltas`・`_resolvedChordMembers`・`_resolvedNotes`・`_lastEndsOffTheBar`・
grace body の作業域 6＝`_carriedGraceBreathStart`・`_innerGraceBreathStarts`・`_innerGraceBreathItems`・`_graceTupletSpans`・
`_graceChordNames`・`_graceFrameSpecs`。

**S（11＋`_meta` の一部）**: `_measureAccidentals`・`_accidentalBar`・`_accidentalOrder`・`_advanceAccidentalBar`・
`_keyCourtesyBuilder`／`Staff`／`Measure`／`LineClef`／`Seen`（Key_engraver は Staff）・`_cursor.StaffIndex`・`_nextBeamId`
（五線内で一意）・`_keyByMeasure`＋`_keyByMeasureLog`（和音の度数の調）・**`_meta` の `Key*`／`Clef`／`InitialKey*`／
`InitialClef`**。

**P（14）**: Form.cs の `_pendingRole`・`_pendingRunStart`・`_pendingRunCount`・`_pendingRewind`・`_pendingPasses`・
`_pendingMarks`・`_formTrailingMarks`・`_pendingFormEdge`・`_formDividerOpen`／`Closed`・`_dividerEnding`・`_meterCursor`・
`_sectionStartLog`・`_voiceName`、＋ Annotations.cs の part 既定 6＝`_partTuning`・`_partFrettedTuning`・`_partShapeTuning`・
`_partSoundingShift`・`_chordFacts`・（`_chordDiagramsWord`／`All`／`_chordDiagramTable` は score 級＝W）。

**W（約 80）**: 定義＝`_sectionState`・`_variables`・`_form`・`_filePartial`・`_root`・Definitions.cs の file 級 12
（`_fileTransposeDefault(+Seen)`・`_fileIsConcert(+Seen)`・`_fileShiftSemitones`／`Seen`／`Interval`・`_fileClef(+Position)`・
`_sectionDeclarationsInOrder`・`_phraseDeclarationsInOrder`・`_variableDeclarationsInOrder`・`_phraseGreens`）・
`_sectionHeaders`・`_canonicalSectionBars`・`_canonicalByName`・`_meterPlan`・`_gatherContainerSite`・`_writtenBarPhrases`・
`_writtenBarAt`／設定＝`_accidentalStyle`・`_chordSpelling`・`_drumOverrides`・`_drumPlans`・`_scoreTime`・**`_meta` の
header／`Fonts`／`Paper`／`LayoutPlan`／`Time*`／`Tempo*`／`Swing*`**・`_chordDiagramsWord`／`All`／`_chordDiagramTable`／
出力＝`_dynamics`・`_articulations`・`_graceNotes`・`_lyricsCollector`・`_lyricsRowNames`・`_voiceMeasuresByName`・
`_musicMarks`（＋`_musicMarkPositions`・`Synced`）・`_customTexts`・`_voltaBrackets`・`_rowsOnlyFormBars`／`Breaks`／`GridBars`・
`_offBarVoices`・`_tupletBrackets`・`_arpeggios`・`_pitchTrace`・`_tabResolver`・`_figuredBasses`・`_chordNameCollector`・
`_percentRepeats`・`_crossStaffItems`・`_grobOverrides`／`Reverts`・`_trillSpannerEvents`・`_courtesySourcePositions`・
`_memberEditorialBySource`・`_fingeringByPosition`・`_markHostMeasure`・`_harvestedVoices`・`_midBarBreakRequests`／
`Refusals`／`Conflicts`・`_cumulativeSideTables`・`_expansionBudget`（＋`ExceededAtField`）／警告＝`_sanityScannedVoices`／
`List`・`_tieTargetWarnings`・`_unpairedSlurWarnings`・`_shadowedRehearsalMarks`・`_condensedStaffChangeWarnings`・
`_cueSpanBoundaryWarnings`・`_unpairedBeamWarnings`・`_unpairedRepeatWarnings`。

**R（18）**: `_walkOrdinal`・`_invocationInSection`・`_sectionVisit`・`_formRepeatDepth`・`_probeRecording`・`_resumePending`・
`_sectionStartMeasureForResume`・`_resumeRestoredSectionStart`・`_suffixPlan`・`_suffixTargets`・`_suffixWindow`・
`_suffixSpliced`・`_spliceCandidateStart`・`_walkMaxSourceRead`・`_walkHeaderReads`・`_walkCanonicalReads`・`_formBarRead`・
`_repetitionOriginalReads`。

**X（1）**: `_parallelSpans`（＋読み手 8 ファイル・§1）。

⚠️ 表は 2026-10-23 のスナップショット。段 ③ 以降で動かすときは `fields.ps1` を回し直して差分から読む。
