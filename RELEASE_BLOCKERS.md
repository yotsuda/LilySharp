# Lily# リリースブロッカー整理

> 作成日: 2026-06-29
> 前提: 本書はコード/ドキュメントの読み取り専用調査（point-in-time）から導出。**別セッションが編集中のため、各項目は着手前に現行コードで再確認すること。**
> 関連: `AI_POSITIONING_HANDOFF.md`（AI時代ポジショニング）、`C:\MyProj\lilypond-vs-lilysharp-code-quality.md`（品質比較レポート）
> 方針: 「線を引けるものだけブロッカー」。判断が割れる/好みの項目は P1 以下に置き、P0 は誇大主張・データ欠落・正しさ未実証など**出すと信用を損なう**ものに限定。

---

## ★ 再検証（2026-06-30、現行コードに照合・並列調査）

作成時(6-29)以降の進捗を踏まえ各 P0 を現行コードで再確認した結果（証拠は各 P0 節に追記）。
**★ 後続対処（同日中の後続コミットで P0-1/P0-3/P0-4 を解消、下表は対処後の状態）:**

| # | 項目 | 判定 | 状態 / 残作業 |
|---|---|---|---|
| **P0-1** | README/audit 誇大主張 | 🟢 **解消**（79df3b1） | README を実装に整合（cross-staff beam 未対応・MusicXML の歌詞/連符未出力を明記）。残は判断1点＝`audit/grob_coverage.md` を注記残置のままにするか公開物から外すか |
| **P0-2** | 視覚回帰ベースライン | 🟢 **解消済み＝ブロッカーでない** | `SvgSnapshotTests`＋`Snapshots/` に **95 本の固定 SVG**（above-dynamics 追加）、byte-identical 比較が稼働中。最小線は達成済み（フル pixel-diff は P1） |
| **P0-3** | MusicXML サイレント欠落 | 🟢 **明示済**（79df3b1） | アーティキュレーション/装飾/強弱は emit 済。残る欠落（歌詞・連符番号）は README で**明示**＝P0(b) 達成。完全 emit (a) は P1-3 |
| **P0-4** | AI 言語仕様1枚 | 🟢 **解消**（1828bb6） | `docs/GRAMMAR_FOR_LLM.md`（圧縮1枚・全例 parse 検証済）を作成。System prompt 投入可 |

**含意**: P0-1〜P0-4 は全て解消（明示 or 実装）。**ハード P0 残はゼロ。** 残るのは判断1点（audit 文書の処遇）と、実装系の fast-follow（歌詞/連符の MusicXML emit、cross-staff beam）＝いずれも P1。

---

## 0. 結論サマリ

- **核（標準西洋記譜）はほぼ完成（~85〜90%）**。技術的にはリリース可能な水準。
- リリースを止めるべきは **コード品質**でなく、**(a) 主張と実装の不一致、(b) サイレントなデータ欠落、(c) 正しさを示す手段ゼロ** の3点。
- 未実装機能の大半は**長い裾野（古楽・世界音楽・特殊記譜）でブロッカーではない**。スコープに混ぜない。

```
P0 (出す前に必須)      : 4件 — 誇大主張除去 / 正しさ検証の最小線 / MusicXML欠落の明示 / AI仕様1枚
  └ 再検証(6-30): P0-2 解消済み(snapshot 95本稼働)。
  └ 対処(6-30 後続): P0-1(README整合) / P0-3(欠落明示) / P0-4(言語仕様1枚) を解消。
    ★ ハード P0 残はゼロ。判断1点(audit 文書の処遇)のみ。
P1 (早期fast-follow)   : 4件 — クロススタッフ連桁 / 視覚回帰フル / MusicXML完全化 / repo体裁
P2 / 非ブロッカー       : god分割 / 長い裾野 / callback property 等
```

---

## P0 — リリースブロッカー（出す前に必須）

