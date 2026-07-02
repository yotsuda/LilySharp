# Lily# 文法監査 (2026-07-02, リリース前)

評価軸（優先順）:
1. **「妥当な楽譜が書けない」ユーザー体験を作らない**（表現力の穴・サイレント消失が最重罪）
2. リリース後に変更コストが跳ね上がる非互換（今なら無償で直せるもの）
3. 仕様書と実装の乖離（「書けるのに書けないと誤認させる」もの含む）

検証方法: GRAMMAR.md / GRAMMAR_FOR_LLM.md / GRAMMAR_ANALYSIS.md / GRAMMAR_STATUS.md の全読
＋ Lexer.cs / Parser.cs / ArticulationRegistry / AnnotationNameValidator / MusicMarkItem /
RenderSpecParser の受理面の突合。各所見に実装箇所を付す。

---

## A. 表現力の穴（最優先）

### A-1. アクセント系ダイナミクスが書けない: `@sfz @sf @fp @rfz @fz` 【高】
`IsDynamicName` (Parser.cs) は p〜fff + cresc/decresc/dim のみ。`c4@sfz` は
「Unknown annotation — ignored」警告で**消える**。sfz/fp はピアノ・オケ譜で頻出。
→ 対処: IsDynamicName のテキスト集合と DynamicText 描画（既存経路）+ MIDI velocity 表に追加。
   小規模・非互換リスクなし。

### A-2. 音符アンカーの自由テキストが存在しない: `@text("dolce")` 【高】
"dolce" "espress." "pizz." "arco" "div." "solo" "cantabile" "sub." などの発想標語・
奏法指示テキストを付ける手段が**皆無**（固定の @rit/@accel のみ）。LP の `c^"text"` 相当。
実譜の大半で遭遇する欠落であり、原則①への最大の違反。
→ **@text() は追加すべき（判断: YES）**。設計は §D。

### A-3. structure の `_ "text"` がサイレント消失（実バグ）【高】
`ParseCustomText` (Parser.cs:2060) は生きており `structure { A _"..." B }` は正常に
パースされるが、**CustomTextSyntax を消費するコレクタが存在しない**（new CustomTextItem
はリポジトリ史上ゼロ件）。engraver→renderer→F3 移行→テストの下流配管は完備。
つまり「書ける構文が黙って何も描かない」— 原則①の最悪形。
→ 対処: コレクタで CustomTextGreen → CustomTextItem（セクション境界の小節に帰属）を生成し
   既存配管に接続する。既存レイアウト（小節末尾・譜表下・イタリック）は structure 指示文
   の意味に合致している（音符テキスト用ではなくこの用途のための設計だったとみられる）。

### A-4. 弦楽器系の頻出記号が registry にない 【中】
ArticulationRegistry には staccatissimo / upbow / downbow / harmonic(flageolet) /
snappizzicato が無い。upbow/downbow は弦楽譜でほぼ必須、staccatissimo も常用。
→ 対処: registry + Emmentaler グリフ + validator 候補への追加（各1行級）。

### A-5. 「実装済みなのに書けないと誤認させる」ドキュメント欠落 【中】
- `tempo "Allegro" 4 = 120`（テキスト・音価=BPM・swing/shuffle）— 実装済み。
  GRAMMAR.md は `tempo Integer [swing]` としか書いていない。
- `@quindicesima/@15ma/@15mb`（15ma オッターヴァ）— 実装済み・両スペック未記載。
- half-tie `@laissezVibrer` / `@repeatTie`、`@cue` `@cross` `@dead`、
  feathered beam `@feather(right|left|accel|rit)`、`@breath` `@caesura`、
  `@fall` `@doit`(+`bendafter`)、`repeat percent/unfold/tremolo`、
  part property `name`（表示名）、略記名（@stac @acc @ten @marc @ferm @tr）
  — いずれも実装済み・GRAMMAR.md の EBNF に不在（LLM 版にも大半が不在）。

---

## B. 仕様の矛盾・二重化（リリース前に確定すべき非互換）

### B-1. octave absolute|relative: 仕様書は「存在しない」、パーサは受理 【要決定】
GRAMMAR.md §Pitch: "There is NO absolute-octave mode and no 'relative'/'absolute'
keyword"。一方パーサは part property `octave (absolute|relative)` と mid-music
`octave absolute` 指令（ParseOctaveDirective）の両方を受理する。
→ どちらかに倒す。残すなら文書化、殺すなら削除+診断。リリース後の削除は非互換。

