#!/usr/bin/env bash
# Which captures in queries/highlights.scm does no highlight test pin?
#
# `tree-sitter test` checks the `// ^ capture` assertions in test/highlight/,
# but nothing notices a capture that no assertion mentions: its rule can be
# rewritten, mis-targeted or deleted and the suite stays green. This lists the
# captures with no assertion. Pass --check to exit 1 when there are any.
#
#     scripts/highlight-coverage.sh            # report
#     scripts/highlight-coverage.sh --check    # gate
set -euo pipefail
cd "$(dirname "$0")/.."

captures=$(grep -oE '@[a-z_]+(\.[a-z_]+)*' queries/highlights.scm | sort -u)
asserted=$(grep -hoE '\^+ *[a-z_]+(\.[a-z_]+)*' test/highlight/*.fsx \
           | sed -E 's/^\^+ *//; s/^/@/' | sort -u)
# `// <- capture` anchors the first column; the same capture name applies.
asserted=$(printf '%s\n%s\n' "$asserted" \
           "$(grep -hoE '<- *[a-z_]+(\.[a-z_]+)*' test/highlight/*.fsx | sed -E 's/^<- *//; s/^/@/')" \
           | sort -u)

missing=$(comm -23 <(echo "$captures") <(echo "$asserted"))
total=$(echo "$captures" | wc -l)
covered=$((total - $(echo "$missing" | grep -c . || true)))

echo "highlights.scm captures: $total, asserted in test/highlight: $covered"
if [ -n "$missing" ]; then
  echo "not asserted by any highlight test:"
  echo "$missing" | sed 's/^/  /'
  if [ "${1:-}" = "--check" ]; then exit 1; fi
fi
