# Lily# 開発ハンドオフ（常設・単一）

> **このファイルが唯一の引継ぎ先。新しい `handoff-*.md` を作らないこと。**
> 引継ぎは §1「現在地」を**書き換えて**行う（追記しない）。恒久的な知識は §4 の表に従って
> それぞれの置き場所へ出す。ここに溜め込むと、以前と同じように 16 個に分裂する。

最終更新: 2026-07-21 / master `HEAD`（§1 で裏取りすること）

---

## 0. セッション開始時にやること（**必ず裏取り**）

```powershell
cd C:\MyProj\LilySharp
git log --oneline -8
git rev-list --count origin/master..master     # 未 push 数
git status --short
dotnet build LilySharp.Core\LilySharp.Core.csproj --no-incremental -v q
dotnet test  LilySharp.Tests\LilySharp.Tests.csproj -v q 2>&1 | Select-String 'Passed!|Failed!'
```

⚠️ **このドキュメントも memory もコード内コメントも、書いた時点のスナップショット。**
HEAD・テスト数・シンボル名・「完了」表記は開始時に実コードで再確認する。
過去の引継ぎでは stale な記述を毎セッション複数踏んでいる（§5.2）。

---

## 1. 現在地 ← **毎セッション書き換える**

**origin より 40 ahead で未 push**（push はユーザー判断。コミットは可）。HEAD は §0 で裏取り。
**テスト 0 failed / 3126 passed / 3 skipped。** Core・Cli とも build 0 warn / 0 err。
**LP 忠実度 17/21 exact, total |residual| = 0.338987 ss。**
**作業ツリーはクリーン**（未追跡の旧 `HANDOFF-*.md` 15個を除く。§8）。

`audit/scripts/Extract-EmmentalerMetrics.py` の WIP（LILC 移行）は**ユーザー解禁のうえ完遂・
コミット済み**（`9de790a2`）。もう「触らない」対象ではない。

### 残る残差は **4点 0.338987 ss、原因は2つだけ**

| 残差 | 点数 | 原因 |
|---|---|---|
| −0.149990 ×2、−0.034272 | **3** | **臨時記号→符頭の距離**（Lily# 0.866666〜1.300 / LP 1.034272〜1.450）。1点は `min_dist` 経由 |
| −0.004735 | 1 | **OPEN** — TimeSignature grob 幅 1.600000 / LP 1.604735 |

**次の一手はこの臨時記号 padding**（§2④'）。3点・0.334252 ss を1つの欠陥が持っている。

### 直近セッション（2026-07-21・後半）でやったこと

**§2① を完了。ただし着手前の計測で旧スコープが誤りと判明したので、設計をやり直してから実装した。**

| commit | 内容 |
|---|---|
| `f168ac57` | 計測ハーネスの穴を塞いだ。`2>&1` で LP の stderr が**dump 行の途中に割り込み**、MC の3番目の符頭が真っ二つになって黙って捨てられていた（`clef→次の音符`が**4番目**の符頭で測られる状態）。stream 分離＋パース失敗を throw に＋行中 gap も印字 |
| `a374317f` | **行中変更 item の LP モデルを導出**（`COORDINATE_AUDIT.md` §4.7.2、両側・両ケース 6 桁一致）。台帳4点の `why` を正しい原因に差し替え、README の stale な現状を更新 |
| `e3685efb` | §2①/③ を再スコープ（frame 修正だけでは 0 にならないと実測で確定したため） |
| `1970b830` | **§2① 実装**。行中変更を LP の専用列として価格付け（左右で別の式）。snapshot 3件 |
| `d6ace8b9` | ④の帰属訂正（`3b322821` で書いた修正手段が誤りだった） |
| `9de790a2` | **§2④ 実装**。グリフ bbox を LILC 由来に＋ advance→ink。snapshot 184件 |
| `28bcd106` | §2③ の測定フェーズ（`COORDINATE_AUDIT.md` §4.7.3、予測を先に置いた） |
| `0aae1016` | **§2③ 実装**。行頭 key/time の境界列。**予測が4点とも桁まで的中**。snapshot 14件 |

