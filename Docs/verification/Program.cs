using System;
using System.Runtime.CompilerServices;

namespace TiltEm.Verification
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var managed = args.Length > 0 ? args[0] : null;

            if (string.IsNullOrEmpty(managed))
            {
                Console.Error.WriteLine("No KSP managed-assembly path given.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("The build normally supplies it: TiltEmVerify.csproj sets RunArguments");
                Console.Error.WriteLine("from KSPBT_GameRoot, which comes from TiltEm.props.user in the");
                Console.Error.WriteLine("repository root (copy TiltEm.props.user.example). Run the harness with:");
                Console.Error.WriteLine();
                Console.Error.WriteLine("    dotnet run --project Docs/verification/TiltEmVerify.csproj");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Running the built executable directly? Pass the path yourself:");
                Console.Error.WriteLine();
                Console.Error.WriteLine("    TiltEmVerify <KSP install>/KSP_x64_Data/Managed");

                return 2;
            }

            Harness.HookAssemblyResolve(managed);

            Console.WriteLine("Tilt'Em reference-frame verification");
            Console.WriteLine("KSP managed assemblies: " + managed);

            return RunAll();
        }

        /// <summary>
        /// Kept separate from Main so the JIT does not need to resolve KSP types until after
        /// the AssemblyResolve hook is installed.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunAll()
        {
            Harness.Section("Frame construction (A5, D1)");
            FrameChecks.Run();

            Harness.Section("Threshold crossing and physics axes (A1-A4, A6, A7, B1-B4, E1)");
            TransitionChecks.Run();

            Harness.Section("Whole system, many tilted bodies at once (A7, A8)");
            SystemChecks.Run();

            Harness.Section("Initialisation and lifecycle (F1, F3)");
            LifecycleChecks.Run();

            Harness.Section("Parent-relative orbit frame (I1)");
            OrbitFrameChecks.Run();

            Harness.Section("Camera framing (G2, G3)");
            CameraChecks.Run();

            var root = SourceChecks.FindRepoRoot();
            Harness.Section("Shipped sources (C1-C4, D1-D3) - " + root);
            SourceChecks.Run(root);

            return Harness.Report();
        }
    }
}
