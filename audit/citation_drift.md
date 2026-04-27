# Citation Drift Audit (Phase 1-1)

**生成日**: 2026-04-25
**対象**: `LilySharp.Core/**/*.cs`
**LP 参照ツリー**: `C:\MyProj\lilypond-src` (devel 2.25.35)
**抽出スクリプト**: `audit/scripts/Extract-LilypondRefs.ps1`
**生データ**: `audit/citation_drift.csv` (899行)

---

## サマリー

| Status | 件数 | 割合 |
|---|---:|---:|
| OK (ファイル存在 + 行範囲内) | 530 | 59% |
| NoRange (ファイル名のみ、行番号なし) | 285 | 32% |
| FileMissing (参照先 .cc/.hh が現 LP に無い) | **74** | **8%** |
| RangeOOB (行範囲がファイルサイズ超) | 10 | 1% |

**有効な citation (OK + NoRange + RangeOOB) = 825 / 899 ≒ 92%**

---

## 致命度 1: FileMissing (74件、修正必須)

LP 2.25.35 に存在しないファイルを参照している。**LP 系統的リネームの追従漏れ**。

| 件数 | 引用先 | 現 LP の実体 | 修正案 |
|---:|---|---|---|
| 34 | `lily/note-collision-interface.cc` | `lily/note-collision.cc` (interface 文字列を削除) | 一括 sed 置換 |
| 13 | `lily/grace-spacing.cc` | `lily/grace-spacing-engraver.cc` | 一括 sed 置換 |
| 8 | `lily/trill-spanner-engraver.cc` | C++ 実装は削除済み (Scheme `scm/scheme-engravers.scm` に移行) | 引用先を `scm/scheme-engravers.scm` + grob は `scm/define-grobs.scm` の `TrillSpanner` 定義へ変更 |
| 7 | `lily/glissando-engraver.cc` | C++ 実装削除 (`scm/scheme-engravers.scm` 移行) + `lily/line-spanner.cc` のロジック | 引用先を分割: ロジック → `line-spanner.cc`、生成 → `scheme-engravers.scm` |
| 6 | `lily/spacing-determine-shortest-duration-op.cc` | 不在。`spacing-spanner.cc` 内 `find_shortest()` 等に統合済み | 引用先を `spacing-spanner.cc` 内該当関数に変更 |
| 2 | `lily/dots.cc` | `lily/dots-engraver.cc` (Engraver) + grob は `scm/define-grobs.scm` の `Dots` | 文脈で振り分け |
| 2 | `lily/lyric-extender-engraver.cc` | `lily/extender-engraver.cc` (lyric- prefix 削除) | sed 置換 |
| 2 | `lily/skyline.hh` | `lily/include/skyline.hh` (パス) | パス修正 |

**着手単位**: `audit/scripts/Fix-CitationRenames.ps1` を作って一括書き換え (Phase 4 着手前に実施)。

---

## 致命度 2: RangeOOB (10件)

行範囲がファイルサイズを超える。多くは "1-200" のような切りの良い overshoot で、コード参照範囲というより「ファイル全体を見よ」の意図と思われる。実害低。

| Cs ファイル:行 | 引用先 | 範囲 | 実 LOC |
|---|---|---|---:|
| BeamScoringProblem.cs:888 | `lily/beam-quanting.cc` | 1372-1403 | 1402 (off-by-one or trailing whitespace) |
| Spring.cs:21,24,177 | `lily/spring.cc` | 1-250, 220-240 | 238 |
| VoltaBracketEngraver.cs:44, VoltaBracketItem.cs:25 | `lily/volta-bracket.cc` | 1-200 | 171 |
| VoltaBracketEngraver.cs:55,58 / VoltaBracketItem.cs:26 / SvgRenderer.cs:3796 | `scm/define-grobs.scm` | 4850-4900 (4870, 4865) | **4414** |

**処置**:
- `scm/define-grobs.scm` の RangeOOB は実害あり (450行ズレ)。grob 定義の位置を再特定して修正。
- それ以外 (volta-bracket / spring / beam-quanting) は範囲を実 LOC 内に丸めるか "see file" 表記化。

---

## 致命度 3: NoRange (285件)

行番号なしの一般参照。実害は無いが、Phase 4 の修正時に行番号を付与するとレビュアビリティが上がる。

NoRange のうち上位:
- `scm/define-grobs.scm`: 47件 (grob 定義は LP 側で頻繁に行が動くので range なしは妥当)
- `lily/grob-property.cc`: 15件
- `lily/context-property.cc`: 12件
- `lily/hara-kiri-group-spanner.cc`: 11件 ← Phase 4 で line range 補完
- `scm/output-lib.scm`: 9件
- `lily/clef-engraver.cc`: 9件
- `lily/system-start-delimiter.cc`: 8件
- `lily/key-engraver.cc`: 8件
- `lily/figured-bass-engraver.cc`: 8件

---

## 致命度 4: 引用ゼロの load-bearing LP ファイル (16件)

