<#
.SYNOPSIS
    Makes an MP4 of a .lys score: the engraved pages, the sounding notes lit, and the sound.

.DESCRIPTION
    Writes the score's MIDI with lysc, plays it through the preview's synthesizer and sound bank,
    draws one picture per playback state (ScoreVideo, which runs LilySharp.Core.Export.ScoreVideo,
    the code behind the editor's Export as MP4 Video), and joins pictures and sound with ffmpeg.
    The first page shows until the music reaches a note printed on the next one; then the whole
    picture turns to that page. See README.md.

.EXAMPLE
    ./tools/lys-to-mp4/New-ScoreVideo.ps1 ~/Downloads/Haru-no-Kaze.lys

.EXAMPLE
    ./tools/lys-to-mp4/New-ScoreVideo.ps1 song.lys -Width 1920 -Height 1080 `
        -Set 'paperWidth=320mm','paperHeight=180mm' -Color 0066E6 -Force
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)] [string] $Path,
    # The MP4 to write. Default: beside the .lys, with its name.
    [string] $Out,
    # Picture size. Default: A4 portrait. The page keeps its shape; what is left is white.
    [int] $Width = 1240,
    [int] $Height = 1754,
    [int] $Fps = 30,
    # Seconds of still score before the music starts.
    [double] $Lead = 1.5,
    # Colour of a sounding notehead, RRGGBB.
    [string] $Color = 'F57800',
    # Paper / layout settings for this run (lysc --set), e.g. 'paperWidth=320mm'.
    [string[]] $Set = @(),
    # lysc png --scale: the resolution the pages are engraved at before they are fitted.
    [double] $Scale = 2.0,
    # A sound bank (.sf2 or .sf3). Default: the one the VS Code extension ships.
    [string] $SoundFont,
    # Where the intermediate files go. Default: a new folder under the temp directory.
    [string] $WorkDir,
    # Overwrite -Out if it exists.
    [switch] $Force
)
$ErrorActionPreference = 'Stop'
$inv = [cultureinfo]::InvariantCulture
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..')).Path

$Path = (Resolve-Path $Path).Path
$name = [IO.Path]::GetFileNameWithoutExtension($Path)
if (-not $Out) { $Out = Join-Path (Split-Path $Path) "$name.mp4" }
$Out = [IO.Path]::GetFullPath($Out)
if ((Test-Path $Out) -and -not $Force) { throw "$Out exists. Pass -Force to overwrite it." }
if (-not $SoundFont) { $SoundFont = Join-Path $repo 'editors\vscode\media\soundfont\MuseScore_General.sf3' }
if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) "lys-to-mp4-$name-$(Get-Date -Format yyyyMMddHHmmss)" }
New-Item -ItemType Directory -Force $WorkDir | Out-Null

foreach ($tool in 'dotnet', 'node', 'ffmpeg', 'ffprobe') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not on PATH." }
}
if (-not (Test-Path (Join-Path $repo 'editors\vscode\node_modules\spessasynth_core'))) {
    throw "spessasynth_core is missing: run 'npm install' in editors/vscode."
}

function Invoke-Native([string] $what, [scriptblock] $run) {
    $output = & $run 2>&1
    if ($LASTEXITCODE -ne 0) { $output | Write-Host; throw "$what failed (exit $LASTEXITCODE)." }
    $output
}

Write-Host '1/5 Building lysc and ScoreVideo'
Invoke-Native 'dotnet build lysc' { dotnet build (Join-Path $repo 'LilySharp.Cli') -c Release -v q -nologo } | Out-Null
Invoke-Native 'dotnet build ScoreVideo' { dotnet build (Join-Path $here 'ScoreVideo') -c Release -v q -nologo } | Out-Null
$lysc = Get-ChildItem (Join-Path $repo 'LilySharp.Cli\bin\Release') -Recurse -Include 'lysc.exe', 'lysc' -File |
    Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
$scoreVideo = Get-ChildItem (Join-Path $here 'ScoreVideo\bin\Release') -Recurse -Include 'ScoreVideo.exe', 'ScoreVideo' -File |
    Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName

Write-Host '2/5 Writing the MIDI'
$setArgs = @($Set | ForEach-Object { '--set', $_ })
Invoke-Native 'lysc midi' { & $lysc midi -d $WorkDir $Path } | Out-Null
$mid = Join-Path $WorkDir "$name.mid"
$wav = Join-Path $WorkDir 'audio.wav'

Write-Host '3/5 Rendering the sound'
Invoke-Native 'render-audio' { node (Join-Path $here 'render-audio.mjs') $SoundFont $mid $wav } | Write-Host

# The music's end: where the last silence begins, plus a second to ring out.
$dur = [double]::Parse((ffprobe -v error -show_entries format=duration -of csv=p=0 $wav), $inv)
$starts = ffmpeg -hide_banner -i $wav -af 'silencedetect=n=-50dB:d=0.5' -f null - 2>&1 |
    Select-String 'silence_start: ([\d.]+)' | ForEach-Object { [double]::Parse($_.Matches[0].Groups[1].Value, $inv) }
$end = $dur
if ($starts) { $last = @($starts)[-1]; if ($last -gt $dur / 2) { $end = [Math]::Min($dur, $last + 1.0) } }
$fadeStart = [Math]::Max(0, $end - 1.5)

Write-Host '4/5 Drawing the pictures'
Invoke-Native 'ScoreVideo' {
    & $scoreVideo $Path $WorkDir --width $Width --height $Height --fps $Fps `
        --lead $Lead.ToString($inv) --length $end.ToString($inv) --color $Color --scale $Scale.ToString($inv) @setArgs
} | Write-Host

Write-Host '5/5 Encoding'
$af = "[1:a]atrim=0:{0},afade=t=out:st={1}:d=1.5,adelay={2}:all=1,loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000[a]" -f `
    $end.ToString($inv), $fadeStart.ToString($inv), [int]($Lead * 1000)
$total = ($Lead + $end).ToString($inv)
Invoke-Native 'ffmpeg' {
    ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i (Join-Path $WorkDir 'frames\frames.txt') -i $wav `
        -filter_complex $af -map 0:v -map '[a]' -r $Fps -t $total `
        -c:v libx264 -pix_fmt yuv420p -crf 20 -tune stillimage -c:a aac -b:a 192k -movflags +faststart $Out
} | Out-Null

$size = (Get-Item $Out).Length / 1MB
Write-Host ("Done: {0} ({1:0.0} MB, {2} s). Work files: {3}" -f $Out, $size, $total, $WorkDir)
