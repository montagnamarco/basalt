using System.ComponentModel;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataBinding;

/// <summary>
/// Binding's push (source into control) and pull (control into source)
/// flows, proved with a plain Control standing in for a TextBox: it already
/// has the Text property and TextChanged event every real TextBox has.
/// </summary>
public class BindingTests
{
    private sealed class Person
    {
        public string? Name { get; set; }

        public int Age { get; set; }
    }

    private static Control NewTextBox() => new() { BindingContext = new BindingContext() };

    [Fact]
    public void Adding_a_binding_pushes_the_source_value_into_the_control_at_once()
    {
        var person = new Person { Name = "Ada" };
        var textBox = NewTextBox();

        textBox.DataBindings.Add("Text", person, "Name");

        Assert.Equal("Ada", textBox.Text);
    }

    [Fact]
    public void The_control_pulls_into_the_source_on_validation_by_default()
    {
        var person = new Person { Name = "Ada" };
        var textBox = NewTextBox();
        textBox.DataBindings.Add("Text", person, "Name");

        textBox.Text = "Grace";

        // DataSourceUpdateMode.OnValidation: nothing is written back yet.
        Assert.Equal("Ada", person.Name);

        textBox.ValidateThis();

        Assert.Equal("Grace", person.Name);
    }

    [Fact]
    public void OnPropertyChanged_pulls_as_soon_as_the_control_property_changes()
    {
        var person = new Person { Name = "Ada" };
        var textBox = NewTextBox();
        textBox.DataBindings.Add("Text", person, "Name", false, DataSourceUpdateMode.OnPropertyChanged);

        textBox.Text = "Grace";

        Assert.Equal("Grace", person.Name);
    }

    [Fact]
    public void Never_update_mode_does_not_pull_even_on_validation()
    {
        var person = new Person { Name = "Ada" };
        var textBox = NewTextBox();
        textBox.DataBindings.Add("Text", person, "Name", false, DataSourceUpdateMode.Never);

        textBox.Text = "Grace";
        textBox.ValidateThis();

        Assert.Equal("Ada", person.Name);
    }

    [Fact]
    public void The_format_event_can_rewrite_the_value_the_control_shows()
    {
        var person = new Person { Name = "ada" };
        var textBox = NewTextBox();
        var binding = textBox.DataBindings.Add("Text", person, "Name");
        binding.Format += (_, e) => e.Value = ((string)e.Value!).ToUpperInvariant();

        binding.ReadValue();

        Assert.Equal("ADA", textBox.Text);
    }

    [Fact]
    public void The_parse_event_can_rewrite_the_value_written_back_to_the_source()
    {
        var person = new Person { Name = "Ada" };
        var textBox = NewTextBox();
        var binding = textBox.DataBindings.Add("Text", person, "Name");
        binding.Parse += (_, e) => e.Value = ((string)e.Value!).ToLowerInvariant();

        textBox.Text = "GRACE";
        textBox.ValidateThis();

        Assert.Equal("grace", person.Name);
    }

    [Fact]
    public void FormatString_formats_a_non_string_field_for_a_string_property()
    {
        var person = new Person { Age = 7 };
        var textBox = NewTextBox();
        textBox.DataBindings.Add("Text", person, "Age", true, DataSourceUpdateMode.OnValidation, null, "D3");

        Assert.Equal("007", textBox.Text);
    }

    [Fact]
    public void NullValue_shows_in_the_control_when_the_source_field_is_null_and_round_trips_back()
    {
        var person = new Person { Name = "Ada" };
        var textBox = NewTextBox();
        var binding = textBox.DataBindings.Add("Text", person, "Name", false, DataSourceUpdateMode.OnValidation, "(none)");
        binding.DataSourceNullValue = null;

        person.Name = null;
        binding.ReadValue();
        Assert.Equal("(none)", textBox.Text);

        textBox.Text = "(none)";
        textBox.ValidateThis();
        Assert.Null(person.Name);
    }

    [Fact]
    public void Removing_a_binding_stops_it_from_pulling_into_the_source()
    {
        var person = new Person { Name = "Ada" };
        var textBox = NewTextBox();
        var binding = textBox.DataBindings.Add("Text", person, "Name");

        textBox.DataBindings.Remove(binding);
        textBox.Text = "Grace";
        textBox.ValidateThis();

        Assert.Equal("Ada", person.Name);
    }

    [Fact]
    public void A_second_binding_for_the_same_property_is_rejected()
    {
        var person = new Person { Name = "Ada" };
        var textBox = NewTextBox();
        textBox.DataBindings.Add("Text", person, "Name");

        Assert.Throws<ArgumentException>(() => textBox.DataBindings.Add("Text", person, "Age"));
    }

    [Fact]
    public void Controls_on_a_form_share_the_forms_own_binding_context()
    {
        var form = new Form();
        var first = new Control { Parent = form };
        var second = new Control { Parent = form };

        Assert.NotNull(form.BindingContext);
        Assert.Same(form.BindingContext, first.BindingContext);
        Assert.Same(first.BindingContext, second.BindingContext);
    }
}
