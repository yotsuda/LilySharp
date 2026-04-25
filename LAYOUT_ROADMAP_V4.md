# LilySharp LilyPond レイアウト再現度向上ロードマップ V4

## Status (2026-04-25 created, updated 2026-04-25)

- **前提**: V3 ロードマップ完全達成 (~89-90% 到達)。実用音楽 (orchestral / piano / vocal / chamber) のほとんどを正しくレンダリング可能。
- **2026-04-25 修正**: 軸 3 の Multi-staff slur/tie underdetection は実は multi-staff 経路の **section 展開バグ** だった。修正済み。詳細は下記
- **V4 目標**: ~95%+ への引き上げ、または "selective fidelity" — LP 完全互換ではなく、**特定用途で LP に劣らない**ことを目指す。
- **絶対原則**: LP ソース準拠 (V3 から継承)。全変更に `LILYPOND-REF: lily/<file>.cc:<lines>` 必須。LP 2.25.35 基準で行番号記録。

---

## 残課題の分類

V3 完了後に残る課題は性質が大きく異なるため、**3 つの軸** に分けて整理する:

### 軸 1: Font / Glyph 精度
- **Pango font / Emmentaler 完全模倣** (~30h)
- 現状: SMuFL Bravura font metadata 使用
- LP: Pango + Emmentaler metafont
- 影響: 文字幅 / instrument-name / lyric が ±5-10% 差
- 戦略選択肢:
  - **(a) SkiaSharp + HarfBuzzSharp 統合** (~30h): 完全模倣
  - **(b) Bravura のまま継続** (0h): 視覚的差は許容、注釈付きで明示
  - **(c) 採用: Emmentaler glyph metrics 部分抽出** (~10h): hot path のみ精度向上

### 軸 2: アーキテクチャ拡張
- **callback property system** (~50h+)
- 現状: engraver 内で直接実装、`\override Foo.calc-Y = #my-fn` 不可
- LP: 47 callback properties (`before-line-breaking`, `after-line-breaking`, `pure-Y-extent`, `springs-and-rods` 等)
- 戦略選択肢:
  - **(a) C# dynamic dispatch 大改修** (~50h+): 完全互換
  - **(b) 採用: 主要 callback のみ static-dispatch で支援** (~10-15h): 90% のユースケース対応
  - **(c) Skip**: LP 完全互換は諦め、LilySharp 設計思想 (declarative) を維持

### 軸 3: V3 partial / refinement
- **Cross-system beam slope correction** (~3h)
- 現状: cross-system broken pieces は anchor measure の Y を使用
- LP: 各 piece が独立に slope 計算 (LP `lily/beam.cc:590-600` の `break_overshoot` ロジック)
- 推定影響: 視覚的に微妙、専門眼でも気付き難い
- 推奨: 後回し、LP との視覚回帰でフラグが立てば対応

- **Multi-staff slur/tie underdetection** ✅ 解決 (2026-04-25)
- 症状再現: feature-tour.lys multi=path 2/rect 11, single=path 11/rect 43。multi で全 section の音楽内容自体が脱落していた
- 根本原因: `MeasureCollector.CollectMeasuresForVoice` が `_structure` を無視し、`_sections.Values` 内の最初の PartBlock のみ採用。`structure { A B C D }` 展開が multi-staff 経路だけ適用されず、1 セクション分しか voice に入らなかった
- 修正: `CollectMeasuresForVoice` の case 2 を structure 認識済の `CollectMeasures()` 委譲に置換 (single-staff と同経路)
- 結果: multi=path 20/rect 43。bass 声部の ties 込みで正常。1324/1326 PASS (skipped 2)、multi-staff snapshot 25 件更新 (section ラベル + 取りこぼし内容)
- 当初の `EnumerateStaves` が `StaffIndexInGroup` を返す件は実在の不整合だが症状原因ではない (FindStaffYInSystem は match 失敗時 `system.Y` fallback で loss にはならず Y ズレのみ)。独立 issue として別途確認の余地あり

---

## V4 Sprint 構成