### P0-1. README/ドキュメントの主張をコードに合わせる（誇大主張の除去）　🟢 解消(79df3b1)　← 旧: 🟡 再検証(6-30) 存続
> 再検証の確認結果: ①README は MusicXML を `[ ] Planned` と誌記するが**実装済み**（partial）→`MusicXmlExporter.cs`。②`SharedRenderer.cs:50-51` に「cross-staff beam PRODUCTION is the remaining known gap」＝README「Multi-staff ✓」と整合させる注記が要る。③`audit/grob_coverage.md`(2026-04-25) は BarNumber/Fingering/LedgerLineSpanner/Glissando/MultiMeasureRest を "Absent" と誤記だが**engraver は全て実在**＝audit スクリプトの欠陥。公開物から除外/訂正。
- **何が問題か**: 主張＞実装の箇所が残ると初日に信用を失う。例として要確認:
  - README「Multi-staff / GrandStaff rendering」 vs コードの「cross-staff beam PRODUCTION は remaining known gap」(SharedRenderer.cs)
  - MusicXML を「export」と書きつつアーティキュレーション/歌詞は未出力（下記 P0-3）
  - `audit/grob_coverage.md` 等は**実際に誤り**（BarNumber/Fingering/LedgerLineSpanner/Glissando を "absent" と誤記＝engraver は存在）。公開物に古い監査を残さない。
- **なぜブロッカー**: 「explicit / 正直」を売りにする製品が自分で誇大主張すると土台が崩れる。
- **対応**: README の機能表を実コードに照合し、未完は「partial/planned」と明記。古い/誤った audit 文書は公開対象から外すか修正。
- **コスト**: 低（記述合わせのみ）。

