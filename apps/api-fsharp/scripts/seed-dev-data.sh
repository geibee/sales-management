#!/usr/bin/env bash
# Seeds a small, predictable set of dev fixtures via the public API.
# Idempotent: re-runs skip lots / sales-cases that already exist (HTTP 409).
set -euo pipefail

API="${API:-http://localhost:5000}"

post() {
  local path="$1" body="$2"
  curl -sS -o /tmp/seed.body -w '%{http_code}' \
    -X POST "$API$path" \
    -H 'content-type: application/json' \
    -d "$body"
}

create_lot() {
  local year="$1" loc="$2" seq="$3"
  local body
  body=$(cat <<EOF
{
  "lotNumber": { "year": $year, "location": "$loc", "seq": $seq },
  "divisionCode": 1, "departmentCode": 10, "sectionCode": 100,
  "processCategory": 1, "inspectionCategory": 1, "manufacturingCategory": 1,
  "details": [{
    "itemCategory": "general", "premiumCategory": "",
    "productCategoryCode": "A",
    "lengthSpecLower": 100, "thicknessSpecLower": 10, "thicknessSpecUpper": 20,
    "qualityGrade": "A", "count": 50, "quantity": 12.5,
    "inspectionResultCategory": ""
  }]
}
EOF
)
  local code
  code=$(post /lots "$body")
  if [[ "$code" == "200" || "$code" == "409" ]]; then
    echo "  lot $year-$loc-$(printf %03d "$seq") -> $code"
  else
    echo "  lot $year-$loc-$(printf %03d "$seq") FAILED ($code):" >&2
    cat /tmp/seed.body >&2; echo >&2
    return 1
  fi
}

transition() {
  local id="$1" verb="$2" body="$3"
  curl -sS -o /tmp/seed.body -w '%{http_code}' \
    -X POST "$API/lots/$id/$verb" \
    -H 'content-type: application/json' \
    -d "$body"
}

advance_lot() {
  # advance_lot <id> <target-state>
  # target ∈ manufactured | shipping_instructed | shipped | conversion_instructed
  local id="$1" target="$2"
  local code
  case "$target" in
    manufacturing) ;;
    manufactured)
      code=$(transition "$id" complete-manufacturing '{"date":"2025-04-01","version":1}')
      ;;
    shipping_instructed)
      code=$(transition "$id" complete-manufacturing '{"date":"2025-04-01","version":1}')
      code=$(transition "$id" instruct-shipping '{"deadline":"2025-06-30","version":2}')
      ;;
    shipped)
      code=$(transition "$id" complete-manufacturing '{"date":"2025-04-01","version":1}')
      code=$(transition "$id" instruct-shipping '{"deadline":"2025-06-30","version":2}')
      code=$(transition "$id" complete-shipping '{"date":"2025-05-15","version":3}')
      ;;
    conversion_instructed)
      code=$(transition "$id" complete-manufacturing '{"date":"2025-04-01","version":1}')
      code=$(transition "$id" instruct-item-conversion '{"destinationItem":"2025-T-902","version":2}')
      ;;
  esac
  echo "  $id -> $target (last=$code)"
}

echo "[1/2] Creating lots..."
create_lot 2025 T 1
create_lot 2025 T 2
create_lot 2025 T 3
create_lot 2025 T 4
create_lot 2025 T 5

echo "[2/2] Advancing lot states..."
# 2025-T-001 stays in manufacturing (no advance)
echo "  2025-T-001 -> manufacturing (initial)"
advance_lot 2025-T-2 manufactured
advance_lot 2025-T-3 shipping_instructed
advance_lot 2025-T-4 shipped
advance_lot 2025-T-5 conversion_instructed

echo "[bonus] Creating a sales case using a fresh manufactured lot 2025-T-101..."
create_lot 2025 T 101
advance_lot 2025-T-101 manufactured

curl -sS -o /tmp/seed.body -w '%{http_code}\n' \
  -X POST "$API/sales-cases" \
  -H 'content-type: application/json' \
  -d '{"lots":["2025-T-101"],"divisionCode":1,"salesDate":"2025-04-15","caseType":"direct"}'

cat /tmp/seed.body; echo
echo
echo "Done. Try:"
echo "  http://localhost:5173/lots/2025-T-1   (manufacturing)"
echo "  http://localhost:5173/lots/2025-T-2   (manufactured)"
echo "  http://localhost:5173/lots/2025-T-3   (shipping_instructed)"
echo "  http://localhost:5173/lots/2025-T-4   (shipped)"
echo "  http://localhost:5173/lots/2025-T-5   (conversion_instructed)"
echo "  http://localhost:5173/sales-cases/<see response above>"
