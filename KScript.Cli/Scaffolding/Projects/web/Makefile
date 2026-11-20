# Shortcuts for kscript. `make serve` runs `kscript serve`, and any other target is a kscript make type:
#   make page name=contact                     ->  kscript make page contact
#   make component name=Hero args="--props title"
# On Windows, install make (for example with winget install GnuWin32.Make, scoop or choco), or run the
# kscript commands directly.
KSCRIPT ?= kscript

.PHONY: run serve test check fmt agents{{#if http}} routes{{/if}}{{#if desktop}} desktop app{{/if}} compile help

run: serve

serve:
	$(KSCRIPT) serve

test:
	$(KSCRIPT) test

check:
	$(KSCRIPT) check

fmt:
	$(KSCRIPT) fmt

agents:
	$(KSCRIPT) agents
{{#if http}}

routes:
	$(KSCRIPT) routes
{{/if}}
{{#if desktop}}

desktop:
	$(KSCRIPT) desktop{{ ext }}

app:
	$(KSCRIPT) app build
{{/if}}

compile:
	$(KSCRIPT) compile scripts/hello{{ ext }}

help:
	$(KSCRIPT) make

# Anything else goes to kscript make: make <type> name=<Name> [args="--option value"].
Makefile: ;
%:
	$(KSCRIPT) make $@ $(name) $(args)
