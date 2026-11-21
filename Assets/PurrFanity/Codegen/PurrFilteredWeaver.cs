using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PurrNet.PurrFanity.Codegen
{
    public struct WeaverMessage
    {
        public bool isError;
        public string text;
        public MethodDefinition method;
    }

    // Adds a filter call to the start of every method with [PurrFiltered] on it or its parameters
    public static class PurrFilteredWeaver
    {
        public const string RuntimeAssembly = "PurrNet.PurrFanity";

        private const string RuntimeNamespace = "PurrNet.PurrFanity";
        private const string AttributeName = RuntimeNamespace + ".PurrFilteredAttribute";
        private const string RuntimeTypeName = "PurrFilteredRuntime";
        private const string OverrideTypeName = "ProfanityActionOverride";
        private const string FilterMethodName = "FilterArgument";

        // PurrNet moves an RPC's body into "<Name>_Original_<id>" and turns the method into the sender
        private const string PurrNetOriginalInfix = "_Original_";

        private const string TaskName = "System.Threading.Tasks.Task";

        private struct Selection
        {
            public ParameterDefinition parameter;
            public int action;
            public string profile;
        }
        private const string GenericTaskName = "System.Threading.Tasks.Task`1";

        public static bool Process(ModuleDefinition module, List<WeaverMessage> messages)
        {
            bool changed = false;

            for (var i = 0; i < module.Types.Count; i++)
                changed |= ProcessType(module, module.Types[i], messages);

            return changed;
        }

        private static bool ProcessType(ModuleDefinition module, TypeDefinition type, List<WeaverMessage> messages)
        {
            bool changed = false;

            for (var i = 0; i < type.NestedTypes.Count; i++)
                changed |= ProcessType(module, type.NestedTypes[i], messages);

            for (var i = 0; i < type.Methods.Count; i++)
            {
                var method = type.Methods[i];
                var attribute = FindAttribute(method.CustomAttributes);

                if (attribute == null && !HasParameterAttribute(method))
                    continue;

                // PurrNet copies parameter attributes onto the moved body, it's handled through the RPC
                if (IsPurrNetOriginal(method))
                    continue;

                try
                {
                    changed |= ProcessMethod(module, method, attribute, messages);
                }
                catch (Exception e)
                {
                    messages.Add(new WeaverMessage
                    {
                        isError = true,
                        text = $"[PurrFiltered] failed on '{method.FullName}': {e.Message}\n{e.StackTrace}",
                        method = method
                    });
                }
            }

            return changed;
        }

        private static CustomAttribute FindAttribute(Mono.Collections.Generic.Collection<CustomAttribute> attributes)
        {
            for (var i = 0; i < attributes.Count; i++)
            {
                if (attributes[i].AttributeType.FullName == AttributeName)
                    return attributes[i];
            }

            return null;
        }

        private static bool HasParameterAttribute(MethodDefinition method)
        {
            for (var i = 0; i < method.Parameters.Count; i++)
            {
                if (method.Parameters[i].HasCustomAttributes && FindAttribute(method.Parameters[i].CustomAttributes) != null)
                    return true;
            }

            return false;
        }

        private static void ReadArguments(CustomAttribute attribute, out int action, out string profile)
        {
            action = -1;
            profile = null;

            if (attribute.ConstructorArguments.Count >= 2)
            {
                action = Convert.ToInt32(attribute.ConstructorArguments[0].Value);
                profile = attribute.ConstructorArguments[1].Value as string;
            }

            if (string.IsNullOrEmpty(profile))
                profile = null;
        }

        private static bool ProcessMethod(ModuleDefinition module, MethodDefinition method, CustomAttribute attribute, List<WeaverMessage> messages)
        {
            // PurrNet might have already moved the body, the filter goes wherever it is now
            var target = FindUserCode(method);

            if (!target.HasBody || target.Body.Instructions.Count == 0)
            {
                Warn(messages, method, "has no body to filter, the attribute is ignored.");
                return false;
            }

            if (target.ReturnType.IsByReference)
            {
                Error(messages, method, "returns by reference, which [PurrFiltered] does not support.");
                return false;
            }

            var selections = Select(method, target, attribute, messages, out var anyAttribute);

            if (selections == null)
                return false;

            if (selections.Count == 0)
            {
                Warn(messages, method, "has no string parameters to filter, the attribute is ignored.");
                return false;
            }

            var filterMethod = GetFilterMethod(module, anyAttribute);

            if (filterMethod == null)
            {
                Error(messages, method, $"could not find {RuntimeTypeName}.{FilterMethodName}.");
                return false;
            }

            var body = target.Body;
            var il = body.GetILProcessor();
            var first = body.Instructions[0];
            bool passTarget = target.HasThis && !target.DeclaringType.IsValueType;
            string displayName = method.DeclaringType.Name + "." + method.Name;
            VariableDefinition defaultLocal = null;

            for (var i = 0; i < selections.Count; i++)
            {
                var selection = selections[i];
                var parameter = selection.parameter;
                var next = il.Create(OpCodes.Nop);

                il.InsertBefore(first, parameter.ParameterType.IsByReference ? il.Create(OpCodes.Ldarg, parameter) : il.Create(OpCodes.Ldarga, parameter));
                il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, selection.action));
                il.InsertBefore(first, selection.profile == null ? il.Create(OpCodes.Ldnull) : il.Create(OpCodes.Ldstr, selection.profile));
                il.InsertBefore(first, passTarget ? il.Create(OpCodes.Ldarg_0) : il.Create(OpCodes.Ldnull));
                il.InsertBefore(first, il.Create(OpCodes.Ldstr, displayName));
                il.InsertBefore(first, il.Create(OpCodes.Call, filterMethod));
                il.InsertBefore(first, il.Create(OpCodes.Brtrue, next));
                EmitReturnDefault(module, target, il, first, ref defaultLocal);
                il.InsertBefore(first, next);
            }

            return true;
        }

        // Every string parameter if the method has the attribute, plus the ones that have it themselves
        private static List<Selection> Select(MethodDefinition method, MethodDefinition target, CustomAttribute attribute, List<WeaverMessage> messages, out CustomAttribute anyAttribute)
        {
            var result = new List<Selection>();
            anyAttribute = attribute;

            if (attribute != null)
            {
                ReadArguments(attribute, out var action, out var profile);

                for (var i = 0; i < target.Parameters.Count; i++)
                {
                    if (IsFilterable(target.Parameters[i]))
                        result.Add(new Selection { parameter = target.Parameters[i], action = action, profile = profile });
                }
            }

            bool failed = false;

            // Attributes come from the method as written, the code goes where its body is now
            for (var i = 0; i < method.Parameters.Count; i++)
            {
                var own = FindAttribute(method.Parameters[i].CustomAttributes);

                if (own == null)
                    continue;

                anyAttribute ??= own;
                var parameter = target.Parameters[i];

                if (!IsFilterable(parameter))
                {
                    Error(messages, method, $"parameter '{parameter.Name}' is not a string that can be filtered.");
                    failed = true;
                    continue;
                }

                ReadArguments(own, out var action, out var profile);
                var selection = new Selection { parameter = parameter, action = action, profile = profile };
                int existing = result.FindIndex(s => s.parameter == parameter);

                if (existing >= 0)
                    result[existing] = selection;
                else
                    result.Add(selection);
            }

            return failed ? null : result;
        }

        private static MethodDefinition FindUserCode(MethodDefinition method)
        {
            var prefix = method.Name + PurrNetOriginalInfix;
            var methods = method.DeclaringType.Methods;

            for (var i = 0; i < methods.Count; i++)
            {
                var candidate = methods[i];

                if (candidate == method || !candidate.HasBody)
                    continue;

                if (!candidate.Name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                if (!IsDigits(candidate.Name, prefix.Length) || !SameParameters(candidate, method))
                    continue;

                return candidate;
            }

            return method;
        }

        // "Send_Original_12" next to a "Send" with the same parameters
        private static bool IsPurrNetOriginal(MethodDefinition method)
        {
            int index = method.Name.LastIndexOf(PurrNetOriginalInfix, StringComparison.Ordinal);

            if (index <= 0 || !IsDigits(method.Name, index + PurrNetOriginalInfix.Length))
                return false;

            var stubName = method.Name.Substring(0, index);
            var methods = method.DeclaringType.Methods;

            for (var i = 0; i < methods.Count; i++)
            {
                if (methods[i] != method && methods[i].Name == stubName && SameParameters(methods[i], method))
                    return true;
            }

            return false;
        }

        private static bool IsDigits(string text, int from)
        {
            if (from >= text.Length)
                return false;

            for (var i = from; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                    return false;
            }

            return true;
        }

        private static bool SameParameters(MethodDefinition md1, MethodDefinition md2)
        {
            if (md1.Parameters.Count != md2.Parameters.Count || md1.IsStatic != md2.IsStatic)
                return false;

            for (var i = 0; i < md1.Parameters.Count; i++)
            {
                if (md1.Parameters[i].ParameterType.FullName != md2.Parameters[i].ParameterType.FullName)
                    return false;
            }

            return true;
        }

        private static bool IsFilterable(ParameterDefinition parameter)
        {
            if (parameter.IsOut || parameter.IsIn)
                return false;

            var type = parameter.ParameterType;

            if (type is ByReferenceType byReference)
                type = byReference.ElementType;

            return type.FullName == "System.String";
        }

        private static MethodReference GetFilterMethod(ModuleDefinition module, CustomAttribute attribute)
        {
            var scope = attribute.AttributeType.Scope;

            // Used inside the runtime assembly itself
            if (scope is ModuleDefinition)
            {
                var runtime = module.GetType(RuntimeNamespace, RuntimeTypeName);

                if (runtime == null)
                    return null;

                for (var i = 0; i < runtime.Methods.Count; i++)
                {
                    if (runtime.Methods[i].Name == FilterMethodName)
                        return runtime.Methods[i];
                }

                return null;
            }

            var runtimeType = new TypeReference(RuntimeNamespace, RuntimeTypeName, module, scope);
            var overrideType = new TypeReference(RuntimeNamespace, OverrideTypeName, module, scope, true);
            var method = new MethodReference(FilterMethodName, module.TypeSystem.Boolean, runtimeType) { HasThis = false };

            method.Parameters.Add(new ParameterDefinition(new ByReferenceType(module.TypeSystem.String)));
            method.Parameters.Add(new ParameterDefinition(overrideType));
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

            return method;
        }

        // A blocked Task returns a completed one, a null task would throw when awaited
        private static void EmitReturnDefault(ModuleDefinition module, MethodDefinition target, ILProcessor il, Instruction before, ref VariableDefinition defaultLocal)
        {
            var returnType = target.ReturnType;

            if (returnType.FullName == module.TypeSystem.Void.FullName)
            {
                il.InsertBefore(before, il.Create(OpCodes.Ret));
                return;
            }

            if (returnType.FullName == TaskName)
            {
                var completed = new MethodReference("get_CompletedTask", returnType, returnType) { HasThis = false };
                il.InsertBefore(before, il.Create(OpCodes.Call, completed));
                il.InsertBefore(before, il.Create(OpCodes.Ret));
                return;
            }

            if (returnType is GenericInstanceType generic && generic.ElementType.FullName == GenericTaskName && generic.GenericArguments.Count == 1)
            {
                var resultType = generic.GenericArguments[0];

                if (defaultLocal == null)
                {
                    defaultLocal = new VariableDefinition(resultType);
                    target.Body.Variables.Add(defaultLocal);
                    target.Body.InitLocals = true;
                }

                var taskType = new TypeReference("System.Threading.Tasks", "Task", module, generic.ElementType.Scope);
                var fromResult = new MethodReference("FromResult", module.TypeSystem.Void, taskType) { HasThis = false };
                var typeParameter = new GenericParameter("TResult", fromResult);
                fromResult.GenericParameters.Add(typeParameter);

                var openReturn = new GenericInstanceType(generic.ElementType);
                openReturn.GenericArguments.Add(typeParameter);
                fromResult.ReturnType = openReturn;
                fromResult.Parameters.Add(new ParameterDefinition(typeParameter));

                var closed = new GenericInstanceMethod(fromResult);
                closed.GenericArguments.Add(resultType);

                il.InsertBefore(before, il.Create(OpCodes.Ldloca, defaultLocal));
                il.InsertBefore(before, il.Create(OpCodes.Initobj, resultType));
                il.InsertBefore(before, il.Create(OpCodes.Ldloc, defaultLocal));
                il.InsertBefore(before, il.Create(OpCodes.Call, closed));
                il.InsertBefore(before, il.Create(OpCodes.Ret));
                return;
            }

            if (defaultLocal == null)
            {
                defaultLocal = new VariableDefinition(returnType);
                target.Body.Variables.Add(defaultLocal);
                target.Body.InitLocals = true;
            }

            il.InsertBefore(before, il.Create(OpCodes.Ldloca, defaultLocal));
            il.InsertBefore(before, il.Create(OpCodes.Initobj, returnType));
            il.InsertBefore(before, il.Create(OpCodes.Ldloc, defaultLocal));
            il.InsertBefore(before, il.Create(OpCodes.Ret));
        }

        private static void Warn(List<WeaverMessage> messages, MethodDefinition method, string text)
        {
            messages.Add(new WeaverMessage
            {
                isError = false,
                text = $"[PurrFiltered] '{method.DeclaringType.Name}.{method.Name}' {text}",
                method = method
            });
        }

        private static void Error(List<WeaverMessage> messages, MethodDefinition method, string text)
        {
            messages.Add(new WeaverMessage
            {
                isError = true,
                text = $"[PurrFiltered] '{method.DeclaringType.Name}.{method.Name}' {text}",
                method = method
            });
        }
    }
}