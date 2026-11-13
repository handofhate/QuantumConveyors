#!/bin/bash
# Builds src/QuantumConveyors.Plugin into one file: src/QuantumConveyors.Plugin/bin/Release/net6.0/QuantumConveyors.dll.
# Needs Moon Sweepers with BepInEx and MoonFramework installed (it compiles against them). Set MOONSWEEPERS_GAME_ROOT if
# the game isn't in Steam's default folder. --install copies the DLL into the game's BepInEx/plugins/QuantumConveyors
# (with the game closed).
set -e
cd "$(dirname "$0")"
GAME="${MOONSWEEPERS_GAME_ROOT:-$HOME/.local/share/Steam/steamapps/common/Moon Sweepers}"
DOTNET=$(command -v dotnet || echo "$HOME/.dotnet/dotnet")
"$DOTNET" build src/QuantumConveyors.Plugin/QuantumConveyors.Plugin.csproj -c Release "-p:MoonSweepersGameRoot=$GAME"
if [ "$1" = "--install" ]; then
    DEST="$GAME/BepInEx/plugins/QuantumConveyors"
    mkdir -p "$DEST"
    find "$DEST" -maxdepth 1 -type f -delete                 # 1.x left models and pictures here; 2.x carries its own
    cp src/QuantumConveyors.Plugin/bin/Release/net6.0/QuantumConveyors.dll "$DEST/"
    echo "Installed to $DEST"
fi
