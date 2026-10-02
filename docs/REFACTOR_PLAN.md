# リファクタの計画（2026-10-02・第739 の調査）

> **このファイルは計画の本文。** 「次の一手」の一覧は `HANDOFF.md` §1.0 だけ（RULES §4）で、そこにはこのファイルへのポインタを 1 行置く。
> 段階が閉じたら、このファイルの該当節に「済み」と commit を書く。全部閉じたら、残す価値のある設計の意図だけを該当する `docs/*.md` に移して、このファイルは消す。

## 0. 結論

今のコードは**保守できている**が、**同じ規則を出力ごとに書き直す構造**が育っていて、変更 1 件あたりの手数と、書き漏らしによる欠陥の両方が増えている。
手を入れる順は **A → B → C**（D は方針の判断が先）。A と B は出力を 1 バイトも変えない整理で、全冊の比較で確かめられる。C が根本対策で、いちばん効くが何便かかかる。
**A・B・C は第740〜748（2026-10-02）で済み**（各節の「済み」・C6＝頁の section の拍子の戻しまで）。残るのは D と、C が型の註に名前を付けて保存した綴りの違い（§5.1 末尾）＝どちらもユーザー判断。

## 1. 保てているもの（壊さないこと）

- テスト 10,745 本（第739 末）。毒で網の効き目を確かめる習慣（RULES §5.0）。第739 の 5 つの変更でも、毒が赤にならなかった網は無かった。
- LP の出典（`LILYPOND-REF`）、全冊の掃き、`-End` の門。変更の影響を数で言える。
- ⇒ **リファクタの各段階も同じ道具で確かめる**（§6）。

## 2. 負担になっているもの（根拠つき）

### 2.1 言語の規則を、出力ごとに別々に実装している（最大）

頁（`MeasureCollector`・`MeasureBuilder`）、検証（`MeasureValidator`・`MeasureModel`）、MIDI（`MidiExporter`）、双子（`LilyPondExporter`）、MusicXML（`MusicXmlExporter`）の 6 か所が、それぞれ自分で次を追っている:

- 拍子（`_timeSignature`／`meter`／`_timeNumerator`／`_timeBeats` …）
- 弱起（partial）
- 小節内の位置
- 音価の引き継ぎ（running duration）

第739 の実例:
- **素の `R`（その小節ぶんの全休符）を入れるのに 6 か所すべてを触った**（`b44667fc`）。
- **MIDI だけが `R1*N` の N を無視していた**（頁は N 小節・MIDI は 1 小節）＝このずれから生まれた既存の欠陥。同じ commit で直した。
- 第737 の「section 頭の書き直しを描かない」規則は、頁と双子に 2 回書かれている（`CollectSectionHeadResumeTests` と `LilyPondExporterSectionPlayTests` が両側を見ている）。

### 2.2 巨大なファイルと、そこへの変更の集中

3,000 行を超えるファイルが 10 個（生成物 2 個を含む）。大きい順に（第739 時点）:

| ファイル | 行 | コメント率 | 9/15 以降の変更回数 |
|---|---:|---:|---:|
| `LilyPond/LilyPondExporter.cs` | 7,861 | 42% | 53 |
| `MusicXml/MusicXmlExporter.cs` | 5,834 | 27% | 54 |
| `Svg/Collector/MeasureCollector.cs` | 5,495 | 43% | 59 |
| `Svg/Layout/ElementCoordinator.cs` | 5,381 | 41% | 62 |
| `Svg/Layout/MultiStaffLayouter.cs` | 5,287 | 51% | 37 |
| `Svg/Layout/OutsideStaffStacker.cs` | 3,485 | 40% | 31 |
| `Svg/Layout/SkylineBuilder.cs` | 3,241 | 51% | — |
| `Midi/MidiExporter.cs` | 3,105 | 36% | — |

（9/15 以降の commit は全体で 777。変更回数の上位 4 つがそのまま最大の 4 ファイル。）

