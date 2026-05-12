# Agent Guidance

This repository is public. Keep the package clean, dependency-light, and suitable for external consumers, while avoiding greenfield design choices that make Guida integration unnecessarily difficult.

## Guida Compatibility

- Treat Guida integration as a primary compatibility constraint, but write public code and docs as public SDK material.
- Do not mention, describe, or depend on non-public Guida implementation details in this repository.
- Before introducing or reshaping runtime, host capability, engine, HTTP, workspace, document, secret, queue, worker, or workflow APIs, consider whether the public abstraction remains straightforward for Guida to adapt.
- Prefer small public contracts that preserve proven integration patterns over novel abstractions that would force downstream consumers to re-architect.
- When a security-sensitive pattern is already established by consumers, preserve the pattern at the public abstraction boundary. In particular, secrets used in HTTP headers should be bound and injected host-side, not exposed to scripts as raw values.

## Public Core Constraints

- Keep `Guida.Scripting` dependency-free unless a dependency is deliberately approved.
- Use .NET BCL types when they are already the standard abstraction, such as `HttpRequestMessage` and `HttpResponseMessage` for HTTP.
- Avoid reinventing mature platform concepts unless the SDK needs a host boundary, policy model, or script-safe result/error model.
- Model unavailable or denied host capabilities explicitly through result/error types rather than null-reference failures.
- Do not add `#include`, source concatenation preprocessing, generic module rewriting, or pragma parsing unless the roadmap is deliberately changed.

## Validation

- Use `-m:1` for solution-level restore, build, and test commands in this repo.
- Keep the standard validation sequence passing:
  - `dotnet restore Guida.Scripting.sln -m:1`
  - `dotnet build Guida.Scripting.sln -c Debug --no-restore -m:1`
  - `dotnet test Guida.Scripting.sln -c Debug --no-build -m:1 --collect:"XPlat Code Coverage"`
  - `dotnet pack src/Guida.Scripting/Guida.Scripting.csproj -c Release`
  - `dotnet format Guida.Scripting.sln --verify-no-changes --verbosity minimal`
