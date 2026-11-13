#!/bin/bash
# preview.sh <dump-id> <out-name> [set]
P="D:/games/SPT-4.1.6/.codex-work/EFTEnvgFusion"
S=/c/Users/LeeCh/AppData/Local/Temp/claude/1b8f5aa763d6006b/3a37a105-6aef-4e65-b6f6-2215a0ae1fce/scratchpad
cp "$P/EnvgChain.cs" "$P/Unity/Assets/Editor/EnvgChain.cs"
D="D:/games/SPT-TEST/BepInEx/plugins/JustachaeEnvgFusion/dump/$1"
O="$(cygpath -w $S/prev/$2)"
mkdir -p "$S/prev/$2"
"D:/Unity 2022.3.43f1/Editor/Unity.exe" -batchmode -quit -projectPath "$P/Unity" -executeMethod EnvgPreview.Run -envgDump "$D" -envgOut "$O" ${3:+-envgSet "$3"} -logFile "$P/_build/preview.log"
grep -a "ENVG_PREVIEW_DONE\|ENVG preview\|error CS\|Exception" "$P/_build/preview.log" | head -5
