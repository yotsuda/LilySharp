# OMR の読みを AI で直す — 設計（第971・2026-10-23）

OMR（`LilySharp-Omr`）が読んだ `.lys` の怪しい小節を、**ユーザーが頼んだときだけ**言語モデルに精査させ、
元画像と描き直しを並べて確認してから書き込む機能。OMR 側 ROADMAP S33「怪しい小節の自動修正」の段 2
（`REQUESTS.md` の「将来の依頼」）の Lily# 側の分。土台の提案は `..\LilySharp-Omr\docs\repro\lilysharp-proposal-2026-10-04.md`
（B2・B6）、既存の AI 変換は `docs/ai-collab-design.md`。

## 0. 原則

1. **自動で走らない。** AI は時間とトークンを使うので、入口は明示の操作だけ（§2）。走る前に件数と見積もりを出す。
2. **絵を見せて、譜で判断させる。** モデルには `.lys` の文字だけでなく元画像のその小節の切り抜きを渡す（§3）。
   人の確認も描いた譜面で行う（AI 変換と同じ「decide on the score」）。
3. **採るまで書かない。** 候補はコンパイル・拍・照合の門を通ってから見せ、Accept したものだけを 1 つの WorkspaceEdit で書く。
4. **OMR は別プログラムのまま。** Lily# は `x.omr.json` を読むだけで書かない。直した記録は `.lys` の `@todo` が外れること。
5. 作りは既存の AI 変換の部品を使い回す（`modelClient.ts`・`checkCandidate`／`renderText`・Before／After パネル）。

## 1. 今ある部品

| 部品 | 場所 | 使い方 |
|---|---|---|
| 怪しい小節の印 | `.lys` の `@todo(#key, "memo")`・LSP `lilysharp/todos`・F8 | 対象の一覧。`kind: "bar"`（拍が合わない小節）が今の全部 |
| 候補・確信度・元画像の場所 | `x.omr.json`（OMR が書く）: `todos[].{key, page, box, measure, part, memo, confidence, candidates[]}`・`measures[].{measure, part, page, box}`・`pages[].image` | 切り抜き（`box`）と候補（まだ空＝OMR 側の段 1 の後） |
| クイックフィックス | LSP `ResolveTodoActions`（`LilySharpLanguageServer.Editing.cs`）・`OmrSideFile.Candidates` | 「候補を書く」「解決済みにする」。ここに「AI で直す…」を足す（Command 型の CodeAction） |
| scan view | `omrScan.ts`／`omrScanCore.ts`（Webview・印の枠・*Marks below* のスライダー） | 「表示中の印」＝対象の既定。頭のバーにボタンを足す |
| モデル | `modelClient.ts`（`vscode.lm`・`ModelUse`・`autoPick`・「Lily#: Select AI Model」） | `ModelUse` に `'omrFix'` を足す |
| 検証と描画 | LSP `lilysharp/checkCandidate`・`lilysharp/renderText`（AI 変換が使う） | 門と確認パネルの絵 |
| 照合 | `omr verify`（OMR 側・保存時の照合 `lilysharp.omr.verifyOnSave`・`omrVerify.ts`） | 小節単位の照合ができれば 3 つ目の門 |
| 確認パネル | `aiTransform.ts` の candidate panel（Accept／Iterate／Reject・version／range guard の apply） | 一覧に広げる |

## 2. UI

### 2.1 入口（3 つ・裏は 1 つのコマンド `lilysharp.omrFixWithAi`）

- **クイックフィックス**「Fix this bar with AI…」: `@todo` の診断の上（`.lys` で `Ctrl+.`）。対象はその 1 か所。
- **コマンド**「Lily#: Fix OMR marks with AI…」: QuickPick にファイル中の印をチェックリストで出す（並びは確信度の低い順・
  各行に小節番号・パート・memo）。**既定でチェック＝scan view のスライダーより下の印**（表示中＝疑わしいもの）。
  選択範囲があればその範囲の印だけ。
- **scan view のボタン**「Fix N marks with AI」（N＝表示中の印）: 頭のバー（スライダーの隣）。押すと同じ QuickPick が
  チェック済みで開く。

### 2.2 見積もりと実行

- QuickPick の確定の前に **件数・要求数・見込みの秒数**を出す（秒数は直近の実測の平均＝出力チャネルの記録から。初回は
  「1 小節 2〜4 秒」）。1 回の上限は設定 `lilysharp.ai.omrFixBatch`（既定 25）。超えた分は「続ける？」で次の回。
- 進捗通知（`withProgress`・キャンセル可・「12 / 25 bars, 3 proposals」）。小節ごとに所要時間と結果を出力チャネル
  「Lily# AI」に 1 行。
- 走っている間、対象の小節に AI 変換と同じ soft-lock の飾り。編集されたら中断（version guard）。

### 2.3 確認パネル（`Lily# — OMR fixes`）

AI 変換の candidate panel を**一覧**にしたもの。1 行＝1 小節:

