using IpSync;
using IpSync.Options;
using IpSync.Services;

var runOnce = args.Any(argument => argument.Equals("--once", StringComparison.OrdinalIgnoreCase));

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "IpSync";
});

builder.Services.Configure<IpSyncOptions>(
    builder.Configuration.GetSection(IpSyncOptions.SectionName));

builder.Services.AddSingleton(new HttpClient
{
    Timeout = TimeSpan.FromSeconds(8)
});

builder.Services.AddSingleton<IpAddressCollector>();
builder.Services.AddSingleton<ReadmeRenderer>();
builder.Services.AddSingleton<GitPublisher>();
builder.Services.AddSingleton<IpSyncRunner>();

if (!runOnce)
{
    builder.Services.AddHostedService<Worker>();
}

var host = builder.Build();

if (runOnce)
{
    await host.Services.GetRequiredService<IpSyncRunner>().RunOnceAsync(CancellationToken.None);
    return;
}

host.Run();
