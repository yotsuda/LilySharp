# Lily# 不具合発見・修正ワークフロー

Lily#(C#/.NET の LilyPond 風楽譜コンパイラ)のレイアウト不具合を発見・修正し、
LilyPond の実装に忠実に移植するための体系的な手順書。**次のセッションがこの文書だけで
同じ品質の作業を再現できる**ことを目的とする。散文は日本語、コマンド/パス/識別子は原文どおり。

> **交差声部の水平 note-collision(per-note 幅対応)は ✅ 完了(2026-07-01 夜、commit `724f8cc`、未 push)。**
> 詳細は §12-7(対応済みの要点)＋`docs/HANDOFF_NOTE_COLLISION_PERNOTE_WIDTH.md`(末尾に完了サマリ追記済み)。
> 要旨: `AnalyzeCollision` に LP の meshing フォールバックを移植＋新 `CollisionType.Meshing` で**下声を左へ寄せる**ピン
> (連桁の上声を列 X に温存)。分離量 `2*0.17*down_head_width` が LP 並置一致。多声ビュー一式はこれで本質的欠落なし。
>
> **直近の完了(2026-07-01)・未 push**:
> - ✅ **交差声部の水平 note-collision**(`724f8cc`、上記)。1986緑。
> - ✅ `docs/HANDOFF_MULTIVOICE_BEAMS.md` ― **多声のビーム完了**(第 2 声部の 8/16 分がその声部の音符に連桁、
>   下声ビームは下・上声は上)。検出器を全声部ループ＋**声部で符幹方向固定**、`LayoutBeams` を `score.Voices[VoiceIndex]`
>   解決、レンダラの `beamedItems` を `(staff,voice,measure,item)` 化。LP 2.24 並置一致。fixture `test/multivoice-beams`。
> - ✅ `docs/HANDOFF_MULTISTAFF_OTTAVA.md` ― #6 ottava **完了**(per-staff 化＋**移調表示**=8va/8vb/15ma/15mb、
>   `@ottava(bassa)` で下方も記述可、data-pos 修正込み)。
> - ✅ `docs/HANDOFF_MULTIVOICE_SPANNERS.md` ― #3 **多声 slur/tie/gliss 完了**(検出・配置・**声部で向き固定**=
>   上声上/下声下、和音タイ含む、Glissando data-pos の voice-aware 化)。詳細は両 handoff 末尾＋
>   `HANDOFF_MULTIVOICE_BEAMS.md` §4(手本として再掲)。
>
> いずれも **単一譜/単一声部 byte-identical を各コミットで死守**する型。未 push コミットが積まれているので、
> 着手前に `git --no-pager log --oneline origin/master..HEAD` で現状を把握すること(**push 保留指示中**)。

---

## 0. 最優先原則 ― 「アドホックにしない」

ユーザーが繰り返し強制する最重要ルール。**症状を消すパッチではなく、対応する LilyPond の
規則そのものを移植する。**

- 修正したら必ず「これはアドホックな修正になっていないか? LilyPond のレイアウトを参照して
  実装できているか?」を自問する。ユーザーはこれを能動的に問い詰めてくる。
- 移植したコードには必ず `// LILYPOND-REF: <file>:<lines> <function/property>` コメントを付け、
  どの LP の規則のどの行を根拠にしたかを残す。
- **見た目が一致しても自己監査を続ける。** 視覚的に LP と一致した後でも LP ソースを読み直し、
  妥協が残っていないか確認する(例: 和音臨時記号のスカイラインは「臨時記号を持つ頭だけ」で
  作っていたが、LP は `build_heads_skyline` で全頭から作ると判明し追加修正した)。
- 定数の出所を疑う。Lily# には SMuFL/Bravura 由来の値が紛れ込んでいた(線の太さ一族)。
  必ず LilyPond の `scm/paper.scm` / `scm/define-grobs.scm` の実値・実式に揃える。
- コミットメッセージに「以前の誤った主張」も正直に書く。誤読・誤実装の経緯を残す。

---

## 1. 環境とパス

| 対象 | パス / コマンド |
|---|---|
| Lily# リポジトリ | `C:\MyProj\LilySharp`(master 直作業、ブランチを勝手に作らない) |
| LilyPond ソースクローン | `C:\MyProj\lilypond-src` |
| LilyPond 実行ファイル | `lilypond`(PATH 上、バージョン 2.24.4) |
| サンプル | `samples/test/*.lys`, `samples/showcase/*.lys` |
| スナップショット | `LilySharp.Tests/Snapshots/test__*.svg`, `showcase__*.svg` |

### シェルとツールの鉄則

- **シェルは ripple MCP の `execute_command`(shell=pwsh)を使う。** PowerShell ツールは
  デタッチプロセスが `bin/Debug` の DLL をロックしてビルドを壊すため使用禁止。Bash ツールも禁止。
- ビルド/テスト/git/CLI 実行はすべて ripple のパイプラインに直書きする。
- **特殊文字を含むファイル(`.ly`, `.py`, `<...>` を含む `.lys`, コミットメッセージ)は
  ripple のヒアストリングに直書きしない**(pwsh のクォート地獄で壊れる)。必ず `Write` ツールで
  ファイルを作ってから実行・参照する。`<`, `>`, `'`, `"`, 日本語 を含むものは特に。

### lilypond.exe の Guile デッドロック回避(最重要)

`lilypond` を MCP コンソールから直接起動すると Guile 初期化でデッドロックして 90 秒以上ハングする。
**必ず `cmd /c` でデタッチし、stdin を NUL に、出力をログにリダイレクトする**:

```pwsh
cmd.exe /d /s /c "lilypond --png -dresolution=200 -o ref ref.ly < NUL > ref.log 2>&1"
Get-Content ref.log -Tail 1   # "成功: コンパイルが成功しました" を確認
```

`< NUL` と `> log 2>&1` の両方が必須。コマンド全体を 1 つの文字列として渡す
(list 形式だとクォートが `\"` に化ける)。

---

## 2. 不具合発見・修正の標準サイクル

ユーザーが確立した 1 件あたりの定型手順:

```
調査 → LP 規則の移植(LILYPOND-REF 付き) → テスト追加 → 視覚検証(LP と並置比較)
   → スナップショット再ベースライン(差分純度の検証) → コミット(詳細メッセージ)
   → push → deploy-extension.ps1 → バージョンスタンプのコミット → push
```

- **「問題を述べているだけ/質問」と「修正依頼」を区別する。** 「これは正しい挙動?」と
  聞かれたら、まず調査して所見を報告し、勝手に直さない(例: barcheck の5拍小節、
  fermata 休符の警告の妥当性確認)。「直して」と言われてから実装する。
- 完了形で「done」と言うのは push 済みのときだけ。tag/publish/告知は green + 明示 GO の2段ゲート。

---

## 3. LilyPond との出力比較(中核手順)

### 3.1 Lily# を PNG / SVG に出力

```pwsh
cd C:\MyProj\LilySharp
# コード変更後は --no-build を付けない(古いバイナリを実行してしまう)
dotnet run --project LilySharp.Cli -- png samples\test\<name>.lys C:\temp\ls.png
dotnet run --project LilySharp.Cli -- svg samples\test\<name>.lys C:\temp\ls.svg
dotnet run --project LilySharp.Cli -- check samples\test\<name>.lys   # 診断のみ
```

CLI のサブコマンド: `svg pdf png midi xml check`。`-o out` か末尾位置引数で出力先指定。

> **落とし穴**: `--no-build` は前回ビルド済みの DLL を実行する。**コードを変更した直後は
> `--no-build` を外す**か、先に `dotnet build` する。これを忘れて「直したのに変わらない」と
> 誤認する事故が何度も起きた(check が古い警告を出す、等)。

### 3.2 LilyPond 参照を作る ― ピッチ翻訳に注意

LilyPond の `.ly` を `Write` ツールで作り、デッドロック回避起動でレンダリングする。

**最大の罠: 相対オクターブの翻訳。** Lily# の相対ピッチ解決は独自で、ソースの見た目と
実音高が違う(`c,8 c c c''` は C3 C3 C3 C5、続く `c, c''` は毎回上にずれる)。
**LP 参照では絶対ピッチを書いて、Lily# が実際に出している音高に合わせる。** 音高がずれた
比較は無意味。Lily# 側の実音高は data-pos プローブ(§4)や probe test(§5)で確認できる。

```ly
\version "2.24.0"
\paper { indent = 0 }
{ \time 4/4 <cis e gis>1 <fis gis>2 <ais bis>2 }
```

### 3.3 PIL で並置合成・トリミング・拡大

`Write` ツールで Python スクリプトを作り `py -3 -X utf8 <script>.py` で実行する。
頻出パターン:

- **並置合成**: Lily# 出力と LP 出力を上下に貼り、ラベルを描いた 1 枚を作って `Read` で見る。
- **ROI トリミング**: 問題箇所だけ切り出して拡大。`Read` ツールは PNG を視覚的に読める。
- **viewBox → PNG ピクセル変換**: SVG の `viewBox="0 0 W H"` と PNG の幅高さから
  `sx=w/W, sy=h/H`。座標 `(svgX, svgY)` → ピクセル `(svgX*sx, svgY*sy)`。微細な不具合
  (ハープン頂点の切り欠き等)は該当点を中心に `Image.NEAREST` で 5 倍拡大して確認。
