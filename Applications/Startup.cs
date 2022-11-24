
namespace UACloudTwin
{
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.HttpOverrides;
    using Microsoft.AspNetCore.Identity;
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

           // // Setup database context for ASP.NetCore Identity Scaffolding
           //services.AddDbContext<UACloudTwinContext>(ServiceLifetime.Transient);

           // services.AddDefaultIdentity<IdentityUser>(options =>
           //         //require confirmation mail if sendgrid API Key is set
           //         options.SignIn.RequireConfirmedAccount = !string.IsNullOrEmpty(Configuration["SendGridAPIKey"])
           //         ).AddEntityFrameworkStores<UACloudTwinContext>();

            //services.AddAuthentication()
            //    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

            //services.AddAuthorization();

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                           ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

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
            }

            app.UseHsts();

            app.UseHttpsRedirection();

            app.UseForwardedHeaders();

            app.UseStaticFiles();

            app.UseRouting();

            //app.UseAuthentication();

            //app.UseAuthorization();

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
