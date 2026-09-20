# Graph Calculator — Function, Curve & Economy Lab

Graph Calculator is a local Windows desktop tool for **seeing, shaping, testing and simulating mathematical behaviour**.

It began as a conventional graphing calculator, but it now has three connected workspaces:

1. **Function Lab** — write mathematics and see what it does.
2. **Curve Designer** — draw the behaviour you want and let the app generate the mathematics.
3. **Economy Designer** — connect gameplay resources into a running, stochastic system and test whether the economy behaves the way you intended.

You do **not** need to be a mathematician to use it. The first half of this README explains the tool in ordinary language and gives concrete workflows. The later sections act as a technical reference for designers, technical artists, VFX artists, programmers and technical designers who want to know exactly what the app is doing.

---

## Contents

- [Run it](#run-it)
- [The three workspaces in one minute](#the-three-workspaces-in-one-minute)
- [A ten-minute tour](#a-ten-minute-tour)
- [Plain-language glossary](#plain-language-glossary)
- [Function Lab — beginner guide](#function-lab--beginner-guide)
- [Function Lab — plot types](#function-lab--plot-types)
- [Live parameters and time](#live-parameters-and-time)
- [Curve Designer](#curve-designer)
- [Economy Designer — beginner guide](#economy-designer--beginner-guide)
- [Economy Designer — the visual language](#economy-designer--the-visual-language)
- [Economy node glossary](#economy-node-glossary)
- [Economy flows](#economy-flows)
- [Scenarios, cohorts, predictions and balance search](#scenarios-cohorts-predictions-and-balance-search)
- [Portable economy files](#portable-economy-files)
- [Technical simulation rules](#technical-simulation-rules)
- [Analysis, inspection and comparison](#analysis-inspection-and-comparison)
- [Presets](#presets)
- [Save/load](#saveload)
- [Export and capture](#export-and-capture)
- [Expression language reference](#expression-language-reference)
- [Render quality](#render-quality)
- [Common workflows](#common-workflows)
- [Common mistakes and troubleshooting](#common-mistakes-and-troubleshooting)
- [Scope and limitations](#scope-and-limitations)

---

# Run it

Open:

```text
GraphCalculator.sln
```

in Visual Studio 2022 with the **.NET 8 desktop development** workload installed, then build and run the `GraphCalculator` project.

The project intentionally does not ship an old compiled `bin` build. Rebuilding avoids accidentally launching an earlier version of the application.

---

# The three workspaces in one minute

The workspace selector is at the top of the window.

## Function Lab

You type:

```text
sin(x)
```

and the app draws the function.

You can also type much more game-oriented expressions:

```text
100*x/(x+20)
```

```text
amplitude*sin(frequency*x+t)
```

```text
texture(fbm(x*frequency,y*frequency,octaves,persistence,lacunarity))
```

```text
surface(
    (majorRadius+minorRadius*cos(v))*cos(u),
    (majorRadius+minorRadius*cos(v))*sin(u),
    minorRadius*sin(v)
)
```

Function Lab is **math → visual result**.

## Curve Designer

You click points onto the graph and drag them until the curve feels right.

The authored keys form a cubic Hermite curve, but the curve is treated as a **curve asset**, not merely as a giant generated equation. You can decide what happens outside the authored key range — clamp, continue the endpoint tendency, repeat, ping-pong, invert every other cycle, or repeat while carrying the net vertical offset forward.

Curve Designer also looks for **simpler analytic functions** that resemble what you drew. A complicated hand-authored spline may turn out to be close enough to a cubic, smoothstep, sigmoid, sine, exponential or damped oscillation that you can use the simpler formula instead. The app shows the approximation over the authored curve and reports its normalized error.

Curve Designer is **visual result → reusable behaviour → math**.

This is useful when you know that a damage curve, joystick response, recoil response or progression curve should *feel* a certain way but you do not want to invent the equation first.

## Economy Designer

You build a diagram such as:

```text
Quest rewards  →  Player wallet  →  Upgrades
    Source            Pool             Sink
```

and tell the connections how quickly Gold moves.

Then you press **Play** and watch the resource move through the system.

You can add randomness, schedules, delays, player cohorts, scenarios, predictions and target-driven balance searches.

Economy Designer is **systems behaviour → simulation and evidence**.

---

# A ten-minute tour

If you have just opened the app and do not know where to begin, this is the shortest useful tour.

## 1. Draw a normal function

In **Function Lab**, select **2D** and enter:

```text
sin(x)
```

Scroll to zoom. Drag to pan.

Add another expression:

```text
0.5*sin(3*x)
```

Both functions remain visible together.

## 2. Create a live parameter

Enter:

```text
amplitude*sin(x)
```

`amplitude` was not a built-in coordinate or function name, so the app automatically creates a parameter slider for it.

Move the slider. The graph changes immediately.

## 3. Animate time

Enter:

```text
sin(x+t)
```

Press Play on the timeline. The wave moves because `t` is the global time variable.

## 4. Draw a curve instead of writing one

Switch to **Curve Designer**.

Click several points on the graph. Drag them. Select a point and adjust its tangents.

Choose what the curve does **before** the first key and **after** the last key. For example, set After to **Continue** for a trend that keeps moving, **Repeat** for a loop, or **Ping-pong** for a back-and-forth response.

The left panel shows a compact curve definition by default. The very large literal piecewise equation is still available under **Exact expanded formula**, but it is intentionally hidden until you ask for it.

Below that, **Similar analytic functions** suggests compact approximations. Select one and the orange dashed line shows how closely it matches the authored spline.

Use **Add exact spline to Function Lab** for mathematical equivalence, or **Add suggestion to Function Lab** when the simpler approximation is good enough. Neither action deletes existing Function Lab expressions.

## 5. Build a tiny economy

Switch to **Economy Designer**.

Add:

- one **Source**,
- one **Pool**,
- one **Sink**.

Set all three resources to `Gold`.

Enable **Connect** and connect:

```text
Source → Pool → Sink
```

Select the first flow and enter:

```text
10
```

as its Rate.

Select the second and enter:

```text
7
```

Press **▶**.

The Pool should slowly fill because 10 Gold/s enters and 7 Gold/s leaves.

The Gold connections are colour-coded, and moving dots show the actual resource traffic.

That simple diagram is already enough to explain faucets, storage, sinks and inflation pressure.

---

# Plain-language glossary

You will see these words throughout the app and this README.

**Function** — a rule that turns an input into an output. `x^2` is a function: give it `3`, it gives `9`.

**Parameter** — a named number you want to tune without rewriting the formula. In `amplitude*sin(x)`, `amplitude` is a parameter.

**Domain** — the input range over which one expression should be used. A function may mathematically exist outside the domain you care about.

**Scalar field** — one number at every position. A terrain height map or grayscale mask is a scalar field.

**Vector field** — a direction and strength at every position. Wind or steering arrows are vector fields.

**Interpolation** — filling in smooth values between authored values or keys.

**Faucet / Source** — something that creates a resource.

**Sink** — something that consumes/destroys a resource.

**Stochastic** — contains randomness. A 20% loot drop is stochastic.

**Seed** — the starting value used by deterministic randomness. Reusing the same seed lets a random-looking simulation be reproduced.

**Cohort** — a group of players represented by one set of behavioural/tuning values, such as Casual or Core.

**Simulation timestep (`dt`)** — how much simulated time one numerical step advances. Smaller steps are more precise temporally but require more work.

**P10 / P90** — distribution percentiles. P10 is a low-end outcome exceeded by roughly 90% of runs; P90 is a high-end outcome exceeded by roughly 10% of runs. The interval between them contains the middle ~80% of results.

---

# Function Lab — beginner guide

## What is a function here?

In ordinary 2D mode, the app treats an expression as:

```text
y = f(x)
```

You usually only need to type the right-hand side.

For example:

```text
x^2
```

means:

```text
y = x^2
```

For every horizontal position `x`, the expression gives one vertical position `y`.

## Calculator-only expressions

An expression that does not depend on a graph coordinate can be evaluated numerically.

For example:

```text
2+2
sqrt(144)
sin(pi/2)
```

## Multiple expressions

Use **+ Expression** to add another expression.

Each expression has:

- its own colour,
- a visibility checkbox,
- optional domain limits,
- an inline error/status line,
- its own special plot type if you use a wrapper such as `curve(...)`, `field(...)`, `texture(...)` or `implicit(...)`.

Expressions coexist. Adding a preset also **adds** its expressions instead of deleting the current set.

## View range versus function domain

These are different ideas.

### View range

The view range answers:

> What part of the mathematical world am I looking at?

For example:

```text
X: -10 to 10
Y: -5 to 20
```

### Function domain

The domain answers:

> Over what input range should this particular function exist in the graph?

For example:

```text
smoothstep(0,1,x)
Domain x: 0 to 1
```

The graph view can be much larger than the function's meaningful domain.

Domain fields accept expressions such as:

```text
-pi
2*pi
1e-3
```

Blank domain limits mean unrestricted.

---

# Function Lab — plot types

Function Lab supports several different mathematical representations. They are deliberately different because not every interesting shape can be described as `y=f(x)` or `z=f(x,y)`.

## Ordinary 2D function

```text
sin(x)
```

Interpreted as:

```text
y = f(x)
```

Useful for:

- damage scaling,
- XP progression,
- falloff,
- easing,
- cooldown curves,
- animation response,
- shader remapping,
- VFX envelopes.

## Piecewise function

Use `if(...)`:

```text
if(x<0,0,x^2)
```

Nested example:

```text
if(x<0,-1,if(x>1,1,x))
```

Comparisons evaluate numerically to `1` or `0`:

```text
x < 0
x >= threshold
x == 1
x != 0
```

Logical helpers:

```text
and(x>0,x<1)
or(x<-1,x>1)
not(x>0)
```

## 2D parametric curve

Use:

```text
curve(x(t),y(t))
```

Example circle:

```text
curve(cos(t),sin(t))
```

Example Lissajous-style curve:

```text
curve(sin(a*t+phase),sin(b*t))
```

The first domain pair becomes the `t` interval. Blank bounds default to `0..2*pi`.

`param(...)` is an alias.

## 2D implicit curve

An implicit curve describes where a field equals zero.

```text
implicit(x^2+y^2-radius^2)
```

You may also use equation notation:

```text
implicit(x^2+y^2=radius^2)
```

The app traces the zero contour numerically.

Aliases:

```text
contour(...)
implicit2(...)
```

This is useful for SDF slices, boundaries and shapes that are awkward as `y=f(x)`.

## 2D vector field

Use:

```text
field(vx,vy)
```

Example rotational field:

```text
field(-y,x)
```

Example attraction field:

```text
field(-strength*x,-strength*y)
```

The graph shows arrows whose directions and lengths represent the vector field.

Aliases:

```text
vector(...)
vectorfield(...)
```

Useful for:

- steering,
- wind,
- force fields,
- VFX velocity,
- agent steering / navigation flow concepts.

## 2D texture / scalar field

Use:

```text
texture(f(x,y))
```

Example:

```text
texture(fbm(x*frequency,y*frequency,octaves,persistence,lacunarity))
```

or:

```text
texture(exp(-(x^2+y^2)))
```

The scalar field is rendered as an image behind the graph.

Palettes:

- Grayscale
- Signed
- Heat

Aliases:

```text
mask(...)
heatmap(...)
```

This is useful for masks, noise fields, terrain brushes, falloff maps and shader prototypes.

## Ordinary 3D height field

Switch the graph to **3D**. A normal scalar expression becomes:

```text
z = f(x,y)
```

Examples:

```text
x^2-y^2
```

```text
exp(-(x^2+y^2))
```

```text
exp(-0.35*sqrt(x^2+y^2))*sin(8*sqrt(x^2+y^2)+t)
```

Drag to orbit. Use the wheel to zoom.

X/Y/Z view fields define the visible 3D volume.

The sampler refines boundaries where a function stops being valid. For example:

```text
sqrt(25-x^2-y^2)
```

follows the circular hemisphere boundary instead of dropping entire square sample cells around the edge.

## 3D parametric curve

Use:

```text
curve3(x(t),y(t),z(t))
```

Examples:

```text
curve3(3*cos(t),3*sin(t),0.35*t)
```

```text
curve3(sin(t)+2*sin(2*t),cos(t)-2*cos(2*t),-sin(3*t))
```

`param3(...)` is an alias.

## Parametric 3D surface

Use two parameters, `u` and `v`:

```text
surface(x(u,v),y(u,v),z(u,v))
```

Example torus:

```text
surface(
    (majorRadius+minorRadius*cos(v))*cos(u),
    (majorRadius+minorRadius*cos(v))*sin(u),
    minorRadius*sin(v)
)
```

This supports surfaces that cannot be represented as `z=f(x,y)`, including tori, Möbius strips, spheres and helicoids.

Aliases:

```text
surface3(...)
surf(...)
paramsurface(...)
```

## 3D implicit surface / SDF

Use:

```text
implicit3(x^2+y^2+z^2-radius^2)
```

or:

```text
sdf(sqrt(x^2+y^2+z^2)-radius)
```

Aliases:

```text
sdf(...)
isosurface(...)
```

The app extracts the zero surface inside the current 3D view volume using marching tetrahedra.

---

# Live parameters and time

## Automatic parameters

Any unknown identifier that is not a graph coordinate or built-in name becomes a live parameter.

Type:

```text
2*x+tuning
```

and `tuning` becomes a slider.

Type:

```text
amplitude*sin(frequency*x)
```

and both `amplitude` and `frequency` appear.

Parameters with the same identifier are shared between expressions.

Each parameter supports:

- minimum,
- maximum,
- current value,
- display label,
- group,
- unit,
- lock,
- independent animation.

## Parameter animation

A parameter may remain manual or automatically move through its range.

Modes:

- **Loop** — reaches the end and wraps back to the beginning.
- **Ping-pong** — moves forward, then backward.
- **Once** — moves to the end and stops.

Animation speed is expressed in parameter units per second and is elapsed-time based rather than frame-count based.

## Global time

`t` and `time` are special global timeline variables.

Example:

```text
sin(x+t)
```

The transport bar provides:

- play,
- pause,
- stop,
- direct scrubbing,
- start/end time,
- playback speed,
- looping.

Inside `curve(...)` and `curve3(...)`, `t` belongs to the parametric curve itself rather than the global timeline.

## A/B parameter states

**Capture A** and **Capture B** store two complete parameter states.

The Blend slider interpolates every unlocked parameter between them.

This is useful for questions such as:

> How does the recoil profile change between a pistol tuning and a rifle tuning?

or:

> What does this material response look like halfway between these two states?

---

# Curve Designer

Curve Designer is the inverse of Function Lab.

Instead of writing an equation first, you draw the behaviour you want. The result behaves much more like an animation/gameplay curve asset than a one-off graph.

## Basic workflow

1. Switch to **Curve Designer**.
2. Click the axes to place keys, or enter exact X/Y values.
3. Drag keys to reshape the curve.
4. Select a key to edit its tangent mode or drag its tangent handles.
5. Choose the **Before** and **After** behaviour for values outside the authored key range.
6. Inspect the suggested analytic functions if you want a smaller formula.
7. Send either the exact spline or a selected approximation to Function Lab.

## Why it does not use one giant polynomial

A high-order polynomial passing through many points can oscillate wildly between those points. Moving one key can also alter the shape far away from that key.

Curve Designer instead uses **piecewise cubic Hermite interpolation**. Each interval between two keys gets its own cubic segment.

That gives predictable local editing and behaves like the curve editors commonly used for animation, gameplay response and technical-art authoring.

## Tangent modes

Each key supports:

- **Auto** — the app estimates a smooth tangent from neighbouring keys.
- **Linear** — the incoming/outgoing slopes follow the adjacent straight-line segments.
- **Flat** — zero slope at the key.
- **Manual** — you control incoming/outgoing tangents explicitly.

Manual tangents may be linked or broken. Selecting a key shows tangent handles on the graph. Dragging a handle switches that key to Manual.

## Behaviour outside the key range

A curve does not have to stop existing at its first and last key. The **Before** and **After** controls can be configured independently.

### Clamp

Hold the endpoint value forever.

```text
----●~~~~~~~~~~~~
```

Useful for normalized response curves where values outside the authored range should simply use the nearest endpoint.

### Continue

Keep moving along the endpoint tangent. This is the closest option to “keep the tendency”.

If the final key is rising with slope `2`, the curve continues rising at that slope after the key range.

Useful for trends, progression estimates and hand-authored regions that should transition into a linear continuation.

### Repeat

Loop the authored X interval.

```text
/\__/\__/\__
```

Useful for periodic effects, looping animation values and repeated gameplay patterns.

### Ping-pong

Play the curve forward, then backward, then forward again.

```text
/\__  __/\
    \//
```

This avoids a discontinuous jump from the last key back to the first.

### Invert repeat

Repeat the curve, but vertically invert every other cycle around the sum of the first and last endpoint values. This keeps adjacent cycles connected while producing an alternating response.

It is useful for oscillatory authoring where a repeated copy should switch polarity instead of simply replaying.

### Repeat + offset

Repeat the curve while adding the authored endpoint difference on every cycle.

If a curve starts at `0` and ends at `10`, the next cycle runs from `10` to `20`, then `20` to `30`, and so on.

This is particularly useful for staircase-like progression, cyclic motion with accumulated displacement, or curves that describe one repeated “step” of a larger trend.

## What the two vertical dashed lines mean

The dashed lines on the graph mark the first and last authored X values. The blue curve inside them is the Hermite spline you directly authored. Outside them, the selected extrapolation behaviour is being applied.

**Fit** intentionally shows additional space when you choose a cycling/continuing mode so you can inspect that behaviour rather than only the key interval.

## Snapping

Enable **Snap** and set a spacing if you want authored values to land on increments such as:

```text
0.1
1
5
```

## Compact definition vs exact formula

The normal Curve Designer view shows a compact description such as:

```text
Hermite spline · 6 keys · x 0 → 1 · before: Clamp · after: Ping-pong
```

That is the useful human-facing representation of the asset.

The exact mathematical version is still available under **Exact expanded formula**. It contains the literal cubic coefficients and nested `if(...)` calls required to reproduce the spline exactly in Function Lab. For many keys this is necessarily long; the application no longer forces that implementation detail into the main UI.

**Add exact spline to Function Lab** uses the exact expanded expression, including the chosen Before/After behaviour.

## Similar analytic functions

A hand-drawn curve may have a much simpler mathematical description than its exact spline expansion. Curve Designer samples the authored curve and tries several compact function families, including:

- linear,
- quadratic,
- cubic,
- smoothstep,
- smootherstep,
- sine-family functions,
- exponentials,
- normalized power curves,
- logistic/sigmoid curves,
- damped oscillations.

The candidates are ranked by fit error. The displayed percentage is **RMSE divided by the Y range of the authored curve**, so it is scale-independent enough to compare suggestions for very different curves.

The currently selected approximation is drawn as an **orange dashed line** over the exact blue spline. This is important: the app does not silently replace your curve or claim that a simpler function is mathematically identical. You can see the approximation and decide whether the simplification is acceptable.

A low error is useful, but context still matters. A 1% approximation error may be irrelevant for a visual easing curve and unacceptable for a tightly balanced economy formula.

**Add suggestion to Function Lab** adds the compact suggested formula and restricts its domain to the original key range.

## A practical example

Suppose you draw a joystick response by eye:

```text
(0.0, 0.0)
(0.2, 0.03)
(0.5, 0.25)
(0.8, 0.75)
(1.0, 1.0)
```

The exact spline is useful if those exact keys are the design. But Curve Designer may discover that a power curve or sigmoid is visually almost identical. If the approximation error is small enough, the simpler expression can be easier to communicate, port to an engine, expose as parameters and maintain later.

## What survives workspace save/load

A `.graphcalc` workspace stores:

- all curve keys,
- tangent values and tangent modes,
- linked/broken tangent state,
- Before extrapolation mode,
- After extrapolation mode.

The analytic suggestions are recalculated from the authored curve when the workspace is opened, so they do not become stale cached data.

---

# Economy Designer — beginner guide

Economy Designer is easiest to understand if you temporarily forget the word “economy”.

It is a **resource-flow simulator**.

A resource can be almost anything you can count:

```text
Gold
XP
Energy
Wood
Ammo
Reputation
Tokens
Crafting materials
```

You place nodes that create, store, transform or consume those resources, then connect the nodes with flows.

## The simplest useful economy

Imagine a game where quests grant Gold and upgrades cost Gold.

Build:

```text
Quest rewards  ─────→  Wallet  ─────→  Upgrades
    Source              Pool             Sink
```

Set all three nodes to the resource:

```text
Gold
```

Set the first flow Rate to:

```text
10
```

and the second to:

```text
7
```

Press Play.

The Wallet rises by roughly 3 Gold per second because:

```text
10 in - 7 out = +3 net Gold/s
```

If you reverse those numbers, the wallet drains.

That is the basic idea behind the entire Economy Designer. The advanced tools answer more difficult versions of the same question.

## Creating a connection

1. Press **Connect**.
2. Click the origin node.
3. Click the destination node.
4. Select the newly created flow in the Flows list.
5. Edit its Rate and any timing/probability settings.

Sources and Events cannot be flow targets. Sinks cannot start resource flows.

## Formula-driven flows

A Rate can be a fixed number:

```text
10
```

or an expression:

```text
baseincome*(1+growth*t)
```

Unknown identifiers such as `baseincome` and `growth` become Economy parameter sliders.

That lets you tune the model without rewriting the expression.

---

# Economy Designer — the visual language

The diagram is intentionally designed so that you can understand a running economy without opening every inspector.

## Resource colours

Every distinct resource name gets a stable colour.

Common resources have familiar fixed colours where useful; arbitrary custom names receive a deterministic palette colour based on their name.

The legend in the upper-left corner of the economy canvas shows the current mapping.

Node cards have a coloured strip representing their resource.

## Flow colours

A normal same-resource connection uses that resource's colour.

For example, a Gold flow is Gold-coloured from start to finish.

A conversion changes colour halfway along the connection:

```text
Ore ──────── Iron
brown-ish → grey-ish
```

That makes the conversion point visually obvious.

## Moving resource packets

When the simulator accepts a real transfer, it creates a short-lived visual packet train for that transfer.

That distinction matters. The dots are **not generated from a looping decorative animation** and they are not inferred merely because a connection exists. A packet is born only after the simulation has resolved conditions, probability, source starvation, target capacity and competing flows.

The packet then travels continuously from the actual source card to the actual target card. This gives discrete flows enough visual lifetime to be readable instead of flashing into existence for a single simulation step.

For example, a flow configured to activate every two seconds produces a visible burst every two seconds. A continuous faucet produces overlapping trains that read as steady traffic. If the source runs dry, the packets stop because no transfer was accepted.

Packet count, packet size and connection thickness scale logarithmically with transferred amount. This keeps a high-volume currency faucet from visually erasing smaller but still meaningful flows.

For resource-changing connections, such as Ore being smelted into Iron, the moving packet changes resource colour at the conversion point. The small diamond on the connection marks that transition. Yield/efficiency can also make the outgoing packet visually smaller.

## Direction

Every flow has three redundant direction cues on purpose:

- the connection begins at the source card and ends at the target card,
- the target end has a filled arrowhead,
- a smaller chevron appears farther along the connection.

The connection label also includes `Source → Target`. Moving packets always follow that same direction.

This redundancy is useful on dense diagrams where a card, label or crossing connection may partially obscure one of the cues.

## Labels

A connection label can show:

```text
Daily reward · Gold · 25/s · every 1s · 65% · +2s
```

depending on what is configured.

You may also give the flow a human-readable **Label** in the flow inspector.

## Disabled flows

Disabled flows are faded.

## Delayed flows

Flows with a delivery Delay are drawn with a dashed line and show the delay in the label.

## Selected flows

The selected flow gets a soft blue highlight underneath its resource colour so selection does not destroy the resource coding.

## What the diagram is promising you

The visual layer follows one rule: **do not show movement the simulation did not perform**.

A configured line means a route *can* exist. A moving packet means value actually crossed that route. A thick active line means meaningful throughput has recently crossed it. A conversion marker means the resource identity changes on that edge. Dashed geometry means delivery is delayed.

This makes the diagram useful for debugging as well as presentation. If the numbers say a flow transferred but no packet follows the expected direction, that is a renderer bug rather than an intended abstraction.

## Node role colour versus resource colour

Two different visual channels are used on purpose:

- **resource colour** answers “what is moving/stored?”
- **node kind colour/glyph** answers “what does this node do?”

That means two Gold nodes can still look different if one is a Source and one is a Sink.

## History graph

The History tab is an inspection tool rather than a fixed screenshot of every Pool.

Each model node appears as a selectable series. Storage nodes are enabled by default, while Sources, Sinks, Events and other cumulative/state nodes can be enabled when you actually want to inspect them.

Controls above the chart let you choose:

- **window (s)** — how many recent simulation seconds to display; enter `0` for all retained history,
- **Y max** — type a number for a fixed comparison scale or `auto` for data-driven scaling,
- **Capacities** — overlays faint dashed storage-capacity guides,
- **All / None** — quickly enable or disable every available line,
- individual series checkboxes — decide exactly which nodes appear.

The chart uses the same resource colours as the diagram. If several visible nodes use the same resource, later lines use different dash patterns while preserving the resource colour. Axes show actual simulation time and numeric values rather than an unlabeled normalized plot.

History display choices are stored in `.gceconomy` files, so a shared model can reopen with the same analysis view.

---

# Economy node glossary

The Economy Designer now has eight node types. Four are core resource nodes; four provide clearer visual vocabulary for logic, timing and abstract state.

## Source

**Plain language:** a faucet. It creates a resource.

Examples:

- quest Gold,
- passive income,
- enemy drops,
- energy regeneration.

A Source's outgoing flow Rate decides how much is created.

Source nodes do not have finite storage.

## Event

**Plain language:** a Source that communicates “this is an occurrence, not a permanent faucet”.

Examples:

- daily reward,
- mission completion,
- weekend bonus,
- seasonal reward,
- boss kill payout.

Simulation-wise it is source-like. The event behaviour is defined on its outgoing flow using:

- Interval,
- Chance,
- Start/End,
- Condition.

This separate visual type makes diagrams easier to read even though the scheduling rules still live on the flow.

## Pool

**Plain language:** a container.

Examples:

- wallet,
- inventory stack,
- XP bank,
- stamina reserve,
- crafting stockpile.

A Pool has:

- Initial amount,
- current Amount,
- Capacity.

## Gate

**Plain language:** a routing buffer.

Use it when a resource reaches a decision point and should continue down different paths depending on conditions.

Example:

```text
Reward → Gate ─→ Savings      when savings < target
              └→ Auto-upgrade when surplus > threshold
```

The Gate itself stores a transient aggregate amount. Branching behaviour is defined by conditions/chances on its outgoing flows.

**Technical note:** Economy Designer evaluates each simulation step from a snapshot. Resource arriving in a Gate during a step becomes available to its outgoing flows on the following step. It is therefore a routing buffer rather than an instantaneous zero-time logic wire.

## Queue

**Plain language:** a holding stage for paced release.

Examples:

- crafting queue,
- reward inbox,
- build queue,
- replenishment pipeline.

Queue is aggregate storage. Pace its outgoing resource using Interval and/or Delay on the outgoing flow.

**Important:** this is not a per-item FIFO queue with individual item timestamps. It models the total quantity waiting in that stage.

## Register

**Plain language:** a numeric state container.

Registers default to the resource name `Value` and are useful for abstract quantities such as:

- reputation,
- threat,
- pressure,
- pity counters,
- score-like state.

A Register uses the same storage mechanics as a Pool, which means other formulas can reason about the current `source`/`target` amount when connected to it.

## Converter

**Plain language:** a processing stage where one resource becomes another.

Example:

```text
Ore → Smelter → Iron
```

The conversion flow's **Yield** controls how much target resource appears for each source unit consumed.

A Yield of:

```text
1.0
```

means one output unit for each input unit.

A Yield of:

```text
0.65
```

means 100 consumed input units deliver 65 output units.

Converters also provide storage, so they can represent processing buffers.

## Sink

**Plain language:** a drain. It removes a resource from the active economy and records cumulative consumption.

Examples:

- shop purchases,
- upgrades,
- repair costs,
- crafting costs,
- stamina usage.

## Why there is no giant list of highly specific node types

The app deliberately keeps a small set of meaningful primitives and lets flows carry most scheduling/probability behaviour.

For example:

- a cooldown is an Interval on a flow,
- a condition is a flow Condition,
- a delayed transfer is a Delay,
- a timed event is a Start/End window,
- stochastic branching uses Chance or conditional flows,
- a trader/exchange can be modelled with opposite conversion paths.

That avoids having separate “Cooldown”, “Timer”, “Probability”, “Delay” and “Condition” boxes everywhere when a connection property communicates the same idea more clearly.

---

# Economy flows

A flow is the directed connection between two nodes.

## Label

Optional human-readable name shown on the diagram.

Examples:

```text
Quest payout
Daily upkeep
Smelt ore
Upgrade purchase
```

## Rate

The amount requested per simulation second.

Examples:

```text
10
```

```text
baseincome
```

```text
baseincome*(1+growth*t)
```

```text
max(0,basespend+pressure*source)
```

## Condition

The flow only activates when the Condition expression is greater than zero.

Examples:

```text
source>0
```

```text
target<500
```

```text
and(source>surplus,target<targetcap)
```

Default:

```text
1
```

which means “always allowed”.

## Yield

The fraction/multiplier delivered to the target after the source amount is taken.

Examples:

```text
1
```

100 taken → 100 delivered.

```text
0.5
```

100 taken → 50 delivered.

Values above 1 are allowed when the conversion intentionally produces more output units than consumed input units.

## Chance

Probability per activation, from `0` to `1`.

Examples:

```text
1.0 = always
0.5 = 50%
0.1 = 10%
```

## Interval

`0` means continuous flow.

A positive value makes the flow fire once every N simulation seconds.

Example:

```text
Interval = 86400
```

could represent a daily event if your simulation time unit is seconds.

## Delay

Delay between source reservation and target delivery.

The source amount is reserved/removed when the flow fires. Delivery occurs later.

If the target is full when a delayed transfer arrives, the undelivered remainder waits instead of disappearing.

## Start / End

Optional active time window.

Useful for:

- limited events,
- seasons,
- temporary boosts,
- tutorial phases,
- timed promotions.

Blank/zero End means no end time.

## Enabled

Temporarily disables the flow without deleting its configuration.

---

# Economy expression variables

Flow expressions use the same parser as Function Lab, with extra simulation context.

Built-ins:

```text
t / time    current simulation time
dt          current activation span / step
source      amount at the source node
target      amount at the target node
sourcecap   source capacity
targetcap   target capacity
rand        deterministic uniform random sample in 0..1
gauss       deterministic standard-normal sample
run         prediction run index
cohort      active cohort index
```

Custom names become economy parameters automatically.

Example:

```text
baseReward*(1+levelScale*t)
```

creates `baseReward` and `levelScale` parameters.

---

# Scenarios, cohorts, predictions and balance search

## Scenarios

A Scenario stores the current economy parameter values under a name.

Examples:

```text
Baseline
Patch 1.2
Double-reward weekend
Reduced grind
```

The diagram stays the same. Only parameter values change.

Use a Scenario when you want to compare tuning versions of the same system.

## Cohorts

A Cohort also stores parameter values, but represents a type of player and has a population weight.

Examples:

```text
Casual
Core
High engagement
High spender
```

Possible parameters might include:

```text
sessionsPerDay
winRate
spendRate
questCompletion
```

Cohort weights are used by Population-mix predictions.

## Predictions

One simulation run is only one possible future when randomness exists.

Predictions run the economy repeatedly using deterministic but different seeded runs.

For relevant nodes the report includes:

```text
mean
standard deviation
median
P10
P90
minimum
maximum
starvation rate
capacity-bound rate
```

### What P10 and P90 mean

If Gold at day 30 has:

```text
P10 = 6,200
P90 = 14,800
```

then roughly 80% of the simulated results landed between those two values.

This is usually much more informative than looking only at the mean.

## Population mix

When enabled, each prediction run selects a cohort according to cohort weights.

That lets the prediction represent a mixed population rather than one fictional average player.

## Balance search

Balance Search tries to find parameter values that satisfy target ranges you define.

Example targets:

```text
Gold wallet       8,000 .. 12,000
Premium currency     20 .. 40
Upgrade sink      4,000 .. 7,000
```

Parameter Minimum/Maximum values define the allowed tuning space.

The search:

1. explores that space with low-discrepancy candidate coverage,
2. evaluates candidates against the same stochastic seeds,
3. scores how far results fall outside targets,
4. refines the best region locally,
5. lets you apply the best found configuration.

A score of `0` means all configured targets fell inside their desired ranges for the scoring runs.

This is numerical search, not magic. The quality of the result depends on sensible parameter bounds, targets and enough stochastic samples.

---

# Portable economy files

Use:

```text
Save economy…
```

and:

```text
Open economy…
```

for dedicated `.gceconomy` files.

A `.gceconomy` file is readable JSON and is intended to be sent to someone else or archived independently of the rest of the graph workspace.

Current `.gceconomy` format: **v3**. Version 1 and 2 projects still load; version 3 marks the expanded Event/Gate/Queue/Register node vocabulary so an older build cannot silently misread those nodes.

It contains:

- project name,
- description/notes,
- node kinds,
- node positions,
- resource names,
- initial/current amounts,
- capacities,
- flows,
- flow labels,
- expressions,
- conditions,
- Yield,
- Chance,
- Interval,
- Delay,
- schedules,
- parameters and parameter ranges,
- scenarios,
- cohorts and weights,
- balance targets,
- simulation timestep/speed/seed,
- prediction settings,
- current simulation time,
- delayed transfers in flight,
- next scheduled activations,
- per-flow deterministic random state,
- a recent history window.

That means a paused stochastic economy can be sent to another machine and continued materially from the same runtime state rather than merely recreating the same diagram.

Format details are also documented in:

```text
ECONOMY_PROJECT_FORMAT.md
```

The normal `.graphcalc` workspace file stores all three workspaces together. Use `.gceconomy` when the economy model itself is the thing you want to share.

---

# Technical simulation rules

This section describes the rules precisely enough to reason about unexpected results.

## Fixed-step simulation

`dt` controls the simulation step.

Smaller values provide finer temporal resolution but require more work.

Playback speed controls how quickly simulation time advances relative to real time; it does not redefine the mathematical rates.

## Snapshot evaluation

Each step begins from a snapshot of node amounts.

Eligible flows calculate their requests from that snapshot.

This prevents UI/list order from silently becoming a hidden priority system.

## Competing outgoing flows

If several flows request more resource from one finite storage node than is available, the requests are scaled proportionally.

Example:

```text
Pool contains 60
Flow A requests 60
Flow B requests 60
```

Neither simply “wins because it appears first”. Both are scaled to share the available 60.

## Competing incoming flows

Immediate flows competing for limited target capacity are also scaled proportionally.

## Continuous flows

`Interval = 0` means continuous flow.

The requested amount is approximately:

```text
rate * activeSpan
```

for the part of the step in which the flow is active.

## Interval flows

Positive Interval values create discrete activations.

If one simulation step crosses several scheduled activations, the engine catches up every crossed activation instead of silently dropping events.

## Randomness

Each flow has its own deterministic random stream derived from:

- the project seed,
- that flow's stable ID.

Adding an unrelated stochastic flow therefore does not reshuffle the random sequence of every existing flow.

With the same project state, seed and parameters, a reset/replay is reproducible.

## Delays

Delayed transfers are stored explicitly in transit.

If they become due inside a larger simulation step, they are delivered at that step boundary rather than being pushed one whole extra `dt` into the future.

If a finite target cannot receive all of a delayed transfer, the remainder stays pending.

## Gate timing

Gate is intentionally an aggregate routing buffer. Incoming resource from the current snapshot becomes outgoing-eligible on a subsequent simulation step.

If you need the delay to be visually negligible, use an appropriately small `dt`.

## Queue timing

Queue does not track individual items. It tracks aggregate amount.

Use outgoing flow Interval/Delay/Condition rules to model release behaviour.

## Model audit

The live diagnostics include a structural audit that can flag problems such as:

- invalid expressions,
- missing or semantically invalid flow endpoints,
- suspicious self-loops,
- cross-resource conversion without a Converter,
- Sources/Events with no outgoing flow,
- Sinks with no incoming flow,
- incomplete Converters,
- incomplete Gates/Queues,
- disconnected Registers,
- invalid capacities,
- stale Scenario/Cohort parameter overrides,
- balance targets pointing at removed nodes.

Predictions refuse malformed enabled flows rather than silently evaluating only part of the model.

---

# Analysis, inspection and comparison

## 2D derivative

Hovering a normal 2D scalar function reports its value and an estimated `dy/dx`.

**Derivative overlay** draws the numerical derivative as a dashed curve.

## Numerical analysis

Select expression **A** and run **Analyze A**.

Within the current view/domain the app searches numerically for:

- roots,
- local extrema,
- intersections with expression **B**.

This is sampled numerical analysis, not symbolic algebra. Extremely narrow features smaller than the sampling interval can be missed.

## Function comparison

The comparison overlay can show:

```text
A - B
|A - B|
A / B
```

This is useful when comparing approximations, response curves or tuning variants.

## 3D display modes

Available modes include:

- Solid
- Wireframe
- Solid + wire
- Height bands
- Slope
- Normals

## 3D probe

Right-click a rendered surface to inspect a point.

For `z=f(x,y)` height fields, the probe reports numerical partial derivatives, slope and a normal.

For parametric/implicit geometry it can use the rendered triangle normal where an explicit height-field derivative is not available.

## Cross-section

Enable Cross-section, choose a scalar height field, choose X or Y and enter a fixed coordinate.

The slice is shown both through the 3D scene and as a small 2D profile.

---

# Presets

The Function Lab preset library includes examples for:

- gameplay progression,
- difficulty curves,
- easing,
- technical art,
- VFX,
- timeline animation,
- procedural noise,
- texture fields,
- vector fields,
- classic 3D height fields,
- parametric curves,
- parametric surfaces,
- piecewise functions,
- implicit/SDF geometry.

There are also multi-expression presets, including:

- enemy archetype scaling,
- economy faucet/sink pressure,
- recoil-response families,
- interpolation comparisons.

**Adding a preset appends expressions. It does not clear your current expressions.**

The preset may adjust the suggested view so the newly added content is visible.

Economy Designer separately includes starter models such as:

- Faucet / sink balance
- Inflation pressure
- Two-currency live service
- Random loot loop
- Seasonal event economy
- Crafting conversion chain
- Branching reward router

Loading an Economy example replaces the current Economy model after confirmation because it is a complete diagram rather than a Function Lab expression snippet.

---

# Save/load

## Full workspace

**Save** / **Load** use `.graphcalc` files.

A workspace can contain:

- Function Lab expressions,
- domains,
- graph view,
- camera state,
- parameters,
- groups/units/locks,
- parameter animations,
- A/B states,
- timeline state,
- derivative/comparison settings,
- field preview settings,
- cross-sections,
- Curve Designer state,
- Economy Designer diagram and settings.

## Economy-only project

Use `.gceconomy` when you specifically want the Economy Designer model to be portable and shareable by itself.

---

# Export and capture

## General sampled CSV

Function Lab CSV export supports compatible visible plots including:

- scalar curves,
- parametric curves,
- vector fields,
- texture fields,
- implicit contours,
- height fields,
- 3D curves,
- parametric surfaces,
- implicit 3D surfaces.

Common columns:

```text
plot,kind,sample,x,y,z,vx,vy
```

## Economy CSV

Economy Designer exports the stored-node history over simulation time for external analysis in tools such as Excel, Python or R.

## Unity AnimationCurve C#

Exports sampled `Keyframe` construction for a compatible 1D scalar curve.

## 1D LUT PNG

Exports a 256×1 grayscale lookup texture.

## Engine samples JSON

Stores an expression, parameter values and sampled points in engine-friendly JSON.

## Curve samples CSV

Simple `Time,Value` output.

## HLSL function

Compatible scalar/field expressions can be translated into a small HLSL-style function with helpers for operations such as remapping, smootherstep, ping-pong, noise and FBM.

Special geometry wrappers are not exported as geometry by HLSL export; their compatible underlying scalar expression is used where appropriate.

## PNG

Captures the current graph area.

## GIF

Records one deterministic pass across the configured global timeline. Independent parameter auto-animation is temporarily paused during capture so the result is driven predictably by `t`.

## Texture export

The active 2D scalar field can be exported as a 512×512 PNG over its active domain.

---

# Expression language reference

## Constants and coordinates

Common names include:

```text
pi
π
e
x
y
z
t
u
v
```

Availability depends on the plot type.

## Shorthand

The parser accepts convenient forms such as:

```text
2x
2pi
(x+1)(x-1)
1e-6
```

Ordinary explicit plots may optionally use:

```text
y = ...
z = ...
```

## Unary functions

```text
abs
ceil
floor
round
sign
frac
saturate
sqrt
sin
cos
tan
asin
acos
atan
ln
log
exp
not
```

## Two-argument functions

```text
pow
min
max
mod
step
atan2
repeat
pingpong
noise
and
or
bernoulli
```

## Three-or-more argument helpers

```text
clamp(value,min,max)
lerp(a,b,t)
inverselerp(a,b,value)
smoothstep(edge0,edge1,value)
smootherstep(edge0,edge1,value)
if(condition,a,b)
select(condition,a,b)
noiseseed(x,y,seed)
uniform(min,max,r)
normal(mean,stddev,g)
remap(inMin,inMax,outMin,outMax,value)
fbm(x,y,octaves,persistence,lacunarity)
```

`noise` is deterministic 2D value noise in roughly `-1..1`.

`fbm` layers noise octaves for quick procedural experiments. It is not intended to reproduce a particular engine's noise implementation bit-for-bit.

## Function suggestions

Press **Ctrl+Space** while editing an expression to open function suggestions.

## Inline errors

Expression errors appear underneath the relevant entry instead of silently failing.

---

# Render quality

Sampling presets:

- **Draft** — fastest iteration and capture.
- **Normal** — default interactive quality.
- **High** — denser curves/meshes/contours.
- **Ultra** — final inspection; implicit 3D surfaces can become much heavier.

Quality is saved with the workspace.

---

# Common workflows

## Gameplay damage scaling

Function Lab:

```text
baseDamage*(1+growth*x)
```

or a diminishing curve:

```text
maxDamage*x/(x+halfPoint)
```

Use live sliders for `growth`, `maxDamage` and `halfPoint`.

## Visually author joystick response

Curve Designer:

```text
(0,0)
(0.2,0.03)
(0.5,0.25)
(0.8,0.75)
(1,1)
```

Tune tangents until it feels right, then Add to Function Lab and export as an AnimationCurve/LUT.

## VFX shockwave

Function Lab 3D:

```text
exp(-3*(sqrt(x^2+y^2)-radius)^2)
```

Animate `radius` or replace it with time.

## Procedural terrain prototype

```text
amplitude*fbm(x*frequency,y*frequency,octaves,persistence,lacunarity)
```

Use 3D surface view and then inspect the same field as a texture.

## Currency inflation test

Economy Designer:

```text
Source → Gold Pool → Sink
```

Income:

```text
baseincome*(1+growth*t)
```

Spend:

```text
basespend+0.02*source
```

Run the live model, then Predictions to see how randomness/cohorts spread outcomes.

## Crafting conversion

```text
Ore Source → Ore Pool → Converter(Iron) → Iron Pool → Crafting Sink
```

The diagram visibly changes flow colour at the conversion.

## Limited event

Use an Event node with an outgoing flow configured with:

```text
Interval = 1
Start = 7
End = 14
```

The event exists only during that simulation window.

---

# Common mistakes and troubleshooting

## “My economy does nothing”

Check:

1. Is the flow Enabled?
2. Is its Rate greater than zero?
3. Is the Condition greater than zero?
4. Is the flow inside Start/End time?
5. Did a Chance roll fail?
6. Is the Source/Pool empty?
7. Is the target at Capacity?
8. Is the flow expression showing an error?

The flow status and moving packets are useful clues.

## “I connected two resources directly and got an audit warning”

The tool allows the sketch, but expects intentional cross-resource conversion to go through a Converter so the diagram communicates the change clearly.

## “Why does my Gate not forward something in the exact same simulation instant?”

Gate is a routing buffer under snapshot-based step simulation. Incoming amount becomes visible to outgoing Gate flows on a later simulation step.

Reduce `dt` if that small discrete delay matters to your model.

## “Queue is not behaving like an individual-item queue”

Correct. Queue is aggregate storage. It does not remember the age of each individual unit.

Use flow Delay/Interval for aggregate timing.

## “A preset erased my functions”

Function Lab presets should append. Economy examples are complete models and therefore ask before replacing the current Economy diagram.

## “My 3D sphere has holes / jagged domain edges”

Restricted scalar surfaces use boundary refinement, but very low render quality can still show coarse tessellation. Increase render quality for final inspection.

## “Prediction results vary from live play”

Predictions deliberately run many stochastic outcomes. With a fixed seed and identical model state they are reproducible, but the reported distribution is not supposed to equal one single live path.

## “Balance Search found a weird solution”

Narrow parameter ranges to values you would genuinely ship, add more targets, increase candidate count, and make sure the targets actually constrain the behaviour you care about.

---

# Scope and limitations

Graph Calculator is deliberately a **numerical prototyping and systems-design tool**, not a computer algebra system.

It does not try to replace:

- Mathematica / Maple / a full CAS,
- a spreadsheet,
- a dedicated statistics package,
- a full shader editor,
- a production telemetry platform,
- a full discrete-event manufacturing simulator.

Economy Designer is also intentionally aggregate. A Pool contains an amount, not ten thousand individually tracked player-owned item objects. Queue is aggregate rather than per-item FIFO. Gate is a buffered routing primitive. Those choices keep local simulation fast enough for repeated predictions and balance search.

The intended workflow is:

```text
Function Lab
    ↓
understand and prototype mathematical behaviour

Curve Designer
    ↓
author behaviour visually when the equation is not the natural starting point

Economy Designer
    ↓
compose gameplay-facing behaviours into a resource system, run it, stress it and tune it
```

Everything stays local. No account or cloud service is required to create, run, save or share the models.

---

# Integrated systems workflow

The current build treats Function Lab, Curve Designer and Economy Designer as parts of one project rather than three unrelated utilities.

## Shared project assets

A useful behaviour can be published once and reused elsewhere.

For example, draw a progression curve in Curve Designer and publish it as:

```text
EnemyHealthCurve(x)
```

Then Function Lab can use:

```text
100 * EnemyHealthCurve(level)
```

and an economy formula can use the same published behaviour in a rate or condition.

Function expressions, published curves and imported two-column CSV tables all live in the **Shared project assets** area. Asset names are callable like functions. Table assets use linear interpolation between imported rows and clamp beyond the table ends.

This is the intended project-level workflow:

```text
Curve Designer -> publish authored behaviour
                    |
Function Lab  <-----+-----> Economy Designer
```

A shared asset is not a copy/paste macro. The expression is expanded when consumers compile, so updating the asset updates the places that call it.

## Multi-channel Curve Designer

Curve Designer can hold several synchronized channels in one curve workspace. Typical uses include:

- recoil pitch + recoil yaw;
- RGB or material channels;
- X/Y/Z motion curves;
- several balancing outputs that share the same input axis.

Use the channel selector to create, rename or switch channels. Inactive visible channels remain drawn behind the active channel for comparison.

### Auto tension and tangent weight

`Tension` affects automatically generated tangents. At low tension the curve preserves more momentum through keys; higher values flatten automatic tangents and make transitions tighter.

Manual/auto tangents also expose **In W** and **Out W** weights. A weight of `1` is the ordinary Hermite tangent. Lower values reduce that tangent's influence; higher values strengthen it without requiring the numerical slope itself to be rewritten.

Curve extrapolation still applies independently before and after the authored key range: Clamp, Continue, Repeat, Ping-pong, Invert repeat and Repeat + offset.

## Undo, autosave and recovery

Undo/redo is workspace-aware. It covers ordinary expressions as well as authored curve/economy structure, shared assets, groups and recipes.

Useful shortcuts include:

```text
Ctrl+Z   Undo
Ctrl+Y   Redo
Ctrl+S   Save the current workspace
```

Unsaved authored changes are marked at the top of the window. A lightweight recovery snapshot is written after editing settles, so an interrupted session can be restored from **Recovery** on the next run.

Recent workspace paths are kept locally and appear in the recent-file selector. No account or online service is involved.

## Imported design tables

Use **Import CSV table** in Shared project assets for real production data such as:

```text
level,xp_required
1,100
2,160
3,250
...
```

The importer expects two numeric columns; header/non-numeric rows are ignored. The result becomes a callable interpolated asset. This is useful for XP tables, price tables, drop-rate curves and any other data that is easier to own in a spreadsheet but useful inside simulation expressions.

## Economy canvas editing

The Economy Designer canvas supports Ctrl+click multi-selection plus common layout operations:

- Group into a subsystem;
- Collapse/expand subsystem;
- Duplicate;
- Copy / paste (`Ctrl+C`, `Ctrl+V`);
- Select all;
- Align horizontally/vertically;
- Distribute;
- Snap to a configurable grid;
- Delete the whole selected set.

A collapsed subsystem behaves as a visual abstraction of its member nodes. Internal links disappear, while flows crossing the subsystem boundary remain visible and connect to the collapsed group. Typed coloured ports summarize resources entering and leaving the subsystem.

Subsystems are deliberately organisational. Collapsing one does not replace its internal simulation with a different approximation.

## Typed resource ports and resource presentation

Economy nodes show typed input/output ports where appropriate. Resource identity also drives flow colour, moving packets and the history chart.

Resource appearance is editable. A resource can have its own:

```text
colour
icon
unit
```

Examples:

```text
Gold   coin icon   coins
Energy lightning   energy
XP     star        XP
```

These are presentation hints, not hidden conversion rules.

## Multi-resource Converter recipes

Ordinary flows are useful for continuous movement. Crafting often needs atomic recipes instead.

Select a Converter connected to its ingredient and result nodes, then build a recipe. A recipe can represent:

```text
30 Wood + 10 Iron + 200 Gold -> 1 Sword
```

The recipe checks all required inputs and output capacity first. Only the number of complete crafts that can actually happen is executed. It will not consume Wood if there is not enough Iron for the same craft.

The old connection lines remain useful as the visual recipe paths: input packets travel into the Converter and output packets leave it when real crafting occurs.

`crafts/s` can be a normal expression and may reference economy parameters and published shared functions.

## Player Action nodes

Not every economy event should happen automatically. An **Action** node represents an explicit decision such as:

```text
Buy item
Claim reward
Craft
Open chest
Enter dungeon
```

Select an Action node and press **Trigger selected Action**. Its outgoing flows are evaluated for that simulation step. This makes interactive decisions visible in the model instead of hiding them inside arbitrary conditions.

## Economy replay

After a run has produced history, **Replay** lets the history slider move the visible economy back through sampled states. This is an inspection mode: it is intended for understanding *when* a system diverged, starved or filled rather than pretending to reverse the random generator and pending-event queue.

Return to live mode before continuing the simulation.

## Simulation debugger

The Debugger tab records accepted transfers and can inspect blocked activations for the selected/breakpoint flow. Entries include the requested, accepted and delivered amounts plus condition/chance information.

Use **Break on selected flow** when a particular transfer is difficult to understand. The simulation pauses when that flow successfully activates, making it easier to inspect its source, target, parameters and surrounding system state.

The deterministic project seed makes the same stochastic run reproducible when the model and inputs are unchanged.

## Sensitivity analysis

Sensitivity analysis answers a different question from optimization:

> Which tuning parameters actually matter to this outcome?

Choose a target node and run the analysis. Each free parameter is perturbed by a small fraction of its legal range using a common random seed. The report ranks parameters by approximate output change per parameter unit.

A large magnitude means the selected outcome is sensitive to that parameter. A tiny magnitude is a hint that changing that parameter may have little effect in the current model and time horizon.

This is a local numerical analysis, not proof that the relationship stays linear across the whole parameter range.

## Balance search and Pareto alternatives

Balance Search searches legal parameter ranges against the configured targets. Candidate configurations are evaluated with common stochastic seeds, which reduces the chance that a lucky random run wins the search.

When several targets compete, the report also lists **Pareto alternatives**. A Pareto candidate is useful because improving one target would require worsening at least one other target among the sampled configurations.

This is preferable to pretending that a single scalar score always represents the only valid design. Use the best-score result when you truly have one combined objective; inspect Pareto alternatives when the economy contains meaningful trade-offs such as:

```text
less grind
vs
slower inflation
vs
higher sink usage
```

## Polar, cylindrical and spherical shorthand

Function Lab also recognizes convenient coordinate-system shorthand:

```text
polar(r)
cyl(radius,z)
sphere(r)
```

These expand to parametric representations using the existing `t`, `u` and `v` domains. They are conveniences, not separate renderers, so all the normal parametric inspection/export behaviour still applies.

## Additional engine export

Alongside CSV, LUT, JSON, HLSL and Unity-oriented exports, the engine export menu can emit:

- a Godot-style Curve resource (`.tres`);
- Unreal CurveTable-style CSV data.

The exports are intentionally data-oriented. Always inspect the generated asset before committing it to a production pipeline, especially when the authored curve uses extrapolation modes that a target engine represents differently.

## Economy project files versus full workspaces

Use `.graphcalc` when you want the whole integrated project: functions, curve channels, shared assets and economy model together.

Use `.gceconomy` when you want to send only the Economy Designer model and its portable simulation state.

That distinction is intentional:

```text
.graphcalc    complete systems-design workspace
.gceconomy   portable economy model
```

Both are local readable JSON formats.
