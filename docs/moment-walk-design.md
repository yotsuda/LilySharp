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

### 3.1 段 ③ の機構＝`async ValueTask` の coroutine（第969 が測って決めた）

- **数**（計器 Lab `sessions/p969/callgraph.ps1` → `callgraph.txt`・2026-10-23）: `MeasureCollector` の宣言メソッド 314 本
  のうち、moment が進む出口は `MeasureBuilder` の 3 本（`AddItem`・`AddItemWithoutDuration`・`AddDuration`）で、
  それを呼ぶ**種メソッド 18 本**（呼び所 51）、その呼び手を遡った**path 上のメソッド 37 本**（`CollectMeasuresFromNode`
  → `ProcessMusicNodeSequence` → `ProcessMusicNode` → tuplet／grace／cue／repeat／section／form／drums／arpeggio の腕）。
  path 上に `ref`／`in`／`Span` は 0（`in GreenSite` は path 外の static helper 2 本・`stackalloc` は Resume.cs の表 1 か所）。
  `ref struct` は Core に 0。`MusicFont.Current` は `[ThreadStatic]`＝scheduler が**同じスレッドで同期的に継続**する限り無害。
- **形**: path 上の 37 本を `async ValueTask`（戻り値のあるものは `ValueTask<T>`）にし、3 本の出口の**直前**に
  `await _clock.ReachAsync(onset)`（voice の次の item の onset まで五線の時計が進むのを待つ）を置く。
  `VoiceScheduler` は LP の `Global_context::iterate` の形＝全 voice の iterator のうち pending moment が最小のものを
  進め、揃ったら timestep を閉じる（五線状態 S をここで書く）。**単声（段 ③）では `ReachAsync` は常に完了済み＝
  一度も suspend しない＝state machine の box は 1 つも allocate されない**（`async ValueTask` は suspend したときだけ
  箱を作る）。外から見える API（`Collect`・`CollectMultiStaff`）は同期のまま＝中で scheduler を完走させる。
- **なぜ async か**: ⑴ C# の正規の coroutine で再帰がそのまま書ける（`yield` の iterator は各段で子を yield し直す
  必要があり毎回 allocate する）／⑵ スレッドで coroutine を作る形は `[ThreadStatic]` と例外の経路を壊す／⑶ 明示
  スタック機械は 17k 行の再帰を手で平らにする仕事＝綴りが 2 つになる。
- **門（段 ③）**: byte 同一（掃き・snapshot）・full・**割当バイトの before／after**（単声で suspend 0 なら増分は state
  machine の struct 初期化だけ＝回数で読む: `ReachAsync` の呼び出し数＝item 数、suspend 数＝0）。
- **段 ③ で触らないもの**: 増分（Resume／record）は path 上の 37 本の*中*に居る（`TryCaptureWalkCheckpoint` 等の呼び所）
  が、同期的に完了する coroutine では checkpoint の意味は変わらない＝段 ④ の仕事。多声（`BuildExtraVoiceTracks`）も
  従来どおり別 walk＝段 ⑤。

### 3.2 段 ④ の設計＝増分コンパイルは「設計し直し」ではなく 3 か所の調整で足りる（第970・2026-10-23）

**鍵になる事実（実コードで裏取り）**: checkpoint は `voice { }` span の**中では取られない**＝`WalkCarriesNothing`
（`Resume.cs:66-72`）が `_cursor.VoiceScope == null` を要求し、`voice { }` は入れ子にならない（LYS0010）。
voices 2..N は span の中にしか存在しない（`BuildExtraVoiceTracks` が「span の供給する所以外は空の小節」で
全長の track を作る）。⇒ **段 ⑤ で五線の全声部を 1 つの時計に載せても、checkpoint の時点で生きている
iterator は primary の 1 本だけ**＝checkpoint の状態目録（`BuildWalkCheckpoint`・`Resume.cs:121-174`＝builder の
capture・octave・meta・既定音価・ambient tonic・tremolo・臨時記号の記憶・grob prop・journal の watermark・
side table の数・walk-local list の数・小節数）は**そのまま使える**。section プロローグ／エピローグ・form 走査も
primary の 1 本が駆動するので、walk 順の住所（sectionVisit・invocation・nodeIndex）も変わらない。

