# Lily# 開発ハンドオフ（常設・単一）

> **このファイルが唯一の引継ぎ先。新しい `handoff-*.md` を作らないこと。**
> 引継ぎは §1「現在地」を**書き換えて**行う（追記しない）。恒久的な知識は §4 の表に従って
> それぞれの置き場所へ出す。ここに溜め込むと、以前と同じように 16 個に分裂する。
>
> **置くのは「次に手を動かすために要るもの」だけ。** セッションの記録・閉じた欠陥の経緯は
> `HANDOFF-ARCHIVE.md`（逐語・2026-07-24 までの §1〜§3）へ出した。読むのは
> **同じ regime にもう一度触るとき**だけでよい。個別事例は原則に汎化して §5 に置く。
>
> ⚠️ **§4〜§8 の見出し番号はコード内コメント（`§5.2 違反`・`§5.2.1④` 等）から参照されている。
> 振り直さないこと。**

---

## 0. セッション開始時にやること（**必ず裏取り**）

```powershell
cd C:\MyProj\LilySharp
git log --oneline -8
git rev-list --count origin/master..master     # 未 push 数
git status --short
dotnet build LilySharp.Core\LilySharp.Core.csproj --no-incremental -v q
dotnet test  LilySharp.Tests\LilySharp.Tests.csproj -v q 2>&1 | Select-String 'Passed!|Failed!'
```

⚠️ **このドキュメントも memory もコード内コメントも、書いた時点のスナップショット。**
HEAD・テスト数・シンボル名・「完了」表記は開始時に実コードで再確認する。
過去の引継ぎでは stale な記述を毎セッション複数踏んでいる（§5.2）。

---

## 1. 現在地 ← **毎セッション書き換える**

最終更新 2026-07-24 / HEAD ＝ `255f494f` の上にこの docs コミット（⚠️ 自己参照。**§0 で裏取り**）
/ 未 push 52 本。**3274 passed / 0 failed / 3 skipped**・Core 0 warn 0 err・**LP 忠実度
57/75 exact・total |residual| 0.806267 ss**。
新点 3 つ（`line-start.clef-to-time.tab`／`tab.staff.line-span.{six,four}-string`）は
**いずれもこのセッションで開いて閉じた**ので total は不変。snapshot は tab の
23 枚（clef）＋ 6 弦 5 枚（弦間隔）を再ベース済み。
⚠️ clef アンカー（`6878c0db`）の方が byte 不変なのは**結果であって構成ではない**——
打楽器 clef と pitched clef が同居する fixture がコーパスに無いだけ。

⚠️ **total を過去の値と直接比べない**（点集合が違う）。0.006267 → 0.806267 の増加は**悪化ではなく、
新しく開いた 2 点（tab-key 対）の正直な残差**＝下の ▶ が正体。**増加は「誰も測っていなかった
乖離の可視化」であることが多い**（§5.2.1④）。

**非ゼロで残っている台帳点**（これが仕事の全リスト）:

| 点 | 残差 | 正体 |
|---|---|---|
| `line-start.time-to-first-note.tab-{concert,keyed}` | 両 **+0.400000000** | ▶ 譜間 merge_springs 未移植 |
| clef sliver（Y 4 点） | 各 0.0001〜0.0008 | LP の実効 scale 未特定（§2C） |
| Pango 量子化の族（tuplet 4 点・強弱 1 点・tie 1 点ほか） | 1e-4〜1e-6 | Lily# に無いテキスト metric＝**閉じる予定の無い名前付き残差** |

### ▶ 次の一手 ＝ **譜間 merge_springs（行頭 spring の平均化）を移植する**

**点は開いている・モデルは実測で確定済み**（probe `TM3`/`TM4` と
`probes/line-start-mindist.ly` の `TKC`/`SKC`/`TKA`＝いずれも台帳点ではなくモデル照合）。
LP の行頭 spring は `Spacing_spanner::breakable_column_spacing`（`spacing-spanner.cc:478-517`）が
**譜ごとに 1 wish を集めて `merge_springs`（`spring.cc:104`＝ideal の単純平均・min は max・
床 min+0.3）**。**前置の最後の grob が譜ごとに違うときだけ効く**：記譜譜＝TimeSignature
（semi-shrink 2.0→ideal 8.82）／tab 譜＝Clef（minimum-fixed 5.0→6.0 が共有 min_dist 床で 8.02）
→平均 8.42。**通常譜 2 段では wish が等しく平均＝自分自身なので 3.700000 のまま＝既存の多段譜の
点では原理的に見えなかった regime**。Lily# は `SpacingRules.FirstNoteSpring` の**系全体 1 本**で
per-staff wish が存在しない＝+0.4。

#### ⚠️ critical path は「平均化」ではなく **`min_dist`**（2026-07-24 に LP ソースで確定）

`min_dist = Paper_column::minimum_distance(l, r)`（`paper-column.cc:145-164`）＝
**2 つの PaperColumn の `horizontal-skylines` 同士の距離**（`Separation_item::conditional_skyline`
込み）。tab 譜の wish は `max(6.0, 0.3 + min_dist)` で、**この床が binding している**。

⚠️ **平均化だけ先に入れてはいけない**——tab wish が 6.0 のままだと平均 7.41＝TIME→HEAD **0.59**
（LP 3.30・現状 3.70）で**今より悪化**する。「計算できる譜だけ床を適用する」は §5.2 の
**byte 一致細工の禁止**そのもの。**順序は min_dist が先。**

#### ⚠️ min_dist は **箱の union**（outline ではない）← 2026-07-24 に実測で確定

**列のスカイラインは grob の extent を esw/esh で膨らませた矩形の union。**
`horizontal-skylines` = `ly:separation-item::calc-skylines`（`define-grobs.scm:2523`）＝
`Separation_item::boxes`（`separation-item.cc:120-190`）で、読むのは
`il->extent(pc, X_AXIS)` と `pure_y_extent` **だけ**。グリフの outline はどこにも出てこない。

プローブ `audit/lp-geometry/probes/line-start-mindist.ly` が実測（model check・台帳点ではない）:

| | 実測 | 内訳 |
|---|---|---|
| SKC（記譜1譜） | **7.485000** | TimeSignature 右 6.585＋esw 0.8 − (NoteHead 左 0＋esw −0.1) |
| SKD（同・調号あり） | **10.135000** | 同上（**調号は shadow**＝時値記号の右の方が更に右） |
| TKC（記譜+tab） | **7.720000** | SKC ＋ **TAB clef が Clef 列を広げる 0.235** |
| TKA（第1音に ♯） | **9.270000** | 7.620 − (Accidental 左 −1.450＋esw −0.2) |

