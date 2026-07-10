# Lily# AI 協調編集ガイド（VS Code 拡張）

`.lys` を編集しながら、**範囲を選んで自然言語で指示すると AI がその箇所だけを書き換える**機能と、**入力中に次の小節を提案する**補完機能です。Lily# の高速コンパイラを使い、**壊れた記譜は表示する前に弾く／自動修復する**ため、常に「鳴る・見える楽譜」で判断できます。

- 器は **VS Code 拡張**（MCP ではありません）。同一ファイルをユーザーと AI が安全に共作でき、`Ctrl+Z` 一発で戻せます。
- モデルは **GitHub Copilot（vscode.lm）** をそのまま利用。Copilot が無い場合は **自分の API キー（Anthropic / OpenAI）** も使えます。

> 設計の詳細は [`docs/ai-collab-design.md`](./ai-collab-design.md) を参照。

---

## 目次

1. [前提条件](#前提条件)
2. [機能1：選択して AI 変形（Ctrl+I）](#機能1選択して-ai-変形ctrli)
3. [機能2：ゴーストテキスト補完（次の小節）](#機能2ゴーストテキスト補完次の小節)
4. [モデルの設定（Copilot / BYO キー）](#モデルの設定copilot--byo-キー)
5. [設定項目一覧](#設定項目一覧)
6. [コマンド一覧](#コマンド一覧)
7. [安全性のしくみ](#安全性のしくみ)
8. [トラブルシューティング](#トラブルシューティング)

---

## 前提条件

- **VS Code 1.90 以上**（言語モデル API `vscode.lm` を使うため）。
- 次のいずれかの **言語モデル**：
  - **GitHub Copilot**（サインイン済み）— 追加設定なしで使えます。既定。
  - **自分の API キー** — Anthropic（Claude）または OpenAI（GPT）。→ [モデルの設定](#モデルの設定copilot--byo-キー)
- **Lily# 言語サーバ**が動作していること（`.lys` を開くと自動起動）。開発ビルドを反映するには拡張の `server/` に最新の LSP を配置してください（`npm run deploy-server` 相当）。

---

## 機能1：選択して AI 変形（Ctrl+I）

### 基本の流れ

1. `.lys` で変えたい**範囲を選択**（例：melody の 4 小節）。選択が空のときはカーソル行が対象になります。
2. **`Ctrl+I`**（macOS は `Cmd+I`）。エディタ右上に入力欄が出ます。
   - エディタの右クリックメニュー、コマンドパレットの **「Lily#: Transform Selection with AI…」** からも起動できます。
3. **やりたいことを自然言語で入力**。例：
   - `3度でハモらせて` / `harmonize a third above`
   - `transpose up a perfect fourth`（4度上に移調）
   - `add a crescendo`（クレッシェンドを付ける）
   - `turn it into triplets`（三連符のノリに）
   - ※プロンプト欄には毎回ちがう**例**が巡回表示されます。
4. AI が置換案を生成 → **コンパイルで検証**（壊れていれば診断を材料に自動で作り直し）。
5. **候補が「Lily# — AI candidate」パネルに楽譜として描画**されます。
6. パネルの操作で決定：
   - **Accept（Enter）** … 採用。単一の編集として適用され、`Ctrl+Z` で元に戻せます。
   - **Iterate…** … 追い指示を入力してさらに調整（会話を継続）。
   - **Reject（Esc）** … 破棄（ファイルは無変更）。

### Before / After の見比べ

候補パネル右上の **After / Before トグル**（パネル内で **Tab** でも切替）で、**変更前後の楽譜を見比べ**られます。差分をテキストではなく「譜面」で確認できます。

### 譜面プレビューから選択して変形

プレビュー（`Ctrl+K V` などで開く譜面）でも同じ変形を起動できます。

1. プレビュー上の**音符をクリック**（起点＝アンカー）。
2. 別の音符を **Shift＋クリック**して範囲を広げる。選択した音符がハイライトされ、右上に **「✨ Transform with AI」** ボタンが出ます（プレビュー内で **`Ctrl+I`** でも可、**`Esc`** で解除）。
3. ボタンを押すと、その範囲がエディタのテキスト選択に変換され、**あとは上記 3〜6 と同じ**流れになります。

> テキストから選んでも、譜面から選んでも、以降の体験は 1 つです。

### 同時編集の安全性

- 生成中はカーソルが他の場所を編集していても大丈夫（開始時点の版・範囲をスナップショット）。
- 適用直前に版と範囲を再確認し、範囲がずれていれば**再アンカー**、無理なら**確認ダイアログ**を出します。
- 生成中は対象範囲に「作業中」のマーカー（ソフトロック。編集はブロックしません）。

---

## 機能2：ゴーストテキスト補完（次の小節）

記譜を打っている最中に、**「次の小節」候補をゴーストテキストで提案**します。`Tab` で確定。

- **トリガ**：カーソルが行末にあり、直前が小節線 `|` で閉じている「次に何が来るか」の瞬間だけ。
- **見せる前に検証**：候補の小節を入れた状態でコンパイルし、**壊れる候補は表示しません**。
- 打鍵ごとの無駄打ちを抑えるため約 300ms のデバウンス＋結果キャッシュ。

**既定はオフ**（打鍵中にモデルを呼ぶコストがあるため）。有効化するには設定：

```jsonc
// settings.json
"lilysharp.ai.ghostCompletion": true
```

「意図的な変形」＝ Ctrl+I（機能1）、「流れで書く補完」＝ ゴースト（機能2）の二段構えです。

---

## モデルの設定（Copilot / BYO キー）

既定（`lilysharp.ai.provider = "auto"`）では **Copilot があればそれを使い、無ければ保存済みの API キー**を使います。

### 自分の API キーを使う（Anthropic / OpenAI）

1. コマンドパレット → **「Lily#: Set AI API Key…」**（`lilysharp.setAiKey`）。
2. プロバイダ（Anthropic / OpenAI）を選び、**API キーを入力**。
   - キーは **VS Code の SecretStorage に安全に保存**され、`settings.json` には書き込まれません。
3. 必要に応じてプロバイダを固定：

```jsonc
"lilysharp.ai.provider": "anthropic",   // auto | copilot | anthropic | openai
"lilysharp.ai.model": "claude-sonnet-5" // 空なら各プロバイダの既定
```

- 既定モデル：Anthropic = `claude-sonnet-5`、OpenAI = `gpt-4.1`。
- キーの削除：**「Lily#: Clear AI API Keys」**（`lilysharp.clearAiKey`）。

---

## 設定項目一覧

| 設定キー | 既定 | 説明 |
|---|---|---|
| `lilysharp.ai.provider` | `auto` | 使用モデル。`auto`＝Copilot 優先→キー。他に `copilot` / `anthropic` / `openai`。 |
| `lilysharp.ai.model` | `""` | BYO キー時のモデル ID（例 `claude-sonnet-5`, `gpt-4.1`）。空で既定。Copilot では無視。 |
| `lilysharp.ai.ghostCompletion` | `false` | 入力中の「次の小節」ゴースト補完のオン/オフ。 |

（プレビュー関連 `lilysharp.preview.*` などは従来どおり。）

---

## コマンド一覧

| コマンド | 内容 | 起動 |
|---|---|---|
| `lilysharp.aiTransform` | 選択範囲を AI で変形 | `Ctrl+I` / `Cmd+I`、右クリック、パレット |
| `lilysharp.setAiKey` | Anthropic/OpenAI の API キーを保存 | パレット「Lily#: Set AI API Key…」 |
| `lilysharp.clearAiKey` | 保存した API キーを削除 | パレット「Lily#: Clear AI API Keys」 |

---

## 安全性のしくみ

- **見せる前に検証・自己修復**：候補はプロセス内コンパイラで即検証し、壊れていれば診断を返して最大 2 回まで作り直し。破綻した記譜はユーザーに出しません。
- **非破壊**：候補は裏でコンパイル/描画するだけ。Accept するまで実ファイルは無変更。
- **共有 undo**：適用は単一の編集。`Ctrl+Z` 一発で戻せます。
- **版・範囲ガード＋ソフトロック**：同時編集でも安全（前述）。
- **品質ログはローカルのみ**：各変換の結果（使用モデル・自己修復回数・accept/reject など）は **「Lily# Extension」出力チャネル**に記録するだけで、外部送信・テレメトリはありません。

---

## トラブルシューティング

- **「no language model available」と出る**
  Copilot にサインインしていない／未導入の可能性。Copilot を有効にするか、**「Lily#: Set AI API Key…」** で API キーを設定してください。ダイアログから直接キー設定に進めます。
- **`Ctrl+I` が反応しない**
  `.lys` ファイルにフォーカスがあるか確認（`editorLangId == lilysharp` のときのみ有効）。
- **ゴースト補完が出ない**
  `lilysharp.ai.ghostCompletion` が `true` か、カーソルが**小節線 `|` の直後（行末）**にあるかを確認。
- **候補が「could not produce a valid transform」で終わる**
  指示が曖昧か、要求が現在の記法で表現しにくい可能性。言い回しを変えるか、範囲を狭めて再試行してください（壊れた案は決して適用されません）。
- **プレビューに変更が反映されない / 機能が古い**
  拡張にバンドルされた言語サーバが古い可能性。最新の LSP を `editors/vscode/server/` に配置し直してください（`npm run deploy-server` 相当）。
- **BYO キーでエラーになる**
  出力チャネル「Lily# Extension」に HTTP ステータス等が出ます。モデル ID（`lilysharp.ai.model`）とキーを確認してください。