Phase 0 偵察で挙げた **load-bearing 50 ファイル** のうち、LilySharp が一切引用していないもの (FileMissing 含めても無し)。**実装欠落 or 暗黙実装の疑い**。

### HIGH (アルゴリズム本体の疑い)
- `lily/optimal-page-breaking.cc` (254 LOC) — 最適 page break。LilySharp `PageBreaker.cs:312-449` は 2D DP を独自実装するが LP 由来 citation なし。Phase 1-4 で精読要。
- `lily/break-substitution.cc` — spanner の改行分割。spanner が改行をまたぐ際の grob 分割を担う。LilySharp に対応箇所なし → 実装欠落の可能性大。
- `lily/dot-column.cc` — 付点配置。Roadmap I-4 で言及されるが、現状で `dot-column.cc` への citation がゼロ → I-4 着手時にゼロから書き起こし。
- `lily/multi-measure-rest.cc` — 多小節休符。`MultiMeasureRest` grob の生成・幅計算。実装の有無を Phase 1-3 grob coverage で要確認。
- `lily/rest-collision.cc` — 多声部での休符配置。
- `lily/note-head.cc` — note head 自体のロジック。
- `lily/grace-spacing-engraver.cc` — `grace-spacing.cc` 名で引用されていたものは FileMissing 化。実体ファイルへの引用ゼロ。

### MEDIUM (インフラ層、暗黙実装の可能性あり)
- `lily/paper-book.cc` (826 LOC) — ドキュメント構造。LilySharp の `LayoutEngine.cs` が独自実装している可能性あり。
- `lily/paper-score.cc` (160 LOC) — タイミング/spacing オーケストレーション。
- `lily/spanner.cc` (648 LOC) — spanner 基底。LilySharp は engraver-per-spanner 構造で代替している可能性あり。
- `lily/item.cc` (309 LOC) — item 基底。
- `lily/context.cc` (1050 LOC) — context 木。LilySharp は C# DSL/grob tree で代替の可能性。

### LOW (幾何プリミティブ — インライン実装で許容)
- `lily/bezier.cc`, `lily/box.cc`, `lily/skyline-pair.cc`
- `lily/open-type-font.cc`, `lily/pango-font.cc` (Pango 不在は roadmap で既知)

---

## Top 15 cited LP ファイル (健全性確認)

| Rank | LP ファイル | OK 件数 |
|---:|---|---:|
| 1 | `scm/define-grobs.scm` | 109 |
| 2 | `lily/beam-quanting.cc` | 61 |
| 3 | `lily/page-layout-problem.cc` | 31 |
| 4 | `lily/skyline.cc` | 24 |
| 5 | `lily/simple-spacer.cc` | 23 |
| 6 | `lily/tuplet-bracket.cc` | 20 |
| 7 | `lily/stem.cc` | 15 |
| 8 | `lily/include/constrained-breaking.hh` | 14 |
| 9 | `lily/lyric-engraver.cc` | 14 |
| 10 | `lily/page-spacing.cc` | 12 |
| 11 | `lily/note-spacing.cc` | 12 |
| 12 | `lily/hairpin.cc` | 11 |
| 13 | `lily/tie-formatting-problem.cc` | 11 |
| 14 | `lily/constrained-breaking.cc` | 10 |
| 15 | `lily/spacing-basic.cc` | 10 |

**観察**: 集中している分野 (beam quanting, page layout, skyline, tuplet) は精度高い citation を維持。spacing/breaking 系も健全。

---

## アクションサマリー

### Phase 4 開始前 (即時作業可能)
1. **FileMissing 74件の一括 sed 置換** (1h):
   - `note-collision-interface.cc` → `note-collision.cc` (34件)
   - `grace-spacing.cc` → `grace-spacing-engraver.cc` (13件)
   - `dots.cc` → `dots-engraver.cc` (2件)
   - `lyric-extender-engraver.cc` → `extender-engraver.cc` (2件)
   - `skyline.hh` → `include/skyline.hh` (2件)
2. **scheme-engravers 移行に伴う再引用** (30min): trill-spanner / glissando / lyric-extender 系 (15件) を `scm/scheme-engravers.scm` + 関連 .cc に分割引用
3. **spacing-determine-shortest-duration-op 系** (20min): `spacing-spanner.cc:find_shortest()` 周辺に再引用 (6件)
4. **define-grobs.scm:4850-4900 行の再特定** (10min): VoltaBracket grob 定義の現位置を grep して書き換え

### Phase 1-3 (grob coverage) 後判定
- `optimal-page-breaking.cc`, `break-substitution.cc`, `dot-column.cc`, `multi-measure-rest.cc`, `rest-collision.cc`, `note-head.cc` の各々につき:
  - LilySharp に等価実装があるか確認 (grep で類似アルゴリズム探索)
  - 等価実装がある場合 → 該当箇所に LILYPOND-REF を追加
  - 等価実装が無い場合 → Phase 4 タスクキューに追加

### Phase 4 実装規約
- すべての修正に LILYPOND-REF + 行範囲 + (推奨) 5-10行コード抜粋
- 行番号は LP 2.25.35 (現ソース) 基準で記録
