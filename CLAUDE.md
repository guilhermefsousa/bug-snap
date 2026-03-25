# BugSnap — CLAUDE.md

## What is this

Blazor Razor Class Library (RCL) for structured bug reporting. Auto-captures context (route, browser, HTTP history, JS errors, correlation IDs), sanitizes sensitive data client-side, shows preview to user, and sends to pluggable destinations.

**Core principle:** BugSnap is capture + sanitize + send. It knows nothing about any specific app, domain, or automation pipeline.

## Stack

- .NET 9, Razor Class Library
- Blazor WebAssembly (primary target, also works with Blazor Server)
- System.Text.Json (no extra NuGet dependencies in core)
- Vanilla JS for interop (no external JS libs)
- xUnit + bUnit for tests

## Project Structure

```
bug-snap/
├── CLAUDE.md
├── SPEC.md                           ← Full specification (read this first)
├── BugSnap.sln
├── src/
│   └── BugSnap/                      ← Core RCL (capture + sanitize + preview + send)
│       ├── Models/
│       ├── Services/
│       ├── Destinations/             ← WebhookDestination, ConsoleDestination
│       ├── Components/
│       ├── wwwroot/
│       ├── BugSnapOptions.cs
│       └── Extensions/
└── tests/
    └── BugSnap.Tests/                ← xUnit + bUnit
```

## Ecosystem (separate packages, NOT in this repo yet)

```
BugSnap.Destinations.GitHub           ← GitHub Issue destination (future separate package)
App-specific adapters                 ← IBugContextProvider implementations (live in consuming apps)
Automation pipeline                   ← GitHub Actions / CI (lives outside BugSnap entirely)
```

## Rules

1. **No app-specific knowledge in core** — zero references to any consuming app's domain (no TenantId, no ConversationId, etc.)
2. **No external JS dependencies** — vanilla JS only
3. **No heavy NuGet dependencies** — System.Text.Json only (built-in)
4. **No server-side requirements** — pure client-side library
5. **Sanitization is mandatory** — runs before any destination, not optional
6. **English code** — class names, properties, methods in English
7. **Tests required** — every public API must have tests
8. **Schema versioned** — payload includes schemaVersion + sdkVersion

## Build & Test

```bash
dotnet build src/BugSnap/BugSnap.csproj
dotnet test tests/BugSnap.Tests/BugSnap.Tests.csproj
```

## Key Design Decisions

- **Webhook is primary destination** — GitHub is a separate package
- **HttpActivityTracker** is a DelegatingHandler (standard .NET pattern)
- **PayloadSanitizer** masks auth headers, tokens, cookies client-side before send
- **IBugContextProvider** is optional — apps implement it for domain-specific context
- **IBugReportDestination** is the extensibility point for where reports go
- **Preview panel** is mandatory — user sees sanitized payload before confirming
- **Category is an enum** (UI, API, Auth, Performance, SignalR, Data, Integration, Other)
- **Correlation IDs** (TraceId, CorrelationId) extracted from HTTP response headers
- Components use scoped CSS + CSS variables for customization
- Ring buffer is a simple custom implementation (no external lib)
