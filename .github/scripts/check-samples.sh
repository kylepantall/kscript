#!/usr/bin/env bash
# Runs the samples and a new project of each kind through kscript: run, check, fmt --check, test, the data sample's
# migrations and seeder, and the kscript make generators. The test jobs run it on every OS after the unit tests.
#
#   .github/scripts/check-samples.sh "dotnet $PWD/KScriptApp/bin/Release/net8.0/KScript.dll"
set -euo pipefail

k="$1"

group() { echo "::endgroup::"; echo "::group::$1"; }
echo "::group::Run sample script"
$k samples/scripts/oldest.kscript

group "Check KScript 2 samples"
$k check samples/scripts samples/xml samples/life-lab samples/http-app samples/auth-app samples/storage samples/documents \
  samples/queue-app samples/data --warnings-as-errors

group "Check KScript formatting"
$k fmt --check samples/scripts samples/xml samples/life-lab samples/http-app samples/auth-app samples/storage \
  samples/documents samples/queue-app samples/data samples/types "Example Sites/team"

group "Run KScript tests"
$k test samples/scripts samples/xml samples/life-lab samples/queue-app samples/data

group "Run the data sample's migrations and seeder"
$k db migrate --seed --project samples/data --no-ansi < /dev/null
# The migrations make the tables the models want, so there is nothing to write.
diff=$($k db diff --pretend --project samples/data --no-ansi < /dev/null)
echo "$diff"
grep -q "Nothing to write" <<< "$diff"

group "Check a new project (kscript make project)"
cd "${RUNNER_TEMP:-$(mktemp -d)}"
$k make project new-app
$k check new-app --warnings-as-errors
$k fmt --check new-app
$k test new-app
$k agents --check new-app
$k make project xml-app --syntax xml
$k check xml-app --warnings-as-errors
$k fmt --check xml-app
$k test xml-app
$k agents --check xml-app
$k make project bare-tool --bare
$k check bare-tool --warnings-as-errors
$k test bare-tool
$k agents --check bare-tool
$k bare-tool/main.kscript
$k make project bare-app --bare --web --api --desktop
$k check bare-app --warnings-as-errors
$k fmt --check bare-app
$k test bare-app
$k agents --check bare-app

group "Run the kscript make generators"
# Routes add themselves to routes/, and everything they make checks and formats cleanly.
cd bare-app
$k make route users
$k make route "users/{id}" --where "id: int"
$k make route users --method post
$k make route people --resource --api
$k make route about --view about
$k make request NewPerson --fields "name: string, age?: int"
$k make middleware secure-headers
$k make type Repo --fields "name: string, stars: int"
$k make enum Size --values small,medium,large
$k make error PaymentFailed --status 402 --extends Validation --fields "order: int, reason?: string"
$k make css theme
$k check .
$k fmt --check .
$k routes .
$k test .
echo "::endgroup::"
