# LilySharp (.lys) 文法設計

## 設計方針

1. **LilyPond 100%互換は目指さない** - それは LilyPond 本体の役割
2. **「LilyPond 経験者が迷わない」レベルの類似性**を維持
3. **明確に改善できる部分は思い切って変更**
4. **変換ツールで既存資産をサポート**

---

## 文法の全体像

### 記号体系の統一

| カテゴリ | プレフィックス | 用途 | 例 |
|----------|----------------|------|-----|
| アノテーション | `@` | 音符への付加情報 | `@staccato`, `@p`, `@trill` |
| 参照 | `$` | 変数・フレーズの展開 | `$intro`, `$theme` |

**決定事項**:
- バックスラッシュ (`\`) プレフィックスは**全廃止**
- アノテーションは `@` に統一
- 変数参照は `$` に統一

### アノテーション (@)

音符に付加する情報すべてに `@` を使用:

```
// ダイナミクス
c4@ppp c4@pp c4@p c4@mp c4@mf c4@f c4@ff c4@fff
c4@cresc c4@decresc c4@dim

// アーティキュレーション
c4@staccato c4@accent c4@tenuto c4@marcato c4@fermata

// 装飾音記号
c4@trill c4@mordent c4@turn c4@prall

// 楽曲記号
@segno @coda @fine @ds.al.fine @dc.al.coda
```

### 参照 ($)

変数・フレーズを展開する場所で `$` を使用:

```
phrase intro { c4 d e f | }
phrase theme { g4 a b c | }

section Verse {
  melody { $intro $theme }    // フレーズを展開
  bass { $bassPattern }       // 変数を展開
}

structure {
  $optionalIntro              // 構造内でも使用可能
  Verse
}
```

---

## オクターブ管理

### 方針: 相対モード + パート基準 + セクションリセット

1. **相対モードをデフォルト** - 絶対モードは使用頻度が低い
2. **パート定義で初期オクターブを設定** - 楽器から自動推定可能
3. **セクション開始時に初期オクターブにリセット** - 予期しないジャンプを防止

### 楽器ベースの既定値

```
// 楽器指定 → clef と octave が自動設定
part melody { instrument: violin }    // → clef: treble, octave: 4
part bass { instrument: cello }       // → clef: bass, octave: 3
part lead { instrument: guitar }      // → clef: treble, octave: 3

// 明示的に上書き可能
part special { instrument: violin, octave: 5 }

// 楽器なしの場合は clef から推定
part melody { clef: treble }          // → octave: 4
part bass { clef: bass }              // → octave: 3
```

### 楽器→既定値マッピング

| instrument | clef | octave | 備考 |
|------------|------|--------|------|
| violin | treble | 4 | |
| viola | alto | 3 | |
| cello | bass | 3 | |
| bass / contrabass | bass | 2 | コントラバス |
| piano-right | treble | 4 | |
| piano-left | bass | 3 | |
| guitar | treble | 3 | 記譜は実音1オクターブ上 |
| flute | treble | 5 | |
| oboe | treble | 4 | |
| clarinet | treble | 4 | |
| bassoon | bass | 3 | |
| trumpet | treble | 4 | |
| horn | treble | 4 | |
| trombone | bass | 3 | |
| tuba | bass | 2 | |
| voice-soprano | treble | 4 | |
| voice-alto | treble | 4 | |
| voice-tenor | treble | 3 | オクターブ記号付き |
| voice-bass | bass | 3 | |

### clef→既定オクターブ（楽器未指定時）

| clef | 既定 octave | 基準音 |
|------|-------------|--------|
| treble | 4 | c' (middle C) |
| bass | 3 | c (middle C - 1 octave) |
| alto | 3 | c (middle C) |
| tenor | 3 | c |

### 相対オクターブ計算

```
// セクション開始: octave=4 (treble clef) にリセット
section Verse {
  melody {
    c4 d e f |     // c4 → d4 → e4 → f4
    g4 a b c |     // g4 → a4 → b4 → c5 (5度超えで自動調整)
    c4 d e f |     // c5 → d5 → e5 → f5 (前の c5 から継続)
  }
}

// 次のセクション開始: octave=4 に自動リセット
section Chorus {
  melody {
    c4 d e f |     // c4 から再スタート（リセット済み）
  }
}
```

### 明示的オクターブ指定（従来通り）

```
c'4    // 1オクターブ上
c''4   // 2オクターブ上
c,4    // 1オクターブ下
c,,4   // 2オクターブ下
```

---

## 文法要素一覧

### 音符・休符

```
c4          // 四分音符
c4.         // 付点四分音符
c4..        // 複付点四分音符
cis4        // C# (is = sharp)
ces4        // Cb (es = flat)
cisis4      // C## (isis = double sharp)
ceses4      // Cbb (eses = double flat)
r4          // 四分休符
r2.         // 付点二分休符
s4          // スペーサー（無音だが時間を占める）
```

### 和音

```
<c e g>4    // Cメジャー和音
<c e g>4.   // 付点和音
<c' e g>4   // オクターブ指定付き
```

### 連符

```
tuplet 3/2 { c4 d e }      // 3連符（2拍に3音）
tuplet 5/4 { c8 d e f g }  // 5連符（4拍に5音）
```

### タイ・スラー

```
c4~ c4      // タイ（同じ音を繋ぐ）
c4( d e f)  // スラー（レガート）
```

### アーティキュレーション

```
c4@staccato     // スタッカート
c4@accent       // アクセント
c4@tenuto       // テヌート
c4@marcato      // マルカート
c4@fermata      // フェルマータ
c4@portato      // ポルタート
```

### 装飾音記号

```
c4@trill        // トリル
c4@mordent      // モルデント
c4@prall        // プラルトリラー（逆モルデント）
c4@turn         // ターン
c4@invertedturn // 逆ターン
```

### ダイナミクス

```
c4@ppp    // pianississimo
c4@pp     // pianissimo
c4@p      // piano
c4@mp     // mezzo-piano
c4@mf     // mezzo-forte
c4@f      // forte
c4@ff     // fortissimo
c4@fff    // fortississimo
c4@cresc  // crescendo
c4@decresc // decrescendo
c4@dim    // diminuendo
```

### 装飾音符

```
grace { d16 e } f4       // 装飾音符
acciaccatura { a16 } b4  // 短前打音（斜線付き）
appoggiatura { a8 } b4   // 長前打音
```

### 小節線

```
|       // 通常の小節線（バーチェック）
||      // 複縦線
|.      // 終止線
|:      // リピート開始
:|      // リピート終了
```

### 改行

```
break   // 段の強制改行
```

---

## 構造定義

### メタデータ

```
title "Symphony No. 5"
composer "Beethoven"
tempo 120
time 4/4
key c major
key d minor
```

### パート定義

```
// 楽器指定（推奨）
part melody { instrument: violin }
part bass { instrument: cello }
part lead { instrument: guitar }

// 手動指定
part custom { clef: treble, octave: 5 }

// 属性一覧
part name {
  instrument: <instrument-name>  // 楽器（clef/octave 自動設定）
  clef: treble | bass | alto | tenor
  octave: <number>               // 初期オクターブ
}
```

### フレーズ定義

```
phrase intro {
  c4 d e f | g2 r2 |
}

phrase theme {
  c4@p d e f@cresc | g2@f r2 |
}
```

### セクション定義

```
section Intro {
  melody { $intro }          // フレーズ参照
  bass { c2 g2 | c1 | }      // インライン記述
}

section Verse {
  key d major                // セクション内で調号変更
  melody { $theme }
  bass { d2 a2 | d1 | }
}
```

### 構造定義

```
structure {
  Intro                      // セクション参照
  |: Verse :|                // リピート
  |: Verse [1. Bridge] [2. Coda] :|  // ボルタ括弧付き
  Outro
}
```

### レンダリング定義

```
// 楽譜出力
render score "output.svg" {
  grandStaff {
    staff { melody }
    staff { bass }
  }
}

// MIDI出力
render midi "output.mid" {
  melody channel: 1 instrument: 1
  bass channel: 2 instrument: 33
}
```

---

## 複声部

```
// 2声部
<< { c'2 d' } \\ { e2 f } >>

// 3声部
<< { g'2 } \\ { b2 } \\ { d2 } >>
```

---

## LilyPond との対応表

| LilyPond | LilySharp | 備考 |
|----------|-----------|------|
| `\header { title = "..." }` | `title "..."` | シンプル化 |
| `\time 4/4` | `time 4/4` | バックスラッシュ不要 |
| `\key g \major` | `key g major` | 自然な英語 |
| `\clef treble` | `clef: treble` (in part) | パート属性として |
| `\relative c' { }` | 暗黙（パート+セクションで管理） | 明示的宣言不要 |
| `\repeat volta 2 { }` | `\|: ... :\|` | 視覚的 |
| `\alternative { { } { } }` | `[1. A] [2. B]` | 簡潔 |
| `name = { ... }` | `phrase name { ... }` | 意図が明確 |
| `\name` (変数参照) | `$name` | プレフィックスで明確 |
| `c-^` | `c@accent` | 名前で意味明確 |
| `c-.` | `c@staccato` | 名前で意味明確 |
| `\p`, `\f` | `@p`, `@f` | @ に統一 |
| `\trill` | `@trill` | @ に統一 |
| `% comment` | `// comment` | プログラマ向け |
| `%{ block }%` | `/* block */` | プログラマ向け |

---

## 記号体系まとめ

```
// @ = アノテーション（音符への付加情報）
c4@staccato@p@trill

// $ = 参照（変数・フレーズの展開）
melody { $intro $theme }

// ~ = タイ
c4~ c4

// ( ) = スラー
c4( d e f)

// < > = 和音
<c e g>4

// << >> = 複声部
<< { voice1 } \\ { voice2 } >>

// { } = ブロック
phrase name { ... }
section Name { ... }

// | = 小節線（バーチェック）
c4 d e f |

// |: :| = リピート
|: c4 d e f :|

// [ ] = ボルタ括弧
[1. A] [2. B]
```

---

## 今後の実装タスク

### 優先度: 高
- [ ] ダイナミクス表記を `\p` → `@p` に変更
- [ ] 変数参照を `name` → `$name` に変更
- [ ] セクション開始時のオクターブリセット実装
- [ ] パート定義に `instrument` 属性追加

### 優先度: 中
- [ ] 楽器→clef/octave マッピングテーブル実装
- [ ] バーチェック警告の改善

### 優先度: 低
- [ ] LilyPond → LilySharp 変換ツール
- [ ] 連桁の手動制御 `c8[ d e f]`
