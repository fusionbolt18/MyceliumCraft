using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MyceliumCraftTools
{
    /// <summary>
    /// The release's Assembly-CSharp with the newer code of the classes Mycelium's post-release commits
    /// changed (your request, 2026-11-10). The release's types and saved fields stay exactly as they
    /// are, in the same order: a built Unity game reads its data by that layout, and the plain DLL swap
    /// crashed ("sharedassets0.assets is corrupted"). For each class in Classes:
    /// - a method in both: its body (and its attributes) become the newer one's;
    /// - a method, field or nested (compiler-made) class only in the newer one: added;
    ///   an added instance field of the class itself is marked NotSerialized, so Unity's saved layout
    ///   doesn't change (it starts with the value the newer constructor gives it);
    /// - whatever only the release has is kept (nothing calls it any more).
    /// Every reference in the copied code is pointed at the release module's own types and members.
    /// </summary>
    static class Merge
    {
        /// <summary>
        /// The classes d64bf8d9 and c5c14682 change (their .cs files). Not the build's own differences:
        /// Unity's generated script list and input-actions class, nor TargetFPS (used by nothing).
        /// </summary>
        static readonly string[] Classes =
        {
            "ForgeShroom", "AcrobatTrait", "CrabAttack", "Adaptive", "Parasitic", "EnemyHealth",
            "RangedEnemyProjectile", "PlayerAttack", "Skill", "StinkCloud", "WeaponInteraction",
        };

        static ModuleDefinition target, source;
        static readonly Dictionary<TypeDefinition, TypeDefinition> typeMap = new Dictionary<TypeDefinition, TypeDefinition>();
        static readonly Dictionary<MethodDefinition, MethodDefinition> methodMap = new Dictionary<MethodDefinition, MethodDefinition>();
        static readonly Dictionary<FieldDefinition, FieldDefinition> fieldMap = new Dictionary<FieldDefinition, FieldDefinition>();
        static readonly List<(MethodDefinition from, MethodDefinition to)> bodies = new List<(MethodDefinition, MethodDefinition)>();
        static int addedTypes, addedFields, addedMethods, replacedBodies, unserialized;

        public static int Run(AssemblyDefinition release, AssemblyDefinition newer, string outPath)
        {
            target = release.MainModule;
            source = newer.MainModule;
            foreach (string name in Classes)
            {
                TypeDefinition n = source.GetType(name), r = target.GetType(name);
                if (n == null || r == null) { Console.WriteLine($"class {name} missing ({(n == null ? "newer" : "release")})"); return 1; }
                Declare(n, r, top: true);
            }
            // Methods both have whose type isn't merged (Unity's own, other classes) keep the release's body.
            foreach (var (from, to) in bodies) CopyBody(from, to);
            foreach (var (from, to) in bodies) CopyAttributes(from, to);
            Console.WriteLine($"merged {Classes.Length} classes: {replacedBodies} method bodies replaced, {addedMethods} methods, {addedFields} fields ({unserialized} kept out of Unity's saved data), {addedTypes} nested classes added");
            release.Write(outPath);
            Console.WriteLine($"wrote {outPath} (module id {target.Mvid}, the release's)");
            return Verify(outPath, newer) ? 0 : 1;
        }

        /// <summary>Phase 1: every type, field and method shell, so all references can be found.</summary>
        static void Declare(TypeDefinition n, TypeDefinition r, bool top)
        {
            typeMap[n] = r;
            foreach (TypeDefinition nested in n.NestedTypes)
            {
                TypeDefinition existing = r.NestedTypes.FirstOrDefault(x => x.Name == nested.Name);
                if (existing == null)
                {
                    existing = new TypeDefinition(nested.Namespace, nested.Name, nested.Attributes);
                    r.NestedTypes.Add(existing);
                    addedTypes++;
                    typeMap[nested] = existing; // so its base type and interfaces can point at merged types
                    existing.BaseType = nested.BaseType != null ? MapType(nested.BaseType, null, null) : null;
                    foreach (var i in nested.Interfaces) existing.Interfaces.Add(new InterfaceImplementation(MapType(i.InterfaceType, null, null)));
                    foreach (var a in nested.CustomAttributes) existing.CustomAttributes.Add(MapAttribute(a));
                }
                Declare(nested, existing, top: false);
            }
            foreach (FieldDefinition f in n.Fields)
            {
                FieldDefinition existing = r.Fields.FirstOrDefault(x => x.Name == f.Name && x.FieldType.FullName == f.FieldType.FullName);
                if (existing == null)
                {
                    var attrs = f.Attributes;
                    if (top && !f.IsStatic) { attrs |= FieldAttributes.NotSerialized; unserialized++; }
                    existing = new FieldDefinition(f.Name, attrs, MapType(f.FieldType, null, null));
                    foreach (var a in f.CustomAttributes) existing.CustomAttributes.Add(MapAttribute(a));
                    if (top && !f.IsStatic) existing.CustomAttributes.Clear(); // no [SerializeField] on a kept-out field
                    r.Fields.Add(existing);
                    addedFields++;
                }
                fieldMap[f] = existing;
            }
            foreach (MethodDefinition m in n.Methods)
            {
                MethodDefinition existing = r.Methods.FirstOrDefault(x => SameSignature(x, m));
                if (existing == null)
                {
                    existing = new MethodDefinition(m.Name, m.Attributes, target.TypeSystem.Void) { ImplAttributes = m.ImplAttributes, HasThis = m.HasThis, ExplicitThis = m.ExplicitThis, CallingConvention = m.CallingConvention };
                    foreach (var gp in m.GenericParameters) existing.GenericParameters.Add(new GenericParameter(gp.Name, existing));
                    r.Methods.Add(existing);
                    methodMap[m] = existing;
                    existing.ReturnType = MapType(m.ReturnType, existing, null);
                    foreach (var p in m.Parameters) existing.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapType(p.ParameterType, existing, null)));
                    addedMethods++;
                }
                else
                {
                    methodMap[m] = existing;
                    if (m.HasBody) replacedBodies++;
                }
                if (m.HasBody) bodies.Add((m, existing));
            }
            // Properties of added classes (state machines' Current), pointing at the merged accessors.
            foreach (PropertyDefinition p in n.Properties)
            {
                if (r.Properties.Any(x => x.Name == p.Name)) continue;
                var np = new PropertyDefinition(p.Name, p.Attributes, MapType(p.PropertyType, null, null));
                if (p.GetMethod != null) np.GetMethod = methodMap[p.GetMethod];
                if (p.SetMethod != null) np.SetMethod = methodMap[p.SetMethod];
                r.Properties.Add(np);
            }
        }

        static bool SameSignature(MethodDefinition r, MethodDefinition n)
        {
            if (r.Name != n.Name || r.Parameters.Count != n.Parameters.Count || r.GenericParameters.Count != n.GenericParameters.Count) return false;
            if (r.ReturnType.FullName != n.ReturnType.FullName) return false;
            for (int i = 0; i < r.Parameters.Count; i++)
                if (r.Parameters[i].ParameterType.FullName != n.Parameters[i].ParameterType.FullName) return false;
            return true;
        }

        /// <summary>Phase 2: the newer body, every operand pointed at the release module.</summary>
        static void CopyBody(MethodDefinition from, MethodDefinition to)
        {
            var body = new MethodBody(to) { InitLocals = from.Body.InitLocals, MaxStackSize = from.Body.MaxStackSize };
            foreach (var v in from.Body.Variables) body.Variables.Add(new VariableDefinition(MapType(v.VariableType, to, null)));
            var map = new Dictionary<Instruction, Instruction>();
            foreach (var ins in from.Body.Instructions)
            {
                var copy = Instruction.Create(OpCodes.Nop);
                copy.OpCode = ins.OpCode;
                map[ins] = copy;
                body.Instructions.Add(copy);
            }
            foreach (var ins in from.Body.Instructions)
            {
                var copy = map[ins];
                switch (ins.Operand)
                {
                    case null: break;
                    case Instruction t: copy.Operand = map[t]; break;
                    case Instruction[] ts: copy.Operand = ts.Select(t => map[t]).ToArray(); break;
                    case VariableDefinition v: copy.Operand = body.Variables[v.Index]; break;
                    case ParameterDefinition p: copy.Operand = p.Index < 0 ? body.ThisParameter : to.Parameters[p.Index]; break;
                    case TypeReference t: copy.Operand = MapType(t, to, null); break;
                    case FieldReference f: copy.Operand = MapField(f, to); break;
                    case MethodReference m: copy.Operand = MapMethod(m, to); break;
                    case CallSite _: throw new NotSupportedException($"calli in {from.FullName}");
                    default: copy.Operand = ins.Operand; break; // numbers, strings
                }
            }
            foreach (var h in from.Body.ExceptionHandlers)
                body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType)
                {
                    TryStart = map[h.TryStart], TryEnd = h.TryEnd != null ? map[h.TryEnd] : null,
                    HandlerStart = map[h.HandlerStart], HandlerEnd = h.HandlerEnd != null ? map[h.HandlerEnd] : null,
                    FilterStart = h.FilterStart != null ? map[h.FilterStart] : null,
                    CatchType = h.CatchType != null ? MapType(h.CatchType, to, null) : null,
                });
            to.Body = body;
            to.Overrides.Clear();
            foreach (var o in from.Overrides) to.Overrides.Add(MapMethod(o, to));
        }

        static void CopyAttributes(MethodDefinition from, MethodDefinition to)
        {
            to.CustomAttributes.Clear();
            foreach (var a in from.CustomAttributes) to.CustomAttributes.Add(MapAttribute(a));
        }

        static CustomAttribute MapAttribute(CustomAttribute a)
        {
            var c = new CustomAttribute(MapMethod(a.Constructor, null));
            foreach (var arg in a.ConstructorArguments) c.ConstructorArguments.Add(MapArgument(arg));
            foreach (var f in a.Fields) c.Fields.Add(new Mono.Cecil.CustomAttributeNamedArgument(f.Name, MapArgument(f.Argument)));
            foreach (var p in a.Properties) c.Properties.Add(new Mono.Cecil.CustomAttributeNamedArgument(p.Name, MapArgument(p.Argument)));
            return c;
        }

        static CustomAttributeArgument MapArgument(CustomAttributeArgument arg) =>
            new CustomAttributeArgument(MapType(arg.Type, null, null), arg.Value is TypeReference t ? MapType(t, null, null) : arg.Value);

        // ---- references: newer module -> release module ----

        static bool InSource(TypeReference t) => t.Scope == source || t.Module == source && t.Scope is ModuleDefinition;

        static TypeReference MapType(TypeReference t, MethodDefinition method, MethodReference ownerInstance)
        {
            switch (t)
            {
                case GenericParameter gp:
                    // A method's own placeholder (T of Foo<T>()): the one of the method being built or copied.
                    if (gp.Type == GenericParameterType.Method)
                    {
                        IGenericParameterProvider ctx = (IGenericParameterProvider)ownerInstance ?? method;
                        if (ctx != null && gp.Position < ctx.GenericParameters.Count) return ctx.GenericParameters[gp.Position];
                        throw new InvalidOperationException($"method placeholder {gp.Name} without a method");
                    }
                    // A type's placeholder (!0 of List<T>): the one of the mapped type.
                    var ownerType = gp.Owner as TypeReference ?? throw new InvalidOperationException($"placeholder {gp.Name} without an owner");
                    var mappedOwner = MapType(ownerType is GenericInstanceType gio ? gio.ElementType : ownerType, method, null);
                    if (gp.Position < mappedOwner.GenericParameters.Count) return mappedOwner.GenericParameters[gp.Position];
                    throw new InvalidOperationException($"placeholder {gp.Name} not on {mappedOwner.FullName}");
                case ArrayType at: return new ArrayType(MapType(at.ElementType, method, ownerInstance), at.Rank);
                case ByReferenceType br: return new ByReferenceType(MapType(br.ElementType, method, ownerInstance));
                case PointerType pt: return new PointerType(MapType(pt.ElementType, method, ownerInstance));
                case PinnedType pin: return new PinnedType(MapType(pin.ElementType, method, ownerInstance));
                case RequiredModifierType rm: return new RequiredModifierType(MapType(rm.ModifierType, method, ownerInstance), MapType(rm.ElementType, method, ownerInstance));
                case OptionalModifierType om: return new OptionalModifierType(MapType(om.ModifierType, method, ownerInstance), MapType(om.ElementType, method, ownerInstance));
                case GenericInstanceType git:
                    var gi = new GenericInstanceType(MapType(git.ElementType, method, ownerInstance));
                    foreach (var a in git.GenericArguments) gi.GenericArguments.Add(MapType(a, method, ownerInstance));
                    return gi;
            }
            if (InSource(t))
            {
                TypeDefinition def = t.Resolve() ?? throw new InvalidOperationException($"can't resolve {t.FullName}");
                if (typeMap.TryGetValue(def, out var merged)) return merged;
                TypeDefinition same = FindInTarget(def) ?? throw new InvalidOperationException($"{def.FullName} isn't in the release");
                return same;
            }
            return target.ImportReference(t);
        }

        static TypeDefinition FindInTarget(TypeDefinition def)
        {
            if (def.DeclaringType == null) return target.GetType(def.Namespace, def.Name);
            return FindInTarget(def.DeclaringType)?.NestedTypes.FirstOrDefault(x => x.Name == def.Name);
        }

        static FieldReference MapField(FieldReference f, MethodDefinition method)
        {
            if (f.DeclaringType is GenericInstanceType || !InSource(f.DeclaringType))
            {
                var declaring = MapType(f.DeclaringType, method, null);
                if (!InSource(f.DeclaringType)) return target.ImportReference(f);
                return new FieldReference(f.Name, MapType(f.FieldType, method, null), declaring);
            }
            FieldDefinition def = f.Resolve() ?? throw new InvalidOperationException($"can't resolve {f.FullName}");
            if (fieldMap.TryGetValue(def, out var merged)) return merged;
            TypeDefinition owner = (TypeDefinition)MapType(def.DeclaringType, method, null);
            return owner.Fields.FirstOrDefault(x => x.Name == def.Name) ?? throw new InvalidOperationException($"field {f.FullName} isn't in the release");
        }

        static MethodReference MapMethod(MethodReference m, MethodDefinition method)
        {
            if (m is GenericInstanceMethod gim)
            {
                var g = new GenericInstanceMethod(MapMethod(gim.ElementMethod, method));
                foreach (var a in gim.GenericArguments) g.GenericArguments.Add(MapType(a, method, null));
                return g;
            }
            bool ownDeclaring = InSource(m.DeclaringType is GenericInstanceType gt ? gt.ElementType : m.DeclaringType);
            if (!ownDeclaring && !HasSourceTypes(m)) return target.ImportReference(m);
            if (ownDeclaring && !(m.DeclaringType is GenericInstanceType))
            {
                MethodDefinition def = m.Resolve() ?? throw new InvalidOperationException($"can't resolve {m.FullName}");
                if (methodMap.TryGetValue(def, out var merged)) return merged;
                TypeDefinition owner = (TypeDefinition)MapType(def.DeclaringType, method, null);
                return owner.Methods.FirstOrDefault(x => SameSignature(x, def)) ?? throw new InvalidOperationException($"method {m.FullName} isn't in the release");
            }
            // A method of a generic type instance (List<EnemyHealth>.Add, ...) or one whose signature names our
            // types: rebuilt on the mapped declaring type, keeping the open signature (!0 and so on).
            var copy = new MethodReference(m.Name, target.TypeSystem.Void, MapType(m.DeclaringType, method, null))
            { HasThis = m.HasThis, ExplicitThis = m.ExplicitThis, CallingConvention = m.CallingConvention };
            foreach (var gp in m.GenericParameters) copy.GenericParameters.Add(new GenericParameter(gp.Name, copy));
            copy.ReturnType = MapType(m.ReturnType, method, copy);
            foreach (var p in m.Parameters) copy.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapType(p.ParameterType, method, copy)));
            return copy;
        }

        static bool HasSourceTypes(MethodReference m)
        {
            bool Uses(TypeReference t) => t switch
            {
                null => false,
                GenericParameter _ => false,
                GenericInstanceType g => Uses(g.ElementType) || g.GenericArguments.Any(Uses),
                TypeSpecification s => Uses(s.ElementType),
                _ => InSource(t),
            };
            return Uses(m.DeclaringType) || Uses(m.ReturnType) || m.Parameters.Any(p => Uses(p.ParameterType));
        }

        /// <summary>The merged classes now read like the newer ones; everything else like the release.</summary>
        static bool Verify(string outPath, AssemblyDefinition newer)
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            var merged = AssemblyDefinition.ReadAssembly(outPath, new ReaderParameters { AssemblyResolver = resolver }).MainModule;
            int same = 0, differ = 0;
            foreach (string name in Classes)
                foreach (var n in Diff.AllTypesOf(newer.MainModule.GetType(name)))
                {
                    var r = Find(merged, n);
                    foreach (var m in n.Methods)
                    {
                        var o = r?.Methods.FirstOrDefault(x => SameSignature(x, m));
                        if (o != null && Diff.Body(o) == Diff.Body(m)) same++;
                        else { differ++; Console.WriteLine($"  still differs: {m.FullName}"); }
                    }
                }
            Console.WriteLine($"check: {same} methods of the merged classes read exactly like the newer code, {differ} don't");
            return differ == 0;
        }

        static TypeDefinition Find(ModuleDefinition module, TypeDefinition def) =>
            def.DeclaringType == null ? module.GetType(def.Namespace, def.Name) : Find(module, def.DeclaringType)?.NestedTypes.FirstOrDefault(x => x.Name == def.Name);
    }
}