前置列の RIGHT スカイラインは **building 全部 x 一定**（3.465＝記譜 clef／3.700＝TAB clef／
7.620＝記譜 timesig）。G clef の outline なら曲線を追って数十 building になる＝**箱で確定**。
臨時記号は `elements` に居らず（`paper-column-engraver.cc:259`）
**`conditional_skyline` 経由でしか min_dist に届かない**（Scheme から呼べないので 9.27 は
箱から再構成）。

⚠️ **よって「clef 族／C・数字／TAB clef の実 outline を bake する」は不要＝発明。**
LP より精密なのは、粗いのと同じ欠陥（§5.2）。島2 の outline 資産が要るのは
`position_apes` の側だけ。

**前置の箱の Y は「自分の譜の縦幅」だけでは足りない。** `item::extra-spacing-height-
including-staff`（StaffSymbol まで伸ばす）**と** `pure-from-neighbor-interface::
extra-spacing-height`（**隣接列の grob＝第1音列そのもの**まで伸ばす。
`pure-from-neighbor-engraver.cc:110-137`）の pairwise (min,max)。
⇒ **前置の箱は必ず第1音列を縦に覆う**ので、行頭の min_dist は音高に依らず、
**譜をまたぐ Y の問題も起きない**（esh が 1譜と 2譜で同一＝neighbours は譜内、実測）。

#### ✅ 手順1は完了（`effeabc3`・出力不変・snapshot 無変更）

`LineStartColumn`（`Svg/Layout/LineStartColumn.cs`）＝箱の列 → `HorizontalSkyline` 距離。
`LineStartColumnTests` が上の 4 数と 6 桁一致。入力は全部 Lily# 自身のメトリクス。
`clefs.tab` は生成メトリクス入り（LILC bbox **(0.200 . 2.800)**＝LP の列相対 1.0..3.6 と一致）。
⚠️ Clef 列が取るのは **原点→ink 右（2.800）** であって ink 幅（2.600）ではない
（LP は打楽器 clef と違い TAB clef を列へシフトしない）。
`BoundaryColumn` と同族＝**列モデルを 2 つに増やさないこと**。

#### ✅ clef 列のアンカー規則は移植済（`6878c0db`・出力不変＝結果であって構成ではない）

手順2-1（`MaxClefWidth` に tab を）に着手したら、**その下に別の欠陥**が出た:
LP の break-align は `- extents[group][LEFT]`（`break-alignment-interface.cc:242`・
group extent は譜の union）でずらす＝**グループの ink 左が 0.8 に載り、各 clef は
その中で自分の stencil offset を保つ**。Lily# は**各 clef 自身の ink 左**を 0.8 に
載せていた。両者は「1 種類の clef しか無い系」では一致する（全 pitched も、
打楽器**単独**も）ので何年も見えなかった。**混ざった系だけが判別する**:

| probe | LP 実測（予測どおり） |
|---|---|
| CGP 打楽器+treble | 打楽器 clef ink **1.470000**..2.800（0.8 に揃わない）／treble 0.800..3.365 |
| CGT tab 単独 | Clef 原点 **0.600000**・ink 0.800..3.400 |

`SpacingRules.ClefGroupExtent` へ移植し `DrawClef` が消費。`MaxClefWidth` も
group extent（`Right − Left`）に書き換え——**現状の clef 集合では全部これまでと同値**
（`ClefGroupWidth_OfOneClef_IsThatClefsInkWidth` が固定）なので予約は動かない。

#### ✅ 手順2-1 完了（`0829185b`・**tab snapshot 23 枚 再ベース済み**）

台帳点 `line-start.clef-to-time.tab`（LP **4.320000**・対照 treble 4.085000）を起こして閉じた
（Lily# 4.085000 → 4.320000）。tab 譜が clef グループに入り、`clefs.tab` は**無スケール**で
グループのアンカーに描かれる（＝予約幅＝描画幅）。LP 実測での裏取り: TabStaff は
`staff-space 1.5`、clef は **5.760000 高・無スケール・中央線中心**で、**4 弦譜（4.600000 高）
からは上下 0.58 はみ出す**（probe `CG4`。予測を先に書いて的中）。

✅ `TabStringSpace` も LP の 1.5 に統一済み（`255f494f`・6 弦 snapshot 5 枚 再ベース）。
tab 譜まわりで残る乖離は**フレット数字のサイズだけ**で、それは §3 の批准済み乖離。

**残り＝手順2**:
2. `LineStartColumn` を spring 生成へ配線し、fixed を `0.3 + min_dist` で床にする
   （`staff-spacing.cc:213`）。Y は `LayoutStaffGroups(score)` の score だけ取る
   オーバーロード（`MultiStaffLayouter.cs:278`）＝LP の pure 側と同じ構造で取れる
3. per-staff wish ＋ `merge_springs`
4. snapshot 再ベース＋承認

##### 手順2-2 に着手する前に読むこと（2026-07-24 に設計まで詰めた）

**① 先に「第1音列の箱が esw を持つ」を作る。** LP の `boxes()` は grob ごとに
`extra-spacing-width` を足す（notehead −0.1／Accidental **−0.2**）。Lily# の
`ItemSkylineFactory.CreateLeftSkyline` は**生の ink しか作らない**ので、そのままでは
min_dist の臨時記号ケースが 0.1 ずれる。「臨時記号付きの第1音は稀だから」で流すのは
§5.2 の byte 一致細工そのもの。**esw を運ぶのは note spacing 側とも共有の関心**なので、
`ItemSkylineFactory` に持たせる（＝1 モデル）のが正しい入口。

**② LP と Lily# で spring の意味が 1 個ずれている。** LP は
`Spring(ideal, min_dist)` ＝ **spring の min_distance は列間距離そのもの**で、
`fixed` は `inverse_compress_strength = ideal − fixed` の形でしか効かない。
Lily# は今 `Spring.MinDistance` に **LP の `fixed` に当たる値**を入れている。
`Spring` には 4 引数コンストラクタ（compress 明示）が既にあるので表現はできる。
**この意味のずれを直さずに床だけ足すと、床が min_distance に化ける。**

**③ 予測（実装前に書いた・反証可能）**: 床は **force 0 の出力を動かさない**。
SKC は fixed 7.585→7.785 に対し ideal は max で 8.585 のまま、TKC も ideal 8.82 のまま
＝台帳の `line-start.*` も tab 2 点（+0.400000）も**不変のはず**。動くのは
`inverse_compress_strength` だけ＝**圧縮された行だけ**。外れたら②の取り違えを疑う。

