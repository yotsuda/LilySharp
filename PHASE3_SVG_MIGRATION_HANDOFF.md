# Phase 3 SVG Migration — 引継ぎ資料

作成: 2026-04-27 (Claude Opus 4.7 セッション)

## このドキュメントの目的

`SvgGenerator` を `SvgRenderer.cs` (5028 行) ベースから、`SharedRenderer` + `SvgDocumentContext` ベースに本格移行するための、次セッション着手前提の引継ぎ資料。

---

## 0. 前提知識・環境

このドキュメント単体で次セッションを開始できるよう、必要な前提を整理する。

### 0.1 プロジェクト所在

| 項目 | 場所 |
|---|---|
| LilySharp 本体 | `C:\MyProj\LilySharp` |
| **LilyPond ソースクローン (必須)** | `C:\MyProj\lilypond-src` |
| ビルド済 CLI | `LilySharp.Cli/bin/Debug/net9.0/lysc.exe` |
| サンプル `.lys` | `samples/test/`, `samples/showcase/`, `samples/demo/` |
| SVG snapshot baseline | `LilySharp.Tests/Snapshots/` (40 件) |
| Emmentaler フォント | `LilySharp.Core/Fonts/emmentaler-20.{otf,woff2}`, `emmentaler-brace.{otf,woff}` |

`C:\MyProj\lilypond-src` は LILYPOND-REF コメントの整合性検証に必須。WebFetch で LP source を見るのは禁止 — 必ずローカルクローンを Read/Grep する。

### 0.2 ビルド・実行環境

- **.NET 9** (`net9.0` target)
- C# 12 (record / pattern matching を多用)
- Build: `dotnet build LilySharp.Core/LilySharp.Core.csproj` (sln ファイルは無いので csproj 個別指定)
- 主要 csproj:
  - `LilySharp.Core/LilySharp.Core.csproj` (engraving 本体)
  - `LilySharp.Cli/LilySharp.Cli.csproj` (`lysc.exe`)
  - `LilySharp.Tests/LilySharp.Tests.csproj` (xUnit)
  - `LilySharp.Lsp/LilySharp.Lsp.csproj` (Language Server)
- 主要 NuGet 依存: `PdfSharpCore` 1.3.65, `Svg.Skia` 3.4.1 (SkiaSharp 2.88 を transitively bring in)

### 0.3 絶対ルール (LilySharp 固有)

[`MEMORY.md` の "LilySharp 絶対ルール" entry および `lilysharp_rules.md` を参照]

1. **LilyPond ソース準拠**: レイアウト/スペーシング実装は常に LP のソースコード準拠。独自の近似やヒューリスティックを追加しない。
2. **LILYPOND-REF コメント必須**: 変更時は `LILYPOND-REF: lily/<file>.cc:<lines> <意味>` を該当箇所に明記。LP 2.25.35 基準で行番号記録。
3. **Emmentaler 排他**: Bravura 由来の数字は完全除去済 (commit `8495976`、2026-04-26)。新規定数は emmentaler-20.otf から `audit/scripts/Extract-EmmentalerMetrics.py` で抽出した値を使う。
4. **命名**: ユーザ向け文字列は "Lily#" (lilysharp ではない)。

### 0.4 検証ツール

| 用途 | コマンド |
|---|---|
| LP-REF citation 整合性検証 | `pwsh -File audit/scripts/Verify-LilyPondRefs.ps1` |
| Emmentaler glyph metrics 再抽出 | `python audit/scripts/Extract-EmmentalerMetrics.py` |
| SVG snapshot 比較 | `dotnet test LilySharp.Tests --filter "FullyQualifiedName~SvgSnapshot"` |
| Snapshot baseline 一括更新 | `$env:LILYSHARP_UPDATE_SNAPSHOTS="1"; dotnet test ...` |
| 全テスト (perf 除く) | `dotnet test LilySharp.Tests --filter "FullyQualifiedName!~PerformanceTests"` |
| サンプル PDF 生成 | `lysc.exe pdf samples/test/ossia.lys out.pdf` |
| サンプル SVG 生成 | `lysc.exe svg samples/test/ossia.lys out.svg` |
| サンプル PNG 生成 | `lysc.exe png samples/test/ossia.lys out.png` |

