\version "2.26.0"
%
% Does a key change that OPENS a system cost that system's line start anything?
%
% Score A puts `\key ees \major` right after the break, so LilyPond engraves the
% courtesy cancellation + signature at the end of system 1 AND the new signature in
% system 2's prefix. Score B is the CONTROL: the same music with the same signature
% on system 2, declared up front, so nothing lands on the break.
%
% MEASURED on 2.26.0 (--png -dresolution=400, ink blobs along system 2's band,
% staff spaces from the staff-line origin):
%
%   A  0.79  3.03  4.33  6.17  9.57  15.02  20.47
%   B  0.79  3.03  4.33  6.17  9.57  15.02  20.47
%
% => IDENTICAL. The change costs the new line NOTHING: its signature is break-aligned
% in the prefix exactly like a reprinted one, and the cancellation belongs to the
% PREVIOUS line. Lily# charged it twice (the measure's spring-0 minimum still carried
% the change column) and reserved the OUTGOING key at the head (3 sharps 3.30 against
% the 3 flats 2.76 it drew) — 5.51 + 0.54 ss of line start nobody engraves.
%
% Observers: SpacingInvariantTests.KeyChangeOpeningASystem_ReservesTheSignatureItDraws
% and .AHoistedChange_DoesNotChargeBarOneForItsColumn.

\paper { indent = 0 }

% ---- A: the key change opens system 2 -------------------------------------
\score {
  \new Staff \relative c {
    \clef bass
    \key a \major
    a4 b cis d | a b cis d | a b cis d | a b cis d |
    \break
    \key ees \major
    aes4 bes c des | aes bes c des | aes bes c des | aes bes c des |
  }
  \header { piece = "A" }
}

% ---- B: the control -------------------------------------------------------
\score {
  \new Staff \relative c {
    \clef bass
    \key ees \major
    aes4 bes c des | aes bes c des | aes bes c des | aes bes c des |
    \break
    aes4 bes c des | aes bes c des | aes bes c des | aes bes c des |
  }
  \header { piece = "B" }
}
