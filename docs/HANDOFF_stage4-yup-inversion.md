# 引継ぎ — `stage4-yup-inversion` ブランチ(Stage 4: 内部 Y-up + 単一出力フリップ)

次セッションがこの1枚 + `docs/STAGE4_YUP_INVERSION.md`(設計スペック)を読めば即再開できる。
散文は日本語、パス/識別子/コマンドは原文どおり。

---

## 0. 一行サマリ

Lily# の垂直座標を LP と同じ Y-up へ移し、LP のレイアウト計算を符号そのままで移植できる素地を作る。

> **改訂(2026-06-23 セッション2)**: 当初の「内部 Y-up・出力時に単一 ScaleY:-1 フリップ・~170箇所
> atomic・42 snapshot byte 不変」路線は**破棄**。実コード精査で (1) 単一 group flip は glyph を
> 上下反転させ byte 一致とも矛盾、(2) 絶対 Y-up は prelim extent pass の H 循環で成立せず、
> (3) `system.Y` 共有フレームで render パスだけの分離も不可、と判明。**byte 一致は要件から外した**
> (ユーザー判断: 正しい実装なら出力はより正しくなってよい/検証は LP 比較 + snapshot 再ベースライン)。
>
> **新路線(LP 忠実・漸進)**: grob ファミリを1つずつ「相対 Y-up offset + 自分の draw 境界で device
> 変換」へ移す(StaffFrame/skyline が per-staff で既にやっている形)。**page-level decorator
> `YFlipDrawingContext` は最終ステップ**(全 grob 相対 Y-up 化後に単一 flip へ畳む)。詳細・根拠は
> `docs/STAGE4_YUP_INVERSION.md` §1, §3.5。
>
> **監査結果(3並列 Explore で全 engraver/配置層を精査)**: 計算層は**ほぼ全て既に Y-up**だった
> (stem/beam/tie/slur/articulation/tuplet/dynamics/fingering/tie-variant、配置/間隔/衝突も中立 or
> 正しい境界符号処理)。真の device-Y 負債は **`SkylineBuilder` だけ**(他は誤検出: LedgerLineSpanner:166
> は StaffFrame.ToDevice のインライン=負債でない、GraceNoteEngraver:132 は renderer 不使用の残骸)。
>
> **済(本ブランチ・push 前)**: `5e8f899` decorator(死にコード)+ 設計修正、`bea2b81` 漸進計画、
> `0b344e5` handoff、**`8725d39` SkylineBuilder を Y-up 化(box/stem/ledger/flag/rest を up+ で再導出、
> FromBox 境界で `staffMiddleY − up` 反射)。出力不変=42 snapshot byte 一致・全 1461 緑で実証。**
> これで**移植負債の最後の島を解消**、計算層は一様に「Y-up + 境界反射」。
> **残るは微小な任意項目のみ**: GraceNoteEngraver:132 の残骸 Y(実害なし)。decorator は将来の単一 flip
> 統合用に死にコードのまま(全 grob が相対 Y-up 化した後に配線=現状その必要性は低い)。

---

## 1. いまの状態(git)

- **`master`(`c4e3a01`、push 済み・全 1461 緑)** — 本日のセッションの成果が確定:
  part-combine gate(`42d1ae8`)、衝突 port(`cd594af`)、design/handoff docs。**これが安定基盤。**
- **ブランチ `stage4-yup-inversion`(`bac32aa`、push 済み)** — master から分岐し
  `docs/STAGE4_YUP_INVERSION.md`(Stage 4 設計)を追加しただけ。**実装コードは未着手。**
- 再開時: `git checkout stage4-yup-inversion`。**完成・byte 一致確認後に master へマージ。**
- ブランチ削除/新規作成は明示 GO 待ち。

---

## 2. 直前セッション(2026-06-23)でやったこと

1. **part-combine ラベル汚染を修正**(`42d1ae8`)。`<< \\ >>` は LP では \partcombine でないので
   "a2"/"Solo" を出さない。`LayoutOptions.EnablePartCombine`(既定 false)で gate。安全網回復。
2. **衝突 port = 真の output バグ修正**(`cd594af`)。真因は **systematic halving**:LP は両声部を
   対称にずらし(±inner)leftmost を pin する→2声で分離 2×inner。Lily# は片側だけ=半分だった
   (close_half の magic 1.0 だけ偶然合っていて隠れていた)。`AnalyzeCollision` を対称返却に、
   magic 1.0→raw 0.52、touch 0.65→0.5。`CalculateVoiceOffsets` を leftmost-pin に。full/close_half/
   mesh を LP 描画と突合済み。extent 正規化は標準 head では ≈1.0 の no-op と判明。
3. **scope 確定**:Stage 2(notehead staff 相対 Y=`StaffFrame.PositionToDevice`)と Stage 3(Align
   風 譜間 skyline=`MultiStaffLayouter.CalculateStaffGapWithSkylines`)は **master で既に実装・配線済み**。
   ⇒ branch の出力影響仕事は実質 Stage 1 衝突 port だけだった。残る Stage 4 は**出力中立**。
4. master へマージ(`5597cb1`→`c4e3a01` fast-forward)、LSP を VS Code へデプロイ
   (`deploy-extension.ps1`、`0.1.2-dev.70`/LSP `0.1.1-20260623-1513`)、HEAD デプロイ確認、回帰なし。
5. **Stage 4 設計を完了・コミット**(`bac32aa`、本ブランチ)。

---

## 3. Stage 4 実装の要点(詳細は `docs/STAGE4_YUP_INVERSION.md`)

