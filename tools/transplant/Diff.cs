using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MyceliumCraftTools
{
    /// <summary>Compares two versions of an assembly by meaning, not by bytes (tokens differ between builds).</summary>
    static class Diff
    {
        public static string Body(MethodDefinition m)
        {
            if (!m.HasBody) return "(no body)";
            var sb = new System.Text.StringBuilder();
            foreach (var v in m.Body.Variables) sb.Append("local ").Append(v.VariableType.FullName).Append('\n');
            foreach (var ins in m.Body.Instructions)
            {
                sb.Append(ins.OpCode.Name);
                switch (ins.Operand)
                {
                    case null: break;
                    case Instruction target: sb.Append(" -> ").Append(m.Body.Instructions.IndexOf(target)); break;
                    case Instruction[] targets: foreach (var t in targets) sb.Append(" -> ").Append(m.Body.Instructions.IndexOf(t)); break;
                    case MemberReference mr: sb.Append(' ').Append(mr.FullName); break;
                    case VariableDefinition vd: sb.Append(" v").Append(vd.Index); break;
                    case ParameterDefinition pd: sb.Append(" p").Append(pd.Index); break;
                    default: sb.Append(' ').Append(ins.Operand); break;
                }
                sb.Append('\n');
            }
            foreach (var h in m.Body.ExceptionHandlers)
                sb.Append($"handler {h.HandlerType} {m.Body.Instructions.IndexOf(h.TryStart)} {m.Body.Instructions.IndexOf(h.HandlerStart)} {h.CatchType?.FullName}\n");
            return sb.ToString();
        }

        public static string Key(MethodDefinition m) => m.FullName;
        public static string Key(FieldDefinition f) => f.FieldType.FullName + " " + f.Name;

        public static IEnumerable<TypeDefinition> AllTypesOf(TypeDefinition t) => WithNested(t);

        public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
        {
            foreach (var t in module.Types)
                foreach (var n in WithNested(t)) yield return n;
        }

        static IEnumerable<TypeDefinition> WithNested(TypeDefinition t)
        {
            yield return t;
            foreach (var n in t.NestedTypes)
                foreach (var x in WithNested(n)) yield return x;
        }
    }
}
