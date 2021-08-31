using BscTokenSniper.Handlers;
using BscTokenSniper.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BscTokenSniper
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args)
        .ConfigureServices((hostContext, services) =>
        {
            var config = hostContext.Configuration;

            var loggerConfig = new LoggerConfiguration()
                  .ReadFrom.Configuration(config);

            var logger = loggerConfig.CreateLogger()
                .ForContext(MethodBase.GetCurrentMethod().DeclaringType);
            Log.Logger = logger;

            logger.Information("Running BSC Token Sniper with Args: @{args}", args);
            services.AddHttpClient();
            services.AddSingleton<TradeHandler>();
            services.AddSingleton<RugHandler>();
            services.Configure<SniperConfiguration>(config.GetSection("SniperConfiguration"));
            services.AddHostedService<SniperService>();
        });

    }
}
