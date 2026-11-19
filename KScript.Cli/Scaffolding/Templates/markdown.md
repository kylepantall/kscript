--- make
description: A Markdown page with front matter (title, layout)
to: {{ dir }}{{ kebab }}.md
hint: Run kscript serve and open /{{ url }}
---
---
title: {{ title }}
layout: {{ layout ?? _layout.html }}
---

# {{ title }}

Write your page here. KScript works too: today is { @today()->format('d MMMM yyyy') }.
