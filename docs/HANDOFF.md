# Lily# 開発ハンドオフ（常設・単一）

> **このファイルが唯一の引継ぎ先。新しい `handoff-*.md` を作らないこと。**
> 引継ぎは §1「現在地」を**書き換えて**行う（追記しない）。恒久的な知識は §4 の表に従って
> それぞれの置き場所へ出す。ここに溜め込むと、以前と同じように 16 個に分裂する。

最終更新: 2026-07-23 / master `87bcde22`（§0 で裏取りすること。origin より 4 ahead・未 push）

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

### ▶ 次のセッションの最初の一手 ＝ **スラー点の FIX を入れる（出力が変わる＝承認ゲート）**

`87bcde22` で **スラーの最初の台帳点を対で開いた**（`staff.staff.slur-under-notes` /
`slur-over-notes`、SD/SU）。**予測 −0.512596 が桁まで的中**し、SD/SU は同値
（＝二つ目の欠陥は無い）。欠陥は確定した: **Lily# の `SkylineBuilder` に `slur` の語が無く、
スラーは `EnrichExtentsWithAnnotationProtrusions`（スカイラインが勝つ scalar fallback）にしか
届かない**＝タプレット括弧を隠していたのと同じ配線。LP のスラーは inside-staff grob
（`outside-staff-priority #f` 実測）で staff スカイラインに入る。

**FIX の場所**: `MultiStaffLayouter.BuildAllStaffSkylines`（譜間）と
`LayoutEngine.AugmentSkylinesForPaging`（ページ）に、`SlurLayout` の bow から**スラーを種として
入れる**——タプレット括弧を `AddTupletBracketsToSkyline` で入れたのと同型。全音符スラーには幻の
符尾が無いので、タプレットと違い**符号逆の第二欠陥は予想されない**（SD/SU が既に一致）。
⚠️ **これは出力を変える**（snapshot 再ベース）＝ **LP 照合 → 承認 → 実行**。承認を取ってから。

### ▶ 保留になった一手 ＝ **clef の LILC-vs-skyline sliver（Y 4 点）は SKPath だけでは閉じない**

⚠️ **`f37d2af1` の「SKPath で輪郭が取れる＝手が届く」は半分だけ正しかった**（2026-07-23 に実測）。
LP の clef skyline (−2.540 . 4.776) は `add_named_glyph_segments`（`stencil-integral.cc:535`）が
**輪郭を `scale = LILC_bbox_X / outline_bbox_X` で縮めて** `add_outline_to_skyline` で作る。
Skia は輪郭 **Y**（635/1194 font-units）を LP と桁一致で返すが、**scale が合わない**:
`LILC_X/outline_X = 2.565/643 = 0.003989` に対し LP の実効 scale は **0.004**（＝ 635×0.004=2.540）。
差 0.27% は **LP 内部の `get_unscaled_indexed_char_dimensions`（grob extent 2.565 とは別の LILC 計測）×
magnification 0.5690551 の相互作用**由来で、Skia からは出せない。**フォントは byte 一致（同一 SHA256）**
なので font 差ではない。閉じるには **LP を instrument して
`get_glyph_outline_bbox` / `get_unscaled_indexed_char_dimensions` / magnification を dump** して
実効 scale を突き止めるのが先（payoff は 4 点 ×0.0001〜0.0008 ss）。**SKPath 直読みで定数を合わせるのは
§5.2 違反（fitting）。** ⚠️ この 4 点はまだ台帳に「clef sliver」として残っている（§3A）。

### ▶ その次 ＝ **さらに未測の領域（ビーム・タイ・臨時記号配置）に点を開く**

#### ✅ このセッション（2026-07-23 後半）＝ **スラー点を対で開いた＋clef sliver を実測で保留した**

| commit | 内容 |
|---|---|
| `87bcde22`（**未 push**） | **スラーの最初の台帳点を対で起票**（SD/SU、`staff.staff.slur-{under,over}-notes`）。出力不変（probe 2 book＋台帳 2 点＋.lys twin のみ）。**予測 −0.512596 が桁まで的中**・SD/SU 同値。欠陥確定＝スラーは skyline に未予約。**FIX は未実施**（出力変更＝承認待ち） |

**テスト 3199 passed / 0 failed / 3 skipped。** Core 0 warn / 0 err。
**LP 忠実度 29/42 exact, total |residual| = 1.044394 ss over 38 distances ＋ counts 4/4**
（+1.025 は新規可視化＝タプレット点追加と同型。§3A・§5.2.1④）。
clef sliver は **SKPath 直読みでは閉じないと実測で判明**（上の「保留になった一手」）＝コード変更ゼロ・investigation の test/probe は削除済み。


**タプレット括弧は 4 点とも閉じた**（残差は名前付きの 1 種類だけ）。X 軸も「定数 1 つで閉じる」ネタは
尽きている。**残っている最大のリスクは、測られていない領域そのもの**——§3A の穴は
**スラー/タイ・ビーム・臨時記号配置**で、台帳の点が **1 つも無い**。

⚠️ **今セッションの実績が根拠**: 点を開いた瞬間、**狙っていた欠陥と一緒に狙っていなかった欠陥が落ちた**。
タプレットでは 4 つ（予約漏れ・幻の符尾・prelim の声部スコープ漏れ・probe が clef を測っていた）。
**穴を開けるまで、そこに何が溜まっているかは分からない**（§5.2.1④）。

**手順は 4 回連続で成立した型をそのまま使う**:

1. **点を起票する**（コード変更ゼロ・出力不変）。`residual: null` ＋ **予測を先に `why` へ書く**。
2. ⚠️ **必ず対で作る。** P/Q・TU/TD・TSU/TSD の 3 組とも、**値ではなく「対が食い違ったこと」**が
   欠陥を出した。片側だけなら予測が当たって欠陥ごと出荷していた（今回まさにそうなりかけた）。
3. ⚠️ **床に座らせない。** 譜間は basic-distance **9**、system 間は **12**。それを超えないと点は
   両側 exact になって**何も測らない**。probe TU が最初 `d'` で 9.000 を返した件を読むこと。
4. ⚠️ **probe が何を測っているか確かめてから信じる。** TSU は最初 clef を測っていた（下記）。

**その後の候補**: 水平スカイライン（X の ♮ 1 点 −0.017672 ＋ 臨時記号配置の族が閉じる。§2④'）と、
下の**描画フォントの乖離**（ユーザー判断待ち）。

#### ✅ 完了: フォント metric を実行時に測るようにした（`f37d2af1`）

**タプレット 4 点は +0.0000208 で閉じた**（残るは Pango 量子化の半分）。
**LP 忠実度 total 2.214721 → 0.023937 ss** ＝ タプレットを測り始める前の 0.023853 に戻った。

決定と実施内容:

| | |
|---|---|
| 既定テキストフォント | **TeX Gyre Schola**（＋ sans は **TeX Gyre Heros**）を同梱 |
| 選定理由 | `"LilyPond Serif"` は C059 を第一優先。**C059 と Schola は全 advance とインクが一致**＝同一設計。ただし **C059 は AGPL v3**、Schola は **LPPL 1.3c**（GUST） |
| 測定 | `SKPaint.GetTextPath(...).Bounds`（**`MeasureText` はフォント単位で量子化されるので不可**） |
| 適用 | レイアウトの 37 箇所＋3 backend すべてを同じファイルから |

✅ **旧メトリック表（`Serif/SansTextMetrics`）と同梱 Liberation は削除済み**。
`THIRD-PARTY-NOTICES.md` / `README.md` は TeX Gyre の帰属に差し替え済み。
⚠️ **`FontEmbedInfo.LibreMarkers` の `"Liberation"` は残す**——あれは**利用者が
`font "X" embedded` と書いたときの許可リスト**で、システム導入フォントの話。同梱物とは無関係。

⚠️ **改行が動いた**: `test/lyric-break-pricing` 2→3 システム、`test/lyrics-after-rest-bar` 1→2。
歌詞が 9% 広くなった直接の結果で方向は正しいが、**目に見える製品挙動の変化**。

★ **予約と描画が一度も同じフォントでなかった**: PNG は CSS generic をシステム解決（実測で
`"serif"` → **Segoe UI ＝ sans**）、SVG は閲覧者任せ、PDF だけが Liberation。
レイアウトは Times 幅を予約していた。**タプレット括弧が「どこにも予約されていなかった」のと同型**で、
**こちらも台帳の点が 1 つも無かった**。

#### ★★ 元の判断メモ（2026-07-22）

> ⚠️ **以下は着手前の状態を現在形で書いたもの。** `Serif/SansTextMetrics` も同梱 Liberation も
> 既に削除済みで、38 箇所は全て `TextFontMetrics` に移っている。**判断の根拠として残す。**

⚠️ **過去の記述「C# 側に OTF パーサは無い」は誤りだった。** 裏取り済み:

| 事実 | 出典 |
|---|---|
| `SkiaSharp` は **`LilySharp.Core` の依存** | `LilySharp.Core.csproj:31-35` |
| `SKTypeface.FromFile` / `FromFamilyName` / `MeasureText` を**本番で使用中** | `PngDrawingContext.cs:284-315`、`FontEmbedInfo.cs:71,137` |
| `.lys` は**テキストフォントを指定できる** | `font "NAME"` ヘッダ → `MultiStaffScore.TextFont` |

つまり**フォントは利用者が選べる**ので、**定数を焼き込む設計は原理的に誤り**——`font "..."` を
書かれた瞬間に外れる。LP と同じく**実効フォントに実行時に問い合わせて式へ入れる**のが正しい。

**本当に無いのはレイアウト層の配線**。今のレイアウトは `SerifTextMetrics` ＝
**Times New Roman の advance 幅を手打ちした表**（`SerifTextMetrics.cs:47-55`）を使っており、
**高さを一切持たない**。しかも**描画側は別のフォントで描いている**ので、
これは強弱記号で記録済みの「確保するのは片方のインク、描くのは別フォント」と同じ病気。

⚠️ **規模はタプレット数字 1 件ではない。** `Serif/SansTextMetrics` の呼び出しは
**10 ファイル 38 箇所**（`MusicMarkEngraver` 15・`OutsideStaffStacker` 6・`LayoutEngine` 4 …）で、
**幅の予約も全部この表**。1 箇所直せば数字の高さも既存の幅も同時に正される。

⚠️ **決めるべきは 1 つ: 既定の決定性。** 実インストールのフォントを測ると、
**同じ `.lys` がマシンによって違う組版になる**。これは `f37d2af1` に入ったプローブ修正（serif 固定）と**同型**
（fontconfig 任せの serif を測っていた）。LP は自分のテキストフォントを同梱して既定を固定している。
Lily# は Emmentaler は同梱しているがテキストフォントは同梱していない。
→ **測るのは動的に、既定は決定的に**（＝既定用のテキストフォントを 1 つ同梱する）が素直。

#### ✅ Phase 0 は実施済み — **設計は成立する**（2026-07-22・コード変更ゼロ）

予測を先に書いてから測った。LP 側: `text-font-size 11pt × 2^(−2/6)` = **8.730706 pt**、
1 ss = 5 pt なので 1.255434 ss = 6.277170 pt ⇒ **予測 ink/em = 0.718976**。

| 経路 | 結果 |
|---|---|
| `SKPaint.MeasureText(s, ref SKRect)` | **0.718750**（TextSize を 10^6 にしても不変＝**量子化**。粒度 0.027 ss で**追っている残差より粗い**） |
| **`SKPaint.GetTextPath(...).Bounds`（アウトライン）** | **0.71900006** ⇒ **1.255476 ss** |

**LP 実測 1.255434 ss との差は 0.000042 ss。** ⇒ 残差 −0.547717 は **−0.000021 程度まで**閉じる見込みで、
残りは `DynamicText` の −0.000076 と同じ **Pango の量子化**と整合する。
⚠️ **使うのは `MeasureText` ではなく輪郭**（LP も FreeType のアウトラインを見ている）。

⚠️⚠️ **そして名前引きは使えない。** このマシンで `SKTypeface.FromFamilyName` は
**`"LilyPond Serif"` も `"C059"` も `"TeX Gyre Schola"` も、そして `"serif"` さえ Segoe UI** に解決した
（＝**sans**）。LP は自分の `share/lilypond/2.26.0/fonts/otf` を私的 fontconfig で登録しているので
システムには居ない。**つまり Lily# の PNG は今この瞬間「serif」を Segoe UI で描いている。**

⇒ **同梱してファイルから `SKTypeface.FromFile` で読むのは必須**（決定性のためだけでなく、
そもそも名前が解決しないため）。LP 同梱の候補は `C059-*.otf` と `texgyreschola-*.otf`（どちらも
数字の輪郭高さは同一 0.71900006 なので、この点だけでは選べない。幅や他グリフで決めること）。

#### ⚠️ このセッションが作った/露わにした複製 2 件（着手時に畳む候補）

- **`EnrichExtentsWithAnnotationProtrusions` は今やタプレット括弧の 2 つ目のモデル**（`LayoutEngine.cs:872-880`
  の手書き封筒 `hi−1.6 / lo+0.7`）。スカイライン経路が入ったので、**同じ grob を別式で持つ状態**＝
  §5.2.1② そのもの。空スカイラインの fallback でしか効かないので優先度は低いが、名指しておく
- **prelim パスが 4 つの引き当て表を自前で作っている**（`LayoutEngine.cs` の `prelimVoicesByStaff` ほか）。
  最終パスの同名テーブルと**同じ構築を 2 回**している。`staffYByIndex` だけは読み元の system 配列が
  違う（prelim/最終）ので、統合するなら等価性を確かめてから

---

### ✅ このセッション（2026-07-23）でやったこと — **タプレット括弧・実フォント計測・TimeSignature**

⚠️ **ユーザーがこのセッションの細かいコミットを squash で意味単位にまとめた**（2026-07-23）。
下表は **squash 後の実在ハッシュ**。作業中の細かいハッシュ（`caa0f239` `b36db266` 等）は
**全部 dangling** になった——本ファイルの他所や過去記述に残る旧ハッシュは
`git merge-base --is-ancestor <sha> master` で到達性を確認すること（§1 末尾の手順）。

