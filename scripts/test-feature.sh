#!/usr/bin/env bash
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/test-common.sh"

if [[ $# -ne 1 || -z "$1" ]]; then
    printf 'Usage: %s <suite-id>\n' "$0" >&2
    printf 'Run %s/list-tests.sh to see available suite IDs.\n' \
        "$(dirname -- "$0")" >&2
    exit 2
fi

selection="$1"
build_status=0
set +e
run_build_and_import
build_status=$?
set -e
if [[ ${build_status} -ne 0 ]]; then
    print_pipeline_summary "FAIL" "FAIL" "SKIPPED" "${build_status}" "${selection}"
    exit "${build_status}"
fi

test_status=0
set +e
run_feature_tests "--suite=${selection}"
test_status=$?
set -e
if [[ ${test_status} -eq 0 ]]; then
    print_pipeline_summary "PASS" "PASS" "PASS" "0" "${selection}"
else
    print_pipeline_summary "FAIL" "PASS" "FAIL" "${test_status}" "${selection}"
fi

exit "${test_status}"
