# 引き継ぎ: 交差声部の水平 note-collision(per-note 幅対応)

> **✅ 完了(2026-07-01 夜、commit `724f8cc`、未 push)。** 以下は着手時の引き継ぎ(経緯記録)。実装は下記の通り本書の
> 段階案から**簡略化**された ― LP 実測で「per-note 幅対応の refactor は不要」と判明したため:
> - **真因確定**: LP 並置実測で、交差の shift は `check_meshing_chords` の meshing フォールバック(0.17)そのもので、
>   **extent スケーリング(343-348)は係数 1.0=no-op**(Lily# の notehead 左 extent=0 かつ正=up-shifts-right)。base 幅は
>   列最大幅=down 幅で既に一致。よって **段階2(extent 係数)/per-note 幅 refactor は本ケースでは不要**だった。
> - **実装**: `AnalyzeCollision` 末尾 `NoCollision` → meshing フォールバック＋新 `CollisionType.Meshing`。
>   `CalculateVoiceOffsets` は Meshing 時のみ **rightMost をピン**=上声(連桁)を列 X に固定し**下声を左へ**(§4 完了定義どおり)。
> - **前セッションの「clear しきれず」の正体**: マグニチュード不足でなく、**連桁の上声を動かすと beam が列 X から外れる**問題。
>   下声を動かす設計(§3 段階4 の (a))で回避＝LP と同じ見た目・beam 無傷。
> - **検証**: 全音符 0.66・4分 0.44 が LP(0.668/0.444=`0.34*down幅`)と一致(並置 PNG＋data-pos 実測)。equal-width 全
>   snapshot byte-identical、`multivoice-beam-collision` は A5＋加線が左へ 0.66 のみ(純差分)。新 fixture
>   `test/multivoice-crossing-collision`＋3 単体テスト。1986緑。**残り簡略化**: 下声は非連桁前提、dotted down-shifts-right の
>   一般 extent スケーリングは据置(既存 snapshot 依存なし)。

**前提: `docs/DEV_BUGFIX_WORKFLOW.md` を先に読むこと**(ripple shell / `Write` でファイル化 / master 直 /
LILYPOND-REF / **単一声部・equal-width byte-identical 不変条件** / `Co-Authored-By: Claude <current-model>` /
**push 保留中**)。本書は `DEV_BUGFIX_WORKFLOW.md` **§12-7 の実装版引き継ぎ**(evidence は §12-7、着手手順は本書)。
多声ビーム一式(検出〜cross-voice **beam** collision)は完了済み。**残る唯一の本質的欠落がこの水平 note-collision**。

---

## 0. 課題

**声部交差**(下声部が上声部より高音)で、上声部の**符幹が下声部の音符を貫通する**。LP は下声部音を左へずらして符幹を clear するが、
Lily# はずらさない(または量が不足)。**垂直=ビーム高さは commit `04c7679`「keep a beam clear of the other voices' notes」で解決済**。
残るは**水平オフセットのみ**。

### repro(`Write` で作り `dotnet run --project LilySharp.Cli -- png <f>.lys C:\temp\x.png`)
scratch に残置(gitignore):`scratch/bt_xvoice.lys`(全音符)/ `scratch/bt_xvoice2.lys`(4分)。
```
octave absolute
part mel { clef treble }
section S {
  mel {
    voice { c'8 c' c' c' c' c' c' c' | }   // 上声=C5 8分・ステム上・ビーム上
    voice { a'1 | }                          // 下声=A5 全音符(C5 より高い=交差)。4分版は a'4 a' a' a'
  }
}
structure { S }
score "x" { staff mel }
```
現状: A5 の頭を C5 の符幹が貫通。期待: A5 が左へ寄り、符幹が右を通って clear(＋ビームは既に上へ避けている)。

### LP 参照(**オクターブ翻訳に注意**: Lily# `octave absolute` は `c'`=C5・`a'`=A5、LP は +1 tick=`c''`/`a''`)
```
\version "2.24.0"
\paper { indent = 0 ragged-right = ##t tagline = ##f }
\new Staff <<
  \new Voice { \voiceOne \time 4/4 c''8 c'' c'' c'' c'' c'' c'' c'' }
  \new Voice { \voiceTwo a''1 }   % 4分版は a''4 a'' a'' a''
>>
```
LP は全音符でも 4分でも下声を明確に左へずらす。`--png` でデッドロック回避起動(§1)。並置比較スクリプトは前セッション作成(PIL trim+zoom、§3.3)。

---

## 1. 現状の事実(grep で確定済み、行番号はドリフトするので識別子で再確認)

- **根因**: `NoteCollision.AnalyzeCollision`(`LilySharp.Core/Svg/Layout/NoteCollision.cs`)は「too far apart」ゲートを
  通過しても**衝突タイプ(full/close_half/distant_half/touch)不一致だと末尾で `NoteCollisionInfo.NoCollision`(シフト0)を返す**。
- **LP は同位置で "meshing" フォールバックを必ず適用**する(`lily/note-collision.cc:332-337`): ドット有 `*0.1` / 無 `*0.17`。
  交差声部(上ステム音が下ステム音の**下**)はこの経路に落ちる。
- 「too far apart」ゲート: `if (ups[0] > downs.Last() + threshold) return NoCollision;`(LP `note-collision.cc:65` と一致)。
  交差(up が down の下)では ups[0] < downs.Last() なのでゲートを**通過**し、LP は shift を計算する。Lily# もゲートまでは一致。
- **マグニチュード周りの LP 実装**(Lily# 未移植):
  - `note-collision.cc:343-348` **extent スケーリング**: `shift_amount *= (extent_down[RIGHT] - extent_up[LEFT]) / extent_down.length()`
    (up が右へ寄る場合)/ down が右なら `(extent_up[RIGHT] - extent_down[LEFT]) / extent_down.length()`。左右で式が違う。
  - `calc_positioning_done`: 最終オフセットに **down note の実幅**を掛ける。
- **Lily# 側の対応部**:
  - `ElementCoordinator.CalculateVoiceOffsets`(`ElementCoordinator.cs`)が列ごとに `noteheadWidth = GetColumnNoteheadWidth(column)`
    =**その列の最も広い notehead 幅**を作り(LP `note-collision.cc:309-312`)、`_noteCollision.CalculateVoiceOffsets(column, noteheadWidth)` を呼ぶ。
    → **基底幅は既に「列の最大幅」で幅対応済**。欠けているのは (a) meshing フォールバック自体、(b) **extent スケーリング係数**。
  - `NoteCollision.CalculateVoiceOffsets`(NoteCollision.cs 末尾)が `AnalyzeCollision` の up/down オフセットを取り、
    3声部 cascade を足し、**`leftMost` を引いて pinning**(最左グループを列スロットに固定)、最後に `* noteheadWidth` する。
- **レンダラの適用点**: `SharedRenderer.cs` の `EnumerateStaffItems` 内 `itemX += layout.GetVoiceOffset(ml.MeasureIndex, voiceNumber, itemIdx);`
  (≈1060 行)。**注意**: ビームは `beam.MemberXPositions`(=列 timing X、voiceOffset 非適用)で描く。
  → **連桁声部の頭を voiceOffset でずらすと、頭が列 X の符幹/ビームからずれる恐れ**(要検証)。

---

## 2. 前セッションで踏んだ落とし穴(重要・同じ轍を踏まない)

meshing フォールバックだけを試作(末尾 `NoCollision` を `0.17/0.1` shift に差し替え)した結果:
1. **全 real fixture は byte-identical**(交差 fixture のみ変化)= 安全側は確認済。
2. しかし `multivoice-beam-collision` fixture の**全音符が予想と逆(右へ 0.67)移動**した。私は「down 声部は pinning で offset 0 のはず」と
   考えたが、diff は down(A5 全音符, data-pos)が動いた。**pinning がどちらの声部を動かすか、確証を持って説明できなかった**。
3. 4分版(`bt_xvoice2`)は A5 が左へ寄り LP と概ね一致=**normal 幅では動く**が、全音符(幅広)は clear しきれず。
4. **§0「理解できない/アドホックな修正は出さない」に従い試作を revert**(byte-identical へ戻し済)。
→ **教訓**: (a) pinning 方向を紙とペンで先に確定、(b) 各段で数値プローブ(§5)で up/down 実オフセットを吐かせて LP と突合、
(c) **連桁声部が動く場合の頭/ビーム整合を必ず目視+数値確認**。「実装してから見る」で混乱した。

---

## 3. 推奨アプローチ(段階的・各段で equal-width byte-identical を刻む)

**設計原則**: LP `note-collision.cc` を忠実移植。equal-width(4分×4分, 2分×2分 等)は extent 係数=1.0 で**不変のはず**=既存
fixture(`collision.lys`, `multi-voice`, `two-voice-polyphony`, `voice-*`)を byte-identical に保つ。**mixed-width(全音符 vs 8分 等)のみ再ベースライン**。

### 段階 1: meshing フォールバックの移植(byte-identical for real fixtures ― 前セッションで確認済)
- `AnalyzeCollision` 末尾の `return NoCollision;` を、`note-collision.cc:332-337` の else 枝
  (`MeshingDottedShift`/`MeshingGeneralShift` を up/down 対称に)へ。**ただし段階2/4を伴わないと交差の見た目は直りきらない**(前セッションの通り)。
- 単独では出さず、段階2〜4 とセットで検証してから commit する。

### 段階 2(核心): extent スケーリングの移植(`note-collision.cc:343-348`)
- `AnalyzeCollision`(or `CalculateVoiceOffsets`)で up/down それぞれの notehead 幅(全音符は広い)を使い、係数
  `(extent_down[RIGHT] - extent_up[LEFT]) / extent_down.length()`(up 右寄せ時)を shift に掛ける。幅は `GlyphMetrics` の
  note-value 別 notehead advance/extent から取得(全音符 `GlyphMetrics.*Whole*` 等、要 grep)。
- **equal-width では係数=1.0**(extent_down.length と分子が一致)→ 既存 fixture 不変を数値で確認。
- 現状 `CalculateVoiceOffsets` は列の最大幅で一括 `* noteheadWidth` している。extent 係数は per-pair(up 幅 vs down 幅)なので、
  `AnalyzeCollision` に up/down の幅を渡すか、`NoteCollisionInfo` に per-side 係数を持たせる設計判断が要る。

### 段階 3: pinning 方向の確定(前セッションの混乱点)
- `CalculateVoiceOffsets` の `leftMost` 減算がどちらを動かすかを、交差ケースで**数値プローブ**して LP と突合。
  LP は下声(down)を左へずらす(全音符が左)。Lily# の pinning が up を右へ動かすなら、**連桁の up 声部頭がビームからずれる**(段階4)。
  → LP の見た目(下声が動く)に合わせる。必要なら pinning 規則 or shiftUpRight の既定を交差時だけ調整(ただしアドホックにしない=LP の
  `calc_positioning_done` の translate 規則を読む)。

### 段階 4: 連桁声部の頭/ビーム整合(要検証・見落とし注意)
- 交差で**連桁している方の声部**(repro では上声 C5 8分)が動く場合、頭は `GetVoiceOffset` で動くがビームは列 X のまま。
  → 頭・符幹・ビームがずれないことを目視+数値で確認。ずれるなら (a) その声部を動かさない設計(下声だけ動かす=LP と同じ)にするか、
  (b) ビーム/符幹 X にも同じ voiceOffset を効かせる(`BeamMember`/`itemXPositions` 経由)必要がある。**(a) が LP 準拠で安全**。

### 段階 5: 検証と fixture
- **byte-identical**: equal-width 既存 fixture 全て不変を `git status --short LilySharp.Tests/Snapshots/` で確認。
- **再ベースライン**: mixed-width の交差のみ(新規 fixture＋`multivoice-beam-collision` の全音符が clear するよう改善)。
- **LP 並置**(§3): `bt_xvoice`(全音符)・`bt_xvoice2`(4分)で**下声が符幹を clear**し LP と一致を目視。
- **新 fixture**: `test/multivoice-crossing-collision`(4分交差)を追加(前セッションで一度作って revert したので再作成)。
- 全 `dotnet test` 緑(現 baseline **1982 passed / 3 skipped**)。

---

## 4. 完了の定義

- 交差声部で、下声部の音が左へ寄り**上声部の符幹が clear**(全音符・4分とも LP 一致)。連桁声部の頭/ビーム整合も破綻なし。
- equal-width byte-identical、mixed-width のみ意図的に再ベースライン(改善)。fixture 追加。全テスト緑。
- コミットは段階ごと(meshing→extent→pinning→連桁整合)に「ビルド緑→全テスト緑→commit」。
  「症状 → 真因(`AnalyzeCollision` の NoCollision fall-through＋extent 未対応)→ LILYPOND-REF → 実装 → 検証(LP 一致)」をメッセージに。

---

## 5. リポジトリ状態(2026-07-01、この引き継ぎ作成時点)

- `origin/master` から**未 push**の 9 コミット(**push 保留指示中**、多声ビーム/連符/ブラケット/beam-collision 一式＋docs):
  ```
  (docs: §12-7 記録 = 本タスクの evidence)
  feat(beams): keep a beam clear of the other voices' notes   ← 垂直 beam collision 完了
  fix(tuplet): place a lower voice's bracket on its own stem side
  fix(beams): scope tuplet beam-breaks to their own staff
  fix(beams): keep tuplet beam-breaks within their own voice
  docs: mark multi-voice beams done
  feat(beams): beam every voice, not just the primary
  refactor(beams): carry VoiceIndex on beam groups
  (以下 ottava/spanner 等の既存未 push 分)
  ```
- push 解禁時は ff＋`git push origin master`。未追跡 `AI_POSITIONING_HANDOFF.md` は別件・温存。
- 検証コマンド: `dotnet test LilySharp.Tests 2>&1 | Select-String "Passed!|Failed!"`(期待 1982/3 skip)。