**重要**: 台帳の値自体は全部正しかった（19点すべて再現）。壊れていたのは**再現手段**のほう。

### 進行中で中断しているものは無い

**X 軸の変更 item は行中・行頭とも完結。** 次は §2④'（臨時記号 padding）。

⚠️ **`total |residual|` の履歴は点集合が違う。同じ集合の中でだけ比較すること。**
15点 4.592405 → 19点 11.435647（`84dc3a79` が行中4点を追加＝それまで測っていなかった発散の可視化）
→ 21点 4.747978（`1970b830`＋MKA 2点）→ 4.738987（`9de790a2`）→ 21点 **0.338987**（`0aae1016`）。

---

## 2. 短期ロードマップ（次の数セッション）

優先順。**①②は COORDINATE_AUDIT §4.7 の残り**で、ユーザー合意済みの順序。

### ① ✅ 完了（`1970b830`）— 行中（mid-measure）の変更 item を LP の専用列として価格付け

> **2026-07-21 に再スコープ → 実装。** 旧①は「3つの extent ヘルパの frame ＋ 定数2つ」だった。
> **着手前に測った結果、それでは 4 点は 0 にならないと分かった**（§5.3「変更する前に測る」が
> 効いた例）。導出済みモデルと実測は `COORDINATE_AUDIT.md` **§4.7.2**。
>
> **結果**: 行中4点 6.843242 → 0.000453 ss（`clef→次の音符`は**厳密一致**）。
> 残差は全て `GlyphMetricsGenerated.cs` の4桁丸め（符頭 1.3040 vs LP ink 1.304212 等）。
> **残った未了**: 分配が正しいのは force 0 のみ。LP は2本の spring を独立に伸ばし、
> key/time の右側は**伸びない**（`shrink-space`/`semi-shrink-space`）。これは本物の
> 第2列が要るので③と同じ仕事。`SpacingRules.MidMeasureChangeGaps` に記録してある。
>
> 以下は③のために残す記録（**行頭側は未着手**。`ChangeItemPrefixWidth` と
> `ClefChangePadding` は行頭パスにまだ残っている）。

**LP は行中の clef/key 変更に non-musical 列を1本立て、左右を別の式で価格付けする**:

| | 式 | 出典 |
|---|---|---|
| 列原点 | 変更グリフの **ink 左端** | 実測 |
| 左 gap | `max(ideal − 列幅, (ideal + min_dist)/2)` ＝実際は常に床側 | `note-spacing.cc:105-107` |
| 右 gap | `ink幅 + space-alist 距離` | `staff-spacing.cc:147-198` |
| 　clef | `next-note` **1.0** | `define-grobs.scm:924` |
| 　key | `next-note` が**無い**ので `first-note` **2.5**（shrink） | `define-grobs.scm:1947` |
| 　time | 同じく `first-note` **2.0**（semi-shrink） | `define-grobs.scm:3948` |
| `min_dist` の左 esw | Clef=既定 0.1 / Key=**0.0** / Time=**0.0** | `define-grobs.scm:1936` / `:3933` |

**MC/MK の左右4点すべて 6 桁一致でモデル確定済み。**

Lily# 側は列が無く、`ChangeItemPrefixWidth`（= W + 2×0.5）を**1本の spring に丸ごと加算**し、
描画側が `列X − (W + 0.5 + 次の臨時記号)` で**ぶら下げる**。実測分解:
`head2→head3 = (1.304 + CalculateLeftExtent(clef) 1.505 + 0.4) + 3.010 + 0.3 = 6.519000`（実測一致）。

→ **frame だけ直すと `+1.119 → +0.612` に減るが、左右の分配は変わらず逆符号のまま。**

**同時に直すもの**（片方だけだと値が破綻する）:

- `GlyphMetrics.ClefChangePadding = 0.5` は **space-alist のエントリ取り違え**
  （0.5 は `right-edge`。行中で効くのは `next-note` の 1.0）。**しかも左右で別の値**
- `GetItemToBarlineSpace` の変更 item エントリ `1.0` / `GetBarlineToItemMinimum` の `1.0/1.0/0.75`
- 変更 clef の幅 `FClefAdvance × 0.75 = 2.010` → LP の `clefs.F_change` ink は **2.146680**。
  `_change` グリフは実在し Lily# も描いている（`GlyphMetrics.cs:141-151`）。**これは独立した差**

**計測対象は台帳の `midmeasure.*` 4点**（着手前に測定済み）:

| 台帳キー | LP | Lily# | residual |
|---|---|---|---|
| `midmeasure.clef.prev-note-to-clef` | 2.253234 | 4.009000 | **+1.755766** |
| `midmeasure.clef.clef-to-next-note` | 3.146680 | 2.510000 | **−0.636680** |
| `midmeasure.key.prev-note-to-key` | 2.203234 | 4.654000 | **+2.450766** |
| `midmeasure.key.key-to-next-note` | 5.800030 | 3.800000 | **−2.000030** |

⚠️ **行頭（小節境界）の key/time は①では直らない。** 台帳の `barline.next.key-change-*` /
`time-change-*`（合計 −4.44 ss）の真因は③の型欠落。**①と③は同じ「列の欠落」**で、
行中か行頭かが違うだけ。**まとめて設計したほうがよい**（未合意・要判断）。

⚠️ 変更 item が extent ヘルパに到達する production 経路は
**`CalculateSkylineDistance` の3箇所のみ**（null-prev / null-next / skyline fallback）。
小節頭では `ChangeItemPrefixWidth`（= W + 2×0.5）が
`GetBarlineToItemMinimum + CalculateLeftExtent`（= W/2 + 1.5）を **W ≥ 1.0 で常に上回る**ので、
小節頭側は frame を直しても出力中立になるはず。**予測なので実測で確認すること。**

### ② `CalculateRightExtent` の統廃合

production 呼び出し元ゼロ、`SvgTests.cs:166-167` のみ。しかもそのテストは左（左端基準）と
右（中心基準）を**ペアで**使い、同じ box を2フレームで測っている。
§5.1 の削除手順（横断 grep →`<see cref>` →**ユーザー承認**）を経ること。

### ③ ✅ 完了（`0aae1016`）— 行頭 key/time の境界列

モデルは `COORDINATE_AUDIT.md` §4.7.3。**着手前の予測が4点とも桁まで的中**した:

| 台帳キー | 実装前 | 予測 | 実測 |
|---|---|---|---|
| `barline.next.key-change-glyph` | −0.500000 | 0 | **0** |
| `barline.next.time-change-glyph` | −0.250000 | 0 | **0** |
| `barline.next.key-change-to-notehead` | −2.234272 | −0.034272 | **−0.034272** |
| `barline.next.time-change-to-notehead` | −1.454735 | −0.004735 | **−0.004735** |

**4.439007 → 0.039007 ss、exact 15/21 → 17/21。** snapshot 14件。

**未モデル化（意図的）**: LP は key 変更を `KeyCancellation` と `KeySignature` の**2 grob**に分け
間に 0.5 を置くが、Lily# は1つの `KeySignatureChangeItem` に畳んでいる。コーパスは踏まない
（probe K は 0→3 個で cancellation が出ない）。`BoundaryChangePrefix` に記録。

⚠️ **`BoundaryColumn.cs`（clef を bar line の前に置く既存の型）とは別物**。今回入れたのは
`SpacingRules.BoundaryChangePrefix` ＋ `BarlineToFirstColumnSpring` の `last_grob` 切替。
両者の統合は未着手。

### ④' 臨時記号 → 符頭の距離（**次の一手**・未着手）

