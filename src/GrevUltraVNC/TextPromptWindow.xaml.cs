using System.Windows;
using System.Windows.Input;

namespace GrevUltraVNC;

public partial class TextPromptWindow : Window
{
    public string Value => ValueBox.Text.Trim();

    public TextPromptWindow(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = $"GrevUltraVNC · {title}";
        PromptTitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            e.Handled = true;
            DialogResult = true;
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }
}