⚠️ `SkylineBuilder.SeedClef` の X は今も原点..Right（グループ ink ではない）。
縦スカイライン専用で位置には効かないので**半端に移さず注記のみ**（コード内にも記載）。

⚠️ **min_dist は単一譜でも効く**（SKC で実測 **7.485000**）。fixed 7.585 に対し床
0.3+7.485=7.785＝**床が binding**（ideal は max で 8.585 のまま＝KCS 台帳 3.700000 と一致）。
つまり ideal は動かないが **fixed と compress 強度が動く**＝force 0 の点は不変でも
**圧縮 regime で出力が動く**。**多段譜だけの話ではない。**

⚠️ `AllStavesTab` の `TabClefToFirstNoteSpace = 1.5`（`MultiStaffLayouter.cs:53-58`）は
「TAB clef は小さいから 5.0 は要らない」という**明示的な Lily# 独自判断**だが、
**LP の TAB clef ink は G clef より広い**＝前提そのものが崩れている（`0829185b` で照合済み）。
LP は 1譜 tab でも Clef の `minimum-fixed-space 5.0`（fixed = 1.0+max(2.6,5.0) = 6.0）＋床。
**手順2-2 で置き換わる側。**
**移植で置き換わる側**なので、TAB clef ink を測る時に一緒に判定する。

⚠️ **出力は tab/ossia を含む全スコアで動く**（第1音が ~0.4 左へ）＝**snapshot 再ベース＋
ユーザー承認**が要る。

---

## 2. 開いている作業

### A. 予約と描画・複数モデルの統一（▶ と同じ族）

LP には break-align モデルが **1 本**しか無い。Lily# に**同じ量を計算する場所が 2 つ以上ある**なら
それが次の欠陥の住所（§5.2.1②）。現在わかっている残り:

- ~~`MaxClefWidth` の staff 集合~~ — `0829185b` で完了（台帳 `line-start.clef-to-time.tab`）。
- ~~tab 譜の弦間隔~~ — `255f494f` で完了（台帳 `tab.staff.line-span.{six,four}-string`）。
  LP の 1.5 に統一。フレット数字の**サイズ**は §3 で批准した意図的乖離＝**戻さない**。
- **prefix 幅の第3のモデル＝`MultiStaffScore.LeadingKey`** — `LayoutEngine` /
  `SystemBreaker` / `IncrementalCompiler` の 3 経路が **score.KeySignature をそのまま**使い、
  per-staff key も「調号を彫る譜」も見ない。`transpose-multistaff`（score=C major・上譜 D major）
  で**改行器の予約が実レイアウトより 2.2 狭い**。**出力（改行位置）が動くので対を起票してから。**
- **break-align 描画 walk の純構造化** — `sharedKeyX`/`sharedTimeX` の手組み max ループを
  `SolvePrefixColumns` 消費へ。値は一致済（出力不変）だが、**予約側は score モデル＋measure 走査、
  描画側は `ResolveKeySignature`＋`GetSystemStartKeyChange` と key 解決経路が別**——
  **この解決経路の統一が本丸**で、片方だけ挿げ替えると多分岐で壊れる。急がず focused session で。
- **ossia 自身の key が全記譜譜より広い regime** — 幅 union には入れた（LP どおり scaled stencil）が
  corpus に fixture が無く**未測定**。踏む対を起票する価値はある。

### B. スカイライン／beam の未測定領域

いずれも**先に LP を dump して対で起票**（発明回避）。アーキ上の不利は無いと確認済み。

- **同一譜 knee の実 ink seed** — LP は同一譜 knee の Beam/Stem stencil を skyline に入れる
  （cross-staff だけが除外＝`axis-group-interface.cc:850-858` の LP 自身のコメント）。
  Lily# の `OuterEdgeStaffSpaceAtX` は対称モデルで stack の内側面を言えない＝
  **LP の knee 帯（Beam ext）を dump してから両面 seed**。現行の固定 stem は観測不能な非忠実。
- **`BuildSystemSkylines` の全譜 union** — LP の `build_system_skyline`
  （`page-layout-problem.cc:1075-1124`）は**全譜**を offset 付きで union。Lily# は先頭/末尾譜のみ＝
  内側譜の ink が edge 譜の silhouette を突き抜ける regime で乖離。3 譜 probe を対で。
- **cross-staff beam 機能そのもの** — `BeamMember.TargetStaffIndex` を立てる producer が皆無で
  `IsCrossStaff` は到達不能（`@cross` は描画側にしか流れない）。skyline 方針（＝LP は除外）は
  `72905813` でピン済み。**機能が届いてから** E2E の対を起票する。
- **mid-line clef change の origin** — 行頭 clef で閉じた origin ズレ（percussion）と同型の疑い。
  台帳点が無いので未着手。

### C. 保留＝先に LP を instrument する必要があるもの

- **clef の LILC-vs-skyline sliver（Y 4 点）** — LP の実効 scale が **0.004**、Skia 直読みは
  **0.003989**（0.27% 差）。差は LP 内部の `get_unscaled_indexed_char_dimensions × magnification`
  由来で **SKPath からは出せない**。フォントは byte 一致なのでフォント差ではない。
  ⚠️ **SKPath の値に定数を合わせるのは fitting＝§5.2 違反。** LP を instrument して
  `get_glyph_outline_bbox` / magnification を dump するのが先（payoff は小さい）。
- **スラーの `move_away_from_staffline` 未移植**（`slur-scoring.cc:640-658`）＝端点が五線の線上
  （±0.2）に落ちると 0.15ss 外へ弾く。既存の点では発火しない＝**端点が線に載る fixture を対で**。

### D. Y 軸（ページ縦）の残り

- **圧縮 regime は未実装**（順序は「余白 → 圧縮」。入れるときは**圧縮が実際に走るケース**
  ＝`SystemsPerPage` 強制などを同時に用意する。過去に PASS 2 を両方向 solve へ替えて測ったら
  1 バイトも動かなかった＝当時は伸長側しか選ばれていなかった）
- **LP の top spring はページ justify で伸びる**（`spring.cc:213-216`）が Lily# は先頭 system を固定＝
  **伸長 regime に着手するとき最初に触る乖離**
- **`PageLayouter` は systemDetails の `i == 0` で `vs.SystemSystem`、配置側は `vs.TopSystem`**＝
  ブレーカーと配置で spec が食い違う（本数見積りにしか効かない）
- **`LayoutEngine` の単一ページ経路が今も自前で積む**（force 0 なので鎖と一致するが二重実装）
- **Y コーパスの拡張**（`page.top-margin` / `page.bottom-margin` / `page.last-page-gap` 等）