- **自動インクバウンディングボックス**: `ImageChops.difference` + `getbbox()` で LP 出力の
  余白を自動トリミング(LP の PNG は A4 全面で余白だらけ)。

```python
from PIL import Image
im = Image.open(r'C:\temp\ls.png'); w, h = im.size
sx, sy = w/80.0, h/342.32          # viewBox の W,H に合わせる
cx, cy = 40.37*sx, 67.43*sy        # 調べたい SVG 座標
box = im.crop((int(cx-30), int(cy-50), int(cx+260), int(cy+50)))
box.resize((box.width*5, box.height*5), Image.NEAREST).save(r'C:\temp\zoom.png')
```

---

## 4. SVG + data-pos によるピンポイント調査

**SVG 出力の各要素は `data-pos="<ソースのバイトオフセット>"` を持つ。** これでどの符頭/
グリフ/線がソースのどの音符に対応するかを厳密に特定できる。「クレフと c の符頭が同一 X」
「臨時記号が重なっている」等の "重なり" 系は、まずこれで座標を数値で確定する。

### 4.1 ソースのバイトオフセットを得る

```python
src = open(r'...\name.lys', encoding='utf-8', newline='').read()  # newline='' でバイト位置保持
i = src.index('g4 a clef bass c,4 d')   # 対象フレーズの先頭オフセット
```

### 4.2 SVG から座標を抜く

```python
import re
svg = open(r'C:\temp\ls.svg', encoding='utf-8').read()
for m in re.finditer(r'<text class="music" x="([\d.]+)" y="([\d.]+)" font-size="[\d.]+" data-pos="(\d+)">', svg):
    pos = int(m.group(3))
    if i <= pos <= i+24:
        print('pos', pos, 'x', m.group(1), 'y', m.group(2))
```

これで「clef pos 317 が x=34.15、note pos 327 も x=34.15 → 同一カラムに潰れている」のように
不具合の根拠を数値で掴む。線(`<line>`)・矩形(`<rect>`)も同様に正規表現で抽出し、
ハープンの2線・小節線・符幹を座標で識別する。

---

## 5. プローブテスト(内部レイアウト状態の覗き見)

SVG に出ない内部値(spring の力、ビーム Y、カラムの timing→X、measure X 等)が欲しいときは、
`LilySharp.Tests` に**一時的な xUnit テスト**を置き、`XunitException` に計算値を載せて投げる。
**実レンダリングと同じ経路を再現すること**が肝心:

```csharp
var tree = SyntaxTree.Parse(src);
var score = new MeasureCollector().Collect(tree);
var multi = MultiStaffScore.FromScore(score);          // ← 単一譜でもこれを通す
var layout = new LayoutEngine().Layout(multi);
// ... ml.Columns, ml.GetXForTiming(t), beam.LeftY などを StringBuilder に集めて
throw new Xunit.Sdk.XunitException(sb.ToString());
```

実行:
```pwsh
dotnet test C:\MyProj\LilySharp\LilySharp.Tests --filter "FullyQualifiedName~MyProbe" 2>&1 |
  Select-String -Pattern "計算値の目印|error CS" -Context 0,3
```

> **重要**: `SvgGenerator.BuildLayout` は**単一譜でも `MultiStaffScore.FromScore` でラップして
> timing カラム経路を通す**。`new LayoutEngine().Layout(score)` で素の Score を渡すと
> アイテムスロット経路になり、カラム経路固有の不具合(中間クレフが音符と同 timing で潰れる等)を
> 見逃す。プローブは必ず実経路に合わせる。

**プローブテストは commit 前に必ず削除する**(`Remove-Item ...ProbeTests.cs`)。

---

## 6. LilyPond ソースの探し方(ナビゲーションマップ)

`C:\MyProj\lilypond-src` を `Grep` で当たる。役割でファイルが分かれている:

| 知りたいもの | 場所 |
|---|---|
| grob のプロパティ既定値(thickness, padding, direction, font-size, layer, outside-staff-priority, space-alist) | `scm/define-grobs.scm` |
| 全体の派生寸法(line-thickness, output-scale, blot-diameter, staff-space) | `scm/paper.scm`(`calc-line-thickness` 等) |
| 幾何・描画ロジック | `lily/<grob>.cc` の `print` / `calc_*` コールバック |
| Scheme の合成ロジック(バーライン合成、スクリプト定義) | `scm/bar-line.scm`, `scm/script.scm`, `scm/output-lib.scm` |
| コンテキスト定義(CueVoice の font-size 等)、grace、music function | `ly/engraver-init.ly`, `ly/grace-init.ly`, `ly/music-functions-init.ly`, `ly/property-init.ly` |
| フォントのグリフ計量 | `mf/feta-*.mf` |

### よく使う .cc(このセッションで参照したもの)

- `stem.cc` ― 符幹長(`calc_stem_info`)、和音内の符頭振り分け(`calc_positioning_done` 606-760)
- `beam.cc` / `beam-quanting.cc` ― ビーム方向・量子化・auto-knee(`consider_auto_knees`)
- `slur-scoring.cc` ― スラーのスコアリング、broken piece の encompass
- `hairpin.cc` ― クレシェンド/デクレシェンドの楔
- `accidental-placement.cc` / `accidental.cc` ― 臨時記号の階段配置・括弧・editorial
- `note-collision.cc` / `note-column.cc` ― 符頭衝突・main extent
- `staff-symbol.cc` ― 五線・加線の太さ(`ledger-line-thickness . (1.0 . 0.1)`)
- `ledger-line-spanner.cc` ― 加線の短縮・length-fraction
- `side-position-interface.cc` / `axis-group-interface.cc` ― outside-staff の積層・スカイライン
- `bar-line.cc` ― バーライングリフ合成
- `paper-column.cc` / `spanner.cc` / `spacing-*.cc` ― 非音楽カラム・破断処理・スペーシング

### 探索のコツ

- プロパティ名・コールバック名・定数(`(thickness . 1.3)`, `hair-thickness`, `auto-knee-gap`)で grep。
- 「既定値は `define-grobs.scm`、幾何は `lily/*.cc`、合成ロジックは `scm/*.scm`」の三層を意識する。
- 定数は**式のまま**移植する。例: `StemThickness = 1.3 * LineThickness`、
  `StaffLineThickness = 1.0 * LineThickness`、`LegerLineThickness = 1.0*line + 0.1*space`。
  生の数値(0.12 等)で書かない ― 別規格の値が紛れる温床。

---

## 7. 修正の実装

- 対応する LP の規則を移植し、`// LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done` の
  ように**ファイル:行番号:関数名**を残す。
- 構造的に等価でよい(逐語移植でなくてよい)が、その旨をコメントで明記する
  (例: 単一グリフ和音では LP の per-head `ell` が単一値に縮退する、support-head 整列ブロックは no-op)。
- 既知の簡略化・未実装は**コミットメッセージに明記**する(「cue 和音の臨時記号はまだフルサイズ」等)。
  silent に切り捨てない。
- ユーザーが手編集した文体・サンプルは戻さない。事実誤り/整合性の実質論点だけ指摘する。

---

## 8. テストとスナップショット

### 8.1 実行

```pwsh
dotnet test C:\MyProj\LilySharp 2>&1 | Select-String -Pattern "Failed!|Passed!|\[FAIL\]"
# 個別: --filter "FullyQualifiedName~SvgSnapshotTests"
```

スナップショットテスト(`SvgSnapshotTests`)は `samples/{test,showcase}/*.lys` を描画し
`Snapshots/*.svg` と LF 正規化比較する。新サンプルは `SvgSnapshotTests.cs` の
`TestSamples()` / `ShowcaseSamples()` に登録する。

### 8.2 再ベースライン

```pwsh
$env:LILYSHARP_UPDATE_SNAPSHOTS='1'
dotnet test C:\MyProj\LilySharp\LilySharp.Tests --filter "FullyQualifiedName~SvgSnapshotTests" 2>&1 |
  Select-String -Pattern "Failed!|Passed!"
$env:LILYSHARP_UPDATE_SNAPSHOTS=$null
```

### 8.3 差分純度の検証(必須)

再ベースラインしたら**差分が意図どおりの変化だけか**を機械的に確認する。意図した属性/値を
除去して残りが一致するか、あるいは行のソート済み集合が一致するかを git と突き合わせる。

```python
# 例: 変更は stroke-linecap="round" の追加だけのはず
import subprocess, re
files = subprocess.run(['git','diff','--name-only','--','LilySharp.Tests/Snapshots'],
                       capture_output=True, text=True).stdout.split()
bad = 0
for f in files:
    old = subprocess.run(['git','show','HEAD:'+f], capture_output=True).stdout.decode('utf-8').replace('\r\n','\n').split('\n')
    new = open(f, encoding='utf-8').read().replace('\r\n','\n').split('\n')
    for a, b in zip(old, new):
        if a != b and b.replace(' stroke-linecap="round"', '') != a:
            print('UNEXPECTED', f, a.strip()[:80], '->', b.strip()[:80]); bad += 1; break
print('files', len(files), 'unexpected', bad)
```