### 0.5 シェル環境

- Windows 11、bash (Git Bash) と PowerShell 7+ (`pwsh`) 両用
- bash は Unix 構文 (`/dev/null`, forward slash path、`/c/MyProj/...` で C: drive アクセス)
- 大量にコマンドを叩くなら ripple MCP 推奨 (高速、可視 console)。pwsh MCP は補助。
- ファイル検索は **Glob/Grep ツール優先** (find/grep を Bash で叩かない)

### 0.6 補足: 現セッションの memory 関連

このセッションで追加された/参照した user memory:

- `lilypond_source_clone.md` — LP source の場所 (上記 §0.1 と同じ)
- `lilysharp_rules.md` — LP 準拠の絶対ルール (上記 §0.3 と同じ)

新規 memory 追加は原則不要 (このドキュメントが project-specific 知識を担う)。ただし:
- 設計判断が変わった場合 (例: byte-identical を諦める方針確定後)
- 新しい絶対ルールが発生した場合

は `MEMORY.md` に追記推奨。

---

## 1. ゴールと現在地

### 最終ゴール

```
すべての出力 backend が SharedRenderer 経由になる:

SVG: SvgGenerator → SharedRenderer ──→ SvgDocumentContext
PDF: PdfGenerator → SharedRenderer ──→ PdfDocumentContext   ← 達成済
PNG: PngGenerator → SharedRenderer ──→ PngDocumentContext   ← 達成済
                          ↑
                    engraving ロジックの単一の真実
                    (現在 1700 行)
```

### 現在地 (commit `f0072c6` 時点)

| Backend | 駆動方式 | 状態 |
|---|---|---|
| SVG | `SvgGenerator → SvgRenderer` (5028 行、独立) | **未移行** |
| PDF | `PdfGenerator → SharedRenderer + PdfDocumentContext` | 達成済 |
| PNG | `PngGenerator → SharedRenderer + PngDocumentContext` | 達成済 |

`IDrawingContext` API は PDF/PNG の 2 backend で実証済 — 抽象が backend 中立に機能することは確認済。

---

## 2. 完了済みフェーズ (このセッション)

### Phase 1 + 2-A 〜 2-J: SharedRenderer 構築

10 commits で `SharedRenderer.cs` を 0 → 1700 行に育てた:

| Commit | 内容 |
|---|---|
| `a0d39b0` | Phase 1+2-A: IDrawingContext foundation + 基本 (staff/clef/notehead/stem/beam/etc.) |
| `cf4c735` | 2-B: accidentals + ties + slurs |
| `47e97fe` | 2-C: dynamics + articulations + lyrics |
| `b849100` | 2-D: hairpins + ottava/volta/tuplet brackets |
| `8c9057d` | 2-E: trill + glissando + arpeggio + grace notes |
| `f491f31` | 2-F: chord names + figured bass + percent + bar/stanza numbers + fingering |
| `b64fc74` | 2-G: music marks + text spanners + pedal + MMR + tie variants + custom text |
| `536f995` | 2-H: tremolo + lyric hyphens + part combine + system-start delimiter (brace 以外) |
| `4e89cb4` | 2-I: brace + mid-measure clef/key changes |
| `2dc453b` | 2-J: cue notes |

### Phase 3-A: legacy PdfRenderer 削除

| Commit | 内容 |
|---|---|
| `1881f04` | `LilySharp.Core/Pdf/Renderer/PdfRenderer.cs` (-593 行) 削除、`PdfRenderOptions.cs` を `LilySharp.Core.Pdf` namespace に移動 |

### PNG backend 追加 (Phase 3-B 着手前の検証)

