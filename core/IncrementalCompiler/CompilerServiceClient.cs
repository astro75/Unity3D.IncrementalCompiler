
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using JKang.IpcServiceFramework;
using JKang.IpcServiceFramework.Services;

namespace IncrementalCompiler
{
    public class CompilerServiceClient
    {
        public static CompileResult Request(int parentProcessId, string currentPath, CompileOptions options, bool useCompilationServer)
        {
            if (!useCompilationServer)
                return new CompilerService().Build(currentPath, options);

            var address = CompilerServiceHelper.BaseAddress + parentProcessId;

            // var client = new IpcServiceClientBuilder<ICompilerService>()
            //     .UseNamedPipe(address)
            //     // or .UseTcp(IPAddress.Loopback, 45684) to invoke using TCP
            //     .Build();

            var client = new NamedPipeIpcServiceClient<ICompilerService>(new DefaultIpcMessageSerializer(),
                new DefaultValueConverter(), address);

            try
            {
                var result = client.InvokeAsync(cs => cs.Build(currentPath, options)).Result;
                return result;
            }
            catch (AggregateException e)
            {
                if (e.InnerException != null && e.InnerException is TimeoutException)
                {
                    throw e.InnerException;
                }
                else
                {
                    throw;
                }
            }
        }
    }

    class NamedPipeIpcServiceClient<TInterface> : IpcServiceClient<TInterface>
        where TInterface : class
    {
        readonly string _pipeName;

        public NamedPipeIpcServiceClient(
            IIpcMessageSerializer serializer,
            IValueConverter converter,
            string pipeName)
            : base(serializer, converter) {
            _pipeName = pipeName;
        }

        protected override async Task<Stream> ConnectToServerAsync(
            CancellationToken cancellationToken) {
            var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await stream.ConnectAsync(100, cancellationToken).ConfigureAwait(false);
            return stream;
        }
    }
}
