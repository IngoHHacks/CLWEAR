using dnlib.DotNet;

namespace CLWEAR
{
    public static class Program
    {
        private const string MappingsURL =
            "https://script.google.com/macros/s/AKfycbwdvLOUsw2MfSr0itlvvqT9tk4Pn_okIMeXA-tRKUsWCYszuf5YgPhhiXHa7_hD7zBjIA/exec";
        
        public static void Main(string[] args)
        {
            var flags = args.Skip(1).ToArray();
            
            if (args.Length == 0)
            {
                PrintHelp();
                return;
            }
            
            if (flags.Contains("-h"))
            {
                PrintHelp();
                return;
            }
            
            if (flags.Contains("-s"))
            {
                Console.SetOut(TextWriter.Null);
            }
            
            var mappings = new Dictionary<string, string>();
            
            Console.WriteLine("Downloading mappings");
            var col = flags.Contains("-p") ? "A" : "B";
            var listE = new HttpClient().GetStringAsync(MappingsURL + $"?mode=column&column={col}").Result.Split('\n');
            var listF = new HttpClient().GetStringAsync(MappingsURL + "?mode=column&column=C").Result.Split('\n');
            for (int i = 1; i < listE.Length; i++)
            {
                if (listF[i] == "TBA" || listF[i].StartsWith("TBA in ") || string.IsNullOrWhiteSpace(listF[i]) ||
                    mappings.ContainsKey(listE[i]))
                    continue;
                if (listF[i].StartsWith("*")) listF[i] = listF[i].Substring(1);
                mappings.Add(listE[i], listF[i]);
                if (flags.Contains("-v"))
                {
                    Console.WriteLine($"Added mapping {listE[i]} -> {listF[i]}");
                }
            }
            
            RewriteAssembly(args[0], mappings, flags);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage: CLWEAR.exe <assembly path> (flags)");
            Console.WriteLine("Flags:");
            Console.WriteLine("  -n: Keep visibility modifiers of types and members");
            Console.WriteLine("  -m: Keep method bodies");
            Console.WriteLine("  -s: Disable logging");
            Console.WriteLine("  -v: Verbose logging");
            Console.WriteLine("  -p: Use prod mappings instead of dev mappings");
            Console.WriteLine("  -o: Don't add OriginalNameAttribute/OriginalParameterNamesAttribute");
            Console.WriteLine("  -h: Show this help message");
        }

        private static void RewriteAssembly(string path, Dictionary<string, string> mappings, string[] flags)
        {
            ModuleDef assembly = ModuleDefMD.Load(path);
            
            var processed = new Dictionary<string, bool>();
            foreach (TypeDef type in assembly.Types)
            {
                RewriteAssemblyRecurse(type, processed, mappings, flags);
            }
            
            assembly.Write($"{Path.ChangeExtension(path, null)}_renamed.dll");
        }
        