「z-order だけ変えたはず」なら**行のソート済み集合が HEAD と一致**するか(並べ替えのみ)を確認。
「太さだけ変えたはず」なら `stroke-width` 属性を剥がして残りが一致するか。
**想定外の差分が出たら原因を追う。** 浮動小数の結合順違いで無関係な末尾桁が揺れることがある
(§9 参照)。

### 8.4 notehead サニティスイープ

snapshot 再ベースライン時は符頭の Y がおかしくなっていないか(数値が飛んでいないか)を
ざっと確認する。

---

## 9. 落とし穴チェックリスト

- **古いバイナリ**: コード変更後の `--no-build` は罠。ビルドし直す。
- **lilypond デッドロック**: 必ず `cmd /d /s /c "... < NUL > log 2>&1"`。
- **PowerShell ツール禁止**: ripple MCP を使う(DLL ロック)。Bash ツールも禁止。
- **クォート地獄**: `.ly`/`.py`/特殊文字 `.lys`/コミットメッセージは `Write` ツールでファイル化。
- **相対ピッチ**: LP 参照は絶対ピッチで、Lily# の実音高に合わせる。
- **実経路の再現**: プローブは `MultiStaffScore.FromScore` でラップして timing カラム経路を通す。
- **浮動小数の結合順**: `x + (w*s - t/2)` と `(x + w*s) - t/2` は非 cue でも末尾桁が変わり、
  無関係な snapshot を動かす。**変更前と bit 一致させたいなら結合順を保つ**(部分式を 1 つにまとめる)。
- **未使用の重複定数**: 同じ概念の定数が複数ファイルに重複していることがある
  (GlyphMetrics と EngravingDefaults に線太さが二重定義 ← Bravura 値で未使用)。掃除する。
- **silent な機能欠落**: 未知の `@name` 注釈が黙って捨てられていた。`AnnotationNameValidator` が
  全サンプルスイープで「使われているのに認識されない名前」を検出する仕組みを入れた。
  新しい注釈を collector に足したらレジストリにも足す(さもないとサンプルが落ちる drift 検出網)。

---

## 10. コミットとデプロイ(定型)

```pwsh
cd C:\MyProj\LilySharp
git add <変更ファイル群>
git commit -F C:\temp\commit-msg.txt          # メッセージは Write でファイル化
git push                                        # 完了形主張は push 済みのときだけ
& .\deploy-extension.ps1                         # VS Code 拡張 + LSP を再ビルド・再インストール
                                                  #   → 版スタンプ2ファイルは deploy が自動コミットする
git push                                          # deploy の自動 "Bump dev build version" コミットを push
```

- master 直作業。ブランチは勝手に作らない。
- コミットメッセージは英語、`Co-Authored-By: Claude <current-model> <noreply@anthropic.com>` を付ける
  (current model 名に合わせる。例: `Claude Opus 4.8`)。§14 のコミット規約で確定済み。
- メッセージには「症状 → 真因 → LILYPOND-REF → 実装 → 検証(LP と一致確認)→ 既知の簡略化」を書く。
- `deploy-extension.ps1` の `Get-Process` "Code not found" エラーは無害(VS Code 未起動時)。

---

## 11. 不具合の類型(このセッションの実例)

次のセッションが「どこを疑うか」の手がかり。Lily# のレイアウト不具合は概ねこの型に収まる。

1. **定数が別規格** ― 線の太さ一族が SMuFL/Bravura 由来で、LP の weight 関係(符幹>五線)が
   逆転していた。→ `scm/paper.scm` の line-thickness から導出。
2. **レンダラがモデルのフラグを無視** ― `ChordItem.IsCue` を `DrawChord` が見ておらず cue 和音が
   縮小されなかった。
3. **レイアウト機構はあるのにレンダラが素朴に描く** ― 和音臨時記号の階段配置機構
   (`AccidentalPlacement`)は存在したが幅計算にしか使われず、`DrawChord` は固定オフセットで
   重ね描きしていた。
4. **描画順 / z-order** ― 加線が符頭の後に描かれ選択ハイライト時に上に乗った
   (LP は `LedgerLineSpanner` layer 0)。CJK フォントフォールバックのラン分割も同型。
5. **butt vs round キャップ** ― ハープン頂点が butt キャップで V 字に切れていた
   (LP は blot で丸める)。
6. **"unknown" スイープが炙り出す未実装/誤実装** ― `@editorial`(未実装)、`@glissando`
   (短縮形しか認識せず)、`@tremolo`(一度も認識されない名前)。
7. **off-spec の半端なモデル** ― editorial 臨時記号が「括弧付き・左・縮小」(別物)として
   半実装されていた。LP の `AccidentalSuggestion` は「音符の上の小さな臨時記号」。
8. **カラム/timing の衝突** ― 音価ゼロの中間クレフが次音符と同 timing を共有し同 X に潰れた
   (LP は非音楽カラムを音楽カラムの前に置く)。
9. **状態の clobber** ― `ExtractVoiceName` が全 render ブロックで `_voiceName` を無条件上書きし、
   最後の render のパートが常に勝っていた。
10. **broken spanner の端点アンカー** ― 改行で分割したスラー/ハープンの端点を spanner 全体の
    遠端ピッチにアンカーし、セグメント自身の符頭を貫通していた。→ そのシステム内の最寄り被覆音に
    アンカー(LP は broken piece を実包含列で再スコア)。
11. **書かれた小節線 vs 計量** ― Lily# は「書かれた `|` が真実」で計量はバリデータ、という
    設計判断(LP と意図的に異なる点もある)。「これは正しい挙動?」系はまずこの設計意図を確認する。

---

## 12. 既知の未対応・次の候補(2026-06-13 時点)

- ~~cue 和音の**臨時記号**はフルサイズのまま~~ ― ✅ **対応済み(2026-06-29 夕、§12-1)**。
  cue note/chord の臨時記号を head と同じ 0.66× に縮小(描画グリフ＋`AccidentalPlacement` の
  X 配置を `scale` 引数で一体縮小)。`DrawNote`/`DrawChord`/`DrawAccidentalAtInkLeft`/
  `AccidentalPlacement.CalculatePositions` に scale を導通。LP(CueVoice fontSize -4)並置で確認、
  fixture `test/cue-accidentals` を追加。**残る近似**: `SpacingRules`(left-extent/skyline)の
  cue 和音の**水平スペーシングは全要素フルサイズのまま**(head も含め一括フルサイズで内部整合)。
  これは「cue を水平圧縮しない」という別系統の近似で、臨時記号だけ縮小すると局所不整合になるため
  意図的に据置(描画のみ修正)。
- ~~トリル以外の装飾(turn/mordent 等)のフォント実寸が未抽出~~ ― ✅ **対応済み(2026-06-29 夕、§12-2)**。
  turn/reverseturn/prall/mordent/prallprall を `Extract-EmmentalerMetrics.py` に追加(純加算・既存不変、
  決定性は再生成 zero-diff で事前確認)し、`GetSeedBBox` で実寸を使用(trill と同作法)。
  **注意: 現状この変更は観測上 no-op**。seed は装飾自身でなく「その上に積む可動グロブ」だけに効くが、
  現行の可動グロブ(リハーサル/ナビ系マーク)は**小節頭 X 固定**で音符付き装飾と X が重ならないため、
  装飾 seed が参照される経路が無い(before/after で実測一致)。**価値=型の確立+将来の安価な拡張**:
  上向きテキスト/markup や複合オーナメント追加時に即活きる。装飾**自身の位置**は trill 同様 simplified
  のまま(GetArticulationExtent=1.0、意図的)。
- スラー/タイ端点・アルペジオ括弧は和音内 2 度の符頭変位 X に未追従(未変位の列 X のまま)。
  - **arpeggio** ― ✅ **対応済み(2026-06-29 夕、§12-3a)**。`ArpeggioEngraver` が列 X 基準で
    置いていたため、stem-down 2度和音の**左反転 head に arpeggio が重なっていた**(実測 before:
    arp x=[17.7,18.09] が反転 head b の ink 内)。`ChordHeadPositioning.CalculateOffsets` の最小
    オフセットだけ左へずらして最左 head を clear(after: x=[16.46,16.86]=Padding 0.5 確保)。
    LP 並置一致、fixture `test/arpeggio-second` 追加。`SpacingRules` の left-extent と同型。
  - **tie / slur** ― 未対応(deferred、低頻度×高難度)。tie は `TieFormattingProblem`、slur は
    `SlurScoringProblem`(LP でも最難)へ headOffset を導通する必要があり、波及大。発火は
    「2度和音 + tie/slur」と稀。腰を据えて別途。
