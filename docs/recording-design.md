# 録音を聴きながら直す（設計メモ）

Dettato の REQUESTS #4（2026-11-15・`C:\MyProj\Dettato\docs\repro\lilysharp-requests-2026-11-15-listening.md`）と、
REQUESTS #1（録音を `.lys` に変換するメニュー）を 1 つの道にする設計。目標はオーナーが長年使っている
Transcribe!（Seventh String）と同じ体験を Lily# のプレビューで（用途の中心はエレキベースの耳コピ）。

## 1. オーナー決定（2026-11-15・第1283）

1. **音源と `.lys` の紐づけは、`.lys` の隣のファイルだけに置く。`.lys` には何も書かない。**
   - 隣のファイルは Dettato の `<名前>.dettato.json`（SIDECAR.md 形式 1）を広げたもの（形式 2＝人が直してよい）。
     音源は同じフォルダのファイル名（パスは持たない）。小節の時刻・人が確かめた印・拍・section のマーク・
     名前つきのループ・調律のずれ（セント）もここ。
   - 理由: 小節の時刻は音楽ではなくその録音の情報（パス 1 行だけ `.lys` に書いても数は別に要り、置き場所が 2 つになる）。
     `.lys` は人に渡す物で、音源は大きく著作物のことも多く、パスは機械ごとに違う。隣のファイルを消せば機能ごと消え、`.lys` は一字も変わらない。
   - Dettato を使わずに人が録音を選んだときも、Lily# が同じ形のファイルを作る（Transcribe! のように小節を人が打つ所から始める使い方も同じ道）。
   - `.lys` の名前を変えたら、拡張がリネームを拾って隣のファイルも変える（紐づけを切らない）。
2. **音源が無いとき、VS Code の UI には何も出さない。** 入口はボタンを置かない所だけ（§2）。
   隣のファイルがある `.lys` のプレビューにだけ録音の UI が現れる。
3. **小節マークはプレビューのヘッダ（ツールバー 1 行）の下に出す、畳める帯。** 役目を分ける:

   | 場所 | 役目 |
   |---|---|
   | 楽譜 | 「どの小節か」。小節を押すとそこから録音が鳴る・範囲を選んでループ・鳴っている小節を追う。浄書の絵には何も描き足さない |
   | ヘッダの下の帯（録音があるときだけ・畳める） | 「録音のどこか」。波形と小節・拍・section のマーク・再生位置。M・B・S で打ち、ドラッグで直す。人が確かめたマークは濃く、確かめていないマークは薄く。ループの範囲 |
   | ツールバー（1 行のまま） | 足すのは 2 つだけ: 鳴らす物の切り替え（楽譜の音／録音）と、録音パネルを開くボタン（Practice と同じ形。速度・セント単位の音高・オクターブ・音高と速度の連動・EQ） |

   楽譜には時間の軸が無い（小節の幅は音符の数で決まる）ので、マークを打つ・直す・テンポの揺れを見るのは帯の仕事。
   帯は再生位置のまわりを拡大して見せ、全体は細い全景で示す（3 分の曲を 600px に並べると 1 小節が約 6px で打てない）。
   下のパネル（ターミナルの場所）に出す案は、楽譜から目が離れるので採らない。

4. **（2026-11-15・同じ便）伸縮のエンジンは Signalsmith Stretch（MIT・公式の Web 版を同梱）。**
5. **m4a は (a)**: Windows では LSP（.NET）が Media Foundation で復号し、拡張の保存場所（`globalStorageUri`）に置いて webview に渡す。
   mac／Linux の m4a は最初の版では受けない（受けないと言う）。wav・mp3・flac・ogg は webview が自分で復号する（§3 で実測）。
6. **最初の版は §4 の 1〜4。**
   小節を打つ操作について（オーナー 2026-11-15「Transcribe! を意識しすぎな感じもするが、便利な機能だから拝借してよい」）:
   Lily# では意味が違う＝Transcribe! は打った所に小節を**作る**が、Lily# の小節は `.lys` が持っていて、打つのは
   「楽譜の次の小節は録音のここから」と**合わせる**操作（小節の数は楽譜が決め、録音がそれに揃う）。だから
   VS Code のコマンド（仮名 `Lily#: Mark Bar Line Here`）にし、キーは既定の割り当て（プレビューにフォーカスがあるとき）で、
   利用者が `keybindings.json` で変えられる。M に固定しない。

