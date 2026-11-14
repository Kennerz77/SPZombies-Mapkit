#!/bin/sh
# Builds the release in BuildProject (never the user's TestProject): the kit (without the self-test), Unity exports the
# .unitypackage into build/. Usage: sh tools/build_package.sh 0.1.0
set -e
V=${1:?version, e.g. 0.1.0}
K=/c/Git/3_Working/SPZombies_MapKit
P=$K/BuildProject
grep -q "KitVersion = \"$V\"" $K/Kit/SPZombiesMapKit/Editor/KitInfo.cs || { echo "KitInfo.KitVersion isn't $V"; exit 1; }
if [ -f $P/Temp/UnityLockfile ] && ! rm -f $P/Temp/UnityLockfile 2>/dev/null; then echo "Unity has BuildProject open: close it first"; exit 1; fi
if [ ! -d $P/ProjectSettings ]; then
  S=/c/Git/2_References/TushonkaMappingSDK-1.9.3/Unity/TushonkaMappingSDK
  mkdir -p $P && cp -r $S/Assets $S/Packages $S/ProjectSettings $P/
fi
T=$P/Assets/SPZombiesMapKit
rm -rf $T $T.meta
cp -r $K/Kit/SPZombiesMapKit $T
cp $K/Kit/SPZombiesMapKit.meta $T.meta
rm -rf $P/Assets/SelfTest $P/Assets/SelfTest.meta $P/Assets/SPZombiesMapData $P/Assets/SPZombiesMapData.meta
mkdir -p $K/build
PKG="SPZombiesMapKit_v$V.unitypackage"
rm -f $K/build/$PKG
"/e/Unity/2022.3.43f1/Editor/Unity.exe" -batchmode -quit -nographics -projectPath "C:\\Git\\3_Working\\SPZombies_MapKit\\BuildProject" \
  -exportPackage Assets/SPZombiesMapKit "C:\\Git\\3_Working\\SPZombies_MapKit\\build\\$PKG" -logFile "C:\\Git\\3_Working\\SPZombies_MapKit\\tools\\package.log"
echo "unity exit $?"
if grep -E "error CS" $K/tools/package.log; then exit 1; fi
ls -la $K/build/$PKG
