// StudentNumber: ST10439147
// StudentName: Dillon Rinkwest
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
        services.AddApplicationInsightsTelemetry();
        services.ConfigureFunctionsApplicationInsights();

        // Register your services
        services.AddSingleton<TableService>();
        services.AddSingleton<BlobService>();
    })
    .Build();

host.Run();