# Tenant middleware & resolver — Complete explanation (simple Indian English)

This is a complete, end-to-end guide to how tenant resolution and middleware work in this repository. I explain from very basic to advanced, step-by-step, with scenario-based examples, code snippets and practical improvements. After reading this document you will understand middleware and single-db multi-tenancy in depth. I also include examples of multi-db approach.

Contents
- Quick summary
- Basic concepts (what is middleware, what is multi-tenancy)
- Request lifecycle: end-to-end explanation with your project files
- Walkthrough: each file explained with scenarios (what happens and why)
- Common pitfalls with scenario examples and how to debug
- Improvements with copy-paste code and scenario outcomes
- Multi-db (per-tenant database) example and scenarios
- Advanced patterns and when to use them
- Testing checklist and sample requests
- Final checklist for production readiness

---

Quick summary (one-liner)
- Middleware is code that runs on every HTTP request. Here `TenantResolver` middleware reads `tenant` header and sets tenant for this request. `AppDbContext` uses that tenant to isolate data. Read the scenarios to see exactly how requests behave.

---

Part 1 — Basic concepts (very simple)

What is middleware? (simple)
- Middleware runs in a pipeline. Each middleware can look at the request, change it, or return a response without calling next.
- Scenario: Logging middleware. It logs "Request started", calls next, waits, then logs "Request finished". This shows pre- and post-processing.

What is multi-tenancy? (simple)
- Multi-tenancy: one app serves many customers (tenants). You must stop tenant A seeing tenant B data.
- Scenario (single DB, tenant column): Products table has `TenantId` column. When tenant1 requests /api/products they see only rows with `TenantId = 'tenant1'`.
- Scenario (multi-db): Tenant1 uses DB connection `db-tenant1`, tenant2 uses `db-tenant2`. This gives stronger isolation.

Why middleware + scoped tenant service?
- Middleware runs per-request and can set tenant information into a scoped service. Scoped services are the same instance for the whole request. So DbContext and other services can read the tenant easily.
- Scenario: `TenantResolver` sets `ICurrentTenantService.TenantId = "tenant1"` early. Later `AppDbContext` sees `TenantId` and applies filter.

---

Part 2 — Request ? Response lifecycle (very detailed + scenarios)

Full sequence with scenario outcomes:

1) Client sends request with header `tenant: tenant1`:
   - Example: `curl -H "tenant: tenant1" https://localhost:5001/api/products`
   - Goal: Return only products for `tenant1`.

2) Kestrel -> ASP.NET builds `HttpContext` and runs middleware pipeline in registration order.
   - Scenario: If middleware order is `UseAuthorization()` then `UseMiddleware<TenantResolver>()`, Authorization runs before tenant is set. If policies need tenant, authorization may deny request wrongly.

3) TenantResolver middleware runs:
   - It reads header and calls `currentTenantService.SetTenant("tenant1")`.
   - If header missing and middleware is strict, it returns 400. If middleware is lenient, it continues with `TenantId` null.
   - Scenario: Header missing + middleware strict => response 400 "Missing tenant header". Header missing + middleware lenient => `AppDbContext` sees null tenant and queries return nothing.

4) MVC constructs controller and dependencies (scoped). `AppDbContext` is created and reads `ICurrentTenantService.TenantId`.
   - Important scenario: If a service or controller was resolved earlier in pipeline (e.g., by a custom middleware executed before tenant middleware), it may get an `AppDbContext` with `TenantId` null — this causes wrong results.

5) Controller action runs and calls `ProductService`:
   - Query example: `_context.Products.ToList()` uses global filter and returns only tenant rows.
   - Save example: `_context.Products.Add(p); _context.SaveChanges();` will set `TenantId` on entity before insert.

6) Response returned to client.
   - Scenario: Tenant invalid — `SetTenant` throws. If middleware does not catch, response 500. If middleware catches and returns 401, client sees friendly message.

Sequence diagram simple:
```
Client -> TenantResolver -> (set Tenant in ICurrentTenantService) -> AppDbContext reads tenant -> Controller -> Service -> DB -> Response
```

---

Part 3 — Walkthrough: each file explained with scenario examples

This section goes file-by-file. For each file: what it does, scenario examples, and why choices matter.

1) `Program.cs` (entrypoint)

