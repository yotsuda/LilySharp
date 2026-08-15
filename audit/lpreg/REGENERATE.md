# lpreg 成果物の再生成（別マシンで測り直す手順）

`audit\lpreg\` に在るのは**入力と道具だけ**（`.ly` / `.lys` / `.ps1` / `.ily` / `.log` / `.txt`）。
出力の `.svg` は `.gitignore` の `*.svg` が弾くので **clone には来ない**（移設時点で 522 枚・2.1 GB）。
この文書は**その 522 枚を作った手順**＝引用が数字を名指しているときに**出所を作り直す**ためのもの。
1 冊を新規に処理する手順（原本の選別・台帳の更新）は `../lp-regression/README.md` が正本。

## 0. 一行で

| 側 | コマンド | 出来るもの |
|---|---|---|
| LP | `cmd /d /s /c "$LP -dbackend=svg -dno-point-and-click --output=foo foo.ly < NUL > foo.log 2>&1"` | `foo.svg` ＋ `foo.log` |
| Lily# | `dotnet …\lysc.dll svg foo.lys -o foo-lys.svg` | `foo-lys.svg` |

## 1. 前提（新しいマシンで最初に確かめる 4 つ）

1. **LilyPond の実体** — `C:\bin\lilypond-2.26.0\bin\lilypond.exe`（`2.24.4` も併存）。
   本の `\version` 行がどちらを使うかを言っている。⚠️ **版で幾何が動く**ので、測った数字には
   どちらで出したかを書く。パスが違うマシンでは以下の `$LP` を差し替えるだけ。
2. **原本コーパス** — `C:\MyProj\lilypond-src\input\regression`（2097 本）。**repo 外**。
   frontier を進めるときだけ要る（**再生成には不要**＝双子は `audit\lpreg` に在る）。
3. **lysc** — `dotnet build LilySharp.Cli -c Release`。⚠️ **Core だけビルドすると lysc は旧 Core を抱く**。
   実体は `LilySharp.Cli\bin\<Config>\net9.0\`**`lysc.dll`**（`LilySharp.Cli.dll` は存在しない）。
4. **cwd** — `.ps1` は **repo ルート**から走らせる（中の相対パスが `audit\lpreg\…`）。
   ⚠️ **LP のレンダだけは `audit\lpreg` を cwd に**する——`.ly` 17 本が `\include "pcdump.ily"` を
   相対で書いている（`-I audit\lpreg` でも可）。

## 2. LP 側（`.svg` ＋ `.log`）

```powershell
$LP = 'C:\bin\lilypond-2.26.0\bin\lilypond.exe'
cd C:\MyProj\LilySharp\audit\lpreg
cmd /d /s /c "$LP -dbackend=svg -dno-point-and-click --output=pcsm pcsm.ly < NUL > pcsm.log 2>&1"
```

- ⚠️⚠️ **`cmd /d /s /c` ＋ `< NUL` は省略できない**。pwsh（MCP コンソール）の子として起動すると
  Guile 初期化でデッドロックする（CPU を数秒食って停止・待っても終わらない）。
- ⚠️ **`-dno-point-and-click` を付ける**。付けないと全 grob が
  `<a xlink:href="textedit://C:/…/foo.ly:14:74:75">` で巻かれ、**絶対パスが焼かれて
  マシンごとに違う SVG** になる（DOM も変わるので抽出器の走査が別物になる）。
- ★ **検算済み**（2026-08-15）: 2.26.0 ＋ 上の 2 フラグで `pcsm.ly` を再レンダすると、
  旧 `scratch\lpreg\pcsm.svg` と **SHA256 がバイト一致**した。⇒ **LP 側は完全に再現できる**。
- **stderr が成果物の本体になる本がある** — `pcdump.ily` / `dump-nc.ily` を include した本は
  grob 1 つ 1 行のレコードを stderr に吐く（`REST x=8.58 y=-2.78 dur=0 dir=() ink src=14:21`）。
  `> foo.log 2>&1` の **`.log` が本体で、`.svg` はおまけ**。
- ⚠️ **`.log` は tracked**（84 本・`*.log` は ignore されていない）。再生成すると上書きされて
  `git diff` に出る。**版や機械が違えば中身も違って当たり前**なので、意図せず出た差分は
  `git checkout -- <path>` で捨てる（測り直した値を残すなら、そう書いてコミットする）。
- `警告: 非対応の形式を無視 (pdf)` は無害。1 本あたり 3〜13 秒（初回はフォントキャッシュ分遅い）。

全部回す（195 本・目安十数分）:

```powershell
Get-ChildItem *.ly | ForEach-Object {
  cmd /d /s /c "$LP -dbackend=svg -dno-point-and-click --output=$($_.BaseName) $($_.Name) < NUL > $($_.BaseName).log 2>&1"
}
```

## 3. Lily# 側（`-lys.svg`）

```powershell
cd C:\MyProj\LilySharp
dotnet LilySharp.Cli\bin\Release\net9.0\lysc.dll svg audit\lpreg\foo.lys -o audit\lpreg\foo-lys.svg
```

- 出力名を省くと**入力と同名の `.svg`**（`foo.lys` → `foo.svg`）。§5 の命名の混乱はこれが出所。
- multi-score 本: `--all`（score ごとに別ファイル）/ `--combined`（1 枚に積む）/ `--score <name>`。
- ⚠️ **Lily# 側はバイト再現しない。それが仕事**。検算（同日）: `dot-column-vertical-positioning`
  を今の HEAD で出すと高さ **217.8 → 209.7** に動いていた。⇒ **旧 SVG と新 SVG の突き合わせは
  「退行の検出」であって「再生成の検算」ではない**。再生成が正しいかは LP 側で見る。
- 双子（`.ly`）を作り直すときは**手書きしない**——`lysc ly foo.lys foo.ly`
  （手書きはオクターブを取り違えて偽の発散を作る。`docs\RULES.md`）。probe は生成した `.ly` に
  `\override <Grob>.after-line-breaking` を**後から正規表現で挿す**（音楽は 1 文字も書かない）。

## 4. 比較・抽出（`.txt` を作る側）

- `compare-*.ps1` / `*-extract.ps1` は **repo ルートを cwd** に（`perf-ab*.ps1` は別物・§7）。
- 読み方の規約: LP は XML を walk して `translate()` を累積、Lily# は
  `<text class="music" x= y=>` を拾い、**y は `(centre − y) / space`** で
  staff space（Y-up・中心線基準）に直す。これで両者が同じ土俵に乗る。
- ⚠️ **ピクセル比較はしない**（visual-diff は 1x ラスタ）。`.png` / `.pdf` は目視用。
- ⚠️ **比較の前に paper を揃える**（双子の `.ly` は `\paper { indent = 0 ragged-right = ##t }` を持つ）。

## 5. 名前の規約（フォルダを読むときの地図）

| 形 | 誰の出力か |
|---|---|
| `foo-lys.svg` / `foo.lys.svg` / `foo-ls.svg` | Lily# |
| `foo-lp.svg` | LP |
| `foo.svg`（素の名前） | **どちらもある** |

⚠️ **素の名前＝LP、ではない**。`lysc svg foo.lys` の既定出力が `foo.svg` なので、単独プローブは
Lily# 側も素の名前で落ちる。実測（522 枚）: **素の名前 349 枚の内訳は Lily# 242 / LP 107**。
**中身で判別する**（1 行目）:

- LP … `<svg … version="1.2" width="210.00mm" height="297.00mm" viewBox=…>`（紙面 mm）
- Lily# … `<?xml …?>` ＋ `font-family="TeX Gyre Schola, serif"`（viewBox が staff space）

## 6. どこまで作り直せるか（522 枚の実測・移設時点）

| 出所 | 枚数 |
|---|---:|
| repo 内の `.ly`/`.lys` と同名（`audit\lpreg`） | 387 |
| 同（`audit\lp-regression\lys`） | 5 |
| **同名ソースが repo に無い** | 130 |

130 枚の内訳は `census-*` 8（`Fixtures`/`samples` の snapshot census 出力）・
世代サフィックス付き 39（`-BASELINE` `-OLD` `-fix` `-new2` `-head124` …＝その場で作った比較用の変種）・
その他 83。⇒ **この 130 枚は「作り直す」ものではない**。引用がそれを名指していたら、
**引用が要求している測定をやり直す**（変種は測定の途中経過であって一次資料ではない）。

## 7. やらないこと

- **旧 PC の `scratch\lpreg\*.svg` を参照しない**（stale・消していないだけ）。
- **`perf-ab*.ps1` の数字を持ち越さない**——機械が違えば床が違う。回すなら**床の取り直しから・
  Release で・構成をラベルに**（生データは `../perf/`）。
