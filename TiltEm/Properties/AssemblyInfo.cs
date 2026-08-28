using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("TiltEm")]
[assembly: AssemblyDescription("Axial tilt for KSP")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("TiltEm")]
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("ballisticfox & Gabriel Vazquez")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("06a3e7cd-b0fb-4869-bdd9-26ab90d6396c")]

[assembly: AssemblyVersion("2.0.1")]
[assembly: AssemblyFileVersion("2.0.1")]
[assembly: AssemblyInformationalVersion("2.0.1")]

//Hard dependency: KSP refuses to load an assembly whose declared dependency is missing,
//which fails cleanly instead of throwing on the first Kopernicus type touched.
[assembly: KSPAssembly("TiltEm", 2, 0, 1)]
[assembly: KSPAssemblyDependency("Kopernicus", 1, 0)]