| Commit | 内容 |
|---|---|
| `f0072c6` | `PngDocumentContext` + `PngDrawingContext` (SkiaSharp 直接駆動)、`PngGenerator` を rewire |

PNG backend を先に作った理由: `IDrawingContext` の API が SVG 以外の 2 backend で動くことを検証してから本番の SVG 移行に進むため。結果、API 設計に問題なし。

---

## 3. Phase 3-B 〜 3-E: 残作業の全体像

```
Phase 3-B: SvgGenerator を SharedRenderer + SvgDocumentContext にスイッチ
Phase 3-C: SharedRenderer に SVG-parity features を追加 (3-B と並行 or 先行)
Phase 3-D: 40 件の SVG snapshot baseline を更新
Phase 3-E: SvgRenderer.cs (5028 行) と BraceRenderer.cs を削除
```

推奨順序: **3-C を先に大半終わらせる → 3-B (スイッチ) → 3-D (baseline 更新) → 残った 3-C の細部 → 3-E (削除)**

---

## 4. SharedRenderer に欠けている SVG-parity features

`SvgRenderer` にあって `SharedRenderer` にまだない要素。Phase 3-C で順に追加:

### 4.1 Layout margins ★最優先★
- `SvgRenderer` は全座標に `_layoutOptions.MarginLeft` / `MarginTop` を加算する。
- `SharedRenderer.RenderTo` は `BeginPage(page.Width, page.Height)` を呼んで staff lines を `(0, 0)` から描く。
- 解決: `SharedRenderer.RenderTo` に optional `LayoutOptions` 引数を追加 → 内部で global translate を適用、または座標計算で margin を加算。
- 影響範囲: 全 Draw* メソッド (X 座標を扱うもの全て)。

### 4.2 Instrument names
- `SvgRenderer.DrawInstrumentNames` (line ~719): system.Indent 領域の左側に staff の楽器名を描画。
- 必要: `_layoutOptions.MarginLeft + system.Indent / 2` の位置に "serif", `FontSize * 0.75`, anchor=Middle で描画。Grand staff 単一名なら Y 中央寄せ、複数名は per-staff。
- 依存: 4.1 (margins) が完了している必要あり。

### 4.3 Tab notation
- TabClef 専用描画 (`DrawTabStaff`, `DrawTabMeasure`, `DrawTabNote`)
- 6 線譜 (string) + フレット番号 (text) + tab 専用 stem
- 影響: 既存 sample に tab 楽譜が少ないので優先度低。後回し可。

### 4.4 GrobPropertyResolver (color override)
- `SvgRenderer` は `_resolver.GetString("Stem", "color")` で `\override Stem.color = #red` 等を解決。
- 現在 SharedRenderer は全要素を black 固定。
- 解決: `SharedRenderer.RenderTo` に `GrobPropertyResolver` を threading、各 Draw* で `GetResolvedColor("Stem")` 等を呼ぶ。
- 影響: 全 glyph/line drawing。

### 4.5 Cross-staff Y adjustment
- Grand staff で voice が staff 間を跨ぐとき、note の Y は別 staff の baseline で計算される。
- `SvgRenderer` は `CrossStaffLayouts` を見て note 位置を調整。
- 現在 SharedRenderer は note を all-on-one-staff で描画。
- 影響: piano grand staff で cross-staff voicings がある楽譜のみ。優先度中。

### 4.6 Tempo collision avoidance with trill
- `SvgRenderer.DrawTrillSpanners` は tempo mark の bbox と重なるとき trill を上に持ち上げる。
- 現在 SharedRenderer.DrawTrillSpanners はそのまま描画 (collision なし)。
- 影響: tempo + trill が同じ measure にある楽譜のみ。優先度低。

### 4.7 Ossia barlines (separate from main barlines)
- `SvgRenderer.DrawOssiaBarlines`: ossia staff は独立した barline を持つ (long 全段 barline ではなく ossia staff 内部のみ)。
- 現在 SharedRenderer は ossia barline を主 barline と同じく描く (group transform 内なので結果は同じだが、コード経路は別)。
- 影響: ossia staff を含む楽譜のみ。確認後対応。

