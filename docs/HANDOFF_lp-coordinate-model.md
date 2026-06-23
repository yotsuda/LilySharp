# 引継ぎ — `lp-coordinate-model` ブランチ(LP 座標モデル完全模倣)

次セッションがこの1枚 + `docs/LP_COORDINATE_MODEL.md` を読めば即再開できることを目的とする。
散文は日本語、コマンド/パス/識別子は原文どおり。

---

## 0. 一行サマリ

Lily# のレイアウト座標を **LilyPond の実際の座標モデル**(grob 親子の相対参照点ツリー、
staff-space、Y-up、出力時に単一フリップ)に揃える大規模リファクタ。**ブランチ
`lp-coordinate-model` 上**で進行中。設計は `docs/LP_COORDINATE_MODEL.md` に確定済み。
**次の一手は Stage 1(stem 相対 head X + 衝突 port)**。

---

## 1. いまの状態(git)

- **origin/master = `5597cb1`(push 済み・全 green・検証済み)** — 10コミット:
  座標 Y-up 化(stem/slur, `StaffFrame`)、dead 重複掃除(`SpacingSettings` クラスタ・未使用
  定数)、実 findings(和音スカイライン・ornament 0.20・in-note-padding 削除・TopSystem=6)。
  **これらは触らない。安全に確定済み。**
- **ブランチ `lp-coordinate-model`(`b4af9da`、未 push)** — master を土台に `docs/
  LP_COORDINATE_MODEL.md`(設計)を追加しただけ。**コードはまだ未着手。**
- 再開時: `git checkout lp-coordinate-model` で作業。ブランチは必要に応じ push 可
  (進行中なので任意)。**ブランチの削除/別ブランチ作成は明示 GO 待ち。**

---

## 2. このリファクタの方針と安全網(重要)

- これは **byte-neutral にならない**(絶対→相対、ジオメトリが変わる)。
  - これまでの Y-up 化(stem/slur)は **厳密な符号反転 `up = -deviceY`** で byte 一致を
    オラクルにできた。**Stage 1 以降はそれが使えない。**
- **安全網 = 「サンプルを LP で描画し出力一致を実測」(§3) + 検証付き snapshot リベース。**
  一時的な回帰は許容(ユーザー合意済み)。
- **進め方の鉄則**: ジオメトリを変える各ステップは、必ず LP 描画と突合 → 差分が LP に
  寄っていることを確認 → リベース。盲目的な定数変更はしない。

---

## 3. 次の一手 — Stage 1: stem 相対 head X + 衝突 port

### 何をするか
1. **stem を note-column の X アンカーにする**。各 head の X-extent を `stem-attachment`
   (−1..+1 の head-box スケール、`note-head.cc:158-161`)で stem 周りの符号付き区間として
   表す。up-stem → head は `[−w, 0]`、down-stem → `[0, +w]`(`stem.cc:1050-1069`)。
   **単一音の最終 head X は不変に保つ**(stem 相対で計算 → 従来と同じ device X へ)。
   → 単一音は出力中立、**衝突/多声のみ変化**するはず。
2. **`NoteCollision.cs` を LP verbatim 化**: `note-collision.cc:319-337` の raw 定数に統一
   (full 0.5 / stem_to_stem 0.65 / touch 0.5 / close_half 0.52 / distant_half 0.4 /
   mesh 0.17・dotted 0.1)、`note-collision.cc:343-348` の **extent 正規化**を
   `GlyphMetrics.GetNoteheadBBox` で実装。現状の magic `1.0` と混在フレームを置換。

### 着手手順(最初の具体アクション)
1. 衝突種ごとのサンプルを作る(`samples/test/` に追記 or 一時 .ly):
   - close_half(既存 `collision.lys` の `<< {e2} \\ {d2} >>` = seconds)
   - full collide(unison: `<< {c2} \\ {c2} >>`)
   - touch / mesh(片方が staff line 上 / 片方しか dot 無し 等)
2. 各を **LP** で描画(下記コマンド)し、**Lily#** でも描画、符頭分離を実測・突合。
3. extent 正規化を実装 → 全衝突種で LP 一致を確認 → snapshot リベース。

### 既に判明している重要事実(必読・誤りを繰り返さないため)
- **`NoteCollision` の magic `1.0` は「正しい」**。`= 0.52 × 2.0`(LP の stem-reference
  正規化結果)を left-edge フレームで表した値。**close_half は `collision.lys` 描画で LP 一致を
  実測済み**(`C:\temp\coll.ly` vs `ls-coll.png`、2026-06-23)。盲目的に 0.52 へ変えると
  **regression**。Stage 1 は「フレームを stem 基準に統一して全種を一貫させる」のが目的で、
  close_half の見た目は変えない(1.0 ≈ 0.52×2 のまま)。
