using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoClickerPro.Helpers;

/// <summary>
/// Attached behavior that restricts a TextBox to numeric (decimal) input only, so users can't
/// type invalid characters into the CPS / jitter boxes and break the binding or crash a parse.
/// Usage in XAML: helpers:NumericTextBoxBehavior.IsNumeric="True"
/// </summary>
public static class NumericTextBoxBehavior
{
    private static readonly Regex AllowedPartial = new(@"^\d*\.?\d*$", RegexOptions.Compiled);

    public static readonly DependencyProperty IsNumericProperty =
        DependencyProperty.RegisterAttached(
            "IsNumeric",
            typeof(bool),
            typeof(NumericTextBoxBehavior),
            new PropertyMetadata(false, OnIsNumericChanged));

    public static bool GetIsNumeric(DependencyObject obj) => (bool)obj.GetValue(IsNumericProperty);

    public static void SetIsNumeric(DependencyObject obj, bool value) => obj.SetValue(IsNumericProperty, value);

    private static void OnIsNumericChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox) return;

        if ((bool)e.NewValue)
        {
            textBox.PreviewTextInput += TextBox_PreviewTextInput;
            DataObject.AddPastingHandler(textBox, TextBox_Pasting);
        }
        else
        {
            textBox.PreviewTextInput -= TextBox_PreviewTextInput;
            DataObject.RemovePastingHandler(textBox, TextBox_Pasting);
        }
    }

    private static void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        string proposed = GetProposedText(textBox, e.Text);
        e.Handled = !AllowedPartial.IsMatch(proposed);
    }

    private static void TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        var textBox = (TextBox)sender;
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            string pasteText = (string)e.DataObject.GetData(typeof(string))!;
            string proposed = GetProposedText(textBox, pasteText);
            if (!AllowedPartial.IsMatch(proposed))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private static string GetProposedText(TextBox textBox, string newText)
    {
        string current = textBox.Text;
        int selectionStart = textBox.SelectionStart;
        int selectionLength = textBox.SelectionLength;

        string result = current.Remove(selectionStart, selectionLength);
        result = result.Insert(selectionStart, newText);
        return result;
    }
}
