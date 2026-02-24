# LilySharp LilyPond レイアウト再現度向上ロードマップ V2

## Status

- **前提**: Phase A-F 完了済み (到達度 ~55-60%)
- **目標**: LilyPond 出力との視覚的差異を最小化し、到達度 ~75-80% を目指す
- **方針**: LilyPond ソース準拠を絶対原則とする。独自近似は禁止。
- **テスト**: 1,004 テスト全パス + 53 サンプル SVG 検証済み

---

## 調査結果サマリー

コードベース全体の NOT YET IMPLEMENTED コメント (33箇所)、LILYPOND-REF コメント (756箇所) を精査し、
出力品質への影響度で以下の 4 Phase に分類した。

| Phase | テーマ | タスク数 | 推定工数 | 到達度向上 |
|-------|--------|---------|---------|-----------|
| G | 垂直レイアウト精度 | 5 | 12h | +5% |
| H | 水平スペーシング精度 | 5 | 10h | +4% |
| I | 音符衝突・配置精度 | 5 | 10h | +3% |
| J | ページ最適化・自動化 | 5 | 14h | +3% |
| **合計** | | **20** | **46h** | **+15%** |

---

## Phase G: 垂直レイアウト精度 (最優先)

段間距離・譜表間距離が LP と最も乖離する領域。全スコアの外観に影響。

### G-1. build_system_skyline 実装

- **問題**: 段間距離が固定フォーミュラ。LP は各段の skyline をマージして衝突回避距離を算出
- **影響**: 段間に突出要素 (ダイナミクス・歌詞・アーティキュレーション) がある場合の垂直距離が不正確
- **ファイル**: `LilySharp.Core/Svg/Layout/MultiStaffLayouter.cs`
- **参照**: `lily/page-layout-problem.cc:1070-1127` (build_system_skyline)
- **作業**:
  1. 各 system の上下 skyline を構築 (staff skylines + outside-staff grobs をマージ)
  2. 隣接 system 間の距離を `topSkyline.distance(bottomSkyline)` で計算
  3. 現在の `CalculateSystemSpacing()` を skyline-aware に置換
- **工数**: 4h

### G-2. outside-staff-priority stacking

- **問題**: 譜表外グロブ (テキスト/リハーサルマーク/ダイナミクス) の垂直積み上げ順が不定
- **影響**: 複数の外部要素が重なった際の配置が LP と異なる
- **ファイル**: `LilySharp.Core/Svg/Layout/MultiStaffLayouter.cs`
- **参照**: `lily/axis-group-interface.cc:359-474` (outside_staff_priority)
- **作業**:
  1. 各 outside-staff grob に priority 値を付与 (LP デフォルト: Hairpin=0, Dynamic=100, TextScript=200, RehearsalMark=750)
  2. priority 昇順で skyline に積み上げ
  3. side-position-interface の outside-staff-padding 適用
- **工数**: 3h

### G-3. staff-affinity for non-spaceable staves

- **問題**: 歌詞・figured bass・dynamics 行が spaceable staves 間に等配分されず固定位置
- **影響**: 歌詞行が上下の譜表に対して LP と異なる距離で配置
- **ファイル**: `LilySharp.Core/Svg/Layout/MultiStaffLayouter.cs`
- **参照**: `lily/align-interface.cc:240-252` (staff-affinity)
- **作業**:
  1. non-spaceable staff (lyrics, dynamics, figured bass) に staff-affinity 属性を付与
  2. UP/DOWN/CENTER に応じて隣接 spaceable staff へ吸着配置
  3. stretchability を spaceable staves 間にのみ配分
- **工数**: 2h

### G-4. pure height estimation