**⑤ のスケジューラは span 局所**: `ProcessMusicNode` の parallel の腕が、`_parallelSpans` に記録して branch 0 だけ
歩く代わりに、branch 1..N の coroutine（今日の `CollectMeasuresFromNode` の sub-walk と同じ builder・同じ開始枠）を
生み、branch 0（primary の builder にインライン）と一緒に `VoiceClock` で走らせ、span が終わったら全 branch を
揃えて track に継ぐ（今日 `BuildExtraVoiceTracks` が walk の後にやっている継ぎを span の終わりに）。
`VoiceClock.ReachAsync(builder)` の実装＝`Global_context::iterate`: 各 branch は「次の item の onset」を告げて
park し、生きている branch が全部 park したら時計を最小の onset へ進め、timestep を閉じ（五線状態 S を書く
場所）、その onset の branch を再開する。branch は同じスレッドで同期的に継続（`[ThreadStatic]` 無害）。

**増分で変わる 3 か所**:
1. **`_parallelSpans` の退役**: 記録の目的（後から再 walk するための node・frame・duration・meta）は要らなくなる。
   残る読み手は collector 自身と増分（`CollectResumePlanner` 3・`CollectWalkProbe.VoiceWalkRecording.ParallelSpans`
   の re-key・`WalkCheckpoint.ParallelSpanCount`）だけ＝exporter 2 つはコメントで言及するだけで自前の枠規則を
   持ち、`VoiceDefaults` はモデルから span を読む。⇒ checkpoint から `ParallelSpanCount` と re-key を外す。
2. **記録が全声部の小節を持つ**: 今日は primary の `PreFinalizeMeasures`／`builder.MeasuresSnapshot()` だけを採用し、
   voices 2..N は span から**live に作り直す**。⑤ では extras の小節は walk の出力になるので、記録は track ごとの
   小節列を持ち、restore／splice は checkpoint の `MeasureCount`（全 track 同じ長さ＝境界では揃っている）で
   prefix／tail を採用する。
3. **side table の watermark の順序**: extras の出力（dynamics・tuplet・articulation …の voice index つき）が今日は
   primary の walk の*後*に append されるのが、⑤ では時間順に interleave される。append-only は保たれるので
   watermark は単調のまま＝増分の論法は不変。ただし**walk 順に依存する 4 つ**（`_keyByMeasureLog` last-wins・
   `_sectionStartLog` first-wins・`_musicMarks` の重複判定・`_nextBeamId`）は出力が動き得る＝⑤ の byte 同一の門で
   捕まえ、必要なら span の終わりに voice 順で flush して今日の順序を再現する。

**変わらないもの**: suffix splice の状態比較（`SuffixStateMatches`）・prefix restore・`CollectResumePlanner` の窓と
読み集合の論法・`IncrementalCompiler` の record／resume の交代・`CollectEditResumeTests`（resumed == full）が門。

**⑤ の本当の値段＝V 状態の分離**: branch を同時に走らせるには、各 branch が自分の V 状態（§7 の 45 本＝octave・
既定音価・pending 系・句のスタック・grace の作業域 …）を持つ必要がある。今日の sub-walk は collector のフィールドを
**逐次**に save／restore して使い回している。2 つの道:
- **⑤a（swap 式・推奨）**: branch の切り替えごとに V 状態を capture／restore する（`OctaveSnapshot`・`MetadataState.Clone`
  の型を 45 本に広げた `BranchState`）。切り替えは span の中の item ごと＝費用は span の中だけ・フィールド参照の
  書き換え 0。byte 同一の門で閉じる。
- **⑤b（cursor 式）**: V 45 本を `VoiceCursor` オブジェクトに移し walk が `_v.…` で読む。参照の書き換え数百か所＝
  後から ⑤a の上に機械的にやれる（§7 の表がその編集一覧）。
