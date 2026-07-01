# 引き継ぎ (c): 多声(polyphony)のビーム ― 第 2 声部の連桁

> **✅ 完了(2026-07-01)。** 第 2 声部の 8/16 分がその声部の音符に連桁され、**下声ビームは下・上声は上**。
> LP 2.24 並置で連桁のグルーピング・下方配置・符幹下向き・beamlet 方向すべて一致を確認。単一声部＋既存多声
> fixture は byte-identical(既存多声 fixture は 4 分/2 分のみで元々連桁しないため無変化)。全 1978 緑 / 3 skip。
> コミット(古い順):
> - `refactor(beams): carry VoiceIndex on beam groups` ― `BeamGroup.VoiceIndex`(byte-identical 基盤)。
> - `feat(beams): beam every voice, not just the primary` ― 検出器を全声部ループ＋**声部で符幹方向固定**
>   (voice1 上/voice2 下、forced 時 auto-knee 無効)、`ElementCoordinator.LayoutBeams` を `score.Voices[VoiceIndex]`
>   解決、`LayoutEngine` が全声部 score をビームへ(最終＋prelim spacing)、レンダラの `beamedItems` を
>   `(staff,voice,measure,item)` 化して声部ごとに flag 抑制。fixture `test/multivoice-beams`。
>
> **選択肢 A(collection で `StemUpOverride` 焼込)は採らず**、検出器で声部方向を固定した(§2 の「layout と render が
> 同じ符幹を見る」を、render が既に voice で強制していることを利用して満たす=より小さい変更で layout/render 整合)。
> **後続で対応済(2026-07-01、この handoff 以降の別コミット)**: 声部/譜表を跨ぐ tuplet 境界の誤分割(`VoiceIndex`/`StaffIndex` で
> フィルタ)、下声部の tuplet ブラケットがステム下側に来ない件、**cross-voice の beam collision(垂直=ビームが他声部の音を避けて上がる)**。
> **残る既知の非対応**: 交差声部の**水平** note-collision(下声部の高音が上声部の符幹を clear しない)＝ `DEV_BUGFIX_WORKFLOW.md` §12-7 に評価・deferred を記録。
> 残りの本書(§0〜§6)は着手前の設計メモとして保存。**push は保留中**(未 push スタックに本作業 3 コミットが積まれた)。

---

**前提: `docs/DEV_BUGFIX_WORKFLOW.md` を先に読むこと**(ripple shell / `Write` でファイル化 / master 直 /
LILYPOND-REF / **単一声部 byte-identical 不変条件** / `Co-Authored-By: Claude <current-model>` / **push は保留中**)。
本書は #3(多声の slur/tie/gliss)の**続きで別軸**。#3 の spanner 対応が手本になる(§4 参照)。

---

## 0. 課題

2 声部の譜表で、**第 2 声部の 8 分音符以下が連桁(ビーム)にならず、旗(flag)のまま**出る。
`LayoutBeams` が primary voice(`Voices[0]`)しか見ていない。#3 の slur/tie/gliss とまったく同型の欠落だが、
**ビームは符幹方向とビーム Y の確定を伴うので #3 より深い**(下記 §2)。

### repro(`Write` で作り `dotnet run --project LilySharp.Cli -- png scratch\mvb.lys C:\temp\mvb.png`)
```
octave absolute
part mel { clef treble }
section S {
  mel {
    voice { c''4 c'' c'' c'' | }
    voice { e'8 f' g' a' b' a' g' f' | }
  }
}
structure { S }
score "x" { staff mel }
```
現状: 下声の 8 分音符が旗のまま(ビームなし)。期待: 拍ごとに連桁され、**下声のビームは音符の下**に出る。

---

## 1. 現状の事実(grep で確定済み、行番号はドリフトするので識別子で再確認)

- **`ElementCoordinator.LayoutBeams(Score, systems, staffIndex)` は全面的に単一声部**:
  - `_beamDetector.DetectBeamGroups(score)`(内部で `score.Voice` = Voices[0] のみ)
  - `score.Voice.Measures[...]` を複数箇所(概ね `LayoutBeams` 本体・`LayoutCrossMeasureBeamPieces`・
    `CollectBeamCollisions` 供給元)。X は `measureLayout.GetXForTiming` で timing 由来(共有カラム)。