### 4.8 LedgerLineSpans (chord-aware merged ledger lines)
- `SvgRenderer` は `layout.LedgerLineSpans` を見て、隣接する同 staff position の chord ledger line を 1 本に merge。
- 現在 SharedRenderer は per-note で個別に描く (per-note 描画は valid; merged は cosmetic improvement)。
- 影響: 高音/低音の chord が密集する楽譜の見た目だけ。優先度低。

### 4.9 SVG 固有の出力差分

`SvgDrawingContext` (現状) と `SvgRenderer` の出力 byte 差:

| 要素 | SvgRenderer | SvgDrawingContext (現状) |
|---|---|---|
| Staff line | `<line class="staff" x1=... />` | `<line stroke="#000000" stroke-width=... />` |
| Barline | `<rect ... fill="black"/>` | `<rect ... fill="#000000"/>` |
| Music glyph | `<text class="music" x=... font-size=... data-pos=...>` | 同じ (一致) |
| `<style>` block | CSS classes (`.staff`, `.barline`, `.ledger`, `.title` 等) | `.music` のみ |
| Color | `"black"` 等の named color | `#000000` (hex) |

**結論**: snapshot 完全 byte-identical は不可能。Phase 3-D で baseline 全更新が必要。

### 4.10 その他の細部
- Title / composer のフォントサイズ (現在は概算; SvgRenderer は厳密値)
- Time signature の特殊形 (mensural / classical)
- Clef change の `_change` glyph 選択 (実装済だが SvgRenderer の `_currentDrawClef` 連携は未対応)
- Rehearsal mark の box 幅計算 (`MeasureSerifBoldText` 相当の文字幅推定)
- `.tab` line styling

---

## 5. 重要な落とし穴

### 5.1 worktree が WIP 依存で失敗 ★

`git worktree` で feature branch を切って試したが失敗。理由:

> 私の Phase 2 commits (`b64fc74`, `f491f31`, `536f995` 等) は `BarNumberLayouts` / `FingeringLayouts` / `MultiMeasureRestLayouts` 等を参照。これらの **engraver / layout 定義はユーザーの WIP (untracked files)**:
>
> - `LilySharp.Core/Svg/Layout/BarNumberEngraver.cs`
> - `LilySharp.Core/Svg/Layout/FingeringEngraver.cs`
> - `LilySharp.Core/Svg/Layout/LedgerLineSpannerEngraver.cs`
> - `LilySharp.Core/Svg/Layout/MultiMeasureRestEngraver.cs`
> - `LilySharp.Core/Svg/Layout/SpannerBreakSubstitution.cs`
> - `LilySharp.Core/Svg/Layout/StaffAffinity.cs`
> - `LilySharp.Core/Svg/Layout/StanzaNumberEngraver.cs`
> - `LilySharp.Core/Svg/Layout/TieVariantEngraver.cs`
>
> これらは master の HEAD に含まれていない (untracked)。worktree は HEAD の committed state だけを持つのでビルド失敗。

**対策**: 次セッション開始時に **ユーザーの WIP を先に commit してもらう**。これらの engraver 群は完成した機能なので commit 可能。それから branch を切る。

`git status --short` で確認できる。

### 5.2 Snapshot tests は byte-identical を要求

`LilySharp.Tests/Svg/SvgSnapshotTests.cs` は `Assert.Equal(baseline, svg)` で生 string 比較。Phase 3-B の switch 直後は 40 件全て fail する。

**対策**: 環境変数 `LILYSHARP_UPDATE_SNAPSHOTS=1` で一括 baseline 再生成。ただし visual regression を見落とすリスクがあるので、代表的な数件は browser で目視確認推奨:

```powershell
$env:LILYSHARP_UPDATE_SNAPSHOTS="1"
dotnet test LilySharp.Tests --filter "FullyQualifiedName~Snapshot"
```

