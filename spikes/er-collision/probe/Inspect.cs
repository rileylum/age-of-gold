using System.Collections;
using System.Reflection;

// Spike: prints a Havok object graph's fields, summarising long lists, for comparing structures.
static class Inspect
{
    public static void Dump(object? o, string indent = "", int depth = 0, HashSet<object>? seen = null)
    {
        seen ??= new(ReferenceEqualityComparer.Instance);
        if (o is null) { Console.WriteLine($"{indent}null"); return; }
        var t = o.GetType();
        if (t.IsPrimitive || o is string || t.IsEnum) { Console.WriteLine($"{indent}{o}"); return; }
        if (!seen.Add(o) || depth > 14) { Console.WriteLine($"{indent}<{t.Name} …>"); return; }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var v = f.GetValue(o);
            var label = $"{indent}{f.Name} ({ShortType(f.FieldType)})";
            switch (v)
            {
                case null: Console.WriteLine($"{label}: null"); break;
                case string s: Console.WriteLine($"{label}: \"{s}\""); break;
                case var p when p.GetType().IsPrimitive || p.GetType().IsEnum: Console.WriteLine($"{label}: {p}"); break;
                case IList list when list.Count == 0: Console.WriteLine($"{label}: []"); break;
                case IList list when IsSimple(list[0]):
                    Console.WriteLine($"{label}: [{list.Count}] {string.Join(", ", list.Cast<object>().Take(12))}{(list.Count > 12 ? ", …" : "")}");
                    break;
                case IList list:
                    Console.WriteLine($"{label}: [{list.Count}]");
                    for (int i = 0; i < Math.Min(list.Count, Limit); i++) { Console.WriteLine($"{indent}  [{i}]"); Dump(list[i], indent + "    ", depth + 1, seen); }
                    if (list.Count > Limit) Console.WriteLine($"{indent}  …");
                    break;
                default: Console.WriteLine($"{label}:"); Dump(v, indent + "  ", depth + 1, seen); break;
            }
        }
    }

    static int Limit => int.Parse(Environment.GetEnvironmentVariable("AOG_LIST") ?? "2");
    static bool IsSimple(object? o) => o is null || o.GetType().IsPrimitive || o is string || o.GetType().IsEnum;
    static string ShortType(Type t) => t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(ShortType))}>" : t.Name;
}
