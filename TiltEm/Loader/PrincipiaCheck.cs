using System.Reflection;
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
        //The assembly's own name, which is not the file's. Principia ships the adapter as
        //ksp_plugin_adapter.dll, and KSP's dllName comes from the file, so it carries only the
        //generic half. The name compiled into the assembly is the one thing that says Principia.
        private const string AdapterAssembly = "principia.ksp_plugin_adapter";

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

        /// <summary>Scans KSP's loaded assemblies for Principia's adapter. Not cached.</summary>
        //Internal so the test kit can put assemblies in front of it; Installed is what the
        //mod reads.
        internal static bool Detect()
        {
            if (AssemblyLoader.loadedAssemblies == null) return false;

            foreach (AssemblyLoader.LoadedAssembly loaded in AssemblyLoader.loadedAssemblies)
            {
                Assembly assembly = loaded.assembly;

                if (assembly != null && assembly.GetName().Name == AdapterAssembly) return true;
            }

            return false;
        }
    }
}
