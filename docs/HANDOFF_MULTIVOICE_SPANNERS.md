# 引き継ぎ (b): #3 多声(polyphony)の slur / tie / glissando 検出・配置

> **✅ 完了(2026-07-01、未 push、5 コミット)。** 検出器を全声部ループ化(声部ごとの open スタック)、
> `LayoutEngine` が `staff.Voices` 全声部 Score を spanner layout に渡し、`ElementCoordinator` が
> `score.Voices[item.VoiceIndex]` で X/障害物/端点を解決。**LP 並置比較で voice-2 スラー/タイの向きが
> 上向き(誤)と判明→声部で向き固定(上声上・下声下、和音タイ含む)に修正**。Glissando の click-to-source
> data-pos も voice-aware 化(`GlissandoLayout.VoiceIndex`＋`ResolveDataPos` の `(staff,voice,measure,item)`)。
> fixture `test/multivoice-spanners`(gliss/slur/単音tie)＋`test/multivoice-chord-tie`。多譜表 polyphony にも効く
> (`staff.Voices` 使用)。**残り = 多声ビーム → `docs/HANDOFF_MULTIVOICE_BEAMS.md`(§4 に本作業を手本として再掲)。**
> 以下は着手前の設計メモ(経緯として保存)。

**前提: `docs/DEV_BUGFIX_WORKFLOW.md` を先に読むこと**(ripple shell / `Write` でファイル化 / master 直 /
LILYPOND-REF / **単一声部 byte-identical 不変条件** / `Co-Authored-By: Claude <current-model>`)。
`§15` に polyphony の背景あり(`voice { … }` → `BuildMultiVoiceScore`、part の既定オクターブ基準)。

---

## 0. 課題(レビュー由来 #3)

**slur / tie / glissando の検出器が primary voice(`Voices[0]`)しか走査していない**。
2 声部の譜表で、第 2 声部内のスラー/タイ/グリッサンドが**まったく出ない**。

これは #6(多譜表スパナ)と**同規模の別サブシステム**。#6 が「マーク → 譜表」だったのに対し、
#3 は「検出 → **声部** → レイアウト」を貫く必要がある。#6 とは独立に進めてよい。

---

## 1. 現状の事実(grep で確定済み、行番号はドリフトするので識別子で再確認)

- 検出器はすべて `score.Voice.Measures`(= `Voices[0]`)のみ:
  - `LilySharp.Core/Svg/Collector/SlurDetector.cs:30` ― `var measures = score.Voice.Measures;`
  - `LilySharp.Core/Svg/Collector/TieDetector.cs:30, 82`
  - `LilySharp.Core/Svg/Collector/GlissandoDetector.cs:34`
- `Score`(`Svg/Model/Score.cs`)は **全声部を露出済み**:
  ```csharp
  public ImmutableArray<Voice> Voices { get; }
  public Voice Voice => Voices[0];
  public bool IsMultiVoice => Voices.Length > 1;
  ```
- レイアウト側(`Svg/Layout/ElementCoordinator.cs`)は `Score` + `staffIndex` を受け、検出器を呼ぶ:
  - `LayoutSlurs(Score, systems, staffIndex)` @ 834 → `_slurDetector.DetectSlurs(score)`
  - `LayoutTies(Score, systems, staffIndex, staff)` @ 598 → `_tieDetector.DetectTies(score)`
  - `LayoutGlissandos(Score, systems, staffIndex)` @ 951 → `_glissandoDetector.DetectGlissandos(score)`
- **`SlurItem` / `TieItem` / `GlissandoItem` は声部識別子を持たない**。
- `LayoutSlurs` は内部で **`score.Voice.Measures` を複数箇所**で使う(スラーの端点ピッチ・X・障害物):
  `EdgeNoteStaffPosition`、`GetItemXOffset`(→ `LayoutUtilities.GetItemXOffset(voice.Measures, …)`)、
  `GetChordHeadXOffset`、`BuildSlurObstacles`。**これらが「どの声部の measures を見るか」を声部対応にするのが本丸**。

---

## 2. 難所 ― なぜ #6 より配管が深いか

- **端点ピッチと曲率**: スラーは start item の符幹方向の**逆**にカーブする(`SlurDetector.StemUpOf`)。
  多声では voice1 = 符幹上・voice2 = 符幹下に**強制**される(`NoteCollision`/voice の StemUp)。
  したがって声部が違えば `curveUp` も端点ピッチ(chord の curve 側の頭)も変わる。**検出時に声部の StemUp を
  正しく読むこと**(`MusicItem.StemUp` は既に voice 強制を反映しているか要確認 ― 反映していれば検出はそのまま
  声部を回すだけ)。
- **X 解決・障害物**: `LayoutSlurs` の X 計算(`GetItemXOffset`)と `BuildSlurObstacles`(encompass 障害物)は
  **同じ声部の measures/items** を見る必要がある。今は `score.Voice.Measures` 直参照。voice を渡す形に変える。
- **broken slur / obstacles**: 直近のセッションで `BuildSlurObstacles`(encompass 障害物を scorer に供給)を実装した
  (`fix(slur): feed encompassed note-head columns to the scorer as obstacles`)。これも `score.Voice` 前提なので、
  声部対応の一環で `voice` 引数化する。

