# .lys の音を 1 本ずつ渡す API（設計の案）

Shosa の依頼 3・4 の続き（`C:\MyProj\Shosa\docs\repro\REQUESTS.md`）。

## オーナー決定（2026-11-20・第1339）

- Shosa（VS Code の拡張・フィギュアがプレビューの中で踊る）は、音源ではなく **.lys を参照**したまま鳴らす。
  メロディ・歌詞・テンポを直せば、Shosa で鳴る音も変わる。
- **Shosa のプレビューに音量ミキサーを出す**（オーナー「便利だし、ユーザーも楽しいと思う」）。
  録音を混ぜる場合も同じ操作にする。
- そのため Lily# の拡張は、.lys の音を**混ぜる前の 1 本ずつ**で渡す API を exports に出す。混ぜるのは Shosa。
  ミキサーを動かすたびに Lily# が作り直すと待たされるため。
- 書き出したファイルの名前を隣のファイルに書く案（`mix`）は取り下げた。
- 録音は伸ばさない（2026-11-20、recording-design.md §5）。速さはいつも 1.0。

## 形（案）

`vscode.extensions.getExtension('yotsuda.lilysharp').exports` に足す。`lyscPath`（依頼 1）と並べる。

```ts
interface LilySharpStemsApi {
  /** 版。形を変えたら上げる（Shosa は 1 だけを読む）。 */
  stemsVersion: 1;
  /** .lys の音を 1 本ずつ。開いているエディタの中身（保存前でも）があればそれ、なければディスクの中身から作る。 */
  renderStems(lys: vscode.Uri, options?: StemsOptions): Promise<Stems>;
  /** その .lys の音が変わりうるとき（文字を打ち終えて PREPARE_IDLE_MS 休んだとき、
   *  隣のファイルが変わったとき）に知らせる。Shosa は renderStems を呼び直す。 */
  onDidChangeStems: vscode.Event<vscode.Uri>;
}

interface StemsOptions {
  /** どの score か（`score 名`）。省略すると最初の score（プレビューと同じ）。 */
  score?: string;
  /** 隣に録音があっても使わない。既定は false ＝ 使う。 */
  withoutRecording?: boolean;
  signal?: AbortSignal;
  /** 歌声を作るのに時間がかかるとき（数十秒から数分）の進み具合。 */
  onProgress?(p: { stem: string; done: number; total: number }): void;
}

interface Stems {
  /** 時間の軸。'recording' なら全部の音が録音の秒の上にある（録音の 0 秒が 0）。
   *  'score' なら楽譜の秒（書いたテンポ）の上にある（`lysc timeline` と同じ秒）。 */
  timeline: 'recording' | 'score';
  stems: Stem[];
  /** 作れなかったもの（歌える声が無い、録音が見つからない、など）。音は残りだけで鳴る。 */
  notes: string[];
}

interface Stem {
  kind: 'part' | 'voice' | 'recording' | 'recordingPart';
  /** ミキサーの行の名前。part と voice は楽譜の名前、recordingPart は隣のファイルの parts の名前。 */
  name: string;
  /** 音のファイル（WAV、FLAC、MP3 など。webview の decodeAudioData で読める形）の絶対パス。
   *  part と voice は Lily# の保存場所（globalStorageUri）。recording と recordingPart は元のファイル。 */
  file: string;
  /** そのファイルの標本 0 が鳴る時刻（timeline の秒）。負もある（歌声は子音のために 0.3 秒前から始まる）。 */
  at: number;
}
```

## 中身

- **part**: 楽譜のパートごとに、プレビューと同じ SoundFont とシンセ（`renderNotes`）で描いた WAV。
  - パートごとに別に描く。足せば全体の音になる（リバーブは各チャンネルのセンドなので、別に描いても和は同じ）。
  - 正規化はしない（`fileLevel` を通さない）。パートの間の釣り合いを保つためで、プレビューの master 0.9 の手前と同じ音量。
  - 歌うだけのパート（歌声が代わりに鳴るもの）は出さない。歌える声が無ければ楽器で出し、`notes` にそう書く（プレビューと同じ）。
  - メトロノームと Count-in は出さない。
- **voice**: 歌声ごとの Cantoria の WAV。プレビューと同じ鍵なので、プレビューで一度歌ったものはすぐ返る。
- **recording / recordingPart**: 隣のファイルの `audio` と `parts` を、そのまま返す（手を加えない）。
  - m4a は、Lily# が Windows で復号して置いた WAV を返す。
  - Shosa は `recording`（元の録音）か `recordingPart`（分けたパート）のどちらかを鳴らす。
    Lily# のプレビューと同じ規則にするなら、パートが全部オンで 100% のときは元の録音、そうでなければパートの和（recording-design.md §8）。
- **時刻**: 録音を使うときは、part と voice を録音の小節の秒へ移して描く（`timeWarp`、依頼 4 と同じ）。
  小節の数が合わない、小節を打っていない録音は、`timeline: 'score'` にして録音を外し、`notes` に理由を書く。
- **置き場所と寿命**: part と voice は内容の鍵（notes・SoundFont・声・warp）で保存場所に置き、同じ鍵なら描き直さない。
  古いものは数を決めて消す（歌声の既存のキャッシュと同じ扱い）。

## Shosa の側（Lily# からの提案）

- `at` に合わせて `AudioBufferSourceNode.start(t0 + at, max(0, -at))` で鳴らし、行ごとの GainNode を通してから、リミッターで足す。
- ミキサーの値は Shosa が持つ。Lily# のプレビューの Practice の値は渡さない（保存されない、ページのもの）。

## オーナーの答え（2026-11-20）

