# 引継ぎ — `lp-coordinate-model` ブランチ(LP 座標モデル完全模倣)

次セッションがこの1枚 + `docs/LP_COORDINATE_MODEL.md` を読めば即再開できることを目的とする。
散文は日本語、コマンド/パス/識別子は原文どおり。

---

## 0. 一行サマリ

Lily# のレイアウト座標を **LilyPond の実際の座標モデル**(grob 親子の相対参照点ツリー、
staff-space、Y-up、出力時に単一フリップ)に揃える大規模リファクタ。**ブランチ
`lp-coordinate-model` 上**で進行中(push 済み)。設計は `docs/LP_COORDINATE_MODEL.md` に確定済み。
**安全網(part-combine ラベル汚染)を 2026-06-23 に修正済み(§3 の確定事項)→ 次の一手は
Stage 1 step 1(stem アンカー + column-X 補正を単一音 byte 不変で導入)**。

---

## 1. いまの状態(git)

- **origin/master = `5597cb1`(push 済み・全 green・検証済み)** — 10コミット:
  座標 Y-up 化(stem/slur, `StaffFrame`)、dead 重複掃除(`SpacingSettings` クラスタ・未使用
  定数)、実 findings(和音スカイライン・ornament 0.20・in-note-padding 削除・TopSystem=6)。
  **これらは触らない。安全に確定済み。**
- **ブランチ `lp-coordinate-model`(`42d1ae8`、push 済み = `origin/lp-coordinate-model`)** —
  master を土台に 3 コミット:
  - `b4af9da` 設計 doc(`docs/LP_COORDINATE_MODEL.md`)
  - `1f25385` 本ハンドオフ
  - `42d1ae8` **part-combine ラベルを既定 off に gate**(`LayoutOptions.EnablePartCombine`、
    既定 false)。`<< \\ >>` は LP では \partcombine でないので "a2"/"Solo" を出さない。
    アナライザ本体・`PartCombineTests` は無傷(直接テスト)。全 1461 緑・snapshot 不変。
    **これで unison/mesh サンプルがラベル無しで描け、LP 視覚突合が機能する。**
- **Stage 1 のジオメトリ変更コードはまだ未着手**(part-combine 修正は安全網の準備で別件)。
- 再開時: `git checkout lp-coordinate-model` で作業。**ブランチの削除/別ブランチ作成は明示 GO 待ち。**

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

### 2026-06-23 セッションの確定事項(必読)

- **計測の結論**: Lily# の衝突定数は**すでに大半が LP-raw 一致**(full 0.5 / mesh 0.17 /
  dotted 0.1 / distant_half 0.4 / close_half-異 head group 0.52)。`NoteCollisionTests.cs` が
  これらを数値で pin 済み。**Stage 1 が実際に動かすジオメトリは 2 点だけ**:
  1. **同 head group close_half = `1.0`**(`NoteCollision.cs:369`、left-edge フレームの
     `0.52×2` 補償値)→ stem 相対フレーム化で `0.52` にして視覚同値を保つ。
  2. **touch = `0.65`**(`NoteCollisionParameters.TouchShift`)→ LP touch は **0.5**
     (`note-collision.cc:324`)。0.65 は `stem_to_stem` の値で取り違え。
- **part-combine は安全網の障害だった**:`<< \\ >>` を無条件 part-combine し "a2"/"Solo"
  ラベルを出していた(LP 非互換)。`42d1ae8` で gate off。**これで視覚突合が機能する。**
  (part-combine はテキスト注記のみ。ヘッド再配置・衝突には無関係と確認済み。)
- **現状モデル(`SharedRenderer.cs` 実読)**: column X = **ヘッド左端**。ヘッド描画は `x`
  (DrawNote:771 / DrawChord:883)、stem はヘッド端へ:up-stem `x + headWidth − thick/2`
  (:790-793)、down-stem `x + StemDownAttachX`(:925-927)。= ハンドオフ言う左端アンカー。

### 2026-06-23 実測値(close_half・byte 単位)— 実装の目標値

`test__collision.svg`(snapshot)から close_half 同群(beat1: up=e2 高 / down=d2 低、
両 half)の実座標:
- up-head 左端 `x=11.10`、up-stem `12.34`(= +1.239 = `StemUpAttachX`)。
- down-head 左端 `x=9.80`、down-stem `9.87`(= +0.065 = `StemDownAttachX`)。
- **head 左端の分離 = 11.10 − 9.80 = 1.30 ≈ noteheadWidth(1.304)。高い音(up)が
  低い音(down)の右に隣接、重なり無し。= 現 `1.0×w`。**
- グリフ実値: `NoteheadBlack`=`[0,1.304]`、`NoteheadHalf`=`[0,1.376]`、
  `NoteheadWhole`=`[0,1.964]`(原点=左端)。`StemThickness≈0.13`。
- LP 実描画(`C:\temp\closehalf.ly` = collision.lys と同 voiceCollision)も**高音を右隣接**で
  Lily# と視覚一致 → **close_half の出力は保存すべき**(変えない)。

**重要な含意(formula では解けない)**: LP 公式の `0.52 × 正規化(同幅 head なら 1.0)×
downstem幅(1.304)≈ 0.678` は現 `1.0×1.304=1.304` と一致しない。差は head アンカー基準
(LP=stem 一致 / Lily#=head 左端一致)にある。closed-form で stem 一致基準を組むと**高音が
左に来る等、符号・量がズレる**。⇒ **このリファクタは定数の置換でなく、1 編集ごとに描画して
LP と突合する反復ループでしか正しく収束しない**。各編集後に必ず close_half/full/mesh を
描画して「高音右隣接・分離 1.30」を維持しているか確認する。

### Stage 1 の着手手順(精緻化版・least-breakage)

1. **stem アンカー + column-X 補正を「単一音 byte 不変」で導入(まず純リファクタとして)**。
   column アンカーを stem(X=0)にし、ヘッドを `[−w,0]`(up)/`[0,+w]`(down)に置く。
   ただし **single-note の device X を不変に保つには、ヘッド描画 X を stem 基準に変える
   だけでなく、layout/spacing 側の column-X 導出を up-stem で `headWidth` 分ずらす補正が要る**
   (描画専用の変更では済まない=ここが非自明)。**チェックポイント: 単一音 snapshot が
   byte 不変**であることを確認してから次へ。
2. その後に **衝突 `1.0→0.52`(extent 正規化込み)+ touch `0.5`** を入れ、衝突種ごとに
   LP 描画(§4 のコマンド・`coll-cases.ly` 雛形が `C:\temp`)と突合 → 意図差分のみ rebaseline。

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