What it does:
- Registers services and DbContexts. Adds middleware in pipeline.

Why order matters (scenarios):
- Scenario A (correct): `app.UseMiddleware<TenantResolver>();` before `app.UseAuthorization();`.
  - Authorization sees tenant and can enforce tenant-specific policies.
- Scenario B (wrong): `UseAuthorization()` before tenant middleware.
  - Authorization may run without tenant info and deny access incorrectly.

Service lifetimes scenario:
- `ICurrentTenantService` must be `Scoped`. If `Singleton`, it would be same for all requests (wrong). If `Transient`, different instances created and not shared by DbContext, failing to propagate tenant.

2) `Middleware/TenantResolver.cs`

What it does (current code):
- Reads header `tenant` and calls `SetTenant`. Continues pipeline.

Scenarios and outcomes:
- Scenario 1: Valid tenant header `tenant1`.
  - Steps: header read -> SetTenant checks DB -> sets TenantId -> pipeline continues -> DB queries return tenant1 data.
  - Client gets only tenant1 data.
- Scenario 2: Missing header.
  - If middleware lenient: SetTenant not called -> TenantId remains null -> DbContext filters by null tenant -> queries return nothing (confusing).
  - If middleware strict (improved): middleware returns 400 "Missing tenant header". Client knows to include header.
- Scenario 3: Invalid tenant header.
  - If `SetTenant` throws and middleware not catching: request results 500 Internal Server Error (bad UX).
  - If middleware catches and returns 401: client gets clear message "Invalid tenant".

Why log tenant resolution?
- Scenario: production debug. If user reports missing data, logs can show what tenant header arrived and whether tenant validation passed.

3) `Services/ICurrentTenantService.cs` and `CurrentTenantService.cs`

What it does:
- Holds `TenantId` for the request. `SetTenant` verifies tenant exists in `TenantDbContext`.

Scenarios:
- Scenario: Heavy traffic, DB lookup on every request.
  - Outcome: DB becomes bottleneck. Improvement: cache tenant metadata for some minutes.
- Scenario: Cached stale tenant (tenant deleted but cache not updated).
  - Outcome: stale cache returns true for deleted tenant. Use short TTL or invalidation.

Behavior differences:
- `SetTenant` throwing vs `TrySetTenantAsync` boolean:
  - Throwing leads to exceptions bubbling up (500) unless middleware handles it.
  - `TrySetTenantAsync` returns false and allows middleware to return 401 cleanly.

4) `Models/AppDbContext.cs` (EF Core context)

What it does:
- Adds global query filter for `Product` and sets `TenantId` on save.

Key scenarios:
- Scenario 1: Correct flow (tenant set before DbContext created)
  - AppDbContext.CurrentTenantId has 'tenant1'. Filter becomes parameterized by EF and queries return tenant1 rows.
- Scenario 2: DbContext created before tenant set
  - CurrentTenantId is null => filter may be `TenantId == null` or parameter baked => no results or wrong results.
  - How it happens: a middleware that resolves a scoped service before tenant middleware can cause this.
- Scenario 3: Concurrency and model caching
  - EF caches model. If filter uses instance property incorrectly, behavior under concurrency may be unexpected. Safer: use `EF.Property<string>(this, nameof(CurrentTenantId))` so EF evaluates property value per context instance.

SaveChanges scenarios:
- Scenario: Create product without setting TenantId
  - Override `SaveChanges()` sets `TenantId` automatically to current tenant. If TenantId is null, products saved with null tenant (bad).
  - Improvement: refuse to save if TenantId null or throw meaningful exception.

5) `Services/ProductService.cs`

What it does:
- CRUD operations for `Product` using `AppDbContext`.

Scenarios:
- Scenario: Synchronous SaveChanges in high load
  - Blocking calls can reduce throughput. Convert to async (`SaveChangesAsync`) for better scalability.
- Scenario: Deleting product by id
  - If product belongs to different tenant, query filter prevents reading it. But if you call `Find(id)` which ignores filters for key lookups, you may find product across tenants. Use filtered queries or include tenant check on delete.

Example bug scenario: `Find(id)` bypasses filter
- If Product has key 1 for tenant1 and tenant2 (different rows in shared table), `Find(1)` returns the first row irrespective of filter. So prefer `_context.Products.Where(p => p.Id == id).FirstOrDefault()` which respects filters.

