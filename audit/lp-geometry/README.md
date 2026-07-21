# LP 忠実度コーパス（LP fidelity corpus）

**Lily# の幾何を、実際の LilyPond で測った値に対して固定する。** snapshot が Lily# を
「前回の自分」に対して固定するのに対し、こちらは **LilyPond に対して**固定し、
残っている差を「原因つきの数値」として台帳に載せる。

## なぜ必要だったか

これまで LP 実測値は毎回 scratchpad に取られて捨てられていた。結果として

- 同じ値を毎セッション測り直していた（引継ぎに「取得済」と書いてあっても実体が無い）
- **誤った解釈が何世代も引き継がれた** — bar line 直後の光学補正を「clef のせい」と
  書いた記述が複数の handoff を生き延びた。実際は**下向き符尾**が原因で、
  2×2 を測れば一発で分かる（`probes/barline-spacing.ly` の score A/B/C/D）
- snapshot は Lily# 同士の比較なので、**一度承認された誤りは永久に緑**のまま

## 構成

| 場所 | 役割 |
|---|---|
| `probes/*.ly` | **LP 側のプローブ**。committed・再実行可能。値の出所がここにある |
| `Measure-LilyPondGeometry.ps1` | プローブを LilyPond に通して台帳用の数値を印字する |
| `lp-geometry.json` | **台帳**。LP 実測値 ＋ 現在の residual ＋ その原因 |
| `LilySharp.Tests/LpFidelity/LpGeometryProbes.cs` | **Lily# 側のプローブ**（同じ音楽を .lys で書いたもの）と測る量の定義 |
| `LilySharp.Tests/LpFidelity/RenderedGeometry.cs` | 描画結果を LP と同じ語彙（anchor 間距離）で問い合わせる |
| `LilySharp.Tests/LpFidelity/RecordingDocumentContext.cs` | 実際の描画パスを記録する `IDocumentContext` |
| `LilySharp.Tests/LpFidelity/LpGeometryLedgerTests.cs` | 台帳を強制するテスト |

## 台帳の意味論

```
residual = lilysharp - lilypond      (単位: staff-space)
```

- residual が **0** = その量は LP と一致している
- residual が **非0** = `why` に**原因を書くことが必須**。まだ切り分けられていないなら
  `OPEN:` で始めて「何が未解明か」を具体的に書く。**禁止なのは黙って baseline 化すること**
- テストは **両方向で失敗する**。residual が増えれば回帰、**減っても失敗**する
  （改善を diff に残さず吸収してしまわないため）

「一致数 / total |residual|」がこのプロジェクトの忠実度スコア。**単調に良くなるべき数値**。

## 精度についての注意

**SVG から測ってはいけない。** `SvgGenerator` は座標を `F2` で出力する（`SvgGenerator.cs:229`）
ので 0.01 に量子化され、0.005 未満の残差は消える。LP 側は 6 桁で語る世界なので、
0.189365 と 0.142857 と 0.19 の区別がノイズに埋もれる。

そのため Lily# 側は `SharedRenderer.RenderTo` に**記録用の `IDocumentContext`** を渡して
`double` のまま取る。製品と同じ描画パスを通るので、別実装がドリフトする心配もない。

実際これのおかげで、SVG では見えなかった **0.002 / 0.0013 の閉じ側の差**
（`barline.prev.*`）が初めて可視化された。

## 点を追加する手順

1. `probes/*.ly` に LP 側のスコアを足す（`\lay "TAG"` でタグ付け）
2. `pwsh audit/lp-geometry/Measure-LilyPondGeometry.ps1` を実行し、印字された値を得る
3. `lp-geometry.json` に `lilypond` の値だけ入れ、`residual` は `null` のままにする
4. `LpGeometryProbes.cs` に **同じ音楽の .lys** と測る量を足す
5. テストを走らせる → `residual` を教えてくれるので、それを台帳に記録する
6. 非0なら `why` を書く。書けないなら `OPEN:` で何が未解明かを書く

### ⚠ 二つの側を必ず一致させること

- **オクターブ表記が違う**: Lily# の `c` は LilyPond の `c'`。
  `LpGeometryProbes.cs` の各プローブは対応する LilyPond 表記をコメントで明記している
- **グリフの数え方が違う**: LilyPond は調号を `KeySignature` 1個として dump するが、
  Lily# は臨時記号を1個ずつ描く。**インデックスで数えず、種類で選ぶ**こと
  （`BarlineRightToNextNotehead` がその例）
- 測る量は**すべて anchor 間距離**にしてある。ink 幅に依存させると、
  「監査したいはずのメトリクス表」を使って監査することになる

## 測定 regime を混ぜない

プローブは `ragged-right`（force 0 ＝ 自然長）で測っている。**同じ量でも
justify された行では binding する制約が変わる**（ragged では spring の床、
圧縮時は rod）。伸び（stretch strength）の検証は別枠で、
`SpacingInvariantTests.BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance`
が2つの行幅から force を解いて独立な spring で交差検証している。

## 現状（2026-07-21 時点）

21 点中 **15 点が LP と厳密一致**、total |residual| = **4.738987 ss**。
残る6点は**たった2原因**:

| residual | 点数 | 原因 |
|---|---|---|
| −2.234272 / −1.454735 / −0.500 / −0.250 | 4 | **行頭**の key/time 変更が break-align 列に入っていない（§3.I・型の欠落） |
| −0.149990 ×2 | 2 | 臨時記号→符頭の **padding** が Lily# 0.2 / LP 0.349990（グリフ幅は一致済み） |

- **行中の clef/key 変更**（4点・6.843242 ss）は `1970b830` で解消
- **グリフメトリクス**（7点）は `9de790a2` で解消 — LP と同じく `LILC` テーブルから読むようにした

⚠️ **点を足すと total は増えうるので、比較は同じ点集合の中でのみ意味を持つ。**
15点 4.592405 → 19点 11.435647（`84dc3a79` が**それまで測っていなかった**行中の発散を可視化）
→ 21点 4.747978（`1970b830`＋MKA 2点）→ 21点 **4.738987**（`9de790a2`）。
