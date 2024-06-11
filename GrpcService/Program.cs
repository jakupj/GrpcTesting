using GrpcService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Runtime;

namespace GrpcService
{
    public class Program
    {
        private static readonly string SocketPath = Path.Combine(Path.GetTempPath(), "GrpcServiceStreamTest.socket");

        public static void Main(string[] args)
        {
            Console.WriteLine($"Start test server");
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel(options =>
            {
                if (File.Exists(SocketPath))
                {
                    File.Delete(SocketPath);
                }

                options.ListenUnixSocket(SocketPath);

                options.ListenLocalhost(5001, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            });


            // Add services to the container.
            builder.Services.AddGrpc();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.MapGrpcService<StreamingService>();
            app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

            app.Run();
        }
    }
}