**残る残差 4 点のうち 3 点・0.334252 ss を1つの欠陥が持っている。**

| 台帳キー | residual | 経路 |
|---|---|---|
| `barline.next.accidental-to-notehead` | −0.149990 | 直接 |
| `midmeasure.key-cancel.key-to-next-note` | −0.149990 | `min_dist` 経由（`:213` 補正） |
| `barline.next.key-change-to-notehead` | −0.034272 | 同上（補正が binding しなくなる分だけ小さい） |

Lily# は `CalculateLeftExtent` で `accBBox.Width + AccidentalNoteGap(0.2)`。実測:

| | LP の距離 | グリフ幅 | 差 |
|---|---|---|---|
| ♯（probe X） | 1.450000 | 1.100010 | **0.349990** |
| ♮（score K） | 1.034272 | 0.666666 | **0.367606** |

⚠️ **差が一定でない**（0.349990 vs 0.367606）ので、**単なる定数 0.2→0.35 ではない。**
`Accidental_placement` の別の式（`right-padding` ＋ グリフごとの何か）を疑うこと。
**着手前に摂動法で LP を割ること**（§5.3）。

### ④ ✅ 完了（`9de790a2`）— グリフメトリクスを LILC 由来に

**LP はグリフ bbox を、フォント埋込の `LILC` テーブルから読む**
（`lily/open-type-font.cc:288` `load_scheme_table("LILC")` ＋ `:389-407`。生アウトラインは fallback）。
`GlyphMetricsGenerated.cs` はアウトライン（`BoundsPen`）から取っていた＝**非 LP 方式**で、
これが台帳に残っていた 1e-4〜1e-3 級の残差の**唯一の原因**だった。

入れたもの: 生成器を LILC 優先に／出力を **6桁**に／`ApplyLeftHeadWidth` と
`GetKeySignatureAccidentalWidth` を **advance → ink extent** に／変更 clef も生成器から。

**7点が 0 に**（`barline.prev.whole-note` `.half-note` `barline.next.down-stems-after-clef`
`midmeasure.clef.prev-note-to-clef` `midmeasure.key.prev-note-to-key`
`midmeasure.key.key-to-next-note` `midmeasure.key-cancel.prev-note-to-key`）。
**8/21 → 15/21 exact。** snapshot 184件・承認のうえ再ベース。

⚠️ **踏んだ罠（再発しやすい）**: 生成 bbox から派生する定数を `static readonly` にすると、
**partial クラス間の静的初期化順序は C# で未定義**なので既定値の `BBox`（=0）を読む。
変更グリフの幅が全部 0 になり clef が自分の gap から消えた。**プロパティにすること。**

⚠️ `down-stems-after-clef` が閉じたのは**予測外**。残差 +0.00002 は「符尾の符頭接続オフセット差」
と帰属されていたが、実際は符頭メトリクスだった。**帰属は閉じてみるまで確定しない**例。

### ⑤ MMR run のグルーピング

**LP は clef があっても run を保つが Lily# は弾く。** 詳細は `handoff-2026-07-21-mmr-runs.md`
（§8 で棚卸し対象）。

---

## 3. 長期ロードマップ

### A. LP 忠実度を測定可能にし、単調に上げる ★中心

**現状 15/21 exact, total |residual| = 4.738987 ss**（`audit/lp-geometry/`）。

これがこのプロジェクトの品質指標。snapshot は「前回の自分」との比較なので、一度承認した誤りは
永久に緑のまま。台帳は **LP との距離**を数値で持ち、増減どちらでもテストが落ちる。

- 短期: X 軸（§2 ①③）で残差を潰す
- 中期: **コーパスを縦（Y）にも広げる** — 現在は bar line 周りの X のみ。
  譜間距離・スラー/タイ・ビーム・臨時記号配置に点を足す
- 原則: **snapshot を再ベースするたびに、LP 照合済みの点が増えているべき**

