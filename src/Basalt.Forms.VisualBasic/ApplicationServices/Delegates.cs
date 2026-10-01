namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>The My.Application events a WindowsFormsApplicationBase raises during Run.</summary>
public delegate void ApplyApplicationDefaultsEventHandler(object sender, ApplyApplicationDefaultsEventArgs e);

public delegate void ShutdownEventHandler(object sender, EventArgs e);

public delegate void StartupEventHandler(object sender, StartupEventArgs e);

public delegate void StartupNextInstanceEventHandler(object sender, StartupNextInstanceEventArgs e);

public delegate void UnhandledExceptionEventHandler(object sender, UnhandledExceptionEventArgs e);
