using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ST10439147_CLDV6212_POE.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((context, config) =>
    {
        // Ensure configuration is loaded properly
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        // Register services - IConfiguration will be automatically available
        services.AddSingleton<TableService>();
        services.AddSingleton<BlobService>();
        services.AddSingleton<QueueService>();
        services.AddSingleton<FileShareService>();
    })
    .Build();

host.Run();