⚠️ **既知の穴**: 以下2つの LP 検証は**数値がコメントに残っているだけで、プローブが未 commit**
（scratchpad に置いたまま消える）。コーパスの「再実行可能」原則から外れているので、
次に触るとき `audit/lp-geometry/probes/` に移すこと。
- **stretch strength 0.45 の検証**（同じ音楽を 120mm / 180mm で justify し、force を解いて
  独立な spring で交差検証）→ 数値は
  `SpacingInvariantTests.BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance` に
- **符尾 Y extent のダンプ**（光学補正の 2×2 の裏取り）→ 数値は
  `SpacingRules.BarlineToNextNotesCorrection` の remarks に

### B. 座標系の LP 統一を完了させる（COORDINATE_AUDIT §4.6）

起票時の実バグ8件は全て対処済み。残るのは「数値は正だが frame 忠実性が未完」の3系統:

| | 内容 | 状況 |
|---|---|---|
| ① | 譜間/system 縦積みの Y-down 残存（**島1**） | 🔄 YFlip 配線と全 grob の Y-up 化は完了。残＝共有 device stacking の de-island（`OutsideStaffStacker` 等）＋ `system.Y`/`staff.Y` の Y-up 格納（W2） |
| ② | device 島群（**島2**） | ⏸ 繰延。TieVariant / 水平 skyline の Y horizon / TabStaffGeometry / beam collision island |
| ③ | non-musical PaperColumn の欠落 | 🔄 §2 ③ |

**X（③）と Y（①）は独立に進められる。** 島1 は boundary-shim で byte 不変移行できることが実証済。

### C. 未移植 LP 計算の取り込み

tuplet on-line / volta shorten / hairpin niente / ledger / brace / 開 chord / Ignatzek。
出典 `HANDOFF-lp-calc-incorporation.md`（§8）。**未検証の一覧なので、着手前に実コードで裏取り。**

### D. 言語・ツール側（X/Y 座標系とは独立）

いずれも**この一覧は伝聞。着手前に実コードで確認すること。**

- MusicXML インポート — ほぼ完遂、実ファイル検証が残
- AI 協調編集 M1–5（Ctrl+I / 譜面選択 / 補完 / BYO-key）— 実機 E2E 未検証
- 文法改善 5 件 — 糖衣 `c?` / `c!` 未実装。0.3.0 リリースは GO 待ち
- Dead-code 監査 — アナライザ検出分は完了、手動分が残
- `LILYPOND-REF` 行番号の一括再採番（cosmetic・繰延）
- `IDrawingContext.cs:37-39` の remark が装飾前後2フレームを記述していない（§4.4）

### E. 保守性の負債（このセッションで見つけたもの）

- `DrawingTransform.Identity` は `new()` なので **`ScaleX/ScaleY = 0`**、
  `Identity.IsIdentity` 自体が false。record struct はプライマリコンストラクタの既定値を
  適用しない。出荷 3 backend は無害だが、**記録用コンテキストの作者を2人捕まえている**。
  `Identity => new(0,0,1,1)` に直す価値あり（未実施・要判断）
- `SharedRendererBeamTests` と `LpFidelity/RecordingDocumentContext` に記録用コンテキストが
  **2実装ある**。統合は既存の通っているテストに触るので要判断

---

## 4. 知識の置き場所 ← **増殖防止の核心**

**引継ぎ文書が増えたのは、寿命の違うものを1つに混ぜたから。** 種類ごとに置き場所を決める。

