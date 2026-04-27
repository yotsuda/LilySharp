# Top 10 Algorithm Fidelity Audit (Phase 1-4)

**生成日**: 2026-04-25
**手法**: 各アルゴリズムを LP ↔ Lily# 双方で 5-15 行 verbatim 引用しながら精読
**対象**: Phase 0 偵察で 4 件 + Phase 1-4 で追加 6 件 = 計 10 件

---

## 統合結果

| # | アルゴリズム | LP 参照 | Lily# 参照 | Verdict | Severity |
|---:|---|---|---|---|---|
| 1 | Skyline build / distance | `lily/skyline.cc:32-176` | `VerticalSkyline.cs:32-746` | **Faithful** | LOW |
| 2 | Spring/Rod solver | `lily/simple-spacer.cc:175-204` | `SpringSolver.cs:96-135` | **Faithful** | LOW |
| 3 | Beam quanting (5 phase + lazy PQ) | `lily/beam-quanting.cc:152-1114` | `BeamScoringProblem.cs:139-180` | **Faithful** | LOW |
| 4 | Optimal page breaking | `lily/optimal-page-breaking.cc:41-254` + `lily/page-spacing.cc:31-132` | `PageBreaker.cs:312-449` | **Partial** | MED |
| 5 | SpacingSpanner spring gen (duration→space) | `lily/spacing-basic.cc:109-183` + `spacing-spanner.cc:300-317` | `SpacingRules.cs:563-588` | **Faithful** | LOW |
| 6 | build_system_skyline | `lily/page-layout-problem.cc:1080-1127` | `VerticalSkyline.cs:700-731` + `MultiStaffLayouter` | **Faithful** | LOW |
| 7 | staff-affinity (non-spaceable) | `lily/align-interface.cc:240-252` | `MultiStaffLayouter.cs:37, 103-127` | **Absent (stub)** | **HIGH** |
| 8 | Note collision (meshing/wipe/dot/cascade) | `lily/note-collision.cc:1-665` + `lily/dot-column.cc` | `NoteCollision.cs:1-607` | **Faithful** | LOW |
| 9 | Accidental placement (skyline + stagger + editorial) | `lily/accidental-placement.cc:1-534` + `lily/accidental.cc:130-166` | `AccidentalPlacement.cs:1-441` | **Partial** (editorial accidental 不在) | MED |
| 10 | Knuth–Plass line breaking + spanner break-substitution | `lily/constrained-breaking.cc:1-600` + `lily/break-substitution.cc` | `KnuthPlassBreaker.cs:1-518` | **Heuristic** (break-substitution 不在) | **HIGH** |

