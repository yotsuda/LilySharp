# LilySharp 文法サポート状況マトリックス

## レイヤー別サポート状況

### 1. Lexer/Parser (Red-Green Tree)
| 機能 | パース | 備考 |
|------|:------:|------|
| title/composer | ✅ | |
| tempo/time/key | ✅ | |
| part { } | ✅ | |
| phrase { } | ✅ | |
| section { } | ✅ | |
| structure { } | ✅ | |
| render score/midi | ✅ | |
| staff/grandStaff | ✅ | |
| Note/Rest/Chord | ✅ | |
| Barline | ✅ | |
| Tie (~) | ✅ | |
| Slur ()/() | ✅ | |
| repeat/alternative | ✅ | |
| ParallelExpr << >> | ✅ | |
| tuplet | ✅ | |
| grace/acciaccatura | ✅ | |
| lyrics { } | ✅ | |
| clef/key (inline) | ✅ | |
| SectionRef | ✅ | |
| SilentSectionRef (~) | ✅ | structure直下のみ |
| MusicMark (@) | ✅ | |
| CustomText (_"") | ✅ | |
| VoltaBracket [1. ] | ⚠️ | ~がパースエラー |
| RepeatBlock |: :| | ✅ | |

### 2. Semantic Layer (Binder/StructureExpander)
| 機能 | バインド | 備考 |
|------|:------:|------|
| phrase参照解決 | ✅ | |
| section参照解決 | ✅ | |
| repeat展開 | ✅ | |
| volta展開 | ✅ | 基本動作 |
| SilentSectionRef | ⚠️ | 未確認 |
| MusicMark | ⚠️ | 未確認 |
| relative pitch | ✅ | |

### 3. Layout/Renderer
| 機能 | レンダリング | 備考 |
|------|:------:|------|
| Note/Rest/Chord | ✅ | |
| Beam | ✅ | |
| Tie/Slur | ✅ | |
| Clef | ✅ | 位置は LilyPond 準拠 |
| KeySignature | ✅ | |
| TimeSignature | ✅ | |
| Barline | ✅ | |
| SectionLabel | ✅ | |
| Tempo | ✅ | |
| MultiStaff | ✅ | |
| GrandStaff | ✅ | |
| MusicMark (@) | ⚠️ | 未実装? |
| CustomText | ⚠️ | 未実装? |
| Dynamics | ❌ | 未実装 |
| Articulations | ❌ | 未実装 |
| Lyrics | ❌ | 未実装 |

## 発見した問題

1. **VoltaBracket内の~がパースエラー**
   - `[2. ~Verse]` → "Expected 'Identifier', found 'Tilde'"
   - 文法定義では許可されているがパーサーが未対応

## 次のアクション候補

| 優先度 | タスク | 工数 |
|:------:|--------|------|
| 1 | VoltaBracket内~対応 (Parser) | 1h |
| 2 | MusicMark描画実装 | 2h |
| 3 | CustomText描画実装 | 1h |
| 4 | Dynamics実装 | 4h |
| 5 | Articulations実装 | 4h |