### P0-2. 浄書出力の「正しさ」を示す最小線を引く　🟢 再検証(6-30): 解消済み＝ブロッカーでない
> 再検証の確認結果: `LilySharp.Tests\Svg\SvgSnapshotTests.cs` が test/86＋showcase/8＝**94 fixture** を `LilySharp.Tests\Snapshots\` の固定 SVG と **byte-identical 比較**（`.gitattributes` で eol=lf 固定、`LILYSHARP_UPDATE_SNAPSHOTS=1` で意図的再ベースライン）。出力が壊れたら即 fail＝**最小線は既に達成**。フル pixel-diff vs 実 LilyPond は P1-2。本項目は P0 から降格。
- **何が問題か**: 視覚回帰テストのベースラインが無い＝出力が正しい保証がコードに存在しない（単体1462件はあるが出力画像の正しさは別物）。
- **なぜブロッカー**: 浄書ソフトの本質品質は「見た目が正しいこと」。かつ Lily# の物語が「AIが書く→人/エージェントが検証」である以上、**自分の出力が未検証**では矛盾。
- **対応（最小線でよい）**: samples/ の既知正常レンダリング集（PNG/SVG）をリポジトリに固定し、差分検出できる状態に。フルコーパス整備は P1。
- **コスト**: 中（最小なら低）。
- 参照: LilyPond の `input/regression/`（2200本視覚diff）が手本。

### P0-3. MusicXML のサイレントなデータ欠落をなくす（または明示）　🟢 明示済(79df3b1)　← 旧: 🟡 再検証(6-30) 縮小
> 再検証の確認結果: **アーティキュレーション/装飾/強弱は現在 emit 済み**（`MusicXmlExporter.cs` `MapArticulation`/`MapOrnament`/`EmitPendingDynamic`、`MusicXmlTests` でカバー）＝旧主張「アーティキュレーションを落とす」は陳腐化。**残るサイレント欠落は「歌詞」と「連符番号」のみ**（`MusicXml` namespace に lyric/tuplet 参照ゼロ、SVG 側ではパース済み）。P0 としては (b)明示で可、(a)emit 実装は P1。
- **何が問題か**: MusicXML エクスポートは ~85% で、**アーティキュレーション/歌詞を黙って落とす**（パース済みだが emit されない）。MusicXML は相互運用の標準フォーマットで、黙ってデータが消えるのは最悪。
- **なぜブロッカー**: 「export できます」と言って往復でデータが失われると、相互運用の信頼を一発で失う。
- **対応（どちらか）**: (a) アーティキュレーション/歌詞も emit する（望ましい、P1 と統合可）、または最低限 (b) 落とす要素を README/警告で**明示**してサイレント欠落をやめる。P0 としては (b) で可、(a) は P1。
- **コスト**: (b)低 / (a)中。

### P0-4. AI が文脈に入れる「言語仕様1枚」を同梱　🟢 解消(1828bb6)　← 旧: 🔴 再検証(6-30) 存続
> **対処(1828bb6): `docs/GRAMMAR_FOR_LLM.md`（圧縮1枚・全例 parse 検証済）を作成し解消。以下は対処前の調査。**
> 再検証の確認結果(対処前): LLM の system prompt にそのまま入れる圧縮1枚は**不在**だった。素材は `docs/SYNTAX_REFERENCE.md`(表中心)＋`docs/GRAMMAR.md`(EBNF)＝品質は高いが未圧縮。`~150-200行`の圧縮1枚を作る低〜中工数タスク。
- **何が問題か**: Lily# はコーパスがゼロ＝LLM は zero-shot で書けない。リリースの旗が「AIフレンドリーなターゲット言語」である以上、**AI に渡せる仕様1枚**が無いと初日に主張を実証できない。
- **なぜブロッカー**: ポジショニングの中核前提。これが無いと「AIフレンドリー」が空手形。
- **対応**: `docs/GRAMMAR.md`/`SYNTAX_REFERENCE.md` から、System prompt 投入用の圧縮1ファイルを作る（全構文＋最小正例、同義表現は載せない）。詳細は `AI_POSITIONING_HANDOFF.md` 優先1。
- **コスト**: 低〜中。
- 注: 「AI ポジショニングを掲げて出す」場合の P0。位置づけを変えるなら P1 に降格可。

---

## P1 — 早期 fast-follow（強く推奨・ハードブロッカーではない）

### P1-1. クロススタッフ連桁
- 核に残る数少ない穴。ピアノ譜で目立つ。コード自身が "remaining known gap" と認識。
- 対応まで README で「cross-staff beam は未対応」と明記すれば P0 を回避できる（→P0-1 に統合）。

### P1-2. 視覚回帰テストのフル整備
- P0-2 の最小線を、サンプル群全体＋差分自動検出へ拡張。品質を「主張」から「実証」へ恒久的に引き上げる最重要施策。

### P1-3. MusicXML を完全化（アーティキュレーション/歌詞/連符番号の emit）
- P0-3(b) の「明示」を「実装」へ。相互運用の完成度を上げる。

### P1-4. リポジトリ体裁の整理（OSS 公開向け）
- ルートに `HANDOFF_*.md` 複数・`LAYOUT_ROADMAP_V2/V3/V4.md`・古い audit が散在＝「未完」印象。公開前に `docs/` や `archive/` へ集約。**メモリやコードは消さず移動のみ**。

---

## P2 / 非ブロッカー（リリースに混ぜない）

明確に「出した後でよい」もの。スコープクリープ防止のため**ブロッカーに昇格させない**。

- **god ファイル分割**（SharedRenderer.cs 3578 / SpacingRules.cs 1879）= 内部品質。ユーザー影響なし。設計は健全、抽出で足りる。
- **長い裾野の記譜**（LP に約100+ grob ある領域）: 古楽（グレゴリオ/メンスラル/キエフ/ヴァティカーナ）、世界音楽/微分音（マカーム/アラブ/ペルシャ）、フレットボード図、コードグリッド、クラスター、Ambitus、キュー音符、脚注/バルーン、特殊タイ（RepeatTie/LaissezVibrer）、SpanBar 等 → ターゲットユーザーが触るものだけ後日選んで追加。
- **callback property system**（before/after-line-breaking 等）= C# 直実装の設計判断による意図的非実装。穴ではない。

---

## リリース前チェックリスト（pre-flight）

- [x] P0-1〜P0-4 解消（README 明示＋言語仕様1枚＋snapshot 最小線。79df3b1 / 1828bb6）
- [ ] `dotnet build` / `dotnet test` 全緑（現行 1937 件）
- [ ] LICENSE（GPLv3）とフォント OFL ライセンスの同梱確認
- [ ] バージョン番号確定（現行 0.1.2-dev → リリース版へ）
- [ ] README の機能表が実コードと一致
- [ ] サンプル（samples/）が全て現行ビルドでレンダリング成功

## 出荷ゲート（2段・メモリ方針に準拠）
1. 上記 pre-flight が全緑
2. **ユーザーの明示 GO**

> tag / 公開 publish / 告知は (1)＋(2) の両方が揃ってから。build/test/commit/push までは可。
> 「verify してから X」を「fix してから X」に拡張解釈しない。
