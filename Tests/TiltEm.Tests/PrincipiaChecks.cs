using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace TiltEm.Verification
{
    /// <summary>
    /// PrincipiaCheck.Detect, run against KSP's own AssemblyLoader types with assemblies shaped
    /// the way Principia's actually is. Real-assemblies mode only; the shims have no AssemblyLoader.
    /// </summary>
    //The detection shipped broken once. Principia's adapter is the file ksp_plugin_adapter.dll
    //with the assembly name principia.ksp_plugin_adapter compiled into it, and the check
    //compared that assembly name against KSP's dllName, which comes from the file. A source
    //pin that only asked whether a comparison existed let it through. So this puts an assembly
    //in front of Detect that is named as Principia's and stored as Principia's, and asks; and
    //puts the two half-right shapes in front of it too, and asks that neither counts.
    public static class PrincipiaChecks
    {
        /// <summary>Where the adapter really sits, under the name it really has.</summary>
        private const string AdapterFile = "GameData/Principia/ksp_plugin_adapter.dll";

        /// <summary>The file the first release was looking for, which does not exist.</summary>
        private const string ImaginedFile = "GameData/Principia/principia.ksp_plugin_adapter.dll";

        private const string AdapterAssembly = "principia.ksp_plugin_adapter";

        public static void Run()
        {
            AssemblyLoader.LoadedAssembyList original = AssemblyLoader.loadedAssemblies;

            // One list per case, never accumulated. The positive case has to be answered by the
            // real shape alone: with the bait from a control still in the list, the old code
            // would say yes to the bait and the check would pass for the wrong reason.
            try
            {
                AssemblyLoader.loadedAssemblies = null;
                Harness.Check("-", "no loaded-assembly list at all reads as not installed",
                    !PrincipiaCheck.Detect(), null);

                Loaded();
                Harness.Check("-", "an empty list reads as not installed",
                    !PrincipiaCheck.Detect(), null);

                // Two controls, each carrying this test assembly under a Principia-looking file
                // name. The first is the real file name; the second is the one the first release
                // compared against, under which the old code said yes.
                Loaded(Entry(typeof(PrincipiaChecks).Assembly, AdapterFile));
                Harness.Check("-", "Principia's file name carrying some other assembly does not count",
                    !PrincipiaCheck.Detect(), null);

                Loaded(Entry(typeof(PrincipiaChecks).Assembly, ImaginedFile));
                Harness.Check("-", "nor does a file named after the assembly, which is what the old check keyed on",
                    !PrincipiaCheck.Detect(), null);

                // The real shape, on its own.
                Loaded(Entry(Named(AdapterAssembly), AdapterFile));
                Harness.Check("-", "the adapter, named as Principia names it and stored as Principia stores it, is detected",
                    PrincipiaCheck.Detect(), null);
            }
            finally
            {
                AssemblyLoader.loadedAssemblies = original;
            }
        }

        /// <summary>Replaces KSP's list with a fresh one holding exactly these.</summary>
        private static void Loaded(params AssemblyLoader.LoadedAssembly[] entries)
        {
            AssemblyLoader.loadedAssemblies = new AssemblyLoader.LoadedAssembyList();

            foreach (AssemblyLoader.LoadedAssembly entry in entries)
            {
                AssemblyLoader.loadedAssemblies.Add(entry);
            }
        }

        /// <summary>A LoadedAssembly the way KSP builds one, so dllName comes from the path.</summary>
        //A non-null assembly keeps the constructor off its Mono.Cecil path, which would try to
        //read the file.
        private static AssemblyLoader.LoadedAssembly Entry(Assembly assembly, string path)
        {
            return new AssemblyLoader.LoadedAssembly(assembly, path, path, null);
        }

        /// <summary>An assembly whose compiled-in name is whatever this says, with nothing in it.</summary>
        private static Assembly Named(string name)
        {
            return AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
        }
    }

    /// <summary>Principia detection against KSP's own loader types.</summary>
    public class PrincipiaCheckTests
    {
        private const string Group = "PrincipiaChecks";

        private static void Body() => PrincipiaChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }
}