### E. 未移植の LP 計算・座標系の島2

- **未移植 LP 計算**: tuplet on-line / volta shorten / hairpin niente / ledger / brace / 開 chord /
  Ignatzek。出典 `HANDOFF-lp-calc-incorporation.md`（§8）。**伝聞なので着手前に実コードで裏取り。**
- **座標系の島2（device 島群）は繰延**: TieVariant / 水平 skyline の Y horizon / TabStaffGeometry /
  beam collision island。`StaffOffsetInSystemDown` の残り呼び出しは**意図的な device 境界＝消さない**。
  島1 が残した手順: ①格納を反転する前に格納値を主張するテストを書く ②生産側は全部同時に
  ③**device 島の縁では 1 回だけ反射する**（反射を島の内側へ押し込まない）。

### F. 言語・ツール側（X/Y とは独立・**一覧は伝聞。着手前に実コードで確認**）

- MusicXML インポート — ほぼ完遂、**実ファイル検証が残**
- AI 協調編集 M1–5 — **実機 E2E 未検証**
- 文法改善 5 件は完了。**0.3.0 リリースは GO 待ち**
- `override` の消費側は 4 つだけ（文法側は元から開いている）。⚠️ **値に小数リテラルが書けない**。
  ⚠️ **page 系（`paper-height`/`top-system-spacing`/`systems-per-page`）を `override` に載せない**——
  LP ではそれらは `\paper` 変数であって grob プロパティではない（コーパスはハーネス引数で解決済み）
- **chords 行 / lyrics 行が `PartReferenceFinder` に無い** — part 参照が検証も改名もされない。
  足すと「未定義の chord/lyrics part を参照するスコアを新たに弾く」挙動変更になる＝要判断
- **対応の取れないスラーが無警告で消える**（`SlurDetector.cs:49-56`）＝タイの LYS4007 相当の警告を。**別ブランチで**
- `editors/vscode/src/smartBrackets.ts` → `smartTyping.ts` 改名（実態から名前が離れた）
- Dead-code 監査の手動分 / `LILYPOND-REF` 行番号の一括再採番（cosmetic）/
  `IDrawingContext.cs:37-39` の remark が装飾前後2フレームを記述していない

### G. 保守性の負債・未 commit のプローブ

- `DrawingTransform.Identity` は `new()` なので **`ScaleX/ScaleY = 0`**（record struct はプライマリ
  コンストラクタの既定値を適用しない）。出荷 3 backend は無害だが記録用コンテキストの作者を
  2 人捕まえた。`Identity => new(0,0,1,1)` に直す価値あり（要判断）
- 記録用コンテキストが **2 実装**（`SharedRendererBeamTests` と `LpFidelity/RecordingDocumentContext`）
- `GlyphMetrics.RestMaximaWidth = 1.8` が**手動値**。フォントメトリクスなので、生成器が `rests.M3` を
  出すようになったら `GlyphMetricsGenerated.cs` へ
- `SystemBreaker.BreakIntoSystemsGreedy` は **MMR run 非対応**。ただし `UseOptimalLineBreaking` が
  既定 `true` なので**既定出力に影響しない**うえ、greedy は LP のアルゴリズムでもない
  （LP＝`constrained-breaking.cc`＝optimal）＝**忠実度は上がらない**。優先度低
- ⚠️ **LP 検証の数値がコメントにだけ残り、プローブが未 commit** の 2 件（コーパスの「再実行可能」
  原則から外れている。次に触るとき `audit/lp-geometry/probes/` へ移す）:
  **stretch strength 0.45 の検証**（数値は `SpacingInvariantTests.BarlineToFirstNoteSpring_…` に）と
  **符尾 Y extent のダンプ**（数値は `SpacingRules.BarlineToNextNotesCorrection` の remarks に）

---

## 3. 決定済み ← **蒸し返さない**

| 決定 | 根拠（要点） |
|---|---|
| **`SystemBreaker` の再入可能化は入れない** | LP はページブレーカーが行分割を選ぶ（`optimal-page-breaking.cc:139-173`）が、入れると F3 の tier-1 skip の健全性論拠が壊れる（break 解が縦の関数になり、gate を計算するのに gate が守る結果が要る＝循環）。⚠️ **判断し直すなら順序は「①まず頻度を測る（コード変更ゼロ）→ ②有意ならオプション分離＋一致不変条件テストとセットで」**。性能が理由ではない |
| **臨時記号の糖衣 `c?` / `c!` / `c??` は入れない** | `!` は点線小節線トークン。`c?` 単独では `!` の罠への導線を作る。痛みは `@courtesy`/`@editorial` の専用エラーで解消済み |
| ★ **記号（sigil）は LP が既に記号で綴っているものにだけ使う。Lily# 固有は全部 `@name`** | 上の決定から出た一般則。今後の記号追加はこれで判断する |
| **休符の実インク化はやらない** | 実測で棄却。休符は中央線に座るので縦インクが極値にならず、LP でも 1 ビット違わない＝箱が名目なのは事実だが**不活性** |
| **単一ページは紙面サイズにしない**（意図的乖離） | Lily# は 1 ページに収まるスコアを内容サイズで出す（明示的な設計）。台帳に載せると total が ~109 になり指標が壊れるので**載せない** |
| **本数（count）の点は ss の総和に入れない** | 距離ではないから（`unit` フィールドで分離） |
| **LP の「正」は 2.26.0** | 版で PUA コードポイントも Emmentaler も動く。**必ず feta 名で引く** |
| **cross-staff beam は skyline から除外**（LP の字面） | `axis-group-interface.cc:850-858` の LP 自身のコメント。Lily# の「固定 3.5 stem を残す」は発明だった |
| ★ **タブのフレット数字を LP より大きく描くのは意図的乖離**（ユーザー判断・2026-07-24 明示） | LP のタブ数字は小さくて読みにくい。Lily# は `TabConstants.FretFontSize = 2.6`（単数字幅 1.625・高さ 1.7875）＝LP の TabNoteHead 幅 0.990155 の約 1.64 倍。和音で数字が被る問題は**じぐざぐ配置**（`SpacingRules.ApplyTabChordSpacing` ほか）で解いてある。**「LP と違う＝発明だから消す」で削らないこと。** ⚠️ 弦間隔（`TabStringSpace`）は別の話で、そちらは LP の 1.5 に揃える |

---

## 4. 知識の置き場所 ← **増殖防止の核心**

**引継ぎ文書が増えたのは、寿命の違うものを1つに混ぜたから。** 種類ごとに置き場所を決める。

