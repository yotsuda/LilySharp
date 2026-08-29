\version "2.24.0"
% ottava-consecutive.ly の比較用 twin。
% 主張: 同じラベルの連続 ottava は誤って結合されない。
\paper { indent = 0 ragged-right = ##t }
{
  \ottava 1
  c1
  \ottava -1
  c1
  \ottava 0
  c1
}