**結論**: 10件中 6 件 Faithful、2 件 Partial、1 件 Heuristic、1 件 Absent。**HIGH severity 2件 (#7, #10)** が最重要。

---

## 重大発見 1: staff-affinity 不在 (#7, HIGH)

LP の `align-interface.cc:240-252` は lyrics / dynamics / figured bass / chord names のような non-spaceable 行を、隣接する spaceable staff に **吸着**させる。
LilySharp は `MultiStaffLayouter.cs:37` に `NOT YET IMPLEMENTED` を残し、現状は単に "affinity あり ⇒ NonStaff spacing 適用" の二値ロジックで、**どの staff に向かって吸着するか**を実装していない。

**LP コード (page-layout-problem.cc:1174-1182 経由):**
```cpp
if (include_fixed_spacing
    && Page_layout_problem::is_spaceable (elems[j])
    && last_spaceable_element)
{
  spec = Page_layout_problem::get_spacing_spec (
    last_spaceable_element, elems[j], pure, start, end);
  Real spaceable_padding = 0;
  Page_layout_problem::read_spacing_spec (
    spec, &spaceable_padding, ly_symbol2scm ("padding"));
  dy = std::max (
    dy, (last_spaceable_skyline.distance (skyline[-stacking_dir])
         + stacking_dir * (last_spaceable_element_pos - where)
         + spaceable_padding));
}
```

**Lily# コード (MultiStaffLayouter.cs:103-127):**
```csharp
bool nextHasAffinity = nextGroup.Staves.Any(s => s.StaffAffinity.HasValue);
bool currentHasAffinity = group.Staves.Any(s => s.StaffAffinity.HasValue);

double interGroupGap;
if (nextHasAffinity || currentHasAffinity)
{
    interGroupGap = sp.NonStaff.BasicDistance - staffHeight;
}
else
{
    interGroupGap = sp.StaffGroupStaff.BasicDistance - staffHeight;
}
```
吸着方向 (UP/DOWN/CENTER) と target staff の指定がない。歌詞付き楽譜・dynamics 多用スコアで顕著な視覚差。

**ロードマップ照合**: Phase G-3 (2h 見積) と一致。

---

## 重大発見 2: break-substitution 完全不在 (#10, HIGH)

Knuth-Plass 本体は faithful だが、**spanner (slur / tie / beam / hairpin / ottava 等) を改行で分割するロジックが完全に欠落**している。LP は `lily/break-substitution.cc` で改行ポイントごとに spanner を 2 個 (前段の終端 + 次段の冒頭) に置換する処理を持つ。

**LP コード (break-substitution.cc::break_substitute 抜粋イメージ):**
- `Spanner::find_broken_piece(System*)` → 各 spanner を broken intoに分割
- bound (最初/最後の grob) を改行点に再アタッチ
- 内部 grob array を分割

**Lily# 状態**: KnuthPlassBreaker.cs に該当処理ナシ。grep でも `break-substitution` または `BreakSubstitution` ヒットなし。

**影響**:
- 段をまたぐ slur / tie / beam が視覚的に途切れる、または計算上未対応
- 段頭/段尾の連続線 (cresc., trill spanner 等) も同様
- multi-line 楽譜全般で重大な視覚劣化

**新規タスクとして Phase 4 に追加要** (ロードマップ未列挙)。推定 10-15h。

---

## 重大発見 3: 行分割 permission 階層がヒューリスティック (#10, MED)

LP は `line-break-permission`, `page-break-permission`, `page-turn-permission` の 3 段階を `min_permission()` で集約。Lily# は単純な `BreakPermission.Forbid` ⇒ skip / `Force` ⇒ skip-spanning の二値処理。

**LP (constrained-breaking.cc:520-535):**
```cpp
out->break_permission_ = get_property(c, "line-break-permission");
out->page_permission_ = get_property(c, "page-break-permission");
out->turn_permission_ = get_property(c, "page-turn-permission");
out->page_permission_ = min_permission(out->break_permission_, out->page_permission_);
out->turn_permission_ = min_permission(out->page_permission_, out->turn_permission_);
```
**Lily# (KnuthPlassBreaker.cs:217-234):**
```csharp
if (i > 0 && springData[i - 1].BreakPermission == BreakPermission.Forbid)
    continue;
```
小説 break ポイントで微差。`page-break-permission` 周りはそもそも未対応 (Phase 1-3 で property 不在を確認済)。

---

## 重大発見 4: 編集臨時記号 (Editorial Accidental) 不在 (#9, MED)

`AccidentalPlacement.cs:34-42` は構造体は持つが、`IsEditorial = false` のスタブ。LP `accidental.cc:130-166` は parenthesized / smaller rendering を実装。
`AccidentalSuggestion`, `AccidentalCautionary` grob 自体が Phase 1-3 で **Used (3)** または **Absent** カテゴリに分類されており、エンドツーエンド対応はしていない。

ロードマップ照合: Phase I-3 で言及あり (skyline collision とまとめて)。

---

## 重大発見 5: 既存ロードマップ G-1 (build_system_skyline) は実装済 (#6, LOW)

`LAYOUT_ROADMAP_V2.md` の Phase G-1 は "build_system_skyline 実装" を 4h タスクとして挙げているが、Phase 1-4 精読の結果、`VerticalSkyline.Distance()` (700-731) は **LP の page-layout-problem.cc:1080-1127 と等価**であり、`MultiStaffLayouter.cs` 内で system skyline 生成も行われている。

**ロードマップへの反映**:
- G-1 を「実装済」に更新、もしくは "build_system_skyline の **インテグレーション完成度確認**" にスコープ縮小
- 4h を staff-affinity (G-3) や break-substitution (新タスク) に振り替え

---

## 修正発見されたロードマップ評価ズレ

| 項目 | LAYOUT_ROADMAP_V2 評価 | 実情 (Phase 1-4 audit) |
|---|---|---|
| G-1 build_system_skyline | 未実装 (4h) | **実装済** (faithful) |
| G-3 staff-affinity | 未実装 (2h) | **未実装、HIGH severity** |
| H-1 multi-voice shortest_playing_duration | 未実装 (2h) | duration formula は **faithful**。multi-voice tracking のみ要確認 |
| I-1 meshing multipliers | 未実装 (2h) | **実装済** (Faithful per Phase 1-4 audit) |
| I-2 head wipe | 未実装 (2h) | **実装済** (Faithful) |
| I-4 dot collision | 未実装 (2h) | **実装済** (Faithful per Phase 1-4) |
| I-5 multi-voice cascading | 未実装 (1h) | **実装済** (Faithful) |
| (新規) break-substitution | 未列挙 | **完全不在、HIGH severity** |
| (新規) line-break permission 3-tier | 未列挙 | ヒューリスティック、MED |
| (新規) editorial accidental rendering | I-3 内で未明記 | スタブ、MED |

---

## アクション優先順位 (Severity × ロードマップ整合)

### Tier S - 着手第一陣 (HIGH severity)
1. **break-substitution 実装** (新規, 10-15h)
   - LP `lily/break-substitution.cc::break_substitute` を C# 移植
   - spanner (slur / tie / beam / hairpin / ottava / volta / text-spanner / glissando 等) を改行ポイントで分割
   - 各 broken piece に bound 再アタッチ
   - **影響**: 段またぎ表現の正常化、multi-line 楽譜全般
2. **staff-affinity 完全実装** (Phase G-3 拡張, 4-5h、当初 2h より大きく見積)
   - UP/DOWN/CENTER 方向制御
   - target spaceable staff 検索
   - non-staff-related-staff-spacing / non-staff-unrelated-staff-spacing の使い分け

### Tier A - 第二陣 (MED severity)
3. **編集臨時記号 (AccidentalSuggestion 完全対応)** (3h) — Phase I-3 の独立タスク化
4. **line-break permission 3-tier 階層** (2h) — Phase H-5 と統合、または独立
5. **AccidentalPlacement BBox→glyph skyline** (3h) — Phase I-3 (現状の roadmap)

### Tier B - 整理タスク (LOW severity / cleanup)
6. **G-1 ロードマップ更新**: 実装済を反映、空き 4h を Tier S に振替
7. **I-1, I-2, I-4, I-5 のロードマップ更新**: Faithful 実装済を反映 (合計 7h 振替可能)

### Tier C - 評価維持 (現状維持で OK)
8. Skyline / Spring/Rod / Beam quanting / SpacingSpanner duration formula / page-layout build_system_skyline — Faithful 評価を維持。今後の修正で degrade させないよう Phase 2 視覚回帰でガード。

---

## 工数再見積もり

| Phase | 当初見積 | 修正後見積 | 差分 |
|---|---:|---:|---:|
| G (vertical) | 12h | 10h (G-1, I-1/2/4/5 を実装済として振替) | -2h |
| H (horizontal) | 10h | 9h (H-5 内に permission 階層) | -1h |
| I (collision) | 10h | 4h (大半が実装済、editorial + skyline のみ) | -6h |
| J (page) | 14h | 14h (変更なし) | 0 |
| 新規: break-substitution | 0 | **+12h** | +12h |
| 新規: staff-affinity 拡張 | 含 | +2h | +2h |
| **合計** | 46h | **51h** | +5h |
