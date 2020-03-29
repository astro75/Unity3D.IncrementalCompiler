using System;
using System.Collections.Generic;
using NLog;

namespace IncrementalCompiler
{
    //[ServiceContract(Namespace = "https://github.com/SaladLab/Unity3D.IncrementalCompiler")]
    public interface ICompilerService
    {
        //[OperationContract]
        CompileResult Build(string projectPath, CompileOptions options);
    }

    //[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, IncludeExceptionDetailInFaults = true)]
    public class CompilerService : ICompilerService
    {
        Logger _logger = LogManager.GetLogger("CompilerService");
        string? _projectPath;
        Dictionary<string, Compiler> _compilerMap = new Dictionary<string, Compiler>();
        readonly object _lockObj = new object();

        public CompileResult Build(string projectPath, CompileOptions options)
        {
            _logger.Info("Build(projectPath={0}, output={1})", projectPath, options.Output);

            Compiler compiler;

            if (options.IsUnityPackage)
            {
                // do not cache packages in ram, because they do not change
                compiler = new Compiler(options);
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
                        compiler = new Compiler(options);
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
