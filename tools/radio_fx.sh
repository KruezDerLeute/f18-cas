#!/bin/bash
# Radio-chatter effect for voice lines.
# Usage: tools/radio_fx.sh input.mp3 assets/sounds/output.ogg
set -e
IN="$1"; OUT="$2"
D=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$IN")
TOTAL=$(python3 -c "print($D + 0.24)")
END=$(python3 -c "print(int(($D + 0.12) * 1000))")
BP="highpass=f=300,highpass=f=300,lowpass=f=3000,lowpass=f=3000"
ffmpeg -hide_banner -loglevel error -y -i "$IN" -filter_complex "
[0:a]$BP,volume=2,asoftclip=type=atan,acompressor=threshold=0.1:ratio=6:attack=5:release=60:makeup=2,lowpass=f=3000,lowpass=f=3000,adelay=120[v];
anoisesrc=color=pink:amplitude=0.12:duration=$TOTAL:sample_rate=44100,$BP[n];
anoisesrc=color=white:amplitude=0.4:duration=0.12:sample_rate=44100,$BP,afade=t=out:st=0.02:d=0.1[s1];
anoisesrc=color=white:amplitude=0.4:duration=0.12:sample_rate=44100,$BP,afade=t=out:st=0.02:d=0.1,adelay=$END[s2];
[v][n][s1][s2]amix=inputs=4:normalize=0:duration=longest,alimiter=limit=0.708:level=false
" -ac 1 -c:a libvorbis -q:a 5 "$OUT"
echo "Saved $OUT"
