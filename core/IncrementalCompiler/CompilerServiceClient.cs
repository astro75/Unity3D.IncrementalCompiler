using System.ServiceModel;

namespace IncrementalCompiler
{
    public class CompilerServiceClient
    {
        public static CompileResult Request(int parentProcessId, string currentPath, CompileOptions options, bool useCompilationServer, bool devMode)
        {
            if (!useCompilationServer)
                return new CompilerService().Build(currentPath, options, devMode);
            var address = CompilerServiceHelper.BaseAddress + parentProcessId;
            var binding = CompilerServiceHelper.GetBinding();
            var ep = new EndpointAddress(address);
            var channel = ChannelFactory<ICompilerService>.CreateChannel(binding, ep);
            return channel.Build(currentPath, options, devMode);
        }
    }
}
