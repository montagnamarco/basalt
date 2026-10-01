// The classic Windows 2000 theme never calls into VisualStyleRenderer (it is
// unsupported, see VisualStyleRenderer.IsSupported), so the class/part/state
// numbers below are inert data: they exist so application code that reads
// VisualStyleElement.X.Y.Z compiles and reports the values documented for the
// corresponding Windows SDK vsstyle.h control parts and states, written here as
// our own compact table instead of one generated property per element.

namespace System.Windows.Forms.VisualStyles;

/// <summary>A theme class, part and state triplet that names one themed drawing primitive.</summary>
public class VisualStyleElement
{
    private VisualStyleElement(string className, int part, int state)
    {
        ArgumentNullException.ThrowIfNull(className);

        ClassName = className;
        Part = part;
        State = state;
    }

    public string ClassName { get; }

    public int Part { get; }

    public int State { get; }

    public static VisualStyleElement CreateElement(string className, int part, int state) => new(className, part, state);

    public static class Button
    {
        public static class CheckBox
        {
            public static VisualStyleElement CheckedDisabled => CreateElement("BUTTON", 3, 8);
            public static VisualStyleElement CheckedHot => CreateElement("BUTTON", 3, 6);
            public static VisualStyleElement CheckedNormal => CreateElement("BUTTON", 3, 5);
            public static VisualStyleElement CheckedPressed => CreateElement("BUTTON", 3, 7);
            public static VisualStyleElement MixedDisabled => CreateElement("BUTTON", 3, 12);
            public static VisualStyleElement MixedHot => CreateElement("BUTTON", 3, 10);
            public static VisualStyleElement MixedNormal => CreateElement("BUTTON", 3, 9);
            public static VisualStyleElement MixedPressed => CreateElement("BUTTON", 3, 11);
            public static VisualStyleElement UncheckedDisabled => CreateElement("BUTTON", 3, 4);
            public static VisualStyleElement UncheckedHot => CreateElement("BUTTON", 3, 2);
            public static VisualStyleElement UncheckedNormal => CreateElement("BUTTON", 3, 1);
            public static VisualStyleElement UncheckedPressed => CreateElement("BUTTON", 3, 3);
        }

        public static class GroupBox
        {
            public static VisualStyleElement Disabled => CreateElement("BUTTON", 4, 2);
            public static VisualStyleElement Normal => CreateElement("BUTTON", 4, 1);
        }

        public static class PushButton
        {
            public static VisualStyleElement Default => CreateElement("BUTTON", 1, 5);
            public static VisualStyleElement Disabled => CreateElement("BUTTON", 1, 4);
            public static VisualStyleElement Hot => CreateElement("BUTTON", 1, 2);
            public static VisualStyleElement Normal => CreateElement("BUTTON", 1, 1);
            public static VisualStyleElement Pressed => CreateElement("BUTTON", 1, 3);
        }

        public static class RadioButton
        {
            public static VisualStyleElement CheckedDisabled => CreateElement("BUTTON", 2, 8);
            public static VisualStyleElement CheckedHot => CreateElement("BUTTON", 2, 6);
            public static VisualStyleElement CheckedNormal => CreateElement("BUTTON", 2, 5);
            public static VisualStyleElement CheckedPressed => CreateElement("BUTTON", 2, 7);
            public static VisualStyleElement UncheckedDisabled => CreateElement("BUTTON", 2, 4);
            public static VisualStyleElement UncheckedHot => CreateElement("BUTTON", 2, 2);
            public static VisualStyleElement UncheckedNormal => CreateElement("BUTTON", 2, 1);
            public static VisualStyleElement UncheckedPressed => CreateElement("BUTTON", 2, 3);
        }

        public static class UserButton
        {
            public static VisualStyleElement Normal => CreateElement("BUTTON", 5, 1);
        }
    }

    public static class ComboBox
    {
        public static class DropDownButton
        {
            public static VisualStyleElement Disabled => CreateElement("COMBOBOX", 1, 4);
            public static VisualStyleElement Hot => CreateElement("COMBOBOX", 1, 2);
            public static VisualStyleElement Normal => CreateElement("COMBOBOX", 1, 1);
            public static VisualStyleElement Pressed => CreateElement("COMBOBOX", 1, 3);
        }
    }

    public static class ExplorerBar
    {
        public static class HeaderBackground
        {
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 1, 1);
        }

