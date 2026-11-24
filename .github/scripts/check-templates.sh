#!/usr/bin/env bash
# Makes a project from every built-in kscript make project template, with the option mixes below, and checks that
# each one passes kscript check (warnings are errors), kscript fmt --check, kscript test and kscript agents --check.
# The api and fullstack projects are also served, and asked for a page and the API, to show they run.
#
#   .github/scripts/check-templates.sh "dotnet KScriptApp/bin/Release/net8.0/KScript.dll" [work folder]
#
# A template the list below doesn't cover fails the run, so a new template gets checked from the start.
set -euo pipefail

k="$1"
work="${2:-${RUNNER_TEMP:-$(mktemp -d)}/templates}"
rm -rf "$work"
mkdir -p "$work"
cd "$work"

cases=(
  "web|"
  "web|--syntax xml"
  "web|--no-desktop --no-http"
  "bare|--bare"
  "bare|--bare --web --api --desktop"
  "api|--template api"
  "api|--template api --syntax xml"
  "api|--template api --db none"
  "api|--template api --no-auth --no-cache"
  "api|--template api --db none --no-auth --no-cache --syntax xml"
  "api|--template api --db postgres --docker"
  "fullstack|--template fullstack"
  "fullstack|--template fullstack --examples"
  "fullstack|--template fullstack --auth"
  "fullstack|--template fullstack --examples --syntax xml --auth"
  "fullstack|--template fullstack --examples --db none --no-cache"
  "fullstack|--template fullstack --db mysql --docker"
)

# Every built-in template needs at least one case.
templates=$($k make project --help | sed -n 's/^.*Built-in templates: \([^(]*\).*$/\1/p' | tr -d ' ' | tr ',' ' ')
if [ -z "$templates" ]; then echo "Couldn't read the built-in templates from kscript make project --help" >&2; exit 1; fi
for t in $templates; do
  if ! printf '%s\n' "${cases[@]}" | grep -q "^$t|"; then
    echo "The $t project template has no case in .github/scripts/check-templates.sh. Add one." >&2
    exit 1
  fi
done

n=0
for c in "${cases[@]}"; do
  n=$((n + 1))
  template="${c%%|*}"
  options="${c#*|}"
  dir="$template-$n"
  echo "::group::$template $options"
  # shellcheck disable=SC2086
  $k make project "$dir" $options
  $k check "$dir" --warnings-as-errors
  $k fmt --check "$dir"
  $k test "$dir"
  $k agents --check "$dir"
  echo "::endgroup::"
done

# Serve the api and both fullstack variants, and ask them for a page and the API.
serve_and_get() {
  local dir="$1" port="$2"; shift 2
  (cd "$dir" && exec $k serve --port "$port" > "$work/$dir.log" 2>&1) &
  local pid=$!
  local up=""
  for _ in $(seq 1 60); do
    if curl -fs --max-time 5 "http://localhost:$port/api/health" > /dev/null; then up=yes; break; fi
    sleep 1
  done
  if [ -z "$up" ]; then cat "$work/$dir.log"; kill "$pid" 2>/dev/null || true; echo "$dir didn't start" >&2; return 1; fi
  for path in "$@"; do
    echo "GET $path"
    curl -fs --max-time 10 "http://localhost:$port$path" > /dev/null || { cat "$work/$dir.log"; kill "$pid" 2>/dev/null || true; echo "$dir: GET $path failed" >&2; return 1; }
  done
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
}

api=$(printf '%s\n' "${cases[@]}" | grep -n '^api|--template api$' | cut -d: -f1)
full=$(printf '%s\n' "${cases[@]}" | grep -n '^fullstack|--template fullstack$' | cut -d: -f1)
serve_and_get "api-$api" 8761 /api/health /_api /_openapi.json
full_examples=$(printf '%s\n' "${cases[@]}" | grep -n '^fullstack|--template fullstack --examples$' | cut -d: -f1)
serve_and_get "fullstack-$full" 8762 / /api/health /_api
serve_and_get "fullstack-$full_examples" 8763 / /notes /api/notes /_api

echo "All ${#cases[@]} template projects check, format, test and serve cleanly."
