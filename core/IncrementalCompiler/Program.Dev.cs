using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;

namespace IncrementalCompiler
{
    partial class Program
    {
        private static int RunAsDev(string[] args)
        {
            SetupLogger("IncrementalCompiler.log", true);

            var workDirectory = args[1];
            var reponseFile = args[2];
            var settings = Settings.Load() ?? Settings.Default;

            var logger = LogManager.GetLogger("Dev");
            logger.Info("Started");

            Directory.SetCurrentDirectory(workDirectory);
            var curPath = Directory.GetCurrentDirectory();

            var options = new CompileOptions();
            options.ParseArgument(new string[]
            {
                "-nostdlib+",
                "-noconfig",
                // Unity5
//                "-r:" + @"C:/Software/Unity-2018/Editor/Data/Mono/lib/mono/2.0/mscorlib.dll",
//                "-r:" + @"C:/Software/Unity-2018/Editor/Data/Mono/lib/mono/2.0/System.dll",
//                "-r:" + @"C:/Software/Unity-2018/Editor/Data/Mono/lib/mono/2.0/System.Core.dll",
//                "-r:" + @"C:/Software/Unity-2018/Editor/Data/Mono/lib/mono/2.0/System.Xml.dll",
                "@Temp/" + reponseFile,
            });

            options.WorkDirectory = curPath;
            options.References = options.References.Distinct().ToList();
            options.Files = options.Files.Distinct().ToList();
            options.PrebuiltOutputReuse = settings.PrebuiltOutputReuse;

            var parentProcessId = Process.GetCurrentProcess().Id;

            Process serverProcess = null;

            // new Thread(() => CompilerServiceServer.Run(logger, parentProcessId)).Start();

            while (true)
            {
                try
                {
                    var w = new Stopwatch();
                    w.Start();
                    Console.WriteLine("Run");

                    var result = CompilerServiceClient.Request(parentProcessId, curPath, options, true);

                    w.Stop();

                    Console.WriteLine("Done: Succeeded={0}. Duration={1}sec. ", result.Succeeded,
                        w.Elapsed.TotalSeconds);
                    foreach (var warning in result.Warnings)
                        Console.WriteLine(warning);
                    foreach (var error in result.Errors)
                        Console.WriteLine(error);

                    Console.ReadLine();
                }
                catch (TimeoutException)
                {
                    if (serverProcess == null)
                    {
                        var a = new Thread(() => CompilerServiceServer.Run(logger, parentProcessId));
                        a.Start();
                        serverProcess = Process.GetCurrentProcess();

                        // serverProcess = Process.Start(
                        //     new ProcessStartInfo
                        //     {
                        //         FileName = Assembly.GetEntryAssembly().Location,
                        //         Arguments = "-server " + parentProcessId,
                        //         WindowStyle = ProcessWindowStyle.Hidden
                        //     });
                        //
                        Thread.Sleep(100);
                    }
                    else
                    {
                        if (serverProcess.HasExited == false)
                            Thread.Sleep(100);
                        else
                            serverProcess = null;
                    }
                }
            }
        }
    }
}
