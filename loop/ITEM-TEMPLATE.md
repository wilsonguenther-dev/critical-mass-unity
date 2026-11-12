---
id: F-00
title: One plain-English line a person can read in a text message
phase: 0
owner: loop
waits_on: []
files_allowed:
  - Assets/_Game/Sim/Example/**
  - Assets/_Game/Sim.Tests/ExampleTests.cs
files_forbidden:
  - Assets/_Game/Sim/*.asmdef
  - loop/items/**
tests_required:
  - ExampleTests.DoesTheOneThingTheItemSays
pass_check: "tools/unity-compile.sh exit 0; tools/unity-test.sh OK; ExampleTests has 1 passed"
art_row: none
tightly_defined: false
---
# F-00 Example (copy this file to loop/items/<ID>.md; this template is not an item)

## Method

One method, every number, key and file named. No choices for the builder: the validator rejects "or", "either",
"if installed" and "TBD" here. A guarded path (ProjectSettings/, Packages/, Controls.inputactions) needs a
front-matter line `architect_approved: U-###`.

## Out of scope

What the builder must not build. This section may name alternatives.
