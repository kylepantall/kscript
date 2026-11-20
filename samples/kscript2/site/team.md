---
title: Team
layout: _layout.html
---
<let name="people" value="@read_json('data/people.json')" />

# Our team ({ $people->count() } people)

<for each="p" in="$people->sort_by(fn($p) => $p.name)">
- **{$p.name}**, {$p.role}
</for>

Written by { $title }. Last updated { @now()->format(pattern: 'd MMMM yyyy') }.
