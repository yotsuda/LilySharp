# LilySharp Grammar Features

## 現状の確認

### 1. ヘッダー/メタデータ
| 機能 | 構文 | 状況 |
|------|------|------|
| タイトル | `title "..."` | ✅ |
| 作曲者 | `composer "..."` | ✅ |
| テンポ | `tempo 120` | ✅ |
| 拍子 | `time 4/4` | ✅ |
| 調号 | `key c major` | ✅ |

### 2. 構造宣言
| 機能 | 構文 | 状況 | 備考 |
|------|------|------|------|
| パート定義 | `part name { clef: treble }` | ❓ | 要確認 |
| フレーズ定義 | `phrase name { notes }` | ❓ | 要確認 |
| セクション | `section Name { part { } }` | ❓ | 要確認 |
| 構造 | `structure { Section }` | ❓ | 要確認 |

### 3. レンダリング指定
| 機能 | 構文 | 状況 | 備考 |
|------|------|------|------|
| スコア | `render score "file.svg" { }` | ❓ | 要確認 |
| 譜表 | `staff { part }` | ❓ | 要確認 |
| 大譜表 | `grandStaff { staff staff }` | ❓ | fur-elise でエラー |
| タブ譜 | `tab { }` | ❓ | 要確認 |
| MIDI | `render midi "file.mid" { }` | ❓ | 要確認 |

### 4. 音楽要素
| 機能 | 構文 | 状況 | 備考 |
|------|------|------|------|
| 音符 | `c4 d8 e16` | ✅ | |
| 休符 | `r4 r8` | ✅ | |
| 和音 | `<c e g>4` | ✅ | |
| オクターブ | `c' c''` / `c, c,,` | ✅ | |
| 臨時記号 | `cis dis ees` | ✅ | |
| 付点 | `c4. c4..` | ✅ | |
| タイ | `c4~ c4` | ❓ | fur-elise でエラー？ |
| スラー | `c4( d e)` | ❓ | 要確認 |
| 連符 | `tuplet 3/2 { c8 d e }` | ❓ | 要確認 |
| 装飾音 | `grace { c16 d }` | ❓ | 要確認 |
| 連桁 | 自動 | ✅ | |

### 5. アーティキュレーション/ダイナミクス
| 機能 | 構文 | 状況 |
|------|------|------|
| スタッカート | `c4-staccato` | ❓ |
| アクセント | `c4-accent` | ❓ |
| フォルテ | `\f` | ❓ |
| ピアノ | `\p` | ❓ |

### 6. リピート/ナビゲーション
| 機能 | 構文 | 状況 |
|------|------|------|
| リピート | `|: ... :|` | ❓ |
| 1st/2nd ending | structure内で定義 | ❓ |
| D.C. / D.S. | `dc`, `ds al fine` | ❓ |

---

## 優先度

### P0: 基本機能（動作確認必須）
- [ ] 単一譜表の基本音符
- [ ] phrase 定義と参照
- [ ] section/structure の解釈

### P1: 中核機能
- [ ] grandStaff（ピアノ譜）
- [ ] タイ/スラー
- [ ] 連符

### P2: 拡張機能
- [ ] リピート
- [ ] アーティキュレーション
- [ ] ダイナミクス
- [ ] タブ譜

---

## 既知のバグ

### fur-elise.lys
```
Error: Measure 0 items mismatch: measure has 1 items, layout has 3 items.
Voice=1, Measure items: [RestItem]
```
原因: 小節の解釈とレイアウトが不一致