---

Part 4 — Common pitfalls with scenario-based debugging

1) No results returned for valid tenant
- Symptoms: API returns empty list for tenant that has data.
- Scenario cause: `AppDbContext` was created before middleware set tenant -> CurrentTenantId null -> filter blocks.
- Fix: Move `UseMiddleware<TenantResolver>()` earlier, ensure services resolved after middleware.

2) Authorization fails unexpectedly
- Symptoms: Authorization denies requests even though user has rights.
- Scenario cause: `UseAuthorization()` runs before tenant middleware and policies use tenant -> policy cannot read tenant and denies.
- Fix: Move tenant middleware before `UseAuthorization()`.

3) Wrong tenant used on create / update
- Symptoms: New entities saved with wrong or null TenantId.
- Scenario cause: TenantId null when `SaveChanges` runs.
- Fix: Validate `CurrentTenantId` before saving; throw friendly error when missing.

4) Cross-tenant delete using `Find`
- Symptoms: Deleted resource of other tenant.
- Scenario cause: `Find()` bypasses query filter for key lookup.
- Fix: Use `.Where(...).FirstOrDefault()` checks with tenant id. Or explicitly check entity.TenantId == currentTenantId before delete.

5) Header spoofing
- Symptoms: One tenant user can see another's data by changing header.
- Scenario cause: Tenant identity in header and unauthenticated or weak auth.
- Fix: Get tenant from JWT claim or ensure authenticated user belongs to tenant.

---

Part 5 — Improvements with copy-paste code and scenario outcomes

For each improvement I show a short code change and possible outcomes in scenarios.

A) Middleware: strict validation (example)

Code (paste into `Middleware/TenantResolver.cs`):
```csharp
public async Task InvokeAsync(HttpContext context, ICurrentTenantService currentTenantService)
{
    if (!context.Request.Headers.TryGetValue("tenant", out var tenantHeader) || string.IsNullOrWhiteSpace(tenantHeader))
    {
        // Scenario: client forgot header -> they get 400 and know to fix.
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Missing tenant header.");
        return;
    }

    var ok = await currentTenantService.TrySetTenantAsync(tenantHeader);
    if (!ok)
    {
        // Scenario: invalid tenant -> 401 returned and request stops.
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Invalid tenant.");
        return;
    }

    await _next(context);
}
```

Outcome scenarios:
- Valid tenant -> request continues.
- Missing header -> 400.
- Invalid tenant -> 401.

B) Tenant service: `TrySetTenantAsync` with caching (example)

Code snippet (paste into service):
```csharp
public async Task<bool> TrySetTenantAsync(string tenant)
{
    if (string.IsNullOrWhiteSpace(tenant)) return false;

    var cacheKey = $"tenant_exists_{tenant}";
    if (!_cache.TryGetValue(cacheKey, out bool exists))
    {
        exists = await _tenantContext.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenant);
        _cache.Set(cacheKey, exists, TimeSpan.FromMinutes(5));
    }

    if (!exists) return false;

    TenantId = tenant;
    return true;
}
```

Outcome scenarios:
- Many requests for same tenant -> first request hits DB, others use cache -> less DB load.
- Tenant removed -> cache TTL may still allow stale access briefly; use short TTL or invalidation.

C) AppDbContext: EF.Property filter and defensive save

Code snippet:
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>().HasQueryFilter(
        p => p.TenantId == EF.Property<string>(this, nameof(CurrentTenantId))
    );
}

private void SetTenantForEntries()
{
    var tenantId = CurrentTenantId;
    if (tenantId == null)
        throw new InvalidOperationException("Tenant not resolved for this request.");

    foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>().ToList())
    {
        if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            entry.Entity.TenantId = tenantId;
    }
}
```

Outcome scenarios:
- If tenant missing at save time -> throw clear error rather than silently writing null tenant.
- Filter uses EF.Property so EF will parameterize tenant value per context instance.

D) Delete safety: check tenant before delete

Bad delete code:
```csharp
var product = _context.Products.Find(id); // can bypass filter
```
Good delete code:
```csharp
var product = _context.Products.Where(p => p.Id == id).FirstOrDefault(); // respects filter
if (product == null) return NotFound();
_context.Products.Remove(product);
await _context.SaveChangesAsync();
```

Outcome scenario:
- Prevents deleting product of another tenant since filter or explicit check stops it.

---

Part 6 — Multi-db (per-tenant database) example and scenarios

Why use multi-db?
- Stronger isolation. If tenant needs different backup/restore, or compliance, separate DB is good.
- More operational overhead (migrations, connection management), but higher security.

High level architecture (scenario):
- Central tenants database stores tenant metadata: id, connection string, status.
- On each request middleware reads tenant id, looks up connection string from central DB (and cache), then provides that connection string to DbContext factory.

Code sketch: DbContext factory approach

```csharp
// ITenantConnectionProvider
public interface ITenantConnectionProvider { Task<string> GetConnectionStringAsync(string tenantId); }

