// The enumerations of this namespace, with the names and values of Microsoft's
// Microsoft.VisualBasic.Forms reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace Microsoft.VisualBasic.ApplicationServices;

public enum AuthenticationMode
{
    Windows = 0,
    ApplicationDefined = 1,
}

public enum ShutdownMode
{
    AfterMainFormCloses = 0,
    AfterAllFormsClose = 1,
}
