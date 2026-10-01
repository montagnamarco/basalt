using System.ComponentModel;

namespace System.Windows.Forms;

public partial class LinkLabel
{
    /// <summary>
    /// One clickable run of a LinkLabel's text: where it starts, how long it
    /// is, and the state (enabled, visited) it is painted and clicked with.
    /// </summary>
    public class Link
    {
        private bool _enabled = true;
        private bool _visited;
        private string _name = string.Empty;

        public Link()
        {
        }

        public Link(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public Link(int start, int length, object? linkData)
        {
            Start = start;
            Length = length;
            LinkData = linkData;
        }

        /// <summary>
        /// The LinkLabel this link was added to, so that changing Visited or
        /// Enabled on the link repaints its owner; null until added to a
        /// <see cref="LinkCollection"/>. Not part of the public API.
        /// </summary>
        internal LinkLabel? Owner { get; set; }

        public string? Description { get; set; }

        [DefaultValue(true)]
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (value == _enabled) return;

                _enabled = value;
                Owner?.InvalidateLinkLayout();
            }
        }

        public int Length { get; set; }

        [DefaultValue(null)]
        public object? LinkData { get; set; }

        [DefaultValue("")]
        public string Name
        {
            get => _name;
            set => _name = value ?? string.Empty;
        }

        public int Start { get; set; }

        [DefaultValue(null)]
        public object? Tag { get; set; }

        [DefaultValue(false)]
        public bool Visited
        {
            get => _visited;
            set
            {
                if (value == _visited) return;

                _visited = value;
                Owner?.InvalidateLinkLayout();
            }
        }
    }
}
