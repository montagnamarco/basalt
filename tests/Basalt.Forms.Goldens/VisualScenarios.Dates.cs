using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>MonthCalendar and DateTimePicker: both fixed to the same date, so neither drifts with today.</summary>
public static partial class VisualScenarios
{
    private static readonly DateTime FixedDate = new(2024, 3, 15);
    private static readonly Size CalendarSize = new(180, 160);
    private static readonly Size PickerSize = new(120, 22);

    private static IReadOnlyList<Scenario> DateScenarios() =>
    [
        Create<MonthCalendar>("monthcalendar-fixed-date", CalendarSize, calendar =>
        {
            calendar.TodayDate = FixedDate;
            calendar.SetDate(FixedDate);
        }, FullArea(CalendarSize)),

        Create<DateTimePicker>("datetimepicker-short-format", PickerSize, picker =>
        {
            AsEditControl(picker);
            picker.Format = DateTimePickerFormat.Short;
            picker.Value = FixedDate;
        }, FullArea(PickerSize)),
        Create<DateTimePicker>("datetimepicker-showcheckbox-checked", PickerSize, picker =>
        {
            AsEditControl(picker);
            picker.Format = DateTimePickerFormat.Short;
            picker.Value = FixedDate;
            picker.ShowCheckBox = true;
            picker.Checked = true;
        }, FullArea(PickerSize)),
        Create<DateTimePicker>("datetimepicker-disabled", PickerSize, picker =>
        {
            AsEditControl(picker);
            picker.Format = DateTimePickerFormat.Short;
            picker.Value = FixedDate;
            picker.Enabled = false;
        }, FullArea(PickerSize)),
    ];
}
