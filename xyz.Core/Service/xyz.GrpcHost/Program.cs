using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProtoBuf.Grpc.Server;
using xyz.Service;
using xyz.Service.Events;
using xyz.Service.UserManger;

namespace xyz.GrpcHost;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(5000, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services.AddCodeFirstGrpc();

        builder.Services.AddXyzServices();

        var app = builder.Build();

        app.MapGrpcService<RpcService>();
        app.MapGrpcService<RoleService>();
        app.MapGrpcService<UserService>();
        app.MapGrpcService<MenuService>();
        app.MapGrpcService<EventService>();
        app.MapGrpcService<LoadPortService>();

        app.Run();
    }
}