### 5.3 `BeginGroup(Identity)` が PdfSharpCore で `NotInvertible` 例外

Phase 2-A で踏んだ落とし穴。`PdfDrawingContext.BeginGroup(DrawingTransform.Identity)` を空でも呼ぶと PdfSharpCore の transform stack が壊れる。修正済: identity の場合は `BeginGroup` を呼ばずに `null` を返す pattern にした (`SharedRenderer.cs:99`)。

SVG/PNG backend では問題なし。

### 5.4 Untracked engraver 依存の伝播

`SvgRenderer.cs` も上記 untracked files に依存している (検証なし; おそらく依存)。SvgRenderer.cs を削除すると依存関係が変わるので、削除前に「ユーザー WIP の engraver 群が引き続き SharedRenderer にとって必要」を確認。

---

## 6. 推奨実行プラン

### 着手前準備

```powershell
# 1. ユーザーに WIP commit を依頼。特に LilySharp.Core/Svg/Layout/ 配下の untracked
#    engraver 群と、それに関連する modified files (Score.cs, ScoreLayout.cs, etc.)
git status --short
git add LilySharp.Core/Svg/Layout/ LilySharp.Core/Svg/Model/ LilySharp.Core/Svg/Collector/ ...
git commit -m "WIP: engravers feeding SharedRenderer Phase 2-F/G/H"

# 2. Branch を切る
git checkout -b feature/svg-shared-renderer

# 3. 既存テスト pass を確認 (baseline)
dotnet test LilySharp.Tests --filter "FullyQualifiedName!~PerformanceTests"
```

### Phase 3-C: SVG-parity features (3-B 前にやるべき範囲)

優先度順:
1. **Layout margins threading** (4.1) — 全座標に影響
2. **Instrument names** (4.2) — orchestral/chamber score で必須
3. **GrobPropertyResolver color** (4.4) — `\override Stem.color` を使う sample がある
4. (オプション) Cross-staff Y (4.5) — piano grand staff で必要なら
5. (オプション) Tempo collision (4.6) — 軽い hack で十分
6. (オプション) Ledger line spans (4.8) — purely cosmetic

各 feature ごとに 1 commit。テストはまだ snapshot 更新せず、SvgRenderer も触らないので既存 1330 件は pass し続ける。

### Phase 3-B: スイッチ

```csharp
// LilySharp.Core/Svg/SvgGenerator.cs
public static string Generate(SyntaxTree tree, SvgRenderOptions? options = null, string? renderName = null)
{
    options ??= SvgRenderOptions.Default;

    // (旧コード削除)
    // var renderer = new SvgRenderer(renderOptions: options);

    var renderSpec = string.IsNullOrEmpty(renderName)
        ? RenderSpecParser.FindFirst(tree)
        : RenderSpecParser.FindByName(tree, renderName);

    MultiStaffScore multiScore;
    ScoreLayout layout;
    if (renderSpec != null && renderSpec.IsMultiStaff)
    {
        var collector = new MeasureCollector();
        multiScore = collector.CollectMultiStaff(tree, renderSpec);
        layout = new LayoutEngine().Layout(multiScore);
    }
    else
    {
        // ... single-staff path → MultiStaffScore.FromScore
    }

    var docOptions = new SvgDocumentOptions
    {
        EmbedFont = options.EmbedFont,
        OmitFontFace = options.OmitFontFace,
        FontDirectory = options.FontDirectory,
        // ... 必要なら LayoutOptions も渡す
    };
    using var doc = new SvgDocumentContext(docOptions);
    SharedRenderer.RenderTo(multiScore, layout, doc);
    doc.Dispose();
    return doc.ToSvg();
}
```

`GenerateAll` と `GenerateMultiMovement` も同じ pattern で書き換え。

build 通ったら snapshot test を走らせて全 fail を確認 (baseline) → 次へ。

### Phase 3-D: Baseline 更新

