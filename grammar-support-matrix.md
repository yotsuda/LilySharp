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
| VoltaBracket [1. ] | ✅ | ~対応済み |
| RepeatBlock |: :| | ✅ | |

### 2. Semantic Layer (Binder/StructureExpander)
| 機能 | バインド | 備考 |
|------|:------:|------|
| phrase参照解決 | ✅ | |
| section参照解決 | ✅ | |
| repeat展開 | ✅ | |
| volta展開 | ✅ | |
| SilentSectionRef | ⚠️ | 未確認 |
| MusicMark | ✅ | |
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
| MusicMark (@) | ✅ | @segno @coda @fine 等 |
| CustomText | ✅ | _"text" |
| Dynamics | ✅ | @p @f @ff @mf 等 |
| Articulations | ✅ | @staccato @accent @fermata 等 |
| Lyrics | ✅ | ハイフン・エクステンダー対応 |
| VoltaBracket | ✅ | |
| TupletBracket | ✅ | |
| GraceNote | ✅ | 65%スケール |
| MultiVoice | ✅ | |
| SystemBreak | ✅ | Knuth-Plass最適改行 |

## 次のアクション候補

| 優先度 | タスク | 工数 |
|:------:|--------|------|
| 1 | タブ譜SVGレンダリング | 4h |
| 2 | 連桁の手動制御 c8[ d e f] | 2h |
| 3 | MusicXML エクスポート (section/structure対応) | 8h |
| 4 | LilyPond → LilySharp 変換ツール | 16h+ |
