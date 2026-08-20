#!/bin/sh
set -eu

if [ "$#" -lt 2 ] || [ "$#" -gt 4 ]; then
    echo "usage: $0 UNIFFI_DLL BOLTFFI_DLL [ROUNDS] [RESULT_DIR]" >&2
    exit 2
fi

uniffi_dll=$1
boltffi_dll=$2
rounds=${3:-3}
result_dir=${4:-ffi-comparison-results}

if [ ! -f "$uniffi_dll" ] || [ ! -f "$boltffi_dll" ]; then
    echo "both benchmark DLL files must exist" >&2
    exit 2
fi

mkdir -p "$result_dir"
round=1
while [ "$round" -le "$rounds" ]; do
    if [ $((round % 2)) -eq 1 ]; then
        first_name=uniffi
        first_dll=$uniffi_dll
        second_name=boltffi
        second_dll=$boltffi_dll
    else
        first_name=boltffi
        first_dll=$boltffi_dll
        second_name=uniffi
        second_dll=$uniffi_dll
    fi

    dotnet "$first_dll" >"$result_dir/$first_name-$round.json"
    dotnet "$second_dll" >"$result_dir/$second_name-$round.json"
    round=$((round + 1))
done
