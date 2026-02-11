using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Jifas Assistant Docs", Version = "v1" });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapControllers();

app.UseHttpsRedirection();

app.UseSwagger(); 
app.UseSwaggerUI();
app.Run();