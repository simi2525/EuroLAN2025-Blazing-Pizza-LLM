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


### AI Integration: Cart Assistant

- **Purpose**: Let users describe pizzas in natural language and have the app plan cart actions via an LLM.
- **Endpoints**:
  - `GET /api/assist/search?q=`: Search specials and toppings by name/description.
  - `POST /api/assist/cart`: Given an utterance, returns a strict JSON plan of actions (e.g., `add_pizza`, `clear_cart`).
- **Client**:
  - Inline assistant in `Home.razor` sidebar posts to `api/assist/cart` and applies the returned plan to `OrderState`.
  - Optional dedicated page exists at `CartAssistant.razor` (`/assist/cart`) for testing/demos.
- **Prompting**: Server composes a system prompt with the current MENU (sizes, specials, toppings) and asks for a single JSON object (`CartPlan`).

#### Configure the LLM

Settings live in `src/BlazingPizza/appsettings.Development.json` under `LLM`:

- `Provider`: `openai` or `ollama`
- `Model`: e.g., `gpt-5-mini` or `llama3.1`
- `OpenAI:ApiKey`: Prefer environment variable `OPENAI_API_KEY` in development
- `OpenAI:BaseUrl`: Optional, for Azure OpenAI or other compatible endpoints
- `Ollama:BaseUrl`: Local Ollama URL (default `http://localhost:11434/v1`)

> Security note: avoid committing real API keys; use environment variables or secret stores.

### Workshop Homework: Edit/Remove Items via LLM

Goal: Extend the assistant so users can edit pizzas already in the cart (update size/toppings) and remove a specific product using natural language.

Requirements:

- Add new supported actions in the LLM plan:
  - `update_pizza` with fields like `{ cartIndex | cartItemId, size?, addToppingIds?, removeToppingIds? }`
  - `remove_pizza` with `{ cartIndex | cartItemId }`
- Provide the current cart context to the LLM so it can reference items deterministically.
  - Easiest path: send a summary from the client in the `POST /api/assist/cart` request (e.g., an extra `cart` field containing a compact JSON summary of items with stable identifiers).
  - Alternative: add a new server endpoint to fetch cart, but current architecture keeps cart client-side in `OrderState`, so client-provided cart context is simplest.
- Update the system prompt to document the new actions, constraints, and how to reference cart items.
- Update client apply-logic in `Home.razor` to handle `update_pizza` and `remove_pizza`.
- Display a stable per-item identifier (or 1-based index) alongside each cart item in the UI so users can refer to items unambiguously (e.g., "remove item 2").

Suggested steps:

1. Define/choose a stable identifier:
   - Option A: Use a 1-based `cartIndex` (simple, order-based).
   - Option B: Assign a GUID `cartItemId` when adding a pizza and store it on each `Pizza`.
2. Expose a minimal cart summary for prompting (id, name, size, toppings).
   - In `Home.razor`, before posting to `api/assist/cart`, build a compact `cart` object: `[ { id, name, size, toppingIds } ]`.
   - Send it along with the utterance, e.g., `new { utterance = assistantUtterance, cart }`.
3. Extend the request contract in `AI/Contracts.cs` to include the optional cart summary: `CartRequest(string Utterance, string UserId = "demo", CartItemSummary[]? Cart = null)`.
4. Extend the system prompt in `AssistController.ToCart` to document the new actions and how to reference cart items by `cartIndex` or `cartItemId`.
   - Provide the serialized `cart` to the model similar to how MENU is provided.
   - Update the expected schema to include `update_pizza` and `remove_pizza` actions, e.g.: `{ "actions": [ { "type": "update_pizza", "cartIndex": number?, "cartItemId": string?, "size": number?, "addToppingIds": number[]?, "removeToppingIds": number[]? }, { "type": "remove_pizza", "cartIndex": number?, "cartItemId": string? } ] }`.
5. Implement client-side handlers for the new actions in `Home.razor` within `RunAssistant()`:
   - `update_pizza`: locate the item by `cartItemId` (preferred) or by index; update `Pizza.Size` and reconcile `Pizza.Toppings` by adding/removing toppings.
   - `remove_pizza`: remove the specific item from `OrderState.Order.Pizzas`.
6. UI hinting: next to each cart item, display either a GUID or a 1-based index so users can refer to items unambiguously.
7. Test with prompts like:
   - "Make item 2 a 12-inch with extra olives"
   - "Remove the margherita with extra cheese"

Relevant files to touch:

- `src/BlazingPizza/AI/Contracts.cs` – extend `CartRequest` and optionally define `CartItemSummary`.
- `src/BlazingPizza/AssistController.cs` – adjust expected schema in the system prompt, include `cart` context if provided, and parse the extended `CartPlan`.
- `src/BlazingPizza.Client/Components/Pages/Home.razor` – send cart summary, implement handlers for `update_pizza` and `remove_pizza`, and render item identifiers in the cart UI.