- **問題**: ページ分割時に歌詞/ダイナミクス/figured bass の高さが推定されない
- **影響**: ページ分割後に垂直スペースが足りず詰まる、または余白が不均一
- **ファイル**: `LilySharp.Core/Svg/Layout/PageBreaker.cs`, `MultiStaffLayouter.cs`
- **参照**: `lily/axis-group-interface.cc:138-173` (pure_height)
- **作業**:
  1. ページ分割前に各 system の "pure height" を計算 (grob の描画なし・高速推定)
  2. loose lines (lyrics/dynamics/cues) の推定高を含めた system height を PageBreaker に提供
  3. 実際のレイアウト後に unpure height で微調整
- **工数**: 2h

### G-5. inter-system skyline collision (PageLayouter)

- **問題**: ページ内の system 配置が均等分配のみ。LP は skyline 衝突を考慮
- **影響**: system 間に突出要素があるページで重なりが発生する可能性
- **ファイル**: `LilySharp.Core/Svg/Layout/PageLayouter.cs`
- **参照**: `lily/page-layout-problem.cc:483-530` (in-note-system-padding)
- **作業**:
  1. G-1 の build_system_skyline を利用
  2. system 間の最小距離を skyline collision + padding で決定
  3. 余剰スペースをストレッチ可能な箇所に配分
- **工数**: 1h (G-1 完了前提)

---

## Phase H: 水平スペーシング精度

音符間の水平距離が LP と微妙に異なる領域。楽譜全体の「密度感」に影響。

### H-1. Multi-voice shortest_playing_duration tracking

- **問題**: `prevDuration` を直接使用。LP は全声部の最短再生音価を追跡して fraction を計算
- **影響**: 多声部スコアでのスペーシング比率が LP と異なる
- **ファイル**: `LilySharp.Core/Svg/Layout/SpacingRules.cs`
- **参照**: `lily/spacing-spanner.cc:266-310` (shortest_playing_duration per column)
- **作業**:
  1. 各 musical column で全声部の再生中音価を収集
  2. shortest_playing_duration = min(全声部の current_duration)
  3. fraction = delta_t / shortest_playing_duration で spring 強度を計算
- **工数**: 2h

### H-2. Column spacing strict_note_spacing モード

- **問題**: 音符列間の最小距離が rod のみ。LP は strict モードで base_note_space も考慮
- **影響**: 全音符の後に16分音符が来る場合等のスペーシングが不均一
- **ファイル**: `LilySharp.Core/Svg/Layout/SpacingRules.cs`
- **参照**: `lily/note-spacing.cc:229-264` (strict_note_spacing)
- **作業**:
  1. grob property `strict-note-spacing` の参照を追加
  2. strict モード時は base_note_space をカラム間最小距離に適用
  3. proportional-notation 支援
- **工数**: 2h

### H-3. Separating group padding refinement

- **問題**: 小節線前後のパディングが固定値。LP は separating-line-group で動的計算
- **影響**: 小節線周辺のスペースが LP と微妙に異なる
- **ファイル**: `LilySharp.Core/Svg/Layout/SpacingRules.cs`
- **参照**: `lily/separation-item.cc:49-70`, `lily/separating-line-group-engraver.cc`
- **作業**:
  1. 小節線・音部記号変更・調号変更の直後に追加 padding を計算
  2. non-musical column の幅を LP の separating-group-spanner 方式に
  3. break-align-orders の適用
- **工数**: 2h

### H-4. Grace note spacing dynamics

- **問題**: 装飾音のスペーシングが固定パラメータ。LP は装飾音専用の spring ダイナミクスを使用
- **影響**: 装飾音群の密度が LP と異なる
- **ファイル**: `LilySharp.Core/Svg/Layout/SpacingRules.cs`, `GraceNoteEngraver.cs`
- **参照**: `lily/grace-spacing.cc:1-120`, `lily/spacing-basic.cc:140-155`
- **作業**:
  1. grace columns の spring を main columns と独立した inverse_stretch で計算
  2. grace-spacing::common-shortest-duration を装飾音列で別途計算
  3. grace→main 接合部の rod を LP 準拠に
- **工数**: 2h

### H-5. Break alignment order