| commit | 内容 |
|---|---|
| `eb8315f8`（origin） | **タプレット括弧を譜間とページ両方のスカイラインへ＋幻の符尾**。起票→予測→実装を対（TU/TD・TSU/TSD）で。**欠陥 4 件**を検出（①予約漏れ ②幻の符尾＝`highestPos` を符尾先端の集約に作り替え ③prelim 注釈パスの声部/譜スコープ漏れ＝間隔だけ壊れ snapshot に出ない ④probe が clef を測っていた）。台帳 4 点 `−1.7/−1.3 → −0.547717` |
| `f7d2983c`（origin） | **強弱の縦インクを実インクへ**（前セッション分。dynamic は fetaText＝アウトライン） |
| `f37d2af1`（origin） | **実フォント計測**。TeX Gyre Schola/Heros 同梱、`TextFontMetrics`（輪郭で測る）、レイアウト 37 箇所＋3 backend を実フォントへ。タプレット 4 点 `−0.547717 → +0.0000208`。**予約と描画が一度も同じフォントでなかった**（PNG=Segoe UI）。CJK 幅の回帰修正・旧 Times 表と Liberation の削除も同梱 |
| `033c0df8`（**未 push**） | **TimeSignature 幅を Pango 量子化へ**（LP 定数から導出：`72×72.27/(1200×5×25.4)`）。`barline.next.time-change-to-notehead −0.004735 → +4.3e-7`。**exact 28→29 / total 0.023937→0.019202 ss**。**snapshot 188 件再ベース**（y 不動・最大 0.03 ss） |
| `11d5bcba`（**未 push**） | §1 書き換え（この分） |

**テスト 3197 passed / 0 failed / 3 skipped。** Core 0 warn / 0 err。
**LP 忠実度 29/40 exact, total |residual| = 0.019202 ss over 36 distances ＋ counts 4/4。**

#### ★ TimeSignature の −0.004735 は Pango 量子化だった（DynamicText の −0.000076 と同一機構）

LP の default 拍子は `\number`（fetaText）＝**テキスト経路**で、幅は device pixel に hint される。
量子 `q = 0.034143 ss` は **`INCH_TO_BP / (PANGO_RESOLUTION × output_scale)`** から**字面導出**
（`pango-font.cc:109-112`）。全 10 桁で LP を 1e-15 再現。⚠️ **同じ Pango 量子化でも
DynamicText の高さ側は輪郭全体の量子化が要るので未着手のまま**（幅は 1 回の snap で済む）。

⚠️ **未測定の本物の乖離を記録した**: Lily# は `fattened` 桁 advance を使うが LP は ASCII 経路。
桁 4 は両者 1.600 で同じだが桁 1 は fattened 1.292 vs ASCII 1.268。1/4 拍子等の点が無いので
**推測で直さず点を先に作る対象**（`GetTimeSigDigitWidth` の remark と台帳 why に記録）。

#### ★ 4 点とも、起票時の予測も実装後の予測も桁まで的中した

| 点 | 起票 | 実装後 |
|---|---|---|
| `staff.staff.tuplet-bracket-{up,down}` | −1.700225（2 点同値） | **+0.0000208**（4 点すべて同値） |
| `system.tuplet-bracket-{up,down}` | −1.295225（2 点同値） | **+0.0000208**（同上） |

⚠️ **途中段階の −0.547717 ×4 は `f37d2af1` のフォント配線で消えた。** 残るのは Pango 量子化の半分。

#### ★★ 教訓: **欠陥を出したのは値ではなく「対が食い違ったこと」**（3 度連続）

| 対 | 何が出たか |
|---|---|
| P/Q | 幻の符尾（`89aaa29f`） |
| TU/TD | 予約漏れ＋幻の符尾（LP 側が同値になるまで pitch を直した） |
| **TSU/TSD（LP 側）** | **probe が clef を測っていた**——小節丸ごとのタプレットだと括弧が clef の直後から始まり、相手 system の最深インクが五線 2.05 ではなく **clef 3.540** になる。差 1.490 がモデルに合わなかった。各小節の頭に素の全音符を置いて解決 |
| **TSU/TSD（Lily# 側）** | **prelim パスが声部を見ていなかった**——シード後 TSU だけ予測どおり動き、TSD が動かなかった |

⚠️ **TSD が動かなかったことが無ければ、TSU は予測に一致して点は閉じ、欠陥ごと出荷していた。**

#### ★★ 教訓: **プローブの描画オプションが答えを決めることがある**（`b69c73e6`）

`ly/paper-defaults-init.ly:170-173` は `fonts.serif` を **SVG backend のときだけ**素の `"serif"`
（＝fontconfig 任せ）に落とす。測定スクリプトは**実ページが要るので `-dbackend=svg`** を使っている。
結果、**binding するインクにテキストが入る量だけ**が、LP 本来のフォントでない・かつ**測定マシン依存**の
値で台帳に入っていた。タプレット 4 点が **0.027492** ずつ小さかった。

⚠️ **対が一致していても捕まらなかった**——両側とも同じだけ間違っていたから。
**対の食い違いは「片側だけの欠陥」しか捕まえない。** 参照そのものが偏っているときは効かない。

切り分けは実測: 両 backend で全書籍を回すと、**binding がグリフか五線の 8 冊（N J S L T P D Q）は
完全に同一**（12.000000 / 12.254816 / 12.255229 / 11.716033 / 11.716074 / 12.018019 /
9.595000 / 10.783076 / 9.595000）で、**タプレット 4 冊だけが毎回 0.027492 違う**。
book D の強弱が無事なのは **DynamicText が fetaText＝Emmentaler** でテキストフォントではないから
（`26afa9fe` が逆向きに使った区別と同じ）。

→ `probeTag` に `property-defaults.fonts.serif = "LilyPond Serif"` を入れた（**個々の book ではなく
タグに**。次にテキストを含む book を足す人が同じ罠を継がないように）。
**数字の半分の高さは 0.627717**（旧記録 0.600225 は誤り）。

#### ★ prelim（間隔決定）パスは最終パスと**別の geometry を見ていた**

`LayoutEngine` の prelim 注釈パスは `VoicesByStaff` / `MeasuresByStaff` / `StaffYByIndex` /
`StaffByIndex` を**渡していなかった**ので、`TupletBracketEngraver` が
**主譜の主声部**にフォールバックしていた。声部 2 のタプレットは**声部 1 の音符**から、
下の譜のタプレットは**上の譜の音符**から位置決めされていた。
⚠️ **最終パスは正しく描いていたので、snapshot には一切現れない**——間隔だけが間違っていた。
**オラクルが構造的に見られない種類の欠陥**で、台帳の対だけが捕まえられる。

#### ★ snapshot 14 件は**全部が剛体の平行移動＋ページ枠**

12 件は `<svg>` の高さと viewBox **だけ**が変わりグリフは 1 つも動かず、
`figbass-chordname-lower-staff` は 67 個の y が**全て −1.74**、`ossia-beams` は 73 個の y ＋
ossia の transform ＋ スラー 2 本が**全て −0.04**。要素数・行数は 14 件とも一致。
ページ枠が動いたのは**単一ページ経路が内容サイズでページを作る**から＝間違っていたのは測定の方。
⚠️ **縮んだ側があるので全 28 ファイル（前後）で「viewBox 外に描画が無いか」を検査した**
（`fc0feb20` の教訓）。**ゼロ**。

---

### 同日の前半（2026-07-22）— 譜間側の記録

#### ★ 起票時の予測も、実装後の予測も、2 点とも桁まで的中した

| 段階 | 予測 | 実測 |
|---|---|---|
| 起票（コード変更ゼロ） | −1.700225 | **−1.700225000**（TU/TD 同値） |
| シード＋符尾ガード後 | −0.547717 | **−0.547717000**（TU/TD 同値） |

**TU と TD が全段階で同じ値を返すこと自体が検証**（P/Q と同じ性質）。2 冊は音高が 0.5 違うだけなので、
引き算で残るものは括弧のスタックだけ。⚠️ **片方だけ作っていたら符号の誤りを見逃せた。**

#### 1 つの数字に**符号の逆な欠陥が 2 つ**入っていた

| | 直した内容 | 寄与 |
|---|---|---|
| ① 予約漏れ | `SkylineBuilder` に **`tuplet` という語が 1 つも無かった**。譜間もページも括弧を種にしておらず、**下の譜の括弧が上の譜の五線を突き抜けて描かれていた**。LP の TupletBracket は `outside-staff-priority` を**持たない**＝VerticalAxisGroup の内部 grob で、clef と同じく staff のスカイラインに入る | **+1.18** |
| ② 幻の符尾 | `CalculateSlope` が `DefaultStemLength` を音価によらず足していた（`89aaa29f`・`26afa9fe` と同型の 3 つ目）。LILYPOND-REF `lily/stem.cc Stem::is_normal_stem` | **−2.955** |

#### ★ ② が 2 度の掃除を生き延びたのは「門を足すだけでは済まなかった」から

`highestPos`/`lowestPos` は **staff position** を集約してから一律に符尾を足していたので、
**音価を見る場所がそもそも無かった**。**列ごとの encompass point**（符尾があれば先端、無ければ符頭の
LILC インク）の集約に作り替えた。⚠️ **これは整理のための書き換えではない**——音価が混ざった瞬間に
**「五線上で最も高い音」と「括弧が避けるべき音」が別物になる**。
LILYPOND-REF `lily/tuplet-bracket.cc calc_position_and_height`。

#### ★ `0.13 → 0.16` は新しい定数ではない — **読み手が居なかった**

`EngravingDefaults.TupletBracketThickness` は **LP の `1.6 × line-thickness` を LILYPOND-REF 付きで
最初から持っていた**のに、`SharedRenderer.Overlays.cs` がその**隣で生の `0.13` を書いていた**。
**§5.2 の「未実装ではなく、書いてあるが呼ばれていない」の実例。** 描画と予約は 1 つの定数から。

#### ★ 残るのは **TupletNumber のテキストインク** 1 件（−0.547717）

`lily/tuplet-number.cc:342` は knee でない限り数字の `Y-offset` に**括弧の positions の中点**を返し、
`:227-228` が stencil を**中心合わせ**する。だから数字は括弧線をまたぎ、**自分の高さの半分＝0.627717**
だけ外へ出る（括弧自身の 0.08 より遠い）。これは LP の**斜体 font-size −2・通常テキストフォント**の
metric ＝ TimeSignature の −0.004735 と同じ経路で、Lily# に測る手段が無い。

⚠️ **一度台帳に「padding 1.1 ＋ 括弧の半分の太さ 0.08 ＋ 数字の食み出し 0.547717」と書いたが、
それは合計から逆算した推論で誤り。** 合計は同じでも分解が違う。訂正済み。
**§5.3「要約された数字から逆算しない」の縦版。**

#### ★ snapshot 9 件は**全行が線幅だけ** — そして**それがフィクスチャの穴の証拠**

56 行が動き、**56 行すべてが `stroke-width 0.130 → 0.160`**。`stroke-width` をマスクすると
**差分 0 行**、要素数・行数は 9 件とも完全一致、**Y は 1 箇所も動いていない**。

⚠️ **これは「無害の証拠」ではない。** 括弧の位置が 1 つも動かなかったのは、
**既存フィクスチャに全音符タプレットが 1 つも無く**、どれも `min(tipY, −staff-padding)` の床が
勝っていたから＝**コーパスは今回の修正の本体を見ていない**。`fc0feb20` の教訓どおり
**`test/tuplet-bracket-whole-notes` を新設**した（PNG 目視済み）。
2 小節に分けてあるのは、1 小節だと**2 つの数字が同じ X で重なる**のをフィクスチャが承認してしまうため。

#### ⚠️ 測定の罠（probe のヘッダにも書いた）

**`after-line-breaking` のコールバックで system 相対座標を読むと、その照会自体が縦の整列を
早期に確定させて答えを動かす。** 同じ音楽が譜間 **18.000000** を報告し（実際は 9.000000）、
その 9 ss を説明する grob を探して時間を溶かした。**数値は無摂動の描画出力から取ること。**

---

### 前セッション（2026-07-22）— **強弱記号の縦インクを閉じた**（`26afa9fe`）

分類 → 承認 → 実行の順で snapshot 16 件を再ベース済み（承認取得済み・**未 push**）。

| commit | 内容 |
|---|---|
| `26afa9fe` | **強弱の縦インクをフォントから読む＋幻の符尾＋符頭インク＋五線 frame**。`+1.866924 → −0.000076`。**snapshot 16 件再ベース**（分類・承認済） |
| `773db20f` | §1 書き換え＋**恒久ルール 2 件**（実測に基づく修正の禁止・リネームはユーザーが MSVS で） |

`staff.staff.dynamic-under-whole-note` は **+1.866924 → −0.000076**。
**着手前に書いた予測 −0.000076 と桁まで一致。**
LP 忠実度 **1.890701 → 0.023853 ss**（28/36 exact。exact 数は予測どおり不変＝
−7.6e-5 は tolerance 1e-6 より大きい）。テスト **3167 passed / 0 failed / 3 skipped**。

#### 1 つの数字に**欠陥が 3 つ**入っていた（符号が違うので同時にしか直せない）

| | 直した内容 | 寄与 |
|---|---|---|
| ① 幻の符尾 | `DynamicEngraver.GetLowestExtent` が**音価を見ずに** `noteY − DefaultStemLength` を返していた。`GetNoteValueFromFraction(...) >= 2` で門を付けた（LILYPOND-REF: `lily/stem.cc Stem::is_normal_stem`） | **−3.000000** |
| ② 符頭の名目箱 | この engraver だけ `NoteheadHalfHeight` 0.5 のまま。スカイラインは `22120764` で LILC の 0.545 に移っていた。`GetNoteheadBBox` へ | **+0.045000** |
| ③ 強弱の縦インク | `0.6 + 1.2 + 0.3 = 2.1` ＝ グリフを 1.5 の高さと見なしていた。LP は **3.188076** | **+1.088076** |

#### ★ ③ の答え: **LILC ではなく「アウトライン」**（`ec7a2254` と逆で、理由は同じ）

`DynamicText` は**グリフ引きではなくテキスト**（`define-grobs.scm:1438` `font-encoding
fetaText` ＋ `:1445` `ly:text-interface::print`）。だから
`Modified_font_metric::text_stencil`（`modified-font-metric.cc:125-143`）→ Pango →
FreeType アウトライン、という経路で測られ、**LILC を読む
`get_indexed_char_dimensions`（`open-type-font.cc:372-409`）は通らない**。
両者は丸め誤差の域ではなく違う: **LILC `f` = (−0.5834 . 2.0066) / アウトライン
= (−0.692 . 1.896)**。⚠️ **`ec7a2254` が「LILC が正」と決めたのは grob 経路の話**で、
**テキスト経路では逆になる。どちらの経路かを先に確かめること。**

