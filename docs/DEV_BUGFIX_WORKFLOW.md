# Lily# 不具合発見・修正ワークフロー

Lily#(C#/.NET の LilyPond 風楽譜コンパイラ)のレイアウト不具合を発見・修正し、
LilyPond の実装に忠実に移植するための体系的な手順書。**次のセッションがこの文書だけで
同じ品質の作業を再現できる**ことを目的とする。散文は日本語、コマンド/パス/識別子は原文どおり。

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
git add -u
git commit -m "Bump extension dev version (0.1.2-dev.NN)"
git push
```

- master 直作業。ブランチは勝手に作らない。
- コミットメッセージは英語、`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` を付ける
  (current model 名に合わせる)。
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

- cue 和音の**臨時記号**はフルサイズのまま(`AccidentalPlacement` の幅も未縮小、対として整合)。
- トリル以外の装飾(turn/mordent 等)のフォント実寸が未抽出で、outside-staff 種に
  フォールバックボックスを使用。実重なりが見えたら計量生成パイプラインへ追加。
- スラー/タイ端点・アルペジオ括弧は和音内 2 度の符頭変位 X に未追従(未変位の列 X のまま)。
- 中間クレフ/調号変更のスペーシングは LP の非音楽カラムの**近似**(端点リザーブ方式)。
- inter-system spacing は X 依存スカイラインでなく per-system extent の近似。
- XFAIL: eighths-vs-quarters の MinItemGap 0.4 vs LP skyline-horizontal-padding 0.1(追跡中)。
