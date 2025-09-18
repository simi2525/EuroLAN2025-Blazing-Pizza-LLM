global using BlazingPizza.Shared;
global using BlazingPizza;
using BlazingPizza.Client;
using BlazingPizza.Components;
using BlazingPizza.Components.Account;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Always include Development settings to simplify workshop setup
builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddRazorComponents()
		.AddInteractiveServerComponents()
		.AddInteractiveWebAssemblyComponents();

// Ensure static web assets from referenced projects/libraries are served (e.g., _content/* and _framework/*)
builder.WebHost.UseStaticWebAssets();

// HttpClient factory for REST calls to OpenAI/Ollama
builder.Services.AddHttpClient();

// Add Security
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();


builder.Services.AddDbContext<PizzaStoreContext>(options =>
				options.UseSqlite("Data Source=pizza.db"));

// Add Identity
builder.Services.AddIdentityCore<PizzaStoreUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<PizzaStoreContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<PizzaStoreUser>, IdentityNoOpEmailSender>();


builder.Services.AddScoped<IRepository, EfRepository>();
builder.Services.AddScoped<OrderState>();

builder.Services.AddControllers();

var app = builder.Build();

// Initialize the database
var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
using (var scope = scopeFactory.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<PizzaStoreContext>();
	if (db.Database.EnsureCreated())
	{
		SeedData.Initialize(db);
	}
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseWebAssemblyDebugging();
}
else
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
	app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapPizzaApi();

app.MapControllers();

app.MapRazorComponents<App>()
		.AddInteractiveServerRenderMode()
		.AddInteractiveWebAssemblyRenderMode()
		.AddAdditionalAssemblies(
			typeof(BlazingPizza.Client._Imports).Assembly,
			typeof(BlazingPizza.ComponentsLibrary._Imports).Assembly
		);

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();


app.Run();
