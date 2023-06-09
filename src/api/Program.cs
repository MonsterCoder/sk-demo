using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.KernelExtensions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "");
var config = builder.Configuration;
// Add services to the container.
builder.Services.AddControllers();
builder.Services
    .AddSignalR( option =>
    {
        option.EnableDetailedErrors = true;
        option.AddFilter<CustomFilter>();
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
var kernel = new KernelBuilder()
    .Configure(k =>
    {
        k.AddAzureTextEmbeddingGenerationService(
            "Embedding",
            config["AzureOpenAI:Embedding"],
            config["AzureOpenAI:EndPoint"],
            config["AzureOpenAI:ApiKey"]);
        //k.AddAzureTextCompletionService( //this won't work with gpt-35-tubro
        k.AddAzureChatCompletionService(
            "TextCompletion",
            config["AzureOpenAI:TextCompletion"],
            config["AzureOpenAI:EndPoint"],
            config["AzureOpenAI:ApiKey"]);  
        
    })
    .WithMemoryStorage(new Microsoft.SemanticKernel.Memory.VolatileMemoryStore()) //in memory embedding store
    .Build();
kernel.ImportSemanticSkillFromDirectory("skills", "chat");
builder.Services.AddSingleton<IKernel>(kernel);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
var allowedCors = config["AllowedCors"].Split(';');
app.UseCors(policy =>
{
    policy
    .WithOrigins(allowedCors)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials();
});
app.UseAuthorization();

app.MapControllers();
app.MapHub<MessageHub>("/hub", options =>
{
    options.Transports =
         HttpTransportType.WebSockets |
         HttpTransportType.ServerSentEvents;
 });

app.Run();