// TenantDbContext contains tenant metadata (central)

// Register IDbContextFactory<AppDbContext> and create per-request instance:
public class AppDbContextFactory
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ITenantConnectionProvider _tenantConnProvider;

    public AppDbContextFactory(IDbContextFactory<AppDbContext> factory, ITenantConnectionProvider tenantConnProvider)
    {
        _factory = factory;
        _tenantConnProvider = tenantConnProvider;
    }

    public async Task<AppDbContext> CreateForTenantAsync(string tenantId)
    {
        var conn = await _tenantConnProvider.GetConnectionStringAsync(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(conn).Options;
        return new AppDbContext(options /*, other deps */);
    }
}
```

Scenario: request for tenant1
- TenantResolver sets tenant1.
- When service needs AppDbContext, factory creates one with connection string for tenant1.
- Queries run against tenant1 DB only.

Scenario: migrating tenant DBs
- You must run migrations per tenant DB. Use tooling to iterate tenants and apply migrations.

Tradeoffs scenario:
- Many small tenants -> many DBs = operational cost but high isolation.
- Few large tenants -> DB-per-tenant may be best.

---

Part 7 — Advanced patterns and examples

1) Tenant in JWT claim (scenario)
- When user logs in, token contains claim `tenant="tenant1"`.
- Middleware reads `User.FindFirst("tenant")` and sets tenant.
- Advantage: client cannot simply change header to impersonate another tenant because token is signed.

2) Subdomain-based tenant (scenario)
- Request to `tenant1.example.com` => parse host and set tenant1.
- Good for UX. But manage DNS and host bindings.

3) Mixed strategies
- Use JWT claim for authentication and header for explicit override by internal services (with checks). Only allow override from trusted clients.

4) Caching tenant metadata with Redis
- Use Redis for distributed cache across multiple app instances. Cache tenant metadata (connection string) and short TTL.

---

Part 8 — Testing, sample requests, and exercises

Sample requests and expected results:
- Valid tenant
  - `curl -H "tenant: tenant1" https://localhost:5001/api/products` -> 200 and list of tenant1 products.
- Missing header with strict middleware
  - `curl https://localhost:5001/api/products` -> 400 Missing tenant header.
- Invalid tenant
  - `curl -H "tenant: bad" https://localhost:5001/api/products` -> 401 Invalid tenant.

Exercises to master:
1. Move `UseMiddleware<TenantResolver>()` before `UseAuthorization()` and test behavior.
2. Change `CurrentTenantService` to `TrySetTenantAsync` with caching and observe DB hits in logs.
3. Write integration test that runs two parallel requests for tenant1 and tenant2 and verify data isolation.
4. Replace header resolution with JWT claim and test header spoofing attempt.

---

Part 9 — Final checklist for production readiness

- Middleware runs early and sets tenant before authorization and before any DbContext creation.
- `ICurrentTenantService` scoped and non-throwing method `TrySetTenantAsync` used.
- Query filters use `EF.Property(this, nameof(CurrentTenantId))` to ensure per-context parameterization.
- `SaveChangesAsync` implemented and checks tenant presence before saving.
- Avoid `Find()` for tenant-scoped queries; use filtered queries.
- Cache tenant metadata to reduce central DB load.
- Prefer tenant claim in JWT for security-sensitive apps.
- Add logging and metrics: tenant resolution successes/failures, cache hits/misses.

---

If you want, I can now apply the recommended code changes to the repository, add unit tests, or implement per-tenant DB factory example in code. Tell me which part to implement next.