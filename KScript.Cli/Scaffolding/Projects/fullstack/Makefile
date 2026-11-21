# Shortcuts for kscript. `make serve` runs `kscript serve`, and any other target is a kscript make type:
#   make page name=contact                     ->  kscript make page contact
#   make model name=Tag args="--fields 'name: string'"
# On Windows, install make (for example with winget install GnuWin32.Make, scoop or choco), or run the
# kscript commands directly.
KSCRIPT ?= kscript

.PHONY: run serve test check fmt routes openapi agents{{#if docker}} up down{{/if}} help

run: serve

serve:
	$(KSCRIPT) serve

test:
	$(KSCRIPT) test

check:
	$(KSCRIPT) check

fmt:
	$(KSCRIPT) fmt

routes:
	$(KSCRIPT) routes

openapi:
	$(KSCRIPT) openapi

agents:
	$(KSCRIPT) agents
{{#if docker}}

up:
	docker compose up --build

down:
	docker compose down
{{/if}}

help:
	$(KSCRIPT) make

# Anything else goes to kscript make: make <type> name=<Name> [args="--option value"].
Makefile: ;
%:
	$(KSCRIPT) make $@ $(name) $(args)
