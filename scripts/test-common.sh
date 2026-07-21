#!/usr/bin/env bash
set -euo pipefail

TEST_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"

find_godot() {
    if [[ -n "${GODOT_BIN:-}" ]]; then
        if [[ ! -x "${GODOT_BIN}" ]]; then
            printf 'GODOT_BIN is not executable: %s\n' "${GODOT_BIN}" >&2
            return 1
        fi

        printf '%s\n' "${GODOT_BIN}"
        return 0
    fi

    local candidate
    for candidate in godot4-mono godot-mono godot4 godot; do
        if command -v "${candidate}" >/dev/null 2>&1; then
            command -v "${candidate}"
            return 0
        fi
    done

    printf '%s\n' \
        'Godot .NET executable was not found. Set GODOT_BIN=/path/to/godot.' >&2
    return 127
}

run_build_and_import() {
    local dotnet_bin="${DOTNET_BIN:-dotnet}"
    if ! command -v "${dotnet_bin}" >/dev/null 2>&1; then
        printf 'The .NET SDK executable was not found: %s\n' "${dotnet_bin}" >&2
        return 127
    fi

    local godot_bin
    godot_bin="$(find_godot)"

    if [[ "${CLEAN_GODOT_CACHE:-0}" == "1" ]]; then
        printf 'Removing generated Godot cache: %s/.godot\n' "${TEST_ROOT}"
        rm -rf -- "${TEST_ROOT}/.godot"
    fi

    (
        set -euo pipefail
        cd "${TEST_ROOT}"
        "${dotnet_bin}" restore LineZero.csproj
        "${dotnet_bin}" build LineZero.csproj --no-restore
        "${godot_bin}" --headless --path "${TEST_ROOT}" --import
    )
}

run_feature_tests() {
    local godot_bin
    godot_bin="$(find_godot)"
    (
        set -euo pipefail
        cd "${TEST_ROOT}"
        "${godot_bin}" \
            --headless \
            --path "${TEST_ROOT}" \
            res://scenes/tests/FeatureTests.tscn \
            -- "$@"
    )
}

print_pipeline_summary() {
    local result="$1"
    local build_import="$2"
    local tests="$3"
    local exit_code="$4"
    local selection="${5:-all}"

    printf '\n[TEST][PIPELINE_SUMMARY]\n'
    printf '  result: %s\n' "${result}"
    printf '  selection: %s\n' "${selection}"
    printf '  build/import: %s\n' "${build_import}"
    printf '  tests: %s\n' "${tests}"
    printf '  exit code: %s\n' "${exit_code}"
}
