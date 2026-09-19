# Graph Calculator

A small WPF graphing calculator built for .NET 8.

## Controls

- Type an expression to plot it immediately.
- Use the mouse wheel over the graph to zoom.
- Drag the graph to pan.
- **Fit graph** adjusts the Y range to the visible functions while keeping the current X range.
- **Reset** returns the viewport to `-10..10` on both axes.
- The divider between the expression list and graph can be dragged to resize either side.

The parser accepts normal operators and common functions, plus shorthand such as `2x`, `2pi`, `(x+1)(x-1)`, `y = sin(x)` and scientific notation such as `1e-6`.

## Build

Open `GraphCalculator.sln` in Visual Studio 2022 with the .NET 8 desktop workload installed, then build or run the `GraphCalculator` project.