- **⑥（意味論・LP の双子が門）**: 五線状態 S の共有＝臨時記号の記憶を span の中で branch 間で共有し（今日は
  `ResetAccidentalMemory` が span 開始で各 branch を独立にする＋`StaffAccidentals` の後処理が足す）、
  `StaffAccidentals` を退役、全様式で LP と一致させる。**ここで初めて絵が動く**ので byte 同一ではなく LP 双子・
  台帳が門。voice 2 の中の clef／key が五線に効く規則もここ。

**⑤a の実測（第970）**: ⑴ 当初の swap（切り替えごとに `new BranchState`＋`_meta.Clone()`）は restpoly1k を 76.2→85.6 MB にした
→ Meta／Octave／list を参照ごと入れ替え・scalar は branch の record に in place で書く形にして 81.8 → ⑵ span の無い
plain1k が 82.0→83.7 と増えた原因は funnel ではなく（fast path を置いても不変）**parallel の腕の lambda が引数 `builder` を
捕まえ、C# が closure を `ProcessMusicNode` の入口で allocate していた**こと（collect 8.31→9.75 MB・計器 Zz970）→ 腕を
`ProcessParallelSpan` に出して 8.31 に戻った。残る +3〜4 MB（span 本）は branch の park ごとの state machine の箱＋
span ごとの child state＝coroutine の値段（LP の iterator も払う）。⑶ **失った再利用**＝全体が 1 つの span の本（v2bow1k）で
第 2 声部を編集すると、以前は primary の walk 全体を splice して第 2 声部だけ live に作り直せたが、今は子の小節が tail の
一部＝tail の外ではないので splice は decline して live（打鍵 40.3→44.6 MB）。取り戻すには「窓に触れた span だけを再 walk する」
経路が要る＝⑥ で五線状態を共有すると sequential な再 walk は意味を変えるので、今は作らない（ユーザー判断があれば別）。

### 3.3 段 ⑥ の実測（第970・コード変更 0・Lab `sessions/p970/probe6/`＝`.lys`・`lysc ly --pin-fonts` の双子・両者の png）

span の中で五線状態が共有されるべき 4 つの形を Lily#（⑤a）と LP 2.26.0 で描いた:

| # | 形 | LP 2.26.0 | Lily# | 判定 |
|---|---|---|---|---|
| P1 | 既定様式・voice 1 の `fis'` の後に voice 2 の `fis'`（同じ小節） | ♯ は五線で 1 回（Staff の記憶） | voice 2 の `fis'` にも ♯（自分の walk の記憶＋`StaffAccidentals` は足すだけ） | **意図した逸脱**＝オーナー決定 2026-10-02「reminder は正しく無害」。LP どおりにするには決定の取り消しが要る |
| P2 | 同じ本・`accidentals modern` | P1 と同じ絵 | P1 と同じ絵 | この形では差なし（modern の追加規則は別の形で測る） |
| P3 | voice 2 の中の `clef bass` | 以後の voice 1 の音も bass の位置 | **同じ**（voice 1 の音も bass の位置） | 一致＝⑥ の仕事なし |
| P4 | voice 2 の中の `key d major`（voice 1 は `f'` を書き続ける） | 調号 ♯♯ を小節の途中に・次の小節の voice 1 の `f'` に **♮** | 調号 ♯♯ は同じ・**次の小節の `f'` に ♮ が無い** | **欠陥**: voice 1 の walk は自分の key（C）のまま＝描かれた譜は f♯ と読め、鳴るのは f＝読みと音が食い違う。LP は Key_engraver が Staff にあるので voice 2 の `key` が全声部に効く |

⇒ **⑥ の核は P4＝key（と、同じ理屈の時刻つき五線状態）の共有**。P1 は意味論の決定（reminder を保つか）であって機構の話ではない＝
機構としては「五線の記憶を branch 間で共有する」1 本で、reminder を保つなら「自分の walk が決めた臨時記号は消さない」規則を
その上に残す（今の `StaffAccidentals` の ⑴ と同じ文）。P3 は既に一致＝clef の item は五線の時刻順で描画側が読んでいる。

