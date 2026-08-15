# Session 130 ledger update (audit/lp-regression/status.json).
function Set-RegStatus([string]$name, [string]$state, [string]$claim, [string]$notes) {
  $p = 'C:\MyProj\LilySharp\audit\lp-regression\status.json'
  $j = Get-Content $p -Raw | ConvertFrom-Json
  $e = $j.files.$name; if ($null -eq $e) { throw "no entry $name" }
  $e | Add-Member -Force NoteProperty state $state
  if ($claim) { $e | Add-Member -Force NoteProperty claim $claim }
  if ($notes) { $e | Add-Member -Force NoteProperty notes $notes }
  $j | ConvertTo-Json -Depth 4 | Set-Content $p -Encoding utf8
}

Set-RegStatus 'part-combine-silence-mixed.ly' 'open' `
  'Different kinds of silence are not merged into the shared voice even if they begin and end simultaneously; however, when rests and skips are present in the same part, the skips are ignored' @'
open(第130第1便・第112 skip→綴りゲート解除で再測。**skip→open**＝主張の後半は移植して一致・前半に 2 件の一般欠陥が残る)。
双子=scratch/lpreg/pcsm.ly(staff1・逐語) 対 pcsm-probe.lys、対照=pcsm-order-probe.lys(枝順を揃えた版)。
LP 実測(pcsm.log・y は STAFF sy を引いた ss、**位置ではなく描画インクで比較**):
 小節1 R1^"R" 対 r1_"r" → 休符1つ dir=() +1.0(part2 の r1)・ラベルは **"r" だけ**("R" は event ごと Null へ)
 小節2 s1^"s" 対 R1_"R" → MMR 1つ **voice two・ink -2.625..-2.000**＋"R"、skip の "s" は出る
 小節3 r1^"r" 対 s1_"s" → 休符1つ dir=+1 **+2.0**(voice one)、"r"(上)と"s"(下)
 小節4 <<R1 s1 s4>> 対 <<s4 s1 R1>> → MMR 1つ **shared・ink +0.375..+1.000**
 小節5 <<r1 s2 s4>> 対 <<s4 s2 r1>> → 休符1つ **+1.0**(part2 の r1・src 14:74)
★★★ **移植＝silence-events(scm/part-combiner.scm:76-86)**。「同じパートに休符と skip が在れば skip は無視」は
**rest/mmrest で先に絞り、無いときだけ skip を見る**フィルタそのもの。Lily# は **span の第1枝しか読んでいなかった**ので
答えが**枝順に依存**していた——**対照 pcsm-order-probe.lys(両パートの枝順を揃えた版)は移植前から LP の答えを出し、
鏡の本(＝この本の綴り)だけが休符2つを出した**。LP は両者を区別できない。
修理＝`PartCombiner.ChooseSilenceWithinPart`(RenderSpec が Combine に渡す枝へ**選んだ休符を swap**、
負けた skip は元の枝へ)。⒝ 字面ではない: LP の Voice-state は 1 モーメントに **event のリスト**を持ち、
Lily# の VoiceState は **1 item**。字面にするには VoiceState を item リストにする＝routing まで届く。
⚠️ **同一パートの同一モーメントに休符が2つ**(LP は両方返し analyze-synced-silence が「各側1つ」を要求＝apart-silence)は
**表現できないので答えない**。コーパスに1冊も無い。
⇒ **小節3・4・5 は LP 一致**(4/5 がこの便で閉じた)。観測者 PartCombineSilenceMixedTests 4本
(主張・**枝順の対照**・**混ざれば merge しない陽性対照**・swap の非破壊性)。
⚠️ **open の理由＝一般欠陥 2 件**(どちらもこの本の外で単独に測ってある):
 ⑴ **MMR は譜に1つしか彫られず voice を取らない**。素の多声譜(合体器なし)で実測:
    `voice { R1 } { R1 }` は Lily# が **MMR 1つ +1.0**、LP は **2つ ink +1.375..+2.000 と -2.625..-2.000**
    (mmr-voice-probe.lys 対 mmr1.ly score2)。**次の小節 `voice { r1 } { r1 }` は +2.0/-2.0 で正しい**＝陽性対照。
    LP: MultiMeasureRest は direction-polyphonic-grobs(scm/music-functions.scm:617-634)で、
    lily/rest.cc:76 が direction を voiced-position 4 に変える。在処 MultiMeasureRestEngraver.Calculate
    (譜ごと・Y は staffHeight/2 固定)。⇒ この本の小節2 が +1.0 で出る。
 ⑵ **Null へ落とした event のラベルが残る**(小節1 の "R")。ラベルは (MeasureIndex,ItemIndex) で引く
    DynamicItem(DynamicItem.cs:37-40)で **combiner の item 流の外**に在り、Score.Dynamics の平配列。
    落ちも付いて行きもしない。
⚠️ ★ **計器の訂正**: MMR の縦位置は **y= で比べてはいけない**。1小節 MMR は
lily/multi-measure-rest.cc:254-264 が通常休符の位置から **2 を引き**、:284-292 が**吊るし字形**を選ぶので
**y= は 1.0 違って ink は同じ**(mmr1.log: 中立 R1 y=0.0 ink +0.375..+1.0 / r1 y=+1.0 同 ink)。
最初の読みは「Lily# の MMR が 1.0 高い」という**在りもしない欠陥**を名指していた。pcdump.ily に
**REST/MMREST の `yext=`(描画インク)** を追加済。
'@

$f = (Get-Content 'C:\MyProj\LilySharp\audit\lp-regression\status.json' -Raw | ConvertFrom-Json).files
$p = @($f.PSObject.Properties | Where-Object { $_.Value.category -eq 'plain' })
"plain $(@($p).Count)"
$p | Group-Object { $_.Value.state } | Sort-Object Name | ForEach-Object { "{0,-8} {1}" -f $_.Name, $_.Count }
