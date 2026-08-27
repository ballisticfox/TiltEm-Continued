using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Whether Principia is installed. Tilt'Em stands down when it is.
    /// </summary>
    //Principia replaces KSP's reference frames and its integrator outright, which is the same
    //machinery Tilt'Em rewrites. The two do not degrade gracefully together, so the whole mod
    //turns itself off rather than fight over the frames.
    internal static class PrincipiaCheck
    {
        //The DLL name, not the KSPAssembly name: Principia declares no KSPAssembly attribute,
        //so KSP falls back to the file name, and that is what its build produces.
        private const string AdapterDll = "principia.ksp_plugin_adapter";

        private static bool _resolved;
        private static bool _installed;

        /// <summary>True when Principia's adapter assembly is loaded.</summary>
        //Resolved on first use rather than in an addon, so no caller depends on start order.
        //The warning goes out with it, once, because a mod doing nothing at all is exactly the
        //case worth a line in the log.
        public static bool Installed
        {
            get
            {
                if (_resolved) return _installed;

                _installed = Detect();
                _resolved = true;

                if (_installed)
                {
                    Debug.LogWarning("[TiltEm]: Principia is installed, so Tilt'Em has disabled "
                                     + "itself: no tilts, no patches, no orbit rebasing. The two "
                                     + "mods rewrite the same reference frames.");
                }

                return _installed;
            }
        }

        private static bool Detect()
        {
            if (AssemblyLoader.loadedAssemblies == null) return false;

            foreach (AssemblyLoader.LoadedAssembly assembly in AssemblyLoader.loadedAssemblies)
            {
                if (assembly.dllName == AdapterDll) return true;
            }

            return false;
        }
    }
}