- **重要前提**:Lily# は native device-Y-down。「per-grob `pageHeight−y` フリップ」は**存在しない**
  ので、Stage 4 は「最後の機械的フリップ」ではなく **device→Y-up の本格反転**(~170箇所/~20ファイル)。
- **単一フリップ**:`SharedRenderer` で page 内容全体を1個の transform group で包む:
  `gc.BeginGroup(new DrawingTransform(TranslateY: page.Height, ScaleY: -1))`(`y_device = page.Height
  − y_up`)。`DrawingTransform` は ScaleY 対応、backend(SVG/PNG/PDF)は既存 margin scope と同経路で
  適用=**LP の stencil-time フリップに相当**。`StaffFrame` の per-staff 反射は**廃止**。
- **内部 Y-up 規約**:Y=0 = page 下端、上方向が正。`page.Height − x` を撒くのでなく**式ごと反転**
  (`staffMiddleY − pos/2` → `staffMiddleY_up + pos/2` 等、staff-position 高=Y-up 大で符号反転)。
- **影響範囲**:`SharedRenderer`(60)・`SkylineBuilder`(16)・`StaffFrame`(12 廃止/反転)・
  `ElementCoordinator`(7)・各 engraver・`MultiStaffLayouter`・`LayoutEngine`・`LayoutUtilities`。
- **atomic**:途中は「一部 Y-up・一部 device」でパイプライン破綻。**全反転完了まで byte 検証不能**。
  成功条件 = **42 snapshot が byte 不変**。差が出たら符号ミス=修正、**リベースしない**(出力を
  変えてはいけない)。
- device-Y 起点(反転対象):`LayoutUtilities.CalculateFirstSystemY`(header を上端に下方向)、
  `LayoutEngine` の `currentY += StaffHeight + SystemSpacing`(下方向累積)、`FindStaffYInSystem`。

---

## 4. 環境・コマンド(厳守)

- **シェルは ripple MCP `execute_command`(shell=pwsh)。PowerShell/Bash ツールは使用禁止**
  (detached プロセスが `bin/Debug` DLL をロックしビルド破壊)。
- リポジトリ `C:\MyProj\LilySharp` / LP クローン `C:\MyProj\lilypond-src` / `lilypond` は PATH(v2.24.4)。
- 特殊文字(`<< \\ >>` 等)を含むファイル/コミットメッセージは ripple ヒアストリングでなく
  `Write` ツールでファイル化(コミットは `git commit -F`)。
- ビルド: `dotnet build LilySharp.slnx`（または `LilySharp.Core`）/ テスト: `dotnet test LilySharp.Tests`
  （snapshot のみ: `--filter "FullyQualifiedName~SvgSnapshotTests"`、計 42）。フル 1461 緑が基準。
- **snapshot 再ベースライン**(Stage 4 では原則使わない=出力不変が条件):
  ```pwsh
  $env:LILYSHARP_UPDATE_SNAPSHOTS='1'
  dotnet test LilySharp.Tests --filter "FullyQualifiedName~SvgSnapshotTests"
  $env:LILYSHARP_UPDATE_SNAPSHOTS=$null
  ```
- **LP 描画(Guile デッドロック回避・必須)**:
  ```pwsh
  cd C:\temp; cmd.exe /d /s /c "lilypond --png -dresolution=200 -o out in.ly < NUL > out.log 2>&1"
  ```
- **Lily# 描画**: `dotnet run --project LilySharp.Cli -- png samples\test\x.lys C:\temp\x.png`。
- **LSP デプロイ**: `.\deploy-extension.ps1`(VS Code を kill→VSIX build→install→再起動、version 自動 bump)。
- `Read` ツールは PNG/SVG を視覚表示できる。snapshot SVG から座標を精密測定できる
  (例: 本日 `test__collision.svg` から close_half 分離 1.30 を実測)。
- コミットは英語、`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`(model 名に合わせる)。
- **ship ゲート**: build/test/commit/push は可。tag/PSGallery publish/告知は green + 明示 GO。

---

## 5. やってはいけないこと / 教訓

- ~~**Stage 4 で snapshot 差をリベースで飲まない**。出力同値が条件、差=符号ミス。~~
  → **撤回**(セッション2)。byte 一致は要件でなくなった。各 increment は LP 比較で正しさを確認し、
  改善・正当な変化なら snapshot を再ベースラインしてよい。ただし「意図しない差」は依然バグなので、
  再ベースライン前に必ず LP と突合して**正当性を確認**すること(無検証の再ベースラインは禁止)。
- 「設計 doc の前提」を一次照合する。本プロジェクトでは設計 doc の想定(extent 正規化が最優先、
   Stage 2-4 が未実装、per-grob フリップ存在)が**実コードと食い違っていた**(衝突の真因は別、
   Stage 2-3 は既達、per-grob フリップ無し)。**必ず実コードを読んで確認**。
- master は安定確定。Stage 4 はブランチで隔離、完走・byte 一致後にマージ。

---

## 6. 参照

- Stage 4 設計: `docs/STAGE4_YUP_INVERSION.md`(本ブランチ、実装スペック)
- 直前 branch の経緯: `docs/HANDOFF_lp-coordinate-model.md`、`docs/LP_COORDINATE_MODEL.md`(master)
- Stage 4 主要ファイル: `Rendering/SharedRenderer.cs`、`Rendering/DrawingTransform.cs`、
  `Svg/Layout/StaffFrame.cs`、`Svg/Layout/LayoutUtilities.cs`、`Svg/Layout/MultiStaffLayouter.cs`、
  `Svg/Layout/SkylineBuilder.cs`
