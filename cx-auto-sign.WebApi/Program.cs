using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace cx_auto_sign.WebApi
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                
                // 包含请求日志
                // .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Information)

                .Enrich.FromLogContext()
                .WriteTo.Logger(Log.Logger)
                .CreateLogger();

            try
            {
                Log.Information("正在启动 WebApi 服务");
                Host.CreateDefaultBuilder(args)
                    .UseSerilog(logger)
                    .ConfigureWebHostDefaults(builder => builder.UseStartup<Startup>())
                    .Build()
                    .Run();
            }
            catch (Exception e)
            {
                Log.Fatal(e, "WebApi 意外终止");
            }

            // Ctrl + C 只会先退出 WebApi，主程序还会继续运行，这里强制退出
            Environment.Exit(0);
        }
    }
}