裏取り（導出ではなく照合）は 3 組: `\p` → (−0.584004 . 1.168008)、
`\mp` → (−0.584004 . 1.196016) ＝ **アウトラインの p と m の和集合で、LILC からは到達不能**。
グリフ名は `dynamics.*` ではなく**素の ASCII 名 `f m n p r s z`**（fetaText 符号化）。

残る **−0.000076 は Pango の量子化**（LP のスカイラインは 2.588076、アウトラインは 2.588000）。
clef の LILC-vs-skyline sliver と同型の**名前付き残差**。埋めない。

#### ★ 4 つ目の欠陥が同じ式にあった — **打ち消し合っていた**

probe D では音符が binding するので**この数字には出ない**が、同時に直した:

| | Lily# 旧 | LP |
|---|---|---|
| 五線の寄与 | 線の**中心** 2.0 | `dim.set_minimum_height(staff_extents[dir])`＝**インク 2.05**（`side-position-interface.cc:323-330`） |
| `staff-padding` 0.1 | padding に**加算**していた | **refpoint の床**（`:433-453`）。加算ではない |

`2.0 + 0.1 + 0.6 + 名目 descent 0.64 = 3.34` と `2.05 + 0.6 + 実インク 0.692002 = 3.342002`。
**LP 実測 1.342 に固定していたテストが 2 桁では区別できず、何年も通っていた。**
`DynamicPlacementTests` は **3 桁**に上げてある（再ピン止めではなく、式が合ったので締めた）。
⚠️ **これが「実測に合わせて定数を選ぶ」の代表的な失敗形。§5.2 の枠に追記した。**

#### snapshot 16 件の分類（**X は 1 件も動いていない・要素数も全件一致＝改行不変**）

`page 高さ` は最大 +1.09。Y の移動量は全部この 5 つの和で説明できる:

| Δ | 正体 |
|---|---|
| **+0.696** | `f` を含む語のインク上端 1.2 → 1.896 |
| **+0.045** | 符頭インク 0.5 → 0.545（②。上向き符尾の音符に付いた強弱だけ） |
| **−0.050** | 五線の寄与 2.0+0.1 → 2.05（④。五線が binding する強弱だけ） |
| **−0.004 〜 −0.132** | 先頭が `p`(1.168)/`m`(1.196)/`s`(1.168)/`z`(1.068) の語＝**1.2 より低いので上がる** |
| 系の伸び | 上向き強弱が高く届くようになった分だけ system/page が伸び、その下が一律に動く |

代表例（`test/scripts-dynamics`、page 不変・動いたのは強弱 5 個だけ）:
`sfz fp sf` が **+0.74**（音符 binding＝0.696+0.045）、`rfz fz` が **+0.65**（五線 binding＝0.696−0.05）。

⚠️ **`test/dynamics` と `test/above-dynamics` は PNG で目視済み**（`--crop --scale 4.0`）。
衝突なし・上向きの積み上げも健全。

#### ⚠️ この変更で**見えるようになった**既存の乖離（未着手・別件）

**Lily# は強弱を serif の太字イタリックで描いている**（`SharedRenderer.Overlays.cs:65`
`gc.DrawText(..., "serif", ...)`）が、**LP は Emmentaler の fetaText グリフ**。
つまり**確保する幅・高さは LP のグリフ、描くのは別のフォント**という状態になった。
serif の `f` は上 ~1.5 なので、LP なら音符の 0.6 下に来るインク上端が **~1.0 下**になる
＝ **0.4 ss ほど余白が増えて見える**。⚠️ **これはこの変更が作った乖離ではなく、
この変更が可視化した乖離**（旧コードは名目 1.2 が偶然 serif に近かった）。
**直すなら描画側を feta グリフへ**——グリフは ASCII コードポイント（`f` = U+0066）に
入っているので、Emmentaler で `f` と描けばそのまま出る。X も同時に閉じられる
（`DynamicHalfWidth = 0.75` / `dynamicWidth = 1.3` も名目のまま）。**判断待ち。**

#### ✅ ここで「残した同型の欠陥」と書いた `TupletBracketEngraver` は `caa0f239` で閉じた

**幻の符尾は 3 箇所とも閉じた**（`89aaa29f` `26afa9fe` `caa0f239`）。予告どおり門の追加では済まず、
**位置の集約 → encompass point の集約**への作り替えになった。§1 冒頭の記録を読むこと。

---

### ✅ 決定: **`SystemBreaker` の再入可能化は入れない**（2026-07-22・蒸し返さないこと）

ページブレーカーの LP 乖離は **5 点とも閉じた**（`113fdeda` で 3 点／`850c7d98` で orphan
penalty／`fc0feb20` で overfull の却下）。台帳の `page.tight.*` は 2 点とも exact。

**残るのは構造差だけで、それは入れないと決めた。**
LP は `Optimal_page_breaking::solve` が**システム数を理想値から下へ掃引**し、各 count の全
line-division 構成でページ配置を解いて **demerit の argmin** を取る
（LILYPOND-REF: `lily/optimal-page-breaking.cc:139-173`、設計は `page-breaking.cc:75-101` に明記）。
つまり **LP ではページブレーカーが行分割を選ぶ**。実測（40 小節・6 システム、単位 ss）:

| 紙面 | LP | Lily# |
|---|---|---|
| 〜75 | 2 ページ 5+1（6 システム） | 2 ページ 5+1（〜76） |
| 76・77 | **1 ページ 5 システム**＝行分割を組み直した | （到達不能） |
| 78〜 | 1 ページ 6 システム | 1 ページ 6 システム（77〜） |

**見送った理由は性能ではなく F3 の健全性論拠が壊れること。**
`IncrementalCompiler.cs:37-42` は tier-1 skip を
「break 解は **per-measure spring ベクタ＋行頭 prefix 幅＋紙面の幅** の純関数」と根拠づけている。
**全部が横方向**。ページが行分割を選ぶと break 解は**縦の関数**にもなり
（紙面高さ・余白・スカイライン・hara-kiri 可視性・縦 spacing spec）、
それらは **break の下流で計算される**＝gate を計算するのに gate が守る結果が要る＝**循環**。
実害: 今日は「行分割を変えないと証明できる」編集（縦 extent だけ動かす＝高い音符・臨時記号・
強弱・スラー）が、**行分割を変えうる**ようになる。

⚠️ **緑ノードは無傷**（幾何を持たない設計は変わらない）。壊れるのは**レイアウト層の gate**。混同しない。

性能面: 候補ごとに system 境界が動くので `SystemLayoutCache` がほぼ全滅＝実質「数回のフルレイアウト」。
**安い部分は既にある**（`KnuthPlassBreaker.FindBreaksByLineCount` ＝ LP の `Constrained_breaking`
と同じ `dp[j,k]`。今は `looseness` 専用）ので、高いのは常に下流の再実行。

**判断し直すときの順序**（この順でないと始めないこと）:
1. **先に頻度を測る**（コード変更ゼロ）。既存 fixture ＋ showcase を LP に通し、
   **LP が選んだシステム数が Lily# と食い違う譜面を数える**。ゼロに近ければ議論は終わり
2. 有意なら**オプション分離**（CLI/PDF は LP 忠実・プレビューは 1-pass）。
   ⚠️ 二重実装＝§5.2.1② の罠そのものなので、**「両者が一致する」不変条件テストとセットでのみ**

**島1（`StaffLayout.Y` の Y-up 格納）も `ff64f38e` で完遂した。** §3B ① を参照。

---

### 同日の前セッション（2026-07-22）— **島1 とページブレーカーを閉じ、強弱に点を届かせた**

⚠️ **docs コミット（`51f229db` `26e44606` `453776a0` `dc28098c` `5f82aaf5` `ba728f31`
`3371d4e8`）は §1 の書き換えと訂正。下表は実質のあるものだけを挙げる。**

| commit | 内容 |
|---|---|
| `ff64f38e` | **`StaffLayout.Y` / `StaffGroupLayout.Y` / `BraceTop`/`BraceBottom` を Y-up 格納へ**（島1 の atomic flip）。3 ディスパッチャ＋7 積み上げ地点＋15 消費側。**byte 不変・snapshot 0 件**。フレームを直接固定する単体テスト 6 件を新設 |
| `920cf4dc` | **ページブレーカーの点を 4 つ起票**。A4 では捕まらないと判明し、**紙面を縮めた probe T** を新設。出力不変 |
| `113fdeda` | **ブレーカーの LP 乖離 3 点を字面移植**。`page.tight.page-count` −1 → 0。**snapshot 1 件再ベース**（分類済） |
| `850c7d98` | **orphan penalty を LP の条件へ**（第4の乖離）。`page.tight.systems-on-first-page` −1 → 0。**snapshot 0 件** |
| `fc0feb20` | **overfull を却下せずクランプ**（LP 準拠）。`test/tab-percent-repeat` が**紙面外描画から 2 ページへ**。**snapshot 1 件再ベース**（分類・承認済） |
| `19e06be1` | **DynamicEngraver に届く点を起票**（probe D、第2声部で符尾方向を強制）。**符号が逆の欠陥 2 つ**を検出。出力不変 |
| `7817c543` | **LP の dynamic 実測**（`glyph-skyline.ly` に book 追加）。descent 2 つの答えは「どちらでもない」と確定。出力不変 |

**テスト 3155 → 3167 passed、0 failed / 3 skipped。** Core 0 warn / 0 err。
**LP 忠実度 28/36 exact, total |residual| = 1.890701 ss（32 distances）＋ counts 4/4。**
⚠️ **ss 合計が 0.023777 → 1.890701 に増えたのは悪化ではない**——probe D が
**誰も測っていなかった 1.87 ss の乖離**を可視化した分。§5.2.1④ と同じ形（点集合が違う）。

#### ★ 教訓: **予測が外れたときこそ、その差が 2 つ目の欠陥**

probe D の予測 +2.955 に対し実測 +1.866924。**符号は的中して magnitude が外れた**とき、
差の 1.088076 は誤差ではなく**符号が逆の別の欠陥**だった。
⚠️ **もし予測を書かずに測っていたら、幻の符尾を直して residual −1.088 を見て
「絶対値が減った＝改善」と読んでいた。** 予測は当てるためではなく、
**外れ方から2つ目を見つけるため**にある。

#### ★ 教訓: **「到達しない」と書いた見立ては、フィクスチャで裏取りする**

`fc0feb20` の欠陥を最初に見つけたとき「小さい紙面を所有するフィクスチャでしか到達しない」と
**書いて残した**。直してみたら `test/tab-percent-repeat` が実際に踏んでおり、
**楽譜の半分以上が紙面外に描かれたまま出荷されていた**（viewBox 169.01 に対し Y=325.58）。
⚠️ **snapshot は「前回の自分」との比較なので、紙面外への描画を何度でも承認できる。**
到達性を主張するなら、コーパスを実際に走らせて数えること。

#### ★ 教訓: **`LILYPOND-REF` は「値の出典」であって「式の出典」とは限らない**

第4の乖離は算術ではなかった。**orphan penalty (100000) を LP に無い条件で課していた**——
Lily# は「最終ページが 1 システム」に課しており、それは**LP 自身の答え（5+1）そのものの形**。
force² の demerit が約 0.001 なので、この 1 つが全ページ分割を単独で決めていた。

LP の実際の規則は **markup 段落の泣き別れ**（`page-spacing.cc:375-383`、
`last_markup_line_`/`first_markup_line_` は markup の Prob 由来で、音楽システムの
`Line_details` は false 固定＝**音楽では絶対に発火しない**）。
旧コードは `page-breaking.cc:269` を引いていたが、そこは**値を読む行**で適用箇所ではない。
⚠️ **§5.2.1① の「REF があっても式が一致しているとは限らない」の実例。REF を見たら、
その行が『値』なのか『式』なのかを確かめる。**

#### ★ 教訓: **A4 では見えない量がある——regime を開くのはコーパスの仕事**

ページあたり本数の点を A4（book J）に足したら**最初から exact だった**。原因を測ったら、
**A4 では本数を容量が決めていない**——第1システムを 4 オクターブ（8 ledger 段）高くしても
LP は 13 のまま。ブレーカーは各候補ページが解く **force** から選んでおり、rod が天井に
当たっているのではない。**紙面を 70 ss に縮めて初めて force が効き**、LP 5+1 vs Lily# 4+2 が出た。
⚠️ **点が exact でも「その量が正しい」とは限らない。その regime に入っていないだけのことがある。**

#### ★ 教訓: **二重実装は、片方だけ直ると生き残る**

`cfdf85b4` が鎖から廃止した `Stretchability/60` は**ブレーカーに生き残っていた**——しかも
LP のブレーカーは stretchability を使わない（`inverse_hooke_ = full_height() + space`）。
§5.2.1② が言う「複製は移植が半分しか当たらない場所」の実例が、まさにその警告の対象で起きていた。

#### ★ 教訓: **byte 不変はこの種の移行の証拠にならない**（島1）

島1 の**成果は seam に出た**: `FindStaffYInSystem` が `system.Y - offsetDown` から
**`system.Y + staff.Y`**（LP 自身がやる素の和）になり、system 原点と staff 原点の間から
反射が消えた。`LayoutUtilities` では **`StaffOffsetInSystemUp` が primitive**（`staff.Y` を
そのまま返す）で `Down` がその否定＝**否定が「もう格納の姿ではない側」へ移った**。

その移行の検証について——生産側と消費側が**一緒に符号を反転すると打ち消し合う**ので、snapshot オラクルは
「緑だがフレームが逆」を素通しする。だから `StaffLayoutFrameTests` は**格納値そのもの**を
主張する — `staves[0].Y == 0` / `staves[i].Y < 0` かつ上の譜の下端をクリア /
`BraceTop > BraceBottom` / `TotalHeight > 0` / `Height >= 0`（長さは正のまま）/
2 つの accessor が厳密な反射で `system.Y + staff.Y` に合成される。
**3 つの overload（素・hara-kiri・skyline）は独立に積むので別々に固定した。**
⚠️ **「修正前なら落ちる」は実証済み**（`LilySharp.Core` だけ stash して 6/6 fail）。
§5.4 の原則をこの形で満たすこと——**先に測れないなら、格納値を主張するテストで代替する。**

