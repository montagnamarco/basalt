// The enumerations of this namespace, with the names and values of Microsoft's
// Microsoft.VisualBasic.Forms reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace Microsoft.VisualBasic.Logging;

public enum DiskSpaceExhaustedOption
{
    ThrowException = 0,
    DiscardMessages = 1,
}

public enum LogFileCreationScheduleOption
{
    None = 0,
    Daily = 1,
    Weekly = 2,
}

public enum LogFileLocation
{
    TempDirectory = 0,
    LocalUserApplicationDirectory = 1,
    CommonApplicationDirectory = 2,
    ExecutableDirectory = 3,
    Custom = 4,
}
