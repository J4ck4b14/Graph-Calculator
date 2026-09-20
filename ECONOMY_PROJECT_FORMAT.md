# `.gceconomy` project format

A `.gceconomy` file is a portable UTF-8 JSON document used by the **Economy Designer**. It is intentionally local-first: no account, server or external database is required. Copying the file is enough to move the model to another Graph Calculator installation.

Current format marker:

```json
{
  "Version": 4,
  "Format": "GraphCalculator.Economy"
}
```

Version 4 keeps the deterministic runtime-continuation data introduced in earlier versions and adds the richer authoring model used by the integrated systems workspace: Action nodes, subsystems, multi-resource recipes and resource presentation metadata.

## Authored model

A project can store:

- project name and description;
- all nodes and their canvas positions;
- node kind, resource type, notes, capacity and starting/current values;
- formula-driven flows and human-readable flow labels;
- flow conditions, efficiency/yield, probability, interval, delay and start/end scheduling;
- economy parameters and legal tuning ranges;
- scenarios and weighted player cohorts;
- balance targets;
- subsystems/groups and collapsed state;
- atomic multi-resource Converter recipes;
- resource colour, icon and unit metadata;
- simulation/prediction settings;
- current history-view preferences.

Supported node-kind strings currently include:

```text
Source
Event
Pool
Gate
Queue
Register
Converter
Action
Sink
```

### Subsystems

Nodes can carry a `SubsystemId`. The matching subsystem record stores the group name, notes and collapsed/expanded state. A subsystem is an authoring/visual organisation layer; the contained nodes and flows remain the simulation truth.

### Converter recipes

Version 4 can persist recipes independently from ordinary flows. A recipe identifies its Converter node and contains input/output items such as:

```text
30 Wood + 10 Iron + 200 Gold -> 1 Sword
```

Inputs are consumed atomically and outputs are produced atomically. A craft does not partially consume ingredients merely because one required input is unavailable.

### Resource presentation

Resource styles can store:

```text
name / colour / icon / unit
```

For example, Gold may use a gold swatch, a coin glyph and the unit `coins`. These values affect visualisation only; they do not change simulation arithmetic.

## Runtime continuation state

The portable file can preserve enough transient state to continue a paused stochastic simulation materially from the same point:

- current simulation time;
- current node amounts;
- delayed transfers still in transit;
- next scheduled activation per interval flow;
- deterministic random-stream state per flow;
- queued Action state where relevant;
- recent resource-history samples;
- selected scenario/cohort and population-mix settings;
- history-view preferences.

This is why a model saved at `t = 18.5` can be sent to another user without reducing the file to a mere diagram snapshot.

## Expressions

Flow and recipe expressions use the same mathematical language as Function Lab. Published shared assets can therefore be referenced from economy formulas when the economy lives inside a `.graphcalc` workspace.

Economy runtime variables include values such as:

```text
t
time
dt
source
target
sourcecap
targetcap
rand
gauss
run
cohort
```

They are supplied by the simulator and are not ordinary user parameters.

## Special values

`EndTime: 0` represents no scheduled end when reading older files.

Non-storage nodes do not use capacity. Pools, Gates, Queues, Registers and Converters may use finite capacity.

Action nodes are triggers rather than storage. Their purpose is to represent an explicit player/designer action that can release connected behaviour when triggered.

## Compatibility

The loader accepts older supported versions and reconstructs fields that did not yet exist using safe defaults. A newer unsupported version is rejected rather than silently guessed at.

The version history is broadly:

- **v1** — authored graph and basic simulation settings;
- **v2** — deterministic runtime-continuation state;
- **v3** — Event/Gate/Queue/Register vocabulary and richer visual-history state;
- **v4** — Action nodes, subsystems, recipes and resource styling.

The format remains readable JSON on purpose. It is meant to be inspectable, diffable and easy to archive or send to another designer.
