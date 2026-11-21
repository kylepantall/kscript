'use strict';

/**
 * What KScript: New Script opens: a short script that runs as it is and shows a variable, text that fills in a
 * value, a function, a list and a loop. Each part says what it is, so the file is the first lesson.
 */
const STARTER = `// Your first script. Press F5, or click Run above the next line, to run it.
name = "world"
echo "Hello $name!"

// A function takes values and gives one back.
fn double(n) => $n * 2

// A list holds several values, and for goes through them one at a time.
numbers = [1, 2, 3]
for n in $numbers {
  echo "$n doubled is { @double(n: $n) }"
}

// Try it: change "world" to your name, add a number to the list, then run again.
`;

/** Where the cursor goes in the starter: inside "world", the first thing to change. */
function starterSelection(text = STARTER) {
  const lines = text.split('\n');
  const line = lines.findIndex((l) => l.includes('"world"'));
  const start = lines[line].indexOf('"world"') + 1;
  return { line, start, end: start + 'world'.length };
}

module.exports = { STARTER, starterSelection };