| 列 | 中身 |
|---|---|
| 元画像 | `x.omr.json` の `box` で切り抜いた画像（scan view と同じ座標＝読み込んだままの画素） |
| 今 | その小節の描画（`renderText` で前後 1 小節ごと） |
| 提案 | 候補の描画（同じ範囲） |
| 理由 | モデルの一言（「`r2` を `r2.` に: 付点が見える・拍が埋まる」） |
| 門 | compile ✓・拍 ✓・verify ✓／—（照合できないとき） |
| 操作 | Accept／Reject（行）・Accept all（門が全部 ✓ の行だけ）・Open in editor |

- Accept したものだけを **1 つの WorkspaceEdit** で書く（AI 変換の Snapshot／EditApplier: version と range の guard・
  文字が動いただけなら再 anchor）。採った小節の `@todo` は外す（クイックフィックス「Resolve」と同じ削除）。
- Reject は何も書かない。`@todo` は残る（memo に AI の提案を足す案は**しない**＝`.lys` は人の言葉のまま）。
- 候補が門を通らなかった小節は行に「no proposal（理由）」で出す＝黙って落とさない。

## 3. 1 小節の処理

### 3.1 入力（プロンプトの中身）

1. 文法の要約（`GRAMMAR_FOR_LLM.md`＝AI 変換と同じ・キャッシュ済み）。
2. **対象の小節の `.lys`**（`@todo` を含む 1 小節）と、前後 1 小節、**同じ小節の他パートの行**（拍が合うかの手がかり。
   `x.omr.json` の `measures[]` で同じ `measure` の `line` を引く）。拍子・調（LSP `factsForRange` か、`lilysharp/todos` の
   `Measure`／`Part` から）。
3. OMR の memo（「bar 4: voices 600+1880/2400 ticks」）と `candidates[]`（あれば）。
4. **元画像の切り抜き**（`pages[].image` を `box` で切る・余白を小節の高さの 30% 足す・PNG）。`vscode.lm` では
   `LanguageModelDataPart.image(bytes, 'image/png')` を User メッセージの content に入れる（§4.2）。
5. 指示: 「この小節だけを、拍子を満たし画像と一致する `.lys` の 1 行に書き直す。音の数・位置・付点・休符を画像で確かめる。
   自信が無ければ `null`。出力は JSON `{ "bar": "...", "reason": "...", "confidence": 0..1 }` だけ」。`@todo` は出力に含めない。

### 3.2 門（見せる前・AI 変換の validate-and-self-repair と同じ）

1. **compile**: `checkCandidate` で、置き換えた全文が**新しい**エラーを増やさない（他所の既存エラーは数えない）。
2. **拍**: その小節に LYS2001／2002（短い・長い小節）が無い＝`checkCandidate` の診断の位置で見る。
3. **照合**（任意・あれば）: `omr verify` をその `.lys` 候補で走らせ、対象の小節の食い違いが消えるか。OMR 側に小節を絞る
   引数（`--bars 12`）があると数秒で済む（§7）。無ければこの門は「—」。
4. 落ちたら 1 回だけ、診断の文を添えて直させる（`MAX_REPAIR_ATTEMPTS` と同じ上限）。それでも落ちたら「no proposal」。

### 3.3 出力

`{ bar, reason, confidence }` の JSON（`cleanCandidate` の仲間で囲いを剥がす）。`bar` は 1 小節の `.lys`（`|` で終わる）。
`confidence < 0.5` の提案は行に出すが Accept all の対象外。

## 4. モデル

### 4.1 今の作り＝`vscode.lm`（Copilot の模型・鍵を持たない）を続ける

- `ModelUse` に **`'omrFix'`** を足し、設定 **`lilysharp.ai.omrFixModel`**。「Lily#: Select AI Model」の 3 つ目の項。
- 自動選択は `'transform'` と同じ規則（最大の模型・mini／nano／lite／haiku／flash を避ける）＋**画像を受ける模型**
  （`LanguageModelChat` に画像可否のフラグは無いので、画像を付けて 400 系で断られたら文字だけで再送し、出力チャネルに
  「この模型は画像を受けない」と残す）。明示操作なので遅さは許せる（Fable 級で 1 小節 1.9〜3.2 秒の実測＝25 小節で 1〜1.5 分）。
- 速さを選ぶ人のために Sonnet 級が候補に出る。Haiku 級は譜の読みに弱いので自動では選ばない（手で選ぶのは自由）。

### 4.2 画像を渡す条件

`@types/vscode` 1.106.1（拡張の `node_modules`）に `LanguageModelDataPart.image` がある。拡張の `engines.vscode` は
`^1.90.0`（AI 変換の床）なので、**床を `LanguageModelDataPart` が入った版に上げる**（d.ts の `@since` を読んで決める・
1.100 前後）。古い VS Code では画像なし（memo と候補だけ）で動く＝M1 の形。

### 4.3 直接 Anthropic API（鍵を持つ）にするなら

