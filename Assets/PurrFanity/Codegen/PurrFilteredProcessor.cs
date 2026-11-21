using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace PurrNet.PurrFanity.Codegen
{
    // Unity's entry point, runs the weaver on every assembly that uses PurrFanity
    public class PurrFilteredProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => this;
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name == PurrFilteredWeaver.RuntimeAssembly)
                return true;

            var references = compiledAssembly.References;

            for (var i = 0; i < references.Length; i++)
            {
                if (Path.GetFileNameWithoutExtension(references[i]) == PurrFilteredWeaver.RuntimeAssembly)
                    return true;
            }

            return false;
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly))
                return null;

            var diagnostics = new List<DiagnosticMessage>();

            try
            {
                using var resolver = new CodegenAssemblyResolver(compiledAssembly.Name, compiledAssembly.References);
                using var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData);
                using var pdbStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData);

                using var assembly = AssemblyDefinition.ReadAssembly(peStream, new ReaderParameters
                {
                    ReadSymbols = true,
                    SymbolStream = pdbStream,
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    AssemblyResolver = resolver,
                    ReadingMode = ReadingMode.Immediate
                });

                resolver.SetSelf(assembly);

                var messages = new List<WeaverMessage>();
                bool changed = false;

                for (var m = 0; m < assembly.Modules.Count; m++)
                    changed |= PurrFilteredWeaver.Process(assembly.Modules[m], messages);

                for (var i = 0; i < messages.Count; i++)
                    diagnostics.Add(ToDiagnostic(messages[i]));

                if (!changed)
                    return diagnostics.Count == 0 ? null : new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);

                var pe = new MemoryStream();
                var pdb = new MemoryStream();

                assembly.Write(pe, new WriterParameters
                {
                    WriteSymbols = true,
                    SymbolStream = pdb,
                    SymbolWriterProvider = new PortablePdbWriterProvider()
                });

                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
            }
            catch (Exception e)
            {
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData = $"[PurrFanity] Failed to process {compiledAssembly.Name}: {e.Message}\n{e.StackTrace}"
                });

                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);
            }
        }

        private static DiagnosticMessage ToDiagnostic(WeaverMessage message)
        {
            var diagnostic = new DiagnosticMessage
            {
                DiagnosticType = message.isError ? DiagnosticType.Error : DiagnosticType.Warning,
                MessageData = message.text
            };

            var method = message.method;

            if (method == null || !method.DebugInformation.HasSequencePoints)
                return diagnostic;

            var point = method.DebugInformation.SequencePoints[0];
            var file = point.Document.Url;

            if (string.IsNullOrEmpty(file))
                return diagnostic;

            file = file.Replace('\\', '/');

            // A path relative to the project makes the console entry clickable
            int root = file.IndexOf("/Assets/", StringComparison.Ordinal);
            if (root < 0)
                root = file.IndexOf("/Packages/", StringComparison.Ordinal);

            diagnostic.File = root >= 0 ? file.Substring(root + 1) : file;
            diagnostic.Line = point.StartLine;
            diagnostic.Column = point.StartColumn;
            return diagnostic;
        }
    }
}