#### ★ device 島の縁では「1 回だけ反射する」

`staffYByIndex`（`LayoutEngine`）と `staffYBySystem`（`LayoutEngine` / `OutsideStaffStacker`）は
下流の注釈エングレーバが**下向き offset を期待している**テーブルなので、島の縁で `-st.Y` と
**1 回だけ**反射させ、flip を下流へ伝播させなかった。`StaffOffsetInSystemDown` を残したのと
同じ理屈（§3B）。**反射を内側へ押し込まない。**

⚠️ **セッション中にユーザーが `fix/note-bang-diagnostic` を master に取り込んだ**
（`a994f418` `1ff10e45` `7d5255cb`）。**テスト数の基準が 3142 → 3155 に動いた**ので、
開始時に読んだ数と突き合わせるときは注意。予告どおり**1 ファイルも衝突しなかった**。

---

### 前セッション（2026-07-22）でやったこと — **譜間を測れるようにして、2 つ閉じた**

| commit | 内容 |
|---|---|
| `b3cfb119` | **2 段譜の台帳点を 2 つ起票**（probe P/Q）。出力不変。**予測を先に書いた** |
| `89aaa29f` | **幻の符尾を止めた**。`+1.450000 → −0.050000`。snapshot 3 件 |
| `854a0e95` | **五線をインクで種にした**（±2.0 → `staffHeight/2 + StaffLineThickness/2`）。`−0.050000 → 0` ×2。snapshot 14 件 |
| `28048fd8` | §1 書き換え |
| `7f2f8ff8` | **`StaffOffsetInSystemDown` 10 箇所を全数調査**。本物の移行は 2 箇所だけと判明し、それを実施（byte 不変・snapshot 0 件）。残り 8 箇所は**意図的な device 境界**＝`Down` は消さないのが正解、と shim の remark に記録 |
| `500627c9` | **決定 2 件を記録**（糖衣 `c?`/`c??` 不採用＋記号ルール、島1 の再スコープ）。docs のみ |

**台帳 22/29 → 24/31 exact、total |residual| = 0.023777 ss（不変）。** 新しい 2 点が両方 exact。

#### ★ 教訓: **対で足したから 2 つ目の欠陥が出た**

P（上の譜から下向きに出っ張る）と Q（下の譜から上向きに出っ張る）は**同じ算術の鏡像**なので
LP はどちらも `9.595000` を返し、**残差も同じ値になるはずだった**。ならなかった
（−0.050000 と **+1.450000**）。その差が**幻の符尾**:

- `SkylineBuilder.AddNoteBoxToSkylines` が**全ての符頭に** 3.5 の符尾を生やしていた。
  **コメントは「全音符には符尾が無い」と書いてあるのに、コードに音価の判定が無かった。**
- 描画側は `noteValue >= 2` で正しく分岐している（`SharedRenderer.Noteheads.cs`）。
  だから**全音符は符尾なしで描かれ、符尾があるものとして間隔が取られていた**。
- LILYPOND-REF: `lily/stem.cc` `Stem::is_normal_stem`（duration-log >= 1 のみ）。

⚠️ **片側だけ足していたら、−0.050000 が「予測どおり」に見えて終わっていた。**
README §「片側しか測っていない点は…」の縦版。**両側を足す。**

#### ★ 2 段譜の点は「素の 2 段譜」では作れない（設計の肝・再掲用）

LP は隣接する譜を `max(skyline距離 + padding, minimum, basic)` に置く
（`align-interface.cc:228-238`、StaffGrouper の 9 / 7 / 1）。**両側とも五線なら
2.05 + 2.05 + 1 = 5.1 で basic 9 が勝ち、五線の extent は出力に一切現れない。**
だから probe は **binding する X で片側だけが突出物**である必要がある
（P = 高音部譜の `d` が中央線の 6 下＋符頭 0.545 ↔ 低音部譜は自分の中央線）。
`staff-refpoint-extent` は system 内の全 staff refpoint の区間（`lily/system.cc:705-717`）
なので、**2 段 1 system なら `StaffGap()` がそのまま譜間距離**。実装追加は不要だった。

#### 参考: 縦の残差 4 点（合計 0.001365 ss）は原因を測って確定済み

`probes/glyph-skyline.ly` で grob に stencil と skyline を同時に聞いた結果:

| grob | `ext`（stencil） | `vertical-skylines` | Lily# |
|---|---|---|---|
| Clef G | (−2.550 . 4.800) | **(−2.540 . 4.776)** | bbox の −2.550 を使用＝**0.010 深い** |
| NoteHead | (−0.545 . 0.545) | (−0.545 . 0.545) | 一致（`22120764` が桁まで閉じた理由） |
| StaffSymbol | (−2.05 . 2.05) | (−2.05 . 2.05) | ✅ 一致（`854a0e95` で線のインクへ） |

**LP はグリフの skyline を stencil の描画プリミティブから作る**（`stencil-integral.cc`）ので、
輪郭が bbox の角から離れるグリフ（clef）だけ skyline が浅くなる。符頭は輪郭が bbox に接する
ので一致する。⚠️ **閉じるには輪郭ベースの skyline 生成が要る**
（⚠️ **「C# 側に OTF パーサは無い」と長く書かれてきたが、これは誤り**。`SkiaSharp` は
`LilySharp.Core.csproj:31-35` の依存で、`PngDrawingContext` が `SKTypeface.FromFile` /
`FromFamilyName` / `MeasureText` を**本番で使っている**。typeface のロードもグリフ輪郭も取れる。
実際に無いのは**レイアウト層がフォントに触る配線**の方——下記のフォント metric の項を読むこと）
（`ec7a2254` が LILC を選んだのと同じ制約）。**実測値を定数で埋めない**こと。

#### ✅ `StaffSymbol` の ±2.05 は閉じた（`854a0e95`）— **保留した判断が正しかった**

前セッションで一度直して**出荷せず戻した**のは「14 fixture が動くのに台帳点が 1 つも動かない」
＝改善かどうかを誰も判定できないため。**先に点を作る**という判断が正しく、作ってみたら
**2 つ目の欠陥（幻の符尾）まで出た**（§1 冒頭）。前セッションの実測「14 件・構造保存・
Y は −0.05〜+0.15」は今回の実測（14 件・X 完全不動・Y +0.03〜+0.15 と −0.05 が 1 件）と一致。

定数ではなく**導出形**で入れた: `_staffHeight / 2 + StaffLineThickness / 2`（§5.2.1⑤）。
`SeedStaffSymbol`（譜間用）と `SeedSystemStaffSymbol`（ページ用）の**両方**。

#### ✅ clef をスカイラインに入れた（`90efec02`）— **本体は padding 4 倍の方だった**

LP の clef は staff の VerticalAxisGroup スカイラインに入る**内部 grob**（`axis-group-interface.cc:914-940`）で、
素のスコアでは**上下ともインクの極値**（refpoint から下 3.550・上 3.800）。Lily# は種にしていなかった。

⚠️ **ただし clef 単独では直らず、一度差し戻した**（`c83a0551`）。狙った 2 点は閉じるのに
`system.natural-distance` が **+1.110000** に後退したため。**真因は別の欠陥**だった:

**`LayoutEngine` の単一ページ経路が system 間 padding に `SystemSpacing * 0.5 = 4` を使っていた。
LP は 1**（`paper-defaults-init.ly:62-65`・`page-layout-problem.cc:625-632`）。**4 倍。**
スカイラインが薄いうちは ink 項が basic-distance 12 に届かず**見えなかった**。clef が入って
ink が 9.110000（clef の下 5.550 と次 system の小節番号 3.560 が同じ左端で向き合う）に達した
瞬間に `9.11 + 4` が binding して露見した（LP なら `9.11 + 1` で効かない）。
同経路を `PageLayouter` の鎖と同じ `max(basic, max(minimum, ink + padding))` に揃えて解決。

⚠️ **教訓: 「新しい種を入れたら縦が広がった」を種のせいにしない。** 種は既存の欠陥を
**可視化しただけ**だった。LP が同じ音楽で動かないなら、動いた側が間違っている。

#### ✅ 決着: 休符の実インク化は**やらなくてよい**（測って否定した）

`22120764` の続きとして「休符も名目 1.0 → `GetRestBBox`」を予定していたが、**実測で棄却**。
128分休符（グリフ下端は中央線の 3.05 下）を敷き詰めた probe と、素の音符の probe が
**LP で 1 ビット違わぬ同じ値**（11.716074 / 12.255229）を返した。休符は中央線に座るので
**縦インクが極値になることが無く**、スペーシングに効かない。箱が名目なのは事実だが**不活性**。

---

### ⚠️ このセッションで踏んだ測定の罠（同じ手を繰り返さないこと）

- **要約された数字から逆算しない。** 「残差 0.044994＝五線の線幅の半分」と一度結論したが、
  これは**丸めた gap から算術した私の誤り**。probe は元から**全 system の生データ**を出しており
  （要約しているのは測定スクリプトの方）、直読みすれば 3.550000000 だった。
- **stencil の extent と skyline は別物。** LP の spring の床が使うのは **skyline**
  （3.540〜3.545）で、stencil の extent（3.550）ではない。
  この2つを突き合わせて「未知の 0.005」を作り出していた。**どちらの量かを必ず言うこと。**
- **probe が何を測っているか確かめてから信じる。** 休符 probe は休符を測っておらず、
  中央線上の音符 probe は**下向き符尾**が clef を隠していた。`a`（中央線の1段下）で初めて
  clef が単離できた。**両方とも罠として probe のヘッダに書いてある。**

---

**HEAD = `87bcde22`・origin より未 push**（`git rev-list --count origin/master..master` で裏取り＝
このセッション開始時 3、`87bcde22` を足して 4。旧記述の「100 ahead」は `git rev-list` と食い違う
stale 値だったので撤去）。push はユーザー判断・コミットは可。
⚠️ **push は明示的に「まだしないで」と言われている**（2026-07-22）。解除まで push しないこと。
⚠️ **未 push には大規模 snapshot 再ベースが多数**（フォント移行 **194 件**・TimeSignature **188 件**
・フォント差し替え 186/192・タプレット 9/14 件・強弱 16 件ほか）**が含まれる**（`87bcde22` は出力不変）。
別ブランチ `fix/vscode-extension`（`7291531a` から）と `fix/note-bang-diagnostic` は
**どちらも master に取り込み済み**（ユーザー）。**走っている別ブランチはもう無い。**
**テスト 0 failed / 3199 passed / 3 skipped。** Core build 0 warn / 0 err。
**LP 忠実度 29/42 exact, total |residual| = 1.044394 ss over 38 distances ＋ counts 4/4**
（**2.26.0 基準**。X 22点＋Y 7点＋譜間 **5点**＋**スラー譜間 2点**＋**system 間タプレット 2点**＋
**ページ本数 4点**。スラー 2 点 −0.512596 ×2＝新規可視化・未 fix。§3A）。
⚠️ **本数の点は距離ではないので ss の総和に入れない**（`unit` フィールドで分離。`page.height` を
台帳から落としたのと同じ理由——1 system を 0.019202 ss に足すと指標が意味を失う）。
**タプレット 4 点は全て +0.0000208**（`caa0f239` `075277ff` `b36db266`。Pango 量子化の半分）。
**`barline.next.time-change-to-notehead` は +4.3e-7（`033c0df8`。Pango 量子化を字面移植して閉じた）。**
⚠️ **途中 2.214721 まで増えたのは悪化ではなく、誰も測っていなかった乖離の可視化**だった（§5.2.1④）。
起票 −1.700225 ×2・−1.295225 ×2 → 予約と符尾で −0.547717 ×4 → 実フォント計測で **+0.0000208 ×4**。
**譜間の残り 3 点は 2 点 exact**（`854a0e95`）**＋ −0.000076**（`26afa9fe`。Pango 量子化）。
**縦 7 点の合計は 0.001365**（4 点が上記の skyline sliver、3 点は exact）。
X 3 点は `e38a76bf` から不変。
**作業ツリーはクリーン**（未追跡の旧 `HANDOFF-*.md` 14個 ＋ `demo-lp-compat-features.lys`
＋ `audit/scripts/__pycache__/` を除く。§8・§7-8）。

⚠️ **2026-07-22 に履歴が書き換えられた（コミット日時の変更）。** メッセージは不変だが **SHA は全部変わった**。
本ファイル・`COORDINATE_AUDIT.md`・`audit/lp-geometry/README.md` の参照は張り替え済み（28件、到達性を検証）。
**同じことが起きたら、subject で新旧を突き合わせて張り替えること** — `git log -1 --format=%s <旧SHA>`
は書き換え後も dangling オブジェクトとして引けるので、そこから `git log --format='%h %s' master` を検索する。
⚠️ `git cat-file -t` では判定できない（旧コミットは gc されるまで残る）。**`git merge-base --is-ancestor` を使う。**

### ✅ LP の「正」は **2.26.0** に確定（2026-07-22・版分岐は解決済み）

src・binary とも 2.26.0 で揃った。**もう版の混成は無い。**

- `C:\MyProj\lilypond-src` は **`v2.26.0`（`3596756be0`）を detached HEAD で checkout**。
  旧 HEAD `bc68038f76`（2.25.35 devel）はその直接の祖先で 128 commit 前だった
- 実測 binary は **`C:\bin\lilypond-2.26.0\bin\lilypond.exe`**（`audit/lp-geometry/*.ps1` の既定値も更新済）
- ⚠️ **PATH 上の `python` は今も 2.24.4 同梱のもの**で pip も fontTools も無い。
  `audit/scripts/*.py` は **`py -3.13`** で起動すること
- ⚠️ `C:\bin\lilypond-2.24.4` は残っている。**比較目的以外で使わない**

`ly/paper-defaults-init.ly` と `scm/define-grobs.scm` の 2.25.35→2.26.0 差分は**著作権年のみ**なので、
旧記述の「src 2.25.35（正）」列の値がそのまま 2.26.0 の値。**2.24.4 との差は下の §2⑧ の表に集約した。**

### ⚠️ 2.26.0 は Emmentaler を作り直している（**このセッションの最大の発見**）