- **唯一明確な値バグは `TouchShift = 0.65`**(`NoteCollision.cs:175`)。LP touch は **0.5**
  (`note-collision.cc:324`)、0.65 は **stem_to_stem**(`:322`、Lily# に欠けているケース)の値。
  ただし正しい実効値はフレーム係数次第なので **描画実測してから**直す。
- **`ChordHeadPositioning.cs` は既に stem/extent モデルで LP 忠実**(`ell` = right ink extent、
  `dir`、`stem.cc:606-760`)。Stage 1 はこれと**統一**する(別実装を増やさない)。
- 衝突 offset の消費は1箇所: `NoteCollision.CalculateVoiceOffsets`(`:499-558`)、
  `collision.UpStemXOffset * noteheadWidth`(`:544`)/ down(`:552`)。この `* noteheadWidth` は
  既に LP の「down-stem head 幅で乗算」(`note-collision.cc:339-340`)と一致。

### 後続ステージ(`LP_COORDINATE_MODEL.md` §6)
- Stage 2: staff 相対 Y(notehead、staff-position 変換) — ジオメトリ変更
- Stage 3: Align 風 譜間 Y スタック(`align-interface.cc:128-296`) — 多譜 fixture 要
- Stage 4: System/Page フレーム + 単一出力フリップ(`page.scm`/`framework-ps.scm`) — 出力リファクタ
- これらは conventional(3 は半分)で、相対チェーン完成後に機械的。

---

## 4. 環境・コマンド(厳守)

- **シェルは ripple MCP `execute_command`(shell=pwsh)。PowerShell ツールと Bash ツールは
  使用禁止**(detached プロセスが `bin/Debug` DLL をロックしビルド破壊)。
- リポジトリ `C:\MyProj\LilySharp` / LP クローン `C:\MyProj\lilypond-src` /
  `lilypond` は PATH(v2.24.4)。
- **特殊文字を含むファイル(`.ly`/`.py`/`<...>` を含む `.lys`/コミットメッセージ)は
  ripple ヒアストリングに直書きせず `Write` ツールでファイル化。**
- ビルド: `dotnet build LilySharp.Core` / テスト: `dotnet test LilySharp.Tests`
  (フィルタ: `--filter "FullyQualifiedName~SvgSnapshotTests"`)。
- **snapshot 再ベースライン**:
  ```pwsh
  $env:LILYSHARP_UPDATE_SNAPSHOTS='1'
  dotnet test LilySharp.Tests --filter "FullyQualifiedName~SvgSnapshotTests"
  $env:LILYSHARP_UPDATE_SNAPSHOTS=$null
  ```
  リベース後は **`git diff` で差分純度を検証**(意図した変化だけか)。
- **LP 描画(Guile デッドロック回避・必須)**:
  ```pwsh
  cd C:\temp; cmd.exe /d /s /c "lilypond --png -dresolution=200 -o out in.ly < NUL > out.log 2>&1"
  ```
  `< NUL` と `> log 2>&1` 両方必須。SVG が要れば `-dbackend=svg`(ただし LP SVG は入れ子
  transform で座標抽出が不安定 → **PNG + 視覚比較が確実**)。
- **Lily# 描画**: `dotnet run --project LilySharp.Cli -- png samples\test\x.lys C:\temp\x.png`
  (コード変更直後は `--no-build` を付けない)。
- `Read` ツールは PNG を視覚表示できる(LP/Lily# 並置比較に使う)。
- コミットメッセージは英語、`Co-Authored-By: Claude <current-model> <noreply@anthropic.com>`
  (current model 名に合わせる)。
- **ship ゲート**: build/test/commit/push は可。**tag/PSGallery publish/告知は green + 明示 GO**。

---

## 5. やってはいけないこと / 教訓

- magic `1.0` を「バグ」と決めつけて 0.52 へ単純置換しない(LP 一致を実測済み)。
- 「座標系を完全同一化」= 相対参照点ツリーの導入であり、**byte-neutral にならない**。
  Y 向きだけの符号反転トリックでは原点(相対 vs 絶対)は揃わない。
- master の10コミットは確定。ブランチ作業はそれを土台にする(rebase/巻き戻ししない)。
- エージェント(調査)の主張は一次照合する(過去に `check_meshing_chords` 不在の誤主張あり)。

---

## 6. 参照

- 設計: `docs/LP_COORDINATE_MODEL.md`(LP 座標モデル全体 + staging)
- ワークフロー: `docs/DEV_BUGFIX_WORKFLOW.md`(§3 描画比較、§8 snapshot)
- LP 主要ファイル: `LP_COORDINATE_MODEL.md` §7 にインデックスあり
- Lily# 主要ファイル: `NoteCollision.cs`、`ChordHeadPositioning.cs`、`SkylineBuilder.cs`、
  `StaffFrame.cs`(座標反射ヘルパ)、`Rendering/SharedRenderer.cs`(描画・符頭 X)