### B-2. 同義キーワードの二重化 【要決定】
- `chordnames` / `chords`（別 SyntaxKind で並存）
- `tab` / `tabStaff`（さらに "tabstaff" 小文字も受理）
- `grandStaff` / `grandstaff`
→ 正式形を1つ宣言し、他は「受理するが docs には出さない alias」か削除かを決めて明文化。
  （大文字小文字の方針も: キーワードは case-sensitive だが grandstaff/tabstaff だけ
  両対応、@注釈名は case-insensitive — 現状規則がバラバラ。）

### B-3. score アイテムの形の不統一 【低〜中】
`staff NAME` / `tab NAME` / `chords NAME` は bare、`ossia { NAME }` だけ braces 必須
（tab は「braces があってもスキップ」の寛容実装）。→ `ossia NAME` を許して統一を推奨。

### B-4. 予約語まわりの記述矛盾 【ドキュメント】
- `p pp ppp mp mf ff fff` は lexer で真に予約（識別子不可）。`f` と a〜g はピッチ優先で
  予約表現とは別物。GRAMMAR.md の Keywords 一覧と「dynamics are NOT reserved」注記が
  読者に矛盾して見える（NOT reserved は cresc/dim/staccato 等の @名の話）。書き分けを明確に。
- `swing` は GRAMMAR.md の Keywords にあるが GRAMMAR_FOR_LLM.md の Reserved words に無い
  （実装は非予約の value token — GRAMMAR.md 側が誤り）。
- `let`（変数宣言）と `use`（参照）は実装済みキーワードだが、両スペックとも本文に構文説明が
  無い（Reserved words 表に名前だけ）。公開するなら文書化、隠すなら診断つき無効化。

### B-5. ペダル系引数の意味論ゆれ 【低】
`@ped(off)` は「状態」を引数に、`@una(corda)` `@tre(corde)` `@sost(ped)` は「名詞の続き」を
引数に置く。同じ `名前(引数)` 形で意味カテゴリが違う。今なら `@unaCorda` 等の単一名 alias を
足して docs は単一名を正にできる（既存形は受理継続）。

---

## C. 名前空間・将来拡張の評価（良い点）

- **@名前空間のガードは堅牢**: 未知の @name は AnnotationNameValidator が警告+Levenshtein
  "Did you mean" を出す。typo がサイレントに消えない（A-3 の structure 側だけが例外）。
- **@ 注釈名は非予約**（tr/acc/ten/dim が識別子として自由）— 将来 @名 を増やしても
  ユーザー識別子と衝突しない。**@text 追加の障壁もゼロ**。
- include/let/use が予約済み = マルチファイル・変数の将来拡張余地を確保済み。
- 値付き注釈は `名前(引数…)` に統一済み（'.' は .up/.down 専用）— @text("…") が自然に収まる。

---

## D. @text() 設計案（追加すべき: YES）

構文: `c4@text("dolce")` / `c4@text("pizz.").up` — 既存の値付き注釈と完全に同形。
- 引数は StringLiteral 1個（空白含む自由文）。`.up/.down` 対応、既定は譜表下（LP の
  TextScript 既定は down）。イタリック serif（LP TextScript 既定 upright だが Lily# の
  発想標語用途はイタリックが自然 — 要好み確認）。
- 実装: ArticulationRegistry ではなく MusicMarkGreen(args) 経路で受け、専用の
  TextAnnotationItem（StaffIndex 付き）→ ArticulationEngraver 同型の engraver
  （skyline 参加・per-staff ルーティング・OssiaShrink 1引数で ossia 縮尺対応）。
  ※ 死配管だった CustomText 系は A-3（structure 指示文）専用として接続し、@text とは
  分離する（アンカーも配置規則も異なる）。
- AnnotationNameValidator: IsKnownCompoundName に "text." prefix を追加。
- LSP: 補完候補 @text + snippet `@text("$0")`。

---

## E. 推奨着手順

1. A-3 接続（サイレント消失の解消 — バグ修正）
2. A-1 sfz 族 + A-4 registry 追加（小粒・即効・非互換なし）
3. D の @text() 実装（表現力の最大の穴）
4. B-1 octave の意思決定 → 実装/削除
5. B-2/B-3 の正式形決定（非互換変更は今だけ無償）
6. A-5 + B-4 のドキュメント同期一括（3スペック相互 + パーサ）

判断保留として明記: B-5（alias 追加のみなら任意のタイミングで可能）。