**① PUA コードポイントは版をまたいで安定しない。** 2.26.0 はグリフを 34 個挿入したため、
Lily# がハードコードしていた 115 定数のうち **73 個の割り当てがずれた**
（`U+E085` は `clefs.G` でなく `clefs.varC`、`U+E0EA` は `noteheads.s2` でなく `flags.stackedu7`）。
LP は常に feta **名**で引く（`lily/clef.cc:29-52`、`lily/note-head.cc`）。
→ `EmmentalerGlyphs.Generated.cs` を **名前引きで生成**するようにした（`a2ceb2f0`）。
**名前がフォントに無ければ生成器が exit 1 で落ちる**のが唯一のガード。実行時にフォントを読む配線が無いので
テストでは固定できていない。**生成器を CI で回して差分ゼロを assert するのが残件。**

**② LILC テーブルが zlib 圧縮になった**（114580 → 6175 バイト。otf が半分になる主因）。
`lily/open-type-font.cc:78-123` が透過的に inflate し、非圧縮ならそのまま読む。
⚠️ **`Extract-EmmentalerMetrics.py` が inflate しないと LILC を 0 件と判定してアウトライン
fallback に落ちる** — それは `ec7a2254` が「非 LP 方式」として捨てた経路。対応済み。

**③ LILC の bbox が design 単位で小数3桁に丸められた**（`noteheads.s2` 6.52106 → 6.521）。
共有 628 グリフ中 566 個が最大 7e-5 ss 動く。**台帳 22 点中 10 点の LP 実測値がこれで動いた。**

### 残る残差は **22点中3点 0.022412 ss**

| 残差 | 点数 | 原因 |
|---|---|---|
| −0.017672 | 1 | **水平スカイライン項**（未移植）。`barline.next.key-change-to-notehead`（♮）。LP は臨時記号の右スカイラインを符頭のスカイラインと測る（`accidental-placement.cc:412`）が Lily# は box で測る |
| −0.004735 | 1 | **TimeSignature grob 幅** 1.600000 / LP 1.604735。原因特定済＝LP は markup を組むので**テキストレイアウト経路**が幅を決める（下の ⚠️）。もう OPEN ではない |
| +0.000005449 | 1 | `barline.next.down-stems-after-clef`。**2.26.0 のフォントで新規発生・`OPEN:`**。LP の光学補正が 0.189365 → 0.189360 と動いたのに Lily# は 0.189365449 のまま。丸めのスケール内だが、どのメトリクスが `/7` を通って 5.4e-6 を運ぶかは**未特定** |

⚠️ **♯ の 2 点（`barline.next.accidental-to-notehead` / `midmeasure.key-cancel.key-to-next-note`）が
exact 化したのは「直った」からではない。** 2.26.0 で `accidentals.sharp` の右端が 1.100000
ちょうどになり、スカイライン項（−0.000010）が**見えなくなった**だけ。水平スカイラインは今も未移植で、
**それを測っている点は ♮ の 1 点だけになった**。台帳の `why` にも同じことを書いてある。
（2 点が同時に、どちらも 0.000010 で閉じたことは「同一原因」という以前の読みの裏取りにはなった。）

**X 軸の「定数を1つ直せば閉じる」ネタは尽きた。** 残る3点のうち2点は**Lily# に無いパイプライン**が
要り（水平スカイライン1点／テキストレイアウト1点）、1点は `OPEN:`（5.4e-6）。
§2⑧ の余白4定数は `e38a76bf` で、Y 台帳の起票は `0c0d8f38` で、
`page.first-staff-refpoint` の −3.000000 は `b94487ad`＋`1dfb62d7` で閉じた。
（**伸長 regime の点も済み**——`6ffbe7bd` で起票、`cfdf85b4` で鎖として移植。現在
`page.stretched.first-staff-refpoint` −0.000042 / `system.stretched-distance` −0.000414。
**次の一手は §1 の冒頭を見ること。**）

⚠️ **TimeSignature 幅 −0.004735 は「定数を直す」問題ではない**（2026-07-22 に実測で確定）。
`ly:time-signature::print` は **markup を組んで `grob-interpret-markup` に渡す**
（`scm/time-signature.scm:18-29`）ので、幅は**テキストレイアウト経路**が決める。同一 grob 上で:

| 経路 | 桁4 |
|---|---|
| `ly:font-get-glyph "fattened.four"`（音楽フォント＝このコーパスが使う経路） | **1.600000** |
| `(markup #:number "4")`（print が実際に通る経路） | **1.604735** |

**Lily# の 1.600000 は誤りではなく、LP 自身の音楽フォント経路の値と一致している。**
10桁すべてについて棄却済み: LILC bbox / 生 outline / advance / plain 族(U+0030-0039) /
fattened 族(U+E0B4-U+E0BF) / フォントビルド差（LP 同梱と repo は別サイズだが桁 metric は同一）。
markup 値は 0,2,8 / 3,5,7 / 6,9 が同値になるが、**フォント内のどの metric もその group を作らない**。
2桁文字列も1桁幅の和にならない（±0.102430）。**閉じるにはテキスト経路の metric が要る。**
⚠️ 以前ここに書いた「advance のまま＝§2④ の取りこぼし」という見立ては**誤りなので採用しない**。

### 直近セッション（2026-07-22 最終）でやったこと — **先頭 system・鎖・プレビューのフォント**

| commit | 内容 |
|---|---|
| `6ffbe7bd` | **伸長 regime を起票**（probe W ＝ V と同じ音楽を 150 小節）。着手前に予測を書いてから測定: `page.stretched.first-staff-refpoint` **−0.025482000（予測どおり）** / `system.stretched-distance` **+0.089206333**（予測は向きのみ的中） |
| `cfdf85b4` | **ページを LP の spring 鎖として解く**（`Page_layout_problem` ＋ `Simple_spacer` の字面移植）。**0.114688 → 0.004090**。転記誤り2件を同時に廃止（下記）。**snapshot 2 件再ベース** |
| `a7b96569` | 生 dump を直読みして**残差の原因の記述を訂正**（コード変更なし）。要約からの逆算が誤りだった |
| `22120764` | **縦スカイラインを符頭の実インク（LILC ±0.545）へ** — `ec7a2254` の縦版。**0.004090 → 0**、**縦は 5/5 exact**。**snapshot 80 件再ベース**（全件で行数・グリフ数一致＝構造保存、Y は −0.05〜+1.80） |
| `769529ed` | **clef がスカイラインに無いことを測れる probe S を起票**（出力不変）。予測を先に書いて桁まで的中。休符案は実測で棄却（上記） |
| `058ab13a` | `VerticalSkyline.Distance` の契約を単体テストで固定（**スカイライン算術は無罪**と確定） |
| `0b30f53d` | **座標系監査**を `COORDINATE_AUDIT.md` §2.1 へ。`staffMiddleY` が 9 ファイルで逆向きの 2 意味だった |
| `a8c75679` | **19 シンボルを `Down` 付きへ改名**（ユーザーが MSVS で実施）。純リネーム |
| `39da7084` | **Y-up shim を開設**（`StaffOffsetInSystemUp`）＋ `OutsideStaffStacker` を移設。**snapshot 0 件** |
| `511ab68c` | **`SkylineBuilder` を Y-up へ**（`ToSystemUp` が単なる加算に）。**snapshot 0 件** |
| `90efec02` | **clef を種に＋単一ページ経路の padding を LP の 1 へ**。0.162412 → **0.023777 ss**。**snapshot 82 件再ベース**（構造保存・縦のみ最大 −6.88） |
| `b94487ad` | **先頭 system の配置を refpoint フレームへ**（§2⑧ の順序①）。`CalculateFirstStaffRefpoint` を新設し、`CalculateFirstSystemY` を **halfStaff 変換の唯一の seam** に。3 呼び出し元とも spec を渡す。**出力不変・snapshot 0 件** |
| `1dfb62d7` | **先頭 system を top-system spring に載せた**（順序②）。`max(basic-distance, ink + padding)`。**`page.first-staff-refpoint` −3.000000 → 0**、22/25 exact・0.022412 ss。**snapshot 2 件再ベース**（`programmatic/hara-kiri{,-paged}`、全て +3.00） |
| `99baed0f` | **`Deploy-Lsp.ps1` が `media/` を配っていなかった**のを修正。プレビューの符頭が三角になっていた原因（下記） |

⚠️ **「全 snapshot が動く」という §2⑧ の予測は外れた。** `max()` は**五線上のインクが薄いときだけ**効くので、
**190 件の `.lys` snapshot は 1 バイトも動かず**、動いたのは programmatic 2 件だけだった。
これは「無害の証拠」ではなく**フィクスチャの穴**（先頭 system が疎なフィクスチャが1つも無い）。
台帳の点が LP に対して固定しているので優先度は低いが、**視覚フィクスチャは未追加**。

### ⚠️ プレビューの符頭が三角になるのはフォントの配り忘れ（`99baed0f` で解決）

`tools/Deploy-Lsp.ps1` は `server/` `syntaxes/` `out/` `package.json` だけを配り、
**`media/`（webview 自身の Emmentaler）を配っていなかった**。`070f1e21` で 2.26.0 に差し替えた後、
サーバは新コードポイントを出すのにプレビューは **2.24.4 の woff2 を掴んだまま**になり、
**115 定数中 73 個の割り当てズレ**がそのまま別グリフとして描かれていた（符頭が三角）。
**レイアウトのバグではない。** 実測: repo 52116 バイト `13D75317…` / 配布済み 50248 バイト `D207674F…`。
⚠️ 同じ症状を見たら**まずインストール済み拡張の `media/fonts/*.woff2` をハッシュで照合する**こと。
CLI の PNG は `LilySharp.Core\Fonts` を埋め込むので**正常に見える＝切り分けに使える**。

### その前のセッション（2026-07-22）でやったこと — **2.26.0 への移行**

| commit | 内容 |
|---|---|
| `a2ceb2f0` | **グリフ表を feta 名引きに**（`EmmentalerGlyphs.Generated.cs` ＋ `Extract-EmmentalerGlyphs.py`）。**出力不変・snapshot 0 件**。副産物で既存の誤りを 5 件発見して修正（下記） |
| `070f1e21` | **フォントと台帳を 2.26.0 へ**。otf 2 個＋派生 woff/woff2 4 個を差し替え、生成器2本を zlib＋名前引きに、弓記号の方向ペアを移植、`lp-geometry.json` を 2.26.0 実測へ、テスト7ファイルの旧 codepoint 直書き 31 箇所を定数参照へ。**snapshot 186 件再ベース** |
| `7291531a` | **3 ページのフィクスチャ `test/multi-page-vertical`**。§2⑧ が警告していた「複数ページを踏む fixture がゼロ」を埋めた |
| `e38a76bf` | **紙面 5 定数を LP の単位で読み直した**（§2⑧ 完了）。**snapshot 192 件再ベース** |
| `0c0d8f38` | **台帳を Y に開いた**（3 点）。`RenderedGeometry` を全ページ対応に。出力不変 |

**`a2ceb2f0` で見つかった既存バグ 5 件**（現行フォントに対しても誤っていた）:

| 定数 | 実際に描かれていたもの | 修正後 |
|---|---|---|
| `Fermata{Short,Long}{Above,Below}` | **Henze フェルマータ** | 通常の `scripts.u/dshortfermata`・`u/dlongfermata` |
| `ArticThumb` | **`scripts.snappizzicato`** | `scripts.thumb` |

LP は両者を別の articulation として持つ（`scm/script.scm:356` shortfermata ↔ `:183`
henzeshortfermata、`:220` ↔ `:174`）。**どのフィクスチャも踏んでいないので snapshot は動かない**
＝コーパスはこの修正を見ていない。articulation を次に触るとき fixture を足すこと。

**弓記号は本物の LP 挙動変更**: 2.24.4 は `("downbow" . "downbow")` と上下で同一グリフだったが、
2.26.0 は `(ddownbow . udownbow)` / `(dupbow . uupbow)` に分割（`scm/script.scm:88`・`:453`）。
旧グリフはフォントから消えている。`ArticulationItem` / `ArticulationEngraver` を方向選択に移植した。

⚠️ **snapshot 186 件の再ベースは全行分類してから承認を取った**: 3277 行がコードポイントのみ、
26 行がコードポイント＋座標、108 行が座標のみ、**座標の最大変化は 0.01＝`F2` 丸め 1 単位**。
36 種のコードポイント移動のうち **35 種が両フォントで同一 feta 名**、残る 1 種が上の弓記号の分割。
**記号の同一性は変わっておらず、レイアウトも丸め以上には動いていない。**

**判明した2つの訂正**（どちらも過去の記述が誤り）:

1. **`11.528` は圧縮ではなく基準点の取り違えだった。** `staff-refpoint-extent` は system ごとに
   違う（小節番号を頭上に持つ system は原点がその分上に伸びる）ので、**system 原点間**で
   測ると間隔が一様でも値がばらつく。同じダンプが原点間で 11.528583 / 12.000000、
   **staff refpoint 間では 12.000000 / 12.000000**。LP はそこで圧縮していなかった。
   → **縦は staff refpoint 間で測ること**（§5.3 に追記済）。
2. **`ragged-last-bottom` は「伸ばさない」ではない。** `page-breaking.cc:570-573` は最終ページに
   **直前ページと同じ force** を渡す（`fixed_force_solution(last_page_force)`）。
   `last_page_force` の初期値 0（`:643`）なので**単ページ書籍だけ**自然長。
   Lily# は無条件に force 0 を入れていた＝**最終ページだけ違って見える**というユーザー報告の
   直接原因。`c353bc85` は「1ページに何本入るか」を直したが「最終ページをどう詰めるか」は
   残っていた。

⚠️ **snapshot が 1 件も動かないのは、複数ページを踏む committed フィクスチャが1つも無いから。**
「変更が無害」の証拠ではなく**フィクスチャの穴**。

### その前のセッション（2026-07-22 前半）でやったこと

**§2④' と §2⑥ を完了。§2⑤ は既に完遂済みと判明。** どちらも予測が全点的中した。

| commit | 内容 |
|---|---|
| `94e8996c` | **§2④' 実装**。`AccidentalNoteGap` 0.2 → **0.35**（LP の `padding` 0.2 ＋ `right-padding` 0.15）。**3点とも予測が桁まで的中**。snapshot 22件（全て `test/*`、showcase はゼロ） |
| `8bc90025` | §2⑤ は `098f5279`＋`8448749a`＋`94656b84` で完遂済みと判明。元 handoff を回収して §2⑤⑥⑦ に整理 |
| `a64ffc16` | **§2⑥ 実装**。`fills_measure` を LP の musicality 判定へ。**変更前に点を足して予測 −1.000000 を記録 → 実測 −1.000000000 → 修正後 0**。snapshot 2件 |