- **問題**: 段頭の要素 (音部記号→調号→拍子記号) の並び順と間隔が概算
- **影響**: 段頭のスペーシングが LP と微妙に異なる
- **ファイル**: `LilySharp.Core/Svg/Layout/SpacingRules.cs`, `ElementCoordinator.cs`
- **参照**: `lily/break-alignment-interface.cc:1-200`, `scm/define-grobs.scm:break-align-orders`
- **作業**:
  1. break-align-orders テーブル (LEFT/CENTER/RIGHT 各3パターン) を実装
  2. 各 break-align グループの self-alignment-X を適用
  3. グループ間 spacing-alist から padding を取得
- **工数**: 2h

---

## Phase I: 音符衝突・配置精度

和音・多声部での音符配置がさらに LP に近づく。

### I-1. NoteCollision meshing multipliers

- **問題**: meshing (符頭が噛み合う) 場合の shift 値が未実装
- **影響**: 2度音程の和音で符頭が LP より離れて配置される
- **ファイル**: `LilySharp.Core/Svg/Layout/NoteCollision.cs`
- **参照**: `lily/note-collision-interface.cc:180-230` (meshing logic)
- **作業**:
  1. meshing_dotted = 0.1、meshing_general = 0.17 の導入
  2. seconds (2度音程) での符頭重なり判定
  3. 付点有無による meshing variant 切り替え
- **工数**: 2h

### I-2. NoteCollision head wipe

- **問題**: 完全に重なる符頭が両方描画される。LP は一方を非表示にする
- **影響**: 同音ユニゾン等で符頭が太く見える
- **ファイル**: `LilySharp.Core/Svg/Layout/NoteCollision.cs`, `SvgRenderer.cs`
- **参照**: `lily/note-collision-interface.cc:381-407` (head_wipe)
- **作業**:
  1. 同一ピッチ・同一音価の符頭を検出
  2. 音価が異なる場合は短い方の符頭を非表示
  3. ステム・付点は保持
- **工数**: 2h

### I-3. Accidental skyline collision (BBox→Skyline 移行)

- **問題**: 臨時記号の衝突検出が矩形 (BBox)。LP は skyline を使用
- **影響**: シャープ/フラットの曲線部分で不要なスペースが発生
- **ファイル**: `LilySharp.Core/Svg/Layout/AccidentalPlacement.cs`
- **参照**: `lily/accidental-placement.cc:338-390`
- **作業**:
  1. 各臨時記号グリフの skyline をフォントメトリクスから生成
  2. BBox 衝突判定を skyline.distance() に置換
  3. stagger_apes グルーピングロジック追加
- **工数**: 3h

### I-4. Dot collision avoidance

- **問題**: 付点が他声部の符頭や線と重なる場合の回避が不完全
- **影響**: 多声部で付点が符頭に重なる
- **ファイル**: `LilySharp.Core/Svg/Layout/NoteCollision.cs`, `SvgRenderer.cs`
- **参照**: `lily/dot-column.cc:1-180`, `lily/note-collision-interface.cc:140-175`
- **作業**:
  1. dot-column の上下シフトロジック実装
  2. 同一 staff position の付点を 1 staff space ずらす
  3. 他声部の符頭との衝突チェック
- **工数**: 2h

### I-5. Multi-voice cascading (3+ voices)

- **問題**: 3声部以上の衝突処理が2声部の繰り返しのみ
- **影響**: 3声部以上のスコアで符頭配置が不正確
- **ファイル**: `LilySharp.Core/Svg/Layout/NoteCollision.cs`
- **参照**: `lily/note-collision-interface.cc:420-480`
- **作業**:
  1. voice priority による累積 shift の計算
  2. 3声部目以降の force-hshift 自動計算
  3. 全声部の skyline を累積マージ
- **工数**: 1h

---

## Phase J: ページ最適化・自動化

ページレイアウトの最終品質を LP に近づける。

### J-1. Hara-kiri (空譜表自動非表示)

