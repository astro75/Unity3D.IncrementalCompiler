using System;
using System.Collections.Generic;
using System.ServiceModel;
using NLog;

namespace IncrementalCompiler
{
    [ServiceContract(Namespace = "https://github.com/SaladLab/Unity3D.IncrementalCompiler")]
    public interface ICompilerService
    {
        [OperationContract]
        CompileResult Build(string projectPath, CompileOptions options);
    }

    [ServiceBehavior(
        InstanceContextMode = InstanceContextMode.Single,
        IncludeExceptionDetailInFaults = true,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class CompilerService : ICompilerService
    {
        private Logger _logger = LogManager.GetLogger("CompilerService");
        private string _projectPath;
        private Dictionary<string, Compiler> _compilerMap;
        readonly object _lockObj = new object();

        public CompileResult Build(string projectPath, CompileOptions options)
        {
            _logger.Info("Build(projectPath={0}, output={1})", projectPath, options.Output);

            Compiler compiler;

            if (options.IsUnityPackage)
            {
                // do not cache packages in ram, because they do not change
                compiler = new Compiler();
            }
            else
            {
                lock (_lockObj)
                {
                    if (string.IsNullOrEmpty(_projectPath) || _projectPath != projectPath)
                    {
                        // create new one
                        _projectPath = projectPath;
                        _compilerMap = new Dictionary<string, Compiler>();
                        if (string.IsNullOrEmpty(_projectPath) == false)
                            _logger.Info("Flush old project. (Project={0})", _projectPath);
                    }

                    if (_compilerMap.TryGetValue(options.Output, out compiler) == false)
                    {
                        compiler = new Compiler();
                        _compilerMap.Add(options.Output, compiler);
                        _logger.Info("Add new project. (Project={0})", _projectPath);
                    }
                }
            }

            try
            {
                lock (compiler)
                {
                    var result = compiler.Build(options);
                    if (options.IsUnityPackage)
                    {
                        compiler.Dispose();
                    }
                    return result;
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error in build.");
                throw;
            }
        }
    }
}