| 知識の種類 | 置き場所 | 例 |
|---|---|---|
| **LP の幾何の実測値** | `audit/lp-geometry/`（プローブ＋台帳） | bar line → 符頭 = 0.900000 |
| **LP の式・定数の出典** | **コード内 `// LILYPOND-REF:`** | `staff-spacing.cc:213` の 0.3 |
| **LP の挙動で驚いたこと** | コード内コメント（数値つき）＋ user memory | 光学補正は clef でなく符尾 |
| **座標系の現状と残作業** | `docs/COORDINATE_AUDIT.md` | §4.5 の対処状況表 |
| **アーキテクチャの意図** | `docs/*.md`（既存の該当ファイル） | `SKYLINE_ARCHITECTURE.md` |
| **不変条件** | **テスト**（`SpacingInvariantTests` 等） | 両 spring 系の一致 |
| **現在地・次の一手・開いている作業** | **このファイル §1–§3** | |
| **過去のセッション記録** | `docs/HANDOFF-ARCHIVE.md`（§8） | 閉じた欠陥をどう測ったか |
| **ユーザーの好み・作業規律** | user memory | 「done は push 済みで」 |

**ここに書かない**: LP の式の導出、実測値の生データ、アーキテクチャ解説、コードの説明。
それらは上表の置き場所へ。このファイルには**ポインタだけ**置く。

> 判断に迷ったら: 「これは**次のセッションだけ**必要か、**ずっと**必要か？」
> ずっと必要ならこのファイル以外へ。

---

## 5. 恒久ルール（滅多に変わらない）

### 5.0 進め方の型 ← **8 セッション連続で成立している。迷ったらこれ**

1. **点を対で起票する**（コード変更ゼロ・出力不変の commit）。
2. **予測を先に `why` へ書く**（実装前に。反証可能にするため）。
3. **移植する**（LP の式だけ・§5.2）。
4. **対の食い違いが第2の欠陥を出す**。

各ステップに、繰り返し噛まれてきた理由がある:

- ⚠️ **必ず「対」で作る。** 片側だけだと予測が当たってしまい、**欠陥ごと出荷する**。
  対の設計で最も強いのは **LP 側が恒等になる対**（同じものを二通りに書く／LP が読まない差だけを
  変える）——LP の差が 0 なので、Lily# 側の差が**そのまま欠陥の量**になる。
- ⚠️ **予測が外れたときこそ収穫**。外れの方向が真因を指す。当たったら移植の照合になる。
- ⚠️ **穴を開けるまで、そこに何が溜まっているかは分からない。** 点を開く／種を入れると、
  狙っていた欠陥と**一緒に狙っていなかった欠陥が落ちる**（これまで一度の例外もなく起きた）。
  だから **control（対の基準側）が非ゼロで開くのは正常**——それは持参金であって失敗ではない。
- ⚠️ **`exact` は「正しい」ではなく「その regime では動かない」かもしれない。**
  新しい点は**既存の点が測っていない regime**を優先する。
- ⚠️ **床に座らせない。** 距離が spec の下限に張り付く配置では、両側 exact になって**何も測らない**。
- ⚠️ **probe が何を測っているか確かめてから信じる**（別の grob を測っていた事例が複数）。

### 5.1 ワークフロー規律

- **master 直コミット。ブランチを勝手に作らない**（作成・削除は GO 待ち）
- **1 島 / 1 関心 = 1 commit**。ただし**依存があるなら同時投入**し、message に
  「単独では入れられない」と書く（frame と定数のように、片方だけだと壊れるケース）
- **巨大ファイルを分割しない**
- **Co-Authored を付けない**。message に「何を・なぜ・**検証結果の数値**」＋
  **未完・残差・意図的に触らなかった点**を明記
- コミットは**関係ファイルのみ明示 `git add`**（無関係の `.py` / handoff を混ぜない）
- **push はユーザー。「done」は push 済みでのみ主張。ship = 全緑 ＋ 明示承認**
- **出力を変える変更はユーザー承認前に出荷しない。** snapshot 再ベースも
  **LP 照合 → 承認 → 実行**
- **シェルは pwsh MCP / ripple（bash 禁止）**。ファイル書き込みは Write ツール
  （`Set-Content` 直書き禁止。**PowerShell に heredoc は無い** — commit message は
  ファイルに書いて `git commit -F`）
- **「未使用に見える」≠「消してよい」。** 削除前に `.cs` 以外も横断 grep →`<see cref>` 確認
  → 削除後にヘルパが孤立しないか再 grep →**ユーザー承認**

#### ★ シンボルのリネームは**ユーザーが MSVS で行う**（Claude は自分で改名しない）

クラス・メソッド・プロパティ・フィールドなど**名前を変えたくなったら、ユーザーに依頼する**。
ユーザーが Visual Studio のリファクタ機能（F2）で実施する。**Claude が自分で置換しない**
（grep 不可視の消費者 — `<see cref>`・XML doc・テスト・文字列 — を取りこぼすため）。

**依頼するときに伝える 4 点**:

| | |
|---|---|
| ファイルパス | `C:\MyProj\LilySharp\...\Foo.cs` |
| 行番号 | `123` |
| 現在の名前 | `OldName` |
| 新しい名前 | `NewName` |

⚠️ **オカレンスは 1 箇所だけ伝えればよい。** そこから全オカレンスが一括で改名される。
複数箇所を列挙する必要はない。複数シンボルを頼むときは上の 4 点を 1 行ずつ並べる。

⚠️ **MSVS のリファクタは「コメント内のシンボル名」を取りこぼすことがある。**
改名後に Claude 側で `grep -r '<旧名>'` を掛け、コメント・XML doc（`<see cref>` は追随するが
`<c>Foo</c>` や地の文は追随しない）・`.md`・`.ly` プローブのヘッダ・
コミットメッセージ用のメモに残った旧名を拾って直すこと。**改名依頼とセットで必ず行う。**

⚠️ 新規シンボルの追加・不要になったシンボルの削除は対象外（通常どおり Claude が行う。
削除は上の「未使用に見える ≠ 消してよい」に従う）。ローカル変数も対象外。

### 5.2 LP 移植の原則

