using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        // Notice that we are not using the WebApplication builder in this case, but
        // the Host. This is because we are using the Azure Functions Worker runtime, which
        // abstracts a lot of the webserver behavior for us.
        var host = new HostBuilder()
            .ConfigureFunctionsWebApplication()
            .ConfigureServices(services =>
            {
                services.AddApplicationInsightsTelemetryWorkerService();
                services.ConfigureFunctionsApplicationInsights();
            })
            .Build();

        host.Run();
    }
}