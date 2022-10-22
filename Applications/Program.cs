using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace UACloudTwin
{
    public class Program
    {
        public static IHost AppHost { get; set; }

        public static void Main(string[] args)
        {
            AppHost = CreateHostBuilder(args).Build();
            AppHost.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