**0.338987 → 0.022361 ss、17/21 → 18/22 exact。** ④' の exact 数は予測どおり動かない
（−0.000010 は tolerance 1e-6 より大きい）。⑥ で足した点が exact で着地して +1。

参考: この前の 2026-07-21 セッションは §2①③④ と §2② の掃除を入れた
（`ec10fd4b` 計測ハーネスの stderr 混入修正 / `f4b94f64` 行中変更 item の LP モデル導出 /
`4eb8cf16` §2① / `ec7a2254` §2④ LILC / `d056b5e5` §2③ 境界列 / `2745d603` §2② 掃除）。
**台帳の値自体は全部正しく、壊れていたのは再現手段のほうだった。**

### 進行中で中断しているものは無い

**X 軸は行中・行頭・臨時記号とも完結。**

⚠️ **`total |residual|` の履歴は点集合が違う。同じ集合の中でだけ比較すること。**
15点 4.592405 → 19点 11.435647（`5c4126d6` が行中4点を追加＝それまで測っていなかった発散の可視化）
→ 21点 4.747978（`4eb8cf16`＋MKA 2点）→ 4.738987（`ec7a2254`）→ 0.338987（`d056b5e5`）
→ 21点 0.022361（`94e8996c`）→ 22点 **0.022412**（`070f1e21`）。

⚠️ **`070f1e21` で比較の基準そのものが 2.24.4 → 2.26.0 に変わった。**
点集合は同じ 22 点だが LP 側の実測値が 10 点動いているので、**0.022361 と 0.022412 は
厳密には別物差**（悪化ではない。exact は 18 → 19 に増えている）。
**この行より上の日付が古い記述に出てくる LP 実測値は全部 2.24.4 のもの**なので、
数値を引き写すときは版を確認すること。

---

## 2. 短期ロードマップ（次の数セッション）

優先順。**①②は COORDINATE_AUDIT §4.7 の残り**で、ユーザー合意済みの順序。

### ① ✅ 完了（`4eb8cf16`）— 行中（mid-measure）の変更 item を LP の専用列として価格付け

> **2026-07-21 に再スコープ → 実装。** 旧①は「3つの extent ヘルパの frame ＋ 定数2つ」だった。
> **着手前に測った結果、それでは 4 点は 0 にならないと分かった**（§5.3「変更する前に測る」が
> 効いた例）。導出済みモデルと実測は `COORDINATE_AUDIT.md` **§4.7.2**。
>
> **結果**: 行中4点 6.843242 → 0.000453 ss（`clef→次の音符`は**厳密一致**）。
> 残差は全て `GlyphMetricsGenerated.cs` の4桁丸め（符頭 1.3040 vs LP ink 1.304212 等）。
> **残った未了**: 分配が正しいのは force 0 のみ。LP は2本の spring を独立に伸ばし、
> key/time の右側は**伸びない**（`shrink-space`/`semi-shrink-space`）。これは本物の
> 第2列が要るので③と同じ仕事。`SpacingRules.MidMeasureChangeGaps` に記録してある。
>
> 以下はモデルの記録（③も `d056b5e5` で完了済み）。

**LP は行中の clef/key 変更に non-musical 列を1本立て、左右を別の式で価格付けする**:

| | 式 | 出典 |
|---|---|---|
| 列原点 | 変更グリフの **ink 左端** | 実測 |
| 左 gap | `max(ideal − 列幅, (ideal + min_dist)/2)` ＝実際は常に床側 | `note-spacing.cc:105-107` |
| 右 gap | `ink幅 + space-alist 距離` | `staff-spacing.cc:147-198` |
| 　clef | `next-note` **1.0** | `define-grobs.scm:924` |
| 　key | `next-note` が**無い**ので `first-note` **2.5**（shrink） | `define-grobs.scm:1947` |
| 　time | 同じく `first-note` **2.0**（semi-shrink） | `define-grobs.scm:3948` |
| `min_dist` の左 esw | Clef=既定 0.1 / Key=**0.0** / Time=**0.0** | `define-grobs.scm:1936` / `:3933` |

**MC/MK の左右4点すべて 6 桁一致でモデル確定済み。**

Lily# 側は列が無く、`ChangeItemPrefixWidth`（= W + 2×0.5）を**1本の spring に丸ごと加算**し、
描画側が `列X − (W + 0.5 + 次の臨時記号)` で**ぶら下げる**。実測分解:
`head2→head3 = (1.304 + CalculateLeftExtent(clef) 1.505 + 0.4) + 3.010 + 0.3 = 6.519000`（実測一致）。

→ **frame だけ直すと `+1.119 → +0.612` に減るが、左右の分配は変わらず逆符号のまま。**

**同時に直したもの**: 変更 clef の幅 `FClefAdvance × 0.75 = 2.010` → LP の `clefs.F_change`
ink **2.146680**（現在は `ec7a2254` で生成器から LILC 由来）。

### ② ✅ 完了 — extent ヘルパの中心基準と、①③が殺したシンボルの掃除

**出力は完全に中立**（snapshot 0件、LP 忠実度 17/21・0.338987 のまま）。削除したもの:

| シンボル | 経緯 |
|---|---|
| `SpacingRules.ChangeItemPrefixWidth` | ③が最後の呼び出し元を外した |
| `SharedRenderer.FollowingAccidentalLeftExtent` | ①で臨時記号が rod 経由になった |
| `SpacingRules.CalculateRightExtent` | 元からの②。テストを `CalculateNoteheadRightExtent`（左端基準）へ寄せた |
| `GlyphMetrics.ClefChangePadding` | 上の掃除で参照ゼロに。**そもそも LP の量ではなかった**（0.5 は `right-edge`） |

`CalculateLeftExtent` / `CalculateNoteheadRightExtent` の変更 item 分岐は
**左端基準（0 / 全幅）**へ。

⚠️ **副産物で本物のバグが1つ出た**: `MeasureLayouter.ItemStartingAt` が zero-duration の
変更 item を返しており、**音符列同士の rod が変更グリフから測られていた**。
音符を返すよう直したところ、分岐が到達不能になり、かつ出力が中立に戻った
（つまり従来は rod が binding していなかっただけで、式は間違っていた）。

⚠️ **未検証で残したもの**: `GetItemToBarlineSpace` / `GetBarlineToItemMinimum` の
変更 item エントリ（`1.0 / 1.0 / 0.75`）。「中心基準だから」という根拠は消えたが、
**LP と照合し直していない**。到達経路は「変更 item が小節の最後の timing を共有する」
という **LP に存在しない構図**だけで、fixture も踏まない。

### ③ ✅ 完了（`d056b5e5`）— 行頭 key/time の境界列

モデルは `COORDINATE_AUDIT.md` §4.7.3。**着手前の予測が4点とも桁まで的中**した:

| 台帳キー | 実装前 | 予測 | 実測 |
|---|---|---|---|
| `barline.next.key-change-glyph` | −0.500000 | 0 | **0** |
| `barline.next.time-change-glyph` | −0.250000 | 0 | **0** |
| `barline.next.key-change-to-notehead` | −2.234272 | −0.034272 | **−0.034272** |
| `barline.next.time-change-to-notehead` | −1.454735 | −0.004735 | **−0.004735** |

**4.439007 → 0.039007 ss、exact 15/21 → 17/21。** snapshot 14件。

**未モデル化（意図的）**: LP は key 変更を `KeyCancellation` と `KeySignature` の**2 grob**に分け
間に 0.5 を置くが、Lily# は1つの `KeySignatureChangeItem` に畳んでいる。コーパスは踏まない
（probe K は 0→3 個で cancellation が出ない）。`BoundaryChangePrefix` に記録。

⚠️ **`BoundaryColumn.cs`（clef を bar line の前に置く既存の型）とは別物**。今回入れたのは
`SpacingRules.BoundaryChangePrefix` ＋ `BarlineToFirstColumnSpring` の `last_grob` 切替。
両者の統合は未着手。

### ④' ✅ 完了（`94e8996c`）— 臨時記号 → 符頭の距離

`GlyphMetrics.AccidentalNoteGap` は LP の `padding` 0.2 **だけ**で、`right-padding` 0.15 が
抜けていた（`accidental-placement.cc:397` / `:400`、適用は `:412-416`）。**0.35 に。**
式・摂動法の裏取り・グリフ別のスカイライン項は**その定数の `<remarks>` に全部書いてある**。

| 台帳キー | 実装前 | 予測 | 実測 |
|---|---|---|---|
| `barline.next.accidental-to-notehead`（♯） | −0.149990 | −0.000010 | **+0.000010** |
| `midmeasure.key-cancel.key-to-next-note`（♯） | −0.149990 | −0.000010 | **+0.000010** |
| `barline.next.key-change-to-notehead`（♮） | −0.034272 | −0.017606 | **−0.017606** |

**0.338987 → 0.022361 ss。** 絶対値は3点とも桁まで的中（符号は逆＝0.35 が LP の 0.349990 を
わずかに超える分）。exact 数は予測どおり不変。snapshot 22件。

**残った未了 = スカイライン項そのもの**（意図的に未移植）。LP は `:412` で臨時記号の右
**スカイライン**を符頭のスカイラインと測るので、縦に細いグリフだけ box より外に出る
（♮ +0.017606 / ♯♯ +0.047704 / ♯ −0.000010 / ♭ −0.000004 / ♭♭ −0.001996）。
移植には**水平スカイラインの基盤**が要る（§3B②の島に接続）。

### ④ ✅ 完了（`ec7a2254`）— グリフメトリクスを LILC 由来に

**LP はグリフ bbox を、フォント埋込の `LILC` テーブルから読む**
（`lily/open-type-font.cc:288` `load_scheme_table("LILC")` ＋ `:389-407`。生アウトラインは fallback）。
`GlyphMetricsGenerated.cs` はアウトライン（`BoundsPen`）から取っていた＝**非 LP 方式**で、
これが台帳に残っていた 1e-4〜1e-3 級の残差の**唯一の原因**だった。

入れたもの: 生成器を LILC 優先に／出力を **6桁**に／`ApplyLeftHeadWidth` と
`GetKeySignatureAccidentalWidth` を **advance → ink extent** に／変更 clef も生成器から。

**7点が 0 に**（`barline.prev.whole-note` `.half-note` `barline.next.down-stems-after-clef`
`midmeasure.clef.prev-note-to-clef` `midmeasure.key.prev-note-to-key`
`midmeasure.key.key-to-next-note` `midmeasure.key-cancel.prev-note-to-key`）。
**8/21 → 15/21 exact。** snapshot 184件・承認のうえ再ベース。

⚠️ **踏んだ罠（再発しやすい）**: 生成 bbox から派生する定数を `static readonly` にすると、
**partial クラス間の静的初期化順序は C# で未定義**なので既定値の `BBox`（=0）を読む。
変更グリフの幅が全部 0 になり clef が自分の gap から消えた。**プロパティにすること。**

⚠️ `down-stems-after-clef` が閉じたのは**予測外**。残差 +0.00002 は「符尾の符頭接続オフセット差」
と帰属されていたが、実際は符頭メトリクスだった。**帰属は閉じてみるまで確定しない**例。

### ⑤ ✅ 完了 — MMR run のグルーピング

**「LP は clef があっても run を保つが Lily# は弾く」は既に解消済み。** 3層とも入っている:

| 層 | commit |
|---|---|
| run グルーピング（`IsBreakAlignedChange` に `ClefChangeItem`） | `098f5279` |
| 描画（clef を bar line の**前**へ＝`BoundaryColumn` / `BoundaryClefAllowance`） | `8448749a` |
| 視覚フィクスチャ `test/mmr-clef-change-bound` | `94656b84` |

2026-07-22 に LP 2.24.4 と PNG を並べて再確認済み（単一 "5" ＝ longa 4 ＋ whole 1、bass clef は
bar line の前）。旧 handoff が「描画位置の方針をユーザーに仰げ」としていた論点は**測定で解決済み**:
clef は列の**原点**を左へ動かすだけで bar line は動かさない（bar line 間は clef の有無によらず
14.133856）。だから rod には足さず、`BoundaryClefAllowance` として**前の小節の閉じ側**に付ける。

**元資料 `handoff-2026-07-21-mmr-runs.md` は本項をもって回収完了**（§8）。残っていた未解決は
下の ⑥⑦ に移した。

### ⑥ ✅ 完了（`a64ffc16`）— `fills_measure` の述語を LP に合わせた

`spacing-spanner.cc:446-472` は**列の musicality しか見ない**のに、Lily# は2箇所とも
`NoteItem or ChordItem` に絞っていた＝**全休符の小節に `full-measure-extra-space` 1.0 が
付かなかった**。2箇所を同時に `SpacingRules.IsMusicalColumn` へ。

⚠️ **ここで推論が外れた。** 「musical＝duration を持つ」ではない。`Paper_column::is_musical` は
`shortest-starter-duration` を読む＝**engrave された grob が決める**ので、**skip は非 musical**。
摂動実測（`full-measure-extra-space` → 0、LP 2.24.4）:

| 列 | 差 | |
|---|---|---|
| `r1` | **−1.000000** | 効く |
| `R1` | **−1.000000** | 効く |
| `s1` | 0 | **効かない** |
| 四分音符×4 | 0 | 効かない |

**コーパスは構造的に盲目だった**: スコア F は閉じ側（`barline.prev.whole-rest`）しか測っておらず、
fmes が乗るのは開き側。`barline.next.whole-rest` を足し、**`residual: null` ＋ 予測 −1.000000 を
先に書いてから**実装 → 変更前 −1.000000000 → 変更後 **0**（LP 1.900000 = Lily# 1.900000000）。

**副産物**: `SpacingInvariantTests.FullMeasureRest_CompactsOnCombinedPath` の
`< noteWidth / 2` は**LP 自身が満たしていない**主張だった（LP の `R1` 小節 7.890000 :
四分音符4つ 13.525735 ＝ 0.583）。`* 0.6` に緩め、**移植定数ではなく粗い gate** だと明記。

**残った未了**: `R1` は幅を MMR rod 経由で決めるので、この spring は rod が binding しない
場面でしか効かない。LP の `max()` と全ケースで一致するかは未測定。

### ⑧ Y 軸（ページ縦方向）— 着手済み。**残りは「Lily# は圧縮できない」**

**ユーザー報告「複数ページで最終ページだけ改行幅が狭い」を実測 → 原因はページブレーカーだった。**

