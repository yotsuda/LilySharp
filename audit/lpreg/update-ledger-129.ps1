# Session 129 ledger update (audit/lp-regression/status.json).
function Set-RegStatus([string]$name, [string]$state, [string]$claim, [string]$notes) {
  $p = 'C:\MyProj\LilySharp\audit\lp-regression\status.json'
  $j = Get-Content $p -Raw | ConvertFrom-Json
  $e = $j.files.$name; if ($null -eq $e) { throw "no entry $name" }
  $e | Add-Member -Force NoteProperty state $state
  if ($claim) { $e | Add-Member -Force NoteProperty claim $claim }
  if ($notes) { $e | Add-Member -Force NoteProperty notes $notes }
  $j | ConvertTo-Json -Depth 4 | Set-Content $p -Encoding utf8
}

Set-RegStatus 'part-combine-tuplet-end.ly' 'fixed' `
  'End tuplet events are sent to the starting context, so even after a switch, a tuplet ends correctly' @'
fixed(第129第1便・第112 skip→綴りゲート解除で再測)。双子=scratch/lpreg/pctend.ly 対 pctend-probe.lys
＋対照 pctend-ctl.{ly,lys}(同じ音楽を合体器なしで=part1単独)。
★主張は engrave として成立(コード変更前から): LP=**三連符2つ・どちらも閉じる**。
TupletBracket は2つとも **x extent が空**(beamed=bracket-visibility if-no-beam)で番号だけ・
TUPNUM "3" が x 17.830355 / 25.342955・y +3.34、beam 2本 15.261155-20.399555 / 22.773755-27.912155、
**第2小節へ届く三連符の破片ゼロ**。ラベル Solo(14.087)/a2(31.403)＝切替は実際に起きている。
Lily# も同数・同座標(番号 17.83/25.34 @+3.34・beam 15.26-20.40/22.77-27.91)。
★★★ **本が測ったのは別の欠陥だった＝左頭精算の voice 境界**。修理前は
**休符→第1音以降の全列が一様に +0.30**(rest 8.59 は一致・列間隔も 2.50/4.79 は一致)。
在処: note-spacing.cc:77 `ideal = base - increment + left_head_end` は
**spacing-spanner.cc:350-393 musical_column_spacing が「左列の wish の right-items に右列が在る」ときだけ**走り、
その right-items を埋める `last_spacing_` は **Note_spacing_engraver のインスタンス変数**
(note-spacing-engraver.cc:75-78)で、この engraver は **Voice**(engraver-init.ly:419)。
⇒ 鎖は **Voice 文脈ごと**で、その文脈が黙っている間も持ち越される。
LP に wish の両端を吐かせて裏取り(pcdump.ily の WISH レコード・pctend.log):
**shared の休符列 8.585 の wish の right=(31.403, 29.313)** ＝ shared が「次に engrave する音」と小節線で、
**14.087 は入っていない** ⇒ この対だけ base のばね＝**LP 5.501955 対 対照 5.801955 の差 = 1.5-1.2**
(半休符の右端 headend=1.5 は LP 自身が両方の本で 1.5 と報告)。
★★ **非対称の正体は小節線**: 最後の三連音 26.608 の wish は **小節線列 29.313 を名指す**
(note-spacing-engraver.cc:115-120 が毎 timestep に currentCommandColumn を足す＝小節線は全声部のもの)
⇒ **solo→shared でも小節線をまたぐ切替は無料**で 26.608→g1 は両本とも 4.795186。
修理=`MusicItem.VoiceContext`(合体器が One/Two/Shared/Solo を焼く)＋
`SpacingRules.CrossesVoiceBoundary` を **cue 専用の述語から「両列の文脈集合が交わるか」へ**。
旧述語は IsCue しか知らず、doc 自身が「cue が唯一の逐次的 voice 変更」と書いていた＝**第126 combinedStaff で stale**。
照合: 全座標が LP 一致(休符 8.59・6音 14.09/16.59/19.10/21.60/24.10/26.61・g1 31.40)。
観測者 PartCombineTupletEndTests 5本(主張・境界・**合体器なし対照 5.802**・**小節線越えは不動**・
**同一文脈内は精算が走る 2.504**)。snapshot 0・テスト 4321 全緑。
perf round30(base a70a0fd8・両順 median-of-5＋床): plain1k/comb300/combmmr **3冊とも hash MATCH**・
符号は両順で反転＝実退行なし。comb300 の床のみ両順 +0.4/+0.6%＝合体器側(1 item 1 clone + mask)の実費。
⚠️ 開示: `InContext` は `WithVoiceDirection` が既に複製した item を**もう一度**複製する(record 別 arm 3綴りを
避けたため・上の +0.5% の中身)。⚠️ cue 側は `IsCue` からの**導出**のままで stamp していない
＝隣接 cue 領域2つは今も1文脈に見える(LP は CueVoice 2つ・コーパスに1冊も無い)。
'@

Set-RegStatus 'part-combine-silence.ly' 'open' $null @'
open(第127・skip→再測／第129 第1便で **X 残差は閉じた**・残るのは重なりの1点)。
3score=双子3対(scratch/lpreg/pcsil-{a,b,c}{.ly,-probe.lys}・LP対照pcsil-ctl.ly=同じ音楽の\voiceOne/\voiceTwo)。
**修理1枚(第127)=Nullに落とした要素が時間を食っていなかった**: LPのNullVoiceは他の文脈と同じ時計で走る
(音楽は在り、engraveだけ誰もしない)。移植は要素ごと捨てていたので、以降がその時価ぶん前に詰まっていた
(score1: 合体したr2の後のpart2 r4が3/4列でなく1/4列)。修理=PartCombinerのslotを(onset,item)で持ち、
Materialiseがonset順に並べて隙間をspacerで埋める(SlotEntry/Materialise/SpacerDurations新設)。
照合(LP grob dump対Lily# SVG・どちらも譜中心線からのss): score1=6列・休符8個・位置全一致
(+2/-2 / -2 / 0 / +2/-2 / +2 / +1。merge2つ=r2対r2とr1対r1、非merge2つ=r4対r8とr8対r4が陽性対照)。
score3=mmrest対restは**通常restが勝ちMMRは1つも出ない**・4要素とも位置一致・ラベルSolo/Solo II同順。
★★★ **第129で X 残差が閉じた**。第127 は「LS の列は LP の \voiceOne/\voiceTwo 対照と 0.005 以内で一致し、
LP 自身の \partCombine 出力だけが ±0.10〜0.20 ずれる(col3 +0.20・col4-6 -0.10)」と記録していたが、
**その全部が note-spacing.cc:77 の左頭精算を voice 境界で止めていなかったこと**だった
(導出と裏取りは part-combine-tuplet-end の notes)。stash 対照で実測: 修理前 LS = 8.59/10.79/**12.99**/
**18.09**/**20.29**/**24.57** 対 LP 8.585/10.785/**13.185**/**17.985**/**20.185**/**24.475** →
修理後は6列とも 0.005 以内。shared の wish が 13.185 から二声の列を飛び越して 24.475 を名指すのが在処。
観測者 PartCombineSilenceTests.TheColumnsSitWhereLilyPondPutsThem(LP6点)。
⚠️ **残差=score2 bar1のみ(open の理由)**: apart-silenceのr4(part1・voice one)が鳴っている最中に
part2のsoloが1/8で入る=LPは"one"と"solo"の2文脈で重なるが、**Lily#の1声部は重なる要素を持てない**ので後ろに付く
(LP列間隔 2.400/2.400 対 LS 4.600/2.750)。閉じるにはsolo/shared流が他スロットへ移れること=§2Aの向きの粒度。
釘=ASoloThatStartsInsideTheOtherPartsRestIsStillPlacedLate(直ったら落ちる)。
起票2(第127・両方処理済): ⑴condensedStaffが休符にvoiced位置を付けない=同セッション第2便で修理済
⑵計器の罠=**RestのY-offsetは位置ではない**(rest-collision.cc:46-66)。pcdump.ilyはsystem相対Y+STAFF記録に切替済。
'@

$f = (Get-Content 'C:\MyProj\LilySharp\audit\lp-regression\status.json' -Raw | ConvertFrom-Json).files
$p = @($f.PSObject.Properties | Where-Object { $_.Value.category -eq 'plain' })
"plain $(@($p).Count)"
$p | Group-Object { $_.Value.state } | Sort-Object Name | ForEach-Object { "{0,-8} {1}" -f $_.Name, $_.Count }