- **`LayoutEngine.Layout(MultiStaffScore)` の per-staff ループ**(#3 で触った所)は、ビームだけ
  **`staffScore = staff.PrimaryVoice` のまま**にしてある(#3 のコミット参照:slur/tie/gliss は
  `staffSpannerScore = staff.Voices` の全声部 Score を渡すが、**ビームは意図的に primary のまま残した**)。
  → まず「ビームにも全声部 Score を渡す」のが入口だが、**それだけでは不足**(§2 の符幹方向)。
- **符幹方向の確定タイミングが経路で違う(最重要)**:
  - **多譜表パス**(`score { staff X }` = 本 repro が通る経路): 第 2 声部の符幹下向きは
    **レンダリング時にしか強制されない**(`SharedRenderer` の `VoiceDefaults.GetDefaultStemUp(voiceNumber)` →
    `DrawStaffMeasures(..., forcedStemUp, ...)`)。**ビームのレイアウト時点では voice-2 音符の `StemUp` は
    ピッチ既定**(`StemUpOverride ?? StaffPosition < 0`)で、混在=下向きに固定されていない。
  - **`BuildMultiVoiceScore` パス**(`voice{}voice{}` を part 構造なしで書いた単一譜): こちらは
    **収集時に `StemUpOverride = member.MemberStemUp` を焼き込む**(`MeasureCollector` の該当行)。
  - `VoiceDefaults.GetDefaultStemUp`(`VoiceColumn.cs`): voice1→true(上)、voice2→false(下)。
- `DetectBeamGroups` は**タプレット括弧の可視性**にも使われる(`LayoutEngine` が `beamGroups` を
  `CalculateAnnotationLayouts` に渡す)。声部対応で挙動が変わらないか要確認。

---

## 2. なぜ #3(spanner)より深いか

1. **ビーム Y 配置**: 下声のビームは**音符の下**、上声は上。ビーム傾き・高さは Lily# で最も複雑な
   `BeamScoringProblem`(量子化スコアリング)が決める。**符幹方向が正しく入っていないと Y が破綻**する。
2. **符幹方向の 2 経路整合**: レイアウト(ビーム)とレンダラの両方が同じ符幹方向を見る必要がある。
   多譜表パスでは render 時強制なので、**ビーム layout の前に voice 強制を効かせる**か、
   **collection で焼き込む**(BuildMultiVoiceScore と同様に)かの設計判断が要る。ここが load-bearing。
3. **タプレット括弧**との相互作用(`DetectBeamGroups` 共用)。

---

## 3. 推奨アプローチ(段階的・各段で単一声部 byte-identical を刻む)

**手本は #3(§4)。VoiceIndex を通し、検出器を全声部ループ、LayoutEngine で全声部 Score を渡す型。**

### 段階 1: `BeamGroup` に `VoiceIndex` を持たせ、`BeamDetector` を全声部ループ化(基盤)
- `BeamGroup`(モデル)に末尾 `int VoiceIndex = 0`。
- `BeamDetector.DetectBeamGroups` を `for (int v=0; v<score.Voices.Length; v++)` に。各声部 `score.Voices[v].Measures`
  を走査し、生成する group に `VoiceIndex: v`。**声部ごとにビーム分割**(自動連桁は拍・タプレット境界で切れる ―
  声部を跨がない)。単一声部は byte-identical。

### 段階 2(核心・最難): 符幹方向を**ビーム layout までに**声部で確定させる
- 選択肢 A(推奨): **多声の符幹方向を collection で焼き込む**。多譜表パスでも各声部の音符に
  `StemUpOverride = VoiceDefaults.GetDefaultStemUp(voiceNumber)` を入れる(`BuildMultiVoiceScore` の
  `member.MemberStemUp` と同じ思想を多譜表経路にも)。これで layout も render も同じ符幹を見る。
  **`SharedRenderer` 側の render 時強制と二重にならないよう整合**(既に焼き込まれていれば render は上書き不要に)。
- 選択肢 B: ビーム layout に voice 強制方向を引数で渡し、ビーム Y をその方向で組む(render はそのまま)。
  → layout/render の符幹が別ソースになり整合が脆い。**A を推奨**。
- **注意**: この段は既存の単一声部・多譜表の符幹に影響しうる(byte-identical 死守。`git status --short
  LilySharp.Tests/Snapshots/` で既存 svg 不変を確認)。

### 段階 3: `LayoutBeams` を声部対応化
- `score.Voice.Measures` を group の `VoiceIndex` の measures に差し替え(`score.Voices[group.VoiceIndex].Measures`)。
  対象: 本体・`CollectBeamCollisions` 供給・`LayoutCrossMeasureBeamPieces`。
- X は共有カラム(同 timing は同 X)なので `GetXForTiming` はそのままで一致するはず。**変わるのは Y(符幹方向・
  ビーム上下)**。プローブ(§5)で「下声ビームが下声音符の下」を数値確認。
- `LayoutEngine` の per-staff ループで、ビームにも `staffSpannerScore`(= 全声部 Score、#3 で導入済)を渡す。

### 段階 4: 検証と fixture
- fixture: `test/multivoice-beams`(下声に 8 分連桁 ― 上声は 4 分)。`SvgSnapshotTests.cs` に登録 →
  `LILYSHARP_UPDATE_SNAPSHOTS=1` で生成。**既存 snapshot 無変化**(新 fixture のみ ??)を確認。
- **LP 並置比較(§3 workflow)を必ず行う**。#3 の spanner でこれを省いたら voice-2 スラー向きの誤り
  (上向き)を見逃しかけた ― LP 照合で初めて「下向きが正」と判明した。**ビームの上下・傾きも LP と並べて確認**。
- 全 `dotnet test` 緑。

---

## 4. 手本 = #3 多声 slur/tie/gliss(本セッションで完了、未 push)

まったく同じ「VoiceIndex を通す」型。コミット(古い順):

| commit(概要) | 内容 |
|---|---|
| `refactor(spanners): carry VoiceIndex …` | SlurItem/TieItem/GlissandoItem に `VoiceIndex`(byte-identical 基盤) |
| `feat(spanners): detect … in every voice` | 検出器を全声部ループ＋**声部ごとの open スタック**(声部跨ぎペアリング防止) |
| `feat(spanners): lay out … on their own voice` | `LayoutEngine` が `staff.Voices` 全声部 Score を spanner layout へ／`ElementCoordinator` が `score.Voices[item.VoiceIndex]` で解決／**Glissando の data-pos を voice-aware 化**(`GlissandoLayout.VoiceIndex`＋`SharedRenderer.ResolveDataPos` の 4 次元ロケータ `(staff,voice,measure,item)`) |
| `fix(spanners): force polyphonic slur/tie direction by voice …` | **多声は符幹でなく声部で向き固定**(上声上・下声下)。単音 tie＋slur。LP 照合で発見 |
| `fix(spanners): force polyphonic CHORD-tie direction by voice` | 和音タイも声部方向に固定。fixture `test/multivoice-chord-tie` |

**再利用できる知見**:
- `LayoutEngine` の `staffSpannerScore = staff.Voices.Length > 1 ? new Score(staff.Voices, …) : staffScore`
  が**既にある** ― ビームにもこれを渡せばよい(段階 3)。
- fixture 記法: `part mel { }`(dynamic 予約語 `p` と衝突回避)、`section { mel { voice { } voice { } } }`、
  `score { staff mel }`。`octave absolute` で声部を別レジスタに。
- **符幹方向の落とし穴**(§1・§2): 多譜表パスは render 時強制。ここがビーム固有の難所で、spanner には無かった。
- 数値プローブ(§4/§5 workflow): SVG の音符 Y を声部で 2 band に分け、grob がどちらの band に着地するかで
  声部対応を検証(眼視は不可 ― spanner で危うく誤った)。

---

## 5. 完了の定義

- 2 声部の譜表で、第 2 声部の 8/16 分が**その声部の音符に連桁**され、**下声ビームは下・上声は上**。
- LP 並置で上下・傾きが一致。単一声部 byte-identical。fixture 追加。全テスト緑。
- コミットは段階ごと(BeamGroup→検出→符幹整合→layout)に「ビルド緑→全テスト緑→commit」を刻む。
  「症状 → 真因(LayoutBeams 単一声部＋符幹 render 時強制)→ LILYPOND-REF → 実装 → 検証(LP 一致)」をメッセージに。

---

## 6. リポジトリ状態(2026-07-01)

- `origin/master` から**未 push**のスタック(**push 保留指示中**):
  - ottava 系 3(per-staff / transpose / docs)
  - 多声 spanner 6(§4 の 5 コミット + 本引き継ぎ docs)
- push 解禁時は ff＋push。未追跡 `AI_POSITIONING_HANDOFF.md` は別件・温存。
- 検証コマンド: `dotnet test LilySharp.Tests 2>&1 | Select-String "Passed!|Failed!"`(期待 1977/3 skip)。
