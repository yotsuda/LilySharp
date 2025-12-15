# LilyPond レイアウト関数 優先順位分析

## 凡例
- ✅ LilySharp 実装済み
- 🟡 部分実装
- 🚀 未実装（重要）
- ⏸️ 未実装（低優先度）

## 1. skyline.cc (760行) - 衝突検出基盤

| 関数 | 行数 | LilySharp | 優先度 | 備考 |
|------|-----:|:---------:|:------:|------|
| Building (斜め対応) | ~50 | ✅ | **Critical** | VerticalSkyline で実装完了 (UP=-1, DOWN=+1) |
| Building::height() | 5 | ✅ | **Critical** | slope * x + y_intercept |
| Building::intersection_x() | 15 | ✅ | **Critical** | 2つの Building の交点計算 |
| Skyline::internal_build_skyline() | 80 | 🚀 | High | マージソートでスカイライン構築 |
| Skyline::internal_merge_skyline() | 100 | 🚀 | High | 2つのスカイラインのマージ |
| Skyline::distance() | 20 | 🟡 | Medium | 基本は実装済み |
| Skyline::touching_point() | 20 | 🚀 | Medium | 接触点を返す |
| その他ユーティリティ | ~300 | ⏸️ | Low | デバッグ・変換系 |

## 2. simple-spacer.cc (579行) - 水平スペーシング

| 関数 | 行数 | LilySharp | 優先度 | 備考 |
|------|-----:|:---------:|:------:|------|
| solve() | 30 | ✅ | - | SpringSolver.SolveForWidth() |
| range_solve() | 30 | ✅ | - | 二分探索 |
| configuration_length() | 10 | ✅ | - | TotalLength() |
| spring_positions() | 20 | ✅ | - | GetPositions() |
| add_rod() | 20 | 🚀 | Medium | Rod制約（未実装） |
| compress_line() | 60 | 🚀 | Low | 圧縮アルゴリズム |
| expand_line() | 50 | 🚀 | Low | 拡張アルゴリズム |
| force_penalty() | 30 | ✅ | - | KnuthPlassBreaker で使用 |

## 3. beam-quanting.cc (1403行) - 連桁最適化

| 関数 | 行数 | LilySharp | 優先度 | 備考 |
|------|-----:|:---------:|:------:|------|
| solve() | 30 | ✅ | - | BeamScoringProblem.Solve() |
| least_squares_positions() | 150 | ✅ | - | CalculateInitialPosition() |
| generate_quants() | 60 | ✅ | - | GenerateQuantCandidates() |
| one_scorer() | 30 | ✅ | - | ScoreConfiguration() |
| score_stem_lengths() | 80 | ✅ | - | ScoreStemLengths() |
| score_slope_direction() | 30 | ✅ | - | ScoreSlopeDirection() |
| score_slope_musical() | 40 | ✅ | - | ScoreSlopeMusical() |
| score_slope_ideal() | 30 | ✅ | - | ScoreSlopeIdeal() |
| score_horizontal_inter_quants() | 40 | ✅ | - | ScoreInterQuantHorizontal() |
| score_forbidden_quants() | 50 | ✅ | - | ScoreForbiddenQuants() |
| score_collisions() | 100 | ✅ | - | ScoreCollisions() |
| calc_concaveness() | 80 | ✅ | Medium | CalculateConcaveness() |
| slope_damping() | 60 | ✅ | Medium | ApplySlopeDamping() |

## 4. page-layout-problem.cc (1369行) - 垂直レイアウト

| 関数 | 行数 | LilySharp | 優先度 | 備考 |
|------|-----:|:---------:|:------:|------|
| solve_rod_spring_problem() | 40 | 🚀 | High | 垂直方向のSpring-Rod |
| build_system_skyline() | 50 | ✅ | - | BuildSystemSkylines() |
| find_system_offsets() | 80 | 🚀 | High | システム間オフセット計算 |
| distribute_loose_lines() | 100 | 🚀 | Medium | 緩い行の分配 |
| append_system() | 70 | ✅ | - | システム追加処理 |
| set_header_height() | 20 | ✅ | - | CalculateHeaderHeight() |
| filter_dead_elements() | 40 | ⏸️ | Low | Scheme用 |
| footnote関連 | ~200 | ⏸️ | Low | 脚注（LilySharp未対応） |

## 5. tie-formatting-problem.cc (1286行) - タイ最適化

| 関数 | 行数 | LilySharp | 優先度 | 備考 |
|------|-----:|:---------:|:------:|------|
| generate_configuration() | 100 | 🟡 | High | 基本実装のみ |
| score_configuration() | 80 | 🚀 | High | スコアリング |
| generate_optimal_configuration() | 60 | 🚀 | High | 最適解探索 |
| set_chord_outline() | 80 | 🚀 | Medium | 和音輪郭計算 |
| generate_collision_variations() | 80 | 🚀 | Medium | 衝突回避バリエーション |

## 6. slur-scoring.cc (906行) - スラー最適化

| 関数 | 行数 | LilySharp | 優先度 | 備考 |
|------|-----:|:---------:|:------:|------|
| get_base_attachments() | 80 | 🟡 | High | 基本実装のみ |
| generate_curves() | 100 | 🚀 | High | 曲線候補生成 |
| get_best_curve() | 60 | 🚀 | High | 最適曲線選択 |
| get_encompass_info() | 60 | 🚀 | Medium | 包含情報取得 |
| enumerate_attachments() | 80 | 🚀 | Medium | 端点列挙 |

---

## 推奨実装順序

### Phase A: Skyline 斜め対応（等価の鍵）
1. Building 構造体を斜め対応に拡張
2. intersection_x() 実装
3. internal_build_skyline() 実装
4. internal_merge_skyline() 実装

### Phase B: 垂直スペーシング強化
1. solve_rod_spring_problem() 
2. find_system_offsets()

### Phase C: タイ/スラー品質向上
1. Tie: score_configuration() + generate_optimal_configuration()
2. Slur: generate_curves() + get_best_curve()

### Phase D: 細部の品質向上
1. Beam: calc_concaveness(), slope_damping()
2. simple-spacer: Rod 制約