> ## ★★ 実測値に基づいてコードを直すことは**禁止**（ユーザー指示・2026-07-22 再確認）
>
> **コードに入るのは LP の `lily/*.cc` / `scm/*.scm` の式だけ。**
> LilyPond の出力から読んだ数値でコードを書いてはならない。可能な限り**字面通りに移植**する。
>
> 実測の役割は 2 つだけ:
> 1. **欠陥を見つけるヒント**（どこがズレているかを知る）
> 2. **移植の照合**（LP の式から導いた値が実測と一致するかを確かめる）
>
> ⚠️ **「実測に合う定数を選ぶ」は 1 も 2 も満たさない。** 合ってしまうので気づきにくい。
> ⚠️ **判定法**: その値を **LP のどの関数のどの行から導いたか**をコメントに書けるか。
> 書けないなら実測を貼っただけ。`LILYSHARP-OWN:` を付けて独自と明示するか、式を探しに戻る。
> ⚠️ **偶然一致に注意**: 2026-07-22 の強弱記号では、誤った frame（五線の中心 2.0 ＋
> staff-padding 0.1）と誤った定数（名目 descent 0.64）が**打ち消し合って** LP 実測の
> 1.342 を 0.002 以内で再現していた。実測に合わせて選んだ定数は、こういう形で
> **2 つの欠陥を固定する**。
>
> ★ **これは何度も繰り返されている指摘。破ったら差し戻し。**

> ## ★★ byte 一致を維持するための細工は**禁止**（ユーザー指示・2026-07-24 再確認）
>
> **目標は出力の一致ではなく、LP の内部ロジックの完全模倣。**
> 出力を動かさないために、LP に無い分岐・除外・条件・近似を入れてはならない。
>
> ⚠️ **判定法**: その分岐／除外／条件は **LP のどの行にあるか**。答えられないなら、
> それは移植ではなく byte 一致を守るための細工。
>
> よく出る形（全部やってはいけない）:
> - **両側から同じものを除外して「一致した」と言う**（LP では両側に在る）
> - **既存 snapshot が動かないほうの実装を選ぶ**
> - 「今のデータ構造では LP の量を表現できない」を**除外の理由にする**
>   （表現できるように直すのが移植。2026-07-24 の ossia 幅がこの形で差し戻された）
>
> ⚠️ **byte 不変は「結果」ならよいが「構成」にしてはいけない。** 報告・commit message でも
> **どちらなのかを書く**（例:「corpus が byte 不変なのは結果であって構成ではない——
> 既存 fixture がその regime を踏まないから」）。
>
> ⚠️ 裏面も同じ強さで成立する: **観測可能な差が無いことは、字面移植をしない理由にならない。**
> 今は差が出なくても、regime が広がった日に欠陥として現れる。
> ⚠️ 同様に **byte 不変は移行が正しい証拠にもならない**（打ち消し合っていても byte は動かない）。

- レイアウト/描画は `C:\MyProj\lilypond-src` の `lily/*.cc` を**符号一致で字面移植**。
  関数名・変数名・符号・丸めまで揃える。**独自の近似・辻褄合わせを入れない**
- **移植したら必ず `// LILYPOND-REF: lily/xxx.cc:行` を付ける**（定数1つ、式1つでも）
- **座標系が揃っていなくて字面移植が難しいときは、勝手に変換して押し込まず報告する**
- **既存の移植を先に探す。**「未実装」でなく「書いてあるが呼ばれていない/引数が違う/
  frame が違う」ことが本当に多い
- **分岐は全部書く**（`space-alist` の型を無視して値だけ使う類の手抜きをしない）
- ⚠️ **doc / コメント / 過去の自分の結論を疑う。ただし「疑った結果」も裏取りする。**
  `LILYPOND-REF` が付いていても式が一致しているとは限らない
- ⚠️ **同名プロパティが grob ごとに別の値**を持つ（`stem-spacing-correction` は
  StaffSpacing 0.4 / NoteSpacing 0.5）。**単位も別**（staff-spacing.cc は staff-space、
  note-spacing.cc は staff position。どちらも /7 するので2倍ずれる）

### 5.2.1 発明を機械に見つけさせる（3 つの仕組み）

**このセッションで見つけた欠陥は 1 件の例外もなく「Lily# が発明した箇所」だった**
（padding 4 倍・`/60`・名目 1.0 の箱・鎖の欠落・clef の欠落）。
「LP の式を正しく移植したが合わなかった」は**ゼロ**。だから方針は正しい。
問題は**発明が何年も気づかれない**ことで、散文の警告では防げなかった。以下は機械が落とす形。

**① 出典の無い定数はテストが落とす** — `LpProvenanceTests`（`UnsourcedBaseline`）。
`EngravingDefaults` / `LayoutOptions` / `VerticalSpacingParameters` の数値定数は
**`LILYPOND-REF:`（LP の式の出典）か `LILYSHARP-OWN:`（Lily# 独自である理由）を必ず持つ**。
台帳と同じラチェットで、**下げるのは可・上げるのは不可**。
⚠️ **初期値 13 は実在の負債**。`NoteheadHeight` `NoteheadHalfHeight` `RestHeight` `RestWidth`
——**今回噛まれた名目の箱がそのまま並んでいる**。1 つ潰すたびに baseline を下げること。
⚠️ **`LILYPOND-REF` が付いていても式が一致しているとは限らない**（§5.2）。
このテストは「出典を書け」までしか強制できない。**REF の隣の式が別物だった実例が
`LayoutEngine` の padding 4 倍**（`page-layout-problem.cc:1070-1127` の真下にあった）。

**② 二重実装は「一致する」不変条件テストで縛る** — spring 2 系統＋改行 gate は既に §5.4 で
縛ってある。**縦も同じ扱いにする**。⚠️ **padding 4 倍のバグは「複製された側」（単一ページ経路）に
住んでいた。** 重複は美観ではなく**移植が半分しか当たらない場所**。新しい経路を足すときは
「既存の経路と同じ答えを出す」テストを同時に足すか、**そもそも足さず統合する**。

**③ 再ベースは台帳点とセットにする** — snapshot を再ベースするコミットは、
**その差分を正当化する台帳キーを message に名指しする**。名指せないなら、
**先に台帳点を作る**（適用例はアーカイブの `StaffSymbol` ±2.05）。
⚠️ 190 snapshot に対し台帳は 29 点。**snapshot は「前回の自分」との比較**なので、
再ベースのたびに誤りを承認する機会が生まれる。網は見た目より薄い。

**⑤ 実測値をコードに書かない。書くのは LP の式。** ★このセッションで指摘された癖
実測は**不具合を見つけるヒントと、移植の照合**にだけ使う。**コードに入るのは LP の
`lily/*.cc` / `scm/*.scm` の式**であって、LilyPond の出力から読んだ数値ではない。
- ⚠️ **判定法**: その定数を**LP のどの行から導いたか**を書けるか。書けないなら、それは
  実測を貼っただけ。`LILYSHARP-OWN:` を付けて**独自と明示する**か、式を探しに戻る。
- ⚠️ **導出形で書く**。`2.05` ではなく `staffHeight/2 + StaffLineThickness/2`。
  数値が同じでも、**前者は測定の写しで、後者は式**。フォントや設定が変われば差が出る。
