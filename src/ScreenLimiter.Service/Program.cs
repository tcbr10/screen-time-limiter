using Microsoft.Extensions.Logging.EventLog;
using ScreenLimiter.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "ScreenLimiter");
// The event log is the parent's record of what happened: time added, sessions locked or signed out.
// By default it only takes warnings, so let this program's own information messages through.
builder.Logging.AddFilter<EventLogLoggerProvider>("ScreenLimiter", LogLevel.Information);
// One event source for everything, so the parent can filter the Application log on "ScreenLimiter".
builder.Services.Configure<EventLogSettings>(settings => settings.SourceName = "ScreenLimiter");
builder.Services.AddSingleton<StateManager>();
builder.Services.AddSingleton<RequestRouter>();
builder.Services.AddHostedService<EnforcementWorker>();
builder.Services.AddHostedService<PipeServer>();
builder.Build().Run();