LP 2.24.4 実測（`ragged-bottom` ＋ `systems-per-page` を絞り、伸長も圧縮も起きない条件）:
**自然 system 間距離 = 12.000 ちょうど。Lily# の最終ページも 12.000 で厳密一致。**
つまり**最終ページが正しく、それ以外のページが伸びすぎていた**。

`c353bc85` で `tallness_` / `spring_length` / `refpoint_extent_` を字面移植（§2⑥ と同じく
「rod と spring の二重計上」だった）。A4・30 反復のプローブで:

| | 変更前 | 変更後 | LP |
|---|---|---|---|
| ページ1 のシステム数 | 11 | **13** | 14 |
| ページ1 の gap | 14.55 | **12.12** | 11.528 |
| 最終ページの gap | 12.00 | 12.00 | 12.000 |

ページ間の見た目の差 **2.55 ss → 0.12 ss**。

#### ✅ 最終ページの force 継承は完了（`4ac3df8e`）

`page-breaking.cc:570-573` — `ragged-last-bottom` は「伸ばさない」ではなく、最終ページに
**直前ページと同じ force** を渡す。移植して予測どおり **12.000000 → 12.450000**（page 1 と一致）。
単ページ書籍は `lastPageForce` 初期値 0 のまま＝ 12.000000 で不変。両方テストで固定済み。

#### ✅ 紙面・余白の 5 定数は完了（`e38a76bf`）

**原因は単位の取り違えだった。** LP の point は `lily/include/dimensions.hh:27` の
`INCH_TO_PT = 72.270`＝**TeX point**（1/72.27"）で、PostScript の big point（1/72"）ではない
（`:31` に `INCH_TO_BP = 72` が別にある）。既定 staff は 20pt なので
**1 staff space = 5pt = 127/72.27 mm = 1.757299 mm**。168.4 は同じ 5pt を PostScript point で
換算した値だった。全定数は `<mm> × 72.27/127` で導ける（下表の「LP 2.26.0」列と 6 桁一致）。

**実測した影響**（推測でなく）: 192 snapshot 全てで **system 数もグリフ数も不変＝改行は動かなかった**。
グリフの最大移動は **0.70 ss** で、これは上余白の増分 +0.690551 そのもの。
つまり内容が下へ 0.69・右へ 0.036 ずれ、五線が 0.38 伸び、紙面が 0.61 高くなっただけ。
**着手前の予測は 2 つとも外れた**（「横が広がって行あたり小節が増える」→ 0.38 では届かない、
「帯が狭まって fixture が 4 ページになる」→ 3 ページのまま 13+13+8）。

⚠️ **`HaraKiriVisualTests.PagedRendering` が本当に壊れた。** `PageHeight=30` だけ固定して
余白は製品既定を継承していたため、帯が 20 → 18.62 になり分割されなくなった。
**ページブレーカーのテストは自分が割るページを所有すべき**なので、上下余白を明示させて直した
（再ピン止めではない）。横は幅に依存しないので製品既定のままにしてある。

#### ✅ Y の台帳を開いた（`0c0d8f38`）— 6 桁で 3 点

LP 2.26.0 の `page-vertical.ly` **book L**（短いスコア＋既定用紙＝そのページが最終ページで、
gap は伸長後でなく **spring の自然長**）に対して:

| 台帳キー | Lily# | LP | 残差 |
|---|---|---|---|
| `page.width` | 119.501575 | 119.501575 | **0** |
| `system.natural-distance` | 12.000000 | 12.000000 | **0** |
| `page.first-staff-refpoint` | 8.690551 | 11.690551 | **−3.000000** → **✅ 0**（`1dfb62d7`） |

⚠️ **gap が exact なのは形式ではなく実結果。** 旧記述は SVG 2 桁読みで「一致」としていたが、
それでは 12.00 と 12.004 を区別できない。6 桁で 12.000000 だった。

#### ✅ 先頭 system の −3.000000 は閉じた（`b94487ad` → `1dfb62d7`）— **順序が本体**

LP はインクが小さいとき先頭 system をインクで測らない: `top-system-spacing` は spring で、
インクはその**床**でしかなく（`page-layout-problem.cc:625-633` ＋ `spring.cc:156-159`）、
force 0 の spring は `max(min_distance, ideal_distance)`（`spring.cc:219-237`）。
つまり距離は **`max(basic-distance, ink + padding)`**。`TopSystem.BasicDistance` の 6 は
元から正しく、**読み手が居なかっただけ**だった。

**ただし字面移植の前にアンカー統一が要る**（2026-07-22 に②だけ入れて差し戻した実測）:

| | Lily# | LP |
|---|---|---|
| `firstY` / `SystemLayout.Y` が指すもの | **五線の最上線**（system 原点） | — |
| `top-system-spacing` が狙うもの | — | **staff refpoint＝中央線** |

**ずれは `halfStaff` = 2 ss ちょうど。単位も向きも同じで、アンカーだけが違う。**
①を飛ばして②だけ入れると *最上線* が LP の *refpoint* 値に着地し、残差は
**−3.000000 → +2.000000** で閉じない。実際にそうなることを実測して差し戻してある。

入れたもの: `LayoutUtilities.CalculateFirstStaffRefpoint`（refpoint フレームで距離を出す。
**header は アンカーでなく床に入る** — `page-layout-problem.cc:441-444`＋`:471-473`）と、
`CalculateFirstSystemY`（= refpoint − halfStaff。**halfStaff 変換はここ 1 箇所だけ**）。
呼び出しは `LayoutEngine`（先頭 seed と単一ページ経路）と `PageLayouter` の 3 箇所。

⚠️ Lily# の `upExtent` は**五線最上線より上のインク**（probe V では 0）で、LP の
`up_skyline.distance()` は **refpoint からのインク**（同 3.8）。**この 2 つを取り違えると
また閉じない。** 変換は `CalculateFirstStaffRefpoint` の中で `upExtent + halfStaff` として
1 箇所に閉じてある。

⚠️ **未了**: LP の top spring は**ページ justify で伸びる**（`set_default_stretch_strength
= ideal_distance`、`spring.cc:213-216`）が、Lily# は先頭 system を固定して system 間 spring だけ
伸ばす。force 0 では見えないので台帳は緑。**伸長 regime に着手するとき最初に触る乖離。**

#### ⚠️ 意図的な乖離: 単一ページは紙面サイズにならない

`page.height` を台帳に入れようとして落とした。**Lily# は 1 ページに収まるスコアを内容サイズの
ページで出し、溢れて初めて紙面に切り替える**（`LayoutEngine.cs:606-611`、明示的な設計）。
LP は常に紙面に組む。この probe では **−109.468268**。実在し理解もできているが閉じる予定が無く、
台帳に載せると `total |residual|` が ~109.5 になり指標が壊れるのでここに書く。
**ページ分割される経路は紙面をそのまま使う**（`test/multi-page-vertical` は 3 × 169.009370）。

#### ✅ 伸長 regime も鎖として移植した（`6ffbe7bd` → `cfdf85b4`）

**LP はページごとに `[top spring][system pair ×N][last-bottom spring]` を1本の鎖にして
`Simple_spacer` で `page_height_` に対して解く**（`page-layout-problem.cc:406-545`・`:780-804`）。
だから**ページ上の全 spring が同じ force を持つ**＝ top も bottom も一緒に伸びる。
Lily# は鎖を持たず、先頭を固定し、足に spring を置かず、**最終 system のインクが下余白に
届くまで**system 間だけ伸ばしていた。solver は既存の `SpringSolver`（`Simple_spacer` 移植済）で足りた。

**同時に廃止した転記誤り2件**:

| 誤り | LP の実際 |
|---|---|
| `TopSystem.Stretchability = 0` | `paper-defaults-init.ly:78-80` に **stretchability キーは無い**＝`set_default_strength` で inverse stretch は **ideal（6）**（`spring.cc:213-216`）。「0」を「未指定」と読むようにした |
| `InverseHooke = max(0.1, Stretchability / 60)` | **`/60` も下限 0.1 も LP に無い。** stretchability をそのまま使う |

⚠️ **compress strength は spec の minimum-distance から取る**（`ensure_min_distance` は
最小値を上げるが**強度を張り直さない** — `spring.cc:156-159`）。上げた後の min を渡すと
全 blocking force が静かに変わる。

✅ **PageBreaker は 4 点とも閉じた**（`113fdeda`: `inverse_hooke_` ／ `min_whitespace_at_top/bottom_of_page`
／ 死んでいる `bottom_padding_`。`850c7d98`: orphan penalty を markup 段落の規則へ）。top spring と last-bottom spring は、ブレーカーには
**バネとしてではなく「最小空白の予約」として入る**のが LP の設計。

⚠️ **旧記述「`systemDetails[0]` が `vs.SystemSystem`、配置側は `vs.TopSystem` ＝食い違い」は誤診だった。**
LP は**先頭を含む全 line に system-system-spacing を与える**（`constrained-breaking.cc:548-555`）。
top-system-spacing がブレーカーに届く経路は `min_whitespace_at_top_of_page` **だけ**。
`i == 0` 分岐は元から正しい。訂正はコード側（`PageBreaker._vs` の remark）にも書いた。

⚠️ **残っているのは構造差（LP はページが行分割を選ぶ）と、単一ページ・フォールバック**。§1 冒頭を見ること。

⚠️ **`LayoutEngine` の単一ページ経路は今も自前で積んでいる**（force 0 なので鎖と一致するが**二重実装**）。

#### 参考: 旧記述の紙面比較表

`Measure-LilyPondPageGeometry.ps1`（LP **2.26.0** 実測、A4、markup 無し、単位 ss）:

| | Lily# 旧 | **Lily# 現在＝LP 2.26.0** | 参考: 2.24.4 |
|---|---|---|---|
| `PageHeight` | 168.4 | **169.009370** | 169.009370 |
| `MarginTop` | 5 | **5.690551**（10 mm） | 2.845276（5 mm） |
| `MarginBottom` | 5 | **5.690551**（10 mm） | 3.414331（6 mm） |
| 縦の使用可能帯 | 158.4 | **157.628268** | 162.749764 |
| `MarginLeft` / `MarginRight` | 8.5 / 8.5 | **8.535827**（15 mm） | 5.690551（10 mm） |
| `ContentWidth` | 102.05 | **102.429921** | 108.120472 |
| `PageWidth` | 119.05 | **119.501575** | 119.501575 |
| system 間の自然距離 | 12.000000 | **12.000000**（一致） | 12.000000 |

**この表は `e38a76bf` で解消済み**（Lily# 列＝LP 列）。2.24.4 列は過去の記述との突き合わせ用。

（旧記述の「LP 5.68 / Lily# 8.93」はプローブの `section Main` マーク由来の交絡で、
markup 無しで測り直すと LP の先頭インク上端は **3.824272**。）

（この4定数は `e38a76bf` で完了。上の ✅ を参照。）

#### 圧縮は**まだ入れない**（旧記述の訂正は維持）

PASS 2 を `SpringSolver` による両方向 solve に置き換えて測ったところ、**プローブも snapshot も
1 バイトも動かなかった**（§3.4 に従い revert 済み）。理由は Lily# の使用可能帯が狭く、
ブレーカーが伸ばす側の本数しか選ばないため。**上の余白を LP に寄せると圧縮側に入る見込み**
なので、順序は「余白 → 圧縮」。置換箇所は `PageLayouter` PASS 2 → `SpringSolver.SolveForWidth`
＋ `GetPositions`、PASS 1 で `gapMinimum[]` を拾う必要あり。
**入れるときは圧縮が実際に走るケース（`SystemsPerPage` 強制など）を同時に用意すること。**

✅ **複数ページ fixture は入った**（`7291531a`）。`test/multi-page-vertical` は **3 ページ**で、
ページ1（埋まったページ）・中間ページ（ヘッダ無し）・**最終ページ（前ページの force を継承）**の
3 ケースを分離する。2 ページだと最終ページ規則と単ページ規則が区別できないので 3 が最小。
全音符なのは、ページ分割が価格付けるのは **system** であり、1 小節 1 グリフなら同じ 37 system を
88 KB で買えるから（四分音符だと 210 KB でツリー最大を超える）。最初の system だけ小節番号が
無いので `staff-refpoint-extent` が system ごとに違う＝ §5.3 の取り違えを検出できる。

**これで余白 4 定数を動かす準備は整った。**

**Y コーパスの起こし方（未着手）**: `audit/lp-geometry` の台帳は X 専用のまま。LP 側の
プローブと測定スクリプトは `d57defcd` で入ったので、あとは `lp-geometry.json` に点を足すだけ。
候補は `page.top-margin` / `page.bottom-margin` / `page.height` / `system.natural-distance`
（現状 exact）/ `page.last-page-gap`。⚠️ 版差のある量には印を付けること（§1）。

#### ✅ 解決: `VerticalSpacingParameters.TopSystem.BasicDistance` は **6 で正しい**

2.26.0 実測で **最初の staff refpoint が `top-margin + 6.000000` ちょうど**に来ることを確認した
（`Measure-LilyPondPageGeometry.ps1`、book N）。**「1 は `last-bottom-spacing` からの転記ミス」と
書いたコメントのほうが誤り**（1 は 2.24.4 の値）。潜在バグではない。
**`1dfb62d7` で読み手が付いた**ので、もう死んでいない（`CalculateFirstStaffRefpoint` が読む）。
⚠️ ただし `PageLayouter` は **systemDetails の `i == 0` でまだ `vs.SystemSystem` を使う**（`:101-104`）。
配置側は `vs.TopSystem` なので**ブレーカーと配置で spec が食い違っている**。
ページあたりの本数見積りにしか効かないが、**伸長 regime に着手するとき最初に確認すること。**

### ⑦ MMR まわりの小さい未解決（記録のみ）

- **`GlyphMetrics.RestMaximaWidth = 1.8` が手動側**。フォントメトリクスなので生成器が `rests.M3` を
  出すようになったら `GlyphMetricsGenerated.cs` へ（`GlyphMetrics.cs:89-96` に記録済）
- ⚠️ **`SystemBreaker` は 1 回しか呼ばれない**（＝レイアウトは 1-pass）。LP はページブレーカーから
  システム数を指定して行分割を**やり直させる**（`optimal-page-breaking.cc:139-173`＋
  `page-breaking.cc:75-101`）。**再入可能化は検討のうえ見送りと決定済み**——§1 の決定記録を読むこと。
  蒸し返す前に「頻度の測定」が先
