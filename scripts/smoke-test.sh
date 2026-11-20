#!/usr/bin/env bash
# End-to-end check against a running API (used by CI on the Docker image; also handy locally).
# Usage: scripts/smoke-test.sh [base-url]   (default http://localhost:5136)
set -euo pipefail

base="${1:-http://localhost:5136}"
samples="$(cd "$(dirname "$0")/../samples" && pwd)"

echo "Waiting for $base/health ..."
for _ in $(seq 1 60); do
  if curl -fsS "$base/health" > /dev/null 2>&1; then break; fi
  sleep 1
done
curl -fsS "$base/health" > /dev/null

check() {
  local name="$1" expected="$2" output="$3"
  if grep -qi -- "$expected" <<< "$output"; then
    echo "PASS $name"
  else
    echo "FAIL $name: expected '$expected' in: $output" >&2
    exit 1
  fi
}

check "front end" "<html" "$(curl -fsS "$base/")"
check "classify cat" "cat" "$(curl -fsS -F model=imageclassifier -F "file=@$samples/cat.jpg" "$base/api/ai")"
check "classify dog" "retriever" "$(curl -fsS -F model=imageclassifier -F "file=@$samples/dog.jpeg" "$base/api/ai")"
check "ocr english" "quick brown fox" "$(curl -fsS -F "file=@$samples/ocr-eng.png" "$base/api/ai/ocr")"
check "bad image is 400" "400" "$(curl -s -o /dev/null -w '%{http_code}' -F model=imageclassifier -F "file=@$0" "$base/api/ai")"

echo "All smoke tests passed."