```powershell
# 1. update mode で全 baseline を再生成
$env:LILYSHARP_UPDATE_SNAPSHOTS="1"
dotnet test LilySharp.Tests --filter "FullyQualifiedName~SvgSnapshot" --nologo --no-restore

# 2. 視覚検証 (代表的な 5-10 件を browser で開く)
start LilySharp.Tests/Snapshots/showcase__03-piano.svg
start LilySharp.Tests/Snapshots/test__feature-tour.svg
start LilySharp.Tests/Snapshots/test__lyrics.svg
start LilySharp.Tests/Snapshots/test__articulations.svg
start LilySharp.Tests/Snapshots/test__ossia.svg

# 3. 問題なければ commit
$env:LILYSHARP_UPDATE_SNAPSHOTS=""
dotnet test LilySharp.Tests --filter "FullyQualifiedName~SvgSnapshot"  # 全 pass 確認
git add LilySharp.Tests/Snapshots/
git commit -m "Phase 3-D: regenerate SVG snapshot baselines for SharedRenderer migration"
```

### Phase 3-E: 削除

```powershell
git rm LilySharp.Core/Svg/Renderer/SvgRenderer.cs        # 5028 行
git rm LilySharp.Core/Svg/Renderer/BraceRenderer.cs      # SharedRenderer に移植済
# Renderer/ フォルダが空になるなら rmdir
git rm LilySharp.Core/Svg/Renderer/SvgRenderOptions.cs   # ← または LilySharp.Core/Svg/SvgRenderOptions.cs に移動
# (SvgRenderOptions は今後も必要だが namespace を整理してもよい)

# 関連 using statement の cleanup
# - SvgGenerator.cs (もう SvgRenderer を使わない)
# - PngGenerator.cs (まだ legacy ConvertSvgToPng で SVG を扱うので using 残す可能性)
# - その他 SvgRenderer を import している場所

# Build + 全テスト
dotnet build
dotnet test LilySharp.Tests --filter "FullyQualifiedName!~PerformanceTests"
```

---

## 7. 検証チェックリスト

Phase 3 完了の判定基準:

- [ ] `dotnet build` clean (0 warning, 0 error)
- [ ] `dotnet test` 全 pass (PerformanceTests を除く)
- [ ] SVG snapshot 40+ 件 pass (baseline 更新後)
- [ ] PDF 出力サイズが Phase 2-J 時点と同等 (~60-120 KB)
- [ ] PNG 出力サイズが Phase 3 開始時点と同等
- [ ] 代表 sample の視覚比較 (旧 SvgRenderer → 新 SharedRenderer):
  - [ ] `samples/test/feature-tour.lys` (multi-staff, 各種記号)
  - [ ] `samples/showcase/03-piano.lys` (grand staff + brace)
  - [ ] `samples/test/lyrics.lys` (lyric hyphens, extenders)
  - [ ] `samples/test/articulations.lys` (各種 articulation)
  - [ ] `samples/test/ossia.lys` (ossia scaling)
  - [ ] `samples/showcase/01-expressions.lys` (dynamics + tempo + accidentals)
- [ ] `audit/scripts/Verify-LilyPondRefs.ps1` クリーン (LP-ref citation 整合性)
- [ ] `SvgRenderer.cs` と `BraceRenderer.cs` が repository から消えている

---

## 8. 工数見積もり

| Phase | 概算工数 | 備考 |
|---|---|---|
| 着手前準備 (WIP commit + branch) | 0.5h | ユーザーが WIP 整理する時間含む |
| 3-C (parity features 主要 4-5 個) | 6-8h | margin / instrument names / color が大半 |
| 3-B (switch + iterate) | 3-4h | 主に positioning bug 修正 |
| 3-D (baseline + 視覚検証) | 2h | 40 件のうち回帰がないか目視 |
| 3-E (削除 + cleanup) | 1h | |
| **合計** | **12-16h** | 1-2 セッションで現実的 |