- **`SystemBreaker.BreakIntoSystemsGreedy` は MMR run 非対応**。ただし
  `LayoutOptions.UseOptimalLineBreaking` が既定 `true`（`LayoutOptions.cs:100`）なので**既定出力に
  影響しない**。かつ greedy は LP のアルゴリズムではない（LP = `constrained-breaking.cc` = optimal）
  ので**忠実度は上がらない**。優先度低

---

## 3. 長期ロードマップ

### A. LP 忠実度を測定可能にし、単調に上げる ★中心

**現状 29/42 exact, total |residual| = 1.044394 ss over 38 distances ＋ counts 4/4**
（`audit/lp-geometry/`・**LP 2.26.0 基準**。1.025 の増分はスラー 2 点の新規可視化＝未 fix）。
**X 22 点は 19 exact / 0.022412、Y 7 点は 3 exact / 0.001365、譜間 5 点は 2 exact、
system 間タプレット 2 点、ページ本数 4 点は全て exact**
（`920cf4dc` で起票、`113fdeda`＋`850c7d98` で閉じた）。
**タプレットの 4 点は全て +0.0000208**（`b36db266` で実フォント計測により閉じた。残るは Pango 量子化）、
**譜間の残り 1 点は probe D の −0.000076**（`26afa9fe`。Pango の量子化）。
**5 点とも Lily# に無いテキスト metric が要る＝閉じる予定は無い名前付き残差**
（`0c0d8f38` で Y に開き、`1dfb62d7` で自然長、`cfdf85b4` で伸長、`22120764` でインク、
`90efec02` で clef、`b3cfb119`＋`854a0e95` で譜間を、`26afa9fe` で強弱を、
`caa0f239`＋`075277ff` でタプレットを閉じた）。
**Y の残り 4 点は同一原因**（clef の LILC bbox 3.550 vs LP の skyline 3.540〜3.545）。
X 3 点のうち 2 点は Lily# に無いパイプライン（水平スカイライン／テキストレイアウト）が要る。

⚠️ **ss 合計を過去の値と直接比べないこと**（点集合が違う）。タプレットは 2.214721 まで増えてから
**0.023937 に戻った**——増加は悪化ではなく**誰も測っていなかった乖離の可視化**だった（§5.2.1④）。

これがこのプロジェクトの品質指標。snapshot は「前回の自分」との比較なので、一度承認した誤りは
永久に緑のまま。台帳は **LP との距離**を数値で持ち、増減どちらでもテストが落ちる。

- 短期: ✅ X 軸（§2 ①③④④'）は完結。**定数1つで閉じる残差はもう無い**
- 中期: **コーパスを縦（Y）にも広げる** — 現在は bar line 周りの X のみ。
  ページ縦は LP 側のプローブと測定スクリプトが `d57defcd` で入った（§2⑧）ので、
  次は `lp-geometry.json` に点を足す番。ほかに譜間距離・スラー/タイ・ビーム・臨時記号配置
- 原則: **snapshot を再ベースするたびに、LP 照合済みの点が増えているべき**

⚠️ **既知の穴**: 以下2つの LP 検証は**数値がコメントに残っているだけで、プローブが未 commit**
（scratchpad に置いたまま消える）。コーパスの「再実行可能」原則から外れているので、
次に触るとき `audit/lp-geometry/probes/` に移すこと。
- **stretch strength 0.45 の検証**（同じ音楽を 120mm / 180mm で justify し、force を解いて
  独立な spring で交差検証）→ 数値は
  `SpacingInvariantTests.BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance` に
- **符尾 Y extent のダンプ**（光学補正の 2×2 の裏取り）→ 数値は
  `SpacingRules.BarlineToNextNotesCorrection` の remarks に

✅ **幻の符尾は 3 箇所とも閉じた**（`89aaa29f` `SkylineBuilder` / `26afa9fe` `DynamicEngraver` /
`caa0f239` `TupletBracketEngraver`）。3 例とも「先に点を作る → 予測を書く → 実装」で桁まで一致した。
**この型は 3 回とも成立している。**

✅ **タプレット括弧は 2 つのスカイラインとも閉じた**（`caa0f239` 譜間 / `075277ff` ページ）。

⚠️ **未測定の疑い**: **ビーム・タイ・臨時記号配置には台帳の点が 1 つも無い**（スラーは `87bcde22` で
対で開いた＝SD/SU、−0.512596 ×2・**skyline 未予約が確定・fix 未実施**。§1 冒頭）。
タプレットで点を開いたら**欠陥が 4 つ**出た（うち 1 つは snapshot に構造的に現れない種類だった）。
着手手順は §1 の「その次」。**必ず対で作ること。**

### B. 座標系の LP 統一を完了させる（COORDINATE_AUDIT §4.6）

起票時の実バグ8件は全て対処済み。残るのは「数値は正だが frame 忠実性が未完」の3系統:

| | 内容 | 状況 |
|---|---|---|
| ① | 譜間/system 縦積みの Y-down 残存（**島1**） | ✅ **完了**（`ff64f38e`。下記） |
| ② | device 島群（**島2**） | ⏸ 繰延。TieVariant / 水平 skyline の Y horizon / TabStaffGeometry / beam collision island |
| ③ | non-musical PaperColumn の欠落 | 🔄 §2 ③ |

#### ✅ 島1 は閉じた（`ff64f38e`）— **byte 不変・snapshot 0 件**

`StaffLayout.Y` / `StaffGroupLayout.Y` / `GrandStaffLayout.BraceTop`・`BraceBottom` が
Y-up 格納（先頭 staff が 0・下ほど負）。**LP の `Align_interface` は
`where += stacking_dir * dy`（`align-interface.cc:274`）で `stacking_dir = DOWN = -1`＝
元から負に歩いて `translates` をそのまま格納する**ので、これは近似ではなく字面移植。
3 ディスパッチャ・7 積み上げ地点・15 消費側。`Height` は長さなので正のまま。

**副産物**: `FindStaffYInSystem` が `system.Y + staff.Y` になり、`StaffOffsetInSystemUp` が
primitive・`Down` がその否定（＝反射が device 島の縁だけに残った）。詳細は §1 の教訓 2 件。

⚠️ **`StaffOffsetInSystemDown` の残り 8 呼び出しは移行していない**（意図的な device 境界＝島2。
`Down` は消さない）。理由は `LayoutUtilities.StaffOffsetInSystemDown` の remark にある。

**X（③）と Y（①）は独立に進められる。** 島1 は boundary-shim で byte 不変移行できると実証された。

★ **島1 が残した再利用可能な型**（島2 に着手するときの手順）:
1. **格納を反転する前に、格納値を主張するテストを書く**（オラクルが打ち消しを見逃すため）
2. **生産側を全部同時に**（半端＝全崩れ）。消費側は grep で網羅してから
3. **device 島の縁では 1 回だけ反射する**。反射を島の内側へ押し込まない

### C. 未移植 LP 計算の取り込み

tuplet on-line / volta shorten / hairpin niente / ledger / brace / 開 chord / Ignatzek。
出典 `HANDOFF-lp-calc-incorporation.md`（§8）。**未検証の一覧なので、着手前に実コードで裏取り。**

### D. 言語・ツール側（X/Y 座標系とは独立）

いずれも**この一覧は伝聞。着手前に実コードで確認すること。**

- MusicXML インポート — ほぼ完遂、実ファイル検証が残
- AI 協調編集 M1–5（Ctrl+I / 譜面選択 / 補完 / BYO-key）— 実機 E2E 未検証
- 文法改善 5 件 — **完了。糖衣は入れないと決定した**（2026-07-22、下記）。0.3.0 リリースは GO 待ち
- **`note!` 密着の診断 — ✅ master に取り込み済み**（`a994f418` `1ff10e45` `7d5255cb`。
  LYS4009 ＋ 点線小節線のフィクスチャ）。ユーザーがマージ済み・ブランチは残っていない
- **`override` の消費側は 4 つだけ**（`NoteHead.transparent` / `<grob>.color` / `NoteColumn.force-hshift`）。
  **文法側は元から開いている**（grob 名・プロパティ名とも任意の識別子・ハイフン連結可・許可リスト無し）
  ので、増やす作業は配線だけ。⚠️ ただし**値に小数リテラルが書けない**（整数/識別子/文字列/負整数のみ）。
  §5.3 の摂動法を Lily# 側でも使うなら `StaffGrouper.staff-staff-spacing` 等の配線＋小数が要る。
  ⚠️ **page 系（`paper-height` / `top-system-spacing` / `systems-per-page`）を `override` に載せないこと**——
  LP ではそれらは `\paper` 変数であって grob プロパティではない。**コーパスは
  `RenderedGeometry.Render(source, LayoutOptions)` というハーネス引数で解決した**（2026-07-22 決定）
- Dead-code 監査 — アナライザ検出分は完了、手動分が残
- `LILYPOND-REF` 行番号の一括再採番（cosmetic・繰延）
- `IDrawingContext.cs:37-39` の remark が装飾前後2フレームを記述していない（§4.4）
- **対応の取れないスラーが無警告で消える** — `SlurDetector.cs:49-56` は開きスタックが
  空のまま `)` を読むと黙って捨て、開いたまま終わった `(` も捨てる。`(e c4 d)` のように
  `(` の前に音符が無い形（`MusicWalk.cs:103-108` の通り `(` は直前の音符に結び付く）は
  スラーが1本消えるのに診断が出ない。タイの LYS4007 に相当する警告を足す。
  LilyPond も unterminated slur を警告する。**未着手・別ブランチでやる**（2026-07-22 記録）
- **`editors/vscode/src/smartBrackets.ts` を `smartTyping.ts` に改名** — 角括弧
  （`<`/`>` の和音・アルペジオ）だけでなくスラー `(`/`)` とオクターブ記号 `'`/`,` も
  扱うようになり、名前が実態から離れた。`registerSmartBrackets` も同様。変更は
  `extension.ts` の import 1 箇所。**未着手・競合を避けて後で**（2026-07-22 記録）

#### ✅ 決定: 臨時記号の糖衣 `c?` / `c??` は**入れない**（2026-07-22・蒸し返さないこと）

⚠️ 旧記述は「糖衣 `c?` / `c!` 未実装」で、**却下済みと未着手を同じ「未実装」に潰していた**。
`c!` は検討して**撤回した**もの（`3e4188b`）で、未着手ではない。

| | 状態 |
|---|---|
| `c!`（LP の強制臨時記号） | **却下**。`!` は点線小節線トークン（`Lexer.cs:192`、LP `\bar "!"`）。密着判定は空白に意味を持たせ、`c4! d` と詰めた既存の点線小節線の意味を黙って変える |
| `c?`（LP の cautionary） | **不採用**。痛み自体は既に解消済み（`Parser.cs:70-72` が `@courtesy`/`@editorial` を案内する専用エラーを出す）。残りはキーストローク節約だけ |
| `c??`（`@editorial` の糖衣） | **不採用**。LP に無い記号の発明 |

`c?` を落とした決め手は**単独では `!` の罠を悪化させること**: LP の `?`/`!` は対なので、
片方だけ通すと「LP の書き方が効く」と学習させ、`c!` を試す導線をこちらから作る。そして
`c!` は今**黙って小節を割り**、LYS2006（「弱起なら partial を宣言しろ」）という見当違いの
助言しか返さない。さらに和音では LP に無い配置設計が要る（LP は `<cis? e g>` の**音符単位**で、
和音全体に付ける `<c e g>?` は存在しない）。

**→ 代わりに `note!` 密着の診断をやる**（上記・別ブランチ）。静かな誤動作を名指しの説明に
変える方が価値が高く、言語の表面積を増やさない。

★ **ここから出た線引き（今後の記号追加はこれで判断する）**:
> **記号（sigil）は LilyPond が既に記号で綴っているものにだけ使う。
> Lily# 固有のものは全部 `@name` で書く。**

この一本で、`?` を入れない理由・`!` を点線小節線のまま残す理由・将来「括弧なし強制」を
足すなら `@force`（記号ではない）にする理由が、すべて同じ根拠で説明できる。
なお**「強制」自体は既に可能**——`@courtesy` は規則上出ない臨時記号を強制的に出したうえで
括弧を付ける（`MeasureCollector.ItemFactory.cs:73-79`）。無いのは「括弧なしの強制」だけ。

### E. 保守性の負債（このセッションで見つけたもの）

- `DrawingTransform.Identity` は `new()` なので **`ScaleX/ScaleY = 0`**、
  `Identity.IsIdentity` 自体が false。record struct はプライマリコンストラクタの既定値を
  適用しない。出荷 3 backend は無害だが、**記録用コンテキストの作者を2人捕まえている**。
  `Identity => new(0,0,1,1)` に直す価値あり（未実施・要判断）
- `SharedRendererBeamTests` と `LpFidelity/RecordingDocumentContext` に記録用コンテキストが
  **2実装ある**。統合は既存の通っているテストに触るので要判断

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
| **現在地・次の一手・ロードマップ** | **このファイル §1–§3** | |
| **ユーザーの好み・作業規律** | user memory | 「done は push 済みで」 |

**ここに書かない**: LP の式の導出、実測値の生データ、アーキテクチャ解説、コードの説明。
それらは上表の置き場所へ。このファイルには**ポインタだけ**置く。

> 判断に迷ったら: 「これは**次のセッションだけ**必要か、**ずっと**必要か？」
> ずっと必要ならこのファイル以外へ。

---

## 5. 恒久ルール（滅多に変わらない）

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
**先に台帳点を作る**（`StaffSymbol` の ±2.05 を保留したのがこの適用例。§1）。
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
  前者が「LP は圧縮している」という誤った結論として引き継がれていた（§2⑧）
- ★ **残差の符号で原因を切り分ける。** あるグリフの**左右の残差が逆符号**なら
  **frame（基準点）の誤り**、**同符号**なら**定数の誤り**。定数が違えば両側とも同じ向きに
  ずれるが、基準点がずれていると片側が広がった分だけ反対側が狭まるため。
  行中 clef/key 変更でこれを使って診断した（`midmeasure.*` の4点。§2①）
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
2. [ ] **§1「現在地」を書き換える**（追記しない）— HEAD / ahead 数 / テスト数 / やったこと
3. [ ] ロードマップ（§2/§3）が動いたなら更新する。**完了した項目は消す**
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

## 8. 旧 handoff ファイルの棚卸し

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