双子には、頁のモデルを読んで「各小節を、記号の onset で区切り、隙間を `s` で埋める」処理が **3 本、ほぼ同じ形**で並んでいる:
- `EmitInlineChordTracks`（和音名）
- `EmitFiguredBassTracks`（第739 で 3 本目として写して作った）
- `EmitLyricTracks`（歌詞・`\skip`）

### 2.3 入れ子の exporter への状態の受け渡しが手書き

phrase・tuplet・grace・cue・repeat の中身は `new LilyPondExporter { … }` に状態を写して書き、写すものは `CarryFrameInto` などにフィールド 1 つずつ並んでいる。

- 第739: 新しく足した `_inlineChordMarks`／`_figureMarks` の写し忘れで、phrase の中の `@chord`／`@figuredBass` が「dropped」と警告されていた（双子には書かれていた）。
- `CarryFrameInto` の註にも、同じ形の穴（phrase の表を写していない入れ子が 4 つあった・2026-08-17）の記録がある。
- ⇒ **写すものを足すたびに同じ事故が起き得る形**。

### 2.4 コメントが経緯の記録を兼ねている

- Core 460 ファイル・246,953 行のうち **44% がコメント行**。
- `Until 2026-…`／`session N`／`MEASURED` の経緯の記述が **3,346 か所**。
- 出典としての価値はある一方、「今の規則は何か」を読み取るのに時間がかかる。
- ⚠️ RULES §4 は「LP の挙動で驚いたこと＝コード内コメント（数値つき）」としている。経緯の記述をどこまで減らすかは**ユーザーの判断**（§D）。

## 3. 段階 A — 入れ子の exporter の状態を 1 つの共有オブジェクトに（小・リスク低）

> **済み（第740・2026-10-02・`166a3857`）**: `SharedState`（共有・private ctor で受け取る）／`PartFrame`（写すだけ）／`StreamFrame`（写して戻す・同じ record）／`OpenNested()`。双子 998 冊 差 0・full 10745。毒と残る穴（stream frame の網は掃きだけ）は HANDOFF §1.1 第740。

**狙い**: §2.3 の「写し忘れ」という種類の事故をなくす。

1. `LilyPondExporter` のうち、入れ子と**共有する**状態（`_phrases`・`_activePhrases`・`_inlineChordMarks`・`_figureMarks` など）と、**入れ子ごとに写して戻す**状態（octave の枠・`_lastWrittenValue`・`_forceNextDuration` など）を棚卸しする。入れ子を作る箇所をすべて列挙する（`new LilyPondExporter` を grep。`CarryFrameInto` の註では 6 か所）。
2. 共有する方を `ExportContext`（仮名）1 つにまとめ、入れ子は参照を 1 つ受け取る。写して戻す方は、明示的な「frame」型にまとめる。
3. 各入れ子の生成を 1 つの工場メソッドに寄せる。

**確かめ方**: 双子の全冊比較でバイト同一（§6 ⑴）＋ full。
**毒**: 共有をやめて新しいオブジェクトを渡す → phrase の網（`LilyPondExporterFiguredBassTests.MarksInAPhrase_AreNotReportedDropped` ほか）が赤。

## 4. 段階 B — 双子を分ける・「頁のモデルを読む流れ」を 1 つに（中・リスク低）

> **済み（第741・2026-10-02・`ee5f306d` 分割＋`da5e3b2c` `EmitTimedStream`）**: 9 本の partial（本体 1,088 行・最大は `.Music` 2,366）。双子 998 冊 差 0・full 10745。毒と残る穴（歌詞の `\skip`・`BreaksMidBar` は suite に網が無い）は HANDOFF §1.1 第741。

**狙い**: §2.2 の最大ファイルを読める単位にする。3 本の写しを 1 本にする。

1. `LilyPondExporter.cs` を役割ごとの partial ファイルに分ける。`MeasureCollector` が既に `.MusicWalk`・`.Form`・`.Annotations`… と分けている型に合わせる。候補:
   - `.Header`（paper・header・fonts）
   - `.Music`（音符・休符・和音・連符）
   - `.Marks`（`EmitMark`・`SplitAttachments`・articulation）
   - `.Streams`（頁のモデルを読む和音名・figures・歌詞）
   - `.Score`（`\score` の組み立て・行）
   - `.Sections`（section play・form・repeat）
   - **移動だけで中身は変えない commit** を先に置く。