- **問題**: 空の譜表 (ossia, cue, 休み小節のみ) が常に表示される
- **影響**: オーケストラスコアや ossia 付きスコアで無駄なスペース
- **ファイル**: `LilySharp.Core/Svg/Layout/MultiStaffLayouter.cs`, `MeasureCollector.cs`
- **参照**: `lily/hara-kiri-group-spanner.cc:1-100`
- **作業**:
  1. 各 system で各 staff の音楽イベント有無を判定
  2. `\RemoveEmptyStaves` 相当のフラグを StaffGroup に追加
  3. 空 staff の height/spacing を 0 に設定し描画をスキップ
  4. system skyline を再計算
- **工数**: 4h

### J-2. fixed_force_solution for ragged-last

- **問題**: 最終ページの ragged-last-bottom 処理が不完全
- **影響**: 最終ページの system が下端に引き伸ばされる/集中しすぎる
- **ファイル**: `LilySharp.Core/Svg/Layout/PageLayouter.cs`
- **参照**: `lily/page-layout-problem.cc:808-823` (fixed_force_solution)
- **作業**:
  1. ragged-last-bottom 時に force=0 の固定配置を計算
  2. 余白を下端に集約する配置
  3. ragged-bottom / ragged-last 4パターンの組み合わせ対応
- **工数**: 2h

### J-3. alignment-distances manual override

- **問題**: `\override StaffGrouper.staff-staff-spacing` 等のユーザー指定が反映されない
- **影響**: ユーザーが譜表間距離を調整できない
- **ファイル**: `LilySharp.Core/Svg/Layout/MultiStaffLayouter.cs`
- **参照**: `lily/page-layout-problem.cc:656-717` (alignment_distances)
- **作業**:
  1. GrobPropertyResolver から spacing override を読み取り
  2. basic-distance, minimum-distance, padding, stretchability の4パラメータ対応
  3. individual staff pair 毎のオーバーライド
- **工数**: 3h

### J-4. Bracket/brace collapse

- **問題**: 空譜表非表示時にブラケット/ブレースの高さが更新されない
- **影響**: J-1 (hara-kiri) 完了後に顕在化する問題
- **ファイル**: `LilySharp.Core/Svg/Renderer/SvgRenderer.cs`
- **参照**: `lily/system-start-delimiter.cc:127-129`
- **作業**:
  1. 描画済み staff の最上・最下 Y座標からブラケット高を再計算
  2. 単一 staff 残りの場合はブラケットを非表示
  3. SquareBracket / LineBracket バリアント対応
- **工数**: 2h

### J-5. Footnote height estimation

- **問題**: 脚注がページ高さに含まれない
- **影響**: 脚注付きスコアでページ下端が溢れる
- **ファイル**: `LilySharp.Core/Svg/Layout/PageBreaker.cs`, `PageLayouter.cs`
- **参照**: `lily/page-layout-problem.cc:186-310` (footnote_height)
- **作業**:
  1. 脚注テキストの高さ推定
  2. ページ利用可能高さから脚注高さを減算
  3. 脚注セパレータ線の描画
- **工数**: 3h

---

## 推奨実装順序

```
Phase G: 垂直レイアウト精度 ─── 12h (到達度 +5%)
  G-1 build_system_skyline          [4h] ← 最重要: 全スコアの段間距離改善
  G-2 outside-staff-priority         [3h] ← G-1 の skyline を活用
  G-3 staff-affinity                 [2h] ← 歌詞/dynamics の配置改善
  G-4 pure height estimation         [2h] ← ページ分割精度向上
  G-5 inter-system skyline (Page)    [1h] ← G-1 前提

Phase H: 水平スペーシング精度 ─── 10h (到達度 +4%)
  H-1 Multi-voice shortest duration  [2h] ← 多声部スコアの密度改善
  H-5 Break alignment order          [2h] ← 段頭のスペーシング改善
  H-3 Separating group padding       [2h] ← 小節線周辺の改善
  H-2 strict_note_spacing            [2h]
  H-4 Grace note spacing dynamics    [2h]

Phase I: 音符衝突・配置精度 ─── 10h (到達度 +3%)
  I-1 Meshing multipliers            [2h] ← 2度音程の表示改善
  I-2 Head wipe                      [2h] ← ユニゾン表示改善
  I-4 Dot collision avoidance        [2h] ← 多声部の付点改善
  I-3 Accidental skyline             [3h] ← 臨時記号の精密配置
  I-5 Multi-voice cascading          [1h]

Phase J: ページ最適化・自動化 ─── 14h (到達度 +3%)
  J-1 Hara-kiri                      [4h] ← オーケストラスコア対応
  J-2 fixed_force_solution           [2h] ← 最終ページ改善
  J-3 alignment-distances override   [3h] ← ユーザーカスタマイズ
  J-4 Bracket collapse               [2h] ← J-1 前提
  J-5 Footnote heights               [3h]
```

