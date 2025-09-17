## Blazing Pizza (Clean Reference)

Minimal, ready-to-run Blazor (.NET 8) app used as a reference for LLM workshop demos.

### Prerequisites
- **.NET 9 SDK** (recommended) or newer SDK capable of targeting `net9.0`.
- macOS/Linux/Windows terminal access.

If you see HTTPS errors on first run, trust the local dev cert:
```bash
dotnet dev-certs https --trust
```

### How to run
From the repository root:
```bash
dotnet restore
dotnet build
dotnet run --project src/BlazingPizza/BlazingPizza.csproj
```

The server will print the listening URLs (typically `https://localhost:<port>`). Open that URL in your browser.

For hot reload during development:
```bash
dotnet watch --project src/BlazingPizza/BlazingPizza.csproj
```

### What’s included
- Single solution under `src/` with projects:
  - `BlazingPizza` (server + host)
  - `BlazingPizza.Client` (WASM referenced by server)
  - `BlazingPizza.ComponentsLibrary`
  - `BlazingPizza.Shared`
- SQLite database auto-creates on first run with sample data (`Data Source=pizza.db`).

### Sign-in / Accounts
- You can register a user from the app UI. Email sending is disabled in dev.
- If your environment enforces email confirmation and blocks sign-in, set `RequireConfirmedAccount = false` in `src/BlazingPizza/Program.cs` and re-run.

### Clean repo
All workshop notes, docs, and save-points were removed to keep the codebase focused on the runnable reference implementation.