2. `.Streams` の 3 本から、共通部分（staff ごとに、記号を `(小節, onset)` 順に並べ、小節を onset で区切り、隙間を埋め、`BreaksMidBar` を扱う）を 1 つの部品にする。記号の書き方（`\chordmode` の entry・`<6 4>`・歌詞の syllable）と隙間の綴り（`s`／`\skip`）だけを差し替え可能にする。
3. ⚠️ `§2.3` の A を先に済ませる（入れ子の状態が分かれていないと、partial に分けたときに写し忘れが見えにくい）。

**確かめ方**: 双子の全冊比較でバイト同一＋ full。行番号の表（`APPROXIMATIONS.md`・`audit/magic_constants.csv`）は再生成する（CLAUDE-OPERATIONS §1）。
**毒**: 部品の「隙間を埋める」を外す → `LilyPondExporterFiguredBassTests`・inline chord・歌詞の網が赤。

## 5. 段階 C — 「小節の文脈」を共有する（大・最も効く）

**狙い**: §2.1 の根本対策。拍子・弱起・小節内の位置・音価の引き継ぎを 1 か所で持ち、MIDI・双子・MusicXML（・検証）はそれを読む。

方針:
- いきなり全部を寄せない。**1 出力ずつ移す**。順番の案は **MIDI → MusicXML → 双子 → 検証**（MIDI が最も小さく、バイト比較が最も簡単）。
- 共有部品の候補は 2 つあり、**最初の便で決める**:
  - (a) **頁のモデル（`MeasureCollector` の結果）をすべての出力が読む**。双子の和音名・figures・歌詞が既にこの形（`PageModel`）。規則の実装が本当に 1 つになる一方、MIDI／XML の構文寄りの情報（書いた綴り）が要る箇所は残る。
  - (b) **構文の walk はそのままに、`BarContext`（拍子・弱起・位置・引き継ぎ）だけを共有する**。差分が小さく、移しやすい。規則（section 頭の扱いなど）は各出力に残る。
  - 第739 の素の `R` は (b) の `Music.BarRest` で「形の判定」だけを共有した。(b) はその延長。
- 各段階の終わりに、その出力の**全冊バイト比較**で差 0 を確かめる（意図した修正は別の commit に分ける）。

**確かめ方**: MIDI は全冊の .mid のバイト比較（**計器が無い＝C の最初に作る**・§6 ⑶）。XML・双子は §6。
**毒**: 共有部品の拍子の更新を 1 か所外す → 移した出力の網が赤になること。

### 5.1 第742 の値付け（2026-10-02・ユーザー判断待ち）