### Sprint 4 (短期、~10-15h): Font 部分模倣
**目的**: hot path (instrument-name, lyric, dynamic) の文字幅を Emmentaler 値に合わせる

- **F-1**: Emmentaler 主要 glyph (notehead, accidental, clef) の bbox 抽出
- **F-2**: Bravura → Emmentaler width remap テーブル
- **F-3**: 既存 GlyphMetrics.cs に Emmentaler 値を併設、設定で切替可
- **F-4**: 視覚回帰での比較 (Compare-Svg.ps1 を vague font diff で許容)

### Sprint 5 (中期、~10-15h): callback property 部分対応
**目的**: 主要 LP 拡張点のうち、ユーザーが実用的に override したい属性を支援

- **C-1**: `before-line-breaking` / `after-line-breaking` 相当の hook 設計
- **C-2**: `Y-offset` / `X-offset` / `padding` の grob-property 経由 override (現状 `_grobOverrides` 経由で部分対応)
- **C-3**: TextScript / DynamicText / TupletNumber の text-formatting callback サポート

### Sprint 6 (任意、~3-5h): V3 refinement
- **R-1**: Cross-system beam slope correction
- **R-2**: Chord ties Y position の MIDdle-line bias (LP nuance)

---

## V4 採用しない方針

### Skip リスト (戦略的に対応しない)
- **中世記譜法** (Gregorian/Mensural/Vaticana ligature, Custos, Episema, Divisio): out-of-scope 継続
- **Tab / FretBoard 完全実装**: 別プロジェクト規模
- **Scheme 互換評価器**: LP 拡張機能の callback として LP は Scheme を使うが、LilySharp は C# native のみ
- **ay/synthesis MIDI 高度機能**: 楽譜表記範囲外

### 単純に保留
- **Beam continuation の advanced slope** (LP `\override Beam.damping`): rare case
- **Volta (repeats) の alternate text per repeat**: 機能追加扱い

---

## V4 達成目標

| マイルストーン | 到達度 | テスト数 | 備考 |
|---|---|---|---|
| Sprint 4 完了 (font) | ~90-92% | ~1,360 | Emmentaler glyph metrics 主要部 |
| Sprint 5 完了 (callback) | ~92-94% | ~1,400 | Y/X offset override 支援 |
| Sprint 6 完了 (refine) | ~93-95% | ~1,420 | beam slope, chord ties |
| **V4 上限** | **~95%** | **~1,420** | LP 完全 100% は callback / Scheme で阻まれる |

---

## 検証戦略

### 自動検証 (継続)
1. xUnit 全 1,324+ テスト pass 維持
2. SVG snapshot 39+ 不変 (意図的変更時のみ更新)

### 視覚回帰 (Sprint 4-6 では強化)
3. **新提案**: `audit/scripts/Run-LilyPond.ps1` を CI で月次実行 (LP 出力 vs Lily# 出力)
4. **新提案**: `audit/visual_regression_baseline.csv` を生成済 baseline として commit
5. **新提案**: 主要メトリクスの thresholds を `audit/regression-thresholds.json` で管理

### 残存リスク受容
6. Pango / Emmentaler 完全模倣の差 (~5%) は注釈付きで Document
7. Callback property の不完全模倣はユーザー文書で明示

---

## 哲学: V4 のアプローチ

V3 まで: **LP 100% 互換を目指す** ロードマップ
V4: **LP 95% で実用十分** + **LilySharp ならではの利点最大化**

LilySharp の独自利点:
- **静的型付け**: C# で型安全な楽譜操作
- **declarative API**: WPF/Avalonia/SVG への組み込みが容易
- **AOT compatibility**: パフォーマンス重視のシナリオで LP より速い可能性
- **Embeddable**: MCP server 経由で AI agent から音楽生成

V4 は **LP との完全一致を譲歩** しつつ、これら独自利点を活かす方向に舵を切る。

---

## 参照

- 親文書: [LAYOUT_ROADMAP_V3.md](LAYOUT_ROADMAP_V3.md) — Sprint 1-3 + ext の達成状況
- 評価レポート: [REVIEW_REPORT.md](REVIEW_REPORT.md) — Phase 1 から Phase 4 までの推移