---

## 3. 推奨アプローチ(段階的・各段で単一声部 byte-identical を刻む)

### 段階 1: モデルに `VoiceIndex` を持たせる(出力不変の基盤)

- `SlurItem` / `TieItem` / `GlissandoItem`(`Svg/Model/…`)に末尾 `int VoiceIndex = 0` を追加。
- 検出器はまだ `Voices[0]` のみ走査(`VoiceIndex=0` 固定)。**この段は完全に byte-identical**。
- #6 の `refactor(marks): carry StaffIndex …` と同じ「基盤コミット」。

### 段階 2: 検出器を全声部に回す

- `DetectSlurs`/`DetectTies`/`DetectGlissandos` を **`for (int v = 0; v < score.Voices.Length; v++)`** に。
  各声部 `score.Voices[v].Measures` を走査し、生成する item に `VoiceIndex: v`。
- **openSlurs / open tie スタックは声部ごとに独立**にすること(声部を跨いでペアリングしない ― #6 のハープンで
  「別譜表の cresc で終端」して消えたのと同型のバグを避ける)。声部ループの内側で新規スタックを持つ。
- 単一声部スコアは `Voices.Length==1` ゆえ **byte-identical**。多声スコアで初めて第2声部の item が増える。

### 段階 3: レイアウトを声部対応にする(本丸)

- `LayoutSlurs`/`LayoutTies`/`LayoutGlissandos` の中で `score.Voice.Measures` を使っている箇所を、
  **その item の `VoiceIndex` の measures**(`score.Voices[item.VoiceIndex].Measures`)に差し替える。
  対象ヘルパ: `EdgeNoteStaffPosition`、`GetItemXOffset`、`GetChordHeadXOffset`、`BuildSlurObstacles`
  (と tie 側の `TieFormattingProblem` 供給、gliss 側の `GlissandoEngraver.Calculate` の `score.Voice.Measures` 引数)。
- **X 座標系の一致**: 多声の同一 timing 列は共有カラムに載る(全声部が同じ X)。したがって item の X 自体は
  声部に依らず timing 由来で一致するはず。**声部で変わるのは Y(符幹方向・頭の上下)と衝突判定**。ここを取り違えると
  第2声部スラーが第1声部の頭にアンカーされる。プローブ(§5)で「声部2の端点 Y が声部2の頭に一致」を数値確認。
- **符幹方向**: voice1 上・voice2 下が既に `Voice`/`NoteCollision` で決まっているなら、`SlurDetector.StemUpOf` は
  声部の値を読むだけでよい。要確認。

### 段階 4: 検証と fixture

- fixture: `voice { c4( d e f) } voice { a4 g f e }`(第2声部にスラー)や、tie/gliss 版。**構文は `<< \\ >>` でなく
  `voice { … } voice { … }`**(§13 参照、`<<` は削除済み)。part 名が dynamic 予約語(`p` 等)と衝突しないよう `mel` 等に。
- `SvgSnapshotTests.cs` に登録 → `LILYSHARP_UPDATE_SNAPSHOTS=1` 生成 → 既存 snapshot 無変化(新 fixture のみ ??)を確認。
- 全 `dotnet test` 緑。プローブで第2声部スラー/タイ/グリスの端点が第2声部の頭に付いていることを数値確認。

---

## 4. 落とし穴(予測 + このコードベース固有)

- **声部跨ぎペアリング**: 段階 2 でスタックを声部ごとに分けないと、声部 A のスラー開始が声部 B の閉じで
  ペアされて壊れる(#6 のハープン消失と同型)。
- **単一声部 byte-identical の死守**: 各段で `git status --short LilySharp.Tests/Snapshots/` に既存 svg 変更が
  出ないこと。出たら段階を切りすぎ or 声部インデックスの取り違え。
- **`--no-build` の罠**(§9): Core 変更後に `dotnet run … --no-build` は古い DLL。CLI は自前 Core を同梱するので
  `dotnet build LilySharp.Cli` も要る(§13.2)。
- **多声レイアウトの既存機構**: `NoteCollision`/`VoiceCollector`(`ElementCoordinator.CalculateVoiceOffsets`)が
  既に多声の頭衝突・符幹方向を扱っている。スラー/タイの声部対応は**その出力(声部ごとの StemUp / head offset)に
  乗る**べき。独自に符幹方向を再計算しない。
- **tie は `TieFormattingProblem`、slur は `SlurScoringProblem`**(LP でも最難)。§12-3 の記録どおり「2度和音 + tie/slur の
  headOffset 導通」も未対応。**多声対応と 2度和音対応は別軸**なので混同しない(まず声部を通す。頭変位追従は別課題)。

---

## 5. 完了の定義

- 2 声部の譜表で、第 2 声部の slur / tie / glissando が**その声部の音符**に正しく付く。
- 単一声部 byte-identical。fixture 追加。全テスト緑。
- コミットは段階ごと(モデル → 検出 → レイアウト)に「ビルド緑 → 全テスト緑 → commit」を刻む
  (#6 の e54ca88→…→0e364ea と同じ刻み)。「症状 → 真因 → 実装 → 検証」をメッセージに。
