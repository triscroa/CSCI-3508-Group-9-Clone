using Microsoft.AspNetCore.HttpOverrides;
using StudentExchangeBck;

// Get Enviroment
Env.GetEnv();

// Tasks that need to be occasionally cleaned
Action Schedule = () =>
{
    LogIn.CleanUp();
};
// Run Clean up at program start
Schedule();
// Schedule Clean up each day at time.
var schedule = new Maitenance(324, Schedule);

// ==================== Created by Visual Studio 2022 ============================

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