既定は `claude-opus-5-5`（画像入力あり・adaptive thinking＝既定・`output_config.effort` は `medium` が既定なので
`high` を明示）。価格は入力 $4／出力 $20 per MTok（2026-10-06 の表）。1 小節＝入力 6〜8K トークン（切り抜き約 1.5K＋
文脈）＋出力 0.3K ≈ **$0.03〜0.04**。Sonnet 5.5（$2／$10）で半分。鍵は SecretStorage、`refusal` の `stop_reason` の扱いと
`fallbacks: "default"` が要る。**今は作らない**＝Copilot の枠で足りるうちは `vscode.lm` のまま（`modelClient.ts` の方針）。
作るときは `ChatClient` の実装を 1 つ足すだけで UI は変わらない。

## 5. 段取り（各段に門）

| 段 | 内容 | 門 |
|---|---|---|
| **M1** ✅ 第972 | コマンド＋QuickPick＋見積もり＋進捗、文字だけの入力（memo・候補・前後と他パート）、門 1・2、一覧パネル（元画像の列は `box` の切り抜きを**表示だけ**）、Accept の WorkspaceEdit と `@todo` の削除。`ModelUse 'omrFix'`。 | headless の網: `omrAiFixCore.ts`（対象の選別・プロンプトの組み立て・JSON の読み・門 1・2 の判定・切り抜きの矩形）を `*Core.ts` の流儀で単体に。`.lys` 側は `OmrSideFile` の既存の網。手動: MANUAL-CHECKS に `[ ]` 3 項（入口 3 つ）。 |
| **M2** | 画像をモデルに渡す（`LanguageModelDataPart.image`・engine の床・受けない模型の再送）。クイックフィックスと scan view のボタン。 | 同じ本で M1 と M2 の提案の採用率を出力チャネルで比べる（OMR の `data/eval` の既知の誤りの小節で） |
| **M3** | 門 3（`omr verify` の小節単位）・OMR 側の `candidates` が来たらプロンプトに。費用の記録（所要時間の平均→見積もり）。 | OMR 側の数字（S33 段 1）と合わせて精度を 1 枚に |

M1 は OMR 側の作業を待たない（`kind: "bar"` の todo と memo は今ある）。

**M1 の実装（第972・2026-10-23）**: `editors/vscode/src/omrAiFixCore.ts`（編集器なし・網 `test/omrAiFixCore.test.ts` 36 本）＋
`omrAiFix.ts`（コマンド `lilysharp.omrFixWithAi`・QuickPick・進捗・パネル `Lily# — OMR fixes`・Apply）。設計からの決定:
⑴ **置き換えの単位は印のある行**（reader はパートの小節を 1 行に書く＝`measures[].line`・`todos[].lines`）＝行の音楽部分
（字下げと改行は残す）を提案で置き換える。同じ行の印は全部その行と一緒に消える。⑵ 「今」「提案」の絵は 1 小節だけを
`renderText` で切り出すのではなく、**全曲の SVG を `<symbol>` に 1 つ置き、`rect.bar-staff` の `data-span` がその行の span に
触れる箱で viewBox を切る**（前後 1 小節の切り出しは文脈＝調・拍子・パートの宣言を要し、1 小節の文書を合成する手間に見合わない）。
⑶ Accept は印、**Apply が書く**（Accept したものだけ 1 つの WorkspaceEdit）。⑷ 模型の `bar` に残った `@todo` は剥がす。
⑸ 見積もりの秒数は直近 20 小節の平均を `globalState` に（出力チャネルの解析ではなく）。⑹ 入口は M1 ではパレットと `.lys` の
右クリック（scan のある本）だけ＝クイックフィックスと scan view のボタンは M2 のまま。

## 6. 費用と限界

- 走るのは明示操作のときだけ。背景で走る経路は作らない（`verifyOnSave` は OMR の照合であって AI ではない）。
- 1 回の上限（既定 25）と見積もりの表示。出力チャネルに小節ごとの模型名・所要時間・結果（proposal／no proposal／reason）。
- モデルが見るのは 1 小節と前後・他パートだけ＝曲全体を渡さない（トークンと文脈の節約・1 小節の判断に全体は要らない）。
- 限界: 連符の無印・声部の入れ替わり・歌詞の位置など「1 小節の文字と絵」で決まらないものは `null` が正解。パネルで
  「no proposal」として残り、人が直す。

## 7. OMR 側へ頼むもの（REQUESTS.md に書く・M3 の前）

1. `omr verify --bars N[,M…]`（または `--measure`）: 小節を絞った照合（門 3 を数秒に）。
2. `candidates[]`（S33 段 1 の `cands`）を `x.omr.json` に（段 2 の前提・既に「将来の依頼」にある）。
3. `x.omr.json` の `pages[].image` の相対パス（今は絶対パス）。切り抜きには今のままでも読める。

## 8. 開いている判断（オーナー）

- 既定の対象を「スライダーより下」にするか「全部の印」にするか（提案: スライダーより下）。
- Accept 後に `x.omr.json` の todo を OMR 側で消すか（Lily# は書かない・`@todo` が外れたことを OMR の `verify` が見る）。
- 直接 API の BYO 鍵を作る時期（§4.3）。