| 知識の種類 | 置き場所 | 例 |
|---|---|---|
| **LP の幾何の実測値** | `audit/lp-geometry/`（プローブ＋台帳） | bar line → 符頭 = 0.900000 |
| **LP の式・定数の出典** | **コード内 `// LILYPOND-REF:`** | `staff-spacing.cc:213` の 0.3 |
| **LP の挙動で驚いたこと** | コード内コメント（数値つき）＋ user memory | 光学補正は clef でなく符尾 |
| **座標系の現状と残作業** | `docs/COORDINATE_AUDIT.md` | §4.5 の対処状況表 |
| **アーキテクチャの意図** | `docs/*.md`（既存の該当ファイル） | `SKYLINE_ARCHITECTURE.md` |
| **不変条件** | **テスト**（`SpacingInvariantTests` 等） | 両 spring 系の一致 |
| **現在地・次の一手・ロードマップ** | **このファイル §1–§3** | |
| **ユーザーの好み・作業規律** | user memory | 「done は push 済みで」 |

**ここに書かない**: LP の式の導出、実測値の生データ、アーキテクチャ解説、コードの説明。
それらは上表の置き場所へ。このファイルには**ポインタだけ**置く。

> 判断に迷ったら: 「これは**次のセッションだけ**必要か、**ずっと**必要か？」
> ずっと必要ならこのファイル以外へ。

---

## 5. 恒久ルール（滅多に変わらない）

### 5.1 ワークフロー規律

- **master 直コミット。ブランチを勝手に作らない**（作成・削除は GO 待ち）
- **1 島 / 1 関心 = 1 commit**。ただし**依存があるなら同時投入**し、message に
  「単独では入れられない」と書く（frame と定数のように、片方だけだと壊れるケース）
- **巨大ファイルを分割しない**
- **Co-Authored を付けない**。message に「何を・なぜ・**検証結果の数値**」＋
  **未完・残差・意図的に触らなかった点**を明記
- コミットは**関係ファイルのみ明示 `git add`**（無関係の `.py` / handoff を混ぜない）
- **push はユーザー。「done」は push 済みでのみ主張。ship = 全緑 ＋ 明示承認**
- **出力を変える変更はユーザー承認前に出荷しない。** snapshot 再ベースも
  **LP 照合 → 承認 → 実行**
- **シェルは pwsh MCP / ripple（bash 禁止）**。ファイル書き込みは Write ツール
  （`Set-Content` 直書き禁止。**PowerShell に heredoc は無い** — commit message は
  ファイルに書いて `git commit -F`）
- **「未使用に見える」≠「消してよい」。** 削除前に `.cs` 以外も横断 grep →`<see cref>` 確認
  → 削除後にヘルパが孤立しないか再 grep →**ユーザー承認**

### 5.2 LP 移植の原則

- レイアウト/描画は `C:\MyProj\lilypond-src` の `lily/*.cc` を**符号一致で字面移植**。
  関数名・変数名・符号・丸めまで揃える。**独自の近似・辻褄合わせを入れない**
- **移植したら必ず `// LILYPOND-REF: lily/xxx.cc:行` を付ける**（定数1つ、式1つでも）
- **座標系が揃っていなくて字面移植が難しいときは、勝手に変換して押し込まず報告する**
- **既存の移植を先に探す。**「未実装」でなく「書いてあるが呼ばれていない/引数が違う/
  frame が違う」ことが本当に多い
- **分岐は全部書く**（`space-alist` の型を無視して値だけ使う類の手抜きをしない）
- ⚠️ **doc / コメント / 過去の自分の結論を疑う。ただし「疑った結果」も裏取りする。**
  `LILYPOND-REF` が付いていても式が一致しているとは限らない
- ⚠️ **同名プロパティが grob ごとに別の値**を持つ（`stem-spacing-correction` は
  StaffSpacing 0.4 / NoteSpacing 0.5）。**単位も別**（staff-spacing.cc は staff-space、
  note-spacing.cc は staff position。どちらも /7 するので2倍ずれる）

### 5.3 測定の原則

- **推論せず測る。** 実測 → 予測との照合 → 一致しなければ**まず自分の当てはめを検算**
- **摂動法が強力**: `\override` で esw / padding を振り、係数1で追随するか不変かを見る。
  **全部ゼロにして残った定数**がハードコード値
