# Lily#

**セッション開始時に `docs/HANDOFF.md` を読むこと。** そこに現在地・ロードマップ・
恒久ルール・コマンド集がある。**引継ぎ用の新しい `handoff-*.md` を作らない**
（root に残っている 15 個は旧方式の残骸。`docs/HANDOFF.md` §8 参照）。

以下は、外すと実害が出るものだけの抜粋。詳細は `docs/HANDOFF.md`。

## 絶対に外さないこと

- **`docs/HANDOFF.md` §1 の「現在地」は書いた時点のスナップショット。**
  HEAD・テスト数・シンボル名は**開始時に実コードで裏取りする**
- **push はユーザー。**「done」は push 済みでのみ主張。ship = 全緑 ＋ 明示承認
- **出力を変える変更はユーザー承認前に出荷しない。**
  snapshot 再ベースも **LP 照合 → 承認 → 実行**
- **master 直コミット。ブランチを勝手に作らない**
- **Co-Authored を付けない**
- 未コミットの `audit/scripts/Extract-EmmentalerMetrics.py` は別作業の WIP。**触らない**

## 実装するとき

- レイアウト/描画は `C:\MyProj\lilypond-src` の `lily/*.cc` を**符号一致で字面移植**。
  独自の近似を入れない。移植したら **`// LILYPOND-REF: lily/xxx.cc:行`** を必ず付ける
- **座標系が揃っていなくて字面移植が難しいときは、押し込まず報告する**
- **doc・コメント・過去の結論を疑う。ただし疑った結果も裏取りする**
  （`LILYPOND-REF` があっても式が一致しているとは限らない）
- **推論せず測る。** LP 幾何の精密測定に SVG を使わない（座標が 2 桁に丸められる）。
  `LilySharp.Tests/LpFidelity/` の記録用コンテキストを使う

## 環境

- **シェルは pwsh MCP / ripple（bash 禁止）。** ファイル書き込みは Write ツール
  （PowerShell に heredoc は無い。commit message はファイルに書いて `git commit -F`）
- **`dotnet` の増分ビルドが腐る** → 前後比較は `--no-incremental` でビルドして `--no-build` で実行
- **LilyPond は Guile デッドロックする** → `cmd /c "... < NUL"` でデタッチ必須

## 品質指標

`audit/lp-geometry/` の **LP 忠実度台帳**が LP との距離を数値で持つ。
snapshot は「前回の自分」との比較なので誤りが固定されうる。台帳は LP との比較で、
**残差が増えても減ってもテストが落ちる**（改善も意図的に記録させるため）。

```powershell
dotnet test LilySharp.Tests\LilySharp.Tests.csproj --no-build `
  --filter 'FullyQualifiedName~Corpus_ReportsTotalDivergence' --logger 'console;verbosity=detailed'
```
