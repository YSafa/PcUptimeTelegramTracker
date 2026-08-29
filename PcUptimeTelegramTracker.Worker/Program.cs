using Microsoft.Extensions.Hosting;
using PcUptimeTelegramTracker.Worker.Models;
using PcUptimeTelegramTracker.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.Local.json overrides values in appsettings.json.
// This file is in .gitignore, so it never gets committed — only exists locally.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));
builder.Services.AddSingleton<TelegramNotifier>();

// Required for proper integration with the Windows Service Control Manager
// when running as a service (under the SYSTEM account).
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PcUptimeTelegramTracker";
});

// Currently registers an empty Worker; EventLogWatcher, TelegramNotifier etc.
// will be added here later via AddSingleton/AddHostedService.
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();