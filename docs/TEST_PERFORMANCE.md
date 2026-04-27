# テスト実行速度の最適化

## 問題

`dotnet test` の実行が非常に遅い（1テストあたり7-15秒）。

## 原因

Windows Defender のリアルタイム保護が、dotnet 関連ファイルをスキャンしていた。

## 解決策

以下のパスを Windows Defender の除外リストに追加する。

### 除外パス

| パス | 理由 |
|------|------|
| `C:\MyProj` | プロジェクトフォルダ |
| `C:\Program Files\dotnet` | .NET SDK/Runtime |
| `%USERPROFILE%\.nuget` | NuGet パッケージキャッシュ |

### 設定方法

管理者権限の PowerShell で実行:

```powershell
Add-MpPreference -ExclusionPath "C:\MyProj"
Add-MpPreference -ExclusionPath "C:\Program Files\dotnet"
Add-MpPreference -ExclusionPath "$env:USERPROFILE\.nuget"
```

### 設定確認

```powershell
Get-MpPreference | Select-Object -ExpandProperty ExclusionPath
```

## 計測結果

### 環境

- Windows 11
- .NET SDK 9.0.308
- xUnit 2.5.3

### 単一テスト実行 (LexerTests.LexSingleNote)

| 状態 | 実行時間 |
|------|---------|
| 改善前 | 7-15秒 |
| **改善後** | **4-5秒** |

### 改善率

約 **50-65%** の高速化

## 備考

- シンプルな空の xunit プロジェクトでも改善前は7秒かかっていた
- LilySharp 固有の問題ではなく、環境の問題だった
- `xunit.runner.json` の並列設定は無効のまま（並列実行は逆に遅くなった経緯あり）

## 関連設定

```json
// LilySharp.Tests/xunit.runner.json
{
  "parallelizeTestCollections": false,
  "preEnumerateTheories": false,
  "parallelizeAssembly": false,
  "maxParallelThreads": 1
}
```

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2025-01-09 | 初版作成。Windows Defender 除外設定による最適化 |
