using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

internal static class TypeLibDump
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    private static extern int LoadRegTypeLib(ref Guid rguid, short wVerMajor, short wVerMinor, int lcid, out ITypeLib typeLib);

    private enum TypeKind
    {
        Enum = 0,
        Record = 1,
        Module = 2,
        Interface = 3,
        Dispatch = 4,
        CoClass = 5,
        Alias = 6,
        Union = 7
    }

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: TypeLibDump <guid> [major] [minor]");
            return 1;
        }

        var guid = new Guid(args[0]);
        short major = args.Length > 1 ? short.Parse(args[1]) : (short)1;
        short minor = args.Length > 2 ? short.Parse(args[2]) : (short)0;

        var hr = LoadRegTypeLib(ref guid, major, minor, 0, out var typeLib);
        if (hr != 0)
        {
            Console.WriteLine("LoadRegTypeLib failed: 0x" + hr.ToString("X8"));
            return 1;
        }

        typeLib.GetDocumentation(-1, out var name, out var doc, out _, out _);
        Console.WriteLine("TypeLib: " + name);
        if (!string.IsNullOrWhiteSpace(doc))
        {
            Console.WriteLine(doc);
        }

        var count = typeLib.GetTypeInfoCount();
        for (var i = 0; i < count; i++)
        {
            typeLib.GetTypeInfoType(i, out var kindRaw);
            typeLib.GetTypeInfo(i, out var typeInfo);
            typeInfo.GetDocumentation(-1, out var typeName, out _, out _, out _);

            Console.WriteLine();
            Console.WriteLine("[" + i + "] " + typeName + " (" + (TypeKind)kindRaw + ")");

            typeInfo.GetTypeAttr(out var attrPtr);
            try
            {
                var attr = Marshal.PtrToStructure<TYPEATTR>(attrPtr);
                var funcCount = attr.cFuncs;
                for (var f = 0; f < funcCount; f++)
                {
                    typeInfo.GetFuncDesc(f, out var funcPtr);
                    try
                    {
                        var func = Marshal.PtrToStructure<FUNCDESC>(funcPtr);
                        var names = new string[Math.Max(1, func.cParams + 1)];
                        typeInfo.GetNames(func.memid, names, names.Length, out var fetched);
                        if (fetched > 0)
                        {
                            Console.Write("  " + names[0] + "(");
                            for (var p = 1; p < fetched; p++)
                            {
                                if (p > 1) Console.Write(", ");
                                Console.Write(names[p]);
                            }

                            Console.WriteLine(")");
                        }
                    }
                    finally
                    {
                        typeInfo.ReleaseFuncDesc(funcPtr);
                    }
                }
            }
            finally
            {
                typeInfo.ReleaseTypeAttr(attrPtr);
            }
        }

        return 0;
    }
}