- ⚠️ **既にある独自定数を別ファイルにコピーしない。** 実例: clef のスカイラインを入れたとき
  `ClefGlyphXOffset = 0.3` を `SkylineBuilder` に**書き足した**。0.3 は LP の量ではなく
  `DrawClef` の独自値で、**発明の家が 1 つから 2 つに増えた**。`EngravingDefaults` に
  1 つだけ置き `LILYSHARP-OWN:` を付けて解消済み。**`SystemSpacing * 0.5` が何年も
  生き延びたのと同じ形**。

**④ コーパスの穴を数える** — 台帳が薄いのは~~多段譜~~・~~ページ跨ぎ~~・**伸長**
（多段譜は `b3cfb119`、ページ本数は `920cf4dc` で入った）。
⚠️ **2026-07-22 に「点が exact でも regime に入っていないだけ」という新しい形が出た**:
ページ本数の点を A4 に足したら最初から exact で、測ってみると**A4 では本数を容量が決めていない**
（第1システムを 4 オクターブ上げても LP は 13 のまま）。**紙面を縮めて初めて binding した。**
**exact は「正しい」ではなく「その regime では動かない」かもしれない。**過去の最大級の欠陥 2 つ（padding 4 倍・clef 欠落）は
**長期間コーパスから見えず**、clef が padding を偶然あぶり出して初めて露見した。
**新しい点を足すときは、既存の点が測っていない regime を優先する。**
⚠️ **2026-07-22 に同じことがもう一度起きた**: 多段譜の regime を開いた瞬間、
狙っていた欠陥（五線 0.05）と**一緒に、狙っていなかった欠陥（幻の符尾 1.45）が落ちた**。
**穴を開けると、そこに何が溜まっていたかは開けるまで分からない。**

### 5.3 測定の原則

- **推論せず測る。** 実測 → 予測との照合 → 一致しなければ**まず自分の当てはめを検算**
- **摂動法が強力**: `\override` で esw / padding を振り、係数1で追随するか不変かを見る。
  **全部ゼロにして残った定数**がハードコード値
- **測定 regime を混ぜない。** ragged-right（force 0）では spring の床、圧縮時は rod が
  binding する。**どちらで測ったか必ず記録する**
- ★ **ダンプは「1 レコード 1 行」にする。** 行を分けると**落ちる**（LilyPond の stdout に
  診断が割り込む）。2026-07-22 に「2 ページなのに PAGE 1 の行しか出ない」形で落ち、
  そこから誤った結論を出した。**内訳は 1 回の format にまとめ、合計が既知の総数と
  一致するかを必ず突き合わせる**（本数の和 ≠ システム総数なら、その読みは捨てる）
- **配置は「両側」を測る。** ある grob の位置は前後2つの間隙で決まる。
  さらに**同じ box の左右が同じ基準点か**を確かめる
- ★ **縦は staff refpoint 間で測る。system 原点間で測ると嘘の値が出る。**
  `staff-refpoint-extent` は system ごとに違う（小節番号を頭上に持つ system は原点が
  その分だけ上に伸びる）ので、**間隔が一様でも原点間距離はばらつく**。同じ LP ダンプが
  原点間で 11.528583 / 12.000000、staff refpoint 間で 12.000000 / 12.000000。
  前者が「LP は圧縮している」という誤った結論として引き継がれていた
- ★ **残差の符号で原因を切り分ける。** あるグリフの**左右の残差が逆符号**なら
  **frame（基準点）の誤り**、**同符号**なら**定数の誤り**。定数が違えば両側とも同じ向きに
  ずれるが、基準点がずれていると片側が広がった分だけ反対側が狭まるため。
  行中 clef/key 変更でこれを使って診断した（`midmeasure.*` の 4 点）
- ★ **変更する前に測る。** 変更後に測ると「LP に近づいたか」を判定できない。
  着手前にコーパスへ点を足しておけば、**反証可能な予測**（この4点が揃って 0 に向かうはず）
  になり、外れたときに診断が違うと分かる
- **「悪化した」＝「変更が間違い」ではない。** 間違った定数が別の欠陥を隠している構図は実在する
- ⚠️ **SVG から精密測定をしない。** 座標は `F2`（`SvgGenerator.cs:229`）で2桁に丸められる。
  6桁の LP 値と比べるなら `LpFidelity/RecordingDocumentContext` を使う
- ⚠️ **紛らわしい数値に飛びつかない。** 6桁一致しないなら別物と疑う
  （残差 0.189365 を「bar line 幅 0.19」と誤認した実例あり）

### 5.4 テストの原則

- **実装の定数を実装自身と比べるテストは何も守っていない。**
  LP 由来の期待値を書き、なぜその値かを `LILYPOND-REF` で示す
- **テストが LP と食い違ったら、テストを実測に合わせる**（再ピン止めしない）
- **追加したテストが「修正前なら落ちる」ことを実証する**
- **1点狙い撃ちにせず掃く**（掃引テストは改行位置が動いても空振りしない）
- 増分再利用（F3）: **小節幅に影響する新要素は `MeasureContentKey` に必ず畳み込む**。
  「隣の小節の内容で決まる」量は intrinsic hash から復元できないので**明示的に**足す
- spring は 2 系統＋改行 gate の 3 箇所を**必ず一致**させる
  （`MeasureLayouter.CreateTimingSprings` / `SpacingRules.CreateSpringsForMeasure` /
  `SystemBreaker`）

### 5.5 環境の落とし穴

- **dotnet の増分ビルドが腐る** → 前後比較では `--no-incremental` でビルドして
  `dotnet run --no-build`。なお `dotnet test` は `--no-incremental` を受け付けない
- **LilyPond は Guile デッドロックする** → `cmd /c "... < NUL"` でデタッチ必須。
  終了コード 1 でもダンプは出ている
- **コンソールの文字化けに騙されない。** ファイル実体は正しいことが多い。Read で確認してから判断
- **fixture**: Lily# の `octave absolute` は LP より一段高い（**LP `c'` ↔ Lily# `c`**）。
  既定は相対オクターブ。mid-music の key/time/clef 変更はバックスラッシュ無し。
  空小節は `| |` ペア。part 名に予約語を避ける（`p` は dynamic）

---

## 6. コマンド集

```powershell
# ビルド（--no-incremental 必須）
dotnet build LilySharp.Core\LilySharp.Core.csproj --no-incremental -v m   # 0 warn/err 期待
dotnet build LilySharp.Tests\LilySharp.Tests.csproj --no-incremental -v q

# 全テスト
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build -v q 2>&1 | Select-String 'Passed!|Failed!|\[FAIL\]'

# LP 忠実度スコア
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build `
  --filter 'FullyQualifiedName~Corpus_ReportsTotalDivergence' --logger 'console;verbosity=detailed'