- **測定 regime を混ぜない。** ragged-right（force 0）では spring の床、圧縮時は rod が
  binding する。**どちらで測ったか必ず記録する**
- **配置は「両側」を測る。** ある grob の位置は前後2つの間隙で決まる。
  さらに**同じ box の左右が同じ基準点か**を確かめる
- ★ **残差の符号で原因を切り分ける。** あるグリフの**左右の残差が逆符号**なら
  **frame（基準点）の誤り**、**同符号**なら**定数の誤り**。定数が違えば両側とも同じ向きに
  ずれるが、基準点がずれていると片側が広がった分だけ反対側が狭まるため。
  行中 clef/key 変更でこれを使って診断した（`midmeasure.*` の4点。§2①）
- ★ **変更する前に測る。** 変更後に測ると「LP に近づいたか」を判定できない。
  着手前にコーパスへ点を足しておけば、**反証可能な予測**（この4点が揃って 0 に向かうはず）
  になり、外れたときに診断が違うと分かる
- **「悪化した」＝「変更が間違い」ではない。** 間違った定数が別の欠陥を隠している構図は実在する
- ⚠️ **SVG から精密測定をしない。** 座標は `F2`（`SvgGenerator.cs:229`）で2桁に丸められる。
  6桁の LP 値と比べるなら `LpFidelity/RecordingDocumentContext` を使う
- ⚠️ **紛らわしい数値に飛びつかない。** 6桁一致しないなら別物と疑う
  （残差 0.189365 を「bar line 幅 0.19」と誤認した実例あり）

### 5.4 テストの原則

- **実装の定数を実装自身と比べるテストは何も守っていない。**
  LP 由来の期待値を書き、なぜその値かを `LILYPOND-REF` で示す
- **テストが LP と食い違ったら、テストを実測に合わせる**（再ピン止めしない）
- **追加したテストが「修正前なら落ちる」ことを実証する**
- **1点狙い撃ちにせず掃く**（掃引テストは改行位置が動いても空振りしない）
- 増分再利用（F3）: **小節幅に影響する新要素は `MeasureContentKey` に必ず畳み込む**。
  「隣の小節の内容で決まる」量は intrinsic hash から復元できないので**明示的に**足す
- spring は 2 系統＋改行 gate の 3 箇所を**必ず一致**させる
  （`MeasureLayouter.CreateTimingSprings` / `SpacingRules.CreateSpringsForMeasure` /
  `SystemBreaker`）

### 5.5 環境の落とし穴

- **dotnet の増分ビルドが腐る** → 前後比較では `--no-incremental` でビルドして
  `dotnet run --no-build`。なお `dotnet test` は `--no-incremental` を受け付けない
- **LilyPond は Guile デッドロックする** → `cmd /c "... < NUL"` でデタッチ必須。
  終了コード 1 でもダンプは出ている
- **コンソールの文字化けに騙されない。** ファイル実体は正しいことが多い。Read で確認してから判断
- **fixture**: Lily# の `octave absolute` は LP より一段高い（**LP `c'` ↔ Lily# `c`**）。
  既定は相対オクターブ。mid-music の key/time/clef 変更はバックスラッシュ無し。
  空小節は `| |` ペア。part 名に予約語を避ける（`p` は dynamic）

---

## 6. コマンド集

```powershell
# ビルド（--no-incremental 必須）
dotnet build LilySharp.Core\LilySharp.Core.csproj --no-incremental -v m   # 0 warn/err 期待
dotnet build LilySharp.Tests\LilySharp.Tests.csproj --no-incremental -v q

# 全テスト
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build -v q 2>&1 | Select-String 'Passed!|Failed!|\[FAIL\]'

# LP 忠実度スコア
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build `
  --filter 'FullyQualifiedName~Corpus_ReportsTotalDivergence' --logger 'console;verbosity=detailed'

# LP 実測（プローブを LilyPond に通す）
pwsh audit\lp-geometry\Measure-LilyPondGeometry.ps1

