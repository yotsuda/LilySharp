\version "2.26.0"

% tablature-new-line-spacer.ly 逐語 + indent 0 + \bar "|."。
% 明示 break 本なので ragged-right は書かない(README 規約の逆・Lily# は全行 justify)。

\new TabStaff { b1 \break s2 b \bar "|." }

\layout { indent = 0\mm }
