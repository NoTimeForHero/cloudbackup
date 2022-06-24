﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using Quartz;
using Unity;
using EmbedIO.Actions;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using NLog;
using NLog.Targets;
using Quartz.Impl.Matchers;
using EmbedServer = EmbedIO.WebServer;

namespace CloudBackuper.Web
{
    public class WebServer : IDisposable
    {
        protected CancellationTokenSource tokenSource;
        protected ILogger logger = LogManager.GetCurrentClassLogger();
        protected Program program;
        protected EmbedServer server;

        public WebServer(IUnityContainer container, string pathStaticFiles=null, bool developmentMode=false)
        {
            logger.Debug($"Путь до папки со статикой: {pathStaticFiles}");
            var config = container.Resolve<Config>();
            program = Program.Instance;

            var options = new WebServerOptions()
                .WithMode(HttpListenerMode.Microsoft)
                .WithUrlPrefix(config.HostingURI);

            var frontendSettings = new
            {
                appName = getApplicationName(),
                apiUrl = $"{config.HostingURI}/api",
                isService = program.IsService
            };

            server = new EmbedServer(options);

            // Из-за отсутствия обработчика ошибок в EmbedIO приходится использовать такой странный способ проверки занятости префикса
            // Конкретнее: https://github.com/unosquare/embedio/blob/3.1.3/src/EmbedIO/WebServerBase%601.cs#L208
            // Проверяется только токен отмены, а все ошибки включая запуск HttpListener будут проигнорированы без всякого сообщения
            server.Listener.Start();
            server.Listener.Stop();

            if (developmentMode) server.WithCors();

            // TODO: Вынести в settings.json путь к /ws-status
            server.WithModule(nameof(WebSocketStatus), new WebSocketStatus(container, "/ws-status"));

            server.WithWebApi("/api", m => m.WithController(() => new MainController(container)))
                .WithModule(new ActionModule("/settings.json", HttpVerbs.Get, ctx => ctx.SendDataAsync(frontendSettings)));

            if (pathStaticFiles != null) server.WithStaticFolder("/", pathStaticFiles, true);

            server.StateChanged += (s, e) => logger.Debug($"New State: {e.NewState}");
            tokenSource = new CancellationTokenSource();
            server.RunAsync(tokenSource.Token);
        }

        public void Dispose()
        {
            tokenSource.Cancel();
            server.Dispose();
        }

        protected static string getApplicationName()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var title = assembly.GetCustomAttribute<AssemblyTitleAttribute>().Title;
            var version = assembly.GetName().Version;
            return $"{title} {version}";
        }

        protected class MainController : WebApiController
        {
            protected MemoryTarget memoryTarget;
            protected Program program;
            protected IScheduler scheduler;
            private PluginManager pm;

            public MainController(IUnityContainer container)
            {
                memoryTarget = LogManager.Configuration.AllTargets.OfType<MemoryTarget>().FirstOrDefault();
                scheduler = container.Resolve<IScheduler>();
                program = Program.Instance;
                pm = container.Resolve<PluginManager>();
            }

            [Route(HttpVerbs.Any, "/logs")]
            public IEnumerable<string> Logs()
            {
                return memoryTarget?.Logs.Reverse();
            }

            [Route(HttpVerbs.Any, "/plugins")]
            public IEnumerable<object> Plugins() => pm.Plugins;

            [Route(HttpVerbs.Any, "/jobs")]
            public object Index()
            {
                var states = (Dictionary<JobKey, UploadJobState>)scheduler.Context["states"];
                return states.ToDictionary(x => x.Key.Name, x => x.Value);
            }

            [Route(HttpVerbs.Post, "/jobs/start/{name}")]
            public async Task<object> StartJob(string name)
            {
                var tasksDetail = (await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
                    .Select(key => scheduler.GetJobDetail(key));
                var details = await Task.WhenAll(tasksDetail);
                var job = details.FirstOrDefault(xJob => xJob.Key.Name == name);
                if (job != null) await scheduler.TriggerJob(job.Key);
                return job;
            }

            [Route(HttpVerbs.Get, "/reload")]
            public async Task Reload()
            {
                await HttpContext.SendDataAsync(new { Message = "Конфиг приложения будет перезагружен!" });
                await Task.Delay(500);
                await program.Reload(true);
            }

            [Route(HttpVerbs.Delete, "/shutdown")]
            public async Task Shutdown()
            {
                if (program.IsService)
                {
                    Response.StatusCode = 400;
                    await HttpContext.SendDataAsync(new { Error = "Службу невозможно остановить через веб-интефрейс!"});
                    return;
                }

                await HttpContext.SendDataAsync(new { Message = "Приложение будет остановлено через несколько секунд!"});
                await Task.Delay(500);
                program.Shutdown();
            }

            // На будущее, если забуду как десериализировать объекты из JSON
            /*
            [Route(HttpVerbs.Post, "/test")]
            public async Task<DataPerson> Test()
            {
                var data = await HttpContext.GetRequestDataAsync<DataPerson>();
                data.id += 25;
                data.name += " and bill";
                return data;
            }
             */

        }
    }
}
