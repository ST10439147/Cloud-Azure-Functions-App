




// CourseCode: CLDV6212
// POE Part: 2 - Azure Functions Configuration

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ST10439147_CLDV6212_POE.Services;

// Create the host builder for Azure Functions
var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        // Register TableService as a singleton
        services.AddSingleton<TableService>();

        // Register BlobService as a singleton (if you have one)
        services.AddSingleton<BlobService>();

        // Register QueueService as a singleton (if you have one)
        services.AddSingleton<QueueService>();

        // Register FileService as a singleton (if you have one)
        services.AddSingleton<FileShareService>();
    })
    .Build();

host.Run();