using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mono.Cecil;

namespace PurrNet.PurrFanity.Codegen
{
    // Finds the assemblies the compiled one references, from the paths Unity hands over
    public sealed class CodegenAssemblyResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, string> _pathsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, AssemblyDefinition> _cache = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);

        private readonly string _selfName;
        private AssemblyDefinition _self;

        public CodegenAssemblyResolver(string compiledAssemblyName, string[] references)
        {
            _selfName = compiledAssemblyName;

            if (references == null)
                return;

            for (var i = 0; i < references.Length; i++)
            {
                var name = Path.GetFileNameWithoutExtension(references[i]);

                if (!string.IsNullOrEmpty(name) && !_pathsByName.ContainsKey(name))
                    _pathsByName.Add(name, references[i]);
            }
        }

        // The assembly being processed references itself, so it's handed in
        public void SetSelf(AssemblyDefinition self)
        {
            _self = self;
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            lock (_cache)
            {
                if (name.Name == _selfName)
                    return _self;

                if (_cache.TryGetValue(name.Name, out var cached))
                    return cached;

                if (!_pathsByName.TryGetValue(name.Name, out var path))
                    return null;

                parameters.AssemblyResolver = this;

                var definition = AssemblyDefinition.ReadAssembly(new MemoryStream(ReadAllBytes(path)), parameters);
                _cache.Add(name.Name, definition);
                return definition;
            }
        }

        // The compiler can still have the file locked
        private static byte[] ReadAllBytes(string path)
        {
            const int attempts = 10;

            for (var attempt = 0;; attempt++)
            {
                try
                {
                    return File.ReadAllBytes(path);
                }
                catch (IOException) when (attempt < attempts)
                {
                    Thread.Sleep(50);
                }
            }
        }

        public void Dispose()
        {
            lock (_cache)
            {
                foreach (var pair in _cache)
                    pair.Value.Dispose();
                _cache.Clear();
            }
        }
    }
}
