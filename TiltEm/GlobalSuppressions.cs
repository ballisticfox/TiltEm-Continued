using System.Diagnostics.CodeAnalysis;

// Harmony patch methods are private and never called from this assembly - the patcher finds them
// by attribute and invokes them by reflection - so the unused-member analyzer flags every one.
[assembly: SuppressMessage("Code Quality", "IDE0051:Remove unused private members",
    Justification = "Harmony invokes patch methods by reflection",
    Scope = "namespaceanddescendants", Target = "~N:TiltEm.Harmony")]