## 2. 入口（メニュー）

- 録音を `.lys` に変換するメニューは **Lily# が持つ**（オーナー決定 2026-11-15・第1264: 名前「Convert Recording to Lily# (.lys)…」・
  Windows 以外では出さない）。**同日・第1284 のオーナー決定で変えた**: D3 を待たずに今作る・Windows では Dettato が無くても出し、
  実行したときに無ければ拡張のインストールを勧める（Calamo の「Convert Image/PDF…」と同じ形）。実装は `src/dettato.ts`・`dettatoCore.ts`（`da1d1caac`）。
  ⚠️ `yotsuda.dettato` は Marketplace に出ていない（2026-11-15）＝「Install Dettato」は今は拡張の検索を開くだけ。今の Dettato の拡張の
  `dettato.transcribe`「Dettato: Transcribe Recording to Lily# Chord Chart (.lys)… (Preview)」は仮置きで、
  Lily# のメニューが出た版で外してもらう（REQUESTS で知らせる）。
- 音源ファイルの右クリックとコマンドパレットに 2 つ:
  - **Convert Recording to Lily# (.lys)…** — Dettato の拡張があるときだけ（`lilysharp.dettatoAvailable`）。`.lys` と隣のファイルを作って開く。
  - **Attach Recording…** — 今ある `.lys` に録音を紐づけ、隣のファイルを作る（Dettato は要らない）。
- どちらも結果は「隣のファイルがある `.lys`」＝その先の帯とパネルは 1 つの道。

## 3. 測ったこと（2026-11-15・第1283・Lab `sessions/p1283/stretch`）

実録音（Dettato `scratch/d3/real` の P001・section D の 8 小節 14.2 秒）を 4 通りに変えて、3 つのエンジンで書き出した:
+1200 セント／0.5 倍速／0.5 倍速＋1200／+30 セント。手本として「連動オン」（再標本化だけ・ffmpeg の soxr）も書いた。
聴き比べは `listen.html`（wav は Lab の gitignore が落とす＝`scripts/` で作り直す）。

| | Signalsmith Stretch | SoundTouch | Rubber Band |
|---|---|---|---|
| ライセンス | MIT | LGPL-2.1 | GPL（商用あり） |
| Web の形 | 公式の `signalsmith-stretch`（npm 1.3.2・114 KB・WASM＋AudioWorklet）。バッファの再生・`rate`・`semitones`・`loopStart`／`loopEnd`・予定した変更（`schedule`）が最初から入っている | `soundtouchjs`（純 JS・`PitchShifter` は ScriptProcessor＝非推奨。AudioWorklet は別パッケージ） | Web 版は調べていない（ここでは ffmpeg 内蔵の R2 で書き出した） |
| 重さ（14 秒の処理、1 コア） | 0.26〜0.57 秒（約 2%・ヘッドレス Edge の OfflineAudioContext） | 0.03〜0.08 秒 | 0.5〜1.5 秒（ffmpeg 込み・pitchq=quality） |
| アタックの一致（音高だけ変える側・1 が最良） | +1200: **0.945**・+30: **0.981**（手本と同じ） | 0.862・0.879 | 0.859・0.964 |

- 拡張は GPL-3.0-or-later なので 3 つとも同梱できる。
- スペクトルの距離（dB）は測り方そのものの底が約 6 dB で、0.5 倍速の尺度も「良い伸縮」を表せていない＝差は判断に使わない（`metric.txt`・`scripts/metric.py`）。スペクトログラムでは SoundTouch は立ち上がりが二重、Rubber Band（R2）は低い帯がぼやける（`pic/all-p1200.png`）。
- **実物の VS Code（1.146・Electron 43）の webview の中で**（`vscode-e2e/run.js`・`result.json`）: `decodeAudioData` は wav・mp3・flac・ogg（Vorbis）が OK、**m4a（AAC）と opus は失敗**。Signalsmith の AudioWorklet は動く（4 秒を 77 ms）。
- +1200 の出力はピークが 1 を超える（1.177）＝音高を変えるときは余裕（例 −6 dB）かリミッターが要る。