1. **パートごとに 1 本。**
2. **録音は Lily# と同じ規則**: パートが全部オンで 100% なら元の録音、そうでなければ分けたパートの和（recording-design.md §8）。
   Shosa のミキサーもこれに従う。
3. **最終的に作るべきものを作れるように**（オーナー: 「FLAC はなじみがない。mp3 の方がなじみがある」）。
   決めた形（オーナー了承 2026-11-20「その形で進めて」）:
   - 1 本ずつの音は**途中のファイル**で、利用者の目には触れない。作るのが一番速く、時刻が標本単位で正確な WAV にする
     （楽譜のパートは 32 bit float・44.1 kHz。歌声は Cantoria の WAV そのもの）。
     MP3 は劣化が重なり、時刻のずれ（符号化の遅れ）が入る余地があるので使わない。
   - 利用者が受け取る**最終のファイル**（MP3 か WAV）は Lily# が書く。
   - ★ **実装で形を 1 つ変えた**: 案の `exportMix(lys, levels)`（Lily# が混ぜる）ではなく、
     **混ぜるのは Shosa の webview、書くのが Lily#**（`writeAudio(mix, { format, path })`）。理由は録音の復号:
     拡張（Node）には MP3／FLAC の復号器が無く、LSP の Media Foundation は糸 (Bank Band).mp3 を **528 標本（16.5 ms）早く**
     復号した（ffmpeg・Chromium とは 0 標本差。Lab `sessions/p1339/align/peaks.mjs`。MF は lamejs の印の無い MP3 も 577 標本遅らせ、
     印のある MP3 は合う＝MP3 の印の読み方が違う）。Shosa の webview は鳴らすために 1 本ずつを Chromium で正確に復号して持っているので、
     同じ値で `OfflineAudioContext` に描いて 2 チャンネルを渡せば、鳴っている音と書き出す音が同じになる。ffmpeg も要らない。
   - 踊りの動画（MP4）は、Shosa がこの音を映像に付ける。

```ts
  /** Shosa が混ぜた 2 チャンネルを、そのまま（音量を変えずに）書く。MP3 は 32000／44100／48000 Hz で LAME タグつき
   *  （0 秒が混ぜた音の 0 秒）。それ以外の値は理由つきで断る（stemsCore.mixProblem）。 */
  writeAudio(mix: { left: Float32Array; right: Float32Array; sampleRate: number }, options: { format: 'mp3' | 'wav'; path: string }): Promise<void>;
```

## 実際の形（第1339）

- `src/stems.ts`（`stemsApi`）・`src/stemsCore.ts`（`notesByPart`・`stemFileName`・`floatWav`・`mixProblem`）。exports は
  `stemsVersion`・`renderStems`・`onDidChangeStems`・`writeAudio`（`lyscPath` と並ぶ）。
- `renderStems` は WAV の書き出しと同じ道: `lilysharp/export`（`playbackOnly`）→ 録音があれば practice に録音を入れて
  `practisedJobs`・`singJobs`（音と声が録音の秒へ）→ パートごとに `renderPartSound`（プレビューの SoundFont）。
  `singJobs` が最初の子音のためにファイルを後ろへずらした分（`Lead`）は、各 Stem の `at` から引く（時間の軸の 0 は動かさない）。
  パートの WAV は音と SoundFont の鍵（`stemFileName`）で `globalStorage/stems` に置き、同じなら描き直さない。
- **実物の VS Code で確かめた**（Lab `sessions/p1339/stems-e2e`、`--extensionTestsPath` の `run.js`）:
  - 録音つき（小節 1.0／3.0／5.5／7.5／10.0 s）: `timeline: 'recording'`。ベースの音の出だしが 1.0／4.245／4.865／5.49／6.485／6.985／7.485 s
    （5 ms 刻みの検出。拍は 1.0・3.625 系列・5.5・6.0・6.5・7.0・7.5）。歌声（neutrino/AKANE、8.7 s で歌った）は `at: -0.30` で、音節が
    0.895（1.0 の「さ」の子音）／1.46／2.495／2.995／4.235／6.48／6.99 s ＝録音の小節に付いて行く。メロディの楽器は声に替わって外れる。
  - 録音なし（morning-light）: `timeline: 'score'`、3 パート。2 回目はパートを描き直さない（1.4 s → 26 ms）。
  - `onDidChangeStems` は開いた .lys の編集の 1.5 s 後に来る。`writeAudio` の 48 kHz の MP3 はクリックが 48000 標本目・長さも 144000 標本。
    22050 Hz の MP3 は理由つきで断る。
- 気づいたこと: 作業場の譜面の最初の版は `lyrics words { … }`（名前つき）を score の行で結び、`singer on melody` と書いていた。
  文法（GRAMMAR.md SingerSung、lyrics-singer-design.md §10・§14）では `sings` を省いた行はパートが名指しした歌詞
  （`part melody { lyrics words }`）か名前の無い歌詞を歌うので、これはエラー LYS4042「This file has no unnamed lyrics to sing.」
  （`lysc check` で確かめた）。エラーのある譜面でも再生の `Sung` は melody を歌うパートとして数え、`lilysharp/sung` は声を返さない。
  そのときメロディは楽器で鳴り、`notes` に理由が出る（stems の側の扱いはこれで正しい）。

## 作る順（了承の後）

1. `renderStems` の中身（パートを 1 本ずつ描く・歌声を集める・録音のパスを返す）と、その網。
2. `onDidChangeStems`。
3. exports への配線と、Shosa の REQUESTS への返事（形の説明、`stemsVersion`）。
4. 実物の VS Code で、Shosa 無しに exports を呼ぶ小さな拡張か CDP で確かめる。
