
namespace UACloudTwin
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using System.Threading.Tasks;
    using UACloudTwin.Interfaces;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            services.AddSignalR();

            services.AddSingleton<IMessageProcessor, UAPubSubMessageProcessor>();

            services.AddSingleton<IDigitalTwinClient, ADTClient>();

            if (!string.IsNullOrEmpty(Configuration["USE_MQTT"]))
            {
                services.AddSingleton<ISubscriber, MQTTSubscriber>();
            }
            else
            {
                services.AddSingleton<ISubscriber, KafkaSubscriber>();
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ISubscriber subscriber, IDigitalTwinClient twinClient)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Shared/Error");

                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            _ = Task.Run(() => subscriber.Run());

            _ = Task.Run(() => twinClient.UploadTwinModels());

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Diag}/{action=Index}/{id?}");
                endpoints.MapHub<StatusHub>("/statushub");
            });
        }
    }
}