**推奨: Signalsmith Stretch。** MIT・公式の Web 版・ループと予定した変更が入っていて、測った中で音高のずれ方が手本に一番近く、重さは 2%。最終の判断はオーナーの耳（`listen.html`）。

## 4. 最初の版の案

1. 入口: 「Attach Recording…」（wav・mp3・flac・ogg）と、Dettato の `x.dettato.json` を読むこと。変換メニューは D3 の後（§2）。
2. ツールバー: 鳴らす物の切り替え（楽譜の音／録音）と録音パネル（速度・半音とセント・オクターブ・連動のオン／オフ）。
3. 楽譜: 小節を押すとそこから録音、選んだ範囲はループ（今の Practice の Loop と同じ選び方）。
4. 帯: 波形・小節のマーク・再生位置。**M で小節を打つ・ドラッグで直すまでは最初の版に入れる**（Dettato を使わずに録音を付けた人は、小節を打たないと小節から鳴らせないため）。
5. 次の版: 拍（B）・section（S）・範囲のテンポ・名前つきのループ・EQ とカラオケ・ショートカットの設定・ペダル。

**決めてほしいこと**: ⑴ エンジン（推奨 Signalsmith）／⑵ m4a の扱い＝(a) Windows では LSP（.NET）が Media Foundation で復号して拡張の保存場所（`globalStorageUri`）に置く・mac／Linux の m4a は最初の版では受けない、(b) Dettato が入っているときだけ受ける、(c) 最初の版は m4a を受けない／⑶ 最初の版の範囲（上の 1〜4）。

## 5. 段取り（最初の版）

段ごとに網を付け、段の終わりで full と拡張のテストを回す。手本は歌い手の声（外から来た音のバッファを楽譜と同期して鳴らす道・`extension.ts` の `voice`／`playVoiceSample`）。

| 段 | 中身 | 確かめ方 |
|---|---|---|
| S1 | 隣のファイルの読み書き（純粋な TS・`recordingCore.ts`）: `.lys` の隣の `<stem>.dettato.json` を探す・形式 1 を読み形式 2（`byHand` ほか）を書く・小節（form をたどった演奏の順）↔ 録音の秒の対応（弱起・反復・最後の小節が録音より長い形） | `editors/vscode/test` の単体の網 |
| S2 | 「Attach Recording…」（コマンドパレットと音源の右クリック）・`.lys` のリネームで隣のファイルも変える・m4a は LSP の Media Foundation で復号（Windows）して `globalStorageUri` に置く | 網＋`--extensionTestsPath` で実物の VS Code |
| S3 | webview の録音の再生: Signalsmith を同梱（`LICENSE` と NOTICE）・秒から鳴らす・ループ・速度・半音とセント・オクターブ・連動のオン／オフ（オンは再標本化）・余裕かリミッター。ツールバーの切り替えと録音パネル（隣のファイルが無ければ出ない） | 網（`previewToolbar` の型）＋OfflineAudioContext で書き出して測る |
| S4 | 楽譜の小節を押すとそこから録音・選んだ範囲はループ・再生位置の小節を追う（録音の秒 → 小節） | 網＋実物 |
| S5 | 帯: 波形・小節のマーク（人が確かめたものは濃く）・再生位置・再生位置のまわりの拡大と全景・`Mark Bar Line Here`・ドラッグ。書いたら隣のファイルの `byHand` | 網＋実物（CDP か `--extensionTestsPath`） |
| S6 | `MANUAL-CHECKS`・CHANGELOG・Dettato の REQUESTS への返事（m4a は Lily# が自分で復号するので PCM を渡してもらう必要はない・形式 2 の項目の合意） | — |

## 6. まだ決めていない細部

- キー操作（M・B・S はプレビューにフォーカスがあるときだけ）、拍への吸い寄せ（オフにできる）、ペダル。
- 隣のファイル（形式 2）の項目と、書き手の取り合い（Lily# はマーク・ループ、Dettato は読み直し）。人が小節を足し消しして小節数が合わないとき。
- 速度・音高・EQ の値をファイルに書くか、表示の状態にするか（Lily# が決めてよい）。