---

## 9. 参考: 主要ファイルの所在

### 既存
- `LilySharp.Core/Svg/Renderer/SvgRenderer.cs` (5028 行, **削除予定**)
- `LilySharp.Core/Svg/Renderer/SvgRenderOptions.cs` (残す, 移動可)
- `LilySharp.Core/Svg/Renderer/BraceRenderer.cs` (**削除予定** — SharedRenderer に移植済)
- `LilySharp.Core/Svg/SvgGenerator.cs` (書き換え対象)
- `LilySharp.Tests/Svg/SvgSnapshotTests.cs` (snapshot テスト本体)
- `LilySharp.Tests/Snapshots/` (40 件の baseline SVG)

### 新規 / 修正
- `LilySharp.Core/Rendering/IDrawingContext.cs` (interface, 完成済)
- `LilySharp.Core/Rendering/IDocumentContext.cs` (interface, 完成済)
- `LilySharp.Core/Rendering/SharedRenderer.cs` (1700 行 → ~2200 行になる見込み)
- `LilySharp.Core/Rendering/Svg/SvgDocumentContext.cs` (拡張要)
- `LilySharp.Core/Rendering/Svg/SvgDrawingContext.cs` (拡張要 — `data-pos` は対応済、`.music` class も対応済、追加要素を見て判断)

### 参考用 (untracked / WIP, 着手前 commit 必須)
- `LilySharp.Core/Svg/Layout/BarNumberEngraver.cs`
- `LilySharp.Core/Svg/Layout/FingeringEngraver.cs`
- `LilySharp.Core/Svg/Layout/LedgerLineSpannerEngraver.cs`
- `LilySharp.Core/Svg/Layout/MultiMeasureRestEngraver.cs`
- `LilySharp.Core/Svg/Layout/SpannerBreakSubstitution.cs`
- `LilySharp.Core/Svg/Layout/StaffAffinity.cs`
- `LilySharp.Core/Svg/Layout/StanzaNumberEngraver.cs`
- `LilySharp.Core/Svg/Layout/TieVariantEngraver.cs`

---

## 10. 推奨アプローチ要点

> **大胆に進めて baseline を更新する**。byte-identical 維持のための CSS class / named color の preservation hack は **やらない**。
>
> 理由:
> - SharedRenderer の SVG output は inline attributes (より自己記述的)、CSS class より保守性が高い
> - 一度 baseline を更新すれば二度とこの問題は起きない
> - SVG の中身が "より verbose" になるが、構造的には等価で機能上の違いはない
> - hack を入れると将来の SharedRenderer 拡張がずっと縛られる

ただし、以下は preserve すべき:
- `<text class="music">` の class 指定 (font-family を CSS で制御するため; 既に対応済)
- `data-pos` 属性 (click-to-source 機能; 既に対応済)
- `@font-face` の Base64 埋め込み (既に対応済)

---

## 11. 次セッションの最初の質問テンプレ

ユーザーに以下を確認してから着手:

1. **WIP commit について**: `git status --short` を見て、`LilySharp.Core/Svg/Layout/` 配下の untracked engraver 群を commit してよいか?
2. **Snapshot 更新方針**: byte-identical 維持を諦めて baseline を全更新する方針で OK か? (このドキュメントの推奨)
3. **Tab notation の優先度**: `samples/` 内に tab を使う実用 lys がほぼないので、Phase 3 で対応せず後回しでよいか?
4. **Cross-staff Y の優先度**: piano grand staff 以外で必要な場面が少ないので、Phase 3 で対応せず後回しでよいか?

---

## 12. 着手前に読むべき関連ドキュメント

- `LAYOUT_ROADMAP_V4.md` — V4 全体ロードマップ (Sprint 4 完了, 5/6 残)
- `audit/scripts/Verify-LilyPondRefs.ps1` — LILYPOND-REF 整合性検証スクリプト
- このセッションの 13 commits (上記の commit ログ参照)

以上。