- **計器を先に作った**: MIDI の全冊バイト比較＝Lab `sessions/p742/sweep-midi.ps1`（998 冊・1,199 枚・SHA256・約 20 秒）。A/B の前後の HEAD で差 0（MIDI は触っていない＝計器の健全性の確認）。
- **(a) 頁のモデルを全出力が読む＝採らない**: MIDI・MusicXML は*演奏される順*（form・反復・volta・section play の serial）で歩くが、頁のモデルは *score ごとの印刷の形*（staff ごとの小節・`BreaksMidBar`・padding・courtesy・text row・combined staff の silence の選択）。MIDI の walk（3,105 行の大半: tie の lane・grace の steal・tuplet・phrase の ordinal）を頁の形の上に書き直すことになり、1 出力でも何便かの書き直し＝音に無関係な決定が混ざる。双子の 3 stream が頁を読むのは「頁が置いた位置を写す」目的で、音楽の規則の共有ではない。
- **(b) `BarContext`＝推す**: 各出力が自前で持つ「拍子（beats／beatType／text／senza）・弱起・小節内の位置・音価の引き継ぎ（value／dots）」と、その規則（section 境界で home へ戻す・header の time／partial を当てる・`time none`・弱起は最初に閉じる小節で使い切る・span／grace／phrase／tuplet で音価の記憶を保存／復元）を 1 型にして、各出力はそれを読む。写しの数（第742 の grep）: 音価の引き継ぎの代入 MIDI 10／XML 9／頁 24・section 境界の meter 戻し 5 か所・**section header の registry の構築 5 か所**（collector `Definitions.cs:419`・MIDI `Export`・XML `BuildSectionHeaderRegistry`・双子 `BuildSectionHeaderRegistry`・validator `CollectSectionTimes/Partials`＝同じ規則「inline music の無い宣言の first direct key／time／tempo／partial・先勝ち」）。
- **提案する順**: **C1 `Semantics.SectionHeaders`**（registry の構築を 1 本に・5 か所が読む。(a)(b) どちらでも要る・出力 3 種の掃き＋import の往復で差 0）→ **C2 `BarContext` の型を MIDI に**（`MeasureTicks` と頁の `EmitEmptyMeasure` の長さの一致を型で保証）→ C3 XML → C4 双子 → C5 検証・頁（頁は打鍵ごとの walk と `Resume` の checkpoint が `_defaultDuration` を持つ＝最後・perf の規則＝ユーザーに一声）。
- **毒の形**: 共有部品の section 境界の meter 戻しを外す → 移した出力の網が赤（MIDI は `SectionBoundaryMeterRevertTests` の族）。registry の構築では「inline music の有無」の判定を外す → 5 出力の section header の網が赤。
- **C1 済み（第742・2026-10-02・`0193be8e`）**: `Semantics.SectionHeaders`（`Read`・4 表・`DirectivesOf`・`FirstDirect<T>`）。頁・MIDI・XML・双子の 4 builder を消した。4 出力の掃き 998 冊で差 0。validator の `CollectSectionTimes/Partials` は規則が違う（後勝ち）＝C5 で。毒: inline music の除外を外す 21 赤／partial 表なし 16 赤／**先勝ち→後勝ち 0 赤・掃きも 0 冊差＝観測者なし**（HANDOFF §1.1 第742 ⑵）。
- **C2 済み（第743・2026-10-02・`776dadb7`）**: `Semantics.BarContext`（`Meter`・`HomeMeter`・`Partial`・`SetTime`・`OpenSection`・`SetPartial`・`SpendPartial`・`BarLength`・`MeterLength`）を MIDI に。MIDI 998 冊 差 0。毒 1／5／0+0／1。MIDI と頁でまだ違う 2 つの綴り（弱起が閉じる前の 2 つ目の `partial`・`time none`）は型の註＝C5 の判断材料。C3（XML）は text（`3+2`）と senza の面を型に足してから。
- **C3 済み（第744・2026-10-02・`b29bae2b`）**: 型に `Meter.BeatsText`・`SenzaMisura`・`HomeSenzaMisura`・`CaptureHome`・`RevertToHome`・`SetMeter` を足し、XML の 5 field（拍子の 4 つ＋`_homeTime`）を消した。XML の弱起（`_pendingPickup`／`_pickupLength`＝書いた音価の累積で閉じる）は独自のまま＝3 つ目の綴りとして型の註に。XML・MIDI 998 冊 差 0。毒 4／10／0+1（senza は `test/senza-misura` だけが観測）／0+0（加算拍子の文字列は観測者なし）。
- **C4 済み（第745・2026-10-02・`2f544c8d`）**: 双子の 8 field（拍子 4・home 3・`_twinPartial`）を `_bars` に。`TimeText` は走っている `Meter` から導出。入れ子の frame は `BarContext.MeterState`（`Save`／`Restore`）で拍子の半分だけ運ぶ（弱起は従来どおり運ばない）。ly・xml・midi 998 冊 差 0。毒 9／4／0+0／0+0。**次は C5＝判断が先**: 弱起の綴り 3 つ（頁＝先勝ち・小節線で消費／MIDI・双子＝置き換え・小節線で消費／XML＝音価の累積で閉じる）と `time none` の 2 つ（頁＝時計を凍らせる／他＝拍子を保つ）を揃えるか、違いのまま型に載せるか。
- **C5 (iii) 済み（第746・2026-10-02・`0ca123b9`）**: ユーザー決定「iii で始めて」＝validator だけ・頁は載せない。validator の 3 field（`_timeSignature/_meterText/_senzaMisura`）を `_bars` に（`MeterText` は pair から導出・block ごとの save/restore と voice span の memo は `MeterState`）。弱起は per-bar の読み（pending 状態でない）・header registry は独自の規則のまま。top-level の `time none` が後の top-level `time` で解けない従来の形は `SetMeterLeavingSenza` と名付けて保存。3 出力 998 冊 差 0・診断 log 998 冊 同一。毒 5／9／44。残る判断は次の項（C5 頁）の末尾に。
- **C5 頁 済み（第747・2026-10-02・`a49b2abc`）**: ユーザー選択（4 択から）。`MeasureBuilder` の 3 field（`_timeSignature`＝走っている小節長・`_senzaMisura`・`_partialRestore`＝弱起中に parked した拍子）と checkpoint の 3 項を `_bars` に。読みは全部 `BarLength`（弱起が pending なら弱起・でなければ拍子の 1 小節）、`SetMeasureLength(Fraction)` → `SetMeter(Meter)`（collector は pair と加算の文字列を渡す・ctor も `Meter`）、checkpoint は `MeterState`＋`Partial`。`time none` が凍らせる clock（`_frozenPosition`）は頁の clock の話＝builder に残す。**保存した頁固有の規則**: 弱起が pending のまま来た `time` は弱起を新拍子の 1 小節に張り替え、閉じたとき partial 前の拍子に戻す（新拍子は捨てる）＝`BarContext.SetMeterRearmingPickup` と名付けて保存（他の 3 出力は `SetTime` で拍子を動かす）。perf: `BarContext` が拍子の 1 小節をキャッシュ（`Fraction` の ctor は GCD・頁は item ごとに読む）＝hot read は field 1 回のまま・割当は builder 1 つに BarContext 1 個・新しい走査なし。svg 1,199 枚・ly・mid・xml 998 冊 差 0・full 10745。毒 13／0／16／10／2／88／1／1（0 は「弱起中の `time`」＝svg・ly の掃きでも 0 冊＝観測者なし）。**註の訂正**: 第742〜746 の「頁は 2 つ目の `partial` を無視して最初を保つ」は `??=` の読み違い（parked されるのは戻す拍子・弱起の field は上書き）＝全出力で置き換え。「`time none` で頁は時計を凍らせる」は拍子の綴りの差ではない（全出力が最後の有拍子を保つ）。**段階 C は完了＝読み手 5 つ全部が型に載った。残る判断**（1 件ずつ別 commit・網つき＝ユーザー判断）: (i) 綴りを揃える＝弱起中の `time`（頁・観測者なし）・XML の弱起（音価の累積で閉じる）・validator の header registry の後勝ち・top-level の senza が解けない／(ii) は C6 で済み（次の項）。
- **C6 済み（第748・2026-10-02・`67d5ef06`）**: 頁の section 境界の拍子の戻し（collector の `_sectionResetTime*` 4 field＝毎 voice `_scoreTime` から写すだけの複製）を builder の `BarContext.HomeMeter` に（`SetHomeMeter`＝builder の生成地点 2 か所で `_scoreTime` から武装・`RevertMeterToHome`）。**保存した頁固有の規則**: 戻しを描くかの判定は pair ではなく**約分した小節長と `time none`**（`BarContext.LeftHomeByLength`・2/2→4/4 は描かず 2/2 のまま・弱起 pending なら弱起で比べる）。svg 1,199 枚・ly・mid・xml 998 冊 差 0・full 10745。毒 9／0／3／0／0／0（restored 0）: 0 のうち「pair で比べる」は **svg の掃きで 1 冊差**（`Locked out of Heaven`＝`section Body_1 { partial 2 }` の body が `r8` で bar 無し→次の境界で弱起 1/2 が pending→頁は 4/4 を描き直して弱起を 1 小節に張り替える・pair なら描かない）＝観測者あり（コーパス 1）・LP なら \time を書かない限り描かない＝(i) の材料。「SetMeter 落とし」0 は AddItem の二重の保証（`time none` の home の pair だけが依存・無観測）、「`_meta` から武装」0 は恒等写像（毒の設計ミス）、「sub-voice を武装しない」0 は sub-voice が section を歩かないため。**段階 C は C6 まで完了。残る判断**（1 件ずつ別 commit・網つき＝ユーザー判断）: (i) 綴りを揃える＝~~弱起中の `time`（頁）~~ **第751・`86fe6fc8`（A1）**・~~section 境界の戻しの判定~~ **第750・`1f804723`（`BarContext.LeftHome`）**・~~top-level の senza が解けない（validator）~~ **第751・`86fe6fc8`（A4）**・~~validator の header registry の後勝ち~~ **第754（A3）**: validator の 2 表を `SectionHeaders` に載せた（頁と同じ offering＝chords／lyrics track の cell を除く全宣言・先勝ち・`part` 直下の directives-only cell も header＝その `time` は文書の拍子を動かさない）。`lysc check` 998 冊 差 0・網 6（`SectionHeaderRegistryValidatorTests`）・header 重複の診断は別の文法判断のまま。~~XML の弱起~~ **第755（A2）**: XML の `_pendingPickup`／`_pickupLength` を `BarContext.Partial` に載せ、閉じた小節線で `SpendPartial`（宣言長での自動 close は頁の `AddItem` と同じで残す）＝宣言より短い弱起が `|` を越えて次の小節の途中で閉じていた形が頁・MIDI・双子と揃った。xml の掃き 998 冊 差 0（コーパスにその形は無い）・網 5（`MusicXmlPickupBarLineTests`）。**綴りの違いは全部閉じた＝残るのは D（§7）だけ。** 裁定の規則は 2026-10-02 のユーザー決定（RULES §5.2「意味論の裁定者は音楽的な妥当性」）。