- 中間クレフ/調号変更のスペーシングは LP の非音楽カラムの**近似**(端点リザーブ方式)。
  - ✅ **評価済み・deferred(2026-06-29 夕、§12-4)**。LP 並置比較で**目立つ欠陥なし**(クレフ前後ギャップ・
    位置とも LP とほぼ一致)。近似(次音符 spring に変更幅を上乗せ＋グリフ左 hang、`MeasureLayouter.cs:300-315`)は
    documented かつ「reserved=drawn 一致」。正しい LP 構造(独立非音楽カラム＝両側 spring＋改行可能点)へ
    揃えるのは**コア spacing への新カラム種別導入**で高コスト・高リスク・低可視効果。劣る点(中間クレフでの
    改行不可など)は稀。**実害例が出たらピンポイント対応**。
- inter-system spacing は X 依存スカイラインでなく per-system extent の近似。
  - ✅ **対応済み(2026-07-01 夜、commit `4fc5a0e`)**。非最適パス(`LayoutEngine.CreatePages`、既定SVG)が
    `perSystemSkylines` を**受け取っていながら捨てて**スカラー和を使っていた=最適パス(`PageLayouter.PositionSystemsOnPage`、
    `prevDown.Distance(nextUp)`)の配線漏れ。非最適側でも `Distance()` を使うよう修正。**座標整合(doc の懸念)は解決**:
    down skyline は system 参照Yから測る(`SkylineBuilder` の bottom-staff seed が `systemHeight-staffHeight/2`)ので
    `Distance()≈systemHeight+downExtent+upExtent`=スカラー和と互換。式は
    `minDistance = Math.Max(systemHeight + SystemSpacing, skylineDistance + padding)` で、**fallback(空 skyline)は
    byte-identical**。`Distance()` は真の per-X 最小クリアランスゆえ、フロア＋padding で**overlap は原理上発生しない**
    (「近づける」だけ)。**24 の複数システム snapshot が全て height 減(=詰まった、広がりゼロ)、数値スイープで staff-block
    gap 全て正、最密 1.80(showcase/01-expressions)も目視でインク非衝突を確認**。単一システムと fallback は不変。1986緑。
- eighths-vs-quarters の note 間隔が LP と ~4-8% ずれる ― ✅ **対応済み(2026-07-01 夜、commit `5d2358a`+`78e3eea`)**。
  **診断が2度誤っていた**(記録として明記):(a) doc 旧説「MinItemGap 0.4→0.1」は**誤り**=0.1 にすると過補正で
  LP をむしろ下回る(2.4 vs 2.504)。(b) audit 旧説「stem_dir_correction 乖離」も**誤り**=当該 pair は隣接2度で
  ヘッド重複ゆえ stemCorr=0(probe 実証)。**真因は2つ**、内部 spring 値の probe で確定:
  **(1) 幻の旗**=`CreateRightSkyline` が**連桁音符にも旗を足し**(旗ボックスが符頭レベルまで垂れ次の頭と Y 重複)、
  note-to-note rod を ~0.9 膨らませ連桁8分を rod-bound 2.596(LP 2.504)に。→ beam 所属を spacing に配線
  (`MeasureLayouter.IsItemBeamed`、`LayoutEngine` が `DetectBeamGroups` から reference-identity predicate 構築)し、
  連桁音符は旗を skip(`5d2358a`)。**(2) head-width ideal 欠落**=LP は `ideal = duration_space − increment + left_head_end`
  (`note-spacing.cc:77`)で左音符の実ヘッド幅を足すが Lily# は生 duration space。全 gap 一律 −0.104(=1.304−1.2)不足。
  → `SpacingRules.ApplyLeftHeadWidth`(`78e3eea`)。**結果**: eighths-vs-quarters が LP と丸め一致(eighth 2.5/2.504、
  quarter 3.7/3.704、per-gap 比 ~1.0)。(1)は連桁密部のみ7 snapshot(詰まる=改善)、(2)はほぼ全譜再ベース(一律 widen=
  衝突リスク無し、構造変化2件は line-break/長い trill wave=benign 実証)。**F3 との churn 懸念より正しさ優先で実施**。
  MinItemGap override 機構(`NoteSpacingParameters.MinItemGap`/`SeparatingPaddingTests`)は無関係の別物として残置。
- **交差声部の水平 note-collision(下声部が上声部より高音で符幹が交差)** ― ✅ **対応済み(2026-07-01 夜、commit `724f8cc`)**。
  **解決の要点**: LP 実測で真因と量が確定 ― `AnalyzeCollision` の末尾 `NoCollision` を LP の "meshing" フォールバック
  (`note-collision.cc:332-337`、ドット0.1/無0.17)へ差し替え、新 `CollisionType.Meshing` を付与。`CalculateVoiceOffsets` は
  Meshing 時だけ **rightMost をピン**(＝上声=連桁声部を列 X に固定し、**下声を左へ寄せる**)。分離量は LP と同一
  (`2*0.17*down_head_width`=全音符 0.668、4分 0.444 を LP 並置で一致確認)。**extent スケーリング(343-348)は Lily# の
  notehead 左 extent=0 かつ up-shifts-right ゆえ係数 1.0=no-op**(全音符が clear しきれなかった前セッションの症状は
  マグニチュード不足でなく**連桁声部を動かすと beam が列 X から外れる**問題だった=下声を動かす設計で回避)。equal-width は
  全 snapshot byte-identical、`multivoice-beam-collision` のみ A5＋加線が左へ 0.66(純差分)、新 fixture
  `test/multivoice-crossing-collision`(4分×8分)＋3 単体テスト追加。1986緑。**残り簡略化**: 下声は非連桁前提(LP がこの交差で
  出す全音符/4分では常に真)、dotted down-shifts-right の一般 extent スケーリングは据置。**push 保留中**。
  <details><summary>(以下・着手前の deferred 記録)</summary>
  多声ビーム一式(`VoiceIndex` 導通〜cross-voice **beam** collision まで)完了後に発見。repro=`voice{ c'8×8 }`(上・ステム上)＋
  `voice{ a'1 }`(下・A5 全音符、C5 より高い交差)。LP は下声部音を左へずらして上声部の符幹を clear するが、Lily# はずらさず
  **符幹が全音符を貫通**(垂直=ビーム高さは §cross-voice beam collision の commit で解決済、残るは水平のみ)。
  **根因(特定済)**: `NoteCollision.AnalyzeCollision` は「too far apart」ゲート通過後も衝突タイプ不一致だと `NoCollision`(シフト0)を返す。
  **LP は同位置で "meshing" フォールバック(0.17、ドット時0.1)を必ず適用**する(`lily/note-collision.cc:332-337`)。交差声部はこの経路。
  **なぜ即修正しなかったか**: フォールバックだけでは不足。(a) **マグニチュード**=LP は per-note extent スケーリング
  (`note-collision.cc:343-348`)＋`calc_positioning_done` で down-note 実幅を掛けるが、Lily# の note-collision は**固定 notehead 幅で近似**
  ゆえ全音符(幅広)が LP ほど動かず clear しきれない。(b) 試作すると全音符が**予想と逆方向(右)に 0.67 移動**し、pinning
  (どの声部が動くか)＋**連桁音の衝突オフセットと beam の列X描画の相互作用**を確証を持って説明できず、レンダも LP と明確一致せず。
  §0「理解できない/アドホックな修正は出さない」に従い**試作を revert**(byte-identical へ戻し済)。
  **希少性**: 発火に**声部交差**(下声部が高音)が必要=通常のポリフォニーでは起きない。
  **やるなら**: note-collision の **per-note 幅対応**(固定 notehead 幅→実 extent)という広めの refactor＋交差時 pinning/連桁相互作用の
  精査が要る。既存 collision fixture(equal-width は extent factor=1.0 で不変のはず)を担保しつつ mixed-width のみ再ベースライン。
  </details>

---

## 13. エディタ / LSP / 拡張機能のワークフロー(描画とは別系統)

§1〜12 は **描画(LilyPond 忠実再現)** の手順。2026-06-28 のセッションは主に
**エディタ機能(LSP 補完・プレビュー連携・拡張機能)** を扱った。別系統なので手順も別。

### 13.1 構成

