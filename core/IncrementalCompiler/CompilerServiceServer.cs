using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using JKang.IpcServiceFramework;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace IncrementalCompiler
{
    public class CompilerServiceServer
    {
        //private static ServiceHost serviceHost;

        static IServiceCollection ConfigureServices(IServiceCollection services)
        {
            return services
                .AddIpc(builder =>
                {
                    builder
                        .AddNamedPipe(options =>
                        {
                            //options.ThreadCount = 2;
                        })
                        .AddService<ICompilerService, CompilerService>();
                });
        }

        public static int Run(Logger logger, int parentProcessId)
        {
            // get parent process which will be monitored

            Process parentProcess = null;
            if (parentProcessId != 0)
            {
                try
                {
                    parentProcess = Process.GetProcessById(parentProcessId);
                }
                catch (Exception e)
                {
                    logger.Error(e, "Cannot find parentProcess (Id={0})", parentProcessId);
                    return 1;
                }
            }

            // open service

            try
            {
                var services = ConfigureServices(new ServiceCollection());

                var address = CompilerServiceHelper.BaseAddress + parentProcessId;

                var host = new IpcServiceHostBuilder(services.BuildServiceProvider())
                    .AddNamedPipeEndpoint<ICompilerService>(name: "endpoint1", pipeName: address)
                    //.AddTcpEndpoint<ICompilerService>(name: "endpoint2", ipEndpoint: IPAddress.Loopback, port: 45684)
                    .Build();

                host.Run();
            }
            catch (Exception e)
            {
                /*if (serviceHost != null)
                {
                    serviceHost.Close();
                }*/
                logger.Error(e, "Service Host got an error");
                return 1;
            }

            /*if (parentProcess != null)
            {
                // WaitForExit returns immediately instead of waiting on Mac so use while loop
                if (PlatformHelper.CurrentPlatform == Platform.Mac)
                {
                    while (!parentProcess.HasExited)
                    {
                        Thread.Sleep(100);
                    }
                }
                else
                {
                    parentProcess.WaitForExit();
                }
                if (serviceHost != null)
                {
                    serviceHost.Close();
                }
                logger.Info("Parent process just exited. (PID={0})", parentProcess.Id);
            }*/

            return 0;
        }
    }
}