## 4. 段取り（各段に門・段 4 の前にもう一度ユーザー判断）

| 段 | 内容 | 門 | 状態 |
|---|---|---|---|
| ① | 状態フィールド 172 の分類表（V／S／P／W／R／X） | 計器だけ・コード変更 0 | ✅ 第969（§7） |
| ② | **B＝`StaffTimeline`** を建て、`StaffAccidentals`・`StaffAccidentalColumns`・`NoteCollision` の入力がそれを読む | 出力不変（掃き・snapshot・full） | ✅ 第969（`Svg/Collector/StaffTimeline.cs`＝1 小節の全声部の item を (onset, grace rank, voice) の k-way merge で・`VoiceCollector.CollectMeasure` と `StaffAccidentals.Apply` が読む・掃き 1,015 冊 1,248 枚 差 0・網 `StaffTimelineTests` 5 本・毒 3 本が各 2／6／10 赤） |
| ③ | voice 0 だけを iterator 化（多声は従来経路のまま） | byte 同一 | ✅ 第969（§3.1 の形＝path 上の 27 本＋local function 4 本＋`ProcessMusicContainer`・`OpenSoundingItem` を `async ValueTask` に・funnel 51 か所は `MeasureCollector.Clock.cs` の gated wrapper 3 本へ・`Run` が境界＝`CollectMeasures`／`CollectMeasuresFromNode` の呼び手 4 か所・delegate 2 型を `Func<…, ValueTask>` に。svg 掃き 1,015 冊 1,248 枚 差 0・ly／xml／midi 掃き差 0・割当 plain1k 82.1／15.3 MB・fingbeam1k 363.7→363.6・v2bow1k 125.1／40.3・grace200 45.1＝同一・網 `VoiceWalkCoroutineTests` 2 本＝Reaches ≥ item 数・Suspensions 0・`Run` は未完了を拒む） |
| ④ | 増分コンパイルの設計 | 設計書 → **ユーザー判断** | ✅ 設計 第970（§3.2＝checkpoint は span の外だけ＝目録は不変・変わるのは 3 か所）→ ⑤a へ進むかはユーザー判断 |
| ⑤a | span 局所のスケジューラ（`VoiceClock` の本実装）・branch 1..N を branch 0 と同じ時計で・V 状態は切り替えごとに swap・`BuildExtraVoiceTracks` と `_parallelSpans` を退役・記録は全声部の小節を持つ | byte 同一（svg／ly／xml／midi の掃き）・増分 `== full`・割当 | ✅ 第970（`MeasureCollector.Clock.cs`＝`VoiceClock`（span の stack・`ReachAwaitable` struct・park／resume）・`BranchState`（V の swap＝Meta／Octave／list は参照ごと・scalar は in place）・`ProcessParallelSpan`＋`RunSpanAsync`（driver＝LP の iterate）・`_parallelSpans`→`SpanTrack`（子の小節を持つ）・増分は restore＝採用・splice＝shift・比較＝形。掃き 4 種 差 0・網 2,231 緑・割当: span 無し同一（plain1k 82.1・fingbeam1k 363.6・grace200 45.1）、span 本 restpoly1k 76.2→80.1・v2bow1k 125.1→128.1／打鍵 40.3→44.6。**失ったもの**＝第 2 声部の編集で全 walk を splice する v2bow の形（子の小節が tail に入ったので tail の外ではない＝live）。**計器の教訓**＝parallel の腕の lambda が `builder`（引数）を捕まえ、closure が `ProcessMusicNode` の*入口*で allocate されて span の無い本で +1.4 MB／collect＝腕を自分のメソッドへ） |
| ⑤b | V 45 本を `VoiceCursor` へ（任意・機械的） | byte 同一 | |
| ⑥ | 五線状態 S の共有（臨時記号の記憶を branch 間で・`StaffAccidentals` 退役・voice 2 の clef／key が五線に効く） | **LP 双子・台帳**（絵が動く） | |

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