        private static void RewriteAssemblyRecurse(TypeDef type, Dictionary<string, bool> processed, Dictionary<string, string> mappings, string[] flags)
        {
            if (processed.ContainsKey(type.FullName))
            {
                return;
            }
            if (type.Name.Length == 11 && type.Name.ToString().All(char.IsUpper))
            {
                if (mappings.TryGetValue(type.Name.ToString(), out var newName) && newName != type.Name)
                {
                    if (flags.Contains("-v"))
                    {
                        Console.WriteLine($"Renaming {type.Name} to {newName}");
                    }
                    if (!flags.Contains("-o"))
                    {
                        var ca = new CustomAttribute(type.Module.Import(typeof(OriginalNameAttribute).GetConstructor(new[] {typeof(string)})) as ICustomAttributeType);
                        ca.ConstructorArguments.Add(new CAArgument(type.Module.CorLibTypes.String, type.Name));
                        type.CustomAttributes.Add(ca);
                    }
                    type.Name = newName;
                }
            }
            processed.Add(type.FullName, true);

            type.Attributes &= ~TypeAttributes.VisibilityMask;

            if (type.IsNested)
            {
                type.Attributes |= TypeAttributes.NestedPublic;
            }
            else
            {
                type.Attributes |= TypeAttributes.Public;
            }

            foreach (MethodDef method in type.Methods)
            {
                if (mappings.TryGetValue(method.Name, out var newName) && newName != method.Name)
                {
                    if (flags.Contains("-v"))
                    {
                        Console.WriteLine($"Renaming {method.Name} to {newName}");
                    }
                    if (!flags.Contains("-o"))
                    {
                        var ca = new CustomAttribute(type.Module.Import(typeof(OriginalNameAttribute).GetConstructor(new[] {typeof(string)})) as ICustomAttributeType);
                        ca.ConstructorArguments.Add(new CAArgument(type.Module.CorLibTypes.String, method.Name));
                        method.CustomAttributes.Add(ca);
                    }
                    method.Name = newName;
                }
                bool changed = false;
                var str = "";
                foreach (var param in method.Parameters)
                {
                    if (param.IsHiddenThisParameter)
                    {
                        continue;
                    }
                    if (str != "")
                    {
                        str += ", ";
                    }
                    if (mappings.TryGetValue(param.Name, out var newParamName) && newParamName != param.Name)
                    {
                        str += param.Name;
                        if (flags.Contains("-v"))
                        {
                            Console.WriteLine($"Renaming {param.Name} to {newParamName}");
                        }
                        param.Name = newParamName;
                        changed = true;
                    }
                    else
                    {
                        str += " - ";
                    }
                }
                if (changed && !flags.Contains("-o"))
                {
                    var ca = new CustomAttribute(
                        method.Module.Import(typeof(OriginalParameterNamesAttribute).GetConstructor(new[] { typeof(string) })) as
                            ICustomAttributeType);
                    ca.ConstructorArguments.Add(new CAArgument(method.Module.CorLibTypes.String, str));
                    method.CustomAttributes.Add(ca);
                }
                method.Attributes &= ~MethodAttributes.MemberAccessMask;
                method.Attributes |= MethodAttributes.Public;
                if (method.Body != null)
                {
                    method.Body.Instructions?.Clear();
                    method.Body.Variables?.Clear();
                }
            }

            foreach (FieldDef field in type.Fields)
            {
                if (mappings.TryGetValue(field.Name, out var newName) && newName != field.Name)
                {
                    if (flags.Contains("-v"))
                    {
                        Console.WriteLine($"Renaming {field.Name} to {newName}");
                    }
                    if (!flags.Contains("-o"))
                    {
                        var ca = new CustomAttribute(type.Module.Import(typeof(OriginalNameAttribute).GetConstructor(new[] {typeof(string)})) as ICustomAttributeType);
                        ca.ConstructorArguments.Add(new CAArgument(type.Module.CorLibTypes.String, field.Name));
                        field.CustomAttributes.Add(ca);
                    }
                    field.Name = newName;
                }
                field.Attributes &= ~FieldAttributes.FieldAccessMask;
                field.Attributes |= FieldAttributes.Public;
            }
            
            foreach (PropertyDef property in type.Properties)
            {
                if (mappings.TryGetValue(property.Name, out var newName) && newName != property.Name)
                {
                    if (flags.Contains("-v"))
                    {
                        Console.WriteLine($"Renaming {property.Name} to {newName}");
                    }
                    if (!flags.Contains("-o"))
                    {
                        var ca = new CustomAttribute(type.Module.Import(typeof(OriginalNameAttribute).GetConstructor(new[] {typeof(string)})) as ICustomAttributeType);
                        ca.ConstructorArguments.Add(new CAArgument(type.Module.CorLibTypes.String, property.Name));
                        property.CustomAttributes.Add(ca);
                    }
                    property.Name = newName;
                }
            }
            
            foreach (EventDef @event in type.Events)
            {
                if (mappings.TryGetValue(@event.Name, out var newName) && newName != @event.Name)
                {
                    if (flags.Contains("-v"))
                    {
                        Console.WriteLine($"Renaming {@event.Name} to {newName}");
                    }
                    if (!flags.Contains("-o"))
                    {
                        var ca = new CustomAttribute(type.Module.Import(typeof(OriginalNameAttribute).GetConstructor(new[] {typeof(string)})) as ICustomAttributeType);
                        ca.ConstructorArguments.Add(new CAArgument(type.Module.CorLibTypes.String, @event.Name));
                        @event.CustomAttributes.Add(ca);
                    }
                    @event.Name = newName;
                }
            }

            foreach (TypeDef nestedType in type.NestedTypes)
            {
                RewriteAssemblyRecurse(nestedType, processed, mappings, flags);
            }
        }
    }
}

internal class OriginalNameAttribute : Attribute
{
    public string Name { get; }

    public OriginalNameAttribute(string name)
    {
        Name = name;
    }
}

internal class OriginalParameterNamesAttribute : Attribute
{
    public string Names { get; }

    public OriginalParameterNamesAttribute(string names)
    {
        Names = names;
    }
}