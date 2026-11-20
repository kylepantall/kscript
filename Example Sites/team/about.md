---
title: About us
layout: _layout.html
---
<let name="people" value="@read_json('data/people.json')" />

# About the team

We are { $people->count() } people. In alphabetical order:

<for each="p" in="$people->sort_by(fn($p) => $p.name)">
- **{$p.name}**, {$p.role}
</for>

Page rendered on { @today()->format(pattern: 'd MMMM yyyy') }.