        public static class HeaderClose
        {
            public static VisualStyleElement Hot => CreateElement("EXPLORERBAR", 2, 2);
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 2, 1);
            public static VisualStyleElement Pressed => CreateElement("EXPLORERBAR", 2, 3);
        }

        public static class HeaderPin
        {
            public static VisualStyleElement Hot => CreateElement("EXPLORERBAR", 3, 2);
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 3, 1);
            public static VisualStyleElement Pressed => CreateElement("EXPLORERBAR", 3, 3);
            public static VisualStyleElement SelectedHot => CreateElement("EXPLORERBAR", 3, 5);
            public static VisualStyleElement SelectedNormal => CreateElement("EXPLORERBAR", 3, 4);
            public static VisualStyleElement SelectedPressed => CreateElement("EXPLORERBAR", 3, 6);
        }

        public static class IEBarMenu
        {
            public static VisualStyleElement Hot => CreateElement("EXPLORERBAR", 4, 2);
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 4, 1);
            public static VisualStyleElement Pressed => CreateElement("EXPLORERBAR", 4, 3);
        }

        public static class NormalGroupBackground
        {
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 5, 1);
        }

        public static class NormalGroupCollapse
        {
            public static VisualStyleElement Hot => CreateElement("EXPLORERBAR", 6, 2);
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 6, 1);
            public static VisualStyleElement Pressed => CreateElement("EXPLORERBAR", 6, 3);
        }

        public static class NormalGroupExpand
        {
            public static VisualStyleElement Hot => CreateElement("EXPLORERBAR", 7, 2);
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 7, 1);
            public static VisualStyleElement Pressed => CreateElement("EXPLORERBAR", 7, 3);
        }

        public static class NormalGroupHead
        {
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 8, 1);
        }

        public static class SpecialGroupBackground
        {
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 9, 1);
        }

        public static class SpecialGroupCollapse
        {
            public static VisualStyleElement Hot => CreateElement("EXPLORERBAR", 10, 2);
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 10, 1);
            public static VisualStyleElement Pressed => CreateElement("EXPLORERBAR", 10, 3);
        }

        public static class SpecialGroupExpand
        {
            public static VisualStyleElement Hot => CreateElement("EXPLORERBAR", 11, 2);
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 11, 1);
            public static VisualStyleElement Pressed => CreateElement("EXPLORERBAR", 11, 3);
        }

        public static class SpecialGroupHead
        {
            public static VisualStyleElement Normal => CreateElement("EXPLORERBAR", 12, 1);
        }
    }

    public static class Header
    {
        public static class Item
        {
            public static VisualStyleElement Hot => CreateElement("HEADER", 1, 2);
            public static VisualStyleElement Normal => CreateElement("HEADER", 1, 1);
            public static VisualStyleElement Pressed => CreateElement("HEADER", 1, 3);
        }

        public static class ItemLeft
        {
            public static VisualStyleElement Hot => CreateElement("HEADER", 2, 2);
            public static VisualStyleElement Normal => CreateElement("HEADER", 2, 1);
            public static VisualStyleElement Pressed => CreateElement("HEADER", 2, 3);
        }

        public static class ItemRight
        {
            public static VisualStyleElement Hot => CreateElement("HEADER", 3, 2);
            public static VisualStyleElement Normal => CreateElement("HEADER", 3, 1);
            public static VisualStyleElement Pressed => CreateElement("HEADER", 3, 3);
        }

        public static class SortArrow
        {
            public static VisualStyleElement SortedDown => CreateElement("HEADER", 4, 1);
            public static VisualStyleElement SortedUp => CreateElement("HEADER", 4, 2);
        }
    }

    public static class ListView
    {
        public static class Detail
        {
            public static VisualStyleElement Normal => CreateElement("LISTVIEW", 3, 1);
        }

        public static class EmptyText
        {
            public static VisualStyleElement Normal => CreateElement("LISTVIEW", 5, 1);
        }

        public static class Group
        {
            public static VisualStyleElement Normal => CreateElement("LISTVIEW", 2, 1);
        }

        public static class Item
        {
            public static VisualStyleElement Disabled => CreateElement("LISTVIEW", 1, 4);
            public static VisualStyleElement Hot => CreateElement("LISTVIEW", 1, 3);
            public static VisualStyleElement Normal => CreateElement("LISTVIEW", 1, 1);
            public static VisualStyleElement Selected => CreateElement("LISTVIEW", 1, 2);
            public static VisualStyleElement SelectedNotFocus => CreateElement("LISTVIEW", 1, 5);
        }

        public static class SortedDetail
        {
            public static VisualStyleElement Normal => CreateElement("LISTVIEW", 4, 1);
        }
    }

    public static class Menu
    {
        public static class BarDropDown
        {
            public static VisualStyleElement Normal => CreateElement("MENU", 4, 1);
        }

        public static class BarItem
        {
            public static VisualStyleElement Normal => CreateElement("MENU", 3, 1);
        }

        public static class Chevron
        {
            public static VisualStyleElement Normal => CreateElement("MENU", 5, 1);
        }

        public static class DropDown
        {
            public static VisualStyleElement Normal => CreateElement("MENU", 2, 1);
        }

        public static class Item
        {
            public static VisualStyleElement Demoted => CreateElement("MENU", 14, 3);
            public static VisualStyleElement Normal => CreateElement("MENU", 14, 1);
            public static VisualStyleElement Selected => CreateElement("MENU", 14, 2);
        }

        public static class Separator
        {
            public static VisualStyleElement Normal => CreateElement("MENU", 15, 1);
        }
    }

    public static class MenuBand
    {
        public static class NewApplicationButton
        {
            public static VisualStyleElement Checked => CreateElement("MENUBAND", 1, 5);
            public static VisualStyleElement Disabled => CreateElement("MENUBAND", 1, 4);
            public static VisualStyleElement Hot => CreateElement("MENUBAND", 1, 2);
            public static VisualStyleElement HotChecked => CreateElement("MENUBAND", 1, 6);
            public static VisualStyleElement Normal => CreateElement("MENUBAND", 1, 1);
            public static VisualStyleElement Pressed => CreateElement("MENUBAND", 1, 3);
        }

        public static class Separator
        {
            public static VisualStyleElement Normal => CreateElement("MENUBAND", 2, 1);
        }
    }

    public static class Page
    {
        public static class Down
        {
            public static VisualStyleElement Disabled => CreateElement("PAGE", 2, 4);
            public static VisualStyleElement Hot => CreateElement("PAGE", 2, 2);
            public static VisualStyleElement Normal => CreateElement("PAGE", 2, 1);
            public static VisualStyleElement Pressed => CreateElement("PAGE", 2, 3);
        }

        public static class DownHorizontal
        {
            public static VisualStyleElement Disabled => CreateElement("PAGE", 4, 4);
            public static VisualStyleElement Hot => CreateElement("PAGE", 4, 2);
            public static VisualStyleElement Normal => CreateElement("PAGE", 4, 1);
            public static VisualStyleElement Pressed => CreateElement("PAGE", 4, 3);
        }

        public static class Up
        {
            public static VisualStyleElement Disabled => CreateElement("PAGE", 1, 4);
            public static VisualStyleElement Hot => CreateElement("PAGE", 1, 2);
            public static VisualStyleElement Normal => CreateElement("PAGE", 1, 1);
            public static VisualStyleElement Pressed => CreateElement("PAGE", 1, 3);
        }

        public static class UpHorizontal
        {
            public static VisualStyleElement Disabled => CreateElement("PAGE", 3, 4);
            public static VisualStyleElement Hot => CreateElement("PAGE", 3, 2);
            public static VisualStyleElement Normal => CreateElement("PAGE", 3, 1);
            public static VisualStyleElement Pressed => CreateElement("PAGE", 3, 3);
        }
    }

    public static class ProgressBar
    {
        public static class Bar
        {
            public static VisualStyleElement Normal => CreateElement("PROGRESS", 1, 1);
        }

        public static class BarVertical
        {
            public static VisualStyleElement Normal => CreateElement("PROGRESS", 2, 1);
        }

        public static class Chunk
        {
            public static VisualStyleElement Normal => CreateElement("PROGRESS", 3, 1);
        }

        public static class ChunkVertical
        {
            public static VisualStyleElement Normal => CreateElement("PROGRESS", 4, 1);
        }
    }

    public static class Rebar
    {
        public static class Band
        {
            public static VisualStyleElement Normal => CreateElement("REBAR", 3, 1);
        }

        public static class Chevron
        {
            public static VisualStyleElement Hot => CreateElement("REBAR", 4, 2);
            public static VisualStyleElement Normal => CreateElement("REBAR", 4, 1);
            public static VisualStyleElement Pressed => CreateElement("REBAR", 4, 3);
        }

        public static class ChevronVertical
        {
            public static VisualStyleElement Hot => CreateElement("REBAR", 5, 2);
            public static VisualStyleElement Normal => CreateElement("REBAR", 5, 1);
            public static VisualStyleElement Pressed => CreateElement("REBAR", 5, 3);
        }

        public static class Gripper
        {
            public static VisualStyleElement Normal => CreateElement("REBAR", 1, 1);
        }

        public static class GripperVertical
        {
            public static VisualStyleElement Normal => CreateElement("REBAR", 2, 1);
        }
    }

    public static class ScrollBar
    {
        public static class ArrowButton
        {
            public static VisualStyleElement DownDisabled => CreateElement("SCROLLBAR", 1, 8);
            public static VisualStyleElement DownHot => CreateElement("SCROLLBAR", 1, 6);
            public static VisualStyleElement DownNormal => CreateElement("SCROLLBAR", 1, 5);
            public static VisualStyleElement DownPressed => CreateElement("SCROLLBAR", 1, 7);
            public static VisualStyleElement LeftDisabled => CreateElement("SCROLLBAR", 1, 12);
            public static VisualStyleElement LeftHot => CreateElement("SCROLLBAR", 1, 10);
            public static VisualStyleElement LeftNormal => CreateElement("SCROLLBAR", 1, 9);
            public static VisualStyleElement LeftPressed => CreateElement("SCROLLBAR", 1, 11);
            public static VisualStyleElement RightDisabled => CreateElement("SCROLLBAR", 1, 16);
            public static VisualStyleElement RightHot => CreateElement("SCROLLBAR", 1, 14);
            public static VisualStyleElement RightNormal => CreateElement("SCROLLBAR", 1, 13);
            public static VisualStyleElement RightPressed => CreateElement("SCROLLBAR", 1, 15);
            public static VisualStyleElement UpDisabled => CreateElement("SCROLLBAR", 1, 4);
            public static VisualStyleElement UpHot => CreateElement("SCROLLBAR", 1, 2);
            public static VisualStyleElement UpNormal => CreateElement("SCROLLBAR", 1, 1);
            public static VisualStyleElement UpPressed => CreateElement("SCROLLBAR", 1, 3);
        }

        public static class GripperHorizontal
        {
            public static VisualStyleElement Normal => CreateElement("SCROLLBAR", 8, 1);
        }

        public static class GripperVertical
        {
            public static VisualStyleElement Normal => CreateElement("SCROLLBAR", 9, 1);
        }

        public static class LeftTrackHorizontal
        {
            public static VisualStyleElement Disabled => CreateElement("SCROLLBAR", 4, 4);
            public static VisualStyleElement Hot => CreateElement("SCROLLBAR", 4, 2);
            public static VisualStyleElement Normal => CreateElement("SCROLLBAR", 4, 1);
            public static VisualStyleElement Pressed => CreateElement("SCROLLBAR", 4, 3);
        }

        public static class LowerTrackVertical
        {
            public static VisualStyleElement Disabled => CreateElement("SCROLLBAR", 6, 4);
            public static VisualStyleElement Hot => CreateElement("SCROLLBAR", 6, 2);
            public static VisualStyleElement Normal => CreateElement("SCROLLBAR", 6, 1);
            public static VisualStyleElement Pressed => CreateElement("SCROLLBAR", 6, 3);
        }

        public static class RightTrackHorizontal
        {
            public static VisualStyleElement Disabled => CreateElement("SCROLLBAR", 5, 4);
            public static VisualStyleElement Hot => CreateElement("SCROLLBAR", 5, 2);
            public static VisualStyleElement Normal => CreateElement("SCROLLBAR", 5, 1);
            public static VisualStyleElement Pressed => CreateElement("SCROLLBAR", 5, 3);
        }

        public static class SizeBox
        {
            public static VisualStyleElement LeftAlign => CreateElement("SCROLLBAR", 10, 2);
            public static VisualStyleElement RightAlign => CreateElement("SCROLLBAR", 10, 1);
        }

        public static class ThumbButtonHorizontal
        {
            public static VisualStyleElement Disabled => CreateElement("SCROLLBAR", 2, 4);
            public static VisualStyleElement Hot => CreateElement("SCROLLBAR", 2, 2);
            public static VisualStyleElement Normal => CreateElement("SCROLLBAR", 2, 1);
            public static VisualStyleElement Pressed => CreateElement("SCROLLBAR", 2, 3);
        }

        public static class ThumbButtonVertical
        {
            public static VisualStyleElement Disabled => CreateElement("SCROLLBAR", 3, 4);
            public static VisualStyleElement Hot => CreateElement("SCROLLBAR", 3, 2);
            public static VisualStyleElement Normal => CreateElement("SCROLLBAR", 3, 1);
            public static VisualStyleElement Pressed => CreateElement("SCROLLBAR", 3, 3);
        }

        public static class UpperTrackVertical
        {
            public static VisualStyleElement Disabled => CreateElement("SCROLLBAR", 7, 4);
            public static VisualStyleElement Hot => CreateElement("SCROLLBAR", 7, 2);
            public static VisualStyleElement Normal => CreateElement("SCROLLBAR", 7, 1);
            public static VisualStyleElement Pressed => CreateElement("SCROLLBAR", 7, 3);
        }
    }

    public static class Spin
    {
        public static class Down
        {
            public static VisualStyleElement Disabled => CreateElement("SPIN", 2, 4);
            public static VisualStyleElement Hot => CreateElement("SPIN", 2, 2);
            public static VisualStyleElement Normal => CreateElement("SPIN", 2, 1);
            public static VisualStyleElement Pressed => CreateElement("SPIN", 2, 3);
        }

        public static class DownHorizontal
        {
            public static VisualStyleElement Disabled => CreateElement("SPIN", 4, 4);
            public static VisualStyleElement Hot => CreateElement("SPIN", 4, 2);
            public static VisualStyleElement Normal => CreateElement("SPIN", 4, 1);
            public static VisualStyleElement Pressed => CreateElement("SPIN", 4, 3);
        }

        public static class Up
        {
            public static VisualStyleElement Disabled => CreateElement("SPIN", 1, 4);
            public static VisualStyleElement Hot => CreateElement("SPIN", 1, 2);
            public static VisualStyleElement Normal => CreateElement("SPIN", 1, 1);
            public static VisualStyleElement Pressed => CreateElement("SPIN", 1, 3);
        }

        public static class UpHorizontal
        {
            public static VisualStyleElement Disabled => CreateElement("SPIN", 3, 4);
            public static VisualStyleElement Hot => CreateElement("SPIN", 3, 2);
            public static VisualStyleElement Normal => CreateElement("SPIN", 3, 1);
            public static VisualStyleElement Pressed => CreateElement("SPIN", 3, 3);
        }
    }

    public static class StartPanel
    {
        public static class LogOff
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 10, 1);
        }

        public static class LogOffButtons
        {
            public static VisualStyleElement Hot => CreateElement("STARTPANEL", 11, 2);
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 11, 1);
            public static VisualStyleElement Pressed => CreateElement("STARTPANEL", 11, 3);
        }

        public static class MorePrograms
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 4, 1);
        }

        public static class MoreProgramsArrow
        {
            public static VisualStyleElement Hot => CreateElement("STARTPANEL", 5, 2);
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 5, 1);
            public static VisualStyleElement Pressed => CreateElement("STARTPANEL", 5, 3);
        }

        public static class PlaceList
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 8, 1);
        }

        public static class PlaceListSeparator
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 9, 1);
        }

        public static class Preview
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 3, 1);
        }

        public static class ProgList
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 6, 1);
        }

        public static class ProgListSeparator
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 7, 1);
        }

        public static class UserPane
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 1, 1);
        }

        public static class UserPicture
        {
            public static VisualStyleElement Normal => CreateElement("STARTPANEL", 2, 1);
        }
    }

    public static class Status
    {
        public static class Bar
        {
            public static VisualStyleElement Normal => CreateElement("STATUS", 0, 1);
        }

        public static class Gripper
        {
            public static VisualStyleElement Normal => CreateElement("STATUS", 3, 1);
        }

        public static class GripperPane
        {
            public static VisualStyleElement Normal => CreateElement("STATUS", 2, 1);
        }

        public static class Pane
        {
            public static VisualStyleElement Normal => CreateElement("STATUS", 1, 1);
        }
    }

    public static class Tab
    {
        public static class Body
        {
            public static VisualStyleElement Normal => CreateElement("TAB", 10, 1);
        }

        public static class Pane
        {
            public static VisualStyleElement Normal => CreateElement("TAB", 9, 1);
        }

        public static class TabItem
        {
            public static VisualStyleElement Disabled => CreateElement("TAB", 1, 4);
            public static VisualStyleElement Hot => CreateElement("TAB", 1, 2);
            public static VisualStyleElement Normal => CreateElement("TAB", 1, 1);
            public static VisualStyleElement Pressed => CreateElement("TAB", 1, 3);
        }

        public static class TabItemBothEdges
        {
            public static VisualStyleElement Normal => CreateElement("TAB", 4, 1);
        }

        public static class TabItemLeftEdge
        {
            public static VisualStyleElement Disabled => CreateElement("TAB", 2, 4);
            public static VisualStyleElement Hot => CreateElement("TAB", 2, 2);
            public static VisualStyleElement Normal => CreateElement("TAB", 2, 1);
            public static VisualStyleElement Pressed => CreateElement("TAB", 2, 3);
        }

        public static class TabItemRightEdge
        {
            public static VisualStyleElement Disabled => CreateElement("TAB", 3, 4);
            public static VisualStyleElement Hot => CreateElement("TAB", 3, 2);
            public static VisualStyleElement Normal => CreateElement("TAB", 3, 1);
            public static VisualStyleElement Pressed => CreateElement("TAB", 3, 3);
        }

        public static class TopTabItem
        {
            public static VisualStyleElement Disabled => CreateElement("TAB", 5, 4);
            public static VisualStyleElement Hot => CreateElement("TAB", 5, 2);
            public static VisualStyleElement Normal => CreateElement("TAB", 5, 1);
            public static VisualStyleElement Pressed => CreateElement("TAB", 5, 3);
        }

        public static class TopTabItemBothEdges
        {
            public static VisualStyleElement Normal => CreateElement("TAB", 8, 1);
        }

        public static class TopTabItemLeftEdge
        {
            public static VisualStyleElement Disabled => CreateElement("TAB", 6, 4);
            public static VisualStyleElement Hot => CreateElement("TAB", 6, 2);
            public static VisualStyleElement Normal => CreateElement("TAB", 6, 1);
            public static VisualStyleElement Pressed => CreateElement("TAB", 6, 3);
        }

        public static class TopTabItemRightEdge
        {
            public static VisualStyleElement Disabled => CreateElement("TAB", 7, 4);
            public static VisualStyleElement Hot => CreateElement("TAB", 7, 2);
            public static VisualStyleElement Normal => CreateElement("TAB", 7, 1);
            public static VisualStyleElement Pressed => CreateElement("TAB", 7, 3);
        }
    }

    public static class TaskBand
    {
        public static class FlashButton
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAND", 2, 1);
        }

        public static class FlashButtonGroupMenu
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAND", 3, 1);
        }

        public static class GroupCount
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAND", 1, 1);
        }
    }

    public static class Taskbar
    {
        public static class BackgroundBottom
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAR", 1, 1);
        }

        public static class BackgroundLeft
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAR", 4, 1);
        }

        public static class BackgroundRight
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAR", 2, 1);
        }

        public static class BackgroundTop
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAR", 3, 1);
        }

        public static class SizingBarBottom
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAR", 5, 1);
        }

        public static class SizingBarLeft
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAR", 8, 1);
        }

        public static class SizingBarRight
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAR", 6, 1);
        }

        public static class SizingBarTop
        {
            public static VisualStyleElement Normal => CreateElement("TASKBAR", 7, 1);
        }
    }

    public static class TaskbarClock
    {
        public static class Time
        {
            public static VisualStyleElement Normal => CreateElement("CLOCK", 1, 1);
        }
    }

    public static class TextBox
    {
        public static class Caret
        {
            public static VisualStyleElement Normal => CreateElement("EDIT", 2, 1);
        }

        public static class TextEdit
        {
            public static VisualStyleElement Assist => CreateElement("EDIT", 1, 7);
            public static VisualStyleElement Disabled => CreateElement("EDIT", 1, 4);
            public static VisualStyleElement Focused => CreateElement("EDIT", 1, 5);
            public static VisualStyleElement Hot => CreateElement("EDIT", 1, 2);
            public static VisualStyleElement Normal => CreateElement("EDIT", 1, 1);
            public static VisualStyleElement ReadOnly => CreateElement("EDIT", 1, 6);
            public static VisualStyleElement Selected => CreateElement("EDIT", 1, 3);
        }
    }

    public static class ToolBar
    {
        public static class Button
        {
            public static VisualStyleElement Checked => CreateElement("TOOLBAR", 1, 5);
            public static VisualStyleElement Disabled => CreateElement("TOOLBAR", 1, 4);
            public static VisualStyleElement Hot => CreateElement("TOOLBAR", 1, 2);
            public static VisualStyleElement HotChecked => CreateElement("TOOLBAR", 1, 6);
            public static VisualStyleElement Normal => CreateElement("TOOLBAR", 1, 1);
            public static VisualStyleElement Pressed => CreateElement("TOOLBAR", 1, 3);
        }

        public static class DropDownButton
        {
            public static VisualStyleElement Checked => CreateElement("TOOLBAR", 2, 5);
            public static VisualStyleElement Disabled => CreateElement("TOOLBAR", 2, 4);
            public static VisualStyleElement Hot => CreateElement("TOOLBAR", 2, 2);
            public static VisualStyleElement HotChecked => CreateElement("TOOLBAR", 2, 6);
            public static VisualStyleElement Normal => CreateElement("TOOLBAR", 2, 1);
            public static VisualStyleElement Pressed => CreateElement("TOOLBAR", 2, 3);
        }

        public static class SeparatorHorizontal
        {
            public static VisualStyleElement Normal => CreateElement("TOOLBAR", 5, 1);
        }

        public static class SeparatorVertical
        {
            public static VisualStyleElement Normal => CreateElement("TOOLBAR", 6, 1);
        }

        public static class SplitButton
        {
            public static VisualStyleElement Checked => CreateElement("TOOLBAR", 3, 5);
            public static VisualStyleElement Disabled => CreateElement("TOOLBAR", 3, 4);
            public static VisualStyleElement Hot => CreateElement("TOOLBAR", 3, 2);
            public static VisualStyleElement HotChecked => CreateElement("TOOLBAR", 3, 6);
            public static VisualStyleElement Normal => CreateElement("TOOLBAR", 3, 1);
            public static VisualStyleElement Pressed => CreateElement("TOOLBAR", 3, 3);
        }

        public static class SplitButtonDropDown
        {
            public static VisualStyleElement Checked => CreateElement("TOOLBAR", 4, 5);
            public static VisualStyleElement Disabled => CreateElement("TOOLBAR", 4, 4);
            public static VisualStyleElement Hot => CreateElement("TOOLBAR", 4, 2);
            public static VisualStyleElement HotChecked => CreateElement("TOOLBAR", 4, 6);
            public static VisualStyleElement Normal => CreateElement("TOOLBAR", 4, 1);
            public static VisualStyleElement Pressed => CreateElement("TOOLBAR", 4, 3);
        }
    }

    public static class ToolTip
    {
        public static class Balloon
        {
            public static VisualStyleElement Link => CreateElement("TOOLTIP", 3, 2);
            public static VisualStyleElement Normal => CreateElement("TOOLTIP", 3, 1);
        }

        public static class BalloonTitle
        {
            public static VisualStyleElement Normal => CreateElement("TOOLTIP", 4, 1);
        }

        public static class Close
        {
            public static VisualStyleElement Hot => CreateElement("TOOLTIP", 5, 2);
            public static VisualStyleElement Normal => CreateElement("TOOLTIP", 5, 1);
            public static VisualStyleElement Pressed => CreateElement("TOOLTIP", 5, 3);
        }

        public static class Standard
        {
            public static VisualStyleElement Link => CreateElement("TOOLTIP", 1, 2);
            public static VisualStyleElement Normal => CreateElement("TOOLTIP", 1, 1);
        }

        public static class StandardTitle
        {
            public static VisualStyleElement Normal => CreateElement("TOOLTIP", 2, 1);
        }
    }

    public static class TrackBar
    {
        public static class Thumb
        {
            public static VisualStyleElement Disabled => CreateElement("TRACKBAR", 3, 5);
            public static VisualStyleElement Focused => CreateElement("TRACKBAR", 3, 4);
            public static VisualStyleElement Hot => CreateElement("TRACKBAR", 3, 2);
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 3, 1);
            public static VisualStyleElement Pressed => CreateElement("TRACKBAR", 3, 3);
        }

        public static class ThumbBottom
        {
            public static VisualStyleElement Disabled => CreateElement("TRACKBAR", 4, 5);
            public static VisualStyleElement Focused => CreateElement("TRACKBAR", 4, 4);
            public static VisualStyleElement Hot => CreateElement("TRACKBAR", 4, 2);
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 4, 1);
            public static VisualStyleElement Pressed => CreateElement("TRACKBAR", 4, 3);
        }

        public static class ThumbLeft
        {
            public static VisualStyleElement Disabled => CreateElement("TRACKBAR", 7, 5);
            public static VisualStyleElement Focused => CreateElement("TRACKBAR", 7, 4);
            public static VisualStyleElement Hot => CreateElement("TRACKBAR", 7, 2);
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 7, 1);
            public static VisualStyleElement Pressed => CreateElement("TRACKBAR", 7, 3);
        }

        public static class ThumbRight
        {
            public static VisualStyleElement Disabled => CreateElement("TRACKBAR", 8, 5);
            public static VisualStyleElement Focused => CreateElement("TRACKBAR", 8, 4);
            public static VisualStyleElement Hot => CreateElement("TRACKBAR", 8, 2);
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 8, 1);
            public static VisualStyleElement Pressed => CreateElement("TRACKBAR", 8, 3);
        }

        public static class ThumbTop
        {
            public static VisualStyleElement Disabled => CreateElement("TRACKBAR", 5, 5);
            public static VisualStyleElement Focused => CreateElement("TRACKBAR", 5, 4);
            public static VisualStyleElement Hot => CreateElement("TRACKBAR", 5, 2);
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 5, 1);
            public static VisualStyleElement Pressed => CreateElement("TRACKBAR", 5, 3);
        }

        public static class ThumbVertical
        {
            public static VisualStyleElement Disabled => CreateElement("TRACKBAR", 6, 5);
            public static VisualStyleElement Focused => CreateElement("TRACKBAR", 6, 4);
            public static VisualStyleElement Hot => CreateElement("TRACKBAR", 6, 2);
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 6, 1);
            public static VisualStyleElement Pressed => CreateElement("TRACKBAR", 6, 3);
        }

        public static class Ticks
        {
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 9, 1);
        }

        public static class TicksVertical
        {
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 10, 1);
        }

        public static class Track
        {
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 1, 1);
        }

        public static class TrackVertical
        {
            public static VisualStyleElement Normal => CreateElement("TRACKBAR", 2, 1);
        }
    }

    public static class TrayNotify
    {
        public static class AnimateBackground
        {
            public static VisualStyleElement Normal => CreateElement("TRAYNOTIFY", 2, 1);
        }

        public static class Background
        {
            public static VisualStyleElement Normal => CreateElement("TRAYNOTIFY", 1, 1);
        }
    }

    public static class TreeView
    {
        public static class Branch
        {
            public static VisualStyleElement Normal => CreateElement("TREEVIEW", 3, 1);
        }

        public static class Glyph
        {
            public static VisualStyleElement Closed => CreateElement("TREEVIEW", 2, 1);
            public static VisualStyleElement Opened => CreateElement("TREEVIEW", 2, 2);
        }

        public static class Item
        {
            public static VisualStyleElement Disabled => CreateElement("TREEVIEW", 1, 4);
            public static VisualStyleElement Hot => CreateElement("TREEVIEW", 1, 2);
            public static VisualStyleElement Normal => CreateElement("TREEVIEW", 1, 1);
            public static VisualStyleElement Selected => CreateElement("TREEVIEW", 1, 3);
            public static VisualStyleElement SelectedNotFocus => CreateElement("TREEVIEW", 1, 5);
        }
    }

    public static class Window
    {
        public static class Caption
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 1, 1);
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 1, 3);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 1, 2);
        }

        public static class CaptionSizingTemplate
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 30, 1);
        }

        public static class CloseButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 18, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 18, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 18, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 18, 3);
        }

        public static class Dialog
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 29, 1);
        }

        public static class FrameBottom
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 9, 1);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 9, 2);
        }

        public static class FrameBottomSizingTemplate
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 36, 1);
        }

        public static class FrameLeft
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 7, 1);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 7, 2);
        }

        public static class FrameLeftSizingTemplate
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 32, 1);
        }

        public static class FrameRight
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 8, 1);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 8, 2);
        }

        public static class FrameRightSizingTemplate
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 34, 1);
        }

        public static class HelpButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 23, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 23, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 23, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 23, 3);
        }

        public static class HorizontalScroll
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 25, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 25, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 25, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 25, 3);
        }

        public static class HorizontalThumb
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 26, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 26, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 26, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 26, 3);
        }

        public static class MaxButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 17, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 17, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 17, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 17, 3);
        }

        public static class MaxCaption
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 5, 1);
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 5, 3);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 5, 2);
        }

        public static class MdiCloseButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 20, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 20, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 20, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 20, 3);
        }

        public static class MdiHelpButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 24, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 24, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 24, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 24, 3);
        }

        public static class MdiMinButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 16, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 16, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 16, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 16, 3);
        }

        public static class MdiRestoreButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 22, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 22, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 22, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 22, 3);
        }

        public static class MdiSysButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 14, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 14, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 14, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 14, 3);
        }

        public static class MinButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 15, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 15, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 15, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 15, 3);
        }

        public static class MinCaption
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 3, 1);
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 3, 3);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 3, 2);
        }

        public static class RestoreButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 21, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 21, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 21, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 21, 3);
        }

        public static class SmallCaption
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 2, 1);
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 2, 3);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 2, 2);
        }

        public static class SmallCaptionSizingTemplate
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 31, 1);
        }

        public static class SmallCloseButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 19, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 19, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 19, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 19, 3);
        }

        public static class SmallFrameBottom
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 12, 1);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 12, 2);
        }

        public static class SmallFrameBottomSizingTemplate
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 37, 1);
        }

        public static class SmallFrameLeft
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 10, 1);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 10, 2);
        }

        public static class SmallFrameLeftSizingTemplate
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 33, 1);
        }

        public static class SmallFrameRight
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 11, 1);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 11, 2);
        }

        public static class SmallFrameRightSizingTemplate
        {
            public static VisualStyleElement Normal => CreateElement("WINDOW", 35, 1);
        }

        public static class SmallMaxCaption
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 6, 1);
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 6, 3);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 6, 2);
        }

        public static class SmallMinCaption
        {
            public static VisualStyleElement Active => CreateElement("WINDOW", 4, 1);
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 4, 3);
            public static VisualStyleElement Inactive => CreateElement("WINDOW", 4, 2);
        }

        public static class SysButton
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 13, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 13, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 13, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 13, 3);
        }

        public static class VerticalScroll
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 27, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 27, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 27, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 27, 3);
        }

        public static class VerticalThumb
        {
            public static VisualStyleElement Disabled => CreateElement("WINDOW", 28, 4);
            public static VisualStyleElement Hot => CreateElement("WINDOW", 28, 2);
            public static VisualStyleElement Normal => CreateElement("WINDOW", 28, 1);
            public static VisualStyleElement Pressed => CreateElement("WINDOW", 28, 3);
        }
    }
}