---

## 依存関係

```
G-1 (build_system_skyline)
 ├── G-2 (outside-staff-priority) ... skyline に grob を積む
 ├── G-5 (inter-system skyline)   ... skyline 距離を使う
 └── J-4 (bracket collapse)       ... hara-kiri 後の再計算

G-4 (pure height) → PageBreaker の精度向上

J-1 (hara-kiri) → J-4 (bracket collapse)

H-1 (multi-voice shortest) は独立
I-1〜I-5 は独立 (並行実装可能)
```

---

## 検証方法

### 自動検証
1. **単体テスト**: 各タスクに xUnit テストを先行作成 (TDD)
2. **スナップショットテスト**: `LILYSHARP_UPDATE_SNAPSHOTS=1 dotnet test` で更新後、diff 確認
3. **回帰テスト**: 全 1,004+ テストが常にパス

### 視覚比較
4. **LP リファレンス**: 各テストケースの .ly ファイルを LilyPond で PDF 出力し、LilySharp SVG と並列比較
5. **53 サンプル SVG 再生成**: 各 Phase 完了後に全サンプルを再生成し MDP で目視確認

### 定量比較 (新規)
6. **スペーシング差分**: LP 出力の音符 X 座標と LilySharp の X 座標を数値比較
7. **垂直距離差分**: LP 出力の system Y 座標と LilySharp の Y 座標を数値比較

---

## 達成目標

| マイルストーン | 到達度 | テスト数 |
|---------------|--------|---------|
| 現状 (Phase F 完了) | ~55-60% | 1,004 |
| Phase G 完了 | ~60-65% | ~1,040 |
| Phase H 完了 | ~64-69% | ~1,070 |
| Phase I 完了 | ~67-72% | ~1,100 |
| Phase J 完了 | ~70-75% | ~1,130 |

---

## LP ソース参照インデックス

| タスク | LilyPond ソースファイル | 行番号 |
|--------|----------------------|--------|
| G-1 | page-layout-problem.cc | 1070-1127 |
| G-2 | axis-group-interface.cc | 359-474 |
| G-3 | align-interface.cc | 240-252 |
| G-4 | axis-group-interface.cc | 138-173 |
| G-5 | page-layout-problem.cc | 483-530 |
| H-1 | spacing-spanner.cc | 266-310 |
| H-2 | note-spacing.cc | 229-264 |
| H-3 | separation-item.cc | 49-70 |
| H-4 | grace-spacing.cc | 1-120 |
| H-5 | break-alignment-interface.cc | 1-200 |
| I-1 | note-collision-interface.cc | 180-230 |
| I-2 | note-collision-interface.cc | 381-407 |
| I-3 | accidental-placement.cc | 338-390 |
| I-4 | dot-column.cc | 1-180 |
| I-5 | note-collision-interface.cc | 420-480 |
| J-1 | hara-kiri-group-spanner.cc | 1-100 |
| J-2 | page-layout-problem.cc | 808-823 |
| J-3 | page-layout-problem.cc | 656-717 |
| J-4 | system-start-delimiter.cc | 127-129 |
| J-5 | page-layout-problem.cc | 186-310 |
