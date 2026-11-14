#!/bin/sh
# Copies the kit (and the self-test) into BuildProject (a copy of the SDK's Unity project kept for builds and tests; the
# TestProject is the user's own playground and is never touched), then runs the self-test in Unity (batch mode, with
# graphics for the pictures). Results: tools/selftest/selftest.txt and shot_*.png / thumb_*.png. Log: tools/selftest.log.
set -e
K=/c/Git/3_Working/SPZombies_MapKit
P=$K/BuildProject
if [ -f $P/Temp/UnityLockfile ] && ! rm -f $P/Temp/UnityLockfile 2>/dev/null; then echo "Unity has BuildProject open: close it first"; exit 1; fi
if [ ! -d $P/ProjectSettings ]; then
  S=/c/Git/2_References/TushonkaMappingSDK-1.9.3/Unity/TushonkaMappingSDK
  mkdir -p $P && cp -r $S/Assets $S/Packages $S/ProjectSettings $P/
fi
# Sound files for the map settings test (made once): mp3, ogg, 24-bit and float wavs, a wav with data before its
# format, and a plain 16-bit one; a 3 s tone at half volume
A=$K/tools/audio_in
if [ ! -f $A/tone_list.wav ]; then
  mkdir -p $A
  FF=ffmpeg; command -v ffmpeg >/dev/null || FF="$(ls /c/Users/*/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg*/ffmpeg-*/bin/ffmpeg.exe | head -1)"
  T="sine=frequency=440:duration=3,volume=0.5"
  "$FF" -v error -y -f lavfi -i "$T" -ac 2 -ar 44100 -b:a 192k $A/tone.mp3
  "$FF" -v error -y -f lavfi -i "$T" -ac 1 -ar 48000 $A/tone.ogg
  "$FF" -v error -y -f lavfi -i "$T" -ac 2 -ar 48000 -c:a pcm_s24le $A/tone_24bit.wav
  "$FF" -v error -y -f lavfi -i "$T" -ac 2 -ar 48000 -c:a pcm_f32le $A/tone_float.wav
  "$FF" -v error -y -f lavfi -i "$T" -ac 2 -ar 48000 -c:a pcm_s16le -bitexact $A/tone_16bit.wav
  /c/Git/.venv/Scripts/python.exe -I -c "import struct,sys; b=open(sys.argv[1],'rb').read(); l=b'LIST'+struct.pack('<I',4)+b'INFO'; o=b[:12]+l+b[12:]; o=o[:4]+struct.pack('<I',len(o)-8)+o[8:]; open(sys.argv[2],'wb').write(o)" "$(cygpath -w $A/tone_16bit.wav)" "$(cygpath -w $A/tone_list.wav)"
fi
T=$P/Assets/SPZombiesMapKit
rm -rf $T $T.meta
cp -r $K/Kit/SPZombiesMapKit $T
cp $K/Kit/SPZombiesMapKit.meta $T.meta
cp $K/tools/KitSelfTest.cs $T/Editor/KitSelfTest.cs
rm -rf $P/Assets/SelfTest $P/Assets/SelfTest.meta $P/Assets/SPZombiesMapData $P/Assets/SPZombiesMapData.meta
"/e/Unity/2022.3.43f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\\Git\\3_Working\\SPZombies_MapKit\\BuildProject" \
  -executeMethod SPZombies.MapKit.Editor.KitSelfTest.Run -logFile "C:\\Git\\3_Working\\SPZombies_MapKit\\tools\\selftest.log"
echo "unity exit $?"
