namespace System.Windows.Forms.Automation;

/// <summary>A control whose changes a screen reader announces without being asked.</summary>
public interface IAutomationLiveRegion
{
    AutomationLiveSetting LiveSetting { get; set; }
}
