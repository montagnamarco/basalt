// The enumerations of this namespace, with the names and values of Microsoft's
// System.Windows.Forms.Primitives reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace System.Windows.Forms;

public enum HighDpiMode
{
    DpiUnaware = 0,
    SystemAware = 1,
    PerMonitor = 2,
    PerMonitorV2 = 3,
    DpiUnawareGdiScaled = 4,
}

public enum ScreenOrientation
{
    Angle0 = 0,
    Angle90 = 1,
    Angle180 = 2,
    Angle270 = 3,
}