# LP 実測（プローブを LilyPond に通す。既定 exe は 2.26.0）
pwsh audit\lp-geometry\Measure-LilyPondGeometry.ps1        # X（小節線まわり）
pwsh audit\lp-geometry\Measure-LilyPondPageGeometry.ps1    # Y（ページ縦）

# フォント由来の生成物（フォント更新後は必ず両方。py -3.13 必須 — PATH の python は
# LilyPond 2.24.4 同梱で fontTools も pip も無い）
py -3.13 audit\scripts\Extract-EmmentalerGlyphs.py     # → Svg\EmmentalerGlyphs.Generated.cs
py -3.13 audit\scripts\Extract-EmmentalerMetrics.py    # → Svg\Layout\GlyphMetricsGenerated.cs
# どちらも「feta 名がフォントに無ければ exit 1」。差分が出たらフォントが変わった証拠

# snapshot 再ベース（LP 照合＋ユーザー承認の後のみ・フィルタを掛けない）
$env:LILYSHARP_UPDATE_SNAPSHOTS = "1"
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build -v q
Remove-Item Env:\LILYSHARP_UPDATE_SNAPSHOTS
"ENV NOW = [$($env:LILYSHARP_UPDATE_SNAPSHOTS)]"    # ← 空であることを必ず目視
# → env を消して再実行し全緑を確認 ＋ git status で「動いたのは意図した snapshot だけ」を確認

# 目視用 PNG / SVG
dotnet run --project LilySharp.Cli -- png --crop --scale 4.0 "NAME.lys" "out.png"
```

- fixtures = `LilySharp.Tests\Fixtures\{test,showcase}\*.lys`、
  snapshot は `Snapshots\<dir>__<name>.svg`
- snapshot テストは `SvgSnapshotTests.TestSamples()` の**明示 `yield return` リスト**
  （`.lys` を置くだけでは走らない）

---

## 7. セッション終了時チェックリスト

1. [ ] 全緑を確認（`Passed!` の数を §1 に書く）
2. [ ] **§1「現在地」を書き換える**（追記しない）— HEAD / ahead 数 / テスト数 / 非ゼロ台帳点 / ▶
3. [ ] §2「開いている作業」が動いたなら更新する。**完了した項目は消す**（経緯を残したいなら
      §5 へ**汎化**するか、アーカイブ（§8）へ落とす。§1・§2 に溜めない）
      ⚠️ **§1 が 1 画面に収まらなくなったら、それは落とす合図。**
4. [ ] このセッションで得た**恒久的な知識**を §4 の表に従って置き場所へ出す
      （LP 実測値 → `audit/lp-geometry/`、LP の式 → コード内 REF、座標系の状態 →
      `COORDINATE_AUDIT.md`）
5. [ ] **新しい `handoff-*.md` を作っていないことを確認**
6. [ ] **snapshot を再ベースしたなら、それを正当化する台帳キーを message に名指したか**（§5.2.1③）
7. [ ] **定数を足したなら `LILYPOND-REF:` か `LILYSHARP-OWN:` を付けたか**
      （`LpProvenanceTests` が落ちる。baseline を上げて通すのは禁止・§5.2.1①）
8. [ ] `git status` で意図しないファイルが混ざっていないか確認
      （特に `audit/scripts/__pycache__/` — 生成器を走らせると必ず出る。commit しない）

---

## 8. 過去の記録の在り処

### `docs/HANDOFF-ARCHIVE.md` ← **このファイルから外した §1〜§3（逐語・2026-07-24 まで）**

セッションごとの経緯・閉じた欠陥の詳細・完了したロードマップ項目は全部そこにある。
**通読する必要は無い。** 読むのは次のどちらか:

- **同じ regime にもう一度触るとき**（その欠陥をどう測って何が第2欠陥だったか）
- **本ファイルの記述を疑うとき**（「完了」表記の裏取り。ただし最終的な裏取りは常に実コード＝§0）

⚠️ **アーカイブへ追記しない。** 本ファイル §1 を書き換えるのが引継ぎで、
§1 が長くなったら**そのときアーカイブへ落とす**（原則・学びは §5 へ汎化してから）。

### root に残る旧 handoff（未追跡）

root に **14 個の未追跡 `HANDOFF-*.md` / `handoff-*.md` / `REVIEW-HANDOFF.md`** が残っている
（`handoff-2026-07-21-mmr-runs.md` は回収完了ののち削除済み）。
各ファイルが「原則・手順」を丸ごと重複コピーしており、これが増殖の主因だった。
**本ファイルがそれらを置き換える。**

⚠️ ただし中身には未回収の知識が残っている可能性がある。**一括削除しないこと。**
以下は着手時に参照する価値がある順:

| ファイル | 参照価値 |
|---|---|
| `handoff-2026-07-21-x-frame-unification.md` | ✅ 内容は完了・`COORDINATE_AUDIT.md` §4.7 と本ファイルに吸収済。**光学補正を clef のせいとする誤記あり**（訂正済） |
| `handoff-2026-07-21-boundary-column.md` | §2③ 着手時に。LP 事実の記録として有用 |
| `HANDOFF-stage4-vertical-yup.md` | **島1 は `ff64f38e` で完了したので、この用途は消えた。** 残る価値は §3B ②（島2 = device 島群）の記述だけ。⚠️ 島1 に関する記述は全部 stale。**次に島2 へ着手する人が回収して削除する候補**（削除はユーザー承認） |
| `HANDOFF-lp-calc-incorporation.md` | §3C 着手時に |
| `HANDOFF-dead-code-audit.md` | §3D 着手時に |
| `HANDOFF-2026-07-20-*.md`（5本） | 過去セッションの記録。LP 事実は概ね吸収済 |
| `HANDOFF-beam-quanter-unification.md` / `HANDOFF-coord-frame-unification.md` / `HANDOFF-layout-x-unification.md` | 完了済み作業の記録 |
| `REVIEW-HANDOFF.md` | 規律は §5.1 に吸収済 |

**回収方針**: 各テーマに着手するとき、そのファイルを読んで**必要な知識を §4 の置き場所へ移し、
読み終えたファイルを削除する**（削除は都度ユーザー承認）。一気にやらない。

なお `AI_POSITIONING_HANDOFF.md` と `docs/arpeggio-rework-handoff.md` は**追跡済み**で
別系統。本ファイルの対象外。
