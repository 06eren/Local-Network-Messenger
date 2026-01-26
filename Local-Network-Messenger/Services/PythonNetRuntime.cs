using System;
using System.IO;
using System.Reflection;

namespace Local_Network_Messenger.Services
{
    public static class PythonNetRuntime
    {
        private static readonly object Sync = new();
        private static bool _initialized;
        private static Assembly? _pythonAssembly;
        private static MethodInfo? _engineInitialize;
        private static MethodInfo? _engineShutdown;
        private static MethodInfo? _engineBeginThreads;
        private static MethodInfo? _pyGil;
        private static MethodInfo? _pyCreateScope;

        public static bool IsInitialized => _initialized;

        public static bool TryInitialize(out string error)
        {
            lock (Sync)
            {
                if (_initialized)
                {
                    error = string.Empty;
                    return true;
                }

                try
                {
                    try
                    {
                        _pythonAssembly = Assembly.Load("Python.Runtime");
                    }
                    catch (FileNotFoundException)
                    {
                        var localPath = Path.Combine(AppContext.BaseDirectory, "Tools", "Python.Runtime.dll");
                        if (File.Exists(localPath))
                        {
                            _pythonAssembly = Assembly.LoadFrom(localPath);
                        }
                    }

                    if (_pythonAssembly == null)
                    {
                        error = "Python.Runtime bulunamadi.";
                        return false;
                    }

                    var engineType = _pythonAssembly.GetType("Python.Runtime.PythonEngine");
                    var pyType = _pythonAssembly.GetType("Python.Runtime.Py");
                    if (engineType == null || pyType == null)
                    {
                        error = "Python.Runtime yuklenemedi.";
                        return false;
                    }

                    _engineInitialize = engineType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                    _engineShutdown = engineType.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Static);
                    _engineBeginThreads = engineType.GetMethod("BeginAllowThreads", BindingFlags.Public | BindingFlags.Static);
                    _pyGil = pyType.GetMethod("GIL", BindingFlags.Public | BindingFlags.Static);
                    _pyCreateScope = pyType.GetMethod("CreateScope", BindingFlags.Public | BindingFlags.Static);

                    _engineInitialize?.Invoke(null, null);
                    _engineBeginThreads?.Invoke(null, null);
                    _initialized = true;
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (!_initialized)
                {
                    return;
                }

                _engineShutdown?.Invoke(null, null);
                _initialized = false;
            }
        }

        public static IDisposable? AcquireGIL()
        {
            if (_pyGil == null)
            {
                return null;
            }

            return _pyGil.Invoke(null, null) as IDisposable;
        }

        public static PythonNetScope? CreateScope()
        {
            if (_pyCreateScope == null)
            {
                return null;
            }

            var scope = _pyCreateScope.Invoke(null, null);
            return scope == null ? null : new PythonNetScope(scope);
        }
    }

    public sealed class PythonNetScope
    {
        private readonly object _scope;
        private readonly MethodInfo? _exec;
        private readonly MethodInfo? _set;
        private readonly MethodInfo? _eval;

        public PythonNetScope(object scope)
        {
            _scope = scope;
            var type = scope.GetType();
            _exec = type.GetMethod("Exec", new[] { typeof(string) });
            _set = type.GetMethod("Set", new[] { typeof(string), typeof(object) });
            _eval = type.GetMethod("Eval", new[] { typeof(string) });
        }

        public void Exec(string code)
        {
            _exec?.Invoke(_scope, new object[] { code });
        }

        public void Set(string name, object value)
        {
            _set?.Invoke(_scope, new[] { name, value });
        }

        public string EvalToString(string expression)
        {
            var result = _eval?.Invoke(_scope, new object[] { expression });
            return result?.ToString() ?? string.Empty;
        }
    }
}
