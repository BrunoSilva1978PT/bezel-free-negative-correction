# Project Rules

These rules apply to every contribution in this repository.

## Language

- **All source code, inline comments, XML/JSDoc/XMLDoc, identifiers, log messages, error strings, commit messages, PR titles/descriptions, issue content, README, and any other file committed to the repository MUST be written in English.**
- Conversational discussion with the project owner happens in Portuguese, but nothing in Portuguese ever lands in committed files.

## Code organization

- Favor modular code: small, focused files with a single clear responsibility.
- Prefer many small files over a few large ones. Group by feature, not by type, when it helps readability.
- Public APIs between modules must be explicit. Avoid leaking implementation details across module boundaries.
- Each module should be independently understandable — a reader should not need to open three other files to understand one.
- Apply standard performance hygiene: avoid needless allocations in hot paths, prefer streaming/zero-copy where practical, do not do work that can be cached or precomputed.
- Do not over-engineer. Only add abstractions, layers, or configuration knobs when a concrete second use case demands them.

## Research before implementation

- Before writing non-trivial code, verify assumptions against authoritative sources: official API documentation, the actual headers/SDKs in use, and existing reference implementations.
- When touching Windows APIs (IddCx, DXGI, D3D11/12, COM wallpaper APIs, display enumeration, etc.), confirm the exact signatures, threading rules, and lifetime semantics in Microsoft Learn or the Windows SDK — do not rely on memory.
- When a design choice has multiple plausible approaches, evaluate them briefly and pick the one that matches actual constraints, not the one that sounds cleanest in the abstract.

## Commits and pushes

- Commit messages must describe *what* changed and *why*, in English, in the imperative mood.
- **Commits and pushes must never mention or attribute authorship to Claude, to any AI tool, or to any assistant.** No `Co-Authored-By` lines referring to AI, no "Generated with" footers, no emoji markers, no trailers of any kind tied to AI tooling.
- The author of every commit is the project owner. Messages should read as if written by the project owner alone.
- Never push directly to `main` without the project owner's explicit request. Default to feature branches.
- Never force-push to shared branches.

## Testing and verification

- Before claiming a feature works, exercise it on the actual target hardware/OS configuration (Windows 10/11, triple-monitor setup with or without NVIDIA Surround).
- For UI behavior, visual verification on the real monitors is required — unit tests alone do not prove that bezel correction looks right.

## Scope

- Changes should be scoped to the task at hand. Out-of-scope cleanups go into separate commits or separate tasks, never bundled.
