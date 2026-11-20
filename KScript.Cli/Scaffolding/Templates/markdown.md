--- make
description: A Markdown page with front matter (title, layout)
category: Pages and styles
usage: <path> [--title "Title"] [--layout _layout.html]
--title: The page title (default from the name)
--layout: The layout it uses (default _layout.html)
example: kscript make markdown blog/first-post
example: kscript make markdown docs/setup --layout _layouts/docs.html
to: {{ dir }}{{ kebab }}.md
hint: Run kscript serve and open /{{ url }}
---
---
title: {{ title }}
layout: {{ layout ?? _layout.html }}
---

# {{ title }}

Write your page here. KScript works too: today is { @today()->format(pattern: 'd MMMM yyyy') }.