# snapshot 再ベース（LP 照合＋ユーザー承認の後のみ・フィルタを掛けない）
$env:LILYSHARP_UPDATE_SNAPSHOTS = "1"
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build -v q
Remove-Item Env:\LILYSHARP_UPDATE_SNAPSHOTS
"ENV NOW = [$($env:LILYSHARP_UPDATE_SNAPSHOTS)]"    # ← 空であることを必ず目視
# → env を消して再実行し全緑を確認 ＋ git status で「動いたのは意図した snapshot だけ」を確認

# 目視用 PNG / SVG
dotnet run --project LilySharp.Cli -- png --crop --scale 4.0 "NAME.lys" "out.png"
```

- fixtures = `LilySharp.Tests\Fixtures\{test,showcase}\*.lys`、
  snapshot は `Snapshots\<dir>__<name>.svg`
- snapshot テストは `SvgSnapshotTests.TestSamples()` の**明示 `yield return` リスト**
  （`.lys` を置くだけでは走らない）

---

## 7. セッション終了時チェックリスト

1. [ ] 全緑を確認（`Passed!` の数を §1 に書く）
2. [ ] **§1「現在地」を書き換える**（追記しない）— HEAD / ahead 数 / テスト数 / やったこと
3. [ ] ロードマップ（§2/§3）が動いたなら更新する。**完了した項目は消す**
4. [ ] このセッションで得た**恒久的な知識**を §4 の表に従って置き場所へ出す
      （LP 実測値 → `audit/lp-geometry/`、LP の式 → コード内 REF、座標系の状態 →
      `COORDINATE_AUDIT.md`）
5. [ ] **新しい `handoff-*.md` を作っていないことを確認**
6. [ ] `git status` で意図しないファイルが混ざっていないか確認
      （特に `audit/scripts/Extract-EmmentalerMetrics.py`）

---

## 8. 旧 handoff ファイルの棚卸し

root に **15 個の未追跡 `HANDOFF-*.md` / `handoff-*.md` / `REVIEW-HANDOFF.md`（計 364 KB）**が
残っている。
各ファイルが「原則・手順」を丸ごと重複コピーしており、これが増殖の主因だった。
**本ファイルがそれらを置き換える。**

⚠️ ただし中身には未回収の知識が残っている可能性がある。**一括削除しないこと。**
以下は着手時に参照する価値がある順:

| ファイル | 参照価値 |
|---|---|
| `handoff-2026-07-21-x-frame-unification.md` | ✅ 内容は完了・`COORDINATE_AUDIT.md` §4.7 と本ファイルに吸収済。**光学補正を clef のせいとする誤記あり**（訂正済） |
| `handoff-2026-07-21-boundary-column.md` | §2③ 着手時に。LP 事実の記録として有用 |
| `handoff-2026-07-21-mmr-runs.md` | §2⑤ 着手時に |
| `HANDOFF-stage4-vertical-yup.md` | §3B 島1 着手時に。**Stage-4 の正確な現状はここ** |
| `HANDOFF-lp-calc-incorporation.md` | §3C 着手時に |
| `HANDOFF-dead-code-audit.md` | §3D 着手時に |
| `HANDOFF-2026-07-20-*.md`（5本） | 過去セッションの記録。LP 事実は概ね吸収済 |
| `HANDOFF-beam-quanter-unification.md` / `HANDOFF-coord-frame-unification.md` / `HANDOFF-layout-x-unification.md` | 完了済み作業の記録 |
| `REVIEW-HANDOFF.md` | 規律は §5.1 に吸収済 |

**回収方針**: 各テーマに着手するとき、そのファイルを読んで**必要な知識を §4 の置き場所へ移し、
読み終えたファイルを削除する**（削除は都度ユーザー承認）。一気にやらない。

なお `AI_POSITIONING_HANDOFF.md` と `docs/arpeggio-rework-handoff.md` は**追跡済み**で
別系統。本ファイルの対象外。
