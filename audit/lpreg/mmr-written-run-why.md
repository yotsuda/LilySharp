# 「書かれた休符 1 つ = MMR 1 本」— 移植前の予測（第128セッション）

## claim（LP 実測・第127第4便の `pcmsh-r1.log` を再読して裏取り済）

`\compressMMRests` の下で LilyPond 2.26.0 は:

| 綴り | MMREST grob | bars | MMNUM |
|---|---|---|---|
| `R1 \| R1 \| R1` | **3 本** (x=0.0 / 14.985 / 22.875) | 各 **1** | **無し** |
| `R1*3` | **1 本** (x=0.0) | **3** | `"3"` |

⇒ **LP の MMR は「書かれたイベント 1 つ = 1 本」**。`\compressMMRests` は
**N 小節イベントを圧縮するだけ**で、別々に書かれた休符を結合しない。

Lily# は現在 `R1 | R1 | R1` を **1 本（3 小節・番号 "3"）**にする。
root = `MultiMeasureRestEngraver.FindRuns` の `OpensNewRun` が
**先頭の clef/key/time 変化**しか見ておらず、**書かれた休符の境界を見ていない**。
モデルにも印が無い——`MeasureCollector.MusicWalk.cs:597` が
`for (i<count) builder.AddItem(restItem)` と**同一コピーを N 個**置くので、
どれが書き始めか区別できない。

## 修理形

1. `RestItem` に `OpensWrittenRun`（既定 **true** ＝「この項目は自分自身が書かれたイベント」）。
2. `R…*N` の展開コピー **2..N 本目だけ** `false` にする（1 本目は書き始め）。
3. `OpensNewRun` が break-aligned 変化に加えて**この印**も読む。

既定を true にすると、他の生成経路（PartCombiner の `with`・spacer・arpeggio 等）は
**1 行も触らずに正しい**——どれも「自分自身が書かれたイベント」だから。
落とすのは MusicWalk の展開ループ 1 か所だけ。

## 予測（実装前に書く・反証可能な形で）

**⑴ snapshot は 1 枚も動かない。**
census（`.lys` 全 219 fixture＋全 corpus）で **`Fixtures\` 配下に隣接した書かれた `R` は 0 件**。
MMR を持つ 10 冊は**全部が音符小節で run を分けている**（`multi-measure-rest-single.lys` は
コメントに「Note measures separate the R-runs so each stays its own church rest」と
**明記までしてある**）。⇒ **テスト 4308 は全緑のまま**。

**⑵ 隣接が在るのは corpus の外**（census の全 10 件）:
`samples\canon-in-d.lys`（`R1 | R1 | R1 | R1` ＝ **出荷サンプルが LP と食い違っている**）・
`audit\lp-regression\lys\key-signature-space.lys`（`R1 || R1`）・
`output\review\20-edge-cases.lys`（`R1 | R1*3`）・`scratch\dogfood\trio.lys` ほか scratch 6 冊。

**⑶ ⚠️ 「出力が動かない」を no-op ではなく*効いた*と読むための別観測**（§5.0 の釘）。
予測 ⑴ が当たると snapshot も台帳も 1 つも鳴らないので、**修理が効いた証拠が要る**。
⇒ **run の割り当てそのものを観測する対**を新規に書く:
- `R1 | R1 | R1` → `FindRuns` が **3 本（各 Count=1）**
- `R1*3` → **1 本（Count=3）** ← **恒等の対**（LP では同じ 3 小節の沈黙・LP の差は上表で実測済）
- `R1*2 | R1*2` → **2 本（各 Count=2）**＝「N を数え直しているだけ」ではないことの陽性対照

**⑷ 反証**: 上以外に snapshot が動いたら、census が構文を取りこぼしたか、
**印がコピー 2..N に漏れている**——漏れれば `R1*N` が N 本の 1 小節休符に砕けるので
`multi-measure-rest{,-long}` の snapshot が**大きく**動く（静かには壊れない）。

## 未着手として開示するもの

- **複数譜での境界**: `OpensNewRun` は「どれか 1 譜が書き始めたら全譜で切る」既存の
  break-aligned 規則にそのまま乗る。第127 の `pcmsh-a.ly` 実測（part1 8|16|4 対 part2 16|8|4 →
  **4 本 = 8/8/8/4**・境界 0/8/16/24）と**同じ規則**だが、あれは `\partCombine` で
  **1 譜**の話。**素の 2 譜（各譜が独立の MMR spanner を持つ形）は測っていない。**
