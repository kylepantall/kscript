# Shortcuts for kscript. `make run` runs main{{ ext }}, and any other target is a kscript make type:
#   make script name=tidy                      ->  kscript make script tidy
#   make test name=checkout
# On Windows, install make (for example with winget install GnuWin32.Make, scoop or choco), or run the
# kscript commands directly.
KSCRIPT ?= kscript

.PHONY: run test check fmt compile help{{#if web}} serve{{else}}{{#if api}} serve{{/if}}{{/if}}{{#if api}} routes{{/if}}{{#if desktop}} desktop app{{/if}}

run:
	$(KSCRIPT) main{{ ext }}

test:
	$(KSCRIPT) test

check:
	$(KSCRIPT) check

fmt:
	$(KSCRIPT) fmt
{{#if web}}

serve:
	$(KSCRIPT) serve
{{else}}
{{#if api}}

serve:
	$(KSCRIPT) serve
{{/if}}
{{/if}}
{{#if api}}

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
	$(KSCRIPT) compile main{{ ext }}

help:
	$(KSCRIPT) make

# Anything else goes to kscript make: make <type> name=<Name> [args="--option value"].
Makefile: ;
%:
	$(KSCRIPT) make $@ $(name) $(args)
