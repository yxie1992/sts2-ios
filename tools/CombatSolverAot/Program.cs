using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

// This tool accepts copied inputs and writes new outputs. It never launches the game,
// installs runtime patches, initializes ModelDb, or writes the Steam installation.
if (args.Length == 4 && args[0] == "bridge")
    BridgeGame(args[1], args[2], args[3]);
else if (args.Length == 6 && args[0] == "recipes")
    GenerateRecipes(args[1], args[2], args[3], args[4], args[5]);
else if (args.Length == 7 && args[0] == "rewrite")
    RewriteAccess(args[1], args[2], args[3], args[4], args[5], args[6]);
else if (args.Length == 4 && args[0] == "strip-analysis-initializer")
    StripAnalysisInitializer(args[1], args[2], args[3]);
else if (args.Length == 2 && args[0] == "inspect-initializer")
{
    using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
    foreach (var method in assembly.MainModule.Types.First(type => type.Name == "<Module>").Methods)
    {
        Console.WriteLine(method.FullName);
        if (method.HasBody) foreach (var instruction in method.Body.Instructions) Console.WriteLine(instruction);
    }
}
else if (args.Length == 4 && args[0] == "inspect-members")
{
    using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
    foreach (var type in AllTypes(assembly.MainModule.Types).Where(type => type.FullName.Contains(args[2], StringComparison.OrdinalIgnoreCase)))
    {
        foreach (var field in type.Fields.Where(field => field.Name.Contains(args[3], StringComparison.OrdinalIgnoreCase))) Console.WriteLine(field.FullName + " constant=" + (field.HasConstant ? field.Constant : "<none>"));
        foreach (var method in type.Methods.Where(method => method.Name.Contains(args[3], StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(method.FullName);
            if (method.HasBody) foreach (var instruction in method.Body.Instructions) Console.WriteLine(instruction);
        }
    }
}
else
    throw new ArgumentException("Usage: bridge <original-sts2.dll> <compile-sts2.dll> <report.json> | recipes <desktop-CombatSolver.dll> <original-beta-refs-dir> <ritsu-dir> <output.cs> <report.json> | rewrite <solver.dll> <runtime-sts2.dll> <output-solver.dll> <output-sts2.dll> <report.json> <original-beta-refs-dir>");

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static void EnsureOutput(string input, string output)
{
    if (Path.GetFullPath(input) == Path.GetFullPath(output))
        throw new InvalidOperationException("In-place mutation is forbidden.");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
}
static void WriteReport(string path, object value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}
static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
    }
}
static MethodDefinition NewInstanceMethod(TypeDefinition type, string name, TypeReference result)
{
    if (type.Methods.Any(m => m.Name == name)) throw new InvalidDataException($"Bridge already exists: {type.FullName}.{name}");
    var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.HideBySig, result);
    type.Methods.Add(method);
    return method;
}
static void BridgeGame(string input, string output, string report)
{
    EnsureOutput(input, output);
    using var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input))!);
    using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false, InMemory = true });
    var module = assembly.MainModule;
    var originalMvid = module.Mvid;
    bool sentryInitializerDisabled = NeutralizeSentryInitializer(module);
    var types = AllTypes(module.Types).ToArray();
    TypeDefinition Required(string name) => types.Single(t => t.FullName == name);
    var model = Required("MegaCrit.Sts2.Core.Models.AbstractModel");
    var card = Required("MegaCrit.Sts2.Core.Models.CardModel");
    var cost = Required("MegaCrit.Sts2.Core.Entities.Cards.CardEnergyCost");
    string hash = Hash(input);
    // The publicized copy is ONLY a compiler reference. Save a runtime copy with
    // original visibility below before making these compile-time changes.
    int fields = 0, methods = 0;

    var clone = NewInstanceMethod(model, "__CombatSolverMemberwiseClone", module.TypeSystem.Object);
    var objectClone = new MethodReference("MemberwiseClone", module.TypeSystem.Object, module.TypeSystem.Object) { HasThis = true };
    var il = clone.Body.GetILProcessor();
    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, objectClone); il.Emit(OpCodes.Ret);
    foreach (string stage in new[] { "DeepCloneFields", "AfterCloned" })
    {
        var original = model.Methods.Single(m => m.Name == stage && m.Parameters.Count == 0);
        if (!original.IsVirtual || original.ReturnType.MetadataType != MetadataType.Void)
            throw new InvalidDataException($"Unexpected clone stage shape: {original.FullName}");
        var bridge = NewInstanceMethod(model, "__CombatSolver" + stage, module.TypeSystem.Void);
        il = bridge.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, original); il.Emit(OpCodes.Ret);
    }

    var detach = NewInstanceMethod(card, "__CombatSolverDetachObservers", module.TypeSystem.Void);
    il = detach.Body.GetILProcessor();
    List<string> observers = [];
    foreach (var evt in card.Events.Where(e => e.AddMethod?.IsStatic != true))
    {
        var field = card.Fields.SingleOrDefault(f => f.Name == evt.Name && !f.IsStatic)
            ?? throw new InvalidDataException($"Event no longer has a plain backing field: {evt.FullName}");
        if (field.FieldType.IsValueType || field.FieldType.FullName != evt.EventType.FullName)
            throw new InvalidDataException($"Unexpected event backing type: {evt.FullName}");
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, field);
        observers.Add(field.Name);
    }
    if (observers.Count == 0) throw new InvalidDataException("No card observers found; refusing an unvalidated clone layout.");
    il.Emit(OpCodes.Ret);
    foreach (var (owner, fieldName, bridgeName) in new[] {
        (card, "_hasSingleTurnRetain", "__CombatSolverGetSingleTurnRetain"),
        (card, "_hasSingleTurnSly", "__CombatSolverGetSingleTurnSly"),
        (cost, "_localModifiers", "__CombatSolverGetLocalCostModifiers") })
    {
        var field = owner.Fields.Single(f => f.Name == fieldName && !f.IsStatic);
        var getter = NewInstanceMethod(owner, bridgeName, field.FieldType);
        il = getter.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
    }
    var stamp = new MethodDefinition("__CombatSolverSourceSha256", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.String);
    model.Methods.Add(stamp);
    il = stamp.Body.GetILProcessor(); il.Emit(OpCodes.Ldstr, hash); il.Emit(OpCodes.Ret);
    string runtimeOutput = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "runtime", "sts2.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(runtimeOutput)!);
    assembly.Write(runtimeOutput);
    foreach (var type in types.Where(t => t.Name != "<Module>"))
    {
        type.Attributes = (type.Attributes & ~TypeAttributes.VisibilityMask) |
            (type.IsNested ? TypeAttributes.NestedPublic : TypeAttributes.Public);
        foreach (var field in type.Fields.Where(field => !field.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")))
        {
            field.Attributes = (field.Attributes & ~FieldAttributes.FieldAccessMask) | FieldAttributes.Public;
            fields++;
        }
        foreach (var method in type.Methods.Where(m => m.Name != ".cctor" && !m.IsVirtual))
        {
            method.Attributes = (method.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Public;
            methods++;
        }
    }
    assembly.Write(output);
    using var checkedAssembly = AssemblyDefinition.ReadAssembly(output);
    if (checkedAssembly.MainModule.Mvid != originalMvid) throw new InvalidDataException("Unexpected MVID change.");
    WriteReport(report, new { inputSha256 = hash, compileReferenceSha256 = Hash(output), runtimeOutput, runtimeSha256 = Hash(runtimeOutput), originalMvid, sentryInitializerDisabled, compileOnlyPublicTypes = types.Length - 1, compileOnlyPublicFields = fields, compileOnlyPublicMethods = methods, detachedCardObservers = observers, cloneStageOrder = new[] { "MemberwiseClone", "IsMutable=true", "DetachCardObservers", "virtual DeepCloneFields", "virtual AfterCloned" } });
    Console.WriteLine($"Bridged {assembly.Name.Name}; {observers.Count} observer fields; source {hash}");
}

static void AssertNoModuleInitializer(string path)
{
    using var assembly = AssemblyDefinition.ReadAssembly(path);
    if (assembly.MainModule.Types.FirstOrDefault(t => t.Name == "<Module>")?.Methods.Any(m => m.Name == ".cctor") == true)
        throw new InvalidDataException($"Refusing to load an input with a module initializer: {path}");
}

static void StripAnalysisInitializer(string input, string output, string references)
{
    EnsureOutput(input, output);
    using var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(references);
    using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
    bool changed = NeutralizeSentryInitializer(assembly.MainModule);
    assembly.Write(output);
    Console.WriteLine(changed ? "Removed only the audited Sentry module initializer from an isolated analysis copy." : "Sentry module initializer was already disabled; wrote the isolated analysis copy.");
}

static bool NeutralizeSentryInitializer(ModuleDefinition module)
{
    var initializer = module.Types.First(type => type.Name == "<Module>").Methods.SingleOrDefault(method => method.Name == ".cctor");
    if (initializer != null)
    {
        var instructions = initializer.Body.Instructions;
        if (instructions.Count == 1 && instructions[0].OpCode == OpCodes.Ret) return false;
        if (instructions.Count != 2 || instructions[0].OpCode != OpCodes.Call || instructions[0].Operand is not MethodReference { Name: "Init", DeclaringType.FullName: "SentryAutoInit" } || instructions[1].OpCode != OpCodes.Ret)
            throw new InvalidDataException("Game module initializer changed; audit required before disabling Sentry.");
        instructions.Clear();
        initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        return true;
    }
    return false;
}

static void RewriteAccess(string solverInput, string gameInput, string solverOutput, string gameOutput, string report, string originalReferences)
{
    EnsureOutput(solverInput, solverOutput);
    EnsureOutput(gameInput, gameOutput);
    using var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(originalReferences);
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(solverInput))!);
    using var game = AssemblyDefinition.ReadAssembly(gameInput, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
    using var solver = AssemblyDefinition.ReadAssembly(solverInput, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
    var gameTypes = AllTypes(game.MainModule.Types).ToDictionary(type => type.FullName);
    Dictionary<string, MethodDefinition> bridges = [];
    List<object> rewrites = [];
    TypeDefinition? GameType(TypeReference type)
    {
        var element = type.GetElementType();
        if (element.Scope is AssemblyNameReference reference && reference.Name != game.Name.Name) return null;
        if (element.Scope is ModuleDefinition module && module.Assembly.Name.Name != game.Name.Name) return null;
        return gameTypes.GetValueOrDefault(element.FullName);
    }
    IEnumerable<TypeDefinition> Hierarchy(TypeDefinition type)
    {
        yield return type;
        while (type.BaseType != null && GameType(type.BaseType) is { } parent)
        {
            yield return parent;
            type = parent;
        }
    }
    bool Visible(TypeDefinition type) => (type.IsPublic || type.IsNestedPublic) && (type.DeclaringType == null || Visible(type.DeclaringType));
    string SignatureType(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        GenericInstanceType instance => instance.ElementType.FullName + "<" + string.Join(",", instance.GenericArguments.Select(SignatureType)) + ">",
        ByReferenceType reference => SignatureType(reference.ElementType) + "&",
        ArrayType array => SignatureType(array.ElementType) + "[" + array.Rank + "]",
        _ => type.FullName
    };
    MethodDefinition MakeBridge(TypeDefinition owner, string identity, TypeReference result, IEnumerable<TypeReference> parameters)
    {
        if (!Visible(owner)) throw new NotSupportedException($"A private owning type needs an explicit bridge design: {owner.FullName}");
        if (owner.HasGenericParameters) throw new NotSupportedException($"A generic owning type needs an explicit bridge design: {owner.FullName}");
        string name = "__CombatSolverAccess_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        var bridge = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, result);
        foreach (var parameter in parameters) bridge.Parameters.Add(new ParameterDefinition(parameter));
        owner.Methods.Add(bridge);
        bridges.Add(identity, bridge);
        return bridge;
    }
    foreach (var type in AllTypes(solver.MainModule.Types))
    foreach (var method in type.Methods.Where(method => method.HasBody))
    foreach (var instruction in method.Body.Instructions)
    {
        MethodDefinition? bridge = null;
        string? member = null;
        string opcode = instruction.OpCode.Name;
        if (instruction.Operand is FieldReference fieldRef && GameType(fieldRef.DeclaringType) is { } owner)
        {
            var field = Hierarchy(owner).SelectMany(type => type.Fields).FirstOrDefault(field => field.Name == fieldRef.Name)
                ?? throw new InvalidDataException($"Game field could not be resolved: {fieldRef.FullName}");
            owner = field.DeclaringType;
            if (field.IsPublic) continue;
            if (instruction.OpCode == OpCodes.Ldtoken) continue; // Metadata lookup does not access the field value.
            bool get = instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldsfld;
            bool address = instruction.OpCode == OpCodes.Ldflda || instruction.OpCode == OpCodes.Ldsflda;
            bool set = instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld;
            if (!(get || address || set)) throw new NotSupportedException($"Unsupported field access: {method.FullName}: {instruction}");
            if (set && field.IsInitOnly) throw new NotSupportedException($"Writing an init-only game field needs explicit review: {field.FullName}");
            string identity = opcode + "|" + field.FullName;
            if (!bridges.TryGetValue(identity, out bridge))
            {
                TypeReference result = set ? game.MainModule.TypeSystem.Void : address ? new ByReferenceType(field.FieldType) : field.FieldType;
                List<TypeReference> parameters = [];
                if (!field.IsStatic) parameters.Add(owner.IsValueType ? new ByReferenceType(owner) : owner);
                if (set) parameters.Add(field.FieldType);
                bridge = MakeBridge(owner, identity, result, parameters);
                var il = bridge.Body.GetILProcessor();
                for (int index = 0; index < parameters.Count; index++) il.Emit(OpCodes.Ldarg, bridge.Parameters[index]);
                il.Emit(instruction.OpCode, field); il.Emit(OpCodes.Ret);
            }
            member = field.FullName;
        }
        else if (instruction.Operand is MethodReference methodRef && GameType(methodRef.DeclaringType) is { } methodOwner)
        {
            var target = Hierarchy(methodOwner).SelectMany(type => type.Methods).FirstOrDefault(candidate => candidate.Name == methodRef.Name && SignatureType(candidate.ReturnType) == SignatureType(methodRef.ReturnType) && candidate.Parameters.Select(parameter => SignatureType(parameter.ParameterType)).SequenceEqual(methodRef.Parameters.Select(parameter => SignatureType(parameter.ParameterType))) && candidate.GenericParameters.Count == methodRef.GetElementMethod().GenericParameters.Count);
            if (target == null)
            {
                // Generic public methods require no bridge. Reject ambiguous/nonpublic
                // candidates rather than emitting an unverified constructed signature.
                var candidates = methodOwner.Methods.Where(candidate => candidate.Name == methodRef.Name && candidate.Parameters.Count == methodRef.Parameters.Count).ToArray();
                if (candidates.Length != 0 && candidates.All(candidate => candidate.IsPublic)) continue;
                throw new NotSupportedException($"Cannot bind private method exactly: {methodRef.FullName}");
            }
            methodOwner = target.DeclaringType;
            if (target.IsPublic) continue;
            if (instruction.OpCode == OpCodes.Ldtoken) continue;
            bool construct = instruction.OpCode == OpCodes.Newobj;
            bool functionPointer = instruction.OpCode == OpCodes.Ldftn || instruction.OpCode == OpCodes.Ldvirtftn;
            if (!(construct || functionPointer || instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt))
                throw new NotSupportedException($"Unsupported method access: {method.FullName}: {instruction}");
            string identity = opcode + "|" + target.FullName;
            if (!bridges.TryGetValue(identity, out bridge))
            {
                List<TypeReference> parameters = [];
                if (!target.IsStatic && !construct && !functionPointer) parameters.Add(methodOwner.IsValueType ? new ByReferenceType(methodOwner) : methodOwner);
                parameters.AddRange(target.Parameters.Select(parameter => parameter.ParameterType));
                bridge = MakeBridge(methodOwner, identity, construct ? methodOwner : target.ReturnType, parameters);
                if (functionPointer && !target.IsStatic)
                {
                    bridge.IsStatic = false;
                    bridge.HasThis = true;
                }
                MethodReference targetCall = target;
                if (target.HasGenericParameters)
                {
                    foreach (var parameter in target.GenericParameters)
                        bridge.GenericParameters.Add(new GenericParameter(parameter.Name, bridge) { Attributes = parameter.Attributes });
                    TypeReference Bind(TypeReference type) => type switch
                    {
                        GenericParameter parameter when parameter.Type == GenericParameterType.Method => bridge.GenericParameters[parameter.Position],
                        ByReferenceType reference => new ByReferenceType(Bind(reference.ElementType)),
                        ArrayType array => new ArrayType(Bind(array.ElementType), array.Rank),
                        GenericInstanceType instance => BindInstance(instance),
                        _ => type
                    };
                    GenericInstanceType BindInstance(GenericInstanceType type)
                    {
                        var instance = new GenericInstanceType(type.ElementType);
                        foreach (var argument in type.GenericArguments) instance.GenericArguments.Add(Bind(argument));
                        return instance;
                    }
                    bridge.ReturnType = Bind(bridge.ReturnType);
                    foreach (var parameter in bridge.Parameters) parameter.ParameterType = Bind(parameter.ParameterType);
                    for (int index = 0; index < target.GenericParameters.Count; index++)
                        foreach (var constraint in target.GenericParameters[index].Constraints)
                            bridge.GenericParameters[index].Constraints.Add(new GenericParameterConstraint(Bind(constraint.ConstraintType)));
                    var genericCall = new GenericInstanceMethod(target);
                    foreach (var parameter in bridge.GenericParameters) genericCall.GenericArguments.Add(parameter);
                    targetCall = genericCall;
                }
                var il = bridge.Body.GetILProcessor();
                if (functionPointer && !target.IsStatic) il.Emit(OpCodes.Ldarg_0);
                for (int index = 0; index < parameters.Count; index++) il.Emit(OpCodes.Ldarg, bridge.Parameters[index]);
                il.Emit(functionPointer ? (instruction.OpCode == OpCodes.Ldvirtftn ? OpCodes.Callvirt : OpCodes.Call) : instruction.OpCode, targetCall); il.Emit(OpCodes.Ret);
            }
            member = target.FullName;
        }
        if (bridge == null) continue;
        if (instruction.Previous?.OpCode == OpCodes.Volatile || instruction.Previous?.OpCode == OpCodes.Unaligned || instruction.Previous?.OpCode == OpCodes.Constrained)
            throw new NotSupportedException($"Prefixed access needs explicit review: {method.FullName}: {instruction}");
        var originalOperand = instruction.Operand;
        if (instruction.OpCode != OpCodes.Ldftn && instruction.OpCode != OpCodes.Ldvirtftn) instruction.OpCode = OpCodes.Call;
        MethodReference imported = solver.MainModule.ImportReference(bridge);
        if (originalOperand is GenericInstanceMethod originalGeneric)
        {
            var genericBridge = new GenericInstanceMethod(imported);
            foreach (var argument in originalGeneric.GenericArguments) genericBridge.GenericArguments.Add(argument);
            imported = genericBridge;
        }
        instruction.Operand = imported;
        rewrites.Add(new { caller = method.FullName, opcode, member, bridge = bridge.FullName });
    }
    game.Write(gameOutput);
    solver.Write(solverOutput);
    using var finalGame = AssemblyDefinition.ReadAssembly(gameOutput);
    var finalTypes = AllTypes(finalGame.MainModule.Types).ToDictionary(type => type.FullName);
    string Body(MethodDefinition method) => !method.HasBody ? "" : string.Join("\n", method.Body.Instructions.Select(instruction => instruction.ToString()));
    using var originalGame = AssemblyDefinition.ReadAssembly(gameInput);
    foreach (var originalType in AllTypes(originalGame.MainModule.Types))
    {
        var finalType = finalTypes[originalType.FullName];
        if (originalType.Attributes != finalType.Attributes) throw new InvalidDataException($"Runtime type visibility changed: {originalType.FullName}");
        foreach (var field in originalType.Fields)
            if (field.Attributes != finalType.Fields.Single(candidate => candidate.FullName == field.FullName).Attributes)
                throw new InvalidDataException($"Runtime field attributes changed: {field.FullName}");
        foreach (var originalMethod in originalType.Methods)
        {
            var finalMethod = finalType.Methods.Single(candidate => candidate.FullName == originalMethod.FullName);
            if (originalMethod.Attributes != finalMethod.Attributes || Body(originalMethod) != Body(finalMethod))
                throw new InvalidDataException($"Original runtime method changed: {originalMethod.FullName}");
        }
    }
    WriteReport(report, new { solverInputSha256 = Hash(solverInput), solverOutputSha256 = Hash(solverOutput), gameInputSha256 = Hash(gameInput), gameOutputSha256 = Hash(gameOutput), originalRuntimeMemberAttributesAndMethodBodiesPreserved = true, bridgeCount = bridges.Count, accessCount = rewrites.Count, rewrites });
    Console.WriteLine($"Rewrote {rewrites.Count} private accesses through {bridges.Count} bridges; original member visibility preserved.");
}
static void GenerateRecipes(string solverPath, string gameDirectory, string ritsuDirectory, string output, string report)
{
    solverPath = Path.GetFullPath(solverPath);
    string gamePath = Path.Combine(Path.GetFullPath(gameDirectory), "sts2.dll");
    EnsureOutput(solverPath, output);
    AssertNoModuleInitializer(solverPath);
    // The Steam build has a Sentry module initializer. It is irrelevant to IL
    // analysis and must never execute in this offline host. Strip that exact,
    // audited two-instruction initializer in a separate analysis-only copy.
    string oracleGamePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "oracle-only", "sts2.dll");
    StripAnalysisInitializer(gamePath, oracleGamePath, gameDirectory);
    // The trusted oracle is only the pinned IL analyzer. No solver Entry.Initialize,
    // Ritsu initialization, model constructors, or game callbacks are invoked.
    using (var assembly = AssemblyDefinition.ReadAssembly(solverPath))
    {
        var inferrer = assembly.MainModule.GetType("CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay.CardOnPlayInferrer")
            ?? throw new InvalidDataException("Pinned inference type missing.");
        if (inferrer.Methods.Any(m => m.Name == ".cctor"))
            throw new InvalidDataException("Unexpected inference static constructor; audit required.");
    }
    var paths = Directory.EnumerateFiles(gameDirectory, "*.dll", SearchOption.TopDirectoryOnly)
        .Concat(Directory.EnumerateFiles(Path.Combine(ritsuDirectory, "compat", "0.111.0"), "*.dll", SearchOption.TopDirectoryOnly))
        .Concat(Directory.EnumerateFiles(Path.Combine(ritsuDirectory, "shared"), "*.dll", SearchOption.TopDirectoryOnly))
        .Concat(Directory.EnumerateFiles(Path.GetDirectoryName(solverPath)!, "*.dll", SearchOption.TopDirectoryOnly))
        .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => Path.GetFullPath(g.First()), StringComparer.OrdinalIgnoreCase);
    var context = new OracleLoadContext(paths, Path.GetDirectoryName(oracleGamePath)!);
    paths["sts2"] = oracleGamePath;
    var game = context.LoadFromAssemblyPath(oracleGamePath);
    var solver = context.LoadFromAssemblyPath(solverPath);
    var cardBase = game.GetType("MegaCrit.Sts2.Core.Models.CardModel", true)!;
    var oracle = solver.GetType("CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay.CardOnPlayInferrer", true)!;
    var inferStrict = oracle.GetMethod("InferStrict", BindingFlags.Public | BindingFlags.Static)!;
    var infer = oracle.GetMethod("Infer", BindingFlags.Public | BindingFlags.Static)!;
    List<RecipeRecord> rows = [];
    foreach (var type in game.GetTypes().Where(t => !t.IsAbstract && cardBase.IsAssignableFrom(t)).OrderBy(t => t.FullName, StringComparer.Ordinal))
    {
        var onPlay = type.GetMethod("OnPlay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, "OnPlay");
        string[]? ReadRecipe(MethodInfo analyzer)
        {
            Delegate? handler;
            try { handler = (Delegate?)analyzer.Invoke(null, new object[] { type, onPlay }); }
            catch (TargetInvocationException ex) { throw new InvalidOperationException($"Inference failed for {type.FullName}/{analyzer.Name}", ex.InnerException); }
            if (handler == null) return null;
            var recipe = handler.Target ?? throw new InvalidDataException("Unexpected static inference delegate.");
            if (recipe.GetType().FullName != "CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay.CardEffectRecipe")
                throw new InvalidDataException($"Unexpected inference recipe {recipe.GetType().FullName}.");
            return ((IEnumerable)recipe.GetType().GetProperty("Effects")!.GetValue(recipe)!).Cast<object>().Select(value => value.ToString()!).ToArray();
        }
        rows.Add(new(type.FullName!, onPlay.DeclaringType!.FullName!, ReadRecipe(inferStrict), ReadRecipe(infer)));
    }
    if (rows.Count < 100) throw new InvalidDataException($"Unexpected card inventory: {rows.Count}.");
    string gameHash = Hash(gamePath);
    StringBuilder code = new("// Generated from the user's copied game and pinned CombatSolver IL analyzer. Do not edit.\n#nullable enable\nusing System.Reflection;\nusing MegaCrit.Sts2.Core.Models;\nnamespace CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;\ninternal static class CardOnPlayRecipes\n{\n");
    code.AppendLine($"    internal const string SourceGameSha256 = \"{gameHash}\";");
    code.AppendLine("    internal static Action<CardModel, CardOnPlayMirrorContext>? Resolve(Type type, MethodInfo method, bool strict)\n    {\n        if (AbstractModel.__CombatSolverSourceSha256() != SourceGameSha256)\n            throw new InvalidOperationException(\"CombatSolver recipes do not match this game build.\");\n        if (type.Assembly != typeof(CardModel).Assembly) return null;\n        string key = type.FullName + \"|\" + method.DeclaringType?.FullName;\n        CardEffectKind[]? effects = strict ? Strict(key) : General(key);\n        return effects is null ? null : new CardEffectRecipe(effects).Execute;\n    }");
    foreach (bool strict in new[] { true, false })
    {
        code.AppendLine($"    private static CardEffectKind[]? {(strict ? "Strict" : "General")}(string key) => key switch\n    {{");
        foreach (var row in rows.Where(row => (strict ? row.Strict : row.General) != null))
        {
            var effects = strict ? row.Strict! : row.General!;
            if (effects.Any(effect => effect is not ("Attack" or "Block" or "OwnerDrawOne" or "OwnerDrawCards")))
                throw new InvalidDataException("Unknown card effect; generator update required.");
            code.AppendLine($"        {JsonSerializer.Serialize(row.Type + "|" + row.DeclaringType)} => [{string.Join(", ", effects.Select(effect => "CardEffectKind." + effect))}],");
        }
        code.AppendLine("        _ => null, // Explicit rejection: the registry reports unsupported behavior.\n    };");
    }
    code.AppendLine("}");
    File.WriteAllText(output, code.ToString());
    WriteReport(report, new { sourceGameSha256 = gameHash, sourceSolverSha256 = Hash(solverPath), cardCount = rows.Count, strictRecipes = rows.Count(r => r.Strict != null), generalRecipes = rows.Count(r => r.General != null), records = rows });
    Console.WriteLine($"Generated {rows.Count} card records; strict={rows.Count(r => r.Strict != null)}, general={rows.Count(r => r.General != null)}; rejected entries remain unsupported.");
}

sealed record RecipeRecord(string Type, string DeclaringType, string[]? Strict, string[]? General);
sealed class OracleLoadContext(Dictionary<string, string> paths, string isolatedDirectory) : AssemblyLoadContext(isCollectible: false)
{
    protected override Assembly? Load(AssemblyName name)
    {
        // Framework assemblies must use the host runtime, not the game app's copies.
        if (name.Name is null || name.Name.StartsWith("System.", StringComparison.Ordinal) || name.Name is "System" or "mscorlib" or "netstandard" or "Microsoft.CSharp") return null;
        if (!paths.TryGetValue(name.Name, out string? path)) return null;
        using (var assembly = AssemblyDefinition.ReadAssembly(path))
        {
            var initializer = assembly.MainModule.Types.FirstOrDefault(type => type.Name == "<Module>")?.Methods.FirstOrDefault(method => method.Name == ".cctor");
            if (initializer is { HasBody: true } && initializer.Body.Instructions.Any(instruction => instruction.OpCode != OpCodes.Ret && instruction.OpCode != OpCodes.Nop))
            {
                var instructions = initializer.Body.Instructions;
                if (name.Name != "0Harmony" || instructions.Count != 2 || instructions[0].OpCode != OpCodes.Call || instructions[0].Operand is not MethodReference log || log.Name != "LogVersion" || !log.DeclaringType.FullName.StartsWith("MonoMod.", StringComparison.Ordinal) || !log.DeclaringType.FullName.EndsWith("MMDbgLog", StringComparison.Ordinal) || instructions[1].OpCode != OpCodes.Ret)
                    throw new InvalidDataException($"Dependency has an unaudited module initializer: {path}");
                // Only version logging; omit it from the isolated oracle host too.
                initializer.Body.Instructions.Clear();
                initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
                path = Path.Combine(isolatedDirectory, "0Harmony.dll");
                assembly.Write(path);
            }
        }
        return LoadFromAssemblyPath(path);
    }
}
