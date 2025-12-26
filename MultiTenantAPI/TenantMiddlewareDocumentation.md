# MultiTenantAPI Complete Guide (Beginner to Advanced)

**How to read this doc**:
- Every concept is explained separately with definition, code from this repo, explanation in plain English, and examples.
- First read what the concept is, then see the code, then understand how it works inside.
- If any word is confusing, check the glossary at bottom.

**Topics we will cover** (concept by concept)
1. [Middleware](#1-middleware)
2. [Dependency Injection (DI) and Service Lifetimes](#2-dependency-injection-di-and-service-lifetimes)
3. [Interfaces](#3-interfaces)
4. [Multi-tenancy (Single-DB with TenantId column)](#4-multi-tenancy-single-db-with-tenantid-column)
5. [Entity Framework Core (DbContext, ChangeTracker, Global Filters)](#5-entity-framework-core-dbcontext-changetracker-global-filters)
6. [Inheritance](#6-inheritance)
7. [Generics and Lambda Expressions](#7-generics-and-lambda-expressions)
8. [Async/Await](#8-asyncawait)
9. [Extension Methods](#9-extension-methods)
10. [Attributes and Routing](#10-attributes-and-routing)
11. [Quick Glossary](#11-quick-glossary)

---

## 1. Middleware (मिडलवेयर)

### Middleware क्या है? (What is it?)
Middleware एक ऐसा component है जो हर HTTP request को check कर सकता है, उसमें changes कर सकता है, या request को आगे जाने से रोक सकता है। Think of it like a security guard at each gate - har gate pe guard check karta hai aur decide karta hai ki aage jaane दूं ya nahi.

Simple words mein: Middleware ek code ka piece hai jo har request पे automatically run होता है before controller tak पहुंचे।

### Is repo mein kahan hai?
File: `Middleware/TenantResolver.cs` (Yeh file tenant ko resolve karti hai)

### Key syntax (actual repo code)
```csharp
public class TenantResolver
{
    private readonly RequestDelegate _next;
    
    public TenantResolver(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentTenantService currentTenantService)
    {
        context.Request.Headers.TryGetValue("tenant", out var tenantFromHeader);
        if (string.IsNullOrEmpty(tenantFromHeader) == false)
        {
            await currentTenantService.SetTenant(tenantFromHeader);
        }
        await _next(context);
    }
}
```

Registration in `Program.cs`:
```csharp
app.UseHttpsRedirection();
app.UseAuthorization();
app.UseMiddleware<TenantResolver>();  // <-- Our custom middleware
app.MapControllers();
```

### Andar se kaise kaam karta hai (Deep dive)
**Constructor (class banate waqt)**:
- `RequestDelegate _next` मिलता है - यह एक function pointer है next middleware का। ASP.NET Core automatically भेज देता है।
- Simple terms: `_next` एक function है जो next gate (middleware) को call करता है।

**InvokeAsync method (har request pe chalta hai)**:
- `HttpContext context`: इसमें request की puri information hoti hai - headers, body, response, user, sab kuch।
- `ICurrentTenantService currentTenantService`: यह service DI se inject hoti hai (method ke parameter mein). Isko "method injection" kehte hain aur ye har request ke liye alag hoti hai.

**Important lines ka matlab**:
- `context.Request.Headers.TryGetValue("tenant", out var tenantFromHeader)`: Header se "tenant" value safely read karo। Agar nahi mila तो false return करेगा। `tenantFromHeader` technically `StringValues` hai but C# use string ki tarah treat karta hai।
- `await _next(context)`: Control अगले middleware ko pass karo। Agar aap yeh line nahi likhoge toh pipeline yahi रुक जाएगा aur controller tak request नहीं पहुंचेगी।

### GET /api/products call mein kya hota hai (step by step)
1. **Client request भेजता है**: `GET /api/products` with header `tenant: tenant1`
2. **Kestrel (web server) `HttpContext` बनाता है**: Yeh ek container hai jismein request/response ka sab data hota hai
3. **Pipeline順序 mein चलता है**: पहले `UseHttpsRedirection`, फिर `UseAuthorization`, फिर **`TenantResolver`**, last mein `MapControllers`
4. **`TenantResolver.InvokeAsync` run होता है**:
   - `tenant` header read karta hai → "tenant1" mil jata है
   - `currentTenantService.SetTenant("tenant1")` call karta hai (yeh database mein check karta hai ki tenant valid hai ya nahi aur store kar leta hai)
   - `_next(context)` call karta hai → pipeline aage बढ़ता है controller tak
5. **Controller execute होता है** (ab tenant set हो चुका है scoped service mein)

### POST /api/products call mein kya hota hai
Step 1 se 4 tak same hai jaise GET mein। Bas difference yeh hai:
5. **Controller JSON body receive करता है** aur `ProductService.CreateProduct` method ko call करता है
6. **Service `AppDbContext` use करती है** jo already tenant set हो चुका है middleware से

### Common गलतियां (Pitfalls)
- **`_next` call nahi kiya**: Pipeline यहीं रुक जाएगा, controller tak request नहीं पहुंचेगी। Always call `_next(context)`.
- **Order important hai**: Abhi order hai `UseAuthorization` → `TenantResolver`। Agar auth policies को tenant चाहिए तoh problem होगी क्योंकि tenant middleware बाद mein चल रहा है। **Fix**: `UseMiddleware<TenantResolver>()` को `UseAuthorization()` से पहले लगाओ।
- **Exceptions handle nahi kiye**: Agar `SetTenant` throw करता है (invalid tenant) तoh middleware catch नहीं करता → 500 error आ जाती है। **Better way**: try/catch lगाओ aur 401 return करो।
- **Header missing hai**: `SetTenant` call ही nahi hota → `TenantId` null रहता है → queries mein koi data नहीं आता।

### Variations (kaise improve kar sakte hain)
**Strict validation (pakka validation)**:
```csharp
public async Task InvokeAsync(HttpContext context, ICurrentTenantService currentTenantService)
{
    if (!context.Request.Headers.TryGetValue("tenant", out var tenantHeader) || string.IsNullOrWhiteSpace(tenantHeader))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Missing tenant header.");
        return;  // Stop here, don't call _next
    }

    var ok = await currentTenantService.TrySetTenantAsync(tenantHeader);
    if (!ok)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Invalid tenant.");
        return;
    }

    await _next(context);
}
```

**Tenant from JWT claim** (instead of header):
```csharp
var tenantClaim = context.User.FindFirst("tenant")?.Value;
if (!string.IsNullOrEmpty(tenantClaim))
{
    await currentTenantService.SetTenant(tenantClaim);
}
```

**Tenant from subdomain**:
```csharp
var host = context.Request.Host.Value;  // e.g., "tenant1.example.com"
var tenant = host.Split('.').FirstOrDefault();
```

---

## 2. Dependency Injection (DI) और Service Lifetimes

### Dependency Injection क्या है? (Kya hai ye?)
DI एक pattern है जहां objects अपनी dependencies खुद नहीं बनाते, बल्कि बाहर से मिलती हैं (DI container से)। ASP.NET Core का DI container lifetimes manage करता है।

Simple words mein: Suppose aapko कार चलानी hai. Aap petrol pump नहीं बनाते, petrol pump पहले से ready मिलता है। Similarly, DI container ready objects provide karta hai।

### Is repo mein kahan hai?
`Program.cs` में service registrations:
```csharp
builder.Services.AddTransient<IProductService, ProductService>();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddDbContext<TenantDbContext>(options => 
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

### Important concepts (samajhne wali cheezein)
**Service Lifetimes (kitne der tak object rahega)**:
- **Transient**: Har bar naya instance बनता है जब request करते हैं। Lightweight, stateless services के लिए use करो।
  - Example: जैसे disposable cup, har bar naya mil jata hai।
- **Scoped**: Ek HTTP request ke लिए ek instance। Request-specific data के लिए perfect (जैसे tenant ID)।
  - Example: Theatre ticket, ek show ke लिए ek ticket।
- **Singleton**: Poora app lifetime के लिए ek \u0939\u0940 instance। **Tenant data के लिए kabhi use mat karo** (requests के beech leak ho jayega)।
  - Example: Manager's office, sab ke liye ek \u0939\u0940 office है।

### Low-level: How it works
When `ProductsController` is created:
1. DI sees constructor: `public ProductsController(IProductService productService)`
2. Looks up registration: `IProductService` → `ProductService` (transient)
3. Creates `ProductService`
4. `ProductService` constructor needs `AppDbContext`
5. Looks up: `AppDbContext` (scoped) → already created for this request? If yes, reuse. If no, create.
6. `AppDbContext` constructor needs `ICurrentTenantService`
7. Looks up: `ICurrentTenantService` → `CurrentTenantService` (scoped) → reuses the same instance that middleware already set tenant on.

This is why scoped is critical: middleware and DbContext share the *same* `CurrentTenantService` instance within one request.

### How it appears in GET /api/products flow
1. Middleware injects `ICurrentTenantService` (scoped) → DI creates `CurrentTenantService` instance #1
2. Middleware calls `SetTenant("tenant1")` on instance #1 → `TenantId = "tenant1"`
3. Controller activates → needs `IProductService` → creates `ProductService` → needs `AppDbContext` → needs `ICurrentTenantService` → DI gives *same* instance #1 (because scoped)
4. `AppDbContext` constructor reads `currentTenantService.TenantId` → gets "tenant1"

### How it appears in POST /api/products flow
Identical to GET. Scoped services ensure tenant set in middleware is visible to DbContext.

### Pitfalls
- **Using Singleton for tenant service**: One instance across all requests → tenant from request A leaks into request B.
- **Resolving services before middleware runs**: If you resolve `AppDbContext` in an early middleware before `TenantResolver`, it gets a fresh `CurrentTenantService` with null tenant.
- **Transient for DbContext**: Every query would get a new context → can't track changes across queries in same request.

### Variations
**Manual resolution** (avoid unless necessary):
```csharp
var tenantService = context.RequestServices.GetRequiredService<ICurrentTenantService>();
```

**Adding scoped factory**:
```csharp
builder.Services.AddScoped<IDbContextFactory<AppDbContext>, AppDbContextFactory>();
```

---

## 3. Interfaces

### What are Interfaces?
A contract that defines members (properties, methods) without implementation. Classes implement the interface and provide the actual behavior.

### Where in this repo?
- `Services/ICurrentTenantService.cs` and implementation `CurrentTenantService.cs`
- `Services/IProductService.cs` and implementation `ProductService.cs`
- `Services/IMustHaveTenant.cs` (marker interface for tenant-owned entities)

### Key syntax (actual repo code)
**Interface definition**:
```csharp
namespace MultiTenantAPI.Services
{
    public interface ICurrentTenantService
    {
        string? TenantId { get; set; }
        public Task<bool> SetTenant(string tenant);
    }
}
```

**Implementation**:
```csharp
public class CurrentTenantService : ICurrentTenantService
{
    private readonly TenantDbContext _context;
    
    public CurrentTenantService(TenantDbContext context)
    {
        _context = context;
    }

    public string? TenantId { get; set; }

    public async Task<bool> SetTenant(string tenant)
    {
        var tenantExists = await _context.Tenants.Where(t => t.Id == tenant).FirstOrDefaultAsync();
        if (tenantExists != null)
        {
            TenantId = tenantExists.Id;
            return true;
        }
        throw new Exception("Tenant Invalid!");
    }
}
```

**Marker interface** (no methods, just a contract):
```csharp
public interface IMustHaveTenant
{
    public string TenantId { get; set; }
}
```

Used by:
```csharp
public class Product : IMustHaveTenant
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string TenantId { get; set; }
}
```

### Low-level: How it works
- **Registration**: `builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();` tells DI: "When someone asks for `ICurrentTenantService`, give them a `CurrentTenantService` instance."
- **Runtime dispatch**: When you call `currentTenantService.SetTenant(...)`, the CLR uses virtual dispatch to call the implementation method on `CurrentTenantService`.
- **Marker interface**: `IMustHaveTenant` lets you write generic code:
  ```csharp
  foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
  {
      entry.Entity.TenantId = CurrentTenantId;  // Works for any entity implementing IMustHaveTenant
  }
  ```

### How it appears in GET /api/products flow
1. Middleware receives `ICurrentTenantService` parameter → DI gives `CurrentTenantService` instance
2. Calls `SetTenant` → interface method → actual `CurrentTenantService.SetTenant` runs
3. `AppDbContext` constructor receives same `ICurrentTenantService` → reads `TenantId` property

### How it appears in POST /api/products flow
1. Service calls `_context.SaveChanges()`
2. Override checks `ChangeTracker.Entries<IMustHaveTenant>()` → finds `Product` (implements `IMustHaveTenant`)
3. Sets `entry.Entity.TenantId = CurrentTenantId`

### Pitfalls
- **Forgetting to register**: If you don't call `AddScoped<ICurrentTenantService, CurrentTenantService>()`, runtime throws when trying to resolve.
- **Using concrete class in constructor**: `public ProductsController(CurrentTenantService tenantService)` couples you to the implementation. Use interface instead.
- **Marker interface forgotten**: If `Product` doesn't implement `IMustHaveTenant`, `SaveChanges` won't stamp tenant on it.

### Variations
**Non-throwing version**:
```csharp
public interface ICurrentTenantService
{
    string? TenantId { get; set; }
    Task<bool> TrySetTenantAsync(string tenant);  // Returns false instead of throwing
}
```

**Read-only tenant**:
```csharp
public interface ICurrentTenantService
{
    string? TenantId { get; }  // No setter, set only via SetTenant
    Task SetTenant(string tenant);
}
```

**Mock for testing**:
```csharp
public class MockTenantService : ICurrentTenantService
{
    public string? TenantId { get; set; } = "test-tenant";
    public Task<bool> SetTenant(string tenant) => Task.FromResult(true);
}
```

---

## 4. Multi-tenancy (Single-DB with TenantId column)

### What is Multi-tenancy?
One application serves many customers (tenants) with data isolation. This repo uses a single database with a `TenantId` column on tenant-owned tables.

### Where in this repo?
- Header-based tenant resolution: `Middleware/TenantResolver.cs`
- Tenant storage: `Services/CurrentTenantService.cs`
- Read isolation: `Models/AppDbContext.cs` global query filter
- Write isolation: `Models/AppDbContext.cs` SaveChanges override
- Entity marker: `Models/Product.cs` implements `IMustHaveTenant`

### Key syntax (actual repo code)
**Global query filter** (in `AppDbContext`):
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>().HasQueryFilter(p => p.TenantId == CurrentTenantId);
}
```

**Tenant stamping on save**:
```csharp
public override int SaveChanges()
{
    foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>().ToList())
    {
        switch (entry.State)
        {
            case EntityState.Added:
            case EntityState.Modified:
                entry.Entity.TenantId = CurrentTenantId;
                break;
        }
    }
    var result = base.SaveChanges();
    return result;
}
```

### Low-level: How it works
**Read path** (GET /api/products):
1. Middleware reads header `tenant: tenant1` → calls `SetTenant("tenant1")`
2. `CurrentTenantService` validates tenant exists in `TenantDbContext` → stores `TenantId = "tenant1"`
3. `AppDbContext` constructor copies `CurrentTenantId = _currentTenantService.TenantId` → "tenant1"
4. Query: `_context.Products.ToList()`
5. EF applies filter: SQL becomes `SELECT * FROM Products WHERE TenantId = @__CurrentTenantId_0`
6. Parameter `@__CurrentTenantId_0` = "tenant1"
7. Database returns only rows with `TenantId = 'tenant1'`

**Write path** (POST /api/products):
1. Service creates `new Product { Name = "...", Description = "..." }` (no `TenantId` set)
2. Calls `_context.Products.Add(product)` → marks as `Added`
3. Calls `_context.SaveChanges()`
4. Override loops: finds `product` (implements `IMustHaveTenant` and state is `Added`)
5. Sets `product.TenantId = CurrentTenantId` → "tenant1"
6. Calls `base.SaveChanges()` → EF generates SQL: `INSERT INTO Products (Name, Description, TenantId) VALUES (@p0, @p1, @p2)`
7. Parameter `@p2` = "tenant1"

### How it appears in GET /api/products flow
Full flow:
1. Request: `GET /api/products` with header `tenant: tenant1`
2. `TenantResolver` middleware: reads header → `SetTenant("tenant1")` → validates → stores in scoped service
3. Controller created → `ProductService` created → `AppDbContext` created → reads tenant from scoped service → `CurrentTenantId = "tenant1"`
4. Service: `_context.Products.ToList()`
5. EF: applies filter → `WHERE TenantId = 'tenant1'`
6. Returns only tenant1 products

If header is **missing**:
- `SetTenant` not called → `TenantId` stays null
- `AppDbContext.CurrentTenantId` = null
- Filter becomes `WHERE TenantId IS NULL`
- Returns no rows (or only null-tenant rows if any exist)

If header is **invalid** (`tenant: bad`):
- `SetTenant("bad")` → queries `Tenants` table → no match → throws `Exception("Tenant Invalid!")`
- Middleware doesn't catch → request returns 500

### How it appears in POST /api/products flow
Full flow:
1. Request: `POST /api/products` with header `tenant: tenant1` and JSON body `{"name":"Widget","description":"..."}`
2. Middleware resolves tenant (same as GET)
3. Controller calls `_productService.CreateProduct(request)`
4. Service: creates `new Product` (no `TenantId` set manually)
5. Adds to context, calls `SaveChanges()`
6. Override: iterates tracked entities → finds `product` (Added) → sets `product.TenantId = "tenant1"`
7. Base `SaveChanges`: generates INSERT with `TenantId = 'tenant1'`
8. Returns created product

### Pitfalls
- **Header missing**: `TenantId` null → filter matches nothing → confusing empty results. Fix: strict middleware returns 400.
- **Invalid header**: throws exception → 500 response. Fix: catch in middleware, return 401.
- **Using `Find(id)`**: Bypasses query filters for primary key lookups. Example:
  ```csharp
  var product = _context.Products.Find(5);  // Can return product from different tenant!
  ```
  Fix: Use filtered query:
  ```csharp
  var product = _context.Products.Where(p => p.Id == 5).FirstOrDefault();  // Respects filter
  ```
- **Null tenant on write**: If `CurrentTenantId` is null, `SaveChanges` writes null → data without tenant. Fix: throw if null:
  ```csharp
  if (CurrentTenantId == null)
      throw new InvalidOperationException("Tenant not resolved for this request.");
  ```
- **Middleware order**: If tenant middleware runs after `UseAuthorization`, auth policies can't see tenant.

### Variations
**Per-tenant database** (strong isolation):
- Central DB stores tenant metadata including connection strings.
- Middleware sets tenant → factory builds `AppDbContext` with tenant-specific connection string.
- Pros: complete data isolation, per-tenant backups. Cons: operational overhead, migrations per tenant.

**Tenant from JWT claim** (secure):
```csharp
var tenantClaim = context.User.FindFirst("tenant")?.Value;
// Client can't spoof because token is signed
```

**Caching tenant metadata**:
```csharp
var cacheKey = $"tenant_{tenant}";
if (!_cache.TryGetValue(cacheKey, out Tenant tenantObj))
{
    tenantObj = await _context.Tenants.FindAsync(tenant);
    _cache.Set(cacheKey, tenantObj, TimeSpan.FromMinutes(5));
}
```

**Strict middleware**:
```csharp
// Return 400 if header missing, 401 if invalid (shown in Middleware section above)
```

---

## 5. Entity Framework Core (DbContext, ChangeTracker, Global Filters)

### What is EF Core?
Object-Relational Mapper (ORM) that maps C# classes to database tables and translates LINQ queries to SQL.

### Where in this repo?
- `Models/AppDbContext.cs`: Main context for business data (Products)
- `Models/TenantDbContext.cs`: Context for tenant metadata
- `Models/Product.cs`, `Models/Tenant.cs`: Entity classes

### Key syntax (actual repo code)
**DbContext definition**:
```csharp
public class AppDbContext : DbContext
{
    private readonly ICurrentTenantService _currentTenantService;
    public string CurrentTenantId { get; set; }
    
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantService currentTenantService) 
        : base(options)
    {
        _currentTenantService = currentTenantService;
        CurrentTenantId = _currentTenantService.TenantId;
    }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<Product> Products { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>().HasQueryFilter(p => p.TenantId == CurrentTenantId);
    }

    public override int SaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                case EntityState.Modified:
                    entry.Entity.TenantId = CurrentTenantId;
                    break;
            }
        }
        var result = base.SaveChanges();
        return result;
    }
}
```

**Entity**:
```csharp
public class Product : IMustHaveTenant
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string TenantId { get; set; }
}
```

### Low-level: How it works
**Global query filter**:
- `OnModelCreating` runs once when EF builds the model (first context usage).
- Lambda `p => p.TenantId == CurrentTenantId` is captured as expression tree.
- For each query, EF evaluates `CurrentTenantId` (instance property) and injects `WHERE TenantId = {value}`.
- Because `CurrentTenantId` is set in constructor, each context instance filters by its own tenant.

**ChangeTracker**:
- Tracks entity states: `Unchanged`, `Added`, `Modified`, `Deleted`.
- When you call `_context.Products.Add(product)`, state becomes `Added`.
- `ChangeTracker.Entries<IMustHaveTenant>()` returns entries implementing the marker interface.
- Setting `entry.Entity.TenantId` modifies the tracked entity before SQL is generated.

**SaveChanges flow**:
1. Override runs → sets `TenantId` on new/modified entities
2. Calls `base.SaveChanges()` → EF generates SQL based on tracked changes
3. Executes SQL, returns number of affected rows

### How it appears in GET /api/products flow
1. `AppDbContext` created → constructor runs:
   - Receives `ICurrentTenantService` (already has `TenantId = "tenant1"` from middleware)
   - Sets `CurrentTenantId = "tenant1"`
2. Service calls `_context.Products.ToList()`
3. EF builds SQL:
   ```sql
   SELECT [p].[Id], [p].[Name], [p].[Description], [p].[TenantId]
   FROM [Products] AS [p]
   WHERE [p].[TenantId] = @__CurrentTenantId_0
   ```
   Parameter: `@__CurrentTenantId_0` = "tenant1"
4. Executes query, returns only tenant1 rows

### How it appears in POST /api/products flow
1. Service creates entity:
   ```csharp
   var product = new Product { Name = "Widget", Description = "..." };
   ```
2. Adds to context:
   ```csharp
   _context.Products.Add(product);
   ```
   ChangeTracker marks `product` as `Added`
3. Calls `SaveChanges()`:
   - Override loops: finds `product` (state `Added`, implements `IMustHaveTenant`)
   - Sets `product.TenantId = "tenant1"`
4. Calls `base.SaveChanges()`:
   - Generates SQL:
     ```sql
     INSERT INTO [Products] ([Name], [Description], [TenantId])
     VALUES (@p0, @p1, @p2);
     ```
     Parameters: `@p0 = "Widget"`, `@p1 = "..."`, `@p2 = "tenant1"`
5. Executes INSERT

### Pitfalls
- **Synchronous methods**: `ToList()`, `SaveChanges()` block threads. Use `ToListAsync()`, `SaveChangesAsync()` for scalability.
- **Using `Find(id)`**: Bypasses global filters (EF optimization for PK lookups). Use `Where` instead.
- **Forgetting `base.SaveChanges()`**: EF won't write to DB.
- **Null `CurrentTenantId` on save**: Writes null tenant. Add guard:
  ```csharp
  if (CurrentTenantId == null)
      throw new InvalidOperationException("Tenant not resolved.");
  ```
- **Filter not parameterized**: If you use a static property instead of instance property, all contexts share same tenant. Always use instance property.

### Variations
**Async override**:
```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
    {
        if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            entry.Entity.TenantId = CurrentTenantId;
    }
    return await base.SaveChangesAsync(cancellationToken);
}
```

**Soft delete filter**:
```csharp
modelBuilder.Entity<Product>().HasQueryFilter(p => 
    p.TenantId == CurrentTenantId && !p.IsDeleted);
```

**Ignoring filter** (when you explicitly want all tenants, e.g., admin):
```csharp
var allProducts = _context.Products.IgnoreQueryFilters().ToList();
```

---

## 6. Inheritance

### What is Inheritance?
A class derives from another class, inheriting its members and behavior. The derived class can override virtual methods.

### Where in this repo?
- `AppDbContext : DbContext`
- `TenantDbContext : DbContext`
- `Product : IMustHaveTenant` (interface inheritance)

### Key syntax (actual repo code)
```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantService currentTenantService) 
        : base(options)  // <-- Calls base class constructor
    {
        _currentTenantService = currentTenantService;
        CurrentTenantId = _currentTenantService.TenantId;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)  // <-- Overrides base method
    {
        modelBuilder.Entity<Product>().HasQueryFilter(p => p.TenantId == CurrentTenantId);
    }

    public override int SaveChanges()  // <-- Overrides base method
    {
        // Custom logic before save
        foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                entry.Entity.TenantId = CurrentTenantId;
        }
        var result = base.SaveChanges();  // <-- Calls base implementation
        return result;
    }
}
```

### Low-level: How it works
**`: base(options)`**:
- Constructor of `AppDbContext` receives `DbContextOptions<AppDbContext> options`.
- `: base(options)` passes `options` to the parent class `DbContext` constructor.
- Parent constructor sets up EF internals (model, connection, etc.).
- After parent constructor completes, `AppDbContext` constructor body runs.

**Override methods**:
- `protected override void OnModelCreating`: parent `DbContext` declares this as `virtual`, allowing derived classes to customize model building.
- `public override int SaveChanges()`: parent declares as `virtual`. Override adds custom logic (tenant stamping) then calls `base.SaveChanges()` to execute actual save.

### How it appears in GET /api/products flow
1. DI creates `AppDbContext`:
   - Calls constructor → `: base(options)` runs first → parent `DbContext` initializes
   - Then `AppDbContext` constructor body runs → sets `CurrentTenantId`
2. First query triggers `OnModelCreating` (if not already built):
   - Parent builds base model → derived override adds global filter
3. Query executes with filter applied

### How it appears in POST /api/products flow
1. Service calls `_context.SaveChanges()`
2. Override `SaveChanges()` runs:
   - Custom logic: stamps `TenantId`
   - Calls `base.SaveChanges()` → parent method generates and executes SQL

### Pitfalls
- **Forgetting `: base(...)`**: Parent constructor doesn't run → EF not initialized → runtime error.
- **Not calling `base.SaveChanges()`**: Custom override runs but nothing writes to DB.
- **Overriding without understanding base behavior**: Can break change tracking, transaction handling, etc.

### Variations
**Shared base context for multi-tenant stamping**:
```csharp
public abstract class MultiTenantDbContext : DbContext
{
    protected string CurrentTenantId { get; set; }
    
    protected MultiTenantDbContext(DbContextOptions options, ICurrentTenantService tenantService)
        : base(options)
    {
        CurrentTenantId = tenantService.TenantId;
    }

    public override int SaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                entry.Entity.TenantId = CurrentTenantId;
        }
        return base.SaveChanges();
    }
}

public class AppDbContext : MultiTenantDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantService tenantService)
        : base(options, tenantService)
    {
    }
    
    public DbSet<Product> Products { get; set; }
}
```

---

## 7. Generics and Lambda Expressions

### What are Generics?
Type parameters that let you write type-safe code without committing to a specific type until runtime.

### What are Lambda Expressions?
Inline anonymous functions. Can be converted to delegates or expression trees (used by EF).

### Where in this repo?
- Generics: `DbContextOptions<AppDbContext>`, `ChangeTracker.Entries<IMustHaveTenant>()`, `IEnumerable<Product>`
- Lambdas: `p => p.TenantId == CurrentTenantId`, `t => t.Id == tenant`, `options => options.UseSqlServer(...)`

### Key syntax (actual repo code)
**Generics**:
```csharp
// Generic type parameter
public DbSet<Product> Products { get; set; }

// Generic method
ChangeTracker.Entries<IMustHaveTenant>()
```

**Lambda expressions**:
```csharp
// Query filter
modelBuilder.Entity<Product>().HasQueryFilter(p => p.TenantId == CurrentTenantId);

// LINQ query
var tenantExists = await _context.Tenants.Where(t => t.Id == tenant).FirstOrDefaultAsync();

// Configuration
builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

### Low-level: How it works
**Generics**:
- `DbContextOptions<AppDbContext>`: The `<AppDbContext>` is a type parameter. At runtime, it becomes `DbContextOptions` specific to `AppDbContext`, avoiding casting and providing compile-time checks.
- `ChangeTracker.Entries<IMustHaveTenant>()`: Returns only entries where entity implements `IMustHaveTenant`. Generic constraint filters at compile time.

**Lambda as delegate**:
```csharp
options => options.UseSqlServer(...)
```
- Short for:
  ```csharp
  DbContextOptionsBuilder options =>
  {
      return options.UseSqlServer(...);
  }
  ```
- Compiled to a method and passed as a delegate.

**Lambda as expression tree** (EF):
```csharp
p => p.TenantId == CurrentTenantId
```
- EF doesn't execute this as code. It inspects the expression tree to build SQL.
- `p` is a parameter of type `Product`.
- `CurrentTenantId` is captured from the context instance.
- EF translates to SQL: `WHERE [p].[TenantId] = @__CurrentTenantId_0`

### How it appears in GET /api/products flow
1. Constructor: `DbContextOptions<AppDbContext> options` → generic ensures only `AppDbContext` options are passed
2. Query filter: `p => p.TenantId == CurrentTenantId` → EF translates to SQL `WHERE` clause
3. Return type: `IEnumerable<Product>` → generic collection of `Product` objects

### How it appears in POST /api/products flow
1. SaveChanges override:
   ```csharp
   ChangeTracker.Entries<IMustHaveTenant>()
   ```
   → Returns entries implementing `IMustHaveTenant` (type-safe, no casting)

### Pitfalls
- **Misunderstanding lambda capture**: `CurrentTenantId` is evaluated at query time, not when filter is defined. If `CurrentTenantId` changes between contexts, each context gets its own value.
- **Lambda in LINQ to Objects vs LINQ to Entities**: `Where(p => p.Name.Contains("x"))` works in memory, but EF translates it to SQL. Some methods can't be translated (e.g., custom methods).

### Variations
**Explicit lambda**:
```csharp
Func<Product, bool> filter = p => p.TenantId == CurrentTenantId;
// vs inline: .Where(p => p.TenantId == CurrentTenantId)
```

**Generic method**:
```csharp
public T GetById<T>(int id) where T : class
{
    return _context.Set<T>().Find(id);
}
```

---

## 8. Async/Await

### What is Async/Await?
Pattern for writing asynchronous code that doesn't block threads while waiting for I/O (database, network).

### Where in this repo?
- `SetTenant` is async: `public async Task<bool> SetTenant(string tenant)`
- Middleware: `public async Task InvokeAsync(...)`
- EF queries: `FirstOrDefaultAsync()`, `Where(...).ToListAsync()` (could be used)

### Key syntax (actual repo code)
```csharp
public async Task<bool> SetTenant(string tenant)
{
    var tenantExists = await _context.Tenants.Where(t => t.Id == tenant).FirstOrDefaultAsync();
    if (tenantExists != null)
    {
        TenantId = tenantExists.Id;
        return true;
    }
    throw new Exception("Tenant Invalid!");
}
```

Middleware:
```csharp
public async Task InvokeAsync(HttpContext context, ICurrentTenantService currentTenantService)
{
    // ...
    await currentTenantService.SetTenant(tenantFromHeader);
    await _next(context);
}
```

### Low-level: How it works
- **`async Task`**: Method can use `await` and returns a `Task` (promise of future result).
- **`await FirstOrDefaultAsync()`**: Thread is released while EF sends SQL and waits for DB response. When DB responds, execution resumes (possibly on a different thread).
- **Why important**: Under load, blocking threads exhausts the thread pool. Async frees threads for other requests.

### How it appears in GET /api/products flow
1. Middleware: `await currentTenantService.SetTenant("tenant1")`
   - Thread released while SQL executes: `SELECT TOP(1) ... FROM Tenants WHERE Id = 'tenant1'`
   - DB returns → thread resumes → `TenantId` set
2. Controller: `_productService.GetAllProducts()` (currently synchronous)
   - Calls `_context.Products.ToList()` → blocks thread while SQL executes
   - **Better**: `await _context.Products.ToListAsync()` → frees thread

### How it appears in POST /api/products flow
1. Middleware: async tenant validation (same as GET)
2. Service: `_context.SaveChanges()` (synchronous) → blocks thread
   - **Better**: `await _context.SaveChangesAsync()` → frees thread

### Pitfalls
- **Mixing sync and async**: Calling `.Result` or `.Wait()` on a Task can cause deadlocks in some contexts.
- **Not awaiting**: Forgetting `await` compiles but method returns before operation completes.
- **Async all the way**: If you use async in service, controller should be async too:
  ```csharp
  [HttpGet]
  public async Task<IActionResult> Get()
  {
      var products = await _productService.GetAllProductsAsync();
      return Ok(products);
  }
  ```

### Variations
**Async service method**:
```csharp
public async Task<IEnumerable<Product>> GetAllProductsAsync()
{
    return await _context.Products.ToListAsync();
}
```

**Async SaveChanges override**:
```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
    {
        if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            entry.Entity.TenantId = CurrentTenantId;
    }
    return await base.SaveChangesAsync(cancellationToken);
}
```

---

## 9. Extension Methods

### What are Extension Methods?
Static methods that appear as instance methods on a type without modifying the type's source code.

### Where in this repo?
None currently exist. This is a concept you could add.

### Key syntax (example)
```csharp
public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseTenantResolver(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantResolver>();
    }
}
```

Usage:
```csharp
app.UseTenantResolver();  // Instead of app.UseMiddleware<TenantResolver>();
```

### Low-level: How it works
- `this IApplicationBuilder app`: First parameter with `this` keyword makes it an extension method.
- Must be in a static class.
- Must import the namespace to use.
- At runtime, `app.UseTenantResolver()` is compiled to `ApplicationBuilderExtensions.UseTenantResolver(app)`.

### How it could appear in this repo
**Service registration extension**:
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMultiTenancy(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();
        services.AddDbContext<TenantDbContext>(options => options.UseSqlServer(connectionString));
        return services;
    }
}
```

Usage in `Program.cs`:
```csharp
builder.Services.AddMultiTenancy(builder.Configuration.GetConnectionString("DefaultConnection"));
```

**HttpContext extension**:
```csharp
public static class HttpContextExtensions
{
    public static string GetTenantId(this HttpContext context)
    {
        var tenantService = context.RequestServices.GetRequiredService<ICurrentTenantService>();
        return tenantService.TenantId;
    }
}
```

Usage in middleware/controller:
```csharp
var tenantId = context.GetTenantId();
```

### Pitfalls
- **Forgetting `this`**: Makes it a regular static method, not an extension.
- **Missing namespace import**: Extension method hidden unless you add `using`.
- **Overusing**: Can make code harder to discover. Use for common patterns.

### Variations
**Generic extension**:
```csharp
public static class QueryableExtensions
{
    public static IQueryable<T> ForTenant<T>(this IQueryable<T> query, string tenantId) 
        where T : IMustHaveTenant
    {
        return query.Where(e => e.TenantId == tenantId);
    }
}
```

Usage:
```csharp
var products = _context.Products.ForTenant("tenant1").ToList();
```

---

## 10. Attributes and Routing

### What are Attributes?
Metadata attached to classes, methods, or properties. Used for routing, validation, ORM configuration, etc.

### Where in this repo?
- Controllers: `[ApiController]`, `[Route("api/[controller]")]`, `[HttpGet]`, `[HttpPost]`, `[HttpDelete("{id}")]`
- Entities: `[Key]`, `[DatabaseGenerated(DatabaseGeneratedOption.None)]`

### Key syntax (actual repo code)
**Controller**:
```csharp
[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        // ...
    }

    [HttpPost]
    public IActionResult Post([FromBody] CreateProductServiceRequest request)
    {
        // ...
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        // ...
    }
}
```

**Entity**:
```csharp
public class Tenant
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string Id { get; set; }
    public string Name { get; set; }
}
```

### Low-level: How it works
**Routing**:
- `[Route("api/[controller]")]`: `[controller]` is replaced with class name minus "Controller" → `api/products`
- `[HttpGet]`: Maps HTTP GET to this method → `GET /api/products`
- `[HttpPost]`: Maps HTTP POST → `POST /api/products`
- `[HttpDelete("{id}")]`: Route parameter `{id}` binds to method parameter `int id` → `DELETE /api/products/5`

**`[ApiController]`**:
- Enables automatic model validation (returns 400 if model invalid)
- Enables automatic binding from body/query/route
- Enables ProblemDetails for errors

**EF attributes**:
- `[Key]`: Marks property as primary key
- `[DatabaseGenerated(DatabaseGeneratedOption.None)]`: EF won't auto-generate value; app must provide

### How it appears in GET /api/products flow
1. Request: `GET /api/products`
2. Routing: matches `[Route("api/[controller]")]` + `[HttpGet]` → calls `ProductsController.Get()`
3. Method runs → returns `Ok(products)` → 200 response

### How it appears in POST /api/products flow
1. Request: `POST /api/products` with JSON body
2. Routing: matches `[HttpPost]`
3. `[FromBody]`: binds JSON to `CreateProductServiceRequest request`
4. `[ApiController]`: validates model; if invalid, returns 400 before method runs
5. Method runs → returns `Ok(product)` → 200 response

### Pitfalls
- **Missing `[ApiController]`**: No automatic validation, need manual `ModelState.IsValid` checks
- **Wrong HTTP verb**: `[HttpGet]` on a method that creates data → violates REST conventions
- **Route conflicts**: Two methods with same route → runtime error

### Variations
**Route constraints**:
```csharp
[HttpDelete("{id:int}")]  // id must be integer
public IActionResult Delete(int id)
```

**Multiple routes**:
```csharp
[HttpGet]
[Route("")]
[Route("all")]
public IActionResult Get()  // Matches both /api/products and /api/products/all
```

**Custom validation attribute**:
```csharp
public class TenantIdAttribute : ValidationAttribute
{
    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        // Custom validation logic
    }
}
```

---

## 11. Quick Glossary

- **Middleware**: Pipeline component that inspects/modifies HTTP requests and responses.
- **Dependency Injection (DI)**: Pattern where objects receive dependencies from external source (container).
- **Scoped service**: One instance per HTTP request.
- **Transient service**: New instance every time requested.
- **Singleton service**: One instance for app lifetime.
- **Interface**: Contract defining members without implementation.
- **Multi-tenancy**: One app serving many customers with data isolation.
- **DbContext**: EF Core unit of work that tracks entities and executes SQL.
- **Global query filter**: Predicate automatically applied to all queries for an entity.
- **ChangeTracker**: EF Core component tracking entity states (Added, Modified, etc.).
- **Entity**: C# class mapped to database table.
- **Inheritance**: Class deriving from another, inheriting behavior.
- **Override**: Replacing base class method with custom implementation.
- **Generics**: Type parameters allowing type-safe code without committing to specific type.
- **Lambda expression**: Inline anonymous function.
- **Async/Await**: Pattern for non-blocking asynchronous code.
- **Extension method**: Static method appearing as instance method on existing type.
- **Attribute**: Metadata attached to code elements for configuration.
- **DTO**: Data Transfer Object for moving data over wire.
- **Marker interface**: Interface with no methods, just used for tagging (e.g., `IMustHaveTenant`).

---

**End of guide**. If you want code improvements (strict middleware, async methods, delete safety, extension methods), let me know which to implement.