## 6. 確かめ方（計器）

1. **双子（.ly）の全冊比較**: Lab `sessions/p739/twin/sweep-ly.ps1 -Base <base の lysc>`（998 冊・1,199 枚・警告の行数と .ly の差分を数える・約 30 秒）。base の lysc は HEAD の worktree をビルドして**掃きの出力先と別のフォルダ**に置く（第739 で `base` に置いたら掃きが消した）。
2. **頁（SVG）の全冊比較**: Lab `sessions/p723/svg2/sweep-all.ps1`（第739 の写しは `sessions/p739/svg3`）。A・B・C は頁を変えないはずなので、念のため。
3. **MIDI の全冊比較**: 無い。C の最初に `sweep-ly.ps1` の型で作る（`lysc midi --batch`・バイト比較）。
4. **MusicXML の import の往復**: Lab `sessions/p734/imp/rt.ps1`（第739 の写しは `sessions/p739/imp2`）。XML 自体のバイト比較は `sweep-ly.ps1` の `ly` を `xml` に替えれば作れる。
5. 各段階で `-End` の門・full・毒（RULES §5.0）。

## 7. 段階 D — コメントの経緯部分の扱い（ユーザー判断が先）

選択肢:
- (a) **今のまま**: 経緯もコードに残す。
- (b) **規則・LP の出典・「なぜ」だけを残し、経緯（`Until 20xx`・`session N`・過去の測定の物語）は HANDOFF-ARCHIVE か commit message に移す**。1 ファイルずつ。触ったファイルから順に。
- (c) (b) を新しく書くコメントにだけ適用し、既存は触らない。

⚠️ RULES §4 の表（「LP の挙動で驚いたこと＝コード内コメント（数値つき）」）と §5 の書き方の規則に関わるので、(b)／(c) を選ぶなら RULES の該当行も一緒に書き換える。

## 8. やらないこと

- 振る舞いを変える修正を、リファクタの commit に混ぜない（見つけたら別の commit・別の網）。
- 生成物（`GlyphSkylinesGenerated.cs`・`GlyphMetricsGenerated.cs`）は対象外。
- perf の島（HANDOFF §1.0 ⒝・一時停止中）とは独立。C で打鍵ごとの経路（`IncrementalCompiler`）に触る場合は、RULES の perf の規則（回数で測る・ベンチの前にユーザーに一声）に従う。
