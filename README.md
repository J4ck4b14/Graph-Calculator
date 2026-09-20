# Graph Calculator

A WPF graphing calculator built for .NET 8. It supports ordinary 2D function plots and 3D surfaces.

## 2D plotting

Choose **2D** in the graph toolbar and enter an expression such as:

- `sin(x)`
- `x^2 - 4`
- `y = sqrt(x)`
- `(x+1)(x-1)`

Use the mouse wheel over the graph to zoom and drag to pan. **Fit graph** adjusts the Y range to the visible functions while keeping the current X range. **Reset** returns both axes to `-10..10`.

## 3D plotting

Choose **3D** in the graph toolbar. Expressions are treated as surfaces in the form `z = f(x, y)`. For example:

- `z = sin(sqrt(x^2+y^2))`
- `x^2-y^2`
- `sin(x)cos(y)`
- `sqrt(25-x^2-y^2)`

Drag the surface to orbit the camera and use the mouse wheel to zoom. The X, Y and Z range fields above the plot control the visible volume. **Fit graph** keeps the current X/Y domain and fits the Z range to the visible surfaces. **Reset** restores the default `-10..10` range and camera angle.

Several expressions can be shown at once in either mode. Their visibility checkboxes and colours carry over when switching between 2D and 3D.

## Calculator and expression syntax

An expression without variables is evaluated as a normal calculator expression and its result is shown below the input.

The parser accepts normal operators, common functions and shorthand such as `2x`, `2pi`, `(x+1)(x-1)` and scientific notation such as `1e-6`. Supported functions include `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `sqrt`, `ln`, `log`, `exp`, `abs`, `floor`, `ceil`, `pow`, `min` and `max`.

## Build

Open `GraphCalculator.sln` in Visual Studio 2022 with the **.NET 8 desktop development** workload installed, then build or run the `GraphCalculator` project.
