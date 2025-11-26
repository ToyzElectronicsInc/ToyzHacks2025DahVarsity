using System.Runtime.CompilerServices;

// This makes internal classes and methods visible to your test assembly.
[assembly: InternalsVisibleTo("Tests.Editor")]
[assembly: InternalsVisibleTo("Tests.PlayMode")] // Optional if you also have PlayMode tests