| 対象 | 場所 |
|---|---|
| VS Code 拡張(TypeScript) | `editors/vscode/src/extension.ts`(webview プレビューも同ファイル) |
| 言語サーバ(C#) | `LilySharp.Lsp/LilySharpLanguageServer.cs`(Core を参照) |
| .lys フィクスチャ | `LilySharp.Tests/Fixtures/{test,showcase}/*.lys` |
| スナップショット | `LilySharp.Tests/Snapshots/*.svg` |
| ナビ記号の手検証用 scratch | `scratch/navtest.lys` |

### 13.2 ビルド・デプロイ

- TS のみの変更でも、deploy 前に型チェック: `cd editors\vscode; npm run compile`(exit 0 を確認)。
- デプロイ: `.\deploy-extension.ps1`(VSIX + LSP を再ビルド・再インストール)。
  `Get-Process "Code"/"lilysharp-lsp" not found` エラーは無害(VS Code 未起動時)。
- **CLI は自前の Core コピーを同梱する。** Core を変更したら、レンダリング確認の前に
  `dotnet build LilySharp.Cli\LilySharp.Cli.csproj` で CLI を建て直す(Core だけ建てても CLI の
  bin には反映されない)。
- **deploy は毎回バージョンスタンプを書き換える**: `LilySharp.Lsp/LilySharpLanguageServer.cs` の
  `public const string Version` と `editors/vscode/package.json` の `version`。**この2ファイルは
  deploy スクリプトの最終ステップが pathspec 指定で自動コミットする**(`Bump dev build version (...)`)。
  本体コミットと分かれてツリーがクリーンに保たれる ― 手動の `git add -u; git commit` は不要になった。
  他の作業中ファイルは pathspec で除外されるので巻き込まれない。deploy 後は `git push` するだけ。

### 13.3 LSP 補完(文脈依存)

`Completion()` → `GetCompletionContext()` が文脈を判定し、文脈別メソッドへ分岐:
TopLevel / MusicBlock / **StructureBlock** / **AfterClef** / AfterAt / AfterBackslash。

- `InnermostOpenBlock(text, offset)`: 最内の `keyword { … }` のキーワード(structure / chordnames 判定)。
  **`{` 直前の語が name 付きブロックでは name になる**(`section A {` → "A"、`structure {` → "structure")。
- `WordBeforeCursor(text, offset)`: カーソル直前の語(入力中の部分語をスキップ)。`clef ` 直後判定に使用。
- 文脈別: `GetTopLevelCompletions` / `GetMusicCompletions` / `GetStructureCompletions` /
  `GetClefCompletions`。並び順は `SortText` で制御(クレフは音域 高→低)。プレースホルダ付きは
  `InsertTextFormat = InsertTextFormat.Snippet` を**必ず**付ける。
- **登録スニペット(`package.json` の `snippets` 契約と `snippets/lilysharp.json`)は削除済み。**
  VS Code の登録スニペットは**位置でスコープできず**、`structure {}` 内に `grace` 等が漏れていた。
  補完は LSP(文脈依存)に一本化したので、**スニペットファイルを復活させない**。リッチな雛形
  (piano / render / lyrics / repeat-alt)は LSP の該当文脈へ移植済み。
- テスト: internal ヘルパは `LilySharp.Lsp.csproj` の `InternalsVisibleTo("LilySharp.Tests")` で公開済み。
  `StructureCompletionTests` / `ClefCompletionTests` が雛形。新文脈を足したら同様に internal 公開 + テスト。

### 13.4 エディタ⇔プレビューの相互参照(data-pos)

すべての grob は `gc.Source(sourcePosition)` 内で描画され、SVG に `data-pos="<ソースオフセット>"` が出る
(§4 の調査と同じ仕組み)。これがカーソル連携の土台:

- **カーソル→プレビュー**(`extension.ts` の `onDidChangeTextEditorSelection`): カーソルオフセットを
  postMessage。webview の `highlightNearestElement` が「`data-pos ≤ cursor` の最大値」を閾値 50 内で
  選んでハイライト。**空白の上(左右とも空白)は -1 を送って何もハイライトしない**
  (`highlightNearestElement(-1)` はクリアして何も選ばない)。
- **セクションラベルの data-pos** は `SectionDeclPos` = `section` **キーワード**の開始
  (`s.SectionKeyword.Span.Start`)。名前にすると `section` キーワード上で手前のノートに
  フォールバックするので、宣言全体がラベル箱にマップされるようキーワードに合わせる。
- **プレビュー→エディタ(クリック)**: data-pos → カーソル。空白を前方スキップして語頭へ。

### 13.5 PNG 拡大確認(pwsh System.Drawing — PIL の代替)

Python/PIL が使いにくいときは pwsh の System.Drawing で切り出し・拡大できる:

```pwsh
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($png)
$rect = New-Object System.Drawing.Rectangle($x,$y,$w,$h)
$c = New-Object System.Drawing.Bitmap($W,$H); $g = [System.Drawing.Graphics]::FromImage($c)
$g.InterpolationMode = 'NearestNeighbor'
$g.DrawImage($img,(New-Object System.Drawing.Rectangle(0,0,$W,$H)),$rect,[System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $c.Save($out)   # その後 Read ツールで $out を視覚確認
```

### 13.6 ナビゲーション記号の配置(MusicMarkEngraver / OutsideStaffStacker)

- `MusicMarkEngraver` が初期位置を計算し、その後 `OutsideStaffStacker.StackAboveStaff` が衝突回避で
  **再積層**する。**マークの最終 Y はここで決まる**(Engraver で Y を弄っても上書きされるので、
  最終調整は stacker の後段で行う)。
- 各マークは **per-measure の system-Y 基準(sysY)** で描画。同じ layout-Y でも measure が違うと
  描画高さが違う(セクションラベルが衝突回避で上に押し上げられるのもこれ)。
- D.S./D.C. 系(jump-from)は五線下。Segno/Coda・To Coda は五線上。To Coda は次セクションラベルと
  同じ小節線を挟むので `CoPlaceToCodaWithLabels`(stacker の後段)で同じ段に横並びさせる
  (**measure は付け替えず**共通ラインへ。改行時は近接 X 判定が外れて自動的に非適用)。

---

## 14. 次セッションへの引き継ぎ(2026-06-28)

### このセッションの成果(参考)

- **リファクタ Tier1/2**(レビュー由来): `RelativeOctave.StepToMidi`、`SyntaxNode.IsInside<T>`、
  `VerticalSkyline.Distance` の端点化、共有 `NullScope`/`ScopeAction`、`LayoutUtilities.ResolveStaffMiddleY`、
  `EmmentalerGlyphs.AccidentalGlyph`、`BeamScoringProblem.MinimumDy`、`LayoutEngine.FinalizeLayout`。
- **ナビゲーション記号**: D.S./D.C. を五線下へ、「To Coda」をコーダ記号(scripts.coda)で描画、
  Segno/Coda を小節線中心に、section ラベルを共通段に整列、To Coda を C ラベルの左・下端揃えに。
- **LSP 補完**: `structure {}` 文脈(セクション名/ナビ記号/リピート/ボルタ/`~Name`/`_""`、note 名は出さない)、
  `clef` 直後はクレフ名のみ(高→低順)。
- **プレビュー連携**: `section` キーワードでセクション記号をハイライト、空白上では非ハイライト。
- **パーサ**: `clef treble_8` を mid-music でも受理(従来は part ヘッダのみ)。

### 2026-06-29 セッションで対応済み

- **`treble_8` の "8"(ClefModifier)描画**(旧残課題1)― 完了。`SharedRenderer.DrawClefModifier8`
  を新設し `DrawClef`(ヘッダ/プレフィックス)と `DrawClefChange`(mid-music)から呼ぶ。LP 2.24 の
  treble_8 を PNG 並置・ピクセル実測で校正(digit ~2ss・font-size 0.80×・中心は底線下 ~1.3ss、
  クレフ下降部の真下)。スナップショット差分は純(`test__treble8.svg`×3・`test__instrument-defaults.svg`×2
  に `<text>8</text>` 追加のみ)。`ClefChangeTests` に header/mid-music の描画テストを追加。
  既知の簡略化: 中心オフセットと縦位置は字形メトリクス非測定ゆえ校正定数。Emmentaler のクレフ下降カールが
  LP より僅かに長く描かれる(既存のクレフ字形特性)ため "8" の絶対位置は LP より僅かに低い。
- **deploy の版バンプ commit ノイズ**(旧残課題3)― 完了。`deploy-extension.ps1` の最終ステップが
  版スタンプ2ファイルを pathspec 指定で自動コミットするようにした(他の作業中ファイルは巻き込まない)。
  gitignore 案は不可(両者とも多目的ファイル内のインライン値)ゆえ auto-commit を採用。

### 残課題(優先度の目安)

1. **相対オクターブの stateful driver 統合(Tier 2 で意図的に保留)。** アルゴリズムは
   `RelativeOctave.Resolve` で既に DRY。残るのは 3 ウォーカー(MeasureCollector / MidiExporter /
   MusicXmlExporter)の running-state ラッパで、アンカー規約が三者三様(C4 / octaveBase / C3)。
   高リスク・低リターンで見送った。やるなら全 octave/MIDI/MusicXML テストを担保しつつ慎重に。
   `RelativeOctave.cs` の remarks に「only the algorithm is shared」と明文化済み(再提案時はここを読む)。

### コミット規約(2026-06-29 確定)

`Co-Authored-By: Claude <current-model> <noreply@anthropic.com>` を**付ける**(§10 の運用を正とする)。
2026-06-28 セッションはセッション指示に従い付けていなかったが、ユーザー確認の結果 §10 を正と決定。
モデル名は current model に合わせる(例: 2026-06-29 は `Claude Opus 4.8`)。

---

## 15. 相対オクターブ連鎖の抽出 ― ✅ 完了(2026-06-29 夕)

**✅ このタスクは完了した。** `MeasureCollector` の「相対オクターブ連鎖(stateful driver)」を
専用 collaborator `OctaveContext`(可変クラス)に抽出済み(commit `638ee7e`)。下記の設計ガイドは
**経緯の記録**として残す。今回の実装サマリと**次の残課題**は §16 を見ること。

- 成果: `OctaveContext.cs` 新設。~10 フィールド(running state＋reset 先＋mode＋transpose 一族)を束ね、
  操作を命名(`Resolve` / `Snapshot`+`Restore` / `ResetToInitial` / `ResetForSection` / `ResetAll`)。
  アルゴリズムは `RelativeOctave` に共有のまま。**挙動完全不変**(snapshot byte-identical、全テスト緑)。
- ついでに死にコード `CollectMultiVoiceScore`(呼び出し元ゼロ、`<< \\ >>` 廃止の取り残し、生 `=4` アンカー)
  を削除。現役 polyphony は `voice { … }` ブロック→`BuildMultiVoiceScore`(part の既定オクターブ基準)。

**(以下は着手前の設計ガイド。当初「次セッションの主タスク」として書かれたもの。)**
`MeasureCollector` の「相対オクターブ連鎖(stateful driver)」を
専用の collaborator に抽出する。これは god-class 分解の**最後で最難**の継ぎ目(deferred endgame)。
§14 の残課題1 と同じ対象を、別角度(まず MeasureCollector から切り出す)で扱う。

### 背景 ― このセッション(PM)で済ませた god-class 分解

レビュー(5観点の並列レビュー)で `MeasureCollector.cs`(当時 4253 行)が唯一の god class と
判定。**家のスタイル(secondary-pass / collaborator 抽出)で3つ切り出し済み**。これが手本:

| collaborator | 中身 | 結合の解き方 |
|---|---|---|
| `TabResolver.cs` | tab tie/string 解決 | 完全な post-pass。voice だけ渡す。警告リスト2本を所有 |
| `ChordNameCollector.cs` | inline `c:m` + `chordnames{}` + `chords` 行 | 蓄積リストを所有。inline 経路は `AddInline` で本体から供給(`ArticulationsOf` は本体に残す)。context(section→measure / time-sig / staffIndex)は引数 |
| `LyricsCollector.cs` | note-bound 歌詞 + 独立歌詞行 | 蓄積リスト＋overflow 警告を所有。context は引数 |

**手本の原則**: collaborator が「蓄積物」を所有し、collection-time の context(`_sectionStartMeasure`,
`_timeBeats`/`_timeBeatType`, `_currentStaffIndex`, `_voiceMeasuresByName`)は**引数で渡す**。
全段で**スナップショット byte-identical**を維持(`git diff backup-or-baseline HEAD` 空)。現在 3559 行。

### なぜオクターブ連鎖だけ難しいか

上の3つは「**出力を貯める accumulator**」で切れた。オクターブ連鎖は逆に
**メインウォーク(`ProcessMusicNode` とピッチ解決)が読み書きするコア状態**で、note/chord/grace/
tuplet の一つ一つが「直前の状態に対して解決し、状態を更新する」。だから単純な後付けパスにはできない。
これが「等間隔に切れない」理由であり、レビューのエージェントも「最初にやるな」と明言した部分。

### 抽出対象の状態(フィールド)

`MeasureCollector.cs`(行番号は 2026-06-29 PM 時点、ズレうるので識別子で再 grep すること):

- `_currentOctave`(503)/ `_lastPitchName`(510) ― 走行中の running state(中核)
- `_initialOctave`(504)/ `_octaveBase`(509) ― セクション境界のリセット先 / 絶対モードの基準
- `_octaveAbsolute`(515)/ `_initialOctaveAbsolute`(516) ― 絶対オクターブモードのフラグ
- transpose 一族 `_hasTranspose`/`_transposeStep`/`_transposeAlt`/`_transposeOctave`(638-641)＋
  `ApplyTranspose`(1907)― オクターブ解決の**直後**に効くので連鎖と密。一緒に設計すること

### 読み書きマップ(grep で最新行を取り直す)

- **解決(read)**: `RelativeOctave.Resolve(...)` @ 3492。アルゴリズムは既に `RelativeOctave.cs` に
  DRY 済み(StepToMidi/StepIndex も)。ここで `_currentOctave`/`_lastPitchName` を読み、直後に
  更新(3257/3291/3427/3453-3454/3495 の `_currentOctave = rp.RelativeOctave` 系)。
- **per-voice セットアップ**: 671-686(初回)/ 854 / 885-889(多譜表ループ)。clef 既定オクターブ＋
  `ApplyTranspose` を絡める。
- **save/restore**: grace 1121-1139、tuplet/repeat 1264-1280(`savedOctave/savedPitch` で退避→復元)。
- **セクション境界リセット**: 1422-1423 / 2243-2244 / 2525-2526(`_currentOctave = _initialOctave; _lastPitchName='c'`)。
- **ファイルレベル reset**: 1877-1882(`Reset()`)。

### 推奨アプローチ(段階的・低リスク順)

1. **第一段(推奨開始点)**: `OctaveContext`(**可変クラス**)を新設し、上記フィールドを束ねる。
   `MeasureCollector` は `private readonly OctaveContext _octave = new();` を1本持つ。
   - 解決は `_octave.Resolve(pitch)` に集約(内部で `RelativeOctave.Resolve` を呼び running state 更新)。
   - save/restore は `var saved = _octave.Snapshot(); … _octave.Restore(saved);`(grace/tuplet)。
   - セクション/ファイルreset は `_octave.ResetToSectionStart()` / `_octave.ResetToFileDefault()`。
   - これで**フィールド ~8本→1オブジェクト**、概念に名前が付く。挙動不変(値は同じ)。
   - 可変クラスにするのは、メインウォークが頻繁に mutate＋save/restore するため(record + with は逆に煩雑)。
2. **第二段(任意・より純粋)**: 解決を純関数化し context を引数/戻り値で通す。コスト大。第一段が
   緑で安定してから、別コミットで検討。**最初からこれを狙わない**。

### §14 残課題1 との統合余地

§14 残課題1 は「3 ウォーカー(MeasureCollector / MidiExporter / MusicXmlExporter)が
`RelativeOctave.Resolve` を**各自の running-state ラッパ**で包んでおり、アンカー規約が三者三様
(C4 / `octaveBase` / C3)」という DRY 課題。**第一段の `OctaveContext` を Core 共通にすれば、
3ウォーカーで共有できる可能性**がある(アンカー規約は ctor 引数化)。ただし MIDI/MusicXML 側の
running-state も合わせて移すのは追加スコープ。まず MeasureCollector 単体で `OctaveContext` を
確立し、共有は次の一歩として評価する。`RelativeOctave.cs` の remarks に
「only the algorithm is shared」と明記済み(再提案時に読む)。

### 厳守事項・検証

- §0 の「アドホック禁止 / LILYPOND-REF」。挙動は**完全不変**(これは整理リファクタで、出力を変えない)。
- 検証: `dotnet test`(**ベースライン 1789 緑** / skip 3)。特に octave / relative / transpose / MIDI /
  MusicXML 系を必ず通す。加えてオクターブが効くサンプル(relative・transpose・grace・tuplet・
  多セクション)を render してスナップショット byte-identical を確認。
- 手順は §1 の鉄則(ripple shell / `Write` でファイル作成 / master 直 / 勝手にブランチ作らない)。
- コミットは §10＋§14 のコミット規約(`Co-Authored-By: Claude <current-model>`)。collaborator 1本ごとに
  「ビルド緑→全テスト緑→commit」を刻む(TabResolver/ChordNameCollector/LyricsCollector と同じ刻み)。

### リポジトリ状態(2026-06-29 PM 終了時)

- `master` = `origin/master`(同期済み)。このセッションのコミットは就業時間外帯に再配置済み
  (履歴 redate＋force-push 済み、private repo)。`backup/pre-redate`(ローカルのみ)に redate 前の
  tip を退避(不要になれば `git branch -D backup/pre-redate`)。
- A5(exporter の `tempo=120` 等リテラル集約)は「層別の妥当な既定で分散が自然」と判断し**見送り**
  (your call 項目)。再検討するならここから。

---

## 16. 次セッションへの引き継ぎ(2026-06-29 夕)― OctaveContext 後の残課題

### このセッションの成果

- **`OctaveContext` 抽出 + 死にコード削除**(commit `638ee7e`、§15 参照)。本体の god-class 分解は
  これで一区切り。`MeasureCollector` は collaborator(Tab/ChordName/Lyrics/Octave)に分解済み。
- **LSP デプロイ**: `deploy-extension.ps1` 実行済み(VSIX `0.1.2-dev.187` / LSP `0.1.1-20260629-1701`)。
  版バンプ自動コミット `f55d0bf`。

### 残課題(ユーザー指定の進行順: 6 → 4 → 3 → 5)

優先順は本セッションのレビューで合意。**6 → 4 → 3 → 5** の順で全て処理済み:
**[6] 調査→変更なし / [4] 評価→見送り / [3] 評価→スキップ / [5] 実装→完了(commit `84f1da0`)。**

1. **[6] `Reset()` が transpose 状態をクリアしない疑い ― ✅ 調査済み・変更なし**
   調査結果: **全本番経路で `MeasureCollector` は単一使用**(`SvgGenerator.BuildLayout` /
   `PngGenerator` / `PdfGenerator` / CLI / 各 validator が `new` → 1 回 collect。マルチムーブメントも
   `foreach(spec)` ごとに新インスタンス。`CollectMultiStaff` の声部ループは各声部で `ApplyTranspose`
   を呼ぶので stale しない)。新インスタンスは `HasTranspose=false` 初期化子で始まる → **transpose 残留は
   到達不能ゆえ無害**。ユーザー方針「理論リスクに先回りしない」に従い `Reset()` は**変更しない**。
   (どうしても内部一貫性で足すなら byte-identical な1行だが、現状不要。)

2. **[4] §14 残課題1 ― `OctaveContext` を MIDI/MusicXML で共有 ― ✅ 評価済み・見送り**
   調査結果: 真の共通部(`RelativeOctave.Resolve`/`StepIndex`/`StepToMidi`)は**既に DRY**。残る
   running-state ラッパは小さく、3ウォーカーで**正当に異なる**: `MidiExporter` は transpose 無・
   initial-mode 無だが repeat の save/restore に **velocity を octave と束ねる**; `MusicXmlExporter` は
   **transpose 有**(`_currentTranspose = PartTranspose.Read`)＋ `_initialOctaveAbsolute` の mode 復元有
   (collector に近い); `MeasureCollector` は absolute anchor=`OctaveBase`(可変)、reset=`InitialOctave`。
   → 共有可能な核は (step,octave,absolute)+Resolve+和音 first-pitch+`=4` リセットのみ(数行)。一方で
   機能セットが分岐する2 exporter を新共有型で**結合**するコスト＋MIDI/MusicXML の byte-identical 検証は
   重い。**kitchen-sink/結合増で見合わず見送り。** 3way も MIDI に transpose が無いため単一抽象は不適。

3. **[3] §15 第二段 ― `OctaveContext` 解決の純関数化 ― ✅ 評価済み・スキップ**
   評価結果: [4] を見送った今、純関数化の**主動機(共有を可能にする)が消えた**。真のコア
   `RelativeOctave.Resolve` は既に純(static・テスト済)で、`OctaveContext.Resolve` はその薄い
   stateful ラッパ。純化で得るのは「`LastPitchName` 変異を呼び出し側へ押し出す」だけで、note/grace/chord
   の**順序依存な変異シーケンス**(和音は per-pitch churn→末尾 first 復元で相対解決)を2〜3箇所に分散させ
   壊すリスクがある(§15 自身「コスト大・最初から狙うな」)。さらに [3](薄く純に)と [5](太く logic 取込)は
   **逆方向**で、stage-1 の可変 collaborator 設計と整合するのは [5]。→ **無期限スキップ。** やるなら別途。

4. **[5] transpose の凝集を締める ― ✅ 完了(commit `84f1da0`)**
   `OctaveContext` に `SetTranspose`(武装)/ `TransposePitch`(per-pitch 適用)/ `TransposeKeySharps`
   (調号シフト)を追加し、transpose の**状態とロジックを同居**。`MeasureCollector.ApplyTranspose` は
   effective target の composition(`ScoreTranspose` 依存=collector 関心)だけ担い、transpose フィールドを
   直接突かなくなった。`CalculateStaffPosition` の inline transpose は `_octave.TransposePitch(...)` に。
   `ComposeTranspose` は collector に残置。**挙動完全不変**(snapshot byte-identical、transpose 系
   fixtures/tests 緑)。

### 厳守事項(次セッション以降のための残し)

- これらは全て**処理済み**。今後 [3](純関数化)を再提案する場合は上記スキップ理由を読むこと。
- 一般則: 挙動変更([6] 型)と整理リファクタ([4]/[5] 型)を混ぜない(§8.3 差分純度)。
- 手順は §1 の鉄則、コミットは §10＋§14 規約(`Co-Authored-By: Claude Opus 4.8`)。1論点1コミットで刻む。

### リポジトリ状態(2026-06-29 夕)

- `master` は `origin/master`(`bea7d74`)より **先行・未 push**(**ユーザー指示で push 保留中**):
  - `638ee7e` OctaveContext 抽出 + 死にコード削除
  - `f55d0bf` 版バンプ(LSP デプロイ)
  - `59ca754` docs: §15 完了化 + §16 引き継ぎ
  - `58a10d4` docs: [6]/[4] クローズ
  - `84f1da0` [5] transpose 凝集(per-pitch 適用を OctaveContext へ)
  - (本コミット)docs: [3] スキップ + [5] 完了を §16 に反映
- 未追跡 `AI_POSITIONING_HANDOFF.md` は別件(本作業と無関係、温存)。

---

## 17. 次セッションへの引き継ぎ(2026-06-29 夜)― §12 描画パス

### §12(描画忠実再現)パスの成果
- **実装済み**: §12-1 cue 臨時記号縮小(`8fb2079`)、§12-2 装飾 seed 実寸(`fc90359`、現状 no-op の prep)、
  §12-3a arpeggio が反転 head を clear(`b36f881`)。
- **評価して deferred**: §12-3 の slur/tie(深い scoring 問題・低頻度)、§12-4 中間クレフ非音楽カラム
  (構造改修・可視欠陥なし)、§12-5 inter-system skyline(最適パスは実装済・非最適は広域 churn)、
  §12-6 MinItemGap 0.4→0.1(グローバル再間隔・要衝突検証)。理由は §12 各項目に明記。

### §12-4/5/6 と F3 の順序メモ
deferred 3 件はいずれも**広域 snapshot 再ベースライン**を伴う。F3(`LSP_F3_QUERY_GRAPH_DESIGN.md`、
意味解析〜レイアウトを query DAG に作り替える計画)は同じ `measure_natural_width`/`system_layout` を
触るので、これら spacing 系を着手するなら **F3 の前後関係を意識**(F3 後にまとめて再ベースラインする方が
無駄が少ない)。緊急の可視バグでなければ F3 と一緒に扱うのが効率的。

### リポジトリ状態(2026-06-29 夜、最終)
- **push 済み**: `origin/master` = `c69e3df`(11 コミット)。内訳: OctaveContext 一式(§15/16)+
  LSP デプロイ版バンプ + §12-1/2/3a 実装 + §12 評価/§17 docs。
- コミット日時は就業時間外帯(19:02〜21:04 JST)へ redate 済み(author/committer 両方、内容は byte 不変)。
  redate 前 tip `ce95dd7` はローカル reflog / `refs/original` に退避(不要なら掃除可)。
- 全テスト緑(**1788 passed / 3 skipped**)。作業ツリー clean(未追跡 .md 4 本は別件)。

---

## 18. このセッションの再利用可能な学び(2026-06-29)

§12 を一周して得た、次セッションの時短になる非自明な点。各 workflow 節の補足。

### 18.1 「直す前に no-op/到達不能を疑う」(§2 の補足)
§12 項目には**観測効果ゼロ**のものが混じる。実装前後で **before/after を実測**して「本当に出力が変わるか」を確認する。
- 確認テク: 修正を当てた後、`git stash push -- <file>` で当該ファイルだけ戻し、再ビルド・再描画して数値比較 → `stash pop` で戻す(§12-2/§12-3 で使用)。SVG から座標を grep するのが速い(`<line ... data-pos="N">` の min x 等)。
- §12-2 は seed が「装飾の**上に積む可動グロブ**」にしか効かず、現行マークは小節頭 X 固定で装飾と X が重ならない → no-op。**「効く消費者がいるか」をまず確認**。

### 18.2 テストケース構築の罠(§3.2 の補足)
- **和音内の相対オクターブ**: `<a' b'>` は2度にならない(`'` が各音に効き 5度等に化ける)。**2度和音は隣接レター(`<c d>` `<e f>`…)を plain で書き、SVG の y で検証**(同 timing の2 head が **Δy=0.5(device)=2度**、Δx≠0=反転)。
- **stem 方向と反転向き**: `ChordItem.StemUp => Average(StaffPosition) < 0`。反転 head が **右**=stem up、**左**=stem down。stem-down の2度和音(=左反転 head)を作るには平均位置を中線より上へ。単声でも平均で自動反転する。

### 18.3 装飾/アーティキュレーション・レイアウトの3箱(§6 の補足)
`ArticulationEngraver` は用途で箱を分ける。混同しない:
- `GetGlyphBBox`/`GetArticulationExtent`/`GetNearExtent` = **その装飾自身の位置決め**(ornament は simplified 1.0/0.5。trill すら実寸でなく 1.0=意図的)。
- `GetSeedBBox` = **outside-staff 占有の種**(可動グロブが避ける範囲)。実寸を使うのはここ(§12-2)。

### 18.4 グリフ計量パイプライン(§6 の補足)
`audit/scripts/Extract-EmmentalerMetrics.py` が font から `GlyphMetricsGenerated.cs` を生成。
- **追加手順**: `BBOX_GLYPHS` に `GlyphSpec("Name", 0xXXXX, ...)` を足す → `py -3 audit\scripts\Extract-EmmentalerMetrics.py` 再生成 → `GlyphMetrics.Name` で参照。
- **安全前提**: 触る前に「glyph を足さず再生成して zero-diff」を確認(決定性チェック)。差分が出たら font/tool ドリフト。

### 18.5 ページレイアウトの2経路(§12-5 関連)
SVG は既定で **非最適パス**(`UseOptimalPageBreaking=false`/`PageHeight=0` → `LayoutEngine.cs:503-513`、スカラー extent 縦積み)。最適パス(`PageLayouter.PositionSystemsOnPage`)は skyline `Distance` 実装済み。縦間隔をいじるときはどちらの経路かをまず確認。

### 18.6 未実装オーナメントのバックログ(§12-2 から派生・実需順)
追加すれば §12-2 の seed パターンがそのまま活きる。
- **A(頻出)**: down-bow/up-bow(`scripts.downbow`/`upbow`)、staccatissimo(`ustaccatissimo`/`dstaccatissimo`)、stopped/open(`+`/`○`)、harmonic/flageolet、snap pizzicato。
- **B**: thumb、espressivo、pedal heel/toe、fermata 変種(short/long/verylong)。
- **C(複合・横広=seed が活きる)**: prallmordent/upprall/downprall/upmordent/downmordent/lineprall、slashturn/haydnturn/schleifer。
- 加えて「**上向き note-anchored テキスト/markup(`^"text"`)**」を入れると §12-2 の seed 実寸が初めて可視で効く(現状の no-op を解消する消費者)。

---

## 19. 次セッションへの引き継ぎ(2026-06-29 深夜)― F3 増分コンパイル(エンジン化)

§1〜18 は **描画忠実再現 / エディタ** の系統。この節は別系統 ―
**意味解析〜レイアウトを Salsa 型の demand-driven メモ化クエリ DAG に作り替える F3** の引き継ぎ。

### 19.0 まず読むもの(3点セット)
1. `LSP_INCREMENTAL_IMPROVEMENT_PROPOSAL.md` ― 親提案(F0〜F4 の全体像)。
2. `LSP_F3_QUERY_GRAPH_DESIGN.md` ― F3 詳細設計。**§0.5「現行コード検証と前提の修正」と S-stage 進捗が常に最新**。本節と二重管理せず、詳細はそちらを正とする。
3. 本節(運用・現在地・次の一歩)。

### 19.1 ブランチ運用と方針(**最重要・通常ルールを上書き**)
- **ブランチ `f3-incremental` はユーザーが明示承認**(「必要ならブランチを切って、安全であることを確認できたら段階的にマージ」)。§ の「ブランチを勝手に作らない / master 直」を**この F3 作業に限り上書き**する。
- 各段は **ビルド緑＋全テスト緑＋snapshot byte-identical** を確認してから `git branch -f master f3-incremental`(ff)＋`git push origin master` で**段階マージ**(作業ツリーを切り替えない ref 移動方式)。
- **byte-identical は純 substrate の既定であって目的ではない(ユーザー指示)。** 出力が**より正しく**なる変更は歓迎 ― その時は snapshot を意図的に貼り直し、改善であることを示す。基準は「**正しさ＞現状維持**」。各段で byte-identical を過剰に強調しない。
- コミットは §10＋§14 規約(`Co-Authored-By: Claude Opus 4.8`)。1論点1コミット。

### 19.2 現在地(2026-06-29 深夜)
`origin/master` = **`4cc2316`**(= ローカル master = `f3-incremental`)。全テスト **1824 passed / 3 skipped**。S0〜S4b 完了:

| commit | 段 | 成果 |
|---|---|---|
| `9425073` | S0 | 設計を実コードに接地・前提訂正(§0.5) |
| `89ac986`/`d3baaf9` | S1/F0 | 差分安全網＋編集レイテンシ baseline |
| `55f9359` | S2 | `MeasureContext` 鎖(key/time) |
| `8eabb39` | S3 | clef 忠実化＋「relative 非依存は既達」発見 |
| `e7b3637` | S4a | 行分割ゲート明示化＋cutoff 実証 |
| `4cc2316` | S4b | `IncrementalCompiler`＝実カットオフ、増分==フル証明 |

### 19.3 作った主な部品(ファイル)
- `LilySharp.Core/Svg/Model/MeasureContext.cs` ― `readonly record struct {Key, Time, Clef}`。entry/exit context のペイロード。value 等値が early-cutoff 比較。
- `LilySharp.Core/Svg/Collector/MeasureContextChain.cs` ― post-pass で `entry/exit` 鎖を構築(`exit[i]=Advance(entry[i],measures[i])`)。
- `KnuthPlassBreaker.ComputeMeasureSpringData`(単一譜)/ `SystemBreaker.ComputeMultiStaffSpringData`(全譜結合・**実経路はこちら**) ― 行分割ゲート=`measure_natural_width` ベクトル。internal。
- `LilySharp.Core/Svg/IncrementalCompiler.cs`(**public**) ― 本丸。`Render()`でキャッシュ確立、`Edit(change)`で gate 不変なら `LayoutEngine.Layout(score, precomputedLineSizes)` 経由で**行分割 DP を skip**、変化なら full。`LastEditSkippedLineBreak` で観測。
- テスト: `IncrementalRenderEquivalenceTests`(S1 差分網)/`MeasureContextChainTests`/`LineBreakGateTests`/`IncrementalCompilerTests`。

### 19.4 検証済みの非自明な前提(次セッションが踏むと事故る所)
- **`measure_green(part,i)` は無料の安定キーでない**(小節は green ノードでなく collector が発見、`GreenNode` に構造ハッシュ無し、reuse は top-level のみ)。S5 で**安定 measure 識別子(content key)の製造**が要る。
- **行分割ゲートは構造的に既存**。`FindOptimalBreaks` は spring vector の純関数 → 幅不変編集は skip 可能(S4a/S4b で実証)。
- **レイアウトは既に relative 非依存**(`Svg/Layout` の相対オクターブ参照0件)。S3 の正規化は追加不要だった。
- **`Score.Clef` は初期 clef 忠実**。ただし `Collect(tree, voiceName)` で収集した時のみ(Phase 1.5 が part clef を読む)。`Collect(tree, null)` は clef が末尾状態になる罠 ― テストは必ず render-spec 経由(`RenderSpecParser.FindFirst`＋voiceName)で収集。
- **spanner/cross-staff は overlay**で自然幅に寄与しない(設計どおり)。臨時記号は barline reset ゆえ context は key のみ運ぶ。
- gotcha: `samples/music/*.lys`(fur-elise 等)は削除済 → **`RenderPipelineBenchmark` は dead path で実行時に壊れる**(未修正・別件)。生きた最大 fixture は `LilySharp.Tests/Fixtures/showcase/grammar-tour.lys`。

### 19.5 次の一歩 ― **S5: per-measure semantics/layout のメモ化**(効果の本丸)
- 動機: F0 実測で編集レイテンシは **render/layout が律速**(編集 ~9.7ms、行分割 DP はその一部)。S4b は DP しか省かない。**目に見える速度向上は `measure_semantics`/`system_layout` を per-measure でメモ化し、変わった小節・段だけ再計算してから**。
- 前提: **安定 measure 識別子**(訂正1)を作る。小節 items＋entry-context の content hash 等、編集で生き残るキー。
- deferred で S5 に持ち込む context 拡張: **octave 基準 / ottava / pending ties / open spanners**。これらは post-pass で忠実復元できず、**walk/green 駆動が必要**(S5 でまとめて)。`MeasureContext.cs` の remarks に明記済み。
- 健全性: 既に敷いた **`IncrementalCompiler` の増分==フル差分テスト**を S5 でも CI ゲートに。新スライスごとに「skip が発火し、かつ full と一致」を `IncrementalCompilerTests` 流で証明。

### 19.6 検証コマンド
```pwsh
cd C:\MyProj\LilySharp
dotnet test LilySharp.Tests 2>&1 | Select-String "Passed!|Failed!"      # 期待 1824/3 skipped
dotnet test LilySharp.Tests --filter "FullyQualifiedName~IncrementalCompiler"  # 増分==フル
git diff --stat master                                                   # substrate 段は tracked 差分ゼロが理想
```
- シェルは ripple MCP(§1 鉄則)。lilypond 比較が要るなら §3 のデッドロック回避起動。

### 19.7 任意・未了
- **速度の定量化**: F0 `IncrementalEditBenchmark` に session-edit ケースを足す(`[IterationSetup]` で session をリセットして1編集を測る)。S4b の DP 省略幅を数値化。
- `RenderPipelineBenchmark` の dead-path 修正(1行・別件)。
- 引き継ぎが依存する F3 文書(`LSP_INCREMENTAL_IMPROVEMENT_PROPOSAL.md` / `LSP_F3_QUERY_GRAPH_DESIGN.md`)は追跡済み。F3 と無関係な未追跡 .md 2本(`AI_POSITIONING_HANDOFF.md` / `RELEASE_BLOCKERS.md`)は別件ゆえ温存。
