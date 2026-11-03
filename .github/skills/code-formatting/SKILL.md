---
name: code-formatting
description: "Use whenever creating, generating, editing, refactoring, or formatting code in this repository. Apply C# braces, multiline argument lists, named arguments, intermediate variables, and .editorconfig formatting; verify changed code before completion."
---

# Code formatting

## Scope

Follow these rules for every new or changed C# statement, including C# in
Razor files. For other languages, follow existing language-specific conventions.
Do not rewrite unrelated code, generated files, engine assets, or public APIs
solely for formatting.

The root `.editorconfig` defines whitespace: tabs with width 4, CRLF, a final
newline, and opening braces on their own lines. This skill adds readability
rules that the standard formatter does not fully enforce.

## Rules

### Braces and statement layout

- Always use `{ }` blocks for `if`, `else`, loops, and other control-flow
  constructs that accept a block, even for a single statement.
- An `else if` chain is allowed; each conditional body must have braces.
- Put opening and closing braces on separate lines. Do not put multiple
  statements or a whole block on one line.
- Prefer block-bodied methods and local functions. Keep simple expression-bodied
  accessors and properties where they fit the existing `.editorconfig`.
- Do not replace clear conditional blocks with nested ternaries to save lines.

```csharp
if (isReady)
{
	StartRound();
}
else
{
	ResetRound();
}
```

### Intermediate values

- Prefer a descriptively named local variable over passing a value-returning
  function call directly into another call.
- Pass the computed value, not the function or a delegate, unless the receiving
  API actually expects a callback.
- Prioritize extraction for nontrivial, nested, or side-effecting calls. Avoid
  pointless temporary variables for literals, constants, and simple property access.
- Preserve evaluation order, call count, short-circuit behavior, null-conditional
  behavior, and deferred execution. Do not hoist work outside its original branch
  or lambda, or out of a call that might be skipped.

```csharp
var spawnPosition = FindSpawnPosition();
SpawnEnemy(position: spawnPosition);
```

### Argument lists and names

- With **3 or more arguments**, put every argument on its own indented line,
  with the closing `);` on its own line. Apply this to method calls, constructor
  calls, and constructor initializers. For declarations with 3 or more parameters,
  put each parameter on its own line too.
- Prefer named arguments over positional ones whenever the API permits it,
  especially for booleans, numeric values, and ambiguous parameters.
- With **4 or more arguments**, use named arguments for every argument whose
  syntax/API permits naming. Three arguments still require multiline layout.
- Use the actual declared parameter names; inspect the signature rather than
  inventing names. Preserve overload selection and argument evaluation order.
- Respect APIs that do not support naming (for example, individual expanded
  `params` elements). Do not change the API or construct an extra array just
  to satisfy this preference. Explain necessary exceptions in the task summary,
  not with boilerplate code comments.

```csharp
SpawnEnemy(
	position: spawnPosition,
	health: startingHealth,
	speed: movementSpeed
);
```

### C# equivalent of a TypeScript options object

C# named arguments use `Method(a: foo, b: baz)`; they do not pass an object.
For an API that genuinely benefits from grouped settings, C# can instead accept
a typed options class or record and use an object initializer. Unlike a
TypeScript object literal, `new { ... }` creates an anonymous type and is not a
substitute for an ordinary typed parameter.

Illustrative syntax (not an existing game API):

```csharp
public sealed class SpawnOptions
{
	public int Count { get; init; }
	public float Delay { get; init; }
	public bool IsElite { get; init; }
}

var options = new SpawnOptions
{
	Count = 3,
	Delay = 0.5f,
	IsElite = false
};

SpawnEnemies(options: options);
```

- Prefer named arguments for existing APIs. Do not introduce options types solely
  because a call has three arguments.
- Consider a typed options object for new APIs with many related or optional
  settings; reuse an existing suitable type first.
- Use explicit types and appropriate validation/defaults. Do not use `dynamic`,
  dictionaries, or anonymous objects to imitate TypeScript's structural typing.
- Do not change engine APIs, serialization shapes, or existing public signatures
  without a functional requirement.

## Workflow and validation

1. Inspect `.editorconfig`, nearby code, the relevant method signatures, and the
   worktree before editing. Apply the rules above while generating code.
2. Format only the C# files created or changed by the task. From the repository
   root, replace the example paths below with the actual task files:

   ```powershell
   dotnet format whitespace . --folder --include "Code\ChangedFile.cs" "Editor\ChangedFile.cs"
   dotnet format whitespace . --folder --include "Code\ChangedFile.cs" "Editor\ChangedFile.cs" --verify-no-changes
   ```

   Never omit `--include` or supply an empty file list: that would format the
   whole repository. Folder mode avoids requiring the engine-specific projects
   to restore merely to format whitespace.

3. Review the diff manually for all of these:
   - Every conditional/loop body has braces and no one-line blocks.
   - Calls and declarations with 3 or more arguments/parameters are multiline.
   - Named arguments match real signatures; calls with 4 or more arguments use
     names wherever legal.
   - Nested value-returning calls use meaningful locals where appropriate,
     without changing evaluation semantics.
   - There are no unrelated edits or accidental API/behavior changes.
4. Run the smallest existing compilation/tests covering any functional code
   changes. Report formatter, compilation, or test failures explicitly.

`dotnet format whitespace` handles whitespace, not insertion of missing braces,
argument-count wrapping, named arguments, or extraction of locals. Missing braces
are flagged by `IDE0011` when the project's code-style analyzer runs; the
`.editorconfig` warning is not itself a build/CI gate. Where the project can load,
a targeted `dotnet format style <project> --diagnostics IDE0011 --severity warn
--include <changed-files> --no-restore` can fix braces, followed by whitespace
formatting. Review C# inside Razor manually as well; formatter coverage differs
from standalone C#.

These are shared AI instructions, not deterministic enforcement for every AI
client. Clients must support repository `AGENTS.md` and/or repository skills.
Guaranteed enforcement of the extra readability rules would require suitable
analyzers and CI checks; do not claim those exist.
