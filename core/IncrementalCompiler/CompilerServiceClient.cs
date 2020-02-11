
using JKang.IpcServiceFramework;

namespace IncrementalCompiler
{
    public class CompilerServiceClient
    {
        public static CompileResult Request(int parentProcessId, string currentPath, CompileOptions options, bool useCompilationServer)
        {
            if (!useCompilationServer)
                return new CompilerService().Build(currentPath, options);

            var address = CompilerServiceHelper.BaseAddress + parentProcessId;

            var client = new IpcServiceClientBuilder<ICompilerService>()
                .UseNamedPipe(address)
                // or .UseTcp(IPAddress.Loopback, 45684) to invoke using TCP
                .Build();

            var result = client.InvokeAsync(cs => cs.Build(currentPath, options)).Result;

            return result;
        }
    }
}
