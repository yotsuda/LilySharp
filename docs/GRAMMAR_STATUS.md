# LilySharp Grammar Features

## 現状の確認

### 1. ヘッダー/メタデータ
| 機能 | 構文 | 状況 |
|------|------|------|
| タイトル | rtitle "..."r | ✅ |
| 作曲者 | rcomposer "..."r | ✅ |
| テンポ | rtempo 120r | ✅ |
| 拍子 | rtime 4/4r | ✅ |
| 調号 | rkey c majorr | ✅ |

### 2. 構造宣言
| 機能 | 構文 | 状況 | 備考 |
|------|------|------|------|
| パート定義 | rpart name { clef: treble }r | ✅ | instrument/octave 属性対応 |
| フレーズ定義 | rphrase name { notes }r | ✅ | |
| セクション | rsection Name { part { } }r | ✅ | |
| 構造 | rstructure { Section }r | ✅ | |

### 3. レンダリング指定
| 機能 | 構文 | 状況 | 備考 |
|------|------|------|------|
| スコア | rrender score "file.svg" { }r | ✅ | |
| 譜表 | rstaff { part }r | ✅ | |
| 大譜表 | rgrandStaff { staff staff }r | ✅ | |
| タブ譜 | rtab standard { part }r | ✅ | Guitar/Bass/Bass5/Ukulele対応 |
| MIDI | CLI: rlysc midi file.lys file.midr | ✅ | rrender midir ブロックは存在しない |

### 4. 音楽要素
| 機能 | 構文 | 状況 | 備考 |
|------|------|------|------|
| 音符 | rc4 d8 e16r | ✅ | |
| 休符 | rr4 r8r | ✅ | |
| 和音 | r<c e g>4r | ✅ | |
| オクターブ | rc' c''r / rc, c,,r | ✅ | |
| 臨時記号 | rcis dis eesr | ✅ | |
| 付点 | rc4. c4..r | ✅ | |
| タイ | rc4~ c4r | ✅ | |
| スラー | rc4( d e)r | ✅ | |
| 連符 | rtuplet 3/2 { c8 d e }r | ✅ | |
| 装飾音 | rgrace { c16 d }r | ✅ | |
| 連桁 | 自動 / rc8[ d e f]r | ✅ | 手動制御対応 |

### 5. アーティキュレーション/ダイナミクス
| 機能 | 構文 | 状況 |
|------|------|------|
| スタッカート | rc4@staccator | ✅ |
| アクセント | rc4@accentr | ✅ |
| フェルマータ | rc4@fermatar | ✅ |
| テヌート | rc4@tenutor | ✅ |
| マルカート | rc4@marcator | ✅ |
| フォルテ | rc4@fr | ✅ |
| ピアノ | rc4@pr | ✅ |
| その他ダイナミクス | r@pp @ff @mf @mpr 等 | ✅ |

### 6. リピート/ナビゲーション
| 機能 | 構文 | 状況 |
|------|------|------|
| リピート | r\|: ... :\|r | ✅ |
| 1st/2nd ending | r[1. A] [2. B]r | ✅ |
| D.S. / D.C. | r@segno @coda @finer 等 | ✅ |

### 7. その他
| 機能 | 構文 | 状況 |
|------|------|------|
| 歌詞 | rlyrics { Hap -- py }r | ✅ |
| 複声部 | r<< { } \\ { } >>r | ✅ |
| 段改行 | rbreakr | ✅ |
| 楽曲記号 | r@segno @coda @finer | ✅ |
| カスタムテキスト | r_"text"r | ✅ |
| 変数参照 | r$namer | ✅ |

---
