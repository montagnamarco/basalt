namespace System.Windows.Forms;

/// <summary>A component, such as BindingSource, that already owns the CurrencyManager a Binding would otherwise create.</summary>
public interface ICurrencyManagerProvider
{
    CurrencyManager? CurrencyManager { get; }

    CurrencyManager GetRelatedCurrencyManager(string? dataMember);
}
