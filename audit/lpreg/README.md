# lpreg — LP 双子とプローブの作業場（第170 で `scratch\lpreg` から移設）

**ここは「測ったことの一次資料」置き場。** LP の実物に当てて何が起きるかを見るための
`.ly`（LP 側）／`.lys`（Lily# 側）の対と、その比較を回す `.ps1`、および出力の `.txt` / `.log`。
`docs\HANDOFF-ARCHIVE.md` が **95 箇所からここを名指している**——だから repo の中にある。

## なぜ移したか（第170）

`scratch/` は `.gitignore` に入っているので、**clone した新しい PC には来なかった**。
引用が指す先が消える＝[REF 印](../../docs/RULES.md) の言う「真にし続ける対象」が
片方だけ消えることになる。移設と同時に **`docs\HANDOFF.md` / `HANDOFF-ARCHIVE.md` の
`scratch\lpreg\…` 102 箇所と、ここの `.ps1` 56 ファイル・290 箇所の自己参照**を
`audit\lpreg\…` に書き換えてある（バイト単位の置換なので他は 1 バイトも動いていない）。

## ⚠️ レンダリング結果（`.svg`）はここに無い

移設時点で **522 枚・2.1 GB** あったので**持ってきていない**（`.gitignore` の `*.svg` が
そもそも弾く）。**これらはここのスクリプト自身の出力**なので、必要になったら
作り直す——`audit\scripts\Run-LilyPond.ps1`（LP 側）と `lysc svg`（Lily# 側）。
再生成すると `.svg` はこのフォルダに落ち、`*.svg` の ignore がそのまま効く。
⚠️ **旧 PC の `scratch\lpreg\*.svg` は消していない**（そちらは stale なので参照しないこと）。

## 中身の数（移設時点）

| 種別 | 数 | 何か |
|---|---|---|
| `.lys` | 257 | Lily# 側のプローブ本 |
| `.ly` | 195 | LP 側の双子（[双子は lysc ly から作る](../../docs/RULES.md)） |
| `.ps1` | 68 | 比較器・A/B ベンチ（`perf-ab*.ps1` は打鍵の A/B、`compare-*.ps1` は SVG 突合） |
| `.txt` | 44 | 測定の出力 |
| `.log` / `.err` | 86 | LP の実行ログ |
| `.png` / `.pdf` | 19 | 目視用（[visual-diff は 1x ラスタ](../../docs/RULES.md)） |
| `.ily` / `.md` | 4 | dump 用インクルード・メモ |

## 使うとき

- **`.ps1` は repo ルートから走らせる想定**（中の相対パスは `audit\lpreg\…`）。
- **A/B ベンチを回すなら床の取り直しから**——`perf-ab*.ps1` が出した数は旧 PC の
  Release 実測で、**機械をまたいだ比較は成立しない**（生データは `../perf/`）。
