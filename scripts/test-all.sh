#!/usr/bin/env bash
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/test-common.sh"

build_status=0
set +e
run_build_and_import
build_status=$?
set -e
if [[ ${build_status} -ne 0 ]]; then
    print_pipeline_summary "FAIL" "FAIL" "SKIPPED" "${build_status}" "all"
    exit "${build_status}"
fi

test_status=0
set +e
run_feature_tests
test_status=$?
set -e
if [[ ${test_status} -eq 0 ]]; then
    print_pipeline_summary "PASS" "PASS" "PASS" "0" "all"
else
    print_pipeline_summary "FAIL" "PASS" "FAIL" "${test_status}" "all"
fi

exit "${test_status}"
